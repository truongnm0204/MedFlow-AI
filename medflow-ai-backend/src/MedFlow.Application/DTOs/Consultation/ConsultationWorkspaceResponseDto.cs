namespace MedFlow.Application.DTOs.Consultation;

public sealed record ConsultationWorkspaceResponseDto(
    string ActionOutcome,
    string WorkspaceState,
    bool CanEditClinical,
    string? ReadOnlyReason,
    DateTime GeneratedAtUtc,
    ConsultationVisitSummaryDto Visit,
    ConsultationPatientSummaryDto Patient,
    ConsultationQueueContextDto? Queue,
    ConsultationClinicalContextDto ClinicalContext,
    IReadOnlyList<ConsultationWorkspaceActionDto> AllowedActions);

public sealed record ConsultationVisitSummaryDto(
    long VisitId,
    string VisitCode,
    string Status,
    string StatusLabel,
    string VisitType,
    string VisitTypeLabel,
    string? ReasonForVisit,
    long? AppointmentId,
    string? AppointmentCode,
    DateTime? ScheduledStartAt,
    DateTime? ScheduledEndAt,
    DateTime? CheckedInAt,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    long HospitalId,
    string HospitalCode,
    string HospitalName,
    long? DepartmentId,
    string? DepartmentCode,
    string DepartmentName,
    long? SpecialtyId,
    string? SpecialtyCode,
    string SpecialtyName,
    long? RoomId,
    string? RoomCode,
    string? RoomName,
    string? RoomLocation,
    long AssignedDoctorId,
    string AssignedDoctorCode,
    string AssignedDoctorName,
    string? AssignedDoctorTitle,
    long? ServiceId,
    string? ServiceCode,
    string? ServiceName);

public sealed record ConsultationPatientSummaryDto(
    long PatientId,
    string PatientCode,
    string FullName,
    string? DateOfBirth,
    int? BirthYear,
    int? Age,
    string? Gender,
    string GenderLabel,
    string? Phone,
    string? Email,
    string? Address,
    string? BloodType,
    string? AllergiesSummary);

public sealed record ConsultationQueueContextDto(
    long QueueEntryId,
    string FormattedQueueEntryId,
    string TicketNo,
    int QueueNumber,
    string QueueDate,
    string QueueType,
    string Status,
    string QueueGroup,
    string? IneligibleReason,
    string Priority,
    string PresenceStatus,
    string ArrivalClass,
    int ServingOrder,
    bool IsEligible,
    bool IsAppointmentProtected,
    string? HoldReason,
    string? BlockedReason,
    int StateVersion,
    string ServicePointId,
    string ServicePointName,
    string? ScheduledAt,
    string? ArrivedAt,
    string? ReadyAt,
    string? CalledAt,
    string? StartedAt,
    string? CompletedAt);

public sealed record ConsultationClinicalContextDto(
    long? MedicalRecordId,
    string MedicalRecordStatus,
    DateTime? FinalizedAt,
    int? CurrentRevisionNo,
    bool IsConfirmed,
    DateTime? LastRecordedAt,
    string? PresentingComplaint,
    string? SymptomHistory,
    string? RelevantHistoryAllergies,
    ConsultationVitalSignsDto VitalSigns,
    string? ClinicalFindings,
    string? ClinicalConclusion,
    string? AmendmentReason,
    IReadOnlyList<ConsultationDiagnosisItemDto> Diagnoses,
    ConsultationDiagnosticSummaryDto DiagnosticSummary,
    ConsultationPrescriptionSummaryDto PrescriptionSummary);

public sealed record ConsultationVitalSignsDto(
    int? BpSystolicMmhg,
    int? BpDiastolicMmhg,
    string? BloodPressureLabel,
    int? PulseBpm,
    decimal? TemperatureC,
    int? Spo2Percent,
    decimal? HeightCm,
    decimal? WeightKg,
    decimal? Bmi);

public sealed record ConsultationDiagnosisItemDto(
    long Id,
    long? Icd10CodeId,
    string Code,
    string Name,
    bool IsPrimary,
    string? ClinicalDiagnosis,
    string Status,
    DateTime? ConfirmedAt,
    DateTime? RecordedAt);

public sealed record ConsultationDiagnosticSummaryDto(
    int TotalQueueEntries,
    int OpenQueueEntries,
    int CompletedQueueEntries,
    bool AllMandatoryResultsReady,
    bool FinalResultRequired);

public sealed record ConsultationPrescriptionSummaryDto(
    int TotalPrescriptions,
    string? LatestPrescriptionCode,
    string? LatestPrescriptionStatus,
    DateTime? LatestIssuedAt,
    int ItemCount);

public sealed record ConsultationWorkspaceActionDto(
    string Id,
    string Label,
    string UcCode,
    bool Enabled,
    string? DisabledReason);
