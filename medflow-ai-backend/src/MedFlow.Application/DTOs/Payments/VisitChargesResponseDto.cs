namespace MedFlow.Application.DTOs.Payments;

public sealed record VisitChargesResponseDto(
    long VisitId,
    string? VisitCode,
    string? VisitStatus,
    long? PatientId,
    IReadOnlyList<VisitChargeLineDto> Charges,
    decimal PayableTotal,
    bool HasUnpricedPayableCharges,
    decimal? AmountDue,
    string Currency,
    decimal RecognizedPaidTotal,
    string SettlementStatus,
    long? PaymentId,
    string? PaymentCode,
    string? PaymentStatus,
    decimal? PaymentAmount,
    string? PaymentMethod,
    DateTime? PaidAt,
    string? ExternalReference,
    bool CanPayByQr);
