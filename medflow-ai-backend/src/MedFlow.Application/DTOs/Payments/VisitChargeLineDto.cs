namespace MedFlow.Application.DTOs.Payments;

public sealed record VisitChargeLineDto(
    long? ChargeId,
    string ChargeSource,
    long? ServiceId,
    string? ServiceCode,
    string? ServiceName,
    long? DiagnosticOrderId,
    decimal? UnitPrice,
    string? PaymentRequirement,
    bool? Payable,
    string ChargeDataStatus,
    string ChargeType = "EXAMINATION",
    string ChargeStatus = "ACTIVE",
    string LineSettlementStatus = "UNPAID",
    long? PaymentId = null,
    string? PaymentCode = null,
    string? PaymentStatus = null,
    bool CanPayByQr = false);
