using System.Globalization;
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

public sealed class EarlyArrivalBackfillEndpointTests : AuthTestBase
{
    public EarlyArrivalBackfillEndpointTests(TestWebAppFactory factory)
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

    private HttpRequestMessage AuthedPost(
        string url,
        EarlyArrivalBackfillRequest body,
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
        var body = new EarlyArrivalBackfillRequest(
            ServicePointId: "sp-1",
            ActionMode: "SAFE_BACKFILL",
            ArrivalClass: "EARLY",
            Reason: "Bệnh nhân đến sớm, phòng trống",
            Notes: null,
            AcknowledgeAppointmentProtection: true);

        var res1 = await Client.PostAsJsonAsync(
            "/api/v1/reception/queue-entries/q-001/early-arrival-backfill",
            body);
        Assert.Equal(HttpStatusCode.Unauthorized, res1.StatusCode);

        var res2 = await Client.PostAsJsonAsync(
            "/api/v1/reception/examination-queue/entries/q-001/early-arrival-backfill",
            body);
        Assert.Equal(HttpStatusCode.Unauthorized, res2.StatusCode);
    }

    [Fact]
    public async Task Endpoints_DoctorRole_Return403()
    {
        var body = new EarlyArrivalBackfillRequest(
            ServicePointId: "sp-1",
            ActionMode: "SAFE_BACKFILL",
            ArrivalClass: "EARLY",
            Reason: "Bệnh nhân đến sớm, phòng trống",
            Notes: null,
            AcknowledgeAppointmentProtection: true);

        using var req = AuthedPost(
            "/api/v1/reception/queue-entries/q-001/early-arrival-backfill",
            body,
            userId: 3,
            email: "doctor01@medflow.test",
            role: "DOCTOR");

        var res = await Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task ApplyEarlyArrivalBackfill_LiveDatabase_PersistsReadyStatusBackfilledFieldsAndAuditLog()
    {
        if (!await Db.Database.CanConnectAsync())
        {
            return;
        }

        var nowLocal = DateTime.UtcNow.AddHours(7);
        var todayLocal = DateOnly.FromDateTime(nowLocal);
        var todayStr = todayLocal.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var checkedInAt = new DateTime(
            nowLocal.Year,
            nowLocal.Month,
            nowLocal.Day,
            nowLocal.Hour,
            nowLocal.Minute,
            nowLocal.Second,
            DateTimeKind.Unspecified).AddMinutes(-10);

        var roomCode = $"EAB-{Guid.NewGuid():N}"[..10].ToUpperInvariant();
        var visitCode = $"VIS-EAB-{Guid.NewGuid():N}"[..20].ToUpperInvariant();

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
                ReasonForVisit = "Khám kiểm tra Safe Backfill",
                Status = "WAITING",
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
                Status = "WAITING",
                PresenceStatus = "PRESENT",
                ArrivalClass = "WALK_IN",
                ManualPriority = "NORMAL",
                StateVersion = 1,
                ArrivedAt = checkedInAt,
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
            Assert.True(discoveredEntry.CanSafeBackfill);

            var backfillReqBody = new EarlyArrivalBackfillRequest(
                ServicePointId: spBucket.ServicePointId,
                ActionMode: "SAFE_BACKFILL",
                ArrivalClass: "WALK_IN",
                Reason: "Đến sớm, phòng khám đang trống và không vướng lịch hẹn",
                Notes: "Test tích hợp M2-044",
                AcknowledgeAppointmentProtection: true);

            using var postReq = AuthedPost(
                $"/api/v1/reception/examination-queue/entries/{discoveredEntry.Id}/early-arrival-backfill",
                backfillReqBody);
            var postRes = await Client.SendAsync(postReq);
            Assert.Equal(HttpStatusCode.OK, postRes.StatusCode);

            var backfillDto = await postRes.Content.ReadFromJsonAsync<EarlyArrivalBackfillResponseDto>();
            Assert.NotNull(backfillDto);
            Assert.Equal("READY", backfillDto.CurrentStatus);
            Assert.True(backfillDto.IsBackfilled);
            Assert.Equal(1, backfillDto.QueueNumber);
            Assert.Equal(2, backfillDto.StateVersion);
            Assert.True(backfillDto.UpdatedEntry.IsBackfilled);

            var dbQueue = await Db.QueueEntries
                .AsNoTracking()
                .SingleAsync(q => q.Id == seededQueue.Id);
            Assert.Equal("READY", dbQueue.Status);
            Assert.Equal(2, dbQueue.StateVersion);
            Assert.NotNull(dbQueue.BackfilledAt);
            Assert.Equal("Đến sớm, phòng khám đang trống và không vướng lịch hẹn", dbQueue.BackfillReason);

            var entityIdStr = seededQueue.Id.ToString(CultureInfo.InvariantCulture);
            var auditCount = await Db.AuditLogs
                .AsNoTracking()
                .CountAsync(a =>
                    a.EntityType == QueueStatusService.QueueEntryEntityType &&
                    a.EntityId == entityIdStr &&
                    a.Action == ExaminationQueueService.EarlyArrivalBackfillAction);
            Assert.Equal(1, auditCount);

            // Repeat via /api/v1/reception/queue-entries/{id}/early-arrival-backfill -> 409 ProblemDetails
            using var repeatReq = AuthedPost(
                $"/api/v1/reception/queue-entries/{discoveredEntry.Id}/early-arrival-backfill",
                backfillReqBody);
            var repeatRes = await Client.SendAsync(repeatReq);
            Assert.Equal(HttpStatusCode.Conflict, repeatRes.StatusCode);

            var problem = await repeatRes.Content.ReadFromJsonAsync<ProblemDetails>();
            Assert.NotNull(problem);
            Assert.Equal(409, problem.Status);
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
