using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Patients;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.UnitTests.Patients;

public sealed class PatientSelfResolverTests : IDisposable
{
    private readonly SelfResolverDb _db;

    public PatientSelfResolverTests()
    {
        var options = new DbContextOptionsBuilder<SelfResolverDb>()
            .UseInMemoryDatabase($"SelfResolver_{Guid.NewGuid()}")
            .Options;
        _db = new SelfResolverDb(options);
    }

    public void Dispose()
    {
        _db.Database.EnsureDeleted();
        _db.Dispose();
    }

    [Fact]
    public void RequireSinglePatientId_MultipleIds_ThrowsConflict()
    {
        var ex = Assert.Throws<ConflictException>(() =>
            PatientSelfResolver.RequireSinglePatientId([10, 11], 1));

        Assert.Equal(PatientSelfResolver.AmbiguousMessage, ex.Message);
    }

    [Fact]
    public void RequireSinglePatientId_SingleId_ReturnsId()
    {
        Assert.Equal(10, PatientSelfResolver.RequireSinglePatientId([10], 1));
    }

    [Fact]
    public void RequireSinglePatientId_Empty_ThrowsNotFound()
    {
        Assert.Throws<NotFoundException>(() =>
            PatientSelfResolver.RequireSinglePatientId([], 1));
    }

    [Fact]
    public async Task LoadActiveAsync_SingleActivePatient_Returns()
    {
        AddUser(1, AccountStatus.ACTIVE);
        AddPatient(10, 1);

        var patient = await PatientSelfResolver.LoadActiveAsync(_db, 1, CancellationToken.None);

        Assert.Equal(10, patient.Id);
    }

    [Fact]
    public async Task LoadActiveAsync_InactiveUser_ThrowsUnauthorized()
    {
        AddUser(1, AccountStatus.LOCKED);
        AddPatient(10, 1);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            PatientSelfResolver.LoadActiveAsync(_db, 1, CancellationToken.None));
    }

    [Fact]
    public async Task LoadActiveAsync_MissingPatient_ThrowsNotFound()
    {
        AddUser(1, AccountStatus.ACTIVE);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            PatientSelfResolver.LoadActiveAsync(_db, 1, CancellationToken.None));
    }

    private void AddUser(long id, AccountStatus status)
    {
        _db.Users.Add(new User { Id = id, Email = $"u{id}@test.local", AccountStatus = status });
        _db.SaveChanges();
    }

    private void AddPatient(long id, long userId)
    {
        _db.Patients.Add(new Patient
        {
            Id = id,
            UserId = userId,
            PatientCode = $"PAT-{id}",
            FullName = "Benh nhan",
            Status = "ACTIVE",
        });
        _db.SaveChanges();
    }

    private sealed class SelfResolverDb : DbContext, IApplicationDbContext
    {
        public SelfResolverDb(DbContextOptions<SelfResolverDb> options)
            : base(options)
        {
        }

        private static NotSupportedException NotUsed() => new("Not used by PatientSelfResolverTests.");

        public DbSet<User> Users => Set<User>();
        public DbSet<Role> Roles => throw NotUsed();
        public DbSet<Permission> Permissions => throw NotUsed();
        public DbSet<UserRole> UserRoles => throw NotUsed();
        public DbSet<RolePermission> RolePermissions => throw NotUsed();
        public DbSet<Patient> Patients => Set<Patient>();
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
            modelBuilder.Entity<User>().Ignore(u => u.UserRoles);
            modelBuilder.Entity<User>().Ignore(u => u.Staff);
            modelBuilder.Entity<User>().Ignore(u => u.AuthSessions);
            modelBuilder.Entity<Patient>().HasKey(p => p.Id);
            modelBuilder.Entity<Patient>()
                .HasOne(p => p.User)
                .WithOne(u => u.Patient)
                .HasForeignKey<Patient>(p => p.UserId);
        }
    }
}
