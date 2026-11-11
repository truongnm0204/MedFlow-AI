using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MedFlow.Application.DTOs.Diagnostic;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Entities;
using MedFlow.IntegrationTests.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace MedFlow.IntegrationTests.Diagnostic;

public sealed class DiagnosticPatientReceiveEndpointTests : AuthTestBase
{
    private const string ReceiveListRoute = "/api/v1/diagnostic/orders/receive";
    private const string ReceiveAliasRoute = "/api/v1/diagnostic-orders/receive";
    private const string ReceiveDetailRoute = "/api/v1/diagnostic/orders/DIA-202610-0001/receive";
    private const string ReceivePatientRoute = "/api/v1/diagnostic/orders/DIA-202610-0001/receive";
    private const string BatchReceiveRoute = "/api/v1/diagnostic/orders/receive";

    private readonly TestWebAppFactory _factory;

    public DiagnosticPatientReceiveEndpointTests(TestWebAppFactory factory)
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
    public async Task DiagnosticPatientReceiveEndpoints_Unauthenticated_Return401()
    {
        var listResponse = await Client.GetAsync(ReceiveListRoute);
        Assert.Equal(HttpStatusCode.Unauthorized, listResponse.StatusCode);

        var detailResponse = await Client.GetAsync(ReceiveDetailRoute);
        Assert.Equal(HttpStatusCode.Unauthorized, detailResponse.StatusCode);

        using var receiveRequest = new HttpRequestMessage(HttpMethod.Post, ReceivePatientRoute);
        var receiveResponse = await Client.SendAsync(receiveRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, receiveResponse.StatusCode);

        using var batchRequest = new HttpRequestMessage(HttpMethod.Post, BatchReceiveRoute);
        var batchResponse = await Client.SendAsync(batchRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, batchResponse.StatusCode);
    }

    [Theory]
    [InlineData("PATIENT")]
    [InlineData("DOCTOR")]
    [InlineData("RECEPTION")]
    [InlineData("PHARMACY_STAFF")]
    [InlineData("HOSPITAL_ADMIN")]
    [InlineData("SYSTEM_ADMIN")]
    public async Task DiagnosticPatientReceiveEndpoints_ForbiddenRoles_Return403(string role)
    {
        var token = GenerateToken(13, $"{role.ToLowerInvariant()}@medflow.test", role);

        var listResponse = await GetWithTokenAsync(ReceiveListRoute, token);
        Assert.Equal(HttpStatusCode.Forbidden, listResponse.StatusCode);

        var detailResponse = await GetWithTokenAsync(ReceiveDetailRoute, token);
        Assert.Equal(HttpStatusCode.Forbidden, detailResponse.StatusCode);

        var receiveResponse = await PostWithTokenAsync(
            ReceivePatientRoute,
            token,
            new { isPatientPresent = true, isIdentityVerified = true });
        Assert.Equal(HttpStatusCode.Forbidden, receiveResponse.StatusCode);

        var batchResponse = await PostWithTokenAsync(
            BatchReceiveRoute,
            token,
            new { orderCodes = new[] { "DIA-202610-0001" } });
        Assert.Equal(HttpStatusCode.Forbidden, batchResponse.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/diagnostic/orders/receive?limit=0")]
    [InlineData("/api/v1/diagnostic/orders/receive?limit=101")]
    [InlineData("/api/v1/diagnostic/orders/receive?category=INVALID_CATEGORY")]
    [InlineData("/api/v1/diagnostic/orders/receive?status=INVALID_STATUS")]
    [InlineData("/api/v1/diagnostic/orders/receive?queueStatus=INVALID_QUEUE_STATUS")]
    [InlineData("/api/v1/diagnostic/orders/receive?priority=INVALID_PRIORITY")]
    public async Task GetOrdersForReceive_InvalidQueryParameters_Return400(string url)
    {
        var token = GenerateToken(7, "diagnostic_staff01@medflow.test", "DIAGNOSTIC_STAFF");

        var response = await GetWithTokenAsync(url, token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ReceivePatient_InvalidPayloads_Return400()
    {
        var token = GenerateToken(7, "diagnostic_staff01@medflow.test", "DIAGNOSTIC_STAFF");

        var notPresentResponse = await PostWithTokenAsync(
            ReceivePatientRoute,
            token,
            new { isPatientPresent = false });
        Assert.Equal(HttpStatusCode.BadRequest, notPresentResponse.StatusCode);

        var unverifiedIdentityResponse = await PostWithTokenAsync(
            ReceivePatientRoute,
            token,
            new { isIdentityVerified = false });
        Assert.Equal(HttpStatusCode.BadRequest, unverifiedIdentityResponse.StatusCode);

        var invalidStateVersionResponse = await PostWithTokenAsync(
            ReceivePatientRoute,
            token,
            new { expectedStateVersion = 0 });
        Assert.Equal(HttpStatusCode.BadRequest, invalidStateVersionResponse.StatusCode);

        var xssNoteResponse = await PostWithTokenAsync(
            ReceivePatientRoute,
            token,
            new { receptionNote = "<script>alert(1)</script>" });
        Assert.Equal(HttpStatusCode.BadRequest, xssNoteResponse.StatusCode);

        var invalidManualPriorityResponse = await PostWithTokenAsync(
            ReceivePatientRoute,
            token,
            new { manualPriority = "INVALID_PRIORITY" });
        Assert.Equal(HttpStatusCode.BadRequest, invalidManualPriorityResponse.StatusCode);

        var emptyBatchResponse = await PostWithTokenAsync(
            BatchReceiveRoute,
            token,
            new { receptionNote = "Không có danh sách mã chỉ định" });
        Assert.Equal(HttpStatusCode.BadRequest, emptyBatchResponse.StatusCode);
    }

    [Fact]
    public async Task GetAndReceivePatient_DiagnosticStaffRole_Returns200And409OnCompletedSeededOrder_WhenDatabaseAvailable()
    {
        var token = GenerateToken(7, "diagnostic_staff01@medflow.test", "DIAGNOSTIC_STAFF");

        var listResponse = await GetWithTokenAsync($"{ReceiveListRoute}?limit=10", token);
        if (listResponse.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable)
        {
            return;
        }

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var listPayload = await listResponse.Content.ReadFromJsonAsync<DiagnosticPatientReceiveListResponseDto>();
        Assert.NotNull(listPayload);
        Assert.Equal(7, listPayload.StaffProfile.UserId);
        Assert.NotEmpty(listPayload.Items);

        var aliasListResponse = await GetWithTokenAsync($"{ReceiveAliasRoute}?limit=5", token);
        Assert.Equal(HttpStatusCode.OK, aliasListResponse.StatusCode);

        var detailResponse = await GetWithTokenAsync(ReceiveDetailRoute, token);
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detailPayload = await detailResponse.Content.ReadFromJsonAsync<DiagnosticPatientReceiveDetailResponseDto>();
        Assert.NotNull(detailPayload);
        Assert.Equal("DIA-202610-0001", detailPayload.Order.OrderCode);
        Assert.False(detailPayload.Order.CanReceive);

        // Seeded order #1 (DIA-202610-0001) is COMPLETED -> receiving again must return 409 Conflict
        var receiveCompletedResponse = await PostWithTokenAsync(
            ReceivePatientRoute,
            token,
            new { isPatientPresent = true, isIdentityVerified = true, receptionNote = "Kiểm tra tiếp nhận lại chỉ định đã hoàn tất" });
        Assert.Equal(HttpStatusCode.Conflict, receiveCompletedResponse.StatusCode);
    }
}
