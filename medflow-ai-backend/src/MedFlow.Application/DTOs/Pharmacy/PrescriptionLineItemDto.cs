namespace MedFlow.Application.DTOs.Pharmacy;

public sealed record PrescriptionLineItemDto(
    string MedicineCode,
    string MedicineName,
    string DosageInstruction,
    string QuantityLabel,
    string BatchCode,
    string ExpiryDate
);
