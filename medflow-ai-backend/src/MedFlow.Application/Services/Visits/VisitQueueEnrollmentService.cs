using System.Globalization;
using System.Text.Json;
using MedFlow.Application.Common.Persistence;
using MedFlow.Application.Common.Time;
using MedFlow.Application.DTOs.Realtime;
using MedFlow.Application.DTOs.Visits;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Appointments;
using MedFlow.Application.Services.Diagnostic;
using MedFlow.Application.Services.Payments;
using MedFlow.Application.Services.Policies;
using MedFlow.Application.Services.Realtime;
using MedFlow.Application.Services.Reception;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Visits;

public sealed class VisitQueueEnrollmentService : IVisitQueueEnrollmentService
{
    public const string QueueEnrolledAuditAction = "QUEUE_ENROLLED";
    public const string QueueLeftAuditAction = "QUEUE_LEFT";
    public const string PharmacyQueueUpdatedAuditAction = "PHARMACY_QUEUE_UPDATED";

    public const string ReturnReceptionRequiredMessage =
        "[RETURN_ENROLLMENT_RECEPTION_REQUIRED] Chỉ Quầy Tiếp Đón (Reception) mới được phép điều phối bác sĩ và cấp số khám kết luận (RETURN_CONSULTATION).";

    public const string QueueEnrollRequiredMessage =
        "[QUEUE_ENROLL_REQUIRED] Tài khoản nhân sự chưa được cấp quyền đăng ký & điều phối lấy số hàng đợi (QUEUE_ENROLL).";

    public const string PatientNotPresentMessage =
        "[PATIENT_NOT_PRESENT] Bệnh nhân chưa được xác nhận có mặt tại bệnh viện hôm nay. Vui lòng đến Quầy Tiếp Đón để xác nhận hiện diện trước khi lấy số.";

    public const string ClinicalHandoffRequiredMessage =
        "[CLINICAL_HANDOFF_REQUIRED] Phiên khám lâm sàng của bệnh nhân vẫn đang mở. Bác sĩ cần kết thúc hoặc bàn giao phiên khám trước khi lấy số tiếp theo.";

    public const string IdempotencyMismatchMessage =
        "[IDEMPOTENCY_PAYLOAD_MISMATCH] Khóa idempotency đã được sử dụng cho một yêu cầu lấy số hàng đợi khác.";

    public const string CannotLeaveInProgressMessage =
        "[CANNOT_LEAVE_IN_PROGRESS_QUEUE] Không thể rời hàng đợi khi lượt khám hoặc kỹ thuật đang được thực hiện (IN_PROGRESS).";

    public const string PrescriptionNotEligibleMessage =
        "[PRESCRIPTION_NOT_ELIGIBLE_FOR_PHARMACY_QUEUE] Đơn thuốc không đủ điều kiện lấy số chờ nhận thuốc tại nhà thuốc bệnh viện.";

    public const string NoActivePharmacyPointMessage =
        "[NO_ACTIVE_PHARMACY_SERVICE_POINT] Hiện không có quầy phát thuốc (PHARMACY) đang hoạt động tại bệnh viện.";

    private static readonly JsonSerializerOptions AuditJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;
    private readonly IVisitChargeSettlementReader _settlementReader;
    private readonly IReturnCoordinationService _returnCoordinationService;
    private readonly IRealtimeNotificationPublisher _realtimePublisher;

    public VisitQueueEnrollmentService(
        IApplicationDbContext db,
        IReturnCoordinationService returnCoordinationService,
        TimeProvider? timeProvider = null,
        IVisitChargeSettlementReader? settlementReader = null,
        IRealtimeNotificationPublisher? realtimePublisher = null)
    {
        _db = db;
        _returnCoordinationService = returnCoordinationService
            ?? throw new ArgumentNullException(nameof(returnCoordinationService));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _settlementReader = settlementReader ?? new VisitChargeSettlementReader(db, _timeProvider);
        _realtimePublisher = realtimePublisher ?? NullRealtimeNotificationPublisher.Instance;
    }

    public async Task<VisitQueueEnrollmentContextDto> GetEnrollmentContextAsync(
        long actorUserId,
        string visitIdOrCode,
        CancellationToken ct = default)
    {
        await VisitPaymentAccess.RequireActiveUserAsync(_db, actorUserId, _timeProvider, ct);
        var (_, roleCodes) = await VisitPaymentAccess.LoadActorRolesAsync(_db, actorUserId, ct);

        var parsed = VisitPaymentAccess.ParseVisitIdentifier(visitIdOrCode);
        var visit = await VisitPaymentAccess.FindVisitAsync(_db, parsed, ct);

        var actor = await AuthorizeContextReaderAsync(actorUserId, roleCodes, visit, ct);
        var hospitalId = actor.HospitalId;
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var nowLocal = HospitalClock.GetNow(_timeProvider);
        var todayLocal = DateOnly.FromDateTime(nowLocal);

        var policy = await CrossDayVisitPolicyReader.LoadAsync(_db, hospitalId, nowUtc, ct);

        var visitQueues = await _db.QueueEntries.AsNoTracking()
            .Where(q =>
                q.VisitId == visit!.Id ||
                (visit.AppointmentId.HasValue && q.AppointmentId == visit.AppointmentId.Value))
            .OrderByDescending(q => q.Id)
            .ToListAsync(ct);

        var isPresentToday = VisitPresenceEvaluator.IsPresentToday(
            visit!,
            visitQueues,
            todayLocal,
            nowLocal,
            policy.PresenceTtlMinutes);

        var clinicalHandoffCompleted = !HasActiveClinicalSession(visitQueues, todayLocal);

        var servicePointsById = await _db.QueueServicePoints.AsNoTracking()
            .Where(sp => sp.HospitalId == hospitalId)
            .ToDictionaryAsync(sp => sp.Id, ct);
        var roomsById = await _db.HospitalRooms.AsNoTracking()
            .Where(r => r.HospitalId == hospitalId)
            .ToDictionaryAsync(r => r.Id, ct);
        var departmentsById = await _db.Departments.AsNoTracking()
            .Where(d => d.HospitalId == hospitalId)
            .ToDictionaryAsync(d => d.Id, ct);

        // E2-R01: PatientId is nullable — a visit with no owning patient cannot carry an
        // active-serving-queue invariant (guard is keyed on patient). Never coalesce to 0:
        // patient 0 does not exist, and scoping the check to it would silently disable
        // the single-active-queue protection for that visit.
        var activeQueue = visit!.PatientId is > 0
            ? await PatientSingleActiveQueueGuard.FindActiveServingQueueForPatientAsync(
                _db,
                visit.PatientId.Value,
                hospitalId,
                todayLocal,
                ct: ct)
            : null;
        var activeTicketDto = activeQueue is not null
            ? MapTicketDto(activeQueue, servicePointsById, roomsById, isIdempotentReplay: false)
            : null;

        var orders = await _db.DiagnosticOrders.AsNoTracking()
            .Where(o => o.VisitId == visit.Id)
            .OrderBy(o => o.Id)
            .ToListAsync(ct);
        var serviceIds = orders.Where(o => o.ServiceId.HasValue).Select(o => o.ServiceId!.Value).Distinct().ToList();
        var servicesById = serviceIds.Count > 0
            ? await _db.Services.AsNoTracking().Where(s => serviceIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct)
            : new Dictionary<long, Service>();

        var diagServicePoints = servicePointsById.Values
            .Where(sp => string.Equals(sp.PointType?.Trim(), "DIAGNOSTIC", StringComparison.OrdinalIgnoreCase))
            .OrderBy(sp => sp.Id)
            .ToList();

        var finalizedOrderIds = await LoadFinalizedDiagnosticOrderIdsAsync(orders.Select(o => o.Id).ToList(), ct);
        var queuesById = visitQueues.ToDictionary(q => q.Id);

        var diagnosticItems = new List<VisitQueueEnrollmentDiagnosticItemDto>(orders.Count);
        foreach (var order in orders)
        {
            servicesById.TryGetValue(order.ServiceId ?? 0, out var svc);
            var dept = svc?.DepartmentId.HasValue == true && departmentsById.TryGetValue(svc.DepartmentId.Value, out var d) ? d : null;
            var orderStatus = finalizedOrderIds.Contains(order.Id)
                ? "COMPLETED"
                : AppointmentBookingRules.NormalizeStatusKey(order.Status);
            var selectionStatus = string.IsNullOrWhiteSpace(order.SelectionStatus)
                ? DiagnosticSelectionStatuses.Accepted
                : order.SelectionStatus.Trim().ToUpperInvariant();
            var isExpired = CrossDayVisitPolicyReader.IsOrderExpiredForExecution(
                order,
                policy.DiagnosticOrderValidityDays,
                nowLocal);

            var eligibility = await _settlementReader.CheckDiagnosticOrderEligibilityAsync(order.Id, ct);
            var compatibleSp = svc is not null
                ? DiagnosticServicePointResolver.TryResolveCompatibleForCatalog(
                    svc.Id,
                    svc,
                    diagServicePoints,
                    roomsById,
                    departmentsById,
                    hospitalId)
                : null;
            var isCompatibleOperational = compatibleSp is not null &&
                DiagnosticServicePointResolver.IsOperational(compatibleSp, roomsById, departmentsById, hospitalId) &&
                DiagnosticServicePointResolver.IsCompatibleForOrder(compatibleSp, roomsById, order.ServiceId, svc, departmentsById, hospitalId);
            var compatibleRoom = compatibleSp?.RoomId.HasValue == true && roomsById.TryGetValue(compatibleSp.RoomId.Value, out var r) ? r : null;

            var linkedQe = order.QueueEntryId.HasValue && queuesById.TryGetValue(order.QueueEntryId.Value, out var lq) ? lq : null;

            var (canEnroll, blockedCode, blockedMessage) = EvaluateDiagnosticItemBlock(
                visit,
                orderStatus,
                selectionStatus,
                isExpired,
                eligibility.IsUnlocked,
                isCompatibleOperational,
                clinicalHandoffCompleted,
                activeQueue,
                actor.IsPatient,
                isPresentToday);

            diagnosticItems.Add(new VisitQueueEnrollmentDiagnosticItemDto(
                OrderId: order.Id,
                OrderCode: FormatOrderCode(order, todayLocal),
                ServiceId: order.ServiceId ?? 0,
                ServiceCode: svc?.ServiceCode ?? string.Empty,
                ServiceName: svc?.Name ?? string.Empty,
                DepartmentId: svc?.DepartmentId,
                DepartmentName: dept?.Name,
                SelectionStatus: selectionStatus,
                OrderStatus: string.IsNullOrWhiteSpace(orderStatus) ? "ORDERED" : orderStatus,
                SettlementStatus: eligibility.StatusCode,
                IsPaymentSettled: eligibility.IsUnlocked,
                IsExpiredForExecution: isExpired,
                CanEnrollNow: canEnroll,
                BlockedReasonCode: blockedCode,
                BlockedReasonMessage: blockedMessage,
                CompatibleServicePointId: compatibleSp?.Id,
                CompatibleServicePointName: compatibleSp?.Name,
                CompatibleRoomId: compatibleRoom?.Id,
                CompatibleRoomCode: compatibleRoom?.RoomCode,
                CompatibleRoomName: compatibleRoom?.Name,
                CurrentQueueEntryId: linkedQe?.Id,
                CurrentQueueStatus: linkedQe is not null ? AppointmentBookingRules.NormalizeStatusKey(linkedQe.Status) : null));
        }

        var isReadyForReturn = await DiagnosticOrderLifecycleHelper.EvaluateVisitReturnReadinessAsync(_db, visit.Id, ct: ct);
        var hasReviewOrExpired = orders.Any(o =>
            AppointmentBookingRules.NormalizeStatusKey(o.Status) != "CANCELLED" &&
            (CrossDayVisitPolicyReader.IsOrderExpiredForExecution(o, policy.DiagnosticOrderValidityDays, nowLocal) ||
             string.Equals(o.SelectionStatus?.Trim(), DiagnosticSelectionStatuses.ReviewRequired, StringComparison.OrdinalIgnoreCase)));

        var currentReturnEntry = visitQueues.FirstOrDefault(q =>
            q.QueueDate == todayLocal &&
            !q.CompletedAt.HasValue &&
            string.Equals(q.QueueType?.Trim(), ClinicalQueueRules.QueueTypeReturnConsultation, StringComparison.OrdinalIgnoreCase) &&
            AppointmentBookingRules.NormalizeStatusKey(q.Status) is not ("COMPLETED" or "DONE" or "CANCELLED" or "DEFERRED"));

        var returnSummary = new VisitQueueEnrollmentReturnSummaryDto(
            IsReadyForReturn: isReadyForReturn,
            ReadyForReturnAt: visit.ReadyForReturnAt,
            HasReviewRequiredOrExpiredOrders: hasReviewOrExpired,
            RequiresReceptionCoordination: true,
            ReceptionGuidanceMessage: isReadyForReturn
                ? "Đã có đủ kết quả cận lâm sàng. Vui lòng đến Quầy Tiếp Đón (Reception) để được điều phối bác sĩ và cấp số khám kết luận."
                : "Khám kết luận và đánh giá lại chỉ định quá hạn được điều phối tập trung tại Quầy Tiếp Đón (Reception).",
            CurrentReturnTicket: currentReturnEntry is not null
                ? MapTicketDto(currentReturnEntry, servicePointsById, roomsById, isIdempotentReplay: false)
                : null);

        var prescriptions = await _db.Prescriptions.AsNoTracking()
            .Where(p => p.VisitId == visit.Id)
            .OrderByDescending(p => p.Id)
            .ToListAsync(ct);

        var pharmacyClearance = await _settlementReader.CheckPharmacyVisitClearanceAsync(visit.Id, ct);
        var activePharmacySp = ResolveOperationalPharmacyServicePoint(
            servicePointsById.Values,
            roomsById,
            departmentsById,
            hospitalId,
            requestedServicePointId: null);

        var pharmacyItems = new List<VisitQueueEnrollmentPharmacyItemDto>(prescriptions.Count);
        foreach (var rx in prescriptions)
        {
            var rxStatus = AppointmentBookingRules.NormalizeStatusKey(rx.Status);
            if (rxStatus == "CANCELLED")
            {
                continue;
            }

            var isSuperseded = prescriptions.Any(other =>
                other.Id != rx.Id &&
                other.ReplacesPrescriptionId == rx.Id &&
                AppointmentBookingRules.NormalizeStatusKey(other.Status) is "ISSUED" or "PREPARING" or "READY" or "DISPENSED");
            var hasCompletedDispense = await _db.MedicationDispenses.AsNoTracking()
                .AnyAsync(md => md.PrescriptionId == rx.Id &&
                                md.Status != null &&
                                (md.Status.ToUpper() == "DISPENSED" || md.Status.ToUpper() == "COMPLETED"), ct);
            var rxQueue = visitQueues.FirstOrDefault(q =>
                string.Equals(q.QueueType?.Trim(), QueueEnrollmentTargets.Pharmacy, StringComparison.OrdinalIgnoreCase) &&
                (q.PrescriptionId == rx.Id || q.PrescriptionId == null));

            var (canEnrollRx, rxBlockedCode, rxBlockedMsg) = EvaluatePharmacyItemBlock(
                visit,
                rx,
                rxStatus,
                isSuperseded,
                hasCompletedDispense,
                pharmacyClearance.IsUnlocked,
                activePharmacySp is not null,
                clinicalHandoffCompleted,
                activeQueue,
                actor.IsPatient,
                isPresentToday);

            pharmacyItems.Add(new VisitQueueEnrollmentPharmacyItemDto(
                PrescriptionId: rx.Id,
                PrescriptionCode: rx.PrescriptionCode ?? $"RX-{rx.Id:D6}",
                PrescriptionStatus: hasCompletedDispense ? "DISPENSED" : (string.IsNullOrWhiteSpace(rxStatus) ? "ISSUED" : rxStatus),
                FulfillmentChoice: rx.FulfillmentChoice,
                IsPaymentCleared: pharmacyClearance.IsUnlocked,
                CanEnrollNow: canEnrollRx,
                BlockedReasonCode: rxBlockedCode,
                BlockedReasonMessage: rxBlockedMsg,
                CurrentQueueEntryId: rxQueue?.Id,
                CurrentQueueStatus: rxQueue is not null ? AppointmentBookingRules.NormalizeStatusKey(rxQueue.Status) : null));
        }

        return new VisitQueueEnrollmentContextDto(
            VisitId: visit.Id,
            VisitCode: visit.VisitCode ?? $"VIS-{visit.Id:D6}",
            VisitStatus: AppointmentBookingRules.NormalizeStatusKey(visit.Status),
            PresenceState: string.IsNullOrWhiteSpace(visit.PresenceState) ? "PRESENT" : visit.PresenceState.Trim().ToUpperInvariant(),
            IsPresentToday: isPresentToday,
            PresenceSessionNo: Math.Max(1, visit.PresenceSessionNo),
            ClinicalHandoffCompleted: clinicalHandoffCompleted,
            ActiveQueueEntry: activeTicketDto,
            DiagnosticItems: diagnosticItems,
            ReturnConsultationSummary: returnSummary,
            PharmacyItems: pharmacyItems);
    }

    public async Task<VisitQueueEnrollmentTicketDto> EnrollAsync(
        long actorUserId,
        string visitIdOrCode,
        CreateVisitQueueEnrollmentRequestDto request,
        string? ipAddress = null,
        CancellationToken ct = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Target))
        {
            throw new ValidationException("Loại hàng đợi cần lấy số (Target) không được để trống.");
        }

        var normalizedTarget = request.Target.Trim().ToUpperInvariant();
        if (normalizedTarget is not (QueueEnrollmentTargets.Diagnostic
            or QueueEnrollmentTargets.ReturnConsultation
            or QueueEnrollmentTargets.Pharmacy))
        {
            throw new ValidationException("Loại hàng đợi cần lấy số (Target) chỉ chấp nhận DIAGNOSTIC, RETURN_CONSULTATION hoặc PHARMACY.");
        }

        var normalizedIdempotencyKey = NormalizeIdempotencyKey(request.IdempotencyKey);

        await VisitPaymentAccess.RequireActiveUserAsync(_db, actorUserId, _timeProvider, ct);
        var (roleIds, roleCodes) = await VisitPaymentAccess.LoadActorRolesAsync(_db, actorUserId, ct);

        if (normalizedTarget == QueueEnrollmentTargets.ReturnConsultation)
        {
            if (!roleCodes.Contains(RoleNames.Reception) && !roleCodes.Contains(RoleNames.HospitalAdmin))
            {
                throw new ForbiddenException(ReturnReceptionRequiredMessage);
            }

            var returnResult = await _returnCoordinationService.EnrollReturnConsultationAsync(
                actorUserId,
                visitIdOrCode,
                new CreateReturnEnrollmentRequestDto(
                    DoctorId: request.ReplacementDoctorId,
                    ServicePointId: request.ServicePointId,
                    Purpose: request.Purpose,
                    ReassignmentReason: request.ReassignmentReason,
                    IdempotencyKey: normalizedIdempotencyKey),
                ipAddress,
                ct);
            return returnResult.Ticket;
        }

        if (normalizedTarget == QueueEnrollmentTargets.Diagnostic)
        {
            return await EnrollDiagnosticAsync(
                actorUserId,
                roleIds,
                roleCodes,
                visitIdOrCode,
                request,
                normalizedIdempotencyKey,
                ipAddress,
                ct);
        }

        return await EnrollPharmacyAsync(
            actorUserId,
            roleIds,
            roleCodes,
            visitIdOrCode,
            request,
            normalizedIdempotencyKey,
            ipAddress,
            ct);
    }

    private async Task<VisitQueueEnrollmentTicketDto> EnrollDiagnosticAsync(
        long actorUserId,
        IReadOnlyCollection<long> preRoleIds,
        IReadOnlySet<string> preRoleCodes,
        string visitIdOrCode,
        CreateVisitQueueEnrollmentRequestDto request,
        string? idempotencyKey,
        string? ipAddress,
        CancellationToken ct)
    {
        if (!request.DiagnosticOrderId.HasValue || request.DiagnosticOrderId.Value <= 0)
        {
            throw new ValidationException("Mã định danh phiếu chỉ định cận lâm sàng (DiagnosticOrderId > 0) là bắt buộc khi lấy số DIAGNOSTIC.");
        }

        if (request.ServicePointId is <= 0)
        {
            throw new ValidationException("ServicePointId phải lớn hơn 0.");
        }

        var parsed = VisitPaymentAccess.ParseVisitIdentifier(visitIdOrCode);
        var preVisit = await VisitPaymentAccess.FindVisitAsync(_db, parsed, ct);
        var preActor = await AuthorizeDiagnosticEnrollmentActorAsync(actorUserId, preRoleIds, preRoleCodes, preVisit, ct);
        var hospitalId = preActor.HospitalId;
        var visitId = preVisit!.Id;
        var targetOrderId = request.DiagnosticOrderId.Value;

        var preOrder = await _db.DiagnosticOrders.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == targetOrderId && o.VisitId == visitId, ct)
            ?? throw new NotFoundException("DiagnosticOrder", targetOrderId);

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var nowLocal = HospitalClock.GetNow(_timeProvider);
        var todayLocal = DateOnly.FromDateTime(nowLocal);

        var preRoomsIds = await _db.HospitalRooms.AsNoTracking()
            .Where(r => r.HospitalId == hospitalId)
            .Select(r => r.Id)
            .OrderBy(id => id)
            .ToArrayAsync(ct);
        var preSpIds = await _db.QueueServicePoints.AsNoTracking()
            .Where(sp => sp.HospitalId == hospitalId)
            .Select(sp => sp.Id)
            .OrderBy(id => id)
            .ToArrayAsync(ct);

        VisitQueueEnrollmentTicketDto ticketDto;
        long? notifySpId = null;

        await using (var tx = await _db.BeginTransactionAsync(ct))
        {
            try
            {
                // ADR-0061 strict lock order: 02 patients -> 04 services -> 06 hospital_rooms -> 09 visits -> 10 queue_service_points -> 11 queue_entries -> 12 diagnostic_orders
                if (preVisit.PatientId is long pid && pid > 0)
                {
                    await _db.TryLockPatientForUpdateAsync(pid, ct);
                }

                if (preOrder.ServiceId is long sid && sid > 0)
                {
                    await _db.TryLockServiceForUpdateAsync(sid, ct);
                }

                if (preRoomsIds.Length > 0)
                {
                    await _db.TryLockRoomsForUpdateAsync(preRoomsIds, ct);
                }

                await _db.TryLockVisitForUpdateAsync(visitId, ct);

                if (preSpIds.Length > 0)
                {
                    await _db.TryLockServicePointsForUpdateAsync(preSpIds, ct);
                }

                var preQeIds = await _db.QueueEntries.AsNoTracking()
                    .Where(q => q.VisitId == visitId)
                    .Select(q => q.Id)
                    .OrderBy(id => id)
                    .ToArrayAsync(ct);
                if (preQeIds.Length > 0)
                {
                    await _db.TryLockQueueEntriesForUpdateAsync(preQeIds, ct);
                }

                await _db.TryLockDiagnosticOrdersForUpdateAsync([targetOrderId], ct);

                if (_db is DbContext trackerCtx)
                {
                    trackerCtx.ChangeTracker.Clear();
                }

                var lockedVisit = await _db.Visits.FirstOrDefaultAsync(v => v.Id == visitId, ct)
                    ?? throw new NotFoundException(VisitPaymentAccess.VisitNotFoundMessage);

                await VisitPaymentAccess.RequireActiveUserAsync(_db, actorUserId, _timeProvider, ct);
                var (postRoleIds, postRoleCodes) = await VisitPaymentAccess.LoadActorRolesAsync(_db, actorUserId, ct);
                var postActor = await AuthorizeDiagnosticEnrollmentActorAsync(actorUserId, postRoleIds, postRoleCodes, lockedVisit, ct);
                hospitalId = postActor.HospitalId;

                var roomsById = await _db.HospitalRooms.AsNoTracking()
                    .Where(r => r.HospitalId == hospitalId)
                    .ToDictionaryAsync(r => r.Id, ct);
                var servicePointsById = await _db.QueueServicePoints.AsNoTracking()
                    .Where(sp => sp.HospitalId == hospitalId)
                    .ToDictionaryAsync(sp => sp.Id, ct);
                var departmentsById = await _db.Departments.AsNoTracking()
                    .Where(d => d.HospitalId == hospitalId)
                    .ToDictionaryAsync(d => d.Id, ct);

                if (idempotencyKey is not null)
                {
                    var existingByKey = await _db.QueueEntries.AsNoTracking()
                        .FirstOrDefaultAsync(q => q.IdempotencyKey == idempotencyKey, ct);
                    if (existingByKey is not null)
                    {
                        if (existingByKey.VisitId != visitId ||
                            !string.Equals(existingByKey.QueueType?.Trim(), QueueEnrollmentTargets.Diagnostic, StringComparison.OrdinalIgnoreCase) ||
                            existingByKey.DiagnosticOrderId != targetOrderId)
                        {
                            throw new ConflictException(IdempotencyMismatchMessage);
                        }

                        await tx.CommitAsync(ct);
                        return MapTicketDto(existingByKey, servicePointsById, roomsById, isIdempotentReplay: true);
                    }
                }

                var visitStatus = AppointmentBookingRules.NormalizeStatusKey(lockedVisit.Status);
                if (visitStatus is "CANCELLED" or "NO_SHOW" or "COMPLETED" or "DONE")
                {
                    throw new ConflictException($"Lượt khám đang ở trạng thái '{visitStatus}', không thể lấy số cận lâm sàng.");
                }

                var trackedVisitQueues = await _db.QueueEntries
                    .Where(q =>
                        q.VisitId == visitId ||
                        (lockedVisit.AppointmentId.HasValue && q.AppointmentId == lockedVisit.AppointmentId.Value))
                    .OrderByDescending(q => q.Id)
                    .ToListAsync(ct);

                PatientSingleActiveQueueGuard.CloseStalePriorDayQueuesOnVisit(trackedVisitQueues, todayLocal, nowLocal);

                var policy = await CrossDayVisitPolicyReader.LoadAsync(_db, hospitalId, nowUtc, ct);

                if (postActor.IsPatient)
                {
                    if (!VisitPresenceEvaluator.IsPresentToday(lockedVisit, trackedVisitQueues, todayLocal, nowLocal, policy.PresenceTtlMinutes))
                    {
                        throw new ConflictException(PatientNotPresentMessage);
                    }
                }
                else
                {
                    VisitPresenceEvaluator.StampPresentToday(lockedVisit, todayLocal, nowLocal);
                }

                if (HasActiveClinicalSession(trackedVisitQueues, todayLocal))
                {
                    throw new ConflictException(ClinicalHandoffRequiredMessage);
                }

                await PatientSingleActiveQueueGuard.EnsureNoOtherActiveServingQueueAsync(
                    _db,
                    lockedVisit.PatientId,
                    hospitalId,
                    todayLocal,
                    excludeQueueEntryId: null,
                    ct: ct);

                var lockedOrder = await _db.DiagnosticOrders.FirstOrDefaultAsync(o => o.Id == targetOrderId && o.VisitId == visitId, ct)
                    ?? throw new NotFoundException("DiagnosticOrder", targetOrderId);

                var finalizedIds = await LoadFinalizedDiagnosticOrderIdsAsync([lockedOrder.Id], ct);
                var orderStatus = finalizedIds.Contains(lockedOrder.Id)
                    ? "COMPLETED"
                    : AppointmentBookingRules.NormalizeStatusKey(lockedOrder.Status);
                if (orderStatus is not ("ORDERED" or "CONFIRMED"))
                {
                    throw new ConflictException(
                        $"[DIAGNOSTIC_ORDER_NOT_ELIGIBLE] Phiếu chỉ định cận lâm sàng đang ở trạng thái '{orderStatus}', không hợp lệ để lấy số xếp hàng.");
                }

                var selectionStatus = string.IsNullOrWhiteSpace(lockedOrder.SelectionStatus)
                    ? DiagnosticSelectionStatuses.Accepted
                    : lockedOrder.SelectionStatus.Trim().ToUpperInvariant();
                if (selectionStatus != DiagnosticSelectionStatuses.Accepted)
                {
                    throw new ConflictException(
                        $"[DIAGNOSTIC_ORDER_NOT_ELIGIBLE] Phiếu chỉ định cận lâm sàng đang ở trạng thái lựa chọn '{selectionStatus}', chưa đủ điều kiện lấy số.");
                }

                if (CrossDayVisitPolicyReader.IsOrderExpiredForExecution(lockedOrder, policy.DiagnosticOrderValidityDays, nowLocal))
                {
                    throw new ConflictException(CrossDayVisitPolicyReader.ExpiredOrderDoctorApprovalRequiredMessage);
                }

                var settlement = await _settlementReader.CheckDiagnosticOrderEligibilityAsync(lockedOrder.Id, ct);
                if (!settlement.IsUnlocked)
                {
                    throw new ConflictException(
                        settlement.Message ?? "[UNPAID_DIAGNOSTIC_FEE] Chỉ định cận lâm sàng chưa hoàn tất thanh toán phí dịch vụ.");
                }

                var liveService = lockedOrder.ServiceId.HasValue
                    ? await _db.Services.AsNoTracking().FirstOrDefaultAsync(s => s.Id == lockedOrder.ServiceId.Value, ct)
                    : null;

                var diagPoints = servicePointsById.Values
                    .Where(sp => string.Equals(sp.PointType?.Trim(), "DIAGNOSTIC", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(sp => sp.Id)
                    .ToList();

                var targetSpId = DiagnosticServicePointResolver.ResolveForExecutionOrThrow(
                    lockedOrder,
                    liveService,
                    request.ServicePointId,
                    diagPoints,
                    roomsById,
                    trackedVisitQueues,
                    hospitalId,
                    departmentsById);

                var maxQueueNumber = await _db.QueueEntries.AsNoTracking()
                    .Where(q =>
                        q.QueueDate == todayLocal &&
                        (q.ServicePointId == targetSpId || q.CurrentServicePointId == targetSpId))
                    .MaxAsync(q => (int?)q.QueueNumber, ct);
                var nextQueueNumber = (maxQueueNumber ?? 0) + 1;

                var derivedPriority = ResolveServerSidePriority(trackedVisitQueues);

                var newEntry = new QueueEntry
                {
                    VisitId = visitId,
                    AppointmentId = null,
                    DiagnosticOrderId = lockedOrder.Id,
                    PrescriptionId = null,
                    ServicePointId = targetSpId,
                    CurrentServicePointId = targetSpId,
                    QueueDate = todayLocal,
                    QueueNumber = nextQueueNumber,
                    QueueType = QueueEnrollmentTargets.Diagnostic,
                    Status = "WAITING",
                    PresenceStatus = "PRESENT",
                    ArrivedAt = nowLocal,
                    ReadyAt = nowLocal,
                    ArrivalClass = "WALK_IN",
                    ManualPriority = derivedPriority,
                    IdempotencyKey = idempotencyKey,
                    StateVersion = 1,
                };

                _db.QueueEntries.Add(newEntry);
                await _db.SaveChangesAsync(ct);

                lockedOrder.QueueEntryId = newEntry.Id;
                lockedOrder.DeferredUntilDate = null;

                _db.AuditLogs.Add(new AuditLog
                {
                    ActorUserId = actorUserId,
                    HospitalId = hospitalId,
                    Action = QueueEnrolledAuditAction,
                    EntityType = "QUEUE_ENTRY",
                    EntityId = newEntry.Id.ToString(CultureInfo.InvariantCulture),
                    Reason = $"Lấy số hàng đợi cận lâm sàng cho chỉ định #{lockedOrder.Id}",
                    ChangeData = JsonSerializer.Serialize(new
                    {
                        queueEntryId = newEntry.Id,
                        visitId,
                        visitCode = lockedVisit.VisitCode,
                        target = QueueEnrollmentTargets.Diagnostic,
                        diagnosticOrderId = lockedOrder.Id,
                        servicePointId = targetSpId,
                        queueNumber = nextQueueNumber,
                        actorRole = postActor.RoleLabel,
                        presenceSessionNo = lockedVisit.PresenceSessionNo,
                        idempotencyKey,
                    }, AuditJsonOptions),
                    IpAddress = QueueEntryRequestParsing.ParseIpAddress(ipAddress),
                    OccurredAt = nowUtc,
                });

                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                notifySpId = targetSpId;
                ticketDto = MapTicketDto(newEntry, servicePointsById, roomsById, isIdempotentReplay: false);
            }
            catch (DbUpdateException ex) when (PostgresConcurrencyException.IsUniqueViolation(ex))
            {
                await SafeRollbackAsync(tx, ct);
                if (_db is DbContext dbCtx)
                {
                    dbCtx.ChangeTracker.Clear();
                }

                if (idempotencyKey is not null)
                {
                    var racedByKey = await _db.QueueEntries.AsNoTracking()
                        .FirstOrDefaultAsync(q => q.IdempotencyKey == idempotencyKey, ct);
                    if (racedByKey is not null)
                    {
                        if (racedByKey.VisitId != visitId ||
                            !string.Equals(racedByKey.QueueType?.Trim(), QueueEnrollmentTargets.Diagnostic, StringComparison.OrdinalIgnoreCase) ||
                            racedByKey.DiagnosticOrderId != targetOrderId)
                        {
                            throw new ConflictException(IdempotencyMismatchMessage);
                        }

                        var replaySps = await _db.QueueServicePoints.AsNoTracking()
                            .Where(sp => sp.HospitalId == hospitalId)
                            .ToDictionaryAsync(sp => sp.Id, ct);
                        var replayRooms = await _db.HospitalRooms.AsNoTracking()
                            .Where(r => r.HospitalId == hospitalId)
                            .ToDictionaryAsync(r => r.Id, ct);
                        return MapTicketDto(racedByKey, replaySps, replayRooms, isIdempotentReplay: true);
                    }
                }

                throw new ConflictException(PatientSingleActiveQueueGuard.ActiveQueueAlreadyExistsMessage);
            }
            catch (Exception ex) when (PostgresConcurrencyException.IsDeadlockOrSerializationFailure(ex))
            {
                await SafeRollbackAsync(tx, ct);
                if (_db is DbContext dbCtx)
                {
                    dbCtx.ChangeTracker.Clear();
                }

                throw new ConflictException("Yêu cầu lấy số cận lâm sàng đang có xung đột đồng thời, vui lòng thử lại.");
            }
        }

        await _realtimePublisher.TryPublishQueueChangedSafeAsync(
            new QueueChangedNotification(HospitalId: hospitalId, ServicePointId: notifySpId),
            ct);

        return ticketDto;
    }

    private async Task<VisitQueueEnrollmentTicketDto> EnrollPharmacyAsync(
        long actorUserId,
        IReadOnlyCollection<long> preRoleIds,
        IReadOnlySet<string> preRoleCodes,
        string visitIdOrCode,
        CreateVisitQueueEnrollmentRequestDto request,
        string? idempotencyKey,
        string? ipAddress,
        CancellationToken ct)
    {
        if (request.PrescriptionId is <= 0)
        {
            throw new ValidationException("PrescriptionId phải lớn hơn 0.");
        }

        if (request.ServicePointId is <= 0)
        {
            throw new ValidationException("ServicePointId phải lớn hơn 0.");
        }

        var parsed = VisitPaymentAccess.ParseVisitIdentifier(visitIdOrCode);
        var preVisit = await VisitPaymentAccess.FindVisitAsync(_db, parsed, ct);
        var preActor = await AuthorizePharmacyEnrollmentActorAsync(actorUserId, preRoleIds, preRoleCodes, preVisit, ct);
        var hospitalId = preActor.HospitalId;
        var visitId = preVisit!.Id;

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var nowLocal = HospitalClock.GetNow(_timeProvider);
        var todayLocal = DateOnly.FromDateTime(nowLocal);

        var preRoomIds = await _db.HospitalRooms.AsNoTracking()
            .Where(r => r.HospitalId == hospitalId)
            .Select(r => r.Id)
            .OrderBy(id => id)
            .ToArrayAsync(ct);
        var preSpIds = await _db.QueueServicePoints.AsNoTracking()
            .Where(sp => sp.HospitalId == hospitalId)
            .Select(sp => sp.Id)
            .OrderBy(id => id)
            .ToArrayAsync(ct);

        VisitQueueEnrollmentTicketDto ticketDto;
        long? notifySpId = null;

        await using (var tx = await _db.BeginTransactionAsync(ct))
        {
            try
            {
                // ADR-0061 strict lock order: 02 patients -> 06 hospital_rooms -> 09 visits -> 10 queue_service_points -> 11 queue_entries -> 13 prescriptions
                if (preVisit.PatientId is long pid && pid > 0)
                {
                    await _db.TryLockPatientForUpdateAsync(pid, ct);
                }

                if (preRoomIds.Length > 0)
                {
                    await _db.TryLockRoomsForUpdateAsync(preRoomIds, ct);
                }

                await _db.TryLockVisitForUpdateAsync(visitId, ct);

                if (preSpIds.Length > 0)
                {
                    await _db.TryLockServicePointsForUpdateAsync(preSpIds, ct);
                }

                var preQeIds = await _db.QueueEntries.AsNoTracking()
                    .Where(q => q.VisitId == visitId)
                    .Select(q => q.Id)
                    .OrderBy(id => id)
                    .ToArrayAsync(ct);
                if (preQeIds.Length > 0)
                {
                    await _db.TryLockQueueEntriesForUpdateAsync(preQeIds, ct);
                }

                var preRxIds = await _db.Prescriptions.AsNoTracking()
                    .Where(p => p.VisitId == visitId)
                    .Select(p => p.Id)
                    .OrderBy(id => id)
                    .ToArrayAsync(ct);
                if (preRxIds.Length > 0)
                {
                    await _db.TryLockPrescriptionsForUpdateAsync(preRxIds, ct);
                }

                if (_db is DbContext trackerCtx)
                {
                    trackerCtx.ChangeTracker.Clear();
                }

                var lockedVisit = await _db.Visits.FirstOrDefaultAsync(v => v.Id == visitId, ct)
                    ?? throw new NotFoundException(VisitPaymentAccess.VisitNotFoundMessage);

                await VisitPaymentAccess.RequireActiveUserAsync(_db, actorUserId, _timeProvider, ct);
                var (postRoleIds, postRoleCodes) = await VisitPaymentAccess.LoadActorRolesAsync(_db, actorUserId, ct);
                var postActor = await AuthorizePharmacyEnrollmentActorAsync(actorUserId, postRoleIds, postRoleCodes, lockedVisit, ct);
                hospitalId = postActor.HospitalId;

                var roomsById = await _db.HospitalRooms.AsNoTracking()
                    .Where(r => r.HospitalId == hospitalId)
                    .ToDictionaryAsync(r => r.Id, ct);
                var servicePointsById = await _db.QueueServicePoints.AsNoTracking()
                    .Where(sp => sp.HospitalId == hospitalId)
                    .ToDictionaryAsync(sp => sp.Id, ct);
                var departmentsById = await _db.Departments.AsNoTracking()
                    .Where(d => d.HospitalId == hospitalId)
                    .ToDictionaryAsync(d => d.Id, ct);

                var visitPrescriptions = await _db.Prescriptions.AsNoTracking()
                    .Where(p => p.VisitId == visitId)
                    .OrderByDescending(p => p.Id)
                    .ToListAsync(ct);

                var targetRx = await ResolveTargetPrescriptionOrThrowAsync(visitPrescriptions, request.PrescriptionId, ct);

                if (idempotencyKey is not null)
                {
                    var existingByKey = await _db.QueueEntries.AsNoTracking()
                        .FirstOrDefaultAsync(q => q.IdempotencyKey == idempotencyKey, ct);
                    if (existingByKey is not null)
                    {
                        if (existingByKey.VisitId != visitId ||
                            !string.Equals(existingByKey.QueueType?.Trim(), QueueEnrollmentTargets.Pharmacy, StringComparison.OrdinalIgnoreCase) ||
                            existingByKey.PrescriptionId != targetRx.Id)
                        {
                            throw new ConflictException(IdempotencyMismatchMessage);
                        }

                        await tx.CommitAsync(ct);
                        return MapTicketDto(existingByKey, servicePointsById, roomsById, isIdempotentReplay: true);
                    }
                }

                var visitStatus = AppointmentBookingRules.NormalizeStatusKey(lockedVisit.Status);
                if (visitStatus is "CANCELLED" or "NO_SHOW")
                {
                    throw new ConflictException($"Lượt khám đang ở trạng thái '{visitStatus}', không thể lấy số nhà thuốc.");
                }

                var trackedVisitQueues = await _db.QueueEntries
                    .Where(q =>
                        q.VisitId == visitId ||
                        (lockedVisit.AppointmentId.HasValue && q.AppointmentId == lockedVisit.AppointmentId.Value))
                    .OrderByDescending(q => q.Id)
                    .ToListAsync(ct);

                PatientSingleActiveQueueGuard.CloseStalePriorDayQueuesOnVisit(trackedVisitQueues, todayLocal, nowLocal);

                var policy = await CrossDayVisitPolicyReader.LoadAsync(_db, hospitalId, nowUtc, ct);

                if (postActor.IsPatient)
                {
                    if (!VisitPresenceEvaluator.IsPresentToday(lockedVisit, trackedVisitQueues, todayLocal, nowLocal, policy.PresenceTtlMinutes))
                    {
                        throw new ConflictException(PatientNotPresentMessage);
                    }
                }
                else
                {
                    // QE-19: Confirming presence for pharmacy pickup NEVER reopens Visit.Status == COMPLETED or MedicalRecord!
                    VisitPresenceEvaluator.StampPresentToday(lockedVisit, todayLocal, nowLocal);
                }

                if (HasActiveClinicalSession(trackedVisitQueues, todayLocal))
                {
                    throw new ConflictException(ClinicalHandoffRequiredMessage);
                }

                await PatientSingleActiveQueueGuard.EnsureNoOtherActiveServingQueueAsync(
                    _db,
                    lockedVisit.PatientId,
                    hospitalId,
                    todayLocal,
                    excludeQueueEntryId: null,
                    ct: ct);

                var clearance = await _settlementReader.CheckPharmacyVisitClearanceAsync(visitId, ct);
                if (!clearance.IsUnlocked)
                {
                    throw new ConflictException(
                        clearance.Message ?? "[UNPAID_PHARMACY_CLEARANCE] Lượt khám chưa hoàn tất thanh toán các khoản phí bắt buộc trước khi nhận thuốc.");
                }

                var pharmacySp = ResolveOperationalPharmacyServicePoint(
                    servicePointsById.Values,
                    roomsById,
                    departmentsById,
                    hospitalId,
                    request.ServicePointId)
                    ?? throw new ConflictException(NoActivePharmacyPointMessage);

                var maxQueueNumber = await _db.QueueEntries.AsNoTracking()
                    .Where(q =>
                        q.QueueDate == todayLocal &&
                        (q.ServicePointId == pharmacySp.Id || q.CurrentServicePointId == pharmacySp.Id))
                    .MaxAsync(q => (int?)q.QueueNumber, ct);
                var nextQueueNumber = (maxQueueNumber ?? 0) + 1;

                var derivedPriority = ResolveServerSidePriority(trackedVisitQueues);

                var newEntry = new QueueEntry
                {
                    VisitId = visitId,
                    AppointmentId = null,
                    DiagnosticOrderId = null,
                    PrescriptionId = targetRx.Id,
                    ServicePointId = pharmacySp.Id,
                    CurrentServicePointId = pharmacySp.Id,
                    QueueDate = todayLocal,
                    QueueNumber = nextQueueNumber,
                    QueueType = QueueEnrollmentTargets.Pharmacy,
                    Status = "WAITING",
                    PresenceStatus = "PRESENT",
                    ArrivedAt = nowLocal,
                    ReadyAt = nowLocal,
                    ArrivalClass = "WALK_IN",
                    ManualPriority = derivedPriority,
                    IdempotencyKey = idempotencyKey,
                    StateVersion = 1,
                };

                _db.QueueEntries.Add(newEntry);
                await _db.SaveChangesAsync(ct);

                _db.AuditLogs.Add(new AuditLog
                {
                    ActorUserId = actorUserId,
                    HospitalId = hospitalId,
                    Action = QueueEnrolledAuditAction,
                    EntityType = "QUEUE_ENTRY",
                    EntityId = newEntry.Id.ToString(CultureInfo.InvariantCulture),
                    Reason = $"Lấy số hàng đợi nhà thuốc cho đơn thuốc {targetRx.PrescriptionCode ?? $"#{targetRx.Id}"}",
                    ChangeData = JsonSerializer.Serialize(new
                    {
                        queueEntryId = newEntry.Id,
                        visitId,
                        visitCode = lockedVisit.VisitCode,
                        visitStatus = lockedVisit.Status,
                        target = QueueEnrollmentTargets.Pharmacy,
                        prescriptionId = targetRx.Id,
                        prescriptionCode = targetRx.PrescriptionCode,
                        servicePointId = pharmacySp.Id,
                        queueNumber = nextQueueNumber,
                        actorRole = postActor.RoleLabel,
                        presenceSessionNo = lockedVisit.PresenceSessionNo,
                        idempotencyKey,
                    }, AuditJsonOptions),
                    IpAddress = QueueEntryRequestParsing.ParseIpAddress(ipAddress),
                    OccurredAt = nowUtc,
                });

                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                notifySpId = pharmacySp.Id;
                ticketDto = MapTicketDto(newEntry, servicePointsById, roomsById, isIdempotentReplay: false);
            }
            catch (DbUpdateException ex) when (PostgresConcurrencyException.IsUniqueViolation(ex))
            {
                await SafeRollbackAsync(tx, ct);
                if (_db is DbContext dbCtx)
                {
                    dbCtx.ChangeTracker.Clear();
                }

                if (idempotencyKey is not null)
                {
                    var racedByKey = await _db.QueueEntries.AsNoTracking()
                        .FirstOrDefaultAsync(q => q.IdempotencyKey == idempotencyKey, ct);
                    if (racedByKey is not null)
                    {
                        if (racedByKey.VisitId != visitId ||
                            !string.Equals(racedByKey.QueueType?.Trim(), QueueEnrollmentTargets.Pharmacy, StringComparison.OrdinalIgnoreCase))
                        {
                            throw new ConflictException(IdempotencyMismatchMessage);
                        }

                        var replaySps = await _db.QueueServicePoints.AsNoTracking()
                            .Where(sp => sp.HospitalId == hospitalId)
                            .ToDictionaryAsync(sp => sp.Id, ct);
                        var replayRooms = await _db.HospitalRooms.AsNoTracking()
                            .Where(r => r.HospitalId == hospitalId)
                            .ToDictionaryAsync(r => r.Id, ct);
                        return MapTicketDto(racedByKey, replaySps, replayRooms, isIdempotentReplay: true);
                    }
                }

                throw new ConflictException(PatientSingleActiveQueueGuard.ActiveQueueAlreadyExistsMessage);
            }
            catch (Exception ex) when (PostgresConcurrencyException.IsDeadlockOrSerializationFailure(ex))
            {
                await SafeRollbackAsync(tx, ct);
                if (_db is DbContext dbCtx)
                {
                    dbCtx.ChangeTracker.Clear();
                }

                throw new ConflictException("Yêu cầu lấy số nhà thuốc đang có xung đột đồng thời, vui lòng thử lại.");
            }
        }

        await _realtimePublisher.TryPublishQueueChangedSafeAsync(
            new QueueChangedNotification(HospitalId: hospitalId, ServicePointId: notifySpId),
            ct);

        return ticketDto;
    }

    public async Task<LeaveQueueEntryResponseDto> LeaveQueueAsync(
        long actorUserId,
        string queueEntryIdOrCode,
        LeaveQueueEntryRequestDto? request = null,
        string? ipAddress = null,
        CancellationToken ct = default)
    {
        var entryId = ParseQueueEntryId(queueEntryIdOrCode);
        if (request?.ExpectedStateVersion is < 1)
        {
            throw new ValidationException("ExpectedStateVersion phải lớn hơn hoặc bằng 1.");
        }

        var reason = ValidateOptionalText(request?.Reason, "Lý do rời hàng đợi", 500);

        await VisitPaymentAccess.RequireActiveUserAsync(_db, actorUserId, _timeProvider, ct);
        var (preRoleIds, preRoleCodes) = await VisitPaymentAccess.LoadActorRolesAsync(_db, actorUserId, ct);

        var preEntry = await _db.QueueEntries.AsNoTracking()
            .FirstOrDefaultAsync(q => q.Id == entryId, ct)
            ?? throw new NotFoundException("QueueEntry", queueEntryIdOrCode);

        var preVisit = preEntry.VisitId.HasValue
            ? await _db.Visits.AsNoTracking().FirstOrDefaultAsync(v => v.Id == preEntry.VisitId.Value, ct)
            : null;
        if (preVisit is null)
        {
            throw new ConflictException("Lượt xếp hàng này không gắn với lượt khám hoạt động.");
        }

        var preActor = await AuthorizeLeaveActorAsync(actorUserId, preRoleIds, preRoleCodes, preEntry, preVisit, ct);
        var hospitalId = preActor.HospitalId;

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var nowLocal = HospitalClock.GetNow(_timeProvider);
        var todayLocal = DateOnly.FromDateTime(nowLocal);

        LeaveQueueEntryResponseDto response;
        long? notifySpId = null;

        await using (var tx = await _db.BeginTransactionAsync(ct))
        {
            try
            {
                // ADR-0061 lock order: 02 patients -> 09 visits -> 10 queue_service_points -> 11 queue_entries -> 12 diagnostic_orders / 13 prescriptions
                if (preVisit.PatientId is long pid && pid > 0)
                {
                    await _db.TryLockPatientForUpdateAsync(pid, ct);
                }

                await _db.TryLockVisitForUpdateAsync(preVisit.Id, ct);

                var spIdsToLock = new[] { preEntry.ServicePointId, preEntry.CurrentServicePointId }
                    .Where(id => id.HasValue && id.Value > 0)
                    .Select(id => id!.Value)
                    .Distinct()
                    .OrderBy(id => id)
                    .ToArray();
                if (spIdsToLock.Length > 0)
                {
                    await _db.TryLockServicePointsForUpdateAsync(spIdsToLock, ct);
                }

                await _db.TryLockQueueEntriesForUpdateAsync([entryId], ct);

                if (preEntry.DiagnosticOrderId is long diagOrderId && diagOrderId > 0)
                {
                    await _db.TryLockDiagnosticOrdersForUpdateAsync([diagOrderId], ct);
                }

                if (preEntry.PrescriptionId is long rxId && rxId > 0)
                {
                    await _db.TryLockPrescriptionForUpdateAsync(rxId, ct);
                }

                if (_db is DbContext trackerCtx)
                {
                    trackerCtx.ChangeTracker.Clear();
                }

                var lockedEntry = await _db.QueueEntries.FirstOrDefaultAsync(q => q.Id == entryId, ct)
                    ?? throw new NotFoundException("QueueEntry", queueEntryIdOrCode);
                var lockedVisit = await _db.Visits.FirstOrDefaultAsync(v => v.Id == preVisit.Id, ct)
                    ?? throw new NotFoundException(VisitPaymentAccess.VisitNotFoundMessage);

                await VisitPaymentAccess.RequireActiveUserAsync(_db, actorUserId, _timeProvider, ct);
                var (postRoleIds, postRoleCodes) = await VisitPaymentAccess.LoadActorRolesAsync(_db, actorUserId, ct);
                var postActor = await AuthorizeLeaveActorAsync(actorUserId, postRoleIds, postRoleCodes, lockedEntry, lockedVisit, ct);
                hospitalId = postActor.HospitalId;

                var prevStatus = AppointmentBookingRules.NormalizeStatusKey(lockedEntry.Status);
                if (prevStatus is "IN_PROGRESS" or "IN_CONSULTATION" or "SERVING")
                {
                    throw new ConflictException(CannotLeaveInProgressMessage);
                }

                if (prevStatus is "COMPLETED" or "DONE" or "CANCELLED" or "DEFERRED" or "NO_SHOW")
                {
                    throw new ConflictException($"Lượt xếp hàng đã đóng ({prevStatus}), không thể rời hàng đợi.");
                }

                if (prevStatus == "CALLED" && postActor.IsPatient)
                {
                    throw new ConflictException(
                        "[CANNOT_LEAVE_CALLED_QUEUE_SELF] Lượt xếp hàng của bạn đang được gọi vào phòng (CALLED). Vui lòng liên hệ nhân viên tại quầy/phòng nếu cần hủy lượt.");
                }

                var prevVersion = lockedEntry.StateVersion is > 0 ? lockedEntry.StateVersion.Value : 1;
                if (request?.ExpectedStateVersion.HasValue == true && request.ExpectedStateVersion.Value != prevVersion)
                {
                    throw new ConflictException("[STATE_VERSION_MISMATCH] Trạng thái hàng đợi đã thay đổi, vui lòng tải lại.");
                }

                var nextVersion = prevVersion + 1;
                lockedEntry.Status = "CANCELLED";
                lockedEntry.CompletedAt = nowLocal;
                lockedEntry.CalledAt = null;
                lockedEntry.PresenceStatus = "LEFT";
                lockedEntry.BlockedReason = $"[LEFT_QUEUE] {reason ?? "Rời hàng đợi theo yêu cầu"}";
                lockedEntry.StateVersion = nextVersion;

                if (!postActor.IsPatient && request?.PatientLeftHospital == true)
                {
                    // E2-R03: stamp LEFT only when staff actually confirms the patient left the
                    // hospital. A patient self-leaving a queue to switch CLS keeps PRESENT.
                    VisitPresenceEvaluator.StampDepartedOrDeferred(lockedVisit, todayLocal, isDeferred: false);
                }

                var queueType = string.IsNullOrWhiteSpace(lockedEntry.QueueType)
                    ? "EXAMINATION"
                    : lockedEntry.QueueType.Trim().ToUpperInvariant();
                var ticketNo = FormatTicketNo(queueType, lockedEntry.QueueNumber);

                _db.AuditLogs.Add(new AuditLog
                {
                    ActorUserId = actorUserId,
                    HospitalId = hospitalId,
                    Action = QueueLeftAuditAction,
                    EntityType = "QUEUE_ENTRY",
                    EntityId = lockedEntry.Id.ToString(CultureInfo.InvariantCulture),
                    Reason = reason ?? "Rời hàng đợi theo yêu cầu",
                    ChangeData = JsonSerializer.Serialize(new
                    {
                        queueEntryId = lockedEntry.Id,
                        visitId = lockedVisit.Id,
                        queueType,
                        previousStatus = prevStatus,
                        newStatus = "CANCELLED",
                        patientLeftHospital = !postActor.IsPatient && request?.PatientLeftHospital == true,
                        actorRole = postActor.RoleLabel,
                        stateVersion = nextVersion,
                    }, AuditJsonOptions),
                    IpAddress = QueueEntryRequestParsing.ParseIpAddress(ipAddress),
                    OccurredAt = nowUtc,
                });

                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                notifySpId = lockedEntry.CurrentServicePointId ?? lockedEntry.ServicePointId;
                response = new LeaveQueueEntryResponseDto(
                    QueueEntryId: lockedEntry.Id,
                    FormattedQueueEntryId: $"q-{lockedEntry.Id:D3}",
                    TicketNo: ticketNo,
                    QueueType: queueType,
                    PreviousStatus: prevStatus,
                    Status: "CANCELLED",
                    PresenceStatus: "LEFT",
                    VisitId: lockedVisit.Id,
                    VisitPresenceState: lockedVisit.PresenceState,
                    VisitPresenceSessionNo: lockedVisit.PresenceSessionNo,
                    StateVersion: nextVersion,
                    LeftAtUtc: nowUtc);
            }
            catch
            {
                await SafeRollbackAsync(tx, ct);
                throw;
            }
        }

        await _realtimePublisher.TryPublishQueueChangedSafeAsync(
            new QueueChangedNotification(HospitalId: hospitalId, ServicePointId: notifySpId),
            ct);

        return response;
    }

    public Task<VisitQueueEnrollmentTicketDto> CallPharmacyQueueEntryAsync(
        long actorUserId,
        string queueEntryIdOrCode,
        PharmacyQueueActionRequestDto? request = null,
        string? ipAddress = null,
        CancellationToken ct = default)
        => TransitionPharmacyQueueEntryAsync(
            actorUserId,
            queueEntryIdOrCode,
            targetStatus: "CALLED",
            request?.Reason,
            request?.ExpectedStateVersion,
            ipAddress,
            ct);

    public Task<VisitQueueEnrollmentTicketDto> UpdatePharmacyQueueEntryStatusAsync(
        long actorUserId,
        string queueEntryIdOrCode,
        PharmacyQueueActionRequestDto request,
        string? ipAddress = null,
        CancellationToken ct = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.TargetStatus))
        {
            throw new ValidationException("TargetStatus là bắt buộc khi cập nhật hàng đợi nhà thuốc.");
        }

        var normalized = AppointmentBookingRules.NormalizeStatusKey(request.TargetStatus);
        if (normalized is not ("READY" or "CALLED" or "IN_PROGRESS" or "SKIPPED" or "CANCELLED"))
        {
            throw new ValidationException(
                "TargetStatus của hàng đợi nhà thuốc chỉ chấp nhận READY, CALLED, IN_PROGRESS, SKIPPED hoặc CANCELLED (COMPLETED chỉ được tự động cập nhật khi xác nhận xuất kho cấp phát thuốc).");
        }

        return TransitionPharmacyQueueEntryAsync(
            actorUserId,
            queueEntryIdOrCode,
            normalized,
            request.Reason,
            request.ExpectedStateVersion,
            ipAddress,
            ct);
    }

    private async Task<VisitQueueEnrollmentTicketDto> TransitionPharmacyQueueEntryAsync(
        long actorUserId,
        string queueEntryIdOrCode,
        string targetStatus,
        string? rawReason,
        int? expectedStateVersion,
        string? ipAddress,
        CancellationToken ct)
    {
        var entryId = ParseQueueEntryId(queueEntryIdOrCode);
        if (expectedStateVersion is < 1)
        {
            throw new ValidationException("ExpectedStateVersion phải lớn hơn hoặc bằng 1.");
        }

        var reason = ValidateOptionalText(rawReason, "Ghi chú hàng đợi nhà thuốc", 500);
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var nowLocal = HospitalClock.GetNow(_timeProvider);
        var todayLocal = DateOnly.FromDateTime(nowLocal);

        var (staff, hospitalId) = await RequirePharmacyDepartmentStaffAsync(actorUserId, ct);

        var preEntry = await _db.QueueEntries.AsNoTracking()
            .FirstOrDefaultAsync(q => q.Id == entryId, ct)
            ?? throw new NotFoundException("QueueEntry", queueEntryIdOrCode);

        if (!string.Equals(preEntry.QueueType?.Trim(), QueueEnrollmentTargets.Pharmacy, StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException("Lượt xếp hàng này không thuộc hàng đợi nhà thuốc (PHARMACY).");
        }

        var preVisit = preEntry.VisitId.HasValue
            ? await _db.Visits.AsNoTracking().FirstOrDefaultAsync(v => v.Id == preEntry.VisitId.Value, ct)
            : null;
        if (preVisit is null)
        {
            throw new ConflictException("Lượt xếp hàng nhà thuốc không gắn với lượt khám hợp lệ.");
        }

        VisitQueueEnrollmentTicketDto updatedDto;
        long? notifySpId = null;

        await using (var tx = await _db.BeginTransactionAsync(ct))
        {
            try
            {
                if (preVisit.PatientId is long pid && pid > 0)
                {
                    await _db.TryLockPatientForUpdateAsync(pid, ct);
                }

                await _db.TryLockVisitForUpdateAsync(preVisit.Id, ct);

                var spIds = new[] { preEntry.ServicePointId, preEntry.CurrentServicePointId }
                    .Where(id => id.HasValue && id.Value > 0)
                    .Select(id => id!.Value)
                    .Distinct()
                    .OrderBy(id => id)
                    .ToArray();
                if (spIds.Length > 0)
                {
                    await _db.TryLockServicePointsForUpdateAsync(spIds, ct);
                }

                await _db.TryLockQueueEntriesForUpdateAsync([entryId], ct);

                if (preEntry.PrescriptionId is long rxId && rxId > 0)
                {
                    await _db.TryLockPrescriptionForUpdateAsync(rxId, ct);
                }

                if (_db is DbContext trackerCtx)
                {
                    trackerCtx.ChangeTracker.Clear();
                }

                (staff, hospitalId) = await RequirePharmacyDepartmentStaffAsync(actorUserId, ct);

                var lockedEntry = await _db.QueueEntries.FirstOrDefaultAsync(q => q.Id == entryId, ct)
                    ?? throw new NotFoundException("QueueEntry", queueEntryIdOrCode);
                var lockedVisit = await _db.Visits.AsNoTracking().FirstOrDefaultAsync(v => v.Id == preVisit.Id, ct)
                    ?? throw new NotFoundException(VisitPaymentAccess.VisitNotFoundMessage);

                var servicePointsById = await _db.QueueServicePoints.AsNoTracking()
                    .Where(sp => sp.HospitalId == hospitalId)
                    .ToDictionaryAsync(sp => sp.Id, ct);
                var roomsById = await _db.HospitalRooms.AsNoTracking()
                    .Where(r => r.HospitalId == hospitalId)
                    .ToDictionaryAsync(r => r.Id, ct);

                var effSpId = lockedEntry.CurrentServicePointId ?? lockedEntry.ServicePointId;
                if (!effSpId.HasValue || !servicePointsById.TryGetValue(effSpId.Value, out var sp) ||
                    !sp.RoomId.HasValue || !roomsById.TryGetValue(sp.RoomId.Value, out var spRoom) ||
                    spRoom.DepartmentId != staff.DepartmentId)
                {
                    throw new ForbiddenException(DiagnosticDepartmentScopeGuard.DepartmentScopeForbiddenMessage);
                }

                var prevStatus = AppointmentBookingRules.NormalizeStatusKey(lockedEntry.Status);
                if (prevStatus is "COMPLETED" or "DONE" or "CANCELLED" or "DEFERRED")
                {
                    throw new ConflictException($"Lượt xếp hàng nhà thuốc đã đóng ({prevStatus}).");
                }

                var prevVersion = lockedEntry.StateVersion is > 0 ? lockedEntry.StateVersion.Value : 1;
                if (expectedStateVersion.HasValue && expectedStateVersion.Value != prevVersion)
                {
                    throw new ConflictException("[STATE_VERSION_MISMATCH] Dữ liệu hàng đợi nhà thuốc đã thay đổi, vui lòng tải lại.");
                }

                if (!PatientSingleActiveQueueGuard.IsActiveServingQueueEntry(lockedEntry) &&
                    targetStatus is "READY" or "CALLED" or "IN_PROGRESS")
                {
                    await PatientSingleActiveQueueGuard.EnsureNoOtherActiveServingQueueAsync(
                        _db,
                        lockedVisit.PatientId,
                        hospitalId,
                        todayLocal,
                        excludeQueueEntryId: lockedEntry.Id,
                        ct: ct);
                }

                if (targetStatus is "CALLED" or "IN_PROGRESS")
                {
                    var busyOther = await _db.QueueEntries.AsNoTracking()
                        .AnyAsync(q =>
                            q.Id != lockedEntry.Id &&
                            q.QueueDate == todayLocal &&
                            (q.CurrentServicePointId == effSpId.Value || q.ServicePointId == effSpId.Value) &&
                            q.Status != null &&
                            (q.Status.ToUpper() == "CALLED" || q.Status.ToUpper() == "IN_PROGRESS"), ct);
                    if (busyOther)
                    {
                        throw new ConflictException("Quầy phát thuốc đang phục vụ một bệnh nhân khác (CALLED/IN_PROGRESS).");
                    }
                }

                lockedEntry.Status = targetStatus;
                lockedEntry.StateVersion = prevVersion + 1;
                switch (targetStatus)
                {
                    case "READY":
                        lockedEntry.PresenceStatus = "PRESENT";
                        lockedEntry.ReadyAt = nowLocal;
                        lockedEntry.BlockedReason = null;
                        break;
                    case "CALLED":
                        lockedEntry.PresenceStatus = "PRESENT";
                        lockedEntry.CalledAt = nowLocal;
                        lockedEntry.BlockedReason = null;
                        break;
                    case "IN_PROGRESS":
                        lockedEntry.PresenceStatus = "PRESENT";
                        lockedEntry.CalledAt ??= nowLocal;
                        lockedEntry.StartedAt ??= nowLocal;
                        lockedEntry.BlockedReason = null;
                        break;
                    case "SKIPPED":
                        lockedEntry.PresenceStatus = "LEFT";
                        lockedEntry.BlockedReason = reason ?? "Bệnh nhân vắng mặt khi gọi tên tại quầy thuốc.";
                        break;
                    case "CANCELLED":
                        lockedEntry.PresenceStatus = "LEFT";
                        lockedEntry.CompletedAt = nowLocal;
                        lockedEntry.BlockedReason = reason ?? "Hủy lượt chờ nhận thuốc.";
                        break;
                }

                _db.AuditLogs.Add(new AuditLog
                {
                    ActorUserId = actorUserId,
                    HospitalId = hospitalId,
                    Action = PharmacyQueueUpdatedAuditAction,
                    EntityType = "QUEUE_ENTRY",
                    EntityId = lockedEntry.Id.ToString(CultureInfo.InvariantCulture),
                    Reason = reason ?? $"Chuyển trạng thái hàng đợi nhà thuốc sang {targetStatus}",
                    ChangeData = JsonSerializer.Serialize(new
                    {
                        queueEntryId = lockedEntry.Id,
                        visitId = lockedVisit.Id,
                        prescriptionId = lockedEntry.PrescriptionId,
                        previousStatus = prevStatus,
                        targetStatus,
                        stateVersion = lockedEntry.StateVersion,
                    }, AuditJsonOptions),
                    IpAddress = QueueEntryRequestParsing.ParseIpAddress(ipAddress),
                    OccurredAt = nowUtc,
                });

                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                notifySpId = effSpId;
                updatedDto = MapTicketDto(lockedEntry, servicePointsById, roomsById, isIdempotentReplay: false);
            }
            catch
            {
                await SafeRollbackAsync(tx, ct);
                throw;
            }
        }

        await _realtimePublisher.TryPublishQueueChangedSafeAsync(
            new QueueChangedNotification(HospitalId: hospitalId, ServicePointId: notifySpId),
            ct);

        return updatedDto;
    }

    internal static VisitQueueEnrollmentTicketDto MapTicketDto(
        QueueEntry entry,
        IReadOnlyDictionary<long, QueueServicePoint> servicePointsById,
        IReadOnlyDictionary<long, HospitalRoom> roomsById,
        bool isIdempotentReplay)
    {
        var spId = entry.CurrentServicePointId ?? entry.ServicePointId;
        var sp = spId.HasValue && servicePointsById.TryGetValue(spId.Value, out var foundSp) ? foundSp : null;
        var room = sp?.RoomId.HasValue == true && roomsById.TryGetValue(sp.RoomId.Value, out var foundRoom) ? foundRoom : null;
        var queueType = string.IsNullOrWhiteSpace(entry.QueueType)
            ? "EXAMINATION"
            : entry.QueueType.Trim().ToUpperInvariant();
        var queueNumber = entry.QueueNumber ?? 0;

        return new VisitQueueEnrollmentTicketDto(
            QueueEntryId: entry.Id,
            FormattedQueueEntryId: $"q-{entry.Id:D3}",
            TicketNo: FormatTicketNo(queueType, entry.QueueNumber),
            QueueNumber: queueNumber,
            QueueDate: (entry.QueueDate ?? DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)))
                .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            QueueType: queueType,
            Status: AppointmentBookingRules.NormalizeStatusKey(entry.Status),
            PresenceStatus: string.IsNullOrWhiteSpace(entry.PresenceStatus)
                ? "PRESENT"
                : entry.PresenceStatus.Trim().ToUpperInvariant(),
            Priority: ClinicalQueueRules.NormalizeStoredPriority(entry.ManualPriority),
            ServicePointId: sp?.Id ?? spId,
            ServicePointName: sp?.Name,
            RoomId: room?.Id ?? sp?.RoomId,
            RoomCode: room?.RoomCode,
            RoomName: room?.Name,
            DiagnosticOrderId: entry.DiagnosticOrderId,
            PrescriptionId: entry.PrescriptionId,
            CoordinationPurpose: entry.CoordinationPurpose,
            StateVersion: entry.StateVersion is > 0 ? entry.StateVersion.Value : 1,
            IsIdempotentReplay: isIdempotentReplay);
    }

    internal static string FormatTicketNo(string queueType, int? queueNumber)
    {
        var num = queueNumber ?? 0;
        if (num <= 0)
        {
            return string.Empty;
        }

        return queueType switch
        {
            QueueEnrollmentTargets.Diagnostic => $"C{num:D3}",
            QueueEnrollmentTargets.Pharmacy => $"P{num:D3}",
            QueueEnrollmentTargets.ReturnConsultation => $"R{num:D3}",
            _ => $"A{num:D3}",
        };
    }

    internal static bool HasActiveClinicalSession(IReadOnlyList<QueueEntry> visitQueues, DateOnly todayLocal)
    {
        return visitQueues.Any(q =>
        {
            if (q.CompletedAt.HasValue)
            {
                return false;
            }

            if (q.QueueDate.HasValue && q.QueueDate.Value < todayLocal)
            {
                var priorStatus = AppointmentBookingRules.NormalizeStatusKey(q.Status);
                return priorStatus is "IN_PROGRESS" or "IN_CONSULTATION" or "SERVING" &&
                       ClinicalQueueRules.IsClinicalExaminationQueueType(q.QueueType);
            }

            if (!ClinicalQueueRules.IsClinicalExaminationQueueType(q.QueueType))
            {
                return false;
            }

            var status = AppointmentBookingRules.NormalizeStatusKey(q.Status);
            return status is "WAITING" or "READY" or "CALLED" or "IN_PROGRESS" or "IN_CONSULTATION" or "SERVING";
        });
    }

    private static string ResolveServerSidePriority(IReadOnlyList<QueueEntry> visitQueues)
    {
        var referenceClinical = visitQueues
            .Where(q => ClinicalQueueRules.IsClinicalExaminationQueueType(q.QueueType) &&
                        AppointmentBookingRules.NormalizeStatusKey(q.Status) != "CANCELLED")
            .OrderByDescending(q => q.Id)
            .FirstOrDefault();
        return ClinicalQueueRules.NormalizeStoredPriority(referenceClinical?.ManualPriority);
    }

    private static (bool CanEnroll, string? Code, string? Message) EvaluateDiagnosticItemBlock(
        Visit visit,
        string orderStatus,
        string selectionStatus,
        bool isExpired,
        bool isSettled,
        bool hasOperationalCompatiblePoint,
        bool clinicalHandoffCompleted,
        QueueEntry? activePatientQueue,
        bool isPatientActor,
        bool isPresentToday)
    {
        var visitStatus = AppointmentBookingRules.NormalizeStatusKey(visit.Status);
        if (visitStatus is "CANCELLED" or "NO_SHOW" or "COMPLETED" or "DONE")
        {
            return (false, "VISIT_NOT_ACTIVE", "Lượt khám đã đóng hoặc không còn hoạt động.");
        }

        if (orderStatus is not ("ORDERED" or "CONFIRMED"))
        {
            return (false, "DIAGNOSTIC_ORDER_NOT_ELIGIBLE", $"Chỉ định cận lâm sàng đang ở trạng thái '{orderStatus}'.");
        }

        if (selectionStatus != DiagnosticSelectionStatuses.Accepted)
        {
            return (false, "DIAGNOSTIC_ORDER_NOT_ELIGIBLE", $"Chỉ định cận lâm sàng đang ở trạng thái lựa chọn '{selectionStatus}'.");
        }

        if (isExpired)
        {
            return (false, "DIAGNOSTIC_ORDER_EXPIRED_DOCTOR_APPROVAL_REQUIRED", CrossDayVisitPolicyReader.ExpiredOrderDoctorApprovalRequiredMessage);
        }

        if (!isSettled)
        {
            return (false, "UNPAID_DIAGNOSTIC_FEE", "Chỉ định cận lâm sàng chưa hoàn tất thanh toán phí dịch vụ.");
        }

        if (!clinicalHandoffCompleted)
        {
            return (false, "CLINICAL_HANDOFF_REQUIRED", ClinicalHandoffRequiredMessage);
        }

        if (isPatientActor && !isPresentToday)
        {
            return (false, "PATIENT_NOT_PRESENT", PatientNotPresentMessage);
        }

        if (activePatientQueue is not null)
        {
            return (false, PatientSingleActiveQueueGuard.ActiveQueueAlreadyExistsCode, PatientSingleActiveQueueGuard.ActiveQueueAlreadyExistsMessage);
        }

        if (!hasOperationalCompatiblePoint)
        {
            return (false, DiagnosticServicePointResolver.NoActiveCompatiblePointCode, "Không có phòng hoặc điểm phục vụ cận lâm sàng tương thích đang hoạt động.");
        }

        return (true, null, null);
    }

    private static (bool CanEnroll, string? Code, string? Message) EvaluatePharmacyItemBlock(
        Visit visit,
        Prescription rx,
        string rxStatus,
        bool isSuperseded,
        bool hasCompletedDispense,
        bool isPaymentCleared,
        bool hasActivePharmacyPoint,
        bool clinicalHandoffCompleted,
        QueueEntry? activePatientQueue,
        bool isPatientActor,
        bool isPresentToday)
    {
        var visitStatus = AppointmentBookingRules.NormalizeStatusKey(visit.Status);
        if (visitStatus is "CANCELLED" or "NO_SHOW")
        {
            return (false, "VISIT_CANCELLED", "Lượt khám đã hủy hoặc vắng mặt.");
        }

        if (isSuperseded || hasCompletedDispense || rxStatus is "DRAFT" or "CANCELLED" or "DISPENSED")
        {
            return (false, "PRESCRIPTION_NOT_ELIGIBLE_FOR_PHARMACY_QUEUE", PrescriptionNotEligibleMessage);
        }

        var isHospitalFulfillment = string.Equals(rx.FulfillmentChoice?.Trim(), "HOSPITAL", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(rx.FulfillmentChoice?.Trim(), "HOSPITAL_PHARMACY", StringComparison.OrdinalIgnoreCase);
        if (!isHospitalFulfillment)
        {
            return (false, "PRESCRIPTION_NOT_ELIGIBLE_FOR_PHARMACY_QUEUE", "Đơn thuốc được lựa chọn mua ngoài viện, không xếp hàng tại nhà thuốc bệnh viện.");
        }

        if (!isPaymentCleared)
        {
            return (false, "UNPAID_PHARMACY_CLEARANCE", "Lượt khám còn khoản phí bắt buộc chưa thanh toán trước khi nhận thuốc.");
        }

        if (!clinicalHandoffCompleted)
        {
            return (false, "CLINICAL_HANDOFF_REQUIRED", ClinicalHandoffRequiredMessage);
        }

        if (isPatientActor && !isPresentToday)
        {
            return (false, "PATIENT_NOT_PRESENT", PatientNotPresentMessage);
        }

        if (activePatientQueue is not null)
        {
            return (false, PatientSingleActiveQueueGuard.ActiveQueueAlreadyExistsCode, PatientSingleActiveQueueGuard.ActiveQueueAlreadyExistsMessage);
        }

        if (!hasActivePharmacyPoint)
        {
            return (false, "NO_ACTIVE_PHARMACY_SERVICE_POINT", NoActivePharmacyPointMessage);
        }

        return (true, null, null);
    }

    private async Task<Prescription> ResolveTargetPrescriptionOrThrowAsync(
        IReadOnlyList<Prescription> visitPrescriptions,
        long? explicitPrescriptionId,
        CancellationToken ct)
    {
        var dispensedRxIds = new HashSet<long>();
        var rxIds = visitPrescriptions.Select(p => p.Id).ToList();
        if (rxIds.Count > 0)
        {
            var completedDispenses = await _db.MedicationDispenses.AsNoTracking()
                .Where(md => md.PrescriptionId.HasValue &&
                             rxIds.Contains(md.PrescriptionId.Value) &&
                             md.Status != null &&
                             (md.Status.ToUpper() == "DISPENSED" || md.Status.ToUpper() == "COMPLETED"))
                .Select(md => md.PrescriptionId!.Value)
                .ToListAsync(ct);
            foreach (var id in completedDispenses)
            {
                dispensedRxIds.Add(id);
            }
        }

        bool IsEligibleRx(Prescription rx)
        {
            var st = AppointmentBookingRules.NormalizeStatusKey(rx.Status);
            if (st is "CANCELLED" or "DRAFT" or "DISPENSED" || dispensedRxIds.Contains(rx.Id))
            {
                return false;
            }

            var isHospital = string.Equals(rx.FulfillmentChoice?.Trim(), "HOSPITAL", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(rx.FulfillmentChoice?.Trim(), "HOSPITAL_PHARMACY", StringComparison.OrdinalIgnoreCase);
            if (!isHospital)
            {
                return false;
            }

            var superseded = visitPrescriptions.Any(other =>
                other.Id != rx.Id &&
                other.ReplacesPrescriptionId == rx.Id &&
                AppointmentBookingRules.NormalizeStatusKey(other.Status) is "ISSUED" or "PREPARING" or "READY" or "DISPENSED");
            return !superseded;
        }

        if (explicitPrescriptionId.HasValue)
        {
            var found = visitPrescriptions.FirstOrDefault(p => p.Id == explicitPrescriptionId.Value)
                ?? throw new NotFoundException("Prescription", explicitPrescriptionId.Value);
            if (!IsEligibleRx(found))
            {
                throw new ConflictException(PrescriptionNotEligibleMessage);
            }

            return found;
        }

        var eligible = visitPrescriptions.Where(IsEligibleRx).ToList();
        if (eligible.Count == 0)
        {
            throw new ConflictException(PrescriptionNotEligibleMessage);
        }

        if (eligible.Count > 1)
        {
            throw new ValidationException("Lượt khám có nhiều đơn thuốc hiệu lực; vui lòng chọn mã đơn thuốc cụ thể (PrescriptionId).");
        }

        return eligible[0];
    }

    private static QueueServicePoint? ResolveOperationalPharmacyServicePoint(
        IEnumerable<QueueServicePoint> hospitalServicePoints,
        IReadOnlyDictionary<long, HospitalRoom> roomsById,
        IReadOnlyDictionary<long, Department> departmentsById,
        long hospitalId,
        long? requestedServicePointId)
    {
        bool IsOperationalPharmacyPoint(QueueServicePoint? sp)
        {
            if (sp is null || sp.IsActive != true || sp.HospitalId != hospitalId)
            {
                return false;
            }

            if (!string.Equals(sp.PointType?.Trim(), QueueEnrollmentTargets.Pharmacy, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!sp.RoomId.HasValue || !roomsById.TryGetValue(sp.RoomId.Value, out var room))
            {
                return false;
            }

            if (room.HospitalId != hospitalId ||
                !string.Equals(room.RoomType?.Trim(), QueueEnrollmentTargets.Pharmacy, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(room.Status?.Trim(), "ACTIVE", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!room.DepartmentId.HasValue ||
                !departmentsById.TryGetValue(room.DepartmentId.Value, out var dept) ||
                dept.HospitalId != hospitalId ||
                dept.IsActive != true)
            {
                return false;
            }

            return true;
        }

        if (requestedServicePointId.HasValue)
        {
            var explicitPoint = hospitalServicePoints.FirstOrDefault(sp => sp.Id == requestedServicePointId.Value);
            if (!IsOperationalPharmacyPoint(explicitPoint))
            {
                throw new ConflictException(
                    $"[INCOMPATIBLE_PHARMACY_SERVICE_POINT] Điểm phục vụ nhà thuốc #{requestedServicePointId.Value} không hợp lệ hoặc đang ngừng hoạt động.");
            }

            return explicitPoint;
        }

        return hospitalServicePoints
            .OrderBy(sp => sp.Id)
            .FirstOrDefault(IsOperationalPharmacyPoint);
    }

    private async Task<EnrollmentActorContext> AuthorizeContextReaderAsync(
        long actorUserId,
        IReadOnlySet<string> roleCodes,
        Visit? visit,
        CancellationToken ct)
    {
        var isStaffRole = roleCodes.Contains(RoleNames.Reception) ||
                          roleCodes.Contains(RoleNames.DiagnosticStaff) ||
                          roleCodes.Contains(RoleNames.PharmacyStaff) ||
                          roleCodes.Contains(RoleNames.HospitalAdmin);
        var isDoctor = roleCodes.Contains(RoleNames.Doctor);
        var isPatient = roleCodes.Contains(RoleNames.Patient);

        if (!isStaffRole && !isDoctor && isPatient)
        {
            var owned = await VisitPaymentAccess.LoadOwnedPatientIdsAsync(_db, actorUserId, _timeProvider, ct);
            if (visit is null || !owned.Contains(visit.PatientId ?? 0))
            {
                throw new NotFoundException(VisitPaymentAccess.VisitNotFoundMessage);
            }

            var hospitalId = await ResolveSingleHospitalIdAsync(visit, ct);
            return new EnrollmentActorContext(true, RoleNames.Patient, hospitalId, null);
        }

        if (visit is null)
        {
            throw new NotFoundException(VisitPaymentAccess.VisitNotFoundMessage);
        }

        var visitHospitalId = await ResolveSingleHospitalIdAsync(visit, ct);

        if (isStaffRole)
        {
            var staff = await RequireActiveHospitalStaffAsync(actorUserId, visitHospitalId, ct);
            var roleLabel = roleCodes.Contains(RoleNames.Reception)
                ? RoleNames.Reception
                : (roleCodes.Contains(RoleNames.DiagnosticStaff)
                    ? RoleNames.DiagnosticStaff
                    : (roleCodes.Contains(RoleNames.PharmacyStaff) ? RoleNames.PharmacyStaff : RoleNames.HospitalAdmin));
            return new EnrollmentActorContext(false, roleLabel, visitHospitalId, staff);
        }

        if (isDoctor)
        {
            var (staff, doctor) = await VisitPaymentAccess.RequireActiveDoctorStaffAsync(
                _db,
                actorUserId,
                "Bác sĩ không có quyền xem ngữ cảnh hàng đợi của lượt khám này.",
                ct);
            await VisitPaymentAccess.EnsureDoctorAssignedToVisitAsync(
                _db,
                doctor,
                visit,
                visitHospitalId,
                "Bác sĩ chỉ được xem ngữ cảnh hàng đợi của lượt khám được phân công.",
                ct);
            return new EnrollmentActorContext(false, RoleNames.Doctor, visitHospitalId, staff);
        }

        throw new ForbiddenException("Người dùng không có quyền xem ngữ cảnh hàng đợi của lượt khám này.");
    }

    private async Task<EnrollmentActorContext> AuthorizeDiagnosticEnrollmentActorAsync(
        long actorUserId,
        IReadOnlyCollection<long> roleIds,
        IReadOnlySet<string> roleCodes,
        Visit? visit,
        CancellationToken ct)
    {
        var isStaffEnrollRole = roleCodes.Contains(RoleNames.Reception) ||
                                roleCodes.Contains(RoleNames.DiagnosticStaff) ||
                                roleCodes.Contains(RoleNames.HospitalAdmin);
        var isPatient = roleCodes.Contains(RoleNames.Patient);

        if (!isStaffEnrollRole && isPatient &&
            !roleCodes.Contains(RoleNames.Doctor) &&
            !roleCodes.Contains(RoleNames.PharmacyStaff) &&
            !roleCodes.Contains(RoleNames.SystemAdmin))
        {
            var owned = await VisitPaymentAccess.LoadOwnedPatientIdsAsync(_db, actorUserId, _timeProvider, ct);
            if (visit is null || !owned.Contains(visit.PatientId ?? 0))
            {
                throw new NotFoundException(VisitPaymentAccess.VisitNotFoundMessage);
            }

            var hospitalId = await ResolveSingleHospitalIdAsync(visit, ct);
            return new EnrollmentActorContext(true, RoleNames.Patient, hospitalId, null);
        }

        if (!isStaffEnrollRole)
        {
            throw new ForbiddenException("Vai trò hiện tại không được phép lấy số hàng đợi cận lâm sàng.");
        }

        var permSet = await VisitPaymentAccess.LoadRolePermissionCodesAsync(_db, roleIds, ct);
        if (!permSet.Contains(PermissionCodes.QueueEnroll))
        {
            throw new ForbiddenException(QueueEnrollRequiredMessage);
        }

        if (visit is null)
        {
            throw new NotFoundException(VisitPaymentAccess.VisitNotFoundMessage);
        }

        var visitHospitalId = await ResolveSingleHospitalIdAsync(visit, ct);
        var staff = await RequireActiveHospitalStaffAsync(actorUserId, visitHospitalId, ct);

        if (roleCodes.Contains(RoleNames.DiagnosticStaff) && !roleCodes.Contains(RoleNames.Reception))
        {
            if (!staff.DepartmentId.HasValue || staff.DepartmentId.Value <= 0)
            {
                throw new ForbiddenException(DiagnosticDepartmentScopeGuard.DepartmentScopeForbiddenMessage);
            }
        }

        var roleLabel = roleCodes.Contains(RoleNames.Reception)
            ? RoleNames.Reception
            : (roleCodes.Contains(RoleNames.DiagnosticStaff) ? RoleNames.DiagnosticStaff : RoleNames.HospitalAdmin);

        return new EnrollmentActorContext(false, roleLabel, visitHospitalId, staff);
    }

    private async Task<EnrollmentActorContext> AuthorizePharmacyEnrollmentActorAsync(
        long actorUserId,
        IReadOnlyCollection<long> roleIds,
        IReadOnlySet<string> roleCodes,
        Visit? visit,
        CancellationToken ct)
    {
        var isStaffEnrollRole = roleCodes.Contains(RoleNames.Reception) ||
                                roleCodes.Contains(RoleNames.PharmacyStaff) ||
                                roleCodes.Contains(RoleNames.HospitalAdmin);
        var isPatient = roleCodes.Contains(RoleNames.Patient);

        if (!isStaffEnrollRole && isPatient &&
            !roleCodes.Contains(RoleNames.Doctor) &&
            !roleCodes.Contains(RoleNames.DiagnosticStaff) &&
            !roleCodes.Contains(RoleNames.SystemAdmin))
        {
            var owned = await VisitPaymentAccess.LoadOwnedPatientIdsAsync(_db, actorUserId, _timeProvider, ct);
            if (visit is null || !owned.Contains(visit.PatientId ?? 0))
            {
                throw new NotFoundException(VisitPaymentAccess.VisitNotFoundMessage);
            }

            var hospitalId = await ResolveSingleHospitalIdAsync(visit, ct);
            return new EnrollmentActorContext(true, RoleNames.Patient, hospitalId, null);
        }

        if (!isStaffEnrollRole)
        {
            throw new ForbiddenException("Vai trò hiện tại không được phép lấy số hàng đợi nhà thuốc.");
        }

        var permSet = await VisitPaymentAccess.LoadRolePermissionCodesAsync(_db, roleIds, ct);
        if (!permSet.Contains(PermissionCodes.QueueEnroll))
        {
            throw new ForbiddenException(QueueEnrollRequiredMessage);
        }

        if (visit is null)
        {
            throw new NotFoundException(VisitPaymentAccess.VisitNotFoundMessage);
        }

        var visitHospitalId = await ResolveSingleHospitalIdAsync(visit, ct);
        var staff = await RequireActiveHospitalStaffAsync(actorUserId, visitHospitalId, ct);

        if (roleCodes.Contains(RoleNames.PharmacyStaff) && !roleCodes.Contains(RoleNames.Reception))
        {
            await EnsureStaffBelongsToPharmacyDepartmentAsync(staff, visitHospitalId, ct);
        }

        var roleLabel = roleCodes.Contains(RoleNames.Reception)
            ? RoleNames.Reception
            : (roleCodes.Contains(RoleNames.PharmacyStaff) ? RoleNames.PharmacyStaff : RoleNames.HospitalAdmin);

        return new EnrollmentActorContext(false, roleLabel, visitHospitalId, staff);
    }

    private async Task<EnrollmentActorContext> AuthorizeLeaveActorAsync(
        long actorUserId,
        IReadOnlyCollection<long> roleIds,
        IReadOnlySet<string> roleCodes,
        QueueEntry entry,
        Visit visit,
        CancellationToken ct)
    {
        var queueType = string.IsNullOrWhiteSpace(entry.QueueType)
            ? "EXAMINATION"
            : entry.QueueType.Trim().ToUpperInvariant();

        var isReceptionOrAdmin = roleCodes.Contains(RoleNames.Reception) || roleCodes.Contains(RoleNames.HospitalAdmin);
        var isDiagStaff = roleCodes.Contains(RoleNames.DiagnosticStaff);
        var isPhaStaff = roleCodes.Contains(RoleNames.PharmacyStaff);
        var isPatient = roleCodes.Contains(RoleNames.Patient);

        if (!isReceptionOrAdmin && !isDiagStaff && !isPhaStaff && isPatient &&
            !roleCodes.Contains(RoleNames.Doctor) && !roleCodes.Contains(RoleNames.SystemAdmin))
        {
            var owned = await VisitPaymentAccess.LoadOwnedPatientIdsAsync(_db, actorUserId, _timeProvider, ct);
            if (!owned.Contains(visit.PatientId ?? 0))
            {
                throw new NotFoundException("QueueEntry", entry.Id);
            }

            var hospitalId = await ResolveSingleHospitalIdAsync(visit, ct);
            return new EnrollmentActorContext(true, RoleNames.Patient, hospitalId, null);
        }

        var visitHospitalId = await ResolveSingleHospitalIdAsync(visit, ct);
        var permSet = await VisitPaymentAccess.LoadRolePermissionCodesAsync(_db, roleIds, ct);
        var hasQueuePermission = permSet.Contains(PermissionCodes.QueueEnroll) || permSet.Contains(PermissionCodes.QueueManage);

        if (isReceptionOrAdmin)
        {
            if (!hasQueuePermission)
            {
                throw new ForbiddenException(QueueEnrollRequiredMessage);
            }

            var staff = await RequireActiveHospitalStaffAsync(actorUserId, visitHospitalId, ct);
            return new EnrollmentActorContext(
                false,
                roleCodes.Contains(RoleNames.Reception) ? RoleNames.Reception : RoleNames.HospitalAdmin,
                visitHospitalId,
                staff);
        }

        if (isDiagStaff && queueType == QueueEnrollmentTargets.Diagnostic)
        {
            if (!hasQueuePermission)
            {
                throw new ForbiddenException(QueueEnrollRequiredMessage);
            }

            var staff = await RequireActiveHospitalStaffAsync(actorUserId, visitHospitalId, ct);
            if (!staff.DepartmentId.HasValue || staff.DepartmentId.Value <= 0)
            {
                throw new ForbiddenException(DiagnosticDepartmentScopeGuard.DepartmentScopeForbiddenMessage);
            }

            return new EnrollmentActorContext(false, RoleNames.DiagnosticStaff, visitHospitalId, staff);
        }

        if (isPhaStaff && queueType == QueueEnrollmentTargets.Pharmacy)
        {
            if (!hasQueuePermission && !permSet.Contains(PermissionCodes.MedicineDispense))
            {
                throw new ForbiddenException(QueueEnrollRequiredMessage);
            }

            var staff = await RequireActiveHospitalStaffAsync(actorUserId, visitHospitalId, ct);
            await EnsureStaffBelongsToPharmacyDepartmentAsync(staff, visitHospitalId, ct);
            return new EnrollmentActorContext(false, RoleNames.PharmacyStaff, visitHospitalId, staff);
        }

        throw new ForbiddenException("Người dùng không có quyền thực hiện thao tác rời hàng đợi cho lượt xếp hàng này.");
    }

    private async Task<(Staff Staff, long HospitalId)> RequirePharmacyDepartmentStaffAsync(long actorUserId, CancellationToken ct)
    {
        await VisitPaymentAccess.RequireActiveUserAsync(_db, actorUserId, _timeProvider, ct);
        var (roleIds, roleCodes) = await VisitPaymentAccess.LoadActorRolesAsync(_db, actorUserId, ct);
        if (!roleCodes.Contains(RoleNames.PharmacyStaff))
        {
            throw new ForbiddenException("Chỉ nhân viên nhà thuốc (PHARMACY_STAFF) mới được điều phối hàng đợi quầy thuốc.");
        }

        var permSet = await VisitPaymentAccess.LoadRolePermissionCodesAsync(_db, roleIds, ct);
        if (!permSet.Contains(PermissionCodes.MedicineDispense) &&
            !permSet.Contains(PermissionCodes.QueueManage) &&
            !permSet.Contains(PermissionCodes.QueueEnroll))
        {
            throw new ForbiddenException("Nhân viên nhà thuốc chưa được cấp quyền điều phối hàng đợi hoặc cấp phát thuốc.");
        }

        var staff = await _db.Staff.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == actorUserId, ct)
            ?? throw new ForbiddenException(VisitPaymentAccess.InactiveStaffMessage);
        if (!string.Equals(staff.Status?.Trim(), "ACTIVE", StringComparison.OrdinalIgnoreCase) || !staff.HospitalId.HasValue)
        {
            throw new ForbiddenException(VisitPaymentAccess.InactiveStaffMessage);
        }

        var hospitalId = staff.HospitalId.Value;
        var hospital = await _db.Hospitals.AsNoTracking().FirstOrDefaultAsync(h => h.Id == hospitalId, ct)
            ?? throw new ForbiddenException(VisitPaymentAccess.InactiveHospitalMessage);
        if (hospital.IsActive != true)
        {
            throw new ForbiddenException(VisitPaymentAccess.InactiveHospitalMessage);
        }

        await EnsureStaffBelongsToPharmacyDepartmentAsync(staff, hospitalId, ct);
        return (staff, hospitalId);
    }

    private async Task EnsureStaffBelongsToPharmacyDepartmentAsync(Staff staff, long hospitalId, CancellationToken ct)
    {
        if (!staff.DepartmentId.HasValue || staff.DepartmentId.Value <= 0)
        {
            throw new ForbiddenException(DiagnosticDepartmentScopeGuard.DepartmentScopeForbiddenMessage);
        }

        var dept = await _db.Departments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == staff.DepartmentId.Value, ct);
        if (dept is null || dept.HospitalId != hospitalId || dept.IsActive != true)
        {
            throw new ForbiddenException(DiagnosticDepartmentScopeGuard.DepartmentScopeForbiddenMessage);
        }

        var phaDepartmentIds = await _db.Departments.AsNoTracking()
            .Where(d => d.HospitalId == hospitalId && d.IsActive == true && d.DepartmentCode != null && d.DepartmentCode.ToUpper() == "PHA")
            .Select(d => d.Id)
            .ToListAsync(ct);
        if (phaDepartmentIds.Count > 0 && !phaDepartmentIds.Contains(staff.DepartmentId.Value))
        {
            throw new ForbiddenException(DiagnosticDepartmentScopeGuard.DepartmentScopeForbiddenMessage);
        }
    }

    private async Task<Staff> RequireActiveHospitalStaffAsync(long actorUserId, long expectedHospitalId, CancellationToken ct)
    {
        var staff = await _db.Staff.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == actorUserId, ct)
            ?? throw new ForbiddenException(VisitPaymentAccess.InactiveStaffMessage);
        if (!string.Equals(staff.Status?.Trim(), "ACTIVE", StringComparison.OrdinalIgnoreCase) || !staff.HospitalId.HasValue)
        {
            throw new ForbiddenException(VisitPaymentAccess.InactiveStaffMessage);
        }

        if (staff.HospitalId.Value != expectedHospitalId)
        {
            throw new ForbiddenException("Nhân sự không thuộc cơ sở y tế của lượt khám này.");
        }

        var hospital = await _db.Hospitals.AsNoTracking().FirstOrDefaultAsync(h => h.Id == expectedHospitalId, ct)
            ?? throw new ForbiddenException(VisitPaymentAccess.InactiveHospitalMessage);
        if (hospital.IsActive != true)
        {
            throw new ForbiddenException(VisitPaymentAccess.InactiveHospitalMessage);
        }

        return staff;
    }

    private async Task<long> ResolveSingleHospitalIdAsync(Visit visit, CancellationToken ct)
    {
        var hospitalIds = await VisitHospitalResolver.CollectAsync(_db, visit, ct);
        if (hospitalIds.Count == 0)
        {
            throw new NotFoundException(VisitPaymentAccess.VisitNotFoundMessage);
        }

        if (hospitalIds.Count > 1)
        {
            throw new ConflictException(VisitPaymentAccess.HospitalConflictMessage);
        }

        return hospitalIds.First();
    }

    private async Task<HashSet<long>> LoadFinalizedDiagnosticOrderIdsAsync(IReadOnlyCollection<long> orderIds, CancellationToken ct)
    {
        if (orderIds.Count == 0)
        {
            return [];
        }

        var results = await _db.DiagnosticResults.AsNoTracking()
            .Where(r => r.DiagnosticOrderId.HasValue && orderIds.Contains(r.DiagnosticOrderId.Value))
            .ToListAsync(ct);
        if (results.Count == 0)
        {
            return [];
        }

        var resultIds = results.Select(r => r.Id).Distinct().ToList();
        var versions = await _db.DiagnosticResultVersions.AsNoTracking()
            .Where(v => v.DiagnosticResultId.HasValue && resultIds.Contains(v.DiagnosticResultId.Value))
            .ToListAsync(ct);
        var latestByResultId = versions
            .Where(v => v.DiagnosticResultId.HasValue)
            .GroupBy(v => v.DiagnosticResultId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(v => v.VersionNo ?? 0).ThenByDescending(v => v.Id).FirstOrDefault());

        var finalized = new HashSet<long>();
        foreach (var r in results)
        {
            latestByResultId.TryGetValue(r.Id, out var ver);
            if (r.DiagnosticOrderId.HasValue && DiagnosticOrderLifecycleHelper.IsResultRowFinalized(r, ver))
            {
                finalized.Add(r.DiagnosticOrderId.Value);
            }
        }

        return finalized;
    }

    private static long ParseQueueEntryId(string? idOrCode)
    {
        if (string.IsNullOrWhiteSpace(idOrCode))
        {
            throw new ValidationException("Mã lượt xếp hàng không hợp lệ.");
        }

        var trimmed = idOrCode.Trim();
        if (trimmed.StartsWith("q-", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[2..];
        }
        else if (trimmed.StartsWith('#'))
        {
            trimmed = trimmed[1..];
        }

        if (!long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
        {
            throw new ValidationException("Mã lượt xếp hàng không hợp lệ.");
        }

        return parsed;
    }

    private static string? NormalizeIdempotencyKey(string? rawKey)
    {
        if (rawKey is null)
        {
            return null;
        }

        var trimmed = rawKey.Trim();
        if (trimmed.Length == 0 || trimmed.Length > 128)
        {
            throw new ValidationException("Tham số IdempotencyKey phải từ 1 đến 128 ký tự.");
        }

        return trimmed;
    }

    private static string? ValidateOptionalText(string? raw, string label, int maxLen)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var trimmed = raw.Trim();
        if (trimmed.Length > maxLen || trimmed.Contains('<') || trimmed.Contains('>'))
        {
            throw new ValidationException($"{label} không hợp lệ (tối đa {maxLen} ký tự và không chứa HTML).");
        }

        return trimmed;
    }

    private static string FormatOrderCode(DiagnosticOrder order, DateOnly fallbackDate)
    {
        if (!string.IsNullOrWhiteSpace(order.OrderCode))
        {
            return order.OrderCode.Trim();
        }

        var datePart = order.OrderedAt.HasValue
            ? DateOnly.FromDateTime(order.OrderedAt.Value)
            : fallbackDate;
        return $"DIA-{datePart:yyyyMM}-{order.Id:D4}";
    }

    private static async Task SafeRollbackAsync(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx, CancellationToken ct)
    {
        try
        {
            await tx.RollbackAsync(ct);
        }
        catch
        {
        }
    }

    private sealed record EnrollmentActorContext(
        bool IsPatient,
        string RoleLabel,
        long HospitalId,
        Staff? Staff);
}
