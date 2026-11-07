namespace MedFlow.Application.DTOs.Reception;

public sealed record CreatePatientResponseDto(
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
    DateTime CreatedAt);
