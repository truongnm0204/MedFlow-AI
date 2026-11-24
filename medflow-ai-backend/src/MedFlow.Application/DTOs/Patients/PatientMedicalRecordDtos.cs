namespace MedFlow.Application.DTOs.Patients;

public sealed record GetPatientMedicalRecordsRequest(
    long? PatientId = null,
    string? FromDate = null,
    string? ToDate = null,
    long? HospitalId = null,
    long? SpecialtyId = null,
    string? SpecialtyCode = null,
    string? Keyword = null,
    int Page = 1,
    int PageSize = 20);

public sealed record PatientEmrScopeMemberDto(
    long PatientId,
    string? PatientCode,
    string FullName,
    string RelationshipLabel,
    bool IsSelf);

public sealed record PatientEmrFilterOptionDto(
    long Id,
    string? Code,
    string Name);

public sealed record PatientMedicalRecordListItemDto(
    long MedicalRecordId,
    string RecordCode,
    long VisitId,
    string? VisitCode,
    string VisitDate,
    DateTime? CheckedInAt,
    DateTime? CompletedAt,
    DateTime? FinalizedAt,
    long PatientId,
    string? PatientCode,
    string PatientName,
    string RelationshipLabel,
    bool IsSelfProfile,
    long? HospitalId,
    string? HospitalName,
    string? DepartmentName,
    long? SpecialtyId,
    string? SpecialtyCode,
    string? SpecialtyName,
    string? DoctorName,
    string? PrimaryIcd10Code,
    string? PrimaryDiagnosisName,
    string? ClinicalConclusion,
    int DiagnosisCount,
    int DiagnosticOrderCount,
    int PrescriptionCount);

public sealed record PatientMedicalRecordListResponseDto(
    IReadOnlyList<PatientMedicalRecordListItemDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    IReadOnlyList<PatientEmrScopeMemberDto> Members,
    IReadOnlyList<PatientEmrFilterOptionDto> Hospitals,
    IReadOnlyList<PatientEmrFilterOptionDto> Specialties);

public sealed record PatientMedicalRecordVitalSignsDto(
    int? BpSystolicMmhg,
    int? BpDiastolicMmhg,
    string? BloodPressureLabel,
    int? PulseBpm,
    decimal? TemperatureC,
    int? Spo2Percent,
    decimal? HeightCm,
    decimal? WeightKg,
    decimal? Bmi);

public sealed record PatientMedicalRecordDiagnosisDto(
    long DiagnosisId,
    string Icd10Code,
    string Icd10NameVi,
    string? ClinicalDiagnosis,
    bool IsPrimary,
    DateTime? ConfirmedAt);

public sealed record PatientMedicalRecordDiagnosticSummaryDto(
    long OrderId,
    string OrderCode,
    string? ServiceCode,
    string ServiceName,
    string Priority,
    string Status,
    DateTime? OrderedAt,
    bool IsResultPublished,
    DateTime? SignedAt,
    string? ConclusionSummary,
    bool HasReportFile);

public sealed record PatientMedicalRecordPrescriptionItemSummaryDto(
    long ItemId,
    string? MedicineCode,
    string MedicineName,
    string? Strength,
    string? DosageForm,
    decimal PrescribedQuantity,
    string? DispensingUnit,
    string? DoseScheduleText,
    string? Frequency,
    int? DurationDays,
    string? AdministrationRoute,
    string? Instructions);

public sealed record PatientMedicalRecordPrescriptionSummaryDto(
    long PrescriptionId,
    string? PrescriptionCode,
    string Status,
    DateTime? IssuedAt,
    string? PrescribingDoctorName,
    int ItemCount,
    bool CanViewPrescriptionDetail,
    IReadOnlyList<PatientMedicalRecordPrescriptionItemSummaryDto> Items);

public sealed record PatientMedicalRecordDetailDto(
    long MedicalRecordId,
    string RecordCode,
    string Status,
    DateTime? FinalizedAt,
    long VisitId,
    string? VisitCode,
    string VisitType,
    string VisitDate,
    DateTime? CheckedInAt,
    DateTime? CompletedAt,
    string? ReasonForVisit,
    long PatientId,
    string? PatientCode,
    string PatientName,
    string RelationshipLabel,
    bool IsSelfProfile,
    string? Gender,
    string? DateOfBirth,
    string? BloodType,
    long? HospitalId,
    string? HospitalCode,
    string? HospitalName,
    string? DepartmentName,
    long? SpecialtyId,
    string? SpecialtyCode,
    string? SpecialtyName,
    string? RoomCode,
    string? RoomName,
    string? DoctorName,
    string? PresentingComplaint,
    string? SymptomHistory,
    string? RelevantHistoryAllergies,
    string? ClinicalFindings,
    string? ClinicalConclusion,
    DateTime? RecordedAt,
    PatientMedicalRecordVitalSignsDto? VitalSigns,
    IReadOnlyList<PatientMedicalRecordDiagnosisDto> Diagnoses,
    IReadOnlyList<PatientMedicalRecordDiagnosticSummaryDto> DiagnosticOrders,
    IReadOnlyList<PatientMedicalRecordPrescriptionSummaryDto> Prescriptions);
