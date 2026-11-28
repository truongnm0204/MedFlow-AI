using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MedFlow.Application.DTOs.Auth;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MedFlow.IntegrationTests.Ekyc;

/// <summary>
/// Reception-assisted identity flows through the real HTTP pipeline:
///  * permission gate (403 without PATIENT_IDENTITY_VERIFY);
///  * UNVERIFIED profile creation (reason required, audit, never VERIFIED);
///  * reception eKYC session on a patient profile → staff drives the proxy → finalize verifies
///    the PATIENT (not an account), with performed_by recorded.
/// </summary>
public sealed class EkycReceptionEndpointTests : IClassFixture<EkycTestWebAppFactory>, IDisposable
{
    private readonly EkycTestWebAppFactory _factory;
    private readonly HttpClient _client;
    private readonly IServiceScope _scope;

    public EkycReceptionEndpointTests(EkycTestWebAppFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _scope = factory.Services.CreateScope();
    }

    private MedFlowDbContext Db => _scope.ServiceProvider.GetRequiredService<MedFlowDbContext>();

    public void Dispose()
    {
        _client.Dispose();
        _scope.Dispose();
    }

    private async Task<(long UserId, long StaffId, string Token)> CreateStaffTokenAsync(bool withVerifyPermission)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var receptionRole = await Db.Roles.AsNoTracking().FirstAsync(r => r.Code == RoleNames.Reception);

        var user = new User
        {
            Email = $"ekyc-reception-{suffix}@medflow.test",
            AuthProvider = "LOCAL",
            AccountStatus = AccountStatus.ACTIVE,
            EmailVerifiedAt = DateTime.UtcNow,
        };
        Db.Users.Add(user);
        await Db.SaveChangesAsync();

        Db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = receptionRole.Id });

        if (withVerifyPermission)
        {
            // Dedicated test role — never modifies the seeded RECEPTION permission set
            // (other suites assert that default reception staff lack it).
            var testRole = new Role { Code = $"EKYC_TEST_VERIFIER_{suffix}", Name = "eKYC Test Verifier" };
            Db.Roles.Add(testRole);
            await Db.SaveChangesAsync();

            var permission = await Db.Permissions.FirstAsync(p => p.Code == PermissionCodes.PatientIdentityVerify);
            Db.RolePermissions.Add(new RolePermission { RoleId = testRole.Id, PermissionId = permission.Id });
            Db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = testRole.Id });
        }

        var hospital = await Db.Hospitals.AsNoTracking().FirstAsync(h => h.IsActive != false);
        var staff = new Staff
        {
            UserId = user.Id,
            HospitalId = hospital.Id,
            StaffCode = $"EKYC{suffix}"[..11].ToUpperInvariant(),
            FullName = $"Staff eKYC {suffix}",
            Status = "ACTIVE",
        };
        Db.Staff.Add(staff);
        await Db.SaveChangesAsync();

        var sessionSvc = _scope.ServiceProvider.GetRequiredService<IAuthSessionService>();
        var jwtSvc = _scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var authSession = await sessionSvc.GenerateAsync(user.Id, "127.0.0.1");
        var token = jwtSvc.GenerateAccessToken(user, [RoleNames.Reception], authSession.Entity.Id).AccessToken;
        return (user.Id, staff.Id, token);
    }

    private async Task<HttpResponseMessage> PostJsonAsync(string token, string url, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(request);
    }

    private async Task ProxyPostAsync(string token, string proxyBase, string vnptPath, string jsonBody)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{proxyBase}{vnptPath}")
        {
            Content = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ReceptionWithoutPermission_CreateUnverifiedProfile_Forbidden()
    {
        var (_, _, token) = await CreateStaffTokenAsync(withVerifyPermission: false);

        var response = await PostJsonAsync(token, "/api/v1/reception/ekyc/patients/unverified", new
        {
            fullName = "Bé Không Quyền",
            dateOfBirth = "2020-01-01",
            gender = "FEMALE",
            reason = "CHILD",
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UnverifiedProfile_RequiresReason_BadRequest()
    {
        var (_, _, token) = await CreateStaffTokenAsync(withVerifyPermission: true);

        var response = await PostJsonAsync(token, "/api/v1/reception/ekyc/patients/unverified", new
        {
            fullName = "Bé Không Lý Do",
            dateOfBirth = "2020-01-01",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ReceptionFlow_UnverifiedProfileThenEkyc_PatientVerified_NoAccountActivation()
    {
        await CleanupStubHmacPatientsAsync();
        var (staffUserId, _, token) = await CreateStaffTokenAsync(withVerifyPermission: true);

        // 1. Create the profile without eKYC (owner-approved exception) — stays UNVERIFIED.
        var createProfile = await PostJsonAsync(token, "/api/v1/reception/ekyc/patients/unverified", new
        {
            fullName = "NGUYEN VAN EKYC",
            dateOfBirth = "1995-06-15",
            gender = "MALE",
            reason = "CONSENT_DECLINED",
            identityCard = "012345678901",
            note = "Khách chưa đồng ý eKYC lúc tiếp nhận",
        });
        Assert.Equal(HttpStatusCode.Created, createProfile.StatusCode);
        var profile = await createProfile.Content.ReadFromJsonAsync<UnverifiedProfileShape>();
        Assert.Equal("UNVERIFIED", profile!.IdentityStatus);

        var persisted = await Db.Patients.AsNoTracking().FirstAsync(p => p.Id == profile.PatientId);
        Assert.Equal(PatientIdentityStatuses.Unverified, persisted.IdentityStatus);
        Assert.Equal(PatientUnverifiedReasons.ConsentDeclined, persisted.UnverifiedReason);

        // 2. Start reception-assisted eKYC for that profile.
        var createSession = await PostJsonAsync(token,
            $"/api/v1/reception/ekyc/patients/{profile.PatientId}/sessions", new
            {
                consentVersion = "ekyc-consent-v1",
                consentAccepted = true,
                identityCard = "012345678901",
            });
        Assert.Equal(HttpStatusCode.Created, createSession.StatusCode);
        var session = await createSession.Content.ReadFromJsonAsync<SessionShape>()
            ?? throw new InvalidOperationException("Reception eKYC session response missing.");

        var dbSession = await Db.EkycVerificationSessions.AsNoTracking()
            .FirstAsync(s => s.Id == long.Parse(session.SessionId));
        Assert.Equal(EkycVerificationModes.ReceptionAssisted, dbSession.VerificationMode);
        Assert.Equal(EkycPurposes.PatientProfile, dbSession.Purpose);
        Assert.Equal(staffUserId, dbSession.PerformedBy);
        Assert.Null(dbSession.UserId);

        // 3. Run the document-only flow through the proxy as the staff operator.
        await ProxyPostAsync(token, session.ProxyBasePath, "/file-service/v1/addFile", "fake-upload");
        await ProxyPostAsync(token, session.ProxyBasePath, "/ai/v1/web/ocr/id/front",
            "{\"img_front\":\"idg20261127/IDG01_stubbed_upload\"}");
        await ProxyPostAsync(token, session.ProxyBasePath, "/ai/v1/web/card/liveness",
            "{\"img\":\"idg20261127/IDG01_stubbed_upload\"}");

        // 4. Finalize as the staff operator — engine decides; patient becomes VERIFIED.
        using var finalize = new HttpRequestMessage(
            HttpMethod.Post, $"/api/v1/ekyc/sessions/{session.SessionId}/finalize");
        finalize.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var finalizeResponse = await _client.SendAsync(finalize);
        Assert.Equal(HttpStatusCode.OK, finalizeResponse.StatusCode);
        var finalizeBody = await finalizeResponse.Content.ReadAsStringAsync();
        Assert.Contains("VERIFIED", finalizeBody);
        Assert.Contains(PatientIdentityStatuses.Verified, finalizeBody);
        Assert.Contains(EkycPurposes.PatientProfile, finalizeBody);

        Db.ChangeTracker.Clear();
        var verified = await Db.Patients.AsNoTracking().FirstAsync(p => p.Id == profile.PatientId);
        Assert.Equal(PatientIdentityStatuses.Verified, verified.IdentityStatus);
        Assert.Null(verified.UnverifiedReason);
        Assert.NotNull(verified.IdentificationHashHmac);

        // Patient identity verification never activates or grants an account.
        Assert.Null(verified.UserId);
        Assert.True(await Db.AuditLogs.AnyAsync(a =>
            a.Action == EkycConstants.AuditActionReceptionAssisted && a.ActorUserId == staffUserId));
    }

    /// <summary>Shared medflow_test DB: null-out prior stub-CCCD rows (unique HMAC index).</summary>
    private async Task CleanupStubHmacPatientsAsync()
    {
        var hmac = ComputeTestIdentificationHmac("012345678901", "integration-ekyc-hmac-key");
        var stale = await Db.Patients.Where(p => p.IdentificationHashHmac == hmac).ToListAsync();
        foreach (var row in stale)
        {
            row.IdentificationHashHmac = null;
            row.IdentityStatus = PatientIdentityStatuses.Unverified;
            row.UnverifiedReason = null;
        }

        if (stale.Count > 0)
        {
            await Db.SaveChangesAsync();
        }
    }

    private static string ComputeTestIdentificationHmac(string canonicalId, string key)
    {
        var keyBytes = System.Text.Encoding.UTF8.GetBytes(key);
        var shaHex = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(canonicalId))).ToLowerInvariant();
        var digest = System.Text.Encoding.UTF8.GetBytes(shaHex);
        return Convert.ToHexString(
            System.Security.Cryptography.HMACSHA256.HashData(keyBytes, digest)).ToLowerInvariant();
    }

    private sealed record UnverifiedProfileShape(
        long PatientId,
        string PatientCode,
        string FullName,
        string IdentityStatus,
        string UnverifiedReason);

    private sealed record SessionShape(string SessionId, string ProxyBasePath, string Status, DateTime ExpiresAt);
}
