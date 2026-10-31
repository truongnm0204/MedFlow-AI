using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Patients;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.UnitTests.Patients;

public sealed class PatientProfileServiceTests : IDisposable
{
    private sealed class ProfileTestDbContext : DbContext, IApplicationDbContext
    {
        public ProfileTestDbContext(DbContextOptions<ProfileTestDbContext> options)
            : base(options)
        {
        }

        public DbSet<User> Users => Set<User>();
        public DbSet<Patient> Patients => Set<Patient>();

        public DbSet<Role> Roles => throw NotUsed();
        public DbSet<Permission> Permissions => throw NotUsed();
        public DbSet<UserRole> UserRoles => throw NotUsed();
        public DbSet<RolePermission> RolePermissions => throw NotUsed();
        public DbSet<Staff> Staff => throw NotUsed();
        public DbSet<Doctor> Doctors => throw NotUsed();
        public DbSet<AuthSession> AuthSessions => throw NotUsed();
        public DbSet<AuthVerificationChallenge> AuthVerificationChallenges => throw NotUsed();
        public DbSet<AuditLog> AuditLogs => throw NotUsed();
        public DbSet<Department> Departments => throw NotUsed();
        public DbSet<HospitalRoom> HospitalRooms => throw NotUsed();
        public DbSet<Specialty> Specialties => throw NotUsed();
        public DbSet<Service> Services => throw NotUsed();
        public DbSet<ServicePrice> ServicePrices => throw NotUsed();

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default)
            => Database.BeginTransactionAsync(ct);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Ignore<Role>();
            modelBuilder.Ignore<Permission>();
            modelBuilder.Ignore<UserRole>();
            modelBuilder.Ignore<RolePermission>();
            modelBuilder.Ignore<Staff>();
            modelBuilder.Ignore<Doctor>();
            modelBuilder.Ignore<AuthSession>();
            modelBuilder.Ignore<AuthVerificationChallenge>();
            modelBuilder.Ignore<AuditLog>();
            modelBuilder.Ignore<Department>();
            modelBuilder.Ignore<HospitalRoom>();
            modelBuilder.Ignore<Specialty>();
            modelBuilder.Ignore<Service>();
            modelBuilder.Ignore<ServicePrice>();

            modelBuilder.Entity<User>().HasKey(u => u.Id);
            modelBuilder.Entity<Patient>(b =>
            {
                b.HasKey(p => p.Id);
                b.HasOne(p => p.User)
                    .WithOne(u => u.Patient)
                    .HasForeignKey<Patient>(p => p.UserId);
            });
        }

        private static NotSupportedException NotUsed()
            => new("DbSet này không được dùng trong PatientProfileServiceTests.");
    }

    private readonly ProfileTestDbContext _db;
    private readonly PatientProfileService _sut;

    public PatientProfileServiceTests()
    {
        var options = new DbContextOptionsBuilder<ProfileTestDbContext>()
            .UseInMemoryDatabase($"patient-profile-{Guid.NewGuid():N}")
            .Options;

        _db = new ProfileTestDbContext(options);
        _sut = new PatientProfileService(_db);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task GetPersonalProfileAsync_ValidActivePatientWithIdentityHash_ReturnsFullProfileAndMaskedIdentity()
    {
        var user = new User
        {
            Id = 1,
            Email = "patient01@medflow.test",
            AccountStatus = AccountStatus.ACTIVE,
            EmailVerifiedAt = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc),
        };

        var patient = new Patient
        {
            Id = 10,
            UserId = 1,
            PatientCode = "PAT-0001",
            FullName = "Nguyễn Văn An",
            DateOfBirth = new DateOnly(1988, 8, 15),
            Gender = "MALE",
            Phone = "0900000001",
            Email = "patient01@medflow.test",
            IdentificationHash = "d4dac994f8bef67d90e2466997300b8e6e35901a751d0a8df2c080fe47836236",
            Status = "ACTIVE",
            Address = "Số 45 Tràng Tiền, Hà Nội",
            BloodType = "O+",
            User = user,
        };

        _db.Users.Add(user);
        _db.Patients.Add(patient);
        await _db.SaveChangesAsync();

        var result = await _sut.GetPersonalProfileAsync(1);

        Assert.Equal(10, result.Id);
        Assert.Equal(1, result.UserId);
        Assert.Equal("PAT-0001", result.PatientCode);
        Assert.Equal("Nguyễn Văn An", result.FullName);
        Assert.Equal(new DateOnly(1988, 8, 15), result.DateOfBirth);
        Assert.NotNull(result.Age);
        Assert.True(result.Age >= 37);
        Assert.Equal("MALE", result.Gender);
        Assert.Equal("0900000001", result.Phone);
        Assert.Equal("patient01@medflow.test", result.Email);
        Assert.True(result.IsEmailVerified);
        Assert.True(result.IsIdentityVerified);
        Assert.Equal("****** (Đã xác thực CCCD)", result.MaskedIdentification);
        Assert.DoesNotContain("d4dac994", result.MaskedIdentification);
        Assert.Equal("ACTIVE", result.Status);
        Assert.Equal("Số 45 Tràng Tiền, Hà Nội", result.Address);
        Assert.Equal("O+", result.BloodType);
        Assert.Equal(
            ["UPDATE_PERSONAL_PROFILE", "MANAGE_FAMILY_PROFILES", "DOWNLOAD_PROFILE_SUMMARY"],
            result.AllowedActions);
    }

    [Fact]
    public async Task GetPersonalProfileAsync_PatientWithoutIdentityHashOrEmail_FallsBackToUserEmailAndUnverifiedIdentity()
    {
        var user = new User
        {
            Id = 2,
            Email = "fallback@medflow.test",
            AccountStatus = AccountStatus.ACTIVE,
            EmailVerifiedAt = null,
        };

        var patient = new Patient
        {
            Id = 20,
            UserId = 2,
            PatientCode = "PAT-0002",
            FullName = "Trần Thị Bình",
            DateOfBirth = null,
            Gender = "FEMALE",
            Phone = null,
            Email = null,
            IdentificationHash = null,
            Status = "ACTIVE",
            Address = null,
            BloodType = null,
            User = user,
        };

        _db.Users.Add(user);
        _db.Patients.Add(patient);
        await _db.SaveChangesAsync();

        var result = await _sut.GetPersonalProfileAsync(2);

        Assert.Equal("fallback@medflow.test", result.Email);
        Assert.False(result.IsEmailVerified);
        Assert.False(result.IsIdentityVerified);
        Assert.Null(result.MaskedIdentification);
        Assert.Null(result.Age);
    }

    [Fact]
    public async Task GetPersonalProfileAsync_PatientNotFound_ThrowsNotFoundException()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _sut.GetPersonalProfileAsync(9999));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task GetPersonalProfileAsync_InvalidUserId_ThrowsUnauthorizedAccessException(long invalidUserId)
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _sut.GetPersonalProfileAsync(invalidUserId));
    }

    [Theory]
    [InlineData(AccountStatus.PENDING)]
    [InlineData(AccountStatus.LOCKED)]
    [InlineData(AccountStatus.INACTIVE)]
    public async Task GetPersonalProfileAsync_NonActiveUserAccount_ThrowsUnauthorizedAccessException(AccountStatus accountStatus)
    {
        var user = new User
        {
            Id = 3,
            Email = "locked@medflow.test",
            AccountStatus = accountStatus,
        };

        var patient = new Patient
        {
            Id = 30,
            UserId = 3,
            PatientCode = "PAT-0003",
            FullName = "Lê Văn C",
            Status = "ACTIVE",
            User = user,
        };

        _db.Users.Add(user);
        _db.Patients.Add(patient);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _sut.GetPersonalProfileAsync(3));
    }

    [Fact]
    public async Task GetPersonalProfileAsync_InactivePatientStatus_ThrowsUnauthorizedAccessException()
    {
        var user = new User
        {
            Id = 4,
            Email = "inactive-patient@medflow.test",
            AccountStatus = AccountStatus.ACTIVE,
        };

        var patient = new Patient
        {
            Id = 40,
            UserId = 4,
            PatientCode = "PAT-0004",
            FullName = "Phạm Văn D",
            Status = "INACTIVE",
            User = user,
        };

        _db.Users.Add(user);
        _db.Patients.Add(patient);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _sut.GetPersonalProfileAsync(4));
    }
}
