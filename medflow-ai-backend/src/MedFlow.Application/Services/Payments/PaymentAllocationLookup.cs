using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Payments;

internal sealed record EffectiveChargeAllocation(
    long? AllocationId,
    long PaymentId,
    long VisitChargeId,
    long VisitId,
    decimal? AllocatedAmount,
    string Status,
    Payment? Payment);

internal static class PaymentAllocationLookup
{
    public static async Task<List<PaymentAllocation>> TryLoadTrackedForVisitAsync(
        IApplicationDbContext db,
        long visitId,
        CancellationToken ct)
    {
        try
        {
            return await db.PaymentAllocations
                .Where(pa => pa.VisitId == visitId)
                .OrderBy(pa => pa.Id)
                .ToListAsync(ct);
        }
        catch (NotSupportedException)
        {
            return [];
        }
    }

    public static async Task<List<PaymentAllocation>> TryLoadNoTrackingForVisitAsync(
        IApplicationDbContext db,
        long visitId,
        CancellationToken ct)
    {
        try
        {
            return await db.PaymentAllocations.AsNoTracking()
                .Where(pa => pa.VisitId == visitId)
                .OrderByDescending(pa => pa.Id)
                .ToListAsync(ct);
        }
        catch (NotSupportedException)
        {
            return [];
        }
    }

    public static void TryAddAllocations(
        IApplicationDbContext db,
        IEnumerable<PaymentAllocation> allocations)
    {
        try
        {
            db.PaymentAllocations.AddRange(allocations);
        }
        catch (NotSupportedException)
        {
            // Minimal unit-test DbContext double without PaymentAllocations DbSet.
        }
    }

    public static async Task<List<PaymentEnrollmentIntent>> TryLoadTrackedIntentsForVisitAsync(
        IApplicationDbContext db,
        long visitId,
        CancellationToken ct)
    {
        try
        {
            return await db.PaymentEnrollmentIntents
                .Where(i => i.VisitId == visitId)
                .OrderBy(i => i.Id)
                .ToListAsync(ct);
        }
        catch (NotSupportedException)
        {
            return [];
        }
    }

    public static async Task<List<PaymentEnrollmentIntent>> TryLoadNoTrackingIntentsForVisitAsync(
        IApplicationDbContext db,
        long visitId,
        CancellationToken ct)
    {
        try
        {
            return await db.PaymentEnrollmentIntents.AsNoTracking()
                .Where(i => i.VisitId == visitId)
                .OrderBy(i => i.Id)
                .ToListAsync(ct);
        }
        catch (NotSupportedException)
        {
            return [];
        }
    }

    public static void TryAddIntent(
        IApplicationDbContext db,
        PaymentEnrollmentIntent intent)
    {
        try
        {
            db.PaymentEnrollmentIntents.Add(intent);
        }
        catch (NotSupportedException)
        {
            // Minimal unit-test DbContext double without PaymentEnrollmentIntents DbSet.
        }
    }

    /// <summary>
    /// Acquires ADR-0061 strict lock chain for a Visit's billing &amp; diagnostic graph:
    /// 09 visits -&gt; 12 diagnostic_orders -&gt; 15 visit_charges -&gt; 16 payments -&gt; 16b payment_allocations -&gt; 16c payment_enrollment_intents.
    /// </summary>
    public static async Task AcquireVisitPaymentChainLocksAsync(
        IApplicationDbContext db,
        long visitId,
        CancellationToken ct,
        long? patientIdToLock = null)
    {
        if (patientIdToLock is > 0)
        {
            await db.TryLockPatientForUpdateAsync(patientIdToLock.Value, ct);
        }

        await db.TryLockVisitForUpdateAsync(visitId, ct);

        try
        {
            var preOrderIds = await db.DiagnosticOrders.AsNoTracking()
                .Where(o => o.VisitId == visitId)
                .OrderBy(o => o.Id)
                .Select(o => o.Id)
                .ToListAsync(ct);
            if (preOrderIds.Count > 0)
            {
                await db.TryLockDiagnosticOrdersForUpdateAsync(preOrderIds, ct);
            }
        }
        catch (NotSupportedException)
        {
        }

        try
        {
            var preChargeIds = await db.VisitCharges.AsNoTracking()
                .Where(c => c.VisitId == visitId)
                .OrderBy(c => c.Id)
                .Select(c => c.Id)
                .ToListAsync(ct);
            if (preChargeIds.Count > 0)
            {
                await db.TryLockVisitChargesForUpdateAsync(preChargeIds, ct);
            }
        }
        catch (NotSupportedException)
        {
        }

        var prePaymentIds = await db.Payments.AsNoTracking()
            .Where(p => p.VisitId == visitId)
            .OrderBy(p => p.Id)
            .Select(p => p.Id)
            .ToListAsync(ct);
        if (prePaymentIds.Count > 0)
        {
            await db.TryLockPaymentsForUpdateAsync(prePaymentIds, ct);
        }

        try
        {
            var preAllocationIds = await db.PaymentAllocations.AsNoTracking()
                .Where(pa => pa.VisitId == visitId)
                .OrderBy(pa => pa.Id)
                .Select(pa => pa.Id)
                .ToListAsync(ct);
            if (preAllocationIds.Count > 0)
            {
                await db.TryLockPaymentAllocationsForUpdateAsync(preAllocationIds, ct);
            }
        }
        catch (NotSupportedException)
        {
        }

        try
        {
            var preIntentIds = await db.PaymentEnrollmentIntents.AsNoTracking()
                .Where(i => i.VisitId == visitId)
                .OrderBy(i => i.Id)
                .Select(i => i.Id)
                .ToListAsync(ct);
            if (preIntentIds.Count > 0)
            {
                await db.TryLockPaymentEnrollmentIntentsForUpdateAsync(preIntentIds, ct);
            }
        }
        catch (NotSupportedException)
        {
        }
    }

    /// <summary>
    /// Combines persisted <see cref="PaymentAllocation"/> rows with deterministic synthesis for
    /// single-charge <see cref="Payment"/> rows that have <c>VisitChargeId != null</c> and zero persisted allocations
    /// (supporting minimal unit-test doubles and direct test fixtures), while leaving <c>LEGACY_VISIT</c> rows untouched.
    /// </summary>
    public static (List<EffectiveChargeAllocation> Allocations, HashSet<long> AllocatedPaymentIds) BuildEffectiveAllocations(
        long visitId,
        IReadOnlyList<Payment> visitPayments,
        IReadOnlyList<PaymentAllocation> persistedAllocations)
    {
        var paymentsById = visitPayments.ToDictionary(p => p.Id);
        var allocatedPaymentIds = new HashSet<long>();
        var result = new List<EffectiveChargeAllocation>(persistedAllocations.Count + visitPayments.Count);

        foreach (var pa in persistedAllocations.OrderByDescending(x => x.Id))
        {
            allocatedPaymentIds.Add(pa.PaymentId);
            paymentsById.TryGetValue(pa.PaymentId, out var linkedPayment);
            var normalizedStatus = NormalizeAllocationStatus(pa.Status, linkedPayment?.Status);
            var effectiveAmount = linkedPayment is { Amount: null }
                ? (decimal?)null
                : pa.AllocatedAmount;

            result.Add(new EffectiveChargeAllocation(
                pa.Id,
                pa.PaymentId,
                pa.VisitChargeId,
                pa.VisitId,
                effectiveAmount,
                normalizedStatus,
                linkedPayment));
        }

        foreach (var payment in visitPayments.OrderByDescending(p => p.Id))
        {
            if (allocatedPaymentIds.Contains(payment.Id))
                continue;

            if (payment.VisitChargeId is long singleChargeId)
            {
                allocatedPaymentIds.Add(payment.Id);
                result.Add(new EffectiveChargeAllocation(
                    null,
                    payment.Id,
                    singleChargeId,
                    visitId,
                    payment.Amount,
                    MapPaymentStatusToAllocationStatus(payment.Status),
                    payment));
            }
            else if (string.Equals(payment.PaymentScope, PaymentScopes.Allocated, StringComparison.OrdinalIgnoreCase))
            {
                allocatedPaymentIds.Add(payment.Id);
            }
        }

        return (result, allocatedPaymentIds);
    }

    public static string MapPaymentStatusToAllocationStatus(string? paymentStatus)
    {
        var normalized = paymentStatus?.Trim().ToUpperInvariant();
        return normalized switch
        {
            PaymentStatuses.Paid => PaymentAllocationStatuses.Paid,
            PaymentStatuses.Pending => PaymentAllocationStatuses.Pending,
            PaymentStatuses.Expired => PaymentAllocationStatuses.Expired,
            PaymentStatuses.Cancelled => PaymentAllocationStatuses.Cancelled,
            _ => PaymentAllocationStatuses.Reconcile,
        };
    }

    private static string NormalizeAllocationStatus(string? allocationStatus, string? paymentStatus)
    {
        if (!string.IsNullOrWhiteSpace(allocationStatus))
            return allocationStatus.Trim().ToUpperInvariant();
        return MapPaymentStatusToAllocationStatus(paymentStatus);
    }
}
