namespace MedFlow.Application.DTOs.Patients;

public sealed record PatientProfileResponse(
    long Id,
    long UserId,
    string PatientCode,
    string FullName,
    DateOnly? DateOfBirth,
    int? Age,
    string? Gender,
    string? Phone,
    string? Email,
    bool IsEmailVerified,
    bool IsIdentityVerified,
    string? MaskedIdentification,
    string Status,
    string? Address,
    string? BloodType,
    IReadOnlyList<string> AllowedActions
);
