using System.Globalization;
using System.Net;
using System.Text.Json;
using MedFlow.Application.Common.Persistence;
using MedFlow.Application.Common.Time;
using MedFlow.Application.DTOs.Payments;
using MedFlow.Application.DTOs.Realtime;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Appointments;
using MedFlow.Application.Services.Diagnostic;
using MedFlow.Application.Services.Realtime;
using MedFlow.Application.Services.Visits;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.Application.Services.Payments;

public sealed class DiagnosticOrderSelectionService : IDiagnosticOrderSelectionService
{
    public const string MandatoryReviewRequiredMessage =
        "[MANDATORY_ORDER_REVIEW_REQUIRED] Chỉ định cận lâm sàng bắt buộc cho kết luận không thể tự từ chối/hủy; vui lòng chuyển bác sĩ khám xem xét lại.";
    public const string PaidOrderStaffRequiredMessage =
        "[PAID_ORDER_STAFF_CONFIRMATION_REQUIRED] Chỉ định cận lâm sàng tùy chọn đã được thanh toán. Vui lòng liên hệ quầy tiếp nhận hoặc bác sĩ khám để xác nhận bỏ qua dịch vụ (không hoàn tiền).";
    public const string ForbiddenMessage =
        "Bạn không có quyền cập nhật lựa chọn chỉ định cận lâm sàng cho lượt khám này.";

    private const string SelectionUpdatedAuditAction = "DIAGNOSTIC_ORDER_SELECTION_UPDATED";
    private const string MandatoryReviewRequestedAuditAction = "MANDATORY_ORDER_REVIEW_REQUESTED";
    private const string OrderDeclinedAuditAction = "DIAGNOSTIC_ORDER_DECLINED";
    private const string PaidOrderSkippedAuditAction = "PAID_DIAGNOSTIC_ORDER_SKIPPED_BY_PATIENT";

    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;
    private readonly IRealtimeNotificationPublisher _realtimePublisher;

    public DiagnosticOrderSelectionService(
        IApplicationDbContext db,
        TimeProvider? timeProvider = null,
        IRealtimeNotificationPublisher? realtimePublisher = null)
    {
        _db = db;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _realtimePublisher = realtimePublisher ?? NullRealtimeNotificationPublisher.Instance;
    }

    public async Task<VisitChargesResponseDto> UpdateSelectionAsync(
        long actorUserId,
        string? visitIdOrCode,
        UpdateDiagnosticOrderSelectionRequestDto? request,
        string? ipAddress = null,
        CancellationToken ct = default)
    {
        if (request is null)
            throw new ValidationException("Dữ liệu cập nhật lựa chọn chỉ định cận lâm sàng không được để trống.");

        var acceptedIds = NormalizeIdSet(request.AcceptedOrderIds, "acceptedOrderIds");
        var undecidedIds = NormalizeIdSet(request.UndecidedOrderIds, "undecidedOrderIds");
        var reviewIds = NormalizeIdSet(request.ReviewRequestedOrderIds, "reviewRequestedOrderIds");

        if (acceptedIds.Count == 0 && undecidedIds.Count == 0 && reviewIds.Count == 0)
            throw new ValidationException("Vui lòng cung cấp ít nhất một chỉ định cần cập nhật lựa chọn.");

        if (acceptedIds.Overlaps(undecidedIds) ||
            acceptedIds.Overlaps(reviewIds) ||
            undecidedIds.Overlaps(reviewIds))
        {
            throw new ValidationException("Một chỉ định không thể đồng thời nằm trong nhiều nhóm trạng thái lựa chọn.");
        }

        var reason = ValidateOptionalReason(request.Reason);
        var (actor, preVisit, hospitalId) = await AuthorizeAndResolveVisitAsync(actorUserId, visitIdOrCode, ct);
        var visitId = preVisit.Id;
        var shouldPublishPaymentChanged = false;

        await using (var tx = await _db.BeginTransactionAsync(ct))
        {
            try
            {
                await DiagnosticOrderLifecycleHelper.AcquireVisitAndDiagnosticLocksAsync(
                    _db,
                    visitId: visitId,
                    extraServicePointIds: null,
                    extraQueueEntryIds: null,
                    extraDiagnosticOrderIds: acceptedIds.Concat(undecidedIds).Concat(reviewIds).ToArray(),
                    includeChargeAndPaymentLocks: true,
                    ct: ct);

                if (_db is DbContext trackerCtx)
                    trackerCtx.ChangeTracker.Clear();

                var lockedVisit = await _db.Visits.FirstOrDefaultAsync(v => v.Id == visitId, ct)
                    ?? throw new NotFoundException(VisitPaymentAccess.VisitNotFoundMessage);

                var verifiedActor = await VisitPaymentAccess.RevalidateActorPostLockAsync(
                    _db,
                    actorUserId,
                    lockedVisit,
                    _timeProvider,
                    ForbiddenMessage,
                    [
                        PermissionCodes.PaymentView,
                        PermissionCodes.PatientCheckin,
                        PermissionCodes.PaymentCollect,
                        PermissionCodes.QueueManage,
                        PermissionCodes.HospitalConfig,
                    ],
                    ct: ct);
                actor = new SelectionActor(
                    verifiedActor.IsPatient,
                    verifiedActor.RoleLabel,
                    verifiedActor.IsPatient ? null : verifiedActor.VisitHospitalId,
                    verifiedActor.Doctor,
                    verifiedActor.OwnedPatientIds);
                hospitalId = verifiedActor.VisitHospitalId;

                EnsureVisitEditable(lockedVisit);

                var allRequestedIds = acceptedIds.Concat(undecidedIds).Concat(reviewIds).Distinct().ToList();
                var orders = await _db.DiagnosticOrders
                    .Where(o => o.VisitId == visitId && allRequestedIds.Contains(o.Id))
                    .ToListAsync(ct);

                if (orders.Count != allRequestedIds.Count)
                    throw new NotFoundException("Không tìm thấy một hoặc nhiều chỉ định cận lâm sàng thuộc lượt khám này.");

                var charges = await _db.VisitCharges
                    .Where(c => c.VisitId == visitId)
                    .ToListAsync(ct);

                var payments = await _db.Payments
                    .Where(p => p.VisitId == visitId)
                    .OrderBy(p => p.Id)
                    .ToListAsync(ct);

                var allocations = await PaymentAllocationLookup.TryLoadTrackedForVisitAsync(_db, visitId, ct);
                var (effectiveAllocations, _) = PaymentAllocationLookup.BuildEffectiveAllocations(visitId, payments, allocations);

                var chargeIdsMovedToUndecided = new HashSet<long>();
                var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

                foreach (var order in orders)
                {
                    EnsureOrderNotExecutedOrCancelled(order);
                    var charge = charges.FirstOrDefault(c => c.DiagnosticOrderId == order.Id);
                    var isPaid = charge is not null && effectiveAllocations.Any(a =>
                        a.VisitChargeId == charge.Id &&
                        string.Equals(a.Status, PaymentAllocationStatuses.Paid, StringComparison.OrdinalIgnoreCase));

                    if (acceptedIds.Contains(order.Id))
                    {
                        if (order.RequiredForConclusion != false &&
                            string.Equals(order.SelectionStatus?.Trim(), DiagnosticSelectionStatuses.ReviewRequired, StringComparison.OrdinalIgnoreCase) &&
                            actor.IsPatient)
                        {
                            // Patient changing mind and accepting mandatory order again is allowed
                        }

                        order.SelectionStatus = DiagnosticSelectionStatuses.Accepted;
                    }
                    else if (undecidedIds.Contains(order.Id))
                    {
                        if (order.RequiredForConclusion != false)
                            throw new ConflictException(MandatoryReviewRequiredMessage);

                        if (isPaid)
                            throw new ConflictException("Chỉ định tùy chọn đã được thanh toán, không thể chuyển về trạng thái chưa quyết định (UNDECIDED).");

                        order.SelectionStatus = DiagnosticSelectionStatuses.Undecided;
                        if (charge is not null)
                            chargeIdsMovedToUndecided.Add(charge.Id);
                    }
                    else if (reviewIds.Contains(order.Id))
                    {
                        if (order.RequiredForConclusion == false)
                            throw new ValidationException("Chỉ định tùy chọn không bắt buộc có thể bỏ chọn hoặc từ chối trực tiếp, không dùng REVIEW_REQUIRED.");

                        order.SelectionStatus = DiagnosticSelectionStatuses.ReviewRequired;
                        if (charge is not null)
                            chargeIdsMovedToUndecided.Add(charge.Id);

                        _db.AuditLogs.Add(new AuditLog
                        {
                            ActorUserId = actorUserId,
                            HospitalId = hospitalId,
                            Action = MandatoryReviewRequestedAuditAction,
                            EntityType = "DIAGNOSTIC_ORDER",
                            EntityId = order.Id.ToString(CultureInfo.InvariantCulture),
                            Reason = reason ?? "Bệnh nhân yêu cầu bác sĩ xem xét lại chỉ định cận lâm sàng bắt buộc.",
                            ChangeData = JsonSerializer.Serialize(new
                            {
                                visitId,
                                visitCode = lockedVisit.VisitCode,
                                diagnosticOrderId = order.Id,
                                orderCode = order.OrderCode,
                                requiredForConclusion = order.RequiredForConclusion ?? true,
                                selectionStatus = order.SelectionStatus,
                                actorRole = actor.RoleLabel,
                                reason,
                            }),
                            IpAddress = IPAddress.TryParse(ipAddress, out var ip) ? ip : null,
                            OccurredAt = nowUtc,
                        });
                    }
                }

                var cancelledQrCount = 0;
                if (chargeIdsMovedToUndecided.Count > 0)
                {
                    cancelledQrCount = CancelOverlappingPendingQrs(payments, allocations, chargeIdsMovedToUndecided);
                }

                _db.AuditLogs.Add(new AuditLog
                {
                    ActorUserId = actorUserId,
                    HospitalId = hospitalId,
                    Action = SelectionUpdatedAuditAction,
                    EntityType = "VISIT",
                    EntityId = visitId.ToString(CultureInfo.InvariantCulture),
                    Reason = reason,
                    ChangeData = JsonSerializer.Serialize(new
                    {
                        visitId,
                        acceptedOrderIds = acceptedIds.OrderBy(x => x).ToArray(),
                        undecidedOrderIds = undecidedIds.OrderBy(x => x).ToArray(),
                        reviewRequestedOrderIds = reviewIds.OrderBy(x => x).ToArray(),
                        cancelledPendingQrCount = cancelledQrCount,
                        actorRole = actor.RoleLabel,
                    }),
                    IpAddress = IPAddress.TryParse(ipAddress, out var auditIp) ? auditIp : null,
                    OccurredAt = nowUtc,
                });

                await _db.SaveChangesAsync(ct);
                shouldPublishPaymentChanged = true;

                var snapshot = await BuildChargesSnapshotAsync(lockedVisit, ct);
                await tx.CommitAsync(ct);

                if (shouldPublishPaymentChanged)
                {
                    await _realtimePublisher.TryPublishVisitPaymentChangedSafeAsync(
                        new VisitPaymentChangedNotification(visitId),
                        ct);
                }

                return snapshot;
            }
            catch (Exception ex) when (PostgresConcurrencyException.IsDeadlockOrSerializationFailure(ex))
            {
                await SafeRollbackAsync(tx, ct);
                if (_db is DbContext dbCtx)
                    dbCtx.ChangeTracker.Clear();
                throw new ConflictException("Cập nhật lựa chọn chỉ định đang có xung đột đồng thời, vui lòng thử lại.");
            }
            catch
            {
                await SafeRollbackAsync(tx, ct);
                throw;
            }
        }
    }

    public async Task<DeclineDiagnosticOrderResponseDto> DeclineOrderAsync(
        long actorUserId,
        string? visitIdOrCode,
        string? orderIdOrCode,
        DeclineDiagnosticOrderRequestDto? request = null,
        string? ipAddress = null,
        CancellationToken ct = default)
    {
        var (orderNumericId, orderCode) = QueueEntryRequestParsing.ParseIdentifier(orderIdOrCode ?? string.Empty);
        if (!orderNumericId.HasValue && string.IsNullOrWhiteSpace(orderCode))
            throw new ValidationException("Mã phiếu chỉ định cận lâm sàng không hợp lệ.");

        var reason = ValidateOptionalReason(request?.Reason);
        var (actor, preVisit, hospitalId) = await AuthorizeAndResolveVisitAsync(actorUserId, visitIdOrCode, ct);
        var visitId = preVisit.Id;

        DeclineDiagnosticOrderResponseDto response;
        var queueNotifications = new List<QueueChangedNotification>();

        await using (var tx = await _db.BeginTransactionAsync(ct))
        {
            try
            {
                // P0 lock-order fix (ADR-0061): level 02 patients MUST be locked before level 09 visits.
                // The decline path enqueues a VISIT_RETURN_READY notification whose patient_id FK takes an
                // implicit KEY SHARE on the patient row; without an explicit level-02 lock that KEY SHARE is
                // acquired *after* the visit lock, inverting the global order against execution-start
                // (which locks patient -> visit) and deadlocking (Postgres 40P01).
                await DiagnosticOrderLifecycleHelper.AcquireVisitAndDiagnosticLocksAsync(
                    _db,
                    visitIds: new[] { visitId },
                    extraServicePointIds: null,
                    extraQueueEntryIds: null,
                    extraDiagnosticOrderIds: orderNumericId.HasValue ? [orderNumericId.Value] : null,
                    includeChargeAndPaymentLocks: true,
                    ct: ct,
                    extraPatientIds: preVisit.PatientId is > 0 ? new[] { preVisit.PatientId.Value } : null);

                if (_db is DbContext trackerCtx)
                    trackerCtx.ChangeTracker.Clear();

                var lockedVisit = await _db.Visits.FirstOrDefaultAsync(v => v.Id == visitId, ct)
                    ?? throw new NotFoundException(VisitPaymentAccess.VisitNotFoundMessage);

                var verifiedActor = await VisitPaymentAccess.RevalidateActorPostLockAsync(
                    _db,
                    actorUserId,
                    lockedVisit,
                    _timeProvider,
                    ForbiddenMessage,
                    [
                        PermissionCodes.PaymentView,
                        PermissionCodes.PatientCheckin,
                        PermissionCodes.PaymentCollect,
                        PermissionCodes.QueueManage,
                        PermissionCodes.HospitalConfig,
                    ],
                    ct: ct);
                actor = new SelectionActor(
                    verifiedActor.IsPatient,
                    verifiedActor.RoleLabel,
                    verifiedActor.IsPatient ? null : verifiedActor.VisitHospitalId,
                    verifiedActor.Doctor,
                    verifiedActor.OwnedPatientIds);
                hospitalId = verifiedActor.VisitHospitalId;

                EnsureVisitEditable(lockedVisit);

                var visitOrders = await _db.DiagnosticOrders
                    .Where(o => o.VisitId == visitId)
                    .ToListAsync(ct);

                var order = orderNumericId.HasValue
                    ? visitOrders.FirstOrDefault(o => o.Id == orderNumericId.Value)
                    : visitOrders.FirstOrDefault(o =>
                        o.OrderCode != null &&
                        string.Equals(o.OrderCode.Trim(), orderCode, StringComparison.OrdinalIgnoreCase));

                if (order is null)
                    throw new NotFoundException("Không tìm thấy phiếu chỉ định cận lâm sàng thuộc lượt khám này.");

                // #877 (owner chốt 5a): Reception/bệnh nhân KHÔNG huỷ chỉ định — chỉ ghi nhận từ chối.
                // Chỉ bác sĩ được huỷ chỉ định chuyên môn và đánh giá tiếp tục/kết thúc khám.
                var isDoctor = string.Equals(actor.RoleLabel, RoleNames.Doctor, StringComparison.OrdinalIgnoreCase);
                var isPatientActor = actor.IsPatient;

                EnsureOrderNotExecutedOrCancelled(order);
                await EnsureNoNonCancelledDiagnosticResultAsync(order.Id, ct);

                var diagQueue = order.QueueEntryId.HasValue
                    ? await _db.QueueEntries.FirstOrDefaultAsync(q => q.Id == order.QueueEntryId.Value, ct)
                    : null;

                if (diagQueue is not null)
                {
                    var qs = AppointmentBookingRules.NormalizeStatusKey(diagQueue.Status);
                    if (qs is "IN_PROGRESS" or "COMPLETED" || diagQueue.CompletedAt.HasValue)
                    {
                        throw new ConflictException("Không thể từ chối chỉ định cận lâm sàng đã bắt đầu hoàn tất thực hiện.");
                    }
                }

                var diagCharge = await _db.VisitCharges
                    .FirstOrDefaultAsync(c => c.DiagnosticOrderId == order.Id && c.VisitId == visitId, ct);

                var payments = await _db.Payments
                    .Where(p => p.VisitId == visitId)
                    .OrderBy(p => p.Id)
                    .ToListAsync(ct);

                var allocations = await PaymentAllocationLookup.TryLoadTrackedForVisitAsync(_db, visitId, ct);
                var (effectiveAllocations, _) = PaymentAllocationLookup.BuildEffectiveAllocations(visitId, payments, allocations);

                var wasAlreadyPaid = diagCharge is not null && effectiveAllocations.Any(a =>
                    a.VisitChargeId == diagCharge.Id &&
                    string.Equals(a.Status, PaymentAllocationStatuses.Paid, StringComparison.OrdinalIgnoreCase));

                var nowLocal = HospitalClock.GetNow(_timeProvider);
                var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
                var cancelledPendingQrCount = 0;

                if (!isDoctor)
                {
                    // === Ghi nhận từ chối (Reception / Bệnh nhân): KHÔNG huỷ chỉ định, KHÔNG huỷ phí, KHÔNG đụng Payment. ===
                    var refusalReason = ValidateRequiredDeclineReason(reason);
                    var notifiedDoctorId = order.OrderingDoctorId ?? lockedVisit.AssignedDoctorId;
                    Doctor? notifiedDoctor = null;
                    if (notifiedDoctorId.HasValue)
                    {
                        notifiedDoctor = await _db.Doctors
                            .AsNoTracking()
                            .Include(d => d.Staff)
                            .FirstOrDefaultAsync(d => d.Id == notifiedDoctorId.Value, ct);
                    }

                    var doctorUserId = notifiedDoctor?.Staff?.UserId;
                    var orderCodeLabel = !string.IsNullOrWhiteSpace(order.OrderCode)
                        ? order.OrderCode!.Trim()
                        : $"#{order.Id}";
                    var serviceName = order.ServiceId.HasValue
                        ? await _db.Services.AsNoTracking()
                            .Where(s => s.Id == order.ServiceId.Value)
                            .Select(s => s.Name)
                            .FirstOrDefaultAsync(ct)
                        : null;

                    _db.DiagnosticOrderDeclines.Add(new DiagnosticOrderDecline
                    {
                        DiagnosticOrderId = order.Id,
                        VisitId = visitId,
                        HospitalId = hospitalId,
                        RecordedByUserId = actorUserId,
                        RecordedByRole = isPatientActor ? RoleNames.Patient : actor.RoleLabel,
                        Source = isPatientActor
                            ? DiagnosticOrderDecline.SourcePatient
                            : DiagnosticOrderDecline.SourceReception,
                        Reason = refusalReason,
                        NotifiedDoctorId = notifiedDoctor?.Id,
                        NotifiedDoctorUserId = doctorUserId,
                        CreatedAt = nowLocal,
                    });

                    if (doctorUserId.HasValue)
                    {
                        var localStamp = DateTime.SpecifyKind(nowLocal, DateTimeKind.Unspecified);
                        _db.Notifications.Add(new Notification
                        {
                            RecipientUserId = doctorUserId.Value,
                            VisitId = visitId,
                            Channel = Notification.ChannelInApp,
                            NotificationType = Notification.TypeDiagnosticOrderDeclined,
                            Subject = "Bệnh nhân từ chối chỉ định cận lâm sàng",
                            Body = $"Bệnh nhân từ chối chỉ định {orderCodeLabel}"
                                + (string.IsNullOrWhiteSpace(serviceName) ? string.Empty : $" ({serviceName})")
                                + $". Lý do: {refusalReason}. Vui lòng đánh giá tiếp tục hay kết thúc khám.",
                            Status = Notification.StatusPending,
                            RetryCount = 0,
                            IdempotencyKey = $"diagnostic-order-declined:{order.Id}:{Guid.NewGuid():N}",
                            ScheduledAt = localStamp,
                            CreatedAt = localStamp,
                        });
                    }

                    _db.AuditLogs.Add(new AuditLog
                    {
                        ActorUserId = actorUserId,
                        HospitalId = hospitalId,
                        Action = "DIAGNOSTIC_ORDER_DECLINE_RECORDED",
                        EntityType = "DIAGNOSTIC_ORDER",
                        EntityId = orderCodeLabel,
                        Reason = refusalReason,
                        ChangeData = JsonSerializer.Serialize(new
                        {
                            visitId,
                            visitCode = lockedVisit.VisitCode,
                            diagnosticOrderId = order.Id,
                            orderCode = order.OrderCode,
                            orderStatus = order.Status,
                            selectionStatus = order.SelectionStatus,
                            requiredForConclusion = order.RequiredForConclusion == true,
                            visitChargeId = diagCharge?.Id,
                            chargeStatus = diagCharge?.Status,
                            wasAlreadyPaid,
                            notifiedDoctorId = notifiedDoctor?.Id,
                            notifiedDoctorUserId = doctorUserId,
                            doctorNotified = doctorUserId.HasValue,
                            actorRole = actor.RoleLabel,
                            reason = refusalReason,
                        }),
                        IpAddress = IPAddress.TryParse(ipAddress, out var ip) ? ip : null,
                        OccurredAt = nowUtc,
                    });

                    await _db.SaveChangesAsync(ct);

                    var refusalSnapshot = await BuildChargesSnapshotAsync(lockedVisit, ct);
                    response = new DeclineDiagnosticOrderResponseDto(
                        visitId,
                        lockedVisit.VisitCode,
                        lockedVisit.Status,
                        order.Id,
                        order.OrderCode,
                        order.Status ?? "ORDERED",
                        order.SelectionStatus,
                        diagCharge?.Id,
                        diagCharge?.Status,
                        wasAlreadyPaid,
                        CancelledPendingQrCount: 0,
                        TransitionedToReturnConsultation: false,
                        refusalSnapshot,
                        RefusalRecorded: true,
                        DoctorNotified: doctorUserId.HasValue,
                        NotifiedDoctorId: notifiedDoctor?.Id);

                    await tx.CommitAsync(ct);
                }
                else
                {
                    // === Bác sĩ huỷ chỉ định chuyên môn (kể cả bắt buộc) và đánh giá tiếp tục/kết thúc khám. ===
                    if (!wasAlreadyPaid)
                    {
                        // Case 1: Unpaid cancellation by the treating doctor.
                        if (diagCharge is not null)
                        {
                            cancelledPendingQrCount = CancelOverlappingPendingQrs(
                                payments,
                                allocations,
                                new HashSet<long> { diagCharge.Id });

                            diagCharge.Status = VisitChargeStatuses.Cancelled;
                            diagCharge.CancelledAt = nowLocal;
                        }

                        order.Status = "CANCELLED";
                        order.SelectionStatus = DiagnosticSelectionStatuses.Declined;
                    }
                    else
                    {
                        // Case 2: Already-PAID Optional Voluntary Abandonment (PAY-16)
                        if (request?.ConfirmAbandonPaid == false ||
                            (request?.ConfirmAbandonPaid != true && string.IsNullOrWhiteSpace(reason)))
                        {
                            throw new ValidationException(
                                "Xác nhận bỏ qua dịch vụ cận lâm sàng đã thanh toán yêu cầu confirmAbandonPaid = true hoặc lý do xác nhận rõ ràng.");
                        }

                        // Keep Payment & PaymentAllocation 100% PAID (no refund module, no CANCELLED_REFUND_PENDING!)
                        order.Status = "CANCELLED";
                        order.SelectionStatus = DiagnosticSelectionStatuses.SkippedAfterPaid;

                        if (diagCharge is not null)
                        {
                            diagCharge.Status = VisitChargeStatuses.PaidNotPerformed;
                            diagCharge.CancelledAt = nowLocal;
                        }
                    }

                    if (diagQueue is not null &&
                        AppointmentBookingRules.NormalizeStatusKey(diagQueue.Status) != "CANCELLED")
                    {
                        var prevVersion = diagQueue.StateVersion is > 0 ? diagQueue.StateVersion.Value : 1;
                        diagQueue.Status = "CANCELLED";
                        diagQueue.CompletedAt ??= nowLocal;
                        diagQueue.BlockedReason = reason;
                        diagQueue.StateVersion = prevVersion + 1;

                        if (diagQueue.ServicePointId.HasValue || diagQueue.CurrentServicePointId.HasValue)
                        {
                            queueNotifications.Add(new QueueChangedNotification(
                                HospitalId: hospitalId,
                                ServicePointId: diagQueue.CurrentServicePointId ?? diagQueue.ServicePointId));
                        }
                    }

                    var (transitionedToReturn, returnQueue) =
                        await DiagnosticOrderLifecycleHelper.TryTransitionVisitToReturnConsultationAsync(
                            _db,
                            lockedVisit,
                            hospitalId,
                            nowLocal,
                            excludingCancelledOrderId: order.Id,
                            newlyFinalizedOrderId: null,
                            ct: ct,
                            mutatingCancelledOrder: order,
                            nowUtcOverride: nowUtc);

                    if (transitionedToReturn)
                    {
                        queueNotifications.Add(new QueueChangedNotification(
                            HospitalId: hospitalId,
                            ServicePointId: returnQueue?.CurrentServicePointId ?? returnQueue?.ServicePointId,
                            RoomId: lockedVisit.ExaminationRoomId,
                            AppointmentId: lockedVisit.AppointmentId));
                    }

                    var auditAction = wasAlreadyPaid ? PaidOrderSkippedAuditAction : OrderDeclinedAuditAction;
                    var defaultReason = wasAlreadyPaid
                        ? "Bác sĩ ghi nhận bệnh nhân không thực hiện chỉ định cận lâm sàng đã thanh toán (không hoàn tiền)."
                        : "Bác sĩ huỷ chỉ định cận lâm sàng sau khi đánh giá chuyên môn.";
                    var doctorReason = reason ?? defaultReason;

                    _db.DiagnosticOrderDeclines.Add(new DiagnosticOrderDecline
                    {
                        DiagnosticOrderId = order.Id,
                        VisitId = visitId,
                        HospitalId = hospitalId,
                        RecordedByUserId = actorUserId,
                        RecordedByRole = RoleNames.Doctor,
                        Source = DiagnosticOrderDecline.SourceDoctor,
                        Reason = doctorReason,
                        NotifiedDoctorId = actor.Doctor?.Id,
                        NotifiedDoctorUserId = actorUserId,
                        CreatedAt = nowLocal,
                    });

                    _db.AuditLogs.Add(new AuditLog
                    {
                        ActorUserId = actorUserId,
                        HospitalId = hospitalId,
                        Action = auditAction,
                        EntityType = "DIAGNOSTIC_ORDER",
                        EntityId = !string.IsNullOrWhiteSpace(order.OrderCode)
                            ? order.OrderCode.Trim()
                            : order.Id.ToString(CultureInfo.InvariantCulture),
                        Reason = doctorReason,
                        ChangeData = JsonSerializer.Serialize(new
                        {
                            visitId,
                            visitCode = lockedVisit.VisitCode,
                            diagnosticOrderId = order.Id,
                            orderCode = order.OrderCode,
                            orderStatus = order.Status,
                            selectionStatus = order.SelectionStatus,
                            visitChargeId = diagCharge?.Id,
                            chargeStatus = diagCharge?.Status,
                            wasAlreadyPaid,
                            cancelledPendingQrCount,
                            transitionedToReturnConsultation = transitionedToReturn,
                            returnQueueEntryId = returnQueue?.Id,
                            actorRole = actor.RoleLabel,
                            reason = doctorReason,
                        }),
                        IpAddress = IPAddress.TryParse(ipAddress, out var ip) ? ip : null,
                        OccurredAt = nowUtc,
                    });

                    await _db.SaveChangesAsync(ct);

                    var snapshot = await BuildChargesSnapshotAsync(lockedVisit, ct);
                    response = new DeclineDiagnosticOrderResponseDto(
                        visitId,
                        lockedVisit.VisitCode,
                        lockedVisit.Status,
                        order.Id,
                        order.OrderCode,
                        order.Status,
                        order.SelectionStatus,
                        diagCharge?.Id,
                        diagCharge?.Status,
                        wasAlreadyPaid,
                        cancelledPendingQrCount,
                        transitionedToReturn,
                        snapshot);

                    await tx.CommitAsync(ct);
                }
            }
            catch (Exception ex) when (PostgresConcurrencyException.IsDeadlockOrSerializationFailure(ex))
            {
                await SafeRollbackAsync(tx, ct);
                if (_db is DbContext dbCtx)
                    dbCtx.ChangeTracker.Clear();
                throw new ConflictException("Thao tác từ chối chỉ định cận lâm sàng đang có xung đột đồng thời, vui lòng thử lại.");
            }
            catch
            {
                await SafeRollbackAsync(tx, ct);
                throw;
            }
        }

        await _realtimePublisher.TryPublishVisitPaymentChangedSafeAsync(
            new VisitPaymentChangedNotification(visitId),
            ct);

        foreach (var qn in queueNotifications.Distinct())
        {
            await _realtimePublisher.TryPublishQueueChangedSafeAsync(qn, ct);
        }

        return response;
    }

    private async Task<(SelectionActor Actor, Visit Visit, long HospitalId)> AuthorizeAndResolveVisitAsync(
        long actorUserId,
        string? visitIdOrCode,
        CancellationToken ct)
    {
        await VisitPaymentAccess.RequireActiveUserAsync(_db, actorUserId, _timeProvider, ct);
        var (roleIds, roleCodes) = await VisitPaymentAccess.LoadActorRolesAsync(_db, actorUserId, ct);

        var isPatient = roleCodes.Contains(RoleNames.Patient);
        var isDoctor = roleCodes.Contains(RoleNames.Doctor);
        var isReception = roleCodes.Contains(RoleNames.Reception);
        var isHospitalAdmin = roleCodes.Contains(RoleNames.HospitalAdmin);

        SelectionActor actor;
        if (isReception || isHospitalAdmin)
        {
            var permSet = await VisitPaymentAccess.LoadRolePermissionCodesAsync(_db, roleIds, ct);
            if (!permSet.Contains(PermissionCodes.PaymentView) &&
                !permSet.Contains(PermissionCodes.PatientCheckin) &&
                !permSet.Contains(PermissionCodes.PaymentCollect) &&
                !permSet.Contains(PermissionCodes.QueueManage) &&
                !permSet.Contains(PermissionCodes.HospitalConfig))
            {
                throw new ForbiddenException(ForbiddenMessage);
            }

            var staff = await _db.Staff.AsNoTracking()
                .FirstOrDefaultAsync(s => s.UserId == actorUserId, ct)
                ?? throw new ForbiddenException(ForbiddenMessage);

            if (!string.Equals(staff.Status?.Trim(), "ACTIVE", StringComparison.OrdinalIgnoreCase) ||
                staff.HospitalId is null)
            {
                throw new ForbiddenException(ForbiddenMessage);
            }

            var hospital = await _db.Hospitals.AsNoTracking()
                .FirstOrDefaultAsync(h => h.Id == staff.HospitalId.Value, ct)
                ?? throw new ForbiddenException(ForbiddenMessage);

            if (hospital.IsActive == false)
                throw new ForbiddenException(VisitPaymentAccess.InactiveHospitalMessage);

            actor = new SelectionActor(false, isHospitalAdmin ? RoleNames.HospitalAdmin : RoleNames.Reception, staff.HospitalId, null, []);
        }
        else if (isDoctor)
        {
            var (staff, doctor) = await VisitPaymentAccess.RequireActiveDoctorStaffAsync(
                _db,
                actorUserId,
                ForbiddenMessage,
                ct);
            actor = new SelectionActor(false, RoleNames.Doctor, staff.HospitalId, doctor, []);
        }
        else if (isPatient)
        {
            var owned = await VisitPaymentAccess.LoadOwnedPatientIdsAsync(_db, actorUserId, _timeProvider, ct);
            actor = new SelectionActor(true, RoleNames.Patient, null, null, owned);
        }
        else
        {
            throw new ForbiddenException(ForbiddenMessage);
        }

        var parsed = VisitPaymentAccess.ParseVisitIdentifier(visitIdOrCode);
        var visit = await VisitPaymentAccess.FindVisitAsync(_db, parsed, ct);

        if (actor.IsPatient)
        {
            if (visit is null || !actor.OwnedPatientIds.Contains(visit.PatientId ?? 0))
                throw new NotFoundException(VisitPaymentAccess.VisitNotFoundMessage);
        }
        else if (visit is null)
        {
            throw new NotFoundException(VisitPaymentAccess.VisitNotFoundMessage);
        }

        var hospitalIds = await VisitHospitalResolver.CollectAsync(_db, visit, ct);
        if (hospitalIds.Count == 0)
            throw new NotFoundException(VisitPaymentAccess.VisitNotFoundMessage);
        if (hospitalIds.Count > 1)
            throw new ConflictException(VisitPaymentAccess.HospitalConflictMessage);

        var visitHospitalId = hospitalIds.First();
        if (!actor.IsPatient)
        {
            if (actor.HospitalId != visitHospitalId)
                throw new ForbiddenException(ForbiddenMessage);

            if (actor.Doctor is not null)
            {
                await VisitPaymentAccess.EnsureDoctorAssignedToVisitAsync(
                    _db,
                    actor.Doctor,
                    visit,
                    visitHospitalId,
                    ForbiddenMessage,
                    ct);
            }
        }

        return (actor, visit, visitHospitalId);
    }

    private async Task<VisitChargesResponseDto> BuildChargesSnapshotAsync(Visit visit, CancellationToken ct)
    {
        var assessment = await VisitChargeAssessor.AssessAsync(_db, _timeProvider, visit, ct);
        return new VisitChargesResponseDto(
            visit.Id,
            visit.VisitCode,
            visit.Status,
            visit.PatientId,
            assessment.Lines,
            assessment.PayableTotal,
            assessment.HasUnpricedPayableCharges,
            assessment.AmountDue,
            VisitChargesService.CurrencyVnd,
            assessment.RecognizedPaidTotal,
            assessment.SettlementStatus,
            assessment.PaymentId,
            assessment.PaymentCode,
            assessment.PaymentStatus,
            assessment.PaymentAmount,
            assessment.PaymentMethod,
            assessment.PaidAt,
            assessment.ExternalReference,
            assessment.CanPayByQr,
            SelectedAmountDue: assessment.SelectedAmountDue,
            ActivePendingQrPaymentId: assessment.ActivePendingQrPaymentId,
            ActivePendingQrPaymentCode: assessment.ActivePendingQrPaymentCode,
            ActivePendingQrAmount: assessment.ActivePendingQrAmount,
            ActivePendingQrChargeIds: assessment.ActivePendingQrChargeIds,
            HasReconciliationItems: assessment.HasReconciliationItems);
    }

    private static int CancelOverlappingPendingQrs(
        IReadOnlyList<Payment> payments,
        IReadOnlyList<PaymentAllocation> allocations,
        IReadOnlySet<long> targetChargeIds)
    {
        var pendingAllocsByPayment = allocations
            .Where(pa => string.Equals(pa.Status, PaymentAllocationStatuses.Pending, StringComparison.OrdinalIgnoreCase))
            .GroupBy(pa => pa.PaymentId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var cancelled = 0;
        foreach (var p in payments)
        {
            if (!string.Equals(p.Status?.Trim(), PaymentStatuses.Pending, StringComparison.OrdinalIgnoreCase))
                continue;

            var overlaps = false;
            if (pendingAllocsByPayment.TryGetValue(p.Id, out var pAllocs))
            {
                overlaps = pAllocs.Any(pa => targetChargeIds.Contains(pa.VisitChargeId));
            }
            else if (p.VisitChargeId.HasValue)
            {
                overlaps = targetChargeIds.Contains(p.VisitChargeId.Value);
            }

            if (!overlaps)
                continue;

            p.Status = PaymentStatuses.Cancelled;
            cancelled++;
            if (pendingAllocsByPayment.TryGetValue(p.Id, out var toCancel))
            {
                foreach (var pa in toCancel)
                    pa.Status = PaymentAllocationStatuses.Cancelled;
            }
        }

        return cancelled;
    }

    private async Task EnsureNoNonCancelledDiagnosticResultAsync(long orderId, CancellationToken ct)
    {
        try
        {
            var hasResult = await _db.DiagnosticResults.AsNoTracking()
                .AnyAsync(r => r.DiagnosticOrderId == orderId &&
                               (r.Status == null || r.Status.ToUpper() != "CANCELLED"), ct);
            if (hasResult)
            {
                throw new ConflictException("Không thể từ chối chỉ định cận lâm sàng đã có kết quả.");
            }
        }
        catch (NotSupportedException)
        {
        }
    }

    private static void EnsureVisitEditable(Visit visit)
    {
        var vs = AppointmentBookingRules.NormalizeStatusKey(visit.Status);
        if (vs is "CANCELLED" or "NO_SHOW" or "COMPLETED" or "DONE")
            throw new ConflictException($"Lượt khám đang ở trạng thái {vs}, không thể thay đổi lựa chọn chỉ định.");
    }

    private static void EnsureOrderNotExecutedOrCancelled(DiagnosticOrder order)
    {
        var os = AppointmentBookingRules.NormalizeStatusKey(order.Status);
        if (os == "CANCELLED")
            throw new ConflictException("Phiếu chỉ định cận lâm sàng đã bị hủy trước đó.");
        if (os is "IN_PROGRESS" or "COMPLETED" or "DONE" or "FINALIZED" or "VERIFIED")
            throw new ConflictException($"Phiếu chỉ định cận lâm sàng đang ở trạng thái {os}, không thể từ chối hoặc bỏ chọn.");
    }

    private static HashSet<long> NormalizeIdSet(IReadOnlyList<long>? ids, string fieldName)
    {
        var set = new HashSet<long>();
        if (ids is null)
            return set;

        foreach (var id in ids)
        {
            if (id <= 0)
                throw new ValidationException($"Danh sách {fieldName} chứa ID không hợp lệ.");
            if (!set.Add(id))
                throw new ValidationException($"Danh sách {fieldName} chứa ID trùng lặp.");
        }

        return set;
    }

    private static string? ValidateOptionalReason(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        var trimmed = raw.Trim();
        if (trimmed.Length > 500 || trimmed.Contains('<') || trimmed.Contains('>'))
            throw new ValidationException("Lý do tối đa 500 ký tự và không chứa ký tự HTML.");
        return trimmed;
    }

    private static string ValidateRequiredDeclineReason(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new ValidationException("Ghi nhận từ chối cận lâm sàng bắt buộc phải có lý do.");
        }

        var trimmed = raw.Trim();
        if (trimmed.Length > 500 || trimmed.Contains('<') || trimmed.Contains('>'))
        {
            throw new ValidationException("Lý do tối đa 500 ký tự và không chứa ký tự HTML.");
        }

        return trimmed;
    }

    private static async Task SafeRollbackAsync(IDbContextTransaction tx, CancellationToken ct)
    {
        try
        {
            await tx.RollbackAsync(ct);
        }
        catch
        {
        }
    }

    private sealed record SelectionActor(
        bool IsPatient,
        string RoleLabel,
        long? HospitalId,
        Doctor? Doctor,
        HashSet<long> OwnedPatientIds);
}
