namespace MedFlow.Application.DTOs.Appointments;

public sealed record ServiceShiftOptionDto(
    string Id,
    string Shift,
    string Title,
    string TimeRange,
    string CheckInWindow,
    int RemainingCapacity,
    string BadgeText,
    string Status,
    string Description);
