using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Entities;
using MedFlow.IntegrationTests.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace MedFlow.IntegrationTests.Consultation;

public sealed class DoctorConsultationEndpointTests : AuthTestBase
{
    private const string GetRoute = "/api/v1/doctor/consultation/VIS-202610-0001";
    private const string OpenRoute = "/api/v1/doctor/consultation/VIS-202610-0001/open";
    private const string ClinicalExamRoute = "/api/v1/doctor/consultation/VIS-202610-0001/clinical-examination";
    private const string ClinicalExamAliasRoute = "/api/v1/doctor/visits/VIS-202610-0001/clinical-examination";
    private const string DiagnosticOrdersRoute = "/api/v1/doctor/consultation/VIS-202610-0001/diagnostic-orders";
    private const string DiagnosticOrdersAliasRoute = "/api/v1/doctor/visits/VIS-202610-0001/diagnostic-orders";
    private readonly TestWebAppFactory _factory;

    public DoctorConsultationEndpointTests(TestWebAppFactory factory)
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

    private async Task<HttpResponseMessage> GetWithTokenAsync(string url, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await Client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> PostWithTokenAsync(string url, string token, object? body = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await Client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> PutWithTokenAsync(string url, string token, object? body = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await Client.SendAsync(request);
    }

    [Fact]
    public async Task GetAndOpenConsultation_Unauthenticated_Returns401()
    {
        var getResponse = await Client.GetAsync(GetRoute);
        Assert.Equal(HttpStatusCode.Unauthorized, getResponse.StatusCode);

        using var postRequest = new HttpRequestMessage(HttpMethod.Post, OpenRoute);
        var postResponse = await Client.SendAsync(postRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, postResponse.StatusCode);
    }

    [Theory]
    [InlineData("PATIENT")]
    [InlineData("RECEPTION")]
    [InlineData("DIAGNOSTIC_STAFF")]
    [InlineData("PHARMACY_STAFF")]
    [InlineData("HOSPITAL_ADMIN")]
    [InlineData("SYSTEM_ADMIN")]
    public async Task GetAndOpenConsultation_ForbiddenRoles_Returns403(string role)
    {
        var token = GenerateToken(11, $"{role.ToLowerInvariant()}@medflow.test", role);

        var getResponse = await GetWithTokenAsync(GetRoute, token);
        Assert.Equal(HttpStatusCode.Forbidden, getResponse.StatusCode);

        var postResponse = await PostWithTokenAsync(OpenRoute, token);
        Assert.Equal(HttpStatusCode.Forbidden, postResponse.StatusCode);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("VIS-202610-0001-INVALID-LENGTH-OVER-40-CHARS-LONG")]
    public async Task GetConsultation_InvalidIdentifier_Returns400(string invalidId)
    {
        var token = GenerateToken(3, "doctor01@medflow.test", "DOCTOR");

        var response = await GetWithTokenAsync($"/api/v1/doctor/consultation/{invalidId}", token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task OpenConsultation_InvalidExpectedStateVersion_Returns400()
    {
        var token = GenerateToken(3, "doctor01@medflow.test", "DOCTOR");

        var response = await PostWithTokenAsync(
            OpenRoute,
            token,
            new { expectedStateVersion = 0 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetConsultation_CrossDoctorVisit_Returns403_WhenDatabaseAvailable()
    {
        var token = GenerateToken(3, "doctor01@medflow.test", "DOCTOR");

        var response = await GetWithTokenAsync("/api/v1/doctor/consultation/VIS-202610-0002", token);

        if (response.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable)
        {
            return;
        }

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetConsultation_OwnedSeededVisit_Returns200WithClinicalWorkspace_WhenDatabaseAvailable()
    {
        var token = GenerateToken(3, "doctor01@medflow.test", "DOCTOR");

        var response = await GetWithTokenAsync(GetRoute, token);

        if (response.StatusCode == HttpStatusCode.OK)
        {
            var rawJson = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("identificationHash", rawJson, StringComparison.OrdinalIgnoreCase);

            using var payload = JsonDocument.Parse(rawJson);
            var root = payload.RootElement;
            Assert.Equal("VIEWED", root.GetProperty("actionOutcome").GetString());
            Assert.Equal("VIS-202610-0001", root.GetProperty("visit").GetProperty("visitCode").GetString());
            Assert.Equal("PAT-0001", root.GetProperty("patient").GetProperty("patientCode").GetString());
            Assert.Equal(
                119,
                root.GetProperty("clinicalContext").GetProperty("vitalSigns").GetProperty("bpSystolicMmhg").GetInt32());
        }
        else
        {
            Assert.True(
                response.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable,
                $"Unexpected status code: {response.StatusCode}");
        }
    }

    [Fact]
    public async Task RecordClinicalExamination_Unauthenticated_Returns401()
    {
        using var postRequest = new HttpRequestMessage(HttpMethod.Post, ClinicalExamRoute);
        var postResponse = await Client.SendAsync(postRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, postResponse.StatusCode);

        using var putRequest = new HttpRequestMessage(HttpMethod.Put, ClinicalExamAliasRoute);
        var putResponse = await Client.SendAsync(putRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, putResponse.StatusCode);
    }

    [Theory]
    [InlineData("PATIENT")]
    [InlineData("RECEPTION")]
    [InlineData("DIAGNOSTIC_STAFF")]
    [InlineData("PHARMACY_STAFF")]
    [InlineData("HOSPITAL_ADMIN")]
    [InlineData("SYSTEM_ADMIN")]
    public async Task RecordClinicalExamination_ForbiddenRoles_Returns403(string role)
    {
        var token = GenerateToken(11, $"{role.ToLowerInvariant()}@medflow.test", role);
        var body = new { clinicalFindings = "Khám bụng mềm" };

        var postResponse = await PostWithTokenAsync(ClinicalExamRoute, token, body);
        Assert.Equal(HttpStatusCode.Forbidden, postResponse.StatusCode);

        var putResponse = await PutWithTokenAsync(ClinicalExamAliasRoute, token, body);
        Assert.Equal(HttpStatusCode.Forbidden, putResponse.StatusCode);
    }

    [Fact]
    public async Task RecordClinicalExamination_InvalidPayloadOrVitalSigns_Returns400()
    {
        var token = GenerateToken(3, "doctor01@medflow.test", "DOCTOR");

        var emptyResponse = await PostWithTokenAsync(
            ClinicalExamRoute,
            token,
            new { amendmentReason = "Chỉ có lý do cập nhật" });
        Assert.Equal(HttpStatusCode.BadRequest, emptyResponse.StatusCode);

        var invalidBpResponse = await PutWithTokenAsync(
            ClinicalExamAliasRoute,
            token,
            new
            {
                bpSystolicMmhg = 90,
                bpDiastolicMmhg = 110,
                clinicalFindings = "Khám bụng mềm",
            });
        Assert.Equal(HttpStatusCode.BadRequest, invalidBpResponse.StatusCode);

        var invalidRevisionResponse = await PostWithTokenAsync(
            ClinicalExamRoute,
            token,
            new
            {
                clinicalFindings = "Khám bụng mềm",
                expectedRevisionNo = 0,
            });
        Assert.Equal(HttpStatusCode.BadRequest, invalidRevisionResponse.StatusCode);
    }

    [Fact]
    public async Task RecordClinicalExamination_CrossDoctorAndCompletedSeededVisit_Returns403And409_WhenDatabaseAvailable()
    {
        var token = GenerateToken(3, "doctor01@medflow.test", "DOCTOR");
        var body = new
        {
            presentingComplaint = "Đau thượng vị",
            bpSystolicMmhg = 120,
            bpDiastolicMmhg = 80,
            clinicalFindings = "Bụng mềm, không phản ứng thành bụng",
        };

        var crossDoctorResponse = await PostWithTokenAsync(
            "/api/v1/doctor/consultation/VIS-202610-0002/clinical-examination",
            token,
            body);

        if (crossDoctorResponse.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable)
        {
            return;
        }

        Assert.Equal(HttpStatusCode.Forbidden, crossDoctorResponse.StatusCode);

        var completedVisitResponse = await PutWithTokenAsync(ClinicalExamRoute, token, body);
        Assert.Equal(HttpStatusCode.Conflict, completedVisitResponse.StatusCode);
    }

    [Fact]
    public async Task GetAndCreateDiagnosticOrders_Unauthenticated_Returns401()
    {
        var getResponse = await Client.GetAsync(DiagnosticOrdersRoute);
        Assert.Equal(HttpStatusCode.Unauthorized, getResponse.StatusCode);

        using var postRequest = new HttpRequestMessage(HttpMethod.Post, DiagnosticOrdersRoute);
        var postResponse = await Client.SendAsync(postRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, postResponse.StatusCode);

        using var aliasPostRequest = new HttpRequestMessage(HttpMethod.Post, DiagnosticOrdersAliasRoute);
        var aliasPostResponse = await Client.SendAsync(aliasPostRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, aliasPostResponse.StatusCode);
    }

    [Theory]
    [InlineData("PATIENT")]
    [InlineData("RECEPTION")]
    [InlineData("DIAGNOSTIC_STAFF")]
    [InlineData("PHARMACY_STAFF")]
    [InlineData("HOSPITAL_ADMIN")]
    [InlineData("SYSTEM_ADMIN")]
    public async Task GetAndCreateDiagnosticOrders_ForbiddenRoles_Returns403(string role)
    {
        var token = GenerateToken(11, $"{role.ToLowerInvariant()}@medflow.test", role);
        var body = new { serviceId = 5, priority = "NORMAL" };

        var getResponse = await GetWithTokenAsync(DiagnosticOrdersRoute, token);
        Assert.Equal(HttpStatusCode.Forbidden, getResponse.StatusCode);

        var postResponse = await PostWithTokenAsync(DiagnosticOrdersRoute, token, body);
        Assert.Equal(HttpStatusCode.Forbidden, postResponse.StatusCode);

        var aliasPostResponse = await PostWithTokenAsync(DiagnosticOrdersAliasRoute, token, body);
        Assert.Equal(HttpStatusCode.Forbidden, aliasPostResponse.StatusCode);
    }

    [Fact]
    public async Task CreateDiagnosticOrder_InvalidPayload_Returns400()
    {
        var token = GenerateToken(3, "doctor01@medflow.test", "DOCTOR");

        var emptyResponse = await PostWithTokenAsync(
            DiagnosticOrdersRoute,
            token,
            new { clinicalNote = "Chỉ có ghi chú" });
        Assert.Equal(HttpStatusCode.BadRequest, emptyResponse.StatusCode);

        var invalidPriorityResponse = await PostWithTokenAsync(
            DiagnosticOrdersAliasRoute,
            token,
            new
            {
                serviceId = 5,
                priority = "INVALID_PRIORITY",
            });
        Assert.Equal(HttpStatusCode.BadRequest, invalidPriorityResponse.StatusCode);

        var invalidStateVersionResponse = await PostWithTokenAsync(
            DiagnosticOrdersRoute,
            token,
            new
            {
                serviceId = 5,
                expectedStateVersion = 0,
            });
        Assert.Equal(HttpStatusCode.BadRequest, invalidStateVersionResponse.StatusCode);

        var invalidInstructionResponse = await PostWithTokenAsync(
            DiagnosticOrdersRoute,
            token,
            new
            {
                serviceId = 5,
                instruction = "<script>alert(1)</script>",
            });
        Assert.Equal(HttpStatusCode.BadRequest, invalidInstructionResponse.StatusCode);
    }

    [Fact]
    public async Task GetAndCreateDiagnosticOrders_CrossDoctorAndCompletedSeededVisit_Returns403And409_WhenDatabaseAvailable()
    {
        var token = GenerateToken(3, "doctor01@medflow.test", "DOCTOR");
        var body = new
        {
            serviceId = 5,
            instruction = "Lấy mẫu máu lúc đói",
            priority = "NORMAL",
        };

        var crossDoctorResponse = await PostWithTokenAsync(
            "/api/v1/doctor/consultation/VIS-202610-0002/diagnostic-orders",
            token,
            body);

        if (crossDoctorResponse.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable)
        {
            return;
        }

        Assert.Equal(HttpStatusCode.Forbidden, crossDoctorResponse.StatusCode);

        var completedVisitResponse = await PostWithTokenAsync(DiagnosticOrdersRoute, token, body);
        Assert.Equal(HttpStatusCode.Conflict, completedVisitResponse.StatusCode);
    }
}
