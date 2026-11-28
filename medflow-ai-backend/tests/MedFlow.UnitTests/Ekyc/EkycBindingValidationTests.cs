using System.Net;
using System.Text;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Options;
using MedFlow.Application.Services.Ekyc;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace MedFlow.UnitTests.Ekyc;

/// <summary>
/// Binding validation logic: compare document hash match/mismatch/unparseable — fail-closed.
/// Uses a stub HttpMessageHandler so no real network traffic occurs.
/// </summary>
public sealed class EkycBindingValidationTests : IDisposable
{
    private sealed class EkycBindingDbContext(DbContextOptions<EkycBindingDbContext> options)
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
            modelBuilder.Ignore<Role>();
            modelBuilder.Ignore<Permission>();
            modelBuilder.Ignore<UserRole>();
            modelBuilder.Ignore<RolePermission>();
            modelBuilder.Ignore<Patient>();
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
            modelBuilder.Ignore<User>();
            modelBuilder.Ignore<AuditLog>();
        }
    }

    /// <summary>Stub upstream handler — returns canned responses per request path, records what it saw.</summary>
    private sealed class StubVnptHandler : HttpMessageHandler
    {
        public readonly List<(string Path, string? Authorization, string? TokenId, string? TokenKey, string? Origin)> Seen = [];
        public Func<string, HttpResponseMessage> Responder = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            var auth = request.Headers.Authorization?.ToString();
            var tokenId = request.Headers.TryGetValues("Token-id", out var tid) ? string.Join(",", tid) : null;
            var tokenKey = request.Headers.TryGetValues("Token-key", out var tk) ? string.Join(",", tk) : null;
            var origin = request.Headers.TryGetValues("Origin", out var og) ? string.Join(",", og) : null;
            Seen.Add((path, auth, tokenId, tokenKey, origin));
            return Task.FromResult(Responder(path));
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private readonly EkycBindingDbContext _db;
    private readonly StubVnptHandler _handler = new();
    private readonly EkycProxyService _proxy;

    public EkycBindingValidationTests()
    {
        var options = new DbContextOptionsBuilder<EkycBindingDbContext>()
            .UseInMemoryDatabase($"ekyc-binding-{Guid.NewGuid()}")
            .Options;
        _db = new EkycBindingDbContext(options);

        var ekycOptions = Options.Create(new EkycOptions
        {
            BackendUrl = "https://stub-vnpt.test",
            CompareThreshold = 70.0,
            SessionTtlMinutes = 15,
            MaxAttempts = 5,
            TokenId = "test-token-id",
            TokenKey = "test-token-key",
            AccessToken = "test-access-token",
            PublicKeyCa = "test-ca",
        });

        _proxy = new EkycProxyService(
            _db,
            new StubHttpClientFactory(_handler),
            options: ekycOptions);
    }

    public void Dispose() => _db.Dispose();

    private async Task<long> SeedSessionAsync()
    {
        var session = new EkycVerificationSession
        {
            UserId = 10,
            Status = EkycVerificationStatuses.Initiated,
            VerificationMode = EkycVerificationModes.SelfService,
            Purpose = EkycPurposes.AccountActivation,
            ExpiresAt = DateTime.UtcNow.AddMinutes(15),
            BindingState = EkycBindingStates.Pending,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        _db.EkycVerificationSessions.Add(session);
        await _db.SaveChangesAsync();
        return session.Id;
    }

    private async Task SeedUploadedHashAsync(long sessionId, string hash)
    {
        _db.EkycProxyCallLogs.Add(new EkycProxyCallLog
        {
            SessionId = sessionId,
            EndpointPath = "/file-service/v1/addFile",
            HttpMethod = "POST",
            HttpStatus = 200,
            ResponseObjectHash = hash,
            CreatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();
    }

    private static byte[] CompareBody(string documentHash)
        => Encoding.UTF8.GetBytes("{\"img_front\":\"" + documentHash + "\",\"img\":\"idg20261127/IDG01_faceimg\"}");

    // ---------------------------------------------------------------
    // Forwarding behavior
    // ---------------------------------------------------------------

    [Fact]
    public async Task Forward_InjectsCredentials_AndNeverSetsOrigin()
    {
        var sessionId = await SeedSessionAsync();

        await _proxy.ForwardAsync(10, sessionId, "ai/v1/web/card/liveness", "POST", "application/json",
            Encoding.UTF8.GetBytes("""{"img":"x"}"""), CancellationToken.None);

        var seen = Assert.Single(_handler.Seen);
        Assert.Equal("Bearer test-access-token", seen.Authorization);
        Assert.Equal("test-token-id", seen.TokenId);
        Assert.Equal("test-token-key", seen.TokenKey);
        Assert.Null(seen.Origin);
    }

    [Fact]
    public async Task Forward_LogsMetadata_WithoutPayloads()
    {
        var sessionId = await SeedSessionAsync();

        await _proxy.ForwardAsync(10, sessionId, "ai/v1/web/card/liveness", "POST", "application/json",
            Encoding.UTF8.GetBytes("""{"img":"idg20261127/IDG01_ref1"}"""), CancellationToken.None);

        var log = await _db.EkycProxyCallLogs.FirstAsync(l => l.SessionId == sessionId);
        Assert.Equal("/ai/v1/web/card/liveness", log.EndpointPath);
        Assert.Equal(200, log.HttpStatus);
        Assert.NotNull(log.RequestObjectHashes);
        Assert.Contains("idg20261127/IDG01_ref1", log.RequestObjectHashes);
        // Metadata must never contain image payloads — only short hash refs.
        Assert.True(log.RequestObjectHashes!.Length < 512);
    }

    [Fact]
    public async Task Forward_AddFile_ExtractsResponseObjectHash()
    {
        var sessionId = await SeedSessionAsync();
        _handler.Responder = path => path.Contains("addFile")
            ? new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"object":{"hash":"idg20261127/IDG01_uploaded1"}}""", Encoding.UTF8, "application/json"),
            }
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };

        await _proxy.ForwardAsync(10, sessionId, "file-service/v1/addFile", "POST", "multipart/form-data",
            Encoding.UTF8.GetBytes("fake-multipart"), CancellationToken.None);

        var log = await _db.EkycProxyCallLogs.FirstAsync(l => l.SessionId == sessionId);
        Assert.Equal("idg20261127/IDG01_uploaded1", log.ResponseObjectHash);
    }

    // ---------------------------------------------------------------
    // Binding validation: match / mismatch / unparseable
    // ---------------------------------------------------------------

    [Fact]
    public async Task Binding_Valid_WhenCompareReferencesUploadedHash()
    {
        var sessionId = await SeedSessionAsync();
        await SeedUploadedHashAsync(sessionId, "idg20261127/IDG01_docfront");

        await _proxy.ForwardAsync(10, sessionId, "ai/v1/web/face/compare", "POST", "application/json",
            CompareBody("idg20261127/IDG01_docfront"), CancellationToken.None);

        var session = await _db.EkycVerificationSessions.FirstAsync(s => s.Id == sessionId);
        Assert.Equal(EkycBindingStates.Valid, session.BindingState);
        Assert.Equal("idg20261127/IDG01_docfront", session.CompareReferenceHash);
    }

    [Fact]
    public async Task Binding_Invalid_WhenCompareReferencesForeignHash()
    {
        var sessionId = await SeedSessionAsync();
        await SeedUploadedHashAsync(sessionId, "idg20261127/IDG01_docfront");

        // Composite attack: compare references a document hash NEVER uploaded in this session.
        await _proxy.ForwardAsync(10, sessionId, "ai/v1/web/face/compare", "POST", "application/json",
            CompareBody("idg20261127/IDG01_FOREIGN"), CancellationToken.None);

        var session = await _db.EkycVerificationSessions.FirstAsync(s => s.Id == sessionId);
        Assert.Equal(EkycBindingStates.Invalid, session.BindingState);
    }

    [Fact]
    public async Task Binding_Unverifiable_WhenCompareBodyUnparseable()
    {
        var sessionId = await SeedSessionAsync();
        await SeedUploadedHashAsync(sessionId, "idg20261127/IDG01_docfront");

        // Compare body without any document reference field — cannot prove binding.
        await _proxy.ForwardAsync(10, sessionId, "ai/v1/web/face/compare", "POST", "application/json",
            Encoding.UTF8.GetBytes("""{"client_session":"x"}"""), CancellationToken.None);

        var session = await _db.EkycVerificationSessions.FirstAsync(s => s.Id == sessionId);
        Assert.Equal(EkycBindingStates.Unverifiable, session.BindingState);
    }

    [Fact]
    public async Task Binding_Invalid_IsSticky()
    {
        var sessionId = await SeedSessionAsync();
        await SeedUploadedHashAsync(sessionId, "idg20261127/IDG01_docfront");

        // First compare: foreign hash → INVALID
        await _proxy.ForwardAsync(10, sessionId, "ai/v1/web/face/compare", "POST", "application/json",
            CompareBody("idg20261127/IDG01_FOREIGN"), CancellationToken.None);

        // Second compare: correct hash — must NOT heal the state (sticky fail-closed)
        await _proxy.ForwardAsync(10, sessionId, "ai/v1/web/face/compare", "POST", "application/json",
            CompareBody("idg20261127/IDG01_docfront"), CancellationToken.None);

        var session = await _db.EkycVerificationSessions.FirstAsync(s => s.Id == sessionId);
        Assert.Equal(EkycBindingStates.Invalid, session.BindingState);
    }

    [Fact]
    public async Task Binding_Invalid_WhenNoUploadsAtAll()
    {
        var sessionId = await SeedSessionAsync();
        // No addFile uploads — any compare reference is unprovable as session-owned → INVALID
        // (the hash parsed fine, but matches nothing).

        await _proxy.ForwardAsync(10, sessionId, "ai/v1/web/face/compare", "POST", "application/json",
            CompareBody("idg20261127/IDG01_anything"), CancellationToken.None);

        var session = await _db.EkycVerificationSessions.FirstAsync(s => s.Id == sessionId);
        Assert.Equal(EkycBindingStates.Invalid, session.BindingState);
    }

    [Fact]
    public async Task Forward_RejectsPathTraversal()
    {
        var sessionId = await SeedSessionAsync();

        await Assert.ThrowsAsync<MedFlow.Domain.Exceptions.ValidationException>(
            () => _proxy.ForwardAsync(10, sessionId, "../etc/passwd", "POST", null, [], CancellationToken.None));

        Assert.Empty(_handler.Seen); // nothing forwarded
    }
}
