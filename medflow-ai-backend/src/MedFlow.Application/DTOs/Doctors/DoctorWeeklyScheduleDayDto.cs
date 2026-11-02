namespace MedFlow.Application.DTOs.Doctors;

public sealed record DoctorWeeklyScheduleDayDto(
    string DayOfWeek,
    bool? HasAvailableSlot,
    string? MorningTime,
    string? AfternoonTime,
    string? Note,
    string? StatusTag,
    bool? IsActive);
