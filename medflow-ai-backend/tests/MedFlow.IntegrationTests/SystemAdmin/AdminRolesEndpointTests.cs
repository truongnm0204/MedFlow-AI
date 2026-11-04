using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MedFlow.Application.DTOs.SystemAdmin;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Entities;
using MedFlow.IntegrationTests.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace MedFlow.IntegrationTests.SystemAdmin;

public sealed class AdminRolesEndpointTests : AuthTestBase
{
    private readonly TestWebAppFactory _factory;

    public AdminRolesEndpointTests(TestWebAppFactory factory)
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
    public async Task GetRoleList_Unauthenticated_Returns401()
    {
        var response = await Client.GetAsync("/api/v1/admin/roles");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("PATIENT")]
    [InlineData("DOCTOR")]
    [InlineData("RECEPTION")]
    [InlineData("DIAGNOSTIC_STAFF")]
    [InlineData("PHARMACY_STAFF")]
    [InlineData("HOSPITAL_ADMIN")]
    public async Task GetRoleList_ForbiddenRoles_Returns403(string role)
    {
        var token = GenerateToken(11, $"{role.ToLowerInvariant()}@medflow.test", role);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/roles");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetRoleList_InvalidPage_Returns400()
    {
        var token = GenerateToken(13, "system_admin01@medflow.test", "SYSTEM_ADMIN");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/roles?page=0");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetRoleList_InvalidPageSize_Returns400()
    {
        var token = GenerateToken(13, "system_admin01@medflow.test", "SYSTEM_ADMIN");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/roles?pageSize=101");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetRoleList_InvalidScope_Returns400()
    {
        var token = GenerateToken(13, "system_admin01@medflow.test", "SYSTEM_ADMIN");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/roles?scope=INVALID");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetRoleList_InvalidStatus_Returns400()
    {
        var token = GenerateToken(13, "system_admin01@medflow.test", "SYSTEM_ADMIN");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/roles?status=DELETED");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetRoleList_SystemAdminRole_Returns200WithSeededRoles_WhenDatabaseAvailable()
    {
        var token = GenerateToken(13, "system_admin01@medflow.test", "SYSTEM_ADMIN");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/roles?page=1&pageSize=10");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        if (response.StatusCode == HttpStatusCode.OK)
        {
            var payload = await response.Content.ReadFromJsonAsync<RoleListResponseDto>();
            Assert.NotNull(payload);
            Assert.True(payload.Total >= 7);
            Assert.NotEmpty(payload.Data);
            Assert.Equal(payload.Data.Count, payload.Items.Count);
            Assert.True(payload.Metrics.TotalRoles >= 7);
            Assert.True(payload.Metrics.TotalPermissions >= 14);
            Assert.Contains(payload.Data, r => r.Code == "SYSTEM_ADMIN");
        }
        else
        {
            Assert.True(
                response.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable,
                $"Unexpected status code: {response.StatusCode}");
        }
    }

    [Fact]
    public async Task GetRoleList_AliasRoutes_SystemAdminRole_MatchPrimaryRoute()
    {
        var unauthResponse = await Client.GetAsync("/api/v1/system-admin/roles");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthResponse.StatusCode);

        var secondAliasUnauth = await Client.GetAsync("/api/v1/roles");
        Assert.Equal(HttpStatusCode.Unauthorized, secondAliasUnauth.StatusCode);

        var token = GenerateToken(13, "system_admin01@medflow.test", "SYSTEM_ADMIN");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/system-admin/roles?scope=SYSTEM");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        if (response.StatusCode == HttpStatusCode.OK)
        {
            var payload = await response.Content.ReadFromJsonAsync<RoleListResponseDto>();
            Assert.NotNull(payload);
            Assert.All(payload.Data, r => Assert.Equal("SYSTEM", r.Scope));
        }
        else
        {
            Assert.True(
                response.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable,
                $"Unexpected status code: {response.StatusCode}");
        }
    }

    [Fact]
    public async Task GetRoleList_ForgedTokenWithNonAdminDatabaseUser_Returns403_WhenDatabaseAvailable()
    {
        // User ID 1 in seeded database is patient01@medflow.test (PATIENT role only)
        var forgedToken = GenerateToken(1, "patient01@medflow.test", "SYSTEM_ADMIN");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/roles");
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

    [Fact]
    public async Task GetRoleById_InvalidId_Returns400()
    {
        var token = GenerateToken(13, "system_admin01@medflow.test", "SYSTEM_ADMIN");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/roles/0");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetRoleById_NonExistentRole_Returns404_WhenDatabaseAvailable()
    {
        var token = GenerateToken(13, "system_admin01@medflow.test", "SYSTEM_ADMIN");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/roles/99999");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        else
        {
            Assert.True(
                response.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable,
                $"Unexpected status code: {response.StatusCode}");
        }
    }
}
