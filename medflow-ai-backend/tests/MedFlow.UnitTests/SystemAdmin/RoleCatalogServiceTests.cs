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
    public async Task CreateRoleAsync_ValidCustomRoleWithPermissions_PersistsRolePermissionsAuditLogAndMetadata()
    {
        var created = await _service.CreateRoleAsync(
            13,
            new CreateRoleRequest(
                Code: "  clinical_coordinator  ",
                Name: "  Điều phối viên lâm sàng  ",
                Description: "  Điều phối lịch và hàng đợi khám.  ",
                Scope: "clinical",
                Status: "active",
                SegregationNote: "  Không trực tiếp kê đơn thuốc.  ",
                PermissionCodes: ["PATIENT_CHECKIN", "patient_profile_read", "PATIENT_CHECKIN"],
                Note: "Tạo vai trò điều phối M2"),
            "10.0.0.25");

        Assert.Equal(9, created.RoleId);
        Assert.Equal("ROL-09", created.Id);
        Assert.Equal("CLINICAL_COORDINATOR", created.Code);
        Assert.Equal("Điều phối viên lâm sàng", created.Name);
        Assert.Equal("Điều phối lịch và hàng đợi khám.", created.Description);
        Assert.Equal("CLINICAL", created.Scope);
        Assert.False(created.IsSystemRole);
        Assert.Equal("ACTIVE", created.Status);
        Assert.Equal("Không trực tiếp kê đơn thuốc.", created.SegregationNote);
        Assert.Equal("20/04/2026", created.UpdatedAt);
        Assert.Equal(0, created.AssignedUsersCount);
        Assert.Equal(0, created.ActiveUsersCount);
        Assert.Equal(2, created.PermissionCount);
        Assert.Contains(created.Permissions, p => p.Code == "PATIENT_PROFILE_READ");
        Assert.Contains(created.Permissions, p => p.Code == "PATIENT_CHECKIN");

        var dbRole = await _dbContext.Roles.SingleAsync(r => r.Id == 9);
        Assert.Equal("CLINICAL_COORDINATOR", dbRole.Code);
        Assert.Equal("Điều phối viên lâm sàng", dbRole.Name);

        var dbPermIds = await _dbContext.RolePermissions
            .Where(rp => rp.RoleId == 9)
            .Select(rp => rp.PermissionId)
            .OrderBy(id => id)
            .ToListAsync();
        Assert.Equal(new long[] { 7, 10 }, dbPermIds);

        var auditLog = await _dbContext.AuditLogs
            .SingleAsync(a => a.Action == "ROLE_CREATED");
        Assert.Equal(13, auditLog.ActorUserId);
        Assert.Equal(1, auditLog.HospitalId);
        Assert.Equal("Role", auditLog.EntityType);
        Assert.Equal("9", auditLog.EntityId);
        Assert.Equal("Tạo vai trò điều phối M2", auditLog.Reason);
        Assert.Equal(FixedNowUtc, auditLog.OccurredAt);
        Assert.NotNull(auditLog.ChangeData);
        Assert.Contains("CLINICAL_COORDINATOR", auditLog.ChangeData);

        var byId = await _service.GetRoleByIdAsync(13, 9);
        Assert.Equal("CLINICAL", byId.Scope);
        Assert.Equal("Điều phối lịch và hàng đợi khám.", byId.Description);
        Assert.Equal("Không trực tiếp kê đơn thuốc.", byId.SegregationNote);

        var afterPermissionUpdate = await _service.AssignRolePermissionsAsync(
            13,
            "ROL-09",
            new AssignRolePermissionsRequest(
                RoleCode: "CLINICAL_COORDINATOR",
                PermissionCodes: ["PATIENT_PROFILE_READ"],
                Note: "Thu hẹp quyền điều phối"));
        Assert.Equal("CLINICAL", afterPermissionUpdate.Scope);
        Assert.Equal("Điều phối lịch và hàng đợi khám.", afterPermissionUpdate.Description);
        Assert.Equal("Không trực tiếp kê đơn thuốc.", afterPermissionUpdate.SegregationNote);
        Assert.Equal(1, afterPermissionUpdate.PermissionCount);
    }

    [Fact]
    public async Task CreateRoleAsync_WithoutPermissionsAndOptionalFields_UsesDefaultCustomRoleMetadata()
    {
        var created = await _service.CreateRoleAsync(
            13,
            new CreateRoleRequest(
                Code: "BILLING_REVIEWER",
                Name: "Chuyên viên đối soát viện phí"));

        Assert.Equal("BILLING_REVIEWER", created.Code);
        Assert.Equal("Chuyên viên đối soát viện phí", created.Name);
        Assert.Equal("OPERATIONAL", created.Scope);
        Assert.Equal("ACTIVE", created.Status);
        Assert.False(created.IsSystemRole);
        Assert.Equal(0, created.PermissionCount);
        Assert.Empty(created.Permissions);
        Assert.Equal("Vai trò cấu hình mở rộng: Chuyên viên đối soát viện phí.", created.Description);
        Assert.Equal("Tuân thủ nguyên tắc phân quyền tối thiểu (Least Privilege).", created.SegregationNote);
    }

    [Fact]
    public async Task CreateRoleAsync_RoleCodeAndRoleNameAliases_CreatesRoleSuccessfully()
    {
        var created = await _service.CreateRoleAsync(
            13,
            new CreateRoleRequest(
                RoleCode: "quality_inspector",
                RoleName: "Giám sát chất lượng",
                Scope: "SYSTEM",
                Status: "INACTIVE"));

        Assert.Equal("QUALITY_INSPECTOR", created.Code);
        Assert.Equal("Giám sát chất lượng", created.Name);
        Assert.Equal("SYSTEM", created.Scope);
        Assert.Equal("INACTIVE", created.Status);
        Assert.False(created.IsSystemRole);
    }

    [Theory]
    [InlineData("DOCTOR")]
    [InlineData("doctor")]
    [InlineData("SECURITY_AUDITOR")]
    public async Task CreateRoleAsync_DuplicateCodeOrReservedSystemRoleCode_ThrowsConflictException(string duplicateCode)
    {
        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.CreateRoleAsync(
                13,
                new CreateRoleRequest(
                    Code: duplicateCode,
                    Name: "Vai trò trùng mã")));
    }

    [Theory]
    [InlineData("Bác sĩ lâm sàng")]
    [InlineData("bác sĩ lâm sàng")]
    public async Task CreateRoleAsync_DuplicateRoleName_ThrowsConflictException(string duplicateName)
    {
        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.CreateRoleAsync(
                13,
                new CreateRoleRequest(
                    Code: "UNIQUE_CUSTOM_ROLE",
                    Name: duplicateName)));
    }

    [Theory]
    [InlineData(null, "Tên hợp lệ")]
    [InlineData("", "Tên hợp lệ")]
    [InlineData("   ", "Tên hợp lệ")]
    [InlineData("A", "Tên hợp lệ")]
    [InlineData("123_INVALID", "Tên hợp lệ")]
    [InlineData("ROLE-WITH-DASH", "Tên hợp lệ")]
    [InlineData("VALID_CODE", null)]
    [InlineData("VALID_CODE", "")]
    [InlineData("VALID_CODE", "   ")]
    [InlineData("VALID_CODE", "A")]
    public async Task CreateRoleAsync_InvalidCodeOrName_ThrowsValidationException(
        string? code,
        string? name)
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.CreateRoleAsync(
                13,
                new CreateRoleRequest(Code: code, Name: name)));
    }

    [Fact]
    public async Task CreateRoleAsync_MismatchedOrTooLongCodeAndName_ThrowsValidationException()
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.CreateRoleAsync(
                13,
                new CreateRoleRequest(
                    Code: "ROLE_ONE",
                    RoleCode: "ROLE_TWO",
                    Name: "Tên vai trò")));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.CreateRoleAsync(
                13,
                new CreateRoleRequest(
                    Code: "ROLE_ONE",
                    Name: "Tên A",
                    RoleName: "Tên B")));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.CreateRoleAsync(
                13,
                new CreateRoleRequest(
                    Code: new string('R', 51),
                    Name: "Tên vai trò")));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.CreateRoleAsync(
                13,
                new CreateRoleRequest(
                    Code: "ROLE_ONE",
                    Name: new string('N', 121))));
    }

    [Fact]
    public async Task CreateRoleAsync_InvalidScopeStatusOrLengths_ThrowsValidationException()
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.CreateRoleAsync(
                13,
                new CreateRoleRequest(
                    Code: "CUSTOM_ROLE_A",
                    Name: "Vai trò A",
                    Scope: "INVALID_SCOPE")));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.CreateRoleAsync(
                13,
                new CreateRoleRequest(
                    Code: "CUSTOM_ROLE_A",
                    Name: "Vai trò A",
                    Status: "ARCHIVED")));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.CreateRoleAsync(
                13,
                new CreateRoleRequest(
                    Code: "CUSTOM_ROLE_A",
                    Name: "Vai trò A",
                    Description: new string('D', 501))));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.CreateRoleAsync(
                13,
                new CreateRoleRequest(
                    Code: "CUSTOM_ROLE_A",
                    Name: "Vai trò A",
                    SegregationNote: new string('S', 501))));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.CreateRoleAsync(
                13,
                new CreateRoleRequest(
                    Code: "CUSTOM_ROLE_A",
                    Name: "Vai trò A",
                    Note: new string('N', 251))));
    }

    [Fact]
    public async Task CreateRoleAsync_UnknownPermissionOrSoDConflict_ThrowsValidationException()
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.CreateRoleAsync(
                13,
                new CreateRoleRequest(
                    Code: "CUSTOM_ROLE_B",
                    Name: "Vai trò B",
                    PermissionCodes: ["UNKNOWN_PERM"])));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.CreateRoleAsync(
                13,
                new CreateRoleRequest(
                    Code: "CUSTOM_ROLE_B",
                    Name: "Vai trò B",
                    PermissionCodes: [""])));

        var sodEx = await Assert.ThrowsAsync<ValidationException>(() =>
            _service.CreateRoleAsync(
                13,
                new CreateRoleRequest(
                    Code: "CUSTOM_ROLE_B",
                    Name: "Vai trò B",
                    PermissionCodes: ["PRESCRIPTION_ISSUE", "MEDICINE_DISPENSE"])));

        Assert.Contains("SoD", sodEx.Message);
    }

    [Fact]
    public async Task CreateRoleAsync_ActorUserNotFound_ThrowsNotFoundException()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.CreateRoleAsync(
                999,
                new CreateRoleRequest(Code: "CUSTOM_ROLE_C", Name: "Vai trò C")));
    }

    [Theory]
    [InlineData(11)]
    [InlineData(14)]
    [InlineData(15)]
    [InlineData(16)]
    public async Task CreateRoleAsync_ForbiddenOrInactiveActor_ThrowsForbiddenException(long actorUserId)
    {
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.CreateRoleAsync(
                actorUserId,
                new CreateRoleRequest(Code: "CUSTOM_ROLE_C", Name: "Vai trò C")));
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
