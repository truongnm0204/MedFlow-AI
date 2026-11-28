using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using MedFlow.Application.Services.Ekyc;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Enums;
using MedFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MedFlow.IntegrationTests.Ekyc;

/// <summary>Serializes slice-4 registration tests — they share the fixed stub-OCR hmac rows in medflow_test.</summary>
[CollectionDefinition("EkycRegistrationDedupe", DisableParallelization = true)]
public sealed class EkycRegistrationDedupeCollection;

/// <summary>
/// Slice-4 end-to-end: mandatory-registration flag ON — register → pending-activation →
/// scope-limited token → eKYC verify → dedupe resolution → activated full auth.
/// Plus the security invariant: registration tokens are rejected outside the whitelist,
/// and the flag-OFF regression guard (legacy flow byte-identical).
/// </summary>
[Collection("EkycRegistrationDedupe")]
public sealed class EkycRegistrationDedupeEndpointTests : IDisposable
{
    private readonly EkycTestWebAppFactory _factory;
    private readonly HttpClient _client;
    private readonly IServiceScope _scope;

    public EkycRegistrationDedupeEndpointTests()
    {
        _factory = new EkycMandatoryRegistrationTestWebAppFactory();
        _client = _factory.CreateClient();
        _scope = _factory.Services.CreateScope();
    }

    private MedFlowDbContext Db => _scope.ServiceProvider.GetRequiredService<MedFlowDbContext>();

    public void Dispose()
    {
        _client.Dispose();
        _scope.Dispose();
        _factory.Dispose();
    }

    private sealed record RegisterShape(long UserId, string Email);
    private sealed record VerifyOtpShape(long UserId, string Message, string? AccessToken, int? AccessTokenExpiresIn, bool RequiresEkycVerification);
    private sealed record LoginShape(string AccessToken, int AccessTokenExpiresIn, string RefreshToken, int RefreshTokenExpiresIn, bool RequiresEkycVerification);
    private sealed record CreateSessionShape(string SessionId, string ProxyBasePath, string Status, DateTime ExpiresAt);
    private sealed record FinalizeShape(string SessionId, string Status, string? FailureReason, string? FailureCode, DateTime? ConsumedAt, bool? AccountActivated, string? ActivationOutcome, string? ActivationReviewReason);

    private async Task<(string Email, VerifyOtpShape Verify)> RegisterAndVerifyOtpAsync(string fullName = "Nguyễn Văn Ekyc")
    {
        var email = $"ekyc-reg-{Guid.NewGuid():N}@example.com";
        var register = await _client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email,
            password = "MedFlow@2026",
            confirmPassword = "MedFlow@2026",
            fullName,
            dateOfBirth = "1995-06-15",
            gender = "MALE",
            phone = "0912345678",
        });
        register.EnsureSuccessStatusCode();

        var otp = _factory.EmailSender.LatestOtpFor(email)
            ?? throw new InvalidOperationException($"No OTP captured for {email}");
        var verifyResponse = await _client.PostAsJsonAsync("/api/v1/auth/verify-otp", new { email, otp });
        verifyResponse.EnsureSuccessStatusCode();
        var verify = await verifyResponse.Content.ReadFromJsonAsync<VerifyOtpShape>();
        return (email, verify!);
    }

    private async Task<(long SessionId, string ProxyBase)> CreateSessionAsync(string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ekyc/sessions")
        {
            Content = JsonContent.Create(new { consentVersion = "ekyc-consent-v1", consentAccepted = true }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CreateSessionShape>();
        return (long.Parse(body!.SessionId), body.ProxyBasePath);
    }

    private async Task ProxyPostAsync(string token, string proxyBase, string vnptPath, string jsonBody)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{proxyBase}{vnptPath}")
        {
            Content = new StringContent(jsonBody, Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task RunFullFlowAsync(string token, string proxyBase)
    {
        const string docHash = "idg20261127/IDG01_stubbed_upload";
        await ProxyPostAsync(token, proxyBase, "/file-service/v1/addFile", "fake-upload");
        await ProxyPostAsync(token, proxyBase, "/ai/v1/web/ocr/id/front", "{\"img_front\":\"" + docHash + "\"}");
        await ProxyPostAsync(token, proxyBase, "/ai/v1/web/card/liveness", "{\"img\":\"" + docHash + "\"}");
        await ProxyPostAsync(token, proxyBase, "/ai/v1/web/face/liveness", "{\"img\":\"idg20261127/IDG01_face\"}");
        await ProxyPostAsync(token, proxyBase, "/ai/v1/web/face/compare", "{\"img_front\":\"" + docHash + "\",\"img\":\"idg20261127/IDG01_face\"}");
    }

    private async Task<FinalizeShape> FinalizeAsync(string token, long sessionId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/ekyc/sessions/{sessionId}/finalize");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<FinalizeShape>())!;
    }

    // ------------------------------------------------------------------
    // CORE FLOW: register → pending → eKYC → activated full auth (case 1)
    // ------------------------------------------------------------------

    [Fact]
    public async Task MandatoryFlow_FullJourney_PendingThenActivatedAfterEkyc()
    {
        await CleanupStubHmacPatientsAsync(ComputeTestIdentificationHmac("012345678901", "integration-ekyc-hmac-key"));

        var (email, verify) = await RegisterAndVerifyOtpAsync();

        // 1. OTP verify returns a registration-scope token, account NOT yet active.
        Assert.True(verify.RequiresEkycVerification);
        Assert.NotNull(verify.AccessToken);
        var registrationToken = verify.AccessToken!;

        var userBefore = await Db.Users.FirstAsync(u => u.Email == email);
        Assert.Equal(AccountStatus.PENDING, userBefore.AccountStatus);

        // 2. Registration token CAN create + run an eKYC session.
        var (sessionId, proxyBase) = await CreateSessionAsync(registrationToken);
        await RunFullFlowAsync(registrationToken, proxyBase);

        // 3. Finalize → VERIFIED + activation outcome case 1 (no pre-existing hmac record).
        var finalize = await FinalizeAsync(registrationToken, sessionId);
        Assert.Equal(EkycVerificationStatuses.Verified, finalize.Status);
        Assert.True(finalize.AccountActivated);
        Assert.Equal(EkycDedupeOutcomes.NewIdentityActivated, finalize.ActivationOutcome);

        // 4. Account is now ACTIVE — full login works with real tokens.
        Db.ChangeTracker.Clear();
        var userAfter = await Db.Users.FirstAsync(u => u.Email == email);
        Assert.Equal(AccountStatus.ACTIVE, userAfter.AccountStatus);

        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "MedFlow@2026" });
        login.EnsureSuccessStatusCode();
        var loginBody = await login.Content.ReadFromJsonAsync<LoginShape>();
        Assert.False(loginBody!.RequiresEkycVerification);
        Assert.NotEmpty(loginBody.RefreshToken);

        // 5. identification hmac written onto the patient record (case 1 outcome).
        var patient = await Db.Patients.FirstAsync(p => p.UserId == userAfter.Id);
        Assert.NotNull(patient.IdentificationHashHmac);
    }

    // ------------------------------------------------------------------
    // SECURITY: registration token cannot touch patient APIs
    // ------------------------------------------------------------------

    [Fact]
    public async Task RegistrationToken_PatientApis_Forbidden()
    {
        var (_, verify) = await RegisterAndVerifyOtpAsync();
        var registrationToken = verify.AccessToken!;

        // Patient-role endpoint.
        using var patientRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/patients/me/profile");
        patientRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", registrationToken);
        var patientResponse = await _client.SendAsync(patientRequest);
        Assert.Equal(HttpStatusCode.Forbidden, patientResponse.StatusCode);

        // Bare-[Authorize] endpoint outside whitelist — blocked by the middleware even though no role-gate.
        using var visitsRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/patients/me/visits");
        visitsRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", registrationToken);
        var visitsResponse = await _client.SendAsync(visitsRequest);
        Assert.Equal(HttpStatusCode.Forbidden, visitsResponse.StatusCode);
    }

    [Fact]
    public async Task RegistrationToken_WhitelistEndpoints_Allowed()
    {
        var (_, verify) = await RegisterAndVerifyOtpAsync();
        var registrationToken = verify.AccessToken!;

        // /api/v1/auth/me is whitelisted (xem trạng thái).
        using var meRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        meRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", registrationToken);
        var meResponse = await _client.SendAsync(meRequest);
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);

        // Email-verification endpoints are whitelisted: attaching the registration token must not
        // turn a client-side convenience header into a 403.
        using var verifyRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/verify-otp")
        {
            Content = JsonContent.Create(new { email = "nobody@example.com", otp = "000000" }),
        };
        verifyRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", registrationToken);
        var verifyResponse = await _client.SendAsync(verifyRequest);
        Assert.NotEqual(HttpStatusCode.Forbidden, verifyResponse.StatusCode);

        using var resendRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/resend-otp")
        {
            Content = JsonContent.Create(new { email = "nobody@example.com" }),
        };
        resendRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", registrationToken);
        var resendResponse = await _client.SendAsync(resendRequest);
        Assert.NotEqual(HttpStatusCode.Forbidden, resendResponse.StatusCode);
    }

    // ------------------------------------------------------------------
    // Flag OFF regression: legacy registration is byte-identical
    // ------------------------------------------------------------------

    [Fact]
    public async Task FlagOff_RegistrationFlow_ActivatesImmediatelyWithoutGate()
    {
        using var factory = new EkycTestWebAppFactory(); // default: MandatoryRegistration = false
        using var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MedFlowDbContext>();

        var email = $"ekyc-off-{Guid.NewGuid():N}@example.com";
        var register = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email,
            password = "MedFlow@2026",
            confirmPassword = "MedFlow@2026",
            fullName = "Nguyễn Văn FlagOff",
            dateOfBirth = "1995-06-15",
            gender = "MALE",
            phone = "0912345678",
        });
        register.EnsureSuccessStatusCode();

        var otp = factory.EmailSender.LatestOtpFor(email)
            ?? throw new InvalidOperationException($"No OTP captured for {email}");
        var verifyResponse = await client.PostAsJsonAsync("/api/v1/auth/verify-otp", new { email, otp });
        verifyResponse.EnsureSuccessStatusCode();
        var verify = (await verifyResponse.Content.ReadFromJsonAsync<VerifyOtpShape>())!;

        // Legacy behavior: no registration token, no eKYC gate, account ACTIVE at email verify.
        Assert.False(verify.RequiresEkycVerification);
        Assert.Null(verify.AccessToken);
        Assert.Equal(AccountStatus.ACTIVE, (await db.Users.FirstAsync(u => u.Email == email)).AccountStatus);

        // Full auth immediately; patient APIs reachable.
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "MedFlow@2026" });
        login.EnsureSuccessStatusCode();
        var loginBody = (await login.Content.ReadFromJsonAsync<LoginShape>())!;
        Assert.False(loginBody.RequiresEkycVerification);
        Assert.NotEmpty(loginBody.RefreshToken);

        using var profileRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/patients/me/profile");
        profileRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", loginBody.AccessToken);
        var profileResponse = await client.SendAsync(profileRequest);
        Assert.Equal(HttpStatusCode.OK, profileResponse.StatusCode);
    }

    // ------------------------------------------------------------------
    // Login-resume: PENDING user logging in gets a registration token
    // ------------------------------------------------------------------

    [Fact]
    public async Task Login_PendingUnverifiedEmail_DoesNotIssueRegistrationToken()
    {
        // Registered but email never verified — password knowledge alone must not grant even a
        // registration-scope token (email ownership is part of the registration proof).
        var email = $"ekyc-unverified-{Guid.NewGuid():N}@example.com";
        var register = await _client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email,
            password = "MedFlow@2026",
            confirmPassword = "MedFlow@2026",
            fullName = "Nguyễn Văn Chưa Xác Thực",
            dateOfBirth = "1995-06-15",
            gender = "MALE",
            phone = "0912345678",
        });
        register.EnsureSuccessStatusCode();

        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "MedFlow@2026" });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    [Fact]
    public async Task Login_PendingAccountFlagOn_ReturnsRegistrationResumeToken()
    {
        var (email, _) = await RegisterAndVerifyOtpAsync();

        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "MedFlow@2026" });
        login.EnsureSuccessStatusCode();
        var body = await login.Content.ReadFromJsonAsync<LoginShape>();

        Assert.True(body!.RequiresEkycVerification);
        Assert.NotEmpty(body.AccessToken);
        Assert.Empty(body.RefreshToken); // no full session issued

        // The resume token is scope-limited: patient APIs reject it.
        using var probe = new HttpRequestMessage(HttpMethod.Get, "/api/v1/patients/me/profile");
        probe.Headers.Authorization = new AuthenticationHeaderValue("Bearer", body.AccessToken);
        var probeResponse = await _client.SendAsync(probe);
        Assert.Equal(HttpStatusCode.Forbidden, probeResponse.StatusCode);
    }

    // ------------------------------------------------------------------
    // Case 4: identification already bound to another account → activation refused
    // ------------------------------------------------------------------

    [Fact]
    public async Task MandatoryFlow_DedupeConflictCase4_ActivationRefused()
    {
        // Seed a pre-existing patient carrying the hmac of the stub OCR id (012345678901),
        // bound to a DIFFERENT user account. Hmac construction mirrors EkycCrypto.ComputeIdentificationHmac
        // with the factory's configured Ekyc:HmacKey.
        var hmac = ComputeTestIdentificationHmac("012345678901", "integration-ekyc-hmac-key");
        await CleanupStubHmacPatientsAsync(hmac);

        var otherEmail = $"ekyc-other-{Guid.NewGuid():N}@example.com";
        var otherUser = new MedFlow.Domain.Entities.User
        {
            Email = otherEmail,
            PasswordHash = "x",
            AuthProvider = "LOCAL",
            AccountStatus = AccountStatus.ACTIVE,
        };
        Db.Users.Add(otherUser);
        await Db.SaveChangesAsync();

        Db.Patients.Add(new MedFlow.Domain.Entities.Patient
        {
            PatientCode = $"P-EXT-{Guid.NewGuid():N}"[..20],
            FullName = "NGUYEN VAN EKYC",
            DateOfBirth = new DateOnly(1995, 6, 15),
            UserId = otherUser.Id,
            IdentificationHashHmac = hmac,
            Status = "ACTIVE",
        });
        await Db.SaveChangesAsync();

        // Register a NEW account that will verify with the same CCCD (stub OCR id).
        var (email, verify) = await RegisterAndVerifyOtpAsync();
        var registrationToken = verify.AccessToken!;
        var (sessionId, proxyBase) = await CreateSessionAsync(registrationToken);
        await RunFullFlowAsync(registrationToken, proxyBase);

        var finalize = await FinalizeAsync(registrationToken, sessionId);

        // eKYC itself passes (identity proven) but activation REFUSED — case 4.
        Assert.Equal(EkycVerificationStatuses.Verified, finalize.Status);
        Assert.False(finalize.AccountActivated);
        Assert.Equal(EkycDedupeOutcomes.ConflictAccountExists, finalize.ActivationOutcome);

        Db.ChangeTracker.Clear();
        var user = await Db.Users.AsNoTracking().FirstAsync(u => u.Email == email);
        Assert.Equal(AccountStatus.PENDING, user.AccountStatus);
    }

    // ------------------------------------------------------------------
    // Case 2: existing unlinked record → claim link
    // ------------------------------------------------------------------

    [Fact]
    public async Task MandatoryFlow_DedupeClaimCase2_LinksExistingRecord()
    {
        var hmac = ComputeTestIdentificationHmac("012345678901", "integration-ekyc-hmac-key");
        await CleanupStubHmacPatientsAsync(hmac);

        // Pre-existing walk-in record, no account, name/DOB matching the OCR stub.
        var existing = new MedFlow.Domain.Entities.Patient
        {
            PatientCode = $"P-WALKIN-{Guid.NewGuid():N}"[..20],
            FullName = "NGUYEN VAN EKYC",
            DateOfBirth = new DateOnly(1995, 6, 15),
            UserId = null,
            IdentificationHashHmac = hmac,
            Status = "ACTIVE",
        };
        Db.Patients.Add(existing);
        await Db.SaveChangesAsync();

        var (email, verify) = await RegisterAndVerifyOtpAsync();
        var registrationToken = verify.AccessToken!;
        var (sessionId, proxyBase) = await CreateSessionAsync(registrationToken);
        await RunFullFlowAsync(registrationToken, proxyBase);

        var finalize = await FinalizeAsync(registrationToken, sessionId);

        Assert.Equal(EkycVerificationStatuses.Verified, finalize.Status);
        Assert.True(finalize.AccountActivated);
        Assert.Equal(EkycDedupeOutcomes.ClaimLinkedActivated, finalize.ActivationOutcome);

        // User now points at the PRE-EXISTING record.
        Db.ChangeTracker.Clear();
        var user = await Db.Users.FirstAsync(u => u.Email == email);
        var claimed = await Db.Patients.FirstAsync(p => p.Id == existing.Id);
        Assert.Equal(user.Id, claimed.UserId);

        // Registration shell voided — never merged.
        var shell = await Db.Patients.FirstOrDefaultAsync(p =>
            p.Email == email && p.Id != existing.Id);
        Assert.NotNull(shell);
        Assert.Null(shell!.UserId);
        Assert.Equal(EkycDedupeResolver.PatientStatusVoidedDuplicate, shell.Status);
    }


    /// <summary>The stub OCR id is fixed (012345678901) so its hmac is fixed — repeated test runs
    /// against the shared medflow_test DB collide on uq_patients_identification_hash_hmac.
    /// Remove leftover rows from prior runs before seeding.</summary>
    private async Task CleanupStubHmacPatientsAsync(string hmac)
    {
        // Null-out rather than delete: prior sessions may FK-reference these rows.
        var stale = await Db.Patients.Where(p => p.IdentificationHashHmac == hmac).ToListAsync();
        foreach (var row in stale)
        {
            row.IdentificationHashHmac = null;
        }

        if (stale.Count > 0)
        {
            await Db.SaveChangesAsync();
        }
    }

    /// <summary>Mirror of EkycCrypto.ComputeIdentificationHmac for test-side seeding.</summary>
    private static string ComputeTestIdentificationHmac(string canonicalId, string key)
    {
        var keyBytes = Encoding.UTF8.GetBytes(key);
        var shaHex = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(canonicalId))).ToLowerInvariant();
        var digest = Encoding.UTF8.GetBytes(shaHex);
        return Convert.ToHexString(
            System.Security.Cryptography.HMACSHA256.HashData(keyBytes, digest)).ToLowerInvariant();
    }
}
