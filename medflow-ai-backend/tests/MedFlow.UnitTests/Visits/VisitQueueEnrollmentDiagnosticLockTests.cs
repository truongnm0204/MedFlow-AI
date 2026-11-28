using MedFlow.Application.Common.Time;
using MedFlow.Application.DTOs.Visits;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Payments;
using MedFlow.Application.Services.Visits;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.UnitTests.Visits;

/// <summary>
/// Issue #863 (S1) — D2 / BR-BF02-06: bệnh nhân không được tự lấy số cận lâm sàng, nhân sự lấy số
/// CLS không còn chọn điểm/thứ tự, và thanh toán tự chọn chỉ định CLS kế tiếp.
/// </summary>
public sealed class VisitQueueEnrollmentDiagnosticLockTests : IDisposable
{
    private const long HospitalId = 1;
    private const long DepartmentId = 10;
    private const long DiagnosticRoomId = 101;
    private const long DiagnosticServiceId = 501;
    private const long DiagnosticServicePointId = 900;
    private const long PatientId = 100;
    private const long PatientUserId = 10;
    private const long StaffUserId = 20;
    private const long VisitId = 1000;
    private const long DiagnosticOrderId = 400;

    private static readonly DateTimeOffset FixedNowUtc = new(2026, 11, 29, 2, 0, 0, TimeSpan.Zero); // 09:00 Vietnam

    private readonly EnrollmentTestDbContext _db;
    private readonly VisitQueueEnrollmentService _service;

    public VisitQueueEnrollmentDiagnosticLockTests()
    {
        var options = new DbContextOptionsBuilder<EnrollmentTestDbContext>()
            .UseInMemoryDatabase($"EnrollmentDiagnosticLock_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _db = new EnrollmentTestDbContext(options);
        var timeProvider = new FixedTimeProvider(FixedNowUtc);
        Seed(_db, timeProvider);
        _service = new VisitQueueEnrollmentService(_db, new StubReturnCoordinationService(), timeProvider);
    }

    public void Dispose()
    {
        _db.Database.EnsureDeleted();
        _db.Dispose();
    }

    [Fact]
    public async Task EnrollDiagnostic_PatientOwnsVisit_ReturnsForbiddenAndCreatesNoQueueEntry()
    {
        var request = new CreateVisitQueueEnrollmentRequestDto(
            Target: QueueEnrollmentTargets.Diagnostic,
            DiagnosticOrderId: DiagnosticOrderId,
            ServicePointId: DiagnosticServicePointId);

        var ex = await Assert.ThrowsAsync<ForbiddenException>(
            () => _service.EnrollAsync(PatientUserId, VisitId.ToString(), request));

        Assert.Contains("không được phép lấy số hàng đợi cận lâm sàng", ex.Message, StringComparison.Ordinal);
        Assert.Empty(_db.QueueEntries);
    }

    [Theory]
    [InlineData(DiagnosticServicePointId)]
    [InlineData(999_999L)]
    [InlineData(0L)]
    [InlineData(-5L)]
    public async Task EnrollDiagnostic_StaffRequestWithClientServicePointId_IgnoresItAndUsesResolvedPoint(long clientServicePointId)
    {
        var request = new CreateVisitQueueEnrollmentRequestDto(
            Target: QueueEnrollmentTargets.Diagnostic,
            DiagnosticOrderId: DiagnosticOrderId,
            ServicePointId: clientServicePointId);

        var ticket = await _service.EnrollAsync(StaffUserId, VisitId.ToString(), request);

        Assert.Equal(DiagnosticServicePointId, ticket.ServicePointId);
        Assert.Equal(QueueEnrollmentTargets.Diagnostic, ticket.QueueType);
        var entry = Assert.Single(_db.QueueEntries);
        Assert.Equal(DiagnosticServicePointId, entry.ServicePointId);
        Assert.Equal(DiagnosticServicePointId, entry.CurrentServicePointId);
        Assert.Equal(DiagnosticOrderId, entry.DiagnosticOrderId);
    }

    [Fact]
    public void EvaluateDiagnosticItemBlock_PatientActor_AlwaysReadOnly()
    {
        var visit = new Visit { Id = VisitId, Status = "CHECKED_IN" };

        var result = VisitQueueEnrollmentService.EvaluateDiagnosticItemBlock(
            visit,
            orderStatus: "ORDERED",
            selectionStatus: "ACCEPTED",
            isExpired: false,
            isSettled: true,
            hasOperationalCompatiblePoint: true,
            clinicalHandoffCompleted: true,
            activePatientQueue: null,
            isPatientActor: true,
            isPresentToday: true);

        Assert.False(result.CanEnroll);
        Assert.Equal(VisitQueueEnrollmentService.PatientDiagnosticEnrollmentDisabledCode, result.Code);
        Assert.Equal(VisitQueueEnrollmentService.PatientDiagnosticEnrollmentDisabledMessage, result.Message);
    }

    [Fact]
    public void EvaluateDiagnosticItemBlock_StaffActor_KeepsExistingEligibilityBehavior()
    {
        var visit = new Visit { Id = VisitId, Status = "CHECKED_IN" };

        var result = VisitQueueEnrollmentService.EvaluateDiagnosticItemBlock(
            visit,
            orderStatus: "ORDERED",
            selectionStatus: "ACCEPTED",
            isExpired: false,
            isSettled: true,
            hasOperationalCompatiblePoint: true,
            clinicalHandoffCompleted: true,
            activePatientQueue: null,
            isPatientActor: false,
            isPresentToday: true);

        Assert.True(result.CanEnroll);
        Assert.Null(result.Code);
    }

    [Fact]
    public void EvaluatePharmacyItemBlock_PatientActor_RemainsSelfService()
    {
        var visit = new Visit { Id = VisitId, Status = "CHECKED_IN" };
        var prescription = new Prescription
        {
            Id = 1,
            VisitId = VisitId,
            FulfillmentChoice = "HOSPITAL",
            Status = "ISSUED",
        };

        var result = VisitQueueEnrollmentService.EvaluatePharmacyItemBlock(
            visit,
            prescription,
            rxStatus: "ISSUED",
            isSuperseded: false,
            hasCompletedDispense: false,
            isPaymentCleared: true,
            hasActivePharmacyPoint: true,
            clinicalHandoffCompleted: true,
            activePatientQueue: null,
            isPatientActor: true,
            isPresentToday: true);

        Assert.True(result.CanEnroll);
        Assert.Null(result.Code);
    }

    [Fact]
    public async Task ResolveNextAutoEnrollOrder_PicksEarliestCreatedEligibleOrderAmongPaidCharges()
    {
        AddOrder(600, orderStatus: "ORDERED", selectionStatus: "ACCEPTED");
        AddOrder(598, orderStatus: "ORDERED", selectionStatus: "ACCEPTED");
        AddOrder(599, orderStatus: "CANCELLED", selectionStatus: "ACCEPTED");
        AddOrder(597, orderStatus: "ORDERED", selectionStatus: "DECLINED");
        _db.SaveChanges();

        var paidCharges = new[]
        {
            ChargeFor(600),
            ChargeFor(598),
            ChargeFor(599),
            ChargeFor(597),
        };

        var picked = await PaymentEnrollmentIntentProcessor.ResolveNextAutoEnrollOrderAsync(
            _db, VisitId, paidCharges, CancellationToken.None);

        Assert.NotNull(picked);
        Assert.Equal(598, picked!.Id);
    }

    [Fact]
    public async Task ResolveNextAutoEnrollOrder_SkipsOrdersOutsideThisPaymentAndAlreadyEnrolled()
    {
        AddOrder(700, orderStatus: "ORDERED", selectionStatus: "ACCEPTED");
        AddOrder(701, orderStatus: "ORDERED", selectionStatus: "ACCEPTED", queueEntryId: 55);
        AddOrder(702, orderStatus: "ORDERED", selectionStatus: "ACCEPTED");
        _db.SaveChanges();

        var picked = await PaymentEnrollmentIntentProcessor.ResolveNextAutoEnrollOrderAsync(
            _db, VisitId, [ChargeFor(700), ChargeFor(701)], CancellationToken.None);

        Assert.NotNull(picked);
        Assert.Equal(700, picked!.Id);
    }

    [Fact]
    public async Task ResolveNextAutoEnrollOrder_NoDiagnosticCharge_ReturnsNull()
    {
        var examinationCharge = new VisitCharge
        {
            Id = 9_000,
            VisitId = VisitId,
            ServiceId = 77,
            DiagnosticOrderId = null,
            UnitPrice = 150_000m,
            PaymentRequirement = "REQUIRED",
            ChargeType = "EXAMINATION",
            Status = "ACTIVE",
        };
        _db.VisitCharges.Add(examinationCharge);
        _db.SaveChanges();

        var picked = await PaymentEnrollmentIntentProcessor.ResolveNextAutoEnrollOrderAsync(
            _db, VisitId, [examinationCharge], CancellationToken.None);

        Assert.Null(picked);
    }

    private static void Seed(EnrollmentTestDbContext db, TimeProvider timeProvider)
    {
        var nowLocal = HospitalClock.GetNow(timeProvider);
        var todayLocal = DateOnly.FromDateTime(nowLocal);

        db.Hospitals.Add(new Hospital
        {
            Id = HospitalId,
            HospitalCode = "HOS-863",
            Name = "Bệnh viện MedFlow #863",
            IsActive = true,
        });

        db.Departments.Add(new Department
        {
            Id = DepartmentId,
            HospitalId = HospitalId,
            DepartmentCode = "CLS",
            Name = "Khoa Cận lâm sàng",
            IsActive = true,
        });

        db.HospitalRooms.Add(new HospitalRoom
        {
            Id = DiagnosticRoomId,
            HospitalId = HospitalId,
            DepartmentId = DepartmentId,
            RoomCode = "CLS-101",
            Name = "Phòng CLS 101",
            RoomType = "DIAGNOSTIC",
            Status = "ACTIVE",
        });

        db.Services.Add(new Service
        {
            Id = DiagnosticServiceId,
            HospitalId = HospitalId,
            DepartmentId = DepartmentId,
            ServiceCode = "XN-MAU",
            Name = "Xét nghiệm máu",
            ServiceType = "DIAGNOSTIC",
            IsActive = true,
        });

        db.QueueServicePoints.Add(new QueueServicePoint
        {
            Id = DiagnosticServicePointId,
            HospitalId = HospitalId,
            RoomId = DiagnosticRoomId,
            ServiceId = DiagnosticServiceId,
            PointType = "DIAGNOSTIC",
            Name = "Điểm CLS 101",
            IsActive = true,
        });

        db.Permissions.Add(new Permission { Id = 1, Code = "QUEUE_ENROLL" });
        db.Roles.AddRange(
            new Role { Id = 1, Code = "PATIENT", Name = "Bệnh nhân" },
            new Role { Id = 2, Code = "RECEPTION", Name = "Tiếp đón" });
        db.RolePermissions.Add(new RolePermission { RoleId = 2, PermissionId = 1 });

        db.Users.AddRange(
            new User { Id = PatientUserId, Email = "patient863@test.local", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = StaffUserId, Email = "reception863@test.local", AccountStatus = AccountStatus.ACTIVE });
        db.UserRoles.AddRange(
            new UserRole { UserId = PatientUserId, RoleId = 1 },
            new UserRole { UserId = StaffUserId, RoleId = 2 });

        db.Patients.Add(new Patient
        {
            Id = PatientId,
            UserId = PatientUserId,
            PatientCode = "P-863",
            FullName = "Bệnh nhân #863",
            Status = "ACTIVE",
        });

        db.Staff.Add(new Staff
        {
            Id = 300,
            UserId = StaffUserId,
            HospitalId = HospitalId,
            FullName = "Lễ tân #863",
            Status = "ACTIVE",
        });

        db.Visits.Add(new Visit
        {
            Id = VisitId,
            VisitCode = "VIS-863",
            PatientId = PatientId,
            ExaminationRoomId = DiagnosticRoomId,
            Status = "CHECKED_IN",
            CheckedInAt = nowLocal,
            LastPresentDate = todayLocal,
            PresenceState = "PRESENT",
            PresenceSessionNo = 1,
        });

        db.DiagnosticOrders.Add(new DiagnosticOrder
        {
            Id = DiagnosticOrderId,
            OrderCode = "DIA-863",
            VisitId = VisitId,
            ServiceId = DiagnosticServiceId,
            Status = "ORDERED",
            SelectionStatus = "ACCEPTED",
            RequiredForConclusion = true,
            OrderedAt = nowLocal,
        });

        // Phí CLS 0đ (WAIVED) -> settlement reader mở khóa dịch vụ mà không cần payment/allocation.
        db.VisitCharges.Add(new VisitCharge
        {
            Id = 700,
            VisitId = VisitId,
            ServiceId = DiagnosticServiceId,
            DiagnosticOrderId = DiagnosticOrderId,
            UnitPrice = 0m,
            PaymentRequirement = "WAIVED",
            ChargeType = "DIAGNOSTIC",
            Status = "ACTIVE",
        });

        db.SaveChanges();
    }

    private void AddOrder(
        long id,
        string orderStatus,
        string selectionStatus,
        long? queueEntryId = null)
    {
        _db.DiagnosticOrders.Add(new DiagnosticOrder
        {
            Id = id,
            OrderCode = $"DIA-{id}",
            VisitId = VisitId,
            ServiceId = DiagnosticServiceId,
            Status = orderStatus,
            SelectionStatus = selectionStatus,
            RequiredForConclusion = true,
            QueueEntryId = queueEntryId,
        });
    }

    private VisitCharge ChargeFor(long orderId)
        => new()
        {
            Id = 8_000 + orderId,
            VisitId = VisitId,
            ServiceId = DiagnosticServiceId,
            DiagnosticOrderId = orderId,
            UnitPrice = 0m,
            PaymentRequirement = "WAIVED",
            ChargeType = "DIAGNOSTIC",
            Status = "ACTIVE",
        };

    private sealed class FixedTimeProvider(DateTimeOffset nowUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => nowUtc;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class StubReturnCoordinationService : IReturnCoordinationService
    {
        public Task<ReturnCoordinationContextDto> GetReturnCoordinationContextAsync(
            long actorUserId,
            string visitIdOrCode,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<ReturnEnrollmentResponseDto> EnrollReturnConsultationAsync(
            long actorUserId,
            string visitIdOrCode,
            CreateReturnEnrollmentRequestDto? request = null,
            string? ipAddress = null,
            CancellationToken ct = default,
            string? legacyAuditAction = null,
            string? legacyAuditReason = null,
            bool legacyReadinessOverride = false,
            long? legacySourceQueueEntryId = null)
            => throw new NotSupportedException();
    }

    private sealed class EnrollmentTestDbContext : DbContext, IApplicationDbContext
    {
        public EnrollmentTestDbContext(DbContextOptions<EnrollmentTestDbContext> options)
            : base(options)
        {
        }

        private static NotSupportedException NotUsed() =>
            new("Not needed for VisitQueueEnrollmentService diagnostic-lock unit tests.");

        public DbSet<User> Users => Set<User>();
        public DbSet<Role> Roles => Set<Role>();
        public DbSet<Permission> Permissions => Set<Permission>();
        public DbSet<UserRole> UserRoles => Set<UserRole>();
        public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
        public DbSet<Patient> Patients => Set<Patient>();
        public DbSet<Staff> Staff => Set<Staff>();
        public DbSet<Doctor> Doctors => Set<Doctor>();
        public DbSet<DoctorServiceAuthorization> DoctorServiceAuthorizations => throw NotUsed();
        public DbSet<ExaminationSlot> ExaminationSlots => Set<ExaminationSlot>();
        public DbSet<AuthSession> AuthSessions => throw NotUsed();
        public DbSet<AuthVerificationChallenge> AuthVerificationChallenges => throw NotUsed();
        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
        public DbSet<Hospital> Hospitals => Set<Hospital>();
        public DbSet<Department> Departments => Set<Department>();
        public DbSet<HospitalRoom> HospitalRooms => Set<HospitalRoom>();
        public DbSet<Specialty> Specialties => throw NotUsed();
        public DbSet<Service> Services => Set<Service>();
        public DbSet<ServicePrice> ServicePrices => throw NotUsed();
        public DbSet<HospitalPolicy> HospitalPolicies => throw NotUsed();
        public DbSet<Appointment> Appointments => Set<Appointment>();
        public DbSet<PatientFamilyLink> PatientFamilyLinks => throw NotUsed();
        public DbSet<Visit> Visits => Set<Visit>();
        public DbSet<VisitCharge> VisitCharges => Set<VisitCharge>();
        public DbSet<QueueEntry> QueueEntries => Set<QueueEntry>();
        public DbSet<Payment> Payments => Set<Payment>();
        public DbSet<QueueServicePoint> QueueServicePoints => Set<QueueServicePoint>();
        public DbSet<DiagnosticOrder> DiagnosticOrders => Set<DiagnosticOrder>();
        public DbSet<DiagnosticResult> DiagnosticResults => Set<DiagnosticResult>();
        public DbSet<DiagnosticResultVersion> DiagnosticResultVersions => Set<DiagnosticResultVersion>();
        public DbSet<Prescription> Prescriptions => throw NotUsed();
        public DbSet<MedicationDispense> MedicationDispenses => throw NotUsed();

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default)
            => Database.BeginTransactionAsync(ct);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Ignore<DoctorServiceAuthorization>();
            modelBuilder.Ignore<AuthSession>();
            modelBuilder.Ignore<AuthVerificationChallenge>();
            modelBuilder.Ignore<Specialty>();
            modelBuilder.Ignore<ServicePrice>();
            modelBuilder.Ignore<HospitalPolicy>();
            modelBuilder.Ignore<PatientFamilyLink>();
            modelBuilder.Ignore<Prescription>();
            modelBuilder.Ignore<MedicationDispense>();

            modelBuilder.Entity<User>().HasKey(u => u.Id);
            modelBuilder.Entity<User>().Ignore(u => u.AuthSessions);
            modelBuilder.Entity<User>().Ignore(u => u.UserRoles);
            modelBuilder.Entity<User>().Ignore(u => u.Staff);
            modelBuilder.Entity<Role>().HasKey(r => r.Id);
            modelBuilder.Entity<Role>().Ignore(r => r.UserRoles);
            modelBuilder.Entity<Role>().Ignore(r => r.RolePermissions);
            modelBuilder.Entity<Permission>().HasKey(p => p.Id);
            modelBuilder.Entity<Permission>().Ignore(p => p.RolePermissions);
            modelBuilder.Entity<UserRole>().HasKey(ur => new { ur.UserId, ur.RoleId });
            modelBuilder.Entity<UserRole>().Ignore(ur => ur.User);
            modelBuilder.Entity<UserRole>().Ignore(ur => ur.Role);
            modelBuilder.Entity<RolePermission>().HasKey(rp => new { rp.RoleId, rp.PermissionId });
            modelBuilder.Entity<RolePermission>().Ignore(rp => rp.Role);
            modelBuilder.Entity<RolePermission>().Ignore(rp => rp.Permission);
            modelBuilder.Entity<Patient>().HasKey(p => p.Id);
            modelBuilder.Entity<Patient>().HasOne(p => p.User).WithOne(u => u.Patient).HasForeignKey<Patient>(p => p.UserId);
            modelBuilder.Entity<Staff>().HasKey(s => s.Id);
            modelBuilder.Entity<Staff>().Ignore(s => s.User);
            modelBuilder.Entity<Staff>().Ignore(s => s.Department);
            modelBuilder.Entity<Staff>().Ignore(s => s.Doctor);
            modelBuilder.Entity<Doctor>().HasKey(d => d.Id);
            modelBuilder.Entity<Doctor>().Ignore(d => d.Staff);
            modelBuilder.Entity<Doctor>().Ignore(d => d.Specialty);
            modelBuilder.Entity<Doctor>().Ignore(d => d.ServiceAuthorizations);
            modelBuilder.Entity<Doctor>().Ignore(d => d.ExaminationSlots);
            modelBuilder.Entity<Hospital>().HasKey(h => h.Id);
            modelBuilder.Entity<Department>().HasKey(d => d.Id);
            modelBuilder.Entity<Department>().Ignore(d => d.Services);
            modelBuilder.Entity<Department>().Ignore(d => d.HospitalRooms);
            modelBuilder.Entity<HospitalRoom>().HasKey(r => r.Id);
            modelBuilder.Entity<HospitalRoom>().Ignore(r => r.Department);
            modelBuilder.Entity<Service>().HasKey(s => s.Id);
            modelBuilder.Entity<Service>().Ignore(s => s.Department);
            modelBuilder.Entity<Service>().Ignore(s => s.Specialty);
            modelBuilder.Entity<Service>().Ignore(s => s.ServicePrices);
            modelBuilder.Entity<Visit>().HasKey(v => v.Id);
            modelBuilder.Entity<VisitCharge>().HasKey(vc => vc.Id);
            modelBuilder.Entity<QueueEntry>().HasKey(q => q.Id);
            modelBuilder.Entity<QueueServicePoint>().HasKey(sp => sp.Id);
            modelBuilder.Entity<DiagnosticOrder>().HasKey(o => o.Id);
            modelBuilder.Entity<DiagnosticResult>().HasKey(r => r.Id);
            modelBuilder.Entity<DiagnosticResultVersion>().HasKey(v => v.Id);
            modelBuilder.Entity<Payment>().HasKey(p => p.Id);
            modelBuilder.Entity<AuditLog>(b =>
            {
                b.HasKey(a => a.Id);
                b.Ignore(a => a.IpAddress);
                b.Ignore(a => a.ActorUser);
            });
        }
    }
}
