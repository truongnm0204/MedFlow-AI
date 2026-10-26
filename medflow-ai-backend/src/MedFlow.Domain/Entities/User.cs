using MedFlow.Domain.Enums;

namespace MedFlow.Domain.Entities;

public class User
{
    public long Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? PasswordHash { get; set; }
    public string AuthProvider { get; set; } = "LOCAL";
    public AccountStatus AccountStatus { get; set; } = AccountStatus.ACTIVE;
    public DateTime? EmailVerifiedAt { get; set; }
    public short FailedLoginAttempts { get; set; }
    public DateTime? LockedUntil { get; set; }

    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
    public Patient? Patient { get; set; }
    public ICollection<AuthSession> AuthSessions { get; set; } = new List<AuthSession>();
}
