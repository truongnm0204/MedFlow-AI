namespace MedFlow.Application.DTOs.Patients;

public sealed record SendIdentityClaimOtpResponse(DateTime ExpiresAt);

public sealed record CreateIdentityClaimRequest(string PatientCode, long HospitalId, string Otp);

public sealed record IdentityClaimResponse(
    long ClaimId,
    string Status,
    DateTime CreatedAt,
    DateTime ExpiresAt,
    DateTime? DecidedAt,
    string? DecisionNote);

public sealed record StaffIdentityClaimDto(
    long ClaimId,
    long RequesterUserId,
    string RequestedPatientCode,
    long? TargetPatientId,
    long? CurrentPatientId,
    long HospitalId,
    string Status,
    DateTime CreatedAt,
    DateTime ExpiresAt);

public sealed record RejectIdentityClaimRequest(string Reason);
