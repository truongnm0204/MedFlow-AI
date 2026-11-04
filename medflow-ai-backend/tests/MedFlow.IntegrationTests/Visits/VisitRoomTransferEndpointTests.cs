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

public sealed class VisitRoomTransferEndpointTests : AuthTestBase
{
    public VisitRoomTransferEndpointTests(TestWebAppFactory factory)
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
        TransferVisitRoomRequest body,
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
        var getResponse = await Client.GetAsync("/api/v1/visits/1/compatible-rooms");
        Assert.Equal(HttpStatusCode.Unauthorized, getResponse.StatusCode);

        var postResponse = await Client.PostAsJsonAsync(
            "/api/v1/visits/1/transfer-room",
            new TransferVisitRoomRequest(TargetRoomId: 2, Reason: "Lý do chuyển phòng"));
        Assert.Equal(HttpStatusCode.Unauthorized, postResponse.StatusCode);
    }

    [Theory]
    [InlineData("PATIENT", 1L, "patient01@medflow.test")]
    [InlineData("DOCTOR", 3L, "doctor01@medflow.test")]
    [InlineData("DIAGNOSTIC_STAFF", 7L, "diagnostic_staff01@medflow.test")]
    [InlineData("PHARMACY_STAFF", 9L, "pharmacy_staff01@medflow.test")]
    public async Task Endpoints_DisallowedRole_Return403(string role, long userId, string email)
    {
        using var getReq = AuthedGet("/api/v1/visits/1/compatible-rooms", userId, email, role);
        var getRes = await Client.SendAsync(getReq);
        Assert.Equal(HttpStatusCode.Forbidden, getRes.StatusCode);

        using var postReq = AuthedSend(
            HttpMethod.Post,
            "/api/v1/visits/1/transfer-room",
            new TransferVisitRoomRequest(TargetRoomId: 2, Reason: "Lý do chuyển phòng"),
            userId,
            email,
            role);
        var postRes = await Client.SendAsync(postReq);
        Assert.Equal(HttpStatusCode.Forbidden, postRes.StatusCode);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("invalid@code")]
    public async Task TransferRoom_InvalidRouteId_Returns400(string invalidId)
    {
        using var getReq = AuthedGet($"/api/v1/visits/{invalidId}/compatible-rooms");
        var getRes = await Client.SendAsync(getReq);
        Assert.Equal(HttpStatusCode.BadRequest, getRes.StatusCode);

        using var postReq = AuthedSend(
            HttpMethod.Post,
            $"/api/v1/visits/{invalidId}/transfer-room",
            new TransferVisitRoomRequest(TargetRoomId: 2, Reason: "Lý do hợp lệ"));
        var postRes = await Client.SendAsync(postReq);
        Assert.Equal(HttpStatusCode.BadRequest, postRes.StatusCode);
    }

    [Fact]
    public async Task TransferRoom_MissingRoomOrReason_Returns400ProblemDetails()
    {
        using var missingRoomReq = AuthedSend(
            HttpMethod.Post,
            "/api/v1/visits/1/transfer-room",
            new TransferVisitRoomRequest(Reason: "Chưa chọn phòng"));
        var missingRoomRes = await Client.SendAsync(missingRoomReq);
        Assert.Equal(HttpStatusCode.BadRequest, missingRoomRes.StatusCode);
        var missingRoomProblem = await missingRoomRes.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(missingRoomProblem);
        Assert.Equal(400, missingRoomProblem.Status);

        using var missingReasonReq = AuthedSend(
            HttpMethod.Post,
            "/api/v1/visits/1/transfer-room",
            new TransferVisitRoomRequest(TargetRoomId: 2, Reason: "   "));
        var missingReasonRes = await Client.SendAsync(missingReasonReq);
        Assert.Equal(HttpStatusCode.BadRequest, missingReasonRes.StatusCode);
    }

    [Fact]
    public async Task TransferRoom_NonExistentVisit_Returns404()
    {
        using var getReq = AuthedGet("/api/v1/visits/VIS-999999/compatible-rooms");
        var getRes = await Client.SendAsync(getReq);
        Assert.Equal(HttpStatusCode.NotFound, getRes.StatusCode);

        using var postReq = AuthedSend(
            HttpMethod.Post,
            "/api/v1/visits/999999/transfer-room",
            new TransferVisitRoomRequest(TargetRoomId: 2, Reason: "Điều phối"));
        var postRes = await Client.SendAsync(postReq);
        Assert.Equal(HttpStatusCode.NotFound, postRes.StatusCode);
    }

    [Fact]
    public async Task TransferRoom_SeededCompletedVisit_Returns409Conflict()
    {
        using var postReq = AuthedSend(
            HttpMethod.Post,
            "/api/v1/visits/VIS-202610-0001/transfer-room",
            new TransferVisitRoomRequest(TargetRoomId: 2, Reason: "Chuyển phòng lượt khám đã hoàn tất"));
        var postRes = await Client.SendAsync(postReq);

        Assert.Equal(HttpStatusCode.Conflict, postRes.StatusCode);
        var problem = await postRes.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(409, problem.Status);
    }

    [Fact]
    public async Task ActiveVisitTransferFlow_ListsCompatibleRoomsTransfersPersistsQueueAndAuditHistory()
    {
        var visitCode = $"VIS-XFER-{Guid.NewGuid():N}"[..22].ToUpperInvariant();
        long? createdVisitId = null;
        long? createdQueueId = null;

        try
        {
            var seededVisit = new Visit
            {
                VisitCode = visitCode,
                PatientId = 1,
                AppointmentId = null,
                AssignedDoctorId = 1,
                ExaminationRoomId = 1, // RM-001 (Department 2 - Khoa Tiêu hóa)
                CheckedInByStaffId = 3,
                ReasonForVisit = "Đau dạ dày cấp",
                Status = "CALLED",
                CheckedInAt = new DateTime(2026, 10, 26, 8, 30, 0, DateTimeKind.Unspecified),
            };
            Db.Visits.Add(seededVisit);
            await Db.SaveChangesAsync();
            createdVisitId = seededVisit.Id;

            var seededQueue = new QueueEntry
            {
                VisitId = seededVisit.Id,
                AppointmentId = null,
                ServicePointId = 1,
                QueueDate = new DateOnly(2026, 10, 26),
                QueueNumber = 88,
                QueueType = "EXAMINATION",
                Status = "CALLED",
                PresenceStatus = "PRESENT",
                ArrivedAt = new DateTime(2026, 10, 26, 8, 30, 0, DateTimeKind.Unspecified),
                ReadyAt = new DateTime(2026, 10, 26, 8, 31, 0, DateTimeKind.Unspecified),
                CalledAt = new DateTime(2026, 10, 26, 8, 35, 0, DateTimeKind.Unspecified),
            };
            Db.QueueEntries.Add(seededQueue);
            await Db.SaveChangesAsync();
            createdQueueId = seededQueue.Id;

            // 1. GET compatible rooms as RECEPTION
            using var getCompatibleReq = AuthedGet($"/api/v1/visits/{visitCode}/compatible-rooms");
            var getCompatibleRes = await Client.SendAsync(getCompatibleReq);
            Assert.Equal(HttpStatusCode.OK, getCompatibleRes.StatusCode);

            var compatibleDto = await getCompatibleRes.Content.ReadFromJsonAsync<VisitCompatibleRoomsResponseDto>();
            Assert.NotNull(compatibleDto);
            Assert.Equal(seededVisit.Id, compatibleDto.VisitId);
            Assert.Equal(visitCode, compatibleDto.VisitCode);
            Assert.Equal(1L, compatibleDto.CurrentRoomId);
            Assert.Equal("RM-001", compatibleDto.CurrentRoomCode);
            Assert.Equal(2L, compatibleDto.DepartmentId);
            Assert.Equal(88, compatibleDto.QueueNumber);

            var rm001 = Assert.Single(compatibleDto.CompatibleRooms, r => r.RoomCode == "RM-001");
            Assert.True(rm001.IsCurrentRoom);
            Assert.False(rm001.IsAvailable);

            var rm002 = Assert.Single(compatibleDto.CompatibleRooms, r => r.RoomCode == "RM-002");
            Assert.False(rm002.IsCurrentRoom);
            Assert.True(rm002.IsCompatible);
            Assert.True(rm002.IsAvailable);

            // 2. Reject invalid targets (same room RM-001, cross-department RM-003, diagnostic RM-005)
            using var sameRoomReq = AuthedSend(
                HttpMethod.Post,
                $"/api/v1/visits/{visitCode}/transfer-room",
                new TransferVisitRoomRequest(TargetRoomId: 1, Reason: "Trùng phòng"));
            var sameRoomRes = await Client.SendAsync(sameRoomReq);
            Assert.Equal(HttpStatusCode.BadRequest, sameRoomRes.StatusCode);

            using var wrongDeptReq = AuthedSend(
                HttpMethod.Post,
                $"/api/v1/visits/{visitCode}/transfer-room",
                new TransferVisitRoomRequest(TargetRoomId: 3, Reason: "Khác khoa phòng"));
            var wrongDeptRes = await Client.SendAsync(wrongDeptReq);
            Assert.Equal(HttpStatusCode.BadRequest, wrongDeptRes.StatusCode);

            using var diagnosticRoomReq = AuthedSend(
                HttpMethod.Post,
                $"/api/v1/visits/{visitCode}/transfer-room",
                new TransferVisitRoomRequest(TargetRoomId: 5, Reason: "Phòng CLS"));
            var diagnosticRoomRes = await Client.SendAsync(diagnosticRoomReq);
            Assert.Equal(HttpStatusCode.BadRequest, diagnosticRoomRes.StatusCode);

            using var missingRoomIdReq = AuthedSend(
                HttpMethod.Post,
                $"/api/v1/visits/{visitCode}/transfer-room",
                new TransferVisitRoomRequest(TargetRoomId: 999999, Reason: "Phòng không tồn tại"));
            var missingRoomIdRes = await Client.SendAsync(missingRoomIdReq);
            Assert.Equal(HttpStatusCode.NotFound, missingRoomIdRes.StatusCode);

            using var missingRoomCodeReq = AuthedSend(
                HttpMethod.Put,
                $"/api/v1/visits/{visitCode}/transfer-room",
                new TransferVisitRoomRequest(TargetRoomCode: "RM-9999", Reason: "Mã phòng không tồn tại"));
            var missingRoomCodeRes = await Client.SendAsync(missingRoomCodeReq);
            Assert.Equal(HttpStatusCode.NotFound, missingRoomCodeRes.StatusCode);

            // 3. Transfer from RM-001 -> RM-002 as RECEPTION
            using var transfer1Req = AuthedSend(
                HttpMethod.Post,
                $"/api/v1/visits/{visitCode}/transfer-room",
                new TransferVisitRoomRequest(
                    TargetRoomId: 2,
                    Reason: "Phòng Khám Tiêu hóa 101 quá tải",
                    Notes: "Bệnh nhân đồng ý chuyển sang phòng 102"));
            var transfer1Res = await Client.SendAsync(transfer1Req);
            Assert.Equal(HttpStatusCode.OK, transfer1Res.StatusCode);

            var transfer1Dto = await transfer1Res.Content.ReadFromJsonAsync<VisitRoomTransferResponseDto>();
            Assert.NotNull(transfer1Dto);
            Assert.Equal(seededVisit.Id, transfer1Dto.VisitId);
            Assert.Equal(1L, transfer1Dto.PreviousRoomId);
            Assert.Equal("RM-001", transfer1Dto.PreviousRoomCode);
            Assert.Equal(2L, transfer1Dto.CurrentRoomId);
            Assert.Equal("RM-002", transfer1Dto.CurrentRoomCode);
            Assert.Equal("WAITING", transfer1Dto.VisitStatus);
            Assert.Equal(88, transfer1Dto.QueueNumber);
            Assert.Equal("WAITING", transfer1Dto.QueueStatus);
            Assert.Single(transfer1Dto.TransferHistory);

            // Verify DB state after transfer 1
            var dbVisitAfter1 = await Db.Visits.AsNoTracking().FirstAsync(v => v.Id == seededVisit.Id);
            Assert.Equal(2L, dbVisitAfter1.ExaminationRoomId);
            Assert.Equal("WAITING", dbVisitAfter1.Status);

            var dbQueueAfter1 = await Db.QueueEntries.AsNoTracking().FirstAsync(q => q.Id == seededQueue.Id);
            Assert.Equal(88, dbQueueAfter1.QueueNumber);
            Assert.Equal("WAITING", dbQueueAfter1.Status);
            Assert.Null(dbQueueAfter1.CalledAt);

            // 4. Transfer back from RM-002 -> RM-001 as HOSPITAL_ADMIN via reception alias route
            using var transfer2Req = AuthedSend(
                HttpMethod.Post,
                $"/api/v1/reception/visits/{seededVisit.Id}/transfer-room",
                new TransferVisitRoomRequest(
                    TargetRoomCode: "RM-001",
                    Reason: "Điều phối lại về phòng 101"),
                userId: 11,
                email: "hospital_admin01@medflow.test",
                role: "HOSPITAL_ADMIN");
            var transfer2Res = await Client.SendAsync(transfer2Req);
            Assert.Equal(HttpStatusCode.OK, transfer2Res.StatusCode);

            var transfer2Dto = await transfer2Res.Content.ReadFromJsonAsync<VisitRoomTransferResponseDto>();
            Assert.NotNull(transfer2Dto);
            Assert.Equal(2L, transfer2Dto.PreviousRoomId);
            Assert.Equal("RM-002", transfer2Dto.PreviousRoomCode);
            Assert.Equal(1L, transfer2Dto.CurrentRoomId);
            Assert.Equal("RM-001", transfer2Dto.CurrentRoomCode);
            Assert.Equal(2, transfer2Dto.TransferHistory.Count);
            Assert.Equal("RM-002", transfer2Dto.TransferHistory[0].FromRoomCode);
            Assert.Equal("RM-001", transfer2Dto.TransferHistory[0].ToRoomCode);
            Assert.Equal("RM-001", transfer2Dto.TransferHistory[1].FromRoomCode);
            Assert.Equal("RM-002", transfer2Dto.TransferHistory[1].ToRoomCode);
        }
        finally
        {
            if (createdVisitId.HasValue)
            {
                var entityIdStr = createdVisitId.Value.ToString();
                await Db.AuditLogs
                    .Where(a => a.Action == VisitRoomTransferService.VisitRoomTransferAction &&
                                a.EntityId == entityIdStr)
                    .ExecuteDeleteAsync();
            }

            if (createdQueueId.HasValue)
            {
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
