using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MedFlow.Application.DTOs.Visits;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Visits;
using MedFlow.Domain.Entities;
using MedFlow.IntegrationTests.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MedFlow.IntegrationTests.Visits;

public sealed class LateArrivalMissedTurnEndpointTests : AuthTestBase
{
    public LateArrivalMissedTurnEndpointTests(TestWebAppFactory factory)
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

    private HttpRequestMessage AuthedPost<TBody>(
        string url,
        TBody body,
        long userId = 5,
        string email = "reception01@medflow.test",
        string role = "RECEPTION")
    {
        var token = GenerateToken(userId, email, role);
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    [Fact]
    public async Task Endpoints_WithoutToken_Return401()
    {
        var missedBody = new MarkMissedTurnRequest(
            ServicePointId: "sp-1",
            Reason: "Gọi 3 lần không có mặt",
            Notes: null,
            ExpectedStateVersion: 1);

        var restoreBody = new RestoreQueueTurnRequest(
            ServicePointId: "sp-1",
            ConfirmPresent: true,
            Reason: "Bệnh nhân đã quay lại quầy",
            Notes: null,
            ExpectedStateVersion: 2);

        var resMissed = await Client.PostAsJsonAsync(
            "/api/v1/reception/queue-entries/q-001/missed-turn",
            missedBody);
        Assert.Equal(HttpStatusCode.Unauthorized, resMissed.StatusCode);

        var resRestore = await Client.PostAsJsonAsync(
            "/api/v1/reception/queue-entries/q-001/restore-turn",
            restoreBody);
        Assert.Equal(HttpStatusCode.Unauthorized, resRestore.StatusCode);
    }

    [Fact]
    public async Task Endpoints_DoctorRole_Return403()
    {
        var missedBody = new MarkMissedTurnRequest(
            ServicePointId: "sp-1",
            Reason: "Gọi 3 lần không có mặt",
            Notes: null,
            ExpectedStateVersion: 1);

        var restoreBody = new RestoreQueueTurnRequest(
            ServicePointId: "sp-1",
            ConfirmPresent: true,
            Reason: "Bệnh nhân đã quay lại quầy",
            Notes: null,
            ExpectedStateVersion: 2);

        using var reqMissed = AuthedPost(
            "/api/v1/reception/queue-entries/q-001/missed-turn",
            missedBody,
            userId: 3,
            email: "doctor01@medflow.test",
            role: "DOCTOR");
        var resMissed = await Client.SendAsync(reqMissed);
        Assert.Equal(HttpStatusCode.Forbidden, resMissed.StatusCode);

        using var reqRestore = AuthedPost(
            "/api/v1/reception/queue-entries/q-001/restore-turn",
            restoreBody,
            userId: 3,
            email: "doctor01@medflow.test",
            role: "DOCTOR");
        var resRestore = await Client.SendAsync(reqRestore);
        Assert.Equal(HttpStatusCode.Forbidden, resRestore.StatusCode);
    }

    [Fact]
    public async Task MissedTurnAndRestore_LiveDatabase_UpdatesStatusPresenceServingOrderAndWritesAuditLogs()
    {
        if (!await Db.Database.CanConnectAsync())
        {
            return;
        }

        var nowLocal = DateTime.UtcNow.AddHours(7);
        var todayLocal = DateOnly.FromDateTime(nowLocal);
        var todayStr = todayLocal.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var baseTime = new DateTime(
            nowLocal.Year,
            nowLocal.Month,
            nowLocal.Day,
            nowLocal.Hour,
            nowLocal.Minute,
            nowLocal.Second,
            DateTimeKind.Unspecified);
        var checkedInAt = baseTime.AddMinutes(-20);
        var calledAt = baseTime.AddMinutes(-2);

        var roomCode = $"LMT-{Guid.NewGuid():N}"[..10].ToUpperInvariant();
        var visitCode = $"VIS-LMT-{Guid.NewGuid():N}"[..20].ToUpperInvariant();

        long? createdRoomId = null;
        long? createdVisitId = null;
        long? createdQueueId = null;

        try
        {
            var newRoom = new HospitalRoom
            {
                HospitalId = 1,
                DepartmentId = 2,
                RoomCode = roomCode,
                Name = $"Phòng khám Test {roomCode}",
                RoomType = "EXAMINATION",
                Location = "Tầng 1",
                Capacity = 2,
                Status = "ACTIVE",
            };
            Db.HospitalRooms.Add(newRoom);
            await Db.SaveChangesAsync();
            createdRoomId = newRoom.Id;

            var seededVisit = new Visit
            {
                VisitCode = visitCode,
                PatientId = 1,
                AppointmentId = null,
                AssignedDoctorId = 1,
                ExaminationRoomId = newRoom.Id,
                CheckedInByStaffId = 3,
                ReasonForVisit = "Khám kiểm tra Qua lượt / Khôi phục lượt",
                Status = "CALLED",
                CheckedInAt = checkedInAt,
            };
            Db.Visits.Add(seededVisit);
            await Db.SaveChangesAsync();
            createdVisitId = seededVisit.Id;

            var seededQueue = new QueueEntry
            {
                VisitId = seededVisit.Id,
                AppointmentId = null,
                ServicePointId = null,
                CurrentServicePointId = null,
                QueueDate = todayLocal,
                QueueNumber = 1,
                QueueType = "EXAMINATION",
                Status = "CALLED",
                PresenceStatus = "PRESENT",
                ArrivalClass = "WALK_IN",
                ManualPriority = "NORMAL",
                StateVersion = 1,
                ArrivedAt = checkedInAt,
                CalledAt = calledAt,
            };
            Db.QueueEntries.Add(seededQueue);
            await Db.SaveChangesAsync();
            createdQueueId = seededQueue.Id;

            using var getQueueReq = AuthedGet(
                $"/api/v1/reception/examination-queue?queueDate={todayStr}&keyword={visitCode}");
            var getQueueRes = await Client.SendAsync(getQueueReq);
            Assert.Equal(HttpStatusCode.OK, getQueueRes.StatusCode);

            var queueDto = await getQueueRes.Content.ReadFromJsonAsync<ExaminationQueueResponseDto>();
            Assert.NotNull(queueDto);

            var spBucket = Assert.Single(queueDto.ServicePoints);
            var discoveredEntry = Assert.Single(spBucket.Entries);
            Assert.Equal(visitCode, discoveredEntry.VisitCode);
            Assert.Equal("CALLED", discoveredEntry.Status);

            var missedReqBody = new MarkMissedTurnRequest(
                ServicePointId: spBucket.ServicePointId,
                Reason: "Gọi 3 lần bệnh nhân vắng mặt tại phòng khám",
                Notes: "Test tích hợp M2-046 missed-turn",
                ExpectedStateVersion: 1);

            using var missedPostReq = AuthedPost(
                $"/api/v1/reception/queue-entries/{discoveredEntry.Id}/missed-turn",
                missedReqBody);
            var missedPostRes = await Client.SendAsync(missedPostReq);
            Assert.Equal(HttpStatusCode.OK, missedPostRes.StatusCode);

            var missedDto = await missedPostRes.Content.ReadFromJsonAsync<QueueTurnActionResponseDto>();
            Assert.NotNull(missedDto);
            Assert.Equal("MISSED_TURN", missedDto.Action);
            Assert.Equal("SKIPPED", missedDto.CurrentStatus);
            Assert.Equal("LEFT", missedDto.PresenceStatus);
            Assert.Equal(1, missedDto.QueueNumber);
            Assert.Equal(2, missedDto.StateVersion);

            var dbAfterMissed = await Db.QueueEntries
                .AsNoTracking()
                .SingleAsync(q => q.Id == seededQueue.Id);
            Assert.Equal("SKIPPED", dbAfterMissed.Status);
            Assert.Equal("LEFT", dbAfterMissed.PresenceStatus);
            Assert.Equal(2, dbAfterMissed.StateVersion);

            var restoreReqBody = new RestoreQueueTurnRequest(
                ServicePointId: spBucket.ServicePointId,
                ConfirmPresent: true,
                Reason: "Bệnh nhân đã quay lại và xác nhận có mặt tại quầy",
                Notes: "Test tích hợp M2-046 restore-turn",
                ExpectedStateVersion: 2);

            using var restorePostReq = AuthedPost(
                $"/api/v1/reception/queue-entries/{discoveredEntry.Id}/restore-turn",
                restoreReqBody);
            var restorePostRes = await Client.SendAsync(restorePostReq);
            Assert.Equal(HttpStatusCode.OK, restorePostRes.StatusCode);

            var restoreDto = await restorePostRes.Content.ReadFromJsonAsync<QueueTurnActionResponseDto>();
            Assert.NotNull(restoreDto);
            Assert.Equal("RESTORE_TURN", restoreDto.Action);
            Assert.Equal("READY", restoreDto.CurrentStatus);
            Assert.Equal("PRESENT", restoreDto.PresenceStatus);
            Assert.Equal("WALK_IN", restoreDto.ArrivalClass);
            Assert.Equal(1, restoreDto.QueueNumber);
            Assert.Equal(3, restoreDto.StateVersion);

            var dbAfterRestore = await Db.QueueEntries
                .AsNoTracking()
                .SingleAsync(q => q.Id == seededQueue.Id);
            Assert.Equal("READY", dbAfterRestore.Status);
            Assert.Equal("PRESENT", dbAfterRestore.PresenceStatus);
            Assert.Equal("WALK_IN", dbAfterRestore.ArrivalClass);
            Assert.Equal(1, dbAfterRestore.QueueNumber);
            Assert.Equal(3, dbAfterRestore.StateVersion);

            var entityIdStr = seededQueue.Id.ToString(CultureInfo.InvariantCulture);
            var missedAuditCount = await Db.AuditLogs
                .AsNoTracking()
                .CountAsync(a =>
                    a.EntityType == QueueStatusService.QueueEntryEntityType &&
                    a.EntityId == entityIdStr &&
                    a.Action == ExaminationQueueService.MissedTurnAction);
            Assert.Equal(1, missedAuditCount);

            var restoreAuditCount = await Db.AuditLogs
                .AsNoTracking()
                .CountAsync(a =>
                    a.EntityType == QueueStatusService.QueueEntryEntityType &&
                    a.EntityId == entityIdStr &&
                    a.Action == ExaminationQueueService.RestoreTurnAction);
            Assert.Equal(1, restoreAuditCount);
        }
        finally
        {
            if (createdQueueId.HasValue)
            {
                var entityIdStr = createdQueueId.Value.ToString(CultureInfo.InvariantCulture);
                await Db.AuditLogs
                    .Where(a => a.EntityType == QueueStatusService.QueueEntryEntityType &&
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

            if (createdRoomId.HasValue)
            {
                await Db.HospitalRooms
                    .Where(r => r.Id == createdRoomId.Value)
                    .ExecuteDeleteAsync();
            }
        }
    }
}
