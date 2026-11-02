namespace MedFlow.Application.DTOs.Appointments;

public sealed record BookingTimeSlotDto(
    string Id,
    string TimeRange,
    string Shift,
    string ShiftLabel,
    string Status,
    int RemainingSlots,
    string BadgeText);
