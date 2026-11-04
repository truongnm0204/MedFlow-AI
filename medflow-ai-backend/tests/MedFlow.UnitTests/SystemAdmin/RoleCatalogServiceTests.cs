using MedFlow.Application.DTOs.SystemAdmin;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.SystemAdmin;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.UnitTests.SystemAdmin;

public sealed class RoleCatalogServiceTests : IDisposable
{
    private sealed class FixedTimeProvider(DateTimeOffset nowUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => nowUtc;
    }

    private sealed class RoleCatalogTestDbContext : DbContext, IApplicationDbContext
    {
        public RoleCatalogTestDbContext(DbContextOptions<RoleCatalogTestDbContext> options)
            : base(options)
        {
        }

        private static NotSupportedException NotUsed() =>
            new("Not needed for RoleCatalogService unit tests.");

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

            modelBuilder.Entity<User>().HasKey(u => u.Id);
            modelBuilder.Entity<Role>().HasKey(r => r.Id);
            modelBuilder.Entity<Permission>().HasKey(p => p.Id);
            modelBuilder.Entity<UserRole>().HasKey(ur => new { ur.UserId, ur.RoleId });
            modelBuilder.Entity<RolePermission>().HasKey(rp => new { rp.RoleId, rp.PermissionId });
            modelBuilder.Entity<Staff>().HasKey(s => s.Id);
            modelBuilder.Entity<AuditLog>().HasKey(a => a.Id);
            modelBuilder.Entity<AuditLog>().Ignore(a => a.IpAddress);
        }
    }

    private static readonly DateTime FixedNowUtc = new(2026, 4, 20, 10, 0, 0, DateTimeKind.Utc);

    private readonly RoleCatalogTestDbContext _dbContext;
    private readonly RoleCatalogService _service;

    public RoleCatalogServiceTests()
    {
        var options = new DbContextOptionsBuilder<RoleCatalogTestDbContext>()
            .UseInMemoryDatabase(databaseName: $"RoleCatalogTests_{Guid.NewGuid()}")
            .Options;

        _dbContext = new RoleCatalogTestDbContext(options);
        SeedData(_dbContext);

        var timeProvider = new FixedTimeProvider(new DateTimeOffset(FixedNowUtc));
        _service = new RoleCatalogService(_dbContext, timeProvider);
    }

    private static void SeedData(RoleCatalogTestDbContext db)
    {
        db.Roles.AddRange(
            new Role { Id = 1, Code = "SYSTEM_ADMIN", Name = "Quản trị hệ thống" },
            new Role { Id = 2, Code = "HOSPITAL_ADMIN", Name = "Quản trị bệnh viện" },
            new Role { Id = 3, Code = "DOCTOR", Name = "Bác sĩ lâm sàng" },
            new Role { Id = 4, Code = "RECEPTION", Name = "Nhân viên tiếp đón" },
            new Role { Id = 5, Code = "DIAGNOSTIC_STAFF", Name = "Kỹ thuật viên CLS" },
            new Role { Id = 6, Code = "PHARMACY_STAFF", Name = "Dược sĩ cấp phát" },
            new Role { Id = 7, Code = "PATIENT", Name = "Bệnh nhân" },
            new Role { Id = 8, Code = "SECURITY_AUDITOR", Name = "Kiểm toán viên an ninh" });

        db.Permissions.AddRange(
            new Permission { Id = 1, Code = "USER_MANAGE" },
            new Permission { Id = 2, Code = "HOSPITAL_CONFIG" },
            new Permission { Id = 3, Code = "AUDIT_READ" },
            new Permission { Id = 4, Code = "MEDICAL_RECORD_WRITE" },
            new Permission { Id = 5, Code = "DIAGNOSTIC_ORDER" },
            new Permission { Id = 6, Code = "PRESCRIPTION_ISSUE" },
            new Permission { Id = 7, Code = "PATIENT_CHECKIN" },
            new Permission { Id = 8, Code = "DIAGNOSTIC_RESULT_WRITE" },
            new Permission { Id = 9, Code = "MEDICINE_DISPENSE" },
            new Permission { Id = 10, Code = "PATIENT_PROFILE_READ" },
            new Permission { Id = 11, Code = "APPOINTMENT_BOOK" });

        db.RolePermissions.AddRange(
            new RolePermission { RoleId = 1, PermissionId = 1 },
            new RolePermission { RoleId = 1, PermissionId = 2 },
            new RolePermission { RoleId = 1, PermissionId = 3 },
            new RolePermission { RoleId = 2, PermissionId = 2 },
            new RolePermission { RoleId = 3, PermissionId = 4 },
            new RolePermission { RoleId = 3, PermissionId = 5 },
            new RolePermission { RoleId = 3, PermissionId = 6 },
            new RolePermission { RoleId = 3, PermissionId = 10 },
            new RolePermission { RoleId = 4, PermissionId = 7 },
            new RolePermission { RoleId = 4, PermissionId = 10 },
            new RolePermission { RoleId = 5, PermissionId = 8 },
            new RolePermission { RoleId = 6, PermissionId = 9 },
            new RolePermission { RoleId = 7, PermissionId = 10 },
            new RolePermission { RoleId = 7, PermissionId = 11 },
            new RolePermission { RoleId = 8, PermissionId = 3 });

        db.Users.AddRange(
            new User
            {
                Id = 13,
                Email = "system_admin01@medflow.test",
                AccountStatus = AccountStatus.ACTIVE,
            },
            new User
            {
                Id = 11,
                Email = "hospital_admin01@medflow.test",
                AccountStatus = AccountStatus.ACTIVE,
            },
            new User
            {
                Id = 3,
                Email = "doctor01@medflow.test",
                AccountStatus = AccountStatus.ACTIVE,
            },
            new User
            {
                Id = 4,
                Email = "doctor_locked@medflow.test",
                AccountStatus = AccountStatus.ACTIVE,
                LockedUntil = FixedNowUtc.AddMinutes(30),
            },
            new User
            {
                Id = 14,
                Email = "admin_inactive@medflow.test",
                AccountStatus = AccountStatus.INACTIVE,
            },
            new User
            {
                Id = 15,
                Email = "admin_locked@medflow.test",
                AccountStatus = AccountStatus.ACTIVE,
                LockedUntil = FixedNowUtc.AddMinutes(15),
            },
            new User
            {
                Id = 16,
                Email = "admin_inactive_staff@medflow.test",
                AccountStatus = AccountStatus.ACTIVE,
            });

        db.UserRoles.AddRange(
            new UserRole { UserId = 13, RoleId = 1 },
            new UserRole { UserId = 14, RoleId = 1 },
            new UserRole { UserId = 15, RoleId = 1 },
            new UserRole { UserId = 16, RoleId = 1 },
            new UserRole { UserId = 11, RoleId = 2 },
            new UserRole { UserId = 3, RoleId = 3 },
            new UserRole { UserId = 4, RoleId = 3 });

        db.Staff.AddRange(
            new Staff
            {
                Id = 1,
                UserId = 13,
                HospitalId = 1,
                StaffCode = "STF-011",
                FullName = "Quản trị viên Hệ thống",
                Status = "ACTIVE",
            },
            new Staff
            {
                Id = 2,
                UserId = 16,
                HospitalId = 1,
                StaffCode = "STF-016",
                FullName = "Quản trị viên Ngừng việc",
                Status = "INACTIVE",
            });

        db.SaveChanges();
    }

    [Fact]
    public async Task GetRoleListAsync_ValidSystemAdmin_ReturnsAllRolesMetricsAndMappedPermissions()
    {
        var result = await _service.GetRoleListAsync(13, new GetRoleListRequest(Page: 1, PageSize: 20));

        Assert.Equal(8, result.Total);
        Assert.Equal(8, result.Data.Count);
        Assert.Equal(8, result.Items.Count);
        Assert.Equal(1, result.Page);
        Assert.Equal(20, result.PageSize);
        Assert.Equal(1, result.TotalPages);

        Assert.Equal(8, result.Metrics.TotalRoles);
        Assert.Equal(7, result.Metrics.ActiveRoles);
        Assert.Equal(1, result.Metrics.InactiveRoles);
        Assert.Equal(7, result.Metrics.SystemRoles);
        Assert.Equal(1, result.Metrics.CustomRoles);
        Assert.Equal(7, result.Metrics.TotalAssignedAccounts);
        Assert.Equal(4, result.Metrics.ActiveAssignedAccounts);
        Assert.Equal(11, result.Metrics.TotalPermissions);

        var doctorRole = Assert.Single(result.Data, r => r.Code == "DOCTOR");
        Assert.Equal("ROL-03", doctorRole.Id);
        Assert.Equal("CLINICAL", doctorRole.Scope);
        Assert.True(doctorRole.IsSystemRole);
        Assert.Equal("ACTIVE", doctorRole.Status);
        Assert.Equal(2, doctorRole.AssignedUsersCount);
        Assert.Equal(1, doctorRole.ActiveUsersCount);
        Assert.Equal(4, doctorRole.PermissionCount);

        var medRecordPerm = Assert.Single(doctorRole.Permissions, p => p.Code == "MEDICAL_RECORD_WRITE");
        Assert.Equal("Ghi nhận bệnh án lâm sàng", medRecordPerm.Name);
        Assert.Equal("Khám lâm sàng", medRecordPerm.Module);
    }

    [Fact]
    public async Task GetRoleListAsync_KeywordOrQueryFilter_MatchesCodeNameDescriptionAndPermission()
    {
        var byQueryAndScope = await _service.GetRoleListAsync(
            13,
            new GetRoleListRequest(Scope: "CLINICAL", Query: "MEDICAL_RECORD_WRITE"));

        var matchedRole = Assert.Single(byQueryAndScope.Data);
        Assert.Equal("DOCTOR", matchedRole.Code);
        var perm = Assert.Single(matchedRole.Permissions, p => p.Code == "MEDICAL_RECORD_WRITE");
        Assert.Equal("Ghi nhận bệnh án lâm sàng", perm.Name);
        Assert.Equal("Khám lâm sàng", perm.Module);
        Assert.Equal(8, byQueryAndScope.Metrics.TotalRoles);

        var byKeyword = await _service.GetRoleListAsync(
            13,
            new GetRoleListRequest(Keyword: "Kê đơn thuốc điện tử"));

        var matchedByPermName = Assert.Single(byKeyword.Data);
        Assert.Equal("DOCTOR", matchedByPermName.Code);
    }

    [Fact]
    public async Task GetRoleListAsync_ScopeFilter_ReturnsOnlyMatchingScope()
    {
        var result = await _service.GetRoleListAsync(
            13,
            new GetRoleListRequest(Scope: "clinical"));

        Assert.Equal(3, result.Total);
        Assert.All(result.Data, r => Assert.Equal("CLINICAL", r.Scope));
        Assert.Contains(result.Data, r => r.Code == "DOCTOR");
        Assert.Contains(result.Data, r => r.Code == "DIAGNOSTIC_STAFF");
        Assert.Contains(result.Data, r => r.Code == "PHARMACY_STAFF");
    }

    [Fact]
    public async Task GetRoleListAsync_StatusAndIsSystemRoleFilter_ReturnsMatchingRoles()
    {
        var inactiveCustom = await _service.GetRoleListAsync(
            13,
            new GetRoleListRequest(Status: "INACTIVE", IsSystemRole: false));

        var role = Assert.Single(inactiveCustom.Data);
        Assert.Equal("SECURITY_AUDITOR", role.Code);
        Assert.Equal("INACTIVE", role.Status);
        Assert.False(role.IsSystemRole);
    }

    [Fact]
    public async Task GetRoleListAsync_Pagination_ReturnsRequestedPageAndTotalPages()
    {
        var page2 = await _service.GetRoleListAsync(
            13,
            new GetRoleListRequest(Page: 2, PageSize: 3));

        Assert.Equal(8, page2.Total);
        Assert.Equal(2, page2.Page);
        Assert.Equal(3, page2.PageSize);
        Assert.Equal(3, page2.TotalPages);
        Assert.Equal(3, page2.Data.Count);
        Assert.Equal(new[] { "RECEPTION", "DIAGNOSTIC_STAFF", "PHARMACY_STAFF" }, page2.Data.Select(r => r.Code).ToArray());
    }

    [Fact]
    public async Task GetRoleListAsync_RoleAuditLogExists_UsesLatestAuditDateForUpdatedAt()
    {
        _dbContext.AuditLogs.AddRange(
            new AuditLog
            {
                Id = 100,
                ActorUserId = 13,
                Action = "ROLE_PERMISSIONS_UPDATED",
                EntityType = "Role",
                EntityId = "3",
                OccurredAt = new DateTime(2026, 4, 10, 8, 0, 0, DateTimeKind.Utc),
            },
            new AuditLog
            {
                Id = 101,
                ActorUserId = 13,
                Action = "ROLE_PERMISSIONS_UPDATED",
                EntityType = "Role",
                EntityId = "DOCTOR",
                OccurredAt = new DateTime(2026, 4, 19, 14, 30, 0, DateTimeKind.Utc),
            });
        await _dbContext.SaveChangesAsync();

        var result = await _service.GetRoleListAsync(13, new GetRoleListRequest(Keyword: "DOCTOR"));

        var doctorRole = Assert.Single(result.Data);
        Assert.Equal("19/04/2026", doctorRole.UpdatedAt);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(-1, 10)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task GetRoleListAsync_InvalidPageOrPageSize_ThrowsValidationException(int page, int pageSize)
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.GetRoleListAsync(13, new GetRoleListRequest(Page: page, PageSize: pageSize)));
    }

    [Theory]
    [InlineData("INVALID_SCOPE", null)]
    [InlineData(null, "ARCHIVED")]
    public async Task GetRoleListAsync_InvalidScopeOrStatus_ThrowsValidationException(string? scope, string? status)
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.GetRoleListAsync(13, new GetRoleListRequest(Scope: scope, Status: status)));
    }

    [Fact]
    public async Task GetRoleListAsync_UserNotFound_ThrowsNotFoundException()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.GetRoleListAsync(999, new GetRoleListRequest()));
    }

    [Fact]
    public async Task GetRoleListAsync_NonSystemAdminUser_ThrowsForbiddenException()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetRoleListAsync(11, new GetRoleListRequest()));
    }

    [Theory]
    [InlineData(14)]
    [InlineData(15)]
    public async Task GetRoleListAsync_InactiveOrLockedUser_ThrowsForbiddenException(long userId)
    {
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetRoleListAsync(userId, new GetRoleListRequest()));
    }

    [Fact]
    public async Task GetRoleListAsync_InactiveStaffProfile_ThrowsForbiddenException()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetRoleListAsync(16, new GetRoleListRequest()));
    }

    [Fact]
    public async Task GetRoleByIdAsync_ExistingRole_ReturnsRoleDetail()
    {
        var role = await _service.GetRoleByIdAsync(13, 1);

        Assert.Equal(1, role.RoleId);
        Assert.Equal("ROL-01", role.Id);
        Assert.Equal("SYSTEM_ADMIN", role.Code);
        Assert.Equal("SYSTEM", role.Scope);
        Assert.True(role.IsSystemRole);
        Assert.Equal(3, role.PermissionCount);
    }

    [Fact]
    public async Task GetRoleByIdAsync_NonExistentRole_ThrowsNotFoundException()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.GetRoleByIdAsync(13, 999));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task GetRoleByIdAsync_InvalidRoleId_ThrowsValidationException(long roleId)
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.GetRoleByIdAsync(13, roleId));
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
