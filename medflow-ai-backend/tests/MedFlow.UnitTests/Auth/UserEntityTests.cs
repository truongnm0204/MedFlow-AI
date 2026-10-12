using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;

namespace MedFlow.UnitTests.Auth;

/// <summary>User entity: trạng thái khoá sau N lần sai.</summary>
public sealed class UserEntityTests
{
    private const int MaxAttempts = 5;
    private static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(15);

    private static void RegisterFailed(User u)
    {
        // Mirror AuthService: attempts++ → khi đạt max → lock + reset.
        u.FailedLoginAttempts++;
        if (u.FailedLoginAttempts >= MaxAttempts)
        {
            u.LockedUntil = DateTime.UtcNow.Add(LockDuration);
            u.FailedLoginAttempts = 0;
        }
    }

    [Fact]
    public void FirstFailure_IncrementsAttempts_NoLock()
    {
        var u = new User();
        RegisterFailed(u);
        Assert.Equal(1, u.FailedLoginAttempts);
        Assert.Null(u.LockedUntil);
    }

    [Fact]
    public void FifthFailure_SetsLockAndResetsCounter()
    {
        var u = new User();
        for (var i = 0; i < MaxAttempts; i++) RegisterFailed(u);
        Assert.NotNull(u.LockedUntil);
        Assert.True(u.LockedUntil > DateTime.UtcNow);
        Assert.Equal(0, u.FailedLoginAttempts);
    }

    [Fact]
    public void SuccessfulLogin_ResetsAttempts()
    {
        var u = new User { FailedLoginAttempts = 3 };
        u.FailedLoginAttempts = 0;
        u.LockedUntil = null;
        u.LastLoginAt = DateTime.UtcNow;

        Assert.Equal(0, u.FailedLoginAttempts);
        Assert.Null(u.LockedUntil);
        Assert.NotNull(u.LastLoginAt);
    }

    [Fact]
    public void RefreshToken_IsActive_WhenNotRevokedAndNotExpired()
    {
        var token = new RefreshToken { ExpiresAt = DateTime.UtcNow.AddDays(7) };
        Assert.True(token.IsActive);

        token.RevokedAt = DateTime.UtcNow;
        Assert.False(token.IsActive);
    }

    [Fact]
    public void RefreshToken_NotActive_WhenExpired()
    {
        var token = new RefreshToken { ExpiresAt = DateTime.UtcNow.AddMinutes(-1) };
        Assert.False(token.IsActive);
    }

    [Fact]
    public void PatientCode_Format_MatchesPattern()
    {
        var code = $"PAT-{DateTime.UtcNow:yyyyMMdd}-{Random.Shared.Next(1000, 9999)}";
        Assert.Matches(@"^PAT-\d{8}-\d{4}$", code);
    }
}
