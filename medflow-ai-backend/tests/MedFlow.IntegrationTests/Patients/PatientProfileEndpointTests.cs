using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MedFlow.Application.DTOs.Patients;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.IntegrationTests.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MedFlow.IntegrationTests.Patients;

public sealed class PatientProfileEndpointTests : AuthTestBase
{
    public PatientProfileEndpointTests(TestWebAppFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task GetMyProfile_Unauthenticated_Returns401()
    {
        var response = await Client.GetAsync("/api/v1/patients/me/profile");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetMyProfile_AuthenticatedPatient_Returns200WithOwnProfile()
    {
        var (email, tokens) = await RegisterVerifyLoginAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/patients/me/profile");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var profile = await response.Content.ReadFromJsonAsync<PatientProfileResponse>();
        Assert.NotNull(profile);
        Assert.Equal(email, profile.Email);
        Assert.Equal("Nguyễn Văn Test", profile.FullName);
        Assert.Equal("MALE", profile.Gender);
        Assert.Equal("0912345678", profile.Phone);
        Assert.True(profile.IsEmailVerified);
        Assert.False(profile.IsIdentityVerified);
        Assert.Null(profile.MaskedIdentification);
        Assert.Equal("ACTIVE", profile.Status);
        Assert.Contains("UPDATE_PERSONAL_PROFILE", profile.AllowedActions);
        Assert.Contains("MANAGE_FAMILY_PROFILES", profile.AllowedActions);
        Assert.Contains("DOWNLOAD_PROFILE_SUMMARY", profile.AllowedActions);
    }

    [Fact]
    public async Task GetMyProfile_SeededDemoPatient_Returns200WithMaskedIdentityAndBloodType()
    {
        var tokens = await LoginAsync("patient01@medflow.test", "Demo@12345");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/patients/me/profile");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var profile = await response.Content.ReadFromJsonAsync<PatientProfileResponse>();
        Assert.NotNull(profile);
        Assert.Equal("PAT-0001", profile.PatientCode);
        Assert.Equal("Nguyễn Văn An", profile.FullName);
        Assert.Equal("A+", profile.BloodType);
        Assert.True(profile.IsIdentityVerified);
        Assert.Equal("****** (Đã xác thực CCCD)", profile.MaskedIdentification);
        Assert.DoesNotContain("d4dac994", profile.MaskedIdentification);
    }

    [Fact]
    public async Task GetMyProfile_NonPatientRole_Returns403Forbidden()
    {
        var doctorTokens = await LoginAsync("doctor01@medflow.test", "Demo@12345");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/patients/me/profile");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", doctorTokens.AccessToken);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetMyProfile_PatientRoleWithoutPatientRecord_Returns404ProblemDetails()
    {
        var patientRole = await Db.Roles.FirstAsync(r => r.Code == "PATIENT");
        var orphanUser = new User
        {
            Email = UniqueEmail(),
            AuthProvider = "LOCAL",
            AccountStatus = AccountStatus.ACTIVE,
            EmailVerifiedAt = DateTime.UtcNow,
        };

        Db.Users.Add(orphanUser);
        await Db.SaveChangesAsync();

        Db.UserRoles.Add(new UserRole
        {
            UserId = orphanUser.Id,
            RoleId = patientRole.Id,
        });
        await Db.SaveChangesAsync();

        var jwtService = Factory.Services.GetRequiredService<IJwtTokenService>();
        var tokenResult = jwtService.GenerateAccessToken(orphanUser, ["PATIENT"]);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/patients/me/profile");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenResult.AccessToken);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(404, problem.Status);
    }

    [Fact]
    public async Task GetMyProfile_TwoDistinctSeededPatients_EnforcesUserIsolation()
    {
        var patient1Tokens = await LoginAsync("patient01@medflow.test", "Demo@12345");
        var patient2Tokens = await LoginAsync("patient02@medflow.test", "Demo@12345");

        using var req1 = new HttpRequestMessage(HttpMethod.Get, "/api/v1/patients/me/profile");
        req1.Headers.Authorization = new AuthenticationHeaderValue("Bearer", patient1Tokens.AccessToken);
        var res1 = await Client.SendAsync(req1);
        var prof1 = await res1.Content.ReadFromJsonAsync<PatientProfileResponse>();

        using var req2 = new HttpRequestMessage(HttpMethod.Get, "/api/v1/patients/me/profile");
        req2.Headers.Authorization = new AuthenticationHeaderValue("Bearer", patient2Tokens.AccessToken);
        var res2 = await Client.SendAsync(req2);
        var prof2 = await res2.Content.ReadFromJsonAsync<PatientProfileResponse>();

        Assert.NotNull(prof1);
        Assert.NotNull(prof2);
        Assert.NotEqual(prof1.Id, prof2.Id);
        Assert.Equal("PAT-0001", prof1.PatientCode);
        Assert.Equal("PAT-0002", prof2.PatientCode);
        Assert.Equal("Trần Thị Bình", prof2.FullName);
    }

    [Fact]
    public async Task GetMyProfile_LockedUserToken_Returns401ProblemDetails()
    {
        var patientRole = await Db.Roles.FirstAsync(r => r.Code == "PATIENT");
        var lockedUser = new User
        {
            Email = UniqueEmail(),
            AuthProvider = "LOCAL",
            AccountStatus = AccountStatus.LOCKED,
            EmailVerifiedAt = DateTime.UtcNow,
        };

        Db.Users.Add(lockedUser);
        await Db.SaveChangesAsync();

        Db.UserRoles.Add(new UserRole { UserId = lockedUser.Id, RoleId = patientRole.Id });
        Db.Patients.Add(new Patient
        {
            UserId = lockedUser.Id,
            PatientCode = $"PAT-LOCK-{Guid.NewGuid():N}"[..20],
            FullName = "Locked Patient",
            Status = "ACTIVE",
        });
        await Db.SaveChangesAsync();

        var jwtService = Factory.Services.GetRequiredService<IJwtTokenService>();
        var tokenResult = jwtService.GenerateAccessToken(lockedUser, ["PATIENT"]);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/patients/me/profile");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenResult.AccessToken);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(401, problem.Status);
    }
}
