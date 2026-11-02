namespace MedFlow.Application.DTOs.Appointments;

public sealed record DoctorDayShiftDto(
    bool HasMorningShift,
    bool HasAfternoonShift,
    string ShiftSummaryLabel,
    string? OffShiftNote,
    IReadOnlyList<BookingTimeSlotDto> MorningSlots,
    IReadOnlyList<BookingTimeSlotDto> AfternoonSlots);
