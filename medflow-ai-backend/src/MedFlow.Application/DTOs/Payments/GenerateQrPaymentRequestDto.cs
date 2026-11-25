namespace MedFlow.Application.DTOs.Payments;

public sealed record GenerateQrPaymentRequestDto(long? VisitChargeId = null);
