using System.Text.Json;
using MedFlow.Application.Common.Time;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Options;
using MedFlow.Application.Services.Notifications;
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

/// <summary>
/// Runs at startup and every <c>RollingSlotCheckHours</c> (default 6h) to top up missing future
/// <c>examination_slots</c> within each active hospital's configured booking window.
/// <para>
/// Safety invariants:
/// <list type="bullet">
/// <item>Projects only from valid configured/established duty templates — never invents Mon–Sat schedules for doctors without an active schedule.</item>
/// <item>Enforces <see cref="HospitalShiftRules"/> active serving windows (Morning 07:30–11:30, Afternoon 13:30–16:30, excluding Sundays).</item>
/// <item>Never reopens or overwrites <c>CLOSED</c> slots, and prevents doctor/room/multi-service overlap both in-memory and via PostgreSQL unique indexes + <c>ON CONFLICT DO NOTHING</c>.</item>
/// </list>
/// </para>
/// </summary>
public sealed class RollingExaminationSlotGenerator : BackgroundService
{
    private const string AppointmentPolicyType = "APPOINTMENT";
    private const string BookingWindowPolicyKey = "booking_window_days";
    private const string PropMaxDaysAhead = "max_days_ahead";
    private const string PropMinutes = "minutes";
    private const string SchedulingPolicyType = "SCHEDULING";
    private const string SlotDurationPolicyKey = "slot_duration_minutes";
    private const string SlotCapacityPolicyKey = "slot_capacity";
    private const string DoctorWeeklySchedulePolicyKey = "doctor_weekly_schedule";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<ScheduleJobsOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RollingExaminationSlotGenerator> _logger;

    public RollingExaminationSlotGenerator(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<ScheduleJobsOptions> options,
        TimeProvider timeProvider,
        ILogger<RollingExaminationSlotGenerator> logger)
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
            _logger.LogInformation("RollingExaminationSlotGenerator is disabled via ScheduleJobs:Enabled=false.");
            return;
        }

        // Run initial check at startup, then every RollingSlotCheckHours.
        try
        {
            await EnsureUpcomingSlotsAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error during startup RollingExaminationSlotGenerator check.");
        }

        var interval = TimeSpan.FromHours(Math.Max(1, initialOpts.RollingSlotCheckHours));
        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (!_options.CurrentValue.Enabled)
            {
                continue;
            }

            try
            {
                await EnsureUpcomingSlotsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error while running RollingExaminationSlotGenerator cycle.");
            }
        }
    }

    public async Task<int> EnsureUpcomingSlotsAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var isRelational = db is MedFlowDbContext efDb && efDb.Database.IsRelational();

        var opts = _options.CurrentValue;
        var nowUtc = HospitalClock.GetUtcNow(_timeProvider);
        var today = HospitalClock.GetToday(_timeProvider);

        var activeHospitalIds = await db.Hospitals
            .AsNoTracking()
            .Where(h => h.IsActive == true)
            .Select(h => h.Id)
            .ToListAsync(ct);

        if (activeHospitalIds.Count == 0)
        {
            return 0;
        }

        var totalCreated = 0;
        foreach (var hospitalId in activeHospitalIds)
        {
            totalCreated += await EnsureHospitalSlotsAsync(
                db,
                isRelational,
                hospitalId,
                today,
                nowUtc,
                opts,
                ct);
        }

        if (totalCreated > 0)
        {
            _logger.LogInformation("RollingExaminationSlotGenerator generated {CreatedCount} upcoming examination slots.", totalCreated);
        }

        return totalCreated;
    }

    private static async Task<int> EnsureHospitalSlotsAsync(
        IApplicationDbContext db,
        bool isRelational,
        long hospitalId,
        DateOnly today,
        DateTime nowUtc,
        ScheduleJobsOptions opts,
        CancellationToken ct)
    {
        var policyRows = await db.HospitalPolicies
            .AsNoTracking()
            .Where(p =>
                p.HospitalId == hospitalId &&
                p.QueueServicePointId == null &&
                (p.PolicyType == AppointmentPolicyType || p.PolicyType == SchedulingPolicyType))
            .ToListAsync(ct);

        var apptPolicies = AppointmentNotificationComposer.SelectCurrentPolicies(
            policyRows.Where(r => r.PolicyType == AppointmentPolicyType),
            nowUtc);
        var schedPolicies = AppointmentNotificationComposer.SelectCurrentPolicies(
            policyRows.Where(r => r.PolicyType == SchedulingPolicyType),
            nowUtc);

        var defaultWindow = Math.Clamp(opts.DefaultRollingSlotWindowDays, 1, opts.MaxRollingSlotWindowDays);
        var windowDays = apptPolicies.TryGetValue(BookingWindowPolicyKey, out var windowRow)
            ? AppointmentNotificationComposer.ReadPolicyInt(windowRow, PropMaxDaysAhead, defaultWindow)
            : defaultWindow;
        windowDays = Math.Clamp(windowDays, 1, Math.Max(1, opts.MaxRollingSlotWindowDays));

        var defaultDuration = Math.Clamp(opts.DefaultSlotDurationMinutes, 10, 120);
        var policyDurationMinutes = schedPolicies.TryGetValue(SlotDurationPolicyKey, out var durationRow)
            ? Math.Clamp(AppointmentNotificationComposer.ReadPolicyInt(durationRow, PropMinutes, defaultDuration), 10, 120)
            : defaultDuration;

        var defaultCapacity = Math.Clamp(opts.DefaultSlotCapacity, 1, 50);
        var policyCapacity = schedPolicies.TryGetValue(SlotCapacityPolicyKey, out var capRow)
            ? Math.Clamp(AppointmentNotificationComposer.ReadPolicyInt(capRow, "capacity", defaultCapacity), 1, 50)
            : defaultCapacity;

        var activeServices = await db.Services
            .AsNoTracking()
            .Include(s => s.Department)
            .Where(s =>
                s.HospitalId == hospitalId &&
                s.IsActive == true &&
                s.AllowsOnlineBooking == true &&
                s.ServiceType.ToUpper() == "EXAMINATION" &&
                (s.Department == null || s.Department.IsActive != false))
            .ToDictionaryAsync(s => s.Id, ct);

        if (activeServices.Count == 0)
        {
            return 0;
        }

        var activeRooms = await db.HospitalRooms
            .AsNoTracking()
            .Include(r => r.Department)
            .Where(r =>
                r.HospitalId == hospitalId &&
                r.Status.ToUpper() == "ACTIVE" &&
                r.RoomType.ToUpper() == "EXAMINATION" &&
                (r.Department == null || r.Department.IsActive != false))
            .ToDictionaryAsync(r => r.Id, ct);

        if (activeRooms.Count == 0)
        {
            return 0;
        }

        var activeDoctors = await db.Doctors
            .AsNoTracking()
            .Include(d => d.Staff)
            .Where(d =>
                d.HospitalId == hospitalId &&
                d.IsActive == true &&
                d.Staff != null &&
                d.Staff.Status != null &&
                d.Staff.Status.ToUpper() == "ACTIVE")
            .ToDictionaryAsync(d => d.Id, ct);

        var activeAuthorizations = (await db.DoctorServiceAuthorizations
            .AsNoTracking()
            .Where(a =>
                a.IsActive == true &&
                activeDoctors.Keys.Contains(a.DoctorId) &&
                activeServices.Keys.Contains(a.ServiceId))
            .Select(a => new { a.DoctorId, a.ServiceId })
            .ToListAsync(ct))
            .Select(a => (a.DoctorId, a.ServiceId))
            .ToHashSet();

        // Derive duty templates from configured policy (if present) or existing valid non-closed duty slots
        // within [today - 35 days .. today + windowDays]. Doctors without any valid duty schedule are NOT auto-populated.
        var lookbackStart = today.AddDays(-35).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var windowEndExclusive = today.AddDays(windowDays + 1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);

        var referenceSlots = await db.ExaminationSlots
            .AsNoTracking()
            .Where(s =>
                s.StartsAt != null &&
                s.EndsAt != null &&
                s.StartsAt >= lookbackStart &&
                s.StartsAt < windowEndExclusive &&
                ((s.ServiceId != null && activeServices.Keys.Contains(s.ServiceId.Value)) ||
                 (s.DoctorId != null && activeDoctors.Keys.Contains(s.DoctorId.Value)) ||
                 (s.RoomId != null && activeRooms.Keys.Contains(s.RoomId.Value))))
            .OrderByDescending(s => s.StartsAt)
            .ThenBy(s => s.Id)
            .ToListAsync(ct);

        var templates = ExtractValidDutyTemplates(
            schedPolicies.GetValueOrDefault(DoctorWeeklySchedulePolicyKey),
            referenceSlots,
            activeDoctors.Keys,
            activeServices,
            activeRooms,
            activeAuthorizations,
            policyDurationMinutes,
            policyCapacity);

        if (templates.Count == 0)
        {
            return 0;
        }

        var futureStart = today.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var existingFutureSlots = referenceSlots
            .Where(s => s.StartsAt!.Value >= futureStart && s.StartsAt.Value < windowEndExclusive)
            .Select(s => new SlotInterval(
                DoctorId: s.DoctorId,
                ServiceId: s.ServiceId,
                RoomId: s.RoomId,
                StartsAt: s.StartsAt!.Value,
                EndsAt: s.EndsAt!.Value,
                Status: s.Status))
            .ToList();

        var createdCount = 0;

        for (var offset = 1; offset <= windowDays; offset++)
        {
            var targetDate = today.AddDays(offset);
            if (targetDate.DayOfWeek == DayOfWeek.Sunday)
            {
                continue;
            }

            var dayTemplates = templates
                .Where(t => t.DayOfWeek == targetDate.DayOfWeek)
                // Generate doctor-assigned duty slots first so unassigned fallback slots never preempt a doctor's room.
                .OrderBy(t => t.DoctorId.HasValue ? 0 : 1)
                .ThenBy(t => t.StartTime)
                .ThenBy(t => t.DoctorId ?? long.MaxValue)
                .ThenBy(t => t.ServiceId)
                .ToList();

            foreach (var tpl in dayTemplates)
            {
                var startsAt = targetDate.ToDateTime(tpl.StartTime, DateTimeKind.Unspecified);
                var endsAt = startsAt.AddMinutes(tpl.DurationMinutes);

                // Never reopen or recreate a slot that already exists (including CLOSED slots) or overlaps doctor/room.
                if (HasConflictOrClosedMatch(existingFutureSlots, tpl.DoctorId, tpl.ServiceId, tpl.RoomId, startsAt, endsAt))
                {
                    continue;
                }

                int inserted;
                if (isRelational)
                {
                    inserted = await InsertSlotAtomicAsync((MedFlowDbContext)db, tpl, startsAt, endsAt, ct);
                }
                else
                {
                    db.ExaminationSlots.Add(new ExaminationSlot
                    {
                        ServiceId = tpl.ServiceId,
                        DoctorId = tpl.DoctorId,
                        RoomId = tpl.RoomId,
                        StartsAt = startsAt,
                        EndsAt = endsAt,
                        Capacity = tpl.Capacity,
                        Status = "OPEN",
                    });
                    await db.SaveChangesAsync(ct);
                    inserted = 1;
                }

                if (inserted > 0)
                {
                    createdCount += inserted;
                    existingFutureSlots.Add(new SlotInterval(
                        DoctorId: tpl.DoctorId,
                        ServiceId: tpl.ServiceId,
                        RoomId: tpl.RoomId,
                        StartsAt: startsAt,
                        EndsAt: endsAt,
                        Status: "OPEN"));
                }
            }
        }

        return createdCount;
    }

    private static List<DutySlotTemplate> ExtractValidDutyTemplates(
        HospitalPolicy? explicitSchedulePolicy,
        IReadOnlyList<ExaminationSlot> referenceSlots,
        ICollection<long> activeDoctorIds,
        IReadOnlyDictionary<long, Service> activeServices,
        IReadOnlyDictionary<long, HospitalRoom> activeRooms,
        IReadOnlySet<(long DoctorId, long ServiceId)> activeAuthorizations,
        int defaultDurationMinutes,
        int defaultCapacity)
    {
        var results = new List<DutySlotTemplate>();
        var seenDoctorDayTimes = new HashSet<(long DoctorId, DayOfWeek Day, TimeOnly Start)>();
        var seenRoomDayTimes = new HashSet<(long RoomId, DayOfWeek Day, TimeOnly Start)>();
        var seenUnassignedServiceDayTimes = new HashSet<(long ServiceId, long RoomId, DayOfWeek Day, TimeOnly Start)>();

        // 1. Optional explicit JSON policy template (policy_type='SCHEDULING', policy_key='doctor_weekly_schedule')
        if (explicitSchedulePolicy is not null && !string.IsNullOrWhiteSpace(explicitSchedulePolicy.PolicyValue))
        {
            TryAppendPolicyTemplates(
                explicitSchedulePolicy.PolicyValue,
                activeDoctorIds,
                activeServices,
                activeRooms,
                activeAuthorizations,
                defaultDurationMinutes,
                defaultCapacity,
                results,
                seenDoctorDayTimes,
                seenRoomDayTimes);
        }

        // 2. Project established non-closed duty slots configured in examination_slots
        foreach (var slot in referenceSlots)
        {
            if (!string.Equals(slot.Status, "OPEN", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(slot.Status, "FULL", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!slot.ServiceId.HasValue || !slot.RoomId.HasValue || !slot.StartsAt.HasValue || !slot.EndsAt.HasValue)
            {
                continue;
            }

            var serviceId = slot.ServiceId.Value;
            var roomId = slot.RoomId.Value;
            if (!activeServices.TryGetValue(serviceId, out var service) ||
                !activeRooms.TryGetValue(roomId, out var room))
            {
                continue;
            }

            if (service.DepartmentId.HasValue && room.DepartmentId.HasValue && service.DepartmentId != room.DepartmentId)
            {
                continue;
            }

            var startsAt = slot.StartsAt.Value;
            var endsAt = slot.EndsAt.Value;
            var dayOfWeek = startsAt.DayOfWeek;
            if (dayOfWeek == DayOfWeek.Sunday)
            {
                continue;
            }

            var startTime = TimeOnly.FromDateTime(startsAt);
            var endTime = TimeOnly.FromDateTime(endsAt);
            if (!IsValidServingSlotWindow(startTime, endTime))
            {
                continue;
            }

            var duration = (int)Math.Round((endsAt - startsAt).TotalMinutes);
            if (duration < 10 || duration > 120)
            {
                duration = defaultDurationMinutes;
            }

            var capacity = slot.Capacity is > 0 and <= 50 ? slot.Capacity.Value : defaultCapacity;

            if (slot.DoctorId.HasValue)
            {
                var docId = slot.DoctorId.Value;
                if (!activeDoctorIds.Contains(docId) || !activeAuthorizations.Contains((docId, serviceId)))
                {
                    continue;
                }

                // Deduplicate per (DoctorId, DayOfWeek, StartTime) and (RoomId, DayOfWeek, StartTime)
                // so a multi-service doctor never gets parallel overlapping slots across services.
                if (!seenDoctorDayTimes.Add((docId, dayOfWeek, startTime)) ||
                    !seenRoomDayTimes.Add((roomId, dayOfWeek, startTime)))
                {
                    continue;
                }

                results.Add(new DutySlotTemplate(
                    DoctorId: docId,
                    ServiceId: serviceId,
                    RoomId: roomId,
                    DayOfWeek: dayOfWeek,
                    StartTime: startTime,
                    DurationMinutes: duration,
                    Capacity: capacity));
            }
            else
            {
                if (!seenUnassignedServiceDayTimes.Add((serviceId, roomId, dayOfWeek, startTime)) ||
                    seenRoomDayTimes.Contains((roomId, dayOfWeek, startTime)))
                {
                    continue;
                }

                results.Add(new DutySlotTemplate(
                    DoctorId: null,
                    ServiceId: serviceId,
                    RoomId: roomId,
                    DayOfWeek: dayOfWeek,
                    StartTime: startTime,
                    DurationMinutes: duration,
                    Capacity: capacity));
            }
        }

        return results;
    }

    private static void TryAppendPolicyTemplates(
        string json,
        ICollection<long> activeDoctorIds,
        IReadOnlyDictionary<long, Service> activeServices,
        IReadOnlyDictionary<long, HospitalRoom> activeRooms,
        IReadOnlySet<(long DoctorId, long ServiceId)> activeAuthorizations,
        int defaultDurationMinutes,
        int defaultCapacity,
        List<DutySlotTemplate> results,
        HashSet<(long DoctorId, DayOfWeek Day, TimeOnly Start)> seenDoctorDayTimes,
        HashSet<(long RoomId, DayOfWeek Day, TimeOnly Start)> seenRoomDayTimes)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                !doc.RootElement.TryGetProperty("schedules", out var schedulesEl) ||
                schedulesEl.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var entry in schedulesEl.EnumerateArray())
            {
                if (!entry.TryGetProperty("doctor_id", out var docEl) || !docEl.TryGetInt64(out var doctorId) ||
                    !entry.TryGetProperty("service_id", out var svcEl) || !svcEl.TryGetInt64(out var serviceId) ||
                    !entry.TryGetProperty("room_id", out var roomEl) || !roomEl.TryGetInt64(out var roomId) ||
                    !entry.TryGetProperty("iso_dow", out var dowEl) || !dowEl.TryGetInt32(out var isoDow))
                {
                    continue;
                }

                if (isoDow is < 1 or > 6 ||
                    !activeDoctorIds.Contains(doctorId) ||
                    !activeServices.ContainsKey(serviceId) ||
                    !activeRooms.ContainsKey(roomId) ||
                    !activeAuthorizations.Contains((doctorId, serviceId)))
                {
                    continue;
                }

                var dayOfWeek = (DayOfWeek)isoDow;
                var includeMorning = !entry.TryGetProperty("morning", out var mEl) || mEl.ValueKind != JsonValueKind.False;
                var includeAfternoon = entry.TryGetProperty("afternoon", out var aEl) && aEl.ValueKind == JsonValueKind.True;

                if (includeMorning)
                {
                    AppendShiftSlots(
                        doctorId,
                        serviceId,
                        roomId,
                        dayOfWeek,
                        HospitalShiftRules.MorningStart,
                        HospitalShiftRules.MorningEnd,
                        defaultDurationMinutes,
                        defaultCapacity,
                        results,
                        seenDoctorDayTimes,
                        seenRoomDayTimes);
                }

                if (includeAfternoon)
                {
                    AppendShiftSlots(
                        doctorId,
                        serviceId,
                        roomId,
                        dayOfWeek,
                        HospitalShiftRules.AfternoonStart,
                        HospitalShiftRules.AfternoonEnd,
                        defaultDurationMinutes,
                        defaultCapacity,
                        results,
                        seenDoctorDayTimes,
                        seenRoomDayTimes);
                }
            }
        }
        catch (JsonException)
        {
            // Ignore malformed policy JSON and fall back to established slot templates.
        }
    }

    private static void AppendShiftSlots(
        long doctorId,
        long serviceId,
        long roomId,
        DayOfWeek dayOfWeek,
        TimeOnly shiftStart,
        TimeOnly shiftEnd,
        int durationMinutes,
        int capacity,
        List<DutySlotTemplate> results,
        HashSet<(long DoctorId, DayOfWeek Day, TimeOnly Start)> seenDoctorDayTimes,
        HashSet<(long RoomId, DayOfWeek Day, TimeOnly Start)> seenRoomDayTimes)
    {
        for (var cursor = shiftStart; cursor.AddMinutes(durationMinutes) <= shiftEnd; cursor = cursor.AddMinutes(durationMinutes))
        {
            if (seenDoctorDayTimes.Add((doctorId, dayOfWeek, cursor)) &&
                seenRoomDayTimes.Add((roomId, dayOfWeek, cursor)))
            {
                results.Add(new DutySlotTemplate(
                    DoctorId: doctorId,
                    ServiceId: serviceId,
                    RoomId: roomId,
                    DayOfWeek: dayOfWeek,
                    StartTime: cursor,
                    DurationMinutes: durationMinutes,
                    Capacity: capacity));
            }
        }
    }

    private static bool IsValidServingSlotWindow(TimeOnly startTime, TimeOnly endTime)
    {
        if (endTime <= startTime || !HospitalShiftRules.IsWithinWorkingHours(startTime))
        {
            return false;
        }

        if (startTime >= HospitalShiftRules.MorningStart && startTime < HospitalShiftRules.MorningEnd)
        {
            return endTime <= HospitalShiftRules.MorningEnd;
        }

        if (startTime >= HospitalShiftRules.AfternoonStart && startTime < HospitalShiftRules.AfternoonEnd)
        {
            return endTime <= HospitalShiftRules.AfternoonEnd;
        }

        return false;
    }

    private static bool HasConflictOrClosedMatch(
        IReadOnlyList<SlotInterval> existingSlots,
        long? doctorId,
        long serviceId,
        long roomId,
        DateTime startsAt,
        DateTime endsAt)
    {
        foreach (var s in existingSlots)
        {
            // Exact tuple match of ANY status (including CLOSED) blocks regeneration so closed slots stay CLOSED.
            if (s.DoctorId == doctorId && s.ServiceId == serviceId && s.StartsAt == startsAt)
            {
                return true;
            }

            if (s.DoctorId is null && doctorId is null && s.ServiceId == serviceId && s.RoomId == roomId && s.StartsAt == startsAt)
            {
                return true;
            }

            var overlaps = s.StartsAt < endsAt && s.EndsAt > startsAt;
            if (!overlaps)
            {
                continue;
            }

            // Any overlapping slot (including CLOSED) for the same doctor or room prevents inserting a conflicting slot.
            if (doctorId.HasValue && s.DoctorId == doctorId.Value)
            {
                return true;
            }

            if (s.RoomId == roomId)
            {
                return true;
            }

            if (!doctorId.HasValue && s.ServiceId == serviceId)
            {
                return true;
            }
        }

        return false;
    }

    private static Task<int> InsertSlotAtomicAsync(
        MedFlowDbContext db,
        DutySlotTemplate tpl,
        DateTime startsAt,
        DateTime endsAt,
        CancellationToken ct)
    {
        var svcParam = new NpgsqlParameter("serviceId", NpgsqlDbType.Bigint) { Value = tpl.ServiceId };
        var roomParam = new NpgsqlParameter("roomId", NpgsqlDbType.Bigint) { Value = tpl.RoomId };
        var startParam = new NpgsqlParameter("startsAt", NpgsqlDbType.Timestamp) { Value = startsAt };
        var endParam = new NpgsqlParameter("endsAt", NpgsqlDbType.Timestamp) { Value = endsAt };
        var capParam = new NpgsqlParameter("capacity", NpgsqlDbType.Integer) { Value = tpl.Capacity };

        if (tpl.DoctorId.HasValue)
        {
            var docParam = new NpgsqlParameter("doctorId", NpgsqlDbType.Bigint) { Value = tpl.DoctorId.Value };
            return db.Database.ExecuteSqlRawAsync("""
                INSERT INTO "examination_slots" ("service_id", "doctor_id", "room_id", "starts_at", "ends_at", "capacity", "status")
                SELECT @serviceId, @doctorId, @roomId, @startsAt, @endsAt, @capacity, 'OPEN'
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM "examination_slots" es
                    WHERE (es."doctor_id" = @doctorId OR es."room_id" = @roomId)
                      AND es."starts_at" < @endsAt
                      AND es."ends_at" > @startsAt
                )
                ON CONFLICT DO NOTHING
                """, [svcParam, docParam, roomParam, startParam, endParam, capParam], ct);
        }

        return db.Database.ExecuteSqlRawAsync("""
            INSERT INTO "examination_slots" ("service_id", "doctor_id", "room_id", "starts_at", "ends_at", "capacity", "status")
            SELECT @serviceId, NULL, @roomId, @startsAt, @endsAt, @capacity, 'OPEN'
            WHERE NOT EXISTS (
                SELECT 1
                FROM "examination_slots" es
                WHERE (es."room_id" = @roomId OR es."service_id" = @serviceId)
                  AND es."starts_at" < @endsAt
                  AND es."ends_at" > @startsAt
            )
            ON CONFLICT DO NOTHING
            """, [svcParam, roomParam, startParam, endParam, capParam], ct);
    }

    private sealed record DutySlotTemplate(
        long? DoctorId,
        long ServiceId,
        long RoomId,
        DayOfWeek DayOfWeek,
        TimeOnly StartTime,
        int DurationMinutes,
        int Capacity);

    private sealed record SlotInterval(
        long? DoctorId,
        long? ServiceId,
        long? RoomId,
        DateTime StartsAt,
        DateTime EndsAt,
        string? Status);
}
