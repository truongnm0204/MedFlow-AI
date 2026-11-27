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
using MedFlow.Domain.Constants;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.Application.Services.Visits;

/// <summary>
/// Stage E2 centralized Reception return-coordination service (ADR-0061 Part 2 / RC-01..RC-22).
/// Owns the RETURN_CONSULTATION ticket-issuance core: doctor eligibility ranking via
/// <c>ProjectedLoadCount</c>, canonical ADR-0059/0060 post-lock revalidation, idempotent
/// enrollment, and the patient-level single-active-serving-queue invariant.
/// NEVER shares a public API surface with <see cref="IVisitQueueEnrollmentService"/> (no circular DI).
/// </summary>
public sealed class ReturnCoordinationService : IReturnCoordinationService
{
    public const string ReturnEnrolledAuditAction = "QUEUE_ENROLLED";
    public const string VisitDoctorAssignedAuditAction = "VISIT_DOCTOR_ASSIGNED";
    public const string HospitalAdminExceptionAuditAction = "HOSPITAL_ADMIN_RETURN_COORDINATION_EXCEPTION";
    public const string ClinicalReviewCoordinatedAuditAction = "VISIT_CLINICAL_REVIEW_COORDINATED";

    public const string ReturnReceptionRequiredMessage =
        "[RETURN_ENROLLMENT_RECEPTION_REQUIRED] Chỉ Quầy Tiếp Đón (Reception) mới được phép điều phối bác sĩ và cấp số khám kết luận (RETURN_CONSULTATION).";

    public const string QueueEnrollRequiredMessage =
        "[QUEUE_ENROLL_REQUIRED] Tài khoản nhân sự chưa được cấp quyền đăng ký & điều phối lấy số hàng đợi (QUEUE_ENROLL).";

    public const string PatientNotPresentMessage =
        "[PATIENT_NOT_PRESENT] Bệnh nhân chưa được xác nhận có mặt tại bệnh viện hôm nay. Vui lòng đến Quầy Tiếp Đón để xác nhận hiện diện trước khi lấy số.";

    public const string IdempotencyMismatchMessage =
        "[IDEMPOTENCY_PAYLOAD_MISMATCH] Khóa idempotency đã được sử dụng cho một yêu cầu điều phối khám kết luận khác.";

    public const string NoEligibleDoctorMessage =
        "[NO_ELIGIBLE_DOCTOR] Hiện không có bác sĩ nào đủ điều kiện tiếp nhận khám kết luận / đánh giá lại. Vui lòng thử lại sau hoặc liên hệ Quản trị bệnh viện.";

    public const string DoctorCapacityChangedMessage =
        "[DOCTOR_CAPACITY_CHANGED] Tải lượng hoặc trạng thái của bác sĩ được chọn vừa thay đổi. Vui lòng tải lại ngữ cảnh điều phối và chọn lại.";

    public const string ReassignmentReasonRequiredMessage =
        "[REASSIGNMENT_REASON_REQUIRED] Vui lòng nhập lý do khi phân công bác sĩ khác với bác sĩ phụ trách hiện tại của lượt khám.";

    private static readonly JsonSerializerOptions AuditJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly HashSet<string> TerminalVisitStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "COMPLETED",
        "DONE",
        "CANCELLED",
        "NO_SHOW",
    };

    private static readonly HashSet<string> ClosedQueueStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "COMPLETED",
        "DONE",
        "CANCELLED",
        "DEFERRED",
        "NO_SHOW",
        "SKIPPED",
    };

    private static readonly HashSet<string> ActiveWaitingStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "WAITING",
        "READY",
        "CALLED",
    };

    private static readonly HashSet<string> ServingStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "CALLED",
        "IN_PROGRESS",
        "IN_CONSULTATION",
        "SERVING",
    };

    private static readonly HashSet<string> UpcomingAppointmentStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "BOOKED",
        "CONFIRMED",
        "CHECKED_IN",
    };

    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;
    private readonly IRealtimeNotificationPublisher _realtimePublisher;

    public ReturnCoordinationService(
        IApplicationDbContext db,
        TimeProvider? timeProvider = null,
        IRealtimeNotificationPublisher? realtimePublisher = null)
    {
        _db = db;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _realtimePublisher = realtimePublisher ?? NullRealtimeNotificationPublisher.Instance;
    }

    public async Task<ReturnCoordinationContextDto> GetReturnCoordinationContextAsync(
        long actorUserId,
        string visitIdOrCode,
        CancellationToken ct = default)
    {
        await VisitPaymentAccess.RequireActiveUserAsync(_db, actorUserId, _timeProvider, ct);
        var (_, roleCodes) = await VisitPaymentAccess.LoadActorRolesAsync(_db, actorUserId, ct);

        var parsed = VisitPaymentAccess.ParseVisitIdentifier(visitIdOrCode);
        var visit = await VisitPaymentAccess.FindVisitAsync(_db, parsed, ct)
            ?? throw new NotFoundException(VisitPaymentAccess.VisitNotFoundMessage);

        var hospitalId = await ResolveSingleHospitalIdAsync(visit, ct);
        await AuthorizeContextReaderAsync(actorUserId, roleCodes, visit, hospitalId, ct);

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var nowLocal = HospitalClock.GetNow(_timeProvider);
        var todayLocal = DateOnly.FromDateTime(nowLocal);

        var policy = await CrossDayVisitPolicyReader.LoadAsync(_db, hospitalId, nowUtc, ct);

        var visitQueues = await _db.QueueEntries.AsNoTracking()
            .Where(q =>
                q.VisitId == visit.Id ||
                (visit.AppointmentId.HasValue && q.AppointmentId == visit.AppointmentId.Value))
            .OrderByDescending(q => q.Id)
            .ToListAsync(ct);

        var isPresentToday = VisitPresenceEvaluator.IsPresentToday(
            visit,
            visitQueues,
            todayLocal,
            nowLocal,
            policy.PresenceTtlMinutes);

        var orders = await _db.DiagnosticOrders.AsNoTracking()
            .Where(o => o.VisitId == visit.Id)
            .OrderBy(o => o.Id)
            .ToListAsync(ct);

        var readiness = await ComputeReturnReadinessAsync(visit, orders, policy, nowLocal, ct);
        var hasClinicalReviewItems = HasClinicalReviewItems(orders, policy, nowLocal);

        var servicePointsById = await _db.QueueServicePoints.AsNoTracking()
            .Where(sp => sp.HospitalId == hospitalId)
            .ToDictionaryAsync(sp => sp.Id, ct);
        var roomsById = await _db.HospitalRooms.AsNoTracking()
            .Where(r => r.HospitalId == hospitalId)
            .ToDictionaryAsync(r => r.Id, ct);

        var activeQueue = visit.PatientId is > 0
            ? await PatientSingleActiveQueueGuard.FindActiveServingQueueForPatientAsync(
                _db,
                visit.PatientId.Value,
                hospitalId,
                todayLocal,
                ct: ct)
            : null;
        var activeTicketDto = activeQueue is not null
            ? VisitQueueEnrollmentService.MapTicketDto(activeQueue, servicePointsById, roomsById, isIdempotentReplay: false)
            : null;

        var candidates = await BuildDoctorCandidatesAsync(
            visit,
            hospitalId,
            policy,
            todayLocal,
            nowLocal,
            ct);

        var originalCandidate = visit.AssignedDoctorId is > 0
            ? candidates.FirstOrDefault(c => c.DoctorId == visit.AssignedDoctorId.Value)
            : null;
        var eligible = candidates.Where(c => c.IsEligible).ToList();

        var currentRoom = visit.ExaminationRoomId.HasValue && roomsById.TryGetValue(visit.ExaminationRoomId.Value, out var cr)
            ? cr
            : null;

        var assignedDoctor = visit.AssignedDoctorId is > 0
            ? await _db.Doctors.AsNoTracking()
                .Include(d => d.Staff)
                .FirstOrDefaultAsync(d => d.Id == visit.AssignedDoctorId.Value, ct)
            : null;

        return new ReturnCoordinationContextDto(
            VisitId: visit.Id,
            VisitCode: FormatVisitCode(visit, todayLocal),
            VisitStatus: AppointmentBookingRules.NormalizeStatusKey(visit.Status),
            PresenceState: VisitPresenceEvaluator.NormalizePresenceState(visit.PresenceState),
            IsPresentToday: isPresentToday,
            PresenceSessionNo: Math.Max(1, visit.PresenceSessionNo),
            IsReadyForReturnConclusion: readiness,
            ReadyForReturnAt: visit.ReadyForReturnAt,
            HasClinicalReviewItems: hasClinicalReviewItems,
            IsReturnOverdue: CrossDayVisitPolicyReader.IsReturnConsultationOverdue(
                visit, policy.ReturnConsultationWindowDays, nowLocal),
            AllowDoctorReassignment: policy.AllowDoctorReassignment,
            CurrentAssignedDoctorId: visit.AssignedDoctorId,
            CurrentAssignedDoctorCode: assignedDoctor?.DoctorCode,
            CurrentAssignedDoctorName: assignedDoctor is not null ? FormatDoctorDisplayName(assignedDoctor) : null,
            CurrentExaminationRoomId: visit.ExaminationRoomId,
            CurrentExaminationRoomCode: currentRoom?.RoomCode,
            ActiveQueueEntry: activeTicketDto,
            OriginalDoctor: originalCandidate,
            EligibleDoctors: eligible,
            GeneratedAtUtc: nowUtc);
    }

    public async Task<ReturnEnrollmentResponseDto> EnrollReturnConsultationAsync(
        long actorUserId,
        string visitIdOrCode,
        CreateReturnEnrollmentRequestDto? request = null,
        string? ipAddress = null,
        CancellationToken ct = default)
    {
        request ??= new CreateReturnEnrollmentRequestDto();

        var purpose = string.IsNullOrWhiteSpace(request.Purpose)
            ? QueueCoordinationPurposes.ResultConclusion
            : request.Purpose.Trim().ToUpperInvariant();
        if (!QueueCoordinationPurposes.IsValid(purpose))
        {
            throw new ValidationException(
                "Mục đích điều phối khám lại (Purpose) chỉ chấp nhận 'RESULT_CONCLUSION' hoặc 'CLINICAL_REVIEW'.");
        }

        if (request.DoctorId is <= 0)
        {
            throw new ValidationException("DoctorId phải lớn hơn 0.");
        }

        if (request.RoomId is <= 0)
        {
            throw new ValidationException("RoomId phải lớn hơn 0.");
        }

        if (request.ServicePointId is <= 0)
        {
            throw new ValidationException("ServicePointId phải lớn hơn 0.");
        }

        var idempotencyKey = NormalizeIdempotencyKey(request.IdempotencyKey);
        var reassignmentReason = ValidateOptionalText(request.ReassignmentReason, "Lý do phân công lại bác sĩ", 500);
        var notes = ValidateOptionalText(request.Notes, "Ghi chú điều phối", 500);

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var nowLocal = HospitalClock.GetNow(_timeProvider);
        var todayLocal = DateOnly.FromDateTime(nowLocal);

        await VisitPaymentAccess.RequireActiveUserAsync(_db, actorUserId, _timeProvider, ct);
        var (preRoleIds, preRoleCodes) = await VisitPaymentAccess.LoadActorRolesAsync(_db, actorUserId, ct);

        var parsed = VisitPaymentAccess.ParseVisitIdentifier(visitIdOrCode);
        var preVisit = await VisitPaymentAccess.FindVisitAsync(_db, parsed, ct)
            ?? throw new NotFoundException(VisitPaymentAccess.VisitNotFoundMessage);
        var visitId = preVisit.Id;
        var visitHospitalId = await ResolveSingleHospitalIdAsync(preVisit, ct);
        var visitCode = FormatVisitCode(preVisit, todayLocal);

        var prePolicy = await CrossDayVisitPolicyReader.LoadAsync(_db, visitHospitalId, nowUtc, ct);
        await AuthorizeEnrollmentActorAsync(
            actorUserId,
            preRoleIds,
            preRoleCodes,
            visitHospitalId,
            visitId,
            visitCode,
            prePolicy,
            ipAddress,
            nowUtc,
            writeExceptionAudit: false,
            ct);

        // Pre-collect lock ids strictly in canonical order (02 -> 04 -> 05 -> 06 -> 09 -> 10 -> 11 -> 12).
        var doctorIdsToLock = new SortedSet<long>();
        if (preVisit.AssignedDoctorId is > 0)
        {
            doctorIdsToLock.Add(preVisit.AssignedDoctorId.Value);
        }
        if (request.DoctorId is > 0)
        {
            doctorIdsToLock.Add(request.DoctorId.Value);
        }

        var orderServiceIds = await _db.DiagnosticOrders.AsNoTracking()
            .Where(o => o.VisitId == visitId && o.ServiceId.HasValue)
            .Select(o => o.ServiceId!.Value)
            .Distinct()
            .OrderBy(id => id)
            .ToArrayAsync(ct);

        var roomIdsToLock = new SortedSet<long>();
        if (preVisit.ExaminationRoomId is > 0)
        {
            roomIdsToLock.Add(preVisit.ExaminationRoomId.Value);
        }
        if (request.RoomId is > 0)
        {
            roomIdsToLock.Add(request.RoomId.Value);
        }
        var hospitalRoomIds = await _db.HospitalRooms.AsNoTracking()
            .Where(r => r.HospitalId == visitHospitalId)
            .Select(r => r.Id)
            .ToArrayAsync(ct);
        foreach (var rid in hospitalRoomIds)
        {
            roomIdsToLock.Add(rid);
        }

        var spIdsToLock = await _db.QueueServicePoints.AsNoTracking()
            .Where(sp => sp.HospitalId == visitHospitalId)
            .Select(sp => sp.Id)
            .ToArrayAsync(ct);
        var spLockSet = new SortedSet<long>(spIdsToLock.Where(id => id > 0));
        if (request.ServicePointId is > 0)
        {
            spLockSet.Add(request.ServicePointId.Value);
        }

        ReturnEnrollmentResponseDto response;
        long? notifySpId = null;

        await using (var tx = await _db.BeginTransactionAsync(ct))
        {
            try
            {
                // ADR-0059/0060/0061 canonical lock order:
                // 02 patients -> 04 services -> 05 doctors -> 06 hospital_rooms -> 09 visits -> 10 queue_service_points -> 11 queue_entries -> 12 diagnostic_orders
                if (preVisit.PatientId is > 0)
                {
                    await _db.TryLockPatientForUpdateAsync(preVisit.PatientId.Value, ct);
                }

                foreach (var sid in orderServiceIds)
                {
                    if (sid > 0)
                    {
                        await _db.TryLockServiceForUpdateAsync(sid, ct);
                    }
                }

                if (doctorIdsToLock.Count > 0)
                {
                    await _db.TryLockDoctorsForUpdateAsync(doctorIdsToLock.ToArray(), ct);
                }

                if (roomIdsToLock.Count > 0)
                {
                    await _db.TryLockRoomsForUpdateAsync(roomIdsToLock.ToArray(), ct);
                }

                await DiagnosticOrderLifecycleHelper.AcquireVisitAndDiagnosticLocksAsync(
                    _db,
                    [visitId],
                    extraServicePointIds: spLockSet.ToArray(),
                    extraQueueEntryIds: null,
                    extraDiagnosticOrderIds: null,
                    includeChargeAndPaymentLocks: false,
                    ct: ct);

                if (_db is DbContext trackerCtx)
                {
                    trackerCtx.ChangeTracker.Clear();
                }

                var visit = await _db.Visits.FirstOrDefaultAsync(v => v.Id == visitId, ct)
                    ?? throw new NotFoundException(VisitPaymentAccess.VisitNotFoundMessage);
                visitCode = FormatVisitCode(visit, todayLocal);

                // Post-lock revalidation (RC-11..14 / E2-R07): actor RBAC live, presence live,
                // readiness by purpose, shift/assignment/capacity re-checked on locked rows.
                await VisitPaymentAccess.RequireActiveUserAsync(_db, actorUserId, _timeProvider, ct);
                var (postRoleIds, postRoleCodes) = await VisitPaymentAccess.LoadActorRolesAsync(_db, actorUserId, ct);

                var policy = await CrossDayVisitPolicyReader.LoadAsync(_db, visitHospitalId, nowUtc, ct);
                var actor = await AuthorizeEnrollmentActorAsync(
                    actorUserId,
                    postRoleIds,
                    postRoleCodes,
                    visitHospitalId,
                    visitId,
                    visitCode,
                    policy,
                    ipAddress,
                    nowUtc,
                    writeExceptionAudit: true,
                    ct);

                var visitStatus = AppointmentBookingRules.NormalizeStatusKey(visit.Status);
                if (TerminalVisitStatuses.Contains(visitStatus))
                {
                    throw new ConflictException(
                        $"[VISIT_NOT_RESUMABLE] Lượt khám '{visitCode}' đã ở trạng thái kết thúc '{visitStatus}', không thể điều phối khám kết luận.");
                }

                var visitQueues = await _db.QueueEntries
                    .Where(q =>
                        q.VisitId == visitId ||
                        (visit.AppointmentId.HasValue && q.AppointmentId == visit.AppointmentId.Value))
                    .OrderBy(q => q.Id)
                    .ToListAsync(ct);

                PatientSingleActiveQueueGuard.CloseStalePriorDayQueuesOnVisit(visitQueues, todayLocal, nowLocal);

                var visitOrders = await _db.DiagnosticOrders
                    .Where(o => o.VisitId == visitId)
                    .OrderBy(o => o.Id)
                    .ToListAsync(ct);

                var isPresentToday = VisitPresenceEvaluator.IsPresentToday(
                    visit,
                    visitQueues,
                    todayLocal,
                    nowLocal,
                    policy.PresenceTtlMinutes);

                // §8.2: presence is never auto-confirmed by the core/adapter. When the patient is not
                // already present today, staff must send ConfirmPresent=true explicitly; otherwise the
                // coordination fails with PATIENT_NOT_PRESENT when the policy requires presence.
                if (!isPresentToday)
                {
                    if (request.ConfirmPresent == true)
                    {
                        VisitPresenceEvaluator.StampPresentToday(visit, todayLocal, nowLocal);
                    }
                    else if (policy.RequirePresenceForReturnQueue)
                    {
                        throw new ConflictException(PatientNotPresentMessage);
                    }
                }

                // A live RETURN_CONSULTATION ticket for the SAME visit today is the idempotent replay target,
                // not another active clinical session. Exclude it from the handoff guard; every other active
                // clinical execution (EXAM/DIAG/RETURN from another queue) still hard-blocks.
                var clinicalHandoffGuardQueues = visitQueues
                    .Where(q => !(
                        q.QueueDate == todayLocal &&
                        ClinicalQueueRules.ResolveQueueType(q.QueueType, null) == ClinicalQueueRules.QueueTypeReturnConsultation &&
                        !ClosedQueueStatuses.Contains(AppointmentBookingRules.NormalizeStatusKey(q.Status)) &&
                        !q.CompletedAt.HasValue))
                    .ToList();

                if (VisitQueueEnrollmentService.HasActiveClinicalSession(clinicalHandoffGuardQueues, todayLocal))
                {
                    throw new ConflictException(VisitQueueEnrollmentService.ClinicalHandoffRequiredMessage);
                }

                // Readiness by purpose (E2-R06): RESULT_CONCLUSION requires ready-for-return;
                // CLINICAL_REVIEW requires at least one overdue / REVIEW_REQUIRED order and
                // deliberately does NOT require ReadyForReturnAt.
                var hasClinicalReviewItems = HasClinicalReviewItems(visitOrders, policy, nowLocal);
                if (purpose == QueueCoordinationPurposes.ClinicalReview)
                {
                    if (!hasClinicalReviewItems)
                    {
                        throw new ConflictException(
                            "[CLINICAL_REVIEW_NOT_REQUIRED] Lượt khám hiện không có phiếu chỉ định cận lâm sàng quá hạn hoặc cần bác sĩ xem xét lại (REVIEW_REQUIRED).");
                    }
                }
                else
                {
                    var isReady = visit.ReadyForReturnAt.HasValue ||
                                  await DiagnosticOrderLifecycleHelper.EvaluateVisitReturnReadinessAsync(_db, visitId, ct: ct);
                    if (!isReady)
                    {
                        throw new ConflictException(
                            "[RETURN_CONSULTATION_NOT_READY] Lượt khám chưa có đầy đủ kết quả cận lâm sàng bắt buộc hoặc đang còn dịch vụ cận lâm sàng đang thực hiện/chờ kết quả.");
                    }
                }

                var hospitalRooms = await _db.HospitalRooms.AsNoTracking()
                    .Where(r => r.HospitalId == visitHospitalId)
                    .ToListAsync(ct);
                var roomsById = hospitalRooms.ToDictionary(r => r.Id);
                var servicePointsById = await _db.QueueServicePoints.AsNoTracking()
                    .Where(sp => sp.HospitalId == visitHospitalId)
                    .ToDictionaryAsync(sp => sp.Id, ct);
                var departmentsById = await _db.Departments.AsNoTracking()
                    .Where(d => d.HospitalId == visitHospitalId)
                    .ToDictionaryAsync(d => d.Id, ct);

                // Idempotency replay: same key + same payload replays current result;
                // same key + different payload -> 409 [IDEMPOTENCY_PAYLOAD_MISMATCH].
                var scopedKey = idempotencyKey is not null
                    ? BuildScopedQueueIdempotencyKey(
                        idempotencyKey,
                        purpose == QueueCoordinationPurposes.ClinicalReview ? "CREV" : "RET")
                    : null;

                if (scopedKey is not null)
                {
                    var existingByKey = await _db.QueueEntries.AsNoTracking()
                        .FirstOrDefaultAsync(q => q.IdempotencyKey == scopedKey, ct);
                    if (existingByKey is not null)
                    {
                        var sameVisit = existingByKey.VisitId == visitId;
                        var sameTarget = string.Equals(
                            existingByKey.QueueType?.Trim(),
                            ClinicalQueueRules.QueueTypeReturnConsultation,
                            StringComparison.OrdinalIgnoreCase);
                        var samePurpose = string.Equals(
                            existingByKey.CoordinationPurpose?.Trim(),
                            purpose,
                            StringComparison.OrdinalIgnoreCase);
                        if (!sameVisit || !sameTarget || !samePurpose)
                        {
                            throw new ConflictException(IdempotencyMismatchMessage);
                        }

                        if (request.DoctorId is > 0)
                        {
                            var keySpId = existingByKey.CurrentServicePointId ?? existingByKey.ServicePointId;
                            var keySp = keySpId.HasValue && servicePointsById.TryGetValue(keySpId.Value, out var ksp) ? ksp : null;
                            if (keySp?.DoctorId is long keyDoctorId && keyDoctorId != request.DoctorId.Value)
                            {
                                throw new ConflictException(IdempotencyMismatchMessage);
                            }
                        }

                        var replayDoctor = visit.AssignedDoctorId is > 0
                            ? await _db.Doctors.AsNoTracking().Include(d => d.Staff)
                                .FirstOrDefaultAsync(d => d.Id == visit.AssignedDoctorId.Value, ct)
                            : null;
                        var replayRoom = visit.ExaminationRoomId.HasValue &&
                                         roomsById.TryGetValue(visit.ExaminationRoomId.Value, out var rr)
                            ? rr
                            : null;

                        await tx.CommitAsync(ct);
                        return new ReturnEnrollmentResponseDto(
                            VisitId: visit.Id,
                            VisitCode: visitCode,
                            VisitStatus: AppointmentBookingRules.NormalizeStatusKey(visit.Status),
                            PresenceState: VisitPresenceEvaluator.NormalizePresenceState(visit.PresenceState),
                            PresenceSessionNo: Math.Max(1, visit.PresenceSessionNo),
                            AssignedDoctorId: visit.AssignedDoctorId ?? replayDoctor?.Id ?? 0,
                            AssignedDoctorCode: replayDoctor?.DoctorCode ?? string.Empty,
                            AssignedDoctorName: replayDoctor is not null ? FormatDoctorDisplayName(replayDoctor) : string.Empty,
                            DoctorWasReassigned: false,
                            PreviousAssignedDoctorId: null,
                            ExaminationRoomId: visit.ExaminationRoomId,
                            ExaminationRoomCode: replayRoom?.RoomCode,
                            ExaminationRoomName: replayRoom?.Name,
                            CoordinationPurpose: purpose,
                            Ticket: VisitQueueEnrollmentService.MapTicketDto(
                                existingByKey, servicePointsById, roomsById, isIdempotentReplay: true));
                    }
                }

                // One active RETURN_CONSULTATION ticket per visit: replay it if already present
                // (partial unique index ux_queue_entries_one_active_post_exam_per_visit backs this).
                // The same-visit live ticket is excluded from the patient-level single-active guard;
                // any OTHER active serving queue for this patient still hard-blocks enrollment (QE-07..08).
                var existingReturn = visitQueues
                    .Where(q =>
                        q.QueueDate == todayLocal &&
                        ClinicalQueueRules.ResolveQueueType(q.QueueType, null) == ClinicalQueueRules.QueueTypeReturnConsultation &&
                        !ClosedQueueStatuses.Contains(AppointmentBookingRules.NormalizeStatusKey(q.Status)) &&
                        !q.CompletedAt.HasValue)
                    .OrderByDescending(q => q.Id)
                    .FirstOrDefault();

                await PatientSingleActiveQueueGuard.EnsureNoOtherActiveServingQueueAsync(
                    _db,
                    visit.PatientId,
                    visitHospitalId,
                    todayLocal,
                    excludeQueueEntryId: existingReturn?.Id,
                    ct: ct);

                var candidates = await BuildDoctorCandidatesAsync(
                    visit,
                    visitHospitalId,
                    policy,
                    todayLocal,
                    nowLocal,
                    ct);

                var selected = SelectDoctor(visit, candidates, request.DoctorId);

                var doctorChanged = visit.AssignedDoctorId != selected.DoctorId;
                var explicitReassignment = request.DoctorId is > 0 && doctorChanged;
                if (doctorChanged && !policy.AllowDoctorReassignment)
                {
                    throw new ConflictException(
                        "[DOCTOR_REASSIGNMENT_DISABLED] Chính sách cơ sở y tế hiện không cho phép chuyển bác sĩ phụ trách khi điều phối khám lại.");
                }

                if (explicitReassignment && reassignmentReason is null)
                {
                    throw new ValidationException(ReassignmentReasonRequiredMessage);
                }

                // Post-lock capacity/shift revalidation (E2-R07 / RC-11..14 / RC-22): snapshot
                // metrics are decision-support only — an explicit pick that raced to capacity or
                // lost eligibility (shift ended, room deactivated, reassignment committed by a
                // concurrent reception) loses with 409 [DOCTOR_CAPACITY_CHANGED].
                var liveCandidate = candidates.FirstOrDefault(c => c.DoctorId == selected.DoctorId);
                if (request.DoctorId is > 0 && (liveCandidate is null || !liveCandidate.IsEligible))
                {
                    throw new ConflictException(DoctorCapacityChangedMessage);
                }

                if (liveCandidate is null || !liveCandidate.IsEligible)
                {
                    throw new ConflictException(NoEligibleDoctorMessage);
                }

                var previousDoctorId = visit.AssignedDoctorId;
                var previousRoomId = visit.ExaminationRoomId;

                var targetDoctor = await _db.Doctors.AsNoTracking()
                    .Include(d => d.Staff)
                    .FirstOrDefaultAsync(d => d.Id == selected.DoctorId, ct)
                    ?? throw new ConflictException(DoctorCapacityChangedMessage);

                var targetSp = ResolveClinicalServicePoint(
                    targetDoctor.Id,
                    visit,
                    request.ServicePointId,
                    liveCandidate.ServicePointId,
                    servicePointsById.Values,
                    roomsById);

                var newRoomId = request.RoomId is > 0
                    ? ValidateExplicitRoom(
                        request.RoomId.Value,
                        roomsById,
                        departmentsById,
                        visitHospitalId,
                        targetDoctor.Staff?.DepartmentId)
                    : (targetSp?.RoomId ?? liveCandidate.RoomId ?? visit.ExaminationRoomId);

                if (doctorChanged)
                {
                    visit.AssignedDoctorId = targetDoctor.Id;
                    visit.ExaminationRoomId = newRoomId;
                    visit.OverdueReturnResultSnapshot = null;
                }
                else if (newRoomId is > 0 && newRoomId != visit.ExaminationRoomId)
                {
                    visit.ExaminationRoomId = newRoomId;
                }

                QueueEntry ticket;
                var isReplay = false;
                if (existingReturn is not null)
                {
                    // Re-use the visit's live RETURN_CONSULTATION ticket: refresh presence,
                    // service point and purpose atomically inside this transaction.
                    ticket = existingReturn;
                    var ticketStatus = AppointmentBookingRules.NormalizeStatusKey(ticket.Status);
                    if (ticketStatus is not ("CALLED" or "IN_PROGRESS" or "IN_CONSULTATION" or "SERVING"))
                    {
                        ticket.Status = "READY";
                        ticket.CalledAt = null;
                        ticket.ReadyAt = nowLocal;
                    }

                    ticket.PresenceStatus = "PRESENT";
                    ticket.CoordinationPurpose = purpose;

                    if (scopedKey is not null)
                    {
                        ticket.IdempotencyKey ??= scopedKey;
                    }
                    if (targetSp is not null)
                    {
                        ticket.ServicePointId ??= targetSp.Id;
                        ticket.CurrentServicePointId = targetSp.Id;
                    }
                    ticket.StateVersion = (ticket.StateVersion is > 0 ? ticket.StateVersion.Value : 1) + 1;
                    isReplay = true;
                }
                else
                {
                    var maxToday = await _db.QueueEntries.AsNoTracking()
                        .Where(q =>
                            q.QueueDate == todayLocal &&
                            (targetSp != null
                                ? (q.ServicePointId == targetSp.Id || q.CurrentServicePointId == targetSp.Id)
                                : (q.ServicePointId == null && q.CurrentServicePointId == null)))
                        .MaxAsync(q => (int?)q.QueueNumber, ct);

                    var referenceClinical = visitQueues
                        .Where(q => ClinicalQueueRules.IsClinicalExaminationQueueType(q.QueueType))
                        .OrderByDescending(q => q.Id)
                        .FirstOrDefault(q => AppointmentBookingRules.NormalizeStatusKey(q.Status) != "CANCELLED");

                    ticket = new QueueEntry
                    {
                        VisitId = visit.Id,
                        AppointmentId = null,
                        DiagnosticOrderId = null,
                        PrescriptionId = null,
                        ServicePointId = targetSp?.Id,
                        CurrentServicePointId = targetSp?.Id,
                        QueueDate = todayLocal,
                        QueueNumber = (maxToday ?? 0) + 1,
                        QueueType = ClinicalQueueRules.QueueTypeReturnConsultation,
                        CoordinationPurpose = purpose,
                        Status = "READY",
                        PresenceStatus = "PRESENT",
                        ArrivedAt = nowLocal,
                        ReadyAt = nowLocal,
                        ArrivalClass = "WALK_IN",
                        ManualPriority = ClinicalQueueRules.NormalizeStoredPriority(referenceClinical?.ManualPriority),
                        IdempotencyKey = scopedKey,
                        StateVersion = 1,
                    };

                    _db.QueueEntries.Add(ticket);
                }

                if (purpose == QueueCoordinationPurposes.ResultConclusion)
                {
                    visit.ReadyForReturnAt ??= nowLocal;
                }
                visit.Status = "RETURN_CONSULTATION";
                visit.LastResumedAt = nowLocal;

                if (ticket.Id == 0)
                {
                    await _db.SaveChangesAsync(ct);
                }

                if (doctorChanged)
                {
                    _db.AuditLogs.Add(new AuditLog
                    {
                        ActorUserId = actorUserId,
                        HospitalId = visitHospitalId,
                        Action = VisitDoctorAssignedAuditAction,
                        EntityType = "VISIT",
                        EntityId = visitCode,
                        Reason = reassignmentReason,
                        ChangeData = JsonSerializer.Serialize(new
                        {
                            visitId = visit.Id,
                            visitCode,
                            previousDoctorId,
                            assignedDoctorId = targetDoctor.Id,
                            assignedDoctorCode = targetDoctor.DoctorCode,
                            previousRoomId,
                            examinationRoomId = visit.ExaminationRoomId,
                            coordinationPurpose = purpose,
                            reassignmentReason,
                        }, AuditJsonOptions),
                        IpAddress = QueueEntryRequestParsing.ParseIpAddress(ipAddress),
                        OccurredAt = nowUtc,
                    });
                }

                _db.AuditLogs.Add(new AuditLog
                {
                    ActorUserId = actorUserId,
                    HospitalId = visitHospitalId,
                    Action = ReturnEnrolledAuditAction,
                    EntityType = "QUEUE_ENTRY",
                    EntityId = ticket.Id.ToString(CultureInfo.InvariantCulture),
                    Reason = notes ?? $"Điều phối khám kết luận (RETURN_CONSULTATION/{purpose}) cho lượt khám {visitCode}.",
                    ChangeData = JsonSerializer.Serialize(new
                    {
                        queueEntryId = ticket.Id,
                        visitId,
                        visitCode,
                        target = ClinicalQueueRules.QueueTypeReturnConsultation,
                        coordinationPurpose = purpose,
                        assignedDoctorId = targetDoctor.Id,
                        doctorWasReassigned = doctorChanged,
                        previousDoctorId,
                        servicePointId = targetSp?.Id,
                        examinationRoomId = visit.ExaminationRoomId,
                        queueNumber = ticket.QueueNumber,
                        actorRole = actor.RoleLabel,
                        presenceSessionNo = visit.PresenceSessionNo,
                        isReplayedTicket = isReplay,
                        idempotencyKey,
                    }, AuditJsonOptions),
                    IpAddress = QueueEntryRequestParsing.ParseIpAddress(ipAddress),
                    OccurredAt = nowUtc,
                });

                if (purpose == QueueCoordinationPurposes.ClinicalReview)
                {
                    _db.AuditLogs.Add(new AuditLog
                    {
                        ActorUserId = actorUserId,
                        HospitalId = visitHospitalId,
                        Action = ClinicalReviewCoordinatedAuditAction,
                        EntityType = "VISIT",
                        EntityId = visitCode,
                        Reason = notes ?? "Điều phối bệnh nhân gặp bác sĩ đánh giá lại chỉ định cận lâm sàng (CLINICAL_REVIEW).",
                        ChangeData = JsonSerializer.Serialize(new
                        {
                            visitId,
                            visitCode,
                            queueEntryId = ticket.Id,
                            coordinationPurpose = purpose,
                            assignedDoctorId = targetDoctor.Id,
                        }, AuditJsonOptions),
                        IpAddress = QueueEntryRequestParsing.ParseIpAddress(ipAddress),
                        OccurredAt = nowUtc,
                    });
                }

                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                notifySpId = targetSp?.Id;

                var room = visit.ExaminationRoomId.HasValue && roomsById.TryGetValue(visit.ExaminationRoomId.Value, out var er)
                    ? er
                    : null;

                response = new ReturnEnrollmentResponseDto(
                    VisitId: visit.Id,
                    VisitCode: visitCode,
                    VisitStatus: AppointmentBookingRules.NormalizeStatusKey(visit.Status),
                    PresenceState: VisitPresenceEvaluator.NormalizePresenceState(visit.PresenceState),
                    PresenceSessionNo: Math.Max(1, visit.PresenceSessionNo),
                    AssignedDoctorId: targetDoctor.Id,
                    AssignedDoctorCode: targetDoctor.DoctorCode,
                    AssignedDoctorName: FormatDoctorDisplayName(targetDoctor),
                    DoctorWasReassigned: doctorChanged,
                    PreviousAssignedDoctorId: doctorChanged ? previousDoctorId : null,
                    ExaminationRoomId: visit.ExaminationRoomId,
                    ExaminationRoomCode: room?.RoomCode,
                    ExaminationRoomName: room?.Name,
                    CoordinationPurpose: purpose,
                    Ticket: VisitQueueEnrollmentService.MapTicketDto(ticket, servicePointsById, roomsById, isIdempotentReplay: isReplay));
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
                    var scopedRaceKey = BuildScopedQueueIdempotencyKey(
                        idempotencyKey,
                        purpose == QueueCoordinationPurposes.ClinicalReview ? "CREV" : "RET");
                    var racedByKey = await _db.QueueEntries.AsNoTracking()
                        .FirstOrDefaultAsync(q => q.IdempotencyKey == scopedRaceKey, ct);
                    if (racedByKey is not null)
                    {
                        var sameVisit = racedByKey.VisitId == visitId;
                        var sameTarget = string.Equals(
                            racedByKey.QueueType?.Trim(),
                            ClinicalQueueRules.QueueTypeReturnConsultation,
                            StringComparison.OrdinalIgnoreCase);
                        var samePurpose = string.Equals(
                            racedByKey.CoordinationPurpose?.Trim(),
                            purpose,
                            StringComparison.OrdinalIgnoreCase);
                        if (!sameVisit || !sameTarget || !samePurpose)
                        {
                            throw new ConflictException(IdempotencyMismatchMessage);
                        }

                        var replaySps = await _db.QueueServicePoints.AsNoTracking()
                            .Where(sp => sp.HospitalId == visitHospitalId)
                            .ToDictionaryAsync(sp => sp.Id, ct);
                        var replayRooms = await _db.HospitalRooms.AsNoTracking()
                            .Where(r => r.HospitalId == visitHospitalId)
                            .ToDictionaryAsync(r => r.Id, ct);
                        var racedVisit = await _db.Visits.AsNoTracking()
                            .FirstOrDefaultAsync(v => v.Id == visitId, ct)
                            ?? throw new NotFoundException(VisitPaymentAccess.VisitNotFoundMessage);
                        var racedDoctor = racedVisit.AssignedDoctorId is > 0
                            ? await _db.Doctors.AsNoTracking().Include(d => d.Staff)
                                .FirstOrDefaultAsync(d => d.Id == racedVisit.AssignedDoctorId.Value, ct)
                            : null;
                        var racedRoom = racedVisit.ExaminationRoomId.HasValue &&
                                        replayRooms.TryGetValue(racedVisit.ExaminationRoomId.Value, out var rrr)
                            ? rrr
                            : null;

                        return new ReturnEnrollmentResponseDto(
                            VisitId: racedVisit.Id,
                            VisitCode: FormatVisitCode(racedVisit, todayLocal),
                            VisitStatus: AppointmentBookingRules.NormalizeStatusKey(racedVisit.Status),
                            PresenceState: VisitPresenceEvaluator.NormalizePresenceState(racedVisit.PresenceState),
                            PresenceSessionNo: Math.Max(1, racedVisit.PresenceSessionNo),
                            AssignedDoctorId: racedDoctor?.Id ?? racedVisit.AssignedDoctorId ?? 0,
                            AssignedDoctorCode: racedDoctor?.DoctorCode ?? string.Empty,
                            AssignedDoctorName: racedDoctor is not null ? FormatDoctorDisplayName(racedDoctor) : string.Empty,
                            DoctorWasReassigned: false,
                            PreviousAssignedDoctorId: null,
                            ExaminationRoomId: racedVisit.ExaminationRoomId,
                            ExaminationRoomCode: racedRoom?.RoomCode,
                            ExaminationRoomName: racedRoom?.Name,
                            CoordinationPurpose: purpose,
                            Ticket: VisitQueueEnrollmentService.MapTicketDto(
                                racedByKey, replaySps, replayRooms, isIdempotentReplay: true));
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

                throw new ConflictException("Yêu cầu điều phối khám kết luận đang có xung đột đồng thời, vui lòng thử lại.");
            }
            catch
            {
                await SafeRollbackAsync(tx, ct);
                throw;
            }
        }

        await _realtimePublisher.TryPublishQueueChangedSafeAsync(
            new QueueChangedNotification(
                HospitalId: visitHospitalId ?? 0,
                ServicePointId: notifySpId,
                RoomId: response.ExaminationRoomId,
                AppointmentId: null),
            ct);

        return response;
    }


    private async Task<bool> ComputeReturnReadinessAsync(
        Visit visit,
        IReadOnlyList<DiagnosticOrder> orders,
        CrossDayVisitPolicySnapshot policy,
        DateTime nowLocal,
        CancellationToken ct)
    {
        var visitStatus = AppointmentBookingRules.NormalizeStatusKey(visit.Status);
        if (TerminalVisitStatuses.Contains(visitStatus))
        {
            return false;
        }

        if (visit.ReadyForReturnAt.HasValue)
        {
            return true;
        }

        return orders.Count > 0 &&
               await DiagnosticOrderLifecycleHelper.EvaluateVisitReturnReadinessAsync(_db, visit.Id, ct: ct);
    }

    private static bool HasClinicalReviewItems(
        IReadOnlyList<DiagnosticOrder> orders,
        CrossDayVisitPolicySnapshot policy,
        DateTime nowLocal)
        => orders.Any(o =>
            AppointmentBookingRules.NormalizeStatusKey(o.Status) != "CANCELLED" &&
            (CrossDayVisitPolicyReader.IsOrderExpiredForExecution(o, policy.DiagnosticOrderValidityDays, nowLocal) ||
             string.Equals(o.SelectionStatus?.Trim(), DiagnosticSelectionStatuses.ReviewRequired, StringComparison.OrdinalIgnoreCase)));

    private async Task<EnrollmentActorContext> AuthorizeEnrollmentActorAsync(
        long actorUserId,
        IReadOnlyCollection<long> roleIds,
        IReadOnlySet<string> roleCodes,
        long? visitHospitalId,
        long visitId,
        string visitCode,
        CrossDayVisitPolicySnapshot policy,
        string? ipAddress,
        DateTime nowUtc,
        bool writeExceptionAudit,
        CancellationToken ct)
    {
        var isReception = roleCodes.Contains(RoleNames.Reception);
        var isHospitalAdmin = roleCodes.Contains(RoleNames.HospitalAdmin);

        // RC-01..03 / RC-16: PATIENT / DIAGNOSTIC_STAFF / PHARMACY_STAFF / DOCTOR / SYSTEM_ADMIN
        // are always rejected with 403 [RETURN_ENROLLMENT_RECEPTION_REQUIRED].
        if (!isReception && !isHospitalAdmin)
        {
            throw new ForbiddenException(ReturnReceptionRequiredMessage);
        }

        var permSet = await VisitPaymentAccess.LoadRolePermissionCodesAsync(_db, roleIds, ct);
        if (!permSet.Contains(PermissionCodes.QueueEnroll))
        {
            throw new ForbiddenException(QueueEnrollRequiredMessage);
        }

        var staff = await _db.Staff.AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == actorUserId, ct)
            ?? throw new ForbiddenException(VisitPaymentAccess.InactiveStaffMessage);
        if (!string.Equals(staff.Status?.Trim(), "ACTIVE", StringComparison.OrdinalIgnoreCase) || !staff.HospitalId.HasValue)
        {
            throw new ForbiddenException(VisitPaymentAccess.InactiveStaffMessage);
        }

        if (visitHospitalId.HasValue && staff.HospitalId.Value != visitHospitalId.Value)
        {
            throw new ForbiddenException("Nhân sự không thuộc cơ sở y tế của lượt khám này.");
        }

        var hospital = await _db.Hospitals.AsNoTracking()
            .FirstOrDefaultAsync(h => h.Id == staff.HospitalId.Value, ct)
            ?? throw new ForbiddenException(VisitPaymentAccess.InactiveHospitalMessage);
        if (hospital.IsActive != true)
        {
            throw new ForbiddenException(VisitPaymentAccess.InactiveHospitalMessage);
        }

        if (isReception)
        {
            return new EnrollmentActorContext(RoleNames.Reception, staff);
        }

        // RC-21: HOSPITAL_ADMIN exception path — requires policy flag + live QUEUE_ENROLL +
        // VISIT_ASSIGN in the SAME hospital + exception audit row.
        if (!policy.AllowHospitalAdminReturnCoordination)
        {
            throw new ForbiddenException(ReturnReceptionRequiredMessage);
        }

        if (!permSet.Contains(PermissionCodes.VisitAssign))
        {
            throw new ForbiddenException(ReturnReceptionRequiredMessage);
        }

        if (writeExceptionAudit)
        {
            _db.AuditLogs.Add(new AuditLog
            {
                ActorUserId = actorUserId,
                HospitalId = staff.HospitalId,
                Action = HospitalAdminExceptionAuditAction,
                EntityType = "VISIT",
                EntityId = visitCode,
                Reason = "Quản trị bệnh viện thực hiện điều phối khám kết luận theo ngoại lệ chính sách (allow_hospital_admin_return_coordination).",
                ChangeData = JsonSerializer.Serialize(new
                {
                    actorUserId,
                    hospitalId = staff.HospitalId,
                    visitId,
                    visitCode,
                    role = RoleNames.HospitalAdmin,
                    permissions = new[] { PermissionCodes.QueueEnroll, PermissionCodes.VisitAssign },
                }, AuditJsonOptions),
                IpAddress = QueueEntryRequestParsing.ParseIpAddress(ipAddress),
                OccurredAt = nowUtc,
            });
        }

        return new EnrollmentActorContext(RoleNames.HospitalAdmin, staff);
    }

    private async Task AuthorizeContextReaderAsync(
        long actorUserId,
        IReadOnlySet<string> roleCodes,
        Visit visit,
        long? visitHospitalId,
        CancellationToken ct)
    {
        // F-02 (security): no SYSTEM_ADMIN bypass. A platform admin has no staff/clinical tenancy on this
        // surface and must be rejected by the role checks below (403), including the return-context path.
        var isStaffReader = roleCodes.Contains(RoleNames.Reception) ||
                            roleCodes.Contains(RoleNames.HospitalAdmin) ||
                            roleCodes.Contains(RoleNames.DiagnosticStaff) ||
                            roleCodes.Contains(RoleNames.PharmacyStaff);
        if (isStaffReader)
        {
            await RequireActiveHospitalStaffAsync(actorUserId, visitHospitalId, ct);
            return;
        }

        if (roleCodes.Contains(RoleNames.Doctor))
        {
            var (doctorStaff, doctor) = await VisitPaymentAccess.RequireActiveDoctorStaffAsync(
                _db,
                actorUserId,
                "Bác sĩ không có quyền xem ngữ cảnh điều phối khám kết luận của lượt khám này.",
                ct);
            var effectiveHospitalId = visitHospitalId ?? doctorStaff.HospitalId
                ?? throw new ForbiddenException(VisitPaymentAccess.HospitalConflictMessage);
            await VisitPaymentAccess.EnsureDoctorAssignedToVisitAsync(
                _db,
                doctor,
                visit,
                effectiveHospitalId,
                "Bác sĩ chỉ được xem ngữ cảnh điều phối của lượt khám được phân công.",
                ct);
            return;
        }

        throw new ForbiddenException("Người dùng không có quyền xem ngữ cảnh điều phối khám kết luận.");
    }

    private ReturnCoordinationDoctorCandidateDto SelectDoctor(
        Visit visit,
        IReadOnlyList<ReturnCoordinationDoctorCandidateDto> candidates,
        long? requestedDoctorId)
    {
        if (requestedDoctorId is > 0)
        {
            // Explicit pick: existence is a hard 404; eligibility is re-validated post-lock by the
            // caller, which maps raced eligibility/capacity loss to 409 [DOCTOR_CAPACITY_CHANGED]
            // (E2-R07 / RC-11..14 / RC-22).
            return candidates.FirstOrDefault(c => c.DoctorId == requestedDoctorId.Value)
                ?? throw new NotFoundException("Doctor", requestedDoctorId.Value);
        }

        // RC-07: original-doctor continuity first, then lowest ProjectedLoadCount.
        var original = visit.AssignedDoctorId is > 0
            ? candidates.FirstOrDefault(c => c.DoctorId == visit.AssignedDoctorId.Value && c.IsEligible)
            : null;
        if (original is not null)
        {
            return original;
        }

        var best = candidates
            .Where(c => c.IsEligible)
            .OrderBy(c => c.ProjectedLoadCount)
            .ThenBy(c => c.DoctorId)
            .FirstOrDefault();
        if (best is null)
        {
            throw new ConflictException(NoEligibleDoctorMessage);
        }

        return best;
    }

    private static bool IsExaminationQueueType(string? queueType)
    {
        var normalized = AppointmentBookingRules.NormalizeStatusKey(queueType);
        return string.IsNullOrEmpty(normalized) || normalized == ClinicalQueueRules.QueueTypeExamination;
    }

    private static bool IsClinicalQueueType(string? queueType)
    {
        var normalized = AppointmentBookingRules.NormalizeStatusKey(queueType);
        return string.IsNullOrEmpty(normalized) ||
               normalized is ClinicalQueueRules.QueueTypeExamination or ClinicalQueueRules.QueueTypeReturnConsultation;
    }

    private async Task<List<ReturnCoordinationDoctorCandidateDto>> BuildDoctorCandidatesAsync(
        Visit visit,
        long? hospitalId,
        CrossDayVisitPolicySnapshot policy,
        DateOnly todayLocal,
        DateTime nowLocal,
        CancellationToken ct)
    {
        if (!hospitalId.HasValue)
        {
            return [];
        }

        var doctors = await _db.Doctors.AsNoTracking()
            .Include(d => d.Staff)
            .Include(d => d.Specialty)
            .Where(d => d.HospitalId == hospitalId.Value || d.Staff!.HospitalId == hospitalId.Value)
            .OrderBy(d => d.DoctorCode)
            .ThenBy(d => d.Id)
            .ToListAsync(ct);

        if (doctors.Count == 0)
        {
            return [];
        }

        var doctorIds = doctors.Select(d => d.Id).ToList();

        var servicePoints = await _db.QueueServicePoints.AsNoTracking()
            .Where(sp => sp.HospitalId == hospitalId.Value && sp.IsActive != false)
            .ToListAsync(ct);
        var rooms = await _db.HospitalRooms.AsNoTracking()
            .Where(r => r.HospitalId == hospitalId.Value)
            .ToListAsync(ct);
        var departments = await _db.Departments.AsNoTracking()
            .Where(d => d.HospitalId == hospitalId.Value)
            .ToDictionaryAsync(d => d.Id, ct);

        var dayStart = todayLocal.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var dayEnd = dayStart.AddDays(1);

        var todaySlots = await _db.ExaminationSlots.AsNoTracking()
            .Where(s =>
                s.DoctorId.HasValue &&
                doctorIds.Contains(s.DoctorId.Value) &&
                s.StartsAt.HasValue &&
                s.StartsAt.Value >= dayStart &&
                s.StartsAt.Value < dayEnd)
            .ToListAsync(ct);
        var todaySlotIds = todaySlots.Select(s => s.Id).ToHashSet();

        var todayAppointments = todaySlotIds.Count == 0
            ? []
            : await _db.Appointments.AsNoTracking()
                .Where(a => a.SlotId.HasValue && todaySlotIds.Contains(a.SlotId.Value))
                .ToListAsync(ct);

        var appointmentIds = todayAppointments.Select(a => a.Id).ToHashSet();

        // Queue entries today across the hospital used to attribute load to each doctor:
        // exclude CANCELLED / DEFERRED / SKIPPED / NO_SHOW / COMPLETED / DONE entirely.
        var spIds = servicePoints.Select(sp => sp.Id).ToHashSet();
        var todayQueues = await _db.QueueEntries.AsNoTracking()
            .Where(q =>
                q.QueueDate == todayLocal &&
                !q.CompletedAt.HasValue &&
                ((q.ServicePointId.HasValue && spIds.Contains(q.ServicePointId.Value)) ||
                 (q.CurrentServicePointId.HasValue && spIds.Contains(q.CurrentServicePointId.Value)) ||
                 (q.AppointmentId.HasValue && appointmentIds.Contains(q.AppointmentId.Value)) ||
                 (q.VisitId.HasValue && q.VisitId == visit.Id)))
            .ToListAsync(ct);

        // Visits currently assigned to candidate doctors (for attribution when a queue entry
        // is bound via visit rather than service point).
        var openVisits = await _db.Visits.AsNoTracking()
            .Where(v =>
                v.CompletedAt == null &&
                v.AssignedDoctorId.HasValue &&
                doctorIds.Contains(v.AssignedDoctorId.Value))
            .ToListAsync(ct);
        var visitDoctorById = openVisits
            .Where(v => v.AssignedDoctorId.HasValue)
            .GroupBy(v => v.Id)
            .ToDictionary(g => g.Key, g => g.First().AssignedDoctorId!.Value);

        // CHECKED_IN appointments that already have a live QueueEntry are represented inside
        // the waiting/serving counts — never double-count them (RC-08..09).
        var liveQueuedAppointmentIds = todayQueues
            .Where(q =>
                q.AppointmentId.HasValue &&
                !ClosedQueueStatuses.Contains(AppointmentBookingRules.NormalizeStatusKey(q.Status)))
            .Select(q => q.AppointmentId!.Value)
            .ToHashSet();

        // Service authorization set for the visit's required examination service (when any).
        var requiredServiceId = await ResolveRequiredServiceIdAsync(visit, ct);
        HashSet<long>? authorizedDoctorIds = null;
        if (requiredServiceId.HasValue)
        {
            var auths = await _db.DoctorServiceAuthorizations.AsNoTracking()
                .Where(a => a.ServiceId == requiredServiceId.Value || doctorIds.Contains(a.DoctorId))
                .ToListAsync(ct);
            if (auths.Count > 0)
            {
                authorizedDoctorIds = auths
                    .Where(a => a.ServiceId == requiredServiceId.Value && a.IsActive != false)
                    .Select(a => a.DoctorId)
                    .ToHashSet();
            }
        }

        var requiredService = requiredServiceId.HasValue
            ? await _db.Services.AsNoTracking().FirstOrDefaultAsync(s => s.Id == requiredServiceId.Value, ct)
            : null;

        var candidates = new List<ReturnCoordinationDoctorCandidateDto>(doctors.Count);
        foreach (var doctor in doctors)
        {
            var staff = doctor.Staff;
            var dept = staff?.DepartmentId.HasValue == true && departments.TryGetValue(staff.DepartmentId.Value, out var dep)
                ? dep
                : null;

            var doctorSpIds = servicePoints
                .Where(sp => sp.DoctorId == doctor.Id)
                .Select(sp => sp.Id)
                .ToHashSet();

            var doctorQueues = todayQueues
                .Where(q =>
                    (q.VisitId.HasValue && visitDoctorById.TryGetValue(q.VisitId.Value, out var vd) && vd == doctor.Id) ||
                    (q.ServicePointId.HasValue && doctorSpIds.Contains(q.ServicePointId.Value)) ||
                    (q.CurrentServicePointId.HasValue && doctorSpIds.Contains(q.CurrentServicePointId.Value)))
                .Where(q => !ClosedQueueStatuses.Contains(AppointmentBookingRules.NormalizeStatusKey(q.Status)))
                .ToList();

            var currentServing = doctorQueues.Count(q =>
                ServingStatuses.Contains(AppointmentBookingRules.NormalizeStatusKey(q.Status)) &&
                IsClinicalQueueType(q.QueueType));
            var waitingExam = doctorQueues.Count(q =>
                ActiveWaitingStatuses.Contains(AppointmentBookingRules.NormalizeStatusKey(q.Status)) &&
                IsExaminationQueueType(q.QueueType));
            var waitingReturn = doctorQueues.Count(q =>
                ActiveWaitingStatuses.Contains(AppointmentBookingRules.NormalizeStatusKey(q.Status)) &&
                string.Equals(q.QueueType?.Trim(), ClinicalQueueRules.QueueTypeReturnConsultation, StringComparison.OrdinalIgnoreCase));

            var doctorSlots = todaySlots.Where(s => s.DoctorId == doctor.Id).ToList();
            var doctorSlotIds = doctorSlots.Select(s => s.Id).ToHashSet();
            var doctorAppointments = todayAppointments
                .Where(a => a.SlotId.HasValue && doctorSlotIds.Contains(a.SlotId.Value))
                .ToList();

            var remainingShiftBooked = doctorAppointments.Count(a =>
                UpcomingAppointmentStatuses.Contains(AppointmentBookingRules.NormalizeStatusKey(a.Status)));
            var upcomingBooked = doctorAppointments.Count(a =>
                UpcomingAppointmentStatuses.Contains(AppointmentBookingRules.NormalizeStatusKey(a.Status)) &&
                !liveQueuedAppointmentIds.Contains(a.Id));

            int? remainingShiftMinutes = null;
            var latestEnd = doctorSlots
                .Where(s => s.EndsAt.HasValue)
                .Select(s => s.EndsAt!.Value)
                .DefaultIfEmpty()
                .Max();
            var hasSlotToday = doctorSlots.Count > 0;
            if (hasSlotToday && latestEnd != default)
            {
                remainingShiftMinutes = Math.Max(0, (int)Math.Ceiling((latestEnd - nowLocal).TotalMinutes));
            }

            var projectedLoad = currentServing + waitingExam + waitingReturn + upcomingBooked;

            // Resolve doctor's room/SP for display + eligibility.
            var (resolvedRoom, resolvedSp) = ResolveDoctorRoomAndServicePoint(
                doctor,
                visit.ExaminationRoomId,
                servicePoints,
                rooms,
                doctorSlots);

            var ineligibleReasons = new List<string>();
            var staffActive = staff is null ||
                              string.Equals(staff.Status?.Trim(), "ACTIVE", StringComparison.OrdinalIgnoreCase);
            if (!doctor.IsActive || !staffActive)
            {
                ineligibleReasons.Add("Bác sĩ hiện đang nghỉ trực hoặc ngừng hoạt động.");
            }

            if (dept is not null && dept.IsActive == false)
            {
                ineligibleReasons.Add($"Khoa phòng '{dept.Name}' đang tạm ngưng hoạt động.");
            }

            if (authorizedDoctorIds is not null && !authorizedDoctorIds.Contains(doctor.Id))
            {
                ineligibleReasons.Add("Bác sĩ chưa được phân quyền thực hiện dịch vụ khám của lượt khám này.");
            }

            if (requiredService?.SpecialtyId is long reqSpecId &&
                doctor.SpecialtyId.HasValue &&
                doctor.SpecialtyId.Value != reqSpecId)
            {
                ineligibleReasons.Add("Bác sĩ không thuộc chuyên khoa yêu cầu của dịch vụ khám.");
            }

            if (resolvedSp is null && resolvedRoom is null)
            {
                ineligibleReasons.Add("Bác sĩ chưa được gán buồng khám / điểm phục vụ đang hoạt động.");
            }

            var shiftExhausted = hasSlotToday &&
                                 remainingShiftMinutes is 0 &&
                                 latestEnd != default;
            if (shiftExhausted)
            {
                ineligibleReasons.Add("Ca trực của bác sĩ hôm nay đã kết thúc.");
            }

            var isAtCapacity = policy.MaxActiveQueuePerDoctor is > 0 &&
                               projectedLoad >= policy.MaxActiveQueuePerDoctor.Value;
            if (isAtCapacity)
            {
                ineligibleReasons.Add($"Bác sĩ đã đạt giới hạn hàng đợi ({projectedLoad}/{policy.MaxActiveQueuePerDoctor}).");
            }

            var isOriginal = visit.AssignedDoctorId == doctor.Id;
            var isEligible = ineligibleReasons.Count == 0;

            candidates.Add(new ReturnCoordinationDoctorCandidateDto(
                DoctorId: doctor.Id,
                DoctorCode: doctor.DoctorCode ?? string.Empty,
                DoctorName: FormatDoctorDisplayName(doctor),
                SpecialtyId: doctor.SpecialtyId,
                SpecialtyName: doctor.Specialty?.Name,
                DepartmentId: staff?.DepartmentId,
                DepartmentName: dept?.Name,
                RoomId: resolvedRoom?.Id,
                RoomCode: resolvedRoom?.RoomCode,
                RoomName: resolvedRoom?.Name,
                ServicePointId: resolvedSp?.Id,
                ServicePointName: resolvedSp?.Name,
                IsOriginalAssignedDoctor: isOriginal,
                IsEligible: isEligible,
                IsAtCapacity: isAtCapacity,
                IsRecommended: false,
                RecommendationReason: null,
                CurrentServingCount: currentServing,
                WaitingExaminationCount: waitingExam,
                WaitingReturnCount: waitingReturn,
                UpcomingBookedAppointmentsTodayCount: upcomingBooked,
                RemainingShiftBookedCount: remainingShiftBooked,
                RemainingShiftMinutes: remainingShiftMinutes,
                ProjectedLoadCount: projectedLoad,
                IneligibleReasons: ineligibleReasons));
        }

        // Recommendation: original doctor first when eligible (RC-07), else the least loaded.
        var recommended = candidates.FirstOrDefault(c => c.IsOriginalAssignedDoctor && c.IsEligible)
            ?? candidates.Where(c => c.IsEligible)
                .OrderBy(c => c.ProjectedLoadCount)
                .ThenBy(c => c.DoctorId)
                .FirstOrDefault();
        if (recommended is not null)
        {
            var index = candidates.FindIndex(c => c.DoctorId == recommended.DoctorId);
            if (index >= 0)
            {
                var c = candidates[index];
                candidates[index] = c with
                {
                    IsRecommended = true,
                    RecommendationReason = c.IsOriginalAssignedDoctor
                        ? "Bác sĩ phụ trách ban đầu — giữ liên tục chăm sóc (RC-07)."
                        : "Bác sĩ đủ điều kiện có tải lượng dự kiến thấp nhất.",
                };
            }
        }

        return candidates;
    }

    private async Task<long?> ResolveRequiredServiceIdAsync(Visit visit, CancellationToken ct)
    {
        if (!visit.AppointmentId.HasValue)
        {
            return null;
        }

        var appt = await _db.Appointments.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == visit.AppointmentId.Value, ct);
        if (appt?.SlotId is not long slotId)
        {
            return null;
        }

        var slot = await _db.ExaminationSlots.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == slotId, ct);
        return slot?.ServiceId;
    }

    private static (HospitalRoom? Room, QueueServicePoint? ServicePoint) ResolveDoctorRoomAndServicePoint(
        Doctor doctor,
        long? visitRoomId,
        IReadOnlyList<QueueServicePoint> servicePoints,
        IReadOnlyList<HospitalRoom> rooms,
        IReadOnlyList<ExaminationSlot> doctorSlots)
    {
        var doctorSp = servicePoints.FirstOrDefault(sp =>
                sp.DoctorId == doctor.Id && visitRoomId.HasValue && sp.RoomId == visitRoomId.Value)
            ?? servicePoints.FirstOrDefault(sp => sp.DoctorId == doctor.Id);

        HospitalRoom? room = null;
        if (doctorSp?.RoomId is long spRoomId)
        {
            room = rooms.FirstOrDefault(r => r.Id == spRoomId);
        }

        room ??= doctorSlots.FirstOrDefault(s => s.RoomId.HasValue) is { } slot
            ? rooms.FirstOrDefault(r => r.Id == slot.RoomId!.Value)
            : null;

        if (room is null && doctor.Staff?.DepartmentId is long deptId)
        {
            room = rooms.FirstOrDefault(r =>
                r.DepartmentId == deptId &&
                string.Equals(r.RoomType?.Trim(), "EXAMINATION", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(r.Status?.Trim(), "ACTIVE", StringComparison.OrdinalIgnoreCase));
        }

        if (doctorSp is null && room is not null)
        {
            doctorSp = servicePoints.FirstOrDefault(sp => sp.RoomId == room.Id);
        }

        return (room, doctorSp);
    }

    private static QueueServicePoint? ResolveClinicalServicePoint(
        long doctorId,
        Visit visit,
        long? requestedServicePointId,
        long? candidateServicePointId,
        IEnumerable<QueueServicePoint> hospitalServicePoints,
        IReadOnlyDictionary<long, HospitalRoom> roomsById)
    {
        var activeExamPoints = hospitalServicePoints
            .Where(sp =>
                sp.IsActive != false &&
                (sp.PointType == null ||
                 string.Equals(sp.PointType.Trim(), "EXAMINATION_ROOM", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(sp.PointType.Trim(), "EXAMINATION", StringComparison.OrdinalIgnoreCase)) &&
                (!sp.RoomId.HasValue ||
                 (roomsById.TryGetValue(sp.RoomId.Value, out var room) &&
                  string.Equals(room.Status?.Trim(), "ACTIVE", StringComparison.OrdinalIgnoreCase))))
            .OrderBy(sp => sp.Id)
            .ToList();

        if (requestedServicePointId is > 0)
        {
            var explicitSp = activeExamPoints.FirstOrDefault(sp => sp.Id == requestedServicePointId.Value)
                ?? throw new ConflictException(
                    "[INCOMPATIBLE_EXAMINATION_SERVICE_POINT] Điểm phục vụ buồng khám được chọn không hợp lệ hoặc đang ngừng hoạt động.");
            return explicitSp;
        }

        if (candidateServicePointId is > 0)
        {
            var candidateSp = activeExamPoints.FirstOrDefault(sp => sp.Id == candidateServicePointId.Value);
            if (candidateSp is not null)
            {
                return candidateSp;
            }
        }

        var byRoomAndDoctor = activeExamPoints.FirstOrDefault(sp =>
            visit.ExaminationRoomId.HasValue &&
            sp.RoomId == visit.ExaminationRoomId.Value &&
            sp.DoctorId == doctorId);
        if (byRoomAndDoctor is not null)
        {
            return byRoomAndDoctor;
        }

        var byDoctor = activeExamPoints.FirstOrDefault(sp => sp.DoctorId == doctorId);
        if (byDoctor is not null)
        {
            return byDoctor;
        }

        return activeExamPoints.FirstOrDefault(sp =>
            visit.ExaminationRoomId.HasValue && sp.RoomId == visit.ExaminationRoomId.Value);
    }

    private static long ValidateExplicitRoom(
        long roomId,
        IReadOnlyDictionary<long, HospitalRoom> roomsById,
        IReadOnlyDictionary<long, Department> departmentsById,
        long? visitHospitalId,
        long? doctorDepartmentId)
    {
        if (!roomsById.TryGetValue(roomId, out var room))
        {
            throw new NotFoundException("HospitalRoom", roomId);
        }

        if (visitHospitalId.HasValue && room.HospitalId.HasValue && room.HospitalId.Value != visitHospitalId.Value)
        {
            throw new ForbiddenException("Buồng khám được chọn thuộc cơ sở y tế khác.");
        }

        if (!string.Equals(room.RoomType?.Trim(), "EXAMINATION", StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException(
                $"Phòng '{room.RoomCode}' (loại '{room.RoomType}') không phải là buồng khám lâm sàng (EXAMINATION).");
        }

        if (!string.Equals(room.Status?.Trim(), "ACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException($"Buồng khám '{room.RoomCode}' hiện không khả dụng.");
        }

        if (room.DepartmentId.HasValue &&
            departmentsById.TryGetValue(room.DepartmentId.Value, out var dept) &&
            dept.IsActive == false)
        {
            throw new ConflictException($"Khoa phòng '{dept.Name}' của buồng khám '{room.RoomCode}' đang tạm ngưng hoạt động.");
        }

        if (room.DepartmentId.HasValue &&
            doctorDepartmentId.HasValue &&
            room.DepartmentId.Value != doctorDepartmentId.Value)
        {
            throw new ValidationException(
                $"Buồng khám '{room.RoomCode}' không thuộc cùng khoa phòng với bác sĩ được chọn.");
        }

        return roomId;
    }

    private async Task<Staff> RequireActiveHospitalStaffAsync(long actorUserId, long? expectedHospitalId, CancellationToken ct)
    {
        var staff = await _db.Staff.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == actorUserId, ct)
            ?? throw new ForbiddenException(VisitPaymentAccess.InactiveStaffMessage);
        if (!string.Equals(staff.Status?.Trim(), "ACTIVE", StringComparison.OrdinalIgnoreCase) || !staff.HospitalId.HasValue)
        {
            throw new ForbiddenException(VisitPaymentAccess.InactiveStaffMessage);
        }

        if (expectedHospitalId.HasValue && staff.HospitalId.Value != expectedHospitalId.Value)
        {
            throw new ForbiddenException("Nhân sự không thuộc cơ sở y tế của lượt khám này.");
        }

        var hospital = await _db.Hospitals.AsNoTracking().FirstOrDefaultAsync(h => h.Id == staff.HospitalId.Value, ct)
            ?? throw new ForbiddenException(VisitPaymentAccess.InactiveHospitalMessage);
        if (hospital.IsActive != true)
        {
            throw new ForbiddenException(VisitPaymentAccess.InactiveHospitalMessage);
        }

        return staff;
    }

    private async Task<long?> ResolveSingleHospitalIdAsync(Visit visit, CancellationToken ct)
    {
        var hospitalIds = await VisitHospitalResolver.CollectAsync(_db, visit, ct);
        if (hospitalIds.Count == 0)
        {
            return null;
        }

        if (hospitalIds.Count > 1)
        {
            throw new ConflictException(VisitPaymentAccess.HospitalConflictMessage);
        }

        return hospitalIds.First();
    }

    private static string FormatVisitCode(Visit visit, DateOnly? fallbackDate = null)
        => !string.IsNullOrWhiteSpace(visit.VisitCode)
            ? visit.VisitCode.Trim()
            : $"VIS-{(fallbackDate ?? DateOnly.FromDateTime(DateTime.UtcNow)):yyyyMM}-{visit.Id:D4}";

    private static string FormatDoctorDisplayName(Doctor doctor)
    {
        var rawName = doctor.Staff?.FullName?.Trim();
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return doctor.DoctorCode ?? $"BS #{doctor.Id}";
        }

        var title = doctor.ProfessionalTitle?.Trim();
        if (!string.IsNullOrWhiteSpace(title) &&
            !rawName.StartsWith("BS", StringComparison.OrdinalIgnoreCase) &&
            !rawName.StartsWith(title, StringComparison.OrdinalIgnoreCase))
        {
            return $"{title} {rawName}";
        }

        return rawName;
    }

    private static string BuildScopedQueueIdempotencyKey(string baseKey, string suffix)
    {
        var combined = $"{baseKey}:{suffix}";
        return combined.Length <= 100 ? combined : combined[..100];
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

    private sealed record EnrollmentActorContext(string RoleLabel, Staff Staff);
}
