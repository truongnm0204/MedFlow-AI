namespace MedFlow.Application.DTOs.Pharmacy;

public sealed record ReceiverVerificationInfoDto(
    string FullName,
    string PatientCode,
    string IdentityCard,
    string Phone,
    string ReceiverName,
    string Relationship,
    bool IsIdentityVerified
);

public sealed record DispenseMedicationCounselingItemDto(
    string Id,
    int OrderIndex,
    long PrescriptionItemId,
    long MedicineId,
    string DrugName,
    string QtyLabel,
    decimal PrescribedQuantity,
    decimal DispensedQuantity,
    string Unit,
    string? BatchCode,
    long? BatchId,
    string? ExpiryDate,
    string DosageInstruction,
    string WarningNote,
    string? TagLabel = null,
    string? TagTone = null,
    string? ShelfLocation = null,
    decimal? UnitPrice = null,
    string? UnitPriceFormatted = null,
    decimal? LineTotal = null,
    string? LineTotalFormatted = null
);

public sealed record PharmacistHandoverCheckDto(
    string Id,
    string Label,
    bool Checked
);

public sealed record DispenseConfirmContextDto(
    long PrescriptionId,
    string RxCode,
    string TicketNo,
    string VisitCode,
    string? IssuedAt,
    string? DoctorName,
    string TotalPaidFormatted,
    string PaymentMethodLabel,
    string PaymentStatus,
    string RegimenBadge,
    ReceiverVerificationInfoDto Receiver,
    IReadOnlyList<DispenseMedicationCounselingItemDto> Medications,
    IReadOnlyList<PharmacistHandoverCheckDto> Checks,
    bool CanDispense,
    string? DisabledReason = null,
    string DispenseStatus = "PENDING"
);

// ShortageReason: giữ lại để tương thích hợp đồng API với FE cũ.
// BR-BF03-03: backend bỏ qua hoàn toàn — không được dùng để xuất thiếu và không được lưu vào DB.
public sealed record ConfirmDispenseItemRequest(
    long PrescriptionItemId,
    long? MedicineBatchId = null,
    decimal? DispensedQuantity = null,
    string? ShortageReason = null
);

public sealed record ConfirmDispenseRequest(
    string? PrescriptionIdOrCode = null,
    string? ReceiverName = null,
    string? Relationship = null,
    string? ReceiverIdentityCard = null,
    string? ReceiverPhone = null,
    bool IsIdentityVerified = false,
    bool ChecksConfirmed = false,
    IReadOnlyList<ConfirmDispenseItemRequest>? Items = null,
    string? IdempotencyKey = null,
    string? Notes = null
);
