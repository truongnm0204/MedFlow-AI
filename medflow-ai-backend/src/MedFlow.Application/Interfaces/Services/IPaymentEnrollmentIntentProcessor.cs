using MedFlow.Application.DTOs.Payments;

namespace MedFlow.Application.Interfaces.Services;

public interface IPaymentEnrollmentIntentProcessor
{
    Task<PaymentEnrollmentResultDto?> ProcessPostCommitAsync(
        long paymentId,
        string? ipAddress = null,
        CancellationToken ct = default);

    Task<int> RecoverPendingIntentsAsync(
        int batchSize = 50,
        CancellationToken ct = default);
}
