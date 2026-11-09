namespace MedFlow.Application.DTOs.Consultation;

public sealed record RecordClinicalExaminationRequest(
    string? PresentingComplaint = null,
    string? SymptomHistory = null,
    string? RelevantHistoryAllergies = null,
    RecordClinicalVitalSignsRequest? VitalSigns = null,
    int? BpSystolicMmhg = null,
    int? BpDiastolicMmhg = null,
    int? PulseBpm = null,
    decimal? TemperatureC = null,
    int? Spo2Percent = null,
    decimal? HeightCm = null,
    decimal? WeightKg = null,
    string? ClinicalFindings = null,
    string? ClinicalConclusion = null,
    string? AmendmentReason = null,
    bool? IsConfirmed = null,
    int? ExpectedStateVersion = null,
    int? ExpectedRevisionNo = null);

public sealed record RecordClinicalVitalSignsRequest(
    int? BpSystolicMmhg = null,
    int? BpDiastolicMmhg = null,
    int? PulseBpm = null,
    decimal? TemperatureC = null,
    int? Spo2Percent = null,
    decimal? HeightCm = null,
    decimal? WeightKg = null);
