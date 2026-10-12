using MedFlow.Domain.Enums;

namespace MedFlow.Domain.Entities;

public class User
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? PasswordHash { get; set; }
    public string AuthProvider { get; set; } = "LOCAL";
    public string? ProviderSubject { get; set; }
    public AccountStatus AccountStatus { get; set; } = AccountStatus.ACTIVE;
    public DateTime? EmailVerifiedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public int FailedLoginAttempts { get; set; }
    public DateTime? LockedUntil { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
    public Patient? Patient { get; set; }
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
}
