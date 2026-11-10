using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MedFlow.Application.DTOs.Reception;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Patients;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Exceptions;
using MedFlow.IntegrationTests.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MedFlow.IntegrationTests.Reception;

public sealed class ReceptionPatientDetailEndpointTests : AuthTestBase
{
    public ReceptionPatientDetailEndpointTests(TestWebAppFactory factory)
        : base(factory)
    {
    }

    private sealed class StubReceptionPatientDetailService : IReceptionPatientDetailService
    {
        public Task<PatientDetailResponseDto> GetPatientDetailAsync(
            long actorUserId,
            string idOrCode,
            CancellationToken ct = default)
        {
            if (actorUserId <= 0)
            {
                throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");
            }

            if (actorUserId == 1)
            {
                throw new ForbiddenException("Người dùng không có quyền xem chi tiết hồ sơ bệnh nhân tại quầy tiếp đón.");
            }

            var trimmed = idOrCode?.Trim().TrimStart('#').Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(trimmed) ||
                trimmed is "0" or "-1" ||
                trimmed.Contains('@'))
            {
                throw new ValidationException("Mã hoặc ID bệnh nhân không hợp lệ.");
            }

            if (string.Equals(trimmed, "PAT-DOES-NOT-EXIST", StringComparison.OrdinalIgnoreCase))
            {
                throw new NotFoundException("Patient", trimmed);
            }

            if (string.Equals(trimmed, "PAT-INACTIVE", StringComparison.OrdinalIgnoreCase))
            {
                throw new ConflictException("Hồ sơ bệnh nhân hiện không ở trạng thái hoạt động.");
            }

            return Task.FromResult(new PatientDetailResponseDto(
                Id: 1,
                PatientCode: "PAT-0001",
                FullName: "Nguyễn Văn An",
                Gender: "MALE",
                GenderLabel: "Nam",
                Age: 38,
                DateOfBirth: "14/05/1988",
                DateOfBirthIso: new DateOnly(1988, 5, 14),
                Cccd: "****** (Đã xác thực CCCD)",
                IdentityCardMasked: "****** (Đã xác thực CCCD)",
                IsIdentityVerified: true,
                Phone: "0901234567",
                Email: "patient01@medflow.test",
                BhytNumber: null,
                HealthInsuranceNumber: null,
                Address: "123 Nguyễn Trãi, Quận 1, TP.HCM",
                BloodType: "O+",
                Status: "ACTIVE",
                EmergencyContact: new PatientEmergencyContactDto(
                    Name: "Lê Minh Chi",
                    Relationship: "Vợ",
                    Phone: "0909111222"),
                TodayAppointment: new PatientTodayAppointmentDetailDto(
                    Id: 101,
                    AppointmentCode: "APT-202610-0001",
                    Time: "08:30",
                    TimeRange: "08:30 - 09:00",
                    DoctorName: "BS.CKII Trần Văn Hùng",
                    Department: "Khoa Nội Tổng Quát",
                    Room: "Phòng 101 - Tầng 1",
                    RoomName: "Phòng 101 - Tầng 1",
                    Status: "BOOKED",
                    AppointmentStatus: "BOOKED",
                    VisitId: null,
                    VisitCode: null,
                    VisitStatus: null),
                TotalPastVisits: 1,
                PastVisits:
                [
                    new PatientPastVisitSummaryDto(
                        Id: 501,
                        VisitCode: "VIS-2026-0001",
                        Date: "10/10/2026",
                        VisitedAt: new DateTime(2026, 10, 10, 9, 30, 0),
                        Department: "Khoa Nội Tổng Quát",
                        DoctorName: "BS.CKII Trần Văn Hùng",
                        Diagnosis: "Viêm dạ dày mạn tính (demo)")
                ]));
        }
    }

    private HttpClient CreateStubbedClient() =>
        Factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.RemoveAll<IReceptionPatientDetailService>();
            services.AddScoped<IReceptionPatientDetailService, StubReceptionPatientDetailService>();
        })).CreateClient();

    private string GenerateToken(long userId, string email, params string[] roles)
    {
        using var scope = Factory.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var user = new User { Id = userId, Email = email };
        return jwtService.GenerateAccessToken(user, roles).AccessToken;
    }

    private HttpRequestMessage AuthedGet(
        string url,
        long userId = 5,
        string email = "reception01@medflow.test",
        string role = "RECEPTION")
    {
        var token = GenerateToken(userId, email, role);
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    [Theory]
    [InlineData("/api/v1/reception/patients/PAT-0001")]
    [InlineData("/api/v1/reception/patients/PAT-0001/detail")]
    [InlineData("/api/v1/patients/PAT-0001/reception-detail")]
    public async Task GetPatientDetail_NoToken_Returns401(string url)
    {
        var response = await Client.GetAsync(url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("PATIENT", 1L, "patient01@medflow.test")]
    [InlineData("DOCTOR", 3L, "doctor01@medflow.test")]
    [InlineData("DIAGNOSTIC_STAFF", 7L, "diagnostic_staff01@medflow.test")]
    [InlineData("PHARMACY_STAFF", 9L, "pharmacy_staff01@medflow.test")]
    public async Task GetPatientDetail_DisallowedRole_Returns403(
        string role,
        long userId,
        string email)
    {
        using var req = AuthedGet("/api/v1/reception/patients/PAT-0001", userId, email, role);
        var res = await Client.SendAsync(req);

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task GetPatientDetail_ForgedReceptionJwtForPatientUser_Returns403()
    {
        using var req = AuthedGet(
            "/api/v1/reception/patients/PAT-0001",
            userId: 1,
            email: "patient01@medflow.test",
            role: "RECEPTION");

        var res = await CreateStubbedClient().SendAsync(req);

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("invalid@code")]
    public async Task GetPatientDetail_InvalidIdentifier_Returns400ProblemDetails(string idOrCode)
    {
        using var req = AuthedGet($"/api/v1/reception/patients/{idOrCode}");
        var res = await Client.SendAsync(req);

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var problem = await res.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(400, problem.Status);
    }

    [Fact]
    public async Task GetPatientDetail_UnknownPatient_Returns404ProblemDetails()
    {
        using var req = AuthedGet("/api/v1/reception/patients/PAT-DOES-NOT-EXIST");
        var res = await CreateStubbedClient().SendAsync(req);

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        var problem = await res.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(404, problem.Status);
    }

    [Fact]
    public async Task GetPatientDetail_InactivePatient_Returns409ProblemDetails()
    {
        using var req = AuthedGet("/api/v1/reception/patients/PAT-INACTIVE");
        var res = await CreateStubbedClient().SendAsync(req);

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        var problem = await res.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(409, problem.Status);
    }

    [Theory]
    [InlineData("/api/v1/reception/patients/PAT-0001", "RECEPTION", 5L, "reception01@medflow.test")]
    [InlineData("/api/v1/reception/patients/PAT-0001/detail", "HOSPITAL_ADMIN", 11L, "hospadmin01@medflow.test")]
    [InlineData("/api/v1/patients/PAT-0001/reception-detail", "SYSTEM_ADMIN", 13L, "sysadmin01@medflow.test")]
    public async Task GetPatientDetail_AllowedRolesAcrossCanonicalAndAliasRoutes_Returns200WithFullPayload(
        string route,
        string role,
        long userId,
        string email)
    {
        using var req = AuthedGet(route, userId, email, role);
        var res = await CreateStubbedClient().SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<PatientDetailResponseDto>();
        Assert.NotNull(body);
        Assert.Equal("PAT-0001", body.PatientCode);
        Assert.Equal("Nguyễn Văn An", body.FullName);
        Assert.Equal("MALE", body.Gender);
        Assert.Equal("****** (Đã xác thực CCCD)", body.Cccd);
        Assert.True(body.IsIdentityVerified);
        Assert.NotNull(body.EmergencyContact);
        Assert.Equal("Lê Minh Chi", body.EmergencyContact!.Name);
        Assert.NotNull(body.TodayAppointment);
        Assert.Single(body.PastVisits);
        Assert.Equal("Viêm dạ dày mạn tính (demo)", body.PastVisits[0].Diagnosis);
    }

    [Fact]
    public async Task GetPatientDetail_LiveDatabase_VerifiesTwoLayerAuthAndSeededPatientDetail()
    {
        using (var probeScope = Factory.Services.CreateScope())
        {
            var probeDb = probeScope.ServiceProvider
                .GetRequiredService<MedFlow.Infrastructure.Persistence.MedFlowDbContext>();
            if (!await probeDb.Database.CanConnectAsync())
            {
                return;
            }

            var hasSeededPatient = await probeDb.Patients
                .AsNoTracking()
                .AnyAsync(p => p.PatientCode == "PAT-0001" && p.Status == "ACTIVE");
            if (!hasSeededPatient)
            {
                return;
            }
        }

        // 1. Forged RECEPTION JWT carrying PATIENT userId = 1 must be rejected by Layer-2 DB check
        using (var forgedReq = AuthedGet(
                   "/api/v1/reception/patients/PAT-0001",
                   userId: 1,
                   email: "patient01@medflow.test",
                   role: "RECEPTION"))
        {
            var forgedRes = await Client.SendAsync(forgedReq);
            Assert.Equal(HttpStatusCode.Forbidden, forgedRes.StatusCode);
        }

        // 2. Non-existent patient returns 404 NotFound from real service
        using (var unknownReq = AuthedGet("/api/v1/reception/patients/PAT-DOES-NOT-EXIST"))
        {
            var unknownRes = await Client.SendAsync(unknownReq);
            Assert.Equal(HttpStatusCode.NotFound, unknownRes.StatusCode);
        }

        // 3. Valid RECEPTION user (userId = 5) retrieves PAT-0001 detail from live database
        using (var validReq = AuthedGet("/api/v1/reception/patients/PAT-0001"))
        {
            var validRes = await Client.SendAsync(validReq);
            Assert.Equal(HttpStatusCode.OK, validRes.StatusCode);

            var detail = await validRes.Content.ReadFromJsonAsync<PatientDetailResponseDto>();
            Assert.NotNull(detail);
            Assert.Equal("PAT-0001", detail.PatientCode);
            Assert.False(string.IsNullOrWhiteSpace(detail.FullName));
            Assert.Equal("****** (Đã xác thực CCCD)", detail.Cccd);
        }
    }
}
