namespace MedFlow.Domain.Entities;

/// <summary>Refresh-token session. Maps to <c>auth_sessions</c> (v4.1 + 005 + 023).</summary>
public class AuthSession
{
    public long Id { get; set; }
    public long UserId { get; set; }
    /// <summary>SHA-256 hex of opaque token — never store plaintext.</summary>
    public string RefreshTokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    /// <summary>Hash of the token that replaced this one on rotation.</summary>
    public string? ReplacedByTokenHash { get; set; }
    public string? CreatedByIp { get; set; }
    public string? RevokedByIp { get; set; }
    public DateTime CreatedAt { get; set; }
    /// <summary>Root login session ID preserved across silent refresh-token rotations (ADR-0059).</summary>
    public long? RootSessionId { get; set; }
    /// <summary>Reason code when RevokedAt is set (e.g. ROTATED, LOGOUT, PASSWORD_RESET).</summary>
    public string? RevokedReason { get; set; }

    public User User { get; set; } = null!;

    public bool IsActive => RevokedAt is null && ExpiresAt > DateTime.UtcNow;
}
