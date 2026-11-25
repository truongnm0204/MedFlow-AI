using MedFlow.Domain.Constants;
using MedFlow.Domain.Entities;

namespace MedFlow.Application.Interfaces.Services;

public class AuthSessionResult
{
    public string RawToken { get; set; } = string.Empty;
    public AuthSession Entity { get; set; } = null!;
}

public class RotateResult
{
    public bool Success { get; set; }
    public bool ReuseDetected { get; set; }
    public string? RawToken { get; set; }
    public AuthSession? Entity { get; set; }
    public User? User { get; set; }
}

public sealed record RevokeSessionFamilyOutcome(
    bool Revoked,
    long? RootSessionId,
    IReadOnlyList<long> FamilySessionIds);

public sealed record RevokeOtherSessionsOutcome(
    long? KeptSessionId,
    int RevokedCount,
    long? KeptRootSessionId = null,
    IReadOnlyList<long>? KeptFamilySessionIds = null);

public interface IAuthSessionService
{
    Task<AuthSessionResult> GenerateAsync(long userId, string? createdByIp, CancellationToken ct = default);
    Task<RotateResult> RotateAsync(string rawToken, string? ipAddress, CancellationToken ct = default);
    Task RevokeAsync(string rawToken, string? ipAddress, CancellationToken ct = default);
    Task RevokeAllForUserAsync(long userId, string? ipAddress, CancellationToken ct = default);

    Task RevokeAllForUserAsync(
        long userId,
        string? ipAddress,
        string reasonCode,
        CancellationToken ct = default)
        => RevokeAllForUserAsync(userId, ipAddress, ct);

    async Task<RevokeSessionFamilyOutcome> RevokeSessionFamilyAsync(
        long userId,
        string? rawToken,
        long? fallbackSessionId,
        string? ipAddress,
        string reasonCode = AuthSessionRevokeReasons.Logout,
        CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(rawToken))
        {
            await RevokeAsync(rawToken, ipAddress, ct);
            return new RevokeSessionFamilyOutcome(
                true,
                fallbackSessionId,
                fallbackSessionId.HasValue ? [fallbackSessionId.Value] : []);
        }

        return new RevokeSessionFamilyOutcome(false, null, []);
    }

    async Task<RevokeOtherSessionsOutcome> RevokeOtherSessionsForUserAsync(
        long userId,
        string? candidateKeepRawToken,
        long? expectedJwtSessionId,
        string? ipAddress,
        CancellationToken ct = default)
    {
        await RevokeAllForUserAsync(userId, ipAddress, AuthSessionRevokeReasons.PasswordChanged, ct);
        return new RevokeOtherSessionsOutcome(null, 0);
    }
}
