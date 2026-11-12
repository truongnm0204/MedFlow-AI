namespace MedFlow.Application.DTOs.Consultation;

public sealed record CreatePrescriptionRequest(
    IReadOnlyList<CreatePrescriptionItemRequest>? Items = null,
    string? FulfillmentChoice = null,
    int? ExpectedStateVersion = null,
    int? ExpectedRevisionNo = null);

public sealed record CreatePrescriptionItemRequest(
    long? MedicineId = null,
    string? MedicineCode = null,
    decimal? PrescribedQuantity = null,
    decimal? SingleDoseQuantity = null,
    string? SingleDoseUnit = null,
    string? DoseScheduleText = null,
    string? Frequency = null,
    int? DurationDays = null,
    string? AdministrationRoute = null,
    string? Instructions = null);
