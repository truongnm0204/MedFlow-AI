namespace MedFlow.Application.DTOs.Visits;

public sealed record CallNextPatientResponseDto(
    string ServicePointId,
    string Outcome,
    string Message,
    string? SelectionReason,
    ExaminationQueueEntryDto? CalledEntry,
    int? PreviousStateVersion,
    int? StateVersion,
    DateTime UpdatedAtUtc,
    IReadOnlyList<ExaminationQueueEntryDto> Entries);
