using MedFlow.Application.DTOs.SystemAdmin;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Auth;
using MedFlow.Application.Services.SystemAdmin;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.UnitTests.SystemAdmin;

public sealed class StaffAccountServiceTests : IDisposable
{
    private static readonly DateTime FixedNowUtc = new(2026, 11, 16, 10, 0, 0, DateTimeKind.Utc);

    private readonly StaffAccountTestDbContext _db;
    private readonly StaffAccountService _service;

    public StaffAccountServiceTests()
    {
        var options = new DbContextOptionsBuilder<StaffAccountTestDbContext>()
            .UseInMemoryDatabase($"StaffAccountTests_{Guid.NewGuid():N}")
            .Options;

        _db = new StaffAccountTestDbContext(options);
        Seed(_db);
        _service = new StaffAccountService(
            _db,
            new AuditLogger(_db),
            new FixedTimeProvider(new DateTimeOffset(FixedNowUtc)));
    }

    [Fact]
    public async Task CreateAsync_ValidTechnician_PersistsLinkedRowsAndSafeAudit()
    {
        var request = ValidRequest() with
        {
            Role = "TECHNICIAN",
            Email = " New.Staff@MedFlow.Test ",
        };

        var response = await _service.CreateAsync(1, request, "127.0.0.1");

        Assert.Equal("new.staff@medflow.test", response.Email);
        Assert.Equal("DIAGNOSTIC_STAFF", response.RoleCode);
        Assert.Equal("MedFlow Hà Nội", response.Facility);
        Assert.Equal("Khoa Tim mạch", response.Department);

        var user = await _db.Users
            .Include(u => u.UserRoles)
            .SingleAsync(u => u.Id == response.UserId);
        var staff = await _db.Staff.SingleAsync(s => s.Id == response.StaffId);
        var audit = await _db.AuditLogs.SingleAsync(a => a.Action == "STAFF_ACCOUNT_CREATED");

        Assert.Equal(user.Id, staff.UserId);
        Assert.Equal(3, Assert.Single(user.UserRoles).RoleId);
        Assert.NotNull(user.PasswordHash);
        Assert.Equal(
            PasswordVerificationResult.Success,
            new PasswordHasher<User>().VerifyHashedPassword(user, user.PasswordHash!, "MedFlow9A"));
        Assert.DoesNotContain("MedFlow9A", audit.ChangeData, StringComparison.Ordinal);
        Assert.DoesNotContain("password", audit.ChangeData, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(user.Email, audit.ChangeData, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(staff.FullName, audit.ChangeData, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateAsync_ForgedSystemAdminClaimWithoutDatabaseRole_ThrowsForbidden()
    {
        var exception = await Assert.ThrowsAsync<ForbiddenException>(
            () => _service.CreateAsync(2, ValidRequest(), null));

        Assert.Contains("không có quyền", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(_db.Staff.Where(s => s.UserId != 1));
    }

    [Fact]
    public async Task CreateAsync_WeakLocalPassword_ThrowsValidationWithoutWriting()
    {
        var request = ValidRequest() with { Password = "password1" };

        await Assert.ThrowsAsync<ValidationException>(
            () => _service.CreateAsync(1, request, null));

        Assert.DoesNotContain(_db.Users, u => u.Email == request.Email);
    }

    [Fact]
    public async Task CreateAsync_DuplicateEmail_ThrowsConflictWithoutWritingStaff()
    {
        var request = ValidRequest() with { Email = "patient@medflow.test" };

        await Assert.ThrowsAsync<ConflictException>(
            () => _service.CreateAsync(1, request, null));

        Assert.DoesNotContain(_db.Staff, s => s.StaffCode == request.StaffCode);
    }

    [Fact]
    public async Task CreateAsync_SystemAdminStaffWithNullStatus_ThrowsForbidden()
    {
        var actorStaff = await _db.Staff.SingleAsync(s => s.UserId == 1);
        actorStaff.Status = null!;
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _service.CreateAsync(1, ValidRequest(), null));
    }

    [Fact]
    public async Task CreateAsync_InactiveBaselineRole_ThrowsValidation()
    {
        var request = ValidRequest() with { Role = "SECURITY_AUDITOR" };

        await Assert.ThrowsAsync<ValidationException>(
            () => _service.CreateAsync(1, request, null));
    }

    [Fact]
    public async Task CreateAsync_AuditRoleStatusWithWhitespaceInactive_ThrowsValidation()
    {
        _db.AuditLogs.Add(new AuditLog
        {
            EntityType = "Role",
            EntityId = "2",
            Action = "ROLE_UPDATED",
            ChangeData = "{\"status\":\" INACTIVE \"}",
            OccurredAt = FixedNowUtc,
        });
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ValidationException>(
            () => _service.CreateAsync(1, ValidRequest(), null));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("2")]
    public async Task CreateAsync_NumericStatus_ThrowsValidation(string status)
    {
        var request = ValidRequest() with { Status = status };

        await Assert.ThrowsAsync<ValidationException>(
            () => _service.CreateAsync(1, request, null));
    }

    [Fact]
    public async Task CreateAsync_WhenRolePermissionsEmpty_ThrowsForbidden()
    {
        _db.RolePermissions.RemoveRange(_db.RolePermissions);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _service.CreateAsync(1, ValidRequest(), null));
    }

    private static CreateStaffAccountRequest ValidRequest() => new()
    {
        StaffCode = "STF-0250",
        FullName = "Lê Minh An",
        AuthProvider = "LOCAL",
        Facility = "HOS-01",
        Email = "new.staff@medflow.test",
        Password = "MedFlow9A",
        Status = "ACTIVE",
        Role = "DOCTOR",
        Department = "CARD",
    };

    private static void Seed(StaffAccountTestDbContext db)
    {
        db.Roles.AddRange(
            new Role { Id = 1, Code = "SYSTEM_ADMIN", Name = "Quản trị hệ thống" },
            new Role { Id = 2, Code = "DOCTOR", Name = "Bác sĩ" },
            new Role { Id = 3, Code = "DIAGNOSTIC_STAFF", Name = "Kỹ thuật viên" },
            new Role { Id = 4, Code = "PATIENT", Name = "Bệnh nhân" },
            new Role { Id = 5, Code = "SECURITY_AUDITOR", Name = "Kiểm toán viên" });

        db.Permissions.Add(new Permission { Id = 12, Code = "USER_MANAGE" });
        db.RolePermissions.Add(new RolePermission { RoleId = 1, PermissionId = 12 });

        db.Users.AddRange(
            new User
            {
                Id = 1,
                Email = "admin@medflow.test",
                AccountStatus = AccountStatus.ACTIVE,
            },
            new User
            {
                Id = 2,
                Email = "patient@medflow.test",
                AccountStatus = AccountStatus.ACTIVE,
            });

        db.UserRoles.AddRange(
            new UserRole { UserId = 1, RoleId = 1 },
            new UserRole { UserId = 2, RoleId = 4 });

        db.Staff.Add(new Staff
        {
            Id = 1,
            UserId = 1,
            HospitalId = 1,
            DepartmentId = 1,
            StaffCode = "STF-0001",
            FullName = "System Admin",
            Status = "ACTIVE",
        });

        db.Hospitals.Add(new Hospital
        {
            Id = 1,
            HospitalCode = "HOS-01",
            Name = "MedFlow Hà Nội",
            IsActive = true,
        });

        db.Departments.Add(new Department
        {
            Id = 1,
            HospitalId = 1,
            DepartmentCode = "CARD",
            Name = "Khoa Tim mạch",
            IsActive = true,
        });

        db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class StaffAccountTestDbContext(
        DbContextOptions<StaffAccountTestDbContext> options)
        : DbContext(options), IApplicationDbContext
    {
        private static NotSupportedException NotUsed() =>
            new("Not needed for StaffAccountService unit tests.");

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

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default) =>
            Task.FromResult<IDbContextTransaction>(new NoopTransaction());

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
            modelBuilder.Entity<User>().Property(u => u.Id).ValueGeneratedOnAdd();
            modelBuilder.Entity<User>().Ignore(u => u.Patient);
            modelBuilder.Entity<User>().Ignore(u => u.AuthSessions);

            modelBuilder.Entity<Role>().HasKey(r => r.Id);
            modelBuilder.Entity<Role>().Ignore(r => r.RolePermissions);
            modelBuilder.Entity<Permission>().HasKey(p => p.Id);
            modelBuilder.Entity<Permission>().Ignore(p => p.RolePermissions);
            modelBuilder.Entity<RolePermission>().HasKey(rp => new { rp.RoleId, rp.PermissionId });
            modelBuilder.Entity<RolePermission>().Ignore(rp => rp.Role);
            modelBuilder.Entity<RolePermission>().Ignore(rp => rp.Permission);

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
            modelBuilder.Entity<Staff>().Property(s => s.Id).ValueGeneratedOnAdd();
            modelBuilder.Entity<Staff>().Property(s => s.Status).IsRequired(false);
            modelBuilder.Entity<Staff>().Ignore(s => s.Department);
            modelBuilder.Entity<Staff>().Ignore(s => s.Doctor);
            modelBuilder.Entity<Staff>()
                .HasOne(s => s.User)
                .WithOne(u => u.Staff)
                .HasForeignKey<Staff>(s => s.UserId);

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
        public Task CreateSavepointAsync(string name, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public void RollbackToSavepoint(string name) => throw new NotSupportedException();
        public Task RollbackToSavepointAsync(string name, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public void ReleaseSavepoint(string name) => throw new NotSupportedException();
        public Task ReleaseSavepointAsync(string name, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
