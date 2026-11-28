using System.Text.Json;
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

public sealed class MedicinePriceAdminServiceTests : IDisposable
{
    private sealed class FixedTimeProvider(DateTimeOffset nowUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => nowUtc;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class MedicinePriceTestDbContext(DbContextOptions<MedicinePriceTestDbContext> options)
        : DbContext(options), IApplicationDbContext
    {
        private static NotSupportedException NotUsed() =>
            new("DbSet not required for MedicinePriceAdminServiceTests.");

        public DbSet<User> Users => Set<User>();
        public DbSet<Role> Roles => Set<Role>();
        public DbSet<Permission> Permissions => Set<Permission>();
        public DbSet<UserRole> UserRoles => Set<UserRole>();
        public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
        public DbSet<Patient> Patients => throw NotUsed();
        public DbSet<Staff> Staff => Set<Staff>();
        public DbSet<Doctor> Doctors => throw NotUsed();
        public DbSet<DoctorServiceAuthorization> DoctorServiceAuthorizations => throw NotUsed();
        public DbSet<ExaminationSlot> ExaminationSlots => throw NotUsed();
        public DbSet<AuthSession> AuthSessions => throw NotUsed();
        public DbSet<AuthVerificationChallenge> AuthVerificationChallenges => throw NotUsed();
        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
        public DbSet<Hospital> Hospitals => Set<Hospital>();
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
        public DbSet<Medicine> Medicines => Set<Medicine>();
        public DbSet<MedicinePrice> MedicinePrices => Set<MedicinePrice>();

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default)
            => Database.BeginTransactionAsync(ct);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Ignore<Patient>();
            modelBuilder.Ignore<Doctor>();
            modelBuilder.Ignore<DoctorServiceAuthorization>();
            modelBuilder.Ignore<ExaminationSlot>();
            modelBuilder.Ignore<AuthSession>();
            modelBuilder.Ignore<AuthVerificationChallenge>();
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

            modelBuilder.Entity<Hospital>().HasKey(h => h.Id);
            modelBuilder.Entity<User>().HasKey(u => u.Id);
            modelBuilder.Entity<Role>().HasKey(r => r.Id);
            modelBuilder.Entity<Permission>().HasKey(p => p.Id);
            modelBuilder.Entity<UserRole>().HasKey(ur => new { ur.UserId, ur.RoleId });
            modelBuilder.Entity<RolePermission>().HasKey(rp => new { rp.RoleId, rp.PermissionId });
            modelBuilder.Entity<Staff>().HasKey(st => st.Id);
            modelBuilder.Entity<Medicine>(b =>
            {
                b.HasKey(m => m.Id);
                b.HasMany(m => m.MedicinePrices)
                    .WithOne(p => p.Medicine)
                    .HasForeignKey(p => p.MedicineId);
            });
            modelBuilder.Entity<MedicinePrice>().HasKey(p => p.Id);
            modelBuilder.Entity<AuditLog>().HasKey(al => al.Id);
        }
    }

    private readonly DateTimeOffset _fixedNowUtc = new(2026, 11, 25, 3, 0, 0, TimeSpan.Zero); // 10:00 Vietnam
    private readonly MedicinePriceTestDbContext _db;
    private readonly MedicinePriceAdminService _sut;
    private readonly MedicinePriceResolver _resolver;

    public MedicinePriceAdminServiceTests()
    {
        var options = new DbContextOptionsBuilder<MedicinePriceTestDbContext>()
            .UseInMemoryDatabase($"medicine-price-admin-tests-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _db = new MedicinePriceTestDbContext(options);
        var timeProvider = new FixedTimeProvider(_fixedNowUtc);
        _sut = new MedicinePriceAdminService(_db, timeProvider);
        _resolver = new MedicinePriceResolver(_db, timeProvider);
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
            new Hospital { Id = 1, HospitalCode = "HOS-01", Name = "Bệnh viện Đa khoa MedFlow Cơ sở 1", IsActive = true },
            new Hospital { Id = 2, HospitalCode = "HOS-02", Name = "Bệnh viện Đa khoa MedFlow Cơ sở 2", IsActive = true });

        _db.Roles.AddRange(
            new Role { Id = 1, Code = "HOSPITAL_ADMIN", Name = "Quản trị bệnh viện" },
            new Role { Id = 2, Code = "SYSTEM_ADMIN", Name = "Quản trị hệ thống" },
            new Role { Id = 3, Code = "DOCTOR", Name = "Bác sĩ" });

        _db.Permissions.Add(new Permission { Id = 1, Code = "HOSPITAL_CONFIG" });
        _db.RolePermissions.AddRange(
            new RolePermission { RoleId = 1, PermissionId = 1 },
            new RolePermission { RoleId = 2, PermissionId = 1 });

        _db.Users.AddRange(
            new User { Id = 1, Email = "hadmin1@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 2, Email = "hadmin2@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 13, Email = "sysadmin@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 20, Email = "doctor@medflow.test", AccountStatus = AccountStatus.ACTIVE });

        _db.UserRoles.AddRange(
            new UserRole { UserId = 1, RoleId = 1 },
            new UserRole { UserId = 2, RoleId = 1 },
            new UserRole { UserId = 13, RoleId = 2 },
            new UserRole { UserId = 20, RoleId = 3 });

        _db.Staff.AddRange(
            new Staff { Id = 1, UserId = 1, HospitalId = 1, StaffCode = "STF-01", FullName = "Quản trị Cơ sở 1", Status = "ACTIVE" },
            new Staff { Id = 2, UserId = 2, HospitalId = 2, StaffCode = "STF-02", FullName = "Quản trị Cơ sở 2", Status = "ACTIVE" });

        var localNow = HospitalClock.GetNow(new FixedTimeProvider(_fixedNowUtc));

        _db.Medicines.AddRange(
            new Medicine
            {
                Id = 10,
                HospitalId = 1,
                MedicineCode = "MED-1-01",
                Name = "Paracetamol 500mg",
                ActiveIngredient = "Paracetamol",
                Strength = "500mg",
                DosageForm = "Viên nén",
                DispensingUnit = "Viên",
                IsActive = true,
            },
            new Medicine
            {
                Id = 11,
                HospitalId = 1,
                MedicineCode = "MED-1-02",
                Name = "Oresol",
                ActiveIngredient = "Glucose",
                DispensingUnit = "Gói",
                IsActive = true,
            },
            new Medicine
            {
                Id = 20,
                HospitalId = 2,
                MedicineCode = "MED-2-01",
                Name = "Amoxicillin 500mg",
                ActiveIngredient = "Amoxicillin",
                DispensingUnit = "Viên",
                IsActive = true,
            },
            new Medicine
            {
                Id = 12,
                HospitalId = 1,
                MedicineCode = "MED-1-03",
                Name = "Vitamin C",
                DispensingUnit = "Viên",
                IsActive = true,
            });

        _db.MedicinePrices.AddRange(
            new MedicinePrice
            {
                Id = 100,
                HospitalId = 1,
                MedicineId = 10,
                PriceAmount = 300m,
                EffectiveFrom = localNow.AddDays(-30),
                EffectiveTo = null,
            },
            new MedicinePrice
            {
                Id = 200,
                HospitalId = 2,
                MedicineId = 20,
                PriceAmount = 500m,
                EffectiveFrom = localNow.AddDays(-15),
                EffectiveTo = null,
            },
            // Bounded history for half-open interval resolution checks (hospital 1, medicine 12).
            new MedicinePrice
            {
                Id = 300,
                HospitalId = 1,
                MedicineId = 12,
                PriceAmount = 100m,
                EffectiveFrom = new DateTime(2026, 1, 1, 0, 0, 0),
                EffectiveTo = new DateTime(2026, 2, 1, 0, 0, 0),
            },
            new MedicinePrice
            {
                Id = 301,
                HospitalId = 1,
                MedicineId = 12,
                PriceAmount = 150m,
                EffectiveFrom = new DateTime(2026, 2, 1, 0, 0, 0),
                EffectiveTo = null,
            });

        _db.SaveChanges();
    }

    [Fact]
    public async Task GetMedicinePriceListAsync_HospitalAdmin_IsolatesOwnHospitalOnly()
    {
        var result = await _sut.GetMedicinePriceListAsync(1, new GetMedicinePriceListAdminRequest());

        Assert.Equal(1, result.HospitalId);
        Assert.Equal(3, result.Total);
        Assert.All(result.Data, item => Assert.Equal(1, item.HospitalId));
        Assert.DoesNotContain(result.Data, item => item.MedicineCode == "MED-2-01");
    }

    [Fact]
    public async Task GetMedicinePriceListAsync_HospitalAdminRequestingOtherHospital_ThrowsForbidden()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.GetMedicinePriceListAsync(1, new GetMedicinePriceListAdminRequest(HospitalId: 2)));
    }

    [Fact]
    public async Task GetMedicinePriceListAsync_FiltersByMedicineAndEffectiveInstant()
    {
        // At 2026-01-15 medicine 12 is priced 100; at 2026-03-01 it is priced 150.
        var january = await _sut.GetMedicinePriceListAsync(
            1,
            new GetMedicinePriceListAdminRequest(MedicineId: 12, EffectiveAt: "2026-01-15"));
        Assert.Equal(100m, Assert.Single(january.Data).Price);

        var march = await _sut.GetMedicinePriceListAsync(
            1,
            new GetMedicinePriceListAdminRequest(MedicineId: 12, EffectiveAt: "2026-03-01"));
        Assert.Equal(150m, Assert.Single(march.Data).Price);

        var unpriced = await _sut.GetMedicinePriceListAsync(
            1,
            new GetMedicinePriceListAdminRequest(MedicineId: 11, HasEffectivePrice: true));
        Assert.Empty(unpriced.Data);
    }

    [Fact]
    public async Task UpdateMedicinePriceAsync_ValidRequest_ClosesOpenPrice_WritesAuditLog()
    {
        var updated = await _sut.UpdateMedicinePriceAsync(
            actorUserId: 1,
            routeIdOrCode: "MED-1-01",
            request: new UpdateMedicinePriceAdminRequest(
                PriceAmount: 380m,
                ValidFrom: "2026-11-25",
                DecisionNumber: "QĐ-501/BV-KH",
                AppliedBy: "Hội đồng Giá Bệnh viện",
                Note: "Điều chỉnh giá thuốc quý IV"),
            ipAddress: "10.0.0.15");

        Assert.Equal(380m, updated.Price);
        Assert.Equal(380m, updated.CurrentPriceAmount);
        Assert.True(updated.HasEffectivePrice);
        Assert.Equal("QĐ-501/BV-KH", updated.DecisionNumber);
        Assert.Equal("Hội đồng Giá Bệnh viện", updated.AppliedBy);
        Assert.Equal("2026-11-25", updated.EffectiveDate);
        Assert.Equal(2, updated.PriceHistory.Count);
        Assert.True(updated.PriceHistory[0].IsActive);
        Assert.False(updated.PriceHistory[1].IsActive);

        var oldRow = await _db.MedicinePrices.FirstAsync(p => p.Id == 100);
        Assert.NotNull(oldRow.EffectiveTo);

        var newRow = await _db.MedicinePrices.SingleAsync(p => p.Id != 100 && p.MedicineId == 10);
        Assert.Equal(1, newRow.HospitalId);
        Assert.Equal(1, newRow.CreatedByUserId);
        Assert.Equal("QĐ-501/BV-KH", newRow.DecisionNumber);

        var audit = await _db.AuditLogs.SingleAsync(a =>
            a.Action == MedicinePriceAdminService.MedicinePriceUpdatedAction && a.EntityId == "10");
        Assert.Equal(1, audit.ActorUserId);
        Assert.Equal(1, audit.HospitalId);
        Assert.Equal("MEDICINE", audit.EntityType);
        using var doc = JsonDocument.Parse(audit.ChangeData!);
        Assert.Equal(380m, doc.RootElement.GetProperty("priceAmount").GetDecimal());
        Assert.Equal("QĐ-501/BV-KH", doc.RootElement.GetProperty("decisionNumber").GetString());
        Assert.Equal(300m, doc.RootElement.GetProperty("previousPriceAmount").GetDecimal());
    }

    [Fact]
    public async Task UpdateMedicinePriceAsync_ZeroPrice_IsAllowed()
    {
        var updated = await _sut.UpdateMedicinePriceAsync(
            actorUserId: 1,
            routeIdOrCode: "MED-1-02",
            request: new UpdateMedicinePriceAdminRequest(
                PriceAmount: 0m,
                ValidFrom: "2026-11-25",
                DecisionNumber: "QĐ-000/BV-MT"));

        Assert.Equal(0m, updated.Price);
        Assert.True(updated.HasEffectivePrice);
        Assert.Equal("0 đ", updated.PriceFormatted);
    }

    [Fact]
    public async Task UpdateMedicinePriceAsync_NegativePrice_ThrowsValidation()
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.UpdateMedicinePriceAsync(
                actorUserId: 1,
                routeIdOrCode: "10",
                request: new UpdateMedicinePriceAdminRequest(PriceAmount: -1_000m)));

        Assert.Contains("PriceAmount >= 0", ex.Message);
    }

    [Fact]
    public async Task UpdateMedicinePriceAsync_ValidFromGreaterThanValidTo_ThrowsValidation()
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.UpdateMedicinePriceAsync(
                actorUserId: 1,
                routeIdOrCode: "10",
                request: new UpdateMedicinePriceAdminRequest(
                    PriceAmount: 350m,
                    ValidFrom: "2026-12-20",
                    ValidTo: "2026-12-10")));

        Assert.Contains("ValidFrom", ex.Message);
    }

    [Fact]
    public async Task UpdateMedicinePriceAsync_CrossHospitalMedicine_ThrowsForbidden()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.UpdateMedicinePriceAsync(
                actorUserId: 1,
                routeIdOrCode: "20",
                request: new UpdateMedicinePriceAdminRequest(PriceAmount: 600m)));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.GetMedicinePriceDetailAsync(1, "MED-2-01"));
    }

    [Fact]
    public async Task SystemAdmin_CanQueryAndUpdateScopedMedicineAcrossHospitals()
    {
        var detailBefore = await _sut.GetMedicinePriceDetailAsync(13, "MED-2-01", hospitalId: 2);
        Assert.Equal(500m, detailBefore.Price);

        var updated = await _sut.UpdateMedicinePriceAsync(
            actorUserId: 13,
            routeIdOrCode: null,
            request: new UpdateMedicinePriceAdminRequest(
                MedicineId: 20,
                HospitalId: 2,
                PriceAmount: 550m,
                ValidFrom: "2026-11-25",
                DecisionNumber: "QĐ-SYS-02"));

        Assert.Equal(550m, updated.Price);
        Assert.Equal("QĐ-SYS-02", updated.DecisionNumber);
    }

    [Fact]
    public async Task GetMedicinePriceHistoryAsync_ReturnsVersionsNewestFirst()
    {
        var history = await _sut.GetMedicinePriceHistoryAsync(1, "MED-1-03");

        Assert.Equal(12, history.MedicineId);
        Assert.Equal(2, history.PriceHistory.Count);
        Assert.Equal(150m, history.PriceHistory[0].PriceAmount);
        Assert.Equal("ACTIVE", history.PriceHistory[0].Status);
        Assert.Equal(100m, history.PriceHistory[1].PriceAmount);
        Assert.Equal("EXPIRED", history.PriceHistory[1].Status);
    }

    [Fact]
    public async Task UpdateMedicinePriceAsync_BoundedWindow_UsesHalfOpenEndSplitsAndResumesBaseline_AndRejectsOverlap()
    {
        var afterPromo = await _sut.UpdateMedicinePriceAsync(
            actorUserId: 1,
            routeIdOrCode: "MED-1-01",
            request: new UpdateMedicinePriceAdminRequest(
                PriceAmount: 220m,
                ValidFrom: "2026-12-01",
                ValidTo: "2026-12-10",
                DecisionNumber: "QĐ-PROMO-12"));

        Assert.Equal(300m, afterPromo.Price);

        var rows = await _db.MedicinePrices
            .AsNoTracking()
            .Where(p => p.MedicineId == 10)
            .OrderBy(p => p.EffectiveFrom)
            .ToListAsync();

        Assert.Equal(3, rows.Count);
        Assert.Equal(300m, rows[0].PriceAmount);
        Assert.Equal(new DateTime(2026, 12, 1, 0, 0, 0), rows[0].EffectiveTo);

        Assert.Equal(220m, rows[1].PriceAmount);
        Assert.Equal(new DateTime(2026, 12, 1, 0, 0, 0), rows[1].EffectiveFrom);
        Assert.Equal(new DateTime(2026, 12, 11, 0, 0, 0), rows[1].EffectiveTo);

        Assert.Equal(300m, rows[2].PriceAmount);
        Assert.Equal(new DateTime(2026, 12, 11, 0, 0, 0), rows[2].EffectiveFrom);
        Assert.Null(rows[2].EffectiveTo);

        await Assert.ThrowsAsync<ConflictException>(() =>
            _sut.UpdateMedicinePriceAsync(
                actorUserId: 1,
                routeIdOrCode: "MED-1-01",
                request: new UpdateMedicinePriceAdminRequest(
                    PriceAmount: 199m,
                    ValidFrom: "2026-12-05",
                    ValidTo: "2026-12-15",
                    DecisionNumber: "QĐ-OVERLAP")));
    }

    [Fact]
    public async Task Resolver_UsesHalfOpenInterval_AndHospitalScope()
    {
        var beforeFirst = await _resolver.TryGetEffectivePriceAsync(
            1, 12, new DateTime(2025, 12, 31, 23, 59, 59));
        Assert.Null(beforeFirst);

        var atStart = await _resolver.TryGetEffectivePriceAsync(
            1, 12, new DateTime(2026, 1, 1, 0, 0, 0));
        Assert.NotNull(atStart);
        Assert.Equal(100m, atStart.PriceAmount);

        var atBoundary = await _resolver.TryGetEffectivePriceAsync(
            1, 12, new DateTime(2026, 2, 1, 0, 0, 0));
        Assert.NotNull(atBoundary);
        Assert.Equal(150m, atBoundary.PriceAmount);

        var afterBoundary = await _resolver.TryGetEffectivePriceAsync(
            1, 12, new DateTime(2026, 3, 1, 0, 0, 0));
        Assert.NotNull(afterBoundary);
        Assert.Equal(150m, afterBoundary.PriceAmount);

        var wrongHospital = await _resolver.TryGetEffectivePriceAsync(
            2, 12, new DateTime(2026, 3, 1, 0, 0, 0));
        Assert.Null(wrongHospital);

        var nowQuote = await _resolver.TryGetEffectivePriceAsync(1, 10);
        Assert.NotNull(nowQuote);
        Assert.Equal(300m, nowQuote.PriceAmount);

        // UTC inputs are converted to the hospital clock, not treated as wall-clock.
        var utcQuote = await _resolver.TryGetEffectivePriceAsync(
            1, 12, new DateTime(2026, 1, 31, 17, 0, 0, DateTimeKind.Utc)); // 2026-02-01 00:00 VN
        Assert.NotNull(utcQuote);
        Assert.Equal(150m, utcQuote.PriceAmount);
    }

    [Fact]
    public async Task Resolver_InvalidIds_ThrowsValidation()
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _resolver.TryGetEffectivePriceAsync(0, 12, new DateTime(2026, 1, 15)));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _resolver.TryGetEffectivePriceAsync(1, 0, new DateTime(2026, 1, 15)));
    }
}
