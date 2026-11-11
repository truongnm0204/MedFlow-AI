using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MedFlow.Application.DTOs.Diagnostic;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Entities;
using MedFlow.IntegrationTests.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace MedFlow.IntegrationTests.Diagnostic;

public sealed class DiagnosticServiceExecutionEndpointTests : AuthTestBase
{
    private const string ListRoute = "/api/v1/diagnostic/orders/execution";
    private const string ListAliasRoute = "/api/v1/diagnostic-orders/execution-status";
    private const string DetailRoute = "/api/v1/diagnostic/orders/DIA-202610-0001/execution";
    private const string UpdateRoute = "/api/v1/diagnostic/orders/DIA-202610-0001/execution-status";
    private const string StartRoute = "/api/v1/diagnostic/orders/DIA-202610-0001/start";
    private const string CompleteRoute = "/api/v1/diagnostic/orders/DIA-202610-0001/complete";
    private const string BatchRoute = "/api/v1/diagnostic/orders/execution-status";

    private readonly TestWebAppFactory _factory;

    public DiagnosticServiceExecutionEndpointTests(TestWebAppFactory factory)
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

    private async Task<HttpResponseMessage> PutWithTokenAsync<TBody>(string url, string token, TBody body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, url)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await Client.SendAsync(request);
    }

    [Fact]
    public async Task ExecutionEndpoints_Unauthenticated_Return401()
    {
        var listResponse = await Client.GetAsync(ListRoute);
        Assert.Equal(HttpStatusCode.Unauthorized, listResponse.StatusCode);

        var detailResponse = await Client.GetAsync(DetailRoute);
        Assert.Equal(HttpStatusCode.Unauthorized, detailResponse.StatusCode);

        var updateResponse = await PutWithTokenAsync(UpdateRoute, string.Empty, new { targetStatus = "IN_PROGRESS" });
        Assert.Equal(HttpStatusCode.Unauthorized, updateResponse.StatusCode);

        var startResponse = await PostWithTokenAsync(StartRoute, string.Empty, new { });
        Assert.Equal(HttpStatusCode.Unauthorized, startResponse.StatusCode);

        var completeResponse = await PostWithTokenAsync(CompleteRoute, string.Empty, new { });
        Assert.Equal(HttpStatusCode.Unauthorized, completeResponse.StatusCode);

        var batchResponse = await PostWithTokenAsync(BatchRoute, string.Empty, new { targetStatus = "IN_PROGRESS" });
        Assert.Equal(HttpStatusCode.Unauthorized, batchResponse.StatusCode);
    }

    [Theory]
    [InlineData("PATIENT")]
    [InlineData("DOCTOR")]
    [InlineData("RECEPTION")]
    [InlineData("PHARMACY_STAFF")]
    [InlineData("HOSPITAL_ADMIN")]
    [InlineData("SYSTEM_ADMIN")]
    public async Task ExecutionEndpoints_ForbiddenRoles_Return403(string role)
    {
        var token = GenerateToken(13, $"{role.ToLowerInvariant()}@medflow.test", role);

        var listResponse = await GetWithTokenAsync(ListRoute, token);
        Assert.Equal(HttpStatusCode.Forbidden, listResponse.StatusCode);

        var detailResponse = await GetWithTokenAsync(DetailRoute, token);
        Assert.Equal(HttpStatusCode.Forbidden, detailResponse.StatusCode);

        var updateResponse = await PutWithTokenAsync(UpdateRoute, token, new { targetStatus = "IN_PROGRESS" });
        Assert.Equal(HttpStatusCode.Forbidden, updateResponse.StatusCode);

        var batchResponse = await PostWithTokenAsync(BatchRoute, token, new { targetStatus = "IN_PROGRESS" });
        Assert.Equal(HttpStatusCode.Forbidden, batchResponse.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/diagnostic/orders/execution?limit=0")]
    [InlineData("/api/v1/diagnostic/orders/execution?limit=101")]
    [InlineData("/api/v1/diagnostic/orders/execution?category=INVALID_CATEGORY")]
    [InlineData("/api/v1/diagnostic/orders/execution?status=INVALID_STATUS")]
    [InlineData("/api/v1/diagnostic/orders/execution?queueStatus=INVALID_QUEUE_STATUS")]
    [InlineData("/api/v1/diagnostic/orders/execution?priority=INVALID_PRIORITY")]
    public async Task GetExecutions_InvalidQueryParameters_Return400(string url)
    {
        var token = GenerateToken(7, "diagnostic_staff01@medflow.test", "DIAGNOSTIC_STAFF");

        var response = await GetWithTokenAsync(url, token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateExecution_InvalidPayloads_Return400()
    {
        var token = GenerateToken(7, "diagnostic_staff01@medflow.test", "DIAGNOSTIC_STAFF");

        var missingTarget = await PutWithTokenAsync(UpdateRoute, token, new { });
        Assert.Equal(HttpStatusCode.BadRequest, missingTarget.StatusCode);

        var invalidTarget = await PutWithTokenAsync(UpdateRoute, token, new { targetStatus = "BOGUS_STATUS" });
        Assert.Equal(HttpStatusCode.BadRequest, invalidTarget.StatusCode);

        var invalidStateVersion = await PutWithTokenAsync(UpdateRoute, token, new { targetStatus = "IN_PROGRESS", expectedStateVersion = 0 });
        Assert.Equal(HttpStatusCode.BadRequest, invalidStateVersion.StatusCode);

        var xssNote = await PutWithTokenAsync(UpdateRoute, token, new { targetStatus = "IN_PROGRESS", executionNote = "<script>alert(1)</script>" });
        Assert.Equal(HttpStatusCode.BadRequest, xssNote.StatusCode);

        var xssSpecimen = await PutWithTokenAsync(UpdateRoute, token, new { targetStatus = "IN_PROGRESS", specimenCode = "<b>" });
        Assert.Equal(HttpStatusCode.BadRequest, xssSpecimen.StatusCode);

        var missingCancelReason = await PutWithTokenAsync(UpdateRoute, token, new { targetStatus = "CANCELLED" });
        Assert.Equal(HttpStatusCode.BadRequest, missingCancelReason.StatusCode);

        var emptyBatch = await PostWithTokenAsync(BatchRoute, token, new { targetStatus = "IN_PROGRESS" });
        var emptyBatchBody = await emptyBatch.Content.ReadAsStringAsync();
        Assert.True(emptyBatch.StatusCode == HttpStatusCode.BadRequest, $"Expected 400, got {(int)emptyBatch.StatusCode}: {emptyBatchBody}");

        var conflictingStart = await PostWithTokenAsync(StartRoute, token, new { targetStatus = "COMPLETED" });
        Assert.Equal(HttpStatusCode.BadRequest, conflictingStart.StatusCode);
    }

    [Fact]
    public async Task GetAndUpdateExecution_DiagnosticStaffRole_Returns200And409OnCompletedSeededOrder_WhenDatabaseAvailable()
    {
        var token = GenerateToken(7, "diagnostic_staff01@medflow.test", "DIAGNOSTIC_STAFF");

        var listResponse = await GetWithTokenAsync($"{ListRoute}?limit=10", token);
        if (listResponse.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable)
        {
            return;
        }

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var listPayload = await listResponse.Content.ReadFromJsonAsync<DiagnosticServiceExecutionListResponseDto>();
        Assert.NotNull(listPayload);
        Assert.Equal(7, listPayload.StaffProfile.UserId);

        var aliasListResponse = await GetWithTokenAsync($"{ListAliasRoute}?limit=5", token);
        Assert.Equal(HttpStatusCode.OK, aliasListResponse.StatusCode);

        var detailResponse = await GetWithTokenAsync(DetailRoute, token);
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detailPayload = await detailResponse.Content.ReadFromJsonAsync<DiagnosticServiceExecutionDetailResponseDto>();
        Assert.NotNull(detailPayload);
        Assert.Equal("DIA-202610-0001", detailPayload.Order.OrderCode);

        // Seeded order #1 (DIA-202610-0001) is COMPLETED -> any transition must return 409 Conflict
        var startCompleted = await PostWithTokenAsync(StartRoute, token, new { executionNote = "retry start on completed" });
        Assert.Equal(HttpStatusCode.Conflict, startCompleted.StatusCode);

        var completeCompleted = await PostWithTokenAsync(CompleteRoute, token, new { executionNote = "retry complete on completed" });
        Assert.Equal(HttpStatusCode.Conflict, completeCompleted.StatusCode);
    }
}
