using MedFlow.Application.DTOs.SystemAdmin;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.SystemAdmin;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.UnitTests.SystemAdmin;

public sealed class SystemAdminDashboardServiceTests : IDisposable
{
    private sealed class FixedTimeProvider(DateTimeOffset nowUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => nowUtc;
    }

    private sealed class SystemAdminTestDbContext : DbContext, IApplicationDbContext
    {
        public SystemAdminTestDbContext(DbContextOptions<SystemAdminTestDbContext> options)
            : base(options)
        {
        }

        private static NotSupportedException NotUsed() =>
            new("Not needed for SystemAdminDashboardService unit tests.");

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
        public DbSet<Specialty> Specialties => throw NotUsed();
        public DbSet<Service> Services => throw NotUsed();
        public DbSet<ServicePrice> ServicePrices => throw NotUsed();
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
            modelBuilder.Entity<Permission>().HasKey(p => p.Id);
            modelBuilder.Entity<UserRole>().HasKey(ur => new { ur.UserId, ur.RoleId });
            modelBuilder.Entity<RolePermission>().HasKey(rp => new { rp.RoleId, rp.PermissionId });
            modelBuilder.Entity<Staff>().HasKey(s => s.Id);
            modelBuilder.Entity<Hospital>().HasKey(h => h.Id);
            modelBuilder.Entity<Department>().HasKey(d => d.Id);
            modelBuilder.Entity<AuditLog>().HasKey(a => a.Id);
            modelBuilder.Entity<AuditLog>().Ignore(a => a.IpAddress);
        }
    }

    private static readonly DateTime FixedNowUtc = new(2026, 4, 20, 10, 0, 0, DateTimeKind.Utc);

    private readonly SystemAdminTestDbContext _dbContext;
    private readonly SystemAdminDashboardService _service;

    public SystemAdminDashboardServiceTests()
    {
        var options = new DbContextOptionsBuilder<SystemAdminTestDbContext>()
            .UseInMemoryDatabase(databaseName: $"SystemAdminDashboardTests_{Guid.NewGuid()}")
            .Options;

        _dbContext = new SystemAdminTestDbContext(options);
        SeedData(_dbContext);

        var timeProvider = new FixedTimeProvider(new DateTimeOffset(FixedNowUtc));
        _service = new SystemAdminDashboardService(_dbContext, timeProvider);
    }

    private static void SeedData(SystemAdminTestDbContext db)
    {
        db.Hospitals.AddRange(
            new Hospital { Id = 1, HospitalCode = "HOS-01", Name = "Bệnh viện Trung ương MedFlow", IsActive = true },
            new Hospital { Id = 2, HospitalCode = "HOS-02", Name = "Bệnh viện Đa khoa Cơ sở 2", IsActive = true });

        db.Departments.AddRange(
            new Department { Id = 1, HospitalId = 1, DepartmentCode = "DEP-01", Name = "Khoa Nội tổng quát", IsActive = true },
            new Department { Id = 2, HospitalId = 1, DepartmentCode = "DEP-02", Name = "Khoa Tim mạch", IsActive = true },
            new Department { Id = 3, HospitalId = 1, DepartmentCode = "DEP-03", Name = "Khoa Cũ Ngừng Hoạt Động", IsActive = false },
            new Department { Id = 4, HospitalId = 2, DepartmentCode = "DEP-04", Name = "Khoa Ngoại Cơ sở 2", IsActive = true });

        db.Roles.AddRange(
            new Role { Id = 1, Code = "SYSTEM_ADMIN", Name = "Quản trị hệ thống" },
            new Role { Id = 2, Code = "HOSPITAL_ADMIN", Name = "Quản trị bệnh viện" },
            new Role { Id = 3, Code = "DOCTOR", Name = "Bác sĩ" });

        db.Permissions.AddRange(
            new Permission { Id = 1, Code = "USER_MANAGE" },
            new Permission { Id = 2, Code = "HOSPITAL_CONFIG" },
            new Permission { Id = 3, Code = "AUDIT_READ" });

        db.RolePermissions.AddRange(
            new RolePermission { RoleId = 1, PermissionId = 1 },
            new RolePermission { RoleId = 1, PermissionId = 2 },
            new RolePermission { RoleId = 1, PermissionId = 3 },
            new RolePermission { RoleId = 2, PermissionId = 2 });

        db.Users.AddRange(
            new User
            {
                Id = 13,
                Email = "system_admin01@medflow.test",
                AccountStatus = AccountStatus.ACTIVE,
            },
            new User
            {
                Id = 14,
                Email = "locked_sysadmin@medflow.test",
                AccountStatus = AccountStatus.ACTIVE,
                LockedUntil = FixedNowUtc.AddMinutes(30),
            },
            new User
            {
                Id = 15,
                Email = "inactive_staff_sysadmin@medflow.test",
                AccountStatus = AccountStatus.ACTIVE,
            },
            new User
            {
                Id = 16,
                Email = "inactive_user_sysadmin@medflow.test",
                AccountStatus = AccountStatus.INACTIVE,
            },
            new User
            {
                Id = 11,
                Email = "hospital_admin01@medflow.test",
                AccountStatus = AccountStatus.ACTIVE,
            },
            new User
            {
                Id = 21,
                Email = "doctor01@medflow.test",
                AccountStatus = AccountStatus.ACTIVE,
            });

        db.UserRoles.AddRange(
            new UserRole { UserId = 13, RoleId = 1 },
            new UserRole { UserId = 14, RoleId = 1 },
            new UserRole { UserId = 15, RoleId = 1 },
            new UserRole { UserId = 16, RoleId = 1 },
            new UserRole { UserId = 11, RoleId = 2 },
            new UserRole { UserId = 21, RoleId = 3 });

        db.Staff.AddRange(
            new Staff
            {
                Id = 101,
                UserId = 13,
                HospitalId = 1,
                StaffCode = "STF-011",
                FullName = "Quản trị viên Hệ thống",
                Status = "ACTIVE",
            },
            new Staff
            {
                Id = 102,
                UserId = 14,
                HospitalId = 1,
                StaffCode = "STF-012",
                FullName = "Quản trị viên Tạm khóa",
                Status = "ACTIVE",
            },
            new Staff
            {
                Id = 103,
                UserId = 15,
                HospitalId = 1,
                StaffCode = "STF-013",
                FullName = "Quản trị viên Ngừng việc",
                Status = "INACTIVE",
            },
            new Staff
            {
                Id = 104,
                UserId = 11,
                HospitalId = 1,
                StaffCode = "STF-010",
                FullName = "Quản trị viên Bệnh viện",
                Status = "ACTIVE",
            },
            new Staff
            {
                Id = 105,
                UserId = 21,
                HospitalId = 2,
                StaffCode = "STF-020",
                FullName = "Bác sĩ Cơ sở 2",
                Status = "ACTIVE",
            });

        db.AuditLogs.AddRange(
            new AuditLog
            {
                Id = 1,
                ActorUserId = 13,
                HospitalId = 1,
                Action = "AUTH_LOGIN_SUCCESS",
                EntityType = "User",
                EntityId = "13",
                Reason = "Đăng nhập thành công",
                OccurredAt = FixedNowUtc.AddHours(-1),
            },
            new AuditLog
            {
                Id = 2,
                ActorUserId = 13,
                HospitalId = 1,
                Action = "ROLE_PERMISSION_UPDATE",
                EntityType = "Role",
                EntityId = "2",
                Reason = "Cập nhật quyền quản trị",
                OccurredAt = FixedNowUtc.AddHours(-5),
            },
            new AuditLog
            {
                Id = 3,
                ActorUserId = 11,
                HospitalId = 1,
                Action = "DEPARTMENT_CREATE",
                EntityType = "Department",
                EntityId = "2",
                Reason = "Tạo khoa mới",
                OccurredAt = FixedNowUtc.AddHours(-20),
            },
            new AuditLog
            {
                Id = 4,
                ActorUserId = 21,
                HospitalId = 2,
                Action = "SLOT_UPDATE",
                EntityType = "ExaminationSlot",
                EntityId = "50",
                Reason = "Cập nhật ca khám cơ sở 2",
                OccurredAt = FixedNowUtc.AddHours(-30),
            },
            new AuditLog
            {
                Id = 5,
                ActorUserId = 13,
                HospitalId = 1,
                Action = "HOSPITAL_POLICY_UPDATE",
                EntityType = "HospitalPolicy",
                EntityId = "1",
                Reason = "Cập nhật chính sách đặt lịch",
                OccurredAt = FixedNowUtc.AddHours(-48),
            });

        db.SaveChanges();
    }

    [Fact]
    public async Task GetDashboardAsync_ValidSystemAdmin_ReturnsCompleteOverviewAndMetrics()
    {
        var result = await _service.GetDashboardAsync(13, new GetSystemAdminDashboardRequest());

        Assert.NotNull(result);
        Assert.Equal(FixedNowUtc, result.GeneratedAtUtc);

        // Profile assertions
        Assert.Equal(13, result.AdminProfile.UserId);
        Assert.Equal("system_admin01@medflow.test", result.AdminProfile.Email);
        Assert.Equal("Quản trị viên Hệ thống", result.AdminProfile.FullName);
        Assert.Equal("STF-011", result.AdminProfile.StaffCode);
        Assert.Equal(1, result.AdminProfile.HospitalId);
        Assert.Equal("Bệnh viện Trung ương MedFlow", result.AdminProfile.HospitalName);
        Assert.Contains("SYSTEM_ADMIN", result.AdminProfile.Roles);
        Assert.Equal(["AUDIT_READ", "HOSPITAL_CONFIG", "USER_MANAGE"], result.AdminProfile.Permissions);

        // Metrics assertions
        Assert.Equal(6, result.Metrics.TotalUsers);
        Assert.Equal(4, result.Metrics.ActiveUsers);
        Assert.Equal(2, result.Metrics.LockedOrInactiveUsers);
        Assert.Equal(5, result.Metrics.TotalStaffAccounts);
        Assert.Equal(3, result.Metrics.ActiveStaffAccounts);
        Assert.Equal(2, result.Metrics.InactiveOrLockedStaffAccounts);
        Assert.Equal(3, result.Metrics.TotalRoles);
        Assert.Equal(3, result.Metrics.TotalPermissions);
        Assert.Equal(2, result.Metrics.TotalHospitals);
        Assert.Equal(3, result.Metrics.ActiveDepartments);
        Assert.Equal(5, result.Metrics.TotalAuditLogs);
        Assert.Equal(3, result.Metrics.RecentAuditCount24h);

        // Allowed modules assertions
        Assert.Equal(5, result.AllowedModules.Count);
        Assert.Equal(
            ["STAFF_ACCOUNTS", "ROLES_PERMISSIONS", "HOSPITAL_DEPARTMENTS", "APPOINTMENT_POLICIES", "AUDIT_LOGS"],
            result.AllowedModules.Select(m => m.ModuleCode).ToArray());

        // Role summaries assertions
        Assert.Equal(3, result.RoleSummaries.Count);
        var sysAdminSummary = Assert.Single(result.RoleSummaries, r => r.Code == "SYSTEM_ADMIN");
        Assert.Equal(4, sysAdminSummary.AssignedUserCount);
        Assert.Equal(2, sysAdminSummary.ActiveUserCount);
        Assert.Equal(3, sysAdminSummary.PermissionCount);

        // Recent audit logs ordered descending by OccurredAt
        Assert.Equal(5, result.RecentAuditLogs.Count);
        Assert.Equal([1L, 2L, 3L, 4L, 5L], result.RecentAuditLogs.Select(a => a.Id).ToArray());
        Assert.Equal("system_admin01@medflow.test", result.RecentAuditLogs[0].ActorEmail);
    }

    [Fact]
    public async Task GetDashboardAsync_RestrictedPermissions_ReturnsOnlyPermittedModules()
    {
        var toRemove = _dbContext.RolePermissions
            .Where(rp => rp.RoleId == 1 && (rp.PermissionId == 2 || rp.PermissionId == 3))
            .ToList();
        _dbContext.RolePermissions.RemoveRange(toRemove);
        await _dbContext.SaveChangesAsync();

        var result = await _service.GetDashboardAsync(13, new GetSystemAdminDashboardRequest());

        Assert.Equal(["USER_MANAGE"], result.AdminProfile.Permissions);
        Assert.Equal(2, result.AllowedModules.Count);
        Assert.Equal(
            ["STAFF_ACCOUNTS", "ROLES_PERMISSIONS"],
            result.AllowedModules.Select(m => m.ModuleCode).ToArray());
    }

    [Fact]
    public async Task GetDashboardAsync_WithHospitalIdFilter_ScopesStaffDepartmentAndAuditMetrics()
    {
        var result = await _service.GetDashboardAsync(13, new GetSystemAdminDashboardRequest(RecentAuditLimit: 10, HospitalId: 1));

        Assert.Equal(1, result.AdminProfile.HospitalId);
        Assert.Equal("Bệnh viện Trung ương MedFlow", result.AdminProfile.HospitalName);
        Assert.Equal(4, result.Metrics.TotalStaffAccounts);
        Assert.Equal(2, result.Metrics.ActiveStaffAccounts);
        Assert.Equal(2, result.Metrics.InactiveOrLockedStaffAccounts);
        Assert.Equal(2, result.Metrics.ActiveDepartments);
        Assert.Equal(4, result.Metrics.TotalAuditLogs);
        Assert.Equal(3, result.Metrics.RecentAuditCount24h);
        Assert.All(result.RecentAuditLogs, log => Assert.Equal(1, log.HospitalId));
    }

    [Fact]
    public async Task GetDashboardAsync_RecentAuditLimitApplied_ReturnsRequestedCount()
    {
        var result = await _service.GetDashboardAsync(13, new GetSystemAdminDashboardRequest(RecentAuditLimit: 2));

        Assert.Equal(2, result.RecentAuditLogs.Count);
        Assert.Equal(1L, result.RecentAuditLogs[0].Id);
        Assert.Equal(2L, result.RecentAuditLogs[1].Id);
        Assert.True(result.RecentAuditLogs[0].OccurredAt >= result.RecentAuditLogs[1].OccurredAt);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(51)]
    public async Task GetDashboardAsync_InvalidRecentAuditLimit_ThrowsValidationException(int invalidLimit)
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.GetDashboardAsync(13, new GetSystemAdminDashboardRequest(RecentAuditLimit: invalidLimit)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task GetDashboardAsync_InvalidHospitalId_ThrowsValidationException(long invalidHospitalId)
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.GetDashboardAsync(13, new GetSystemAdminDashboardRequest(RecentAuditLimit: 10, HospitalId: invalidHospitalId)));
    }

    [Fact]
    public async Task GetDashboardAsync_NonExistentHospitalId_ThrowsNotFoundException()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.GetDashboardAsync(13, new GetSystemAdminDashboardRequest(RecentAuditLimit: 10, HospitalId: 999)));
    }

    [Fact]
    public async Task GetDashboardAsync_UserNotFound_ThrowsNotFoundException()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.GetDashboardAsync(9999, new GetSystemAdminDashboardRequest()));
    }

    [Fact]
    public async Task GetDashboardAsync_NonSystemAdminUser_ThrowsForbiddenException()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetDashboardAsync(11, new GetSystemAdminDashboardRequest()));
    }

    [Theory]
    [InlineData(14)]
    [InlineData(16)]
    public async Task GetDashboardAsync_InactiveOrLockedUser_ThrowsForbiddenException(long actorUserId)
    {
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetDashboardAsync(actorUserId, new GetSystemAdminDashboardRequest()));
    }

    [Fact]
    public async Task GetDashboardAsync_InactiveStaffProfile_ThrowsForbiddenException()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetDashboardAsync(15, new GetSystemAdminDashboardRequest()));
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
