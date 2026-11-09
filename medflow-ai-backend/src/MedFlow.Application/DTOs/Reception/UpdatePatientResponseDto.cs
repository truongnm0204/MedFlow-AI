namespace MedFlow.Application.DTOs.Reception;

public sealed record UpdatePatientResponseDto(
    long Id,
    string PatientCode,
    string FullName,
    DateOnly? DateOfBirth,
    string? Gender,
    string? Phone,
    string? Address,
    string? Email,
    string? IdentityCardMasked,
    string? BloodType,
    DateTime UpdatedAt);
