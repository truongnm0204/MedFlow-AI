namespace MedFlow.Application.DTOs.Appointments;

public sealed record BookingDateOptionDto(
    string DateIso,
    string DayTopLabel,
    string DayNumber,
    string SubDateLabel,
    string FullDateDisplay,
    string Status,
    int RemainingSlots,
    string BadgeText,
    bool IsToday,
    string? DoctorShiftSummary);
