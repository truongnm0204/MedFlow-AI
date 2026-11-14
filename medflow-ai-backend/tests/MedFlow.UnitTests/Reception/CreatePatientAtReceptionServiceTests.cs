using System.Security.Cryptography;
using System.Text;
using MedFlow.Application.DTOs.Reception;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Options;
using MedFlow.Application.Services.Patients;
using MedFlow.Application.Services.Reception;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.UnitTests.Reception;

public sealed class CreatePatientAtReceptionServiceTests : IDisposable
{
    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class TestDbContext : DbContext, IApplicationDbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options)
        {
        }

        public DbSet<Patient> Patients => Set<Patient>();

        public DbSet<User> Users => throw NotUsed();
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
            modelBuilder.Ignore<User>();
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
            modelBuilder.Entity<Patient>(b =>
            {
                b.HasKey(p => p.Id);
                b.Ignore(p => p.User);
            });
        }

        private static NotSupportedException NotUsed() => new("Not used in CreatePatientAtReceptionServiceTests.");
    }

    private static readonly DateTimeOffset Now = new(2026, 11, 8, 3, 0, 0, TimeSpan.Zero);

    private readonly TestDbContext _db;
    private readonly CreatePatientAtReceptionService _sut;

    public CreatePatientAtReceptionServiceTests()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase($"create-patient-{Guid.NewGuid():N}")
            .Options;
        _db = new TestDbContext(options);
        _sut = new CreatePatientAtReceptionService(
            _db,
            new FixedTimeProvider(Now),
            Options.Create(new IdentityHashOptions { HashKey = "unit-test-identity-key" }));
    }

    public void Dispose() => _db.Dispose();

    private static CreatePatientAtReceptionRequest ValidRequest(string? identityCard = null) => new()
    {
        FullName = "  Nguyễn Văn An ",
        DateOfBirth = new DateOnly(1990, 1, 15),
        Gender = "male",
        Phone = "0912345678",
        Address = "12 Lê Lợi, Quận 1, TP.HCM",
        IdentityCard = identityCard,
        Email = "an@example.com",
        BloodType = "o+",
    };

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    [Fact]
    public async Task CreatePatient_ValidRequest_PersistsActivePatientAndReturnsMaskedIdentity()
    {
        var result = await _sut.CreatePatientAsync(ValidRequest("012345678901"));

        Assert.Equal("BN-2026-0001", result.PatientCode);
        Assert.Equal("Nguyễn Văn An", result.FullName);
        Assert.Equal("MALE", result.Gender);
        Assert.Equal("O+", result.BloodType);
        Assert.Equal("********8901", result.IdentityCardMasked);
        Assert.Equal(Now.UtcDateTime, result.CreatedAt);

        var saved = await _db.Patients.SingleAsync();
        Assert.Equal("ACTIVE", saved.Status);
        Assert.Equal(Hash("012345678901"), saved.IdentificationHash);
        Assert.Equal(
            IdentificationNumber.ComputeHmac("012345678901", "unit-test-identity-key"),
            saved.IdentificationHashHmac);
        Assert.Equal(IdentificationNumber.CurrentHashVersion, saved.HashVersion);
        Assert.Equal(result.Id, saved.Id);
    }

    [Fact]
    public async Task CreatePatient_WithNineDigitIdentity_StoresHash()
    {
        var result = await _sut.CreatePatientAsync(ValidRequest("012345678"));

        Assert.Equal("********5678", result.IdentityCardMasked);
        var saved = await _db.Patients.SingleAsync();
        Assert.Equal(Hash("012345678"), saved.IdentificationHash);
        Assert.Equal(
            IdentificationNumber.ComputeHmac("012345678", "unit-test-identity-key"),
            saved.IdentificationHashHmac);
    }

    [Fact]
    public async Task CreatePatient_WithoutHashKey_FailsClosed()
    {
        var service = new CreatePatientAtReceptionService(_db, new FixedTimeProvider(Now));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreatePatientAsync(ValidRequest("012345678901")));

        Assert.Equal(IdentificationNumber.MissingKeyMessage, ex.Message);
        Assert.Empty(_db.Patients);
    }

    [Fact]
    public async Task CreatePatient_WithoutIdentityCard_StoresNullHash()
    {
        var result = await _sut.CreatePatientAsync(ValidRequest());

        Assert.Null(result.IdentityCardMasked);
        Assert.Null((await _db.Patients.SingleAsync()).IdentificationHash);
    }

    [Fact]
    public async Task CreatePatient_FutureDateOfBirth_ThrowsValidation()
    {
        var req = ValidRequest();
        req.DateOfBirth = DateOnly.FromDateTime(Now.UtcDateTime).AddDays(1);

        await Assert.ThrowsAsync<ValidationException>(() => _sut.CreatePatientAsync(req));
        Assert.Empty(_db.Patients);
    }

    [Theory]
    [InlineData("0123456789")]
    [InlineData("0212345678")]
    [InlineData("091234567")]
    [InlineData("abc")]
    public async Task CreatePatient_InvalidPhone_ThrowsValidation(string phone)
    {
        var req = ValidRequest();
        req.Phone = phone;

        await Assert.ThrowsAsync<ValidationException>(() => _sut.CreatePatientAsync(req));
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("12345678901a")]
    public async Task CreatePatient_InvalidIdentityCard_ThrowsValidation(string cccd)
    {
        await Assert.ThrowsAsync<ValidationException>(() => _sut.CreatePatientAsync(ValidRequest(cccd)));
    }

    [Fact]
    public async Task CreatePatient_InvalidGenderOrBloodType_ThrowsValidation()
    {
        var badGender = ValidRequest();
        badGender.Gender = "UNKNOWN";
        await Assert.ThrowsAsync<ValidationException>(() => _sut.CreatePatientAsync(badGender));

        var badBlood = ValidRequest();
        badBlood.BloodType = "C+";
        await Assert.ThrowsAsync<ValidationException>(() => _sut.CreatePatientAsync(badBlood));
    }

    [Fact]
    public async Task CreatePatient_DuplicateIdentityCard_ThrowsConflict()
    {
        await _sut.CreatePatientAsync(ValidRequest("012345678901"));

        await Assert.ThrowsAsync<ConflictException>(() => _sut.CreatePatientAsync(ValidRequest("012345678901")));
        Assert.Equal(1, await _db.Patients.CountAsync());
    }

    [Fact]
    public async Task CreatePatient_GeneratesSequentialCodesPerYear()
    {
        _db.Patients.Add(new Patient { PatientCode = "BN-2025-0099", FullName = "Old" });
        _db.Patients.Add(new Patient { PatientCode = "BN-2026-0007", FullName = "Seven" });
        await _db.SaveChangesAsync();

        var first = await _sut.CreatePatientAsync(ValidRequest());
        var second = await _sut.CreatePatientAsync(ValidRequest());

        Assert.Equal("BN-2026-0008", first.PatientCode);
        Assert.Equal("BN-2026-0009", second.PatientCode);
    }
}
