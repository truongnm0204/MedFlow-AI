using MedFlow.Application.DTOs.SystemAdmin;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Auth;
using MedFlow.Application.Services.SystemAdmin;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.UnitTests.SystemAdmin;

public sealed class StaffAccountUpdateServiceTests : IDisposable
{
    private static readonly DateTime FixedNowUtc =
        new(2026, 11, 16, 10, 0, 0, DateTimeKind.Utc);

    private readonly StaffAccountUpdateTestDbContext _db;
    private readonly StaffAccountUpdateService _service;

    public StaffAccountUpdateServiceTests()
    {
        var options = new DbContextOptionsBuilder<StaffAccountUpdateTestDbContext>()
            .UseInMemoryDatabase($"StaffAccountUpdateTests_{Guid.NewGuid():N}")
            .Options;

        _db = new StaffAccountUpdateTestDbContext(options);
        Seed(_db);
        _service = new StaffAccountUpdateService(
            _db,
            new AuditLogger(_db),
            new FixedTimeProvider(new DateTimeOffset(FixedNowUtc)));
    }

    [Fact]
    public async Task UpdateAsync_ValidRequest_PersistsAccountRoleScopeAndSafeAudit()
    {
        var request = new UpdateStaffAccountRequest
        {
            FullName = " Trần Minh Khoa ",
            AuthProvider = "GOOGLE",
            Facility = "HOS-02",
            Department = "LAB",
            Email = " Updated.Staff@MedFlow.Test ",
            Status = "LOCKED",
            Role = "TECHNICIAN",
        };

        var response = await _service.UpdateAsync(1, "STF-0100", request, "127.0.0.1");

        Assert.Equal("Trần Minh Khoa", response.FullName);
        Assert.Equal("updated.staff@medflow.test", response.Email);
        Assert.Equal("GOOGLE", response.AuthProvider);
        Assert.Equal("LOCKED", response.Status);
        Assert.Equal("DIAGNOSTIC_STAFF", response.RoleCode);
        Assert.Equal(2, response.HospitalId);
        Assert.Equal(2, response.DepartmentId);

        var user = await _db.Users.SingleAsync(u => u.Id == 2);
        var staff = await _db.Staff.SingleAsync(s => s.Id == 2);
        var assignment = await _db.UserRoles.SingleAsync(ur => ur.UserId == 2);
        var audit = await _db.AuditLogs.SingleAsync(a => a.Action == "STAFF_ACCOUNT_UPDATED");

        Assert.Null(user.PasswordHash);
        Assert.Equal(AccountStatus.LOCKED, user.AccountStatus);
        Assert.Equal(3, assignment.RoleId);
        Assert.Equal(2, staff.HospitalId);
        Assert.Equal(2, staff.DepartmentId);
        var session = await _db.AuthSessions.SingleAsync(s => s.UserId == 2);
        Assert.Equal(FixedNowUtc, session.RevokedAt);
        Assert.Equal("127.0.0.1", session.RevokedByIp);
        Assert.DoesNotContain(user.Email, audit.ChangeData, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(staff.FullName, audit.ChangeData, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", audit.ChangeData, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateAsync_ForgedSystemAdminClaimWithoutDatabaseRole_ThrowsForbidden()
    {
        var originalName = (await _db.Staff.SingleAsync(s => s.Id == 2)).FullName;

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.UpdateAsync(
                2,
                "STF-0100",
                new UpdateStaffAccountRequest { FullName = "Changed" },
                null));

        Assert.Equal(originalName, (await _db.Staff.SingleAsync(s => s.Id == 2)).FullName);
    }

    [Fact]
    public async Task UpdateAsync_SelfRoleChange_ThrowsForbidden()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.UpdateAsync(
                1,
                "STF-0001",
                new UpdateStaffAccountRequest { Role = "DOCTOR" },
                null));

        Assert.Contains(await _db.UserRoles.ToListAsync(), ur => ur.UserId == 1 && ur.RoleId == 1);
    }

    [Fact]
    public async Task UpdateAsync_OtherSystemAdminAccessChange_ThrowsForbidden()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.UpdateAsync(
                1,
                "STF-0002",
                new UpdateStaffAccountRequest { Status = "LOCKED" },
                null));

        Assert.Equal(AccountStatus.ACTIVE, (await _db.Users.SingleAsync(u => u.Id == 3)).AccountStatus);
    }

    [Fact]
    public async Task UpdateAsync_DuplicateEmail_ThrowsConflictWithoutWriting()
    {
        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.UpdateAsync(
                1,
                "STF-0100",
                new UpdateStaffAccountRequest { Email = "admin@medflow.test" },
                null));

        Assert.Equal("staff@medflow.test", (await _db.Users.SingleAsync(u => u.Id == 2)).Email);
        Assert.Empty(_db.AuditLogs.Where(a => a.Action == "STAFF_ACCOUNT_UPDATED"));
    }

    [Fact]
    public async Task UpdateAsync_ActiveToDisabled_ThrowsValidation()
    {
        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateAsync(
                1,
                "STF-0100",
                new UpdateStaffAccountRequest { Status = "DISABLED" },
                null));

        Assert.Contains("vô hiệu hóa", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(AccountStatus.ACTIVE, (await _db.Users.SingleAsync(u => u.Id == 2)).AccountStatus);
    }

    [Fact]
    public async Task UpdateAsync_AssignSystemAdminRole_ThrowsValidation()
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateAsync(
                1,
                "STF-0100",
                new UpdateStaffAccountRequest { Role = "SYSTEM_ADMIN" },
                null));

        Assert.Contains(await _db.UserRoles.ToListAsync(), ur => ur.UserId == 2 && ur.RoleId == 2);
    }

    [Fact]
    public async Task UpdateAsync_GoogleToLocalWithoutStrongPassword_ThrowsValidation()
    {
        var user = await _db.Users.SingleAsync(u => u.Id == 2);
        user.AuthProvider = "GOOGLE";
        user.PasswordHash = null;
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateAsync(
                1,
                "STF-0100",
                new UpdateStaffAccountRequest
                {
                    AuthProvider = "LOCAL",
                    Password = "weak",
                },
                null));

        Assert.Equal("GOOGLE", user.AuthProvider);
        Assert.Null(user.PasswordHash);
    }

    [Fact]
    public async Task UpdateAsync_DuplicateStaffCodeAcrossHospitals_ThrowsConflict()
    {
        _db.Staff.Add(new Staff
        {
            Id = 4,
            HospitalId = 2,
            DepartmentId = 2,
            StaffCode = "STF-0100",
            FullName = "Duplicate Code",
            Status = "ACTIVE",
        });
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.UpdateAsync(
                1,
                "STF-0100",
                new UpdateStaffAccountRequest { FullName = "Changed" },
                null));
    }

    [Fact]
    public async Task UpdateAsync_UnassignedStaffNameOnly_SucceedsWithNullScope()
    {
        var staff = await _db.Staff.SingleAsync(s => s.Id == 2);
        staff.HospitalId = null;
        staff.DepartmentId = null;
        await _db.SaveChangesAsync();

        var response = await _service.UpdateAsync(
            1,
            "2",
            new UpdateStaffAccountRequest { FullName = "Updated Name" },
            null);

        Assert.Equal("Updated Name", response.FullName);
        Assert.Null(response.HospitalId);
        Assert.Null(response.DepartmentId);
    }

    [Fact]
    public async Task UpdateAsync_AmbiguousHospitalName_ThrowsValidation()
    {
        _db.Hospitals.AddRange(
            new Hospital
            {
                Id = 3,
                HospitalCode = "HOS-03",
                Name = "Cơ sở trùng tên",
                IsActive = true,
            },
            new Hospital
            {
                Id = 4,
                HospitalCode = "HOS-04",
                Name = "Cơ sở trùng tên",
                IsActive = true,
            });
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateAsync(
                1,
                "2",
                new UpdateStaffAccountRequest
                {
                    Facility = "Cơ sở trùng tên",
                    Department = "CARD",
                },
                null));
    }

    [Fact]
    public async Task UpdateAsync_AmbiguousDepartmentName_ThrowsValidation()
    {
        _db.Departments.AddRange(
            new Department
            {
                Id = 3,
                HospitalId = 1,
                DepartmentCode = "DUP-01",
                Name = "Khoa trùng tên",
                IsActive = true,
            },
            new Department
            {
                Id = 4,
                HospitalId = 1,
                DepartmentCode = "DUP-02",
                Name = "Khoa trùng tên",
                IsActive = true,
            });
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateAsync(
                1,
                "2",
                new UpdateStaffAccountRequest { Department = "Khoa trùng tên" },
                null));
    }

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }

    private static void Seed(StaffAccountUpdateTestDbContext db)
    {
        db.Roles.AddRange(
            new Role { Id = 1, Code = "SYSTEM_ADMIN", Name = "Quản trị hệ thống" },
            new Role { Id = 2, Code = "DOCTOR", Name = "Bác sĩ" },
            new Role { Id = 3, Code = "DIAGNOSTIC_STAFF", Name = "Kỹ thuật viên" },
            new Role { Id = 4, Code = "PATIENT", Name = "Bệnh nhân" });

        db.Users.AddRange(
            new User
            {
                Id = 1,
                Email = "admin@medflow.test",
                AuthProvider = "LOCAL",
                PasswordHash = "admin-hash",
                AccountStatus = AccountStatus.ACTIVE,
            },
            new User
            {
                Id = 2,
                Email = "staff@medflow.test",
                AuthProvider = "LOCAL",
                PasswordHash = "staff-hash",
                AccountStatus = AccountStatus.ACTIVE,
            },
            new User
            {
                Id = 3,
                Email = "second-admin@medflow.test",
                AuthProvider = "LOCAL",
                PasswordHash = "second-admin-hash",
                AccountStatus = AccountStatus.ACTIVE,
            });

        db.UserRoles.AddRange(
            new UserRole { UserId = 1, RoleId = 1 },
            new UserRole { UserId = 2, RoleId = 2 },
            new UserRole { UserId = 3, RoleId = 1 });

        db.Staff.AddRange(
            new Staff
            {
                Id = 1,
                UserId = 1,
                HospitalId = 1,
                DepartmentId = 1,
                StaffCode = "STF-0001",
                FullName = "System Admin",
                Status = "ACTIVE",
            },
            new Staff
            {
                Id = 2,
                UserId = 2,
                HospitalId = 1,
                DepartmentId = 1,
                StaffCode = "STF-0100",
                FullName = "Staff User",
                Status = "ACTIVE",
            },
            new Staff
            {
                Id = 3,
                UserId = 3,
                HospitalId = 1,
                DepartmentId = 1,
                StaffCode = "STF-0002",
                FullName = "Second System Admin",
                Status = "ACTIVE",
            });

        db.Hospitals.AddRange(
            new Hospital
            {
                Id = 1,
                HospitalCode = "HOS-01",
                Name = "MedFlow Hà Nội",
                IsActive = true,
            },
            new Hospital
            {
                Id = 2,
                HospitalCode = "HOS-02",
                Name = "MedFlow TP.HCM",
                IsActive = true,
            });

        db.Departments.AddRange(
            new Department
            {
                Id = 1,
                HospitalId = 1,
                DepartmentCode = "CARD",
                Name = "Khoa Tim mạch",
                IsActive = true,
            },
            new Department
            {
                Id = 2,
                HospitalId = 2,
                DepartmentCode = "LAB",
                Name = "Khoa Xét nghiệm",
                IsActive = true,
            });

        db.AuthSessions.Add(new AuthSession
        {
            Id = 1,
            UserId = 2,
            RefreshTokenHash = "active-session",
            ExpiresAt = FixedNowUtc.AddDays(1),
            CreatedAt = FixedNowUtc.AddDays(-1),
        });

        db.SaveChanges();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class StaffAccountUpdateTestDbContext(
        DbContextOptions<StaffAccountUpdateTestDbContext> options)
        : DbContext(options), IApplicationDbContext
    {
        private static NotSupportedException NotUsed() =>
            new("Not needed for StaffAccountUpdateService unit tests.");

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
        public DbSet<AuthSession> AuthSessions => Set<AuthSession>();
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

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default) =>
            Task.FromResult<IDbContextTransaction>(new NoopTransaction());

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Ignore<Permission>();
            modelBuilder.Ignore<RolePermission>();
            modelBuilder.Ignore<Patient>();
            modelBuilder.Ignore<Doctor>();
            modelBuilder.Ignore<DoctorServiceAuthorization>();
            modelBuilder.Ignore<ExaminationSlot>();
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
            modelBuilder.Entity<User>().Ignore(u => u.Patient);
            modelBuilder.Entity<Role>().HasKey(r => r.Id);
            modelBuilder.Entity<Role>().Ignore(r => r.RolePermissions);
            modelBuilder.Entity<UserRole>().HasKey(ur => new { ur.UserId, ur.RoleId });
            modelBuilder.Entity<UserRole>()
                .HasOne(ur => ur.User)
                .WithMany(u => u.UserRoles)
                .HasForeignKey(ur => ur.UserId);
            modelBuilder.Entity<UserRole>()
                .HasOne(ur => ur.Role)
                .WithMany(r => r.UserRoles)
                .HasForeignKey(ur => ur.RoleId);
            modelBuilder.Entity<Staff>().HasKey(s => s.Id);
            modelBuilder.Entity<Staff>().Ignore(s => s.Department);
            modelBuilder.Entity<Staff>().Ignore(s => s.Doctor);
            modelBuilder.Entity<Staff>()
                .HasOne(s => s.User)
                .WithOne(u => u.Staff)
                .HasForeignKey<Staff>(s => s.UserId);
            modelBuilder.Entity<AuthSession>().HasKey(session => session.Id);
            modelBuilder.Entity<AuthSession>()
                .HasOne(session => session.User)
                .WithMany(user => user.AuthSessions)
                .HasForeignKey(session => session.UserId);
            modelBuilder.Entity<Hospital>().HasKey(h => h.Id);
            modelBuilder.Entity<Department>().HasKey(d => d.Id);
            modelBuilder.Entity<Department>().Ignore(d => d.Services);
            modelBuilder.Entity<Department>().Ignore(d => d.HospitalRooms);
            modelBuilder.Entity<AuditLog>().HasKey(a => a.Id);
            modelBuilder.Entity<AuditLog>().Property(a => a.Id).ValueGeneratedOnAdd();
            modelBuilder.Entity<AuditLog>().Ignore(a => a.ActorUser);
            modelBuilder.Entity<AuditLog>().Ignore(a => a.IpAddress);
        }
    }

    private sealed class NoopTransaction : IDbContextTransaction
    {
        public Guid TransactionId { get; } = Guid.NewGuid();
        public bool SupportsSavepoints => false;
        public void Commit() { }
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Rollback() { }
        public Task RollbackAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void CreateSavepoint(string name) => throw new NotSupportedException();
        public Task CreateSavepointAsync(
            string name,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void RollbackToSavepoint(string name) => throw new NotSupportedException();
        public Task RollbackToSavepointAsync(
            string name,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void ReleaseSavepoint(string name) => throw new NotSupportedException();
        public Task ReleaseSavepointAsync(
            string name,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
