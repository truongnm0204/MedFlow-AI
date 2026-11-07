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

public sealed class QueueCallNextEndpointTests : AuthTestBase
{
    public QueueCallNextEndpointTests(TestWebAppFactory factory)
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
    public async Task CallNext_WithoutToken_Returns401()
    {
        var res = await Client.PostAsJsonAsync(
            "/api/v1/queue/service-points/sp-1/call-next",
            new CallNextPatientRequest(Notes: null));

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task CallNext_PatientRole_Returns403()
    {
        using var req = AuthedPost(
            "/api/v1/queue/service-points/sp-1/call-next",
            new CallNextPatientRequest(Notes: null),
            userId: 1,
            email: "patient01@medflow.test",
            role: "PATIENT");

        var res = await Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task CallNext_LiveDatabase_UpdatesStatusFairnessAndAuditAndSerializesConcurrentCalls()
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

        var roomCode = $"CNX-{Guid.NewGuid():N}"[..10].ToUpperInvariant();
        var visitCode1 = $"VIS-CN1-{Guid.NewGuid():N}"[..20].ToUpperInvariant();
        var visitCode2 = $"VIS-CN2-{Guid.NewGuid():N}"[..20].ToUpperInvariant();

        long? createdRoomId = null;
        long? createdServicePointId = null;
        var createdVisitIds = new List<long>();
        var createdQueueIds = new List<long>();

        try
        {
            var staleSpIds = await Db.QueueServicePoints
                .Where(sp => sp.Name.StartsWith("Điểm phục vụ CallNext CNX-"))
                .Select(sp => sp.Id)
                .ToListAsync();
            if (staleSpIds.Count > 0)
            {
                await Db.QueueFairnessStates.Where(s => staleSpIds.Contains(s.ServicePointId)).ExecuteDeleteAsync();
                await Db.QueueEntries.Where(q => q.ServicePointId.HasValue && staleSpIds.Contains(q.ServicePointId.Value)).ExecuteDeleteAsync();
                await Db.QueueServicePoints.Where(sp => staleSpIds.Contains(sp.Id)).ExecuteDeleteAsync();
            }
            var staleRooms = await Db.HospitalRooms
                .Where(r => r.RoomCode.StartsWith("CNX-"))
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
                Name = $"Phòng khám CallNext {roomCode}",
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
                Name = $"Điểm phục vụ CallNext {roomCode}",
                IsActive = true,
            };
            Db.QueueServicePoints.Add(newSp);
            await Db.SaveChangesAsync();
            createdServicePointId = newSp.Id;

            var visit1 = new Visit
            {
                VisitCode = visitCode1,
                PatientId = 1,
                AssignedDoctorId = 1,
                ExaminationRoomId = newRoom.Id,
                CheckedInByStaffId = 3,
                ReasonForVisit = "Khám kiểm tra call-next 1",
                Status = "WAITING",
                CheckedInAt = baseTime.AddMinutes(-20),
            };
            var visit2 = new Visit
            {
                VisitCode = visitCode2,
                PatientId = 4,
                AssignedDoctorId = 1,
                ExaminationRoomId = newRoom.Id,
                CheckedInByStaffId = 3,
                ReasonForVisit = "Khám kiểm tra call-next 2",
                Status = "WAITING",
                CheckedInAt = baseTime.AddMinutes(-15),
            };
            Db.Visits.AddRange(visit1, visit2);
            await Db.SaveChangesAsync();
            createdVisitIds.AddRange([visit1.Id, visit2.Id]);

            var queue1 = new QueueEntry
            {
                VisitId = visit1.Id,
                ServicePointId = newSp.Id,
                CurrentServicePointId = newSp.Id,
                QueueDate = todayLocal,
                QueueNumber = 1,
                QueueType = "EXAMINATION",
                Status = "WAITING",
                PresenceStatus = "PRESENT",
                ArrivalClass = "WALK_IN",
                ManualPriority = "ELDERLY",
                StateVersion = 1,
                ArrivedAt = baseTime.AddMinutes(-20),
                ReadyAt = baseTime.AddMinutes(-20),
            };
            var queue2 = new QueueEntry
            {
                VisitId = visit2.Id,
                ServicePointId = newSp.Id,
                CurrentServicePointId = newSp.Id,
                QueueDate = todayLocal,
                QueueNumber = 2,
                QueueType = "EXAMINATION",
                Status = "WAITING",
                PresenceStatus = "PRESENT",
                ArrivalClass = "WALK_IN",
                ManualPriority = "NORMAL",
                StateVersion = 1,
                ArrivedAt = baseTime.AddMinutes(-15),
                ReadyAt = baseTime.AddMinutes(-15),
            };
            Db.QueueEntries.AddRange(queue1, queue2);
            await Db.SaveChangesAsync();
            createdQueueIds.AddRange([queue1.Id, queue2.Id]);

            // Scenario 9: Concurrent call-next requests on the same service point
            using var req1 = AuthedPost(
                $"/api/v1/queue/service-points/sp-{newSp.Id}/call-next",
                new CallNextPatientRequest(Notes: "Concurrent call 1"));
            using var req2 = AuthedPost(
                $"/api/v1/queue/service-points/sp-{newSp.Id}/call-next",
                new CallNextPatientRequest(Notes: "Concurrent call 2"));

            var responses = await Task.WhenAll(Client.SendAsync(req1), Client.SendAsync(req2));
            var statusCodes = responses.Select(r => r.StatusCode).OrderBy(s => (int)s).ToArray();

            Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], statusCodes);

            var okResponse = responses.Single(r => r.StatusCode == HttpStatusCode.OK);
            var dto = await okResponse.Content.ReadFromJsonAsync<CallNextPatientResponseDto>();
            Assert.NotNull(dto);
            Assert.Equal("CALLED", dto.Outcome);
            Assert.NotNull(dto.CalledEntry);
            Assert.Equal("CALLED", dto.CalledEntry!.Status);

            var dbEntries = await Db.QueueEntries
                .AsNoTracking()
                .Where(q => createdQueueIds.Contains(q.Id))
                .ToListAsync();
            Assert.Single(dbEntries, q => q.Status == "CALLED");

            var fairness = await Db.QueueFairnessStates
                .AsNoTracking()
                .SingleOrDefaultAsync(s => s.ServicePointId == newSp.Id && s.QueueDate == todayLocal);
            Assert.NotNull(fairness);
            Assert.Equal(queue1.Id, fairness.LastStartedQueueEntryId);

            var entityIdStr = queue1.Id.ToString(CultureInfo.InvariantCulture);
            var auditCount = await Db.AuditLogs
                .AsNoTracking()
                .CountAsync(a =>
                    a.EntityType == QueueStatusService.QueueEntryEntityType &&
                    a.EntityId == entityIdStr &&
                    a.Action == ExaminationQueueService.CallNextAction);
            Assert.Equal(1, auditCount);

            var token = GenerateToken(5, "reception01@medflow.test", "RECEPTION");
            using var getBoardReq = new HttpRequestMessage(
                HttpMethod.Get,
                $"/api/v1/reception/examination-queue?servicePointId=sp-{newSp.Id}");
            getBoardReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var getBoardRes = await Client.SendAsync(getBoardReq);
            Assert.Equal(HttpStatusCode.OK, getBoardRes.StatusCode);
            var boardDto = await getBoardRes.Content.ReadFromJsonAsync<ExaminationQueueResponseDto>();
            Assert.NotNull(boardDto);
            var spDto = Assert.Single(boardDto.ServicePoints, sp => sp.ServicePointId == $"sp-{newSp.Id}");
            var calledBoardEntry = Assert.Single(spDto.Entries, e => e.Status == "CALLED");
            Assert.Equal(1, calledBoardEntry.ServingOrder);
        }
        finally
        {
            if (createdServicePointId.HasValue)
            {
                await Db.QueueFairnessStates
                    .Where(s => s.ServicePointId == createdServicePointId.Value)
                    .ExecuteDeleteAsync();
            }

            if (createdQueueIds.Count > 0)
            {
                var entityIds = createdQueueIds
                    .Select(id => id.ToString(CultureInfo.InvariantCulture))
                    .ToList();
                await Db.AuditLogs
                    .Where(a => a.EntityType == QueueStatusService.QueueEntryEntityType &&
                                entityIds.Contains(a.EntityId!))
                    .ExecuteDeleteAsync();

                await Db.QueueEntries
                    .Where(q => createdQueueIds.Contains(q.Id))
                    .ExecuteDeleteAsync();
            }

            if (createdServicePointId.HasValue)
            {
                await Db.QueueServicePoints
                    .Where(sp => sp.Id == createdServicePointId.Value)
                    .ExecuteDeleteAsync();
            }

            if (createdVisitIds.Count > 0)
            {
                await Db.Visits
                    .Where(v => createdVisitIds.Contains(v.Id))
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
