using System.Globalization;
using System.Net;
using System.Text.Json;
using MedFlow.Application.DTOs.Payments;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Appointments;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

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
    public const string ForbiddenMessage = "Bạn không có quyền tạo yêu cầu thanh toán QR cho lượt khám này.";

    private const decimal MaxVndAmount = 9_999_999_999_999m;
    private const string AuditAction = "QR_PAYMENT_REQUESTED";
    private const string PaymentEntityType = "PAYMENT";

    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;

    public GenerateQrPaymentService(IApplicationDbContext db, TimeProvider? timeProvider = null)
    {
        _db = db;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<GenerateQrPaymentResponseDto> CreateAsync(
        long actorUserId,
        string? idOrCode,
        string? ipAddress,
        CancellationToken ct = default)
        => IssueAsync(actorUserId, idOrCode, ipAddress, recovering: false, ct);

    private async Task<GenerateQrPaymentResponseDto> IssueAsync(
        long actorUserId,
        string? idOrCode,
        string? ipAddress,
        bool recovering,
        CancellationToken ct)
    {
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
        await using (var tx = await _db.BeginTransactionAsync(ct))
        {
            Payment? added = null;
            try
            {
                var assessment = await VisitChargeAssessor.AssessAsync(_db, _timeProvider, visit, ct);
                var amountDue = RequireAmount(visit, assessment);
                var account = await VietQrAccountPolicyReader.ReadAsync(
                    _db,
                    hospitalId,
                    _timeProvider.GetUtcNow().UtcDateTime,
                    ct);
                var hospital = await _db.Hospitals.AsNoTracking()
                    .FirstOrDefaultAsync(h => h.Id == hospitalId, ct);
                var merchant = VietQrPayloadBuilder.MerchantName(hospital?.Name);

                await ExpireStalePendingAsync(visit.Id, ct);
                var pending = await LoadPendingAsync(visit.Id, ct);
                if (pending.Count > 1)
                    throw new ConflictException(MultipleOpenMessage);

                if (pending.Count == 1)
                {
                    var existing = pending[0];
                    if (existing.Amount != amountDue)
                        throw new ConflictException(AmountMismatchMessage);

                    if (string.IsNullOrWhiteSpace(existing.PaymentCode))
                    {
                        existing.PaymentCode = CodeFor(existing.Id);
                        AddAudit(actorUserId, hospitalId, visit.Id, existing, amountDue, QrPaymentIssueKind.Adopted, ipAddress);
                        await _db.SaveChangesAsync(ct);
                        await tx.CommitAsync(ct);
                        return ToDto(visit, existing, account, merchant, amountDue, QrPaymentIssueKind.Adopted);
                    }

                    await tx.CommitAsync(ct);
                    return ToDto(visit, existing, account, merchant, amountDue, QrPaymentIssueKind.Reused);
                }

                added = new Payment
                {
                    VisitId = visit.Id,
                    Amount = amountDue,
                    Status = PendingStatus,
                    PaidAt = null,
                    ExternalReference = null,
                    CreatedAt = Clock(),
                };
                _db.Payments.Add(added);
                await _db.SaveChangesAsync(ct);
                added.PaymentCode = CodeFor(added.Id);
                AddAudit(actorUserId, hospitalId, visit.Id, added, amountDue, QrPaymentIssueKind.Created, ipAddress);
                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return ToDto(visit, added, account, merchant, amountDue, QrPaymentIssueKind.Created);
            }
            catch (DbUpdateException ex) when (PostgresUniqueViolation.Is(ex))
            {
                Detach(added);
                await SafeRollback(tx, ct);
                if (recovering)
                    throw new ConflictException(AmountMismatchMessage);
            }
        }

        return await IssueAsync(actorUserId, idOrCode, ipAddress, recovering: true, ct);
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

        var amount = assessment.AmountDue.Value;
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
        return codes.Any(c => string.Equals(c, "PATIENT", StringComparison.OrdinalIgnoreCase));
    }

    private async Task ExpireStalePendingAsync(long visitId, CancellationToken ct)
    {
        var cutoff = Clock().AddMinutes(-PendingExpiryMinutes);
        var stale = await _db.Payments
            .Where(p => p.VisitId == visitId
                && p.Status != null
                && p.Status.ToUpper() == PaymentStatuses.Pending
                && p.CreatedAt != null
                && p.CreatedAt < cutoff)
            .ToListAsync(ct);

        if (stale.Count == 0)
            return;

        foreach (var payment in stale)
            payment.Status = PaymentStatuses.Expired;
        await _db.SaveChangesAsync(ct);
    }

    private async Task<List<Payment>> LoadPendingAsync(long visitId, CancellationToken ct)
    {
        var rows = await _db.Payments
            .Where(p => p.VisitId == visitId)
            .ToListAsync(ct);
        return rows.Where(p => IsPending(p.Status)).ToList();
    }

    private void AddAudit(
        long actorUserId,
        long hospitalId,
        long visitId,
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
        string issueKind)
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
            issueKind);
    }

    private void Detach(Payment? payment)
    {
        if (payment is not null && _db is DbContext context)
            context.Entry(payment).State = EntityState.Detached;
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
        => AppointmentBookingRules.GetVietnamNow(_timeProvider);

    private static string CodeFor(long paymentId) =>
        "MF" + paymentId.ToString(CultureInfo.InvariantCulture);

    private static bool IsPending(string? status) =>
        string.Equals(status?.Trim(), PendingStatus, StringComparison.OrdinalIgnoreCase);

    private static bool IsBlocked(string? status) =>
        string.Equals(status?.Trim(), PaymentStatuses.Cancelled, StringComparison.OrdinalIgnoreCase)
        || string.Equals(status?.Trim(), "NO_SHOW", StringComparison.OrdinalIgnoreCase);
}
