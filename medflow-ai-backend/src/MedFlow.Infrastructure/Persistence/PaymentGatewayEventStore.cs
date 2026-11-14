using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Payments;
using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Infrastructure.Persistence;

internal sealed class PaymentGatewayEventStore : IPaymentGatewayEventStore
{
    private readonly MedFlowDbContext _db;

    public PaymentGatewayEventStore(MedFlowDbContext db)
    {
        _db = db;
    }

    public Task<bool> ExistsAsync(string provider, string externalId, CancellationToken ct) =>
        _db.PaymentGatewayEvents.AsNoTracking()
            .AnyAsync(e => e.Provider == provider && e.ExternalId == externalId, ct);

    public Task<decimal> SumReceivedAmountAsync(string paymentCode, CancellationToken ct) =>
        _db.PaymentGatewayEvents.AsNoTracking()
            .Where(e => e.PaymentCode == paymentCode
                && PaymentGatewayOutcomes.Counted.Contains(e.Outcome))
            .SumAsync(e => e.TransferAmount ?? 0m, ct);

    public void Add(PaymentGatewayEvent gatewayEvent) => _db.PaymentGatewayEvents.Add(gatewayEvent);
}
