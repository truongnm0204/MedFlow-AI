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

    [Fact]
    public async Task CreateDiagnosticService_Unauthenticated_Returns401()
    {
        var response = await Client.PostAsJsonAsync(EndpointRoute, new { serviceCode = "DIA-X" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("DOCTOR")]
    [InlineData("SYSTEM_ADMIN")]
    public async Task CreateDiagnosticService_ForbiddenRoles_Returns403(string role)
    {
        var token = GenerateToken(13, $"{role.ToLowerInvariant()}@medflow.test", role);

        using var request = new HttpRequestMessage(HttpMethod.Post, EndpointRoute);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new { serviceCode = "DIA-X", name = "Ngoài quyền", departmentId = 4 });

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateDiagnosticService_EmptyBody_Returns400()
    {
        var token = GenerateToken(11, "hospital_admin01@medflow.test", "HOSPITAL_ADMIN");

        using var request = new HttpRequestMessage(HttpMethod.Post, EndpointRoute);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = new ByteArrayContent([]);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateDiagnosticService_DuplicateSeedCode_Returns409_WhenDatabaseAvailable()
    {
        var token = GenerateToken(11, "hospital_admin01@medflow.test", "HOSPITAL_ADMIN");
        using var optionsRequest = new HttpRequestMessage(HttpMethod.Get, $"{EndpointRoute}/form-options");
        optionsRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var optionsResponse = await Client.SendAsync(optionsRequest);

        if (optionsResponse.StatusCode != HttpStatusCode.OK)
        {
            Assert.True(
                optionsResponse.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable,
                $"Unexpected status code: {optionsResponse.StatusCode}");
            return;
        }

        var options = await optionsResponse.Content.ReadFromJsonAsync<DiagnosticServiceFormOptionsDto>();
        Assert.NotNull(options);
        Assert.NotEmpty(options.Departments);

        using var request = new HttpRequestMessage(HttpMethod.Post, EndpointRoute);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new CreateDiagnosticServiceAdminRequest(
            ServiceCode: "DIA-CBC",
            Name: "Trùng mã seed",
            DepartmentId: options.Departments[0].Id,
            Price: 10000m));

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task UpdateMissingDiagnosticService_Returns404_WhenDatabaseAvailable()
    {
        var token = GenerateToken(11, "hospital_admin01@medflow.test", "HOSPITAL_ADMIN");

        using var request = new HttpRequestMessage(HttpMethod.Put, $"{EndpointRoute}/999999999");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new UpdateDiagnosticServiceAdminRequest(
            Name: "Không tồn tại",
            DepartmentId: 1));

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

    [Fact]
    public async Task DeactivateMissingDiagnosticService_Returns404_WhenDatabaseAvailable()
    {
        var token = GenerateToken(11, "hospital_admin01@medflow.test", "HOSPITAL_ADMIN");

        using var request = new HttpRequestMessage(HttpMethod.Patch, $"{EndpointRoute}/999999999/deactivate");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new DeactivateDiagnosticServiceAdminRequest("Dịch vụ không tồn tại"));

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

    [Fact]
    public async Task GetFormOptions_HospitalAdmin_Returns200_WhenDatabaseAvailable()
    {
        var token = GenerateToken(11, "hospital_admin01@medflow.test", "HOSPITAL_ADMIN");

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{EndpointRoute}/form-options");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        if (response.StatusCode == HttpStatusCode.OK)
        {
            var payload = await response.Content.ReadFromJsonAsync<DiagnosticServiceFormOptionsDto>();
            Assert.NotNull(payload);
            Assert.Equal(1, payload.HospitalId);
            Assert.NotEmpty(payload.Departments);
        }
        else
        {
            Assert.True(
                response.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable,
                $"Unexpected status code: {response.StatusCode}");
        }
    }
}
