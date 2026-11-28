using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Payments;
using MedFlow.Application.Services.Pharmacy;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.UnitTests.Payments;

public sealed class MedicineChargeProvisionerTests
{
    private sealed class ProvisionerTestDbContext : DbContext, IApplicationDbContext
    {
        public ProvisionerTestDbContext(DbContextOptions<ProvisionerTestDbContext> options)
            : base(options)
        {
        }

        private static NotSupportedException NotUsed() => new("Not needed for MedicineChargeProvisioner unit tests.");

        public DbSet<User> Users => throw NotUsed();
        public DbSet<Role> Roles => throw NotUsed();
        public DbSet<Permission> Permissions => throw NotUsed();
        public DbSet<UserRole> UserRoles => throw NotUsed();
        public DbSet<RolePermission> RolePermissions => throw NotUsed();
        public DbSet<Patient> Patients => throw NotUsed();
        public DbSet<Staff> Staff => throw NotUsed();
        public DbSet<Doctor> Doctors => throw NotUsed();
        public DbSet<DoctorServiceAuthorization> DoctorServiceAuthorizations => throw NotUsed();
        public DbSet<ExaminationSlot> ExaminationSlots => throw NotUsed();
        public DbSet<AuthSession> AuthSessions => throw NotUsed();
        public DbSet<AuthVerificationChallenge> AuthVerificationChallenges => throw NotUsed();
        public DbSet<Hospital> Hospitals => throw NotUsed();
        public DbSet<Department> Departments => throw NotUsed();
        public DbSet<HospitalRoom> HospitalRooms => throw NotUsed();
        public DbSet<Specialty> Specialties => throw NotUsed();
        public DbSet<Service> Services => throw NotUsed();
        public DbSet<ServicePrice> ServicePrices => throw NotUsed();
        public DbSet<HospitalPolicy> HospitalPolicies => throw NotUsed();
        public DbSet<Appointment> Appointments => throw NotUsed();
        public DbSet<PatientFamilyLink> PatientFamilyLinks => throw NotUsed();
        public DbSet<Visit> Visits => throw NotUsed();
        public DbSet<QueueEntry> QueueEntries => throw NotUsed();
        public DbSet<Payment> Payments => throw NotUsed();

        public DbSet<VisitCharge> VisitCharges => Set<VisitCharge>();
        public DbSet<MedicinePrice> MedicinePrices => Set<MedicinePrice>();
        public DbSet<PrescriptionItem> PrescriptionItems => Set<PrescriptionItem>();
        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default)
            => Database.BeginTransactionAsync(ct);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Ignore<User>();
            modelBuilder.Ignore<Role>();
            modelBuilder.Ignore<Permission>();
            modelBuilder.Ignore<UserRole>();
            modelBuilder.Ignore<RolePermission>();
            modelBuilder.Ignore<Patient>();
            modelBuilder.Ignore<Staff>();
            modelBuilder.Ignore<Doctor>();
            modelBuilder.Ignore<DoctorServiceAuthorization>();
            modelBuilder.Ignore<ExaminationSlot>();
            modelBuilder.Ignore<AuthSession>();
            modelBuilder.Ignore<AuthVerificationChallenge>();
            modelBuilder.Ignore<Hospital>();
            modelBuilder.Ignore<Department>();
            modelBuilder.Ignore<HospitalRoom>();
            modelBuilder.Ignore<Specialty>();
            modelBuilder.Ignore<Service>();
            modelBuilder.Ignore<ServicePrice>();
            modelBuilder.Ignore<HospitalPolicy>();
            modelBuilder.Ignore<Appointment>();
            modelBuilder.Ignore<PatientFamilyLink>();
            modelBuilder.Ignore<Visit>();
            modelBuilder.Ignore<QueueEntry>();
            modelBuilder.Ignore<Payment>();
            modelBuilder.Ignore<Medicine>();

            modelBuilder.Entity<VisitCharge>().HasKey(c => c.Id);
            modelBuilder.Entity<MedicinePrice>(b =>
            {
                b.HasKey(p => p.Id);
                b.Ignore(p => p.Medicine);
            });
            modelBuilder.Entity<PrescriptionItem>().HasKey(i => i.Id);
            modelBuilder.Entity<AuditLog>(b =>
            {
                b.HasKey(a => a.Id);
                b.Ignore(a => a.IpAddress);
                b.Ignore(a => a.ActorUser);
            });
        }
    }

    private static readonly DateTime AtLocal = new(2026, 11, 29, 9, 0, 0, DateTimeKind.Unspecified);

    private static ProvisionerTestDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<ProvisionerTestDbContext>()
            .UseInMemoryDatabase($"MedicineChargeProvisioner_{Guid.NewGuid():N}")
            .Options);

    private static Prescription IssuedPrescription(string fulfillment = "HOSPITAL") => new()
    {
        Id = 700,
        PrescriptionCode = "RX-866-700",
        VisitId = 900,
        Status = "ISSUED",
        FulfillmentChoice = fulfillment,
        IssuedAt = AtLocal,
    };

    private static void AddPrice(ProvisionerTestDbContext db, long medicineId, decimal price) =>
        db.MedicinePrices.Add(new MedicinePrice
        {
            Id = medicineId * 10,
            HospitalId = 1,
            MedicineId = medicineId,
            PriceAmount = price,
            EffectiveFrom = AtLocal.AddDays(-10),
            CreatedAt = AtLocal.AddDays(-10),
        });

    [Fact]
    public void CreateMedicine_SnapshotsUnitPriceAndRoundsLineTotal()
    {
        var charge = VisitCharge.CreateMedicine(
            visitId: 900,
            prescriptionId: 700,
            prescriptionItemId: 5,
            medicineId: 11,
            medicineUnitPrice: 1_500.50m,
            quantity: 3m,
            createdAtLocal: AtLocal);

        Assert.Equal(VisitChargeTypes.Medicine, charge.ChargeType);
        Assert.Equal(900, charge.VisitId);
        Assert.Equal(700, charge.PrescriptionId);
        Assert.Equal(5, charge.PrescriptionItemId);
        Assert.Equal(11, charge.MedicineId);
        Assert.Equal(1_500.50m, charge.MedicineUnitPrice);
        Assert.Equal(3m, charge.Quantity);
        Assert.Equal(4_502m, charge.UnitPrice);
        Assert.Equal("REQUIRED", charge.PaymentRequirement);
        Assert.Equal(VisitChargeStatuses.Active, charge.Status);
    }

    [Fact]
    public void CreateMedicine_ZeroUnitPrice_IsWaived()
    {
        var charge = VisitCharge.CreateMedicine(900, 700, 5, 11, 0m, 4m, AtLocal);

        Assert.Equal(0m, charge.UnitPrice);
        Assert.Equal("WAIVED", charge.PaymentRequirement);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(-1, 1)]
    [InlineData(1, -1)]
    public void CreateMedicine_RejectsNonPositiveQuantityOrIds(decimal quantity, long medicineId)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            VisitCharge.CreateMedicine(900, 700, 5, medicineId, 1_000m, quantity, AtLocal));
    }

    [Fact]
    public async Task EnsureForPrescription_CreatesOneChargePerLine_WithPriceSnapshot()
    {
        await using var db = CreateDb();
        var prescription = IssuedPrescription();
        db.PrescriptionItems.AddRange(
            new PrescriptionItem { Id = 1, PrescriptionId = prescription.Id, MedicineId = 11, PrescribedQuantity = 3m },
            new PrescriptionItem { Id = 2, PrescriptionId = prescription.Id, MedicineId = 12, PrescribedQuantity = 1.5m });
        AddPrice(db, 11, 1_000m);
        AddPrice(db, 12, 2_000.50m);
        await db.SaveChangesAsync();

        var result = await MedicineChargeProvisioner.EnsureForPrescriptionAsync(
            db,
            new MedicinePriceResolver(db),
            prescription,
            hospitalId: 1,
            actorUserId: 20,
            atHospitalLocal: AtLocal);

        Assert.Equal(2, result.CreatedCount);
        Assert.Empty(result.MissingPriceMedicineIds);
        await db.SaveChangesAsync();

        var charges = await db.VisitCharges.AsNoTracking().OrderBy(c => c.PrescriptionItemId).ToListAsync();
        Assert.Equal(2, charges.Count);
        Assert.All(charges, c =>
        {
            Assert.Equal(VisitChargeTypes.Medicine, c.ChargeType);
            Assert.Equal(prescription.Id, c.PrescriptionId);
            Assert.Equal(prescription.VisitId, c.VisitId);
        });
        Assert.Equal(3_000m, charges[0].UnitPrice);
        Assert.Equal(1_000m, charges[0].MedicineUnitPrice);
        Assert.Equal(3m, charges[0].Quantity);
        Assert.Equal(3_001m, charges[1].UnitPrice);
        Assert.Equal(2_000.50m, charges[1].MedicineUnitPrice);
    }

    [Fact]
    public async Task EnsureForPrescription_IsIdempotent()
    {
        await using var db = CreateDb();
        var prescription = IssuedPrescription();
        db.PrescriptionItems.Add(new PrescriptionItem { Id = 1, PrescriptionId = 700, MedicineId = 11, PrescribedQuantity = 2m });
        AddPrice(db, 11, 500m);
        await db.SaveChangesAsync();

        var resolver = new MedicinePriceResolver(db);
        var first = await MedicineChargeProvisioner.EnsureForPrescriptionAsync(db, resolver, prescription, 1, 20, AtLocal);
        await db.SaveChangesAsync();
        var second = await MedicineChargeProvisioner.EnsureForPrescriptionAsync(db, resolver, prescription, 1, 20, AtLocal);
        await db.SaveChangesAsync();

        Assert.Equal(1, first.CreatedCount);
        Assert.Equal(0, second.CreatedCount);
        Assert.Equal(1, await db.VisitCharges.CountAsync());
    }

    [Fact]
    public async Task EnsureForPrescription_MissingPrice_SkipsLineAndWritesAudit()
    {
        await using var db = CreateDb();
        var prescription = IssuedPrescription();
        db.PrescriptionItems.AddRange(
            new PrescriptionItem { Id = 1, PrescriptionId = 700, MedicineId = 11, PrescribedQuantity = 2m },
            new PrescriptionItem { Id = 2, PrescriptionId = 700, MedicineId = 12, PrescribedQuantity = 1m });
        AddPrice(db, 11, 500m); // medicine 12 has no effective price
        await db.SaveChangesAsync();

        var result = await MedicineChargeProvisioner.EnsureForPrescriptionAsync(
            db,
            new MedicinePriceResolver(db),
            prescription,
            1,
            20,
            AtLocal);
        await db.SaveChangesAsync();

        Assert.Equal(1, result.CreatedCount);
        Assert.Equal([12L], result.MissingPriceMedicineIds);
        Assert.Equal(1, await db.VisitCharges.CountAsync());
        var audit = await db.AuditLogs.SingleAsync();
        Assert.Equal(MedicineChargeProvisioner.UnpricedAuditAction, audit.Action);
        Assert.Contains("\"medicineIds\":[12]", audit.ChangeData);
    }

    [Fact]
    public async Task EnsureForPrescription_ReadyStatus_BackfillsMissingCharge()
    {
        await using var db = CreateDb();
        var prescription = IssuedPrescription();
        prescription.Status = "READY";
        db.PrescriptionItems.Add(new PrescriptionItem { Id = 1, PrescriptionId = 700, MedicineId = 11, PrescribedQuantity = 2m });
        AddPrice(db, 11, 12_500m);
        await db.SaveChangesAsync();

        var result = await MedicineChargeProvisioner.EnsureForPrescriptionAsync(
            db,
            new MedicinePriceResolver(db),
            prescription,
            hospitalId: 1,
            actorUserId: 20,
            atHospitalLocal: AtLocal);
        await db.SaveChangesAsync();

        Assert.Equal(1, result.CreatedCount);
        var charge = await db.VisitCharges.SingleAsync();
        Assert.Equal(VisitChargeTypes.Medicine, charge.ChargeType);
        Assert.Equal(700, charge.PrescriptionId);
        Assert.Equal(1, charge.PrescriptionItemId);
        Assert.Equal(25_000m, charge.UnitPrice);
        Assert.Equal(12_500m, charge.MedicineUnitPrice);
    }

    [Fact]
    public async Task EnsureForPrescription_ExternalPharmacy_DoesNotCreateCharges()
    {
        await using var db = CreateDb();
        var prescription = IssuedPrescription("EXTERNAL_PHARMACY");
        db.PrescriptionItems.Add(new PrescriptionItem { Id = 1, PrescriptionId = 700, MedicineId = 11, PrescribedQuantity = 2m });
        AddPrice(db, 11, 500m);
        await db.SaveChangesAsync();

        var result = await MedicineChargeProvisioner.EnsureForPrescriptionAsync(
            db,
            new MedicinePriceResolver(db),
            prescription,
            1,
            20,
            AtLocal);

        Assert.Equal(0, result.CreatedCount);
        Assert.Equal(0, await db.VisitCharges.CountAsync());
    }

    [Fact]
    public async Task EnsureForPrescription_Draft_DoesNotCreateCharges()
    {
        await using var db = CreateDb();
        var prescription = IssuedPrescription();
        prescription.Status = "DRAFT";
        db.PrescriptionItems.Add(new PrescriptionItem { Id = 1, PrescriptionId = 700, MedicineId = 11, PrescribedQuantity = 2m });
        AddPrice(db, 11, 500m);
        await db.SaveChangesAsync();

        var result = await MedicineChargeProvisioner.EnsureForPrescriptionAsync(
            db,
            new MedicinePriceResolver(db),
            prescription,
            1,
            20,
            AtLocal);

        Assert.Equal(0, result.CreatedCount);
    }
}
