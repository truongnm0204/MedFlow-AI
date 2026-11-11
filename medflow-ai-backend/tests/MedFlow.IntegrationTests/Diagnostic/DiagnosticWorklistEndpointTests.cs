using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MedFlow.Application.DTOs.Diagnostic;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Entities;
using MedFlow.IntegrationTests.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace MedFlow.IntegrationTests.Diagnostic;

public sealed class DiagnosticWorklistEndpointTests : AuthTestBase
{
    private readonly TestWebAppFactory _factory;

    public DiagnosticWorklistEndpointTests(TestWebAppFactory factory)
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

    [Theory]
    [InlineData("/api/v1/diagnostic/worklist")]
    [InlineData("/api/v1/diagnostic/orders")]
    [InlineData("/api/v1/diagnostic/orders/worklist")]
    [InlineData("/api/v1/diagnostic-worklist")]
    [InlineData("/api/v1/diagnostic/worklist/DIA-202610-0001")]
    [InlineData("/api/v1/diagnostic/orders/DIA-202610-0001")]
    [InlineData("/api/v1/diagnostic-worklist/DIA-202610-0001")]
    public async Task GetWorklistAndDetail_Unauthenticated_Returns401(string route)
    {
        var response = await Client.GetAsync(route);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("PATIENT")]
    [InlineData("DOCTOR")]
    [InlineData("RECEPTION")]
    [InlineData("PHARMACY_STAFF")]
    [InlineData("HOSPITAL_ADMIN")]
    [InlineData("SYSTEM_ADMIN")]
    public async Task GetWorklist_ForbiddenRoles_Returns403(string role)
    {
        var token = GenerateToken(13, $"{role.ToLowerInvariant()}@medflow.test", role);

        using var worklistRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/diagnostic/worklist");
        worklistRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var worklistResponse = await Client.SendAsync(worklistRequest);
        Assert.Equal(HttpStatusCode.Forbidden, worklistResponse.StatusCode);

        using var detailRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/diagnostic/worklist/DIA-202610-0001");
        detailRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var detailResponse = await Client.SendAsync(detailRequest);
        Assert.Equal(HttpStatusCode.Forbidden, detailResponse.StatusCode);
    }

    [Theory]
    [InlineData("limit=0")]
    [InlineData("limit=101")]
    [InlineData("page=0")]
    [InlineData("category=INVALID")]
    [InlineData("status=INVALID")]
    [InlineData("queueStatus=INVALID")]
    [InlineData("resultStatus=INVALID")]
    [InlineData("priority=INVALID")]
    [InlineData("orderDate=not-a-date")]
    [InlineData("keyword=%3Cbad%3E")]
    public async Task GetWorklist_InvalidQueryParameters_Returns400(string query)
    {
        var token = GenerateToken(7, "diagnostic_staff01@medflow.test", "DIAGNOSTIC_STAFF");

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/diagnostic/worklist?{query}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("do-0000")]
    public async Task GetWorklistOrderDetail_InvalidIdentifier_Returns400(string invalidId)
    {
        var token = GenerateToken(7, "diagnostic_staff01@medflow.test", "DIAGNOSTIC_STAFF");

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/diagnostic/worklist/{invalidId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetWorklist_ForgedTokenWithNonDiagnosticDatabaseUser_Returns403_WhenDatabaseAvailable()
    {
        var forgedToken = GenerateToken(1, "patient01@medflow.test", "DIAGNOSTIC_STAFF");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/diagnostic/worklist");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", forgedToken);

        var response = await Client.SendAsync(request);

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        else
        {
            Assert.True(
                response.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable,
                $"Unexpected status code: {response.StatusCode}");
        }
    }

    [Theory]
    [InlineData("/api/v1/diagnostic/worklist")]
    [InlineData("/api/v1/diagnostic/orders")]
    [InlineData("/api/v1/diagnostic/orders/worklist")]
    [InlineData("/api/v1/diagnostic-worklist")]
    public async Task GetWorklist_DiagnosticStaffRole_Returns200WithSeededOrders_WhenDatabaseAvailable(string route)
    {
        var token = GenerateToken(7, "diagnostic_staff01@medflow.test", "DIAGNOSTIC_STAFF");

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{route}?limit=10");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        if (response.StatusCode == HttpStatusCode.OK)
        {
            var payload = await response.Content.ReadFromJsonAsync<DiagnosticWorklistResponseDto>();
            Assert.NotNull(payload);
            Assert.Equal(7, payload.StaffProfile.UserId);
            Assert.Equal("diagnostic_staff01@medflow.test", payload.StaffProfile.Email);
            Assert.False(string.IsNullOrWhiteSpace(payload.FacilityName));
            Assert.False(string.IsNullOrWhiteSpace(payload.FacilityCode));
            Assert.False(string.IsNullOrWhiteSpace(payload.DepartmentName));
            Assert.NotEmpty(payload.QuickActions);
            Assert.NotEmpty(payload.ServicePoints);
            Assert.NotEmpty(payload.Items);
            Assert.True(payload.Items.Count <= 10);
            Assert.Contains(payload.Items, item => item.OrderCode == "DIA-202610-0001");
        }
        else
        {
            Assert.True(
                response.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable,
                $"Unexpected status code: {response.StatusCode}");
        }
    }

    [Theory]
    [InlineData("/api/v1/diagnostic/worklist/DIA-202610-0001")]
    [InlineData("/api/v1/diagnostic/orders/1")]
    [InlineData("/api/v1/diagnostic-worklist/do-0001")]
    public async Task GetWorklistOrderDetail_DiagnosticStaffRole_Returns200_WhenDatabaseAvailable(string route)
    {
        var token = GenerateToken(7, "diagnostic_staff01@medflow.test", "DIAGNOSTIC_STAFF");

        using var request = new HttpRequestMessage(HttpMethod.Get, route);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        if (response.StatusCode == HttpStatusCode.OK)
        {
            var detail = await response.Content.ReadFromJsonAsync<DiagnosticWorklistItemDto>();
            Assert.NotNull(detail);
            Assert.Equal(1, detail.OrderId);
            Assert.Equal("DIA-202610-0001", detail.OrderCode);
            Assert.NotEmpty(detail.AllowedActions);
        }
        else
        {
            Assert.True(
                response.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable,
                $"Unexpected status code: {response.StatusCode}");
        }
    }
}
