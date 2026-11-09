namespace MedFlow.Application.DTOs.Consultation;

public sealed record GetPatientMedicalHistoryRequest(
    string? VisitIdOrCode = null,
    string? Status = null,
    string? SpecialtyCode = null,
    long? DepartmentId = null,
    string? FromDate = null,
    string? ToDate = null,
    string? Keyword = null,
    bool IncludeCurrentVisit = true,
    int Limit = 20);

public sealed record PatientMedicalHistoryResponseDto(
    string FacilityName,
    string FacilityCode,
    DateTime GeneratedAtUtc,
    MedicalHistoryPatientSummaryDto Patient,
    MedicalHistoryCurrentVisitDto? CurrentVisit,
    MedicalHistoryStatsDto Stats,
    IReadOnlyList<MedicalHistoryVisitEntryDto> Visits);

public sealed record MedicalHistoryPatientSummaryDto(
    long PatientId,
    string PatientCode,
    string FullName,
    string Gender,
    string? DateOfBirth,
    int Age,
    string? BloodType,
    string AllergiesSummary,
    IReadOnlyList<string> ChronicConditions);

public sealed record MedicalHistoryCurrentVisitDto(
    long VisitId,
    string VisitCode,
    string Status,
    string? ReasonForVisit,
    DateTime? CheckedInAt,
    string? RoomCode,
    string? RoomName,
    string? DepartmentName,
    string? DoctorName);

public sealed record MedicalHistoryStatsDto(
    int TotalVisits,
    int CompletedVisits,
    int FinalizedRecordsCount,
    int TotalDiagnosesCount,
    int TotalDiagnosticOrdersCount,
    int TotalPrescriptionsCount,
    DateTime? LastVisitAt);

public sealed record MedicalHistoryVisitEntryDto(
    long VisitId,
    string VisitCode,
    string VisitDate,
    DateTime? CheckedInAt,
    DateTime? CompletedAt,
    string Status,
    string VisitType,
    string? ReasonForVisit,
    long? DepartmentId,
    string DepartmentName,
    string? SpecialtyCode,
    string SpecialtyName,
    string? RoomCode,
    string? RoomName,
    long? DoctorId,
    string? DoctorCode,
    string DoctorName,
    bool IsAssignedToCurrentDoctor,
    bool IsCurrentVisit,
    MedicalHistoryRecordDto? MedicalRecord,
    IReadOnlyList<MedicalHistoryDiagnosisDto> Diagnoses,
    IReadOnlyList<MedicalHistoryDiagnosticOrderDto> DiagnosticOrders,
    IReadOnlyList<MedicalHistoryPrescriptionDto> Prescriptions);

public sealed record MedicalHistoryRecordDto(
    long MedicalRecordId,
    string Status,
    DateTime? FinalizedAt,
    int LatestRevisionNo,
    bool IsConfirmed,
    DateTime? RecordedAt,
    string? EditedByDoctorName,
    string? PresentingComplaint,
    string? SymptomHistory,
    string? RelevantHistoryAllergies,
    string? ClinicalFindings,
    string? ClinicalConclusion,
    string? AmendmentReason,
    MedicalHistoryVitalSignsDto? VitalSigns,
    int RevisionCount,
    IReadOnlyList<MedicalHistoryRevisionSummaryDto> Revisions,
    IReadOnlyList<MedicalHistoryAddendumDto> Addenda);

public sealed record MedicalHistoryVitalSignsDto(
    int? BpSystolicMmhg,
    int? BpDiastolicMmhg,
    string? BloodPressureLabel,
    int? PulseBpm,
    decimal? TemperatureC,
    int? Spo2Percent,
    decimal? HeightCm,
    decimal? WeightKg,
    decimal? Bmi);

public sealed record MedicalHistoryRevisionSummaryDto(
    long RevisionId,
    int RevisionNo,
    DateTime? RecordedAt,
    string? EditedByDoctorName,
    bool IsConfirmed,
    string? AmendmentReason,
    string? ClinicalConclusion);

public sealed record MedicalHistoryAddendumDto(
    long AddendumId,
    string RecordType,
    long? TargetRecordId,
    string? Reason,
    string? Note,
    string? CorrectedContentJson,
    string? AuthorDoctorName,
    DateTime? RecordedAt);

public sealed record MedicalHistoryDiagnosisDto(
    long DiagnosisId,
    long? Icd10CodeId,
    string Icd10Code,
    string Icd10NameVi,
    string? CatalogVersion,
    string? ClinicalDiagnosis,
    bool IsPrimary,
    string Status,
    DateTime? ConfirmedAt,
    DateTime? RecordedAt,
    string? RecordedByDoctorName);

public sealed record MedicalHistoryDiagnosticOrderDto(
    long OrderId,
    string OrderCode,
    long? ServiceId,
    string? ServiceCode,
    string ServiceName,
    string Priority,
    string Status,
    bool RequiredForConclusion,
    string? Instruction,
    DateTime? OrderedAt,
    DateTime? ConfirmedAt,
    string? ResultStatus,
    DateTime? ResultFinalizedAt,
    int? ResultVersionNo,
    string? ResultText,
    string? Conclusion,
    string? ReportFileUrl,
    bool IsResultFinalized);

public sealed record MedicalHistoryPrescriptionDto(
    long PrescriptionId,
    string PrescriptionCode,
    string Status,
    string? FulfillmentChoice,
    DateTime? IssuedAt,
    string? PrescribingDoctorName,
    IReadOnlyList<MedicalHistoryPrescriptionItemDto> Items);

public sealed record MedicalHistoryPrescriptionItemDto(
    long ItemId,
    long? MedicineId,
    string? MedicineCode,
    string MedicineName,
    string? ActiveIngredient,
    string? Strength,
    string? DosageForm,
    string? DispensingUnit,
    decimal? PrescribedQuantity,
    decimal? SingleDoseQuantity,
    string? SingleDoseUnit,
    string? DoseScheduleText,
    string? Frequency,
    int? DurationDays,
    string? AdministrationRoute,
    string? Instructions);
