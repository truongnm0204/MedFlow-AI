namespace MedFlow.Application.DTOs.Payments;

public sealed record GenerateQrPaymentRequestDto(
    long? VisitChargeId = null,
    IReadOnlyList<long>? VisitChargeIds = null,
    string? IdempotencyKey = null,
    long? EnrollDiagnosticOrderId = null,
    string? EnrollDiagnosticOrderCode = null);
