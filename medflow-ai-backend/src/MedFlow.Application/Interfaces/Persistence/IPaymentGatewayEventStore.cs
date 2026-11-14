using MedFlow.Domain.Entities;

namespace MedFlow.Application.Interfaces.Persistence;

/// <summary>
/// Truy cập bảng đối soát cổng thanh toán. Tách khỏi <see cref="IApplicationDbContext"/>
/// để không phải mở rộng mọi test double hiện có.
/// </summary>
public interface IPaymentGatewayEventStore
{
    Task<bool> ExistsAsync(string provider, string externalId, CancellationToken ct);

    Task<decimal> SumReceivedAmountAsync(string paymentCode, CancellationToken ct);

    void Add(PaymentGatewayEvent gatewayEvent);
}
