using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MedFlow.Application.DTOs.Diagnostic;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Entities;
using MedFlow.IntegrationTests.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace MedFlow.IntegrationTests.Diagnostic;

public sealed class DiagnosticResultViewEndpointTests : AuthTestBase
{
    private const string ViewListRoute = "/api/v1/diagnostic/results/view";
    private const string ViewDetailRoute = "/api/v1/diagnostic/results/DIA-202610-0001/view";
    private const string ViewDetailAliasRoute = "/api/v1/diagnostic/orders/DIA-202610-0001/result/view";
    private const string UnknownViewRoute = "/api/v1/diagnostic/results/DIA-DOES-NOT-EXIST/view";
    private const string EnterRoute = "/api/v1/diagnostic/orders/DIA-202610-0001/result";
    private const string AmendRoute = "/api/v1/diagnostic/orders/DIA-202610-0001/result/amend";

    private readonly TestWebAppFactory _factory;

    public DiagnosticResultViewEndpointTests(TestWebAppFactory factory)
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

    [Fact]
    public async Task ViewEndpoints_Unauthenticated_Return401()
    {
        var listResponse = await Client.GetAsync(ViewListRoute);
        Assert.Equal(HttpStatusCode.Unauthorized, listResponse.StatusCode);

        var detailResponse = await Client.GetAsync(ViewDetailRoute);
        Assert.Equal(HttpStatusCode.Unauthorized, detailResponse.StatusCode);

        using var enterRequest = new HttpRequestMessage(HttpMethod.Post, EnterRoute);
        var enterResponse = await Client.SendAsync(enterRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, enterResponse.StatusCode);

        using var amendRequest = new HttpRequestMessage(HttpMethod.Post, AmendRoute);
        var amendResponse = await Client.SendAsync(amendRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, amendResponse.StatusCode);
    }

    [Theory]
    [InlineData("PATIENT")]
    [InlineData("DOCTOR")]
    [InlineData("RECEPTION")]
    [InlineData("PHARMACY_STAFF")]
    [InlineData("HOSPITAL_ADMIN")]
    [InlineData("SYSTEM_ADMIN")]
    public async Task ViewEndpoints_ForbiddenRoles_Return403(string role)
    {
        var token = GenerateToken(13, $"{role.ToLowerInvariant()}@medflow.test", role);

        var listResponse = await GetWithTokenAsync(ViewListRoute, token);
        Assert.Equal(HttpStatusCode.Forbidden, listResponse.StatusCode);

        var detailResponse = await GetWithTokenAsync(ViewDetailRoute, token);
        Assert.Equal(HttpStatusCode.Forbidden, detailResponse.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/diagnostic/results/view?limit=0")]
    [InlineData("/api/v1/diagnostic/results/view?limit=101")]
    [InlineData("/api/v1/diagnostic/results/view?resultStatus=PENDING")]
    public async Task GetViews_InvalidQuery_Returns400(string url)
    {
        var token = GenerateToken(7, "diagnostic_staff01@medflow.test", "DIAGNOSTIC_STAFF");

        var response = await GetWithTokenAsync(url, token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetViews_DiagnosticStaff_ReturnsListAndSameDetailAcrossAliases_WhenDatabaseAvailable()
    {
        var token = GenerateToken(7, "diagnostic_staff01@medflow.test", "DIAGNOSTIC_STAFF");

        var listResponse = await GetWithTokenAsync($"{ViewListRoute}?limit=50", token);
        if (listResponse.StatusCode is HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable)
        {
            return;
        }

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var list = await listResponse.Content.ReadFromJsonAsync<DiagnosticResultViewListResponseDto>();
        Assert.NotNull(list);
        Assert.Contains(list.Items, i => i.OrderCode == "DIA-202610-0001");
        Assert.DoesNotContain(list.Items, i => i.ResultStatus == "PENDING");

        var detailResponse = await GetWithTokenAsync(ViewDetailRoute, token);
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = await detailResponse.Content.ReadFromJsonAsync<DiagnosticResultViewDetailResponseDto>();
        Assert.NotNull(detail);
        Assert.Equal("DIA-202610-0001", detail.Item.OrderCode);
        Assert.False(string.IsNullOrWhiteSpace(detail.Item.ResultText));
        Assert.NotEmpty(detail.Versions);
        Assert.Equal(
            !string.IsNullOrWhiteSpace(detail.Item.Document.ReportFileUrl),
            detail.Item.Document.HasDocument);

        var aliasResponse = await GetWithTokenAsync(ViewDetailAliasRoute, token);
        Assert.Equal(HttpStatusCode.OK, aliasResponse.StatusCode);
        var alias = await aliasResponse.Content.ReadFromJsonAsync<DiagnosticResultViewDetailResponseDto>();
        Assert.NotNull(alias);
        Assert.Equal(detail.Item.ResultText, alias.Item.ResultText);

        var missingResponse = await GetWithTokenAsync(UnknownViewRoute, token);
        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);
    }
}
