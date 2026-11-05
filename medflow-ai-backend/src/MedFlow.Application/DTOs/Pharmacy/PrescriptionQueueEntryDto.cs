namespace MedFlow.Application.DTOs.Pharmacy;

public sealed record PrescriptionQueueEntryDto(
    string Id,
    string PrescriptionCode,
    string TicketNo,
    string PatientName,
    string PatientCode,
    int PatientAge,
    string PrescribingDoctor,
    string DepartmentRoom,
    string PrescribedAt,
    int ItemCount,
    string FefoSummary,
    string PaymentStatus,
    string Status,
    string? StockTransactionCode,
    string AllergyNote,
    IReadOnlyList<PrescriptionLineItemDto> Items
);
