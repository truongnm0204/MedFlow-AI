namespace MedFlow.Application.DTOs.Pharmacy;

public sealed record MedicineBatchOptionDto(
    long BatchId,
    string BatchCode,
    string ExpiryDate,
    decimal AvailableStock,
    string ShelfLocation,
    bool IsFefoRecommended = false);

public sealed record PrepareMedicineItemDto(
    long Id,
    int OrderIndex,
    long MedicineId,
    string? MedicineCode,
    string DrugName,
    string? ActiveIngredient,
    string? Strength,
    string? DosageForm,
    string? UsageInstruction,
    decimal PrescribedQty,
    decimal PickedQty,
    string? Unit,
    long? SelectedBatchId,
    string? SelectedBatchCode,
    bool IsPrepared,
    IReadOnlyList<MedicineBatchOptionDto> AvailableBatches);

public sealed record GppCheckItemDto(
    string Id,
    string Title,
    string Description,
    bool Checked);

public sealed record PreparePrescriptionContextDto(
    long PrescriptionId,
    string RxCode,
    long VisitId,
    string VisitCode,
    string PatientCode,
    string PatientName,
    string? Gender,
    string? AgeLabel,
    string? DoctorName,
    string? RoomLabel,
    string? PrimaryDiagnosis,
    string? SecondaryDiagnosis,
    string? CriticalAllergy,
    string? PaymentBadge,
    string Status,
    long? DispenseId,
    DateTime? IssuedAt,
    IReadOnlyList<PrepareMedicineItemDto> Items,
    IReadOnlyList<GppCheckItemDto> GppChecks);

public sealed record PrepareMedicineItemRequest(
    long PrescriptionItemId,
    long? MedicineBatchId,
    string? BatchCode,
    decimal PickedQuantity,
    string? ShortageReason = null);

public sealed record PreparePrescriptionRequest(
    IReadOnlyList<PrepareMedicineItemRequest> Items,
    string? Notes = null);

public sealed record PreparePrescriptionResponseDto(
    long PrescriptionId,
    string PrescriptionCode,
    long DispenseId,
    string Status,
    DateTime PreparedAt,
    string PreparedByStaffName,
    int PreparedItemCount,
    string Message);
