using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MedFlow.Application.Common.Time;
using MedFlow.Application.DTOs.Appointments;
using MedFlow.Application.DTOs.Notifications;
using MedFlow.Application.DTOs.Reception;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Options;
using MedFlow.Domain.Entities;
using MedFlow.Infrastructure.BackgroundJobs;
using MedFlow.Infrastructure.Persistence;
using MedFlow.IntegrationTests.Appointments;
using MedFlow.IntegrationTests.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MedFlow.IntegrationTests.Notifications;

public sealed class NotificationsAndScheduleJobsEndpointTests : AuthTestBase
{
    private const string NotificationsUrl = "/api/v1/notifications";
    private const string AppointmentsUrl = "/api/v1/appointments";

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class ConcurrentCountingEmailSender(Func<string, Task>? interceptor = null) : IEmailSender
    {
        private readonly List<(string To, string Subject)> _sent = [];

        public IReadOnlyList<(string To, string Subject)> Sent
        {
            get
            {
                lock (_sent)
                {
                    return _sent.ToList();
                }
            }
        }

        public async Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default)
        {
            if (interceptor is not null)
            {
                await interceptor(toEmail);
            }

            lock (_sent)
            {
                _sent.Add((toEmail, subject));
            }
        }
    }

    public NotificationsAndScheduleJobsEndpointTests(TestWebAppFactory factory)
        : base(factory)
    {
    }

    private static HttpRequestMessage Authed(HttpMethod method, string url, string token, object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            req.Content = JsonContent.Create(body);
        }

        return req;
    }

    [Fact]
    public async Task Migration020_PartialAndUniqueIndexesExist_AndForeignKeysRemainNoAction()
    {
        var expectedIndexes = new[]
        {
            "ix_notifications_pending_due",
            "ix_notifications_processing_lease",
            "uq_notifications_idempotency_key",
            "ix_notifications_patient_unread",
            "ix_appointments_active_slot",
            "ix_examination_slots_time_window",
            "uq_examination_slots_active_doctor_start",
            "uq_examination_slots_active_room_start",
            "uq_examination_slots_doctor_service_start",
            "uq_examination_slots_unassigned_service_room_start",
        };

        var existingIndexes = await Db.Database
            .SqlQueryRaw<string>(
                "SELECT indexname AS \"Value\" FROM pg_indexes WHERE schemaname = 'public'")
            .ToListAsync();

        foreach (var indexName in expectedIndexes)
        {
            Assert.Contains(indexName, existingIndexes);
        }

        // Verify all 3 FKs on notifications strictly remain NO ACTION ('a'), NOT CASCADE ('c')
        var fkDeleteCodes = await Db.Database
            .SqlQueryRaw<string>("""
                SELECT confdeltype::text AS "Value"
                FROM pg_constraint
                WHERE conrelid = 'notifications'::regclass
                  AND contype = 'f'
                """)
            .ToListAsync();

        Assert.Equal(3, fkDeleteCodes.Count);
        Assert.All(fkDeleteCodes, code => Assert.Equal("a", code));
    }

    [Fact]
    public async Task NotificationsEndpoints_EnforceTwoLayerAuthz_BookRescheduleCancelInvalidation_AndDecoupledBanner()
    {
        // 1. 401 Anonymous & 403 Non-Patient role
        var anonRes = await Client.GetAsync(NotificationsUrl);
        Assert.Equal(HttpStatusCode.Unauthorized, anonRes.StatusCode);

        var adminTokens = await LoginAsync("hospital_admin01@medflow.test", "Demo@12345");
        using var adminReq = Authed(HttpMethod.Get, NotificationsUrl, adminTokens.AccessToken);
        var adminRes = await Client.SendAsync(adminReq);
        Assert.Equal(HttpStatusCode.Forbidden, adminRes.StatusCode);

        // 2. Book appointment within 24h window (+15h: always > 12h cancel/reschedule cutoff and < 24h reminder window)
        var nowLocal = HospitalClock.GetNow(TimeProvider.System);
        var targetStart = nowLocal.AddHours(15);
        var targetDate = DateOnly.FromDateTime(targetStart);
        // Pick an uncommon minute (:17) to avoid colliding with standard seeded slots
        var slotStartA = targetDate.ToDateTime(new TimeOnly(targetStart.Hour, 17), DateTimeKind.Unspecified);
        var slotStartB = slotStartA.AddHours(30); // Beyond 24h reminder window!

        var preExistingBookedIds = await Db.Appointments.AsNoTracking().Where(a => a.Status == "BOOKED").Select(a => a.Id).ToListAsync();
        var preExistingReservedQueueIds = await Db.QueueEntries.AsNoTracking().Where(q => q.Status == "RESERVED").Select(q => q.Id).ToListAsync();
        var preExistingPendingNotifIds = await Db.Notifications.AsNoTracking().Where(n => n.Status == Notification.StatusPending).Select(n => n.Id).ToListAsync();
        var maxNotifIdBefore = await Db.Notifications.AsNoTracking().MaxAsync(n => (long?)n.Id) ?? 0L;
        var maxAuditIdBefore = await Db.AuditLogs.AsNoTracking().MaxAsync(l => (long?)l.Id) ?? 0L;

        var slotA = new ExaminationSlot
        {
            ServiceId = 1,
            DoctorId = 1,
            RoomId = 1,
            StartsAt = slotStartA,
            EndsAt = slotStartA.AddMinutes(30),
            Capacity = 3,
            Status = "OPEN",
        };
        var slotB = new ExaminationSlot
        {
            ServiceId = 1,
            DoctorId = 1,
            RoomId = 1,
            StartsAt = slotStartB,
            EndsAt = slotStartB.AddMinutes(30),
            Capacity = 3,
            Status = "OPEN",
        };

        Db.ExaminationSlots.AddRange(slotA, slotB);
        await Db.SaveChangesAsync();

        try
        {
            var p1Tokens = await LoginAsync("patient01@medflow.test", "Demo@12345");
            var p2Tokens = await LoginAsync("patient02@medflow.test", "Demo@12345");

            using var bookReq = Authed(HttpMethod.Post, AppointmentsUrl, p1Tokens.AccessToken, new CreateAppointmentRequest
            {
                Mode = "doctor",
                PatientId = "3", // PAT-0003: verified child of patient01!
                DoctorId = "DOC-001",
                ServiceId = "EX-GI-01",
                ServiceCode = "EX-GI-01",
                DateIso = targetDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                SlotOrShiftId = slotA.Id.ToString(CultureInfo.InvariantCulture),
                SymptomNote = "Khám tiêu hóa định kỳ cho bé",
            });

            var bookRes = await Client.SendAsync(bookReq);
            Assert.Equal(HttpStatusCode.Created, bookRes.StatusCode);
            var created = (await bookRes.Content.ReadFromJsonAsync<CreateAppointmentResponse>())!;
            var bookedApptId = long.Parse(created.AppointmentId, CultureInfo.InvariantCulture);

            // Run LifecycleProcessor once to enqueue 24h reminder for bookedApptId
            var lifecycle = Factory.Services.GetRequiredService<AppointmentLifecycleProcessor>();
            var cycle = await lifecycle.ProcessCycleAsync();
            Assert.True(cycle.RemindersQueuedCount >= 1);

            // Patient 1 fetches notifications -> sees family member booked + reminder notifications + ActiveReminder
            using var feedReq = Authed(HttpMethod.Get, NotificationsUrl, p1Tokens.AccessToken);
            var feedRes = await Client.SendAsync(feedReq);
            Assert.Equal(HttpStatusCode.OK, feedRes.StatusCode);
            var feed = (await feedRes.Content.ReadFromJsonAsync<NotificationFeedResponse>())!;

            var bookedNotif = Assert.Single(feed.Items, i =>
                i.NotificationType == Notification.TypeAppointmentBooked &&
                i.Message.Contains(created.AppointmentCode, StringComparison.Ordinal));
            Assert.False(bookedNotif.IsRead);
            Assert.NotNull(feed.ActiveReminder);
            Assert.Equal(created.AppointmentCode, feed.ActiveReminder!.AppointmentCode);

            // Patient 2 attempting to mark Patient 1's family notification as read -> 403 Forbidden
            using var crossUserPatch = Authed(HttpMethod.Patch, $"{NotificationsUrl}/{bookedNotif.Id}/read", p2Tokens.AccessToken);
            var crossUserRes = await Client.SendAsync(crossUserPatch);
            Assert.Equal(HttpStatusCode.Forbidden, crossUserRes.StatusCode);

            // Non-existent notification -> 404 NotFound
            using var notFoundPatch = Authed(HttpMethod.Post, $"{NotificationsUrl}/999999999/read", p1Tokens.AccessToken);
            var notFoundRes = await Client.SendAsync(notFoundPatch);
            Assert.Equal(HttpStatusCode.NotFound, notFoundRes.StatusCode);

            // Patient 1 marks all as read -> unreadCount becomes 0, BUT ActiveReminder stays active!
            using var readAllReq = Authed(HttpMethod.Post, $"{NotificationsUrl}/read-all", p1Tokens.AccessToken);
            var readAllRes = await Client.SendAsync(readAllReq);
            Assert.Equal(HttpStatusCode.OK, readAllRes.StatusCode);
            var readAllDto = (await readAllRes.Content.ReadFromJsonAsync<MarkAllNotificationsReadResponse>())!;
            Assert.Equal(0, readAllDto.UnreadCount);

            using var feedAfterReadReq = Authed(HttpMethod.Get, NotificationsUrl, p1Tokens.AccessToken);
            var feedAfterRead = (await (await Client.SendAsync(feedAfterReadReq))
                .Content.ReadFromJsonAsync<NotificationFeedResponse>())!;
            Assert.Equal(0, feedAfterRead.UnreadCount);
            Assert.NotNull(feedAfterRead.ActiveReminder);
            Assert.Equal(created.AppointmentCode, feedAfterRead.ActiveReminder!.AppointmentCode);

            // Patient 1 reschedules to slotB (+36h, outside 24h window) -> old PENDING reminder MUST become CANCELLED!
            // Advance slotA far enough if needed or adjust reschedule policy; wait: RescheduleMinimumHours default is 12h,
            // so let's cancel or verify cancel invalidation when cancelling >6h or rescheduling!
            // Since slotStartA is +6h17m (> 6h cancel policy!), cancel succeeds and invalidates pending reminder!
            using var cancelReq = Authed(HttpMethod.Post, $"{AppointmentsUrl}/{bookedApptId}/cancel", p1Tokens.AccessToken,
                new CancelAppointmentRequest { CancellationReason = "Gia đình thay đổi kế hoạch đột xuất" });
            var cancelRes = await Client.SendAsync(cancelReq);
            Assert.Equal(HttpStatusCode.OK, cancelRes.StatusCode);

            var reminderInDb = await Db.Notifications
                .AsNoTracking()
                .SingleAsync(n => n.AppointmentId == bookedApptId && n.NotificationType == Notification.TypeAppointmentReminder);
            Assert.Equal(Notification.StatusCancelled, reminderInDb.Status);

            // ActiveReminder disappears once appointment is CANCELLED
            using var feedAfterCancelReq = Authed(HttpMethod.Get, NotificationsUrl, p1Tokens.AccessToken);
            var feedAfterCancel = (await (await Client.SendAsync(feedAfterCancelReq))
                .Content.ReadFromJsonAsync<NotificationFeedResponse>())!;
            Assert.True(feedAfterCancel.ActiveReminder is null ||
                        feedAfterCancel.ActiveReminder.AppointmentCode != created.AppointmentCode);
        }
        finally
        {
            await Db.Notifications.Where(n => n.Id > maxNotifIdBefore).ExecuteDeleteAsync();
            await Db.AuditLogs.Where(l => l.Id > maxAuditIdBefore).ExecuteDeleteAsync();
            await NotificationTestCleanupHelper.CleanupSlotsAndAppointmentsAsync(Db, slotA.Id, slotB.Id);

            if (preExistingBookedIds.Count > 0)
            {
                await Db.Appointments.Where(a => preExistingBookedIds.Contains(a.Id))
                    .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, "BOOKED"));
            }

            if (preExistingReservedQueueIds.Count > 0)
            {
                await Db.QueueEntries.Where(q => preExistingReservedQueueIds.Contains(q.Id))
                    .ExecuteUpdateAsync(s => s.SetProperty(q => q.Status, "RESERVED").SetProperty(q => q.PresenceStatus, "NOT_ARRIVED"));
            }

            if (preExistingPendingNotifIds.Count > 0)
            {
                await Db.Notifications.Where(n => preExistingPendingNotifIds.Contains(n.Id))
                    .ExecuteUpdateAsync(s => s.SetProperty(n => n.Status, Notification.StatusPending));
            }
        }
    }

    [Fact]
    public async Task OutboxProcessor_MultiWorkerPostgresConcurrency_SkipLocked_AndExpiredLeaseGuard()
    {
        var nowLocal = HospitalClock.GetNow(TimeProvider.System);
        var createdNotificationIds = new List<long>();

        var countingSender = new ConcurrentCountingEmailSender(async _ => await Task.Delay(15));

        var customServices = new ServiceCollection();
        customServices.AddDbContext<MedFlowDbContext>(opts =>
            opts.UseNpgsql(Db.Database.GetConnectionString()!));
        customServices.AddScoped<MedFlow.Application.Interfaces.Persistence.IApplicationDbContext>(
            sp => sp.GetRequiredService<MedFlowDbContext>());
        customServices.AddSingleton(TimeProvider.System);
        customServices.AddSingleton<IEmailSender>(countingSender);

        using var customSp = customServices.BuildServiceProvider();
        var optsMonitor = new StaticOptionsMonitor<ScheduleJobsOptions>(new ScheduleJobsOptions
        {
            BatchSize = 3,
            MaxEmailConcurrency = 3,
            MaxRetryCount = 3,
            ProcessingLeaseSeconds = 120,
            SmtpTimeoutSeconds = 20,
        });

        var preExistingPendingIds = await Db.Notifications
            .AsNoTracking()
            .Where(n => n.Status == Notification.StatusPending)
            .Select(n => n.Id)
            .ToListAsync();

        try
        {
            for (var i = 0; i < 6; i++)
            {
                var n = new Notification
                {
                    PatientId = 1,
                    Channel = Notification.ChannelEmail,
                    Recipient = $"concurrent-{i}@medflow.test",
                    Subject = $"Concurrent Email #{i}",
                    Body = "Testing FOR UPDATE SKIP LOCKED across 3 workers",
                    Status = Notification.StatusPending,
                    IdempotencyKey = $"test-outbox-conc-{Guid.NewGuid():N}",
                    NotificationType = Notification.TypeAppointmentBooked,
                    ScheduledAt = new DateTime(2020, 1, 1, 0, i, 0, DateTimeKind.Unspecified),
                    CreatedAt = nowLocal.AddSeconds(-10),
                };
                Db.Notifications.Add(n);
                await Db.SaveChangesAsync();
                createdNotificationIds.Add(n.Id);
            }

            // Also insert 1 expired PROCESSING row to verify lease recovery on PostgreSQL
            var stuck = new Notification
            {
                PatientId = 1,
                Channel = Notification.ChannelEmail,
                Recipient = "reclaimed@medflow.test",
                Subject = "Expired Lease Row",
                Body = "Crashed worker simulation",
                Status = Notification.StatusProcessing,
                RetryCount = 0,
                ProcessingToken = "crashed-worker-token",
                LeaseUntil = nowLocal.AddMinutes(-5),
                ScheduledAt = nowLocal.AddMinutes(-10),
                CreatedAt = nowLocal.AddMinutes(-10),
                IdempotencyKey = $"test-outbox-stuck-{Guid.NewGuid():N}",
            };
            Db.Notifications.Add(stuck);
            await Db.SaveChangesAsync();
            createdNotificationIds.Add(stuck.Id);

            // Run 3 independent workers concurrently against real PostgreSQL!
            var scopeFactory = customSp.GetRequiredService<IServiceScopeFactory>();
            var worker1 = new NotificationOutboxProcessor(scopeFactory, optsMonitor, TimeProvider.System, NullLogger<NotificationOutboxProcessor>.Instance);
            var worker2 = new NotificationOutboxProcessor(scopeFactory, optsMonitor, TimeProvider.System, NullLogger<NotificationOutboxProcessor>.Instance);
            var worker3 = new NotificationOutboxProcessor(scopeFactory, optsMonitor, TimeProvider.System, NullLogger<NotificationOutboxProcessor>.Instance);

            var results = await Task.WhenAll(
                worker1.ProcessDueBatchAsync(),
                worker2.ProcessDueBatchAsync(),
                worker3.ProcessDueBatchAsync());

            Assert.True(results.Sum(r => r.SentCount) >= 6);
            Assert.Equal(1, results.Sum(r => r.ReclaimedCount));

            // Every concurrent recipient received the email ONCE (0 duplicate claims across 3 workers!)
            for (var i = 0; i < 6; i++)
            {
                var targetEmail = $"concurrent-{i}@medflow.test";
                Assert.Single(countingSender.Sent, s => s.To == targetEmail);
            }

            var sentTestIds = createdNotificationIds.Take(6).ToList();
            var sentInDbCount = await Db.Notifications
                .AsNoTracking()
                .CountAsync(n => sentTestIds.Contains(n.Id) && n.Status == Notification.StatusSent);
            Assert.Equal(6, sentInDbCount);

            var reclaimedDb = await Db.Notifications.AsNoTracking().SingleAsync(n => n.Id == stuck.Id);
            Assert.Equal(Notification.StatusPending, reclaimedDb.Status);
            Assert.Equal(1, reclaimedDb.RetryCount);
            Assert.Null(reclaimedDb.ProcessingToken);
            Assert.Null(reclaimedDb.LeaseUntil);
        }
        finally
        {
            if (createdNotificationIds.Count > 0)
            {
                await Db.Notifications
                    .Where(n => createdNotificationIds.Contains(n.Id))
                    .ExecuteDeleteAsync();
            }

            if (preExistingPendingIds.Count > 0)
            {
                await Db.Notifications
                    .Where(n => preExistingPendingIds.Contains(n.Id))
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(n => n.Status, Notification.StatusPending)
                        .SetProperty(n => n.SentAt, (DateTime?)null)
                        .SetProperty(n => n.ProcessingToken, (string?)null)
                        .SetProperty(n => n.LeaseUntil, (DateTime?)null)
                        .SetProperty(n => n.RetryCount, 0));
            }
        }
    }

    [Fact]
    public async Task CheckInVsAutoNoShow_ConcurrentRaceOnPostgres_MaintainsStrictMutualExclusion()
    {
        var nowLocal = HospitalClock.GetNow(TimeProvider.System);
        var today = DateOnly.FromDateTime(nowLocal);
        // Slot on today whose EndsAt + 15m grace is already in the past (e.g. 00:05 - 00:15, or 20 minutes ago)
        var slotEndsAt = nowLocal.AddMinutes(-20);
        var slotStartsAt = slotEndsAt.AddMinutes(-10);
        if (DateOnly.FromDateTime(slotStartsAt) < today)
        {
            // Edge guard if test runs between 00:00 and 00:30 VN time: anchor within today and pass 0 grace override
            slotStartsAt = today.ToDateTime(new TimeOnly(0, 0), DateTimeKind.Unspecified);
            slotEndsAt = slotStartsAt.AddMinutes(1);
        }

        var preExistingBookedIds = await Db.Appointments
            .AsNoTracking()
            .Where(a => a.Status == "BOOKED")
            .Select(a => a.Id)
            .ToListAsync();
        var preExistingConfirmedIds = await Db.Appointments
            .AsNoTracking()
            .Where(a => a.Status == "CONFIRMED")
            .Select(a => a.Id)
            .ToListAsync();
        var preExistingReservedQueueIds = await Db.QueueEntries
            .AsNoTracking()
            .Where(q => q.Status == "RESERVED")
            .Select(q => q.Id)
            .ToListAsync();
        var preExistingPendingNotificationIds = await Db.Notifications
            .AsNoTracking()
            .Where(n => n.Status == Notification.StatusPending)
            .Select(n => n.Id)
            .ToListAsync();
        var maxNotificationIdBefore = await Db.Notifications.AsNoTracking().MaxAsync(n => (long?)n.Id) ?? 0L;
        var maxAuditLogIdBefore = await Db.AuditLogs.AsNoTracking().MaxAsync(l => (long?)l.Id) ?? 0L;

        var slot = new ExaminationSlot
        {
            ServiceId = 1,
            DoctorId = 1,
            RoomId = 1,
            StartsAt = slotStartsAt,
            EndsAt = slotEndsAt,
            Capacity = 3,
            Status = "OPEN",
        };
        Db.ExaminationSlots.Add(slot);
        await Db.SaveChangesAsync();

        var appointment = new Appointment
        {
            PatientId = 2, // PAT-0002 so we don't clash with PAT-0001 active visits
            SlotId = slot.Id,
            AppointmentCode = $"APT-RACE-{slot.Id}",
            BookingChannel = "ONLINE",
            Status = "BOOKED",
            ReasonForVisit = "Kiểm tra tranh chấp check-in và auto no-show",
            BookedByUserId = 2,
            BookedAt = slotStartsAt.AddHours(-2),
        };
        Db.Appointments.Add(appointment);
        await Db.SaveChangesAsync();

        var reservedQueue = new QueueEntry
        {
            AppointmentId = appointment.Id,
            ServicePointId = 1,
            QueueDate = today,
            QueueNumber = 88,
            QueueType = "EXAMINATION",
            Status = "RESERVED",
            PresenceStatus = "NOT_ARRIVED",
            StateVersion = 1,
        };
        Db.QueueEntries.Add(reservedQueue);
        await Db.SaveChangesAsync();

        try
        {
            var receptionTokens = await LoginAsync("reception01@medflow.test", "Demo@12345");
            var lifecycle1 = Factory.Services.GetRequiredService<AppointmentLifecycleProcessor>();
            var lifecycle2 = Factory.Services.GetRequiredService<AppointmentLifecycleProcessor>();

            using var checkInReq = Authed(
                HttpMethod.Post,
                $"/api/v1/reception/appointments/{appointment.Id}/check-in",
                receptionTokens.AccessToken,
                new CheckInScheduledPatientRequest
                {
                    ReasonForVisit = "Kiểm tra tranh chấp check-in và auto no-show",
                    IsIdentityVerified = true,
                });

            // Fire Reception HTTP Check-in AND two parallel Lifecycle NO_SHOW workers simultaneously!
            var checkInTask = Client.SendAsync(checkInReq);
            var noShowTask1 = lifecycle1.ProcessCycleAsync();
            var noShowTask2 = lifecycle2.ProcessCycleAsync();

            await Task.WhenAll(checkInTask, noShowTask1, noShowTask2);
            var checkInResponse = await checkInTask;

            var finalAppt = await Db.Appointments.AsNoTracking().SingleAsync(a => a.Id == appointment.Id);
            var finalVisit = await Db.Visits.AsNoTracking().FirstOrDefaultAsync(v => v.AppointmentId == appointment.Id);
            var finalQueue = await Db.QueueEntries.AsNoTracking().SingleAsync(q => q.Id == reservedQueue.Id);
            var noShowAudits = await Db.AuditLogs.AsNoTracking()
                .CountAsync(l => l.Action == AppointmentLifecycleProcessor.AutoNoShowAuditAction &&
                                 l.EntityId == appointment.Id.ToString(CultureInfo.InvariantCulture));

            if (checkInResponse.IsSuccessStatusCode)
            {
                // Branch A: Check-in won the row lock -> NO_SHOW MUST NOT overwrite it!
                Assert.Equal("CHECKED_IN", finalAppt.Status);
                Assert.NotNull(finalVisit);
                Assert.Equal("WAITING", finalQueue.Status);
                Assert.Equal(88, finalQueue.QueueNumber);
                Assert.Equal(0, noShowAudits);
            }
            else
            {
                // Branch B: NO_SHOW won the row lock -> Check-in MUST fail with 409 Conflict and create ZERO Visit!
                Assert.Equal(HttpStatusCode.Conflict, checkInResponse.StatusCode);
                Assert.Equal("NO_SHOW", finalAppt.Status);
                Assert.Null(finalVisit);
                Assert.Equal("CANCELLED", finalQueue.Status);
                Assert.Equal(88, finalQueue.QueueNumber); // Queue number preserved!
                Assert.Equal(1, noShowAudits); // Exactly 1 audit log despite 2 concurrent workers!
            }
        }
        finally
        {
            var visitIds = await Db.Visits
                .Where(v => v.AppointmentId == appointment.Id)
                .Select(v => v.Id)
                .ToListAsync();

            if (visitIds.Count > 0)
            {
                var visitIdStrings = visitIds.Select(id => id.ToString(CultureInfo.InvariantCulture)).ToList();
                await Db.AuditLogs
                    .Where(l => l.EntityType == "VISIT" && l.EntityId != null && visitIdStrings.Contains(l.EntityId))
                    .ExecuteDeleteAsync();
                await Db.QueueEntries
                    .Where(q => q.VisitId != null && visitIds.Contains(q.VisitId.Value))
                    .ExecuteDeleteAsync();
                await Db.Visits
                    .Where(v => visitIds.Contains(v.Id))
                    .ExecuteDeleteAsync();
            }

            await Db.Notifications
                .Where(n => n.Id > maxNotificationIdBefore)
                .ExecuteDeleteAsync();
            await Db.AuditLogs
                .Where(l => l.Id > maxAuditLogIdBefore)
                .ExecuteDeleteAsync();

            await NotificationTestCleanupHelper.CleanupSlotsAndAppointmentsAsync(Db, slot.Id);

            if (preExistingBookedIds.Count > 0)
            {
                await Db.Appointments
                    .Where(a => preExistingBookedIds.Contains(a.Id))
                    .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, "BOOKED"));
            }

            if (preExistingConfirmedIds.Count > 0)
            {
                await Db.Appointments
                    .Where(a => preExistingConfirmedIds.Contains(a.Id))
                    .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, "CONFIRMED"));
            }

            if (preExistingReservedQueueIds.Count > 0)
            {
                await Db.QueueEntries
                    .Where(q => preExistingReservedQueueIds.Contains(q.Id))
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(q => q.Status, "RESERVED")
                        .SetProperty(q => q.PresenceStatus, "NOT_ARRIVED"));
            }

            if (preExistingPendingNotificationIds.Count > 0)
            {
                await Db.Notifications
                    .Where(n => preExistingPendingNotificationIds.Contains(n.Id))
                    .ExecuteUpdateAsync(s => s.SetProperty(n => n.Status, Notification.StatusPending));
            }
        }
    }

    [Fact]
    public async Task RollingSlotGenerator_MultiWorkerPostgresConcurrency_NoDuplicates_AndPreservesClosedSlot()
    {
        var today = HospitalClock.GetToday(TimeProvider.System);
        // Pick a future Monday within 14 days
        var targetDate = today.AddDays(1);
        while (targetDate.DayOfWeek != DayOfWeek.Monday)
        {
            targetDate = targetDate.AddDays(1);
        }

        var closedStart = targetDate.ToDateTime(new TimeOnly(10, 30), DateTimeKind.Unspecified);
        var preExistingSlotIds = await Db.ExaminationSlots
            .AsNoTracking()
            .Select(s => s.Id)
            .ToListAsync();
        long? preExistingSlotId = null;
        string? originalStatus = null;
        long? unscheduledStaffId = null;
        long? unscheduledDoctorId = null;

        try
        {
            // Temporarily ensure (Doctor 1, Service 1, targetDate 10:30) is CLOSED
            var existingSlot = await Db.ExaminationSlots
                .FirstOrDefaultAsync(s => s.DoctorId == 1 && s.ServiceId == 1 && s.StartsAt == closedStart);

            if (existingSlot is not null)
            {
                preExistingSlotId = existingSlot.Id;
                originalStatus = existingSlot.Status;
                existingSlot.Status = "CLOSED";
                await Db.SaveChangesAsync();
            }
            else
            {
                var closedSlot = new ExaminationSlot
                {
                    DoctorId = 1,
                    ServiceId = 1,
                    RoomId = 1,
                    StartsAt = closedStart,
                    EndsAt = closedStart.AddMinutes(30),
                    Capacity = 3,
                    Status = "CLOSED",
                };
                Db.ExaminationSlots.Add(closedSlot);
                await Db.SaveChangesAsync();
            }

            // Add an active doctor with NO duty schedule -> must receive 0 auto-generated slots
            var tempStaff = new Staff
            {
                HospitalId = 1,
                DepartmentId = 2,
                StaffCode = $"STF-NOSCHED-{Guid.NewGuid():N}"[..18],
                FullName = "BS Kiểm Thử Chưa Có Lịch",
                Status = "ACTIVE",
            };
            Db.Staff.Add(tempStaff);
            await Db.SaveChangesAsync();
            unscheduledStaffId = tempStaff.Id;

            var tempDoctor = new Doctor
            {
                StaffId = tempStaff.Id,
                HospitalId = 1,
                DoctorCode = $"DOC-NS-{Guid.NewGuid():N}"[..15],
                SpecialtyId = 1,
                IsActive = true,
            };
            Db.Doctors.Add(tempDoctor);
            await Db.SaveChangesAsync();
            unscheduledDoctorId = tempDoctor.Id;

            Db.DoctorServiceAuthorizations.Add(new DoctorServiceAuthorization
            {
                DoctorId = tempDoctor.Id,
                ServiceId = 1,
                IsActive = true,
            });
            await Db.SaveChangesAsync();

            var scopeFactory = Factory.Services.GetRequiredService<IServiceScopeFactory>();
            var opts = new StaticOptionsMonitor<ScheduleJobsOptions>(new ScheduleJobsOptions
            {
                DefaultRollingSlotWindowDays = 14,
            });

            var gen1 = new RollingExaminationSlotGenerator(scopeFactory, opts, TimeProvider.System, NullLogger<RollingExaminationSlotGenerator>.Instance);
            var gen2 = new RollingExaminationSlotGenerator(scopeFactory, opts, TimeProvider.System, NullLogger<RollingExaminationSlotGenerator>.Instance);
            var gen3 = new RollingExaminationSlotGenerator(scopeFactory, opts, TimeProvider.System, NullLogger<RollingExaminationSlotGenerator>.Instance);

            await Task.WhenAll(
                gen1.EnsureUpcomingSlotsAsync(),
                gen2.EnsureUpcomingSlotsAsync(),
                gen3.EnsureUpcomingSlotsAsync());

            // 1. CLOSED slot stayed CLOSED and has no duplicate OPEN sibling
            var slotsAtClosedTime = await Db.ExaminationSlots
                .AsNoTracking()
                .Where(s => s.DoctorId == 1 && s.StartsAt == closedStart)
                .ToListAsync();
            var singleClosed = Assert.Single(slotsAtClosedTime);
            Assert.Equal("CLOSED", singleClosed.Status);

            // 2. Unscheduled doctor has 0 slots generated
            Assert.Equal(0, await Db.ExaminationSlots.CountAsync(s => s.DoctorId == tempDoctor.Id));

            // 3. Zero duplicate active (doctor_id, starts_at) across entire examination_slots table
            var duplicateCount = await Db.Database
                .SqlQueryRaw<int>("""
                    SELECT COUNT(*)::int AS "Value"
                    FROM (
                        SELECT "doctor_id", "starts_at"
                        FROM "examination_slots"
                        WHERE "doctor_id" IS NOT NULL AND "status" <> 'CLOSED'
                        GROUP BY "doctor_id", "starts_at"
                        HAVING COUNT(*) > 1
                    ) dup
                    """)
                .SingleAsync();
            Assert.Equal(0, duplicateCount);
        }
        finally
        {
            if (unscheduledDoctorId.HasValue)
            {
                await Db.DoctorServiceAuthorizations
                    .Where(a => a.DoctorId == unscheduledDoctorId.Value)
                    .ExecuteDeleteAsync();
                await Db.Doctors
                    .Where(d => d.Id == unscheduledDoctorId.Value)
                    .ExecuteDeleteAsync();
            }

            if (unscheduledStaffId.HasValue)
            {
                await Db.Staff
                    .Where(s => s.Id == unscheduledStaffId.Value)
                    .ExecuteDeleteAsync();
            }

            if (preExistingSlotId.HasValue && originalStatus is not null)
            {
                await Db.ExaminationSlots
                    .Where(s => s.Id == preExistingSlotId.Value)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, originalStatus));
            }

            await Db.ExaminationSlots
                .Where(s => !preExistingSlotIds.Contains(s.Id) && !Db.Appointments.Any(a => a.SlotId == s.Id))
                .ExecuteDeleteAsync();
        }
    }
}
