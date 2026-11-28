using System.Text;
using MedFlow.Application.DTOs.Ekyc;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Options;
using MedFlow.Application.Services.Ekyc;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace MedFlow.UnitTests.Ekyc;

/// <summary>
/// Slice-5 owner scope: CCCD-only finalize (document + card liveness; face/compare optional),
/// VERIFIED written only by finalize, reception-assisted sessions with performed_by, and
/// UNVERIFIED profile exceptions with mandatory reason + dedupe. In-memory, no network.
/// </summary>
public sealed class EkycOwnerScopeTests : IDisposable
{
    private const string TestPublicKey =
        "MFwwDQYJKoZIhvcNAQEBBQADSwAwSAJBAOeLFfK2OXWZ+y+7QjrjvzHUnE9kQFLkUPA+wcrEcEOB9oWPGqRg8TpDClzg275jmTJvLyU9M5E2KDFsmETs9dMCAwEAAQ==";
    private const string HmacKey = "test-hmac-key-owner-scope";
    private const string DeclaredId = "012345678901";

    private static readonly byte[] EncryptionKey = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
    private static readonly string EncryptionKeyBase64 = Convert.ToBase64String(EncryptionKey);

    private const string OcrDataBase64 =
        "eyJvYmplY3QiOnsiaWQiOiIwMTIzNDU2Nzg5MDEiLCJuYW1lIjoiTkdVWUVOIFZBTiBBTiIsImJpcnRoX2RheSI6IjE1LzA2LzE5OTUiLCJnZW5kZXIiOiJOYW0iLCJvcmlnaW5fbG9jYXRpb24iOiJIYSBOb2kiLCJyZWNlbnRfbG9jYXRpb24iOiJUUCBIbyBDaGkgTWluaCIsIm5hdGlvbmFsaXR5IjoiVmlldCBOYW0iLCJ2YWxpZF9kYXRlIjoiMTUvMDYvMjA0MCIsImlzc3VlX2RhdGUiOiIxNS8wNi8yMDI1IiwiaXNzdWVfcGxhY2UiOiJDdWMgQ2FuaCBzYXQgUUxIQyB2ZSBUVFhIIiwiaWRfcHJvYiI6Ijk5LjUiLCJuYW1lX3Byb2IiOiI5OC4yIiwiYmlydGhfZGF5X3Byb2IiOiI5OS4xIiwidGFtcGVyaW5nIjp7ImlzX2xlZ2FsIjoieWVzIiwid2FybmluZyI6W119fSwidHlwZV9kb2N1bWVudCI6OSwiY2hhbGxlbmdlQ29kZSI6IjExMTExIiwic2VydmVyX3ZlcnNpb24iOiIyLjEuMyJ9";
    private const string OcrDataSign = "nGMvNHpOq8BxWIjSf9nvRVG0hZbnxczUiopvMbukM/lefImyUBku7k9EAs93tSuR2DHvKVXf4sytblF6PAm7ng==";
    private const string LivenessDataBase64 = "eyJvYmplY3QiOnsibGl2ZW5lc3MiOiJzdWNjZXNzIiwibGl2ZW5lc3NfbXNnIjoiT0sifSwiY2hhbGxlbmdlQ29kZSI6IjExMTExIn0=";
    private const string LivenessDataSign = "4EXpAJxTZ+EwA63gAqh7eEUWiH4BBElfKILn50cSLarVKHJS+Nxo8tTDWPduyYWi3owbVrJ4ryOEtge2TcJMVg==";

    private sealed class TestDbContext(DbContextOptions<TestDbContext> options)
        : DbContext(options), IApplicationDbContext
    {
        public DbSet<User> Users => Set<User>();
        public DbSet<Role> Roles => Set<Role>();
        public DbSet<Permission> Permissions => Set<Permission>();
        public DbSet<UserRole> UserRoles => Set<UserRole>();
        public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
        public DbSet<Patient> Patients => Set<Patient>();
        public DbSet<Staff> Staff => Set<Staff>();
        public DbSet<Doctor> Doctors => Set<Doctor>();
        public DbSet<DoctorServiceAuthorization> DoctorServiceAuthorizations => Set<DoctorServiceAuthorization>();
        public DbSet<ExaminationSlot> ExaminationSlots => Set<ExaminationSlot>();
        public DbSet<AuthSession> AuthSessions => Set<AuthSession>();
        public DbSet<AuthVerificationChallenge> AuthVerificationChallenges => Set<AuthVerificationChallenge>();
        public DbSet<Hospital> Hospitals => Set<Hospital>();
        public DbSet<Department> Departments => Set<Department>();
        public DbSet<HospitalRoom> HospitalRooms => Set<HospitalRoom>();
        public DbSet<Specialty> Specialties => Set<Specialty>();
        public DbSet<Service> Services => Set<Service>();
        public DbSet<ServicePrice> ServicePrices => Set<ServicePrice>();
        public DbSet<HospitalPolicy> HospitalPolicies => Set<HospitalPolicy>();
        public DbSet<Appointment> Appointments => Set<Appointment>();
        public DbSet<PatientFamilyLink> PatientFamilyLinks => Set<PatientFamilyLink>();
        public DbSet<Visit> Visits => Set<Visit>();
        public DbSet<QueueEntry> QueueEntries => Set<QueueEntry>();
        public DbSet<Payment> Payments => Set<Payment>();
        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
        public DbSet<EkycVerificationSession> EkycVerificationSessions => Set<EkycVerificationSession>();
        public DbSet<EkycProxyCallLog> EkycProxyCallLogs => Set<EkycProxyCallLog>();

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default)
            => Database.BeginTransactionAsync(ct);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<User>().HasKey(u => u.Id);
            modelBuilder.Entity<User>().Property(u => u.Id).ValueGeneratedOnAdd();
            modelBuilder.Entity<User>().Ignore(u => u.Patient);
            modelBuilder.Entity<User>().Ignore(u => u.Staff);
            modelBuilder.Entity<User>().Ignore(u => u.UserRoles);
            modelBuilder.Entity<User>().Ignore(u => u.AuthSessions);

            modelBuilder.Entity<Role>().HasKey(r => r.Id);
            modelBuilder.Entity<Role>().Property(r => r.Id).ValueGeneratedOnAdd();
            modelBuilder.Entity<Permission>().HasKey(p => p.Id);
            modelBuilder.Entity<Permission>().Property(p => p.Id).ValueGeneratedOnAdd();
            modelBuilder.Entity<UserRole>().HasKey(ur => new { ur.UserId, ur.RoleId });
            modelBuilder.Entity<RolePermission>().HasKey(rp => new { rp.RoleId, rp.PermissionId });

            modelBuilder.Entity<Patient>().HasKey(p => p.Id);
            modelBuilder.Entity<Patient>().Property(p => p.Id).ValueGeneratedOnAdd();
            modelBuilder.Entity<Patient>().Ignore(p => p.User);

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

            modelBuilder.Entity<Staff>().HasKey(s => s.Id);
            modelBuilder.Entity<Staff>().Property(s => s.Id).ValueGeneratedOnAdd();
            modelBuilder.Entity<Staff>().Ignore(s => s.User);
            modelBuilder.Entity<Hospital>().HasKey(h => h.Id);
            modelBuilder.Entity<Hospital>().Property(h => h.Id).ValueGeneratedOnAdd();

            modelBuilder.Entity<AuditLog>().HasKey(a => a.Id);
            modelBuilder.Entity<AuditLog>().Property(a => a.Id).ValueGeneratedOnAdd();
            modelBuilder.Entity<AuditLog>().Ignore(a => a.ActorUser);

            modelBuilder.Entity<EkycVerificationSession>().HasKey(s => s.Id);
            modelBuilder.Entity<EkycVerificationSession>().Property(s => s.Id).ValueGeneratedOnAdd();
            modelBuilder.Entity<EkycProxyCallLog>().HasKey(l => l.Id);
            modelBuilder.Entity<EkycProxyCallLog>().Property(l => l.Id).ValueGeneratedOnAdd();
        }
    }

    private readonly TestDbContext _db;
    private readonly EkycVerificationService _verification;
    private readonly EkycSessionService _sessions;
    private readonly ReceptionPatientProfileService _profiles;
    private readonly EkycOptions _options;

    public EkycOwnerScopeTests()
    {
        var dbOptions = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase($"ekyc-owner-scope-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _db = new TestDbContext(dbOptions);

        _options = new EkycOptions
        {
            ConsentVersion = "ekyc-consent-v1",
            SessionTtlMinutes = 15,
            CompareThreshold = 70.0,
            TokenId = "tid",
            TokenKey = "tkey",
            AccessToken = "tok",
            PublicKeyCa = TestPublicKey,
            EncryptionKeyBase64 = EncryptionKeyBase64,
            HmacKey = HmacKey,
            DailySessionQuotaPerSubject = 3,
            DailySessionQuotaPerStaff = 20,
        };

        var wrapped = Options.Create(_options);
        _verification = new EkycVerificationService(_db, options: wrapped);
        _sessions = new EkycSessionService(_db, options: wrapped);
        _profiles = new ReceptionPatientProfileService(_db, ekycOptions: wrapped);

        SeedAuthority();
    }

    public void Dispose() => _db.Dispose();

    private void SeedAuthority()
    {
        _db.Hospitals.Add(new Hospital { Id = 1, Name = "BV Test", IsActive = true });

        _db.Roles.Add(new Role { Id = 1, Code = RoleNames.HospitalAdmin });
        _db.Roles.Add(new Role { Id = 2, Code = RoleNames.Reception });
        _db.Permissions.Add(new Permission { Id = 1, Code = PermissionCodes.PatientIdentityVerify });
        _db.RolePermissions.Add(new RolePermission { RoleId = 1, PermissionId = 1 });

        _db.Users.Add(new User
        {
            Id = 100,
            Email = "reception-admin@medflow.test",
            AccountStatus = AccountStatus.ACTIVE,
        });
        _db.UserRoles.Add(new UserRole { UserId = 100, RoleId = 1 });
        _db.Staff.Add(new Staff
        {
            Id = 10,
            UserId = 100,
            HospitalId = 1,
            StaffCode = "NV001",
            FullName = "Le Tan A",
            Status = "ACTIVE",
        });

        _db.Users.Add(new User
        {
            Id = 200,
            Email = "reception-no-perm@medflow.test",
            AccountStatus = AccountStatus.ACTIVE,
        });
        _db.UserRoles.Add(new UserRole { UserId = 200, RoleId = 2 });
        _db.Staff.Add(new Staff
        {
            Id = 20,
            UserId = 200,
            HospitalId = 1,
            StaffCode = "NV002",
            FullName = "Le Tan B",
            Status = "ACTIVE",
        });

        _db.SaveChanges();
    }

    private string Hmac(string id) => EkycCrypto.ComputeIdentificationHmac(id, HmacKey);

    private Patient SeedPatient(
        long id,
        string? userId = null,
        string? hmac = null,
        string identityStatus = PatientIdentityStatuses.Unverified,
        string? reason = null)
    {
        var patient = new Patient
        {
            Id = id,
            PatientCode = $"BN-2026-{id:D4}",
            FullName = "Nguyễn Văn An",
            DateOfBirth = new DateOnly(1995, 6, 15),
            Gender = "MALE",
            Phone = "0912345678",
            UserId = userId is null ? null : long.Parse(userId),
            IdentificationHashHmac = hmac,
            HashVersion = hmac is null ? null : 1,
            IdentityStatus = identityStatus,
            UnverifiedReason = reason,
            Status = "ACTIVE",
        };
        _db.Patients.Add(patient);
        _db.SaveChanges();
        return patient;
    }

    private EkycVerificationSession SeedSession(
        string mode,
        string purpose,
        long? userId,
        long? patientId,
        long? performedBy,
        string? declaredHmac,
        string status = EkycVerificationStatuses.Initiated)
    {
        var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        var session = new EkycVerificationSession
        {
            UserId = userId,
            PatientId = patientId,
            PerformedBy = performedBy,
            Status = status,
            VerificationMode = mode,
            Purpose = purpose,
            DeclaredFullName = "Nguyễn Văn An",
            DeclaredBirthDay = new DateOnly(1995, 6, 15),
            DeclaredIdentificationHmac = declaredHmac,
            BindingState = EkycBindingStates.Pending,
            ExpiresAt = now.AddMinutes(10),
            CreatedAt = now.AddMinutes(-5),
            UpdatedAt = now.AddMinutes(-5),
        };
        _db.EkycVerificationSessions.Add(session);
        _db.SaveChanges();
        return session;
    }

    private void SeedDocumentAndCardEvidence(long sessionId)
    {
        var baseTime = DateTime.SpecifyKind(DateTime.UtcNow.AddMinutes(-4), DateTimeKind.Unspecified);
        var entries = new (string Path, string B64, string Sign, int Order)[]
        {
            ("/ai/v1/web/ocr/id/front", OcrDataBase64, OcrDataSign, 1),
            ("/ai/v1/web/card/liveness", LivenessDataBase64, LivenessDataSign, 2),
        };

        foreach (var (path, b64, sign, order) in entries)
        {
            _db.EkycProxyCallLogs.Add(new EkycProxyCallLog
            {
                SessionId = sessionId,
                EndpointPath = path,
                HttpMethod = "POST",
                HttpStatus = 200,
                ResponseDataSign = sign,
                ResponsePayloadEncrypted = EkycCrypto.Encrypt(Encoding.UTF8.GetBytes(b64), EncryptionKey),
                CreatedAt = baseTime.AddSeconds(order * 10),
            });
        }

        _db.SaveChanges();
    }

    // ---------------------------------------------------------------
    // Document-only policy
    // ---------------------------------------------------------------

    [Fact]
    public async Task Finalize_DocumentAndCardOnly_VerifiesAndBindingNotApplicable()
    {
        _db.Users.Add(new User { Id = 7, Email = "patient@medflow.test", AccountStatus = AccountStatus.ACTIVE });
        var patient = SeedPatient(500, userId: "7", hmac: null);
        var session = SeedSession(
            EkycVerificationModes.SelfService, EkycPurposes.AccountActivation,
            userId: 7, patientId: patient.Id, performedBy: null, declaredHmac: Hmac(DeclaredId));
        SeedDocumentAndCardEvidence(session.Id);

        var result = await _verification.FinalizeAsync(7, session.Id, "127.0.0.1");

        Assert.Equal(EkycVerificationStatuses.Verified, result.Status);
        Assert.Equal(PatientIdentityStatuses.Verified, result.PatientIdentityStatus);

        var persisted = await _db.EkycVerificationSessions.FindAsync(session.Id);
        Assert.Equal(EkycBindingStates.NotApplicable, persisted!.BindingState);
        Assert.Null(persisted.CompareResult);

        var verifiedPatient = await _db.Patients.FindAsync(patient.Id);
        Assert.Equal(PatientIdentityStatuses.Verified, verifiedPatient!.IdentityStatus);
        Assert.Equal(Hmac(DeclaredId), verifiedPatient.IdentificationHashHmac);
    }

    // ---------------------------------------------------------------
    // Reception-assisted: performed_by, no account activation, patient VERIFIED only via finalize
    // ---------------------------------------------------------------

    [Fact]
    public async Task ReceptionSession_Finalize_VerifiesPatient_AndNeverActivatesAccount()
    {
        _db.Users.Add(new User
        {
            Id = 300,
            Email = "pending-patient@medflow.test",
            AccountStatus = AccountStatus.PENDING,
            EmailVerifiedAt = DateTime.UtcNow,
        });
        var patient = SeedPatient(600, userId: "300", hmac: Hmac(DeclaredId));

        var created = await _sessions.CreateReceptionAsync(100,
            new CreateReceptionEkycSessionRequest
            {
                PatientId = patient.Id,
                ConsentVersion = "ekyc-consent-v1",
                ConsentAccepted = true,
            }, "127.0.0.1", "agent");

        var sessionId = long.Parse(created.SessionId);
        SeedDocumentAndCardEvidence(sessionId);

        var result = await _verification.FinalizeAsync(100, sessionId, "127.0.0.1");

        Assert.Equal(EkycVerificationStatuses.Verified, result.Status);
        Assert.Equal(EkycPurposes.PatientProfile, result.Purpose);
        Assert.Equal(PatientIdentityStatuses.Verified, result.PatientIdentityStatus);
        Assert.False(result.AccountActivated);

        var session = await _db.EkycVerificationSessions.FindAsync(sessionId);
        Assert.Equal(EkycVerificationModes.ReceptionAssisted, session!.VerificationMode);
        Assert.Equal(100, session.PerformedBy);

        var verified = await _db.Patients.FindAsync(patient.Id);
        Assert.Equal(PatientIdentityStatuses.Verified, verified!.IdentityStatus);
        Assert.Null(verified.UnverifiedReason);

        // Account activation is untouched by patient identity verification.
        var account = await _db.Users.FindAsync(300L);
        Assert.Equal(AccountStatus.PENDING, account!.AccountStatus);
        Assert.False(await _db.AuditLogs.AnyAsync(a =>
            a.Action == EkycDedupeResolver.AuditActionAccountActivated && a.EntityId == sessionId.ToString()));
    }

    [Fact]
    public async Task ReceptionSession_FinalizeByOtherStaff_NotFound()
    {
        var patient = SeedPatient(601, hmac: Hmac(DeclaredId));
        var created = await _sessions.CreateReceptionAsync(100,
            new CreateReceptionEkycSessionRequest
            {
                PatientId = patient.Id,
                ConsentVersion = "ekyc-consent-v1",
                ConsentAccepted = true,
            }, null, null);
        var sessionId = long.Parse(created.SessionId);
        SeedDocumentAndCardEvidence(sessionId);

        await Assert.ThrowsAsync<NotFoundException>(() => _verification.FinalizeAsync(200, sessionId, null));
    }

    [Fact]
    public async Task ReceptionSession_RequiresPermission()
    {
        var patient = SeedPatient(602, hmac: Hmac(DeclaredId));

        await Assert.ThrowsAsync<ForbiddenException>(() => _sessions.CreateReceptionAsync(200,
            new CreateReceptionEkycSessionRequest
            {
                PatientId = patient.Id,
                ConsentVersion = "ekyc-consent-v1",
                ConsentAccepted = true,
            }, null, null));
    }

    [Fact]
    public async Task ReceptionSession_ForVerifiedPatient_Conflict()
    {
        var patient = SeedPatient(603, hmac: Hmac(DeclaredId), identityStatus: PatientIdentityStatuses.Verified);

        await Assert.ThrowsAsync<ConflictException>(() => _sessions.CreateReceptionAsync(100,
            new CreateReceptionEkycSessionRequest
            {
                PatientId = patient.Id,
                ConsentVersion = "ekyc-consent-v1",
                ConsentAccepted = true,
            }, null, null));
    }

    [Fact]
    public async Task ReceptionSession_PerformedByEqualsSubject_Forbidden()
    {
        var patient = SeedPatient(604, userId: "100", hmac: Hmac(DeclaredId));

        await Assert.ThrowsAsync<ForbiddenException>(() => _sessions.CreateReceptionAsync(100,
            new CreateReceptionEkycSessionRequest
            {
                PatientId = patient.Id,
                ConsentVersion = "ekyc-consent-v1",
                ConsentAccepted = true,
            }, null, null));
    }

    [Fact]
    public async Task ReceptionSession_IdentityConflictWithOtherProfile_Conflict()
    {
        SeedPatient(605, hmac: Hmac(DeclaredId));
        var target = SeedPatient(606, hmac: null);

        await Assert.ThrowsAsync<ConflictException>(() => _sessions.CreateReceptionAsync(100,
            new CreateReceptionEkycSessionRequest
            {
                PatientId = target.Id,
                ConsentVersion = "ekyc-consent-v1",
                ConsentAccepted = true,
                IdentityCard = DeclaredId,
            }, null, null));
    }

    // ---------------------------------------------------------------
    // Quota
    // ---------------------------------------------------------------

    [Fact]
    public async Task SelfServiceQuota_Exceeded_ReturnsQuotaCodeAndAudits()
    {
        _db.Users.Add(new User { Id = 77, Email = "quota@medflow.test", AccountStatus = AccountStatus.ACTIVE });
        _db.SaveChanges();

        var today = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified).Date.AddHours(1);
        for (var i = 0; i < 3; i++)
        {
            _db.EkycVerificationSessions.Add(new EkycVerificationSession
            {
                UserId = 77,
                Status = EkycVerificationStatuses.Rejected,
                VerificationMode = EkycVerificationModes.SelfService,
                Purpose = EkycPurposes.AccountActivation,
                ExpiresAt = today.AddMinutes(15),
                CreatedAt = today,
                UpdatedAt = today,
            });
        }

        _db.SaveChanges();

        var ex = await Assert.ThrowsAsync<EkycServiceException>(() => _sessions.CreateAsync(77,
            new CreateEkycSessionRequest
            {
                ConsentVersion = "ekyc-consent-v1",
                ConsentAccepted = true,
                IdentityCard = DeclaredId,
            }, null, null));

        Assert.Equal(EkycErrorCodes.QuotaExhausted, ex.Code);
        Assert.Equal(429, ex.StatusCode);
        Assert.True(await _db.AuditLogs.AnyAsync(a => a.Action == EkycConstants.AuditActionQuotaExceeded));
        // The audit of the failure survives the thrown exception.
        Assert.Equal(3, await _db.EkycVerificationSessions.CountAsync(s => s.UserId == 77));
    }

    [Fact]
    public async Task ReceptionQuota_PerStaff_Exceeded_ReturnsQuotaCode()
    {
        _options.DailySessionQuotaPerStaff = 1;
        var patient = SeedPatient(700, hmac: Hmac(DeclaredId));

        var today = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified).Date.AddHours(1);
        _db.EkycVerificationSessions.Add(new EkycVerificationSession
        {
            PatientId = patient.Id,
            Status = EkycVerificationStatuses.Rejected,
            VerificationMode = EkycVerificationModes.ReceptionAssisted,
            Purpose = EkycPurposes.PatientProfile,
            PerformedBy = 100,
            ExpiresAt = today.AddMinutes(15),
            CreatedAt = today,
            UpdatedAt = today,
        });
        _db.SaveChanges();

        var ex = await Assert.ThrowsAsync<EkycServiceException>(() => _sessions.CreateReceptionAsync(100,
            new CreateReceptionEkycSessionRequest
            {
                PatientId = patient.Id,
                ConsentVersion = "ekyc-consent-v1",
                ConsentAccepted = true,
            }, null, null));

        Assert.Equal(EkycErrorCodes.QuotaExhausted, ex.Code);
    }

    // ---------------------------------------------------------------
    // UNVERIFIED creation exceptions
    // ---------------------------------------------------------------

    [Fact]
    public async Task UnverifiedProfile_RequiresReason()
    {
        await Assert.ThrowsAsync<ValidationException>(() => _profiles.CreateUnverifiedAsync(100,
            new CreateUnverifiedPatientProfileRequest
            {
                FullName = "Bé Minh",
                DateOfBirth = new DateOnly(2020, 1, 1),
            }, null));
    }

    [Fact]
    public async Task UnverifiedProfile_ReasonRequiringCard_RequiresIdentity()
    {
        await Assert.ThrowsAsync<ValidationException>(() => _profiles.CreateUnverifiedAsync(100,
            new CreateUnverifiedPatientProfileRequest
            {
                FullName = "Nguyễn Văn Không Giấy Tờ",
                DateOfBirth = new DateOnly(1990, 2, 2),
                Reason = PatientUnverifiedReasons.VnptUnavailable,
            }, null));
    }

    [Fact]
    public async Task UnverifiedProfile_DuplicateIdentity_ConflictBeforeCreate()
    {
        SeedPatient(800, hmac: Hmac(DeclaredId));

        await Assert.ThrowsAsync<ConflictException>(() => _profiles.CreateUnverifiedAsync(100,
            new CreateUnverifiedPatientProfileRequest
            {
                FullName = "Nguyễn Văn Trùng",
                DateOfBirth = new DateOnly(1990, 2, 2),
                IdentityCard = DeclaredId,
                Reason = PatientUnverifiedReasons.QuotaExhausted,
            }, null));
    }

    [Fact]
    public async Task UnverifiedProfile_CreatesNeverVerified_AndAuditsReason()
    {
        var result = await _profiles.CreateUnverifiedAsync(100,
            new CreateUnverifiedPatientProfileRequest
            {
                FullName = "Bé Minh",
                DateOfBirth = new DateOnly(2020, 1, 1),
                Reason = PatientUnverifiedReasons.Child,
                Note = "Trẻ em dưới 6 tuổi",
            }, "127.0.0.1");

        Assert.Equal(PatientIdentityStatuses.Unverified, result.IdentityStatus);
        Assert.Equal(PatientUnverifiedReasons.Child, result.UnverifiedReason);

        var patient = await _db.Patients.FindAsync(result.PatientId);
        Assert.Equal(PatientIdentityStatuses.Unverified, patient!.IdentityStatus);
        Assert.Equal(PatientUnverifiedReasons.Child, patient.UnverifiedReason);

        var audit = await _db.AuditLogs.FirstAsync(a =>
            a.Action == EkycConstants.AuditActionPatientProfileCreatedUnverified);
        Assert.Equal(100, audit.ActorUserId);
        Assert.Contains("CHILD", audit.ChangeData);
        Assert.Contains("reason", audit.ChangeData);
    }

    [Fact]
    public async Task UnverifiedProfile_RequiresPermission()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() => _profiles.CreateUnverifiedAsync(200,
            new CreateUnverifiedPatientProfileRequest
            {
                FullName = "Bé Minh",
                DateOfBirth = new DateOnly(2020, 1, 1),
                Reason = PatientUnverifiedReasons.Child,
            }, null));
    }

    // ---------------------------------------------------------------
    // Self-service session requires declared CCCD
    // ---------------------------------------------------------------

    [Fact]
    public async Task SelfServiceSession_WithoutDeclaredCccd_Rejected()
    {
        _db.Users.Add(new User { Id = 88, Email = "nocccd@medflow.test", AccountStatus = AccountStatus.ACTIVE });
        _db.SaveChanges();

        await Assert.ThrowsAsync<ValidationException>(() => _sessions.CreateAsync(88,
            new CreateEkycSessionRequest
            {
                ConsentVersion = "ekyc-consent-v1",
                ConsentAccepted = true,
            }, null, null));
    }
}
