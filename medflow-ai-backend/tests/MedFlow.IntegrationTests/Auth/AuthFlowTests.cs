using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MedFlow.API.Realtime;
using MedFlow.Application.DTOs.Auth;
using MedFlow.Application.DTOs.Realtime;
using MedFlow.Application.Options;
using MedFlow.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MedFlow.IntegrationTests.Auth;

/// <summary>Happy path: register → verify-otp → login → me.</summary>
public sealed class AuthFlowTests(TestWebAppFactory factory) : AuthTestBase(factory)
{
    [Fact]
    public async Task Register_ValidRequest_Returns200_AndCreatesPendingUser()
    {
        var (response, email) = await RegisterAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var user = await Db.Users.FirstOrDefaultAsync(u => u.Email == email);
        Assert.NotNull(user);
        Assert.Equal(AccountStatus.PENDING, user.AccountStatus);

        var patient = await Db.Patients.FirstOrDefaultAsync(p => p.UserId == user.Id);
        Assert.NotNull(patient);
        Assert.StartsWith("PAT-", patient.PatientCode);

        var challenge = await Db.AuthVerificationChallenges
            .FirstOrDefaultAsync(c => c.UserId == user.Id && c.Status == ChallengeStatus.PENDING);
        Assert.NotNull(challenge);
        Assert.Equal(32, challenge.RecipientFingerprint.Length);

        Assert.NotNull(Factory.EmailSender.LatestOtpFor(email));
    }

    [Fact]
    public async Task Register_DuplicateEmail_PendingPreservesCredentials_ActiveReturns400_CaseInsensitive()
    {
        var email = UniqueEmail();
        var initialReq = ValidRegister(email);
        var first = await Client.PostAsJsonAsync("/api/v1/auth/register", initialReq);
        first.EnsureSuccessStatusCode();

        var initialUser = await Db.Users.AsNoTracking().FirstAsync(u => u.Email == email);
        var initialPatient = await Db.Patients.AsNoTracking().FirstAsync(p => p.UserId == initialUser.Id);

        // BUG-P2-07: Re-registering a PENDING email returns 200 without overwriting PasswordHash/FullName/Phone
        var hijackedReq = new RegisterRequest
        {
            Email = email.ToUpperInvariant(),
            Password = "HijackPassword999!",
            ConfirmPassword = "HijackPassword999!",
            FullName = "Kẻ Chiếm Đoạt",
            DateOfBirth = initialReq.DateOfBirth,
            Gender = initialReq.Gender,
            Phone = "0999888777",
        };
        var pendingDup = await Client.PostAsJsonAsync("/api/v1/auth/register", hijackedReq);
        Assert.Equal(HttpStatusCode.OK, pendingDup.StatusCode);

        var afterDupUser = await Db.Users.AsNoTracking().FirstAsync(u => u.Id == initialUser.Id);
        var afterDupPatient = await Db.Patients.AsNoTracking().FirstAsync(p => p.UserId == initialUser.Id);
        Assert.Equal(initialUser.PasswordHash, afterDupUser.PasswordHash);
        Assert.Equal(initialPatient.FullName, afterDupPatient.FullName);
        Assert.Equal(initialPatient.Phone, afterDupPatient.Phone);

        // Once verified (ACTIVE), duplicate registration returns 400 BadRequest
        var otp = Factory.EmailSender.LatestOtpFor(email)!;
        var verify = await Client.PostAsJsonAsync("/api/v1/auth/verify-otp",
            new VerifyOtpRequest { Email = email, Otp = otp });
        verify.EnsureSuccessStatusCode();

        var activeDup = await Client.PostAsJsonAsync("/api/v1/auth/register",
            ValidRegister(email.ToUpperInvariant()));
        Assert.Equal(HttpStatusCode.BadRequest, activeDup.StatusCode);
    }

    [Fact]
    public async Task VerifyOtp_CorrectCode_ActivatesAccount()
    {
        var email = UniqueEmail();
        var register = await Client.PostAsJsonAsync("/api/v1/auth/register", ValidRegister(email));
        register.EnsureSuccessStatusCode();

        var otp = Factory.EmailSender.LatestOtpFor(email)!;
        var verify = await Client.PostAsJsonAsync("/api/v1/auth/verify-otp",
            new VerifyOtpRequest { Email = email, Otp = otp });

        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);

        var user = await Db.Users.FirstAsync(u => u.Email == email);
        Assert.Equal(AccountStatus.ACTIVE, user.AccountStatus);
        Assert.NotNull(user.EmailVerifiedAt);

        var challenge = await Db.AuthVerificationChallenges
            .FirstAsync(c => c.UserId == user.Id && c.Status == ChallengeStatus.VERIFIED);
        Assert.NotNull(challenge.VerifiedAt);
    }

    [Fact]
    public async Task Login_VerifiedUser_ReturnsJwt_WithRoles()
    {
        var email = await RegisterAndVerifyAsync();
        var login = await LoginAsync(email);

        Assert.InRange(login.AccessTokenExpiresIn, 3590, 3600);
        Assert.InRange(login.RefreshTokenExpiresIn, 604790, 604800);

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(login.AccessToken);
        Assert.Equal("MedFlow", jwt.Issuer);
        Assert.Contains(jwt.Claims, c => c.Type == "sub" || c.Type == JwtRegisteredClaimNames.Sub);
        Assert.Contains(jwt.Claims, c => c.Value == "PATIENT");
    }

    [Fact]
    public async Task Me_WithToken_ReturnsProfile()
    {
        var (email, tokens) = await RegisterVerifyLoginAsync();

        Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var me = await Client.GetFromJsonAsync<UserDto>("/api/v1/auth/me");

        Assert.NotNull(me);
        Assert.Equal(email, me.Email);
        Assert.StartsWith("PAT-", me.PatientCode);
        Assert.Equal("Nguyễn Văn Test", me.FullName);
        Assert.Contains("PATIENT", me.Roles);
    }

    [Fact]
    public async Task Me_WithoutToken_Returns401()
    {
        var response = await Client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ForgotAndResetPassword_EndToEnd_CommitsFailedAttempt_EnforcesSingleUseOtp_AndRevokesSessions()
    {
        var (email, oldTokens) = await RegisterVerifyLoginAsync();

        var forgotRes = await Client.PostAsJsonAsync(
            "/api/v1/auth/forgot-password",
            new ForgotPasswordRequest { Email = email });
        Assert.Equal(HttpStatusCode.OK, forgotRes.StatusCode);
        var forgotBody = await forgotRes.Content.ReadFromJsonAsync<ForgotPasswordResponse>();
        Assert.NotNull(forgotBody);
        Assert.Equal(
            "Nếu email đã được đăng ký và đủ điều kiện, mã OTP khôi phục mật khẩu sẽ được gửi đến email của bạn.",
            forgotBody.Message);

        var otp = Factory.EmailSender.LatestOtpFor(email);
        Assert.NotNull(otp);

        // Wrong OTP returns 400 AND persists Attempts = 1 in PostgreSQL despite failed reset
        var wrongCode = otp == "000000" ? "999999" : "000000";
        var wrongReset = await Client.PostAsJsonAsync(
            "/api/v1/auth/reset-password",
            new ResetPasswordRequest
            {
                Email = email,
                OtpCode = wrongCode,
                NewPassword = "NewStrongPass456",
            });
        Assert.Equal(HttpStatusCode.BadRequest, wrongReset.StatusCode);

        var user = await Db.Users.AsNoTracking().SingleAsync(u => u.Email == email);
        var pendingChallenge = await Db.AuthVerificationChallenges
            .AsNoTracking()
            .SingleAsync(c => c.UserId == user.Id && c.Purpose == ChallengePurpose.PASSWORD_RESET);
        Assert.Equal(1, pendingChallenge.Attempts);

        // Correct OTP resets password & revokes old refresh token
        var okReset = await Client.PostAsJsonAsync(
            "/api/v1/auth/reset-password",
            new ResetPasswordRequest
            {
                Email = email,
                OtpCode = otp!,
                NewPassword = "NewStrongPass456",
            });
        Assert.Equal(HttpStatusCode.OK, okReset.StatusCode);

        // Replaying the same OTP must be rejected (single-use)
        var replayReset = await Client.PostAsJsonAsync(
            "/api/v1/auth/reset-password",
            new ResetPasswordRequest
            {
                Email = email,
                OtpCode = otp!,
                NewPassword = "AnotherPass789",
            });
        Assert.Equal(HttpStatusCode.BadRequest, replayReset.StatusCode);

        // Old refresh token is now revoked
        var oldRefreshRes = await Client.PostAsJsonAsync(
            "/api/v1/auth/refresh",
            new RefreshRequest { RefreshToken = oldTokens.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, oldRefreshRes.StatusCode);

        // Login with new password succeeds
        var newTokens = await LoginAsync(email, "NewStrongPass456");
        Assert.NotEmpty(newTokens.AccessToken);
    }

    [Fact]
    public async Task ChangePassword_MultiDeviceSessions_KeepsVerifiedCurrentSessionAndRevokesOtherDevices()
    {
        const string initialPassword = "MedFlow@2026";
        var email = await RegisterAndVerifyAsync();
        var laptopTokens = await LoginAsync(email, initialPassword);
        var phoneTokens = await LoginAsync(email, initialPassword);
        var otherPcTokens = await LoginAsync(email, initialPassword);

        // Verify JWT carries session binding ("sid")
        var laptopJwt = new JwtSecurityTokenHandler().ReadJwtToken(laptopTokens.AccessToken);
        Assert.Contains(laptopJwt.Claims, c => c.Type == "sid" && long.TryParse(c.Value, out var sid) && sid > 0);

        using var changeReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/change-password")
        {
            Content = JsonContent.Create(new ChangePasswordRequest
            {
                CurrentPassword = initialPassword,
                NewPassword = "UpdatedPass789",
                CurrentRefreshToken = laptopTokens.RefreshToken,
            }),
        };
        changeReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", laptopTokens.AccessToken);

        var changeRes = await Client.SendAsync(changeReq);
        Assert.Equal(HttpStatusCode.OK, changeRes.StatusCode);
        var changeBody = await changeRes.Content.ReadFromJsonAsync<ChangePasswordResponse>();
        Assert.NotNull(changeBody);
        Assert.True(changeBody.KeptCurrentSession);

        // Phone & Other PC refresh tokens are revoked -> 401
        var phoneRefresh = await Client.PostAsJsonAsync(
            "/api/v1/auth/refresh",
            new RefreshRequest { RefreshToken = phoneTokens.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, phoneRefresh.StatusCode);

        var otherPcRefresh = await Client.PostAsJsonAsync(
            "/api/v1/auth/refresh",
            new RefreshRequest { RefreshToken = otherPcTokens.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, otherPcRefresh.StatusCode);

        // Laptop's refresh token remains active -> rotates 200 OK!
        var laptopRefresh = await Client.PostAsJsonAsync(
            "/api/v1/auth/refresh",
            new RefreshRequest { RefreshToken = laptopTokens.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, laptopRefresh.StatusCode);
    }

    [Fact]
    public void RealtimeConnectionRegistry_EvictUserSubscriptionsExceptSession_KeepsActiveSessionAndEvictsOtherDevices()
    {
        var monitor = Factory.Services.GetRequiredService<IOptionsMonitor<RealtimeOptions>>();
        var registry = new ClinicalRealtimeConnectionRegistry(monitor);

        registry.RegisterConnection("conn-laptop", userId: 77, sessionId: 501);
        registry.RegisterConnection("conn-phone", userId: 77, sessionId: 502);
        registry.RegisterConnection("conn-other-pc", userId: 77, sessionId: null);

        var grant = new AuthorizedRealtimeSubscription(
            ResourceKey: "visit-payment:1001",
            PrimaryGroupNames: ["visit:1001"],
            BucketGroupNames: [],
            HospitalId: 1,
            PatientId: 10,
            AppointmentId: null,
            VisitId: 1001);

        registry.TryUpsertSubscription("conn-laptop", 77, grant);
        registry.TryUpsertSubscription("conn-phone", 77, grant);
        registry.TryUpsertSubscription("conn-other-pc", 77, grant);

        var evicted = registry.EvictUserSubscriptionsExceptSession(userId: 77, keepSessionId: 501);

        Assert.Equal(2, evicted.Count);
        Assert.DoesNotContain(evicted, e => e.ConnectionId == "conn-laptop");
        Assert.Contains(evicted, e => e.ConnectionId == "conn-phone");
        Assert.Contains(evicted, e => e.ConnectionId == "conn-other-pc");
    }
}
