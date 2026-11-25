using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Payments;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.UnitTests.Payments;

public sealed class VisitChargeSettlementReaderTests : IDisposable
{
    private static readonly DateTimeOffset FixedNow = new(2026, 11, 25, 3, 0, 0, TimeSpan.Zero);
    private readonly ReaderTestDb _db;
    private readonly VisitChargeSettlementReader _reader;

    public VisitChargeSettlementReaderTests()
    {
        var options = new DbContextOptionsBuilder<ReaderTestDb>()
            .UseInMemoryDatabase($"SettlementReader_{Guid.NewGuid()}")
            .Options;
        _db = new ReaderTestDb(options);
        _reader = new VisitChargeSettlementReader(_db, new FixedTime(FixedNow));
    }

    public void Dispose()
    {
        _db.Database.EnsureDeleted();
        _db.Dispose();
    }

    [Fact]
    public async Task CheckExaminationEligibilityAsync_WaivedOrZeroPrice_IsUnlocked_UnpricedOrInvalid_IsLocked()
    {
        // Waived -> Unlocked
        _db.Visits.Add(new Visit { Id = 1, Status = "CHECKED_IN" });
        _db.VisitCharges.Add(VisitCharge.CreateExamination(1, 10, 200_000m, FixedNow.DateTime, waived: true));

        // Zero price -> Unlocked
        _db.Visits.Add(new Visit { Id = 2, Status = "CHECKED_IN" });
        _db.VisitCharges.Add(VisitCharge.CreateExamination(2, 10, 0m, FixedNow.DateTime));

        // Unpriced -> Locked (never free!)
        _db.Visits.Add(new Visit { Id = 3, Status = "CHECKED_IN" });
        _db.VisitCharges.Add(new VisitCharge
        {
            Id = 30,
            VisitId = 3,
            ServiceId = 10,
            ChargeType = VisitChargeTypes.Examination,
            UnitPrice = null,
            PaymentRequirement = "REQUIRED",
            Status = VisitChargeStatuses.Active,
        });

        // Invalid -> Locked
        _db.Visits.Add(new Visit { Id = 4, Status = "CHECKED_IN" });
        _db.VisitCharges.Add(new VisitCharge
        {
            Id = 40,
            VisitId = 4,
            ServiceId = 10,
            ChargeType = VisitChargeTypes.Examination,
            UnitPrice = -100m,
            PaymentRequirement = "REQUIRED",
            Status = VisitChargeStatuses.Active,
        });

        await _db.SaveChangesAsync();

        var waivedGate = await _reader.CheckExaminationEligibilityAsync(1);
        Assert.True(waivedGate.IsUnlocked);
        Assert.Equal(VisitChargeSettlementReader.StatusWaived, waivedGate.StatusCode);

        var zeroGate = await _reader.CheckExaminationEligibilityAsync(2);
        Assert.True(zeroGate.IsUnlocked);
        Assert.Equal(VisitChargeSettlementReader.StatusWaived, zeroGate.StatusCode);

        var unpricedGate = await _reader.CheckExaminationEligibilityAsync(3);
        Assert.False(unpricedGate.IsUnlocked);
        Assert.Equal(VisitChargeSettlementReader.StatusUnpriced, unpricedGate.StatusCode);

        var invalidGate = await _reader.CheckExaminationEligibilityAsync(4);
        Assert.False(invalidGate.IsUnlocked);
        Assert.Equal(VisitChargeSettlementReader.StatusInvalidCharge, invalidGate.StatusCode);
    }

    [Fact]
    public async Task CheckDiagnosticOrderEligibilityAsync_PerChargeIsolation_ExamPaidDoesNotUnlockDiagnostic()
    {
        _db.Visits.Add(new Visit { Id = 10, Status = "IN_PROGRESS" });
        var examCharge = VisitCharge.CreateExamination(10, 1, 200_000m, FixedNow.DateTime);
        examCharge.Id = 101;
        var examPayment = Payment.CreateForCharge(10, 101, 200_000m, FixedNow.DateTime, PaymentStatuses.Paid);
        examPayment.Id = 1001;
        examPayment.PaymentCode = "MF1001";

        var diagOrder = new DiagnosticOrder { Id = 501, VisitId = 10, ServiceId = 6, Status = "ORDERED" };
        var diagCharge = VisitCharge.CreateDiagnostic(10, 501, 6, 150_000m, FixedNow.DateTime);
        diagCharge.Id = 102;

        _db.VisitCharges.AddRange(examCharge, diagCharge);
        _db.DiagnosticOrders.Add(diagOrder);
        _db.Payments.Add(examPayment);
        await _db.SaveChangesAsync();

        var examGate = await _reader.CheckExaminationEligibilityAsync(10);
        Assert.True(examGate.IsUnlocked);

        var diagGateBefore = await _reader.CheckDiagnosticOrderEligibilityAsync(501);
        Assert.False(diagGateBefore.IsUnlocked);
        Assert.Equal(VisitChargeSettlementReader.StatusUnpaid, diagGateBefore.StatusCode);

        var diagPayment = Payment.CreateForCharge(10, 102, 150_000m, FixedNow.DateTime, PaymentStatuses.Paid);
        diagPayment.Id = 1002;
        diagPayment.PaymentCode = "MF1002";
        _db.Payments.Add(diagPayment);
        await _db.SaveChangesAsync();

        var diagGateAfter = await _reader.CheckDiagnosticOrderEligibilityAsync(501);
        Assert.True(diagGateAfter.IsUnlocked);
        Assert.Equal(VisitChargeSettlementReader.StatusPaid, diagGateAfter.StatusCode);
    }

    [Fact]
    public async Task CheckPharmacyVisitClearanceAsync_RespectsAdr0047ExamSettlementAndBlocksMismatchOrCancelled()
    {
        _db.Visits.Add(new Visit { Id = 20, Status = "COMPLETED" });
        var examCharge = VisitCharge.CreateExamination(20, 1, 200_000m, FixedNow.DateTime);
        examCharge.Id = 201;
        _db.VisitCharges.Add(examCharge);
        await _db.SaveChangesAsync();

        // Unpaid exam -> Pharmacy blocked
        var unpaidClearance = await _reader.CheckPharmacyVisitClearanceAsync(20);
        Assert.False(unpaidClearance.IsUnlocked);

        // Mismatch exam -> Pharmacy blocked
        var mismatchPay = Payment.CreateForCharge(20, 201, 200_000m, FixedNow.DateTime, SePayWebhookService.MismatchPaymentStatus);
        mismatchPay.Id = 2001;
        _db.Payments.Add(mismatchPay);
        await _db.SaveChangesAsync();

        var mismatchClearance = await _reader.CheckPharmacyVisitClearanceAsync(20);
        Assert.False(mismatchClearance.IsUnlocked);
        Assert.Equal(VisitChargeSettlementReader.StatusMismatch, mismatchClearance.StatusCode);

        // Exact PAID exam -> Pharmacy cleared (ADR-0047: medicine retail prices remain out of v4.1 scope)
        mismatchPay.Status = PaymentStatuses.Paid;
        await _db.SaveChangesAsync();

        var paidClearance = await _reader.CheckPharmacyVisitClearanceAsync(20);
        Assert.True(paidClearance.IsUnlocked);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class ReaderTestDb(DbContextOptions<ReaderTestDb> options) : DbContext(options), IApplicationDbContext
    {
        public DbSet<User> Users => Set<User>();
        public DbSet<Role> Roles => Set<Role>();
        public DbSet<Permission> Permissions => Set<Permission>();
        public DbSet<UserRole> UserRoles => Set<UserRole>();
        public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
        public DbSet<Patient> Patients => Set<Patient>();
        public DbSet<Staff> Staff => Set<Staff>();
        public DbSet<Doctor> Doctors => Set<Doctor>();
        public DbSet<DoctorServiceAuthorization> DoctorServiceAuthorizations => throw new NotSupportedException();
        public DbSet<ExaminationSlot> ExaminationSlots => Set<ExaminationSlot>();
        public DbSet<AuthSession> AuthSessions => throw new NotSupportedException();
        public DbSet<AuthVerificationChallenge> AuthVerificationChallenges => throw new NotSupportedException();
        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
        public DbSet<Hospital> Hospitals => Set<Hospital>();
        public DbSet<Department> Departments => throw new NotSupportedException();
        public DbSet<HospitalRoom> HospitalRooms => Set<HospitalRoom>();
        public DbSet<Specialty> Specialties => throw new NotSupportedException();
        public DbSet<Service> Services => Set<Service>();
        public DbSet<ServicePrice> ServicePrices => Set<ServicePrice>();
        public DbSet<HospitalPolicy> HospitalPolicies => Set<HospitalPolicy>();
        public DbSet<Appointment> Appointments => Set<Appointment>();
        public DbSet<PatientFamilyLink> PatientFamilyLinks => Set<PatientFamilyLink>();
        public DbSet<Visit> Visits => Set<Visit>();
        public DbSet<QueueEntry> QueueEntries => Set<QueueEntry>();
        public DbSet<Payment> Payments => Set<Payment>();
        public DbSet<VisitCharge> VisitCharges => Set<VisitCharge>();
        public DbSet<DiagnosticOrder> DiagnosticOrders => Set<DiagnosticOrder>();

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default)
            => Database.BeginTransactionAsync(ct);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Ignore<DoctorServiceAuthorization>();
            modelBuilder.Ignore<AuthSession>();
            modelBuilder.Ignore<AuthVerificationChallenge>();
            modelBuilder.Ignore<Department>();
            modelBuilder.Ignore<Specialty>();
            modelBuilder.Entity<AuditLog>().Ignore(a => a.IpAddress);
            modelBuilder.Entity<PatientFamilyLink>().HasKey(l => new { l.OwnerPatientId, l.MemberPatientId });
            modelBuilder.Entity<RolePermission>().HasKey(rp => new { rp.RoleId, rp.PermissionId });
            modelBuilder.Entity<UserRole>().HasKey(ur => new { ur.UserId, ur.RoleId });
        }
    }
}
