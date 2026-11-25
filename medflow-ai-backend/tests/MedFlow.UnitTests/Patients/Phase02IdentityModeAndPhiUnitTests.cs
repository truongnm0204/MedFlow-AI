using MedFlow.Application.Common.Time;
using MedFlow.Application.DTOs.Patients;
using MedFlow.Application.DTOs.Reception;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Options;
using MedFlow.Application.Services.Patients;
using MedFlow.Application.Services.Reception;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace MedFlow.UnitTests.Patients;

public sealed class Phase02IdentityModeAndPhiUnitTests : IDisposable
{
    private const string HmacKey = "phase02-unit-test-hmac-key-32-bytes-minimum!!";

    private sealed class NoopAuditLogger : IAuditLogger
    {
        public void Add(long? actorUserId, string action, string entityType, string? entityId, object? changeData, string? ipAddress)
        {
        }
    }

    private sealed class Phase02UnitDbContext(DbContextOptions<Phase02UnitDbContext> options)
        : DbContext(options), IApplicationDbContext
    {
        public DbSet<User> Users => Set<User>();
        public DbSet<Patient> Patients => Set<Patient>();
        public DbSet<PatientFamilyLink> PatientFamilyLinks => Set<PatientFamilyLink>();
        public DbSet<Visit> Visits => Set<Visit>();
        public DbSet<Appointment> Appointments => Set<Appointment>();
        public DbSet<ExaminationSlot> ExaminationSlots => Set<ExaminationSlot>();
        public DbSet<Service> Services => Set<Service>();
        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

        public DbSet<Role> Roles => throw NotUsed();
        public DbSet<Permission> Permissions => throw NotUsed();
        public DbSet<UserRole> UserRoles => throw NotUsed();
        public DbSet<RolePermission> RolePermissions => throw NotUsed();
        public DbSet<Staff> Staff => throw NotUsed();
        public DbSet<Doctor> Doctors => throw NotUsed();
        public DbSet<DoctorServiceAuthorization> DoctorServiceAuthorizations => throw NotUsed();
        public DbSet<AuthSession> AuthSessions => throw NotUsed();
        public DbSet<AuthVerificationChallenge> AuthVerificationChallenges => throw NotUsed();
        public DbSet<Hospital> Hospitals => throw NotUsed();
        public DbSet<Department> Departments => throw NotUsed();
        public DbSet<HospitalRoom> HospitalRooms => throw NotUsed();
        public DbSet<Specialty> Specialties => throw NotUsed();
        public DbSet<ServicePrice> ServicePrices => throw NotUsed();
        public DbSet<HospitalPolicy> HospitalPolicies => throw NotUsed();
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
            modelBuilder.Ignore<AuthSession>();
            modelBuilder.Ignore<AuthVerificationChallenge>();
            modelBuilder.Ignore<Hospital>();
            modelBuilder.Ignore<Department>();
            modelBuilder.Ignore<HospitalRoom>();
            modelBuilder.Ignore<Specialty>();
            modelBuilder.Ignore<ServicePrice>();
            modelBuilder.Ignore<HospitalPolicy>();
            modelBuilder.Ignore<QueueEntry>();
            modelBuilder.Ignore<Payment>();

            modelBuilder.Entity<User>(b =>
            {
                b.HasKey(u => u.Id);
                b.Ignore(u => u.UserRoles);
                b.Ignore(u => u.Staff);
                b.Ignore(u => u.AuthSessions);
            });

            modelBuilder.Entity<Patient>(b =>
            {
                b.HasKey(p => p.Id);
                b.HasOne(p => p.User).WithOne(u => u.Patient).HasForeignKey<Patient>(p => p.UserId);
            });

            modelBuilder.Entity<PatientFamilyLink>(b =>
            {
                b.HasKey(l => new { l.OwnerPatientId, l.MemberPatientId });
                b.HasOne(l => l.OwnerPatient).WithMany().HasForeignKey(l => l.OwnerPatientId);
                b.HasOne(l => l.MemberPatient).WithMany().HasForeignKey(l => l.MemberPatientId);
            });

            modelBuilder.Entity<Service>(b =>
            {
                b.HasKey(s => s.Id);
                b.Ignore(s => s.Department);
                b.Ignore(s => s.Specialty);
                b.Ignore(s => s.ServicePrices);
            });

            modelBuilder.Entity<ExaminationSlot>(b =>
            {
                b.HasKey(s => s.Id);
                b.Ignore(s => s.Doctor);
                b.Ignore(s => s.Room);
                b.Ignore(s => s.Service);
            });

            modelBuilder.Entity<Appointment>(b =>
            {
                b.HasKey(a => a.Id);
                b.Ignore(a => a.Patient);
                b.Ignore(a => a.Slot);
            });

            modelBuilder.Entity<Visit>(b => b.HasKey(v => v.Id));
            modelBuilder.Entity<AuditLog>(b => b.HasKey(a => a.Id));
        }

        private static NotSupportedException NotUsed()
            => new("DbSet not configured in Phase02IdentityModeAndPhiUnitTests.");
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private readonly Phase02UnitDbContext _db;

    public Phase02IdentityModeAndPhiUnitTests()
    {
        var options = new DbContextOptionsBuilder<Phase02UnitDbContext>()
            .UseInMemoryDatabase($"phase02-unit-{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _db = new Phase02UnitDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task IdentityHashMode_HmacOnly_DisablesShaFallbackAcrossReceptionProfileAndFamily_AndFailsClosedWithoutKey()
    {
        const string cccd = "001190123456";
        var shaOnlyHash = IdentificationNumber.ComputeSha256(cccd);
        var hmacOpts = Options.Create(new IdentityHashOptions
        {
            HashKey = HmacKey,
            Mode = "hmac",
        });

        var legacyUser = new User { Id = 1, Email = "legacy@medflow.test", AccountStatus = AccountStatus.ACTIVE };
        var legacyPatient = new Patient
        {
            Id = 10,
            UserId = legacyUser.Id,
            User = legacyUser,
            PatientCode = "PAT-LEGACY-10",
            FullName = "Nguyễn Văn Legacy",
            DateOfBirth = new DateOnly(1990, 1, 1),
            Gender = "MALE",
            Phone = "0912345678",
            Address = "10 Phố Huế, Hà Nội",
            IdentificationHash = shaOnlyHash,
            IdentificationHashHmac = null,
            Status = "ACTIVE",
        };
        var secondPatient = new Patient
        {
            Id = 11,
            PatientCode = "PAT-NEW-11",
            FullName = "Trần Thị Hmac",
            DateOfBirth = new DateOnly(1992, 2, 2),
            Gender = "FEMALE",
            Phone = "0987654321",
            Address = "20 Lê Lợi, Hà Nội",
            Status = "ACTIVE",
        };
        _db.Users.Add(legacyUser);
        _db.Patients.AddRange(legacyPatient, secondPatient);
        await _db.SaveChangesAsync();

        // 1. PatientProfileService in Mode="hmac" treats SHA-only row as NOT identity-verified
        var profileSvc = new PatientProfileService(_db, new NoopAuditLogger(), hashOptions: hmacOpts);
        var profile = await profileSvc.GetPersonalProfileAsync(legacyUser.Id);
        Assert.False(profile.IsIdentityVerified);

        // 2. UpdatePatientAtReceptionService without HashKey fails closed when IdentityCard is provided
        var missingKeyReceptionSvc = new UpdatePatientAtReceptionService(
            _db,
            hashOptions: Options.Create(new IdentityHashOptions { HashKey = "", Mode = "hmac" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            missingKeyReceptionSvc.UpdatePatientAsync(
                secondPatient.Id.ToString(),
                new UpdatePatientAtReceptionRequest
                {
                    FullName = "Trần Thị Hmac",
                    DateOfBirth = new DateOnly(1992, 2, 2),
                    Gender = "FEMALE",
                    Phone = "0987654321",
                    Address = "20 Lê Lợi, Hà Nội",
                    IdentityCard = cccd,
                }));

        // 3. UpdatePatientAtReceptionService in Mode="hmac" ignores SHA-only duplicate and writes ONLY IdentificationHashHmac (null IdentificationHash)
        var hmacReceptionSvc = new UpdatePatientAtReceptionService(_db, hashOptions: hmacOpts);
        await hmacReceptionSvc.UpdatePatientAsync(
            secondPatient.Id.ToString(),
            new UpdatePatientAtReceptionRequest
            {
                FullName = "Trần Thị Hmac",
                DateOfBirth = new DateOnly(1992, 2, 2),
                Gender = "FEMALE",
                Phone = "0987654321",
                Address = "20 Lê Lợi, Hà Nội",
                IdentityCard = cccd,
            });

        Assert.Null(secondPatient.IdentificationHash);
        Assert.Equal(IdentificationNumber.ComputeHmac(cccd, HmacKey), secondPatient.IdentificationHashHmac);
        Assert.Equal(IdentificationNumber.CurrentHashVersion, secondPatient.HashVersion);

        // 4. FamilyProfileService in Mode="hmac" ignores SHA-only row and writes null IdentificationHash on new patient
        var ownerUser = new User { Id = 2, Email = "owner@medflow.test", AccountStatus = AccountStatus.ACTIVE };
        var ownerPatient = new Patient
        {
            Id = 20,
            UserId = ownerUser.Id,
            User = ownerUser,
            PatientCode = "PAT-OWNER-20",
            FullName = "Chủ Hồ Sơ",
            Status = "ACTIVE",
        };
        const string shaOnlyRelativeCccd = "001195999888";
        var shaOnlyRelative = new Patient
        {
            Id = 21,
            PatientCode = "PAT-SHA-21",
            FullName = "Người Thân Sha",
            IdentificationHash = IdentificationNumber.ComputeSha256(shaOnlyRelativeCccd),
            IdentificationHashHmac = null,
            Status = "ACTIVE",
        };
        _db.Users.Add(ownerUser);
        _db.Patients.AddRange(ownerPatient, shaOnlyRelative);
        await _db.SaveChangesAsync();

        var familySvc = new FamilyProfileService(_db, hmacOpts);
        var created = await familySvc.CreateFamilyMemberAsync(ownerUser.Id, new CreateFamilyMemberRequest
        {
            FullName = "Người Thân Mới",
            Relationship = "Vợ",
            Gender = "Nữ",
            BirthDate = "1995-05-05",
            Address = "30 Hai Bà Trưng, Hà Nội",
            IdentificationNo = shaOnlyRelativeCccd,
        });

        Assert.NotEqual(shaOnlyRelative.Id, created.MemberPatientId);
        var newMember = await _db.Patients.FirstAsync(p => p.Id == created.MemberPatientId);
        Assert.Null(newMember.IdentificationHash);
        Assert.Equal(IdentificationNumber.ComputeHmac(shaOnlyRelativeCccd, HmacKey), newMember.IdentificationHashHmac);
    }

    [Fact]
    public async Task FamilyProfile_EmergencyContactsPersisted_ClearableContactFields_AndLocalVietnamRevokedAt()
    {
        var fixedUtc = new DateTimeOffset(2026, 11, 25, 8, 30, 0, TimeSpan.Zero); // 15:30 Vietnam time
        var clock = new FixedTimeProvider(fixedUtc);
        var expectedLocalNow = HospitalClock.GetNow(clock);

        var user = new User { Id = 50, Email = "guardian@medflow.test", AccountStatus = AccountStatus.ACTIVE };
        var owner = new Patient
        {
            Id = 500,
            UserId = user.Id,
            User = user,
            PatientCode = "PAT-500",
            FullName = "Phạm Văn Giám Hộ",
            DateOfBirth = new DateOnly(1988, 4, 10),
            Gender = "MALE",
            Phone = "0911000111",
            Status = "ACTIVE",
        };
        _db.Users.Add(user);
        _db.Patients.Add(owner);
        await _db.SaveChangesAsync();

        var svc = new FamilyProfileService(
            _db,
            Options.Create(new IdentityHashOptions { HashKey = HmacKey, Mode = "dual" }),
            clock);

        var created = await svc.CreateFamilyMemberAsync(user.Id, new CreateFamilyMemberRequest
        {
            FullName = "Phạm Minh Bé",
            Relationship = "Con gái",
            Gender = "Nữ",
            BirthDate = "2020-06-01",
            Address = "Số 10 Trần Hưng Đạo, Hà Nội",
            Phone = "0912345678",
            Email = "be@medflow.test",
            EmergencyContactName = "Phạm Thị Cô",
            EmergencyContactRelation = "Cô ruột",
            EmergencyPhone = "0988111222",
        });

        Assert.Equal("Phạm Thị Cô", created.EmergencyContactName);
        Assert.Equal("Cô ruột", created.EmergencyContactRelation);
        Assert.Equal("0988111222", created.EmergencyPhone);
        Assert.True(created.Permissions?.CanBookAppointments);
        Assert.False(created.Permissions?.CanViewMedicalRecords);

        // Clearing Phone and Email with "" sets both to null cleanly in DB
        var updated = await svc.UpdateFamilyMemberAsync(
            user.Id,
            created.MemberPatientId.ToString(),
            new UpdateFamilyMemberRequest
            {
                Phone = "",
                Email = "",
                Address = "Số 12 Trần Hưng Đạo, Hà Nội",
                EmergencyContactName = "",
                EmergencyContactRelation = "",
                EmergencyPhone = "",
            });

        var storedMember = await _db.Patients.FirstAsync(p => p.Id == created.MemberPatientId);
        Assert.Null(storedMember.Phone);
        Assert.Null(storedMember.Email);
        Assert.Null(storedMember.EmergencyContactName);
        Assert.Null(storedMember.EmergencyContactRelationship);
        Assert.Null(storedMember.EmergencyContactPhone);
        Assert.Null(updated.Phone);
        Assert.Null(updated.Email);
        Assert.Null(updated.EmergencyContactRelation);

        // RemoveFamilyMemberAsync records Vietnam local HospitalClock.GetNow (NOT UTC) in RevokedAt
        await svc.RemoveFamilyMemberAsync(
            user.Id,
            created.MemberPatientId.ToString(),
            "Hủy");
        var revokedLink = await _db.PatientFamilyLinks
            .FirstAsync(l => l.OwnerPatientId == owner.Id && l.MemberPatientId == created.MemberPatientId);
        Assert.Equal(expectedLocalNow, revokedLink.RevokedAt);
    }
}
