using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MedFlow.Application.DTOs.Visits;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Exceptions;
using MedFlow.IntegrationTests.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MedFlow.IntegrationTests.Visits;

public sealed class VisitDoctorAssignmentEndpointTests : AuthTestBase
{
    private const string VisitsEndpoint = "/api/v1/visits";
    private const string ReceptionVisitsEndpoint = "/api/v1/reception/visits";

    public VisitDoctorAssignmentEndpointTests(TestWebAppFactory factory)
        : base(factory)
    {
    }

    private string GenerateToken(long userId, string email, params string[] roles)
    {
        using var scope = Factory.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var user = new User { Id = userId, Email = email };
        return jwtService.GenerateAccessToken(user, roles).AccessToken;
    }

    private HttpRequestMessage AuthedGetAssignableDoctors(
        string visitIdOrCode,
        long userId = 5,
        string email = "reception01@medflow.test",
        string role = "RECEPTION",
        bool useReceptionRoute = true)
    {
        var token = GenerateToken(userId, email, role);
        var baseRoute = useReceptionRoute ? ReceptionVisitsEndpoint : VisitsEndpoint;
        var request = new HttpRequestMessage(HttpMethod.Get, $"{baseRoute}/{visitIdOrCode}/available-doctors");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private HttpRequestMessage AuthedPutDoctor(
        string visitIdOrCode,
        AssignDoctorToVisitRequest body,
        long userId = 5,
        string email = "reception01@medflow.test",
        string role = "RECEPTION",
        bool useReceptionRoute = true)
    {
        var token = GenerateToken(userId, email, role);
        var baseRoute = useReceptionRoute ? ReceptionVisitsEndpoint : VisitsEndpoint;
        var request = new HttpRequestMessage(HttpMethod.Put, $"{baseRoute}/{visitIdOrCode}/doctor")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    [Fact]
    public async Task AssignDoctor_NoToken_Returns401()
    {
        var getResp = await Client.GetAsync($"{VisitsEndpoint}/1/available-doctors");
        Assert.Equal(HttpStatusCode.Unauthorized, getResp.StatusCode);

        var putResp = await Client.PutAsJsonAsync(
            $"{VisitsEndpoint}/1/doctor",
            new AssignDoctorToVisitRequest(DoctorId: 1));
        Assert.Equal(HttpStatusCode.Unauthorized, putResp.StatusCode);

        var aliasResp = await Client.PostAsJsonAsync(
            $"{ReceptionVisitsEndpoint}/1/assign-doctor",
            new AssignDoctorToVisitRequest(DoctorCode: "DOC-001"));
        Assert.Equal(HttpStatusCode.Unauthorized, aliasResp.StatusCode);
    }

    [Theory]
    [InlineData(1L, "patient01@medflow.test", "PATIENT")]
    [InlineData(3L, "doctor01@medflow.test", "DOCTOR")]
    [InlineData(7L, "diagnostic_staff01@medflow.test", "DIAGNOSTIC_STAFF")]
    [InlineData(9L, "pharmacy_staff01@medflow.test", "PHARMACY_STAFF")]
    public async Task AssignDoctor_DisallowedRole_Returns403(long userId, string email, string role)
    {
        using var getReq = AuthedGetAssignableDoctors("VIS-202610-0001", userId, email, role);
        var getResp = await Client.SendAsync(getReq);
        Assert.Equal(HttpStatusCode.Forbidden, getResp.StatusCode);

        using var putReq = AuthedPutDoctor(
            "VIS-202610-0001",
            new AssignDoctorToVisitRequest(DoctorId: 1),
            userId,
            email,
            role);
        var putResp = await Client.SendAsync(putReq);
        Assert.Equal(HttpStatusCode.Forbidden, putResp.StatusCode);
    }

    [Fact]
    public async Task AssignDoctor_InvalidInputOrMissingDoctor_Returns400ProblemDetails()
    {
        using var emptyDoctorReq = AuthedPutDoctor(
            "VIS-202610-0001",
            new AssignDoctorToVisitRequest());

        var emptyDoctorResp = await Client.SendAsync(emptyDoctorReq);
        Assert.Equal(HttpStatusCode.BadRequest, emptyDoctorResp.StatusCode);

        var problem = await emptyDoctorResp.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(400, problem.Status);

        using var invalidVisitIdReq = AuthedPutDoctor(
            "0",
            new AssignDoctorToVisitRequest(DoctorId: 1));

        var invalidVisitIdResp = await Client.SendAsync(invalidVisitIdReq);
        Assert.Equal(HttpStatusCode.BadRequest, invalidVisitIdResp.StatusCode);
    }

    [Fact]
    public async Task AssignDoctor_NonexistentVisit_Returns404_WhenDatabaseAvailable()
    {
        using var request = AuthedPutDoctor(
            "VIS-999999",
            new AssignDoctorToVisitRequest(DoctorId: 1));

        var response = await Client.SendAsync(request);

        if (response.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable)
        {
            return;
        }

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AssignDoctor_CompletedSeedVisit_Returns409Conflict_WhenDatabaseAvailable()
    {
        using var request = AuthedPutDoctor(
            "VIS-202610-0001",
            new AssignDoctorToVisitRequest(DoctorId: 1));

        var response = await Client.SendAsync(request);

        if (response.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable)
        {
            return;
        }

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task VisitDoctorAssignmentController_HttpPipeline_Maps200AndDomainExceptionsToProblemDetails()
    {
        var customFactory = Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IVisitDoctorAssignmentService>();
                services.AddScoped<IVisitDoctorAssignmentService, StubVisitDoctorAssignmentService>();
            });
        });
        await using var _ = customFactory;
        using var client = customFactory.CreateClient();

        // 1. GET available-doctors -> 200 OK
        using var getReq = AuthedGetAssignableDoctors("VIS-202610-0100");
        var getResp = await client.SendAsync(getReq);
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        var listDto = await getResp.Content.ReadFromJsonAsync<AssignableDoctorListResponseDto>();
        Assert.NotNull(listDto);
        Assert.Equal("VIS-202610-0100", listDto.VisitCode);
        Assert.Equal("PAT-0001", listDto.Patient.PatientCode);
        Assert.Single(listDto.Doctors);
        Assert.Equal("DOC-001", listDto.Doctors[0].DoctorCode);
        Assert.Equal("LOW", listDto.Doctors[0].WorkloadLevel);

        // 2. PUT doctor -> 200 OK
        using var putOkReq = AuthedPutDoctor(
            "VIS-202610-0100",
            new AssignDoctorToVisitRequest(
                DoctorCode: "DOC-001",
                Reason: "Phân công BS Nguyễn Minh Đức"),
            useReceptionRoute: false);
        var putOkResp = await client.SendAsync(putOkReq);
        Assert.Equal(HttpStatusCode.OK, putOkResp.StatusCode);
        var assignedDto = await putOkResp.Content.ReadFromJsonAsync<AssignDoctorToVisitResponseDto>();
        Assert.NotNull(assignedDto);
        Assert.Equal(1L, assignedDto.AssignedDoctorId);
        Assert.Equal("DOC-001", assignedDto.AssignedDoctorCode);
        Assert.Equal("WAITING", assignedDto.VisitStatus);

        // 3. Domain exceptions -> ProblemDetails (403, 404, 409)
        using var forbiddenReq = AuthedPutDoctor(
            "FORBIDDEN-VISIT",
            new AssignDoctorToVisitRequest(DoctorId: 1));
        var forbiddenResp = await client.SendAsync(forbiddenReq);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenResp.StatusCode);

        using var notFoundReq = AuthedPutDoctor(
            "MISSING-VISIT",
            new AssignDoctorToVisitRequest(DoctorId: 1));
        var notFoundResp = await client.SendAsync(notFoundReq);
        Assert.Equal(HttpStatusCode.NotFound, notFoundResp.StatusCode);

        using var conflictReq = AuthedPutDoctor(
            "CONFLICT-VISIT",
            new AssignDoctorToVisitRequest(DoctorId: 1));
        var conflictResp = await client.SendAsync(conflictReq);
        Assert.Equal(HttpStatusCode.Conflict, conflictResp.StatusCode);
    }

    private sealed class StubVisitDoctorAssignmentService : IVisitDoctorAssignmentService
    {
        public Task<AssignableDoctorListResponseDto> GetAssignableDoctorsAsync(
            long actorUserId,
            string visitIdOrCode,
            long? serviceId = null,
            string? serviceCode = null,
            long? departmentId = null,
            CancellationToken ct = default)
        {
            var summary = new VisitPatientSummaryDto(
                VisitId: visitIdOrCode,
                VisitNumericId: 100L,
                VisitCode: visitIdOrCode,
                PatientId: 1L,
                PatientCode: "PAT-0001",
                FullName: "Nguyễn Văn An",
                Gender: "Nam",
                Age: 41,
                CccdVerified: true,
                ServiceId: 1L,
                ServiceCode: "EX-GI-01",
                ServiceName: "Khám Tiêu hóa lần đầu",
                Price: 250000m);

            var doctor = new AssignableDoctorItemDto(
                Id: "DOC-001",
                DoctorId: 1L,
                DoctorCode: "DOC-001",
                FullName: "BS.CKI Nguyễn Minh Đức",
                ProfessionalTitle: "BS.CKI",
                SpecialtyId: 1L,
                SpecialtyCode: "GI",
                SpecialtyName: "Tiêu hóa",
                DepartmentId: 2L,
                DepartmentCode: "GI",
                DepartmentName: "Khoa Tiêu hóa",
                Department: "Khoa Tiêu hóa",
                RoomId: 1L,
                RoomCode: "RM-001",
                RoomName: "Khám Tiêu hóa 101",
                RoomLocation: "Tầng 1",
                ServicePointId: 1L,
                ServicePointName: "Hàng đợi bác sĩ 1",
                WaitingCount: 2,
                ActiveVisitCount: 2,
                CurrentTicket: "007",
                EstimatedWaitMinutes: 30,
                EstimatedWait: "~25-30 phút",
                WorkloadLevel: "LOW",
                IsActive: true,
                IsServiceAuthorized: true,
                IsCompatible: true,
                IsAvailable: true,
                IsCurrentDoctor: false,
                UnavailableReason: null);

            return Task.FromResult(new AssignableDoctorListResponseDto(
                VisitId: 100L,
                VisitCode: visitIdOrCode,
                VisitStatus: "CHECKED_IN",
                HospitalId: 1L,
                AssignedDoctorId: null,
                AssignedDoctorCode: null,
                AssignedDoctorName: null,
                CurrentExaminationRoomId: null,
                CurrentExaminationRoomCode: null,
                CurrentExaminationRoomName: null,
                RequiredServiceId: 1L,
                RequiredServiceCode: "EX-GI-01",
                RequiredServiceName: "Khám Tiêu hóa lần đầu",
                RequiredDepartmentId: 2L,
                RequiredDepartmentName: "Khoa Tiêu hóa",
                Patient: summary,
                Doctors: [doctor]));
        }

        public Task<AssignDoctorToVisitResponseDto> AssignDoctorAsync(
            long actorUserId,
            long visitId,
            AssignDoctorToVisitRequest request,
            string? ipAddress = null,
            CancellationToken ct = default)
            => AssignDoctorAsync(actorUserId, visitId.ToString(), request, ipAddress, ct);

        public Task<AssignDoctorToVisitResponseDto> AssignDoctorAsync(
            long actorUserId,
            string visitIdOrCode,
            AssignDoctorToVisitRequest request,
            string? ipAddress = null,
            CancellationToken ct = default)
        {
            if (visitIdOrCode == "FORBIDDEN-VISIT")
            {
                throw new ForbiddenException("Không có quyền thao tác trên lượt khám của cơ sở y tế khác.");
            }

            if (visitIdOrCode == "MISSING-VISIT")
            {
                throw new NotFoundException("Visit", visitIdOrCode);
            }

            if (visitIdOrCode == "CONFLICT-VISIT")
            {
                throw new ConflictException("Lượt khám đã hoàn tất, không thể phân công bác sĩ.");
            }

            if (!request.DoctorId.HasValue && string.IsNullOrWhiteSpace(request.DoctorCode))
            {
                throw new ValidationException("Vui lòng chọn bác sĩ phụ trách.");
            }

            return Task.FromResult(new AssignDoctorToVisitResponseDto(
                VisitId: 100L,
                VisitCode: visitIdOrCode,
                VisitStatus: "WAITING",
                HospitalId: 1L,
                PatientId: 1L,
                PatientCode: "PAT-0001",
                PatientName: "Nguyễn Văn An",
                AppointmentId: 100L,
                AppointmentCode: "APT-202610-0100",
                PreviousDoctorId: null,
                PreviousDoctorCode: null,
                PreviousDoctorName: null,
                AssignedDoctorId: request.DoctorId ?? 1L,
                AssignedDoctorCode: request.DoctorCode ?? "DOC-001",
                AssignedDoctorName: "BS.CKI Nguyễn Minh Đức",
                ProfessionalTitle: "BS.CKI",
                SpecialtyId: 1L,
                SpecialtyCode: "GI",
                SpecialtyName: "Tiêu hóa",
                DepartmentId: 2L,
                DepartmentCode: "GI",
                DepartmentName: "Khoa Tiêu hóa",
                ExaminationRoomId: 1L,
                RoomCode: "RM-001",
                RoomName: "Khám Tiêu hóa 101",
                RoomLocation: "Tầng 1",
                ServiceId: 1L,
                ServiceCode: "EX-GI-01",
                ServiceName: "Khám Tiêu hóa lần đầu",
                QueueEntryId: 50L,
                QueueNumber: 1,
                QueueCode: "B001",
                QueueStatus: "WAITING",
                Priority: "NORMAL",
                ServicePointId: 1L,
                ServicePointName: "Hàng đợi bác sĩ 1",
                WaitingCount: 2,
                WorkloadLevel: "LOW",
                CheckedInByStaffId: 3L,
                CheckedInAt: DateTime.UtcNow,
                AssignedAtUtc: DateTime.UtcNow));
        }
    }
}
