using MedFlow.Application.Common.Time;
using MedFlow.Application.DTOs.Pharmacy;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Pharmacy;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.UnitTests.Pharmacy;

public sealed class FefoBatchSelectionServiceTests : IDisposable
{
    private sealed class FixedTimeProvider(DateTimeOffset nowUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => nowUtc;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class FefoTestDbContext(DbContextOptions<FefoTestDbContext> options)
        : DbContext(options), IApplicationDbContext
    {
        private static NotSupportedException NotUsed() =>
            new("DbSet not required for FefoBatchSelectionServiceTests.");

        public DbSet<User> Users => Set<User>();
        public DbSet<Role> Roles => Set<Role>();
        public DbSet<Permission> Permissions => Set<Permission>();
        public DbSet<UserRole> UserRoles => Set<UserRole>();
        public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
        public DbSet<Patient> Patients => Set<Patient>();
        public DbSet<Staff> Staff => Set<Staff>();
        public DbSet<Doctor> Doctors => Set<Doctor>();
        public DbSet<DoctorServiceAuthorization> DoctorServiceAuthorizations => throw NotUsed();
        public DbSet<ExaminationSlot> ExaminationSlots => throw NotUsed();
        public DbSet<AuthSession> AuthSessions => throw NotUsed();
        public DbSet<AuthVerificationChallenge> AuthVerificationChallenges => throw NotUsed();
        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
        public DbSet<Hospital> Hospitals => Set<Hospital>();
        public DbSet<Department> Departments => Set<Department>();
        public DbSet<HospitalRoom> HospitalRooms => Set<HospitalRoom>();
        public DbSet<Specialty> Specialties => throw NotUsed();
        public DbSet<Service> Services => throw NotUsed();
        public DbSet<ServicePrice> ServicePrices => throw NotUsed();
        public DbSet<HospitalPolicy> HospitalPolicies => throw NotUsed();
        public DbSet<Appointment> Appointments => throw NotUsed();
        public DbSet<PatientFamilyLink> PatientFamilyLinks => throw NotUsed();
        public DbSet<Visit> Visits => Set<Visit>();
        public DbSet<QueueEntry> QueueEntries => throw NotUsed();
        public DbSet<Payment> Payments => throw NotUsed();
        public DbSet<Medicine> Medicines => Set<Medicine>();
        public DbSet<MedicineBatch> MedicineBatches => Set<MedicineBatch>();
        public DbSet<Prescription> Prescriptions => Set<Prescription>();
        public DbSet<PrescriptionItem> PrescriptionItems => Set<PrescriptionItem>();
        public DbSet<MedicationDispense> MedicationDispenses => Set<MedicationDispense>();
        public DbSet<DispenseItem> DispenseItems => Set<DispenseItem>();

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default)
            => Database.BeginTransactionAsync(ct);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Ignore<DoctorServiceAuthorization>();
            modelBuilder.Ignore<ExaminationSlot>();
            modelBuilder.Ignore<AuthSession>();
            modelBuilder.Ignore<AuthVerificationChallenge>();
            modelBuilder.Ignore<Specialty>();
            modelBuilder.Ignore<Service>();
            modelBuilder.Ignore<ServicePrice>();
            modelBuilder.Ignore<HospitalPolicy>();
            modelBuilder.Ignore<Appointment>();
            modelBuilder.Ignore<PatientFamilyLink>();
            modelBuilder.Ignore<QueueEntry>();
            modelBuilder.Ignore<Payment>();

            modelBuilder.Entity<User>().HasKey(u => u.Id);
            modelBuilder.Entity<Role>().HasKey(r => r.Id);
            modelBuilder.Entity<Permission>().HasKey(p => p.Id);
            modelBuilder.Entity<UserRole>().HasKey(ur => new { ur.UserId, ur.RoleId });
            modelBuilder.Entity<RolePermission>().HasKey(rp => new { rp.RoleId, rp.PermissionId });
            modelBuilder.Entity<Patient>().HasKey(p => p.Id);
            modelBuilder.Entity<Staff>().HasKey(s => s.Id);
            modelBuilder.Entity<Doctor>().HasKey(d => d.Id);
            modelBuilder.Entity<AuditLog>().HasKey(a => a.Id);
            modelBuilder.Entity<Hospital>().HasKey(h => h.Id);
            modelBuilder.Entity<Department>().HasKey(d => d.Id);
            modelBuilder.Entity<HospitalRoom>().HasKey(hr => hr.Id);
            modelBuilder.Entity<Visit>().HasKey(v => v.Id);
            modelBuilder.Entity<Medicine>().HasKey(m => m.Id);
            modelBuilder.Entity<MedicineBatch>().HasKey(mb => mb.Id);
            modelBuilder.Entity<Prescription>().HasKey(p => p.Id);
            modelBuilder.Entity<PrescriptionItem>().HasKey(pi => pi.Id);
            modelBuilder.Entity<MedicationDispense>().HasKey(md => md.Id);
            modelBuilder.Entity<DispenseItem>().HasKey(di => di.Id);
        }
    }

    // 2026-11-24 18:30 UTC == 2026-11-25 01:30 Vietnam time (HospitalClock today = 2026-11-25!)
    private readonly DateTimeOffset _fixedNowUtc = new(2026, 11, 24, 18, 30, 0, TimeSpan.Zero);
    private readonly FefoTestDbContext _db;
    private readonly FefoBatchSelectionService _sut;

    public FefoBatchSelectionServiceTests()
    {
        var options = new DbContextOptionsBuilder<FefoTestDbContext>()
            .UseInMemoryDatabase($"fefo-batch-tests-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _db = new FefoTestDbContext(options);
        _sut = new FefoBatchSelectionService(_db, new FixedTimeProvider(_fixedNowUtc));
        SeedData();
    }

    public void Dispose()
    {
        _db.Database.EnsureDeleted();
        _db.Dispose();
    }

    private void SeedData()
    {
        _db.Hospitals.AddRange(
            new Hospital { Id = 1, HospitalCode = "HOS-01", Name = "Bệnh viện MedFlow CS1", IsActive = true },
            new Hospital { Id = 2, HospitalCode = "HOS-02", Name = "Bệnh viện MedFlow CS2", IsActive = true });

        _db.Roles.AddRange(
            new Role { Id = 1, Code = "PHARMACY_STAFF", Name = "Dược sĩ" },
            new Role { Id = 2, Code = "DOCTOR", Name = "Bác sĩ" });

        _db.Permissions.Add(new Permission { Id = 1, Code = "MEDICINE_DISPENSE" });
        _db.RolePermissions.Add(new RolePermission { RoleId = 1, PermissionId = 1 });

        _db.Users.AddRange(
            new User { Id = 10, Email = "pha1@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 11, Email = "pha2@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 12, Email = "doc1@medflow.test", AccountStatus = AccountStatus.ACTIVE });

        _db.UserRoles.AddRange(
            new UserRole { UserId = 10, RoleId = 1 },
            new UserRole { UserId = 11, RoleId = 1 },
            new UserRole { UserId = 12, RoleId = 2 });

        _db.Staff.AddRange(
            new Staff { Id = 20, UserId = 10, HospitalId = 1, StaffCode = "STF-PHA-01", FullName = "DS. Nguyễn Thu Hà", Status = "ACTIVE" },
            new Staff { Id = 21, UserId = 11, HospitalId = 2, StaffCode = "STF-PHA-02", FullName = "DS. Trần Văn Khác", Status = "ACTIVE" },
            new Staff { Id = 22, UserId = 12, HospitalId = 1, StaffCode = "STF-DOC-01", FullName = "BS. Lê Hoàng", Status = "ACTIVE" });

        _db.Doctors.AddRange(
            new Doctor { Id = 30, StaffId = 22, HospitalId = 1, DoctorCode = "DOC-01" },
            new Doctor { Id = 31, StaffId = 21, HospitalId = 2, DoctorCode = "DOC-02" });

        _db.Patients.Add(new Patient
        {
            Id = 50,
            PatientCode = "PAT-0050",
            FullName = "Phạm Văn Bình",
            Status = "ACTIVE",
        });

        _db.Visits.AddRange(
            new Visit { Id = 60, VisitCode = "VIS-0060", PatientId = 50, AssignedDoctorId = 30, Status = "COMPLETED" },
            new Visit { Id = 61, VisitCode = "VIS-0061", PatientId = 50, AssignedDoctorId = 31, Status = "COMPLETED" });

        _db.Prescriptions.AddRange(
            new Prescription { Id = 100, PrescriptionCode = "RX-FEFO-100", VisitId = 60, PrescribingDoctorId = 30, Status = "ISSUED" },
            new Prescription { Id = 200, PrescriptionCode = "RX-FEFO-200", VisitId = 61, PrescribingDoctorId = 31, Status = "ISSUED" });

        _db.Medicines.AddRange(
            new Medicine
            {
                Id = 1,
                HospitalId = 1,
                MedicineCode = "MED-001",
                Name = "Amoxicillin 500mg",
                DosageForm = "Viên nang",
                DispensingUnit = "viên",
                IsActive = true,
            },
            new Medicine
            {
                Id = 2,
                HospitalId = 2,
                MedicineCode = "MED-002",
                Name = "Cefuroxim 500mg",
                DosageForm = "Viên nén",
                DispensingUnit = "viên",
                IsActive = true,
            });

        _db.PrescriptionItems.AddRange(
            new PrescriptionItem { Id = 1001, PrescriptionId = 100, MedicineId = 1, PrescribedQuantity = 20m },
            new PrescriptionItem { Id = 2001, PrescriptionId = 200, MedicineId = 2, PrescribedQuantity = 10m });

        // HospitalClock.GetToday == 2026-11-25 (even though UTC date is 2026-11-24!)
        _db.MedicineBatches.AddRange(
            // Expired yesterday in HospitalClock (2026-11-24 < 2026-11-25) -> MUST be EXPIRED!
            new MedicineBatch
            {
                Id = 501,
                MedicineId = 1,
                BatchNumber = "LO-EXP-1124",
                ExpiryDate = new DateOnly(2026, 11, 24),
                QuantityOnHand = 100m,
                IsBlocked = false,
            },
            // Blocked batch -> MUST be BLOCKED!
            new MedicineBatch
            {
                Id = 502,
                MedicineId = 1,
                BatchNumber = "LO-BLK-1201",
                ExpiryDate = new DateOnly(2026, 12, 1),
                QuantityOnHand = 100m,
                IsBlocked = true,
            },
            // Later expiry batch (2027-06-30)
            new MedicineBatch
            {
                Id = 503,
                MedicineId = 1,
                BatchNumber = "LO-FAR-2706",
                ExpiryDate = new DateOnly(2027, 6, 30),
                QuantityOnHand = 150m,
                IsBlocked = false,
            },
            // Earliest valid FEFO batch (today 2026-11-25 >= today -> valid & FEFO_RECOMMENDED!)
            new MedicineBatch
            {
                Id = 504,
                MedicineId = 1,
                BatchNumber = "LO-NEAR-1125",
                ExpiryDate = new DateOnly(2026, 11, 25),
                QuantityOnHand = 60m,
                IsBlocked = false,
            },
            // Insufficient stock batch (5 < 20)
            new MedicineBatch
            {
                Id = 505,
                MedicineId = 1,
                BatchNumber = "LO-LOW-1126",
                ExpiryDate = new DateOnly(2026, 11, 26),
                QuantityOnHand = 5m,
                IsBlocked = false,
            });

        _db.SaveChanges();
    }

    [Fact]
    public async Task GetFefoBatchesAsync_SortsAvailableByExpiryAsc_UsesHospitalClockToday_AndMarksExpiredBlocked()
    {
        var context = await _sut.GetFefoBatchesAsync(10, "RX-FEFO-100");

        Assert.Equal(new DateOnly(2026, 11, 25), context.TodayHospitalDate);
        var item = Assert.Single(context.Items);
        Assert.Equal(504, item.SelectedBatchId);

        // Selectable batches come first sorted by ExpiryDate ASC: 504 (2026-11-25) -> 503 (2027-06-30)
        Assert.Equal("LO-NEAR-1125", item.Batches[0].BatchCode);
        Assert.Equal("FEFO_RECOMMENDED", item.Batches[0].Status);
        Assert.True(item.Batches[0].IsSelectable);

        Assert.Equal("LO-FAR-2706", item.Batches[1].BatchCode);
        Assert.Equal("AVAILABLE", item.Batches[1].Status);
        Assert.True(item.Batches[1].IsSelectable);

        var expired = Assert.Single(item.Batches, b => b.BatchCode == "LO-EXP-1124");
        Assert.Equal("EXPIRED", expired.Status);
        Assert.False(expired.IsSelectable);

        var blocked = Assert.Single(item.Batches, b => b.BatchCode == "LO-BLK-1201");
        Assert.Equal("BLOCKED", blocked.Status);
        Assert.False(blocked.IsSelectable);

        var lowStock = Assert.Single(item.Batches, b => b.BatchCode == "LO-LOW-1126");
        Assert.Equal("BLOCKED", lowStock.Status);
        Assert.False(lowStock.IsSelectable);
    }

    [Fact]
    public async Task SelectFefoBatchesAsync_SelectingRecommendedBatch_SucceedsWithoutOverrideReason()
    {
        var response = await _sut.SelectFefoBatchesAsync(
            actorUserId: 10,
            idOrCode: "RX-FEFO-100",
            request: new SelectFefoBatchesRequest(
                Items:
                [
                    new SelectFefoBatchItemRequest(
                        PrescriptionItemId: 1001,
                        MedicineBatchId: 504,
                        PickedQuantity: 20m),
                ]));

        Assert.Equal("PREPARING", response.Status);
        var alloc = Assert.Single(response.Allocations);
        Assert.True(alloc.IsFefoRecommended);
        Assert.False(alloc.IsFefoOverride);
        Assert.Equal(504, alloc.MedicineBatchId);

        var audit = await _db.AuditLogs.SingleAsync(a => a.Action == FefoBatchSelectionService.FefoBatchSelectedAction);
        Assert.Equal("100", audit.EntityId);
    }

    [Fact]
    public async Task SelectFefoBatchesAsync_SelectingLaterExpiryWithoutReason_ThrowsValidation_AndWithReason_Succeeds()
    {
        // Choosing LO-FAR-2706 (503) while LO-NEAR-1125 (504) has 60 >= 20 stock without reason -> 400 ValidationException
        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.SelectFefoBatchesAsync(
                actorUserId: 10,
                idOrCode: "RX-FEFO-100",
                request: new SelectFefoBatchesRequest(
                    Items:
                    [
                        new SelectFefoBatchItemRequest(
                            PrescriptionItemId: 1001,
                            MedicineBatchId: 503,
                            PickedQuantity: 20m),
                    ])));

        // Providing valid override reason -> succeeds!
        var okResponse = await _sut.SelectFefoBatchesAsync(
            actorUserId: 10,
            idOrCode: "RX-FEFO-100",
            request: new SelectFefoBatchesRequest(
                Items:
                [
                    new SelectFefoBatchItemRequest(
                        PrescriptionItemId: 1001,
                        MedicineBatchId: 503,
                        PickedQuantity: 20m,
                        OverrideReason: "Bao bì lô hạn gần đang chờ kiểm tra cảm quan tại quầy"),
                ]));

        var alloc = Assert.Single(okResponse.Allocations);
        Assert.True(alloc.IsFefoOverride);
        Assert.Equal("Bao bì lô hạn gần đang chờ kiểm tra cảm quan tại quầy", alloc.OverrideReason);
    }

    [Fact]
    public async Task SelectFefoBatchesAsync_ExpiredBlockedOrInsufficientStock_ThrowsConflict()
    {
        // Expired batch 501
        await Assert.ThrowsAsync<ConflictException>(() =>
            _sut.SelectFefoBatchesAsync(
                10,
                "RX-FEFO-100",
                new SelectFefoBatchesRequest([new SelectFefoBatchItemRequest(1001, MedicineBatchId: 501)])));

        // Blocked batch 502
        await Assert.ThrowsAsync<ConflictException>(() =>
            _sut.SelectFefoBatchesAsync(
                10,
                "RX-FEFO-100",
                new SelectFefoBatchesRequest([new SelectFefoBatchItemRequest(1001, MedicineBatchId: 502)])));

        // Insufficient stock batch 505 (5 < 20)
        await Assert.ThrowsAsync<ConflictException>(() =>
            _sut.SelectFefoBatchesAsync(
                10,
                "RX-FEFO-100",
                new SelectFefoBatchesRequest([new SelectFefoBatchItemRequest(1001, MedicineBatchId: 505)])));
    }

    [Fact]
    public async Task CrossHospitalAndMissingPermission_ThrowsForbidden()
    {
        // Cross-hospital prescription RX-FEFO-200 belongs to Hospital 2 -> Pharmacist of Hospital 1 gets 403 Forbidden
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.GetFefoBatchesAsync(10, "RX-FEFO-200"));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.SelectFefoBatchesAsync(
                10,
                "RX-FEFO-200",
                new SelectFefoBatchesRequest([new SelectFefoBatchItemRequest(2001, MedicineBatchId: 504)])));

        // Doctor role (user 12) -> 403 Forbidden
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.GetFefoBatchesAsync(12, "RX-FEFO-100"));

        // Revoke MEDICINE_DISPENSE permission -> fail-closed 403 Forbidden
        _db.RolePermissions.RemoveRange(_db.RolePermissions);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.GetFefoBatchesAsync(10, "RX-FEFO-100"));
    }
}
