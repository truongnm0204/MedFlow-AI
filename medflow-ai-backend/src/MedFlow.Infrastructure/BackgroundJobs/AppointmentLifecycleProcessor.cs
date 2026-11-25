using System.Globalization;
using System.Text.Json;
using MedFlow.Application.Common.Time;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Options;
using MedFlow.Application.Services.Appointments;
using MedFlow.Application.Services.Notifications;
using MedFlow.Application.Services.Policies;
using MedFlow.Domain.Entities;
using MedFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace MedFlow.Infrastructure.BackgroundJobs;

public sealed record LifecycleCycleSummary(
    int RemindersQueuedCount,
    int AutoNoShowCount,
    int CancelledReservedQueueCount);

/// <summary>
/// Periodic worker (default 10m) handling:
/// <list type="number">
/// <item>Upcoming appointment reminder generation (<c>UC17</c> / <c>#62</c>) using hospital <c>reminder_hours</c> policy and slot-versioned <c>idempotency_key</c>.</item>
/// <item>Automated <c>NO_SHOW</c> finalization past <c>slot.ends_at + no_show_grace_minutes</c> synchronized transactionally with <c>RESERVED</c> <c>queue_entries</c>, <c>audit_logs</c>, and patient notifications under <c>FOR UPDATE SKIP LOCKED</c>.</item>
/// </list>
/// </summary>
public sealed class AppointmentLifecycleProcessor : BackgroundService
{
    public const string AutoNoShowAuditAction = "APPOINTMENT_AUTO_NO_SHOW";
    private const string NotificationPolicyType = "NOTIFICATION";
    private const string ReminderHoursPolicyKey = "reminder_hours";
    private const string HoursBeforeProperty = "hours_before";

    private static readonly string[] ServingQueueStatuses =
    [
        "WAITING",
        "READY",
        "CALLED",
        "IN_PROGRESS",
        "IN_CONSULTATION",
        "COMPLETED",
        "SKIPPED",
    ];

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<ScheduleJobsOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AppointmentLifecycleProcessor> _logger;

    public AppointmentLifecycleProcessor(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<ScheduleJobsOptions> options,
        TimeProvider timeProvider,
        ILogger<AppointmentLifecycleProcessor> logger)
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
            _logger.LogInformation("AppointmentLifecycleProcessor is disabled via ScheduleJobs:Enabled=false.");
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Max(1, initialOpts.LifecycleIntervalMinutes));
        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (!_options.CurrentValue.Enabled)
            {
                continue;
            }

            try
            {
                await ProcessCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error while running AppointmentLifecycleProcessor cycle.");
            }
        }
    }

    public async Task<LifecycleCycleSummary> ProcessCycleAsync(CancellationToken ct = default)
    {
        var opts = _options.CurrentValue;
        var batchSize = Math.Max(1, opts.BatchSize);
        var defaultReminderHours = Math.Clamp(opts.DefaultReminderHours, 1, 168);

        var remindersQueued = await EnqueueUpcomingRemindersAsync(batchSize, defaultReminderHours, ct);
        var (autoNoShowCount, cancelledQueuesCount) = await FinalizeOverdueNoShowsAsync(batchSize, ct);

        return new LifecycleCycleSummary(
            RemindersQueuedCount: remindersQueued,
            AutoNoShowCount: autoNoShowCount,
            CancelledReservedQueueCount: cancelledQueuesCount);
    }

    public static async Task<int> ResolveEffectiveReminderHoursAsync(
        IApplicationDbContext db,
        long hospitalId,
        DateTime nowUtc,
        int defaultHours,
        CancellationToken ct)
    {
        if (hospitalId <= 0)
        {
            return defaultHours;
        }

        var rows = await db.HospitalPolicies
            .AsNoTracking()
            .Where(p =>
                p.HospitalId == hospitalId &&
                p.PolicyType == NotificationPolicyType &&
                p.PolicyKey == ReminderHoursPolicyKey &&
                p.QueueServicePointId == null &&
                (p.EffectiveFrom == null || p.EffectiveFrom <= nowUtc) &&
                (p.EffectiveTo == null || p.EffectiveTo > nowUtc))
            .OrderByDescending(p => p.PolicyVersion ?? 0)
            .ThenByDescending(p => p.Id)
            .ToListAsync(ct);

        var effectiveRow = rows.FirstOrDefault();
        if (effectiveRow is null || string.IsNullOrWhiteSpace(effectiveRow.PolicyValue))
        {
            return defaultHours;
        }

        try
        {
            using var doc = JsonDocument.Parse(effectiveRow.PolicyValue);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (doc.RootElement.TryGetProperty(HoursBeforeProperty, out var prop) && prop.TryGetInt32(out var h) && h > 0)
                {
                    return Math.Clamp(h, 1, 168);
                }

                if (doc.RootElement.TryGetProperty("minimum_hours", out var altProp) && altProp.TryGetInt32(out var altH) && altH > 0)
                {
                    return Math.Clamp(altH, 1, 168);
                }
            }
        }
        catch (JsonException)
        {
            // Fallback to configured default when malformed JSON is encountered.
        }

        return defaultHours;
    }

    private async Task<int> EnqueueUpcomingRemindersAsync(
        int batchSize,
        int defaultReminderHours,
        CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var nowUtc = HospitalClock.GetUtcNow(_timeProvider);
        var nowLocal = HospitalClock.GetNow(_timeProvider);

        var activeHospitalIds = await db.Hospitals
            .AsNoTracking()
            .Where(h => h.IsActive != false)
            .Select(h => h.Id)
            .ToListAsync(ct);

        var reminderHoursByHospital = new Dictionary<long, int>();
        var maxReminderHours = defaultReminderHours;
        foreach (var hId in activeHospitalIds)
        {
            var hours = await ResolveEffectiveReminderHoursAsync(db, hId, nowUtc, defaultReminderHours, ct);
            reminderHoursByHospital[hId] = hours;
            if (hours > maxReminderHours)
            {
                maxReminderHours = hours;
            }
        }

        var upperScanBound = nowLocal.AddHours(maxReminderHours);

        var candidates = await db.Appointments
            .AsNoTracking()
            .Include(a => a.Slot)
                .ThenInclude(s => s!.Service)
            .Include(a => a.Slot)
                .ThenInclude(s => s!.Doctor)
                    .ThenInclude(d => d!.Staff)
            .Include(a => a.Slot)
                .ThenInclude(s => s!.Room)
            .Where(a =>
                a.PatientId != null &&
                a.SlotId != null &&
                (a.Status == "BOOKED" || a.Status == "CONFIRMED") &&
                a.Slot != null &&
                a.Slot.StartsAt != null &&
                a.Slot.StartsAt > nowLocal &&
                a.Slot.StartsAt <= upperScanBound)
            .OrderBy(a => a.Slot!.StartsAt)
            .ThenBy(a => a.Id)
            .Take(batchSize * 2)
            .ToListAsync(ct);

        if (candidates.Count == 0)
        {
            return 0;
        }

        var candidateApptIds = candidates.Select(a => a.Id).ToList();
        var attendedApptIds = (await db.Visits
            .AsNoTracking()
            .Where(v =>
                v.AppointmentId != null &&
                candidateApptIds.Contains(v.AppointmentId.Value) &&
                (v.Status == null || v.Status.ToUpper() != AppointmentBookingRules.CancelledStatus))
            .Select(v => v.AppointmentId!.Value)
            .ToListAsync(ct))
            .ToHashSet();

        var filtered = new List<(Appointment Appointment, string IdempotencyKey)>();
        foreach (var appt in candidates)
        {
            if (attendedApptIds.Contains(appt.Id))
            {
                continue;
            }

            var slot = appt.Slot!;
            var startsAt = slot.StartsAt!.Value;
            var hospitalId = slot.Service?.HospitalId
                ?? slot.Room?.HospitalId
                ?? slot.Doctor?.HospitalId
                ?? 0L;

            if (!reminderHoursByHospital.TryGetValue(hospitalId, out var reminderHours))
            {
                reminderHours = await ResolveEffectiveReminderHoursAsync(db, hospitalId, nowUtc, defaultReminderHours, ct);
                reminderHoursByHospital[hospitalId] = reminderHours;
            }

            if (startsAt > nowLocal.AddHours(reminderHours))
            {
                continue;
            }

            var key = AppointmentNotificationComposer.BuildReminderIdempotencyKey(appt.Id, slot.Id, startsAt);
            filtered.Add((appt, key));

            if (filtered.Count >= batchSize)
            {
                break;
            }
        }

        if (filtered.Count == 0)
        {
            return 0;
        }

        var candidateKeys = filtered.Select(x => x.IdempotencyKey).ToList();
        var existingKeys = (await db.Notifications
            .AsNoTracking()
            .Where(n => n.IdempotencyKey != null && candidateKeys.Contains(n.IdempotencyKey))
            .Select(n => n.IdempotencyKey!)
            .ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);

        var insertedCount = 0;
        var isRelational = db is MedFlowDbContext efDb && efDb.Database.IsRelational();

        foreach (var (appt, key) in filtered)
        {
            if (existingKeys.Contains(key))
            {
                continue;
            }

            var slot = appt.Slot!;
            var startsAt = slot.StartsAt!.Value;
            var recipientEmail = await AppointmentNotificationComposer.ResolveAuthorizedRecipientEmailAsync(
                db,
                appt.PatientId!.Value,
                appt.BookedByUserId,
                nowLocal,
                ct);

            var serviceName = !string.IsNullOrWhiteSpace(slot.Service?.Name)
                ? slot.Service!.Name
                : "Khám chuyên khoa ngoại trú";
            var doctorTitle = string.IsNullOrWhiteSpace(slot.Doctor?.ProfessionalTitle) ? "BS" : slot.Doctor!.ProfessionalTitle!.Trim();
            var doctorName = slot.Doctor?.Staff?.FullName is { Length: > 0 } fullName
                ? AppointmentNotificationComposer.FormatDoctorDisplayName(fullName, doctorTitle)
                : null;
            var roomLabel = !string.IsNullOrWhiteSpace(slot.Room?.Name)
                ? slot.Room!.Name
                : slot.Room?.RoomCode;
            var apptCode = !string.IsNullOrWhiteSpace(appt.AppointmentCode)
                ? appt.AppointmentCode!
                : $"APT-{appt.Id}";

            var notification = AppointmentNotificationComposer.CreateReminderNotification(
                patientId: appt.PatientId.Value,
                appointmentId: appt.Id,
                slotId: slot.Id,
                appointmentCode: apptCode,
                serviceName: serviceName,
                doctorDisplayName: doctorName,
                roomLabel: roomLabel,
                startsAt: startsAt,
                authorizedEmail: recipientEmail,
                nowLocal: nowLocal);

            insertedCount += await InsertNotificationIdempotentAsync(db, isRelational, notification, ct);
        }

        return insertedCount;
    }

    private async Task<(int AutoNoShowCount, int CancelledQueuesCount)> FinalizeOverdueNoShowsAsync(
        int batchSize,
        CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var policyService = scope.ServiceProvider.GetRequiredService<IAppointmentPolicyService>();

        var nowUtc = HospitalClock.GetUtcNow(_timeProvider);
        var nowLocal = HospitalClock.GetNow(_timeProvider);

        var rawCandidates = await db.Appointments
            .AsNoTracking()
            .Include(a => a.Slot)
                .ThenInclude(s => s!.Service)
            .Include(a => a.Slot)
                .ThenInclude(s => s!.Doctor)
            .Include(a => a.Slot)
                .ThenInclude(s => s!.Room)
            .Where(a =>
                a.PatientId != null &&
                a.SlotId != null &&
                (a.Status == "BOOKED" || a.Status == "CONFIRMED") &&
                a.Slot != null &&
                a.Slot.EndsAt != null &&
                a.Slot.EndsAt < nowLocal)
            .OrderBy(a => a.Slot!.EndsAt)
            .ThenBy(a => a.Id)
            .Take(batchSize * 2)
            .ToListAsync(ct);

        if (rawCandidates.Count == 0)
        {
            return (0, 0);
        }

        var policyByHospital = new Dictionary<long, int>();
        var eligibleCandidateIds = new List<long>(batchSize);

        foreach (var candidate in rawCandidates)
        {
            var slot = candidate.Slot!;
            var hospitalId = slot.Service?.HospitalId
                ?? slot.Room?.HospitalId
                ?? slot.Doctor?.HospitalId
                ?? 0L;

            if (!policyByHospital.TryGetValue(hospitalId, out var graceMinutes))
            {
                var policy = await policyService.GetEffectiveAsync(hospitalId, ct);
                graceMinutes = Math.Max(0, policy.NoShowGraceMinutes);
                policyByHospital[hospitalId] = graceMinutes;
            }

            if (slot.EndsAt!.Value.AddMinutes(graceMinutes) < nowLocal)
            {
                eligibleCandidateIds.Add(candidate.Id);
                if (eligibleCandidateIds.Count >= batchSize)
                {
                    break;
                }
            }
        }

        if (eligibleCandidateIds.Count == 0)
        {
            return (0, 0);
        }

        var autoNoShowCount = 0;
        var cancelledQueueCount = 0;

        foreach (var targetAppointmentId in eligibleCandidateIds)
        {
            using var itemScope = _scopeFactory.CreateScope();
            var itemDb = itemScope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
            var isRelational = itemDb is MedFlowDbContext efItemDb && efItemDb.Database.IsRelational();

            await using var tx = await itemDb.BeginTransactionAsync(ct);

            Appointment? lockedAppt;
            if (isRelational)
            {
                var lockedList = await ((MedFlowDbContext)itemDb).Appointments
                    .FromSqlInterpolated($"""
                        SELECT *
                        FROM "appointments"
                        WHERE "id" = {targetAppointmentId}
                          AND "status" IN ('BOOKED', 'CONFIRMED')
                        FOR UPDATE SKIP LOCKED
                        """)
                    .ToListAsync(ct);

                lockedAppt = lockedList.FirstOrDefault();
            }
            else
            {
                lockedAppt = await itemDb.Appointments
                    .FirstOrDefaultAsync(
                        a => a.Id == targetAppointmentId &&
                             (a.Status == "BOOKED" || a.Status == "CONFIRMED"),
                        ct);
            }

            if (lockedAppt is null || !lockedAppt.PatientId.HasValue || !lockedAppt.SlotId.HasValue)
            {
                await tx.RollbackAsync(ct);
                continue;
            }

            var slot = await itemDb.ExaminationSlots
                .AsNoTracking()
                .Include(s => s.Service)
                .Include(s => s.Room)
                .Include(s => s.Doctor)
                .FirstOrDefaultAsync(s => s.Id == lockedAppt.SlotId.Value, ct);

            if (slot?.EndsAt is null)
            {
                await tx.RollbackAsync(ct);
                continue;
            }

            var hospitalId = slot.Service?.HospitalId
                ?? slot.Room?.HospitalId
                ?? slot.Doctor?.HospitalId
                ?? 0L;

            if (!policyByHospital.TryGetValue(hospitalId, out var graceMinutes))
            {
                var itemPolicySvc = itemScope.ServiceProvider.GetRequiredService<IAppointmentPolicyService>();
                graceMinutes = Math.Max(0, (await itemPolicySvc.GetEffectiveAsync(hospitalId, ct)).NoShowGraceMinutes);
                policyByHospital[hospitalId] = graceMinutes;
            }

            if (slot.EndsAt.Value.AddMinutes(graceMinutes) >= nowLocal)
            {
                await tx.RollbackAsync(ct);
                continue;
            }

            // Re-verify inside lock that Reception Check-in has not created a Visit or activated a QueueEntry.
            var hasNonCancelledVisit = await itemDb.Visits
                .AsNoTracking()
                .AnyAsync(
                    v => v.AppointmentId == lockedAppt.Id &&
                         (v.Status == null || v.Status.ToUpper() != AppointmentBookingRules.CancelledStatus),
                    ct);

            if (hasNonCancelledVisit)
            {
                await tx.RollbackAsync(ct);
                continue;
            }

            var hasActiveServingQueue = await itemDb.QueueEntries
                .AsNoTracking()
                .AnyAsync(
                    q => q.AppointmentId == lockedAppt.Id &&
                         q.Status != null &&
                         ServingQueueStatuses.Contains(q.Status.ToUpper()),
                    ct);

            if (hasActiveServingQueue)
            {
                await tx.RollbackAsync(ct);
                continue;
            }

            var previousStatus = lockedAppt.Status ?? AppointmentBookingRules.BookedStatus;
            lockedAppt.Status = AppointmentBookingRules.NoShowStatus;

            var noShowReason = "Hệ thống tự động ghi nhận vắng mặt (NO_SHOW) do quá thời gian tiếp đón theo chính sách bệnh viện.";
            var cancelledQueues = await AppointmentNotificationComposer.CancelReservedQueueEntriesForAppointmentAsync(
                itemDb,
                lockedAppt.Id,
                actorUserId: null,
                hospitalId: hospitalId,
                reason: noShowReason,
                ipAddress: null,
                nowUtc: nowUtc,
                ct: ct);

            await AppointmentNotificationComposer.InvalidatePendingRemindersAsync(
                itemDb,
                lockedAppt.Id,
                "Hủy gửi nhắc lịch do lịch hẹn đã chuyển sang trạng thái vắng mặt (NO_SHOW).",
                ct);

            var apptCode = !string.IsNullOrWhiteSpace(lockedAppt.AppointmentCode)
                ? lockedAppt.AppointmentCode!
                : $"APT-{lockedAppt.Id}";

            itemDb.AuditLogs.Add(new AuditLog
            {
                ActorUserId = null,
                HospitalId = hospitalId > 0 ? hospitalId : null,
                Action = AutoNoShowAuditAction,
                EntityType = "APPOINTMENT",
                EntityId = lockedAppt.Id.ToString(CultureInfo.InvariantCulture),
                Reason = noShowReason,
                ChangeData = JsonSerializer.Serialize(new
                {
                    appointmentId = lockedAppt.Id,
                    appointmentCode = apptCode,
                    patientId = lockedAppt.PatientId.Value,
                    slotId = slot.Id,
                    slotStartsAt = slot.StartsAt,
                    slotEndsAt = slot.EndsAt,
                    graceMinutes,
                    previousStatus,
                    newStatus = AppointmentBookingRules.NoShowStatus,
                    cancelledQueueEntryIds = cancelledQueues.Select(q => q.Id).ToArray(),
                    preservedQueueNumbers = cancelledQueues.Select(q => q.QueueNumber).ToArray(),
                }),
                IpAddress = null,
                OccurredAt = nowUtc,
            });

            var noShowNotifKey = AppointmentNotificationComposer.BuildNoShowIdempotencyKey(lockedAppt.Id, slot.Id);
            var alreadyNotified = await itemDb.Notifications
                .AsNoTracking()
                .AnyAsync(n => n.IdempotencyKey == noShowNotifKey, ct);

            if (!alreadyNotified)
            {
                var authorizedEmail = await AppointmentNotificationComposer.ResolveAuthorizedRecipientEmailAsync(
                    itemDb,
                    lockedAppt.PatientId.Value,
                    lockedAppt.BookedByUserId,
                    nowLocal,
                    ct);

                var serviceName = !string.IsNullOrWhiteSpace(slot.Service?.Name)
                    ? slot.Service!.Name
                    : "Khám chuyên khoa ngoại trú";

                var noShowNotification = AppointmentNotificationComposer.CreateNoShowNotification(
                    patientId: lockedAppt.PatientId.Value,
                    appointmentId: lockedAppt.Id,
                    slotId: slot.Id,
                    appointmentCode: apptCode,
                    serviceName: serviceName,
                    startsAt: slot.StartsAt ?? slot.EndsAt.Value,
                    authorizedEmail: authorizedEmail,
                    nowLocal: nowLocal);

                await InsertNotificationIdempotentAsync(itemDb, isRelational, noShowNotification, ct);
            }

            await itemDb.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            var publisher = itemScope.ServiceProvider.GetService<MedFlow.Application.Interfaces.Services.IRealtimeNotificationPublisher>();
            var revoker = itemScope.ServiceProvider.GetService<MedFlow.Application.Interfaces.Services.IRealtimeAccessRevoker>();
            var noShowServicePointId = cancelledQueues
                .Select(q => q.CurrentServicePointId ?? q.ServicePointId)
                .FirstOrDefault(id => id.HasValue);

            if (publisher is not null)
            {
                await MedFlow.Application.Services.Realtime.RealtimePublishSafeExtensions.TryPublishQueueChangedSafeAsync(
                    publisher,
                    new MedFlow.Application.DTOs.Realtime.QueueChangedNotification(
                        HospitalId: hospitalId,
                        ServicePointId: noShowServicePointId,
                        RoomId: slot.RoomId,
                        AppointmentId: lockedAppt.Id),
                    ct);
            }

            if (revoker is not null)
            {
                await MedFlow.Application.Services.Realtime.RealtimeRevokeSafeExtensions.TryRevokeAppointmentTrackingSafeAsync(
                    revoker,
                    lockedAppt.Id,
                    ct);
            }

            autoNoShowCount++;
            cancelledQueueCount += cancelledQueues.Count;
        }

        return (autoNoShowCount, cancelledQueueCount);
    }

    private static async Task<int> InsertNotificationIdempotentAsync(
        IApplicationDbContext db,
        bool isRelational,
        Notification notification,
        CancellationToken ct)
    {
        if (isRelational)
        {
            var parameters = new object[]
            {
                new NpgsqlParameter("patientId", NpgsqlDbType.Bigint) { Value = (object?)notification.PatientId ?? DBNull.Value },
                new NpgsqlParameter("appointmentId", NpgsqlDbType.Bigint) { Value = (object?)notification.AppointmentId ?? DBNull.Value },
                new NpgsqlParameter("visitId", NpgsqlDbType.Bigint) { Value = (object?)notification.VisitId ?? DBNull.Value },
                new NpgsqlParameter("channel", NpgsqlDbType.Varchar) { Value = (object?)notification.Channel ?? DBNull.Value },
                new NpgsqlParameter("recipient", NpgsqlDbType.Varchar) { Value = (object?)notification.Recipient ?? DBNull.Value },
                new NpgsqlParameter("subject", NpgsqlDbType.Varchar) { Value = (object?)notification.Subject ?? DBNull.Value },
                new NpgsqlParameter("body", NpgsqlDbType.Text) { Value = (object?)notification.Body ?? DBNull.Value },
                new NpgsqlParameter("status", NpgsqlDbType.Varchar) { Value = (object?)notification.Status ?? DBNull.Value },
                new NpgsqlParameter("retryCount", NpgsqlDbType.Integer) { Value = notification.RetryCount },
                new NpgsqlParameter("idempotencyKey", NpgsqlDbType.Varchar) { Value = (object?)notification.IdempotencyKey ?? DBNull.Value },
                new NpgsqlParameter("notificationType", NpgsqlDbType.Varchar) { Value = (object?)notification.NotificationType ?? DBNull.Value },
                new NpgsqlParameter("processingToken", NpgsqlDbType.Varchar) { Value = (object?)notification.ProcessingToken ?? DBNull.Value },
                new NpgsqlParameter("leaseUntil", NpgsqlDbType.Timestamp) { Value = (object?)notification.LeaseUntil ?? DBNull.Value },
                new NpgsqlParameter("scheduledAt", NpgsqlDbType.Timestamp) { Value = (object?)notification.ScheduledAt ?? DBNull.Value },
                new NpgsqlParameter("sentAt", NpgsqlDbType.Timestamp) { Value = (object?)notification.SentAt ?? DBNull.Value },
                new NpgsqlParameter("readAt", NpgsqlDbType.Timestamp) { Value = (object?)notification.ReadAt ?? DBNull.Value },
                new NpgsqlParameter("lastError", NpgsqlDbType.Text) { Value = (object?)notification.LastError ?? DBNull.Value },
                new NpgsqlParameter("createdAt", NpgsqlDbType.Timestamp) { Value = (object?)notification.CreatedAt ?? DBNull.Value },
            };

            return await ((MedFlowDbContext)db).Database.ExecuteSqlRawAsync("""
                INSERT INTO "notifications" (
                    "patient_id",
                    "appointment_id",
                    "visit_id",
                    "channel",
                    "recipient",
                    "subject",
                    "body",
                    "status",
                    "retry_count",
                    "idempotency_key",
                    "notification_type",
                    "processing_token",
                    "lease_until",
                    "scheduled_at",
                    "sent_at",
                    "read_at",
                    "last_error",
                    "created_at"
                )
                VALUES (
                    @patientId,
                    @appointmentId,
                    @visitId,
                    @channel,
                    @recipient,
                    @subject,
                    @body,
                    @status,
                    @retryCount,
                    @idempotencyKey,
                    @notificationType,
                    @processingToken,
                    @leaseUntil,
                    @scheduledAt,
                    @sentAt,
                    @readAt,
                    @lastError,
                    @createdAt
                )
                ON CONFLICT ("idempotency_key") WHERE "idempotency_key" IS NOT NULL DO NOTHING
                """, parameters, ct);
        }

        try
        {
            db.Notifications.Add(notification);
            await db.SaveChangesAsync(ct);
            return 1;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return 0;
        }
    }
}
