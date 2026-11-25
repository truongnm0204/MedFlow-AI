using MedFlow.Application.DTOs.Notifications;
using MedFlow.Application.DTOs.Policies;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Options;
using MedFlow.Application.Services.Notifications;
using MedFlow.Application.Services.Policies;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using MedFlow.Infrastructure.BackgroundJobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MedFlow.UnitTests.Notifications;

public sealed class ScheduleBackgroundJobsAndNotificationTests
{
    private sealed class MutableTimeProvider(DateTimeOffset initialUtc) : TimeProvider
    {
        private DateTimeOffset _utcNow = initialUtc;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan delta) => _utcNow = _utcNow.Add(delta);
    }

    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class RecordingEmailSender : IEmailSender
    {
        private readonly Func<string, string, string, CancellationToken, Task>? _handler;

        public List<(string To, string Subject, string HtmlBody)> Sent { get; } = [];

        public RecordingEmailSender(Func<string, string, string, CancellationToken, Task>? handler = null)
        {
            _handler = handler;
        }

        public async Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default)
        {
            if (_handler is not null)
            {
                await _handler(toEmail, subject, htmlBody, ct);
            }

            lock (Sent)
            {
                Sent.Add((toEmail, subject, htmlBody));
            }
        }
    }

    private sealed class StubAppointmentPolicyService(int graceMinutes = 15, int bookingWindowDays = 14)
        : IAppointmentPolicyService
    {
        public Task<AppointmentPolicyDto> GetEffectiveAsync(long hospitalId, CancellationToken ct = default)
            => Task.FromResult(new AppointmentPolicyDto(
                BookingWindowDays: bookingWindowDays,
                BookingLeadHours: 2,
                MaxActiveBookings: 3,
                RescheduleMinimumHours: 12,
                MaxReschedules: 2,
                CancelMinimumHours: 6,
                NoShowGraceMinutes: graceMinutes,
                NoShowMaxCount: 3,
                NoShowLookbackDays: 90,
                NoShowBlockDays: 30,
                LastUpdatedAt: null));

        public Task<AppointmentPolicyDto> GetForAdminAsync(long userId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<AppointmentPolicyDto> UpdateForAdminAsync(
            long userId,
            UpdateAppointmentPolicyRequest request,
            string? ipAddress,
            CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class MinimalUnconfiguredDbContext : IApplicationDbContext
    {
        public DbSet<User> Users => throw new NotSupportedException();
        public DbSet<Role> Roles => throw new NotSupportedException();
        public DbSet<Permission> Permissions => throw new NotSupportedException();
        public DbSet<UserRole> UserRoles => throw new NotSupportedException();
        public DbSet<RolePermission> RolePermissions => throw new NotSupportedException();
        public DbSet<Patient> Patients => throw new NotSupportedException();
        public DbSet<Staff> Staff => throw new NotSupportedException();
        public DbSet<Doctor> Doctors => throw new NotSupportedException();
        public DbSet<DoctorServiceAuthorization> DoctorServiceAuthorizations => throw new NotSupportedException();
        public DbSet<ExaminationSlot> ExaminationSlots => throw new NotSupportedException();
        public DbSet<AuthSession> AuthSessions => throw new NotSupportedException();
        public DbSet<AuthVerificationChallenge> AuthVerificationChallenges => throw new NotSupportedException();
        public DbSet<AuditLog> AuditLogs => throw new NotSupportedException();
        public DbSet<Hospital> Hospitals => throw new NotSupportedException();
        public DbSet<Department> Departments => throw new NotSupportedException();
        public DbSet<HospitalRoom> HospitalRooms => throw new NotSupportedException();
        public DbSet<Specialty> Specialties => throw new NotSupportedException();
        public DbSet<Service> Services => throw new NotSupportedException();
        public DbSet<ServicePrice> ServicePrices => throw new NotSupportedException();
        public DbSet<HospitalPolicy> HospitalPolicies => throw new NotSupportedException();
        public DbSet<Appointment> Appointments => throw new NotSupportedException();
        public DbSet<PatientFamilyLink> PatientFamilyLinks => throw new NotSupportedException();
        public DbSet<Visit> Visits => throw new NotSupportedException();
        public DbSet<QueueEntry> QueueEntries => throw new NotSupportedException();
        public DbSet<Payment> Payments => throw new NotSupportedException();

        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class JobsTestDbContext(DbContextOptions<JobsTestDbContext> options)
        : DbContext(options), IApplicationDbContext
    {
        public DbSet<User> Users => Set<User>();
        public DbSet<Role> Roles => Set<Role>();
        public DbSet<Permission> Permissions => Set<Permission>();
        public DbSet<UserRole> UserRoles => Set<UserRole>();
        public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
        public DbSet<Patient> Patients => Set<Patient>();
        public DbSet<Staff> Staff => Set<Staff>();
        public DbSet<Doctor> Doctors => Set<Doctor>();
        public DbSet<DoctorServiceAuthorization> DoctorServiceAuthorizations => Set<DoctorServiceAuthorization>();
        public DbSet<ExaminationSlot> ExaminationSlots => Set<ExaminationSlot>();
        public DbSet<AuthSession> AuthSessions => Set<AuthSession>();
        public DbSet<AuthVerificationChallenge> AuthVerificationChallenges => Set<AuthVerificationChallenge>();
        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
        public DbSet<Hospital> Hospitals => Set<Hospital>();
        public DbSet<Department> Departments => Set<Department>();
        public DbSet<HospitalRoom> HospitalRooms => Set<HospitalRoom>();
        public DbSet<Specialty> Specialties => Set<Specialty>();
        public DbSet<Service> Services => Set<Service>();
        public DbSet<ServicePrice> ServicePrices => Set<ServicePrice>();
        public DbSet<HospitalPolicy> HospitalPolicies => Set<HospitalPolicy>();
        public DbSet<Appointment> Appointments => Set<Appointment>();
        public DbSet<PatientFamilyLink> PatientFamilyLinks => Set<PatientFamilyLink>();
        public DbSet<Visit> Visits => Set<Visit>();
        public DbSet<QueueEntry> QueueEntries => Set<QueueEntry>();
        public DbSet<Payment> Payments => Set<Payment>();
        public DbSet<Notification> Notifications => Set<Notification>();

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default)
            => Database.BeginTransactionAsync(ct);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<UserRole>().HasKey(ur => new { ur.UserId, ur.RoleId });
            modelBuilder.Entity<RolePermission>().HasKey(rp => new { rp.RoleId, rp.PermissionId });
            modelBuilder.Entity<DoctorServiceAuthorization>().HasKey(a => new { a.DoctorId, a.ServiceId });
            modelBuilder.Entity<PatientFamilyLink>(b =>
            {
                b.HasKey(l => new { l.OwnerPatientId, l.MemberPatientId });
                b.HasOne(l => l.OwnerPatient).WithMany().HasForeignKey(l => l.OwnerPatientId);
                b.HasOne(l => l.MemberPatient).WithMany().HasForeignKey(l => l.MemberPatientId);
            });
            modelBuilder.Entity<Patient>(b =>
            {
                b.HasKey(p => p.Id);
                b.HasOne(p => p.User).WithOne(u => u.Patient).HasForeignKey<Patient>(p => p.UserId);
            });
            modelBuilder.Entity<AuditLog>(b =>
            {
                b.HasKey(a => a.Id);
                b.Ignore(a => a.IpAddress);
            });
            modelBuilder.Entity<Notification>().HasKey(n => n.Id);
        }
    }

    private static ServiceProvider BuildHarness(
        string dbName,
        MutableTimeProvider timeProvider,
        RecordingEmailSender emailSender,
        ScheduleJobsOptions? jobOptions = null,
        int graceMinutes = 15)
    {
        var services = new ServiceCollection();
        services.AddDbContext<JobsTestDbContext>(opts =>
            opts.UseInMemoryDatabase(dbName)
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));

        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<JobsTestDbContext>());
        services.AddSingleton<TimeProvider>(timeProvider);
        services.AddSingleton<IEmailSender>(emailSender);
        services.AddScoped<IAppointmentPolicyService>(_ => new StubAppointmentPolicyService(graceMinutes));
        services.AddSingleton<IOptionsMonitor<ScheduleJobsOptions>>(
            new StaticOptionsMonitor<ScheduleJobsOptions>(jobOptions ?? new ScheduleJobsOptions()));

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task DefaultInterfaceGetter_ThrowsNotSupportedException_AndIsNeverSwallowed()
    {
        IApplicationDbContext unconfigured = new MinimalUnconfiguredDbContext();

        Assert.Throws<NotSupportedException>(() => _ = unconfigured.Notifications);
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            AppointmentNotificationComposer.InvalidatePendingRemindersAsync(
                unconfigured,
                appointmentId: 1,
                cancellationReason: "Test",
                ct: CancellationToken.None));
    }

    [Fact]
    public async Task OutboxProcessor_ValidAndInvalidRecipients_MarksSentOrFailed_WithoutTouchingInvalidAsSent()
    {
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 11, 25, 1, 0, 0, TimeSpan.Zero)); // 08:00 VN
        var emailSender = new RecordingEmailSender();
        using var sp = BuildHarness(Guid.NewGuid().ToString("N"), clock, emailSender);

        var nowLocal = new DateTime(2026, 11, 25, 8, 0, 0, DateTimeKind.Unspecified);
        using (var seedScope = sp.CreateScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<JobsTestDbContext>();
            db.Notifications.AddRange(
                new Notification
                {
                    Id = 1,
                    PatientId = 1,
                    Channel = Notification.ChannelEmail,
                    Recipient = "patient01@medflow.test",
                    Subject = "Xác nhận lịch khám",
                    Body = "Chi tiết lịch khám",
                    Status = Notification.StatusPending,
                    ScheduledAt = nowLocal.AddMinutes(-1),
                    CreatedAt = nowLocal.AddMinutes(-1),
                },
                new Notification
                {
                    Id = 2,
                    PatientId = 2,
                    Channel = Notification.ChannelEmail,
                    Recipient = "not-an-email",
                    Subject = "Email sai định dạng",
                    Body = "Không được đánh dấu SENT",
                    Status = Notification.StatusPending,
                    ScheduledAt = nowLocal.AddMinutes(-1),
                    CreatedAt = nowLocal.AddMinutes(-1),
                });
            await db.SaveChangesAsync();
        }

        var processor = new NotificationOutboxProcessor(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<IOptionsMonitor<ScheduleJobsOptions>>(),
            clock,
            NullLogger<NotificationOutboxProcessor>.Instance);

        var summary = await processor.ProcessDueBatchAsync();

        Assert.Equal(2, summary.ClaimedCount);
        Assert.Equal(1, summary.SentCount);
        Assert.Equal(1, summary.FailedCount);
        Assert.Single(emailSender.Sent);

        using var verifyScope = sp.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<JobsTestDbContext>();
        var validRow = await verifyDb.Notifications.SingleAsync(n => n.Id == 1);
        var invalidRow = await verifyDb.Notifications.SingleAsync(n => n.Id == 2);

        Assert.Equal(Notification.StatusSent, validRow.Status);
        Assert.NotNull(validRow.SentAt);
        Assert.Null(validRow.ProcessingToken);
        Assert.Null(validRow.LeaseUntil);
        Assert.Equal(nowLocal.AddMinutes(-1), validRow.ScheduledAt);

        Assert.Equal(Notification.StatusFailed, invalidRow.Status);
        Assert.Null(invalidRow.SentAt);
        Assert.Contains("không hợp lệ", invalidRow.LastError);
    }

    [Fact]
    public async Task OutboxProcessor_RetryBackoffAndStaleProcessingTokenGuard_PreventsOverwrite()
    {
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 11, 25, 1, 0, 0, TimeSpan.Zero));
        var shouldFail = true;
        var emailSender = new RecordingEmailSender((_, _, _, _) =>
        {
            if (shouldFail)
            {
                throw new InvalidOperationException("SMTP temporary failure");
            }

            return Task.CompletedTask;
        });

        var opts = new ScheduleJobsOptions { MaxRetryCount = 3, ProcessingLeaseSeconds = 60, SmtpTimeoutSeconds = 10 };
        using var sp = BuildHarness(Guid.NewGuid().ToString("N"), clock, emailSender, opts);
        var nowLocal = new DateTime(2026, 11, 25, 8, 0, 0, DateTimeKind.Unspecified);

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<JobsTestDbContext>();
            db.Notifications.Add(new Notification
            {
                Id = 10,
                PatientId = 1,
                Channel = Notification.ChannelEmail,
                Recipient = "patient01@medflow.test",
                Subject = "Retry test",
                Body = "Retry body",
                Status = Notification.StatusPending,
                ScheduledAt = nowLocal,
                CreatedAt = nowLocal,
            });
            await db.SaveChangesAsync();
        }

        var processor = new NotificationOutboxProcessor(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<IOptionsMonitor<ScheduleJobsOptions>>(),
            clock,
            NullLogger<NotificationOutboxProcessor>.Instance);

        // Attempt 1 -> retry_count = 1 (+2m)
        var r1 = await processor.ProcessDueBatchAsync();
        Assert.Equal(1, r1.RetriedCount);

        clock.Advance(TimeSpan.FromMinutes(3));
        // Attempt 2 -> retry_count = 2 (+4m)
        var r2 = await processor.ProcessDueBatchAsync();
        Assert.Equal(1, r2.RetriedCount);

        clock.Advance(TimeSpan.FromMinutes(5));
        // Attempt 3 -> retry_count = 3 -> FAILED
        var r3 = await processor.ProcessDueBatchAsync();
        Assert.Equal(1, r3.FailedCount);

        using (var checkScope = sp.CreateScope())
        {
            var db = checkScope.ServiceProvider.GetRequiredService<JobsTestDbContext>();
            var failed = await db.Notifications.SingleAsync(n => n.Id == 10);
            Assert.Equal(Notification.StatusFailed, failed.Status);
            Assert.Equal(3, failed.RetryCount);
        }

        // Now verify stale processing_token guard when lease expires mid-flight:
        shouldFail = false;
        var hijackSp = sp;
        var hijackingSender = new RecordingEmailSender(async (_, _, _, ct) =>
        {
            using var innerScope = hijackSp.CreateScope();
            var innerDb = innerScope.ServiceProvider.GetRequiredService<JobsTestDbContext>();
            var row = await innerDb.Notifications.SingleAsync(n => n.Id == 11, ct);
            // Simulate Worker B having reclaimed and completed the row after Worker A's lease expired
            row.ProcessingToken = "newer-worker-token";
            row.Status = Notification.StatusSent;
            row.SentAt = nowLocal.AddMinutes(10);
            await innerDb.SaveChangesAsync(ct);
        });

        using var sp2 = BuildHarness(Guid.NewGuid().ToString("N"), clock, hijackingSender, opts);
        hijackSp = sp2;
        using (var s2 = sp2.CreateScope())
        {
            var db2 = s2.ServiceProvider.GetRequiredService<JobsTestDbContext>();
            db2.Notifications.Add(new Notification
            {
                Id = 11,
                PatientId = 1,
                Channel = Notification.ChannelEmail,
                Recipient = "patient01@medflow.test",
                Subject = "Stale token test",
                Body = "Body",
                Status = Notification.StatusPending,
                ScheduledAt = nowLocal,
                CreatedAt = nowLocal,
            });
            await db2.SaveChangesAsync();
        }

        var processor2 = new NotificationOutboxProcessor(
            sp2.GetRequiredService<IServiceScopeFactory>(),
            sp2.GetRequiredService<IOptionsMonitor<ScheduleJobsOptions>>(),
            clock,
            NullLogger<NotificationOutboxProcessor>.Instance);

        var staleSummary = await processor2.ProcessDueBatchAsync();
        Assert.Equal(1, staleSummary.StaleTokenSkippedCount);
    }

    [Fact]
    public async Task LifecycleProcessor_SyncsNoShowWithReservedQueue_PreservesQueueNumber_AndSkipsCheckedInVisit()
    {
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 11, 25, 3, 0, 0, TimeSpan.Zero)); // 10:00 VN
        var emailSender = new RecordingEmailSender();
        using var sp = BuildHarness(Guid.NewGuid().ToString("N"), clock, emailSender, graceMinutes: 15);

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<JobsTestDbContext>();
            db.Hospitals.Add(new Hospital { Id = 1, HospitalCode = "HOS-01", Name = "MedFlow", IsActive = true });
            db.Services.Add(new Service { Id = 1, HospitalId = 1, ServiceCode = "EX-01", Name = "Khám Nội", ServiceType = "EXAMINATION", IsActive = true });
            db.Users.Add(new User { Id = 1, Email = "patient01@medflow.test", AccountStatus = AccountStatus.ACTIVE });
            db.Patients.Add(new Patient { Id = 1, UserId = 1, PatientCode = "PAT-0001", FullName = "Nguyễn Văn A", Email = "patient01@medflow.test", Status = "ACTIVE" });

            // Slot 1: ended at 09:30 (+15m grace = 09:45 < 10:00) -> overdue!
            db.ExaminationSlots.Add(new ExaminationSlot
            {
                Id = 100,
                ServiceId = 1,
                StartsAt = new DateTime(2026, 11, 25, 9, 0, 0),
                EndsAt = new DateTime(2026, 11, 25, 9, 30, 0),
                Capacity = 3,
                Status = "OPEN",
            });
            // Slot 2: starts tomorrow 09:00 (within 24h reminder window from 10:00 today!)
            db.ExaminationSlots.Add(new ExaminationSlot
            {
                Id = 101,
                ServiceId = 1,
                StartsAt = new DateTime(2026, 11, 26, 9, 0, 0),
                EndsAt = new DateTime(2026, 11, 26, 9, 30, 0),
                Capacity = 3,
                Status = "OPEN",
            });

            // Appt 1: Overdue, no Visit, has RESERVED QueueEntry #5 -> MUST become NO_SHOW + Queue CANCELLED keeping #5
            db.Appointments.Add(new Appointment
            {
                Id = 500,
                AppointmentCode = "APT-202611-0500",
                PatientId = 1,
                SlotId = 100,
                Status = "BOOKED",
                BookedByUserId = 1,
            });
            db.QueueEntries.Add(new QueueEntry
            {
                Id = 900,
                AppointmentId = 500,
                QueueDate = new DateOnly(2026, 11, 25),
                QueueNumber = 5,
                QueueType = "EXAMINATION",
                Status = "RESERVED",
                PresenceStatus = "NOT_ARRIVED",
                StateVersion = 1,
            });

            // Appt 2: Same overdue slot, but ALREADY has an active Visit -> MUST NOT become NO_SHOW!
            db.Appointments.Add(new Appointment
            {
                Id = 501,
                AppointmentCode = "APT-202611-0501",
                PatientId = 1,
                SlotId = 100,
                Status = "BOOKED",
                BookedByUserId = 1,
            });
            db.Visits.Add(new Visit
            {
                Id = 700,
                AppointmentId = 501,
                PatientId = 1,
                Status = "WAITING",
            });

            // Appt 3: Upcoming within 24h -> reminder queued idempotently
            db.Appointments.Add(new Appointment
            {
                Id = 502,
                AppointmentCode = "APT-202611-0502",
                PatientId = 1,
                SlotId = 101,
                Status = "BOOKED",
                BookedByUserId = 1,
            });

            await db.SaveChangesAsync();
        }

        var lifecycle = new AppointmentLifecycleProcessor(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<IOptionsMonitor<ScheduleJobsOptions>>(),
            clock,
            NullLogger<AppointmentLifecycleProcessor>.Instance);

        var firstRun = await lifecycle.ProcessCycleAsync();
        var secondRun = await lifecycle.ProcessCycleAsync();

        Assert.Equal(1, firstRun.RemindersQueuedCount);
        Assert.Equal(1, firstRun.AutoNoShowCount);
        Assert.Equal(1, firstRun.CancelledReservedQueueCount);

        // Idempotent second cycle creates 0 duplicates
        Assert.Equal(0, secondRun.RemindersQueuedCount);
        Assert.Equal(0, secondRun.AutoNoShowCount);

        using var verifyScope = sp.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<JobsTestDbContext>();

        var overdueAppt = await verifyDb.Appointments.SingleAsync(a => a.Id == 500);
        var attendedAppt = await verifyDb.Appointments.SingleAsync(a => a.Id == 501);
        var queueRow = await verifyDb.QueueEntries.SingleAsync(q => q.Id == 900);

        Assert.Equal("NO_SHOW", overdueAppt.Status);
        Assert.Equal("BOOKED", attendedAppt.Status);

        Assert.Equal("CANCELLED", queueRow.Status);
        Assert.Equal("NOT_ARRIVED", queueRow.PresenceStatus);
        Assert.Equal(5, queueRow.QueueNumber); // Queue number strictly preserved!
        Assert.Equal(2, queueRow.StateVersion);

        Assert.Single(verifyDb.AuditLogs.Where(l => l.Action == AppointmentLifecycleProcessor.AutoNoShowAuditAction && l.EntityId == "500"));
        Assert.Single(verifyDb.AuditLogs.Where(l => l.Action == AppointmentNotificationComposer.QueueCancelledAuditAction && l.EntityId == "900"));
        Assert.Single(verifyDb.Notifications.Where(n => n.AppointmentId == 502 && n.NotificationType == Notification.TypeAppointmentReminder));
        Assert.Single(verifyDb.Notifications.Where(n => n.AppointmentId == 500 && n.NotificationType == Notification.TypeAppointmentNoShow));
    }

    [Fact]
    public async Task RollingSlotGenerator_RespectsDutySchedule_NeverInventsForUnscheduledDoctor_AndNeverReopensClosedSlots()
    {
        // Wednesday 2026-11-25 08:00 VN
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 11, 25, 1, 0, 0, TimeSpan.Zero));
        var opts = new ScheduleJobsOptions { DefaultRollingSlotWindowDays = 7 };
        using var sp = BuildHarness(Guid.NewGuid().ToString("N"), clock, new RecordingEmailSender(), opts);

        var nextMonday = new DateOnly(2026, 11, 30);

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<JobsTestDbContext>();
            db.Hospitals.Add(new Hospital { Id = 1, HospitalCode = "HOS-01", Name = "MedFlow", IsActive = true });
            db.Departments.Add(new Department { Id = 2, HospitalId = 1, DepartmentCode = "GI", Name = "Khoa Tiêu hóa", IsActive = true });
            db.HospitalRooms.Add(new HospitalRoom { Id = 1, HospitalId = 1, DepartmentId = 2, RoomCode = "RM-101", Name = "Phòng 101", RoomType = "EXAMINATION", Status = "ACTIVE" });
            db.Services.AddRange(
                new Service { Id = 1, HospitalId = 1, DepartmentId = 2, ServiceCode = "EX-GI-01", Name = "Khám Tiêu hóa lần đầu", ServiceType = "EXAMINATION", AllowsOnlineBooking = true, IsActive = true },
                new Service { Id = 3, HospitalId = 1, DepartmentId = 2, ServiceCode = "EX-GI-02", Name = "Khám Tiêu hóa tái khám", ServiceType = "EXAMINATION", AllowsOnlineBooking = true, IsActive = true });

            db.Staff.AddRange(
                new Staff { Id = 1, HospitalId = 1, DepartmentId = 2, StaffCode = "STF-1", FullName = "BS Có Lịch", Status = "ACTIVE" },
                new Staff { Id = 2, HospitalId = 1, DepartmentId = 2, StaffCode = "STF-2", FullName = "BS Chưa Xếp Lịch", Status = "ACTIVE" });

            db.Doctors.AddRange(
                new Doctor { Id = 1, StaffId = 1, HospitalId = 1, DoctorCode = "DOC-001", IsActive = true },
                new Doctor { Id = 2, StaffId = 2, HospitalId = 1, DoctorCode = "DOC-002", IsActive = true });

            // Doctor 1 is authorized for BOTH service 1 & 3; Doctor 2 is also authorized but has NO schedule template!
            db.DoctorServiceAuthorizations.AddRange(
                new DoctorServiceAuthorization { DoctorId = 1, ServiceId = 1, IsActive = true },
                new DoctorServiceAuthorization { DoctorId = 1, ServiceId = 3, IsActive = true },
                new DoctorServiceAuthorization { DoctorId = 2, ServiceId = 1, IsActive = true });

            // Reference duty slot on previous Monday 2026-11-23 08:00-08:30 for Doctor 1 (Service 1, Room 1)
            db.ExaminationSlots.Add(new ExaminationSlot
            {
                Id = 1,
                DoctorId = 1,
                ServiceId = 1,
                RoomId = 1,
                StartsAt = new DateTime(2026, 11, 23, 8, 0, 0),
                EndsAt = new DateTime(2026, 11, 23, 8, 30, 0),
                Capacity = 3,
                Status = "OPEN",
            });
            // Reference duty slot on previous Monday 2026-11-23 08:30-09:00, which Admin CLOSED for next Monday 2026-11-30!
            db.ExaminationSlots.Add(new ExaminationSlot
            {
                Id = 2,
                DoctorId = 1,
                ServiceId = 1,
                RoomId = 1,
                StartsAt = new DateTime(2026, 11, 23, 8, 30, 0),
                EndsAt = new DateTime(2026, 11, 23, 9, 0, 0),
                Capacity = 3,
                Status = "OPEN",
            });
            db.ExaminationSlots.Add(new ExaminationSlot
            {
                Id = 3,
                DoctorId = 1,
                ServiceId = 1,
                RoomId = 1,
                StartsAt = nextMonday.ToDateTime(new TimeOnly(8, 30)),
                EndsAt = nextMonday.ToDateTime(new TimeOnly(9, 0)),
                Capacity = 3,
                Status = "CLOSED",
            });

            await db.SaveChangesAsync();
        }

        var generator = new RollingExaminationSlotGenerator(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<IOptionsMonitor<ScheduleJobsOptions>>(),
            clock,
            NullLogger<RollingExaminationSlotGenerator>.Instance);

        var generated = await generator.EnsureUpcomingSlotsAsync();
        var secondPass = await generator.EnsureUpcomingSlotsAsync();

        Assert.Equal(1, generated); // Only Monday 2026-11-30 08:00-08:30 is generated!
        Assert.Equal(0, secondPass);

        using var verifyScope = sp.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<JobsTestDbContext>();

        // 1. Unscheduled Doctor 2 has ZERO slots invented
        Assert.Equal(0, await verifyDb.ExaminationSlots.CountAsync(s => s.DoctorId == 2));

        // 2. Closed slot on next Monday 08:30 stayed CLOSED and was NOT duplicated
        var closedSlots = await verifyDb.ExaminationSlots
            .Where(s => s.StartsAt == nextMonday.ToDateTime(new TimeOnly(8, 30)))
            .ToListAsync();
        var singleClosed = Assert.Single(closedSlots);
        Assert.Equal("CLOSED", singleClosed.Status);

        // 3. Generated slot on next Monday 08:00 was created ONCE (not duplicated across Service 1 & Service 3)
        var monday8Slots = await verifyDb.ExaminationSlots
            .Where(s => s.StartsAt == nextMonday.ToDateTime(new TimeOnly(8, 0)))
            .ToListAsync();
        Assert.Single(monday8Slots);
    }

    [Fact]
    public async Task NotificationService_EnforcesFamilyOwnership_AndKeepsBannerIndependentOfReadAt()
    {
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 11, 25, 1, 0, 0, TimeSpan.Zero)); // 08:00 VN
        using var sp = BuildHarness(Guid.NewGuid().ToString("N"), clock, new RecordingEmailSender());
        var nowLocal = new DateTime(2026, 11, 25, 8, 0, 0);

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<JobsTestDbContext>();
            db.Hospitals.Add(new Hospital { Id = 1, HospitalCode = "HOS-1", Name = "MedFlow", IsActive = true });
            db.Specialties.Add(new Specialty { Id = 1, SpecialtyCode = "GI", Name = "Tiêu hóa", IsActive = true });
            db.Services.Add(new Service { Id = 1, HospitalId = 1, SpecialtyId = 1, ServiceCode = "EX-GI-01", Name = "Khám Tiêu hóa", ServiceType = "EXAMINATION", IsActive = true });

            db.Users.AddRange(
                new User { Id = 1, Email = "p1@medflow.test", AccountStatus = AccountStatus.ACTIVE },
                new User { Id = 2, Email = "p2@medflow.test", AccountStatus = AccountStatus.ACTIVE });

            db.Patients.AddRange(
                new Patient { Id = 1, UserId = 1, PatientCode = "PAT-1", FullName = "Chủ hộ 1", Status = "ACTIVE" },
                new Patient { Id = 2, UserId = 2, PatientCode = "PAT-2", FullName = "Người bệnh 2", Status = "ACTIVE" },
                new Patient { Id = 3, UserId = null, PatientCode = "PAT-3", FullName = "Con Chủ hộ 1", Status = "ACTIVE" });

            db.PatientFamilyLinks.Add(new PatientFamilyLink
            {
                OwnerPatientId = 1,
                MemberPatientId = 3,
                Relationship = "CHILD",
                VerificationStatus = "VERIFIED",
                VerificationMethod = "OTP",
                ValidUntil = nowLocal.AddDays(30),
            });

            db.ExaminationSlots.Add(new ExaminationSlot
            {
                Id = 50,
                ServiceId = 1,
                StartsAt = nowLocal.AddHours(3),
                EndsAt = nowLocal.AddHours(3.5),
                Capacity = 3,
                Status = "OPEN",
            });

            db.Appointments.Add(new Appointment
            {
                Id = 80,
                AppointmentCode = "APT-202611-0080",
                PatientId = 3,
                SlotId = 50,
                Status = "BOOKED",
                BookedByUserId = 1,
                BookedAt = nowLocal,
            });

            db.Notifications.AddRange(
                new Notification
                {
                    Id = 1001,
                    PatientId = 3,
                    AppointmentId = 80,
                    Subject = "Nhắc lịch khám cho bé",
                    Body = "Lịch khám lúc 11:00 hôm nay",
                    NotificationType = Notification.TypeAppointmentReminder,
                    Status = Notification.StatusSent,
                    CreatedAt = nowLocal,
                },
                new Notification
                {
                    Id = 1002,
                    PatientId = 2,
                    Subject = "Thông báo riêng của người bệnh 2",
                    Body = "Mật",
                    NotificationType = Notification.TypeAppointmentBooked,
                    Status = Notification.StatusSent,
                    CreatedAt = nowLocal,
                });

            await db.SaveChangesAsync();
        }

        using var actScope = sp.CreateScope();
        var service = new NotificationService(
            actScope.ServiceProvider.GetRequiredService<IApplicationDbContext>(),
            clock);

        // User 1 sees family member (PAT-3) notification and activeReminder, NEVER sees Patient 2's notification
        var feedBeforeRead = await service.GetMyNotificationsAsync(1, new GetNotificationsRequest());
        Assert.Equal(1, feedBeforeRead.UnreadCount);
        Assert.Single(feedBeforeRead.Items);
        Assert.Equal("1001", feedBeforeRead.Items[0].Id);
        Assert.NotNull(feedBeforeRead.ActiveReminder);
        Assert.Equal("APT-202611-0080", feedBeforeRead.ActiveReminder!.AppointmentCode);

        // User 1 attempting to mark User 2's notification as read -> 403 Forbidden
        await Assert.ThrowsAsync<ForbiddenException>(() => service.MarkAsReadAsync(1, 1002));

        // User 1 marks all notifications read -> unreadCount becomes 0, BUT ActiveReminder STAYS visible!
        var markAllRes = await service.MarkAllAsReadAsync(1);
        Assert.Equal(1, markAllRes.MarkedCount);
        Assert.Equal(0, markAllRes.UnreadCount);

        var feedAfterRead = await service.GetMyNotificationsAsync(1, new GetNotificationsRequest());
        Assert.Equal(0, feedAfterRead.UnreadCount);
        Assert.True(feedAfterRead.Items[0].IsRead);
        Assert.NotNull(feedAfterRead.ActiveReminder);
        Assert.Equal("APT-202611-0080", feedAfterRead.ActiveReminder!.AppointmentCode);
    }
}
