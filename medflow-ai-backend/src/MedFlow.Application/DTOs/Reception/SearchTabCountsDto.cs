namespace MedFlow.Application.DTOs.Reception;

public sealed record SearchTabCountsDto(
    int All,
    int HasAppointment,
    int CheckedIn,
    int NoAppointment);
