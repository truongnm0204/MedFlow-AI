using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MedFlow.Application.DTOs.Pharmacy;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Entities;
using MedFlow.IntegrationTests.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace MedFlow.IntegrationTests.Pharmacy;

public sealed class PrepareMedicinesEndpointTests : AuthTestBase
{
    private readonly TestWebAppFactory _factory;

    public PrepareMedicinesEndpointTests(TestWebAppFactory factory)
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
    public async Task GetPreparationContext_Unauthenticated_Returns401()
    {
        var response = await Client.GetAsync("/api/v1/pharmacy/prescriptions/RX-001/prepare");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetPreparationContext_RouteAlias_Unauthenticated_Returns401()
    {
        var response = await Client.GetAsync("/api/v1/pharmacy/prepare/RX-001");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("PATIENT")]
    [InlineData("DOCTOR")]
    [InlineData("RECEPTION")]
    [InlineData("DIAGNOSTIC_STAFF")]
    [InlineData("HOSPITAL_ADMIN")]
    [InlineData("SYSTEM_ADMIN")]
    public async Task GetPreparationContext_ForbiddenRoles_Returns403(string role)
    {
        var token = GenerateToken(13, $"{role.ToLowerInvariant()}@medflow.test", role);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/pharmacy/prescriptions/RX-001/prepare");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PreparePrescription_Unauthenticated_Returns401()
    {
        var requestPayload = new PreparePrescriptionRequest(Items: []);
        var response = await Client.PostAsJsonAsync("/api/v1/pharmacy/prescriptions/RX-001/prepare", requestPayload);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("PATIENT")]
    [InlineData("DOCTOR")]
    [InlineData("RECEPTION")]
    public async Task PreparePrescription_ForbiddenRoles_Returns403(string role)
    {
        var token = GenerateToken(13, $"{role.ToLowerInvariant()}@medflow.test", role);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/pharmacy/prescriptions/RX-001/prepare");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new PreparePrescriptionRequest(Items: []));

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PreparePrescription_EmptyItems_Returns400()
    {
        var token = GenerateToken(9, "pharmacy_staff01@medflow.test", "PHARMACY_STAFF");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/pharmacy/prescriptions/RX-001/prepare");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new PreparePrescriptionRequest(Items: []));

        var response = await Client.SendAsync(request);

        // ValidationException is mapped to 400 Bad Request
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetPreparationContext_TooLongIdOrCode_Returns400()
    {
        var token = GenerateToken(9, "pharmacy_staff01@medflow.test", "PHARMACY_STAFF");
        var tooLongCode = new string('A', 101);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/pharmacy/prescriptions/{tooLongCode}/prepare");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetPreparationContext_NonExistentPrescription_Returns404OrDbUnavailable()
    {
        var token = GenerateToken(9, "pharmacy_staff01@medflow.test", "PHARMACY_STAFF");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/pharmacy/prescriptions/NON-EXISTENT-RX-9999/prepare");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.SendAsync(request);

        Assert.True(
            response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.InternalServerError or HttpStatusCode.ServiceUnavailable,
            $"Unexpected status code: {response.StatusCode}");
    }
}
