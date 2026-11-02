namespace MedFlow.Application.DTOs.Appointments;

public sealed record BookingServiceInfoDto(
    string ServiceId,
    string ServiceCode,
    string ServiceName,
    string DepartmentName,
    string Location,
    int DurationMinutes,
    string DurationDescription,
    decimal Price,
    string OnlineBadgeLabel);
