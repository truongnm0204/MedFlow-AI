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

public sealed class ReturnToDoctorQueueEndpointTests : AuthTestBase
{
    public ReturnToDoctorQueueEndpointTests(TestWebAppFactory factory)
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
    public async Task ReturnToDoctor_WithoutToken_Returns401()
    {
        var res = await Client.PostAsJsonAsync(
            "/api/v1/reception/queue-entries/q-001/return-to-doctor",
            new CreateReturnToDoctorQueueEntryRequest(
                ServicePointId: "sp-1",
                ConfirmPresent: true,
                Reason: "Quay lại khám đọc kết quả cận lâm sàng"));

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task ReturnToDoctor_PatientAndDiagnosticStaffRoles_Return403()
    {
        using var patientReq = AuthedPost(
            "/api/v1/reception/queue-entries/q-001/return-to-doctor",
            new CreateReturnToDoctorQueueEntryRequest(
                ServicePointId: "sp-1",
                ConfirmPresent: true,
                Reason: "Quay lại khám đọc kết quả cận lâm sàng"),
            userId: 1,
            email: "patient01@medflow.test",
            role: "PATIENT");

        var patientRes = await Client.SendAsync(patientReq);
        Assert.Equal(HttpStatusCode.Forbidden, patientRes.StatusCode);

        using var diagReq = AuthedPost(
            "/api/v1/reception/queue-entries/q-001/return-to-doctor",
            new CreateReturnToDoctorQueueEntryRequest(
                ServicePointId: "sp-1",
                ConfirmPresent: true,
                Reason: "Quay lại khám đọc kết quả cận lâm sàng"),
            userId: 6,
            email: "diag01@medflow.test",
            role: "DIAGNOSTIC_STAFF");

        var diagRes = await Client.SendAsync(diagReq);
        Assert.Equal(HttpStatusCode.Forbidden, diagRes.StatusCode);
    }

    [Fact]
    public async Task ReturnToDoctor_LiveDatabase_CreatesWaitingWhileDiagnosticOpenThenActivatesReadyAndCallsNextWithFairness()
    {
        if (!await Db.Database.CanConnectAsync())
        {
            return;
        }

        var nowLocal = DateTime.UtcNow.AddHours(7);
        var todayLocal = DateOnly.FromDateTime(nowLocal);
        var baseTime = new DateTime(
            nowLocal.Year,
            nowLocal.Month,
            nowLocal.Day,
            nowLocal.Hour,
            nowLocal.Minute,
            nowLocal.Second,
            DateTimeKind.Unspecified);

        var roomCode = $"RTD-{Guid.NewGuid():N}"[..10].ToUpperInvariant();
        var visitCodeReturn = $"VIS-RTD1-{Guid.NewGuid():N}"[..20].ToUpperInvariant();
        var visitCodeNormal = $"VIS-RTD2-{Guid.NewGuid():N}"[..20].ToUpperInvariant();

        long? createdRoomId = null;
        long? createdServicePointId = null;
        var createdVisitIds = new List<long>();

        try
        {
            var staleSpIds = await Db.QueueServicePoints
                .Where(sp => sp.Name.StartsWith("Điểm phục vụ ReturnToDoctor RTD-"))
                .Select(sp => sp.Id)
                .ToListAsync();
            if (staleSpIds.Count > 0)
            {
                await Db.QueueFairnessStates.Where(s => staleSpIds.Contains(s.ServicePointId)).ExecuteDeleteAsync();
                await Db.QueueEntries.Where(q => q.ServicePointId.HasValue && staleSpIds.Contains(q.ServicePointId.Value)).ExecuteDeleteAsync();
                await Db.QueueServicePoints.Where(sp => staleSpIds.Contains(sp.Id)).ExecuteDeleteAsync();
            }

            var staleRooms = await Db.HospitalRooms
                .Where(r => r.RoomCode.StartsWith("RTD-"))
                .Select(r => r.Id)
                .ToListAsync();
            if (staleRooms.Count > 0)
            {
                await Db.Visits.Where(v => v.ExaminationRoomId.HasValue && staleRooms.Contains(v.ExaminationRoomId.Value)).ExecuteDeleteAsync();
                await Db.HospitalRooms.Where(r => staleRooms.Contains(r.Id)).ExecuteDeleteAsync();
            }

            var newRoom = new HospitalRoom
            {
                HospitalId = 1,
                DepartmentId = 2,
                RoomCode = roomCode,
                Name = $"Phòng khám ReturnToDoctor {roomCode}",
                RoomType = "EXAMINATION",
                Location = "Tầng 1",
                Capacity = 2,
                Status = "ACTIVE",
            };
            Db.HospitalRooms.Add(newRoom);
            await Db.SaveChangesAsync();
            createdRoomId = newRoom.Id;

            var newSp = new QueueServicePoint
            {
                HospitalId = 1,
                RoomId = newRoom.Id,
                DoctorId = 1,
                PointType = "DOCTOR",
                Name = $"Điểm phục vụ ReturnToDoctor {roomCode}",
                IsActive = true,
            };
            Db.QueueServicePoints.Add(newSp);
            await Db.SaveChangesAsync();
            createdServicePointId = newSp.Id;

            var visitReturn = new Visit
            {
                VisitCode = visitCodeReturn,
                PatientId = 1,
                AssignedDoctorId = 1,
                ExaminationRoomId = newRoom.Id,
                CheckedInByStaffId = 3,
                ReasonForVisit = "Khám tổng quát có chỉ định cận lâm sàng",
                Status = "IN_PROGRESS",
                CheckedInAt = baseTime.AddMinutes(-40),
            };
            var visitNormal = new Visit
            {
                VisitCode = visitCodeNormal,
                PatientId = 4,
                AssignedDoctorId = 1,
                ExaminationRoomId = newRoom.Id,
                CheckedInByStaffId = 3,
                ReasonForVisit = "Khám thường đang chờ",
                Status = "WAITING",
                CheckedInAt = baseTime.AddMinutes(-25),
            };
            Db.Visits.AddRange(visitReturn, visitNormal);
            await Db.SaveChangesAsync();
            createdVisitIds.AddRange([visitReturn.Id, visitNormal.Id]);

            var initialExamEntry = new QueueEntry
            {
                VisitId = visitReturn.Id,
                ServicePointId = newSp.Id,
                CurrentServicePointId = newSp.Id,
                QueueDate = todayLocal,
                QueueNumber = 1,
                QueueType = "EXAMINATION",
                Status = "IN_PROGRESS",
                PresenceStatus = "PRESENT",
                ArrivalClass = "WALK_IN",
                ManualPriority = "NORMAL",
                StateVersion = 2,
                ArrivedAt = baseTime.AddMinutes(-40),
                ReadyAt = baseTime.AddMinutes(-40),
                CalledAt = baseTime.AddMinutes(-35),
                StartedAt = baseTime.AddMinutes(-34),
            };
            var waitingNormalEntry = new QueueEntry
            {
                VisitId = visitNormal.Id,
                ServicePointId = newSp.Id,
                CurrentServicePointId = newSp.Id,
                QueueDate = todayLocal,
                QueueNumber = 2,
                QueueType = "EXAMINATION",
                Status = "READY",
                PresenceStatus = "PRESENT",
                ArrivalClass = "WALK_IN",
                ManualPriority = "NORMAL",
                StateVersion = 1,
                ArrivedAt = baseTime.AddMinutes(-25),
                ReadyAt = baseTime.AddMinutes(-25),
            };
            var openDiagnosticEntry = new QueueEntry
            {
                VisitId = visitReturn.Id,
                ServicePointId = null,
                CurrentServicePointId = null,
                QueueDate = todayLocal,
                QueueNumber = 1,
                QueueType = "DIAGNOSTIC",
                Status = "IN_PROGRESS",
                PresenceStatus = "PRESENT",
                StateVersion = 1,
                ArrivedAt = baseTime.AddMinutes(-20),
            };
            Db.QueueEntries.AddRange(initialExamEntry, waitingNormalEntry, openDiagnosticEntry);
            await Db.SaveChangesAsync();

            // 1. Create RETURN_CONSULTATION entry while open DIAGNOSTIC entry exists -> WAITING + NOT_ELIGIBLE (AWAITING_RESULTS)
            using var createReq = AuthedPost(
                $"/api/v1/reception/queue-entries/q-{initialExamEntry.Id:D3}/return-to-doctor",
                new CreateReturnToDoctorQueueEntryRequest(
                    ServicePointId: $"sp-{newSp.Id}",
                    ConfirmPresent: true,
                    Reason: "Bệnh nhân quay lại phòng khám chờ kết quả xét nghiệm",
                    Notes: "Đã lấy mẫu xét nghiệm",
                    ExpectedStateVersion: 2));

            var createRes = await Client.SendAsync(createReq);
            Assert.Equal(HttpStatusCode.OK, createRes.StatusCode);

            var createdBody = await createRes.Content.ReadFromJsonAsync<CreateReturnToDoctorQueueEntryResponseDto>();
            Assert.NotNull(createdBody);
            Assert.Equal("CREATED", createdBody!.ActionOutcome);
            Assert.Equal("RETURN_CONSULTATION", createdBody.QueueType);
            Assert.Equal("WAITING", createdBody.CurrentStatus);
            Assert.Equal(3, createdBody.QueueNumber);
            Assert.False(createdBody.IsEligible);
            Assert.Equal("NOT_ELIGIBLE", createdBody.QueueGroup);
            Assert.Equal("AWAITING_RESULTS", createdBody.IneligibleReason);
            Assert.Equal(1, createdBody.OpenDiagnosticCount);
            Assert.Equal(1, createdBody.StateVersion);

            // 2. Attempting to activate while DIAGNOSTIC entry is still open -> 409 Conflict
            using var prematureActivateReq = AuthedPost(
                $"/api/v1/reception/queue-entries/{createdBody.QueueEntryId}/return-to-doctor",
                new CreateReturnToDoctorQueueEntryRequest(
                    ServicePointId: $"sp-{newSp.Id}",
                    ConfirmPresent: true,
                    Reason: "Thử kích hoạt khi cận lâm sàng chưa hoàn tất",
                    ExpectedStateVersion: 1));

            var prematureRes = await Client.SendAsync(prematureActivateReq);
            Assert.Equal(HttpStatusCode.Conflict, prematureRes.StatusCode);

            // 3. Complete the open DIAGNOSTIC entry
            await Db.QueueEntries
                .Where(q => q.Id == openDiagnosticEntry.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(q => q.Status, "COMPLETED")
                    .SetProperty(q => q.CompletedAt, baseTime.AddMinutes(-2)));

            // 4. Activate the existing RETURN_CONSULTATION entry -> ACTIVATED + READY + ELIGIBLE (ServingOrder = 1 ahead of NORMAL)
            using var activateReq = AuthedPost(
                $"/api/v1/reception/queue-entries/{createdBody.QueueEntryId}/return-to-doctor",
                new CreateReturnToDoctorQueueEntryRequest(
                    ServicePointId: $"sp-{newSp.Id}",
                    ConfirmPresent: true,
                    Reason: "Đã có đủ kết quả xét nghiệm bắt buộc, kích hoạt khám lại",
                    Notes: "Đủ điều kiện gọi vào phòng khám",
                    ExpectedStateVersion: 1));

            var activateRes = await Client.SendAsync(activateReq);
            Assert.Equal(HttpStatusCode.OK, activateRes.StatusCode);

            var activatedBody = await activateRes.Content.ReadFromJsonAsync<CreateReturnToDoctorQueueEntryResponseDto>();
            Assert.NotNull(activatedBody);
            Assert.Equal("ACTIVATED", activatedBody!.ActionOutcome);
            Assert.Equal(createdBody.QueueEntryId, activatedBody.QueueEntryId);
            Assert.Equal(3, activatedBody.QueueNumber);
            Assert.Equal("READY", activatedBody.CurrentStatus);
            Assert.True(activatedBody.AllDiagnosticResultsReady);
            Assert.Equal(0, activatedBody.OpenDiagnosticCount);
            Assert.True(activatedBody.IsEligible);
            Assert.Equal("ELIGIBLE", activatedBody.QueueGroup);
            Assert.Null(activatedBody.IneligibleReason);
            Assert.Equal(1, activatedBody.ServingOrder);
            Assert.Equal(2, activatedBody.StateVersion);

            // 5. Call-next selects the activated RETURN_CONSULTATION entry ahead of the waiting NORMAL entry
            using var callNextReq = AuthedPost(
                $"/api/v1/queue/service-points/sp-{newSp.Id}/call-next",
                new CallNextPatientRequest(Notes: "Gọi bệnh nhân khám lại đọc kết quả CLS"));

            var callNextRes = await Client.SendAsync(callNextReq);
            Assert.Equal(HttpStatusCode.OK, callNextRes.StatusCode);

            var callNextBody = await callNextRes.Content.ReadFromJsonAsync<CallNextPatientResponseDto>();
            Assert.NotNull(callNextBody);
            Assert.Equal("CALLED", callNextBody!.Outcome);
            Assert.Equal("RETURN_CONSULTATION", callNextBody.SelectionReason);
            Assert.Equal(activatedBody.QueueEntryId, callNextBody.CalledEntry!.Id);

            // Verify QUEUE_RETURN_TO_DOCTOR audit logs were persisted
            var returnAudits = await Db.AuditLogs
                .AsNoTracking()
                .Where(a => a.Action == ExaminationQueueService.ReturnToDoctorAction &&
                            a.EntityType == QueueStatusService.QueueEntryEntityType)
                .ToListAsync();
            Assert.True(returnAudits.Count >= 2);
        }
        finally
        {
            if (createdServicePointId is long spId)
            {
                await Db.QueueFairnessStates.Where(s => s.ServicePointId == spId).ExecuteDeleteAsync();
                await Db.QueueEntries.Where(q => q.ServicePointId == spId || q.CurrentServicePointId == spId).ExecuteDeleteAsync();
            }

            if (createdVisitIds.Count > 0)
            {
                await Db.QueueEntries.Where(q => q.VisitId.HasValue && createdVisitIds.Contains(q.VisitId.Value)).ExecuteDeleteAsync();
                await Db.Visits.Where(v => createdVisitIds.Contains(v.Id)).ExecuteDeleteAsync();
            }

            if (createdServicePointId is long spIdToDelete)
            {
                await Db.QueueServicePoints.Where(sp => sp.Id == spIdToDelete).ExecuteDeleteAsync();
            }

            if (createdRoomId is long roomIdToDelete)
            {
                await Db.HospitalRooms.Where(r => r.Id == roomIdToDelete).ExecuteDeleteAsync();
            }
        }
    }
}
