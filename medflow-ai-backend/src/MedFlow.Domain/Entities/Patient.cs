namespace MedFlow.Domain.Entities;

public class Patient
{
    public long Id { get; set; }
    public long? UserId { get; set; }
    public string PatientCode { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public DateOnly? DateOfBirth { get; set; }
    public string? Gender { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    /// <summary>SHA-256 HMAC hash of identity number — never store plaintext.</summary>
    public string? IdentificationHash { get; set; }
    public string? IdentificationHashHmac { get; set; }
    public short? HashVersion { get; set; }

    /// <summary>
    /// Identity verification state. VERIFIED is written ONLY by a successful eKYC finalize
    /// (migration 032); no manual API can set it.
    /// </summary>
    public string IdentityStatus { get; set; } = Constants.PatientIdentityStatuses.Unverified;

    /// <summary>
    /// Owner-approved reason for creating this profile without eKYC
    /// (<see cref="Constants.PatientUnverifiedReasons"/>). NULL while UNVERIFIED for profiles
    /// whose eKYC is still in progress.
    /// </summary>
    public string? UnverifiedReason { get; set; }

    /// <summary>Free-text operational note accompanying an UNVERIFIED creation (optional).</summary>
    public string? IdentityNote { get; set; }

    public string Status { get; set; } = "ACTIVE";
    public string? Address { get; set; }
    public string? BloodType { get; set; }
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactRelationship { get; set; }
    public string? EmergencyContactPhone { get; set; }

    public User? User { get; set; }
}
