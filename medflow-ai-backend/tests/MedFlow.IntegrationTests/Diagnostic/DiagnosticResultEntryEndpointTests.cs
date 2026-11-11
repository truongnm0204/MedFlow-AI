using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MedFlow.Application.DTOs.Diagnostic;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Entities;
using MedFlow.IntegrationTests.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace MedFlow.IntegrationTests.Diagnostic;

public sealed class DiagnosticResultEntryEndpointTests : AuthTestBase
{
    private const string ResultsListRoute = "/api/v1/diagnostic/results";
    private const string ResultsListAliasRoute = "/api/v1/diagnostic/orders/results";
    private const string ResultDetailRoute = "/api/v1/diagnostic/results/DIA-202610-0001";
    private const string OrderResultRoute = "/api/v1/diagnostic/orders/DIA-202610-0001/result";
    private const string BodyEnterResultRoute = "/api/v1/diagnostic/results";

    private readonly TestWebAppFactory _factory;

    public DiagnosticResultEntryEndpointTests(TestWebAppFactory factory)
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
    public async Task DiagnosticResultEndpoints_Unauthenticated_Return401()
    {
        var listResponse = await Client.GetAsync(ResultsListRoute);
        Assert.Equal(HttpStatusCode.Unauthorized, listResponse.StatusCode);

        var detailResponse = await Client.GetAsync(ResultDetailRoute);
        Assert.Equal(HttpStatusCode.Unauthorized, detailResponse.StatusCode);

        using var postOrderRequest = new HttpRequestMessage(HttpMethod.Post, OrderResultRoute);
        var postOrderResponse = await Client.SendAsync(postOrderRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, postOrderResponse.StatusCode);

        using var postBodyRequest = new HttpRequestMessage(HttpMethod.Post, BodyEnterResultRoute);
        var postBodyResponse = await Client.SendAsync(postBodyRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, postBodyResponse.StatusCode);
    }

    [Theory]
    [InlineData("PATIENT")]
    [InlineData("DOCTOR")]
    [InlineData("RECEPTION")]
    [InlineData("PHARMACY_STAFF")]
    [InlineData("HOSPITAL_ADMIN")]
    [InlineData("SYSTEM_ADMIN")]
    public async Task DiagnosticResultEndpoints_ForbiddenRoles_Return403(string role)
    {
        var token = GenerateToken(13, $"{role.ToLowerInvariant()}@medflow.test", role);

        var listResponse = await GetWithTokenAsync(ResultsListRoute, token);
        Assert.Equal(HttpStatusCode.Forbidden, listResponse.StatusCode);

        var detailResponse = await GetWithTokenAsync(ResultDetailRoute, token);
        Assert.Equal(HttpStatusCode.Forbidden, detailResponse.StatusCode);

        var postOrderResponse = await PostWithTokenAsync(
            OrderResultRoute,
            token,
            new
            {
                resultText = "WBC 7.5 G/L; HGB 140 g/L.",
                conclusion = "Công thức máu bình thường.",
            });
        Assert.Equal(HttpStatusCode.Forbidden, postOrderResponse.StatusCode);

        var postBodyResponse = await PostWithTokenAsync(
            BodyEnterResultRoute,
            token,
            new
            {
                orderCode = "DIA-202610-0001",
                resultText = "WBC 7.5 G/L; HGB 140 g/L.",
                conclusion = "Công thức máu bình thường.",
            });
        Assert.Equal(HttpStatusCode.Forbidden, postBodyResponse.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/diagnostic/results?limit=0")]
    [InlineData("/api/v1/diagnostic/results?limit=101")]
    [InlineData("/api/v1/diagnostic/results?category=INVALID_CATEGORY")]
    [InlineData("/api/v1/diagnostic/results?status=INVALID_STATUS")]
    [InlineData("/api/v1/diagnostic/results?resultStatus=INVALID_RESULT_STATUS")]
    [InlineData("/api/v1/diagnostic/results?queueStatus=INVALID_QUEUE_STATUS")]
    [InlineData("/api/v1/diagnostic/results?priority=INVALID_PRIORITY")]
    public async Task GetOrdersForResultEntry_InvalidQueryParameters_Return400(string url)
    {
        var token = GenerateToken(7, "diagnostic_staff01@medflow.test", "DIAGNOSTIC_STAFF");

        var response = await GetWithTokenAsync(url, token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task EnterResult_InvalidPayloads_Return400()
    {
        var token = GenerateToken(7, "diagnostic_staff01@medflow.test", "DIAGNOSTIC_STAFF");

        var emptyResultTextResponse = await PostWithTokenAsync(
            OrderResultRoute,
            token,
            new { resultText = "", conclusion = "Kết luận hợp lệ." });
        Assert.Equal(HttpStatusCode.BadRequest, emptyResultTextResponse.StatusCode);

        var emptyConclusionResponse = await PostWithTokenAsync(
            OrderResultRoute,
            token,
            new { resultText = "Kết quả xét nghiệm hợp lệ.", conclusion = "" });
        Assert.Equal(HttpStatusCode.BadRequest, emptyConclusionResponse.StatusCode);

        var xssResultTextResponse = await PostWithTokenAsync(
            OrderResultRoute,
            token,
            new { resultText = "<script>alert(1)</script>", conclusion = "Kết luận hợp lệ." });
        Assert.Equal(HttpStatusCode.BadRequest, xssResultTextResponse.StatusCode);

        var xssConclusionResponse = await PostWithTokenAsync(
            OrderResultRoute,
            token,
            new { resultText = "Kết quả xét nghiệm hợp lệ.", conclusion = "<script>alert(1)</script>" });
        Assert.Equal(HttpStatusCode.BadRequest, xssConclusionResponse.StatusCode);

        var invalidReportUrlResponse = await PostWithTokenAsync(
            OrderResultRoute,
            token,
            new
            {
                resultText = "Kết quả xét nghiệm hợp lệ.",
                conclusion = "Kết luận hợp lệ.",
                reportFileUrl = "ftp://invalid-url/report.pdf",
            });
        Assert.Equal(HttpStatusCode.BadRequest, invalidReportUrlResponse.StatusCode);

        var invalidStateVersionResponse = await PostWithTokenAsync(
            OrderResultRoute,
            token,
            new
            {
                resultText = "Kết quả xét nghiệm hợp lệ.",
                conclusion = "Kết luận hợp lệ.",
                expectedStateVersion = 0,
            });
        Assert.Equal(HttpStatusCode.BadRequest, invalidStateVersionResponse.StatusCode);

        var conflictingFlagsResponse = await PostWithTokenAsync(
            OrderResultRoute,
            token,
            new
            {
                resultText = "Kết quả xét nghiệm hợp lệ.",
                conclusion = "Kết luận hợp lệ.",
                status = "DRAFT",
                isFinalized = true,
            });
        Assert.Equal(HttpStatusCode.BadRequest, conflictingFlagsResponse.StatusCode);

        var missingTargetInBodyResponse = await PostWithTokenAsync(
            BodyEnterResultRoute,
            token,
            new
            {
                resultText = "Kết quả xét nghiệm hợp lệ.",
                conclusion = "Kết luận hợp lệ.",
            });
        Assert.Equal(HttpStatusCode.BadRequest, missingTargetInBodyResponse.StatusCode);
    }

    [Fact]
    public async Task GetAndEnterResult_DiagnosticStaffRole_Returns200And409OnFinalizedSeededOrder_WhenDatabaseAvailable()
    {
        var token = GenerateToken(7, "diagnostic_staff01@medflow.test", "DIAGNOSTIC_STAFF");

        var listResponse = await GetWithTokenAsync($"{ResultsListRoute}?limit=10", token);
        if (listResponse.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable)
        {
            return;
        }

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var listPayload = await listResponse.Content.ReadFromJsonAsync<DiagnosticResultEntryListResponseDto>();
        Assert.NotNull(listPayload);
        Assert.Equal(7, listPayload.StaffProfile.UserId);
        Assert.NotEmpty(listPayload.Items);

        var aliasListResponse = await GetWithTokenAsync($"{ResultsListAliasRoute}?limit=5", token);
        Assert.Equal(HttpStatusCode.OK, aliasListResponse.StatusCode);

        var detailResponse = await GetWithTokenAsync(ResultDetailRoute, token);
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detailPayload = await detailResponse.Content.ReadFromJsonAsync<DiagnosticResultEntryDetailResponseDto>();
        Assert.NotNull(detailPayload);
        Assert.Equal("DIA-202610-0001", detailPayload.Order.OrderCode);
        Assert.True(detailPayload.Order.IsResultFinalized);
        Assert.False(detailPayload.Order.CanEnterResult);

        // Seeded order #1 (DIA-202610-0001) already has a finalized result -> entering again must return 409 Conflict
        var enterFinalizedResponse = await PostWithTokenAsync(
            OrderResultRoute,
            token,
            new
            {
                resultText = "WBC: 8.0 G/L; RBC: 4.5 T/L; HGB: 140 g/L.",
                conclusion = "Công thức máu bình thường.",
            });
        Assert.Equal(HttpStatusCode.Conflict, enterFinalizedResponse.StatusCode);
    }
}
