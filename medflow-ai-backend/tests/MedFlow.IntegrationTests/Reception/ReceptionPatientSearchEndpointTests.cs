using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MedFlow.Application.DTOs.Reception;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Entities;
using MedFlow.IntegrationTests.Auth;
using MedFlow.Domain.Exceptions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MedFlow.IntegrationTests.Reception;

public sealed class ReceptionPatientSearchEndpointTests : AuthTestBase
{
    private const string Url = "/api/v1/reception/patients/search";

    public ReceptionPatientSearchEndpointTests(TestWebAppFactory factory)
        : base(factory)
    {
    }

    private sealed class StubSearchService : IReceptionPatientSearchService
    {
        public Task<SearchPatientsResponseDto> SearchPatientsAsync(
            SearchPatientsRequest request,
            CancellationToken ct = default,
            long? actorUserId = null)
        {
            if (request.Tab == "BOGUS")
                throw new ValidationException("Tab không hợp lệ.");

            return Task.FromResult(new SearchPatientsResponseDto(
                [],
                0,
                request.Page,
                request.PageSize,
                new SearchTabCountsDto(0, 0, 0, 0),
                new ReceptionDashboardSummaryDto(3, 2, 1, 4)));
        }

        public Task<ReceptionDashboardSummaryDto> GetDashboardSummaryAsync(
            long actorUserId,
            CancellationToken ct = default)
            => Task.FromResult(new ReceptionDashboardSummaryDto(3, 2, 1, 4));
    }

    private HttpClient CreateStubbedClient() =>
        Factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.RemoveAll<IReceptionPatientSearchService>();
            services.AddScoped<IReceptionPatientSearchService, StubSearchService>();
        })).CreateClient();

    private HttpRequestMessage AuthedGet(string url, long userId, string email, string role)
    {
        using var scope = Factory.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var token = jwtService.GenerateAccessToken(new User { Id = userId, Email = email }, [role]).AccessToken;
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    [Fact]
    public async Task Search_NoToken_Returns401()
    {
        var response = await Client.GetAsync(Url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Search_PatientRole_Returns403()
    {
        using var request = AuthedGet(Url, 1, "patient01@medflow.test", "PATIENT");
        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/reception/patients/search")]
    [InlineData("/api/v1/reception/patients")]
    public async Task Search_ReceptionRole_Returns200(string url)
    {
        using var request = AuthedGet(url + "?tab=ALL&page=1&pageSize=5", 5, "reception01@medflow.test", "RECEPTION");
        var response = await CreateStubbedClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SearchPatientsResponseDto>();
        Assert.NotNull(body);
        Assert.Equal(5, body.PageSize);
    }

    [Fact]
    public async Task Search_InvalidTab_Returns400()
    {
        using var request = AuthedGet(Url + "?tab=BOGUS", 5, "reception01@medflow.test", "RECEPTION");
        var response = await CreateStubbedClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DashboardSummary_NoToken_Returns401_AndPatientRole_Returns403()
    {
        var unauth = await Client.GetAsync("/api/v1/reception/dashboard-summary");
        Assert.Equal(HttpStatusCode.Unauthorized, unauth.StatusCode);

        using var patientReq = AuthedGet("/api/v1/reception/dashboard-summary", 1, "patient01@medflow.test", "PATIENT");
        var forbidden = await Client.SendAsync(patientReq);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task DashboardSummary_SeededReceptionStaff_Returns200MatchingEmbeddedSearchSummary()
    {
        var tokens = await LoginAsync("reception01@medflow.test", "Demo@12345");

        using var summaryReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/reception/dashboard-summary");
        summaryReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var summaryRes = await Client.SendAsync(summaryReq);
        Assert.Equal(HttpStatusCode.OK, summaryRes.StatusCode);
        var summary = await summaryRes.Content.ReadFromJsonAsync<ReceptionDashboardSummaryDto>();
        Assert.NotNull(summary);

        using var searchReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/reception/patients/search?tab=ALL&page=1&pageSize=5");
        searchReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var searchRes = await Client.SendAsync(searchReq);
        Assert.Equal(HttpStatusCode.OK, searchRes.StatusCode);
        var searchBody = await searchRes.Content.ReadFromJsonAsync<SearchPatientsResponseDto>();
        Assert.NotNull(searchBody);
        Assert.Equal(summary, searchBody.DashboardSummary);
    }
}
