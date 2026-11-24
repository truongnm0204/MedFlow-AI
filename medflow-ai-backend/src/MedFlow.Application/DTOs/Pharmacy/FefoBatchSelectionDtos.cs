namespace MedFlow.Application.DTOs.Pharmacy;

public sealed record FefoBatchCandidateDto(
    long BatchId,
    string BatchCode,
    string ExpiryDate,
    string? ExpiryDateIso,
    int DaysUntilExpiry,
    decimal AvailableStock,
    string ShelfLocation,
    string Status,
    bool IsFefoRecommended,
    bool IsSelectable,
    string? BlockReason = null);

public sealed record FefoPrescriptionItemRequirementDto(
    long PrescriptionItemId,
    int OrderIndex,
    long MedicineId,
    string? MedicineCode,
    string DrugName,
    string? ActiveIngredient,
    string? Strength,
    string DosageForm,
    decimal RequiredQuantity,
    string Unit,
    long? SelectedBatchId,
    string? SelectedBatchCode,
    string? OverrideReason,
    IReadOnlyList<FefoBatchCandidateDto> Batches);

public sealed record FefoBatchSelectionContextDto(
    long PrescriptionId,
    string PrescriptionCode,
    long VisitId,
    string VisitCode,
    string PatientCode,
    string PatientName,
    string Status,
    long? DispenseId,
    DateOnly TodayHospitalDate,
    IReadOnlyList<FefoPrescriptionItemRequirementDto> Items);

public sealed record SelectFefoBatchItemRequest(
    long PrescriptionItemId,
    long? MedicineBatchId = null,
    string? BatchCode = null,
    decimal? PickedQuantity = null,
    decimal? Quantity = null,
    string? OverrideReason = null,
    string? Reason = null);

public sealed record SelectFefoBatchesRequest(
    IReadOnlyList<SelectFefoBatchItemRequest> Items,
    string? OverrideReason = null,
    string? Notes = null);

public sealed record SelectedFefoBatchItemResultDto(
    long PrescriptionItemId,
    long MedicineId,
    string DrugName,
    long MedicineBatchId,
    string BatchCode,
    string ExpiryDate,
    decimal AllocatedQuantity,
    bool IsFefoRecommended,
    bool IsFefoOverride,
    string? OverrideReason);

public sealed record SelectFefoBatchesResponseDto(
    long PrescriptionId,
    string PrescriptionCode,
    long DispenseId,
    string Status,
    DateTime SelectedAt,
    string SelectedByStaffName,
    int ItemCount,
    IReadOnlyList<SelectedFefoBatchItemResultDto> Allocations,
    string Message);
