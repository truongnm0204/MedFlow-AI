namespace MedFlow.Application.DTOs.Patients;

public sealed record PatientVisitListItemDto(
    long VisitId,
    string? VisitCode,
    string? VisitStatus,
    DateTime? CheckedInAt,
    DateTime? CompletedAt,
    string? HospitalName,
    string? PaymentStatus,
    string? PaymentCode,
    DateTime? ReadyForReturnAt = null,
    bool IsPresentToday = false,
    string? PresenceState = null);

public sealed record PatientVisitListResponseDto(IReadOnlyList<PatientVisitListItemDto> Items);
