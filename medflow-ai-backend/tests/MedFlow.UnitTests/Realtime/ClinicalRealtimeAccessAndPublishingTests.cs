using MedFlow.Application.DTOs.Payments;
using MedFlow.Application.DTOs.Realtime;
using MedFlow.Application.DTOs.SystemAdmin;
using MedFlow.Application.DTOs.Visits;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Patients;
using MedFlow.Application.Services.Payments;
using MedFlow.Application.Services.Realtime;
using MedFlow.Application.Services.SystemAdmin;
using MedFlow.Application.Services.Visits;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.UnitTests.Realtime;

public sealed class ClinicalRealtimeAccessAndPublishingTests : IDisposable
{
    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class RecordingRealtimePublisher(bool shouldThrow = false) : IRealtimeNotificationPublisher
    {
        public List<QueueChangedNotification> QueueEvents { get; } = [];
        public List<VisitPaymentChangedNotification> PaymentEvents { get; } = [];

        public Task PublishQueueChangedAsync(QueueChangedNotification notification, CancellationToken ct = default)
        {
            if (shouldThrow)
                throw new InvalidOperationException("Simulated SignalR broker outage.");
            QueueEvents.Add(notification);
            return Task.CompletedTask;
        }

        public Task PublishVisitPaymentChangedAsync(VisitPaymentChangedNotification notification, CancellationToken ct = default)
        {
            if (shouldThrow)
                throw new InvalidOperationException("Simulated SignalR broker outage.");
            PaymentEvents.Add(notification);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingAccessRevoker(bool shouldThrow = false) : IRealtimeAccessRevoker
    {
        public List<(long OwnerUserId, long MemberPatientId)> FamilyRevocations { get; } = [];
        public List<long> AppointmentRevocations { get; } = [];
        public List<(long AppointmentId, long? SpId, long? RoomId)> BucketMigrations { get; } = [];
        public List<(long UserId, string Reason)> UserRevocations { get; } = [];

        public Task RevokeFamilyMemberAccessAsync(long ownerUserId, long memberPatientId, CancellationToken ct = default)
        {
            if (shouldThrow) throw new InvalidOperationException("Revoker failure");
            FamilyRevocations.Add((ownerUserId, memberPatientId));
            return Task.CompletedTask;
        }

        public Task RevokeAppointmentTrackingAsync(long appointmentId, CancellationToken ct = default)
        {
            if (shouldThrow) throw new InvalidOperationException("Revoker failure");
            AppointmentRevocations.Add(appointmentId);
            return Task.CompletedTask;
        }

        public Task MigrateAppointmentBucketAsync(long appointmentId, long? newServicePointId, long? newRoomId, CancellationToken ct = default)
        {
            if (shouldThrow) throw new InvalidOperationException("Revoker failure");
            BucketMigrations.Add((appointmentId, newServicePointId, newRoomId));
            return Task.CompletedTask;
        }

        public Task RevokeUserConnectionsAsync(long userId, string reasonCode = "ACCOUNT_REVOKED", CancellationToken ct = default)
        {
            if (shouldThrow) throw new InvalidOperationException("Revoker failure");
            UserRevocations.Add((userId, reasonCode));
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryGatewayEventStore : IPaymentGatewayEventStore
    {
        public List<PaymentGatewayEvent> Events { get; } = [];

        public Task<bool> ExistsAsync(string provider, string externalId, CancellationToken ct = default) =>
            Task.FromResult(Events.Any(e => e.Provider == provider && e.ExternalId == externalId));

        public Task<decimal> SumReceivedAmountAsync(string paymentCode, CancellationToken ct = default) =>
            Task.FromResult(Events
                .Where(e => e.PaymentCode == paymentCode && e.Outcome is "PAID" or "PARTIAL" or "OVERPAID")
                .Sum(e => e.TransferAmount ?? 0m));

        public void Add(PaymentGatewayEvent entity) => Events.Add(entity);
    }

    private sealed class RealtimeTestDbContext(DbContextOptions<RealtimeTestDbContext> options)
        : DbContext(options), IApplicationDbContext
    {
        public DbSet<User> Users => Set<User>();
        public DbSet<Role> Roles => Set<Role>();
        public DbSet<Permission> Permissions => Set<Permission>();
        public DbSet<UserRole> UserRoles => Set<UserRole>();
        public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
        public DbSet<Patient> Patients => Set<Patient>();
        public DbSet<PatientFamilyLink> PatientFamilyLinks => Set<PatientFamilyLink>();
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
        public DbSet<Visit> Visits => Set<Visit>();
        public DbSet<QueueEntry> QueueEntries => Set<QueueEntry>();
        public DbSet<QueueServicePoint> QueueServicePoints => Set<QueueServicePoint>();
        public DbSet<QueueFairnessState> QueueFairnessStates => Set<QueueFairnessState>();
        public DbSet<Payment> Payments => Set<Payment>();
        public DbSet<Notification> Notifications => Set<Notification>();

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default) =>
            Database.BeginTransactionAsync(ct);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<AuthVerificationChallenge>().HasKey(c => c.Id);
            modelBuilder.Ignore<Medicine>();
            modelBuilder.Ignore<MedicineBatch>();
            modelBuilder.Ignore<Prescription>();
            modelBuilder.Ignore<PrescriptionItem>();
            modelBuilder.Ignore<MedicationDispense>();
            modelBuilder.Ignore<DispenseItem>();
            modelBuilder.Ignore<StockTransaction>();

            modelBuilder.Entity<User>().HasKey(u => u.Id);
            modelBuilder.Entity<Role>().HasKey(r => r.Id);
            modelBuilder.Entity<Permission>().HasKey(p => p.Id);
            modelBuilder.Entity<UserRole>().HasKey(ur => new { ur.UserId, ur.RoleId });
            modelBuilder.Entity<RolePermission>().HasKey(rp => new { rp.RoleId, rp.PermissionId });
            modelBuilder.Entity<AuthSession>().HasKey(s => s.Id);
            modelBuilder.Entity<AuditLog>(b =>
            {
                b.HasKey(a => a.Id);
                b.Ignore(a => a.IpAddress);
            });
            modelBuilder.Entity<Hospital>().HasKey(h => h.Id);
            modelBuilder.Entity<Department>().HasKey(d => d.Id);
            modelBuilder.Entity<Specialty>().HasKey(s => s.Id);
            modelBuilder.Entity<HospitalPolicy>().HasKey(p => p.Id);
            modelBuilder.Entity<QueueServicePoint>().HasKey(sp => sp.Id);
            modelBuilder.Entity<QueueFairnessState>().HasKey(f => new { f.ServicePointId, f.QueueDate });
            modelBuilder.Entity<Notification>().HasKey(n => n.Id);
            modelBuilder.Entity<Payment>().HasKey(p => p.Id);
            modelBuilder.Entity<DoctorServiceAuthorization>().HasKey(a => new { a.DoctorId, a.ServiceId });

            modelBuilder.Entity<Patient>(b =>
            {
                b.HasKey(p => p.Id);
                b.HasOne(p => p.User).WithOne(u => u.Patient).HasForeignKey<Patient>(p => p.UserId);
            });
            modelBuilder.Entity<PatientFamilyLink>(b =>
            {
                b.HasKey(l => new { l.OwnerPatientId, l.MemberPatientId });
                b.HasOne(l => l.OwnerPatient).WithMany().HasForeignKey(l => l.OwnerPatientId);
                b.HasOne(l => l.MemberPatient).WithMany().HasForeignKey(l => l.MemberPatientId);
            });
            modelBuilder.Entity<HospitalRoom>(b =>
            {
                b.HasKey(r => r.Id);
                b.HasOne(r => r.Department).WithMany(d => d.HospitalRooms).HasForeignKey(r => r.DepartmentId);
            });
            modelBuilder.Entity<Staff>(b =>
            {
                b.HasKey(s => s.Id);
                b.HasOne(s => s.User).WithOne(u => u.Staff).HasForeignKey<Staff>(s => s.UserId);
                b.HasOne(s => s.Department).WithMany().HasForeignKey(s => s.DepartmentId);
            });
            modelBuilder.Entity<Doctor>(b =>
            {
                b.HasKey(d => d.Id);
                b.HasOne(d => d.Staff).WithOne(s => s.Doctor).HasForeignKey<Doctor>(d => d.StaffId);
                b.HasOne(d => d.Specialty).WithMany().HasForeignKey(d => d.SpecialtyId);
            });
            modelBuilder.Entity<Service>(b =>
            {
                b.HasKey(s => s.Id);
                b.HasOne(s => s.Department).WithMany(d => d.Services).HasForeignKey(s => s.DepartmentId);
                b.HasOne(s => s.Specialty).WithMany(sp => sp.Services).HasForeignKey(s => s.SpecialtyId);
                b.HasMany(s => s.ServicePrices).WithOne(p => p.Service).HasForeignKey(p => p.ServiceId);
            });
            modelBuilder.Entity<ServicePrice>().HasKey(p => p.Id);
            modelBuilder.Entity<ExaminationSlot>(b =>
            {
                b.HasKey(s => s.Id);
                b.HasOne(s => s.Doctor).WithMany(d => d.ExaminationSlots).HasForeignKey(s => s.DoctorId);
                b.HasOne(s => s.Service).WithMany().HasForeignKey(s => s.ServiceId);
                b.HasOne(s => s.Room).WithMany().HasForeignKey(s => s.RoomId);
            });
            modelBuilder.Entity<Appointment>(b =>
            {
                b.HasKey(a => a.Id);
                b.HasOne(a => a.Slot).WithMany(s => s.Appointments).HasForeignKey(a => a.SlotId);
                b.HasOne(a => a.Patient).WithMany().HasForeignKey(a => a.PatientId);
            });
            modelBuilder.Entity<Visit>().HasKey(v => v.Id);
            modelBuilder.Entity<QueueEntry>().HasKey(q => q.Id);
        }
    }

    private readonly RealtimeTestDbContext _db;
    private readonly FixedTimeProvider _time = new(new DateTimeOffset(2026, 11, 25, 2, 0, 0, TimeSpan.Zero));
    private readonly ClinicalRealtimeAccessService _accessService;

    public ClinicalRealtimeAccessAndPublishingTests()
    {
        var options = new DbContextOptionsBuilder<RealtimeTestDbContext>()
            .UseInMemoryDatabase($"realtime-unit-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _db = new RealtimeTestDbContext(options);
        SeedBaseTopology();
        _accessService = new ClinicalRealtimeAccessService(_db, _time);
    }

    public void Dispose() => _db.Dispose();

    private void SeedBaseTopology()
    {
        _db.Hospitals.AddRange(
            new Hospital { Id = 1, HospitalCode = "HOSP-01", Name = "BV Cơ sở 1", IsActive = true },
            new Hospital { Id = 2, HospitalCode = "HOSP-02", Name = "BV Cơ sở 2", IsActive = true });

        _db.Departments.AddRange(
            new Department { Id = 1, HospitalId = 1, DepartmentCode = "NOI", Name = "Khoa Nội", IsActive = true },
            new Department { Id = 2, HospitalId = 2, DepartmentCode = "NGOAI", Name = "Khoa Ngoại", IsActive = true });

        _db.HospitalRooms.AddRange(
            new HospitalRoom { Id = 101, HospitalId = 1, DepartmentId = 1, RoomCode = "P101", Name = "Phòng 101", Status = "ACTIVE" },
            new HospitalRoom { Id = 201, HospitalId = 2, DepartmentId = 2, RoomCode = "P201", Name = "Phòng 201", Status = "ACTIVE" });

        _db.QueueServicePoints.AddRange(
            new QueueServicePoint { Id = 11, HospitalId = 1, RoomId = 101, PointType = "DOCTOR", Name = "Quầy khám P101", IsActive = true },
            new QueueServicePoint { Id = 22, HospitalId = 2, RoomId = 201, PointType = "DOCTOR", Name = "Quầy khám P201", IsActive = true });

        _db.Roles.AddRange(
            new Role { Id = 1, Code = RoleNames.Patient, Name = "Bệnh nhân" },
            new Role { Id = 2, Code = RoleNames.Reception, Name = "Tiếp đón" },
            new Role { Id = 3, Code = RoleNames.HospitalAdmin, Name = "Quản trị BV" },
            new Role { Id = 4, Code = RoleNames.SystemAdmin, Name = "Quản trị HT" });

        _db.Permissions.AddRange(
            new Permission { Id = 1, Code = PermissionCodes.PaymentView },
            new Permission { Id = 2, Code = PermissionCodes.QueueManage },
            new Permission { Id = 3, Code = PermissionCodes.UserManage });

        // Role 2 (RECEPTION) has PaymentView + QueueManage; Role 3 (HOSPITAL_ADMIN) intentionally has NO PaymentView here to test unpermitted admin!
        _db.RolePermissions.AddRange(
            new RolePermission { RoleId = 2, PermissionId = 1 },
            new RolePermission { RoleId = 2, PermissionId = 2 },
            new RolePermission { RoleId = 4, PermissionId = 3 });

        // Users & Patients
        _db.Users.AddRange(
            new User { Id = 100, Email = "owner@test.vn", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 101, Email = "stranger@test.vn", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 200, Email = "reception.h1@test.vn", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 201, Email = "reception.h2@test.vn", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 202, Email = "admin.noperm@test.vn", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 300, Email = "locked@test.vn", AccountStatus = AccountStatus.ACTIVE, LockedUntil = _time.GetUtcNow().UtcDateTime.AddMinutes(10) },
            new User { Id = 400, Email = "sysadmin@test.vn", AccountStatus = AccountStatus.ACTIVE });

        _db.UserRoles.AddRange(
            new UserRole { UserId = 100, RoleId = 1 },
            new UserRole { UserId = 101, RoleId = 1 },
            new UserRole { UserId = 200, RoleId = 2 },
            new UserRole { UserId = 201, RoleId = 2 },
            new UserRole { UserId = 202, RoleId = 3 },
            new UserRole { UserId = 400, RoleId = 4 });

        _db.Patients.AddRange(
            new Patient { Id = 1, UserId = 100, PatientCode = "PAT-0001", FullName = "Chính Chủ", Status = "ACTIVE" },
            new Patient { Id = 2, UserId = null, PatientCode = "PAT-0002", FullName = "Người Thân OTP", Status = "ACTIVE" },
            new Patient { Id = 3, UserId = null, PatientCode = "PAT-0003", FullName = "Người Thân Legacy", Status = "ACTIVE" },
            new Patient { Id = 4, UserId = 101, PatientCode = "PAT-0004", FullName = "Bệnh Nhân Khác", Status = "ACTIVE" });

        var nowLocal = _time.GetUtcNow().UtcDateTime.AddHours(7);
        _db.PatientFamilyLinks.AddRange(
            new PatientFamilyLink
            {
                OwnerPatientId = 1,
                MemberPatientId = 2,
                Relationship = "CHILD",
                VerificationStatus = "VERIFIED",
                VerificationMethod = "OTP",
            },
            new PatientFamilyLink
            {
                OwnerPatientId = 1,
                MemberPatientId = 3,
                Relationship = "PARENT",
                VerificationStatus = "VERIFIED",
                VerificationMethod = "LEGACY",
                LegacyAccessUntil = nowLocal.AddDays(15),
            });

        _db.Staff.AddRange(
            new Staff { Id = 10, UserId = 200, HospitalId = 1, DepartmentId = 1, StaffCode = "STF-01", FullName = "NV Tiếp đón H1", Status = "ACTIVE" },
            new Staff { Id = 11, UserId = 201, HospitalId = 2, DepartmentId = 2, StaffCode = "STF-02", FullName = "NV Tiếp đón H2", Status = "ACTIVE" },
            new Staff { Id = 12, UserId = 202, HospitalId = 1, DepartmentId = 1, StaffCode = "STF-03", FullName = "Admin Không Quyền Phí", Status = "ACTIVE" },
            new Staff { Id = 13, UserId = 400, HospitalId = 1, DepartmentId = 1, StaffCode = "STF-SYS", FullName = "SysAdmin", Status = "ACTIVE" });

        _db.Services.Add(new Service { Id = 1, HospitalId = 1, DepartmentId = 1, ServiceCode = "KHAM-NOI", Name = "Khám Nội", IsActive = true });
        _db.ExaminationSlots.Add(new ExaminationSlot
        {
            Id = 50,
            ServiceId = 1,
            RoomId = 101,
            StartsAt = new DateTime(2026, 11, 25, 9, 0, 0),
            EndsAt = new DateTime(2026, 11, 25, 10, 0, 0),
            Capacity = 10,
            Status = "OPEN",
        });

        _db.Appointments.AddRange(
            new Appointment { Id = 500, AppointmentCode = "APT-500", PatientId = 1, SlotId = 50, Status = "CONFIRMED" },
            new Appointment { Id = 501, AppointmentCode = "APT-501", PatientId = 1, SlotId = 50, Status = "CANCELLED" },
            new Appointment { Id = 502, AppointmentCode = "APT-502", PatientId = 1, SlotId = 50, Status = "NO_SHOW" });

        _db.Visits.AddRange(
            new Visit { Id = 1001, VisitCode = "VIS-1001", PatientId = 1, AppointmentId = 500, ExaminationRoomId = 101, Status = "WAITING", CheckedInAt = nowLocal },
            new Visit { Id = 1002, VisitCode = "VIS-1002", PatientId = 2, ExaminationRoomId = 101, Status = "WAITING", CheckedInAt = nowLocal },
            new Visit { Id = 1003, VisitCode = "VIS-1003", PatientId = 3, ExaminationRoomId = 101, Status = "WAITING", CheckedInAt = nowLocal },
            new Visit { Id = 1004, VisitCode = "VIS-1004", PatientId = 4, ExaminationRoomId = 101, Status = "WAITING", CheckedInAt = nowLocal });

        _db.QueueEntries.Add(new QueueEntry
        {
            Id = 700,
            VisitId = 1001,
            AppointmentId = 500,
            ServicePointId = 11,
            CurrentServicePointId = 11,
            QueueDate = new DateOnly(2026, 11, 25),
            QueueNumber = 5,
            QueueType = "EXAMINATION",
            Status = "WAITING",
            PresenceStatus = "PRESENT",
            StateVersion = 1,
        });

        _db.SaveChanges();
    }

    [Fact]
    public async Task AuthorizeVisitPaymentJoin_OwnerAndOtpVerifiedFamily_Allowed_NormalizesVisitGroup()
    {
        var byId = await _accessService.AuthorizeVisitPaymentJoinAsync(100, "1001");
        var byCode = await _accessService.AuthorizeVisitPaymentJoinAsync(100, "vis-1001");
        var familyOtp = await _accessService.AuthorizeVisitPaymentJoinAsync(100, "VIS-1002");

        Assert.Equal(["visit:1001"], byId.AllGroupNames);
        Assert.Equal(["visit:1001"], byCode.AllGroupNames);
        Assert.Equal(byId.ResourceKey, byCode.ResourceKey);
        Assert.Equal(["visit:1002"], familyOtp.AllGroupNames);
    }

    [Fact]
    public async Task AuthorizeVisitPaymentJoin_LegacyFamily_CrossPatient_CrossHospital_AndUnpermittedAdmin_Rejected()
    {
        // LEGACY family link denied for PHI/payment scope (ADR-0045)
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _accessService.AuthorizeVisitPaymentJoinAsync(100, "VIS-1003"));

        // Cross-patient denied
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _accessService.AuthorizeVisitPaymentJoinAsync(100, "VIS-1004"));

        // Reception in Hospital 1 allowed, Reception in Hospital 2 denied
        var h1Staff = await _accessService.AuthorizeVisitPaymentJoinAsync(200, "1001");
        Assert.Equal(["visit:1001"], h1Staff.AllGroupNames);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _accessService.AuthorizeVisitPaymentJoinAsync(201, "1001"));

        // HospitalAdmin without PAYMENT_VIEW / PATIENT_CHECKIN / HOSPITAL_CONFIG denied
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _accessService.AuthorizeVisitPaymentJoinAsync(202, "1001"));
    }

    [Fact]
    public async Task AuthorizeAppointmentQueueJoin_DerivesServerBucket_AndRejectsCancelledOrNoShow()
    {
        var active = await _accessService.AuthorizeAppointmentQueueJoinAsync(100, "APT-500");

        Assert.Equal(["appointment:500"], active.PrimaryGroupNames);
        Assert.Contains("patient-bucket:sp:11", active.BucketGroupNames);
        Assert.Contains("patient-bucket:room:101", active.BucketGroupNames);

        // Cancelled & NO_SHOW appointments cannot join queue tracking / buckets
        await Assert.ThrowsAsync<ConflictException>(() =>
            _accessService.AuthorizeAppointmentQueueJoinAsync(100, "APT-501"));
        await Assert.ThrowsAsync<ConflictException>(() =>
            _accessService.AuthorizeAppointmentQueueJoinAsync(100, "APT-502"));

        // Stranger patient denied
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _accessService.AuthorizeAppointmentQueueJoinAsync(101, "APT-500"));
    }

    [Fact]
    public async Task AuthorizeHospitalAndServicePointGroups_EnforcesRolePermissionHospital_AndBlocksLockedUsers()
    {
        // Patient cannot join hospital or service-point staff groups
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _accessService.AuthorizeHospitalQueueJoinAsync(100));
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _accessService.AuthorizeServicePointJoinAsync(100, "sp-11"));

        // Hospital 1 staff joins Hospital 1 queue & sp-11, blocked from Hospital 2 sp-22
        var h1Queue = await _accessService.AuthorizeHospitalQueueJoinAsync(200);
        Assert.Equal(["hospital:1:queue"], h1Queue.AllGroupNames);

        var h1Sp = await _accessService.AuthorizeServicePointJoinAsync(200, "sp-11");
        Assert.Contains("hospital:1:sp:11", h1Sp.AllGroupNames);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _accessService.AuthorizeServicePointJoinAsync(200, "sp-22"));

        // Locked user rejected at connection/join check
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _accessService.EnsureConnectedUserActiveAsync(300));
    }

    [Fact]
    public async Task SePayWebhook_PublishesOnlyAfterPaidCommit_IgnoresPartialAndDuplicate_AndIsolatesPublisherErrors()
    {
        _db.Payments.Add(new Payment
        {
            Id = 90,
            VisitId = 1001,
            PaymentCode = "MF90",
            Amount = 200_000m,
            Status = PaymentStatuses.Pending,
        });
        await _db.SaveChangesAsync();

        var store = new InMemoryGatewayEventStore();
        var publisher = new RecordingRealtimePublisher();
        var service = new SePayWebhookService(_db, store, _time, publisher);

        // 1. Partial transfer -> no VisitPaymentChanged emitted
        var partial = await service.HandleAsync(new SePayWebhookRequest
        {
            Id = 1,
            TransferType = "in",
            TransferAmount = 100_000m,
            Content = "Thanh toan MF90",
            Code = "MF90",
            TransactionDate = "2026-11-25 09:10:00",
            ReferenceCode = "REF-1",
        });
        Assert.Equal(PaymentGatewayOutcomes.Partial, partial.Outcome);
        Assert.Empty(publisher.PaymentEvents);

        // 2. Completing transfer -> PAID committed + exactly 1 VisitPaymentChanged(1001)
        var paid = await service.HandleAsync(new SePayWebhookRequest
        {
            Id = 2,
            TransferType = "in",
            TransferAmount = 100_000m,
            Content = "Thanh toan MF90",
            Code = "MF90",
            TransactionDate = "2026-11-25 09:11:00",
            ReferenceCode = "REF-2",
        });
        Assert.Equal(PaymentGatewayOutcomes.Paid, paid.Outcome);
        Assert.Single(publisher.PaymentEvents);
        Assert.Equal(1001L, publisher.PaymentEvents[0].VisitId);

        // 3. Duplicate webhook -> IGNORED + still only 1 VisitPaymentChanged
        var duplicate = await service.HandleAsync(new SePayWebhookRequest
        {
            Id = 2,
            TransferType = "in",
            TransferAmount = 100_000m,
            Content = "Thanh toan MF90",
            Code = "MF90",
            TransactionDate = "2026-11-25 09:11:00",
            ReferenceCode = "REF-2",
        });
        Assert.Equal(PaymentGatewayOutcomes.Ignored, duplicate.Outcome);
        Assert.Single(publisher.PaymentEvents);

        // 4. Publisher failure isolation -> DB still commits PAID without throwing
        _db.Payments.Add(new Payment
        {
            Id = 91,
            VisitId = 1002,
            PaymentCode = "MF91",
            Amount = 150_000m,
            Status = PaymentStatuses.Pending,
        });
        await _db.SaveChangesAsync();

        var throwingService = new SePayWebhookService(
            _db,
            store,
            _time,
            new RecordingRealtimePublisher(shouldThrow: true));

        var paidDespiteBrokerDown = await throwingService.HandleAsync(new SePayWebhookRequest
        {
            Id = 3,
            TransferType = "in",
            TransferAmount = 150_000m,
            Content = "MF91",
            Code = "MF91",
            TransactionDate = "2026-11-25 09:12:00",
            ReferenceCode = "REF-3",
        });

        Assert.Equal(PaymentGatewayOutcomes.Paid, paidDespiteBrokerDown.Outcome);
        Assert.Equal(PaymentStatuses.Paid, (await _db.Payments.FindAsync(91L))!.Status);
    }

    [Fact]
    public async Task QueueCancel_FamilyRemoval_AndStaffDeactivate_TriggerRealtimeRevocationsSafely()
    {
        var publisher = new RecordingRealtimePublisher();
        var revoker = new RecordingAccessRevoker();

        // 1. Cancel queue entry -> publishes QueueChanged AND revokes appointment tracking
        var queueStatusService = new QueueStatusService(_db, _time, publisher, revoker);
        await queueStatusService.CancelQueueEntryAsync(
            actorUserId: 200,
            idOrCode: "700",
            request: new CancelQueueEntryRequest(Reason: "Bệnh nhân xin hủy lượt khám", Notes: null, ExpectedStateVersion: 1));

        Assert.Single(publisher.QueueEvents);
        Assert.Equal(500L, publisher.QueueEvents[0].AppointmentId);
        Assert.Contains(500L, revoker.AppointmentRevocations);

        // 2. Remove family member -> revokes family member subscriptions on owner's open connections
        var familyService = new FamilyProfileService(_db, null, _time, revoker);
        await familyService.RemoveFamilyMemberAsync(100, "PAT-0002", "Thu hồi quyền xem");
        Assert.Contains((100L, 2L), revoker.FamilyRevocations);

        // 3. Deactivate staff account -> revokes all connections of deactivated staff user
        var staffAdminService = new StaffAccountAdminService(_db, _time, revoker);
        await staffAdminService.DeactivateAsync(
            actorUserId: 400,
            staffId: 11,
            request: new DeactivateStaffAccountRequest(Reason: "Nhân sự nghỉ việc tại cơ sở 2"));
        Assert.Contains(revoker.UserRevocations, r => r.UserId == 201L && r.Reason == "STAFF_DEACTIVATED");
    }
}
