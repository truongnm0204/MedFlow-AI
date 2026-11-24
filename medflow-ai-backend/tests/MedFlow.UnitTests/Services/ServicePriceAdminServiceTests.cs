using System.Text.Json;
using MedFlow.Application.Common.Time;
using MedFlow.Application.DTOs.Services;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Services;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.UnitTests.Services;

public sealed class ServicePriceAdminServiceTests : IDisposable
{
    private sealed class FixedTimeProvider(DateTimeOffset nowUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => nowUtc;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class ServicePriceTestDbContext(DbContextOptions<ServicePriceTestDbContext> options)
        : DbContext(options), IApplicationDbContext
    {
        private static NotSupportedException NotUsed() =>
            new("DbSet not required for ServicePriceAdminServiceTests.");

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
        public DbSet<Department> Departments => Set<Department>();
        public DbSet<HospitalRoom> HospitalRooms => throw NotUsed();
        public DbSet<Specialty> Specialties => Set<Specialty>();
        public DbSet<Service> Services => Set<Service>();
        public DbSet<ServicePrice> ServicePrices => Set<ServicePrice>();
        public DbSet<HospitalPolicy> HospitalPolicies => throw NotUsed();
        public DbSet<Appointment> Appointments => throw NotUsed();
        public DbSet<PatientFamilyLink> PatientFamilyLinks => throw NotUsed();
        public DbSet<Visit> Visits => throw NotUsed();
        public DbSet<QueueEntry> QueueEntries => throw NotUsed();
        public DbSet<Payment> Payments => throw NotUsed();

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
            modelBuilder.Ignore<HospitalRoom>();
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
            modelBuilder.Entity<Department>().HasKey(d => d.Id);
            modelBuilder.Entity<Specialty>().HasKey(sp => sp.Id);
            modelBuilder.Entity<Service>(b =>
            {
                b.HasKey(s => s.Id);
                b.HasOne(s => s.Department).WithMany(d => d.Services).HasForeignKey(s => s.DepartmentId);
                b.HasOne(s => s.Specialty).WithMany(sp => sp.Services).HasForeignKey(s => s.SpecialtyId);
                b.HasMany(s => s.ServicePrices).WithOne(p => p.Service).HasForeignKey(p => p.ServiceId);
            });
            modelBuilder.Entity<ServicePrice>().HasKey(p => p.Id);
            modelBuilder.Entity<AuditLog>().HasKey(al => al.Id);
        }
    }

    private readonly DateTimeOffset _fixedNowUtc = new(2026, 11, 25, 3, 0, 0, TimeSpan.Zero); // 10:00 Vietnam time
    private readonly ServicePriceTestDbContext _db;
    private readonly ServicePriceAdminService _sut;

    public ServicePriceAdminServiceTests()
    {
        var options = new DbContextOptionsBuilder<ServicePriceTestDbContext>()
            .UseInMemoryDatabase($"service-price-admin-tests-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _db = new ServicePriceTestDbContext(options);
        _sut = new ServicePriceAdminService(_db, new FixedTimeProvider(_fixedNowUtc));
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
            new Role { Id = 3, Code = "DOCTOR", Name = "Bác sĩ" },
            new Role { Id = 4, Code = "HOSPITAL_ADMIN_NO_PERM", Name = "Admin thiếu quyền" });

        _db.Permissions.Add(new Permission { Id = 1, Code = "HOSPITAL_CONFIG" });
        _db.RolePermissions.AddRange(
            new RolePermission { RoleId = 1, PermissionId = 1 },
            new RolePermission { RoleId = 2, PermissionId = 1 });

        _db.Users.AddRange(
            new User { Id = 1, Email = "hadmin1@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 2, Email = "hadmin2@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 13, Email = "sysadmin@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 20, Email = "doctor@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 21, Email = "noperm@medflow.test", AccountStatus = AccountStatus.ACTIVE });

        _db.UserRoles.AddRange(
            new UserRole { UserId = 1, RoleId = 1 },
            new UserRole { UserId = 2, RoleId = 1 },
            new UserRole { UserId = 13, RoleId = 2 },
            new UserRole { UserId = 20, RoleId = 3 },
            new UserRole { UserId = 21, RoleId = 4 });

        _db.Staff.AddRange(
            new Staff { Id = 1, UserId = 1, HospitalId = 1, StaffCode = "STF-01", FullName = "Quản trị Cơ sở 1", Status = "ACTIVE" },
            new Staff { Id = 2, UserId = 2, HospitalId = 2, StaffCode = "STF-02", FullName = "Quản trị Cơ sở 2", Status = "ACTIVE" },
            new Staff { Id = 3, UserId = 21, HospitalId = 1, StaffCode = "STF-21", FullName = "Admin Thiếu Quyền", Status = "ACTIVE" });

        _db.Departments.AddRange(
            new Department { Id = 1, HospitalId = 1, DepartmentCode = "KKB", Name = "Khoa Khám bệnh", IsActive = true },
            new Department { Id = 2, HospitalId = 2, DepartmentCode = "KTM", Name = "Khoa Tim mạch CS2", IsActive = true });

        _db.Services.AddRange(
            new Service
            {
                Id = 10,
                HospitalId = 1,
                DepartmentId = 1,
                ServiceCode = "EX-1-01",
                Name = "Khám Nội Tổng quát",
                ServiceType = "EXAMINATION",
                Description = "Khám ngoại trú tổng quát",
                AllowsOnlineBooking = true,
                IsActive = true,
            },
            new Service
            {
                Id = 11,
                HospitalId = 1,
                DepartmentId = 1,
                ServiceCode = "EX-1-02",
                Name = "Tư vấn Dinh dưỡng Miễn phí",
                ServiceType = "CONSULTATION",
                AllowsOnlineBooking = false,
                IsActive = true,
            },
            new Service
            {
                Id = 20,
                HospitalId = 2,
                DepartmentId = 2,
                ServiceCode = "EX-2-01",
                Name = "Khám Tim mạch Cơ sở 2",
                ServiceType = "EXAMINATION",
                AllowsOnlineBooking = true,
                IsActive = true,
            });

        var localNow = HospitalClock.GetNow(new FixedTimeProvider(_fixedNowUtc));
        _db.ServicePrices.AddRange(
            new ServicePrice
            {
                Id = 100,
                ServiceId = 10,
                PriceAmount = 300_000m,
                EffectiveFrom = localNow.AddDays(-30),
                EffectiveTo = null,
            },
            new ServicePrice
            {
                Id = 200,
                ServiceId = 20,
                PriceAmount = 500_000m,
                EffectiveFrom = localNow.AddDays(-15),
                EffectiveTo = null,
            });

        _db.SaveChanges();
    }

    [Fact]
    public async Task GetServicePriceListAsync_HospitalAdmin_IsolatesOwnHospitalOnly()
    {
        var result = await _sut.GetServicePriceListAsync(1, new GetServicePriceListAdminRequest());

        Assert.Equal(1, result.HospitalId);
        Assert.Equal(2, result.Total);
        Assert.All(result.Data, item => Assert.Equal(1, item.HospitalId));
        Assert.DoesNotContain(result.Data, item => item.ServiceCode == "EX-2-01");
    }

    [Fact]
    public async Task GetServicePriceListAsync_HospitalAdminRequestingOtherHospital_ThrowsForbidden()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.GetServicePriceListAsync(1, new GetServicePriceListAdminRequest(HospitalId: 2)));
    }

    [Fact]
    public async Task GetServicePriceListAsync_MissingHospitalConfigPermission_ThrowsForbidden()
    {
        var role = await _db.Roles.FirstAsync(r => r.Id == 1);
        var rp = await _db.RolePermissions.Where(x => x.RoleId == role.Id).ToListAsync();
        _db.RolePermissions.RemoveRange(rp);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.GetServicePriceListAsync(1, new GetServicePriceListAdminRequest()));
    }

    [Fact]
    public async Task UpdateServicePriceAsync_ValidRequest_ClosesOpenPrice_SavesZeroOrPositivePrice_AndWritesAuditLog()
    {
        var updated = await _sut.UpdateServicePriceAsync(
            actorUserId: 1,
            routeIdOrCode: "EX-1-01",
            request: new UpdateServicePriceAdminRequest(
                PriceAmount: 380_000m,
                ValidFrom: "2026-11-25",
                DecisionNumber: "QĐ-501/BV-KH",
                AppliedBy: "Hội đồng Giá Bệnh viện",
                Note: "Điều chỉnh biểu phí quý IV"),
            ipAddress: "10.0.0.15");

        Assert.Equal(380_000m, updated.Price);
        Assert.True(updated.HasEffectivePrice);
        Assert.Equal("QĐ-501/BV-KH", updated.DecisionNumber);
        Assert.Equal("Hội đồng Giá Bệnh viện", updated.AppliedBy);
        Assert.Equal("2026-11-25", updated.EffectiveDate);
        Assert.Equal(2, updated.PriceHistory.Count);
        Assert.True(updated.PriceHistory[0].IsActive);
        Assert.False(updated.PriceHistory[1].IsActive);

        var oldRow = await _db.ServicePrices.FirstAsync(p => p.Id == 100);
        Assert.NotNull(oldRow.EffectiveTo);

        var audit = await _db.AuditLogs
            .SingleAsync(a => a.Action == ServicePriceAdminService.ServicePriceUpdatedAction && a.EntityId == "10");
        Assert.Equal(1, audit.ActorUserId);
        Assert.Equal(1, audit.HospitalId);
        using var doc = JsonDocument.Parse(audit.ChangeData!);
        Assert.Equal(380_000m, doc.RootElement.GetProperty("priceAmount").GetDecimal());
        Assert.Equal("QĐ-501/BV-KH", doc.RootElement.GetProperty("decisionNumber").GetString());

        // Zero price (0 VND) is allowed per PriceAmount >= 0
        var zeroPriced = await _sut.UpdateServicePriceAsync(
            actorUserId: 1,
            routeIdOrCode: "11",
            request: new UpdateServicePriceAdminRequest(
                PriceAmount: 0m,
                ValidFrom: "2026-11-25",
                ValidTo: "2026-12-31",
                DecisionNumber: "QĐ-000/BV-MT",
                Note: "Miễn phí tư vấn dinh dưỡng"));

        Assert.Equal(0m, zeroPriced.Price);
        Assert.Equal(0m, zeroPriced.CurrentPriceAmount);
        Assert.True(zeroPriced.HasEffectivePrice);
        Assert.Equal("0 đ", zeroPriced.PriceFormatted);
    }

    [Fact]
    public async Task UpdateServicePriceAsync_NegativePrice_ThrowsValidation()
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.UpdateServicePriceAsync(
                actorUserId: 1,
                routeIdOrCode: "10",
                request: new UpdateServicePriceAdminRequest(PriceAmount: -1_000m)));

        Assert.Contains("PriceAmount >= 0", ex.Message);
    }

    [Fact]
    public async Task UpdateServicePriceAsync_ValidFromGreaterThanValidTo_ThrowsValidation()
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.UpdateServicePriceAsync(
                actorUserId: 1,
                routeIdOrCode: "10",
                request: new UpdateServicePriceAdminRequest(
                    PriceAmount: 350_000m,
                    ValidFrom: "2026-12-20",
                    ValidTo: "2026-12-10")));

        Assert.Contains("ValidFrom", ex.Message);
    }

    [Fact]
    public async Task UpdateServicePriceAsync_CrossHospitalService_ThrowsForbidden()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.UpdateServicePriceAsync(
                actorUserId: 1,
                routeIdOrCode: "20",
                request: new UpdateServicePriceAdminRequest(
                    PriceAmount: 600_000m,
                    DecisionNumber: "QĐ-999")));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.GetServicePriceDetailAsync(1, "EX-2-01"));
    }

    [Fact]
    public async Task SystemAdmin_CanQueryAndUpdateScopedServiceAcrossHospitals()
    {
        var detailBefore = await _sut.GetServicePriceDetailAsync(13, "EX-2-01", hospitalId: 2);
        Assert.Equal(500_000m, detailBefore.Price);

        var updated = await _sut.UpdateServicePriceAsync(
            actorUserId: 13,
            routeIdOrCode: null,
            request: new UpdateServicePriceAdminRequest(
                ServiceId: 20,
                PriceAmount: 550_000m,
                ValidFrom: "2026-11-25",
                DecisionNumber: "QĐ-SYS-02"));

        Assert.Equal(550_000m, updated.Price);
        Assert.Equal("QĐ-SYS-02", updated.DecisionNumber);
    }
}
