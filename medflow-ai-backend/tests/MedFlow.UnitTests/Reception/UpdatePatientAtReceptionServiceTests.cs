using MedFlow.Application.DTOs.Reception;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Reception;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.UnitTests.Reception;

public sealed class UpdatePatientAtReceptionServiceTests : IDisposable
{
    private sealed class UpdateTestDbContext : DbContext, IApplicationDbContext
    {
        public UpdateTestDbContext(DbContextOptions<UpdateTestDbContext> options)
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
        public DbSet<DoctorServiceAuthorization> DoctorServiceAuthorizations => throw NotUsed();
        public DbSet<ExaminationSlot> ExaminationSlots => throw NotUsed();
        public DbSet<AuthSession> AuthSessions => throw NotUsed();
        public DbSet<AuthVerificationChallenge> AuthVerificationChallenges => throw NotUsed();
        public DbSet<AuditLog> AuditLogs => throw NotUsed();
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
            modelBuilder.Ignore<Role>();
            modelBuilder.Ignore<Permission>();
            modelBuilder.Ignore<UserRole>();
            modelBuilder.Ignore<RolePermission>();
            modelBuilder.Ignore<Staff>();
            modelBuilder.Ignore<Doctor>();
            modelBuilder.Ignore<DoctorServiceAuthorization>();
            modelBuilder.Ignore<ExaminationSlot>();
            modelBuilder.Ignore<AuthSession>();
            modelBuilder.Ignore<AuthVerificationChallenge>();
            modelBuilder.Ignore<AuditLog>();
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
            modelBuilder.Entity<Patient>(b =>
            {
                b.HasKey(p => p.Id);
                b.HasOne(p => p.User)
                    .WithOne(u => u.Patient)
                    .HasForeignKey<Patient>(p => p.UserId);
            });
        }

        private static NotSupportedException NotUsed()
            => new("DbSet này không được dùng trong UpdatePatientAtReceptionServiceTests.");
    }

    private readonly UpdateTestDbContext _db;
    private readonly UpdatePatientAtReceptionService _sut;

    public UpdatePatientAtReceptionServiceTests()
    {
        var options = new DbContextOptionsBuilder<UpdateTestDbContext>()
            .UseInMemoryDatabase($"update-patient-{Guid.NewGuid():N}")
            .Options;
        _db = new UpdateTestDbContext(options);
        _sut = new UpdatePatientAtReceptionService(_db);
    }

    public void Dispose() => _db.Dispose();

    private static UpdatePatientAtReceptionRequest ValidRequest(
        DateOnly? dob = null,
        string phone = "0912345678",
        string? identityCard = null) => new()
        {
            FullName = "Trần Thị Bình",
            DateOfBirth = dob ?? new DateOnly(1990, 5, 20),
            Gender = "FEMALE",
            Phone = phone,
            Address = "12 Lê Lợi, Hà Nội",
            IdentityCard = identityCard,
            Email = "binh@medflow.test",
            BloodType = "A+",
        };

    private async Task<Patient> SeedPatientAsync(long id = 10, string code = "PAT-0010", string? hash = null)
    {
        var patient = new Patient
        {
            Id = id,
            PatientCode = code,
            FullName = "Old Name",
            Phone = "0900000000",
            Address = "Old",
            IdentificationHash = hash,
        };
        _db.Patients.Add(patient);
        await _db.SaveChangesAsync();
        return patient;
    }

    [Theory]
    [InlineData("10")]
    [InlineData("PAT-0010")]
    public async Task UpdatePatientAsync_ValidRequest_UpdatesFieldsByIdOrCode(string idOrCode)
    {
        await SeedPatientAsync();

        var result = await _sut.UpdatePatientAsync(idOrCode, ValidRequest(identityCard: "012345678901"));

        Assert.Equal(10, result.Id);
        Assert.Equal("Trần Thị Bình", result.FullName);
        Assert.Equal("0912345678", result.Phone);
        Assert.Equal("FEMALE", result.Gender);
        Assert.Equal("A+", result.BloodType);
        Assert.Equal("********8901", result.IdentityCardMasked);

        var stored = await _db.Patients.SingleAsync();
        Assert.Equal("Trần Thị Bình", stored.FullName);
        Assert.Equal("12 Lê Lợi, Hà Nội", stored.Address);
        Assert.NotNull(stored.IdentificationHash);
        Assert.DoesNotContain("012345678901", stored.IdentificationHash);
    }

    [Fact]
    public async Task UpdatePatientAsync_PatientCodeIsImmutable()
    {
        await SeedPatientAsync();

        var result = await _sut.UpdatePatientAsync("10", ValidRequest());

        Assert.Equal("PAT-0010", result.PatientCode);
        Assert.Equal("PAT-0010", (await _db.Patients.SingleAsync()).PatientCode);
    }

    [Theory]
    [InlineData("999")]
    [InlineData("PAT-9999")]
    public async Task UpdatePatientAsync_NotFound_ThrowsNotFound(string idOrCode)
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.UpdatePatientAsync(idOrCode, ValidRequest()));
    }

    [Fact]
    public async Task UpdatePatientAsync_FutureDateOfBirth_ThrowsValidation()
    {
        await SeedPatientAsync();
        var future = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(2);

        await Assert.ThrowsAsync<ValidationException>(() => _sut.UpdatePatientAsync("10", ValidRequest(dob: future)));
    }

    [Theory]
    [InlineData("0123456789")]
    [InlineData("091234567")]
    [InlineData("abc")]
    public async Task UpdatePatientAsync_InvalidPhone_ThrowsValidation(string phone)
    {
        await SeedPatientAsync();

        await Assert.ThrowsAsync<ValidationException>(() => _sut.UpdatePatientAsync("10", ValidRequest(phone: phone)));
    }

    [Fact]
    public async Task UpdatePatientAsync_InvalidIdentityCard_ThrowsValidation()
    {
        await SeedPatientAsync();

        await Assert.ThrowsAsync<ValidationException>(() => _sut.UpdatePatientAsync("10", ValidRequest(identityCard: "123")));
    }

    [Fact]
    public async Task UpdatePatientAsync_IdentityCardUsedByAnotherPatient_ThrowsConflict()
    {
        const string card = "012345678901";
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(card))).ToLowerInvariant();
        await SeedPatientAsync(id: 11, code: "PAT-0011", hash: hash);
        await SeedPatientAsync();

        await Assert.ThrowsAsync<ConflictException>(() => _sut.UpdatePatientAsync("10", ValidRequest(identityCard: card)));
    }

    [Fact]
    public async Task UpdatePatientAsync_SameIdentityCardOnSamePatient_Succeeds()
    {
        const string card = "012345678901";
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(card))).ToLowerInvariant();
        await SeedPatientAsync(hash: hash);

        var result = await _sut.UpdatePatientAsync("10", ValidRequest(identityCard: card));

        Assert.Equal(10, result.Id);
    }
}
