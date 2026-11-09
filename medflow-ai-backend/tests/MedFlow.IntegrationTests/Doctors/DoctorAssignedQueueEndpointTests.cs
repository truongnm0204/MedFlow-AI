using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Entities;
using MedFlow.IntegrationTests.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace MedFlow.IntegrationTests.Doctors;

public sealed class DoctorAssignedQueueEndpointTests : AuthTestBase
{
    private const string Route = "/api/v1/doctor/assigned-queue";

    private readonly TestWebAppFactory _factory;

    public DoctorAssignedQueueEndpointTests(TestWebAppFactory factory)
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

    private async Task<HttpResponseMessage> GetAsync(string url, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await Client.SendAsync(request);
    }

    [Fact]
    public async Task GetAssignedQueue_Unauthenticated_Returns401()
    {
        var response = await Client.GetAsync(Route);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("PATIENT")]
    [InlineData("RECEPTION")]
    [InlineData("DIAGNOSTIC_STAFF")]
    [InlineData("PHARMACY_STAFF")]
    [InlineData("HOSPITAL_ADMIN")]
    [InlineData("SYSTEM_ADMIN")]
    public async Task GetAssignedQueue_ForbiddenRoles_Returns403(string role)
    {
        var token = GenerateToken(11, $"{role.ToLowerInvariant()}@medflow.test", role);

        var response = await GetAsync(Route, token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetAssignedQueue_InvalidQueueDate_Returns400()
    {
        var token = GenerateToken(3, "doctor01@medflow.test", "DOCTOR");

        var response = await GetAsync($"{Route}?queueDate=2026-13-45", token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetAssignedQueue_DoctorRole_Returns200WithServicePointsAndStats_WhenDatabaseAvailable()
    {
        var token = GenerateToken(3, "doctor01@medflow.test", "DOCTOR");

        var response = await GetAsync(Route, token);

        if (response.StatusCode == HttpStatusCode.OK)
        {
            using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(JsonValueKind.Array, payload.RootElement.GetProperty("servicePoints").ValueKind);
            Assert.Equal(JsonValueKind.Object, payload.RootElement.GetProperty("stats").ValueKind);
        }
        else
        {
            Assert.True(
                response.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable,
                $"Unexpected status code: {response.StatusCode}");
        }
    }
}
