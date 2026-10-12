using MedFlow.Domain.Enums;

namespace MedFlow.Domain.Entities;

public class AuthVerificationChallenge
{
    public Guid Id { get; set; }
    public Guid? UserId { get; set; }
    public ChallengePurpose Purpose { get; set; }
    public ChallengeChannel Channel { get; set; }
    public byte[] RecipientFingerprint { get; set; } = [];
    public string SecretHash { get; set; } = string.Empty;
    public ChallengeStatus Status { get; set; } = ChallengeStatus.PENDING;
    public int Attempts { get; set; }
    public int MaxAttempts { get; set; } = 5;
    public DateTime ExpiresAt { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public User? User { get; set; }
}
