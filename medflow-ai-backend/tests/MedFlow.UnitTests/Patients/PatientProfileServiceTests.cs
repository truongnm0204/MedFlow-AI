using MedFlow.Application.DTOs.Patients;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Patients;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.UnitTests.Patients;

public sealed class PatientProfileServiceTests : IDisposable
{
    private sealed class TestDbContext : DbContext, IApplicationDbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }

        public DbSet<User> Users => Set<User>();
        public DbSet<Role> Roles => throw new NotSupportedException();
        public DbSet<Permission> Permissions => throw new NotSupportedException();
        public DbSet<UserRole> UserRoles => throw new NotSupportedException();
        public DbSet<RolePermission> RolePermissions => throw new NotSupportedException();
        public DbSet<Patient> Patients => Set<Patient>();
        public DbSet<AuthVerificationChallenge> AuthVerificationChallenges => throw new NotSupportedException();
        public DbSet<RefreshToken> RefreshTokens => throw new NotSupportedException();
        public DbSet<AuditLog> AuditLogs => throw new NotSupportedException();

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default)
            => Database.BeginTransactionAsync(ct);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Patient>().HasKey(p => p.Id);
            modelBuilder.Entity<User>().HasKey(u => u.Id);
            modelBuilder.Ignore<Role>();
            modelBuilder.Ignore<Permission>();
            modelBuilder.Ignore<UserRole>();
            modelBuilder.Ignore<RolePermission>();
            modelBuilder.Ignore<AuthVerificationChallenge>();
            modelBuilder.Ignore<RefreshToken>();
            modelBuilder.Ignore<AuditLog>();
        }
    }

    private readonly TestDbContext _db;
    private readonly PatientProfileService _service;

    public PatientProfileServiceTests()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _db = new TestDbContext(options);
        _service = new PatientProfileService(_db);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task UpdatePersonalProfile_ValidData_ReturnsUpdatedProfile()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var patient = new Patient
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            PatientCode = "PAT001",
            FullName = "Nguyễn Văn A",
            Phone = "0901234567",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.Patients.Add(patient);
        await _db.SaveChangesAsync();

        var request = new UpdatePersonalProfileRequest(
            FullName: "Nguyễn Văn B",
            Phone: "0987654321",
            Email: "b@test.com",
            DateOfBirth: new DateOnly(1995, 5, 20),
            Gender: "MALE",
            IdentificationNo: "001234567890",
            IdentityIssuedDate: null,
            IdentityIssuedPlace: null,
            Address: "Hà Nội",
            AvatarUrl: null,
            Nationality: "Việt Nam",
            BloodType: "O",
            EmergencyContactName: "Mẹ",
            EmergencyContactPhone: "0911223344",
            EmergencyContactRelationship: "Mẹ"
        );

        // Act
        var result = await _service.UpdatePersonalProfileAsync(userId, request);

        // Assert
        Assert.Equal("Nguyễn Văn B", result.FullName);
        Assert.Equal("0987654321", result.Phone);
        Assert.Equal("001234567890", result.IdentificationNo);
        Assert.Equal("Hà Nội", result.Address);
    }

    [Fact]
    public async Task UpdatePersonalProfile_PatientNotFound_ThrowsNotFoundException()
    {
        // Arrange
        var request = new UpdatePersonalProfileRequest("Tên Mới", null, null, null, null, null, null, null, null, null, null, null, null, null, null);

        // Act & Assert
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.UpdatePersonalProfileAsync(Guid.NewGuid(), request));
    }

    [Fact]
    public async Task UpdatePersonalProfile_DuplicateIdentificationNo_ThrowsValidationException()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var currentPatient = new Patient
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            PatientCode = "PAT001",
            FullName = "Bệnh nhân 1"
        };

        var otherPatient = new Patient
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            PatientCode = "PAT002",
            FullName = "Bệnh nhân 2",
            IdentificationNo = "001234567890" // Số CCCD đã có người khác dùng
        };

        _db.Patients.AddRange(currentPatient, otherPatient);
        await _db.SaveChangesAsync();

        var request = new UpdatePersonalProfileRequest(
            FullName: "Bệnh nhân 1",
            Phone: null,
            Email: null,
            DateOfBirth: null,
            Gender: null,
            IdentificationNo: "001234567890", // Cố tình điền trùng số CCCD của người khác
            IdentityIssuedDate: null,
            IdentityIssuedPlace: null,
            Address: null,
            AvatarUrl: null,
            Nationality: null,
            BloodType: null,
            EmergencyContactName: null,
            EmergencyContactPhone: null,
            EmergencyContactRelationship: null
        );

        // Act & Assert
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdatePersonalProfileAsync(userId, request));
    }
}
