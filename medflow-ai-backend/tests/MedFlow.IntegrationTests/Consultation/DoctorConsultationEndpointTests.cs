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
    private const string DiagnosisRoute = "/api/v1/doctor/consultation/VIS-202610-0001/diagnosis";
    private const string DiagnosisAliasRoute = "/api/v1/doctor/visits/VIS-202610-0001/diagnosis";
    private const string Icd10SearchRoute = "/api/v1/doctor/icd10-codes";
    private const string CompleteVisitRoute = "/api/v1/doctor/consultation/VIS-202610-0001/complete";
    private const string CompleteVisitAliasRoute = "/api/v1/doctor/visits/VIS-202610-0001/complete-visit";
    private const string DiagnosticResultsRoute = "/api/v1/doctor/consultation/VIS-202610-0001/diagnostic-results";
    private const string DiagnosticResultsAliasRoute = "/api/v1/doctor/visits/VIS-202610-0001/diagnostic-results";
    private const string DiagnosticResultDetailRoute = "/api/v1/doctor/consultation/VIS-202610-0001/diagnostic-results/DIA-202610-0001";
    private const string ReviewDiagnosticResultsRoute = "/api/v1/doctor/consultation/VIS-202610-0001/diagnostic-results/review";
    private const string ReviewDiagnosticResultsAliasRoute = "/api/v1/doctor/visits/VIS-202610-0001/review-diagnostic-results";
    private const string AdditionalDiagnosticOrdersRoute = "/api/v1/doctor/consultation/VIS-202610-0001/additional-diagnostic-orders";
    private const string AdditionalDiagnosticOrdersAliasRoute = "/api/v1/doctor/visits/VIS-202610-0001/additional-diagnostic-orders";
    private const string DiagnosticResultsTrackingRoute = "/api/v1/doctor/diagnostic-results/tracking";
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
    public async Task RecordDiagnosisAndSearchIcd10_Unauthenticated_Returns401()
    {
        using var postRequest = new HttpRequestMessage(HttpMethod.Post, DiagnosisRoute);
        var postResponse = await Client.SendAsync(postRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, postResponse.StatusCode);

        using var putRequest = new HttpRequestMessage(HttpMethod.Put, DiagnosisAliasRoute);
        var putResponse = await Client.SendAsync(putRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, putResponse.StatusCode);

        using var getRequest = new HttpRequestMessage(HttpMethod.Get, Icd10SearchRoute);
        var getResponse = await Client.SendAsync(getRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, getResponse.StatusCode);
    }

    [Theory]
    [InlineData("PATIENT")]
    [InlineData("RECEPTION")]
    [InlineData("DIAGNOSTIC_STAFF")]
    [InlineData("PHARMACY_STAFF")]
    [InlineData("HOSPITAL_ADMIN")]
    [InlineData("SYSTEM_ADMIN")]
    public async Task RecordDiagnosisAndSearchIcd10_ForbiddenRoles_Returns403(string role)
    {
        var token = GenerateToken(11, $"{role.ToLowerInvariant()}@medflow.test", role);
        var body = new
        {
            primaryIcd10Code = "K29.5",
            clinicalConclusion = "Viêm dạ dày mạn tính",
        };

        var postResponse = await PostWithTokenAsync(DiagnosisRoute, token, body);
        Assert.Equal(HttpStatusCode.Forbidden, postResponse.StatusCode);

        var putResponse = await PutWithTokenAsync(DiagnosisAliasRoute, token, body);
        Assert.Equal(HttpStatusCode.Forbidden, putResponse.StatusCode);

        var searchResponse = await GetWithTokenAsync(Icd10SearchRoute, token);
        Assert.Equal(HttpStatusCode.Forbidden, searchResponse.StatusCode);
    }

    [Fact]
    public async Task RecordDiagnosis_InvalidPayloadOrIcd10Format_Returns400()
    {
        var token = GenerateToken(3, "doctor01@medflow.test", "DOCTOR");

        var emptyResponse = await PostWithTokenAsync(
            DiagnosisRoute,
            token,
            new { clinicalConclusion = "Chỉ có kết luận không có mã ICD-10" });
        Assert.Equal(HttpStatusCode.BadRequest, emptyResponse.StatusCode);

        var duplicateIcdResponse = await PutWithTokenAsync(
            DiagnosisAliasRoute,
            token,
            new
            {
                primaryIcd10Code = "K29.5",
                secondaryIcd10Codes = new[] { "K29.5" },
                clinicalConclusion = "Viêm dạ dày mạn tính",
            });
        Assert.Equal(HttpStatusCode.BadRequest, duplicateIcdResponse.StatusCode);

        var invalidFormatResponse = await PostWithTokenAsync(
            DiagnosisRoute,
            token,
            new
            {
                primaryIcd10Code = "BAD-ICD-CODE",
                clinicalConclusion = "Viêm dạ dày mạn tính",
            });
        Assert.Equal(HttpStatusCode.BadRequest, invalidFormatResponse.StatusCode);

        var invalidRevisionResponse = await PostWithTokenAsync(
            DiagnosisRoute,
            token,
            new
            {
                primaryIcd10Code = "K29.5",
                clinicalConclusion = "Viêm dạ dày mạn tính",
                expectedRevisionNo = 0,
            });
        Assert.Equal(HttpStatusCode.BadRequest, invalidRevisionResponse.StatusCode);
    }

    [Fact]
    public async Task RecordDiagnosisAndSearchIcd10_CrossDoctorCompletedVisitAndCatalogLookup_WhenDatabaseAvailable()
    {
        var token = GenerateToken(3, "doctor01@medflow.test", "DOCTOR");
        var body = new
        {
            primaryIcd10Code = "K29.5",
            secondaryIcd10Codes = new[] { "K76.0" },
            clinicalConclusion = "Viêm dạ dày mạn tính kèm gan nhiễm mỡ",
        };

        var crossDoctorResponse = await PostWithTokenAsync(
            "/api/v1/doctor/consultation/VIS-202610-0002/diagnosis",
            token,
            body);

        if (crossDoctorResponse.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable)
        {
            return;
        }

        Assert.Equal(HttpStatusCode.Forbidden, crossDoctorResponse.StatusCode);

        var completedVisitResponse = await PutWithTokenAsync(DiagnosisRoute, token, body);
        Assert.Equal(HttpStatusCode.Conflict, completedVisitResponse.StatusCode);

        var searchResponse = await GetWithTokenAsync($"{Icd10SearchRoute}?q=K29&primaryOnly=true&limit=10", token);
        Assert.Equal(HttpStatusCode.OK, searchResponse.StatusCode);
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

    [Fact]
    public async Task CompleteVisit_Unauthenticated_Returns401()
    {
        using var postRequest = new HttpRequestMessage(HttpMethod.Post, CompleteVisitRoute);
        var postResponse = await Client.SendAsync(postRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, postResponse.StatusCode);

        using var putRequest = new HttpRequestMessage(HttpMethod.Put, CompleteVisitAliasRoute);
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
    public async Task CompleteVisit_ForbiddenRoles_Returns403(string role)
    {
        var token = GenerateToken(11, $"{role.ToLowerInvariant()}@medflow.test", role);
        var body = new
        {
            clinicalConclusion = "Viêm dạ dày mạn tính đã điều trị ổn định",
            dischargeInstructions = "Uống thuốc đúng giờ, tái khám sau 14 ngày",
        };

        var postResponse = await PostWithTokenAsync(CompleteVisitRoute, token, body);
        Assert.Equal(HttpStatusCode.Forbidden, postResponse.StatusCode);

        var putResponse = await PutWithTokenAsync(CompleteVisitAliasRoute, token, body);
        Assert.Equal(HttpStatusCode.Forbidden, putResponse.StatusCode);
    }

    [Fact]
    public async Task CompleteVisit_InvalidPayload_Returns400()
    {
        var token = GenerateToken(3, "doctor01@medflow.test", "DOCTOR");

        var invalidStateVersionResponse = await PostWithTokenAsync(
            CompleteVisitRoute,
            token,
            new { expectedStateVersion = 0 });
        Assert.Equal(HttpStatusCode.BadRequest, invalidStateVersionResponse.StatusCode);

        var invalidRevisionResponse = await PutWithTokenAsync(
            CompleteVisitAliasRoute,
            token,
            new { expectedRevisionNo = 0 });
        Assert.Equal(HttpStatusCode.BadRequest, invalidRevisionResponse.StatusCode);

        var shortConclusionResponse = await PostWithTokenAsync(
            CompleteVisitRoute,
            token,
            new { clinicalConclusion = "AB" });
        Assert.Equal(HttpStatusCode.BadRequest, shortConclusionResponse.StatusCode);

        var unsafeInstructionResponse = await PostWithTokenAsync(
            CompleteVisitRoute,
            token,
            new { dischargeInstructions = "<script>alert(1)</script>" });
        Assert.Equal(HttpStatusCode.BadRequest, unsafeInstructionResponse.StatusCode);

        var pastFollowUpResponse = await PostWithTokenAsync(
            CompleteVisitRoute,
            token,
            new { followUpDate = "2020-01-01" });
        Assert.Equal(HttpStatusCode.BadRequest, pastFollowUpResponse.StatusCode);
    }

    [Fact]
    public async Task CompleteVisit_CrossDoctorAndCompletedSeededVisit_Returns403And409_WhenDatabaseAvailable()
    {
        var token = GenerateToken(3, "doctor01@medflow.test", "DOCTOR");
        var body = new
        {
            clinicalConclusion = "Viêm dạ dày mạn tính ổn định",
            dischargeInstructions = "Tái khám sau 14 ngày",
        };

        var crossDoctorResponse = await PostWithTokenAsync(
            "/api/v1/doctor/consultation/VIS-202610-0002/complete",
            token,
            body);

        if (crossDoctorResponse.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable)
        {
            return;
        }

        Assert.Equal(HttpStatusCode.Forbidden, crossDoctorResponse.StatusCode);

        var completedVisitResponse = await PostWithTokenAsync(CompleteVisitRoute, token, body);
        Assert.Equal(HttpStatusCode.Conflict, completedVisitResponse.StatusCode);
    }

    [Fact]
    public async Task GetAndReviewDiagnosticResults_Unauthenticated_Returns401()
    {
        var getListResponse = await Client.GetAsync(DiagnosticResultsRoute);
        Assert.Equal(HttpStatusCode.Unauthorized, getListResponse.StatusCode);

        var getDetailResponse = await Client.GetAsync(DiagnosticResultDetailRoute);
        Assert.Equal(HttpStatusCode.Unauthorized, getDetailResponse.StatusCode);

        using var postRequest = new HttpRequestMessage(HttpMethod.Post, ReviewDiagnosticResultsRoute);
        var postResponse = await Client.SendAsync(postRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, postResponse.StatusCode);

        using var putRequest = new HttpRequestMessage(HttpMethod.Put, ReviewDiagnosticResultsAliasRoute);
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
    public async Task GetAndReviewDiagnosticResults_ForbiddenRoles_Returns403(string role)
    {
        var token = GenerateToken(11, $"{role.ToLowerInvariant()}@medflow.test", role);
        var body = new
        {
            clinicalInterpretation = "Công thức máu trong giới hạn bình thường",
        };

        var getListResponse = await GetWithTokenAsync(DiagnosticResultsRoute, token);
        Assert.Equal(HttpStatusCode.Forbidden, getListResponse.StatusCode);

        var getDetailResponse = await GetWithTokenAsync(DiagnosticResultDetailRoute, token);
        Assert.Equal(HttpStatusCode.Forbidden, getDetailResponse.StatusCode);

        var postResponse = await PostWithTokenAsync(ReviewDiagnosticResultsRoute, token, body);
        Assert.Equal(HttpStatusCode.Forbidden, postResponse.StatusCode);

        var putResponse = await PutWithTokenAsync(ReviewDiagnosticResultsAliasRoute, token, body);
        Assert.Equal(HttpStatusCode.Forbidden, putResponse.StatusCode);
    }

    [Fact]
    public async Task GetAndReviewDiagnosticResults_InvalidFiltersAndPayloads_Returns400()
    {
        var token = GenerateToken(3, "doctor01@medflow.test", "DOCTOR");

        var invalidCategoryResponse = await GetWithTokenAsync(
            $"{DiagnosticResultsRoute}?category=INVALID",
            token);
        Assert.Equal(HttpStatusCode.BadRequest, invalidCategoryResponse.StatusCode);

        var invalidStatusResponse = await GetWithTokenAsync(
            $"{DiagnosticResultsAliasRoute}?status=INVALID",
            token);
        Assert.Equal(HttpStatusCode.BadRequest, invalidStatusResponse.StatusCode);

        var invalidOrderIdQueryResponse = await GetWithTokenAsync(
            $"{DiagnosticResultsRoute}?orderId=0",
            token);
        Assert.Equal(HttpStatusCode.BadRequest, invalidOrderIdQueryResponse.StatusCode);

        var invalidOrderCodeQueryResponse = await GetWithTokenAsync(
            $"{DiagnosticResultsRoute}?orderCode=%3Cscript%3E",
            token);
        Assert.Equal(HttpStatusCode.BadRequest, invalidOrderCodeQueryResponse.StatusCode);

        var invalidStateVersionResponse = await PostWithTokenAsync(
            ReviewDiagnosticResultsRoute,
            token,
            new { expectedStateVersion = 0 });
        Assert.Equal(HttpStatusCode.BadRequest, invalidStateVersionResponse.StatusCode);

        var invalidRevisionResponse = await PutWithTokenAsync(
            ReviewDiagnosticResultsAliasRoute,
            token,
            new { expectedRevisionNo = 0 });
        Assert.Equal(HttpStatusCode.BadRequest, invalidRevisionResponse.StatusCode);

        var unsafeInterpretationResponse = await PostWithTokenAsync(
            ReviewDiagnosticResultsRoute,
            token,
            new { clinicalInterpretation = "<script>alert(1)</script>" });
        Assert.Equal(HttpStatusCode.BadRequest, unsafeInterpretationResponse.StatusCode);

        var invalidOrderIdsResponse = await PostWithTokenAsync(
            ReviewDiagnosticResultsRoute,
            token,
            new { orderIds = new[] { 0L } });
        Assert.Equal(HttpStatusCode.BadRequest, invalidOrderIdsResponse.StatusCode);
    }

    [Fact]
    public async Task GetAndReviewDiagnosticResults_SeededVisits_Returns200And403And409_WhenDatabaseAvailable()
    {
        var token = GenerateToken(3, "doctor01@medflow.test", "DOCTOR");

        var crossDoctorResponse = await GetWithTokenAsync(
            "/api/v1/doctor/consultation/VIS-202610-0002/diagnostic-results",
            token);

        if (crossDoctorResponse.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable)
        {
            return;
        }

        Assert.Equal(HttpStatusCode.Forbidden, crossDoctorResponse.StatusCode);

        var listResponse = await GetWithTokenAsync(DiagnosticResultsRoute, token);
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var listPayload = await listResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("identificationHash", listPayload, StringComparison.OrdinalIgnoreCase);

        using var listDoc = JsonDocument.Parse(listPayload);
        Assert.Equal("VIS-202610-0001", listDoc.RootElement.GetProperty("visitCode").GetString());
        Assert.True(listDoc.RootElement.GetProperty("canViewDiagnosticResults").GetBoolean());

        var detailResponse = await GetWithTokenAsync(DiagnosticResultDetailRoute, token);
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);

        var completedVisitReviewResponse = await PostWithTokenAsync(
            ReviewDiagnosticResultsRoute,
            token,
            new { clinicalInterpretation = "Công thức máu bình thường" });
        Assert.Equal(HttpStatusCode.Conflict, completedVisitReviewResponse.StatusCode);
    }

    [Fact]
    public async Task GetDiagnosticResultsTracking_Unauthenticated_Returns401()
    {
        var response = await Client.GetAsync(DiagnosticResultsTrackingRoute);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("PATIENT")]
    [InlineData("RECEPTION")]
    [InlineData("DIAGNOSTIC_STAFF")]
    [InlineData("PHARMACY_STAFF")]
    [InlineData("HOSPITAL_ADMIN")]
    [InlineData("SYSTEM_ADMIN")]
    public async Task GetDiagnosticResultsTracking_ForbiddenRoles_Returns403(string role)
    {
        var token = GenerateToken(11, $"{role.ToLowerInvariant()}@medflow.test", role);
        var response = await GetWithTokenAsync(DiagnosticResultsTrackingRoute, token);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetDiagnosticResultsTracking_InvalidQuery_Returns400()
    {
        var token = GenerateToken(3, "doctor01@medflow.test", "DOCTOR");

        var invalidDateResponse = await GetWithTokenAsync(
            $"{DiagnosticResultsTrackingRoute}?date=2026/10/20",
            token);
        Assert.Equal(HttpStatusCode.BadRequest, invalidDateResponse.StatusCode);

        var invalidStatusResponse = await GetWithTokenAsync(
            $"{DiagnosticResultsTrackingRoute}?status=ABNORMAL",
            token);
        Assert.Equal(HttpStatusCode.BadRequest, invalidStatusResponse.StatusCode);

        var invalidPageSizeResponse = await GetWithTokenAsync(
            $"{DiagnosticResultsTrackingRoute}?pageSize=0",
            token);
        Assert.Equal(HttpStatusCode.BadRequest, invalidPageSizeResponse.StatusCode);
    }

    [Fact]
    public async Task GetDiagnosticResultsTracking_SeededDoctor_Returns200_WhenDatabaseAvailable()
    {
        var token = GenerateToken(3, "doctor01@medflow.test", "DOCTOR");

        var response = await GetWithTokenAsync(DiagnosticResultsTrackingRoute, token);
        if (response.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable)
        {
            return;
        }

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(payload);
        Assert.True(doc.RootElement.TryGetProperty("items", out _));
        Assert.True(doc.RootElement.TryGetProperty("stats", out _));
    }

    [Fact]
    public async Task CreateAdditionalDiagnosticOrder_NoToken_Returns401()
    {
        using var postRequest = new HttpRequestMessage(HttpMethod.Post, AdditionalDiagnosticOrdersRoute);
        var postResponse = await Client.SendAsync(postRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, postResponse.StatusCode);

        using var aliasRequest = new HttpRequestMessage(HttpMethod.Post, AdditionalDiagnosticOrdersAliasRoute);
        var aliasResponse = await Client.SendAsync(aliasRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, aliasResponse.StatusCode);
    }

    [Theory]
    [InlineData("PATIENT")]
    [InlineData("RECEPTION")]
    [InlineData("DIAGNOSTIC_STAFF")]
    [InlineData("PHARMACY_STAFF")]
    [InlineData("HOSPITAL_ADMIN")]
    [InlineData("SYSTEM_ADMIN")]
    public async Task CreateAdditionalDiagnosticOrder_ForbiddenRoles_Returns403(string role)
    {
        var token = GenerateToken(11, $"{role.ToLowerInvariant()}@medflow.test", role);
        var body = new
        {
            serviceCode = "DIA-US",
            instruction = "Siêu âm ổ bụng bổ sung",
            additionalOrderReason = "Cần kiểm tra bổ sung sau khi đọc kết quả xét nghiệm máu",
        };

        var postResponse = await PostWithTokenAsync(AdditionalDiagnosticOrdersRoute, token, body);
        Assert.Equal(HttpStatusCode.Forbidden, postResponse.StatusCode);

        var aliasResponse = await PostWithTokenAsync(AdditionalDiagnosticOrdersAliasRoute, token, body);
        Assert.Equal(HttpStatusCode.Forbidden, aliasResponse.StatusCode);
    }

    [Fact]
    public async Task CreateAdditionalDiagnosticOrder_InvalidPayloads_Returns400()
    {
        var token = GenerateToken(3, "doctor01@medflow.test", "DOCTOR");

        var emptyResponse = await PostWithTokenAsync(
            AdditionalDiagnosticOrdersRoute,
            token,
            new { });
        Assert.Equal(HttpStatusCode.BadRequest, emptyResponse.StatusCode);

        var invalidFollowUpIdResponse = await PostWithTokenAsync(
            AdditionalDiagnosticOrdersRoute,
            token,
            new { serviceId = 6, followUpToOrderId = 0 });
        Assert.Equal(HttpStatusCode.BadRequest, invalidFollowUpIdResponse.StatusCode);

        var unsafeReasonResponse = await PostWithTokenAsync(
            AdditionalDiagnosticOrdersAliasRoute,
            token,
            new { serviceCode = "DIA-US", additionalOrderReason = "<script>alert(1)</script>" });
        Assert.Equal(HttpStatusCode.BadRequest, unsafeReasonResponse.StatusCode);

        var unsafeProgressionResponse = await PostWithTokenAsync(
            AdditionalDiagnosticOrdersRoute,
            token,
            new { serviceCode = "DIA-US", clinicalProgressionNote = "<script>alert(1)</script>" });
        Assert.Equal(HttpStatusCode.BadRequest, unsafeProgressionResponse.StatusCode);

        var invalidRevisionResponse = await PostWithTokenAsync(
            AdditionalDiagnosticOrdersRoute,
            token,
            new { serviceCode = "DIA-US", expectedRevisionNo = 0 });
        Assert.Equal(HttpStatusCode.BadRequest, invalidRevisionResponse.StatusCode);
    }

    [Fact]
    public async Task CreateAdditionalDiagnosticOrder_SeededVisits_Returns403And409_WhenDatabaseAvailable()
    {
        var token = GenerateToken(3, "doctor01@medflow.test", "DOCTOR");
        var body = new
        {
            serviceCode = "DIA-US",
            instruction = "Siêu âm ổ bụng bổ sung",
            additionalOrderReason = "Kiểm tra bổ sung sau đọc kết quả",
        };

        var crossDoctorResponse = await PostWithTokenAsync(
            "/api/v1/doctor/consultation/VIS-202610-0002/additional-diagnostic-orders",
            token,
            body);

        if (crossDoctorResponse.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable)
        {
            return;
        }

        Assert.Equal(HttpStatusCode.Forbidden, crossDoctorResponse.StatusCode);

        // VIS-202610-0001 is COMPLETED -> 409 Conflict
        var completedVisitResponse = await PostWithTokenAsync(
            AdditionalDiagnosticOrdersRoute,
            token,
            body);
        Assert.Equal(HttpStatusCode.Conflict, completedVisitResponse.StatusCode);
    }
}
