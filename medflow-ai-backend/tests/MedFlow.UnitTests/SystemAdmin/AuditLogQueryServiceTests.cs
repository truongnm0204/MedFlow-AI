using System.Net;
using MedFlow.Application.DTOs.SystemAdmin;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.SystemAdmin;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.UnitTests.SystemAdmin;

public sealed class AuditLogQueryServiceTests : IDisposable
{
    // 2026-11-25 02:00:00 UTC == 2026-11-25 09:00:00 Asia/Ho_Chi_Minh (UTC+7)
    private static readonly DateTime FixedNowUtc = new(2026, 11, 25, 2, 0, 0, DateTimeKind.Utc);

    private sealed class FixedTimeProvider(DateTimeOffset nowUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => nowUtc;
    }

    private sealed class AuditLogTestDbContext : DbContext, IApplicationDbContext
    {
        public AuditLogTestDbContext(DbContextOptions<AuditLogTestDbContext> options)
            : base(options)
        {
        }

        private static NotSupportedException NotUsed() =>
            new("Not needed for AuditLogQueryService unit tests.");

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

            modelBuilder.Entity<User>().HasKey(u => u.Id);
            modelBuilder.Entity<Role>().HasKey(r => r.Id);
            modelBuilder.Entity<Permission>().HasKey(p => p.Id);
            modelBuilder.Entity<UserRole>().HasKey(ur => new { ur.UserId, ur.RoleId });
            modelBuilder.Entity<RolePermission>().HasKey(rp => new { rp.RoleId, rp.PermissionId });
            modelBuilder.Entity<Staff>(b =>
            {
                b.HasKey(s => s.Id);
                b.Ignore(s => s.User);
                b.Ignore(s => s.Department);
                b.Ignore(s => s.Doctor);
            });
            modelBuilder.Entity<Hospital>().HasKey(h => h.Id);
            modelBuilder.Entity<AuditLog>(b =>
            {
                b.HasKey(a => a.Id);
                b.Ignore(a => a.ActorUser);
                b.Ignore(a => a.IpAddress);
            });
        }
    }

    private readonly AuditLogTestDbContext _db;
    private readonly AuditLogQueryService _sut;

    public AuditLogQueryServiceTests()
    {
        var options = new DbContextOptionsBuilder<AuditLogTestDbContext>()
            .UseInMemoryDatabase($"AuditLogQueryTests_{Guid.NewGuid():N}")
            .Options;

        _db = new AuditLogTestDbContext(options);
        SeedData(_db);
        _sut = new AuditLogQueryService(_db, new FixedTimeProvider(new DateTimeOffset(FixedNowUtc)));
    }

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }

    private static void SeedData(AuditLogTestDbContext db)
    {
        db.Hospitals.AddRange(
            new Hospital { Id = 1, HospitalCode = "HOS-01", Name = "Bệnh viện MedFlow Cơ sở 1", IsActive = true },
            new Hospital { Id = 2, HospitalCode = "HOS-02", Name = "Bệnh viện MedFlow Cơ sở 2", IsActive = true });

        db.Roles.AddRange(
            new Role { Id = 1, Code = RoleNames.SystemAdmin, Name = "Quản trị hệ thống" },
            new Role { Id = 2, Code = RoleNames.HospitalAdmin, Name = "Quản trị bệnh viện" },
            new Role { Id = 3, Code = RoleNames.Doctor, Name = "Bác sĩ" });

        db.Permissions.AddRange(
            new Permission { Id = 11, Code = PermissionCodes.HospitalConfig },
            new Permission { Id = 12, Code = PermissionCodes.UserManage },
            new Permission { Id = 13, Code = PermissionCodes.AuditRead });

        db.RolePermissions.AddRange(
            new RolePermission { RoleId = 1, PermissionId = 11 },
            new RolePermission { RoleId = 1, PermissionId = 12 },
            new RolePermission { RoleId = 1, PermissionId = 13 },
            new RolePermission { RoleId = 2, PermissionId = 11 });

        db.Users.AddRange(
            new User { Id = 13, Email = "system_admin01@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 11, Email = "hospital_admin01@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 12, Email = "hospital_admin02@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 21, Email = "doctor01@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 14, Email = "locked_admin@medflow.test", AccountStatus = AccountStatus.ACTIVE, LockedUntil = FixedNowUtc.AddMinutes(20) });

        db.UserRoles.AddRange(
            new UserRole { UserId = 13, RoleId = 1 },
            new UserRole { UserId = 11, RoleId = 2 },
            new UserRole { UserId = 12, RoleId = 2 },
            new UserRole { UserId = 21, RoleId = 3 },
            new UserRole { UserId = 14, RoleId = 1 });

        db.Staff.AddRange(
            new Staff { Id = 101, UserId = 13, HospitalId = 1, StaffCode = "STF-011", FullName = "QTHT Lưu Huy", Status = "ACTIVE" },
            new Staff { Id = 102, UserId = 11, HospitalId = 1, StaffCode = "STF-009", FullName = "QLBV Phan Bình", Status = "ACTIVE" },
            new Staff { Id = 103, UserId = 12, HospitalId = 2, StaffCode = "STF-010", FullName = "QLBV Đỗ Hương", Status = "ACTIVE" });

        // Timestamps test Vietnam UTC+7 boundary:
        // Log 1: 2026-11-24 17:30 UTC == 2026-11-25 00:30 UTC+7 (falls on 2026-11-25 in HospitalClock!)
        // Log 2: 2026-11-24 16:30 UTC == 2026-11-24 23:30 UTC+7 (falls on 2026-11-24 in HospitalClock!)
        // Log 3: Hospital 2 log
        // Log 4: Global log (HospitalId == null) with sensitive fields
        db.AuditLogs.AddRange(
            new AuditLog
            {
                Id = 1,
                ActorUserId = 13,
                HospitalId = 1,
                Action = "INTEGRATION_CONFIG_UPDATED",
                EntityType = "INTEGRATION_CONFIG",
                EntityId = "1:VIETQR_SEPAY",
                Reason = "Cập nhật VietQR kèm Authorization: Bearer raw-jwt-token-xyz",
                ChangeData = "{\"Key\":\"vietqr_sepay\",\"webhookApiKey\":\"raw-webhook-secret\",\"NewValue\":\"{\\\"apiKey\\\":\\\"nested-secret-999\\\",\\\"enabled\\\":true}\",\"maxTokens\":512}",
                OccurredAt = new DateTime(2026, 11, 24, 17, 30, 0, DateTimeKind.Unspecified),
            },
            new AuditLog
            {
                Id = 2,
                ActorUserId = 11,
                HospitalId = 1,
                Action = "APPOINTMENT_POLICY_UPDATE",
                EntityType = "HOSPITAL_POLICY",
                EntityId = "1",
                Reason = "Cập nhật chính sách hủy hẹn",
                ChangeData = "{\"Changes\":[{\"Key\":\"cancel_hours\",\"OldValue\":\"6\",\"NewValue\":\"8\"}]}",
                OccurredAt = new DateTime(2026, 11, 24, 16, 30, 0, DateTimeKind.Unspecified),
            },
            new AuditLog
            {
                Id = 3,
                ActorUserId = 12,
                HospitalId = 2,
                Action = "DEPARTMENT_CREATE",
                EntityType = "DEPARTMENT",
                EntityId = "DEP-02",
                Reason = "Tạo khoa mới tại Cơ sở 2",
                ChangeData = "{\"departmentCode\":\"DEP-02\"}",
                OccurredAt = new DateTime(2026, 11, 23, 10, 0, 0, DateTimeKind.Unspecified),
            },
            new AuditLog
            {
                Id = 4,
                ActorUserId = 13,
                HospitalId = null,
                Action = "LOGIN_SUCCESS",
                EntityType = "USER",
                EntityId = "13",
                Reason = "Đăng nhập hệ thống",
                ChangeData = "{\"passwordHash\":\"pbkdf2-raw-hash\",\"refreshToken\":\"raw-rt-token\",\"otpCode\":\"123456\"}",
                OccurredAt = new DateTime(2026, 11, 25, 1, 0, 0, DateTimeKind.Unspecified),
            });

        db.SaveChanges();
    }

    [Fact]
    public async Task GetAuditLogsAsync_SystemAdmin_ReturnsGlobalLogsAndMasksSensitiveFields()
    {
        var response = await _sut.GetAuditLogsAsync(13, new GetAuditLogsRequest(Page: 1, PageSize: 10));

        Assert.Equal(4, response.Total);
        Assert.Equal(4, response.Items.Count);
        Assert.Equal([4L, 1L, 2L, 3L], response.Items.Select(i => i.Id).ToArray());
        Assert.Equal(4, response.Metrics.TotalLogs);
        Assert.Equal(3, response.Metrics.Last24HoursCount);
        Assert.Equal(1, response.Metrics.AuthEventsCount);
        Assert.Equal(3, response.Metrics.ConfigAndRbacEventsCount);

        var log1 = response.Items.Single(i => i.Id == 1);
        Assert.NotNull(log1.ChangeData);
        Assert.DoesNotContain("raw-webhook-secret", log1.ChangeData, StringComparison.Ordinal);
        Assert.DoesNotContain("nested-secret-999", log1.ChangeData, StringComparison.Ordinal);
        Assert.Contains("***REDACTED***", log1.ChangeData, StringComparison.Ordinal);
        Assert.Contains("\"maxTokens\":512", log1.ChangeData, StringComparison.Ordinal);
        Assert.DoesNotContain("raw-jwt-token-xyz", log1.Reason!, StringComparison.Ordinal);

        var detail4 = await _sut.GetAuditLogByIdAsync(13, 4);
        Assert.DoesNotContain("pbkdf2-raw-hash", detail4.ChangeData!, StringComparison.Ordinal);
        Assert.DoesNotContain("raw-rt-token", detail4.ChangeData!, StringComparison.Ordinal);
        Assert.DoesNotContain("123456", detail4.ChangeData!, StringComparison.Ordinal);
        Assert.Contains("passwordHash", detail4.RedactedFields);
        Assert.Contains("refreshToken", detail4.RedactedFields);
        Assert.Contains("otpCode", detail4.RedactedFields);
    }

    [Fact]
    public async Task GetAuditLogsAsync_FiltersByHospitalClockUtcPlus7DateBoundariesAndCriteria()
    {
        // Filtering 2026-11-25 in Vietnam time (UTC+7) must include 2026-11-24 17:30 UTC (00:30 +07)
        // and exclude 2026-11-24 16:30 UTC (23:30 +07 on Nov 24).
        var nov25Only = await _sut.GetAuditLogsAsync(
            13,
            new GetAuditLogsRequest(
                FromDate: new DateOnly(2026, 11, 25),
                ToDate: new DateOnly(2026, 11, 25)));

        Assert.Equal([4L, 1L], nov25Only.Items.Select(i => i.Id).ToArray());

        var nov24Only = await _sut.GetAuditLogsAsync(
            13,
            new GetAuditLogsRequest(
                FromDate: new DateOnly(2026, 11, 24),
                ToDate: new DateOnly(2026, 11, 24)));

        var item = Assert.Single(nov24Only.Items);
        Assert.Equal(2L, item.Id);

        // Filter by keyword (AuditCode / staff full name), action, entityType, actorUserId
        var byActionAndActor = await _sut.GetAuditLogsAsync(
            13,
            new GetAuditLogsRequest(
                Keyword: "Phan Bình",
                Action: "appointment_policy_update",
                EntityType: "hospital_policy",
                ActorUserId: 11));

        Assert.Single(byActionAndActor.Items);
        Assert.Equal(2L, byActionAndActor.Items[0].Id);
        Assert.Equal("AUD-0002", byActionAndActor.Items[0].AuditCode);
    }

    [Fact]
    public async Task GetAuditLogsAsync_HospitalAdminWithoutAuditRead_FailsClosedWithForbidden()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.GetAuditLogsAsync(11, new GetAuditLogsRequest()));
    }

    [Fact]
    public async Task GetAuditLogsAsync_HospitalAdminWithAuditRead_EnforcesOwnHospitalIsolation()
    {
        _db.RolePermissions.Add(new RolePermission { RoleId = 2, PermissionId = 13 });
        await _db.SaveChangesAsync();

        var scoped = await _sut.GetAuditLogsAsync(11, new GetAuditLogsRequest());
        Assert.Equal(2, scoped.Total);
        Assert.All(scoped.Items, item => Assert.Equal(1L, item.HospitalId));

        // Reject cross-hospital filter
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.GetAuditLogsAsync(11, new GetAuditLogsRequest(HospitalId: 2)));

        // Reject viewing Hospital 2 detail or global (HospitalId == null) detail
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.GetAuditLogByIdAsync(11, 3));
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.GetAuditLogByIdAsync(11, 4));

        // Own hospital detail succeeds
        var ownDetail = await _sut.GetAuditLogByIdAsync(11, 2);
        Assert.Equal(2L, ownDetail.Id);
    }

    [Fact]
    public async Task GetAuditLogsAsync_EmptyRolePermissionsOrLockedOrNonAdmin_ThrowsForbidden()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.GetAuditLogsAsync(21, new GetAuditLogsRequest()));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.GetAuditLogsAsync(14, new GetAuditLogsRequest()));

        _db.RolePermissions.RemoveRange(_db.RolePermissions);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.GetAuditLogsAsync(13, new GetAuditLogsRequest()));
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task GetAuditLogsAsync_InvalidPaging_ThrowsValidationException(int page, int pageSize)
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.GetAuditLogsAsync(13, new GetAuditLogsRequest(Page: page, PageSize: pageSize)));
    }

    [Fact]
    public async Task GetAuditLogsAsync_InvalidDateRangeAndNotFound_ThrowsExpectedExceptions()
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.GetAuditLogsAsync(
                13,
                new GetAuditLogsRequest(
                    FromDate: new DateOnly(2026, 11, 25),
                    ToDate: new DateOnly(2026, 11, 20))));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.GetAuditLogsAsync(
                13,
                new GetAuditLogsRequest(
                    FromDate: new DateOnly(2025, 1, 1),
                    ToDate: new DateOnly(2026, 11, 25))));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.GetAuditLogByIdAsync(13, 0));

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _sut.GetAuditLogByIdAsync(13, 9999));
    }
}
