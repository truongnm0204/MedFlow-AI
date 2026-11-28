using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Ekyc;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.UnitTests.Ekyc;

/// <summary>
/// Dedupe matrix cases 1-6 (owner 3.1, plan v2.1 mục 3.3) — each case asserted end-to-end
/// through <see cref="EkycDedupeResolver.ResolveAsync"/>: outcome code, activation decision,
/// patient linkage, audit rows, and the NEVER-auto-merge invariant.
/// </summary>
public sealed class EkycDedupeResolverTests : IDisposable
{
    private const string TestHmac = "deadbeefhmac0123456789abcdef";

    private sealed class DedupeTestDbContext(DbContextOptions<DedupeTestDbContext> options)
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
            modelBuilder.Entity<User>().Ignore(u => u.Patient);
            modelBuilder.Entity<User>().Ignore(u => u.Staff);
            modelBuilder.Entity<User>().Ignore(u => u.UserRoles);
            modelBuilder.Entity<User>().Ignore(u => u.AuthSessions);
            modelBuilder.Entity<Patient>().HasKey(p => p.Id);
            modelBuilder.Entity<Patient>().Property(p => p.Id).ValueGeneratedOnAdd();
            modelBuilder.Entity<Patient>().Ignore(p => p.User);
            modelBuilder.Entity<AuditLog>().HasKey(a => a.Id);
            modelBuilder.Entity<AuditLog>().Property(a => a.Id).ValueGeneratedOnAdd();
            modelBuilder.Entity<AuditLog>().Ignore(a => a.ActorUser);
            modelBuilder.Entity<PatientFamilyLink>().HasKey(l => new { l.OwnerPatientId, l.MemberPatientId });
            modelBuilder.Entity<PatientFamilyLink>().Ignore(l => l.OwnerPatient);
            modelBuilder.Entity<PatientFamilyLink>().Ignore(l => l.MemberPatient);
            modelBuilder.Entity<EkycVerificationSession>().HasKey(s => s.Id);
            modelBuilder.Entity<EkycVerificationSession>().Property(s => s.Id).ValueGeneratedOnAdd();
            modelBuilder.Entity<EkycProxyCallLog>().HasKey(l => l.Id);

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
            modelBuilder.Ignore<Visit>();
            modelBuilder.Ignore<QueueEntry>();
            modelBuilder.Ignore<Payment>();
        }
    }

    private readonly DedupeTestDbContext _db;

    public EkycDedupeResolverTests()
    {
        var options = new DbContextOptionsBuilder<DedupeTestDbContext>()
            .UseInMemoryDatabase($"ekyc-dedupe-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        _db = new DedupeTestDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    private User SeedUser(long id = 7)
    {
        var user = new User
        {
            Id = id,
            Email = $"user{id}@example.com",
            AccountStatus = AccountStatus.PENDING,
            AuthProvider = "LOCAL",
            PasswordHash = "hash",
        };
        _db.Users.Add(user);
        _db.SaveChanges();
        return user;
    }

    private Patient SeedShell(long userId, string fullName = "NGUYEN VAN AN", DateOnly? dob = null)
    {
        var shell = new Patient
        {
            UserId = userId,
            PatientCode = "P-SHELL",
            FullName = fullName,
            DateOfBirth = dob ?? new DateOnly(1995, 6, 15),
            Status = "ACTIVE",
        };
        _db.Patients.Add(shell);
        _db.SaveChanges();
        return shell;
    }

    private EkycVerificationSession SeedSession(long userId)
    {
        var session = new EkycVerificationSession
        {
            UserId = userId,
            Status = EkycVerificationStatuses.Verified,
            VerificationMethod = EkycVerificationMethods.EkycVnpt,
            IdentificationHmac = TestHmac,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        _db.EkycVerificationSessions.Add(session);
        _db.SaveChanges();
        return session;
    }

    private static Patient ExistingPatient(
        string fullName = "NGUYEN VAN AN",
        DateOnly? dob = null,
        long? userId = null,
        string? hmac = TestHmac)
    {
        return new Patient
        {
            PatientCode = "P-EXISTING",
            FullName = fullName,
            DateOfBirth = dob ?? new DateOnly(1995, 6, 15),
            UserId = userId,
            IdentificationHashHmac = hmac,
            Status = "ACTIVE",
        };
    }

    // ---------------------------------------------------------------
    // Case 1 — chưa có hồ sơ
    // ---------------------------------------------------------------

    [Fact]
    public async Task Case1_NoExistingRecord_ActivatesWithShellAndWritesHmac()
    {
        var user = SeedUser();
        var shell = SeedShell(user.Id);
        var session = SeedSession(user.Id);

        var result = await EkycDedupeResolver.ResolveAsync(
            _db, user, shell, session, TestHmac, "NGUYEN VAN AN", new DateOnly(1995, 6, 15), user.Id, "127.0.0.1", default);
        await _db.SaveChangesAsync();

        Assert.Equal(EkycDedupeOutcomes.NewIdentityActivated, result.Outcome);
        Assert.True(result.AccountActivated);
        Assert.Equal(shell.Id, result.LinkedPatientId);
        Assert.Equal(AccountStatus.ACTIVE, user.AccountStatus);
        Assert.Equal(TestHmac, shell.IdentificationHashHmac);
        Assert.Equal(shell.Id, session.PatientId);
        Assert.True(await _db.AuditLogs.AnyAsync(a => a.Action == EkycDedupeResolver.AuditActionAccountActivated));
    }

    // ---------------------------------------------------------------
    // Case 2 — đã khám chưa tài khoản → claim link
    // ---------------------------------------------------------------

    [Fact]
    public async Task Case2_ExistingUnlinkedRecord_ClaimsAndVoidsShell()
    {
        var user = SeedUser();
        var shell = SeedShell(user.Id);
        var session = SeedSession(user.Id);
        var existing = ExistingPatient();
        _db.Patients.Add(existing);
        await _db.SaveChangesAsync();

        var result = await EkycDedupeResolver.ResolveAsync(
            _db, user, shell, session, TestHmac, "NGUYEN VAN AN", new DateOnly(1995, 6, 15), user.Id, "127.0.0.1", default);
        await _db.SaveChangesAsync();

        Assert.Equal(EkycDedupeOutcomes.ClaimLinkedActivated, result.Outcome);
        Assert.True(result.AccountActivated);
        Assert.Equal(existing.Id, result.LinkedPatientId);

        // User re-pointed at existing record; shell unlinked + voided — NOT merged.
        Assert.Equal(user.Id, existing.UserId);
        Assert.Null(shell.UserId);
        Assert.Equal(EkycDedupeResolver.PatientStatusVoidedDuplicate, shell.Status);
        Assert.Equal(existing.Id, session.PatientId);

        // CLAIM_EKYC_MATCHED audit written.
        Assert.True(await _db.AuditLogs.AnyAsync(a => a.Action == EkycConstants.AuditActionClaimEkycMatched));
    }

    // ---------------------------------------------------------------
    // Case 3 — đã có người thân quản lý → review, KHÔNG auto-claim
    // ---------------------------------------------------------------

    [Fact]
    public async Task Case3_FamilyManagedRecord_ActivatesButPendingReview()
    {
        var user = SeedUser();
        var shell = SeedShell(user.Id);
        var session = SeedSession(user.Id);
        var existing = ExistingPatient();
        _db.Patients.Add(existing);
        await _db.SaveChangesAsync();

        // Verified family link: someone else manages this record.
        _db.PatientFamilyLinks.Add(new PatientFamilyLink
        {
            OwnerPatientId = 999,
            MemberPatientId = existing.Id,
            VerificationStatus = "VERIFIED",
        });
        await _db.SaveChangesAsync();

        var result = await EkycDedupeResolver.ResolveAsync(
            _db, user, shell, session, TestHmac, "NGUYEN VAN AN", new DateOnly(1995, 6, 15), user.Id, "127.0.0.1", default);
        await _db.SaveChangesAsync();

        Assert.Equal(EkycDedupeOutcomes.FamilyManagedPendingReview, result.Outcome);
        Assert.True(result.AccountActivated); // the PERSON is verified — activation proceeds
        Assert.NotNull(result.ReviewReason);

        // NO claim happened — the managed record stays untouched.
        Assert.Null(existing.UserId);
        Assert.Equal(shell.Id, session.PatientId);
        Assert.True(await _db.AuditLogs.AnyAsync(a => a.Action == EkycDedupeResolver.AuditActionDedupeReviewRequired));
    }

    // ---------------------------------------------------------------
    // Case 4 — CCCD đã gắn tài khoản khác → từ chối
    // ---------------------------------------------------------------

    [Fact]
    public async Task Case4_BoundToOtherAccount_ActivationRefused()
    {
        var user = SeedUser();
        var shell = SeedShell(user.Id);
        var session = SeedSession(user.Id);

        var otherUser = SeedUser(id: 888);
        var existing = ExistingPatient(userId: otherUser.Id);
        _db.Patients.Add(existing);
        await _db.SaveChangesAsync();

        var result = await EkycDedupeResolver.ResolveAsync(
            _db, user, shell, session, TestHmac, "NGUYEN VAN AN", new DateOnly(1995, 6, 15), user.Id, "127.0.0.1", default);
        await _db.SaveChangesAsync();

        Assert.Equal(EkycDedupeOutcomes.ConflictAccountExists, result.Outcome);
        Assert.False(result.AccountActivated);
        Assert.NotNull(result.ReviewReason);

        // Account stays PENDING; existing record untouched.
        Assert.Equal(AccountStatus.PENDING, user.AccountStatus);
        Assert.Equal(otherUser.Id, existing.UserId);
        Assert.True(await _db.AuditLogs.AnyAsync(a => a.Action == EkycDedupeResolver.AuditActionActivationRefused));
    }

    // ---------------------------------------------------------------
    // Case 5 — trùng CCCD không khớp info → manual review
    // ---------------------------------------------------------------

    [Fact]
    public async Task Case5_InfoMismatch_ActivatesButPendingReview()
    {
        var user = SeedUser();
        var shell = SeedShell(user.Id);
        var session = SeedSession(user.Id);

        // Same hmac but different name — disagreement.
        var existing = ExistingPatient(fullName: "TRAN VAN KHAC", dob: new DateOnly(1990, 1, 1));
        _db.Patients.Add(existing);
        await _db.SaveChangesAsync();

        var result = await EkycDedupeResolver.ResolveAsync(
            _db, user, shell, session, TestHmac, "NGUYEN VAN AN", new DateOnly(1995, 6, 15), user.Id, "127.0.0.1", default);
        await _db.SaveChangesAsync();

        Assert.Equal(EkycDedupeOutcomes.InfoMismatchPendingReview, result.Outcome);
        Assert.True(result.AccountActivated);
        Assert.NotNull(result.ReviewReason);
        Assert.Null(existing.UserId); // no claim
        Assert.True(await _db.AuditLogs.AnyAsync(a => a.Action == EkycDedupeResolver.AuditActionDedupeReviewRequired));
    }

    // ---------------------------------------------------------------
    // Case 6 — nhiều hồ sơ cùng định danh → conflict, NO auto-merge
    // ---------------------------------------------------------------

    [Fact]
    public async Task Case6_MultipleRecords_NeverAutoMerges()
    {
        var user = SeedUser();
        var shell = SeedShell(user.Id);
        var session = SeedSession(user.Id);

        var first = ExistingPatient();
        var second = ExistingPatient();
        second.PatientCode = "P-DUP";
        _db.Patients.AddRange(first, second);
        await _db.SaveChangesAsync();

        var result = await EkycDedupeResolver.ResolveAsync(
            _db, user, shell, session, TestHmac, "NGUYEN VAN AN", new DateOnly(1995, 6, 15), user.Id, "127.0.0.1", default);
        await _db.SaveChangesAsync();

        Assert.Equal(EkycDedupeOutcomes.MultipleRecordsConflict, result.Outcome);
        Assert.True(result.AccountActivated);
        Assert.NotNull(result.ReviewReason);

        // NEITHER record touched — the never-auto-merge invariant.
        Assert.Null(first.UserId);
        Assert.Null(second.UserId);
        Assert.Equal(shell.Id, session.PatientId);
    }

    // ---------------------------------------------------------------
    // Cross-cutting: same-user re-verification edge
    // ---------------------------------------------------------------

    [Fact]
    public async Task SameUserMatch_TreatedAsNormalActivation()
    {
        var user = SeedUser();
        var shell = SeedShell(user.Id);
        var session = SeedSession(user.Id);

        // Edge: the existing record is ALREADY the user's own (re-verification).
        var existing = ExistingPatient(userId: user.Id);
        _db.Patients.Add(existing);
        await _db.SaveChangesAsync();

        var result = await EkycDedupeResolver.ResolveAsync(
            _db, user, shell, session, TestHmac, "NGUYEN VAN AN", new DateOnly(1995, 6, 15), user.Id, "127.0.0.1", default);

        Assert.Equal(EkycDedupeOutcomes.NewIdentityActivated, result.Outcome);
        Assert.True(result.AccountActivated);
        Assert.Equal(existing.Id, result.LinkedPatientId);
    }

    // ---------------------------------------------------------------
    // Null hmac — no dedupe possible, plain activation
    // ---------------------------------------------------------------

    [Fact]
    public async Task NullHmac_ActivatesWithoutDedupe()
    {
        var user = SeedUser();
        var shell = SeedShell(user.Id);
        var session = SeedSession(user.Id);

        var result = await EkycDedupeResolver.ResolveAsync(
            _db, user, shell, session, null, "NGUYEN VAN AN", new DateOnly(1995, 6, 15), user.Id, "127.0.0.1", default);

        Assert.Equal(EkycDedupeOutcomes.NewIdentityActivated, result.Outcome);
        Assert.True(result.AccountActivated);
        Assert.Null(shell.IdentificationHashHmac); // nothing written — no id proven
    }
}
