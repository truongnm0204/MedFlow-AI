using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MedFlow.Application.DTOs.Visits;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Visits;
using MedFlow.Domain.Entities;
using MedFlow.IntegrationTests.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MedFlow.IntegrationTests.Visits;

public sealed class QueueStatusEndpointTests : AuthTestBase
{
    public QueueStatusEndpointTests(TestWebAppFactory factory)
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

    private HttpRequestMessage AuthedSend(
        HttpMethod method,
        string url,
        UpdateQueueStatusRequest body,
        long userId = 5,
        string email = "reception01@medflow.test",
        string role = "RECEPTION")
    {
        var token = GenerateToken(userId, email, role);
        var request = new HttpRequestMessage(method, url)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    [Fact]
    public async Task Endpoints_WithoutToken_Return401()
    {
        var getResponse = await Client.GetAsync("/api/v1/reception/queue-entries/1/status");
        Assert.Equal(HttpStatusCode.Unauthorized, getResponse.StatusCode);

        var patchResponse = await Client.PatchAsJsonAsync(
            "/api/v1/reception/queue-entries/1/status",
            new UpdateQueueStatusRequest(TargetStatus: "CALLED"));
        Assert.Equal(HttpStatusCode.Unauthorized, patchResponse.StatusCode);
    }

    [Theory]
    [InlineData("PATIENT", 1L, "patient01@medflow.test")]
    [InlineData("PHARMACY_STAFF", 9L, "pharmacy_staff01@medflow.test")]
    public async Task Endpoints_DisallowedRole_Return403(string role, long userId, string email)
    {
        using var getReq = AuthedGet("/api/v1/reception/queue-entries/1/status", userId, email, role);
        var getRes = await Client.SendAsync(getReq);
        Assert.Equal(HttpStatusCode.Forbidden, getRes.StatusCode);

        using var patchReq = AuthedSend(
            HttpMethod.Patch,
            "/api/v1/reception/queue-entries/1/status",
            new UpdateQueueStatusRequest(TargetStatus: "CALLED"),
            userId,
            email,
            role);
        var patchRes = await Client.SendAsync(patchReq);
        Assert.Equal(HttpStatusCode.Forbidden, patchRes.StatusCode);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("bad@id")]
    public async Task Endpoints_InvalidRouteIdentifier_Return400(string invalidId)
    {
        using var getReq = AuthedGet($"/api/v1/reception/queue-entries/{invalidId}/status");
        var getRes = await Client.SendAsync(getReq);
        Assert.Equal(HttpStatusCode.BadRequest, getRes.StatusCode);

        using var patchReq = AuthedSend(
            HttpMethod.Patch,
            $"/api/v1/reception/queue-entries/{invalidId}/status",
            new UpdateQueueStatusRequest(TargetStatus: "CALLED"));
        var patchRes = await Client.SendAsync(patchReq);
        Assert.Equal(HttpStatusCode.BadRequest, patchRes.StatusCode);
    }

    [Fact]
    public async Task UpdateQueueStatus_InvalidTargetStatusOrMissingSkipReason_Returns400ProblemDetails()
    {
        using var invalidStatusReq = AuthedSend(
            HttpMethod.Patch,
            "/api/v1/reception/queue-entries/1/status",
            new UpdateQueueStatusRequest(TargetStatus: "UNKNOWN_STATUS"));
        var invalidStatusRes = await Client.SendAsync(invalidStatusReq);
        Assert.Equal(HttpStatusCode.BadRequest, invalidStatusRes.StatusCode);
        var invalidStatusProblem = await invalidStatusRes.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(invalidStatusProblem);
        Assert.Equal(400, invalidStatusProblem.Status);

        using var missingReasonReq = AuthedSend(
            HttpMethod.Patch,
            "/api/v1/reception/queue-entries/1/status",
            new UpdateQueueStatusRequest(TargetStatus: "SKIPPED", Reason: "   "));
        var missingReasonRes = await Client.SendAsync(missingReasonReq);
        Assert.Equal(HttpStatusCode.BadRequest, missingReasonRes.StatusCode);
    }

    [Fact]
    public async Task Endpoints_NonExistentQueueEntryOrVisit_Return404()
    {
        using var getReq = AuthedGet("/api/v1/reception/queue-entries/999999/status");
        var getRes = await Client.SendAsync(getReq);
        Assert.Equal(HttpStatusCode.NotFound, getRes.StatusCode);

        using var patchReq = AuthedSend(
            HttpMethod.Patch,
            "/api/v1/reception/visits/VIS-999999/queue-status",
            new UpdateQueueStatusRequest(TargetStatus: "CALLED"));
        var patchRes = await Client.SendAsync(patchReq);
        Assert.Equal(HttpStatusCode.NotFound, patchRes.StatusCode);
    }

    [Fact]
    public async Task UpdateQueueStatus_SeededCompletedQueueEntry_Returns409Conflict()
    {
        using var patchReq = AuthedSend(
            HttpMethod.Patch,
            "/api/v1/reception/visits/VIS-202610-0001/queue-status",
            new UpdateQueueStatusRequest(TargetStatus: "WAITING"));
        var patchRes = await Client.SendAsync(patchReq);

        Assert.Equal(HttpStatusCode.Conflict, patchRes.StatusCode);
        var problem = await patchRes.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(409, problem.Status);
    }

    [Fact]
    public async Task ActiveQueueLifecycle_UpdatesStatusPersistsVisitStateVersionAndAuditLogs()
    {
        var visitCode = $"VIS-QST-{Guid.NewGuid():N}"[..22].ToUpperInvariant();
        long? createdVisitId = null;
        long? createdQueueId = null;
        long? crossHospitalVisitId = null;
        long? crossHospitalQueueId = null;
        long? crossHospitalRoomId = null;
        long? crossHospitalId = null;

        try
        {
            var seededVisit = new Visit
            {
                VisitCode = visitCode,
                PatientId = 1,
                AppointmentId = null,
                AssignedDoctorId = 1,
                ExaminationRoomId = 1, // RM-001 (Hospital 1)
                CheckedInByStaffId = 3,
                ReasonForVisit = "Khám dạ dày định kỳ",
                Status = "WAITING",
                CheckedInAt = new DateTime(2026, 10, 26, 8, 15, 0, DateTimeKind.Unspecified),
            };
            Db.Visits.Add(seededVisit);
            await Db.SaveChangesAsync();
            createdVisitId = seededVisit.Id;

            var seededQueue = new QueueEntry
            {
                VisitId = seededVisit.Id,
                AppointmentId = null,
                ServicePointId = 1,
                CurrentServicePointId = 1,
                QueueDate = new DateOnly(2026, 10, 26),
                QueueNumber = 45,
                QueueType = "EXAMINATION",
                Status = "WAITING",
                PresenceStatus = "PRESENT",
                ArrivalClass = "ON_TIME_OR_GRACE",
                ManualPriority = "NORMAL",
                StateVersion = 1,
                ArrivedAt = new DateTime(2026, 10, 26, 8, 15, 0, DateTimeKind.Unspecified),
            };
            Db.QueueEntries.Add(seededQueue);
            await Db.SaveChangesAsync();
            createdQueueId = seededQueue.Id;

            // Cross-hospital entry (Hospital 2) to verify 403 Forbidden for Hospital 1 reception staff
            var crossHospital = new Hospital
            {
                HospitalCode = $"HOS-Q2-{Guid.NewGuid():N}"[..14].ToUpperInvariant(),
                Name = "Bệnh viện Cơ sở 2 (Test)",
                IsActive = true,
            };
            Db.Hospitals.Add(crossHospital);
            await Db.SaveChangesAsync();
            crossHospitalId = crossHospital.Id;

            var crossRoom = new HospitalRoom
            {
                HospitalId = crossHospital.Id,
                DepartmentId = null,
                RoomCode = $"H2-{Guid.NewGuid():N}"[..10].ToUpperInvariant(),
                Name = "Phòng khám Cơ sở 2",
                RoomType = "EXAMINATION",
                Capacity = 5,
                Status = "ACTIVE",
            };
            Db.HospitalRooms.Add(crossRoom);
            await Db.SaveChangesAsync();
            crossHospitalRoomId = crossRoom.Id;

            var crossVisit = new Visit
            {
                VisitCode = $"VIS-H2-{Guid.NewGuid():N}"[..20].ToUpperInvariant(),
                PatientId = 1,
                ExaminationRoomId = crossRoom.Id,
                Status = "WAITING",
                CheckedInAt = new DateTime(2026, 10, 26, 8, 20, 0, DateTimeKind.Unspecified),
            };
            Db.Visits.Add(crossVisit);
            await Db.SaveChangesAsync();
            crossHospitalVisitId = crossVisit.Id;

            var crossQueue = new QueueEntry
            {
                VisitId = crossVisit.Id,
                QueueDate = new DateOnly(2026, 10, 26),
                QueueNumber = 1,
                QueueType = "EXAMINATION",
                Status = "WAITING",
                PresenceStatus = "PRESENT",
                StateVersion = 1,
            };
            Db.QueueEntries.Add(crossQueue);
            await Db.SaveChangesAsync();
            crossHospitalQueueId = crossQueue.Id;

            using var crossHospitalReq = AuthedSend(
                HttpMethod.Patch,
                $"/api/v1/reception/queue-entries/{crossQueue.Id}/status",
                new UpdateQueueStatusRequest(TargetStatus: "CALLED"));
            var crossHospitalRes = await Client.SendAsync(crossHospitalReq);
            Assert.Equal(HttpStatusCode.Forbidden, crossHospitalRes.StatusCode);

            // 1. GET initial queue status detail
            using var getInitialReq = AuthedGet($"/api/v1/reception/examination-queue/entries/{seededQueue.Id}/status");
            var getInitialRes = await Client.SendAsync(getInitialReq);
            Assert.Equal(HttpStatusCode.OK, getInitialRes.StatusCode);

            var initialDto = await getInitialRes.Content.ReadFromJsonAsync<QueueEntryStatusDetailDto>();
            Assert.NotNull(initialDto);
            Assert.Equal(seededQueue.Id, initialDto.QueueEntryId);
            Assert.Equal("WAITING", initialDto.CurrentStatus);
            Assert.Equal(1, initialDto.StateVersion);
            Assert.Equal(45, initialDto.QueueNumber);
            Assert.Contains("CALLED", initialDto.AllowedTargetStatuses);
            Assert.Contains("SKIPPED", initialDto.AllowedTargetStatuses);

            // 2. WAITING -> SKIPPED -> WAITING recovery
            using var skipReq = AuthedSend(
                HttpMethod.Patch,
                $"/api/v1/reception/examination-queue/entries/{seededQueue.Id}/status",
                new UpdateQueueStatusRequest(
                    TargetStatus: "SKIPPED",
                    Reason: "Gọi 3 lần bệnh nhân chưa vào phòng",
                    PresenceStatus: "LEFT",
                    ExpectedStateVersion: 1));
            var skipRes = await Client.SendAsync(skipReq);
            Assert.Equal(HttpStatusCode.OK, skipRes.StatusCode);

            var skipDto = await skipRes.Content.ReadFromJsonAsync<UpdateQueueStatusResponseDto>();
            Assert.NotNull(skipDto);
            Assert.Equal("WAITING", skipDto.PreviousStatus);
            Assert.Equal("SKIPPED", skipDto.CurrentStatus);
            Assert.Equal("LEFT", skipDto.PresenceStatus);
            Assert.Equal(2, skipDto.StateVersion);
            Assert.Equal("Gọi 3 lần bệnh nhân chưa vào phòng", skipDto.BlockedReason);

            // Stale expectedStateVersion = 1 returns 409 Conflict
            using var staleReq = AuthedSend(
                HttpMethod.Patch,
                $"/api/v1/reception/queue-entries/{seededQueue.Id}/status",
                new UpdateQueueStatusRequest(TargetStatus: "WAITING", ExpectedStateVersion: 1));
            var staleRes = await Client.SendAsync(staleReq);
            Assert.Equal(HttpStatusCode.Conflict, staleRes.StatusCode);

            // Recover SKIPPED -> WAITING
            using var requeueReq = AuthedSend(
                HttpMethod.Put,
                $"/api/v1/reception/queue-entries/{seededQueue.Id}/status",
                new UpdateQueueStatusRequest(
                    TargetStatus: "WAITING",
                    Reason: "Bệnh nhân đã quay lại quầy tiếp nhận",
                    PresenceStatus: "PRESENT",
                    ExpectedStateVersion: 2));
            var requeueRes = await Client.SendAsync(requeueReq);
            Assert.Equal(HttpStatusCode.OK, requeueRes.StatusCode);

            var requeueDto = await requeueRes.Content.ReadFromJsonAsync<UpdateQueueStatusResponseDto>();
            Assert.NotNull(requeueDto);
            Assert.Equal("WAITING", requeueDto.CurrentStatus);
            Assert.Equal(3, requeueDto.StateVersion);
            Assert.Null(requeueDto.BlockedReason);

            // 3. WAITING -> CALLED -> IN_PROGRESS -> COMPLETED
            using var callReq = AuthedSend(
                HttpMethod.Patch,
                $"/api/v1/reception/visits/{visitCode}/queue-status",
                new UpdateQueueStatusRequest(TargetStatus: "CALLED", ExpectedStateVersion: 3));
            var callRes = await Client.SendAsync(callReq);
            Assert.Equal(HttpStatusCode.OK, callRes.StatusCode);

            var callDto = await callRes.Content.ReadFromJsonAsync<UpdateQueueStatusResponseDto>();
            Assert.NotNull(callDto);
            Assert.Equal("CALLED", callDto.CurrentStatus);
            Assert.Equal("CALLED", callDto.VisitStatus);
            Assert.Equal(4, callDto.StateVersion);
            Assert.NotNull(callDto.CalledAt);

            using var inProgressReq = AuthedSend(
                HttpMethod.Post,
                $"/api/v1/visits/{visitCode}/queue-status",
                new UpdateQueueStatusRequest(TargetStatus: "IN_PROGRESS", ExpectedStateVersion: 4),
                userId: 3,
                email: "doctor01@medflow.test",
                role: "DOCTOR");
            var inProgressRes = await Client.SendAsync(inProgressReq);
            Assert.Equal(HttpStatusCode.OK, inProgressRes.StatusCode);

            var inProgressDto = await inProgressRes.Content.ReadFromJsonAsync<UpdateQueueStatusResponseDto>();
            Assert.NotNull(inProgressDto);
            Assert.Equal("IN_PROGRESS", inProgressDto.CurrentStatus);
            Assert.Equal("IN_PROGRESS", inProgressDto.VisitStatus);
            Assert.Equal(5, inProgressDto.StateVersion);
            Assert.NotNull(inProgressDto.StartedAt);

            using var completeReq = AuthedSend(
                HttpMethod.Patch,
                $"/api/v1/queue-entries/{seededQueue.Id}/status",
                new UpdateQueueStatusRequest(
                    TargetStatus: "COMPLETED",
                    Notes: "Đã khám xong và kê đơn",
                    ExpectedStateVersion: 5),
                userId: 3,
                email: "doctor01@medflow.test",
                role: "DOCTOR");
            var completeRes = await Client.SendAsync(completeReq);
            Assert.Equal(HttpStatusCode.OK, completeRes.StatusCode);

            var completeDto = await completeRes.Content.ReadFromJsonAsync<UpdateQueueStatusResponseDto>();
            Assert.NotNull(completeDto);
            Assert.Equal("COMPLETED", completeDto.CurrentStatus);
            Assert.Equal("COMPLETED", completeDto.VisitStatus);
            Assert.Equal(6, completeDto.StateVersion);
            Assert.Equal(45, completeDto.QueueNumber);
            Assert.NotNull(completeDto.CompletedAt);
            Assert.Empty(completeDto.AllowedTargetStatuses);
            Assert.Equal(5, completeDto.StatusHistory.Count);

            var dbQueue = await Db.QueueEntries.AsNoTracking().FirstAsync(q => q.Id == seededQueue.Id);
            Assert.Equal("COMPLETED", dbQueue.Status);
            Assert.Equal(6, dbQueue.StateVersion);
            Assert.NotNull(dbQueue.CompletedAt);

            var dbVisit = await Db.Visits.AsNoTracking().FirstAsync(v => v.Id == seededVisit.Id);
            Assert.Equal("COMPLETED", dbVisit.Status);
            Assert.NotNull(dbVisit.CompletedAt);

            var queueIdStr = seededQueue.Id.ToString();
            var auditCount = await Db.AuditLogs
                .AsNoTracking()
                .CountAsync(a => a.Action == QueueStatusService.QueueStatusUpdateAction &&
                                 a.EntityId == queueIdStr);
            Assert.Equal(5, auditCount);
        }
        finally
        {
            if (createdQueueId.HasValue)
            {
                var entityIdStr = createdQueueId.Value.ToString();
                await Db.AuditLogs
                    .Where(a => a.Action == QueueStatusService.QueueStatusUpdateAction &&
                                a.EntityId == entityIdStr)
                    .ExecuteDeleteAsync();

                await Db.QueueEntries
                    .Where(q => q.Id == createdQueueId.Value)
                    .ExecuteDeleteAsync();
            }

            if (createdVisitId.HasValue)
            {
                await Db.Visits
                    .Where(v => v.Id == createdVisitId.Value)
                    .ExecuteDeleteAsync();
            }

            if (crossHospitalQueueId.HasValue)
            {
                await Db.QueueEntries
                    .Where(q => q.Id == crossHospitalQueueId.Value)
                    .ExecuteDeleteAsync();
            }

            if (crossHospitalVisitId.HasValue)
            {
                await Db.Visits
                    .Where(v => v.Id == crossHospitalVisitId.Value)
                    .ExecuteDeleteAsync();
            }

            if (crossHospitalRoomId.HasValue)
            {
                await Db.HospitalRooms
                    .Where(r => r.Id == crossHospitalRoomId.Value)
                    .ExecuteDeleteAsync();
            }

            if (crossHospitalId.HasValue)
            {
                await Db.Hospitals
                    .Where(h => h.Id == crossHospitalId.Value)
                    .ExecuteDeleteAsync();
            }
        }
    }
}
