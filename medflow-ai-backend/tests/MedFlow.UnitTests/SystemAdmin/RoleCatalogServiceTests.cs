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

    [Fact]
    public async Task GetRolePermissionsCatalogAsync_ValidSystemAdmin_ReturnsRolesCanonicalPermissionsAndMetrics()
    {
        var catalog = await _service.GetRolePermissionsCatalogAsync(13);

        Assert.Equal(8, catalog.Roles.Count);
        Assert.Equal(11, catalog.Permissions.Count);
        Assert.Equal(8, catalog.Metrics.TotalRoles);
        Assert.Equal(11, catalog.Metrics.TotalPermissions);
        Assert.Equal("PATIENT_PROFILE_READ", catalog.Permissions[0].Code);
        Assert.Equal("Xem hồ sơ bệnh nhân", catalog.Permissions[0].Name);
    }

    [Fact]
    public async Task AssignRolePermissionsAsync_ByFormattedRoleId_UpdatesRolePermissionsAuditLogAndUpdatedAt()
    {
        var updatedRole = await _service.AssignRolePermissionsAsync(
            13,
            "ROL-03",
            new AssignRolePermissionsRequest(
                RoleCode: "DOCTOR",
                PermissionCodes: ["PATIENT_PROFILE_READ", "MEDICAL_RECORD_WRITE", "DIAGNOSTIC_ORDER"],
                Note: "Thu hồi tạm thời quyền kê đơn thuốc điện tử"),
            "10.0.0.15");

        Assert.Equal(3, updatedRole.RoleId);
        Assert.Equal("ROL-03", updatedRole.Id);
        Assert.Equal("DOCTOR", updatedRole.Code);
        Assert.Equal(3, updatedRole.PermissionCount);
        Assert.Equal(3, updatedRole.Permissions.Count);
        Assert.DoesNotContain(updatedRole.Permissions, p => p.Code == "PRESCRIPTION_ISSUE");
        Assert.Equal("20/04/2026", updatedRole.UpdatedAt);

        var dbRolePermIds = await _dbContext.RolePermissions
            .Where(rp => rp.RoleId == 3)
            .Select(rp => rp.PermissionId)
            .OrderBy(id => id)
            .ToListAsync();
        Assert.Equal(new long[] { 4, 5, 10 }, dbRolePermIds);

        var auditLog = await _dbContext.AuditLogs
            .SingleAsync(a => a.Action == "ROLE_PERMISSIONS_UPDATED");
        Assert.Equal(13, auditLog.ActorUserId);
        Assert.Equal(1, auditLog.HospitalId);
        Assert.Equal("Role", auditLog.EntityType);
        Assert.Equal("3", auditLog.EntityId);
        Assert.Equal("Thu hồi tạm thời quyền kê đơn thuốc điện tử", auditLog.Reason);
        Assert.Equal(FixedNowUtc, auditLog.OccurredAt);
        Assert.NotNull(auditLog.ChangeData);
        Assert.Contains("PRESCRIPTION_ISSUE", auditLog.ChangeData);
    }

    [Fact]
    public async Task AssignRolePermissionsAsync_ByNumericIdAndRoleCode_DeduplicatesAndPersistsPermissions()
    {
        var afterAdd = await _service.AssignRolePermissionsAsync(
            13,
            "4",
            new AssignRolePermissionsRequest(
                PermissionCodes: ["patient_checkin", "PATIENT_PROFILE_READ", "PATIENT_CHECKIN", "APPOINTMENT_BOOK"]));

        Assert.Equal(4, afterAdd.RoleId);
        Assert.Equal("RECEPTION", afterAdd.Code);
        Assert.Equal(3, afterAdd.PermissionCount);
        Assert.Contains(afterAdd.Permissions, p => p.Code == "APPOINTMENT_BOOK");

        var afterRevoke = await _service.AssignRolePermissionsAsync(
            13,
            "RECEPTION",
            new AssignRolePermissionsRequest(
                RoleCode: "RECEPTION",
                PermissionCodes: ["PATIENT_CHECKIN"]));

        Assert.Equal(1, afterRevoke.PermissionCount);
        var singlePerm = Assert.Single(afterRevoke.Permissions);
        Assert.Equal("PATIENT_CHECKIN", singlePerm.Code);
    }

    [Fact]
    public async Task AssignRolePermissionsAsync_NullOrEmptyPermissionCodes_ThrowsValidationException()
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.AssignRolePermissionsAsync(
                13,
                "ROL-03",
                new AssignRolePermissionsRequest(RoleCode: "DOCTOR", PermissionCodes: null)));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.AssignRolePermissionsAsync(
                13,
                "ROL-03",
                new AssignRolePermissionsRequest(RoleCode: "DOCTOR", PermissionCodes: [])));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("NON_EXISTENT_PERMISSION")]
    public async Task AssignRolePermissionsAsync_UnknownOrWhitespacePermissionCode_ThrowsValidationException(
        string invalidCode)
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.AssignRolePermissionsAsync(
                13,
                "ROL-03",
                new AssignRolePermissionsRequest(
                    RoleCode: "DOCTOR",
                    PermissionCodes: ["PATIENT_PROFILE_READ", invalidCode])));
    }

    [Theory]
    [InlineData("ROL-01", "SYSTEM_ADMIN")]
    [InlineData("1", null)]
    [InlineData("SYSTEM_ADMIN", null)]
    public async Task AssignRolePermissionsAsync_SystemAdminMissingUserManage_ThrowsValidationException(
        string roleIdentifier,
        string? bodyRoleCode)
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _service.AssignRolePermissionsAsync(
                13,
                roleIdentifier,
                new AssignRolePermissionsRequest(
                    RoleCode: bodyRoleCode,
                    PermissionCodes: ["HOSPITAL_CONFIG", "AUDIT_READ"])));

        Assert.Contains("USER_MANAGE", ex.Message);
    }

    [Fact]
    public async Task AssignRolePermissionsAsync_SegregationOfDutiesConflict_ThrowsValidationException()
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _service.AssignRolePermissionsAsync(
                13,
                "ROL-03",
                new AssignRolePermissionsRequest(
                    RoleCode: "DOCTOR",
                    PermissionCodes: ["PATIENT_PROFILE_READ", "PRESCRIPTION_ISSUE", "MEDICINE_DISPENSE"])));

        Assert.Contains("SoD", ex.Message);
    }

    [Fact]
    public async Task AssignRolePermissionsAsync_NoteExceedsMaxLength_ThrowsValidationException()
    {
        var tooLongNote = new string('N', 251);

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.AssignRolePermissionsAsync(
                13,
                "ROL-03",
                new AssignRolePermissionsRequest(
                    RoleCode: "DOCTOR",
                    PermissionCodes: ["PATIENT_PROFILE_READ"],
                    Note: tooLongNote)));
    }

    [Theory]
    [InlineData("ROL-03", "RECEPTION")]
    [InlineData("DOCTOR", "RECEPTION")]
    [InlineData("ROL-03", "   ")]
    public async Task AssignRolePermissionsAsync_InvalidOrMismatchedRoleCode_ThrowsValidationException(
        string roleIdentifier,
        string bodyRoleCode)
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.AssignRolePermissionsAsync(
                13,
                roleIdentifier,
                new AssignRolePermissionsRequest(
                    RoleCode: bodyRoleCode,
                    PermissionCodes: ["PATIENT_PROFILE_READ"])));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("ROL-0")]
    [InlineData("ROL--3")]
    [InlineData("ROL-abc")]
    [InlineData("1INVALID")]
    [InlineData("@@@")]
    public async Task AssignRolePermissionsAsync_InvalidRoleIdentifier_ThrowsValidationException(
        string invalidIdentifier)
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.AssignRolePermissionsAsync(
                13,
                invalidIdentifier,
                new AssignRolePermissionsRequest(PermissionCodes: ["PATIENT_PROFILE_READ"])));
    }

    [Theory]
    [InlineData("999")]
    [InlineData("ROL-99")]
    [InlineData("UNKNOWN_CUSTOM_ROLE")]
    public async Task AssignRolePermissionsAsync_NonExistentRole_ThrowsNotFoundException(
        string roleIdentifier)
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.AssignRolePermissionsAsync(
                13,
                roleIdentifier,
                new AssignRolePermissionsRequest(PermissionCodes: ["PATIENT_PROFILE_READ"])));
    }

    [Fact]
    public async Task AssignRolePermissionsAsync_ActorUserNotFound_ThrowsNotFoundException()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.AssignRolePermissionsAsync(
                999,
                "ROL-03",
                new AssignRolePermissionsRequest(PermissionCodes: ["PATIENT_PROFILE_READ"])));
    }

    [Theory]
    [InlineData(11)]
    [InlineData(14)]
    [InlineData(15)]
    [InlineData(16)]
    public async Task AssignRolePermissionsAsync_ForbiddenOrInactiveActor_ThrowsForbiddenException(long actorUserId)
    {
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.AssignRolePermissionsAsync(
                actorUserId,
                "ROL-03",
                new AssignRolePermissionsRequest(PermissionCodes: ["PATIENT_PROFILE_READ"])));
    }

    [Fact]
    public async Task UpdateRoleAsync_CustomRoleFullUpdate_PersistsCodeNameMetadataPermissionsAndAuditLog()
    {
        var result = await _service.UpdateRoleAsync(
            13,
            "ROL-08",
            new UpdateRoleRequest(
                Code: "compliance_auditor",
                Name: "Kiểm toán viên tuân thủ",
                Scope: "OPERATIONAL",
                Status: "ACTIVE",
                Description: "Kiểm toán tuân thủ quy trình vận hành.",
                SegregationNote: "Chỉ đọc dữ liệu vận hành.",
                PermissionCodes: ["AUDIT_READ", "PATIENT_PROFILE_READ"],
                Note: "Kích hoạt và mở rộng phạm vi kiểm toán"),
            "10.0.0.20");

        Assert.Equal(8, result.RoleId);
        Assert.Equal("ROL-08", result.Id);
        Assert.Equal("COMPLIANCE_AUDITOR", result.Code);
        Assert.Equal("Kiểm toán viên tuân thủ", result.Name);
        Assert.Equal("OPERATIONAL", result.Scope);
        Assert.Equal("ACTIVE", result.Status);
        Assert.False(result.IsSystemRole);
        Assert.Equal("Kiểm toán tuân thủ quy trình vận hành.", result.Description);
        Assert.Equal("Chỉ đọc dữ liệu vận hành.", result.SegregationNote);
        Assert.Equal("20/04/2026", result.UpdatedAt);
        Assert.Equal(2, result.PermissionCount);
        Assert.Equal(
            new[] { "AUDIT_READ", "PATIENT_PROFILE_READ" },
            result.Permissions.Select(p => p.Code));

        var dbRole = await _dbContext.Roles.SingleAsync(r => r.Id == 8);
        Assert.Equal("COMPLIANCE_AUDITOR", dbRole.Code);
        Assert.Equal("Kiểm toán viên tuân thủ", dbRole.Name);

        var dbRolePermissions = await _dbContext.RolePermissions
            .Where(rp => rp.RoleId == 8)
            .ToListAsync();
        Assert.Equal(2, dbRolePermissions.Count);

        var audit = await _dbContext.AuditLogs
            .SingleAsync(a => a.EntityType == "Role" && a.Action == "ROLE_UPDATED" && a.EntityId == "8");
        Assert.Equal(13, audit.ActorUserId);
        Assert.Equal(1, audit.HospitalId);
        Assert.Equal("Kích hoạt và mở rộng phạm vi kiểm toán", audit.Reason);
        Assert.Equal(FixedNowUtc, audit.OccurredAt);
        Assert.NotNull(audit.ChangeData);
        Assert.Contains("COMPLIANCE_AUDITOR", audit.ChangeData);
        Assert.Contains("SECURITY_AUDITOR", audit.ChangeData);
        Assert.Contains("OPERATIONAL", audit.ChangeData);
        Assert.Contains("ACTIVE", audit.ChangeData);

        // Verify subsequent read and permission assignment preserve updated metadata
        var byId = await _service.GetRoleByIdAsync(13, 8);
        Assert.Equal("COMPLIANCE_AUDITOR", byId.Code);
        Assert.Equal("Kiểm toán viên tuân thủ", byId.Name);
        Assert.Equal("OPERATIONAL", byId.Scope);
        Assert.Equal("ACTIVE", byId.Status);
        Assert.Equal("Kiểm toán tuân thủ quy trình vận hành.", byId.Description);
        Assert.Equal("Chỉ đọc dữ liệu vận hành.", byId.SegregationNote);

        var afterPermUpdate = await _service.AssignRolePermissionsAsync(
            13,
            "ROL-08",
            new AssignRolePermissionsRequest(PermissionCodes: ["AUDIT_READ"]));
        Assert.Equal("COMPLIANCE_AUDITOR", afterPermUpdate.Code);
        Assert.Equal("Kiểm toán viên tuân thủ", afterPermUpdate.Name);
        Assert.Equal("OPERATIONAL", afterPermUpdate.Scope);
        Assert.Equal("ACTIVE", afterPermUpdate.Status);
        Assert.Equal("Kiểm toán tuân thủ quy trình vận hành.", afterPermUpdate.Description);
        Assert.Equal("Chỉ đọc dữ liệu vận hành.", afterPermUpdate.SegregationNote);
        Assert.Equal(1, afterPermUpdate.PermissionCount);
    }

    [Fact]
    public async Task UpdateRoleAsync_SystemRoleMetadataUpdate_UpdatesNameDescriptionScopeAndSegregationNoteWhilePreservingPermissions()
    {
        var result = await _service.UpdateRoleAsync(
            13,
            "ROL-03",
            new UpdateRoleRequest(
                Name: "Bác sĩ điều trị chính",
                Description: "Khám lâm sàng, kê đơn và điều phối chuyên môn.",
                Scope: "CLINICAL",
                SegregationNote: "Tách biệt hoàn toàn với cấp phát dược.",
                Status: "ACTIVE",
                Reason: "Chuẩn hóa mô tả chức danh bác sĩ"));

        Assert.Equal(3, result.RoleId);
        Assert.Equal("DOCTOR", result.Code);
        Assert.True(result.IsSystemRole);
        Assert.Equal("Bác sĩ điều trị chính", result.Name);
        Assert.Equal("Khám lâm sàng, kê đơn và điều phối chuyên môn.", result.Description);
        Assert.Equal("Tách biệt hoàn toàn với cấp phát dược.", result.SegregationNote);
        Assert.Equal("CLINICAL", result.Scope);
        Assert.Equal("ACTIVE", result.Status);
        Assert.Equal(4, result.PermissionCount);
    }

    [Fact]
    public async Task UpdateRoleAsync_SystemRoleCodeChange_ThrowsValidationException()
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateRoleAsync(
                13,
                "ROL-03",
                new UpdateRoleRequest(Code: "CLINICAL_DOCTOR")));

        Assert.Contains("code", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("ROL-01", "INACTIVE", null)]
    [InlineData("SYSTEM_ADMIN", null, false)]
    [InlineData("ROL-03", "INACTIVE", null)]
    [InlineData("DOCTOR", null, false)]
    public async Task UpdateRoleAsync_DeactivateSystemRole_ThrowsValidationException(
        string roleIdentifier,
        string? status,
        bool? isActive)
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateRoleAsync(
                13,
                roleIdentifier,
                new UpdateRoleRequest(Status: status, IsActive: isActive)));
    }

    [Fact]
    public async Task UpdateRoleAsync_SystemAdminInvalidScopeOrMissingUserManage_ThrowsValidationException()
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateRoleAsync(
                13,
                "ROL-01",
                new UpdateRoleRequest(Scope: "OPERATIONAL")));

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateRoleAsync(
                13,
                "ROL-01",
                new UpdateRoleRequest(PermissionCodes: ["HOSPITAL_CONFIG", "AUDIT_READ"])));

        Assert.Contains("USER_MANAGE", ex.Message);
    }

    [Fact]
    public async Task UpdateRoleAsync_DuplicateCodeOrDuplicateName_ThrowsConflictException()
    {
        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.UpdateRoleAsync(
                13,
                "ROL-08",
                new UpdateRoleRequest(Code: "DOCTOR")));

        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.UpdateRoleAsync(
                13,
                "ROL-08",
                new UpdateRoleRequest(Name: "Bác sĩ lâm sàng")));
    }

    [Fact]
    public async Task UpdateRoleAsync_EmptyPayloadOrInvalidFields_ThrowsValidationException()
    {
        // Empty payload
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateRoleAsync(13, "ROL-08", new UpdateRoleRequest()));

        // Invalid scope
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateRoleAsync(13, "ROL-08", new UpdateRoleRequest(Scope: "INVALID")));

        // Invalid status
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateRoleAsync(13, "ROL-08", new UpdateRoleRequest(Status: "DELETED")));

        // Contradicted status and isActive
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateRoleAsync(13, "ROL-08", new UpdateRoleRequest(Status: "ACTIVE", IsActive: false)));

        // Mismatched code and roleCode
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateRoleAsync(13, "ROL-08", new UpdateRoleRequest(Code: "CUSTOM_A", RoleCode: "CUSTOM_B")));

        // Invalid code token
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateRoleAsync(13, "ROL-08", new UpdateRoleRequest(Code: "1INVALID")));

        // Mismatched name and roleName
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateRoleAsync(13, "ROL-08", new UpdateRoleRequest(Name: "Vai trò A", RoleName: "Vai trò B")));

        // Name too short / too long
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateRoleAsync(13, "ROL-08", new UpdateRoleRequest(Name: "A")));
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateRoleAsync(13, "ROL-08", new UpdateRoleRequest(Name: new string('A', 121))));

        // Description whitespace or > 500 chars
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateRoleAsync(13, "ROL-08", new UpdateRoleRequest(Description: "   ")));
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateRoleAsync(13, "ROL-08", new UpdateRoleRequest(Description: new string('D', 501))));

        // SegregationNote whitespace or > 500 chars
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateRoleAsync(13, "ROL-08", new UpdateRoleRequest(SegregationNote: "   ")));
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateRoleAsync(13, "ROL-08", new UpdateRoleRequest(SegregationNote: new string('S', 501))));

        // Mismatched or too-long audit note/reason
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateRoleAsync(13, "ROL-08", new UpdateRoleRequest(Description: "Hợp lệ", Note: "Ghi chú 1", Reason: "Ghi chú 2")));
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateRoleAsync(13, "ROL-08", new UpdateRoleRequest(Description: "Hợp lệ", Note: new string('N', 251))));

        // Empty or unknown permission codes
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateRoleAsync(13, "ROL-08", new UpdateRoleRequest(PermissionCodes: [])));
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateRoleAsync(13, "ROL-08", new UpdateRoleRequest(PermissionCodes: ["UNKNOWN_PERM"])));

        // SoD conflict
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateRoleAsync(
                13,
                "ROL-08",
                new UpdateRoleRequest(PermissionCodes: ["PRESCRIPTION_ISSUE", "MEDICINE_DISPENSE"])));
    }

    [Fact]
    public async Task UpdateRoleAsync_NonExistentRoleOrInvalidIdentifier_ThrowsExpectedException()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.UpdateRoleAsync(13, "999", new UpdateRoleRequest(Description: "Mô tả mới")));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.UpdateRoleAsync(13, "ROL-99", new UpdateRoleRequest(Description: "Mô tả mới")));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.UpdateRoleAsync(13, "UNKNOWN_ROLE", new UpdateRoleRequest(Description: "Mô tả mới")));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateRoleAsync(13, "0", new UpdateRoleRequest(Description: "Mô tả mới")));
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateRoleAsync(13, "ROL-0", new UpdateRoleRequest(Description: "Mô tả mới")));
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateRoleAsync(13, "", new UpdateRoleRequest(Description: "Mô tả mới")));
    }

    [Fact]
    public async Task UpdateRoleAsync_UnauthorizedOrInactiveActor_ThrowsNotFoundOrForbiddenException()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.UpdateRoleAsync(999, "ROL-08", new UpdateRoleRequest(Description: "Mô tả mới")));

        foreach (var forbiddenActorId in new long[] { 11, 14, 15, 16 })
        {
            await Assert.ThrowsAsync<ForbiddenException>(() =>
                _service.UpdateRoleAsync(forbiddenActorId, "ROL-08", new UpdateRoleRequest(Description: "Mô tả mới")));
        }
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
