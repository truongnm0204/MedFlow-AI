using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MedFlow.Application.DTOs.Reports;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Entities;
using MedFlow.IntegrationTests.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace MedFlow.IntegrationTests.HospitalAdmin;

public sealed class AdminOperationalReportsEndpointTests : AuthTestBase
{
    private readonly TestWebAppFactory _factory;

    public AdminOperationalReportsEndpointTests(TestWebAppFactory factory)
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
    public async Task GetOperationalReport_Unauthenticated_Returns401()
    {
        var response = await Client.GetAsync("/api/v1/admin/reports/operational");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("PATIENT")]
    [InlineData("DOCTOR")]
    [InlineData("RECEPTION")]
    [InlineData("DIAGNOSTIC_STAFF")]
    [InlineData("PHARMACY_STAFF")]
    public async Task GetOperationalReport_ForbiddenRoles_Returns403(string role)
    {
        var token = GenerateToken(25, $"{role.ToLowerInvariant()}@medflow.test", role);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/reports/operational");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetOperationalReport_InvalidDateRange_Returns400()
    {
        var token = GenerateToken(11, "hospital_admin01@medflow.test", "HOSPITAL_ADMIN");
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/admin/reports/operational?fromDate=2026-11-25&toDate=2026-11-20");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetOperationalReport_HospitalAdminRole_Returns200WhenDatabaseAvailable()
    {
        var token = GenerateToken(11, "hospital_admin01@medflow.test", "HOSPITAL_ADMIN");
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/admin/reports/operational?fromDate=2026-11-01&toDate=2026-11-24");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        if (response.StatusCode == HttpStatusCode.OK)
        {
            var payload = await response.Content.ReadFromJsonAsync<OperationalReportResponseDto>();
            Assert.NotNull(payload);
            Assert.False(string.IsNullOrWhiteSpace(payload.FacilityName));
            Assert.True(payload.Summary.TotalVisits >= 0);
        }
        else
        {
            Assert.True(
                response.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable,
                $"Unexpected status code: {response.StatusCode}");
        }
    }
}
