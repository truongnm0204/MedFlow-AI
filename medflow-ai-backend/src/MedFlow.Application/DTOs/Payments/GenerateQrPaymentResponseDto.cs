namespace MedFlow.Application.DTOs.Payments;

public static class QrPaymentIssueKind
{
    public const string Created = "CREATED";
    public const string Adopted = "ADOPTED";
    public const string Reused = "REUSED";
}

public sealed record PaymentAllocationItemDto(
    long? AllocationId,
    long VisitChargeId,
    string ChargeType,
    long? DiagnosticOrderId,
    string? ServiceName,
    decimal AllocatedAmount,
    string Status);

public sealed record GenerateQrPaymentResponseDto(
    long VisitId,
    string? VisitCode,
    long PaymentId,
    string PaymentCode,
    decimal Amount,
    string Currency,
    string Status,
    string QrPayload,
    string BankBin,
    string AccountNumber,
    string AccountName,
    string TransferContent,
    string IssueKind,
    long? VisitChargeId = null,
    IReadOnlyList<PaymentAllocationItemDto>? Allocations = null,
    DateTime? ExpiresAtLocal = null);
