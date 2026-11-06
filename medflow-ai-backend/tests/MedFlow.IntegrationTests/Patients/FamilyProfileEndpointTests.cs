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

public sealed class FamilyProfileEndpointTests : AuthTestBase
{
    public FamilyProfileEndpointTests(TestWebAppFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task GetMyFamilyMembers_Unauthenticated_Returns401()
    {
        var response = await Client.GetAsync("/api/v1/patients/me/family-members");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetMyFamilyMembers_NonPatientRole_Returns403Forbidden()
    {
        var doctorTokens = await LoginAsync("doctor01@medflow.test", "Demo@12345");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/patients/me/family-members");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", doctorTokens.AccessToken);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetMyFamilyMembers_OrphanUserWithoutPatient_Returns404ProblemDetails()
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

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/patients/me/family-members");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenResult.AccessToken);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(404, problem.Status);
    }

    [Fact]
    public async Task GetMyFamilyMembers_SeededDemoPatient01_Returns200WithPrimaryAndChildLink()
    {
        var tokens = await LoginAsync("patient01@medflow.test", "Demo@12345");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/patients/me/family-members");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var members = await response.Content.ReadFromJsonAsync<List<FamilyMemberResponse>>();
        Assert.NotNull(members);
        Assert.True(members.Count >= 2);

        // First item: Self (Primary account)
        var self = members[0];
        Assert.True(self.IsPrimaryAccount);
        Assert.Equal("Bản thân", self.Relationship);
        Assert.Equal("slate", self.RelationshipBadgeColor);
        Assert.Equal("PAT-0001", self.PatientCode);
        Assert.Equal("Nguyễn Văn An", self.FullName);

        // Second item: Linked child (PAT-0003 - Lê Minh Chi)
        var child = members.FirstOrDefault(m => !m.IsPrimaryAccount && m.PatientCode == "PAT-0003");
        Assert.NotNull(child);
        Assert.Equal("Lê Minh Chi", child.FullName);
        Assert.Equal("Con gái", child.Relationship);
        Assert.Equal("blue", child.RelationshipBadgeColor);
        Assert.NotNull(child.Permissions);
        Assert.True(child.Permissions.CanBookAppointments);
    }

    [Fact]
    public async Task GetMyFamilyMembers_IncludeSelfFalse_ReturnsOnlyChildLink()
    {
        var tokens = await LoginAsync("patient01@medflow.test", "Demo@12345");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/patients/me/family-members?includeSelf=false");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var members = await response.Content.ReadFromJsonAsync<List<FamilyMemberResponse>>();
        Assert.NotNull(members);
        Assert.DoesNotContain(members, m => m.IsPrimaryAccount);
        Assert.Contains(members, m => m.PatientCode == "PAT-0003");
    }

    [Fact]
    public async Task GetMyFamilyMembers_AliasRoute_ReturnsSame200Result()
    {
        var tokens = await LoginAsync("patient01@medflow.test", "Demo@12345");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/patients/me/family-profiles");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var members = await response.Content.ReadFromJsonAsync<List<FamilyMemberResponse>>();
        Assert.NotNull(members);
        Assert.NotEmpty(members);
    }
}
