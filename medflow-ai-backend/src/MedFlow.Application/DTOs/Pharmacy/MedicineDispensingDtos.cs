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
    bool IsIdentityVerified = true,
    bool ChecksConfirmed = true,
    IReadOnlyList<ConfirmDispenseItemRequest>? Items = null,
    string? IdempotencyKey = null,
    string? Notes = null
);

public sealed record DispensedLineItemDto(
    string Id,
    int OrderIndex,
    string DrugName,
    string DosageForm,
    decimal QtyDispensed,
    string Unit,
    string BatchCode,
    string ExpiryDate,
    string ShelfLocation,
    string UnitPriceFormatted,
    string LineTotalFormatted
);

public sealed record DispensingResultDetailDto(
    long DispenseId,
    string DispenseCode,
    string RxCode,
    string VisitCode,
    string TicketNo,
    string PatientName,
    string PatientCode,
    string ReceiverName,
    string ReceiverIdentityMasked,
    string PrescribingDoctor,
    string DispensingPharmacist,
    string DispensedAt,
    string StatusLabel,
    string PaymentSummary,
    string TotalAmountFormatted,
    IReadOnlyList<DispensedLineItemDto> Items,
    string AuditNote
);
