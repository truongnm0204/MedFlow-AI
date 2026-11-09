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

    [Fact]
    public async Task GetMyFamilyMemberDetail_Unauthenticated_Returns401()
    {
        var response = await Client.GetAsync("/api/v1/patients/me/family-members/PAT-0003");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetMyFamilyMemberDetail_NonPatientRole_Returns403Forbidden()
    {
        var doctorTokens = await LoginAsync("doctor01@medflow.test", "Demo@12345");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/patients/me/family-members/PAT-0003");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", doctorTokens.AccessToken);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetMyFamilyMemberDetail_LinkedMemberByNumericId_Returns200WithFullDetails()
    {
        var tokens = await LoginAsync("patient01@medflow.test", "Demo@12345");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/patients/me/family-members/3");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var member = await response.Content.ReadFromJsonAsync<FamilyMemberResponse>();
        Assert.NotNull(member);
        Assert.Equal(3, member.MemberPatientId);
        Assert.Equal("PAT-0003", member.PatientCode);
        Assert.Equal("Lê Minh Chi", member.FullName);
        Assert.Equal("Con gái", member.Relationship);
        Assert.False(member.IsPrimaryAccount);
        Assert.NotNull(member.Permissions);
        Assert.True(member.Permissions.CanBookAppointments);
    }

    [Fact]
    public async Task GetMyFamilyMemberDetail_LinkedMemberByPatientCode_Returns200()
    {
        var tokens = await LoginAsync("patient01@medflow.test", "Demo@12345");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/patients/me/family-members/PAT-0003");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var member = await response.Content.ReadFromJsonAsync<FamilyMemberResponse>();
        Assert.NotNull(member);
        Assert.Equal(3, member.MemberPatientId);
        Assert.Equal("PAT-0003", member.PatientCode);
        Assert.Equal("Lê Minh Chi", member.FullName);
    }

    [Fact]
    public async Task GetMyFamilyMemberDetail_LinkedMemberByFamCode_Returns200()
    {
        var tokens = await LoginAsync("patient01@medflow.test", "Demo@12345");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/patients/me/family-members/FAM-003");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var member = await response.Content.ReadFromJsonAsync<FamilyMemberResponse>();
        Assert.NotNull(member);
        Assert.Equal(3, member.MemberPatientId);
        Assert.Equal("PAT-0003", member.PatientCode);
    }

    [Fact]
    public async Task GetMyFamilyMemberDetail_SelfIdentifier_Returns200PrimaryAccount()
    {
        var tokens = await LoginAsync("patient01@medflow.test", "Demo@12345");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/patients/me/family-members/self");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var member = await response.Content.ReadFromJsonAsync<FamilyMemberResponse>();
        Assert.NotNull(member);
        Assert.True(member.IsPrimaryAccount);
        Assert.Equal("Bản thân", member.Relationship);
        Assert.Equal("PAT-0001", member.PatientCode);
        Assert.Equal("Nguyễn Văn An", member.FullName);
    }

    [Fact]
    public async Task GetMyFamilyMemberDetail_UnlinkedPatient_Returns404NotFound()
    {
        var tokens = await LoginAsync("patient01@medflow.test", "Demo@12345");

        // PAT-0002 belongs to someone else / not linked to patient01
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/patients/me/family-members/9999");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(404, problem.Status);
    }

    [Fact]
    public async Task GetMyFamilyMemberDetail_AliasRoute_Returns200()
    {
        var tokens = await LoginAsync("patient01@medflow.test", "Demo@12345");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/patients/me/family-profiles/PAT-0003");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var member = await response.Content.ReadFromJsonAsync<FamilyMemberResponse>();
        Assert.NotNull(member);
        Assert.Equal("PAT-0003", member.PatientCode);
    }

    // ── CreateFamilyMember Tests ──────────────────────────────────────────

    [Fact]
    public async Task CreateFamilyMember_Unauthenticated_Returns401()
    {
        var request = new CreateFamilyMemberRequest
        {
            FullName = "Nguyễn Bé Con",
            Relationship = "Con gái",
            Gender = "Nữ",
            BirthDate = "2020-01-01",
            Address = "Số 123 Đường Láng, Hà Nội"
        };

        var response = await Client.PostAsJsonAsync("/api/v1/patients/me/family-members", request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateFamilyMember_NonPatientRole_Returns403Forbidden()
    {
        var doctorTokens = await LoginAsync("doctor01@medflow.test", "Demo@12345");

        var request = new CreateFamilyMemberRequest
        {
            FullName = "Nguyễn Bé Con",
            Relationship = "Con gái",
            Gender = "Nữ",
            BirthDate = "2020-01-01",
            Address = "Số 123 Đường Láng, Hà Nội"
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/patients/me/family-members");
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", doctorTokens.AccessToken);
        httpRequest.Content = JsonContent.Create(request);

        var response = await Client.SendAsync(httpRequest);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateFamilyMember_InvalidInput_Returns400BadRequest()
    {
        var tokens = await LoginAsync("patient01@medflow.test", "Demo@12345");

        var request = new CreateFamilyMemberRequest
        {
            FullName = "", // Invalid
            Relationship = "Con gái",
            Gender = "Nữ",
            BirthDate = "2020-01-01",
            Address = "Số 123 Đường Láng, Hà Nội"
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/patients/me/family-members");
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        httpRequest.Content = JsonContent.Create(request);

        var response = await Client.SendAsync(httpRequest);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(400, problem.Status);
    }

    [Fact]
    public async Task CreateFamilyMember_SelfRelationship_Returns400BadRequest()
    {
        var tokens = await LoginAsync("patient01@medflow.test", "Demo@12345");

        var request = new CreateFamilyMemberRequest
        {
            FullName = "Nguyễn Văn An",
            Relationship = "Bản thân", // Invalid for family member
            Gender = "Nam",
            BirthDate = "1990-01-01",
            Address = "Số 123 Đường Láng, Hà Nội"
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/patients/me/family-members");
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        httpRequest.Content = JsonContent.Create(request);

        var response = await Client.SendAsync(httpRequest);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateFamilyMember_ValidNewChild_Returns201Created()
    {
        var (_, tokens) = await RegisterVerifyLoginAsync();

        var request = new CreateFamilyMemberRequest
        {
            FullName = "Nguyễn Minh Khang",
            Relationship = "Con trai",
            Gender = "Nam",
            BirthDate = "2019-08-10",
            Address = "Số 100 Nguyễn Trãi, Hà Nội",
            Phone = "0912345678"
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/patients/me/family-members");
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        httpRequest.Content = JsonContent.Create(request);

        var response = await Client.SendAsync(httpRequest);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var member = await response.Content.ReadFromJsonAsync<FamilyMemberResponse>();
        Assert.NotNull(member);
        Assert.Equal("Nguyễn Minh Khang", member.FullName);
        Assert.Equal("Con trai", member.Relationship);
        Assert.Equal("blue", member.RelationshipBadgeColor);
        Assert.NotNull(member.Permissions);
        Assert.True(member.Permissions.CanBookAppointments);
    }

    [Fact]
    public async Task CreateFamilyMember_AliasRoute_Returns201Created()
    {
        var (_, tokens) = await RegisterVerifyLoginAsync();

        var request = new CreateFamilyMemberRequest
        {
            FullName = "Trần Thị Ánh",
            Relationship = "Vợ",
            Gender = "Nữ",
            BirthDate = "1985-05-12",
            Address = "Số 100 Nguyễn Trãi, Hà Nội"
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/patients/me/family-profiles");
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        httpRequest.Content = JsonContent.Create(request);

        var response = await Client.SendAsync(httpRequest);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var member = await response.Content.ReadFromJsonAsync<FamilyMemberResponse>();
        Assert.NotNull(member);
        Assert.Equal("Trần Thị Ánh", member.FullName);
        Assert.Equal("Vợ", member.Relationship);
        Assert.Equal("rose", member.RelationshipBadgeColor);
    }
}
