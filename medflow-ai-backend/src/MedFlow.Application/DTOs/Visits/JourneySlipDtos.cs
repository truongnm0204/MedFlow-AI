namespace MedFlow.Application.DTOs.Visits;

/// <summary>
/// Minimal operational journey slip (Stage E2 / QE-21 / E2-R09).
/// Contains only the operational fields a rightful viewer (patient owner or same-hospital staff)
/// needs to coordinate the visit — NO service names, payment data, diagnosis, results, or patient PHI.
/// </summary>
public sealed record JourneySlipTicketDto(
    string? QueueEntryId,
    string QueueType,
    string TicketNo,
    string? ServicePointName,
    string Status);

public sealed record JourneySlipDto(
    long VisitId,
    string VisitCode,
    string PresenceState,
    string? CurrentAssignedDoctorDisplay,
    string? CurrentExaminationRoomDisplay,
    IReadOnlyList<JourneySlipTicketDto> Tickets,
    string ServingDate,
    DateTime GeneratedAtUtc,
    string InternalLookupQr);

public sealed record JourneySlipPrintResultDto(
    long VisitId,
    DateTime PrintedAtUtc,
    bool PrintAuditRecorded,
    string Note);
