using MedFlow.Application.DTOs.SystemAdmin;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.SystemAdmin;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.UnitTests.SystemAdmin;

public sealed class StaffAccountListServiceTests : IDisposable
{
    private static readonly DateTime FixedNowUtc = new(2026, 11, 16, 8, 0, 0, DateTimeKind.Utc);

    private sealed class FixedTimeProvider(DateTimeOffset nowUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => nowUtc;
    }

    private sealed class StaffAccountTestDbContext : DbContext, IApplicationDbContext
    {
        public StaffAccountTestDbContext(DbContextOptions<StaffAccountTestDbContext> options)
            : base(options)
        {
        }

        private static NotSupportedException NotUsed() =>
            new("Not needed for StaffAccountListService unit tests.");

        public DbSet<User> Users => Set<User>();
        public DbSet<Role> Roles => Set<Role>();
        public DbSet<Permission> Permissions => throw NotUsed();
        public DbSet<UserRole> UserRoles => Set<UserRole>();
        public DbSet<RolePermission> RolePermissions => throw NotUsed();
        public DbSet<Patient> Patients => throw NotUsed();
        public DbSet<Staff> Staff => Set<Staff>();
        public DbSet<Doctor> Doctors => throw NotUsed();
        public DbSet<DoctorServiceAuthorization> DoctorServiceAuthorizations => throw NotUsed();
        public DbSet<ExaminationSlot> ExaminationSlots => throw NotUsed();
        public DbSet<AuthSession> AuthSessions => throw NotUsed();
        public DbSet<AuthVerificationChallenge> AuthVerificationChallenges => throw NotUsed();
        public DbSet<AuditLog> AuditLogs => throw NotUsed();
        public DbSet<Hospital> Hospitals => Set<Hospital>();
        public DbSet<Department> Departments => Set<Department>();
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

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default) =>
            Database.BeginTransactionAsync(ct);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Ignore<Permission>();
            modelBuilder.Ignore<RolePermission>();
            modelBuilder.Ignore<Patient>();
            modelBuilder.Ignore<Doctor>();
            modelBuilder.Ignore<DoctorServiceAuthorization>();
            modelBuilder.Ignore<ExaminationSlot>();
            modelBuilder.Ignore<AuthSession>();
            modelBuilder.Ignore<AuthVerificationChallenge>();
            modelBuilder.Ignore<AuditLog>();
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

            modelBuilder.Entity<User>().HasKey(u => u.Id);
            modelBuilder.Entity<Role>().HasKey(r => r.Id);
            modelBuilder.Entity<UserRole>().HasKey(ur => new { ur.UserId, ur.RoleId });
            modelBuilder.Entity<Staff>().HasKey(s => s.Id);
            modelBuilder.Entity<Hospital>().HasKey(h => h.Id);
            modelBuilder.Entity<Department>().HasKey(d => d.Id);

            modelBuilder.Entity<User>()
                .HasMany(u => u.UserRoles)
                .WithOne(ur => ur.User)
                .HasForeignKey(ur => ur.UserId);
            modelBuilder.Entity<Role>()
                .HasMany(r => r.UserRoles)
                .WithOne(ur => ur.Role)
                .HasForeignKey(ur => ur.RoleId);
            modelBuilder.Entity<User>()
                .HasOne(u => u.Staff)
                .WithOne(s => s.User)
                .HasForeignKey<Staff>(s => s.UserId);
            modelBuilder.Entity<Staff>()
                .HasOne(s => s.Department)
                .WithMany()
                .HasForeignKey(s => s.DepartmentId);
        }
    }

    private readonly StaffAccountTestDbContext _db;
    private readonly StaffAccountListService _service;

    public StaffAccountListServiceTests()
    {
        var options = new DbContextOptionsBuilder<StaffAccountTestDbContext>()
            .UseInMemoryDatabase($"StaffAccountListTests_{Guid.NewGuid()}")
            .Options;
        _db = new StaffAccountTestDbContext(options);
        SeedData(_db);
        _service = new StaffAccountListService(
            _db,
            new FixedTimeProvider(new DateTimeOffset(FixedNowUtc)));
    }

    [Fact]
    public async Task GetStaffAccountsAsync_SystemAdmin_FiltersAndReturnsScopedMetrics()
    {
        var result = await _service.GetStaffAccountsAsync(
            100,
            new GetStaffAccountListRequest(
                Keyword: "locked@medflow.test",
                RoleCode: "doctor",
                Status: "LOCKED",
                PageSize: 20));

        var item = Assert.Single(result.Data);
        Assert.Equal("STF-002", item.Code);
        Assert.Equal("LOCKED", item.Status);
        Assert.Equal("DOCTOR", item.RoleCode);
        Assert.Equal("Khoa Nội", item.Department);
        Assert.Equal("Cơ sở 1", item.Facility);
        Assert.Equal(6, result.Metrics.Total);
        Assert.Equal(2, result.Metrics.Active);
        Assert.Equal(1, result.Metrics.Locked);
        Assert.Equal(2, result.Metrics.Disabled);
        Assert.Equal(1, result.Metrics.Pending);
    }

    [Fact]
    public async Task GetStaffAccountsAsync_HospitalAdmin_LimitsResultsToOwnHospital()
    {
        var result = await _service.GetStaffAccountsAsync(
            200,
            new GetStaffAccountListRequest(PageSize: 20));

        Assert.Equal(4, result.Total);
        Assert.All(result.Data, item => Assert.Equal(1, item.HospitalId));
        Assert.DoesNotContain(result.Data, item => item.Code == "STF-003");
    }

    [Fact]
    public async Task GetStaffAccountsAsync_HospitalAdminRejectsAnotherHospital()
    {
        var exception = await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetStaffAccountsAsync(
                200,
                new GetStaffAccountListRequest(HospitalId: 2)));

        Assert.Contains("ngoài bệnh viện", exception.Message);
    }

    [Fact]
    public async Task GetStaffAccountsAsync_ForgedAdminRoleWithoutDatabaseAssignment_ReturnsForbidden()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetStaffAccountsAsync(1, new GetStaffAccountListRequest()));
    }

    [Theory]
    [InlineData(0, 10, null)]
    [InlineData(1, 0, null)]
    [InlineData(1, 101, null)]
    [InlineData(1, 10, "UNKNOWN")]
    public async Task GetStaffAccountsAsync_InvalidInput_ReturnsValidation(
        int page,
        int pageSize,
        string? status)
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.GetStaffAccountsAsync(
                100,
                new GetStaffAccountListRequest(Status: status, Page: page, PageSize: pageSize)));
    }

    private static void SeedData(StaffAccountTestDbContext db)
    {
        db.Hospitals.AddRange(
            new Hospital { Id = 1, HospitalCode = "HOS-01", Name = "Cơ sở 1", IsActive = true },
            new Hospital { Id = 2, HospitalCode = "HOS-02", Name = "Cơ sở 2", IsActive = true });
        db.Departments.AddRange(
            new Department { Id = 1, HospitalId = 1, DepartmentCode = "DEP-01", Name = "Khoa Nội", IsActive = true },
            new Department { Id = 2, HospitalId = 2, DepartmentCode = "DEP-02", Name = "Khoa Ngoại", IsActive = true });
        db.Roles.AddRange(
            new Role { Id = 1, Code = "SYSTEM_ADMIN", Name = "Quản trị hệ thống" },
            new Role { Id = 2, Code = "HOSPITAL_ADMIN", Name = "Quản trị bệnh viện" },
            new Role { Id = 3, Code = "DOCTOR", Name = "Bác sĩ" });
        db.Users.AddRange(
            new User { Id = 100, Email = "system@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 200, Email = "hospital@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 1, Email = "active@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 2, Email = "locked@medflow.test", AccountStatus = AccountStatus.ACTIVE, LockedUntil = FixedNowUtc.AddHours(1) },
            new User { Id = 3, Email = "disabled@medflow.test", AccountStatus = AccountStatus.INACTIVE },
            new User { Id = 4, Email = "pending@medflow.test", AccountStatus = AccountStatus.PENDING },
            new User { Id = 5, Email = "profile-disabled@medflow.test", AccountStatus = AccountStatus.ACTIVE });
        db.UserRoles.AddRange(
            new UserRole { UserId = 100, RoleId = 1 },
            new UserRole { UserId = 200, RoleId = 2 },
            new UserRole { UserId = 1, RoleId = 3 },
            new UserRole { UserId = 2, RoleId = 3 },
            new UserRole { UserId = 3, RoleId = 3 },
            new UserRole { UserId = 4, RoleId = 3 },
            new UserRole { UserId = 5, RoleId = 3 });
        db.Staff.AddRange(
            new Staff { Id = 200, UserId = 200, HospitalId = 1, DepartmentId = 1, StaffCode = "STF-200", FullName = "Quản trị Cơ sở", Status = "ACTIVE" },
            new Staff { Id = 1, UserId = 1, HospitalId = 1, DepartmentId = 1, StaffCode = "STF-001", FullName = "Active Doctor", Status = "ACTIVE" },
            new Staff { Id = 2, UserId = 2, HospitalId = 1, DepartmentId = 1, StaffCode = "STF-002", FullName = "Locked Doctor", Status = "ACTIVE" },
            new Staff { Id = 3, UserId = 3, HospitalId = 2, DepartmentId = 2, StaffCode = "STF-003", FullName = "Disabled Doctor", Status = "ACTIVE" },
            new Staff { Id = 4, UserId = 4, HospitalId = 1, DepartmentId = 1, StaffCode = "STF-004", FullName = "Pending Doctor", Status = "ACTIVE" },
            new Staff { Id = 5, UserId = 5, HospitalId = 2, DepartmentId = 2, StaffCode = "STF-005", FullName = "Inactive Profile", Status = "INACTIVE" });
        db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }
}
