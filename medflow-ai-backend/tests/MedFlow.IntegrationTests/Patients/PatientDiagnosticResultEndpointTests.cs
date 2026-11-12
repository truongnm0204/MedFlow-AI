using System.Net;
using System.Net.Http.Headers;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Entities;
using MedFlow.IntegrationTests.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace MedFlow.IntegrationTests.Patients;

public sealed class PatientDiagnosticResultEndpointTests : AuthTestBase
{
    private const string ListRoute = "/api/v1/patients/me/diagnostic-results";
    private const string DetailRoute = "/api/v1/patients/me/diagnostic-results/ORD-2026-0142";

    private readonly TestWebAppFactory _factory;

    public PatientDiagnosticResultEndpointTests(TestWebAppFactory factory)
        : base(factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PatientDiagnosticResultEndpoints_Unauthenticated_Return401()
    {
        var listResponse = await Client.GetAsync(ListRoute);
        var detailResponse = await Client.GetAsync(DetailRoute);

        Assert.Equal(HttpStatusCode.Unauthorized, listResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, detailResponse.StatusCode);
    }

    [Fact]
    public async Task PatientDiagnosticResultEndpoints_PatientWithoutProfile_Return404()
    {
        var token = GenerateToken(560_000_000_001, "missing-patient@medflow.test", "PATIENT");

        var listResponse = await GetWithTokenAsync(ListRoute, token);
        var detailResponse = await GetWithTokenAsync(DetailRoute, token);

        Assert.Equal(HttpStatusCode.NotFound, listResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, detailResponse.StatusCode);
    }

    [Theory]
    [InlineData("DOCTOR")]
    [InlineData("DIAGNOSTIC_STAFF")]
    [InlineData("RECEPTION")]
    [InlineData("PHARMACY_STAFF")]
    [InlineData("HOSPITAL_ADMIN")]
    [InlineData("SYSTEM_ADMIN")]
    public async Task PatientDiagnosticResultEndpoints_NonPatientRoles_Return403(string role)
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
        var user = new User { Id = userId, Email = email };
        return jwtService.GenerateAccessToken(user, roles).AccessToken;
    }

    private async Task<HttpResponseMessage> GetWithTokenAsync(string url, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await Client.SendAsync(request);
    }
}
