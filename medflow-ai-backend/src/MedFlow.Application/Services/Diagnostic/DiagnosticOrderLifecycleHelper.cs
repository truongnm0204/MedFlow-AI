using System.Globalization;
using System.Text.Json;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Appointments;
using MedFlow.Application.Services.Visits;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Diagnostic;

internal static class DiagnosticOrderLifecycleHelper
{
    private static readonly JsonSerializerOptions AuditJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string? ValidateInternalReportFileUrl(string? rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            return null;
        }

        var trimmed = rawUrl.Trim();
        if (trimmed.Length > 500)
        {
            throw new ValidationException("Đường dẫn tệp báo cáo kết quả tối đa 500 ký tự.");
        }

        if (trimmed.Contains('<') ||
            trimmed.Contains('>') ||
            trimmed.Any(char.IsWhiteSpace) ||
            trimmed.Any(char.IsControl))
        {
            throw new ValidationException("Đường dẫn tệp báo cáo kết quả chứa ký tự không hợp lệ.");
        }

        if (trimmed.Contains("..", StringComparison.Ordinal) ||
            trimmed.Contains("//", StringComparison.Ordinal) ||
            trimmed.Contains('\\'))
        {
            throw new ValidationException(
                "Đường dẫn tệp báo cáo kết quả không hợp lệ (không cho phép path traversal hoặc ký tự phân tách ngoại lệ).");
        }

        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("://", StringComparison.Ordinal))
        {
            throw new ValidationException(
                "Không cho phép sử dụng đường dẫn URL bên ngoài cho tệp báo cáo cận lâm sàng. Chỉ chấp nhận đường dẫn lưu trữ nội bộ của hệ thống.");
        }

        var isAllowedInternalPath =
            trimmed.StartsWith("/api/v1/diagnostic/results/", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("/storage/diagnostic-reports/", StringComparison.OrdinalIgnoreCase);

        if (!isAllowedInternalPath)
        {
            throw new ValidationException(
                "Đường dẫn tệp báo cáo kết quả phải nằm trong phân vùng lưu trữ nội bộ được bảo vệ (/storage/diagnostic-reports/... hoặc /api/v1/diagnostic/results/...).");
        }

        return trimmed;
    }

    public static bool IsResultRowFinalized(
        DiagnosticResult? result,
        DiagnosticResultVersion? latestVersion)
    {
        if (result is null)
        {
            return false;
        }

        var rawStatus = AppointmentBookingRules.NormalizeStatusKey(result.Status);
        if (rawStatus == "CANCELLED")
        {
            return false;
        }

        return result.FinalizedAt.HasValue ||
               rawStatus is "FINALIZED" or "VERIFIED" ||
               latestVersion?.IsFinalized == true ||
               latestVersion?.VerifiedAt.HasValue == true;
    }

    /// <summary>
    /// Acquires ADR-0059 locks in strict ascending order:
    /// visits (09) -> queue_service_points (10) -> queue_entries (11) -> diagnostic_orders (12) -> visit_charges (15) -> payments (16).
    /// Must be called inside an active transaction BEFORE loading tracked entities to mutate.
    /// </summary>
    public static Task AcquireVisitAndDiagnosticLocksAsync(
        IApplicationDbContext db,
        long? visitId,
        IReadOnlyCollection<long>? extraServicePointIds,
        IReadOnlyCollection<long>? extraQueueEntryIds,
        IReadOnlyCollection<long>? extraDiagnosticOrderIds,
        bool includeChargeAndPaymentLocks,
        CancellationToken ct)
        => AcquireVisitAndDiagnosticLocksAsync(
            db,
            visitId is > 0 ? new[] { visitId.Value } : Array.Empty<long>(),
            extraServicePointIds,
            extraQueueEntryIds,
            extraDiagnosticOrderIds,
            includeChargeAndPaymentLocks,
            ct);

    public static async Task AcquireVisitAndDiagnosticLocksAsync(
        IApplicationDbContext db,
        IEnumerable<long>? visitIds,
        IEnumerable<long>? extraServicePointIds,
        IEnumerable<long>? extraQueueEntryIds,
        IEnumerable<long>? extraDiagnosticOrderIds,
        bool includeChargeAndPaymentLocks,
        CancellationToken ct)
    {
        var anyRelationalLockAcquired = false;

        var sortedVisitIds = new SortedSet<long>();
        if (visitIds is not null)
        {
            foreach (var vid in visitIds)
            {
                if (vid > 0)
                {
                    sortedVisitIds.Add(vid);
                }
            }
        }

        foreach (var vid in sortedVisitIds)
        {
            if (await db.TryLockVisitForUpdateAsync(vid, ct))
            {
                anyRelationalLockAcquired = true;
            }
        }

        var spIds = new SortedSet<long>();
        if (extraServicePointIds is not null)
        {
            foreach (var spId in extraServicePointIds)
            {
                if (spId > 0)
                {
                    spIds.Add(spId);
                }
            }
        }

        var qeIds = new SortedSet<long>();
        if (extraQueueEntryIds is not null)
        {
            foreach (var qeId in extraQueueEntryIds)
            {
                if (qeId > 0)
                {
                    qeIds.Add(qeId);
                }
            }
        }

        var orderIds = new SortedSet<long>();
        if (extraDiagnosticOrderIds is not null)
        {
            foreach (var orderId in extraDiagnosticOrderIds)
            {
                if (orderId > 0)
                {
                    orderIds.Add(orderId);
                }
            }
        }

        foreach (var vid in sortedVisitIds)
        {
            try
            {
                var visitRow = await db.Visits.AsNoTracking()
                    .FirstOrDefaultAsync(v => v.Id == vid, ct);

                var visitQueues = await db.QueueEntries.AsNoTracking()
                    .Where(q =>
                        q.VisitId == vid ||
                        (visitRow != null && visitRow.AppointmentId.HasValue && q.AppointmentId == visitRow.AppointmentId.Value))
                    .ToListAsync(ct);

                foreach (var q in visitQueues)
                {
                    qeIds.Add(q.Id);
                    if (q.ServicePointId is > 0)
                    {
                        spIds.Add(q.ServicePointId.Value);
                    }

                    if (q.CurrentServicePointId is > 0)
                    {
                        spIds.Add(q.CurrentServicePointId.Value);
                    }
                }

                if (visitRow?.ExaminationRoomId is long examRoomId)
                {
                    try
                    {
                        var roomSpIds = await db.QueueServicePoints.AsNoTracking()
                            .Where(sp => sp.RoomId == examRoomId)
                            .Select(sp => sp.Id)
                            .ToListAsync(ct);
                        foreach (var spId in roomSpIds)
                        {
                            spIds.Add(spId);
                        }
                    }
                    catch (NotSupportedException)
                    {
                        // Minimal unit test DbContext without QueueServicePoints.
                    }
                }
            }
            catch (NotSupportedException)
            {
                // Minimal unit test DbContext.
            }

            try
            {
                var visitOrderIds = await db.DiagnosticOrders.AsNoTracking()
                    .Where(o => o.VisitId == vid)
                    .Select(o => o.Id)
                    .ToListAsync(ct);
                foreach (var oid in visitOrderIds)
                {
                    orderIds.Add(oid);
                }
            }
            catch (NotSupportedException)
            {
                // Minimal unit test DbContext without DiagnosticOrders.
            }
        }

        if (spIds.Count > 0)
        {
            if (await db.TryLockServicePointsForUpdateAsync(spIds.ToArray(), ct))
            {
                anyRelationalLockAcquired = true;
            }
        }

        if (qeIds.Count > 0)
        {
            if (await db.TryLockQueueEntriesForUpdateAsync(qeIds.ToArray(), ct))
            {
                anyRelationalLockAcquired = true;
            }
        }

        if (orderIds.Count > 0)
        {
            if (await db.TryLockDiagnosticOrdersForUpdateAsync(orderIds.ToArray(), ct))
            {
                anyRelationalLockAcquired = true;
            }
        }

        if (includeChargeAndPaymentLocks && sortedVisitIds.Count > 0)
        {
            var chargeIds = new SortedSet<long>();
            try
            {
                var ids = await db.VisitCharges.AsNoTracking()
                    .Where(c => c.VisitId.HasValue && sortedVisitIds.Contains(c.VisitId.Value))
                    .Select(c => c.Id)
                    .ToListAsync(ct);
                foreach (var cid in ids)
                {
                    chargeIds.Add(cid);
                }
            }
            catch (NotSupportedException)
            {
                // Minimal unit test DbContext without VisitCharges.
            }

            if (chargeIds.Count > 0)
            {
                if (await db.TryLockVisitChargesForUpdateAsync(chargeIds.ToArray(), ct))
                {
                    anyRelationalLockAcquired = true;
                }
            }

            var paymentIds = new SortedSet<long>();
            try
            {
                var pIds = await db.Payments.AsNoTracking()
                    .Where(p => p.VisitId.HasValue && sortedVisitIds.Contains(p.VisitId.Value))
                    .Select(p => p.Id)
                    .ToListAsync(ct);
                foreach (var pid in pIds)
                {
                    paymentIds.Add(pid);
                }
            }
            catch (NotSupportedException)
            {
                // Minimal unit test DbContext without Payments.
            }

            if (paymentIds.Count > 0)
            {
                if (await db.TryLockPaymentsForUpdateAsync(paymentIds.ToArray(), ct))
                {
                    anyRelationalLockAcquired = true;
                }
            }
        }

        if (anyRelationalLockAcquired && db is DbContext efContext)
        {
            efContext.ChangeTracker.Clear();
        }
    }

    public static async Task<DiagnosticOrderCancellationOutcome> CancelOrderStateAwareAsync(
        IApplicationDbContext db,
        long actorUserId,
        long hospitalId,
        long diagnosticOrderId,
        string? reason,
        bool technicalAbort,
        string? cancelDisposition,
        DateTime nowLocal,
        DateTime nowUtc,
        System.Net.IPAddress? ipAddress,
        CancellationToken ct)
    {
        var preOrder = await db.DiagnosticOrders.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == diagnosticOrderId, ct)
            ?? throw new NotFoundException("DiagnosticOrder", diagnosticOrderId);

        await AcquireVisitAndDiagnosticLocksAsync(
            db,
            visitId: preOrder.VisitId,
            extraServicePointIds: null,
            extraQueueEntryIds: preOrder.QueueEntryId.HasValue ? [preOrder.QueueEntryId.Value] : null,
            extraDiagnosticOrderIds: [diagnosticOrderId],
            includeChargeAndPaymentLocks: true,
            ct: ct);

        var order = await db.DiagnosticOrders
            .FirstOrDefaultAsync(o => o.Id == diagnosticOrderId, ct)
            ?? throw new NotFoundException("DiagnosticOrder", diagnosticOrderId);

        var visit = order.VisitId.HasValue
            ? await db.Visits.FirstOrDefaultAsync(v => v.Id == order.VisitId.Value, ct)
            : null;

        var diagQueue = order.QueueEntryId.HasValue
            ? await db.QueueEntries.FirstOrDefaultAsync(q => q.Id == order.QueueEntryId.Value, ct)
            : null;

        var core = await ApplyCancellationOnLockedEntitiesAsync(
            db,
            order,
            visit,
            diagQueue,
            hospitalId,
            actorUserId,
            reason,
            technicalAbort,
            cancelDisposition,
            ipAddress,
            nowUtc,
            nowLocal,
            ct);

        return new DiagnosticOrderCancellationOutcome(
            Order: order,
            Visit: visit,
            DiagnosticQueueEntry: diagQueue,
            DiagnosticCharge: core.DiagnosticCharge,
            WasAlreadyPaid: core.WasAlreadyPaid,
            ExpiredPendingPaymentCount: core.ExpiredPendingPaymentsCount,
            TransitionedToReturnConsultation: core.TransitionedToReturnConsultation,
            ReturnConsultationQueueEntry: core.ReturnQueueEntry);
    }

    public static Task<DiagnosticOrderCancelOutcome> CancelOrderStateAwareAsync(
        IApplicationDbContext db,
        DiagnosticOrder order,
        Visit? visit,
        QueueEntry? diagQueue,
        long hospitalId,
        long actorUserId,
        string? reason,
        bool technicalAbort,
        string? explicitDisposition,
        string? ipAddress,
        DateTime nowUtc,
        DateTime nowLocal,
        DateOnly referenceDate,
        CancellationToken ct)
        => ApplyCancellationOnLockedEntitiesAsync(
            db,
            order,
            visit,
            diagQueue,
            hospitalId,
            actorUserId,
            reason,
            technicalAbort,
            explicitDisposition,
            QueueEntryRequestParsing.ParseIpAddress(ipAddress),
            nowUtc,
            nowLocal,
            ct);

    private static async Task<DiagnosticOrderCancelOutcome> ApplyCancellationOnLockedEntitiesAsync(
        IApplicationDbContext db,
        DiagnosticOrder order,
        Visit? visit,
        QueueEntry? diagQueue,
        long hospitalId,
        long actorUserId,
        string? reason,
        bool technicalAbort,
        string? cancelDisposition,
        System.Net.IPAddress? ipAddress,
        DateTime nowUtc,
        DateTime nowLocal,
        CancellationToken ct)
    {
        if (visit is not null)
        {
            var visitStatus = AppointmentBookingRules.NormalizeStatusKey(visit.Status);
            if (visitStatus is "COMPLETED" or "DONE")
            {
                throw new ConflictException(
                    "Lượt khám đã hoàn tất (COMPLETED), không thể hủy phiếu chỉ định cận lâm sàng.");
            }
        }

        var orderStatus = AppointmentBookingRules.NormalizeStatusKey(order.Status);
        var queueStatus = diagQueue is not null
            ? AppointmentBookingRules.NormalizeStatusKey(diagQueue.Status)
            : null;

        if (orderStatus == "CANCELLED")
        {
            throw new ConflictException("Phiếu chỉ định cận lâm sàng đã được hủy trước đó.");
        }

        var hasExistingResult = false;
        try
        {
            hasExistingResult = await db.DiagnosticResults.AsNoTracking()
                .AnyAsync(
                    r => r.DiagnosticOrderId == order.Id &&
                         (r.Status == null || r.Status.ToUpper() != "CANCELLED"),
                    ct);
        }
        catch (NotSupportedException)
        {
            hasExistingResult = false;
        }

        if (hasExistingResult ||
            orderStatus is "COMPLETED" or "DONE" or "FINALIZED" or "VERIFIED" ||
            diagQueue?.CompletedAt.HasValue == true ||
            queueStatus == "COMPLETED")
        {
            throw new ConflictException(
                "Không thể hủy phiếu chỉ định cận lâm sàng đã hoàn tất thực hiện/lấy mẫu (PERFORMED_AWAITING_RESULT) hoặc đã có kết quả.");
        }

        var normalizedDisposition = string.IsNullOrWhiteSpace(cancelDisposition)
            ? null
            : cancelDisposition.Trim().ToUpperInvariant();
        var isTechnicalAbort = technicalAbort ||
            string.Equals(normalizedDisposition, QueueCancelDispositions.TechnicalError, StringComparison.OrdinalIgnoreCase);

        if (orderStatus == "IN_PROGRESS" || queueStatus == "IN_PROGRESS")
        {
            if (!isTechnicalAbort || string.IsNullOrWhiteSpace(reason))
            {
                throw new ConflictException(
                    "Phiếu chỉ định đang trong quá trình thực hiện (IN_PROGRESS). Chỉ được phép hủy kỹ thuật (technicalAbort / TECHNICAL_ERROR) kèm lý do hủy rõ ràng.");
            }
        }

        var effectiveDisposition = isTechnicalAbort
            ? QueueCancelDispositions.TechnicalError
            : normalizedDisposition;

        var previousOrderStatus = order.Status ?? "ORDERED";
        var previousQueueStatus = diagQueue?.Status;

        order.Status = "CANCELLED";

        if (diagQueue is not null && queueStatus != "CANCELLED")
        {
            var prevVersion = diagQueue.StateVersion is > 0 ? diagQueue.StateVersion.Value : 1;
            diagQueue.Status = "CANCELLED";
            diagQueue.CompletedAt ??= nowLocal;
            diagQueue.BlockedReason = null;
            diagQueue.StateVersion = prevVersion + 1;
        }

        VisitCharge? diagCharge = null;
        try
        {
            diagCharge = await db.VisitCharges
                .FirstOrDefaultAsync(c => c.DiagnosticOrderId == order.Id, ct);
        }
        catch (NotSupportedException)
        {
            diagCharge = null;
        }

        var expiredPendingPaymentCount = 0;
        var wasAlreadyPaid = false;
        string? resultingChargeStatus = null;

        if (diagCharge is not null)
        {
            List<Payment> chargePayments = [];
            try
            {
                chargePayments = await db.Payments
                    .Where(p => p.VisitChargeId == diagCharge.Id)
                    .ToListAsync(ct);
            }
            catch (NotSupportedException)
            {
                chargePayments = [];
            }

            wasAlreadyPaid = chargePayments.Any(p =>
                string.Equals(p.Status?.Trim(), "PAID", StringComparison.OrdinalIgnoreCase));

            foreach (var pendingPayment in chargePayments.Where(p =>
                         string.Equals(p.Status?.Trim(), "PENDING", StringComparison.OrdinalIgnoreCase)))
            {
                pendingPayment.Status = "EXPIRED";
                expiredPendingPaymentCount++;
            }

            if (wasAlreadyPaid)
            {
                diagCharge.Status = VisitChargeStatuses.CancelledRefundPending;
                diagCharge.CancelledAt = nowLocal;
                resultingChargeStatus = VisitChargeStatuses.CancelledRefundPending;
            }
            else
            {
                diagCharge.Status = VisitChargeStatuses.Cancelled;
                diagCharge.CancelledAt = nowLocal;
                resultingChargeStatus = VisitChargeStatuses.Cancelled;
            }
        }

        QueueEntry? returnQueueEntry = null;
        var transitionedToReturnConsultation = false;
        if (visit is not null)
        {
            (transitionedToReturnConsultation, returnQueueEntry) =
                await TryTransitionVisitToReturnConsultationAsync(
                    db,
                    visit,
                    hospitalId,
                    nowLocal,
                    excludingCancelledOrderId: order.Id,
                    newlyFinalizedOrderId: null,
                    ct: ct);
        }

        var auditAction = wasAlreadyPaid
            ? "DIAGNOSTIC_ORDER_CANCELLED_REFUND_PENDING"
            : "DIAGNOSTIC_ORDER_CANCELLED";

        var auditPayload = new
        {
            diagnosticOrderId = order.Id,
            orderCode = order.OrderCode,
            visitId = order.VisitId,
            visitCode = visit?.VisitCode,
            previousOrderStatus,
            targetOrderStatus = "CANCELLED",
            previousQueueStatus,
            diagnosticQueueEntryId = diagQueue?.Id,
            technicalAbort = isTechnicalAbort,
            cancelDisposition = effectiveDisposition,
            visitChargeId = diagCharge?.Id,
            chargeStatus = resultingChargeStatus,
            wasAlreadyPaid,
            expiredPendingPaymentCount,
            transitionedToReturnConsultation,
            returnQueueEntryId = returnQueueEntry?.Id,
            reason,
        };

        try
        {
            db.AuditLogs.Add(new AuditLog
            {
                ActorUserId = actorUserId,
                HospitalId = hospitalId,
                Action = auditAction,
                EntityType = "DIAGNOSTIC_ORDER",
                EntityId = !string.IsNullOrWhiteSpace(order.OrderCode)
                    ? order.OrderCode.Trim()
                    : order.Id.ToString(CultureInfo.InvariantCulture),
                Reason = string.IsNullOrWhiteSpace(reason)
                    ? "Hủy phiếu chỉ định cận lâm sàng"
                    : reason.Trim(),
                ChangeData = JsonSerializer.Serialize(auditPayload, AuditJsonOptions),
                IpAddress = ipAddress,
                OccurredAt = nowUtc,
            });
        }
        catch (NotSupportedException)
        {
            // Minimal unit test DbContext without AuditLogs.
        }

        return new DiagnosticOrderCancelOutcome(
            PreviousOrderStatus: previousOrderStatus,
            OrderStatus: order.Status,
            QueueStatus: diagQueue?.Status,
            Disposition: effectiveDisposition,
            ChargeStatus: resultingChargeStatus,
            DiagnosticCharge: diagCharge,
            WasAlreadyPaid: wasAlreadyPaid,
            ExpiredPendingPaymentsCount: expiredPendingPaymentCount,
            TransitionedToReturnConsultation: transitionedToReturnConsultation,
            ReturnQueueEntry: returnQueueEntry);
    }

    public static Task<(bool Transitioned, QueueEntry? ReturnQueueEntry)> TryTransitionVisitToReturnConsultationAsync(
        IApplicationDbContext db,
        Visit visit,
        long hospitalId,
        long actorUserId,
        string? ipAddress,
        DateTime nowUtc,
        DateTime nowLocal,
        DateOnly referenceDate,
        CancellationToken ct)
        => TryTransitionVisitToReturnConsultationAsync(
            db,
            visit,
            hospitalId,
            nowLocal,
            excludingCancelledOrderId: null,
            newlyFinalizedOrderId: null,
            ct: ct);

    public sealed record DiagnosticOrderCancelOutcome(
        string PreviousOrderStatus,
        string OrderStatus,
        string? QueueStatus,
        string? Disposition,
        string? ChargeStatus,
        VisitCharge? DiagnosticCharge,
        bool WasAlreadyPaid,
        int ExpiredPendingPaymentsCount,
        bool TransitionedToReturnConsultation,
        QueueEntry? ReturnQueueEntry);

    public static async Task<(bool Transitioned, QueueEntry? ReturnQueueEntry)> TryTransitionVisitToReturnConsultationAsync(
        IApplicationDbContext db,
        Visit visit,
        long hospitalId,
        DateTime nowLocal,
        long? excludingCancelledOrderId,
        long? newlyFinalizedOrderId,
        CancellationToken ct)
    {
        var visitStatus = AppointmentBookingRules.NormalizeStatusKey(visit.Status);
        if (visitStatus is not ("AWAITING_RESULTS" or "AWAITING_DIAGNOSTIC"))
        {
            return (false, null);
        }

        List<DiagnosticOrder> visitOrders;
        try
        {
            visitOrders = await db.DiagnosticOrders.AsNoTracking()
                .Where(o => o.VisitId == visit.Id)
                .ToListAsync(ct);
        }
        catch (NotSupportedException)
        {
            return (false, null);
        }

        var remainingNonCancelledOrders = visitOrders
            .Where(o =>
                o.Id != excludingCancelledOrderId &&
                AppointmentBookingRules.NormalizeStatusKey(o.Status) != "CANCELLED")
            .ToList();

        if (remainingNonCancelledOrders.Count > 0)
        {
            var remainingIds = remainingNonCancelledOrders
                .Where(o => o.Id != newlyFinalizedOrderId)
                .Select(o => o.Id)
                .ToList();

            if (remainingIds.Count > 0)
            {
                List<DiagnosticResult> results;
                List<DiagnosticResultVersion> versions = [];
                try
                {
                    results = await db.DiagnosticResults.AsNoTracking()
                        .Where(r => r.DiagnosticOrderId.HasValue && remainingIds.Contains(r.DiagnosticOrderId.Value))
                        .ToListAsync(ct);

                    var resultIds = results.Select(r => r.Id).Distinct().ToList();
                    if (resultIds.Count > 0)
                    {
                        versions = await db.DiagnosticResultVersions.AsNoTracking()
                            .Where(v => v.DiagnosticResultId.HasValue && resultIds.Contains(v.DiagnosticResultId.Value))
                            .ToListAsync(ct);
                    }
                }
                catch (NotSupportedException)
                {
                    return (false, null);
                }

                var versionsByResultId = versions
                    .Where(v => v.DiagnosticResultId.HasValue)
                    .GroupBy(v => v.DiagnosticResultId!.Value)
                    .ToDictionary(
                        g => g.Key,
                        g => g.OrderByDescending(v => v.VersionNo ?? 0).ThenByDescending(v => v.Id).FirstOrDefault());

                foreach (var candidateOrder in remainingNonCancelledOrders)
                {
                    if (candidateOrder.Id == newlyFinalizedOrderId)
                    {
                        continue;
                    }

                    var candidateResults = results
                        .Where(r => r.DiagnosticOrderId == candidateOrder.Id)
                        .OrderByDescending(r => r.Id)
                        .ToList();

                    var isCandidateFinalized = candidateResults.Any(r =>
                    {
                        versionsByResultId.TryGetValue(r.Id, out var latestVer);
                        return IsResultRowFinalized(r, latestVer);
                    });

                    if (!isCandidateFinalized)
                    {
                        return (false, null);
                    }
                }
            }
        }

        var visitQueueEntries = await db.QueueEntries
            .Where(q =>
                q.VisitId == visit.Id ||
                (visit.AppointmentId.HasValue && q.AppointmentId == visit.AppointmentId.Value))
            .OrderByDescending(q => q.Id)
            .ToListAsync(ct);

        var clinicalEntries = visitQueueEntries
            .Where(q => ClinicalQueueRules.IsClinicalExaminationQueueType(q.QueueType))
            .ToList();

        var openPrimaryServing = clinicalEntries.FirstOrDefault(q =>
            ClinicalQueueRules.ResolveQueueType(q.QueueType, null) != ClinicalQueueRules.QueueTypeReturnConsultation &&
            !q.CompletedAt.HasValue &&
            AppointmentBookingRules.NormalizeStatusKey(q.Status) is "IN_PROGRESS" or "IN_CONSULTATION" or "SERVING" or "CALLED");

        if (openPrimaryServing is not null)
        {
            return (false, openPrimaryServing);
        }

        var existingOpenReturn = clinicalEntries.FirstOrDefault(q =>
            ClinicalQueueRules.ResolveQueueType(q.QueueType, null) == ClinicalQueueRules.QueueTypeReturnConsultation &&
            !q.CompletedAt.HasValue &&
            AppointmentBookingRules.NormalizeStatusKey(q.Status) is not ("COMPLETED" or "DONE" or "CANCELLED"));

        if (existingOpenReturn is not null)
        {
            var existingStatus = AppointmentBookingRules.NormalizeStatusKey(existingOpenReturn.Status);
            if (existingStatus == "WAITING")
            {
                existingOpenReturn.Status = "READY";
                existingOpenReturn.PresenceStatus = "PRESENT";
                existingOpenReturn.ArrivedAt ??= nowLocal;
                existingOpenReturn.ReadyAt ??= nowLocal;
                existingOpenReturn.BlockedReason = null;
                existingOpenReturn.StateVersion = (existingOpenReturn.StateVersion is > 0
                    ? existingOpenReturn.StateVersion.Value
                    : 1) + 1;
            }

            visit.Status = "RETURN_CONSULTATION";
            return (true, existingOpenReturn);
        }

        var referenceClinical = clinicalEntries
            .FirstOrDefault(q => AppointmentBookingRules.NormalizeStatusKey(q.Status) != "CANCELLED")
            ?? clinicalEntries.FirstOrDefault();

        var targetSpId = referenceClinical?.CurrentServicePointId ?? referenceClinical?.ServicePointId;
        if (!targetSpId.HasValue && (visit.ExaminationRoomId.HasValue || visit.AssignedDoctorId.HasValue))
        {
            try
            {
                var examPoints = await db.QueueServicePoints.AsNoTracking()
                    .Where(sp =>
                        sp.HospitalId == hospitalId &&
                        sp.IsActive != false &&
                        (sp.PointType == null || sp.PointType.ToUpper() == "EXAMINATION_ROOM"))
                    .OrderBy(sp => sp.Id)
                    .ToListAsync(ct);

                targetSpId = examPoints
                    .FirstOrDefault(sp =>
                        visit.ExaminationRoomId.HasValue &&
                        visit.AssignedDoctorId.HasValue &&
                        sp.RoomId == visit.ExaminationRoomId.Value &&
                        sp.DoctorId == visit.AssignedDoctorId.Value)?.Id
                    ?? examPoints.FirstOrDefault(sp =>
                        visit.ExaminationRoomId.HasValue && sp.RoomId == visit.ExaminationRoomId.Value)?.Id
                    ?? examPoints.FirstOrDefault(sp =>
                        visit.AssignedDoctorId.HasValue && sp.DoctorId == visit.AssignedDoctorId.Value)?.Id;
            }
            catch (NotSupportedException)
            {
                targetSpId = null;
            }
        }

        var queueDate = referenceClinical?.QueueDate
            ?? (visit.CheckedInAt.HasValue
                ? DateOnly.FromDateTime(visit.CheckedInAt.Value)
                : DateOnly.FromDateTime(nowLocal));

        var maxQueueNumber = await db.QueueEntries.AsNoTracking()
            .Where(q =>
                q.QueueDate == queueDate &&
                (targetSpId.HasValue
                    ? (q.ServicePointId == targetSpId.Value || q.CurrentServicePointId == targetSpId.Value)
                    : (q.ServicePointId == null && q.CurrentServicePointId == null)))
            .MaxAsync(q => (int?)q.QueueNumber, ct);

        var returnEntry = new QueueEntry
        {
            VisitId = visit.Id,
            AppointmentId = null,
            ServicePointId = targetSpId,
            CurrentServicePointId = targetSpId,
            QueueDate = queueDate,
            QueueNumber = (maxQueueNumber ?? 0) + 1,
            QueueType = ClinicalQueueRules.QueueTypeReturnConsultation,
            Status = "READY",
            ScheduledAt = null,
            PresenceStatus = "PRESENT",
            ArrivedAt = nowLocal,
            ReadyAt = nowLocal,
            ArrivalClass = "WALK_IN",
            ManualPriority = ClinicalQueueRules.NormalizeStoredPriority(referenceClinical?.ManualPriority),
            StateVersion = 1,
        };

        db.QueueEntries.Add(returnEntry);
        visit.Status = "RETURN_CONSULTATION";

        return (true, returnEntry);
    }
}

internal sealed record DiagnosticOrderCancellationOutcome(
    DiagnosticOrder Order,
    Visit? Visit,
    QueueEntry? DiagnosticQueueEntry,
    VisitCharge? DiagnosticCharge,
    bool WasAlreadyPaid,
    int ExpiredPendingPaymentCount,
    bool TransitionedToReturnConsultation,
    QueueEntry? ReturnConsultationQueueEntry);
