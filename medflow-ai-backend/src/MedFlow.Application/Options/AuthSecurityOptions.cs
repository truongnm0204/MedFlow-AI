namespace MedFlow.Application.Options;

/// <summary>
/// Configurable authentication, OTP, lockout, and identity claim security thresholds (bound to <c>Auth</c> section).
/// Default <see cref="LockoutDurationMinutes"/> aligns with <c>docs/system-architecture.md</c> (15 minutes after 5 failed attempts)
/// and can be overridden per environment (e.g. 30 minutes) without code changes.
/// </summary>
public sealed class AuthSecurityOptions
{
    public const string SectionName = "Auth";

    public int OtpTtlMinutes { get; set; } = 5;

    public int OtpResendThrottleSeconds { get; set; } = 45;

    public int OtpMaxAttempts { get; set; } = 5;

    public int MaxLoginAttempts { get; set; } = 5;

    public int LockoutDurationMinutes { get; set; } = 15;

    public int IdentityClaimTtlDays { get; set; } = 7;
}
