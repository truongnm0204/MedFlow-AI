namespace MedFlow.Application.DTOs.Appointments;

public sealed record BookingPatientProfileDto(
    string Id,
    string FullName,
    string RelationshipLabel,
    string BadgeText,
    string BadgeVariant,
    string Gender,
    int Age,
    string MetaPrimary,
    string MetaSecondary,
    string PatientCode);
