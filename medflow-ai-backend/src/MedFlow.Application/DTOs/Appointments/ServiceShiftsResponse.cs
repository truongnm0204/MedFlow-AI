namespace MedFlow.Application.DTOs.Appointments;

public sealed record ServiceShiftsResponse(
    BookingServiceInfoDto ServiceInfo,
    string MonthLabel,
    IReadOnlyList<BookingPatientProfileDto> Profiles,
    IReadOnlyList<BookingDateOptionDto> Dates,
    IReadOnlyList<ServiceShiftOptionDto> Shifts);
