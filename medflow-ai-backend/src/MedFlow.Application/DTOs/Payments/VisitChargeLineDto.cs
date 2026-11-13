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
    string ChargeDataStatus);
