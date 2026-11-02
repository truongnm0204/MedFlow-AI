namespace MedFlow.Application.DTOs.Appointments;

public sealed record AppointmentMemberCountDto(
    string Id,
    string PatientCode,
    string FullName,
    string RelationshipLabel,
    string AgeLabel,
    int AppointmentCount);
