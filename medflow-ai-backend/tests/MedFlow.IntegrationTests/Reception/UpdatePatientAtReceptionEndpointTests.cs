using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MedFlow.Application.DTOs.Reception;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Entities;
using MedFlow.IntegrationTests.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MedFlow.IntegrationTests.Reception;

public sealed class UpdatePatientAtReceptionEndpointTests : AuthTestBase
{
    public UpdatePatientAtReceptionEndpointTests(TestWebAppFactory factory)
        : base(factory)
    {
    }

    private static UpdatePatientAtReceptionRequest ValidPayload(string? identityCard = null) => new()
    {
        FullName = "Lê Minh Châu",
        DateOfBirth = new DateOnly(1992, 3, 4),
        Gender = "FEMALE",
        Phone = "0987654321",
        Address = "88 Nguyễn Huệ, TP.HCM",
        IdentityCard = identityCard,
        Email = "chau@medflow.test",
        BloodType = "B+",
    };

    private HttpRequestMessage AuthedPut(
        string url,
        object? payload,
        string role = "RECEPTION",
        long userId = 5,
        string email = "reception01@medflow.test")
    {
        using var scope = Factory.Services.CreateScope();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var token = jwt.GenerateAccessToken(new User { Id = userId, Email = email }, [role]).AccessToken;
        var request = new HttpRequestMessage(HttpMethod.Put, url)
        {
            Content = JsonContent.Create(payload),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private async Task<Patient> SeedPatientAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var patient = new Patient
        {
            PatientCode = $"PAT-T{suffix}",
            FullName = "Original Name",
            Phone = "0900000000",
            Address = "Original address",
            Status = "ACTIVE",
        };
        Db.Patients.Add(patient);
        await Db.SaveChangesAsync();
        return patient;
    }

    [Fact]
    public async Task UpdatePatient_NoToken_Returns401()
    {
        var response = await Client.PutAsJsonAsync("/api/v1/reception/patients/1", ValidPayload());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("PATIENT", 1L, "patient01@medflow.test")]
    [InlineData("DOCTOR", 3L, "doctor01@medflow.test")]
    [InlineData("PHARMACY_STAFF", 9L, "pharmacy_staff01@medflow.test")]
    public async Task UpdatePatient_DisallowedRole_Returns403(string role, long userId, string email)
    {
        using var request = AuthedPut("/api/v1/reception/patients/1", ValidPayload(), role, userId, email);
        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdatePatient_InvalidPhone_Returns400()
    {
        var payload = new UpdatePatientAtReceptionRequest
        {
            FullName = "Lê Minh Châu",
            Phone = "123",
            Address = "88 Nguyễn Huệ",
        };
        using var request = AuthedPut("/api/v1/reception/patients/1", payload);
        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdatePatient_UnknownPatient_Returns404()
    {
        using var request = AuthedPut("/api/v1/reception/patients/PAT-DOES-NOT-EXIST", ValidPayload());
        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal(404, problem?.Status);
    }

    [Theory]
    [InlineData("RECEPTION")]
    [InlineData("HOSPITAL_ADMIN")]
    [InlineData("SYSTEM_ADMIN")]
    public async Task UpdatePatient_ValidPayload_Returns200AndPersists(string role)
    {
        var patient = await SeedPatientAsync();

        using var request = AuthedPut($"/api/v1/reception/patients/{patient.PatientCode}", ValidPayload(), role);
        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<UpdatePatientResponseDto>();
        Assert.NotNull(body);
        Assert.Equal(patient.Id, body.Id);
        Assert.Equal(patient.PatientCode, body.PatientCode);
        Assert.Equal("Lê Minh Châu", body.FullName);
        Assert.Equal("0987654321", body.Phone);

        var stored = await Db.Patients.AsNoTracking().SingleAsync(p => p.Id == patient.Id);
        Assert.Equal("Lê Minh Châu", stored.FullName);
        Assert.Equal(patient.PatientCode, stored.PatientCode);
    }

    [Fact]
    public async Task UpdatePatient_DuplicateIdentityCard_Returns409()
    {
        var first = await SeedPatientAsync();
        var second = await SeedPatientAsync();
        var card = Random.Shared.NextInt64(100_000_000_000, 999_999_999_999).ToString();

        using var ok = AuthedPut($"/api/v1/reception/patients/{first.Id}", ValidPayload(card));
        Assert.Equal(HttpStatusCode.OK, (await Client.SendAsync(ok)).StatusCode);

        using var conflict = AuthedPut($"/api/v1/reception/patients/{second.Id}", ValidPayload(card));
        var response = await Client.SendAsync(conflict);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
}
