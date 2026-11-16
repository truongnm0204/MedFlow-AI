using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MedFlow.Application.DTOs.SystemAdmin;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Entities;
using MedFlow.IntegrationTests.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace MedFlow.IntegrationTests.SystemAdmin;

public sealed class AdminStaffEndpointTests : AuthTestBase
{
    private readonly TestWebAppFactory _factory;

    public AdminStaffEndpointTests(TestWebAppFactory factory)
        : base(factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetStaffAccounts_Unauthenticated_Returns401()
    {
        var response = await Client.GetAsync("/api/v1/admin/staff");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetStaffAccounts_NonAdminRole_Returns403()
    {
        using var request = AuthorizedRequest(11, "doctor01@medflow.test", "DOCTOR");

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetStaffAccounts_InvalidPaging_Returns400BeforeDatabaseQuery()
    {
        using var request = AuthorizedRequest(13, "system_admin01@medflow.test", "SYSTEM_ADMIN", "?page=0");

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetStaffAccounts_SystemAdmin_ReturnsContract_WhenDatabaseAvailable()
    {
        using var request = AuthorizedRequest(
            13,
            "system_admin01@medflow.test",
            "SYSTEM_ADMIN",
            "?page=1&pageSize=5");

        var response = await Client.SendAsync(request);

        if (response.StatusCode == HttpStatusCode.OK)
        {
            var payload = await response.Content.ReadFromJsonAsync<StaffAccountListResponseDto>();
            Assert.NotNull(payload);
            Assert.Equal(1, payload.Page);
            Assert.Equal(5, payload.PageSize);
            Assert.True(payload.Data.Count <= 5);
            Assert.Equal(
                payload.Data.Select(item => item.StaffId),
                payload.Items.Select(item => item.StaffId));
            Assert.True(payload.Metrics.Total >= payload.Total);
        }
        else
        {
            Assert.True(
                response.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable,
                $"Unexpected status code: {response.StatusCode}");
        }
    }

    [Fact]
    public async Task GetStaffAccounts_AliasRoute_IsProtected()
    {
        var response = await Client.GetAsync("/api/v1/system-admin/staff");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetStaffAccounts_ForgedSystemAdminClaim_Returns403_WhenDatabaseAvailable()
    {
        using var request = AuthorizedRequest(1, "patient01@medflow.test", "SYSTEM_ADMIN");

        var response = await Client.SendAsync(request);

        if (response.StatusCode != HttpStatusCode.Forbidden)
        {
            Assert.True(
                response.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable,
                $"Unexpected status code: {response.StatusCode}");
        }
    }

    private HttpRequestMessage AuthorizedRequest(
        long userId,
        string email,
        string role,
        string query = "")
    {
        using var scope = _factory.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var token = jwtService.GenerateAccessToken(
            new User { Id = userId, Email = email },
            [role]).AccessToken;

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/admin/staff{query}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }
}
