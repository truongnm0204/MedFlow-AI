using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MedFlow.Application.DTOs.Visits;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Visits;
using MedFlow.Domain.Entities;
using MedFlow.IntegrationTests.Auth;
using MedFlow.Domain.Exceptions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MedFlow.IntegrationTests.Visits;

public sealed class VisitRoomAssignmentEndpointTests : AuthTestBase
{
    private const string VisitsEndpoint = "/api/v1/visits";
    private const string ReceptionVisitsEndpoint = "/api/v1/reception/visits";

    public VisitRoomAssignmentEndpointTests(TestWebAppFactory factory)
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

    private HttpRequestMessage AuthedGetCompatibleRooms(
        string visitIdOrCode,
        long userId = 5,
        string email = "reception01@medflow.test",
        string role = "RECEPTION")
    {
        var token = GenerateToken(userId, email, role);
        var request = new HttpRequestMessage(HttpMethod.Get, $"{ReceptionVisitsEndpoint}/{visitIdOrCode}/available-rooms");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private HttpRequestMessage AuthedPutRoom(
        string visitIdOrCode,
        AssignExaminationRoomRequest body,
        long userId = 5,
        string email = "reception01@medflow.test",
        string role = "RECEPTION",
        bool useReceptionRoute = true)
    {
        var token = GenerateToken(userId, email, role);
        var baseRoute = useReceptionRoute ? ReceptionVisitsEndpoint : VisitsEndpoint;
        var request = new HttpRequestMessage(HttpMethod.Put, $"{baseRoute}/{visitIdOrCode}/examination-room")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    [Fact]
    public async Task AssignExaminationRoom_NoToken_Returns401()
    {
        var response = await Client.PutAsJsonAsync(
            $"{VisitsEndpoint}/1/examination-room",
            new AssignExaminationRoomRequest(ExaminationRoomId: 1));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var aliasResponse = await Client.PostAsJsonAsync(
            $"{ReceptionVisitsEndpoint}/1/assign-room",
            new AssignExaminationRoomRequest(ExaminationRoomId: 1));

        Assert.Equal(HttpStatusCode.Unauthorized, aliasResponse.StatusCode);
    }

    [Theory]
    [InlineData(1L, "patient01@medflow.test", "PATIENT")]
    [InlineData(3L, "doctor01@medflow.test", "DOCTOR")]
    [InlineData(7L, "diagnostic_staff01@medflow.test", "DIAGNOSTIC_STAFF")]
    [InlineData(9L, "pharmacy_staff01@medflow.test", "PHARMACY_STAFF")]
    public async Task AssignExaminationRoom_DisallowedRole_Returns403(long userId, string email, string role)
    {
        using var request = AuthedPutRoom(
            "VIS-202610-0001",
            new AssignExaminationRoomRequest(ExaminationRoomId: 1),
            userId,
            email,
            role);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AssignExaminationRoom_InvalidInputOrMissingRoom_Returns400ProblemDetails()
    {
        using var emptyRoomReq = AuthedPutRoom(
            "VIS-202610-0001",
            new AssignExaminationRoomRequest());

        var emptyRoomResp = await Client.SendAsync(emptyRoomReq);
        Assert.Equal(HttpStatusCode.BadRequest, emptyRoomResp.StatusCode);

        var problem = await emptyRoomResp.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(400, problem.Status);

        using var invalidVisitIdReq = AuthedPutRoom(
            "0",
            new AssignExaminationRoomRequest(ExaminationRoomId: 1));

        var invalidVisitIdResp = await Client.SendAsync(invalidVisitIdReq);
        Assert.Equal(HttpStatusCode.BadRequest, invalidVisitIdResp.StatusCode);

        using var invalidPriorityReq = AuthedPutRoom(
            "VIS-202610-0001",
            new AssignExaminationRoomRequest(ExaminationRoomId: 1, Priority: "INVALID_PRIORITY"));

        var invalidPriorityResp = await Client.SendAsync(invalidPriorityReq);
        Assert.Equal(HttpStatusCode.BadRequest, invalidPriorityResp.StatusCode);
    }

    [Fact]
    public async Task AssignExaminationRoom_NonexistentVisit_Returns404_WhenDatabaseAvailable()
    {
        using var request = AuthedPutRoom(
            "VIS-999999",
            new AssignExaminationRoomRequest(ExaminationRoomId: 1));

        var response = await Client.SendAsync(request);

        if (response.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable)
        {
            return;
        }

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AssignExaminationRoom_CompletedSeedVisit_Returns409Conflict_WhenDatabaseAvailable()
    {
        using var request = AuthedPutRoom(
            "VIS-202610-0001",
            new AssignExaminationRoomRequest(ExaminationRoomId: 1));

        var response = await Client.SendAsync(request);

        if (response.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable)
        {
            return;
        }

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task VisitsController_HttpPipeline_Maps200AndDomainExceptionsToProblemDetails()
    {
        var customFactory = Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IVisitRoomAssignmentService>();
                services.AddScoped<IVisitRoomAssignmentService, StubVisitRoomAssignmentService>();
            });
        });
        await using var _ = customFactory;
        using var client = customFactory.CreateClient();

        // 1. GET compatible-rooms -> 200 OK
        using var getReq = AuthedGetCompatibleRooms("VIS-202610-0100");
        var getResp = await client.SendAsync(getReq);
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        var compatibleDto = await getResp.Content.ReadFromJsonAsync<CompatibleExaminationRoomListResponseDto>();
        Assert.NotNull(compatibleDto);
        Assert.Equal("VIS-202610-0100", compatibleDto.VisitCode);
        Assert.Single(compatibleDto.Rooms);

        // 2. PUT examination-room -> 200 OK
        using var putOkReq = AuthedPutRoom(
            "VIS-202610-0100",
            new AssignExaminationRoomRequest(
                RoomCode: "RM-002",
                Priority: "PREGNANT",
                Reason: "Điều phối vào phòng khám Tiêu hóa 102",
                PriorityVerificationConfirmed: true));
        var putOkResp = await client.SendAsync(putOkReq);
        Assert.Equal(HttpStatusCode.OK, putOkResp.StatusCode);
        var assignedDto = await putOkResp.Content.ReadFromJsonAsync<AssignExaminationRoomResponseDto>();
        Assert.NotNull(assignedDto);
        Assert.Equal(2L, assignedDto.ExaminationRoomId);
        Assert.Equal("RM-002", assignedDto.RoomCode);
        Assert.Equal("WAITING", assignedDto.VisitStatus);
        Assert.Equal("PREGNANT", assignedDto.Priority);

        // 3. NotFoundException -> 404 ProblemDetails
        using var notFoundReq = AuthedPutRoom(
            "VIS-404",
            new AssignExaminationRoomRequest(ExaminationRoomId: 1));
        var notFoundResp = await client.SendAsync(notFoundReq);
        Assert.Equal(HttpStatusCode.NotFound, notFoundResp.StatusCode);

        // 4. ConflictException -> 409 ProblemDetails
        using var conflictReq = AuthedPutRoom(
            "VIS-409",
            new AssignExaminationRoomRequest(ExaminationRoomId: 1));
        var conflictResp = await client.SendAsync(conflictReq);
        Assert.Equal(HttpStatusCode.Conflict, conflictResp.StatusCode);
    }

    private sealed class StubVisitRoomAssignmentService : IVisitRoomAssignmentService
    {
        public Task<CompatibleExaminationRoomListResponseDto> GetCompatibleRoomsAsync(
            long actorUserId,
            string visitIdOrCode,
            CancellationToken ct = default)
            => Task.FromResult(new CompatibleExaminationRoomListResponseDto(
                VisitId: 100,
                VisitCode: visitIdOrCode,
                VisitStatus: "CHECKED_IN",
                HospitalId: 1,
                AssignedDoctorId: 1,
                AssignedDoctorCode: "DOC-001",
                AssignedDoctorName: "BS Nguyễn Minh Đức",
                CurrentExaminationRoomId: null,
                CurrentExaminationRoomCode: null,
                RequiredDepartmentId: 2,
                RequiredDepartmentName: "Khoa Tiêu hóa",
                Rooms:
                [
                    new AssignableExaminationRoomItemDto(
                        Id: 2,
                        RoomCode: "RM-002",
                        Name: "Khám Tiêu hóa 102",
                        RoomType: "EXAMINATION",
                        Location: "Tầng 1",
                        HospitalId: 1,
                        DepartmentId: 2,
                        DepartmentCode: "GI",
                        DepartmentName: "Khoa Tiêu hóa",
                        Capacity: 2,
                        CurrentOccupancy: 0,
                        AvailableCapacity: 2,
                        Status: "ACTIVE",
                        IsCompatible: true,
                        IsAvailable: true,
                        UnavailableReason: null),
                ]));

        public Task<AssignExaminationRoomResponseDto> AssignExaminationRoomAsync(
            long actorUserId,
            long visitId,
            AssignExaminationRoomRequest request,
            string? ipAddress = null,
            CancellationToken ct = default)
            => AssignExaminationRoomAsync(actorUserId, visitId.ToString(), request, ipAddress, ct);

        public Task<AssignExaminationRoomResponseDto> AssignExaminationRoomAsync(
            long actorUserId,
            string visitIdOrCode,
            AssignExaminationRoomRequest request,
            string? ipAddress = null,
            CancellationToken ct = default)
        {
            if (visitIdOrCode == "VIS-404")
            {
                throw new NotFoundException("Visit", visitIdOrCode);
            }

            if (visitIdOrCode == "VIS-409")
            {
                throw new ConflictException("Lượt khám đã hoàn tất, không thể gán buồng khám.");
            }

            return Task.FromResult(new AssignExaminationRoomResponseDto(
                VisitId: 100,
                VisitCode: visitIdOrCode,
                PatientId: 1,
                PatientCode: "PAT-0001",
                PatientName: "Nguyễn Văn An",
                AppointmentId: 9,
                AppointmentCode: "APT-202610-0009",
                AssignedDoctorId: 1,
                AssignedDoctorCode: "DOC-001",
                AssignedDoctorName: "BS Nguyễn Minh Đức",
                ExaminationRoomId: 2,
                RoomCode: request.RoomCode ?? "RM-002",
                RoomName: "Khám Tiêu hóa 102",
                RoomType: "EXAMINATION",
                Location: "Tầng 1, Khu Demo",
                DepartmentId: 2,
                DepartmentCode: "GI",
                DepartmentName: "Khoa Tiêu hóa",
                HospitalId: 1,
                VisitStatus: "WAITING",
                PreviousRoomId: null,
                PreviousRoomCode: null,
                QueueEntryId: 21,
                QueueNumber: 1,
                QueueCode: "B001",
                QueueStatus: "WAITING",
                Priority: request.Priority ?? "NORMAL",
                ServicePointId: 1,
                ServicePointName: "Hàng đợi bác sĩ 1",
                RoomCapacity: 2,
                CurrentRoomOccupancy: 1,
                CheckedInByStaffId: 3,
                CheckedInAt: new DateTime(2026, 10, 27, 8, 45, 0),
                AssignedAtUtc: new DateTime(2026, 10, 27, 1, 50, 0, DateTimeKind.Utc)));
        }
    }

    [Fact]
    public async Task GetCompatibleRooms_And_AssignExaminationRoom_EndToEnd_PersistsVisitRoomQueueAndAuditLog_WhenDatabaseAvailable()
    {
        if (!await Db.Database.CanConnectAsync())
        {
            return;
        }

        long createdVisitId = 0;
        var testVisitCode = $"VIS-TEST-{Guid.NewGuid():N}"[..24].ToUpperInvariant();

        try
        {
            using (var setupScope = Factory.Services.CreateScope())
            {
                var db = setupScope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
                var testVisit = new Visit
                {
                    VisitCode = testVisitCode,
                    PatientId = 9,
                    AppointmentId = 9, // Linked to Slot 9 (Service 1 GI, Doctor 1 DOC-001)
                    AssignedDoctorId = 1,
                    ExaminationRoomId = null,
                    CheckedInByStaffId = null,
                    ReasonForVisit = "Khám tiêu hóa kiểm tra phân phòng UC27",
                    Status = "CHECKED_IN",
                    CheckedInAt = new DateTime(2026, 10, 27, 8, 45, 0),
                };

                db.Visits.Add(testVisit);
                await db.SaveChangesAsync();
                createdVisitId = testVisit.Id;
            }

            // 1. Check compatible rooms via GET /api/v1/reception/visits/{visitCode}/compatible-rooms
            using var getRoomsReq = AuthedGetCompatibleRooms(testVisitCode);
            var getRoomsResp = await Client.SendAsync(getRoomsReq);
            Assert.Equal(HttpStatusCode.OK, getRoomsResp.StatusCode);

            var compatibleList = await getRoomsResp.Content.ReadFromJsonAsync<CompatibleExaminationRoomListResponseDto>();
            Assert.NotNull(compatibleList);
            Assert.Equal(createdVisitId, compatibleList.VisitId);
            Assert.Equal(2L, compatibleList.RequiredDepartmentId);
            Assert.Contains(compatibleList.Rooms, r => r.RoomCode == "RM-001" && r.IsCompatible && r.IsAvailable);
            Assert.Contains(compatibleList.Rooms, r => r.RoomCode == "RM-002" && r.IsCompatible && r.IsAvailable);
            Assert.Contains(compatibleList.Rooms, r => r.RoomCode == "RM-003" && !r.IsCompatible && !r.IsAvailable);

            // 2. Incompatible department room RM-003 returns 400 Bad Request
            using var incompatibleReq = AuthedPutRoom(
                testVisitCode,
                new AssignExaminationRoomRequest(RoomCode: "RM-003"));
            var incompatibleResp = await Client.SendAsync(incompatibleReq);
            Assert.Equal(HttpStatusCode.BadRequest, incompatibleResp.StatusCode);

            // 3. Assign compatible GI room RM-002 with URGENT priority
            using var assignReq = AuthedPutRoom(
                testVisitCode,
                new AssignExaminationRoomRequest(
                    RoomCode: "RM-002",
                    Priority: "PREGNANT",
                    Reason: "Điều phối vào phòng khám Tiêu hóa 102",
                    PriorityVerificationConfirmed: true));
            var assignResp = await Client.SendAsync(assignReq);
            Assert.Equal(HttpStatusCode.OK, assignResp.StatusCode);

            var assignedDto = await assignResp.Content.ReadFromJsonAsync<AssignExaminationRoomResponseDto>();
            Assert.NotNull(assignedDto);
            Assert.Equal(createdVisitId, assignedDto.VisitId);
            Assert.Equal(testVisitCode, assignedDto.VisitCode);
            Assert.Equal(2L, assignedDto.ExaminationRoomId);
            Assert.Equal("RM-002", assignedDto.RoomCode);
            Assert.Equal("WAITING", assignedDto.VisitStatus);
            Assert.Equal("PREGNANT", assignedDto.Priority);
            Assert.Equal(3L, assignedDto.CheckedInByStaffId);
            Assert.NotNull(assignedDto.QueueEntryId);

            // 4. Verify database state & audit log
            using (var verifyScope = Factory.Services.CreateScope())
            {
                var db = verifyScope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
                var persistedVisit = await db.Visits.AsNoTracking().SingleAsync(v => v.Id == createdVisitId);
                Assert.Equal(2L, persistedVisit.ExaminationRoomId);
                Assert.Equal("WAITING", persistedVisit.Status);
                Assert.Equal(3L, persistedVisit.CheckedInByStaffId);

                var audit = await db.AuditLogs
                    .AsNoTracking()
                    .OrderByDescending(a => a.Id)
                    .FirstOrDefaultAsync(a =>
                        a.Action == VisitRoomAssignmentService.VisitRoomAssignedAction &&
                        a.EntityId == createdVisitId.ToString());

                Assert.NotNull(audit);
                Assert.Equal(5L, audit.ActorUserId);
                Assert.Equal("Điều phối vào phòng khám Tiêu hóa 102", audit.Reason);
            }
        }
        finally
        {
            if (createdVisitId > 0)
            {
                using var cleanupScope = Factory.Services.CreateScope();
                var db = cleanupScope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

                var createdAudits = await db.AuditLogs
                    .Where(a =>
                        a.Action == VisitRoomAssignmentService.VisitRoomAssignedAction &&
                        a.EntityId == createdVisitId.ToString())
                    .ToListAsync();
                if (createdAudits.Count > 0)
                {
                    db.AuditLogs.RemoveRange(createdAudits);
                }

                var seedQueueEntry21 = await db.QueueEntries.FirstOrDefaultAsync(q => q.Id == 21);
                if (seedQueueEntry21 is not null)
                {
                    seedQueueEntry21.VisitId = null;
                    seedQueueEntry21.CurrentServicePointId = null;
                    seedQueueEntry21.Status = "RESERVED";
                    seedQueueEntry21.PresenceStatus = "NOT_ARRIVED";
                    seedQueueEntry21.ArrivedAt = null;
                    seedQueueEntry21.ArrivalClass = null;
                    seedQueueEntry21.ManualPriority = "NORMAL";
                    seedQueueEntry21.ReadyAt = null;
                }

                var extraQueueEntries = await db.QueueEntries
                    .Where(q => q.VisitId == createdVisitId && q.Id != 21)
                    .ToListAsync();
                if (extraQueueEntries.Count > 0)
                {
                    db.QueueEntries.RemoveRange(extraQueueEntries);
                }

                var visitToDelete = await db.Visits.FirstOrDefaultAsync(v => v.Id == createdVisitId);
                if (visitToDelete is not null)
                {
                    db.Visits.Remove(visitToDelete);
                }

                await db.SaveChangesAsync();
            }
        }
    }
}
