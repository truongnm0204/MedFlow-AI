namespace MedFlow.Application.DTOs.Appointments;

public sealed record BookingDoctorServiceInfoDto(
    string DoctorId,
    string DoctorName,
    string RoleTag,
    string Specialty,
    string Room,
    string ShortRoom,
    string ServiceId,
    string ServiceCode,
    string ServiceName,
    decimal Price,
    string AvatarUrl,
    string AvailabilityLabel,
    string DutyNote);
