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

public sealed class QueueCancelEndpointTests : AuthTestBase
{
    public QueueCancelEndpointTests(TestWebAppFactory factory)
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

    private HttpRequestMessage AuthedPost(
        string url,
        CancelQueueEntryRequest body,
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

    private HttpRequestMessage AuthedPatchStatus(
        string url,
        UpdateQueueStatusRequest body,
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
    public async Task CancelQueueEntry_WithoutToken_Returns401()
    {
        var response = await Client.PostAsJsonAsync(
            "/api/v1/reception/queue-entries/1/cancel",
            new CancelQueueEntryRequest(
                Reason: "Bệnh nhân xin về",
                ExpectedStateVersion: 1));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("DOCTOR", 3L, "doctor01@medflow.test")]
    [InlineData("DIAGNOSTIC_STAFF", 7L, "diagnostic01@medflow.test")]
    public async Task CancelQueueEntry_DisallowedRole_Returns403(string role, long userId, string email)
    {
        using var req = AuthedPost(
            "/api/v1/reception/queue-entries/1/cancel",
            new CancelQueueEntryRequest(
                Reason: "Bệnh nhân xin về",
                ExpectedStateVersion: 1),
            userId,
            email,
            role);

        var res = await Client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task CancelQueueEntry_RecomputesServingOrderPreservesQueueNumberAndAudits()
    {
        var visitCodeA = $"VIS-CNL-A-{Guid.NewGuid():N}"[..22].ToUpperInvariant();
        var visitCodeB = $"VIS-CNL-B-{Guid.NewGuid():N}"[..22].ToUpperInvariant();
        long? createdVisitIdA = null;
        long? createdVisitIdB = null;
        long? createdQueueIdA = null;
        long? createdQueueIdB = null;

        try
        {
            var visitA = new Visit
            {
                VisitCode = visitCodeA,
                PatientId = 9,
                AppointmentId = null,
                AssignedDoctorId = 1,
                ExaminationRoomId = 1,
                CheckedInByStaffId = 3,
                ReasonForVisit = "Khám ngoại trú A",
                Status = "WAITING",
                CheckedInAt = new DateTime(2026, 12, 15, 8, 0, 0, DateTimeKind.Unspecified),
            };
            var visitB = new Visit
            {
                VisitCode = visitCodeB,
                PatientId = 1,
                AppointmentId = null,
                AssignedDoctorId = 1,
                ExaminationRoomId = 1,
                CheckedInByStaffId = 3,
                ReasonForVisit = "Khám ngoại trú B",
                Status = "WAITING",
                CheckedInAt = new DateTime(2026, 12, 15, 8, 5, 0, DateTimeKind.Unspecified),
            };
            Db.Visits.AddRange(visitA, visitB);
            await Db.SaveChangesAsync();
            createdVisitIdA = visitA.Id;
            createdVisitIdB = visitB.Id;

            var queueA = new QueueEntry
            {
                VisitId = visitA.Id,
                AppointmentId = null,
                ServicePointId = 1,
                CurrentServicePointId = 1,
                QueueDate = new DateOnly(2026, 12, 15),
                QueueNumber = 71,
                QueueType = "EXAMINATION",
                Status = "WAITING",
                PresenceStatus = "PRESENT",
                ArrivalClass = "ON_TIME_OR_GRACE",
                ManualPriority = "NORMAL",
                StateVersion = 1,
                ArrivedAt = new DateTime(2026, 12, 15, 8, 0, 0, DateTimeKind.Unspecified),
            };
            var queueB = new QueueEntry
            {
                VisitId = visitB.Id,
                AppointmentId = null,
                ServicePointId = 1,
                CurrentServicePointId = 1,
                QueueDate = new DateOnly(2026, 12, 15),
                QueueNumber = 72,
                QueueType = "EXAMINATION",
                Status = "WAITING",
                PresenceStatus = "PRESENT",
                ArrivalClass = "ON_TIME_OR_GRACE",
                ManualPriority = "NORMAL",
                StateVersion = 1,
                ArrivedAt = new DateTime(2026, 12, 15, 8, 5, 0, DateTimeKind.Unspecified),
            };
            Db.QueueEntries.AddRange(queueA, queueB);
            await Db.SaveChangesAsync();
            createdQueueIdA = queueA.Id;
            createdQueueIdB = queueB.Id;

            // (a) GET /api/v1/reception/examination-queue?queueDate=2026-12-15
            using var getBeforeReq = AuthedGet("/api/v1/reception/examination-queue?queueDate=2026-12-15");
            var getBeforeRes = await Client.SendAsync(getBeforeReq);
            Assert.Equal(HttpStatusCode.OK, getBeforeRes.StatusCode);

            var beforeQueue = await getBeforeRes.Content.ReadFromJsonAsync<ExaminationQueueResponseDto>();
            Assert.NotNull(beforeQueue);

            var bucketBefore = Assert.Single(
                beforeQueue.ServicePoints,
                sp => sp.Entries.Any(e => e.VisitCode == visitCodeA) &&
                      sp.Entries.Any(e => e.VisitCode == visitCodeB));
            var entryABefore = Assert.Single(bucketBefore.Entries, e => e.VisitCode == visitCodeA);
            var entryBBefore = Assert.Single(bucketBefore.Entries, e => e.VisitCode == visitCodeB);
            var previousServingOrderB = entryBBefore.ServingOrder;
            var previousTicketNoB = entryBBefore.TicketNo;
            var wasAAheadOfB = entryABefore.ServingOrder < entryBBefore.ServingOrder;

            var cancelUrlA = $"/api/v1/reception/queue-entries/{queueA.Id}/cancel";

            // (b) POST cancel A with blank reason -> 400
            using var blankReasonReq = AuthedPost(
                cancelUrlA,
                new CancelQueueEntryRequest(
                    Reason: "   ",
                    ExpectedStateVersion: 1));
            var blankReasonRes = await Client.SendAsync(blankReasonReq);
            Assert.Equal(HttpStatusCode.BadRequest, blankReasonRes.StatusCode);

            // (c) Stale version 99 -> 409
            using var staleReq = AuthedPost(
                cancelUrlA,
                new CancelQueueEntryRequest(
                    Reason: "Bệnh nhân xin về",
                    ExpectedStateVersion: 99));
            var staleRes = await Client.SendAsync(staleReq);
            Assert.Equal(HttpStatusCode.Conflict, staleRes.StatusCode);

            // (d) Valid cancel -> 200
            using var validCancelReq = AuthedPost(
                cancelUrlA,
                new CancelQueueEntryRequest(
                    Reason: "Bệnh nhân xin về",
                    ExpectedStateVersion: 1));
            var validCancelRes = await Client.SendAsync(validCancelReq);
            Assert.Equal(HttpStatusCode.OK, validCancelRes.StatusCode);

            var cancelDto = await validCancelRes.Content.ReadFromJsonAsync<UpdateQueueStatusResponseDto>();
            Assert.NotNull(cancelDto);
            Assert.Equal("CANCELLED", cancelDto.CurrentStatus);
            Assert.Equal(71, cancelDto.QueueNumber);
            Assert.Equal(2, cancelDto.StateVersion);
            Assert.Equal("Bệnh nhân xin về", cancelDto.Reason);

            // (e) Same request again -> 409
            using var repeatCancelReq = AuthedPost(
                cancelUrlA,
                new CancelQueueEntryRequest(
                    Reason: "Bệnh nhân xin về",
                    ExpectedStateVersion: 1));
            var repeatCancelRes = await Client.SendAsync(repeatCancelReq);
            Assert.Equal(HttpStatusCode.Conflict, repeatCancelRes.StatusCode);

            // (f) GET again -> A absent from all buckets; B's ServingOrder moved up by 1 if A was ahead; TicketNo unchanged
            using var getAfterReq = AuthedGet("/api/v1/reception/examination-queue?queueDate=2026-12-15");
            var getAfterRes = await Client.SendAsync(getAfterReq);
            Assert.Equal(HttpStatusCode.OK, getAfterRes.StatusCode);

            var afterQueue = await getAfterRes.Content.ReadFromJsonAsync<ExaminationQueueResponseDto>();
            Assert.NotNull(afterQueue);
            Assert.DoesNotContain(afterQueue.ServicePoints.SelectMany(sp => sp.Entries), e => e.VisitCode == visitCodeA);

            var entryBAfter = Assert.Single(afterQueue.ServicePoints.SelectMany(sp => sp.Entries), e => e.VisitCode == visitCodeB);
            if (wasAAheadOfB)
            {
                Assert.Equal(previousServingOrderB - 1, entryBAfter.ServingOrder);
            }
            else
            {
                Assert.Equal(previousServingOrderB, entryBAfter.ServingOrder);
            }

            Assert.Equal(previousTicketNoB, entryBAfter.TicketNo);

            // (g) DB: A Status "CANCELLED", QueueNumber 71. Single audit with QueueEntryCancelAction, ActorUserId 5
            var dbQueueA = await Db.QueueEntries.AsNoTracking().FirstAsync(q => q.Id == queueA.Id);
            Assert.Equal("CANCELLED", dbQueueA.Status);
            Assert.Equal(71, dbQueueA.QueueNumber);

            var queueAIdStr = queueA.Id.ToString();
            var audit = await Db.AuditLogs
                .AsNoTracking()
                .SingleAsync(a => a.Action == QueueStatusService.QueueEntryCancelAction &&
                                  a.EntityId == queueAIdStr);
            Assert.Equal(5L, audit.ActorUserId);
            Assert.Equal("Bệnh nhân xin về", audit.Reason);

            // (h) PATCH /api/v1/reception/queue-entries/{B}/status with TargetStatus "CANCELLED" -> 400
            using var patchCancelBReq = AuthedPatchStatus(
                $"/api/v1/reception/queue-entries/{queueB.Id}/status",
                new UpdateQueueStatusRequest(
                    TargetStatus: "CANCELLED",
                    Reason: "Thử hủy qua API status chung"));
            var patchCancelBRes = await Client.SendAsync(patchCancelBReq);
            Assert.Equal(HttpStatusCode.BadRequest, patchCancelBRes.StatusCode);
        }
        finally
        {
            var queueIdStrings = new[] { createdQueueIdA?.ToString(), createdQueueIdB?.ToString() }
                .Where(s => s is not null)
                .Cast<string>()
                .ToArray();

            if (queueIdStrings.Length > 0)
            {
                await Db.AuditLogs
                    .Where(a => (a.Action == QueueStatusService.QueueEntryCancelAction ||
                                 a.Action == QueueStatusService.QueueStatusUpdateAction) &&
                                a.EntityId != null &&
                                queueIdStrings.Contains(a.EntityId))
                    .ExecuteDeleteAsync();
            }

            var queueIds = new[] { createdQueueIdA, createdQueueIdB }
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToArray();

            if (queueIds.Length > 0)
            {
                await Db.QueueEntries
                    .Where(q => queueIds.Contains(q.Id))
                    .ExecuteDeleteAsync();
            }

            var visitIds = new[] { createdVisitIdA, createdVisitIdB }
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToArray();

            if (visitIds.Length > 0)
            {
                await Db.Visits
                    .Where(v => visitIds.Contains(v.Id))
                    .ExecuteDeleteAsync();
            }
        }
    }
}
