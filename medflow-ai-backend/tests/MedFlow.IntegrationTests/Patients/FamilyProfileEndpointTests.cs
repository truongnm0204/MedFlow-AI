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
    public async Task CreateFamilyMember_ExistingIdentityCard_ReturnsPendingNotVerified()
    {
        var (_, firstTokens) = await RegisterVerifyLoginAsync();
        var (_, secondTokens) = await RegisterVerifyLoginAsync();
        var cccd = "001" + Random.Shared.Next(100000000, 999999999);

        var firstRequest = new CreateFamilyMemberRequest
        {
            FullName = "Nguyễn Thị Một",
            Relationship = "Vợ",
            Gender = "Nữ",
            BirthDate = "1990-01-01",
            Address = "Số 1 Nguyễn Trãi, Hà Nội",
            IdentificationNo = cccd
        };

        using var firstPost = new HttpRequestMessage(HttpMethod.Post, "/api/v1/patients/me/family-members");
        firstPost.Headers.Authorization = new AuthenticationHeaderValue("Bearer", firstTokens.AccessToken);
        firstPost.Content = JsonContent.Create(firstRequest);
        var firstResponse = await Client.SendAsync(firstPost);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        var firstMember = await firstResponse.Content.ReadFromJsonAsync<FamilyMemberResponse>();
        Assert.NotNull(firstMember);
        Assert.False(firstMember.IsIdentityVerified);
        Assert.Equal("PENDING", firstMember.Permissions?.RelationshipStatus);
        Assert.False(firstMember.Permissions!.CanViewMedicalRecords);

        var stored = await Db.Patients.AsNoTracking().FirstAsync(p => p.Id == firstMember.MemberPatientId);
        Assert.False(string.IsNullOrWhiteSpace(stored.IdentificationHashHmac));
        Assert.Equal((short?)1, stored.HashVersion);

        var secondRequest = new CreateFamilyMemberRequest
        {
            FullName = "Nguyễn Thị Một",
            Relationship = "Chị gái",
            Gender = "Nữ",
            BirthDate = "1990-01-01",
            Address = "Số 2 Nguyễn Trãi, Hà Nội",
            IdentificationNo = cccd
        };

        using var secondPost = new HttpRequestMessage(HttpMethod.Post, "/api/v1/patients/me/family-members");
        secondPost.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secondTokens.AccessToken);
        secondPost.Content = JsonContent.Create(secondRequest);
        var secondResponse = await Client.SendAsync(secondPost);

        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);
        var secondMember = await secondResponse.Content.ReadFromJsonAsync<FamilyMemberResponse>();
        Assert.NotNull(secondMember);
        Assert.Equal(firstMember.MemberPatientId, secondMember.MemberPatientId);
        Assert.False(secondMember.IsIdentityVerified);
        Assert.Equal("PENDING", secondMember.Permissions?.RelationshipStatus);
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

    [Fact]
    public async Task UpdateFamilyMember_UnverifiedLink_ContactUpdate_Returns403()
    {
        var (_, tokens) = await RegisterVerifyLoginAsync();

        var createReq = new CreateFamilyMemberRequest
        {
            FullName = "Nguyễn Minh Khang",
            Relationship = "Con trai",
            Gender = "Nam",
            BirthDate = "2019-08-10",
            Address = "Số 100 Nguyễn Trãi, Hà Nội",
            Phone = "0912345678"
        };

        using var postReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/patients/me/family-members");
        postReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        postReq.Content = JsonContent.Create(createReq);
        var postRes = await Client.SendAsync(postReq);
        Assert.Equal(HttpStatusCode.Created, postRes.StatusCode);
        var created = await postRes.Content.ReadFromJsonAsync<FamilyMemberResponse>();
        Assert.NotNull(created);

        var updateReq = new UpdateFamilyMemberRequest
        {
            Phone = "0988765432",
            Email = "khang.new@medflow.vn",
            Address = "Số 45 Tràng Tiền, Quận Hoàn Kiếm, Hà Nội"
        };

        using var putReq = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/patients/me/family-members/{created.MemberPatientId}");
        putReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        putReq.Content = JsonContent.Create(updateReq);
        var putRes = await Client.SendAsync(putReq);

        Assert.Equal(HttpStatusCode.Forbidden, putRes.StatusCode);
        var stored = await Db.Patients.AsNoTracking().FirstAsync(p => p.Id == created.MemberPatientId);
        Assert.Equal("0912345678", stored.Phone);
    }

    [Fact]
    public async Task UpdateFamilyMember_AliasRoute_Returns200OK()
    {
        var (_, tokens) = await RegisterVerifyLoginAsync();

        var createReq = new CreateFamilyMemberRequest
        {
            FullName = "Nguyễn Bảo Châu",
            Relationship = "Con gái",
            Gender = "Nữ",
            BirthDate = "2020-05-20",
            Address = "Số 100 Nguyễn Trãi, Hà Nội"
        };

        using var postReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/patients/me/family-members");
        postReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        postReq.Content = JsonContent.Create(createReq);
        var postRes = await Client.SendAsync(postReq);
        var created = await postRes.Content.ReadFromJsonAsync<FamilyMemberResponse>();
        Assert.NotNull(created);

        var updateReq = new UpdateFamilyMemberRequest
        {
            Relationship = "Khác"
        };

        using var putReq = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/patients/me/family-profiles/{created.MemberPatientId}");
        putReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        putReq.Content = JsonContent.Create(updateReq);
        var putRes = await Client.SendAsync(putReq);

        Assert.Equal(HttpStatusCode.OK, putRes.StatusCode);
        var updated = await putRes.Content.ReadFromJsonAsync<FamilyMemberResponse>();
        Assert.NotNull(updated);
        Assert.NotEqual("Con gái", updated.Relationship);
    }

    [Fact]
    public async Task UpdateFamilyMember_InvalidInput_Returns400BadRequest()
    {
        var (_, tokens) = await RegisterVerifyLoginAsync();

        var createReq = new CreateFamilyMemberRequest
        {
            FullName = "Nguyễn Minh Khang",
            Relationship = "Con trai",
            Gender = "Nam",
            BirthDate = "2019-08-10",
            Address = "Số 100 Nguyễn Trãi, Hà Nội",
            Phone = "0912345678"
        };

        using var postReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/patients/me/family-members");
        postReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        postReq.Content = JsonContent.Create(createReq);
        var postRes = await Client.SendAsync(postReq);
        Assert.Equal(HttpStatusCode.Created, postRes.StatusCode);
        var created = await postRes.Content.ReadFromJsonAsync<FamilyMemberResponse>();
        Assert.NotNull(created);

        var link = await Db.PatientFamilyLinks.FirstAsync(l => l.MemberPatientId == created.MemberPatientId);
        link.VerificationStatus = "VERIFIED";
        link.VerificationMethod = "STAFF";
        link.VerifiedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        await Db.SaveChangesAsync();

        var updateReq = new UpdateFamilyMemberRequest
        {
            Phone = "invalidphone",
            Address = "123" // Too short (< 5)
        };

        using var putReq = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/patients/me/family-members/{created.MemberPatientId}");
        putReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        putReq.Content = JsonContent.Create(updateReq);
        var putRes = await Client.SendAsync(putReq);

        Assert.Equal(HttpStatusCode.BadRequest, putRes.StatusCode);
    }

    [Fact]
    public async Task UpdateFamilyMember_CannotUpdateSelf_Returns400BadRequest()
    {
        var (_, tokens) = await RegisterVerifyLoginAsync();

        var updateReq = new UpdateFamilyMemberRequest
        {
            Phone = "0912345678"
        };

        using var putReq = new HttpRequestMessage(HttpMethod.Put, "/api/v1/patients/me/family-members/self");
        putReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        putReq.Content = JsonContent.Create(updateReq);
        var putRes = await Client.SendAsync(putReq);

        Assert.Equal(HttpStatusCode.BadRequest, putRes.StatusCode);
    }

    [Fact]
    public async Task UpdateFamilyMember_UnlinkedPatient_Returns404NotFound()
    {
        var (_, tokens) = await RegisterVerifyLoginAsync();

        var updateReq = new UpdateFamilyMemberRequest
        {
            Address = "Số 123 Đường Láng, Hà Nội"
        };

        using var putReq = new HttpRequestMessage(HttpMethod.Put, "/api/v1/patients/me/family-members/99999");
        putReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        putReq.Content = JsonContent.Create(updateReq);
        var putRes = await Client.SendAsync(putReq);

        Assert.Equal(HttpStatusCode.NotFound, putRes.StatusCode);
    }

    [Fact]
    public async Task UpdateFamilyMember_Unauthorized_Returns401Unauthorized()
    {
        var updateReq = new UpdateFamilyMemberRequest
        {
            Address = "Số 123 Đường Láng, Hà Nội"
        };

        using var putReq = new HttpRequestMessage(HttpMethod.Put, "/api/v1/patients/me/family-members/FAM-001");
        putReq.Content = JsonContent.Create(updateReq);
        var putRes = await Client.SendAsync(putReq);

        Assert.Equal(HttpStatusCode.Unauthorized, putRes.StatusCode);
    }

    [Fact]
    public async Task RemoveFamilyMember_ValidMember_Returns204NoContent()
    {
        var (_, tokens) = await RegisterVerifyLoginAsync();

        var createReq = new CreateFamilyMemberRequest
        {
            FullName = "Nguyễn Hải Đăng",
            Relationship = "Con trai",
            Gender = "Nam",
            BirthDate = "2018-01-01",
            Address = "Số 50 Tràng Tiền, Hà Nội"
        };

        using var postReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/patients/me/family-members");
        postReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        postReq.Content = JsonContent.Create(createReq);
        var postRes = await Client.SendAsync(postReq);
        var created = await postRes.Content.ReadFromJsonAsync<FamilyMemberResponse>();
        Assert.NotNull(created);

        using var delReq = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/patients/me/family-members/{created.MemberPatientId}");
        delReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        delReq.Content = JsonContent.Create(new RemoveFamilyMemberRequest { Reason = "Người thân tự lập tài khoản riêng" });
        var delRes = await Client.SendAsync(delReq);

        Assert.Equal(HttpStatusCode.NoContent, delRes.StatusCode);

        // Verify member is not in active family list
        using var getReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/patients/me/family-members?includeSelf=false");
        getReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var getRes = await Client.SendAsync(getReq);
        var list = await getRes.Content.ReadFromJsonAsync<List<FamilyMemberResponse>>();
        Assert.NotNull(list);
        Assert.DoesNotContain(list, m => m.MemberPatientId == created.MemberPatientId);
    }

    [Fact]
    public async Task RemoveFamilyMember_AliasRoute_Returns204NoContent()
    {
        var (_, tokens) = await RegisterVerifyLoginAsync();

        var createReq = new CreateFamilyMemberRequest
        {
            FullName = "Nguyễn Phương Nhi",
            Relationship = "Con gái",
            Gender = "Nữ",
            BirthDate = "2021-02-02",
            Address = "Số 60 Tràng Tiền, Hà Nội"
        };

        using var postReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/patients/me/family-members");
        postReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        postReq.Content = JsonContent.Create(createReq);
        var postRes = await Client.SendAsync(postReq);
        var created = await postRes.Content.ReadFromJsonAsync<FamilyMemberResponse>();
        Assert.NotNull(created);

        using var delReq = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/patients/me/family-profiles/{created.MemberPatientId}");
        delReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var delRes = await Client.SendAsync(delReq);

        Assert.Equal(HttpStatusCode.NoContent, delRes.StatusCode);
    }

    [Fact]
    public async Task RemoveFamilyMember_CannotRemoveSelf_Returns400BadRequest()
    {
        var (_, tokens) = await RegisterVerifyLoginAsync();

        using var delReq = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/patients/me/family-members/self");
        delReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var delRes = await Client.SendAsync(delReq);

        Assert.Equal(HttpStatusCode.BadRequest, delRes.StatusCode);
    }

    [Fact]
    public async Task RemoveFamilyMember_UnlinkedPatient_Returns404NotFound()
    {
        var (_, tokens) = await RegisterVerifyLoginAsync();

        using var delReq = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/patients/me/family-members/99999");
        delReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var delRes = await Client.SendAsync(delReq);

        Assert.Equal(HttpStatusCode.NotFound, delRes.StatusCode);
    }

    [Fact]
    public async Task RemoveFamilyMember_Unauthorized_Returns401Unauthorized()
    {
        using var delReq = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/patients/me/family-members/FAM-001");
        var delRes = await Client.SendAsync(delReq);

        Assert.Equal(HttpStatusCode.Unauthorized, delRes.StatusCode);
    }

    [Fact]
    public async Task RemoveFamilyMember_WithActiveAppointment_Returns400BadRequest()
    {
        var (_, tokens) = await RegisterVerifyLoginAsync();

        var createReq = new CreateFamilyMemberRequest
        {
            FullName = "Nguyễn Hải Đăng",
            Relationship = "Con trai",
            Gender = "Nam",
            BirthDate = "2018-01-01",
            Address = "Số 70 Tràng Tiền, Hà Nội"
        };

        using var postReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/patients/me/family-members");
        postReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        postReq.Content = JsonContent.Create(createReq);
        var postRes = await Client.SendAsync(postReq);
        var created = await postRes.Content.ReadFromJsonAsync<FamilyMemberResponse>();
        Assert.NotNull(created);
        var memberId = created.MemberPatientId;

        var appt = new Appointment
        {
            PatientId = memberId,
            Status = "CONFIRMED",
            BookedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)
        };
        Db.Appointments.Add(appt);
        await Db.SaveChangesAsync();

        using var delReq = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/patients/me/family-members/{created.MemberPatientId}");
        delReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var delRes = await Client.SendAsync(delReq);

        Assert.Equal(HttpStatusCode.BadRequest, delRes.StatusCode);
    }
}
