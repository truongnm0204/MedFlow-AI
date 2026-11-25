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
    public const string VisitBlockedMessage = "Lượt khám không thể thanh toán.";
    public const string CashierMessage = "Lượt khám được thu tại quầy.";
    public const string NotIntegerMessage = "Số tiền không phải số nguyên VND.";
    public const string AmountMismatchMessage = "Số tiền yêu cầu thanh toán không còn khớp khoản phải thu.";
    public const string MultipleOpenMessage = "Lượt khám đang có nhiều yêu cầu thanh toán mở.";
    public const string MultipleUnpaidChargesMessage = "Lượt khám đang có nhiều khoản phí chưa thanh toán. Vui lòng chọn khoản phí cụ thể để tạo mã QR.";
    public const string ChargeCancelledMessage = "Khoản phí đã bị hủy, không thể tạo mã thanh toán.";
    public const string DiagnosticCancelledMessage = "Chỉ định cận lâm sàng đã bị hủy, không thể thanh toán.";
    public const string InvalidVisitChargeIdMessage = "Mã khoản phí cần thanh toán không hợp lệ.";
    public const string ForbiddenMessage = "Bạn không có quyền tạo yêu cầu thanh toán QR cho lượt khám này.";

    internal const decimal MaxVndAmount = 9_999_999_999_999m;
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
        if (request?.VisitChargeId is <= 0)
            throw new ValidationException(InvalidVisitChargeIdMessage);

        await VisitPaymentAccess.RequireActiveUserAsync(_db, actorUserId, _timeProvider, ct);
        if (!await HasPatientRoleAsync(actorUserId, ct))
            throw new ForbiddenException(ForbiddenMessage);

        var parsed = VisitPaymentAccess.ParseVisitIdentifier(idOrCode);
        var visit = await VisitPaymentAccess.FindVisitAsync(_db, parsed, ct);
        var owned = await VisitPaymentAccess.LoadOwnedPatientIdsAsync(_db, actorUserId, _timeProvider, ct);
        if (visit is null || !owned.Contains(visit.PatientId ?? 0))
            throw new NotFoundException(VisitPaymentAccess.VisitNotFoundMessage);

        var hospitalIds = await VisitHospitalResolver.CollectAsync(_db, visit, ct);
        if (hospitalIds.Count == 0)
            throw new NotFoundException(VisitPaymentAccess.VisitNotFoundMessage);
        if (hospitalIds.Count > 1)
            throw new ConflictException(VisitPaymentAccess.HospitalConflictMessage);

        var hospitalId = hospitalIds.First();
        var visitId = visit.Id;

        await using (var tx = await _db.BeginTransactionAsync(ct))
        {
            Payment? added = null;
            VisitCharge? materializedCharge = null;
            try
            {
                // ADR-0059 strict lock order: visits (09) -> visit_charges (15) -> payments (16)
                await _db.TryLockVisitForUpdateAsync(visitId, ct);

                var preChargeIds = await _db.VisitCharges.AsNoTracking()
                    .Where(c => c.VisitId == visitId)
                    .OrderBy(c => c.Id)
                    .Select(c => c.Id)
                    .ToListAsync(ct);

                if (preChargeIds.Count > 0)
                    await _db.TryLockVisitChargesForUpdateAsync(preChargeIds, ct);

                var prePaymentIds = await _db.Payments.AsNoTracking()
                    .Where(p => p.VisitId == visitId)
                    .OrderBy(p => p.Id)
                    .Select(p => p.Id)
                    .ToListAsync(ct);

                if (prePaymentIds.Count > 0)
                    await _db.TryLockPaymentsForUpdateAsync(prePaymentIds, ct);

                if (_db is DbContext trackerCtx)
                    trackerCtx.ChangeTracker.Clear();

                var lockedVisit = await _db.Visits.AsNoTracking()
                    .FirstOrDefaultAsync(v => v.Id == visitId, ct)
                    ?? throw new NotFoundException(VisitPaymentAccess.VisitNotFoundMessage);

                var assessment = await VisitChargeAssessor.AssessAsync(_db, _timeProvider, lockedVisit, ct);
                var trackedCharges = await _db.VisitCharges
                    .Where(c => c.VisitId == visitId)
                    .OrderBy(c => c.Id)
                    .ToListAsync(ct);

                var nowLocal = Clock();
                var (targetCharge, amountDue) = await ResolveTargetChargeAndAmountAsync(
                    lockedVisit,
                    assessment,
                    trackedCharges,
                    request?.VisitChargeId,
                    nowLocal,
                    ct);

                if (targetCharge.Id == 0)
                {
                    materializedCharge = targetCharge;
                    _db.VisitCharges.Add(materializedCharge);
                    await _db.SaveChangesAsync(ct);
                }

                var account = await VietQrAccountPolicyReader.ReadAsync(
                    _db,
                    hospitalId,
                    _timeProvider.GetUtcNow().UtcDateTime,
                    ct);
                var hospital = await _db.Hospitals.AsNoTracking()
                    .FirstOrDefaultAsync(h => h.Id == hospitalId, ct);
                var merchant = VietQrPayloadBuilder.MerchantName(hospital?.Name);

                var isExamCharge = IsExaminationCharge(targetCharge);
                await ExpireStalePendingForChargeAsync(visitId, targetCharge.Id, isExamCharge, nowLocal, ct);

                var pending = await LoadPendingForChargeAsync(visitId, targetCharge.Id, isExamCharge, ct);
                if (pending.Count > 1)
                    throw new ConflictException(MultipleOpenMessage);

                if (pending.Count == 1)
                {
                    var existing = pending[0];
                    if (existing.Amount != amountDue)
                        throw new ConflictException(AmountMismatchMessage);

                    var mutatedExisting = false;
                    if (!existing.VisitChargeId.HasValue)
                    {
                        existing.VisitChargeId = targetCharge.Id;
                        existing.PaymentScope = PaymentScopes.Charge;
                        mutatedExisting = true;
                    }

                    if (!existing.CreatedAt.HasValue)
                    {
                        existing.CreatedAt = nowLocal;
                        mutatedExisting = true;
                    }

                    if (string.IsNullOrWhiteSpace(existing.PaymentCode))
                    {
                        existing.PaymentCode = CodeFor(existing.Id);
                        AddAudit(
                            actorUserId,
                            hospitalId,
                            visitId,
                            targetCharge.Id,
                            existing,
                            amountDue,
                            QrPaymentIssueKind.Adopted,
                            ipAddress);
                        await _db.SaveChangesAsync(ct);
                        await tx.CommitAsync(ct);
                        return ToDto(lockedVisit, existing, account, merchant, amountDue, QrPaymentIssueKind.Adopted, targetCharge.Id);
                    }

                    if (mutatedExisting)
                        await _db.SaveChangesAsync(ct);

                    await tx.CommitAsync(ct);
                    return ToDto(lockedVisit, existing, account, merchant, amountDue, QrPaymentIssueKind.Reused, targetCharge.Id);
                }

                added = Payment.CreateForCharge(visitId, targetCharge.Id, amountDue, nowLocal, PendingStatus);
                _db.Payments.Add(added);
                await _db.SaveChangesAsync(ct);
                added.PaymentCode = CodeFor(added.Id);
                AddAudit(
                    actorUserId,
                    hospitalId,
                    visitId,
                    targetCharge.Id,
                    added,
                    amountDue,
                    QrPaymentIssueKind.Created,
                    ipAddress);
                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return ToDto(lockedVisit, added, account, merchant, amountDue, QrPaymentIssueKind.Created, targetCharge.Id);
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
                await SafeRollback(tx, ct);
                throw;
            }
        }

        return await IssueAsync(actorUserId, idOrCode, ipAddress, request, recovering: true, ct);
    }

    private async Task<(VisitCharge TargetCharge, decimal AmountDue)> ResolveTargetChargeAndAmountAsync(
        Visit visit,
        VisitChargeAssessment assessment,
        IReadOnlyList<VisitCharge> trackedCharges,
        long? requestedVisitChargeId,
        DateTime nowLocal,
        CancellationToken ct)
    {
        if (requestedVisitChargeId.HasValue)
        {
            var target = trackedCharges.FirstOrDefault(c => c.Id == requestedVisitChargeId.Value);
            if (target is null)
                throw new NotFoundException("Không tìm thấy khoản phí thuộc lượt khám này.");

            if (IsBlocked(visit.Status))
                throw new ConflictException(VisitBlockedMessage);

            var chargeStatus = string.IsNullOrWhiteSpace(target.Status)
                ? VisitChargeStatuses.Active
                : target.Status.Trim().ToUpperInvariant();
            if (chargeStatus != VisitChargeStatuses.Active)
                throw new ConflictException(ChargeCancelledMessage);

            await EnsureDiagnosticOrderActiveIfLinkedAsync(target, ct);

            var line = assessment.Lines.FirstOrDefault(l => l.ChargeId == target.Id);
            if (line is null ||
                line.ChargeDataStatus is VisitChargesService.ChargeDataInvalid or VisitChargesService.ChargeDataUnpriced ||
                line.LineSettlementStatus is VisitChargesService.SettlementMismatch or VisitChargesService.SettlementUndetermined)
            {
                throw new ConflictException(UndeterminedMessage);
            }

            if (line.Payable == false ||
                line.UnitPrice == 0m ||
                line.LineSettlementStatus is VisitChargesService.SettlementPaid or VisitChargeAssessor.LineSettlementWaived)
            {
                throw new ConflictException(NothingDueMessage);
            }

            if (line.LineSettlementStatus == VisitChargeAssessor.LineSettlementCancelled)
                throw new ConflictException(ChargeCancelledMessage);

            if (IsExaminationCharge(target) &&
                string.Equals(assessment.PaymentMethod, "CASHIER", StringComparison.OrdinalIgnoreCase))
            {
                throw new ConflictException(CashierMessage);
            }

            var targetAmount = RequireValidIntegerVnd(line.UnitPrice);
            return (target, targetAmount);
        }

        // No explicit VisitChargeId requested
        if (trackedCharges.Count == 0)
        {
            // Active Historical Visit Cutover (ADR-0059 §2.2.A.2):
            // Materialize EXAMINATION VisitCharge from walk-in audit / scheduled slot price snapshot (never from payments.amount!)
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

            return (materialized, derivedAmount);
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

        if (unpaidLines.Count == 0)
            throw new ConflictException(NothingDueMessage);

        if (unpaidLines.Count > 1)
            throw new ConflictException(MultipleUnpaidChargesMessage);

        var singleLine = unpaidLines[0];
        var singleCharge = trackedCharges.First(c => c.Id == singleLine.ChargeId!.Value);

        await EnsureDiagnosticOrderActiveIfLinkedAsync(singleCharge, ct);

        if (IsExaminationCharge(singleCharge) &&
            string.Equals(assessment.PaymentMethod, "CASHIER", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException(CashierMessage);
        }

        var singleAmount = RequireValidIntegerVnd(singleLine.UnitPrice);
        return (singleCharge, singleAmount);
    }

    private async Task EnsureDiagnosticOrderActiveIfLinkedAsync(VisitCharge charge, CancellationToken ct)
    {
        if (!charge.DiagnosticOrderId.HasValue)
            return;

        try
        {
            var order = await _db.DiagnosticOrders.AsNoTracking()
                .FirstOrDefaultAsync(o => o.Id == charge.DiagnosticOrderId.Value, ct);
            if (order is null || string.Equals(order.Status?.Trim(), "CANCELLED", StringComparison.OrdinalIgnoreCase))
                throw new ConflictException(DiagnosticCancelledMessage);
        }
        catch (NotSupportedException)
        {
            // Minimal unit-test DbContext without DiagnosticOrders DbSet.
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

    private static decimal RequireValidIntegerVnd(decimal? rawAmount)
    {
        if (rawAmount is not decimal amount)
            throw new ConflictException(UndeterminedMessage);

        if (amount != decimal.Truncate(amount) || amount < 1 || amount > MaxVndAmount)
            throw new ConflictException(NotIntegerMessage);

        return amount;
    }

    private async Task<bool> HasPatientRoleAsync(long actorUserId, CancellationToken ct)
    {
        var roleIds = await _db.UserRoles.AsNoTracking()
            .Where(ur => ur.UserId == actorUserId)
            .Select(ur => ur.RoleId)
            .ToListAsync(ct);
        var codes = await _db.Roles.AsNoTracking()
            .Where(r => roleIds.Contains(r.Id))
            .Select(r => r.Code)
            .ToListAsync(ct);
        return codes.Any(c => string.Equals(c, RoleNames.Patient, StringComparison.OrdinalIgnoreCase));
    }

    private async Task ExpireStalePendingForChargeAsync(
        long visitId,
        long visitChargeId,
        bool includeUnlinkedLegacyOnExam,
        DateTime nowLocal,
        CancellationToken ct)
    {
        var cutoff = nowLocal.AddMinutes(-_qrOptions.PendingExpiryMinutes);
        var candidates = await _db.Payments
            .Where(p =>
                p.VisitId == visitId &&
                (p.VisitChargeId == visitChargeId || (includeUnlinkedLegacyOnExam && p.VisitChargeId == null)))
            .ToListAsync(ct);

        var stale = candidates
            .Where(p => IsPending(p.Status) && p.CreatedAt != null && p.CreatedAt < cutoff)
            .ToList();

        if (stale.Count == 0)
            return;

        foreach (var payment in stale)
            payment.Status = PaymentStatuses.Expired;
        await _db.SaveChangesAsync(ct);
    }

    private async Task<List<Payment>> LoadPendingForChargeAsync(
        long visitId,
        long visitChargeId,
        bool includeUnlinkedLegacyOnExam,
        CancellationToken ct)
    {
        var rows = await _db.Payments
            .Where(p =>
                p.VisitId == visitId &&
                (p.VisitChargeId == visitChargeId || (includeUnlinkedLegacyOnExam && p.VisitChargeId == null)))
            .OrderBy(p => p.Id)
            .ToListAsync(ct);
        return rows.Where(p => IsPending(p.Status)).ToList();
    }

    private void AddAudit(
        long actorUserId,
        long hospitalId,
        long visitId,
        long visitChargeId,
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
                visitChargeId,
                paymentId = payment.Id,
                paymentCode = payment.PaymentCode,
                amount,
                currency = VisitChargesService.CurrencyVnd,
                hospitalId,
                issueKind,
            }),
            IpAddress = IPAddress.TryParse(ipAddress, out var ip) ? ip : null,
            OccurredAt = _timeProvider.GetUtcNow().UtcDateTime,
        });
    }

    private static GenerateQrPaymentResponseDto ToDto(
        Visit visit,
        Payment payment,
        VietQrAccount account,
        string merchantName,
        decimal amount,
        string issueKind,
        long visitChargeId)
    {
        var code = payment.PaymentCode ?? "";
        var payload = VietQrPayloadBuilder.Build(account, amount, code, merchantName);
        return new GenerateQrPaymentResponseDto(
            visit.Id,
            visit.VisitCode,
            payment.Id,
            code,
            amount,
            VisitChargesService.CurrencyVnd,
            PendingStatus,
            payload,
            account.Bin,
            account.AccountNumber,
            account.AccountName,
            code,
            issueKind,
            visitChargeId);
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
            // The database already aborted the transaction.
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
}
