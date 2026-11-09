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

public sealed class QueuePriorityEndpointTests : AuthTestBase
{
    public QueuePriorityEndpointTests(TestWebAppFactory factory)
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

    private HttpRequestMessage AuthedPatch(
        string url,
        UpdateQueuePriorityRequest body,
        long userId = 5,
        string email = "reception01@medflow.test",
        string role = "RECEPTION")
    {
        var token = GenerateToken(userId, email, role);
        var request = new HttpRequestMessage(HttpMethod.Patch, url)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    [Fact]
    public async Task UpdateQueuePriority_WithoutToken_Returns401()
    {
        var response = await Client.PatchAsJsonAsync(
            "/api/v1/reception/queue-entries/1/priority",
            new UpdateQueuePriorityRequest(
                Priority: "PREGNANT",
                Reason: "Có sổ khám thai",
                VerificationConfirmed: true,
                ExpectedStateVersion: 1));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("DOCTOR", 3L, "doctor01@medflow.test")]
    [InlineData("DIAGNOSTIC_STAFF", 7L, "diagnostic01@medflow.test")]
    public async Task UpdateQueuePriority_DisallowedRole_Returns403(string role, long userId, string email)
    {
        using var req = AuthedPatch(
            "/api/v1/reception/queue-entries/1/priority",
            new UpdateQueuePriorityRequest(
                Priority: "PREGNANT",
                Reason: "Có sổ khám thai",
                VerificationConfirmed: true,
                ExpectedStateVersion: 1),
            userId,
            email,
            role);

        var res = await Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task UpdateQueuePriority_ValidationVersionPolicyAndHappyPath_PersistsPriorityStateVersionAndAuditLog()
    {
        var visitCode = $"VIS-PRI-{Guid.NewGuid():N}"[..22].ToUpperInvariant();
        long? createdVisitId = null;
        long? createdQueueId = null;

        try
        {
            // Seeded patientId = 9 (Hoàng Ngọc Phương) is FEMALE
            var seededVisit = new Visit
            {
                VisitCode = visitCode,
                PatientId = 9,
                AppointmentId = null,
                AssignedDoctorId = 1,
                ExaminationRoomId = 1,
                CheckedInByStaffId = 3,
                ReasonForVisit = "Khám thai ngoại trú",
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
                QueueNumber = 58,
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

            var url = $"/api/v1/reception/queue-entries/{seededQueue.Id}/priority";

            // 1. URGENT -> 400 Bad Request
            using var urgentReq = AuthedPatch(
                url,
                new UpdateQueuePriorityRequest(
                    Priority: "URGENT",
                    Reason: "Cấp cứu",
                    VerificationConfirmed: true,
                    ExpectedStateVersion: 1));
            var urgentRes = await Client.SendAsync(urgentReq);
            Assert.Equal(HttpStatusCode.BadRequest, urgentRes.StatusCode);

            // 2. Missing reason -> 400 Bad Request
            using var missingReasonReq = AuthedPatch(
                url,
                new UpdateQueuePriorityRequest(
                    Priority: "PREGNANT",
                    Reason: "   ",
                    VerificationConfirmed: true,
                    ExpectedStateVersion: 1));
            var missingReasonRes = await Client.SendAsync(missingReasonReq);
            Assert.Equal(HttpStatusCode.BadRequest, missingReasonRes.StatusCode);

            // 3. PREGNANT without verificationConfirmed (seeded hospital-1 policy enabled) -> 400 Bad Request
            using var unverifiedReq = AuthedPatch(
                url,
                new UpdateQueuePriorityRequest(
                    Priority: "PREGNANT",
                    Reason: "Sổ khám thai đã xuất trình",
                    VerificationConfirmed: false,
                    ExpectedStateVersion: 1));
            var unverifiedRes = await Client.SendAsync(unverifiedReq);
            Assert.Equal(HttpStatusCode.BadRequest, unverifiedRes.StatusCode);

            // 4. Stale version -> 409 Conflict
            using var staleReq = AuthedPatch(
                url,
                new UpdateQueuePriorityRequest(
                    Priority: "PREGNANT",
                    Reason: "Sổ khám thai đã xuất trình",
                    VerificationConfirmed: true,
                    ExpectedStateVersion: 99));
            var staleRes = await Client.SendAsync(staleReq);
            Assert.Equal(HttpStatusCode.Conflict, staleRes.StatusCode);

            // 5. Happy path with verificationConfirmed = true -> 200 OK
            using var okReq = AuthedPatch(
                url,
                new UpdateQueuePriorityRequest(
                    Priority: "PREGNANT",
                    Reason: "Đã kiểm tra sổ khám thai tại quầy tiếp nhận",
                    VerificationConfirmed: true,
                    ExpectedStateVersion: 1));
            var okRes = await Client.SendAsync(okReq);
            Assert.Equal(HttpStatusCode.OK, okRes.StatusCode);

            var dto = await okRes.Content.ReadFromJsonAsync<UpdateQueuePriorityResponseDto>();
            Assert.NotNull(dto);
            Assert.Equal(seededQueue.Id, dto.QueueEntryId);
            Assert.Equal(seededVisit.Id, dto.VisitId);
            Assert.Equal(58, dto.QueueNumber);
            Assert.Equal("EXAMINATION", dto.QueueType);
            Assert.Equal("NORMAL", dto.PreviousPriority);
            Assert.Equal("PREGNANT", dto.Priority);
            Assert.True(dto.VerificationRequired);
            Assert.True(dto.VerificationConfirmed);
            Assert.Equal(1, dto.PreviousStateVersion);
            Assert.Equal(2, dto.StateVersion);
            Assert.Equal("Đã kiểm tra sổ khám thai tại quầy tiếp nhận", dto.Reason);

            var dbQueue = await Db.QueueEntries.AsNoTracking().FirstAsync(q => q.Id == seededQueue.Id);
            Assert.Equal("PREGNANT", dbQueue.ManualPriority);
            Assert.Equal(2, dbQueue.StateVersion);
            Assert.Equal(58, dbQueue.QueueNumber);

            var queueIdStr = seededQueue.Id.ToString();
            var audit = await Db.AuditLogs
                .AsNoTracking()
                .SingleAsync(a => a.Action == QueueStatusService.QueuePriorityUpdateAction &&
                                  a.EntityId == queueIdStr);
            Assert.Equal(5L, audit.ActorUserId);
            Assert.Equal("Đã kiểm tra sổ khám thai tại quầy tiếp nhận", audit.Reason);
        }
        finally
        {
            if (createdQueueId.HasValue)
            {
                var entityIdStr = createdQueueId.Value.ToString();
                await Db.AuditLogs
                    .Where(a => a.Action == QueueStatusService.QueuePriorityUpdateAction &&
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
        }
    }
}
