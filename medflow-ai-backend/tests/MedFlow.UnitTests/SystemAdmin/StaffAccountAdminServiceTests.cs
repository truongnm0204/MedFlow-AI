using MedFlow.Application.DTOs.SystemAdmin;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.SystemAdmin;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.UnitTests.SystemAdmin;

public sealed class StaffAccountAdminServiceTests : IDisposable
{
    private static readonly DateTime FixedNowUtc = new(2026, 11, 17, 8, 0, 0, DateTimeKind.Utc);

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(FixedNowUtc);
    }

    private sealed class StaffAccountTestDbContext : DbContext, IApplicationDbContext
    {
        public StaffAccountTestDbContext(DbContextOptions<StaffAccountTestDbContext> options)
            : base(options)
        {
        }

        private static NotSupportedException NotUsed() => new("Not used by StaffAccountAdminService tests.");

        public DbSet<User> Users => Set<User>();
        public DbSet<Role> Roles => Set<Role>();
        public DbSet<Permission> Permissions => Set<Permission>();
        public DbSet<UserRole> UserRoles => Set<UserRole>();
        public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
        public DbSet<Patient> Patients => throw NotUsed();
        public DbSet<Staff> Staff => Set<Staff>();
        public DbSet<Doctor> Doctors => Set<Doctor>();
        public DbSet<DoctorServiceAuthorization> DoctorServiceAuthorizations => throw NotUsed();
        public DbSet<ExaminationSlot> ExaminationSlots => Set<ExaminationSlot>();
        public DbSet<AuthSession> AuthSessions => Set<AuthSession>();
        public DbSet<AuthVerificationChallenge> AuthVerificationChallenges => throw NotUsed();
        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
        public DbSet<Hospital> Hospitals => throw NotUsed();
        public DbSet<Department> Departments => throw NotUsed();
        public DbSet<HospitalRoom> HospitalRooms => throw NotUsed();
        public DbSet<Specialty> Specialties => throw NotUsed();
        public DbSet<Service> Services => throw NotUsed();
        public DbSet<ServicePrice> ServicePrices => throw NotUsed();
        public DbSet<HospitalPolicy> HospitalPolicies => throw NotUsed();
        public DbSet<Appointment> Appointments => throw NotUsed();
        public DbSet<PatientFamilyLink> PatientFamilyLinks => throw NotUsed();
        public DbSet<Visit> Visits => Set<Visit>();
        public DbSet<QueueEntry> QueueEntries => throw NotUsed();
        public DbSet<Payment> Payments => throw NotUsed();
        public DbSet<QueueServicePoint> QueueServicePoints => Set<QueueServicePoint>();

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default)
            => throw NotUsed();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Ignore<Patient>();
            modelBuilder.Ignore<DoctorServiceAuthorization>();
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
            modelBuilder.Ignore<QueueEntry>();
            modelBuilder.Ignore<Payment>();

            modelBuilder.Entity<User>().HasKey(u => u.Id);
            modelBuilder.Entity<User>().Ignore(u => u.Patient);
            modelBuilder.Entity<User>().Ignore(u => u.UserRoles);
            modelBuilder.Entity<User>().Ignore(u => u.AuthSessions);
            modelBuilder.Entity<User>()
                .HasOne(u => u.Staff)
                .WithOne(s => s.User)
                .HasForeignKey<Staff>(s => s.UserId);

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

            modelBuilder.Entity<Staff>().HasKey(s => s.Id);
            modelBuilder.Entity<Staff>().Ignore(s => s.Department);
            modelBuilder.Entity<Staff>()
                .HasOne(s => s.Doctor)
                .WithOne(d => d.Staff)
                .HasForeignKey<Doctor>(d => d.StaffId);

            modelBuilder.Entity<Doctor>().HasKey(d => d.Id);
            modelBuilder.Entity<Doctor>().Ignore(d => d.Specialty);
            modelBuilder.Entity<Doctor>().Ignore(d => d.ServiceAuthorizations);
            modelBuilder.Entity<Doctor>().Ignore(d => d.ExaminationSlots);
            modelBuilder.Entity<ExaminationSlot>().HasKey(s => s.Id);
            modelBuilder.Entity<ExaminationSlot>().Ignore(s => s.Service);
            modelBuilder.Entity<ExaminationSlot>().Ignore(s => s.Doctor);
            modelBuilder.Entity<ExaminationSlot>().Ignore(s => s.Room);
            modelBuilder.Entity<ExaminationSlot>().Ignore(s => s.Appointments);
            modelBuilder.Entity<AuthSession>().HasKey(s => s.Id);
            modelBuilder.Entity<AuthSession>().Ignore(s => s.User);
            modelBuilder.Entity<AuthSession>().Ignore(s => s.IsActive);
            modelBuilder.Entity<AuditLog>().HasKey(a => a.Id);
            modelBuilder.Entity<AuditLog>().Ignore(a => a.ActorUser);
            modelBuilder.Entity<AuditLog>().Ignore(a => a.IpAddress);
            modelBuilder.Entity<Visit>().HasKey(v => v.Id);
            modelBuilder.Entity<QueueServicePoint>().HasKey(sp => sp.Id);
        }
    }

    private readonly StaffAccountTestDbContext _db;
    private readonly StaffAccountAdminService _service;

    public StaffAccountAdminServiceTests()
    {
        var options = new DbContextOptionsBuilder<StaffAccountTestDbContext>()
            .UseInMemoryDatabase($"StaffAccountAdminTests_{Guid.NewGuid():N}")
            .Options;
        _db = new StaffAccountTestDbContext(options);
        SeedBaseData();
        _service = new StaffAccountAdminService(_db, new FixedTimeProvider());
    }

    [Fact]
    public async Task DeactivateAsync_ValidTarget_DeactivatesAllAccountSurfacesAndAudits()
    {
        _db.AuthSessions.AddRange(
            new AuthSession
            {
                Id = 1,
                UserId = 200,
                RefreshTokenHash = "active",
                ExpiresAt = FixedNowUtc.AddDays(1),
            },
            new AuthSession
            {
                Id = 2,
                UserId = 200,
                RefreshTokenHash = "expired",
                ExpiresAt = FixedNowUtc.AddMinutes(-1),
            });
        await _db.SaveChangesAsync();

        var result = await _service.DeactivateAsync(
            100,
            20,
            new DeactivateStaffAccountRequest("Nhân viên đã nghỉ việc"),
            "127.0.0.1");

        Assert.Equal("INACTIVE", result.AccountStatus);
        Assert.Equal("INACTIVE", result.StaffStatus);
        Assert.True(result.DoctorProfileDeactivated);
        Assert.Equal(1, result.RevokedSessionCount);
        Assert.Equal(AccountStatus.INACTIVE, (await _db.Users.FindAsync(200L))!.AccountStatus);
        Assert.Equal("INACTIVE", (await _db.Staff.FindAsync(20L))!.Status);
        Assert.False((await _db.Doctors.FindAsync(30L))!.IsActive);
        Assert.NotNull((await _db.AuthSessions.FindAsync(1L))!.RevokedAt);
        Assert.Null((await _db.AuthSessions.FindAsync(2L))!.RevokedAt);

        var audit = Assert.Single(_db.AuditLogs);
        Assert.Equal("STAFF_ACCOUNT_DEACTIVATED", audit.Action);
        Assert.Equal("Nhân viên đã nghỉ việc", audit.Reason);
        Assert.Contains("\"revokedSessionCount\":1", audit.ChangeData);
    }

    [Fact]
    public async Task DeactivateAsync_SelfTarget_ThrowsForbidden()
    {
        var exception = await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.DeactivateAsync(
                100,
                10,
                new DeactivateStaffAccountRequest("Không được tự khóa")));

        Assert.Contains("không thể tự", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeactivateAsync_MissingUserManagePermission_ThrowsForbidden()
    {
        _db.RolePermissions.RemoveRange(_db.RolePermissions);
        await _db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.DeactivateAsync(
                100,
                20,
                new DeactivateStaffAccountRequest("Nhân viên đã nghỉ việc")));

        Assert.Contains("USER_MANAGE", exception.Message);
    }

    [Fact]
    public async Task DeactivateAsync_ActiveVisit_ThrowsConflictWithoutMutation()
    {
        _db.Visits.Add(new Visit { Id = 1, AssignedDoctorId = 30, Status = "IN_PROGRESS" });
        await _db.SaveChangesAsync();

        await AssertOperationalConflictAsync();
    }

    [Fact]
    public async Task DeactivateAsync_FutureActiveSlot_ThrowsConflictWithoutMutation()
    {
        _db.ExaminationSlots.Add(new ExaminationSlot
        {
            Id = 1,
            DoctorId = 30,
            StartsAt = DateTime.SpecifyKind(FixedNowUtc.AddHours(8).AddDays(1), DateTimeKind.Unspecified),
            Status = "AVAILABLE",
        });
        await _db.SaveChangesAsync();

        await AssertOperationalConflictAsync();
    }

    [Fact]
    public async Task DeactivateAsync_ActiveServicePoint_ThrowsConflictWithoutMutation()
    {
        _db.QueueServicePoints.Add(new QueueServicePoint
        {
            Id = 1,
            DoctorId = 30,
            Name = "Phòng khám 01",
            IsActive = true,
        });
        await _db.SaveChangesAsync();

        await AssertOperationalConflictAsync();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ngắn")]
    public async Task DeactivateAsync_InvalidReason_ThrowsValidation(string? reason)
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.DeactivateAsync(100, 20, new DeactivateStaffAccountRequest(reason)));
    }

    private async Task AssertOperationalConflictAsync()
    {
        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.DeactivateAsync(
                100,
                20,
                new DeactivateStaffAccountRequest("Nhân viên đã nghỉ việc")));

        Assert.Equal(AccountStatus.ACTIVE, (await _db.Users.FindAsync(200L))!.AccountStatus);
        Assert.Equal("ACTIVE", (await _db.Staff.FindAsync(20L))!.Status);
        Assert.Empty(_db.AuditLogs);
    }

    private void SeedBaseData()
    {
        _db.Users.AddRange(
            new User { Id = 100, Email = "admin@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 200, Email = "doctor@medflow.test", AccountStatus = AccountStatus.ACTIVE });
        _db.Roles.Add(new Role { Id = 1, Code = "SYSTEM_ADMIN", Name = "Quản trị hệ thống" });
        _db.Permissions.Add(new Permission { Id = 1, Code = "USER_MANAGE" });
        _db.UserRoles.Add(new UserRole { UserId = 100, RoleId = 1 });
        _db.RolePermissions.Add(new RolePermission { RoleId = 1, PermissionId = 1 });
        _db.Staff.AddRange(
            new Staff
            {
                Id = 10,
                UserId = 100,
                HospitalId = 1,
                StaffCode = "ADM-001",
                FullName = "Quản trị viên",
                Status = "ACTIVE",
            },
            new Staff
            {
                Id = 20,
                UserId = 200,
                HospitalId = 1,
                StaffCode = "DOC-001",
                FullName = "Bác sĩ mục tiêu",
                Status = "ACTIVE",
            });
        _db.Doctors.Add(new Doctor
        {
            Id = 30,
            StaffId = 20,
            HospitalId = 1,
            DoctorCode = "DOC-001",
            IsActive = true,
        });
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
    }

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }
}
