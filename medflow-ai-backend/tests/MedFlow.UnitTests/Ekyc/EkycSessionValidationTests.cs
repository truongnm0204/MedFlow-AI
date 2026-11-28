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
/// Unit tests for session creation consent gate, ownership/terminal/expiry validation
/// (proxy forwarding itself is covered by the integration slice).
/// </summary>
public sealed class EkycSessionValidationTests : IDisposable
{
    private sealed class EkycTestDbContext(DbContextOptions<EkycTestDbContext> options)
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
            modelBuilder.Entity<User>().HasKey(u => u.Id);
            modelBuilder.Entity<User>().Property(u => u.Id).ValueGeneratedOnAdd();
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
            modelBuilder.Entity<EkycVerificationSession>().HasKey(s => s.Id);
            modelBuilder.Entity<EkycVerificationSession>().Property(s => s.Id).ValueGeneratedOnAdd();
            modelBuilder.Entity<EkycProxyCallLog>().HasKey(l => l.Id);
            modelBuilder.Entity<EkycProxyCallLog>().Property(l => l.Id).ValueGeneratedOnAdd();
            modelBuilder.Entity<AuditLog>().HasKey(a => a.Id);
            modelBuilder.Entity<AuditLog>().Property(a => a.Id).ValueGeneratedOnAdd();
        }
    }

    private readonly EkycTestDbContext _db;
    private readonly EkycSessionService _sessionService;

    public EkycSessionValidationTests()
    {
        var options = new DbContextOptionsBuilder<EkycTestDbContext>()
            .UseInMemoryDatabase($"ekyc-test-{Guid.NewGuid()}")
            .Options;
        _db = new EkycTestDbContext(options);

        _db.Users.Add(new User
        {
            Id = 10,
            Email = "patient@medflow.test",
            AccountStatus = AccountStatus.ACTIVE,
        });
        _db.Users.Add(new User
        {
            Id = 11,
            Email = "locked@medflow.test",
            AccountStatus = AccountStatus.LOCKED,
        });
        _db.SaveChanges();

        var ekycOptions = Options.Create(new EkycOptions
        {
            ConsentVersion = "ekyc-consent-v1",
            SessionTtlMinutes = 15,
        });
        _sessionService = new EkycSessionService(_db, options: ekycOptions);
    }

    public void Dispose() => _db.Dispose();

    private static CreateEkycSessionRequest ValidConsent() => new()
    {
        ConsentVersion = "ekyc-consent-v1",
        ConsentAccepted = true,
    };

    // ---------------------------------------------------------------
    // Consent gate
    // ---------------------------------------------------------------

    [Fact]
    public async Task Create_Rejects_WhenConsentNotAccepted()
    {
        var request = new CreateEkycSessionRequest { ConsentVersion = "ekyc-consent-v1", ConsentAccepted = false };

        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => _sessionService.CreateAsync(10, request, "127.0.0.1", "agent"));

        Assert.Equal(EkycSessionService.ConsentRequiredMessage, ex.Message);
    }

    [Fact]
    public async Task Create_Rejects_WhenConsentVersionMismatch()
    {
        var request = new CreateEkycSessionRequest { ConsentVersion = "ekyc-consent-v0-old", ConsentAccepted = true };

        var ex = await Assert.ThrowsAsync<ValidationException>(
            () => _sessionService.CreateAsync(10, request, null, null));

        Assert.Equal(EkycSessionService.ConsentVersionMismatchMessage, ex.Message);
    }

    [Fact]
    public async Task Create_Rejects_WhenConsentVersionMissing()
    {
        var request = new CreateEkycSessionRequest { ConsentVersion = null, ConsentAccepted = true };

        await Assert.ThrowsAsync<ValidationException>(
            () => _sessionService.CreateAsync(10, request, null, null));
    }

    [Fact]
    public async Task Create_Rejects_WhenAccountLocked()
    {
        await Assert.ThrowsAsync<ForbiddenException>(
            () => _sessionService.CreateAsync(11, ValidConsent(), null, null));
    }

    [Fact]
    public async Task Create_Succeeds_WithValidConsent()
    {
        var response = await _sessionService.CreateAsync(10, ValidConsent(), "127.0.0.1", "agent/1.0");

        Assert.NotNull(response.SessionId);
        Assert.StartsWith("/api/v1/ekyc/proxy/", response.ProxyBasePath);
        Assert.Equal(EkycVerificationStatuses.Initiated, response.Status);
        Assert.True(response.ExpiresAt > DateTime.UtcNow.AddMinutes(-1));

        var session = await _db.EkycVerificationSessions.FirstAsync();
        Assert.Equal(10, session.UserId);
        Assert.Equal("ekyc-consent-v1", session.ConsentVersion);
        Assert.NotNull(session.ConsentAcceptedAt);
        Assert.Equal(EkycVerificationMethods.EkycVnpt, session.VerificationMethod);
        Assert.Equal(EkycBindingStates.Pending, session.BindingState);
    }

    [Fact]
    public async Task Create_WritesAuditLog()
    {
        await _sessionService.CreateAsync(10, ValidConsent(), "127.0.0.1", null);

        var audit = await _db.AuditLogs.FirstOrDefaultAsync(a => a.Action == EkycConstants.AuditActionSessionCreated);
        Assert.NotNull(audit);
        Assert.Equal(10, audit.ActorUserId);
        Assert.Equal(EkycConstants.AuditEntityType, audit.EntityType);
    }

    // ---------------------------------------------------------------
    // Status endpoint ownership
    // ---------------------------------------------------------------

    [Fact]
    public async Task GetStatus_ReturnsForOwner()
    {
        var created = await _sessionService.CreateAsync(10, ValidConsent(), null, null);
        var sessionId = long.Parse(created.SessionId);

        var status = await _sessionService.GetStatusAsync(10, sessionId);

        Assert.Equal(EkycVerificationStatuses.Initiated, status.Status);
        Assert.False(status.Expired);
    }

    [Fact]
    public async Task GetStatus_RejectsNonOwner_WithNotFound()
    {
        var created = await _sessionService.CreateAsync(10, ValidConsent(), null, null);
        var sessionId = long.Parse(created.SessionId);

        // NotFound (not Forbidden) — never leak session existence cross-account.
        await Assert.ThrowsAsync<NotFoundException>(
            () => _sessionService.GetStatusAsync(11, sessionId));
    }

    [Fact]
    public async Task GetStatus_RejectsUnknownSession()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => _sessionService.GetStatusAsync(10, 99999));
    }

    // ---------------------------------------------------------------
    // Proxy validation (via EkycProxyService.ValidateSessionForForwardAsync)
    // ---------------------------------------------------------------

    private EkycProxyService BuildProxyService()
        => new(_db, httpClientFactory: null!, options: Options.Create(new EkycOptions()));

    private async Task<long> SeedSessionAsync(long userId, string status, DateTime expiresAt)
    {
        var session = new EkycVerificationSession
        {
            UserId = userId,
            Status = status,
            VerificationMethod = EkycVerificationMethods.EkycVnpt,
            ExpiresAt = expiresAt,
            BindingState = EkycBindingStates.Pending,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        _db.EkycVerificationSessions.Add(session);
        await _db.SaveChangesAsync();
        return session.Id;
    }

    [Fact]
    public async Task ValidateForForward_RejectsTerminalSession()
    {
        var proxy = BuildProxyService();
        var id = await SeedSessionAsync(10, EkycVerificationStatuses.Verified, DateTime.UtcNow.AddMinutes(10));

        var ex = await Assert.ThrowsAsync<ConflictException>(
            () => proxy.ValidateSessionForForwardAsync(10, id));

        Assert.Equal(EkycProxyService.SessionNotForwardableMessage, ex.Message);
    }

    [Fact]
    public async Task ValidateForForward_RejectsConsumedSession()
    {
        var proxy = BuildProxyService();
        var id = await SeedSessionAsync(10, EkycVerificationStatuses.Consumed, DateTime.UtcNow.AddMinutes(10));

        await Assert.ThrowsAsync<ConflictException>(
            () => proxy.ValidateSessionForForwardAsync(10, id));
    }

    [Fact]
    public async Task ValidateForForward_RejectsExpiredSession()
    {
        var proxy = BuildProxyService();
        var id = await SeedSessionAsync(10, EkycVerificationStatuses.Initiated, DateTime.UtcNow.AddMinutes(-5));

        var ex = await Assert.ThrowsAsync<ConflictException>(
            () => proxy.ValidateSessionForForwardAsync(10, id));

        Assert.Equal(EkycProxyService.SessionExpiredMessage, ex.Message);
    }

    [Fact]
    public async Task ValidateForForward_RejectsWrongOwner_AsNotFound()
    {
        var proxy = BuildProxyService();
        var id = await SeedSessionAsync(10, EkycVerificationStatuses.Initiated, DateTime.UtcNow.AddMinutes(10));

        await Assert.ThrowsAsync<NotFoundException>(
            () => proxy.ValidateSessionForForwardAsync(11, id));
    }

    [Fact]
    public async Task ValidateForForward_RejectsUnknownSession()
    {
        var proxy = BuildProxyService();

        await Assert.ThrowsAsync<NotFoundException>(
            () => proxy.ValidateSessionForForwardAsync(10, 424242));
    }

    [Fact]
    public async Task ValidateForForward_AcceptsActiveOwnedSession()
    {
        var proxy = BuildProxyService();
        var id = await SeedSessionAsync(10, EkycVerificationStatuses.Initiated, DateTime.UtcNow.AddMinutes(10));

        var session = await proxy.ValidateSessionForForwardAsync(10, id);

        Assert.Equal(id, session.Id);
        Assert.Equal(10, session.UserId);
    }

    // ---------------------------------------------------------------
    // Endpoint path / method normalization
    // ---------------------------------------------------------------

    [Theory]
    [InlineData("ai/v1/web/ocr/id/front", "/ai/v1/web/ocr/id/front")]
    [InlineData("/ai/v1/web/face/compare", "/ai/v1/web/face/compare")]
    [InlineData("file-service/v1/addFile", "/file-service/v1/addFile")]
    public void NormalizeEndpointPath_AcceptsValidPaths(string input, string expected)
    {
        Assert.Equal(expected, EkycProxyService.NormalizeEndpointPath(input));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("ai/../../etc/passwd")]
    [InlineData("https://evil.com/x")]
    [InlineData("http://api.idg.vnpt.vn/ai/v1/web/ocr/id")]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeEndpointPath_RejectsTraversalAndAbsoluteUrls(string input)
    {
        Assert.Throws<ValidationException>(() => EkycProxyService.NormalizeEndpointPath(input));
    }

    [Fact]
    public void NormalizeMethod_AcceptsPostGetHead()
    {
        Assert.Equal(HttpMethod.Post, EkycProxyService.NormalizeMethod("POST"));
        Assert.Equal(HttpMethod.Get, EkycProxyService.NormalizeMethod("get"));
        Assert.Equal(HttpMethod.Head, EkycProxyService.NormalizeMethod("HEAD"));
    }

    [Theory]
    [InlineData("DELETE")]
    [InlineData("PUT")]
    [InlineData("TRACE")]
    public void NormalizeMethod_RejectsUnsafeMethods(string method)
    {
        Assert.Throws<ValidationException>(() => EkycProxyService.NormalizeMethod(method));
    }

    [Fact]
    public void BuildUpstreamUri_JoinsCorrectly()
    {
        var uri = EkycProxyService.BuildUpstreamUri("https://api.idg.vnpt.vn", "/ai/v1/web/ocr/id/front");

        Assert.Equal("https://api.idg.vnpt.vn/ai/v1/web/ocr/id/front", uri.ToString());
    }
}
