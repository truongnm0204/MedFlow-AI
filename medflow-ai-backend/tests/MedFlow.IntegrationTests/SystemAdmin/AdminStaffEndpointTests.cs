using System.Net;
using System.Net.Http.Headers;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Entities;
using MedFlow.IntegrationTests.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace MedFlow.IntegrationTests.SystemAdmin;

public sealed class AdminStaffEndpointTests : IClassFixture<TestWebAppFactory>, IDisposable
{
    private readonly TestWebAppFactory _factory;
    private readonly HttpClient _client;

    public AdminStaffEndpointTests(TestWebAppFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Deactivate_Unauthenticated_Returns401()
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Patch,
            "/api/v1/admin/staff/20/deactivate");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Deactivate_NonSystemAdminRole_Returns403()
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Patch,
            "/api/v1/admin/staff/20/deactivate");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            GenerateToken(200, "doctor@medflow.test", "DOCTOR"));

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Deactivate_SystemAdminWithMissingBody_ReturnsProblemDetails400()
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Patch,
            "/api/v1/admin/staff/20/deactivate");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            GenerateToken(100, "admin@medflow.test", "SYSTEM_ADMIN"));

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    private string GenerateToken(long userId, string email, params string[] roles)
    {
        using var scope = _factory.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        return jwtService.GenerateAccessToken(new User { Id = userId, Email = email }, roles).AccessToken;
    }

    public void Dispose()
    {
        _client.Dispose();
        GC.SuppressFinalize(this);
    }
}
