using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MedFlow.Application.DTOs.Auth;
using MedFlow.Domain.Enums;
using Microsoft.EntityFrameworkCore;

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
    public async Task Register_DuplicateEmail_Returns400_CaseInsensitive()
    {
        var email = UniqueEmail();
        var first = await Client.PostAsJsonAsync("/api/v1/auth/register", ValidRegister(email));
        first.EnsureSuccessStatusCode();

        var dup = await Client.PostAsJsonAsync("/api/v1/auth/register",
            ValidRegister(email.ToUpperInvariant()));
        Assert.Equal(HttpStatusCode.BadRequest, dup.StatusCode);
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
}
