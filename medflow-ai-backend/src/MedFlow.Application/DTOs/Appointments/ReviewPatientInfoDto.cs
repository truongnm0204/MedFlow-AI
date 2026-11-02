namespace MedFlow.Application.DTOs.Appointments;

public sealed record ReviewPatientInfoDto(
    string PatientId,
    string PatientCode,
    string FullName,
    string RelationshipBadgeLabel,
    bool IsPrimaryAccount,
    string DateOfBirth,
    string AgeLabel,
    string Gender,
    string Phone,
    string NationalId,
    string CurrentAddress,
    string SymptomNote);
