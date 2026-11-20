using System.Net;
using System.Net.Http.Headers;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Entities;
using MedFlow.IntegrationTests.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace MedFlow.IntegrationTests.Patients;

public sealed class PatientPrescriptionEndpointTests : IClassFixture<TestWebAppFactory>
{
    private const string ListRoute = "/api/v1/patients/me/prescriptions";
    private const string DetailRoute = "/api/v1/patients/me/prescriptions/RX-2026-0001";

    private readonly TestWebAppFactory _factory;
    private readonly HttpClient _client;

    public PatientPrescriptionEndpointTests(TestWebAppFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task PatientPrescriptionEndpoints_Unauthenticated_Return401()
    {
        var listResponse = await _client.GetAsync(ListRoute);
        var detailResponse = await _client.GetAsync(DetailRoute);

        Assert.Equal(HttpStatusCode.Unauthorized, listResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, detailResponse.StatusCode);
    }

    [Theory]
    [InlineData("DOCTOR")]
    [InlineData("DIAGNOSTIC_STAFF")]
    [InlineData("RECEPTION")]
    [InlineData("PHARMACY_STAFF")]
    [InlineData("HOSPITAL_ADMIN")]
    [InlineData("SYSTEM_ADMIN")]
    public async Task PatientPrescriptionEndpoints_NonPatientRoles_Return403(string role)
    {
        var token = GenerateToken(560, $"{role.ToLowerInvariant()}@medflow.test", role);

        var listResponse = await GetWithTokenAsync(ListRoute, token);
        var detailResponse = await GetWithTokenAsync(DetailRoute, token);

        Assert.Equal(HttpStatusCode.Forbidden, listResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, detailResponse.StatusCode);
    }

    private string GenerateToken(long userId, string email, params string[] roles)
    {
        using var scope = _factory.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        return jwtService.GenerateAccessToken(new User { Id = userId, Email = email }, roles).AccessToken;
    }

    private async Task<HttpResponseMessage> GetWithTokenAsync(string route, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, route);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(request);
    }
}
