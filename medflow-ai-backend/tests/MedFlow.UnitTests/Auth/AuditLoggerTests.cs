using System.Net;
using System.Text.Json;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Auth;
using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.UnitTests.Auth;

/// <summary>AuditLogger: queue AuditLog vào context, KHÔNG tự SaveChanges.</summary>
public sealed class AuditLoggerTests : IDisposable
{
    /// <summary>
    /// In-memory <see cref="IApplicationDbContext"/> — chỉ <see cref="AuditLogs"/> được dùng,
    /// các DbSet khác ném <see cref="NotSupportedException"/> để bắt lỗi gọi nhầm sớm.
    /// </summary>
    private sealed class TestDbContext : DbContext, IApplicationDbContext
    {
        private int _saveCalls;

        public int SaveCalls => _saveCalls;

        public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }

        public DbSet<User> Users => throw NotUsed();
        public DbSet<Role> Roles => throw NotUsed();
        public DbSet<Permission> Permissions => throw NotUsed();
        public DbSet<UserRole> UserRoles => throw NotUsed();
        public DbSet<RolePermission> RolePermissions => throw NotUsed();
        public DbSet<Patient> Patients => throw NotUsed();
        public DbSet<AuthVerificationChallenge> AuthVerificationChallenges => throw NotUsed();
        public DbSet<RefreshToken> RefreshTokens => throw NotUsed();
        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

        public override Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            _saveCalls++;
            return base.SaveChangesAsync(ct);
        }

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default)
            => Database.BeginTransactionAsync(ct);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Chỉ AuditLog tham gia model — các entity khác không cần key/config cho test này.
            modelBuilder.Ignore<User>();
            modelBuilder.Ignore<Role>();
            modelBuilder.Ignore<Permission>();
            modelBuilder.Ignore<UserRole>();
            modelBuilder.Ignore<RolePermission>();
            modelBuilder.Ignore<Patient>();
            modelBuilder.Ignore<AuthVerificationChallenge>();
            modelBuilder.Ignore<RefreshToken>();
        }

        private static NotSupportedException NotUsed()
            => new("DbSet này không được dùng trong AuditLoggerTests.");
    }

    private readonly TestDbContext _db;
    private readonly AuditLogger _sut;

    public AuditLoggerTests()
    {
        _db = new TestDbContext(new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        _sut = new AuditLogger(_db);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public void Add_EnqueuesAuditLog_WithoutSaving()
    {
        var userId = Guid.NewGuid();

        _sut.Add(userId, "LOGIN_SUCCESS", "USER", userId, null, "192.168.1.1");

        var entry = _db.ChangeTracker.Entries<AuditLog>().Single();
        Assert.Equal(EntityState.Added, entry.State);
        Assert.Equal(0, _db.SaveCalls);
    }

    [Fact]
    public void Add_EmptyGuid_StoresNullActorUserId()
    {
        _sut.Add(Guid.Empty, "REFRESH_REUSE_DETECTED", "USER", null, null, null);

        var log = _db.ChangeTracker.Entries<AuditLog>().Single().Entity;
        Assert.Null(log.ActorUserId);
    }

    [Fact]
    public void Add_SerializesNewValueToJson()
    {
        var payload = new { Email = "a@b.com", Count = 3 };

        _sut.Add(Guid.NewGuid(), "REGISTER", "USER", Guid.NewGuid(), payload, null);

        var log = _db.ChangeTracker.Entries<AuditLog>().Single().Entity;
        Assert.Equal(JsonSerializer.Serialize(payload), log.NewValue);
    }

    [Fact]
    public void Add_NullNewValue_StoresNull()
    {
        _sut.Add(Guid.NewGuid(), "LOGOUT", "USER", null, null, null);

        Assert.Null(_db.ChangeTracker.Entries<AuditLog>().Single().Entity.NewValue);
    }

    [Fact]
    public void Add_ParsesIpAddress()
    {
        _sut.Add(Guid.NewGuid(), "LOGIN_FAILED", "USER", null, null, "10.0.0.7");

        var log = _db.ChangeTracker.Entries<AuditLog>().Single().Entity;
        Assert.Equal(IPAddress.Parse("10.0.0.7"), log.IpAddress);
    }

    [Fact]
    public void Add_InvalidIp_StoresNull()
    {
        _sut.Add(Guid.NewGuid(), "LOGIN_FAILED", "USER", null, null, "not-an-ip");

        Assert.Null(_db.ChangeTracker.Entries<AuditLog>().Single().Entity.IpAddress);
    }
}
