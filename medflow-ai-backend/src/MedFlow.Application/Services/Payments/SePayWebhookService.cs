using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using MedFlow.Application.DTOs.Payments;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Appointments;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Payments;

public sealed class SePayWebhookService : ISePayWebhookService
{
    public const string Provider = "SEPAY";
    public const string AuditAction = "PAYMENT_CONFIRMED";

    public const string InvalidTransactionMessage = "Mã giao dịch SePay không hợp lệ.";
    public const string InvalidAmountMessage = "Số tiền giao dịch SePay không hợp lệ.";

    private const int MaxCodeLength = 40;
    private const int MaxReferenceLength = 150;

    private static readonly Regex CodePattern = new(
        @"\bMF(\d{1,18})\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly IApplicationDbContext _db;
    private readonly IPaymentGatewayEventStore _store;
    private readonly TimeProvider _timeProvider;

    public SePayWebhookService(
        IApplicationDbContext db,
        IPaymentGatewayEventStore store,
        TimeProvider? timeProvider = null)
    {
        _db = db;
        _store = store;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<SePayWebhookResult> HandleAsync(
        SePayWebhookRequest request,
        CancellationToken ct = default)
    {
        if (request.Id <= 0)
            throw new ValidationException(InvalidTransactionMessage);

        if (request.TransferAmount is not decimal amount || amount <= 0)
            throw new ValidationException(InvalidAmountMessage);

        if (!string.Equals(request.TransferType?.Trim(), "in", StringComparison.OrdinalIgnoreCase))
            return new SePayWebhookResult(true, PaymentGatewayOutcomes.Ignored);

        var externalId = request.Id.ToString(CultureInfo.InvariantCulture);
        if (await _store.ExistsAsync(Provider, externalId, ct))
            return new SePayWebhookResult(true, PaymentGatewayOutcomes.Ignored);

        var code = ExtractCode(request);
        var outcome = PaymentGatewayOutcomes.Unmatched;
        var referenceCode = TrimTo(request.ReferenceCode, MaxReferenceLength);

        Payment? payment = null;
        if (code is not null)
            payment = await _db.Payments.FirstOrDefaultAsync(p => p.PaymentCode == code, ct);

        var gatewayEvent = new PaymentGatewayEvent
        {
            Provider = Provider,
            ExternalId = externalId,
            PaymentId = payment?.Id,
            PaymentCode = code,
            TransferAmount = amount,
            ReferenceCode = referenceCode,
            Outcome = PaymentGatewayOutcomes.Unmatched,
            ReceivedAt = _timeProvider.GetUtcNow().UtcDateTime,
        };

        if (payment is not null)
        {
            var status = Normalize(payment.Status);
            if (status == PaymentStatuses.Paid)
            {
                outcome = PaymentGatewayOutcomes.AlreadyPaid;
            }
            else if (status is PaymentStatuses.Pending or PaymentStatuses.Expired)
            {
                if (payment.Amount is not decimal expected || expected <= 0)
                {
                    outcome = PaymentGatewayOutcomes.Unmatched;
                }
                else
                {
                    var receivedBefore = await _store.SumReceivedAmountAsync(code!, ct);
                    var total = receivedBefore + amount;
                    if (total >= expected)
                    {
                        payment.Status = PaymentStatuses.Paid;
                        payment.PaidAt = ResolvePaidAt(request.TransactionDate);
                        payment.ExternalReference = referenceCode;
                        outcome = total > expected
                            ? PaymentGatewayOutcomes.Overpaid
                            : PaymentGatewayOutcomes.Paid;
                        await AddAuditAsync(payment, code!, amount, total, expected, externalId, outcome, ct);
                    }
                    else
                    {
                        outcome = PaymentGatewayOutcomes.Partial;
                    }
                }
            }
            else
            {
                outcome = PaymentGatewayOutcomes.AlreadyClosed;
            }
        }

        gatewayEvent.Outcome = outcome;
        _store.Add(gatewayEvent);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (PostgresUniqueViolation.Is(ex))
        {
            // Cùng một giao dịch được giao lại: unique (provider, external_id) chặn ghi trùng.
            if (_db is DbContext context)
                context.ChangeTracker.Clear();
            return new SePayWebhookResult(true, PaymentGatewayOutcomes.Ignored);
        }

        return new SePayWebhookResult(true, outcome);
    }

    internal static string? ExtractCode(SePayWebhookRequest request)
    {
        var direct = request.Code?.Trim();
        if (!string.IsNullOrEmpty(direct)
            && direct.Length <= MaxCodeLength
            && direct.StartsWith("MF", StringComparison.OrdinalIgnoreCase)
            && CodePattern.IsMatch(direct)
            && CodePattern.Match(direct).Value.Equals(direct, StringComparison.OrdinalIgnoreCase))
        {
            return direct.ToUpperInvariant();
        }

        if (string.IsNullOrWhiteSpace(request.Content))
            return null;

        var match = CodePattern.Match(request.Content);
        return match.Success ? "MF" + match.Groups[1].Value : null;
    }

    private async Task AddAuditAsync(
        Payment payment,
        string code,
        decimal received,
        decimal total,
        decimal expected,
        string externalId,
        string outcome,
        CancellationToken ct)
    {
        long? hospitalId = null;
        if (payment.VisitId is long visitId)
        {
            var visit = await _db.Visits.AsNoTracking().FirstOrDefaultAsync(v => v.Id == visitId, ct);
            if (visit is not null)
            {
                var hospitalIds = await VisitHospitalResolver.CollectAsync(_db, visit, ct);
                if (hospitalIds.Count == 1)
                    hospitalId = hospitalIds.First();
            }
        }

        _db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = null,
            HospitalId = hospitalId,
            Action = AuditAction,
            EntityType = "PAYMENT",
            EntityId = payment.Id.ToString(CultureInfo.InvariantCulture),
            ChangeData = JsonSerializer.Serialize(new
            {
                paymentCode = code,
                provider = Provider,
                externalId,
                receivedAmount = received,
                totalReceived = total,
                expectedAmount = expected,
                currency = VisitChargesService.CurrencyVnd,
                outcome,
            }),
            OccurredAt = _timeProvider.GetUtcNow().UtcDateTime,
        });
    }

    private DateTime ResolvePaidAt(string? raw)
    {
        if (!string.IsNullOrWhiteSpace(raw)
            && DateTime.TryParseExact(
                raw.Trim(),
                "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            return DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified);
        }

        return AppointmentBookingRules.GetVietnamNow(_timeProvider);
    }

    private static string? Normalize(string? status) =>
        string.IsNullOrWhiteSpace(status) ? null : status.Trim().ToUpperInvariant();

    private static string? TrimTo(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
