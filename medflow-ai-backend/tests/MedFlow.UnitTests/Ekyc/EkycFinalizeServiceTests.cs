using System.Text;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Options;
using MedFlow.Application.Services.Ekyc;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace MedFlow.UnitTests.Ekyc;

/// <summary>
/// Service-level finalize security tests through <see cref="EkycVerificationService.FinalizeAsync"/>:
/// ownership, expired, replay/re-finalize, terminal state, cross-account isolation.
/// Plus the race test: concurrent finalize — exactly one winner.
/// </summary>
public sealed class EkycFinalizeServiceTests : IDisposable
{
    private const string TestPublicKey =
        "MFwwDQYJKoZIhvcNAQEBBQADSwAwSAJBAOeLFfK2OXWZ+y+7QjrjvzHUnE9kQFLkUPA+wcrEcEOB9oWPGqRg8TpDClzg275jmTJvLyU9M5E2KDFsmETs9dMCAwEAAQ==";
    private const string HmacKey = "test-hmac-key-service-level";

    private static readonly byte[] EncryptionKey = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
    private static readonly string EncryptionKeyBase64 = Convert.ToBase64String(EncryptionKey);

    private const string OcrDataBase64 =
        "eyJvYmplY3QiOnsiaWQiOiIwMTIzNDU2Nzg5MDEiLCJuYW1lIjoiTkdVWUVOIFZBTiBBTiIsImJpcnRoX2RheSI6IjE1LzA2LzE5OTUiLCJnZW5kZXIiOiJOYW0iLCJvcmlnaW5fbG9jYXRpb24iOiJIYSBOb2kiLCJyZWNlbnRfbG9jYXRpb24iOiJUUCBIbyBDaGkgTWluaCIsIm5hdGlvbmFsaXR5IjoiVmlldCBOYW0iLCJ2YWxpZF9kYXRlIjoiMTUvMDYvMjA0MCIsImlzc3VlX2RhdGUiOiIxNS8wNi8yMDI1IiwiaXNzdWVfcGxhY2UiOiJDdWMgQ2FuaCBzYXQgUUxIQyB2ZSBUVFhIIiwiaWRfcHJvYiI6Ijk5LjUiLCJuYW1lX3Byb2IiOiI5OC4yIiwiYmlydGhfZGF5X3Byb2IiOiI5OS4xIiwidGFtcGVyaW5nIjp7ImlzX2xlZ2FsIjoieWVzIiwid2FybmluZyI6W119fSwidHlwZV9kb2N1bWVudCI6OSwiY2hhbGxlbmdlQ29kZSI6IjExMTExIiwic2VydmVyX3ZlcnNpb24iOiIyLjEuMyJ9";
    private const string OcrDataSign = "nGMvNHpOq8BxWIjSf9nvRVG0hZbnxczUiopvMbukM/lefImyUBku7k9EAs93tSuR2DHvKVXf4sytblF6PAm7ng==";
    private const string LivenessDataBase64 = "eyJvYmplY3QiOnsibGl2ZW5lc3MiOiJzdWNjZXNzIiwibGl2ZW5lc3NfbXNnIjoiT0sifSwiY2hhbGxlbmdlQ29kZSI6IjExMTExIn0=";
    private const string LivenessDataSign = "4EXpAJxTZ+EwA63gAqh7eEUWiH4BBElfKILn50cSLarVKHJS+Nxo8tTDWPduyYWi3owbVrJ4ryOEtge2TcJMVg==";
    private const string FaceDataBase64 = "eyJvYmplY3QiOnsibGl2ZW5lc3MiOiJzdWNjZXNzIiwibGl2ZW5lc3NfbXNnIjoiT0siLCJpc19leWVfb3BlbiI6dHJ1ZX0sImNoYWxsZW5nZUNvZGUiOiIxMTExMSJ9";
    private const string FaceDataSign = "hxdOSM6/nqMTVdMswWgT4LF/nAU+fa8zxCXwujCzXPLqTJbvjmPHfHBxYLCJmpeS4UV5R9USKHELypE0uzA9hg==";
    private const string CompareDataBase64 = "eyJvYmplY3QiOnsicmVzdWx0Ijoic3VjY2VzcyIsIm1zZyI6Im1hdGNoIiwicHJvYiI6IjkyLjQ1In0sImNoYWxsZW5nZUNvZGUiOiIxMTExMSJ9";
    private const string CompareDataSign = "OR+AkdVdRy7pOAljzxUeeQfIyZ/GiUScAlY0Ow++DGJ1M0Njoha6JJete2SSYZTy5OxmhBhtU83diMhqwARWcQ==";

    private sealed class FinalizeTestDbContext(DbContextOptions<FinalizeTestDbContext> options)
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
        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
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
        public DbSet<EkycVerificationSession> EkycVerificationSessions => Set<EkycVerificationSession>();
        public DbSet<EkycProxyCallLog> EkycProxyCallLogs => Set<EkycProxyCallLog>();

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default)
            => Database.BeginTransactionAsync(ct);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<EkycVerificationSession>().HasKey(s => s.Id);
            modelBuilder.Entity<EkycVerificationSession>().Property(s => s.Id).ValueGeneratedOnAdd();
            modelBuilder.Entity<EkycProxyCallLog>().HasKey(l => l.Id);
            modelBuilder.Entity<EkycProxyCallLog>().Property(l => l.Id).ValueGeneratedOnAdd();
            modelBuilder.Entity<Patient>().HasKey(p => p.Id);
            modelBuilder.Entity<Patient>().Property(p => p.Id).ValueGeneratedOnAdd();
            modelBuilder.Entity<AuditLog>().HasKey(a => a.Id);
            modelBuilder.Entity<AuditLog>().Property(a => a.Id).ValueGeneratedOnAdd();
            modelBuilder.Entity<User>().HasKey(u => u.Id);
            modelBuilder.Entity<User>().Property(u => u.Id).ValueGeneratedOnAdd();
            modelBuilder.Entity<User>().Ignore(u => u.Patient);
            modelBuilder.Entity<User>().Ignore(u => u.Staff);
            modelBuilder.Entity<User>().Ignore(u => u.UserRoles);
            modelBuilder.Entity<User>().Ignore(u => u.AuthSessions);
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
        }
    }

    private readonly FinalizeTestDbContext _db;
    private readonly EkycVerificationService _service;
    private readonly string _dbName = $"ekyc-finalize-{Guid.NewGuid()}";

    public EkycFinalizeServiceTests()
    {
        var options = new DbContextOptionsBuilder<FinalizeTestDbContext>()
            .UseInMemoryDatabase(_dbName)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _db = new FinalizeTestDbContext(options);

        var ekycOptions = Options.Create(new EkycOptions
        {
            BackendUrl = "https://api.idg.vnpt.vn",
            CompareThreshold = 70.0,
            SessionTtlMinutes = 15,
            MaxAttempts = 5,
            TokenId = "tid",
            TokenKey = "tkey",
            AccessToken = "tok",
            PublicKeyCa = TestPublicKey,
            EncryptionKeyBase64 = EncryptionKeyBase64,
            HmacKey = HmacKey,
        });

        _service = new EkycVerificationService(_db, options: ekycOptions);
    }

    public void Dispose() => _db.Dispose();

    private EkycVerificationSession SeedSession(
        long userId = 7,
        string status = EkycVerificationStatuses.Initiated,
        string bindingState = EkycBindingStates.Valid,
        DateTime? expiresAt = null)
    {
        var session = new EkycVerificationSession
        {
            UserId = userId,
            Status = status,
            VerificationMethod = EkycVerificationMethods.EkycVnpt,
            BindingState = bindingState,
            ExpiresAt = expiresAt ?? DateTime.SpecifyKind(DateTime.UtcNow.AddMinutes(10), DateTimeKind.Unspecified),
            CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow.AddMinutes(-5), DateTimeKind.Unspecified),
            UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow.AddMinutes(-5), DateTimeKind.Unspecified),
        };
        _db.EkycVerificationSessions.Add(session);
        _db.SaveChanges();
        return session;
    }

    private void SeedEvidence(long sessionId)
    {
        var baseTime = DateTime.SpecifyKind(DateTime.UtcNow.AddMinutes(-4), DateTimeKind.Unspecified);
        var entries = new (string Path, string B64, string Sign, int Order)[]
        {
            ("/ai/v1/web/ocr/id/front", OcrDataBase64, OcrDataSign, 1),
            ("/ai/v1/web/card/liveness", LivenessDataBase64, LivenessDataSign, 2),
            ("/ai/v1/web/face/liveness", FaceDataBase64, FaceDataSign, 3),
            ("/ai/v1/web/face/compare", CompareDataBase64, CompareDataSign, 4),
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
    // Happy path end-to-end through FinalizeAsync
    // ---------------------------------------------------------------

    [Fact]
    public async Task Finalize_ValidFlow_SetsVerifiedConsumedEvidence()
    {
        var session = SeedSession();
        SeedEvidence(session.Id);

        var result = await _service.FinalizeAsync(7, session.Id, "127.0.0.1");

        Assert.Equal(EkycVerificationStatuses.Verified, result.Status);
        Assert.NotNull(result.ConsumedAt);

        var persisted = await _db.EkycVerificationSessions.FindAsync(session.Id);
        Assert.Equal(EkycVerificationStatuses.Verified, persisted!.Status);
        Assert.NotNull(persisted.ConsumedAt);
        Assert.NotNull(persisted.OcrSnapshot);
        Assert.NotNull(persisted.IdentificationHmac);
        Assert.NotNull(persisted.EvidenceDataSign);
        Assert.NotNull(persisted.EvidencePayloadFull);

        // Audit row written.
        Assert.True(await _db.AuditLogs.AnyAsync(a => a.Action == EkycConstants.AuditActionVerified));
    }

    // ---------------------------------------------------------------
    // Security: ownership
    // ---------------------------------------------------------------

    [Fact]
    public async Task Finalize_ForeignSession_NotFound()
    {
        var session = SeedSession(userId: 7);
        SeedEvidence(session.Id);

        // Different user must not even learn the session exists.
        await Assert.ThrowsAsync<NotFoundException>(
            () => _service.FinalizeAsync(999, session.Id, "127.0.0.1"));
    }

    // ---------------------------------------------------------------
    // Security: replay / re-finalize
    // ---------------------------------------------------------------

    [Fact]
    public async Task Finalize_ReplayAfterVerified_Rejected()
    {
        var session = SeedSession();
        SeedEvidence(session.Id);

        await _service.FinalizeAsync(7, session.Id, "127.0.0.1");

        // Second finalize — session is terminal/consumed.
        await Assert.ThrowsAsync<ConflictException>(
            () => _service.FinalizeAsync(7, session.Id, "127.0.0.1"));
    }

    [Fact]
    public async Task Finalize_RejectedSessionCannotRefinalize()
    {
        var session = SeedSession(bindingState: EkycBindingStates.Invalid);
        SeedEvidence(session.Id);

        var first = await _service.FinalizeAsync(7, session.Id, "127.0.0.1");
        Assert.Equal(EkycVerificationStatuses.Rejected, first.Status);

        await Assert.ThrowsAsync<ConflictException>(
            () => _service.FinalizeAsync(7, session.Id, "127.0.0.1"));
    }

    // ---------------------------------------------------------------
    // Security: expired session
    // ---------------------------------------------------------------

    [Fact]
    public async Task Finalize_ExpiredSession_Rejected()
    {
        var session = SeedSession(expiresAt: DateTime.SpecifyKind(DateTime.UtcNow.AddMinutes(-1), DateTimeKind.Unspecified));
        SeedEvidence(session.Id);

        await Assert.ThrowsAsync<ConflictException>(
            () => _service.FinalizeAsync(7, session.Id, "127.0.0.1"));

        var persisted = await _db.EkycVerificationSessions.FindAsync(session.Id);
        Assert.Equal(EkycVerificationStatuses.Expired, persisted!.Status);
    }

    // ---------------------------------------------------------------
    // Race test: concurrent finalize — exactly one winner
    // ---------------------------------------------------------------

    [Fact]
    public async Task Finalize_SequentialSecondCall_AlwaysRejected()
    {
        // Unit-level invariant: even without database locking, a second finalize AFTER the first
        // committed is always rejected — the terminal-status guard is the last line of defense.
        // The true concurrent-race serialization (FOR UPDATE row lock) can only be validated
        // against real PostgreSQL — covered by integration tests, where two simultaneous finalize
        // requests must yield exactly one VERIFIED and one 409.
        var session = SeedSession();
        SeedEvidence(session.Id);

        var first = await _service.FinalizeAsync(7, session.Id, "127.0.0.1");
        Assert.Equal(EkycVerificationStatuses.Verified, first.Status);

        await Assert.ThrowsAsync<ConflictException>(
            () => _service.FinalizeAsync(7, session.Id, "127.0.0.1"));

        // consumed_at set exactly once — never overwritten.
        var persisted = await _db.EkycVerificationSessions.FindAsync(session.Id);
        Assert.Equal(first.ConsumedAt, persisted!.ConsumedAt);
    }
}
