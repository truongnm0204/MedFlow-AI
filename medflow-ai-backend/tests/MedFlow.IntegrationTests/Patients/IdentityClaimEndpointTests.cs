using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MedFlow.Application.DTOs.Patients;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.IntegrationTests.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MedFlow.IntegrationTests.Patients;

public sealed class IdentityClaimEndpointTests : AuthTestBase
{
    public IdentityClaimEndpointTests(TestWebAppFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task Claim_Approve_LinksExistingPatientAndArchivesCurrent()
    {
        var ctx = await ArrangeAsync();
        try
        {
            var otp = await SendOtpAsync(ctx.PatientToken, ctx.PatientEmail);
            var claim = await CreateClaimAsync(ctx.PatientToken, ctx.TargetPatientCode, ctx.HospitalId, otp);
            Assert.Equal("PENDING", claim.Status);

            var response = await ApproveAsync(ctx.AdminToken, claim.ClaimId);
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == HttpStatusCode.OK, body);
            var result = await response.Content.ReadFromJsonAsync<IdentityClaimResponse>();
            Assert.Equal("APPROVED", result!.Status);

            Db.ChangeTracker.Clear();
            var target = await Db.Patients.AsNoTracking().FirstAsync(p => p.Id == ctx.TargetPatientId);
            Assert.Equal(ctx.PatientUserId, target.UserId);
            var current = await Db.Patients.AsNoTracking().FirstAsync(p => p.Id == ctx.CurrentPatientId);
            Assert.Null(current.UserId);
            Assert.Equal("ARCHIVED", current.Status);
        }
        finally
        {
            await ctx.CleanupAsync(Db);
        }
    }

    [Fact]
    public async Task Claim_TargetAlreadyLinked_Returns409()
    {
        var ctx = await ArrangeAsync(targetLinkedToOtherUser: true);
        try
        {
            var otp = await SendOtpAsync(ctx.PatientToken, ctx.PatientEmail);
            var claim = await CreateClaimAsync(ctx.PatientToken, ctx.TargetPatientCode, ctx.HospitalId, otp);

            var response = await ApproveAsync(ctx.AdminToken, claim.ClaimId);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Db.ChangeTracker.Clear();
            var target = await Db.Patients.AsNoTracking().FirstAsync(p => p.Id == ctx.TargetPatientId);
            Assert.Equal(ctx.OtherUserId, target.UserId);
        }
        finally
        {
            await ctx.CleanupAsync(Db);
        }
    }

    [Fact]
    public async Task Claim_StaffOtherHospital_Returns403()
    {
        var ctx = await ArrangeAsync(withOtherHospitalAdmin: true);
        try
        {
            var otp = await SendOtpAsync(ctx.PatientToken, ctx.PatientEmail);
            var claim = await CreateClaimAsync(ctx.PatientToken, ctx.TargetPatientCode, ctx.HospitalId, otp);

            var response = await ApproveAsync(ctx.OtherHospitalAdminToken!, claim.ClaimId);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        finally
        {
            await ctx.CleanupAsync(Db);
        }
    }

    [Fact]
    public async Task Claim_CurrentPatientHasData_NeedsReconciliation()
    {
        var ctx = await ArrangeAsync(withFamilyLink: true);
        try
        {
            var otp = await SendOtpAsync(ctx.PatientToken, ctx.PatientEmail);
            var claim = await CreateClaimAsync(ctx.PatientToken, ctx.TargetPatientCode, ctx.HospitalId, otp);

            var response = await ApproveAsync(ctx.AdminToken, claim.ClaimId);
            var result = await response.Content.ReadFromJsonAsync<IdentityClaimResponse>();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("NEEDS_RECONCILIATION", result!.Status);
            Db.ChangeTracker.Clear();
            var target = await Db.Patients.AsNoTracking().FirstAsync(p => p.Id == ctx.TargetPatientId);
            Assert.Null(target.UserId);
            var current = await Db.Patients.AsNoTracking().FirstAsync(p => p.Id == ctx.CurrentPatientId);
            Assert.Equal(ctx.PatientUserId, current.UserId);
        }
        finally
        {
            await ctx.CleanupAsync(Db);
        }
    }

    [Fact]
    public async Task Claim_WithoutValidOtp_Returns400()
    {
        var ctx = await ArrangeAsync();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/patients/me/identity-claims");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ctx.PatientToken);
            request.Content = JsonContent.Create(new CreateIdentityClaimRequest(
                ctx.TargetPatientCode, ctx.HospitalId, "000000"));

            var response = await Client.SendAsync(request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        finally
        {
            await ctx.CleanupAsync(Db);
        }
    }

    private async Task<string> SendOtpAsync(string token, string email)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/patients/me/identity-claims/otp");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var otp = Factory.EmailSender.LatestOtpFor(email);
        Assert.NotNull(otp);
        return otp!;
    }

    private async Task<IdentityClaimResponse> CreateClaimAsync(
        string token,
        string patientCode,
        long hospitalId,
        string otp)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/patients/me/identity-claims");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new CreateIdentityClaimRequest(patientCode, hospitalId, otp));
        var response = await Client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created, body);
        return (await response.Content.ReadFromJsonAsync<IdentityClaimResponse>())!;
    }

    private async Task<HttpResponseMessage> ApproveAsync(string adminToken, long claimId)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/reception/patient-identity-claims/{claimId}/approve");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        return await Client.SendAsync(request);
    }

    private async Task<ClaimContext> ArrangeAsync(
        bool targetLinkedToOtherUser = false,
        bool withFamilyLink = false,
        bool withOtherHospitalAdmin = false)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (email, tokens) = await RegisterVerifyLoginAsync();
        var user = await Db.Users.AsNoTracking().FirstAsync(u => u.Email == email);
        var current = await Db.Patients.AsNoTracking().FirstAsync(p => p.UserId == user.Id);

        var hospitalId = (await Db.Hospitals.AsNoTracking().OrderBy(h => h.Id).FirstAsync()).Id;
        var target = new Patient
        {
            PatientCode = $"LINK-T-{suffix}",
            FullName = "Hồ sơ gốc",
            Status = "ACTIVE",
        };
        Db.Patients.Add(target);
        await Db.SaveChangesAsync();

        long? otherUser = null;
        if (targetLinkedToOtherUser)
        {
            var other = new User
            {
                Email = $"other-{suffix}@medflow.test",
                AuthProvider = "LOCAL",
                AccountStatus = AccountStatus.ACTIVE,
            };
            Db.Users.Add(other);
            await Db.SaveChangesAsync();
            target.UserId = other.Id;
            await Db.SaveChangesAsync();
            otherUser = other.Id;
        }

        long? memberId = null;
        if (withFamilyLink)
        {
            var member = new Patient
            {
                PatientCode = $"LINK-M-{suffix}",
                FullName = "Người thân",
                Status = "ACTIVE",
            };
            Db.Patients.Add(member);
            await Db.SaveChangesAsync();
            Db.PatientFamilyLinks.Add(new PatientFamilyLink
            {
                OwnerPatientId = current.Id,
                MemberPatientId = member.Id,
                Relationship = "CHILD",
                VerificationStatus = "PENDING",
            });
            await Db.SaveChangesAsync();
            memberId = member.Id;
        }

        var admin = await CreateAdminAsync(hospitalId, suffix);
        long? otherAdminUserId = null;
        string? otherAdminToken = null;
        if (withOtherHospitalAdmin)
        {
            var otherHospital = await Db.Hospitals.AsNoTracking()
                .Where(h => h.Id != hospitalId)
                .OrderBy(h => h.Id)
                .FirstOrDefaultAsync()
                ?? new Hospital
                {
                    HospitalCode = $"LINK-H-{suffix}",
                    Name = "Cơ sở khác",
                    IsActive = true,
                };
            if (otherHospital.Id == 0)
            {
                Db.Hospitals.Add(otherHospital);
                await Db.SaveChangesAsync();
            }

            var otherAdmin = await CreateAdminAsync(otherHospital.Id, $"o{suffix}");
            otherAdminUserId = otherAdmin.UserId;
            otherAdminToken = otherAdmin.Token;
        }

        return new ClaimContext(
            user.Id,
            current.Id,
            target.Id,
            target.PatientCode!,
            hospitalId,
            email,
            tokens.AccessToken,
            admin.Token,
            admin.UserId,
            admin.StaffId,
            otherUser,
            memberId,
            otherAdminUserId,
            otherAdminToken);
    }

    private async Task<AdminContext> CreateAdminAsync(long hospitalId, string suffix)
    {
        var role = await Db.Roles.AsNoTracking().FirstAsync(r => r.Code == "HOSPITAL_ADMIN");
        var user = new User
        {
            Email = $"admin-{suffix}@medflow.test",
            AuthProvider = "LOCAL",
            AccountStatus = AccountStatus.ACTIVE,
        };
        Db.Users.Add(user);
        await Db.SaveChangesAsync();

        Db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
        var staff = new Staff
        {
            UserId = user.Id,
            HospitalId = hospitalId,
            StaffCode = $"LINK-ADM-{suffix}",
            FullName = "Quản trị hồ sơ",
            Status = "ACTIVE",
        };
        Db.Staff.Add(staff);
        await Db.SaveChangesAsync();

        using var scope = Factory.Services.CreateScope();
        var jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var token = jwt.GenerateAccessToken(
            new User { Id = user.Id, Email = user.Email },
            ["HOSPITAL_ADMIN"]).AccessToken;

        return new AdminContext(user.Id, staff.Id, token);
    }

    private sealed record AdminContext(long UserId, long StaffId, string Token);

    private sealed record ClaimContext(
        long PatientUserId,
        long CurrentPatientId,
        long TargetPatientId,
        string TargetPatientCode,
        long HospitalId,
        string PatientEmail,
        string PatientToken,
        string AdminToken,
        long AdminUserId,
        long AdminStaffId,
        long? OtherUserId,
        long? MemberPatientId,
        long? OtherAdminUserId,
        string? OtherHospitalAdminToken)
    {
        public async Task CleanupAsync(MedFlow.Infrastructure.Persistence.MedFlowDbContext db)
        {
            db.ChangeTracker.Clear();
            var claims = await db.PatientIdentityClaims
                .Where(c => c.RequesterUserId == PatientUserId || c.DecidedByStaffId == AdminStaffId)
                .ToListAsync();
            var claimIds = claims.Select(c => c.Id.ToString()).ToList();
            db.PatientIdentityClaims.RemoveRange(claims);

            var audits = await db.AuditLogs
                .Where(a => claimIds.Contains(a.EntityId!) ||
                            a.ActorUserId == PatientUserId ||
                            a.ActorUserId == AdminUserId ||
                            (OtherAdminUserId != null && a.ActorUserId == OtherAdminUserId))
                .ToListAsync();
            db.AuditLogs.RemoveRange(audits);

            if (MemberPatientId is long memberId)
            {
                var links = await db.PatientFamilyLinks
                    .Where(l => l.OwnerPatientId == CurrentPatientId || l.MemberPatientId == memberId)
                    .ToListAsync();
                db.PatientFamilyLinks.RemoveRange(links);
            }

            var patients = await db.Patients
                .Where(p => p.Id == TargetPatientId ||
                            p.Id == CurrentPatientId ||
                            (MemberPatientId != null && p.Id == MemberPatientId))
                .ToListAsync();
            db.Patients.RemoveRange(patients);
            await db.SaveChangesAsync();

            var staff = await db.Staff.Where(s => s.UserId == AdminUserId || (OtherAdminUserId != null && s.UserId == OtherAdminUserId)).ToListAsync();
            db.Staff.RemoveRange(staff);
            var challenges = await db.AuthVerificationChallenges
                .Where(c => c.UserId == PatientUserId)
                .ToListAsync();
            db.AuthVerificationChallenges.RemoveRange(challenges);
            var sessions = await db.AuthSessions.Where(s => s.UserId == PatientUserId).ToListAsync();
            db.AuthSessions.RemoveRange(sessions);
            var roles = await db.UserRoles
                .Where(ur => ur.UserId == PatientUserId || ur.UserId == AdminUserId || (OtherAdminUserId != null && ur.UserId == OtherAdminUserId))
                .ToListAsync();
            db.UserRoles.RemoveRange(roles);
            var users = await db.Users
                .Where(u => u.Id == PatientUserId ||
                            u.Id == AdminUserId ||
                            (OtherUserId != null && u.Id == OtherUserId) ||
                            (OtherAdminUserId != null && u.Id == OtherAdminUserId))
                .ToListAsync();
            db.Users.RemoveRange(users);
            await db.SaveChangesAsync();
        }
    }
}
