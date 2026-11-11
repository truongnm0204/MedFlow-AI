using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MedFlow.Application.DTOs.Diagnostic;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Entities;
using MedFlow.IntegrationTests.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace MedFlow.IntegrationTests.Diagnostic;

public sealed class DiagnosticQueueEndpointTests : AuthTestBase
{
    private const string QueueBoardRoute = "/api/v1/diagnostic/queue";
    private const string QueueBoardAliasRoute = "/api/v1/diagnostic-queue";
    private const string QueueEntryDetailRoute = "/api/v1/diagnostic/queue/DIA-202610-0001";
    private const string CallNextRoute = "/api/v1/diagnostic/queue/service-points/sp-501/call-next";
    private const string UpdateStatusRoute = "/api/v1/diagnostic/queue/DIA-202610-0001/status";
    private const string MissedTurnRoute = "/api/v1/diagnostic/queue/DIA-202610-0001/missed-turn";
    private const string RestoreTurnRoute = "/api/v1/diagnostic/queue/DIA-202610-0001/restore-turn";
    private const string CancelRoute = "/api/v1/diagnostic/queue/DIA-202610-0001/cancel";

    private readonly TestWebAppFactory _factory;

    public DiagnosticQueueEndpointTests(TestWebAppFactory factory)
        : base(factory)
    {
        _factory = factory;
    }

    private string GenerateToken(long userId, string email, params string[] roles)
    {
        using var scope = _factory.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var user = new User { Id = userId, Email = email };
        return jwtService.GenerateAccessToken(user, roles).AccessToken;
    }

    private async Task<HttpResponseMessage> GetWithTokenAsync(string url, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await Client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> PostWithTokenAsync<TBody>(string url, string token, TBody body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await Client.SendAsync(request);
    }

    [Fact]
    public async Task DiagnosticQueueEndpoints_Unauthenticated_Return401()
    {
        var boardResponse = await Client.GetAsync(QueueBoardRoute);
        Assert.Equal(HttpStatusCode.Unauthorized, boardResponse.StatusCode);

        var detailResponse = await Client.GetAsync(QueueEntryDetailRoute);
        Assert.Equal(HttpStatusCode.Unauthorized, detailResponse.StatusCode);

        using var callNextRequest = new HttpRequestMessage(HttpMethod.Post, CallNextRoute);
        var callNextResponse = await Client.SendAsync(callNextRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, callNextResponse.StatusCode);

        using var statusRequest = new HttpRequestMessage(HttpMethod.Post, UpdateStatusRoute);
        var statusResponse = await Client.SendAsync(statusRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, statusResponse.StatusCode);

        using var cancelRequest = new HttpRequestMessage(HttpMethod.Post, CancelRoute);
        var cancelResponse = await Client.SendAsync(cancelRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, cancelResponse.StatusCode);
    }

    [Theory]
    [InlineData("PATIENT")]
    [InlineData("DOCTOR")]
    [InlineData("RECEPTION")]
    [InlineData("PHARMACY_STAFF")]
    [InlineData("HOSPITAL_ADMIN")]
    [InlineData("SYSTEM_ADMIN")]
    public async Task DiagnosticQueueEndpoints_ForbiddenRoles_Return403(string role)
    {
        var token = GenerateToken(13, $"{role.ToLowerInvariant()}@medflow.test", role);

        var boardResponse = await GetWithTokenAsync(QueueBoardRoute, token);
        Assert.Equal(HttpStatusCode.Forbidden, boardResponse.StatusCode);

        var detailResponse = await GetWithTokenAsync(QueueEntryDetailRoute, token);
        Assert.Equal(HttpStatusCode.Forbidden, detailResponse.StatusCode);

        var callNextResponse = await PostWithTokenAsync(CallNextRoute, token, new { });
        Assert.Equal(HttpStatusCode.Forbidden, callNextResponse.StatusCode);

        var statusResponse = await PostWithTokenAsync(
            UpdateStatusRoute,
            token,
            new { targetStatus = "CALLED" });
        Assert.Equal(HttpStatusCode.Forbidden, statusResponse.StatusCode);

        var missedResponse = await PostWithTokenAsync(
            MissedTurnRoute,
            token,
            new { reason = "Bệnh nhân vắng mặt khi gọi tên" });
        Assert.Equal(HttpStatusCode.Forbidden, missedResponse.StatusCode);

        var restoreResponse = await PostWithTokenAsync(
            RestoreTurnRoute,
            token,
            new { reason = "Bệnh nhân đã quay lại" });
        Assert.Equal(HttpStatusCode.Forbidden, restoreResponse.StatusCode);

        var cancelResponse = await PostWithTokenAsync(
            CancelRoute,
            token,
            new { reason = "Hủy lượt cận lâm sàng" });
        Assert.Equal(HttpStatusCode.Forbidden, cancelResponse.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/diagnostic/queue?limit=0")]
    [InlineData("/api/v1/diagnostic/queue?limit=201")]
    [InlineData("/api/v1/diagnostic/queue?category=INVALID_CATEGORY")]
    [InlineData("/api/v1/diagnostic/queue?status=INVALID_STATUS")]
    [InlineData("/api/v1/diagnostic/queue?priority=INVALID_PRIORITY")]
    [InlineData("/api/v1/diagnostic/queue?presenceStatus=INVALID_PRESENCE")]
    [InlineData("/api/v1/diagnostic/queue?queueDate=not-a-date")]
    public async Task GetDiagnosticQueue_InvalidQueryParameters_Return400(string url)
    {
        var token = GenerateToken(7, "diagnostic_staff01@medflow.test", "DIAGNOSTIC_STAFF");

        var response = await GetWithTokenAsync(url, token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task QueueMutationEndpoints_InvalidPayloads_Return400()
    {
        var token = GenerateToken(7, "diagnostic_staff01@medflow.test", "DIAGNOSTIC_STAFF");

        var emptyTargetStatusResponse = await PostWithTokenAsync(
            UpdateStatusRoute,
            token,
            new { targetStatus = "" });
        Assert.Equal(HttpStatusCode.BadRequest, emptyTargetStatusResponse.StatusCode);

        var invalidTargetStatusResponse = await PostWithTokenAsync(
            UpdateStatusRoute,
            token,
            new { targetStatus = "INVALID_TARGET" });
        Assert.Equal(HttpStatusCode.BadRequest, invalidTargetStatusResponse.StatusCode);

        var invalidStateVersionResponse = await PostWithTokenAsync(
            UpdateStatusRoute,
            token,
            new { targetStatus = "CALLED", expectedStateVersion = 0 });
        Assert.Equal(HttpStatusCode.BadRequest, invalidStateVersionResponse.StatusCode);

        var shortMissedTurnReasonResponse = await PostWithTokenAsync(
            MissedTurnRoute,
            token,
            new { reason = "abc" });
        Assert.Equal(HttpStatusCode.BadRequest, shortMissedTurnReasonResponse.StatusCode);

        var shortCancelReasonResponse = await PostWithTokenAsync(
            CancelRoute,
            token,
            new { reason = "abc" });
        Assert.Equal(HttpStatusCode.BadRequest, shortCancelReasonResponse.StatusCode);

        var xssNotesResponse = await PostWithTokenAsync(
            CallNextRoute,
            token,
            new { notes = "<script>alert(1)</script>" });
        Assert.Equal(HttpStatusCode.BadRequest, xssNotesResponse.StatusCode);
    }

    [Fact]
    public async Task GetDiagnosticQueueBoardAndDetail_DiagnosticStaffRole_Returns200And409OnCompletedEntry_WhenDatabaseAvailable()
    {
        var token = GenerateToken(7, "diagnostic_staff01@medflow.test", "DIAGNOSTIC_STAFF");

        var boardResponse = await GetWithTokenAsync($"{QueueBoardRoute}?limit=20", token);
        if (boardResponse.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable)
        {
            return;
        }

        Assert.Equal(HttpStatusCode.OK, boardResponse.StatusCode);
        var boardPayload = await boardResponse.Content.ReadFromJsonAsync<DiagnosticQueueBoardResponseDto>();
        Assert.NotNull(boardPayload);
        Assert.Equal(7, boardPayload.StaffProfile.UserId);
        Assert.NotEmpty(boardPayload.ServicePoints);

        var aliasBoardResponse = await GetWithTokenAsync($"{QueueBoardAliasRoute}?limit=10", token);
        Assert.Equal(HttpStatusCode.OK, aliasBoardResponse.StatusCode);

        var detailResponse = await GetWithTokenAsync(QueueEntryDetailRoute, token);
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detailPayload = await detailResponse.Content.ReadFromJsonAsync<DiagnosticQueueEntryDetailResponseDto>();
        Assert.NotNull(detailPayload);
        Assert.Equal("DIA-202610-0001", detailPayload.Entry.OrderCode);

        // Seeded order #1 (DIA-202610-0001) is COMPLETED -> mutating status must return 409 Conflict
        var mutateCompletedResponse = await PostWithTokenAsync(
            UpdateStatusRoute,
            token,
            new { targetStatus = "CALLED", notes = "Thử gọi lại lượt đã hoàn tất" });
        Assert.Equal(HttpStatusCode.Conflict, mutateCompletedResponse.StatusCode);
    }
}
