namespace MedFlow.Application.DTOs.Consultation;

public sealed record RecordDiagnosisRequest(
    IReadOnlyList<RecordDiagnosisItemRequest>? Diagnoses = null,
    long? PrimaryIcd10CodeId = null,
    string? PrimaryIcd10Code = null,
    string? PrimaryClinicalDiagnosis = null,
    IReadOnlyList<long>? SecondaryIcd10CodeIds = null,
    IReadOnlyList<string>? SecondaryIcd10Codes = null,
    string? ClinicalConclusion = null,
    string? ClinicalFindings = null,
    string? DifferentialNotes = null,
    string? LifestyleInstructions = null,
    string? WarningSigns = null,
    string? AmendmentReason = null,
    string? DiagnosisStatus = null,
    bool? IsConfirmed = null,
    int? ExpectedStateVersion = null,
    int? ExpectedRevisionNo = null);

public sealed record RecordDiagnosisItemRequest(
    long? Icd10CodeId = null,
    string? Code = null,
    string? Icd10Code = null,
    string? Name = null,
    bool? IsPrimary = null,
    string? ClinicalDiagnosis = null,
    string? Status = null);

public sealed record Icd10CatalogItemDto(
    long Id,
    string Code,
    string NameVi,
    string? CatalogVersion,
    string? NodeType,
    bool IsSelectable,
    bool NotPrimaryDx);
