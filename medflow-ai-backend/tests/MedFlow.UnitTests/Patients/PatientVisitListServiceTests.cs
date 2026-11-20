using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Patients;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.UnitTests.Patients;

public sealed class PatientVisitListServiceTests : IDisposable
{
    private const long HospitalA = 1;
    private const long HospitalB = 2;
    private static readonly DateTimeOffset FixedNow = new(2026, 11, 14, 3, 0, 0, TimeSpan.Zero);

    private readonly VisitListTestDb _db;
    private readonly PatientVisitListService _service;

    public PatientVisitListServiceTests()
    {
        var options = new DbContextOptionsBuilder<VisitListTestDb>()
            .UseInMemoryDatabase($"VisitList_{Guid.NewGuid()}")
            .Options;
        _db = new VisitListTestDb(options);
        _db.Hospitals.AddRange(
            new Hospital { Id = HospitalA, HospitalCode = "H-A", Name = "Bệnh viện A", IsActive = true },
            new Hospital { Id = HospitalB, HospitalCode = "H-B", Name = "Bệnh viện B", IsActive = true });
        _db.Roles.AddRange(
            new Role { Id = 1, Code = "PATIENT", Name = "Benh nhan" },
            new Role { Id = 5, Code = "DOCTOR", Name = "Bac si" });
        _db.HospitalRooms.AddRange(
            new HospitalRoom { Id = 5001, HospitalId = HospitalA, RoomCode = "R-A", Name = "Phòng A", RoomType = "EXAM", Status = "ACTIVE" },
            new HospitalRoom { Id = 5002, HospitalId = HospitalB, RoomCode = "R-B", Name = "Phòng B", RoomType = "EXAM", Status = "ACTIVE" });
        _db.SaveChanges();
        _service = new PatientVisitListService(_db, new FixedTime(FixedNow));
    }

    public void Dispose()
    {
        _db.Database.EnsureDeleted();
        _db.Dispose();
    }

    [Fact]
    public async Task GetMyVisits_ReturnsOwnAndActiveFamily_SortedByCheckedInDesc()
    {
        var actor = AddPatient(10, 100);
        AddPatient(11, 200);
        AddFamilyLink(100, 200);
        AddVisit(1000, 100, 5001, new DateTime(2026, 11, 10, 8, 0, 0));
        AddVisit(1001, 200, 5002, new DateTime(2026, 11, 12, 8, 0, 0));

        var result = await _service.GetMyVisitsAsync(actor, limit: 10);

        Assert.Equal([1001L, 1000L], result.Items.Select(i => i.VisitId));
        Assert.Equal("Bệnh viện B", result.Items[0].HospitalName);
        Assert.Equal("Bệnh viện A", result.Items[1].HospitalName);
    }

    [Fact]
    public async Task GetMyVisits_ExcludesStrangerAndRevokedFamily()
    {
        var actor = AddPatient(10, 100);
        AddPatient(12, 300);
        AddPatient(13, 400);
        AddFamilyLink(100, 400, revoked: true);
        AddVisit(1000, 100, 5001, new DateTime(2026, 11, 10, 8, 0, 0));
        AddVisit(1001, 300, 5001, new DateTime(2026, 11, 11, 8, 0, 0));
        AddVisit(1002, 400, 5001, new DateTime(2026, 11, 12, 8, 0, 0));

        var result = await _service.GetMyVisitsAsync(actor, limit: 10);

        Assert.Equal([1000L], result.Items.Select(i => i.VisitId));
    }

    [Fact]
    public async Task GetMyVisits_ExcludesAmbiguousHospital()
    {
        var actor = AddPatient(10, 100);
        AddVisit(1000, 100, 5001, new DateTime(2026, 11, 10, 8, 0, 0));
        AddVisit(1001, 100, 5001, new DateTime(2026, 11, 11, 8, 0, 0));
        _db.QueueServicePoints.Add(new QueueServicePoint { Id = 900, HospitalId = HospitalB, Name = "Điểm B" });
        _db.QueueEntries.Add(new QueueEntry { Id = 1, VisitId = 1001, ServicePointId = 900, Status = "WAITING" });
        _db.SaveChanges();

        var result = await _service.GetMyVisitsAsync(actor, limit: 10);

        Assert.Equal([1000L], result.Items.Select(i => i.VisitId));
    }

    [Fact]
    public async Task GetMyVisits_ReturnsLatestPayment()
    {
        var actor = AddPatient(10, 100);
        var visit = AddVisit(1000, 100, 5001, new DateTime(2026, 11, 10, 8, 0, 0));
        _db.Payments.AddRange(
            new Payment { Id = 10, VisitId = visit.Id, Amount = 100_000m, Status = "PENDING", PaymentCode = "MF10" },
            new Payment { Id = 11, VisitId = visit.Id, Amount = 100_000m, Status = "PAID", PaymentCode = "MF10" });
        _db.SaveChanges();

        var result = await _service.GetMyVisitsAsync(actor, limit: 10);

        var item = Assert.Single(result.Items);
        Assert.Equal("PAID", item.PaymentStatus);
        Assert.Equal("MF10", item.PaymentCode);
    }

    [Fact]
    public async Task GetMyVisits_ClampsLimitToRange()
    {
        var actor = AddPatient(10, 100);
        var day = new DateTime(2026, 11, 1, 8, 0, 0);
        for (var i = 0; i < 22; i++)
            AddVisit(2000 + i, 100, 5001, day.AddDays(i));

        var minimum = await _service.GetMyVisitsAsync(actor, limit: 0);
        Assert.Single(minimum.Items);

        var maximum = await _service.GetMyVisitsAsync(actor, limit: 999);
        Assert.Equal(PatientVisitListService.MaxLimit, maximum.Items.Count);
        Assert.Equal(2021, maximum.Items[0].VisitId);
    }

    [Fact]
    public async Task GetMyVisits_DoctorRole_Forbidden()
    {
        AddUser(20, AccountStatus.ACTIVE, roleId: 5);

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => _service.GetMyVisitsAsync(20, null));

        Assert.Equal(PatientVisitListService.ForbiddenMessage, ex.Message);
    }

    [Fact]
    public async Task GetMyVisits_InactiveUser_Unauthorized()
    {
        AddUser(21, AccountStatus.INACTIVE, roleId: 1);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.GetMyVisitsAsync(21, null));
    }

    private long AddPatient(long userId, long patientId)
    {
        AddUser(userId, AccountStatus.ACTIVE, roleId: 1);
        _db.Patients.Add(new Patient
        {
            Id = patientId,
            UserId = userId,
            PatientCode = $"P-{patientId}",
            FullName = "Benh nhan",
            Status = "ACTIVE",
        });
        _db.SaveChanges();
        return userId;
    }

    private void AddUser(long userId, AccountStatus status, long roleId)
    {
        _db.Users.Add(new User { Id = userId, Email = $"u{userId}@test.local", AccountStatus = status });
        _db.UserRoles.Add(new UserRole { UserId = userId, RoleId = roleId });
        _db.SaveChanges();
    }

    private void AddFamilyLink(long ownerId, long memberId, bool revoked = false)
    {
        _db.PatientFamilyLinks.Add(new PatientFamilyLink
        {
            OwnerPatientId = ownerId,
            MemberPatientId = memberId,
            VerificationStatus = "VERIFIED",
            RevokedAt = revoked ? FixedNow.UtcDateTime : null,
        });
        _db.SaveChanges();
    }

    private Visit AddVisit(long id, long patientId, long roomId, DateTime checkedInAt)
    {
        var visit = new Visit
        {
            Id = id,
            VisitCode = $"VIS-{id}",
            PatientId = patientId,
            Status = "CHECKED_IN",
            ExaminationRoomId = roomId,
            CheckedInAt = checkedInAt,
        };
        _db.Visits.Add(visit);
        _db.SaveChanges();
        return visit;
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class VisitListTestDb : DbContext, IApplicationDbContext
    {
        public VisitListTestDb(DbContextOptions<VisitListTestDb> options)
            : base(options)
        {
        }

        public DbSet<User> Users => Set<User>();
        public DbSet<Role> Roles => Set<Role>();
        public DbSet<Permission> Permissions => throw new NotSupportedException();
        public DbSet<UserRole> UserRoles => Set<UserRole>();
        public DbSet<RolePermission> RolePermissions => throw new NotSupportedException();
        public DbSet<Patient> Patients => Set<Patient>();
        public DbSet<Staff> Staff => Set<Staff>();
        public DbSet<Doctor> Doctors => Set<Doctor>();
        public DbSet<DoctorServiceAuthorization> DoctorServiceAuthorizations => throw new NotSupportedException();
        public DbSet<ExaminationSlot> ExaminationSlots => Set<ExaminationSlot>();
        public DbSet<AuthSession> AuthSessions => throw new NotSupportedException();
        public DbSet<AuthVerificationChallenge> AuthVerificationChallenges => throw new NotSupportedException();
        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
        public DbSet<Hospital> Hospitals => Set<Hospital>();
        public DbSet<Department> Departments => throw new NotSupportedException();
        public DbSet<HospitalRoom> HospitalRooms => Set<HospitalRoom>();
        public DbSet<Specialty> Specialties => throw new NotSupportedException();
        public DbSet<Service> Services => Set<Service>();
        public DbSet<ServicePrice> ServicePrices => Set<ServicePrice>();
        public DbSet<HospitalPolicy> HospitalPolicies => Set<HospitalPolicy>();
        public DbSet<Appointment> Appointments => Set<Appointment>();
        public DbSet<PatientFamilyLink> PatientFamilyLinks => Set<PatientFamilyLink>();
        public DbSet<Visit> Visits => Set<Visit>();
        public DbSet<QueueEntry> QueueEntries => Set<QueueEntry>();
        public DbSet<Payment> Payments => Set<Payment>();
        public DbSet<QueueServicePoint> QueueServicePoints => Set<QueueServicePoint>();

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default)
            => Database.BeginTransactionAsync(ct);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Ignore<DoctorServiceAuthorization>();
            modelBuilder.Ignore<AuthSession>();
            modelBuilder.Ignore<AuthVerificationChallenge>();
            modelBuilder.Ignore<Permission>();
            modelBuilder.Ignore<RolePermission>();
            modelBuilder.Ignore<Department>();
            modelBuilder.Ignore<Specialty>();

            modelBuilder.Entity<User>().HasKey(u => u.Id);
            modelBuilder.Entity<User>().Ignore(u => u.AuthSessions);
            modelBuilder.Entity<User>().Ignore(u => u.UserRoles);
            modelBuilder.Entity<User>().Ignore(u => u.Staff);
            modelBuilder.Entity<Role>().HasKey(r => r.Id);
            modelBuilder.Entity<Role>().Ignore(r => r.UserRoles);
            modelBuilder.Entity<Role>().Ignore(r => r.RolePermissions);
            modelBuilder.Entity<UserRole>().HasKey(ur => new { ur.UserId, ur.RoleId });
            modelBuilder.Entity<UserRole>().Ignore(ur => ur.User);
            modelBuilder.Entity<UserRole>().Ignore(ur => ur.Role);
            modelBuilder.Entity<Patient>().HasKey(p => p.Id);
            modelBuilder.Entity<Patient>().HasOne(p => p.User).WithOne(u => u.Patient).HasForeignKey<Patient>(p => p.UserId);
            modelBuilder.Entity<Staff>().HasKey(s => s.Id);
            modelBuilder.Entity<Staff>().Ignore(s => s.User);
            modelBuilder.Entity<Staff>().Ignore(s => s.Department);
            modelBuilder.Entity<Staff>().Ignore(s => s.Doctor);
            modelBuilder.Entity<Doctor>().HasKey(d => d.Id);
            modelBuilder.Entity<Doctor>().Ignore(d => d.Staff);
            modelBuilder.Entity<Doctor>().Ignore(d => d.Specialty);
            modelBuilder.Entity<Doctor>().Ignore(d => d.ServiceAuthorizations);
            modelBuilder.Entity<Doctor>().Ignore(d => d.ExaminationSlots);
            modelBuilder.Entity<Service>().HasKey(s => s.Id);
            modelBuilder.Entity<Service>().Ignore(s => s.Department);
            modelBuilder.Entity<Service>().Ignore(s => s.Specialty);
            modelBuilder.Entity<Service>().Ignore(s => s.ServicePrices);
            modelBuilder.Entity<ServicePrice>().HasKey(p => p.Id);
            modelBuilder.Entity<Appointment>().HasKey(a => a.Id);
            modelBuilder.Entity<Appointment>().Ignore(a => a.Slot);
            modelBuilder.Entity<Appointment>().Ignore(a => a.Patient);
            modelBuilder.Entity<ExaminationSlot>().HasKey(s => s.Id);
            modelBuilder.Entity<ExaminationSlot>().Ignore(s => s.Service);
            modelBuilder.Entity<ExaminationSlot>().Ignore(s => s.Doctor);
            modelBuilder.Entity<ExaminationSlot>().Ignore(s => s.Room);
            modelBuilder.Entity<ExaminationSlot>().Ignore(s => s.Appointments);
            modelBuilder.Entity<HospitalPolicy>().HasKey(p => p.Id);
            modelBuilder.Entity<PatientFamilyLink>().HasKey(l => new { l.OwnerPatientId, l.MemberPatientId });
            modelBuilder.Entity<PatientFamilyLink>().Ignore(l => l.OwnerPatient);
            modelBuilder.Entity<PatientFamilyLink>().HasOne(l => l.MemberPatient).WithMany().HasForeignKey(l => l.MemberPatientId);
            modelBuilder.Entity<Hospital>().HasKey(h => h.Id);
            modelBuilder.Entity<HospitalRoom>().HasKey(r => r.Id);
            modelBuilder.Entity<HospitalRoom>().Ignore(r => r.Department);
            modelBuilder.Entity<Visit>().HasKey(v => v.Id);
            modelBuilder.Entity<Payment>().HasKey(p => p.Id);
            modelBuilder.Entity<QueueEntry>().HasKey(q => q.Id);
            modelBuilder.Entity<QueueServicePoint>().HasKey(p => p.Id);
            modelBuilder.Entity<AuditLog>(b =>
            {
                b.HasKey(a => a.Id);
                b.Ignore(a => a.IpAddress);
                b.Ignore(a => a.ActorUser);
            });
        }
    }
}
