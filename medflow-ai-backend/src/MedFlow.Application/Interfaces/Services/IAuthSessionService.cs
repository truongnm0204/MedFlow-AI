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

public sealed record RevokeOtherSessionsOutcome(long? KeptSessionId, int RevokedCount);

public interface IAuthSessionService
{
    Task<AuthSessionResult> GenerateAsync(long userId, string? createdByIp, CancellationToken ct = default);
    Task<RotateResult> RotateAsync(string rawToken, string? ipAddress, CancellationToken ct = default);
    Task RevokeAsync(string rawToken, string? ipAddress, CancellationToken ct = default);
    Task RevokeAllForUserAsync(long userId, string? ipAddress, CancellationToken ct = default);

    async Task<RevokeOtherSessionsOutcome> RevokeOtherSessionsForUserAsync(
        long userId,
        string? candidateKeepRawToken,
        long? expectedJwtSessionId,
        string? ipAddress,
        CancellationToken ct = default)
    {
        await RevokeAllForUserAsync(userId, ipAddress, ct);
        return new RevokeOtherSessionsOutcome(null, 0);
    }
}
