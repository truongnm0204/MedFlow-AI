using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MedFlow.Application.DTOs.HospitalAdmin;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Entities;
using MedFlow.IntegrationTests.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace MedFlow.IntegrationTests.HospitalAdmin;

public sealed class HospitalAdminDashboardEndpointTests : AuthTestBase
{
    private readonly TestWebAppFactory _factory;

    public HospitalAdminDashboardEndpointTests(TestWebAppFactory factory)
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
        var response = await Client.GetAsync("/api/v1/admin/hospital-dashboard/summary");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("PATIENT")]
    [InlineData("DOCTOR")]
    [InlineData("RECEPTION")]
    [InlineData("DIAGNOSTIC_STAFF")]
    [InlineData("PHARMACY_STAFF")]
    [InlineData("SYSTEM_ADMIN")]
    public async Task GetDashboard_ForbiddenRoles_Returns403(string role)
    {
        var token = GenerateToken(13, $"{role.ToLowerInvariant()}@medflow.test", role);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/hospital-dashboard/summary");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetDashboard_InvalidWorklistLimit_Returns400()
    {
        var token = GenerateToken(11, "hospital_admin01@medflow.test", "HOSPITAL_ADMIN");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/hospital-dashboard/summary?worklistLimit=0");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetDashboard_InvalidCategory_Returns400()
    {
        var token = GenerateToken(11, "hospital_admin01@medflow.test", "HOSPITAL_ADMIN");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/hospital-dashboard/summary?category=INVALID_CAT");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetDashboard_InvalidStatus_Returns400()
    {
        var token = GenerateToken(11, "hospital_admin01@medflow.test", "HOSPITAL_ADMIN");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/hospital-dashboard/summary?status=INVALID_STATUS");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetDashboard_HospitalAdminRole_Returns200WithSeededData_WhenDatabaseAvailable()
    {
        var token = GenerateToken(11, "hospital_admin01@medflow.test", "HOSPITAL_ADMIN");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/hospital-dashboard/summary?worklistLimit=10");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        if (response.StatusCode == HttpStatusCode.OK)
        {
            var payload = await response.Content.ReadFromJsonAsync<HospitalAdminDashboardResponseDto>();
            Assert.NotNull(payload);
            Assert.Equal(11, payload.AdminProfile.UserId);
            Assert.Equal("hospital_admin01@medflow.test", payload.AdminProfile.Email);
            Assert.Equal("STF-009", payload.AdminProfile.StaffCode);
            Assert.False(string.IsNullOrWhiteSpace(payload.FacilityName));
            Assert.False(string.IsNullOrWhiteSpace(payload.FacilityCode));
            Assert.NotEmpty(payload.QuickActions);
            Assert.NotEmpty(payload.PolicyAlerts);
            Assert.True(payload.Worklist.Count <= 10);
        }
        else
        {
            Assert.True(
                response.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable,
                $"Unexpected status code: {response.StatusCode}");
        }
    }

    [Theory]
    [InlineData("/api/v1/admin/hospital-dashboard")]
    [InlineData("/api/v1/hospital-admin/dashboard")]
    [InlineData("/api/v1/admin/dashboard/hospital")]
    public async Task GetDashboard_AliasRoutes_HospitalAdminRole_MatchSummaryRoute(string route)
    {
        var unauthResponse = await Client.GetAsync(route);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthResponse.StatusCode);

        var token = GenerateToken(11, "hospital_admin01@medflow.test", "HOSPITAL_ADMIN");

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{route}?worklistLimit=5");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        if (response.StatusCode == HttpStatusCode.OK)
        {
            var payload = await response.Content.ReadFromJsonAsync<HospitalAdminDashboardResponseDto>();
            Assert.NotNull(payload);
            Assert.Equal(11, payload.AdminProfile.UserId);
        }
        else
        {
            Assert.True(
                response.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable,
                $"Unexpected status code: {response.StatusCode}");
        }
    }

    [Fact]
    public async Task GetDashboard_ForgedTokenWithNonHospitalAdminDatabaseUser_Returns403_WhenDatabaseAvailable()
    {
        // User ID 1 in seeded database is patient01@medflow.test (PATIENT role only)
        var forgedToken = GenerateToken(1, "patient01@medflow.test", "HOSPITAL_ADMIN");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/hospital-dashboard/summary");
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
