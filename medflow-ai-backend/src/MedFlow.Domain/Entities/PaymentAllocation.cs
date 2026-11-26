using MedFlow.Domain.Constants;

namespace MedFlow.Domain.Entities;

public class PaymentAllocation
{
    public long Id { get; set; }
    public long PaymentId { get; set; }
    public long VisitChargeId { get; set; }
    public long VisitId { get; set; }
    public decimal AllocatedAmount { get; set; }
    public string Status { get; set; } = PaymentAllocationStatuses.Pending;
    public DateTime CreatedAt { get; set; }

    public static PaymentAllocation Create(
        long paymentId,
        long visitChargeId,
        long visitId,
        decimal allocatedAmount,
        string status,
        DateTime createdAtLocal)
    {
        if (paymentId <= 0)
            throw new ArgumentOutOfRangeException(nameof(paymentId));
        if (visitChargeId <= 0)
            throw new ArgumentOutOfRangeException(nameof(visitChargeId));
        if (visitId <= 0)
            throw new ArgumentOutOfRangeException(nameof(visitId));
        if (allocatedAmount < 0m)
            throw new ArgumentOutOfRangeException(nameof(allocatedAmount));
        ArgumentException.ThrowIfNullOrWhiteSpace(status);

        return new PaymentAllocation
        {
            PaymentId = paymentId,
            VisitChargeId = visitChargeId,
            VisitId = visitId,
            AllocatedAmount = allocatedAmount,
            Status = status.Trim().ToUpperInvariant(),
            CreatedAt = createdAtLocal,
        };
    }
}
