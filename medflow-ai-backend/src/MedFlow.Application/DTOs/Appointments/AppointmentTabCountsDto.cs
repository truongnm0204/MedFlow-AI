namespace MedFlow.Application.DTOs.Appointments;

public sealed record AppointmentTabCountsDto(
    int All,
    int Upcoming,
    int Completed,
    int Cancelled);
