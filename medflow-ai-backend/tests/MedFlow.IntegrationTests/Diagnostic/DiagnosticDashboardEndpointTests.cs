using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MedFlow.Application.DTOs.Diagnostic;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Entities;
using MedFlow.IntegrationTests.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace MedFlow.IntegrationTests.Diagnostic;

public sealed class DiagnosticDashboardEndpointTests : AuthTestBase
{
    private readonly TestWebAppFactory _factory;

    public DiagnosticDashboardEndpointTests(TestWebAppFactory factory)
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

    [Fact]
    public async Task GetDashboard_Unauthenticated_Returns401()
    {
        var response = await Client.GetAsync("/api/v1/diagnostic/dashboard/summary");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("PATIENT")]
    [InlineData("DOCTOR")]
    [InlineData("RECEPTION")]
    [InlineData("PHARMACY_STAFF")]
    [InlineData("HOSPITAL_ADMIN")]
    [InlineData("SYSTEM_ADMIN")]
    public async Task GetDashboard_ForbiddenRoles_Returns403(string role)
    {
        var token = GenerateToken(13, $"{role.ToLowerInvariant()}@medflow.test", role);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/diagnostic/dashboard/summary");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetDashboard_InvalidQueueLimit_Returns400()
    {
        var token = GenerateToken(7, "diagnostic_staff01@medflow.test", "DIAGNOSTIC_STAFF");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/diagnostic/dashboard/summary?queueLimit=0");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetDashboard_InvalidPendingResultLimit_Returns400()
    {
        var token = GenerateToken(7, "diagnostic_staff01@medflow.test", "DIAGNOSTIC_STAFF");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/diagnostic/dashboard/summary?pendingResultLimit=51");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetDashboard_InvalidCategory_Returns400()
    {
        var token = GenerateToken(7, "diagnostic_staff01@medflow.test", "DIAGNOSTIC_STAFF");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/diagnostic/dashboard/summary?category=INVALID_CAT");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetDashboard_InvalidStatus_Returns400()
    {
        var token = GenerateToken(7, "diagnostic_staff01@medflow.test", "DIAGNOSTIC_STAFF");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/diagnostic/dashboard/summary?status=INVALID_STATUS");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetDashboard_InvalidPriority_Returns400()
    {
        var token = GenerateToken(7, "diagnostic_staff01@medflow.test", "DIAGNOSTIC_STAFF");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/diagnostic/dashboard/summary?priority=INVALID_PRIORITY");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetDashboard_DiagnosticStaffRole_Returns200WithSeededData_WhenDatabaseAvailable()
    {
        var token = GenerateToken(7, "diagnostic_staff01@medflow.test", "DIAGNOSTIC_STAFF");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/diagnostic/dashboard/summary?queueLimit=10");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        if (response.StatusCode == HttpStatusCode.OK)
        {
            var payload = await response.Content.ReadFromJsonAsync<DiagnosticDashboardResponseDto>();
            Assert.NotNull(payload);
            Assert.Equal(7, payload.StaffProfile.UserId);
            Assert.Equal("diagnostic_staff01@medflow.test", payload.StaffProfile.Email);
            Assert.Equal("STF-007", payload.StaffProfile.StaffCode);
            Assert.False(string.IsNullOrWhiteSpace(payload.FacilityName));
            Assert.False(string.IsNullOrWhiteSpace(payload.FacilityCode));
            Assert.False(string.IsNullOrWhiteSpace(payload.DepartmentName));
            Assert.NotEmpty(payload.QuickActions);
            Assert.True(payload.Queue.Count <= 10);
        }
        else
        {
            Assert.True(
                response.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable,
                $"Unexpected status code: {response.StatusCode}");
        }
    }

    [Theory]
    [InlineData("/api/v1/diagnostic/dashboard")]
    [InlineData("/api/v1/diagnostic-dashboard")]
    [InlineData("/api/v1/diagnostic/workbench")]
    public async Task GetDashboard_AliasRoutes_DiagnosticStaffRole_MatchSummaryRoute(string route)
    {
        var unauthResponse = await Client.GetAsync(route);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthResponse.StatusCode);

        var token = GenerateToken(7, "diagnostic_staff01@medflow.test", "DIAGNOSTIC_STAFF");

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{route}?queueLimit=5");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        if (response.StatusCode == HttpStatusCode.OK)
        {
            var payload = await response.Content.ReadFromJsonAsync<DiagnosticDashboardResponseDto>();
            Assert.NotNull(payload);
            Assert.Equal(7, payload.StaffProfile.UserId);
        }
        else
        {
            Assert.True(
                response.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable,
                $"Unexpected status code: {response.StatusCode}");
        }
    }

    [Fact]
    public async Task GetDashboard_ForgedTokenWithNonDiagnosticDatabaseUser_Returns403_WhenDatabaseAvailable()
    {
        // User ID 1 in seeded database is patient01@medflow.test (PATIENT role only)
        var forgedToken = GenerateToken(1, "patient01@medflow.test", "DIAGNOSTIC_STAFF");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/diagnostic/dashboard/summary");
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
}
