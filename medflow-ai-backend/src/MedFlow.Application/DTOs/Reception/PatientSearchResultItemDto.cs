namespace MedFlow.Application.DTOs.Reception;

public sealed record PatientSearchResultItemDto(
    long Id,
    string PatientCode,
    string FullName,
    string? Gender,
    DateOnly? DateOfBirth,
    string? IdentityCardMasked,
    string? Phone,
    string? Address,
    TodayAppointmentSummaryDto? TodayAppointment,
    int TotalPastVisits);
