using System.Net;
using System.Net.Http.Json;
using MedFlow.Application.DTOs.Auth;
using MedFlow.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.IntegrationTests.Auth;

/// <summary>OTP edge cases: sai mã, hết hạn, replay, lock, throttle.</summary>
public sealed class AuthOtpTests(TestWebAppFactory factory) : AuthTestBase(factory)
{
    [Fact]
    public async Task VerifyOtp_WrongCode_IncrementsAttempts()
    {
        var email = UniqueEmail();
        await Client.PostAsJsonAsync("/api/v1/auth/register", ValidRegister(email));

        var wrong = "000000" == Factory.EmailSender.LatestOtpFor(email) ? "111111" : "000000";
        var response = await Client.PostAsJsonAsync("/api/v1/auth/verify-otp",
            new VerifyOtpRequest { Email = email, Otp = wrong });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var user = await Db.Users.FirstAsync(u => u.Email == email);
        var challenge = await Db.AuthVerificationChallenges
            .FirstAsync(c => c.UserId == user.Id && c.Status == ChallengeStatus.PENDING);
        Assert.Equal(1, challenge.Attempts);
    }

    [Fact]
    public async Task VerifyOtp_FiveWrong_LocksChallenge()
    {
        var email = UniqueEmail();
        await Client.PostAsJsonAsync("/api/v1/auth/register", ValidRegister(email));

        var otp = Factory.EmailSender.LatestOtpFor(email)!;
        var wrong = otp == "000000" ? "111111" : "000000";

        // Attempts++ sau mỗi lần sai — cần 6 lần để đạt MaxAttempts=5 → LOCKED.
        for (var i = 0; i < 6; i++)
            await Client.PostAsJsonAsync("/api/v1/auth/verify-otp",
                new VerifyOtpRequest { Email = email, Otp = wrong });

        var user = await Db.Users.FirstAsync(u => u.Email == email);
        var challenge = await Db.AuthVerificationChallenges
            .Where(c => c.UserId == user.Id)
            .OrderByDescending(c => c.CreatedAt)
            .FirstAsync();
        Assert.Equal(ChallengeStatus.LOCKED, challenge.Status);

        // Lần 6 với OTP đúng vẫn fail vì đã không còn PENDING.
        var response = await Client.PostAsJsonAsync("/api/v1/auth/verify-otp",
            new VerifyOtpRequest { Email = email, Otp = otp });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task VerifyOtp_AfterExpiry_Returns400()
    {
        var email = UniqueEmail();
        await Client.PostAsJsonAsync("/api/v1/auth/register", ValidRegister(email));
        var otp = Factory.EmailSender.LatestOtpFor(email)!;

        var user = await Db.Users.FirstAsync(u => u.Email == email);
        // Bypass EF change tracking — check constraint chặn expires_at <= created_at.
        // Check constraint expires_at > created_at — phải update cả created_at cùng lúc.
        var past = DateTime.UtcNow.AddMinutes(-10);
        await Db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE medflow.auth_verification_challenges SET expires_at = {past.AddMinutes(1)}, created_at = {past} WHERE user_id = {user.Id} AND status = 'PENDING'");

        var response = await Client.PostAsJsonAsync("/api/v1/auth/verify-otp",
            new VerifyOtpRequest { Email = email, Otp = otp });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task VerifyOtp_Replay_Returns400()
    {
        var email = UniqueEmail();
        await Client.PostAsJsonAsync("/api/v1/auth/register", ValidRegister(email));
        var otp = Factory.EmailSender.LatestOtpFor(email)!;

        var first = await Client.PostAsJsonAsync("/api/v1/auth/verify-otp",
            new VerifyOtpRequest { Email = email, Otp = otp });
        first.EnsureSuccessStatusCode();

        var replay = await Client.PostAsJsonAsync("/api/v1/auth/verify-otp",
            new VerifyOtpRequest { Email = email, Otp = otp });
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
    }

    [Fact]
    public async Task ResendOtp_WithinThrottle_Returns400()
    {
        var email = UniqueEmail();
        await Client.PostAsJsonAsync("/api/v1/auth/register", ValidRegister(email));

        var resend = await Client.PostAsJsonAsync("/api/v1/auth/resend-otp",
            new ResendOtpRequest { Email = email });
        Assert.Equal(HttpStatusCode.BadRequest, resend.StatusCode);
    }

    [Fact]
    public async Task ResendOtp_NonExistentEmail_Returns200_Generic()
    {
        var resend = await Client.PostAsJsonAsync("/api/v1/auth/resend-otp",
            new ResendOtpRequest { Email = UniqueEmail() });
        Assert.Equal(HttpStatusCode.OK, resend.StatusCode);
    }
}
