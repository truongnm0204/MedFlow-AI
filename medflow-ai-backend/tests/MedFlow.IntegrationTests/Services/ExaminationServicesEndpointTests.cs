using System.Net;
using System.Net.Http.Json;
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
    public async Task GetExaminationServiceByIdOrCode_ValidCode_Returns200WithListedPrice()
    {
        var response = await _client.GetAsync("/api/v1/examination-services/EX-GI-01");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<ExaminationServiceDto>();
        Assert.NotNull(dto);
        Assert.Equal("1", dto.Id);
        Assert.Equal("EX-GI-01", dto.ServiceCode);
        Assert.Equal(200_000m, dto.Price);
        Assert.Equal("200.000 VNĐ", dto.PriceFormatted);
    }

    [Fact]
    public async Task GetExaminationServiceByIdOrCode_UnknownCode_Returns404ProblemDetails()
    {
        var response = await _client.GetAsync("/api/v1/examination-services/NON-EXISTENT-CODE");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(404, problem.Status);
    }
}
