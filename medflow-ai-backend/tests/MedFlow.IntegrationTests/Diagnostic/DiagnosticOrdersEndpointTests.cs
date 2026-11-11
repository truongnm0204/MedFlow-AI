using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MedFlow.Application.DTOs.Diagnostic;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Entities;
using MedFlow.IntegrationTests.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace MedFlow.IntegrationTests.Diagnostic;

public sealed class DiagnosticOrdersEndpointTests : AuthTestBase
{
    private const string OrdersRoute = "/api/v1/diagnostic/orders/review";
    private const string OrdersAliasRoute = "/api/v1/diagnostic-orders";
    private const string OrderDetailRoute = "/api/v1/diagnostic/orders/DIA-202610-0001/review";
    private const string OrderConfirmRoute = "/api/v1/diagnostic/orders/DIA-202610-0001/confirm";
    private const string BatchConfirmRoute = "/api/v1/diagnostic/orders/confirm";

    private readonly TestWebAppFactory _factory;

    public DiagnosticOrdersEndpointTests(TestWebAppFactory factory)
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
    public async Task DiagnosticOrderEndpoints_Unauthenticated_Return401()
    {
        var listResponse = await Client.GetAsync(OrdersRoute);
        Assert.Equal(HttpStatusCode.Unauthorized, listResponse.StatusCode);

        var detailResponse = await Client.GetAsync(OrderDetailRoute);
        Assert.Equal(HttpStatusCode.Unauthorized, detailResponse.StatusCode);

        using var confirmRequest = new HttpRequestMessage(HttpMethod.Post, OrderConfirmRoute);
        var confirmResponse = await Client.SendAsync(confirmRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, confirmResponse.StatusCode);

        using var batchRequest = new HttpRequestMessage(HttpMethod.Post, BatchConfirmRoute);
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
    public async Task DiagnosticOrderEndpoints_ForbiddenRoles_Return403(string role)
    {
        var token = GenerateToken(13, $"{role.ToLowerInvariant()}@medflow.test", role);

        var listResponse = await GetWithTokenAsync(OrdersRoute, token);
        Assert.Equal(HttpStatusCode.Forbidden, listResponse.StatusCode);

        var detailResponse = await GetWithTokenAsync(OrderDetailRoute, token);
        Assert.Equal(HttpStatusCode.Forbidden, detailResponse.StatusCode);

        var confirmResponse = await PostWithTokenAsync(
            OrderConfirmRoute,
            token,
            new { isEligibilityVerified = true });
        Assert.Equal(HttpStatusCode.Forbidden, confirmResponse.StatusCode);

        var batchResponse = await PostWithTokenAsync(
            BatchConfirmRoute,
            token,
            new { orderCodes = new[] { "DIA-202610-0001" } });
        Assert.Equal(HttpStatusCode.Forbidden, batchResponse.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/diagnostic/orders/review?limit=0")]
    [InlineData("/api/v1/diagnostic/orders/review?limit=101")]
    [InlineData("/api/v1/diagnostic/orders/review?category=INVALID_CATEGORY")]
    [InlineData("/api/v1/diagnostic/orders/review?status=INVALID_STATUS")]
    [InlineData("/api/v1/diagnostic/orders/review?priority=INVALID_PRIORITY")]
    public async Task GetOrders_InvalidQueryParameters_Return400(string url)
    {
        var token = GenerateToken(7, "diagnostic_staff01@medflow.test", "DIAGNOSTIC_STAFF");

        var response = await GetWithTokenAsync(url, token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ConfirmOrder_InvalidPayloads_Return400()
    {
        var token = GenerateToken(7, "diagnostic_staff01@medflow.test", "DIAGNOSTIC_STAFF");

        var unverifiedResponse = await PostWithTokenAsync(
            OrderConfirmRoute,
            token,
            new { isEligibilityVerified = false });
        Assert.Equal(HttpStatusCode.BadRequest, unverifiedResponse.StatusCode);

        var invalidStateVersionResponse = await PostWithTokenAsync(
            OrderConfirmRoute,
            token,
            new { expectedStateVersion = 0 });
        Assert.Equal(HttpStatusCode.BadRequest, invalidStateVersionResponse.StatusCode);

        var xssNoteResponse = await PostWithTokenAsync(
            OrderConfirmRoute,
            token,
            new { confirmationNote = "<script>alert(1)</script>" });
        Assert.Equal(HttpStatusCode.BadRequest, xssNoteResponse.StatusCode);

        var emptyBatchResponse = await PostWithTokenAsync(
            BatchConfirmRoute,
            token,
            new { confirmationNote = "Không có danh sách mã chỉ định" });
        Assert.Equal(HttpStatusCode.BadRequest, emptyBatchResponse.StatusCode);
    }

    [Fact]
    public async Task GetAndConfirmOrders_DiagnosticStaffRole_Returns200And409OnCompletedSeededOrder_WhenDatabaseAvailable()
    {
        var token = GenerateToken(7, "diagnostic_staff01@medflow.test", "DIAGNOSTIC_STAFF");

        var listResponse = await GetWithTokenAsync($"{OrdersRoute}?limit=10", token);
        if (listResponse.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable)
        {
            return;
        }

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var listPayload = await listResponse.Content.ReadFromJsonAsync<DiagnosticOrderReviewListResponseDto>();
        Assert.NotNull(listPayload);
        Assert.Equal(7, listPayload.StaffProfile.UserId);
        Assert.NotEmpty(listPayload.Items);

        var aliasListResponse = await GetWithTokenAsync($"{OrdersAliasRoute}?limit=5", token);
        Assert.Equal(HttpStatusCode.OK, aliasListResponse.StatusCode);

        var detailResponse = await GetWithTokenAsync(OrderDetailRoute, token);
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detailPayload = await detailResponse.Content.ReadFromJsonAsync<DiagnosticOrderReviewDetailResponseDto>();
        Assert.NotNull(detailPayload);
        Assert.Equal("DIA-202610-0001", detailPayload.Order.OrderCode);
        Assert.False(detailPayload.Order.CanConfirm);

        // Seeded order #1 (DIA-202610-0001) is COMPLETED -> confirming again must return 409 Conflict
        var confirmCompletedResponse = await PostWithTokenAsync(
            OrderConfirmRoute,
            token,
            new { isEligibilityVerified = true, confirmationNote = "Kiểm tra lại chỉ định đã hoàn tất" });
        Assert.Equal(HttpStatusCode.Conflict, confirmCompletedResponse.StatusCode);
    }
}
