using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MedFlow.Application.DTOs.Auth;
using MedFlow.Application.DTOs.Services;
using MedFlow.IntegrationTests.Auth;
using Microsoft.AspNetCore.Mvc;

namespace MedFlow.IntegrationTests.Services;

public sealed class ExaminationServicesEndpointTests : IClassFixture<TestWebAppFactory>
{
    private readonly HttpClient _client;

    public ExaminationServicesEndpointTests(TestWebAppFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetExaminationServices_DefaultQuery_ReturnsSeededExaminationServicesOnly()
    {
        var response = await _client.GetAsync("/api/v1/examination-services");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ServiceSearchResponseDto>();
        Assert.NotNull(body);
        Assert.Equal(4, body.Total);
        Assert.Equal(1, body.Page);
        Assert.Equal(6, body.PageSize);
        Assert.Equal(1, body.TotalPages);
        Assert.Equal(
            ["EX-GI-01", "EX-CARD-01", "EX-GI-02", "EX-CARD-02"],
            body.Data.Select(s => s.ServiceCode).ToArray());
    }

    [Fact]
    public async Task GetExaminationServices_WithDepartmentAndPriceFilters_ReturnsFilteredSubset()
    {
        var response = await _client.GetAsync(
            "/api/v1/examination-services?department=dept-th&priceDurationRange=under-200k");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ServiceSearchResponseDto>();
        Assert.NotNull(body);
        Assert.Equal(2, body.Total);
        Assert.Equal(["EX-GI-01", "EX-GI-02"], body.Data.Select(s => s.ServiceCode).ToArray());
    }

    [Fact]
    public async Task GetExaminationServices_InvalidPageSize_Returns400ProblemDetails()
    {
        var response = await _client.GetAsync("/api/v1/examination-services?pageSize=100");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(400, problem.Status);
    }

    [Fact]
    public async Task GetExaminationServiceByIdOrCode_ValidCode_Returns200WithListedPriceAndRoomLocation()
    {
        var response = await _client.GetAsync("/api/v1/examination-services/EX-GI-01");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<ExaminationServiceDto>();
        Assert.NotNull(dto);
        Assert.Equal("1", dto.Id);
        Assert.Equal("EX-GI-01", dto.ServiceCode);
        Assert.Equal("Khoa Tiêu hóa", dto.DepartmentName);
        Assert.Equal("Khu Khám Ngoại trú - Tầng 1, Khu Demo (Phòng 101-102)", dto.Location);
        Assert.Equal(25, dto.DurationMinutes);
        Assert.Equal(200_000m, dto.Price);
        Assert.Equal("200.000 VNĐ", dto.PriceFormatted);
        Assert.NotNull(dto.PreparationNotes);
        Assert.NotEmpty(dto.PreparationNotes!);
        Assert.False(string.IsNullOrWhiteSpace(dto.TargetAudience));
        Assert.Equal(4, dto.StandardSteps.Count);
    }

    [Theory]
    [InlineData("1", "EX-GI-01", "Khu Khám Ngoại trú - Tầng 1, Khu Demo (Phòng 101-102)")]
    [InlineData("EX-CARD-01", "EX-CARD-01", "Khu Khám Ngoại trú - Tầng 1, Khu Demo (Phòng 201-202)")]
    [InlineData("srv-th-01", "EX-GI-01", "Khu Khám Ngoại trú - Tầng 1, Khu Demo (Phòng 101-102)")]
    [InlineData("DV-TM-01", "EX-CARD-01", "Khu Khám Ngoại trú - Tầng 1, Khu Demo (Phòng 201-202)")]
    public async Task GetExaminationServiceByIdOrCode_ResolvesByIdCodeSlugOrDisplayCode(
        string idOrCode,
        string expectedServiceCode,
        string expectedLocation)
    {
        var response = await _client.GetAsync($"/api/v1/examination-services/{idOrCode}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<ExaminationServiceDto>();
        Assert.NotNull(dto);
        Assert.Equal(expectedServiceCode, dto.ServiceCode);
        Assert.Equal(expectedLocation, dto.Location);
    }

    [Fact]
    public async Task GetExaminationServiceByIdOrCode_AuthenticatedPatient_Returns200()
    {
        var loginResponse = await _client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest { Email = "patient01@medflow.test", Password = "Demo@12345" });
        loginResponse.EnsureSuccessStatusCode();
        var tokens = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(tokens);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/examination-services/EX-GI-01");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<ExaminationServiceDto>();
        Assert.NotNull(dto);
        Assert.Equal("EX-GI-01", dto.ServiceCode);
    }

    [Fact]
    public async Task GetExaminationServiceByIdOrCode_InvalidIdentifier_Returns400ProblemDetails()
    {
        var tooLongCode = new string('A', 65);
        var response = await _client.GetAsync($"/api/v1/examination-services/{tooLongCode}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(400, problem.Status);
    }

    [Fact]
    public async Task GetExaminationServiceByIdOrCode_UnknownOrDiagnosticCode_Returns404ProblemDetails()
    {
        var unknownResponse = await _client.GetAsync("/api/v1/examination-services/NON-EXISTENT-CODE");
        Assert.Equal(HttpStatusCode.NotFound, unknownResponse.StatusCode);
        var unknownProblem = await unknownResponse.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(unknownProblem);
        Assert.Equal(404, unknownProblem.Status);

        var diagnosticResponse = await _client.GetAsync("/api/v1/examination-services/DIA-CBC");
        Assert.Equal(HttpStatusCode.NotFound, diagnosticResponse.StatusCode);
        var diagnosticProblem = await diagnosticResponse.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(diagnosticProblem);
        Assert.Equal(404, diagnosticProblem.Status);
    }
}
