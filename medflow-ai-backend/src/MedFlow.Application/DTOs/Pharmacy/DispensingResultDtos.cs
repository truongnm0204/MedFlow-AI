namespace MedFlow.Application.DTOs.Pharmacy;

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
    string LineTotalFormatted,
    decimal? UnitPrice = null,
    decimal? LineTotal = null);

public sealed record DispensingResultDetailDto(
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
    string AuditNote,
    long DispenseId,
    long PrescriptionId,
    decimal? TotalAmount);
