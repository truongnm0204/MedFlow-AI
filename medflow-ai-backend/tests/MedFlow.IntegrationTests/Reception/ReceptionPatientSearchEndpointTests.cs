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
        public Task<SearchPatientsResponseDto> SearchPatientsAsync(SearchPatientsRequest request, CancellationToken ct = default)
        {
            if (request.Tab == "BOGUS")
                throw new ValidationException("Tab không hợp lệ.");

            return Task.FromResult(new SearchPatientsResponseDto(
                [],
                0,
                request.Page,
                request.PageSize,
                new SearchTabCountsDto(0, 0, 0, 0)));
        }
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
}
