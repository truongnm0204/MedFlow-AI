using System.Globalization;
using System.Net;
using System.Text.Json;
using MedFlow.Application.Common.Persistence;
using MedFlow.Application.Common.Time;
using MedFlow.Application.DTOs.Payments;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Options;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace MedFlow.Application.Services.Payments;

public sealed class GenerateQrPaymentService : IGenerateQrPaymentService
{
    public const string PendingStatus = PaymentStatuses.Pending;
    public const int PendingExpiryMinutes = 15;

    public const string UndeterminedMessage = "Chưa xác định được số tiền cần thanh toán.";
    public const string NothingDueMessage = "Lượt khám không có khoản phải thu.";
    public const string ChargeAlreadyPaidMessage = "[CHARGE_ALREADY_PAID] Một hoặc nhiều khoản phí đã được thanh toán.";
    public const string IdempotencyMismatchMessage = "[IDEMPOTENCY_PAYLOAD_MISMATCH] Khóa idempotency đã được sử dụng cho yêu cầu thanh toán khác.";
    public const string VisitBlockedMessage = "Lượt khám không thể thanh toán.";
    public const string CashierMessage = "Lượt khám được thu tại quầy.";
    public const string NotIntegerMessage = "Số tiền không phải số nguyên VND.";
    public const string AmountMismatchMessage = "Số tiền yêu cầu thanh toán không còn khớp khoản phải thu.";
    public const string MultipleOpenMessage = "Lượt khám đang có nhiều yêu cầu thanh toán mở.";
    public const string MultipleUnpaidChargesMessage = "Lượt khám đang có nhiều khoản phí chưa thanh toán. Vui lòng chọn khoản phí cụ thể để tạo mã QR.";
    public const string ChargeCancelledMessage = "Khoản phí đã bị hủy, không thể tạo mã thanh toán.";
    public const string DiagnosticCancelledMessage = "Chỉ định cận lâm sàng đã bị hủy hoặc đang chờ xem xét lại, không thể thanh toán.";
    public const string InvalidVisitChargeIdMessage = "Mã khoản phí cần thanh toán không hợp lệ.";
    public const string ForbiddenMessage = "Bạn không có quyền tạo yêu cầu thanh toán QR cho lượt khám này.";
    public const string PharmacyCounterOnlyMessage = "[PHARMACY_COUNTER_ONLY] Khoản phí thuốc chỉ được thu tại quầy thuốc bởi nhân viên nhà thuốc.";
    public const string PharmacyMedicineOnlyMessage = "[PHARMACY_MEDICINE_ONLY] Nhân viên nhà thuốc chỉ được thu các khoản phí thuốc (MEDICINE).";

    internal const decimal MaxVndAmount = 9_999_999_999_999m;
    private const int MaxIdempotencyKeyLength = 100;
    private const string AuditAction = "QR_PAYMENT_REQUESTED";
    private const string PaymentEntityType = "PAYMENT";

    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;
    private readonly QrPaymentOptions _qrOptions;

    public GenerateQrPaymentService(
        IApplicationDbContext db,
        TimeProvider? timeProvider = null,
        IOptions<QrPaymentOptions>? qrOptions = null)
    {
        _db = db;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _qrOptions = qrOptions?.Value ?? new QrPaymentOptions();
    }

    public Task<GenerateQrPaymentResponseDto> CreateAsync(
        long actorUserId,
        string? idOrCode,
        string? ipAddress,
        CancellationToken ct = default)
        => IssueAsync(actorUserId, idOrCode, ipAddress, request: null, recovering: false, ct);

    public Task<GenerateQrPaymentResponseDto> CreateAsync(
        long actorUserId,
        string? idOrCode,
        string? ipAddress,
        GenerateQrPaymentRequestDto? request,
        CancellationToken ct = default)
        => IssueAsync(actorUserId, idOrCode, ipAddress, request, recovering: false, ct);

    private async Task<GenerateQrPaymentResponseDto> IssueAsync(
        long actorUserId,
        string? idOrCode,
        string? ipAddress,
        GenerateQrPaymentRequestDto? request,
        bool recovering,
        CancellationToken ct)
    {
        var explicitChargeIds = VisitPaymentAccess.ParseExplicitVisitChargeIds(
            request?.VisitChargeId,
            request?.VisitChargeIds,
            InvalidVisitChargeIdMessage);
        var usesStageBAllocatedMode = request?.VisitChargeIds is not null;

        string? idempotencyKey = null;
        if (request?.IdempotencyKey is not null)
        {
            idempotencyKey = request.IdempotencyKey.Trim();
            if (idempotencyKey.Length == 0 || idempotencyKey.Length > MaxIdempotencyKeyLength)
                throw new ValidationException("Tham số idempotencyKey phải từ 1 đến 100 ký tự.");
        }

        var actor = await AuthorizeActorPreVisitAsync(actorUserId, ct);

        var parsed = VisitPaymentAccess.ParseVisitIdentifier(idOrCode);
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

        var hospitalId = hospitalIds.First();
        if (!actor.IsPatient)
        {
            if (actor.HospitalId != hospitalId)
                throw new ForbiddenException(ForbiddenMessage);

            if (actor.Doctor is not null)
            {
                await VisitPaymentAccess.EnsureDoctorAssignedToVisitAsync(
                    _db,
                    actor.Doctor,
                    visit,
                    hospitalId,
                    ForbiddenMessage,
                    ct);
            }
        }

        var visitId = visit.Id;

        await using (var tx = await _db.BeginTransactionAsync(ct))
        {
            Payment? added = null;
            VisitCharge? materializedCharge = null;
            var txCommitted = false;
            try
            {
                // D2 / BR-BF02-06: việc tự lấy số CLS sau thanh toán do hệ thống quyết định, nên luôn
                // khóa bệnh nhân (nếu có) để tuần tự hóa presence + intent với các luồng lấy số khác.
                // ADR-0061 strict lock order: [02 patients] -> 09 visits -> 12 diagnostic_orders -> 15 visit_charges -> 16 payments -> 16b payment_allocations -> 16c payment_enrollment_intents
                await PaymentAllocationLookup.AcquireVisitPaymentChainLocksAsync(
                    _db,
                    visitId,
                    ct,
                    patientIdToLock: visit.PatientId);

                if (_db is DbContext trackerCtx)
                    trackerCtx.ChangeTracker.Clear();

                var lockedVisit = await _db.Visits.AsNoTracking()
                    .FirstOrDefaultAsync(v => v.Id == visitId, ct)
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
                        PermissionCodes.HospitalConfig,
                    ],
                    allowPharmacy: actor.IsPharmacyStaff,
                    ct: ct);
                actor = new QrActorContext(
                    verifiedActor.IsPatient,
                    verifiedActor.RoleLabel,
                    verifiedActor.IsPatient ? null : verifiedActor.VisitHospitalId,
                    verifiedActor.Doctor,
                    verifiedActor.OwnedPatientIds)
                {
                    IsPharmacyStaff = string.Equals(
                        verifiedActor.RoleLabel,
                        RoleNames.PharmacyStaff,
                        StringComparison.OrdinalIgnoreCase),
                };
                hospitalId = verifiedActor.VisitHospitalId;

                var nowLocal = Clock();
                var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

                // Check IdempotencyKey post-lock before any mutation
                if (idempotencyKey is not null)
                {
                    var existingByKey = await _db.Payments.AsNoTracking()
                        .FirstOrDefaultAsync(p => p.IdempotencyKey == idempotencyKey, ct);
                    if (existingByKey is not null)
                    {
                        var replayDto = await ValidateAndBuildIdempotentQrReplayAsync(
                            lockedVisit,
                            hospitalId,
                            existingByKey,
                            explicitChargeIds,
                            nowLocal,
                            nowUtc,
                            async token =>
                            {
                                txCommitted = true;
                                await tx.CommitAsync(token);
                            },
                            ct);
                        if (!txCommitted)
                        {
                            txCommitted = true;
                            await tx.CommitAsync(ct);
                        }

                        return replayDto;
                    }
                }

                var assessment = await VisitChargeAssessor.AssessAsync(_db, _timeProvider, lockedVisit, ct);
                var trackedCharges = await _db.VisitCharges
                    .Where(c => c.VisitId == visitId)
                    .OrderBy(c => c.Id)
                    .ToListAsync(ct);

                var allowCashierToQrSwitch = usesStageBAllocatedMode || !actor.IsPatient;

                var (targetCharges, amountDue) = await ResolveTargetChargesAndAmountAsync(
                    lockedVisit,
                    assessment,
                    trackedCharges,
                    explicitChargeIds,
                    usesStageBAllocatedMode,
                    allowCashierToQrSwitch,
                    pharmacyMedicineOnly: actor.IsPharmacyStaff,
                    nowLocal: nowLocal,
                    ct: ct);

                if (targetCharges.Count == 1 && targetCharges[0].Id == 0)
                {
                    materializedCharge = targetCharges[0];
                    _db.VisitCharges.Add(materializedCharge);
                    await _db.SaveChangesAsync(ct);
                }

                var targetChargeIds = targetCharges.Select(c => c.Id).OrderBy(id => id).ToList();

                // Promote any explicitly selected UNDECIDED optional DiagnosticOrders -> ACCEPTED
                if (explicitChargeIds is not null && await PromoteUndecidedOrdersAsync(targetCharges, ct))
                {
                    await _db.SaveChangesAsync(ct);
                }

                // D2 / BR-BF02-06: hệ thống tự chọn chỉ định CLS kế tiếp trong các khoản phí của giao dịch
                // (thứ tự tạo chỉ định, chưa xếp hàng, đã chấp nhận). Client không còn truyền chỉ định.
                var enrollOrder = await PaymentEnrollmentIntentProcessor.ResolveNextAutoEnrollOrderAsync(
                    _db,
                    visitId,
                    targetCharges,
                    ct);
                PaymentEnrollmentIntentProcessor.EnsureEnrollOrderInPaidChargesOrThrow(enrollOrder, targetCharges);

                var account = await VietQrAccountPolicyReader.ReadAsync(
                    _db,
                    hospitalId,
                    nowUtc,
                    ct);
                var hospital = await _db.Hospitals.AsNoTracking()
                    .FirstOrDefaultAsync(h => h.Id == hospitalId, ct);
                var merchant = VietQrPayloadBuilder.MerchantName(hospital?.Name);

                var trackedPayments = await _db.Payments
                    .Where(p => p.VisitId == visitId)
                    .OrderBy(p => p.Id)
                    .ToListAsync(ct);

                var trackedAllocations = await PaymentAllocationLookup.TryLoadTrackedForVisitAsync(_db, visitId, ct);
                var trackedIntents = await PaymentAllocationLookup.TryLoadTrackedIntentsForVisitAsync(_db, visitId, ct);

                // Critical Invariant (§2.2): Verify zero PAID allocation/payment exists on ANY target charge post-lock
                var (effectiveCheck, _) = PaymentAllocationLookup.BuildEffectiveAllocations(visitId, trackedPayments, trackedAllocations);
                var targetSet = targetChargeIds.ToHashSet();
                if (effectiveCheck.Any(a => targetSet.Contains(a.VisitChargeId) &&
                                            string.Equals(a.Status, PaymentAllocationStatuses.Paid, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new ConflictException(usesStageBAllocatedMode ? ChargeAlreadyPaidMessage : NothingDueMessage);
                }

                var includeUnlinkedLegacyOnExam = targetCharges.Count == 1 &&
                                                  !usesStageBAllocatedMode &&
                                                  IsExaminationCharge(targetCharges[0]);

                await ExpireStaleOverlappingPendingAsync(
                    trackedPayments,
                    trackedAllocations,
                    trackedIntents,
                    targetSet,
                    includeUnlinkedLegacyOnExam,
                    nowLocal,
                    nowUtc,
                    ct);

                // Find active PENDING payments overlapping ANY target charge
                var overlappingPending = FindOverlappingPendingPayments(
                    trackedPayments,
                    trackedAllocations,
                    targetSet,
                    includeUnlinkedLegacyOnExam);

                if (!usesStageBAllocatedMode && targetCharges.Count == 1 && overlappingPending.Count > 1)
                {
                    // Legacy unit-test compatibility when two unallocated PENDING rows exist on the same single charge
                    var allUnallocatedSameSingle = overlappingPending.All(p =>
                        trackedAllocations.All(pa => pa.PaymentId != p.Id));
                    if (allUnallocatedSameSingle)
                        throw new ConflictException(MultipleOpenMessage);
                }

                // Check if an exact-same-set open PENDING QR already exists
                var exactSamePending = overlappingPending.FirstOrDefault(p =>
                    HasExactSameTargetCharges(p, trackedAllocations, targetChargeIds, includeUnlinkedLegacyOnExam));

                if (exactSamePending is not null && overlappingPending.Count == 1)
                {
                    var existingPendingIntent = trackedIntents.FirstOrDefault(i => i.PaymentId == exactSamePending.Id);
                    var intentTargetChanged = existingPendingIntent?.DiagnosticOrderId != enrollOrder?.Id;

                    if (exactSamePending.Amount != amountDue || intentTargetChanged)
                    {
                        if (exactSamePending.Amount != amountDue && !usesStageBAllocatedMode && trackedAllocations.All(pa => pa.PaymentId != exactSamePending.Id))
                            throw new ConflictException(AmountMismatchMessage);

                        // Cancel mismatched-amount or changed-intent pending attempt and issue fresh QR
                        CancelPendingAttempt(exactSamePending, trackedAllocations, trackedIntents, nowUtc);
                        await _db.SaveChangesAsync(ct);
                    }
                    else
                    {
                        var mutatedExisting = false;
                        if (!exactSamePending.VisitChargeId.HasValue && targetChargeIds.Count == 1)
                        {
                            exactSamePending.VisitChargeId = targetChargeIds[0];
                            exactSamePending.PaymentScope = usesStageBAllocatedMode ? PaymentScopes.Allocated : PaymentScopes.Charge;
                            mutatedExisting = true;
                        }

                        if (!exactSamePending.CreatedAt.HasValue)
                        {
                            exactSamePending.CreatedAt = nowLocal;
                            mutatedExisting = true;
                        }

                        exactSamePending.PaymentMethod ??= PaymentMethods.VietQr;
                        if (idempotencyKey is not null && string.IsNullOrWhiteSpace(exactSamePending.IdempotencyKey))
                        {
                            exactSamePending.IdempotencyKey = idempotencyKey;
                            mutatedExisting = true;
                        }

                        // Ensure allocation row exists if PaymentAllocations DbSet is available
                        if (trackedAllocations.All(pa => pa.PaymentId != exactSamePending.Id))
                        {
                            var backfilledAllocs = targetCharges
                                .Select(c => PaymentAllocation.Create(
                                    exactSamePending.Id,
                                    c.Id,
                                    visitId,
                                    c.UnitPrice!.Value,
                                    PaymentAllocationStatuses.Pending,
                                    exactSamePending.CreatedAt ?? nowLocal))
                                .ToList();
                            PaymentAllocationLookup.TryAddAllocations(_db, backfilledAllocs);
                            trackedAllocations.AddRange(backfilledAllocs);
                            mutatedExisting = true;
                        }

                        var reusedEnrollmentDto = existingPendingIntent is not null
                            ? await PaymentEnrollmentIntentProcessor.BuildIntentDtoAsync(_db, existingPendingIntent, ct)
                            : null;

                        if (string.IsNullOrWhiteSpace(exactSamePending.PaymentCode))
                        {
                            exactSamePending.PaymentCode = CodeFor(exactSamePending.Id);
                            AddAudit(
                                actorUserId,
                                hospitalId,
                                visitId,
                                targetChargeIds,
                                exactSamePending,
                                amountDue,
                                QrPaymentIssueKind.Adopted,
                                ipAddress);
                            await _db.SaveChangesAsync(ct);
                            await tx.CommitAsync(ct);
                            return ToDto(
                                lockedVisit,
                                exactSamePending,
                                account,
                                merchant,
                                amountDue,
                                QrPaymentIssueKind.Adopted,
                                targetCharges,
                                trackedAllocations,
                                assessment.Lines,
                                reusedEnrollmentDto);
                        }

                        if (mutatedExisting)
                            await _db.SaveChangesAsync(ct);

                        await tx.CommitAsync(ct);
                        return ToDto(
                            lockedVisit,
                            exactSamePending,
                            account,
                            merchant,
                            amountDue,
                            QrPaymentIssueKind.Reused,
                            targetCharges,
                            trackedAllocations,
                            assessment.Lines,
                            reusedEnrollmentDto);
                    }
                }
                else if (overlappingPending.Count > 0)
                {
                    // §2.3 PAY-21: Charge selection changed -> cancel ENTIRE overlapping PENDING QR attempt(s)
                    // AND ALL of their PENDING allocations (never mutating old Payment.Amount!).
                    foreach (var oldPending in overlappingPending)
                    {
                        CancelPendingAttempt(oldPending, trackedAllocations, trackedIntents, nowUtc);
                    }

                    await _db.SaveChangesAsync(ct);
                }

                var singleChargeId = targetChargeIds.Count == 1 ? targetChargeIds[0] : (long?)null;
                added = usesStageBAllocatedMode || targetChargeIds.Count > 1
                    ? Payment.CreateAllocatedQr(visitId, singleChargeId, amountDue, nowLocal, idempotencyKey, PendingStatus)
                    : Payment.CreateForCharge(visitId, targetChargeIds[0], amountDue, nowLocal, PendingStatus);

                if (idempotencyKey is not null)
                    added.IdempotencyKey = idempotencyKey;

                _db.Payments.Add(added);
                await _db.SaveChangesAsync(ct);

                added.PaymentCode = CodeFor(added.Id);

                var createdAllocations = targetCharges
                    .Select(c => PaymentAllocation.Create(
                        added.Id,
                        c.Id,
                        visitId,
                        c.UnitPrice!.Value,
                        PaymentAllocationStatuses.Pending,
                        nowLocal))
                    .ToList();
                PaymentAllocationLookup.TryAddAllocations(_db, createdAllocations);

                PaymentEnrollmentResultDto? createdEnrollmentDto = null;
                if (enrollOrder is not null && lockedVisit.PatientId is long enrollPatientId && enrollPatientId > 0)
                {
                    var todayLocal = DateOnly.FromDateTime(nowLocal);
                    var effectiveVisitForPresence = lockedVisit;
                    if (!actor.IsPatient && actor.Doctor is null)
                    {
                        var trackedVisitForStamp = await _db.Visits.FirstOrDefaultAsync(v => v.Id == visitId, ct);
                        if (trackedVisitForStamp is not null)
                        {
                            MedFlow.Application.Services.Visits.VisitPresenceEvaluator.StampPresentToday(trackedVisitForStamp, todayLocal, nowLocal);
                            effectiveVisitForPresence = trackedVisitForStamp;
                        }
                    }

                    var policy = await MedFlow.Application.Services.Policies.CrossDayVisitPolicyReader.LoadAsync(_db, hospitalId, nowUtc, ct);
                    var visitQueues = await _db.QueueEntries.AsNoTracking()
                        .Where(q =>
                            q.VisitId == visitId ||
                            (effectiveVisitForPresence.AppointmentId.HasValue && q.AppointmentId == effectiveVisitForPresence.AppointmentId.Value))
                        .ToListAsync(ct);
                    var isPresentAtCreation = MedFlow.Application.Services.Visits.VisitPresenceEvaluator.IsPresentToday(
                        effectiveVisitForPresence,
                        visitQueues,
                        todayLocal,
                        nowLocal,
                        policy.PresenceTtlMinutes);

                    var createdIntent = new PaymentEnrollmentIntent
                    {
                        PaymentId = added.Id,
                        VisitId = visitId,
                        PatientId = enrollPatientId,
                        DiagnosticOrderId = enrollOrder.Id,
                        RequestedByUserId = actorUserId,
                        RequestedByRole = actor.RoleLabel,
                        IdempotencyKey = $"pei:pay:{added.Id}",
                        PresenceVerifiedDate = isPresentAtCreation ? todayLocal : null,
                        PresenceSessionNo = isPresentAtCreation ? Math.Max(1, effectiveVisitForPresence.PresenceSessionNo) : null,
                        Status = PaymentEnrollmentIntentStatuses.PendingSettlement,
                        RetryCount = 0,
                        // payment_enrollment_intents dùng timestamp without time zone (giờ bệnh viện).
                        CreatedAt = nowLocal,
                        ExpiresAt = nowLocal.AddMinutes(Math.Max(30, _qrOptions.PendingExpiryMinutes + 15)),
                    };
                    PaymentAllocationLookup.TryAddIntent(_db, createdIntent);
                    await _db.SaveChangesAsync(ct);
                    createdEnrollmentDto = await PaymentEnrollmentIntentProcessor.BuildIntentDtoAsync(_db, createdIntent, ct);
                }

                AddAudit(
                    actorUserId,
                    hospitalId,
                    visitId,
                    targetChargeIds,
                    added,
                    amountDue,
                    QrPaymentIssueKind.Created,
                    ipAddress);

                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                return ToDto(
                    lockedVisit,
                    added,
                    account,
                    merchant,
                    amountDue,
                    QrPaymentIssueKind.Created,
                    targetCharges,
                    createdAllocations,
                    assessment.Lines,
                    createdEnrollmentDto);
            }
            catch (DbUpdateException ex) when (PostgresUniqueViolation.Is(ex) || PostgresConcurrencyException.IsUniqueViolation(ex))
            {
                Detach(added);
                DetachCharge(materializedCharge);
                await SafeRollback(tx, ct);
                if (_db is DbContext dbCtx)
                    dbCtx.ChangeTracker.Clear();
                if (recovering)
                    throw new ConflictException(AmountMismatchMessage);
            }
            catch (Exception ex) when (PostgresConcurrencyException.IsDeadlockOrSerializationFailure(ex))
            {
                Detach(added);
                DetachCharge(materializedCharge);
                await SafeRollback(tx, ct);
                if (_db is DbContext dbCtx)
                    dbCtx.ChangeTracker.Clear();
                throw new ConflictException("Yêu cầu tạo mã thanh toán đang có xung đột đồng thời, vui lòng thử lại.");
            }
            catch
            {
                Detach(added);
                DetachCharge(materializedCharge);
                if (!txCommitted)
                    await SafeRollback(tx, ct);
                throw;
            }
        }

        return await IssueAsync(actorUserId, idOrCode, ipAddress, request, recovering: true, ct);
    }

    private async Task<QrActorContext> AuthorizeActorPreVisitAsync(long actorUserId, CancellationToken ct)
    {
        await VisitPaymentAccess.RequireActiveUserAsync(_db, actorUserId, _timeProvider, ct);

        var (roleIds, roleCodes) = await VisitPaymentAccess.LoadActorRolesAsync(_db, actorUserId, ct);

        var isPatient = roleCodes.Contains(RoleNames.Patient);
        var isDoctor = roleCodes.Contains(RoleNames.Doctor);
        var isReception = roleCodes.Contains(RoleNames.Reception);
        var isPharmacy = roleCodes.Contains(RoleNames.PharmacyStaff);
        var isHospitalAdmin = roleCodes.Contains(RoleNames.HospitalAdmin);

        if (isReception || isHospitalAdmin || isPharmacy)
        {
            var permSet = await VisitPaymentAccess.LoadRolePermissionCodesAsync(_db, roleIds, ct);
            var hasPerm = permSet.Contains(PermissionCodes.PaymentView) ||
                          permSet.Contains(PermissionCodes.PatientCheckin) ||
                          permSet.Contains(PermissionCodes.PaymentCollect) ||
                          permSet.Contains(PermissionCodes.HospitalConfig);
            if (!hasPerm)
                throw new ForbiddenException(ForbiddenMessage);

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

            var roleLabel = isHospitalAdmin
                ? RoleNames.HospitalAdmin
                : isPharmacy ? RoleNames.PharmacyStaff : RoleNames.Reception;
            return new QrActorContext(false, roleLabel, staff.HospitalId, null, [])
            {
                IsPharmacyStaff = string.Equals(roleLabel, RoleNames.PharmacyStaff, StringComparison.OrdinalIgnoreCase),
            };
        }

        if (isDoctor)
        {
            var (staff, doctor) = await VisitPaymentAccess.RequireActiveDoctorStaffAsync(
                _db,
                actorUserId,
                ForbiddenMessage,
                ct);
            return new QrActorContext(false, RoleNames.Doctor, staff.HospitalId, doctor, []);
        }

        if (!isPatient)
            throw new ForbiddenException(ForbiddenMessage);

        var owned = await VisitPaymentAccess.LoadOwnedPatientIdsAsync(_db, actorUserId, _timeProvider, ct);
        return new QrActorContext(true, RoleNames.Patient, null, null, owned);
    }

    public const string QrAttemptExpiredMessage =
        "[QR_ATTEMPT_EXPIRED] Mã QR thanh toán của yêu cầu này đã hết hạn hiệu lực. Vui lòng tạo yêu cầu thanh toán mới.";

    private async Task<GenerateQrPaymentResponseDto> ValidateAndBuildIdempotentQrReplayAsync(
        Visit visit,
        long hospitalId,
        Payment existingPayment,
        IReadOnlyList<long>? requestedChargeIds,
        DateTime nowLocal,
        DateTime nowUtc,
        Func<CancellationToken, Task> commitBeforeThrowAsync,
        CancellationToken ct)
    {
        if (existingPayment.VisitId != visit.Id ||
            !string.Equals(existingPayment.PaymentMethod ?? PaymentMethods.VietQr, PaymentMethods.VietQr, StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException(IdempotencyMismatchMessage);
        }

        // D2 / BR-BF02-06: chỉ định CLS do hệ thống tự chọn nên không còn là một phần payload
        // idempotency của client — chỉ so khớp phương thức + tập khoản phí.
        var existingIntents = await PaymentAllocationLookup.TryLoadNoTrackingIntentsForVisitAsync(_db, visit.Id, ct);
        var existingIntent = existingIntents.FirstOrDefault(i => i.PaymentId == existingPayment.Id);

        var persistedAllocs = await PaymentAllocationLookup.TryLoadNoTrackingForVisitAsync(_db, visit.Id, ct);
        var existingAllocs = persistedAllocs
            .Where(pa => pa.PaymentId == existingPayment.Id)
            .OrderBy(pa => pa.VisitChargeId)
            .ToList();

        var existingChargeIds = existingAllocs.Count > 0
            ? existingAllocs.Select(pa => pa.VisitChargeId).Distinct().OrderBy(id => id).ToList()
            : (existingPayment.VisitChargeId.HasValue ? [existingPayment.VisitChargeId.Value] : []);

        if (requestedChargeIds is not null && !existingChargeIds.SequenceEqual(requestedChargeIds))
        {
            throw new ConflictException(IdempotencyMismatchMessage);
        }

        var normalizedStatus = string.IsNullOrWhiteSpace(existingPayment.Status)
            ? PendingStatus
            : existingPayment.Status.Trim().ToUpperInvariant();

        if (normalizedStatus is PaymentStatuses.Cancelled
            or PaymentStatuses.Expired
            or SePayWebhookService.MismatchPaymentStatus)
        {
            await commitBeforeThrowAsync(ct);
            throw new ConflictException(
                $"[QR_ATTEMPT_CLOSED] Yêu cầu mã QR này đã đóng ({normalizedStatus}) và không còn hiệu lực thanh toán. Vui lòng tạo mã QR mới.");
        }

        if (normalizedStatus == PendingStatus)
        {
            var expiresAtLocal = (existingPayment.CreatedAt ?? nowLocal).AddMinutes(_qrOptions.PendingExpiryMinutes);
            if (expiresAtLocal <= nowLocal)
            {
                var trackedPayment = await _db.Payments.FirstOrDefaultAsync(p => p.Id == existingPayment.Id, ct);
                var trackedAllocs = await PaymentAllocationLookup.TryLoadTrackedForVisitAsync(_db, visit.Id, ct);
                var trackedIntents = await PaymentAllocationLookup.TryLoadTrackedIntentsForVisitAsync(_db, visit.Id, ct);

                if (trackedPayment is not null)
                    trackedPayment.Status = PaymentStatuses.Expired;

                foreach (var pa in trackedAllocs.Where(pa =>
                             pa.PaymentId == existingPayment.Id &&
                             string.Equals(pa.Status, PaymentAllocationStatuses.Pending, StringComparison.OrdinalIgnoreCase)))
                {
                    pa.Status = PaymentAllocationStatuses.Expired;
                }

                foreach (var pi in trackedIntents.Where(i =>
                             i.PaymentId == existingPayment.Id &&
                             string.Equals(i.Status, PaymentEnrollmentIntentStatuses.PendingSettlement, StringComparison.OrdinalIgnoreCase)))
                {
                    pi.Status = PaymentEnrollmentIntentStatuses.Expired;
                    pi.OutcomeCode = PaymentEnrollmentIntentOutcomeCodes.IntentExpired;
                    pi.ProcessedAt = HospitalClock.EnsureHospitalLocal(nowUtc);
                }

                await _db.SaveChangesAsync(ct);
                await commitBeforeThrowAsync(ct);
                throw new ConflictException(QrAttemptExpiredMessage);
            }
        }
        else if (normalizedStatus != PaymentStatuses.Paid)
        {
            await commitBeforeThrowAsync(ct);
            throw new ConflictException(
                $"[QR_ATTEMPT_CLOSED] Yêu cầu mã QR này đã đóng ({normalizedStatus}) và không còn hiệu lực thanh toán. Vui lòng tạo mã QR mới.");
        }

        var charges = await _db.VisitCharges.AsNoTracking()
            .Where(c => c.VisitId == visit.Id && existingChargeIds.Contains(c.Id))
            .OrderBy(c => c.Id)
            .ToListAsync(ct);

        if (normalizedStatus == PendingStatus)
        {
            var allPayments = await _db.Payments.AsNoTracking()
                .Where(p => p.VisitId == visit.Id)
                .OrderBy(p => p.Id)
                .ToListAsync(ct);
            var (effectiveAllocs, _) = PaymentAllocationLookup.BuildEffectiveAllocations(visit.Id, allPayments, persistedAllocs);
            var targetChargeSet = existingChargeIds.ToHashSet();

            var targetInvalidOrSettled =
                IsBlocked(visit.Status) ||
                (existingChargeIds.Count > 0 && charges.Count != existingChargeIds.Count) ||
                charges.Any(c =>
                {
                    var cs = string.IsNullOrWhiteSpace(c.Status)
                        ? VisitChargeStatuses.Active
                        : c.Status.Trim().ToUpperInvariant();
                    return cs != VisitChargeStatuses.Active;
                }) ||
                effectiveAllocs.Any(a =>
                    targetChargeSet.Contains(a.VisitChargeId) &&
                    string.Equals(a.Status, PaymentAllocationStatuses.Paid, StringComparison.OrdinalIgnoreCase));

            if (!targetInvalidOrSettled)
            {
                foreach (var c in charges)
                {
                    if (await IsDiagnosticOrderIneligibleForPendingQrAsync(c, ct))
                    {
                        targetInvalidOrSettled = true;
                        break;
                    }
                }
            }

            if (targetInvalidOrSettled)
            {
                var trackedPayment = await _db.Payments.FirstOrDefaultAsync(p => p.Id == existingPayment.Id, ct);
                var trackedAllocs = await PaymentAllocationLookup.TryLoadTrackedForVisitAsync(_db, visit.Id, ct);
                var trackedIntents = await PaymentAllocationLookup.TryLoadTrackedIntentsForVisitAsync(_db, visit.Id, ct);

                if (trackedPayment is not null)
                    trackedPayment.Status = PaymentStatuses.Cancelled;

                foreach (var pa in trackedAllocs.Where(pa =>
                             pa.PaymentId == existingPayment.Id &&
                             string.Equals(pa.Status, PaymentAllocationStatuses.Pending, StringComparison.OrdinalIgnoreCase)))
                {
                    pa.Status = PaymentAllocationStatuses.Cancelled;
                }

                foreach (var pi in trackedIntents.Where(i =>
                             i.PaymentId == existingPayment.Id &&
                             string.Equals(i.Status, PaymentEnrollmentIntentStatuses.PendingSettlement, StringComparison.OrdinalIgnoreCase)))
                {
                    pi.Status = PaymentEnrollmentIntentStatuses.Cancelled;
                    pi.OutcomeCode = "PAYMENT_NOT_SETTLED";
                    pi.ProcessedAt = HospitalClock.EnsureHospitalLocal(nowUtc);
                }

                await _db.SaveChangesAsync(ct);
                await commitBeforeThrowAsync(ct);
                throw new ConflictException(
                    "[QR_ATTEMPT_CLOSED] Yêu cầu mã QR này đã đóng (CANCELLED) do khoản phí mục tiêu không còn hiệu lực hoặc đã được tất toán. Vui lòng tạo mã QR mới.");
            }
        }

        var assessment = await VisitChargeAssessor.AssessAsync(_db, _timeProvider, visit, ct);
        var account = await VietQrAccountPolicyReader.ReadAsync(
            _db,
            hospitalId,
            _timeProvider.GetUtcNow().UtcDateTime,
            ct);
        var hospital = await _db.Hospitals.AsNoTracking()
            .FirstOrDefaultAsync(h => h.Id == hospitalId, ct);
        var merchant = VietQrPayloadBuilder.MerchantName(hospital?.Name);
        var amount = existingPayment.Amount ?? charges.Sum(c => c.UnitPrice ?? 0m);
        var issueKind = normalizedStatus == PaymentStatuses.Paid
            ? QrPaymentIssueKind.AlreadyPaid
            : QrPaymentIssueKind.Reused;

        var replayEnrollmentDto = existingIntent is not null
            ? await PaymentEnrollmentIntentProcessor.BuildIntentDtoAsync(_db, existingIntent, ct)
            : null;

        return ToDto(
            visit,
            existingPayment,
            account,
            merchant,
            amount,
            issueKind,
            charges,
            existingAllocs,
            assessment.Lines,
            replayEnrollmentDto);
    }

    private async Task<bool> IsDiagnosticOrderIneligibleForPendingQrAsync(VisitCharge charge, CancellationToken ct)
    {
        if (!charge.DiagnosticOrderId.HasValue)
            return false;

        try
        {
            var order = await _db.DiagnosticOrders.AsNoTracking()
                .FirstOrDefaultAsync(o => o.Id == charge.DiagnosticOrderId.Value, ct);

            return order is null ||
                   string.Equals(order.Status?.Trim(), "CANCELLED", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(order.SelectionStatus?.Trim(), DiagnosticSelectionStatuses.Declined, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(order.SelectionStatus?.Trim(), DiagnosticSelectionStatuses.SkippedAfterPaid, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(order.SelectionStatus?.Trim(), DiagnosticSelectionStatuses.ReviewRequired, StringComparison.OrdinalIgnoreCase);
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    private async Task<(List<VisitCharge> TargetCharges, decimal TotalAmount)> ResolveTargetChargesAndAmountAsync(
        Visit visit,
        VisitChargeAssessment assessment,
        IReadOnlyList<VisitCharge> trackedCharges,
        IReadOnlyList<long>? explicitChargeIds,
        bool usesStageBAllocatedMode,
        bool allowCashierToQrSwitch,
        bool pharmacyMedicineOnly,
        DateTime nowLocal,
        CancellationToken ct)
    {
        if (explicitChargeIds is { Count: > 0 })
        {
            if (IsBlocked(visit.Status))
                throw new ConflictException(VisitBlockedMessage);

            var selectedCharges = new List<VisitCharge>(explicitChargeIds.Count);
            decimal total = 0m;

            foreach (var requestedId in explicitChargeIds)
            {
                var target = trackedCharges.FirstOrDefault(c => c.Id == requestedId);
                if (target is null)
                    throw new NotFoundException("Không tìm thấy khoản phí thuộc lượt khám này.");

                var chargeStatus = string.IsNullOrWhiteSpace(target.Status)
                    ? VisitChargeStatuses.Active
                    : target.Status.Trim().ToUpperInvariant();

                if (chargeStatus != VisitChargeStatuses.Active)
                    throw new ConflictException(ChargeCancelledMessage);

                // Owner decision D1: MEDICINE charges are paid only at the pharmacy counter.
                if (pharmacyMedicineOnly && !VisitPaymentAccess.IsMedicineCharge(target))
                    throw new ForbiddenException(PharmacyMedicineOnlyMessage);
                if (!pharmacyMedicineOnly && VisitPaymentAccess.IsMedicineCharge(target))
                    throw new ForbiddenException(PharmacyCounterOnlyMessage);

                await EnsureDiagnosticOrderEligibleForPaymentAsync(target, ct);

                var line = assessment.Lines.FirstOrDefault(l => l.ChargeId == target.Id);
                if (line is null ||
                    line.ChargeDataStatus is VisitChargesService.ChargeDataInvalid or VisitChargesService.ChargeDataUnpriced ||
                    line.LineSettlementStatus is VisitChargesService.SettlementMismatch or VisitChargesService.SettlementUndetermined)
                {
                    throw new ConflictException(UndeterminedMessage);
                }

                if (line.LineSettlementStatus == VisitChargesService.SettlementPaid)
                {
                    throw new ConflictException(usesStageBAllocatedMode ? ChargeAlreadyPaidMessage : NothingDueMessage);
                }

                if (line.Payable == false ||
                    line.UnitPrice == 0m ||
                    line.LineSettlementStatus == VisitChargeAssessor.LineSettlementWaived)
                {
                    throw new ConflictException(NothingDueMessage);
                }

                if (line.LineSettlementStatus == VisitChargeAssessor.LineSettlementCancelled ||
                    line.LineSettlementStatus == VisitChargeAssessor.LineSettlementPaidNotPerformed)
                {
                    throw new ConflictException(ChargeCancelledMessage);
                }

                if (!allowCashierToQrSwitch &&
                    IsExaminationCharge(target) &&
                    string.Equals(assessment.PaymentMethod, "CASHIER", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ConflictException(CashierMessage);
                }

                total += RequireValidIntegerVnd(line.UnitPrice);
                selectedCharges.Add(target);
            }

            RequireValidIntegerVnd(total);
            return (selectedCharges, total);
        }

        // No explicit VisitChargeId / VisitChargeIds requested
        if (trackedCharges.Count == 0)
        {
            // Pharmacy staff cannot materialise the legacy examination charge — they bill medicine only.
            if (pharmacyMedicineOnly)
                throw new ConflictException(NothingDueMessage);

            var derivedAmount = RequireAmount(visit, assessment);
            var derivedLine = assessment.Lines.FirstOrDefault();

            var materialized = new VisitCharge
            {
                VisitId = visit.Id,
                ServiceId = derivedLine?.ServiceId,
                DiagnosticOrderId = null,
                ChargeType = VisitChargeTypes.Examination,
                UnitPrice = derivedAmount,
                PaymentRequirement = "REQUIRED",
                Status = VisitChargeStatuses.Active,
                CreatedAt = nowLocal,
            };

            return ([materialized], derivedAmount);
        }

        if (pharmacyMedicineOnly)
        {
            var medicineLines = assessment.Lines
                .Where(l => string.Equals(l.ChargeType, VisitChargeTypes.Medicine, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (medicineLines.Count == 0)
                throw new ConflictException(NothingDueMessage);

            if (medicineLines.Any(l =>
                    l.ChargeDataStatus is VisitChargesService.ChargeDataInvalid or VisitChargesService.ChargeDataUnpriced ||
                    l.LineSettlementStatus is VisitChargesService.SettlementMismatch or VisitChargesService.SettlementUndetermined))
            {
                throw new ConflictException(UndeterminedMessage);
            }

            if (IsBlocked(visit.Status))
                throw new ConflictException(VisitBlockedMessage);

            var unpaidMedicineLines = medicineLines
                .Where(l =>
                    l.ChargeId.HasValue &&
                    string.Equals(l.ChargeStatus, VisitChargeStatuses.Active, StringComparison.OrdinalIgnoreCase) &&
                    l.Payable == true &&
                    l.UnitPrice is > 0m &&
                    l.LineSettlementStatus == VisitChargesService.SettlementUnpaid)
                .ToList();

            if (unpaidMedicineLines.Count == 0)
                throw new ConflictException(NothingDueMessage);

            if (unpaidMedicineLines.Count > 1)
                throw new ConflictException(MultipleUnpaidChargesMessage);

            var singleMedicineLine = unpaidMedicineLines[0];
            var singleMedicineCharge = trackedCharges.First(c => c.Id == singleMedicineLine.ChargeId!.Value);
            return ([singleMedicineCharge], RequireValidIntegerVnd(singleMedicineLine.UnitPrice));
        }

        if (assessment.HasUnpricedPayableCharges ||
            assessment.HasInvalidCharges ||
            assessment.AmountDue is null ||
            assessment.SettlementStatus is VisitChargesService.SettlementMismatch or VisitChargesService.SettlementUndetermined)
        {
            throw new ConflictException(UndeterminedMessage);
        }

        if (assessment.SettlementStatus == VisitChargesService.SettlementPaid || assessment.AmountDue == 0m)
            throw new ConflictException(NothingDueMessage);

        if (IsBlocked(visit.Status))
            throw new ConflictException(VisitBlockedMessage);

        var unpaidLines = assessment.Lines
            .Where(l =>
                l.ChargeId.HasValue &&
                string.Equals(l.ChargeStatus, VisitChargeStatuses.Active, StringComparison.OrdinalIgnoreCase) &&
                l.Payable == true &&
                l.UnitPrice is > 0m &&
                l.LineSettlementStatus == VisitChargesService.SettlementUnpaid)
            .ToList();

        var nonMedicineUnpaidLines = unpaidLines
            .Where(l => !string.Equals(l.ChargeType, VisitChargeTypes.Medicine, StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Owner decision D1: a non-pharmacy actor can never settle medicine money over QR.
        if (nonMedicineUnpaidLines.Count == 0 && unpaidLines.Count > 0)
            throw new ConflictException(PharmacyCounterOnlyMessage);

        unpaidLines = nonMedicineUnpaidLines;

        if (unpaidLines.Count == 0)
            throw new ConflictException(NothingDueMessage);

        if (unpaidLines.Count > 1)
            throw new ConflictException(MultipleUnpaidChargesMessage);

        var singleLine = unpaidLines[0];
        var singleCharge = trackedCharges.First(c => c.Id == singleLine.ChargeId!.Value);

        await EnsureDiagnosticOrderEligibleForPaymentAsync(singleCharge, ct);

        if (!allowCashierToQrSwitch &&
            IsExaminationCharge(singleCharge) &&
            string.Equals(assessment.PaymentMethod, "CASHIER", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException(CashierMessage);
        }

        var singleAmount = RequireValidIntegerVnd(singleLine.UnitPrice);
        return ([singleCharge], singleAmount);
    }

    private async Task EnsureDiagnosticOrderEligibleForPaymentAsync(VisitCharge charge, CancellationToken ct)
    {
        if (!charge.DiagnosticOrderId.HasValue)
            return;

        try
        {
            var order = await _db.DiagnosticOrders.AsNoTracking()
                .FirstOrDefaultAsync(o => o.Id == charge.DiagnosticOrderId.Value, ct);

            if (order is null ||
                string.Equals(order.Status?.Trim(), "CANCELLED", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(order.SelectionStatus?.Trim(), DiagnosticSelectionStatuses.Declined, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(order.SelectionStatus?.Trim(), DiagnosticSelectionStatuses.SkippedAfterPaid, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(order.SelectionStatus?.Trim(), DiagnosticSelectionStatuses.ReviewRequired, StringComparison.OrdinalIgnoreCase))
            {
                throw new ConflictException(DiagnosticCancelledMessage);
            }
        }
        catch (NotSupportedException)
        {
        }
    }

    private async Task<bool> PromoteUndecidedOrdersAsync(IReadOnlyList<VisitCharge> selectedCharges, CancellationToken ct)
    {
        var orderIds = selectedCharges
            .Where(c => c.DiagnosticOrderId.HasValue)
            .Select(c => c.DiagnosticOrderId!.Value)
            .Distinct()
            .ToList();

        if (orderIds.Count == 0)
            return false;

        try
        {
            var orders = await _db.DiagnosticOrders
                .Where(o => orderIds.Contains(o.Id))
                .ToListAsync(ct);

            var promoted = false;
            foreach (var order in orders)
            {
                if (string.Equals(order.SelectionStatus?.Trim(), DiagnosticSelectionStatuses.Undecided, StringComparison.OrdinalIgnoreCase))
                {
                    order.SelectionStatus = DiagnosticSelectionStatuses.Accepted;
                    promoted = true;
                }
            }

            return promoted;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    private async Task ExpireStaleOverlappingPendingAsync(
        IReadOnlyList<Payment> trackedPayments,
        IReadOnlyList<PaymentAllocation> trackedAllocations,
        IReadOnlyList<PaymentEnrollmentIntent> trackedIntents,
        IReadOnlySet<long> targetChargeSet,
        bool includeUnlinkedLegacyOnExam,
        DateTime nowLocal,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var cutoff = nowLocal.AddMinutes(-_qrOptions.PendingExpiryMinutes);
        var overlapping = FindOverlappingPendingPayments(
            trackedPayments,
            trackedAllocations,
            targetChargeSet,
            includeUnlinkedLegacyOnExam);

        var stale = overlapping
            .Where(p => p.CreatedAt != null && p.CreatedAt < cutoff)
            .ToList();

        if (stale.Count == 0)
            return;

        var staleIds = stale.Select(p => p.Id).ToHashSet();
        foreach (var payment in stale)
            payment.Status = PaymentStatuses.Expired;

        foreach (var pa in trackedAllocations.Where(pa => staleIds.Contains(pa.PaymentId) &&
                                                          string.Equals(pa.Status, PaymentAllocationStatuses.Pending, StringComparison.OrdinalIgnoreCase)))
        {
            pa.Status = PaymentAllocationStatuses.Expired;
        }

        foreach (var pi in trackedIntents.Where(i => staleIds.Contains(i.PaymentId) &&
                                                     string.Equals(i.Status, PaymentEnrollmentIntentStatuses.PendingSettlement, StringComparison.OrdinalIgnoreCase)))
        {
            pi.Status = PaymentEnrollmentIntentStatuses.Expired;
            pi.OutcomeCode = PaymentEnrollmentIntentOutcomeCodes.IntentExpired;
            pi.ProcessedAt = HospitalClock.EnsureHospitalLocal(nowUtc);
        }

        await _db.SaveChangesAsync(ct);
    }

    private static List<Payment> FindOverlappingPendingPayments(
        IReadOnlyList<Payment> trackedPayments,
        IReadOnlyList<PaymentAllocation> trackedAllocations,
        IReadOnlySet<long> targetChargeSet,
        bool includeUnlinkedLegacyOnExam)
    {
        var allocChargeIdsByPayment = trackedAllocations
            .Where(pa => string.Equals(pa.Status, PaymentAllocationStatuses.Pending, StringComparison.OrdinalIgnoreCase))
            .GroupBy(pa => pa.PaymentId)
            .ToDictionary(g => g.Key, g => g.Select(pa => pa.VisitChargeId).ToHashSet());

        var result = new List<Payment>();
        foreach (var p in trackedPayments)
        {
            if (!IsPending(p.Status))
                continue;

            if (allocChargeIdsByPayment.TryGetValue(p.Id, out var allocSet))
            {
                if (allocSet.Overlaps(targetChargeSet))
                    result.Add(p);
                continue;
            }

            if (p.VisitChargeId.HasValue && targetChargeSet.Contains(p.VisitChargeId.Value))
            {
                result.Add(p);
            }
            else if (includeUnlinkedLegacyOnExam &&
                     !p.VisitChargeId.HasValue &&
                     !string.Equals(p.PaymentScope, PaymentScopes.Allocated, StringComparison.OrdinalIgnoreCase))
            {
                result.Add(p);
            }
        }

        return result;
    }

    private static bool HasExactSameTargetCharges(
        Payment pending,
        IReadOnlyList<PaymentAllocation> trackedAllocations,
        IReadOnlyList<long> sortedTargetChargeIds,
        bool includeUnlinkedLegacyOnExam)
    {
        var pendingAllocs = trackedAllocations
            .Where(pa => pa.PaymentId == pending.Id &&
                         string.Equals(pa.Status, PaymentAllocationStatuses.Pending, StringComparison.OrdinalIgnoreCase))
            .Select(pa => pa.VisitChargeId)
            .Distinct()
            .OrderBy(id => id)
            .ToList();

        if (pendingAllocs.Count > 0)
            return pendingAllocs.SequenceEqual(sortedTargetChargeIds);

        if (pending.VisitChargeId.HasValue)
            return sortedTargetChargeIds.Count == 1 && sortedTargetChargeIds[0] == pending.VisitChargeId.Value;

        return includeUnlinkedLegacyOnExam && sortedTargetChargeIds.Count == 1;
    }

    private static void CancelPendingAttempt(
        Payment pending,
        IReadOnlyList<PaymentAllocation> trackedAllocations,
        IReadOnlyList<PaymentEnrollmentIntent> trackedIntents,
        DateTime nowUtc)
    {
        pending.Status = PaymentStatuses.Cancelled;
        foreach (var pa in trackedAllocations.Where(pa => pa.PaymentId == pending.Id &&
                                                          string.Equals(pa.Status, PaymentAllocationStatuses.Pending, StringComparison.OrdinalIgnoreCase)))
        {
            pa.Status = PaymentAllocationStatuses.Cancelled;
        }

        foreach (var pi in trackedIntents.Where(i => i.PaymentId == pending.Id &&
                                                     string.Equals(i.Status, PaymentEnrollmentIntentStatuses.PendingSettlement, StringComparison.OrdinalIgnoreCase)))
        {
            pi.Status = PaymentEnrollmentIntentStatuses.Cancelled;
            pi.OutcomeCode = "PAYMENT_CANCELLED";
            pi.ProcessedAt = HospitalClock.EnsureHospitalLocal(nowUtc);
        }
    }

    internal static decimal RequireAmount(Visit visit, VisitChargeAssessment assessment)
    {
        if (assessment.HasUnpricedPayableCharges
            || assessment.HasInvalidCharges
            || assessment.AmountDue is null
            || assessment.SettlementStatus != VisitChargesService.SettlementUnpaid)
            throw new ConflictException(UndeterminedMessage);

        if (assessment.AmountDue == 0)
            throw new ConflictException(NothingDueMessage);

        if (IsBlocked(visit.Status))
            throw new ConflictException(VisitBlockedMessage);

        if (string.Equals(assessment.PaymentMethod, "CASHIER", StringComparison.OrdinalIgnoreCase))
            throw new ConflictException(CashierMessage);

        return RequireValidIntegerVnd(assessment.AmountDue);
    }

    internal static decimal RequireValidIntegerVnd(decimal? rawAmount)
    {
        if (rawAmount is not decimal amount)
            throw new ConflictException(UndeterminedMessage);

        if (amount != decimal.Truncate(amount) || amount < 1 || amount > MaxVndAmount)
            throw new ConflictException(NotIntegerMessage);

        return amount;
    }

    private void AddAudit(
        long actorUserId,
        long hospitalId,
        long visitId,
        IReadOnlyList<long> visitChargeIds,
        Payment payment,
        decimal amount,
        string issueKind,
        string? ipAddress)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actorUserId,
            HospitalId = hospitalId,
            Action = AuditAction,
            EntityType = PaymentEntityType,
            EntityId = payment.Id.ToString(CultureInfo.InvariantCulture),
            ChangeData = JsonSerializer.Serialize(new
            {
                visitId,
                visitChargeId = visitChargeIds.Count == 1 ? visitChargeIds[0] : (long?)null,
                visitChargeIds,
                paymentId = payment.Id,
                paymentCode = payment.PaymentCode,
                paymentScope = payment.PaymentScope,
                amount,
                currency = VisitChargesService.CurrencyVnd,
                hospitalId,
                issueKind,
            }),
            IpAddress = IPAddress.TryParse(ipAddress, out var ip) ? ip : null,
            OccurredAt = _timeProvider.GetUtcNow().UtcDateTime,
        });
    }

    private GenerateQrPaymentResponseDto ToDto(
        Visit visit,
        Payment payment,
        VietQrAccount account,
        string merchantName,
        decimal amount,
        string issueKind,
        IReadOnlyList<VisitCharge> charges,
        IReadOnlyList<PaymentAllocation> allocations,
        IReadOnlyList<VisitChargeLineDto> assessedLines,
        PaymentEnrollmentResultDto? enrollment = null)
    {
        var code = payment.PaymentCode ?? "";
        var nowLocal = Clock();
        var expiresAtLocal = (payment.CreatedAt ?? nowLocal).AddMinutes(_qrOptions.PendingExpiryMinutes);
        var payload = IsPending(payment.Status) && expiresAtLocal > nowLocal && !string.IsNullOrWhiteSpace(code)
            ? VietQrPayloadBuilder.Build(account, amount, code, merchantName)
            : string.Empty;
        var lineByChargeId = assessedLines
            .Where(l => l.ChargeId.HasValue)
            .ToDictionary(l => l.ChargeId!.Value);

        var paymentAllocs = allocations
            .Where(pa => pa.PaymentId == payment.Id)
            .OrderBy(pa => pa.VisitChargeId)
            .ToList();

        var dtoAllocations = paymentAllocs.Count > 0
            ? paymentAllocs.Select(pa =>
            {
                lineByChargeId.TryGetValue(pa.VisitChargeId, out var line);
                var charge = charges.FirstOrDefault(c => c.Id == pa.VisitChargeId);
                return new PaymentAllocationItemDto(
                    pa.Id > 0 ? pa.Id : null,
                    pa.VisitChargeId,
                    line?.ChargeType ?? charge?.ChargeType ?? VisitChargeTypes.Examination,
                    line?.DiagnosticOrderId ?? charge?.DiagnosticOrderId,
                    line?.ServiceName,
                    pa.AllocatedAmount,
                    pa.Status,
                    line?.PrescriptionId ?? charge?.PrescriptionId,
                    line?.MedicineId ?? charge?.MedicineId,
                    line?.MedicineName,
                    line?.Quantity ?? charge?.Quantity,
                    line?.MedicineUnitPrice ?? charge?.MedicineUnitPrice);
            }).ToList()
            : charges.Select(c =>
            {
                lineByChargeId.TryGetValue(c.Id, out var line);
                return new PaymentAllocationItemDto(
                    null,
                    c.Id,
                    line?.ChargeType ?? c.ChargeType,
                    line?.DiagnosticOrderId ?? c.DiagnosticOrderId,
                    line?.ServiceName,
                    c.UnitPrice ?? amount,
                    PaymentAllocationLookup.MapPaymentStatusToAllocationStatus(payment.Status),
                    line?.PrescriptionId ?? c.PrescriptionId,
                    line?.MedicineId ?? c.MedicineId,
                    line?.MedicineName,
                    line?.Quantity ?? c.Quantity,
                    line?.MedicineUnitPrice ?? c.MedicineUnitPrice);
            }).ToList();

        var singleChargeId = charges.Count == 1 ? charges[0].Id : payment.VisitChargeId;

        return new GenerateQrPaymentResponseDto(
            visit.Id,
            visit.VisitCode,
            payment.Id,
            code,
            amount,
            VisitChargesService.CurrencyVnd,
            payment.Status ?? PendingStatus,
            payload,
            account.Bin,
            account.AccountNumber,
            account.AccountName,
            code,
            issueKind,
            singleChargeId,
            dtoAllocations,
            expiresAtLocal,
            enrollment);
    }

    private void Detach(Payment? payment)
    {
        if (payment is not null && _db is DbContext context)
            context.Entry(payment).State = EntityState.Detached;
    }

    private void DetachCharge(VisitCharge? charge)
    {
        if (charge is not null && _db is DbContext context)
            context.Entry(charge).State = EntityState.Detached;
    }

    private static async Task SafeRollback(IDbContextTransaction tx, CancellationToken ct)
    {
        try
        {
            await tx.RollbackAsync(ct);
        }
        catch (Exception)
        {
        }
    }

    private DateTime Clock()
        => HospitalClock.GetNow(_timeProvider);

    private static string CodeFor(long paymentId) =>
        "MF" + paymentId.ToString(CultureInfo.InvariantCulture);

    private static bool IsExaminationCharge(VisitCharge charge) =>
        string.IsNullOrWhiteSpace(charge.ChargeType)
            ? !charge.DiagnosticOrderId.HasValue
            : string.Equals(charge.ChargeType.Trim(), VisitChargeTypes.Examination, StringComparison.OrdinalIgnoreCase);

    private static bool IsPending(string? status) =>
        string.Equals(status?.Trim(), PendingStatus, StringComparison.OrdinalIgnoreCase);

    private static bool IsBlocked(string? status) =>
        string.Equals(status?.Trim(), PaymentStatuses.Cancelled, StringComparison.OrdinalIgnoreCase)
        || string.Equals(status?.Trim(), "NO_SHOW", StringComparison.OrdinalIgnoreCase);

    private sealed record QrActorContext(
        bool IsPatient,
        string RoleLabel,
        long? HospitalId,
        Doctor? Doctor,
        HashSet<long> OwnedPatientIds)
    {
        public bool IsPharmacyStaff { get; init; }
    }
}
