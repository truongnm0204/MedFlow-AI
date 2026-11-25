using MedFlow.Application.Common.Serialization;
using MedFlow.Application.DTOs.Patients;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Patients;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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
            => new("DbSet này không được dùng trong PatientProfileServiceTests.");
    }

    private sealed class RecordingAuditLogger : IAuditLogger
    {
        public List<(long? Actor, string Action, string EntityType, string? EntityId, object? Data, string? Ip)> Entries { get; } = [];

        public void Add(
            long? actorUserId,
            string action,
            string entityType,
            string? entityId,
            object? changeData,
            string? ipAddress)
        {
            Entries.Add((actorUserId, action, entityType, entityId, changeData, ipAddress));
        }
    }

    private readonly ProfileTestDbContext _db;
    private readonly RecordingAuditLogger _audit = new();
    private readonly PatientProfileService _sut;

    public PatientProfileServiceTests()
    {
        var options = new DbContextOptionsBuilder<ProfileTestDbContext>()
            .UseInMemoryDatabase($"patient-profile-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _db = new ProfileTestDbContext(options);
        _sut = new PatientProfileService(_db, _audit);
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

    [Fact]
    public async Task GetPersonalProfileAsync_HospitalClockMidnightBoundary_ComputesAgeUsingVietnamDate()
    {
        var user = new User
        {
            Id = 5,
            Email = "birthday@medflow.test",
            AccountStatus = AccountStatus.ACTIVE,
        };

        var patient = new Patient
        {
            Id = 50,
            UserId = 5,
            PatientCode = "PAT-0005",
            FullName = "Hoàng Thị E",
            DateOfBirth = new DateOnly(2000, 11, 9),
            Status = "ACTIVE",
            User = user,
        };

        _db.Users.Add(user);
        _db.Patients.Add(patient);
        await _db.SaveChangesAsync();

        // 2026-11-08 18:00 UTC == 2026-11-09 01:00 Vietnam time -> already turned 26 in Vietnam!
        var sutWithClock = new PatientProfileService(
            _db,
            _audit,
            new FixedTimeProvider(new DateTimeOffset(2026, 11, 8, 18, 0, 0, TimeSpan.Zero)));

        var profile = await sutWithClock.GetPersonalProfileAsync(5);
        Assert.Equal(26, profile.Age);
    }

    [Fact]
    public void Constructor_NullAuditLogger_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new PatientProfileService(_db, null!));
    }

    [Fact]
    public async Task UpdatePersonalProfileAsync_PutFullUpdate_PersistsContactBloodTypeEmergencyContactAndAudits()
    {
        var user = new User
        {
            Id = 11,
            Email = "update-put@medflow.test",
            AccountStatus = AccountStatus.ACTIVE,
            EmailVerifiedAt = DateTime.UtcNow,
        };
        var patient = new Patient
        {
            Id = 110,
            UserId = 11,
            PatientCode = "PAT-0110",
            FullName = "Nguyễn Minh Khoa",
            DateOfBirth = new DateOnly(1992, 4, 10),
            Gender = "MALE",
            Phone = "0901234567",
            IdentificationHash = "verified-hash-110",
            Status = "ACTIVE",
            User = user,
        };
        _db.Users.Add(user);
        _db.Patients.Add(patient);
        await _db.SaveChangesAsync();

        var updated = await _sut.UpdatePersonalProfileAsync(
            11,
            new UpdateMyPatientProfileRequest
            {
                Phone = OptionalField<string?>.FromValue("0988112233"),
                Address = OptionalField<string?>.FromValue("  123 Nguyễn Trãi, Quận 1, TP.HCM  "),
                BloodType = OptionalField<string?>.FromValue("ab+"),
                EmergencyContact = OptionalField<UpdateEmergencyContactRequest?>.FromValue(
                    new UpdateEmergencyContactRequest
                    {
                        FullName = OptionalField<string?>.FromValue("Nguyễn Thị Lan"),
                        Relationship = OptionalField<string?>.FromValue("Vợ/Chồng"),
                        Phone = OptionalField<string?>.FromValue("0912334455"),
                    }),
            },
            isPatch: false,
            ipAddress: "203.0.113.55");

        Assert.Equal("0988112233", updated.Phone);
        Assert.Equal("123 Nguyễn Trãi, Quận 1, TP.HCM", updated.Address);
        Assert.Equal("AB+", updated.BloodType);
        Assert.NotNull(updated.EmergencyContact);
        Assert.Equal("Nguyễn Thị Lan", updated.EmergencyContact!.FullName);
        Assert.Equal("Vợ/Chồng", updated.EmergencyContact.Relationship);
        Assert.Equal("0912334455", updated.EmergencyContact.Phone);

        var audit = Assert.Single(_audit.Entries);
        Assert.Equal(11L, audit.Actor);
        Assert.Equal(PatientProfileService.ProfileUpdatedAuditAction, audit.Action);
        Assert.Equal("PATIENT", audit.EntityType);
        Assert.Equal("110", audit.EntityId);
        Assert.Equal("203.0.113.55", audit.Ip);
        var auditJson = System.Text.Json.JsonSerializer.Serialize(audit.Data);
        Assert.Contains("PATIENT_SELF_REPORTED", auditJson);
    }

    [Fact]
    public async Task UpdatePersonalProfileAsync_PatchOmittedVsExplicitNull_PreservesOmittedAndClearsExplicitNull()
    {
        var user = new User
        {
            Id = 12,
            Email = "update-patch@medflow.test",
            AccountStatus = AccountStatus.ACTIVE,
        };
        var patient = new Patient
        {
            Id = 120,
            UserId = 12,
            PatientCode = "PAT-0120",
            FullName = "Lê Thu Hà",
            Phone = "0911222333",
            Address = "Số 9 Hai Bà Trưng, Hà Nội",
            BloodType = "O+",
            EmergencyContactName = "Lê Văn Nam",
            EmergencyContactRelationship = "Anh trai",
            EmergencyContactPhone = "0933444555",
            Status = "ACTIVE",
            User = user,
        };
        _db.Users.Add(user);
        _db.Patients.Add(patient);
        await _db.SaveChangesAsync();

        // 1. PATCH only phone -> address, bloodType, emergencyContact stay intact
        var afterPhonePatch = await _sut.UpdatePersonalProfileAsync(
            12,
            new UpdateMyPatientProfileRequest
            {
                Phone = OptionalField<string?>.FromValue("0977888999"),
            },
            isPatch: true);

        Assert.Equal("0977888999", afterPhonePatch.Phone);
        Assert.Equal("Số 9 Hai Bà Trưng, Hà Nội", afterPhonePatch.Address);
        Assert.Equal("O+", afterPhonePatch.BloodType);
        Assert.NotNull(afterPhonePatch.EmergencyContact);
        Assert.Equal("Lê Văn Nam", afterPhonePatch.EmergencyContact!.FullName);

        // 2. PATCH explicit null on address + emergencyContact -> clears both while keeping phone & bloodType
        var afterClearPatch = await _sut.UpdatePersonalProfileAsync(
            12,
            new UpdateMyPatientProfileRequest
            {
                Address = OptionalField<string?>.FromValue(null),
                EmergencyContact = OptionalField<UpdateEmergencyContactRequest?>.FromValue(null),
            },
            isPatch: true);

        Assert.Equal("0977888999", afterClearPatch.Phone);
        Assert.Null(afterClearPatch.Address);
        Assert.Equal("O+", afterClearPatch.BloodType);
        Assert.Null(afterClearPatch.EmergencyContact);
    }

    [Fact]
    public async Task UpdatePersonalProfileAsync_VerifiedIdentity_LocksFullNameIdentityCardDobAndGender()
    {
        var user = new User
        {
            Id = 13,
            Email = "verified-lock@medflow.test",
            AccountStatus = AccountStatus.ACTIVE,
        };
        var patient = new Patient
        {
            Id = 130,
            UserId = 13,
            PatientCode = "PAT-0130",
            FullName = "Phạm Quốc Bảo",
            DateOfBirth = new DateOnly(1985, 6, 20),
            Gender = "MALE",
            Phone = "0905111222",
            IdentificationHash = "verified-cccd-hash",
            Status = "ACTIVE",
            User = user,
        };
        _db.Users.Add(user);
        _db.Patients.Add(patient);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.UpdatePersonalProfileAsync(
                13,
                new UpdateMyPatientProfileRequest
                {
                    FullName = OptionalField<string?>.FromValue("Tên Giả Mạo"),
                },
                isPatch: true));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.UpdatePersonalProfileAsync(
                13,
                new UpdateMyPatientProfileRequest
                {
                    IdentityCard = OptionalField<string?>.FromValue("001085012345"),
                },
                isPatch: true));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.UpdatePersonalProfileAsync(
                13,
                new UpdateMyPatientProfileRequest
                {
                    DateOfBirth = OptionalField<DateOnly?>.FromValue(new DateOnly(1990, 1, 1)),
                },
                isPatch: true));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.UpdatePersonalProfileAsync(
                13,
                new UpdateMyPatientProfileRequest
                {
                    Gender = OptionalField<string?>.FromValue("FEMALE"),
                },
                isPatch: true));
    }

    [Fact]
    public async Task UpdatePersonalProfileAsync_UnverifiedIdentity_AllowsUpdatingDobAndGender()
    {
        var user = new User
        {
            Id = 14,
            Email = "unverified-dob@medflow.test",
            AccountStatus = AccountStatus.ACTIVE,
        };
        var patient = new Patient
        {
            Id = 140,
            UserId = 14,
            PatientCode = "PAT-0140",
            FullName = "Đặng Thùy Trang",
            DateOfBirth = new DateOnly(1999, 1, 1),
            Gender = "OTHER",
            Phone = "0906111222",
            IdentificationHash = null,
            Status = "ACTIVE",
            User = user,
        };
        _db.Users.Add(user);
        _db.Patients.Add(patient);
        await _db.SaveChangesAsync();

        var updated = await _sut.UpdatePersonalProfileAsync(
            14,
            new UpdateMyPatientProfileRequest
            {
                DateOfBirth = OptionalField<DateOnly?>.FromValue(new DateOnly(1998, 12, 25)),
                Gender = OptionalField<string?>.FromValue("female"),
            },
            isPatch: true);

        Assert.Equal(new DateOnly(1998, 12, 25), updated.DateOfBirth);
        Assert.Equal("FEMALE", updated.Gender);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
