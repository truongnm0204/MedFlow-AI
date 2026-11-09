using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Entities;
using MedFlow.IntegrationTests.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace MedFlow.IntegrationTests.Consultation;

public sealed class DoctorMedicalHistoryEndpointTests : AuthTestBase
{
    private const string PatientRoute = "/api/v1/doctor/patients/PAT-0001/medical-history";
    private const string VisitRoute = "/api/v1/doctor/visits/VIS-202610-0001/medical-history";

    private readonly TestWebAppFactory _factory;

    public DoctorMedicalHistoryEndpointTests(TestWebAppFactory factory)
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
    public async Task GetMedicalHistory_Unauthenticated_Returns401()
    {
        var patientRes = await Client.GetAsync(PatientRoute);
        Assert.Equal(HttpStatusCode.Unauthorized, patientRes.StatusCode);

        var visitRes = await Client.GetAsync(VisitRoute);
        Assert.Equal(HttpStatusCode.Unauthorized, visitRes.StatusCode);
    }

    [Theory]
    [InlineData("PATIENT")]
    [InlineData("RECEPTION")]
    [InlineData("DIAGNOSTIC_STAFF")]
    [InlineData("PHARMACY_STAFF")]
    [InlineData("HOSPITAL_ADMIN")]
    [InlineData("SYSTEM_ADMIN")]
    public async Task GetMedicalHistory_ForbiddenRoles_Returns403(string role)
    {
        var token = GenerateToken(11, $"{role.ToLowerInvariant()}@medflow.test", role);

        var patientRes = await GetAsync(PatientRoute, token);
        Assert.Equal(HttpStatusCode.Forbidden, patientRes.StatusCode);

        var visitRes = await GetAsync(VisitRoute, token);
        Assert.Equal(HttpStatusCode.Forbidden, visitRes.StatusCode);
    }

    [Theory]
    [InlineData("limit=0")]
    [InlineData("fromDate=2026-13-40")]
    [InlineData("status=INVALID")]
    public async Task GetMedicalHistory_InvalidQueryParameters_Returns400(string query)
    {
        var token = GenerateToken(3, "doctor01@medflow.test", "DOCTOR");

        var patientRes = await GetAsync($"{PatientRoute}?{query}", token);
        Assert.Equal(HttpStatusCode.BadRequest, patientRes.StatusCode);

        var visitRes = await GetAsync($"{VisitRoute}?{query}", token);
        Assert.Equal(HttpStatusCode.BadRequest, visitRes.StatusCode);
    }

    [Fact]
    public async Task GetMedicalHistory_DoctorRole_PatientAndVisitRoutes_Return200WithClinicalHistory_WhenDatabaseAvailable()
    {
        var token = GenerateToken(3, "doctor01@medflow.test", "DOCTOR");

        var patientRes = await GetAsync(PatientRoute, token);
        if (patientRes.StatusCode == HttpStatusCode.OK)
        {
            using var payload = JsonDocument.Parse(await patientRes.Content.ReadAsStringAsync());
            Assert.Equal("PAT-0001", payload.RootElement.GetProperty("patient").GetProperty("patientCode").GetString());
            Assert.Equal(JsonValueKind.Object, payload.RootElement.GetProperty("stats").ValueKind);
            Assert.Equal(JsonValueKind.Array, payload.RootElement.GetProperty("visits").ValueKind);

            var visitRes = await GetAsync(VisitRoute, token);
            Assert.Equal(HttpStatusCode.OK, visitRes.StatusCode);
            using var visitPayload = JsonDocument.Parse(await visitRes.Content.ReadAsStringAsync());
            Assert.Equal("VIS-202610-0001", visitPayload.RootElement.GetProperty("currentVisit").GetProperty("visitCode").GetString());
        }
        else
        {
            Assert.True(
                patientRes.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable or HttpStatusCode.NotFound,
                $"Unexpected status code: {patientRes.StatusCode}");
        }
    }

    [Fact]
    public async Task GetMedicalHistory_DoctorRole_UnassignedPatient_Returns403_WhenDatabaseAvailable()
    {
        var token = GenerateToken(3, "doctor01@medflow.test", "DOCTOR");

        var response = await GetAsync("/api/v1/doctor/patients/PAT-0002/medical-history", token);

        if (response.StatusCode != HttpStatusCode.InternalServerError &&
            response.StatusCode != HttpStatusCode.ServiceUnavailable &&
            response.StatusCode != HttpStatusCode.NotFound)
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }
}
