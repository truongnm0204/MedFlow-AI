using MedFlow.Application.Common.Time;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Options;
using MedFlow.Application.Services.Appointments;
using MedFlow.Application.Services.Notifications;
using MedFlow.Domain.Entities;
using MedFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using Npgsql;
using NpgsqlTypes;

namespace MedFlow.Infrastructure.BackgroundJobs;

public sealed record OutboxBatchSummary(
    int ReclaimedCount,
    int ClaimedCount,
    int SentCount,
    int RetriedCount,
    int FailedCount,
    int CancelledCount,
    int StaleTokenSkippedCount);

/// <summary>
/// Periodic worker (default 30s) processing <c>notifications</c> outbox entries under an
/// at-least-once delivery contract using <c>FOR UPDATE SKIP LOCKED</c>, dedicated
/// <c>processing_token</c> + <c>lease_until</c> ownership, SMTP execution strictly outside
/// database transactions (<see cref="SemaphoreSlim"/> bounded), and token-guarded finalization updates.
/// </summary>
public sealed class NotificationOutboxProcessor : BackgroundService
{
    private const int MaxErrorLength = 500;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<ScheduleJobsOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<NotificationOutboxProcessor> _logger;

    public NotificationOutboxProcessor(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<ScheduleJobsOptions> options,
        TimeProvider timeProvider,
        ILogger<NotificationOutboxProcessor> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var initialOpts = _options.CurrentValue;
        if (!initialOpts.Enabled)
        {
            _logger.LogInformation("NotificationOutboxProcessor is disabled via ScheduleJobs:Enabled=false.");
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(1, initialOpts.OutboxIntervalSeconds));
        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (!_options.CurrentValue.Enabled)
            {
                continue;
            }

            try
            {
                await ProcessDueBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error while running NotificationOutboxProcessor cycle.");
            }
        }
    }

    public async Task<OutboxBatchSummary> ProcessDueBatchAsync(CancellationToken ct = default)
    {
        var opts = _options.CurrentValue;
        var batchSize = Math.Max(1, opts.BatchSize);
        var maxRetries = Math.Max(1, opts.MaxRetryCount);
        var leaseSeconds = Math.Max(opts.SmtpTimeoutSeconds + 5, opts.ProcessingLeaseSeconds);
        var maxConcurrency = Math.Max(1, opts.MaxEmailConcurrency);

        var reclaimed = await RecoverExpiredLeasesAsync(batchSize, maxRetries, ct);

        var claimToken = Guid.NewGuid().ToString("N");
        var claimedItems = await ClaimDueNotificationsAsync(batchSize, leaseSeconds, claimToken, ct);
        if (claimedItems.Count == 0)
        {
            return new OutboxBatchSummary(reclaimed, 0, 0, 0, 0, 0, 0);
        }

        var staleReminderIds = await FindInvalidReminderIdsAsync(claimedItems, ct);

        using var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        var tasks = claimedItems.Select(item =>
            DispatchSingleOutsideTransactionAsync(item, staleReminderIds.Contains(item.Id), maxRetries, semaphore, ct));

        var outcomes = await Task.WhenAll(tasks);

        return await FinalizeOutcomesAsync(reclaimed, claimedItems.Count, claimToken, outcomes, ct);
    }

    public async Task<int> RecoverExpiredLeasesAsync(
        int batchSize,
        int maxRetries,
        CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var nowLocal = HospitalClock.GetNow(_timeProvider);

        List<Notification> expiredRows;
        if (db is MedFlowDbContext efDb && efDb.Database.IsRelational())
        {
            await using var tx = await db.BeginTransactionAsync(ct);
            var nowParam = new NpgsqlParameter("nowLocal", NpgsqlDbType.Timestamp) { Value = nowLocal };
            var limitParam = new NpgsqlParameter("batchSize", NpgsqlDbType.Integer) { Value = batchSize };

            expiredRows = await efDb.Notifications
                .FromSqlRaw("""
                    SELECT *
                    FROM "notifications"
                    WHERE "status" = 'PROCESSING'
                      AND "lease_until" IS NOT NULL
                      AND "lease_until" <= @nowLocal
                    ORDER BY "lease_until" ASC, "id" ASC
                    LIMIT @batchSize
                    FOR UPDATE SKIP LOCKED
                    """, nowParam, limitParam)
                .ToListAsync(ct);

            ApplyLeaseRecovery(expiredRows, nowLocal, maxRetries);
            if (expiredRows.Count > 0)
            {
                await db.SaveChangesAsync(ct);
            }

            await tx.CommitAsync(ct);
            return expiredRows.Count;
        }

        expiredRows = await db.Notifications
            .Where(n =>
                n.Status == Notification.StatusProcessing &&
                n.LeaseUntil != null &&
                n.LeaseUntil <= nowLocal)
            .OrderBy(n => n.LeaseUntil)
            .ThenBy(n => n.Id)
            .Take(batchSize)
            .ToListAsync(ct);

        ApplyLeaseRecovery(expiredRows, nowLocal, maxRetries);
        if (expiredRows.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        return expiredRows.Count;
    }

    private static void ApplyLeaseRecovery(
        List<Notification> expiredRows,
        DateTime nowLocal,
        int maxRetries)
    {
        foreach (var row in expiredRows)
        {
            var nextRetry = row.RetryCount + 1;
            row.RetryCount = nextRetry;
            row.ProcessingToken = null;
            row.LeaseUntil = null;

            if (nextRetry >= maxRetries)
            {
                row.Status = Notification.StatusFailed;
                row.LastError = "Quá hạn khóa xử lý (worker lease expired) vượt số lần thử lại tối đa.";
            }
            else
            {
                row.Status = Notification.StatusPending;
                row.ScheduledAt = nowLocal.AddMinutes(Math.Pow(2, nextRetry));
                row.LastError = "Thu hồi khóa xử lý quá hạn (worker lease expired).";
            }
        }
    }

    private async Task<List<ClaimedOutboxItem>> ClaimDueNotificationsAsync(
        int batchSize,
        int leaseSeconds,
        string claimToken,
        CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var nowLocal = HospitalClock.GetNow(_timeProvider);
        var leaseUntil = nowLocal.AddSeconds(leaseSeconds);

        if (db is MedFlowDbContext efDb && efDb.Database.IsRelational())
        {
            // Single atomic statement: claims up to batchSize PENDING rows, sets lease_until + processing_token
            // without touching scheduled_at, and releases the DB connection immediately upon scope disposal.
            var nowParam = new NpgsqlParameter("nowLocal", NpgsqlDbType.Timestamp) { Value = nowLocal };
            var limitParam = new NpgsqlParameter("batchSize", NpgsqlDbType.Integer) { Value = batchSize };
            var tokenParam = new NpgsqlParameter("claimToken", NpgsqlDbType.Varchar) { Value = claimToken };
            var leaseParam = new NpgsqlParameter("leaseUntil", NpgsqlDbType.Timestamp) { Value = leaseUntil };

            var claimed = await efDb.Notifications
                .FromSqlRaw("""
                    WITH due AS (
                        SELECT "id"
                        FROM "notifications"
                        WHERE "status" = 'PENDING'
                          AND "scheduled_at" <= @nowLocal
                        ORDER BY "scheduled_at" ASC, "id" ASC
                        LIMIT @batchSize
                        FOR UPDATE SKIP LOCKED
                    )
                    UPDATE "notifications" AS n
                    SET "status" = 'PROCESSING',
                        "processing_token" = @claimToken,
                        "lease_until" = @leaseUntil
                    FROM due
                    WHERE n."id" = due."id"
                    RETURNING n.*
                    """, nowParam, limitParam, tokenParam, leaseParam)
                .AsNoTracking()
                .ToListAsync(ct);

            return claimed.Select(MapClaimed).ToList();
        }

        var dueRows = await db.Notifications
            .Where(n => n.Status == Notification.StatusPending && n.ScheduledAt <= nowLocal)
            .OrderBy(n => n.ScheduledAt)
            .ThenBy(n => n.Id)
            .Take(batchSize)
            .ToListAsync(ct);

        foreach (var row in dueRows)
        {
            row.Status = Notification.StatusProcessing;
            row.ProcessingToken = claimToken;
            row.LeaseUntil = leaseUntil;
        }

        if (dueRows.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        return dueRows.Select(MapClaimed).ToList();
    }

    private async Task<HashSet<long>> FindInvalidReminderIdsAsync(
        IReadOnlyList<ClaimedOutboxItem> claimedItems,
        CancellationToken ct)
    {
        var reminderItems = claimedItems
            .Where(i =>
                string.Equals(i.NotificationType, Notification.TypeAppointmentReminder, StringComparison.OrdinalIgnoreCase) &&
                i.AppointmentId.HasValue)
            .ToList();

        if (reminderItems.Count == 0)
        {
            return [];
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var nowLocal = HospitalClock.GetNow(_timeProvider);

        var appointmentIds = reminderItems
            .Select(i => i.AppointmentId!.Value)
            .Distinct()
            .ToList();

        var appointments = await db.Appointments
            .AsNoTracking()
            .Include(a => a.Slot)
            .Where(a => appointmentIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, ct);

        var activeVisitApptIds = (await db.Visits
            .AsNoTracking()
            .Where(v =>
                v.AppointmentId != null &&
                appointmentIds.Contains(v.AppointmentId.Value) &&
                (v.Status == null || v.Status.ToUpper() != AppointmentBookingRules.CancelledStatus))
            .Select(v => v.AppointmentId!.Value)
            .ToListAsync(ct))
            .ToHashSet();

        var staleIds = new HashSet<long>();
        foreach (var item in reminderItems)
        {
            var apptId = item.AppointmentId!.Value;
            if (!appointments.TryGetValue(apptId, out var appt))
            {
                staleIds.Add(item.Id);
                continue;
            }

            var statusKey = AppointmentBookingRules.NormalizeStatusKey(appt.Status);
            var isWaitingForExam = statusKey is "BOOKED" or "CONFIRMED";
            var startsInFuture = appt.Slot?.StartsAt is { } startsAt && startsAt > nowLocal;
            var hasNonCancelledVisit = activeVisitApptIds.Contains(apptId);

            if (!isWaitingForExam || !startsInFuture || hasNonCancelledVisit)
            {
                staleIds.Add(item.Id);
            }
        }

        return staleIds;
    }

    private async Task<DispatchOutcome> DispatchSingleOutsideTransactionAsync(
        ClaimedOutboxItem item,
        bool isStaleReminder,
        int maxRetries,
        SemaphoreSlim semaphore,
        CancellationToken ct)
    {
        if (isStaleReminder)
        {
            return new DispatchOutcome(
                item.Id,
                TargetStatus: Notification.StatusCancelled,
                RetryCount: item.RetryCount,
                NextScheduledAt: null,
                MarkSentAt: false,
                LastError: "Bỏ qua gửi email nhắc lịch vì lịch hẹn đã hủy, đổi trạng thái, đã check-in hoặc đã qua giờ khám.");
        }

        if (!string.Equals(item.Channel, Notification.ChannelEmail, StringComparison.OrdinalIgnoreCase))
        {
            return new DispatchOutcome(
                item.Id,
                TargetStatus: Notification.StatusDelivered,
                RetryCount: item.RetryCount,
                NextScheduledAt: null,
                MarkSentAt: false,
                LastError: null);
        }

        if (!IsValidMailboxRecipient(item.Recipient, out var normalizedRecipient))
        {
            return new DispatchOutcome(
                item.Id,
                TargetStatus: Notification.StatusFailed,
                RetryCount: item.RetryCount,
                NextScheduledAt: null,
                MarkSentAt: false,
                LastError: "Địa chỉ email người nhận trống hoặc không hợp lệ.");
        }

        await semaphore.WaitAsync(ct);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var emailSender = scope.ServiceProvider.GetRequiredService<IEmailSender>();

            var subject = string.IsNullOrWhiteSpace(item.Subject)
                ? "Thông báo lịch khám MedFlow AI"
                : item.Subject.Trim();
            var htmlBody = AppointmentNotificationComposer.BuildEmailHtmlBody(subject, item.Body ?? string.Empty);

            await emailSender.SendAsync(normalizedRecipient, subject, htmlBody, ct);

            return new DispatchOutcome(
                item.Id,
                TargetStatus: Notification.StatusSent,
                RetryCount: item.RetryCount,
                NextScheduledAt: null,
                MarkSentAt: true,
                LastError: null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var nextRetry = item.RetryCount + 1;
            var safeError = SanitizeError(ex.Message);
            var nowLocal = HospitalClock.GetNow(_timeProvider);

            if (nextRetry >= maxRetries)
            {
                _logger.LogWarning(ex, "Notification {NotificationId} permanently failed after {RetryCount} attempts.", item.Id, nextRetry);
                return new DispatchOutcome(
                    item.Id,
                    TargetStatus: Notification.StatusFailed,
                    RetryCount: nextRetry,
                    NextScheduledAt: null,
                    MarkSentAt: false,
                    LastError: safeError);
            }

            var backoffMinutes = Math.Pow(2, nextRetry);
            return new DispatchOutcome(
                item.Id,
                TargetStatus: Notification.StatusPending,
                RetryCount: nextRetry,
                NextScheduledAt: nowLocal.AddMinutes(backoffMinutes),
                MarkSentAt: false,
                LastError: safeError);
        }
        finally
        {
            semaphore.Release();
        }
    }

    private async Task<OutboxBatchSummary> FinalizeOutcomesAsync(
        int reclaimedCount,
        int claimedCount,
        string claimToken,
        IReadOnlyList<DispatchOutcome> outcomes,
        CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var nowLocal = HospitalClock.GetNow(_timeProvider);
        var isRelational = db is MedFlowDbContext efDb && efDb.Database.IsRelational();

        var sentCount = 0;
        var retriedCount = 0;
        var failedCount = 0;
        var cancelledCount = 0;
        var staleSkippedCount = 0;

        foreach (var outcome in outcomes)
        {
            var sentAt = outcome.MarkSentAt ? (DateTime?)nowLocal : null;
            int affected;

            if (isRelational)
            {
                affected = await db.Notifications
                    .Where(n =>
                        n.Id == outcome.NotificationId &&
                        n.Status == Notification.StatusProcessing &&
                        n.ProcessingToken == claimToken)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(n => n.Status, outcome.TargetStatus)
                        .SetProperty(n => n.RetryCount, outcome.RetryCount)
                        .SetProperty(n => n.ScheduledAt, n => outcome.NextScheduledAt ?? n.ScheduledAt)
                        .SetProperty(n => n.SentAt, n => sentAt ?? n.SentAt)
                        .SetProperty(n => n.LastError, outcome.LastError)
                        .SetProperty(n => n.ProcessingToken, (string?)null)
                        .SetProperty(n => n.LeaseUntil, (DateTime?)null),
                        ct);
            }
            else
            {
                var tracked = await db.Notifications.FirstOrDefaultAsync(
                    n => n.Id == outcome.NotificationId &&
                         n.Status == Notification.StatusProcessing &&
                         n.ProcessingToken == claimToken,
                    ct);

                if (tracked is null)
                {
                    affected = 0;
                }
                else
                {
                    tracked.Status = outcome.TargetStatus;
                    tracked.RetryCount = outcome.RetryCount;
                    if (outcome.NextScheduledAt.HasValue)
                    {
                        tracked.ScheduledAt = outcome.NextScheduledAt.Value;
                    }

                    if (sentAt.HasValue)
                    {
                        tracked.SentAt = sentAt.Value;
                    }

                    tracked.LastError = outcome.LastError;
                    tracked.ProcessingToken = null;
                    tracked.LeaseUntil = null;
                    affected = await db.SaveChangesAsync(ct);
                }
            }

            if (affected == 0)
            {
                staleSkippedCount++;
                _logger.LogWarning(
                    "Skipped outbox finalization for notification {NotificationId} because processing_token {Token} is no longer active.",
                    outcome.NotificationId,
                    claimToken);
                continue;
            }

            switch (outcome.TargetStatus)
            {
                case Notification.StatusSent:
                case Notification.StatusDelivered:
                    sentCount++;
                    break;
                case Notification.StatusPending:
                    retriedCount++;
                    break;
                case Notification.StatusCancelled:
                    cancelledCount++;
                    break;
                default:
                    failedCount++;
                    break;
            }
        }

        return new OutboxBatchSummary(
            ReclaimedCount: reclaimedCount,
            ClaimedCount: claimedCount,
            SentCount: sentCount,
            RetriedCount: retriedCount,
            FailedCount: failedCount,
            CancelledCount: cancelledCount,
            StaleTokenSkippedCount: staleSkippedCount);
    }

    private static bool IsValidMailboxRecipient(string? rawRecipient, out string normalizedAddress)
    {
        normalizedAddress = string.Empty;
        if (!AppointmentNotificationComposer.IsSyntacticallyValidEmail(rawRecipient))
        {
            return false;
        }

        if (!MailboxAddress.TryParse(rawRecipient!.Trim(), out var mailbox) ||
            string.IsNullOrWhiteSpace(mailbox.Address))
        {
            return false;
        }

        normalizedAddress = mailbox.Address.Trim();
        return true;
    }

    private static string SanitizeError(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "Lỗi gửi email không xác định.";
        }

        var trimmed = message.Trim();
        return trimmed.Length <= MaxErrorLength ? trimmed : trimmed[..MaxErrorLength];
    }

    private static ClaimedOutboxItem MapClaimed(Notification n) => new(
        Id: n.Id,
        AppointmentId: n.AppointmentId,
        Channel: n.Channel,
        Recipient: n.Recipient,
        Subject: n.Subject,
        Body: n.Body,
        NotificationType: n.NotificationType,
        RetryCount: n.RetryCount);

    private sealed record ClaimedOutboxItem(
        long Id,
        long? AppointmentId,
        string? Channel,
        string? Recipient,
        string? Subject,
        string? Body,
        string? NotificationType,
        int RetryCount);

    private sealed record DispatchOutcome(
        long NotificationId,
        string TargetStatus,
        int RetryCount,
        DateTime? NextScheduledAt,
        bool MarkSentAt,
        string? LastError);
}
