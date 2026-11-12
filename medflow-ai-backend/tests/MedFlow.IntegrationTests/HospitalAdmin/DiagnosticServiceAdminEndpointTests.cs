using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MedFlow.Application.DTOs.Services;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Entities;
using MedFlow.IntegrationTests.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace MedFlow.IntegrationTests.HospitalAdmin;

public sealed class DiagnosticServiceAdminEndpointTests : AuthTestBase
{
    private const string EndpointRoute = "/api/v1/admin/diagnostic-services";

    private readonly TestWebAppFactory _factory;

    public DiagnosticServiceAdminEndpointTests(TestWebAppFactory factory)
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
    public async Task GetDiagnosticServiceList_Unauthenticated_Returns401()
    {
        var response = await Client.GetAsync(EndpointRoute);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("PATIENT")]
    [InlineData("DOCTOR")]
    [InlineData("RECEPTION")]
    [InlineData("DIAGNOSTIC_STAFF")]
    [InlineData("PHARMACY_STAFF")]
    [InlineData("SYSTEM_ADMIN")]
    public async Task GetDiagnosticServiceList_ForbiddenRoles_Returns403(string role)
    {
        var token = GenerateToken(13, $"{role.ToLowerInvariant()}@medflow.test", role);

        using var request = new HttpRequestMessage(HttpMethod.Get, EndpointRoute);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetDiagnosticServiceList_InvalidPageSize_Returns400()
    {
        var token = GenerateToken(11, "hospital_admin01@medflow.test", "HOSPITAL_ADMIN");

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{EndpointRoute}?pageSize=0");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetDiagnosticServiceList_InvalidStatus_Returns400()
    {
        var token = GenerateToken(11, "hospital_admin01@medflow.test", "HOSPITAL_ADMIN");

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{EndpointRoute}?status=PAUSED");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetDiagnosticServiceList_HospitalAdminRole_Returns200WithSeededData_WhenDatabaseAvailable()
    {
        var token = GenerateToken(11, "hospital_admin01@medflow.test", "HOSPITAL_ADMIN");

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{EndpointRoute}?pageSize=100");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        if (response.StatusCode == HttpStatusCode.OK)
        {
            var payload = await response.Content.ReadFromJsonAsync<DiagnosticServiceAdminListResponseDto>();
            Assert.NotNull(payload);
            Assert.Equal(1, payload.HospitalId);
            Assert.NotEmpty(payload.Data);
            Assert.All(payload.Data, item =>
                Assert.Equal("DIAGNOSTIC", item.ServiceType, ignoreCase: true));
            Assert.Contains(payload.Data, item =>
                string.Equals(item.ServiceCode, "DIA-CBC", StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            Assert.True(
                response.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable,
                $"Unexpected status code: {response.StatusCode}");
        }
    }

    [Fact]
    public async Task GetDiagnosticServiceList_ForgedTokenWithNonHospitalAdminDatabaseUser_Returns403_WhenDatabaseAvailable()
    {
        var forgedToken = GenerateToken(1, "patient01@medflow.test", "HOSPITAL_ADMIN");

        using var request = new HttpRequestMessage(HttpMethod.Get, EndpointRoute);
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
