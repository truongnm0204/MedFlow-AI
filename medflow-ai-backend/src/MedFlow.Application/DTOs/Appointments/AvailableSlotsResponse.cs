namespace MedFlow.Application.DTOs.Appointments;

public sealed record AvailableSlotsResponse(
    BookingDoctorServiceInfoDto DoctorService,
    string MonthLabel,
    IReadOnlyList<BookingPatientProfileDto> Profiles,
    IReadOnlyList<BookingDateOptionDto> Dates,
    IReadOnlyList<BookingTimeSlotDto> MorningSlots,
    IReadOnlyList<BookingTimeSlotDto> AfternoonSlots,
    DoctorDayShiftDto ActiveDayShift);
