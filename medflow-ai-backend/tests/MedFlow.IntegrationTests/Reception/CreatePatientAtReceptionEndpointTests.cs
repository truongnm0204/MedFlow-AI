using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MedFlow.Application.DTOs.Reception;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Entities;
using MedFlow.IntegrationTests.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace MedFlow.IntegrationTests.Reception;

public sealed class CreatePatientAtReceptionEndpointTests : AuthTestBase
{
    private const string Url = "/api/v1/reception/patients";

    public CreatePatientAtReceptionEndpointTests(TestWebAppFactory factory)
        : base(factory)
    {
    }

    private string GenerateToken(string role, long userId = 5)
    {
        using var scope = Factory.Services.CreateScope();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var user = new User { Id = userId, Email = $"{role.ToLowerInvariant()}@medflow.test" };
        return jwt.GenerateAccessToken(user, new[] { role }).AccessToken;
    }

    private HttpRequestMessage AuthedPost(CreatePatientAtReceptionRequest payload, string role = "RECEPTION")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Url) { Content = JsonContent.Create(payload) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GenerateToken(role));
        return request;
    }

    private static string RandomCccd() =>
        string.Concat(Guid.NewGuid().ToByteArray().Select(b => (b % 10).ToString())).Substring(0, 12);

    private static CreatePatientAtReceptionRequest ValidPayload(string? cccd = null) => new()
    {
        FullName = "Trần Thị Bình",
        DateOfBirth = new DateOnly(1992, 3, 4),
        Gender = "FEMALE",
        Phone = "0987654321",
        Address = "45 Nguyễn Huệ, Quận 1, TP.HCM",
        IdentityCard = cccd ?? RandomCccd(),
        Email = "binh@example.com",
        BloodType = "A+",
    };

    [Fact]
    public async Task Create_NoToken_Returns401()
    {
        var response = await Client.PostAsJsonAsync(Url, ValidPayload());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("PATIENT")]
    [InlineData("DOCTOR")]
    [InlineData("PHARMACY_STAFF")]
    public async Task Create_UnauthorizedRole_Returns403(string role)
    {
        using var request = AuthedPost(ValidPayload(), role);
        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("RECEPTION")]
    [InlineData("HOSPITAL_ADMIN")]
    [InlineData("SYSTEM_ADMIN")]
    public async Task Create_AllowedRole_Returns201WithMaskedIdentity(string role)
    {
        var payload = ValidPayload();
        using var request = AuthedPost(payload, role);
        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CreatePatientResponseDto>();
        Assert.NotNull(body);
        Assert.True(body.Id > 0);
        Assert.StartsWith($"BN-{DateTime.UtcNow.Year}-", body.PatientCode);
        Assert.Equal("Trần Thị Bình", body.FullName);
        Assert.Equal("********" + payload.IdentityCard![^4..], body.IdentityCardMasked);
    }

    [Fact]
    public async Task Create_InvalidPhone_Returns400()
    {
        var payload = ValidPayload();
        payload.Phone = "0123456789";
        using var request = AuthedPost(payload);
        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_FutureDateOfBirth_Returns400ProblemDetails()
    {
        var payload = ValidPayload();
        payload.DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(2);
        using var request = AuthedPost(payload);
        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal(400, problem?.Status);
    }

    [Fact]
    public async Task Create_DuplicateIdentityCard_Returns409()
    {
        var cccd = RandomCccd();
        using var first = AuthedPost(ValidPayload(cccd));
        Assert.Equal(HttpStatusCode.Created, (await Client.SendAsync(first)).StatusCode);

        using var second = AuthedPost(ValidPayload(cccd));
        var response = await Client.SendAsync(second);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal(409, problem?.Status);
    }
}
