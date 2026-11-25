using MedFlow.Application.DTOs.Reception;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Reception;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.UnitTests.Reception;

public sealed class ReceptionPatientSearchServiceTests : IDisposable
{
    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }

    private sealed class SearchTestDbContext : DbContext, IApplicationDbContext
    {
        public SearchTestDbContext(DbContextOptions<SearchTestDbContext> options)
            : base(options)
        {
        }

        private static NotSupportedException NotUsed() =>
            new("Not needed for ReceptionPatientSearchService unit tests.");

        public DbSet<User> Users => Set<User>();
        public DbSet<Role> Roles => Set<Role>();
        public DbSet<Permission> Permissions => Set<Permission>();
        public DbSet<UserRole> UserRoles => Set<UserRole>();
        public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
        public DbSet<Patient> Patients => Set<Patient>();
        public DbSet<Staff> Staff => Set<Staff>();
        public DbSet<Doctor> Doctors => Set<Doctor>();
        public DbSet<DoctorServiceAuthorization> DoctorServiceAuthorizations => throw NotUsed();
        public DbSet<ExaminationSlot> ExaminationSlots => Set<ExaminationSlot>();
        public DbSet<AuthSession> AuthSessions => throw NotUsed();
        public DbSet<AuthVerificationChallenge> AuthVerificationChallenges => throw NotUsed();
        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
        public DbSet<Hospital> Hospitals => Set<Hospital>();
        public DbSet<Department> Departments => Set<Department>();
        public DbSet<HospitalRoom> HospitalRooms => Set<HospitalRoom>();
        public DbSet<Specialty> Specialties => Set<Specialty>();
        public DbSet<Service> Services => Set<Service>();
        public DbSet<ServicePrice> ServicePrices => throw NotUsed();
        public DbSet<HospitalPolicy> HospitalPolicies => throw NotUsed();
        public DbSet<Appointment> Appointments => Set<Appointment>();
        public DbSet<PatientFamilyLink> PatientFamilyLinks => throw NotUsed();
        public DbSet<Visit> Visits => Set<Visit>();
        public DbSet<QueueEntry> QueueEntries => Set<QueueEntry>();
        public DbSet<Payment> Payments => throw NotUsed();

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default)
            => Database.BeginTransactionAsync(ct);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Ignore<AuthSession>();
            modelBuilder.Ignore<AuthVerificationChallenge>();
            modelBuilder.Ignore<DoctorServiceAuthorization>();
            modelBuilder.Ignore<ServicePrice>();
            modelBuilder.Ignore<HospitalPolicy>();
            modelBuilder.Ignore<PatientFamilyLink>();
            modelBuilder.Ignore<Payment>();

            modelBuilder.Entity<User>().HasKey(u => u.Id);
            modelBuilder.Entity<Role>().HasKey(r => r.Id);
            modelBuilder.Entity<Permission>().HasKey(p => p.Id);
            modelBuilder.Entity<UserRole>().HasKey(ur => new { ur.UserId, ur.RoleId });
            modelBuilder.Entity<RolePermission>().HasKey(rp => new { rp.RoleId, rp.PermissionId });
            modelBuilder.Entity<Hospital>().HasKey(h => h.Id);
            modelBuilder.Entity<Department>().HasKey(d => d.Id);
            modelBuilder.Entity<Specialty>().HasKey(s => s.Id);
            modelBuilder.Entity<Patient>().HasKey(p => p.Id);
            modelBuilder.Entity<Visit>().HasKey(v => v.Id);
            modelBuilder.Entity<QueueEntry>().HasKey(q => q.Id);
            modelBuilder.Entity<AuditLog>().HasKey(a => a.Id);
            modelBuilder.Entity<AuditLog>().Ignore(a => a.IpAddress);

            modelBuilder.Entity<HospitalRoom>(b =>
            {
                b.HasKey(r => r.Id);
                b.HasOne(r => r.Department).WithMany(d => d.HospitalRooms).HasForeignKey(r => r.DepartmentId);
            });

            modelBuilder.Entity<Staff>(b =>
            {
                b.HasKey(s => s.Id);
                b.HasOne(s => s.User).WithOne(u => u.Staff).HasForeignKey<Staff>(s => s.UserId);
                b.HasOne(s => s.Department).WithMany().HasForeignKey(s => s.DepartmentId);
            });

            modelBuilder.Entity<Doctor>(b =>
            {
                b.HasKey(d => d.Id);
                b.HasOne(d => d.Staff).WithOne(s => s.Doctor).HasForeignKey<Doctor>(d => d.StaffId);
                b.HasOne(d => d.Specialty).WithMany().HasForeignKey(d => d.SpecialtyId);
            });

            modelBuilder.Entity<Service>(b =>
            {
                b.HasKey(s => s.Id);
                b.HasOne(s => s.Department).WithMany(d => d.Services).HasForeignKey(s => s.DepartmentId);
                b.HasOne(s => s.Specialty).WithMany(sp => sp.Services).HasForeignKey(s => s.SpecialtyId);
            });

            modelBuilder.Entity<ExaminationSlot>(b =>
            {
                b.HasKey(s => s.Id);
                b.HasOne(s => s.Service).WithMany().HasForeignKey(s => s.ServiceId);
                b.HasOne(s => s.Doctor).WithMany(d => d.ExaminationSlots).HasForeignKey(s => s.DoctorId);
                b.HasOne(s => s.Room).WithMany().HasForeignKey(s => s.RoomId);
            });

            modelBuilder.Entity<Appointment>(b =>
            {
                b.HasKey(a => a.Id);
                b.HasOne(a => a.Slot).WithMany(s => s.Appointments).HasForeignKey(a => a.SlotId);
                b.HasOne(a => a.Patient).WithMany().HasForeignKey(a => a.PatientId);
            });
        }
    }


    private static readonly DateTimeOffset FixedUtcNow =
        new(2026, 10, 15, 2, 0, 0, TimeSpan.Zero); // 09:00 Vietnam time

    private static readonly DateTime Today = new(2026, 10, 15);

    private readonly SearchTestDbContext _db;
    private readonly ReceptionPatientSearchService _service;

    public ReceptionPatientSearchServiceTests()
    {
        var options = new DbContextOptionsBuilder<SearchTestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new SearchTestDbContext(options);
        _service = new ReceptionPatientSearchService(_db, new FixedTimeProvider(FixedUtcNow));
        Seed();
    }

    public void Dispose() => _db.Dispose();

    private static string Hash(string value) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private void Seed()
    {
        _db.Departments.Add(new Department { Id = 1, HospitalId = 1, DepartmentCode = "GI", Name = "Khoa Tiêu hóa", IsActive = true });
        _db.HospitalRooms.Add(new HospitalRoom { Id = 1, HospitalId = 1, DepartmentId = 1, RoomCode = "P101", Name = "Phòng 101", RoomType = "EXAM", Status = "ACTIVE" });
        _db.Staff.Add(new Staff { Id = 1, StaffCode = "ST1", FullName = "BS. Trần Minh", HospitalId = 1, DepartmentId = 1 });
        _db.Doctors.Add(new Doctor { Id = 1, StaffId = 1, HospitalId = 1, DoctorCode = "DOC1" });
        _db.Services.Add(new Service { Id = 1, DepartmentId = 1 });

        _db.Patients.AddRange(
            new Patient { Id = 1, PatientCode = "PAT-0001", FullName = "Nguyễn An", Phone = "0912345678", IdentificationHash = Hash("012345678901"), Gender = "MALE" },
            new Patient { Id = 2, PatientCode = "PAT-0002", FullName = "Lê Bình", Phone = "0987654321" },
            new Patient { Id = 3, PatientCode = "PAT-0003", FullName = "Phạm Chi", Phone = "0355555555" },
            new Patient { Id = 4, PatientCode = "PAT-0004", FullName = "Võ Dũng", Phone = "0366666666" });

        _db.ExaminationSlots.AddRange(
            new ExaminationSlot { Id = 1, ServiceId = 1, DoctorId = 1, RoomId = 1, StartsAt = Today.AddHours(8), EndsAt = Today.AddHours(8.5), Status = "OPEN" },
            new ExaminationSlot { Id = 2, ServiceId = 1, DoctorId = 1, RoomId = 1, StartsAt = Today.AddHours(9), EndsAt = Today.AddHours(9.5), Status = "OPEN" },
            new ExaminationSlot { Id = 3, ServiceId = 1, DoctorId = 1, RoomId = 1, StartsAt = Today.AddDays(1).AddHours(8), EndsAt = Today.AddDays(1).AddHours(8.5), Status = "OPEN" });

        _db.Appointments.AddRange(
            new Appointment { Id = 1, PatientId = 1, SlotId = 1, Status = "BOOKED" },
            new Appointment { Id = 2, PatientId = 2, SlotId = 2, Status = "CHECKED_IN" },
            new Appointment { Id = 3, PatientId = 3, SlotId = 3, Status = "BOOKED" },
            new Appointment { Id = 4, PatientId = 4, SlotId = 1, Status = "CANCELLED" });

        _db.Visits.AddRange(
            new Visit { Id = 1, PatientId = 1, Status = "COMPLETED" },
            new Visit { Id = 2, PatientId = 1, Status = "COMPLETED" },
            new Visit { Id = 3, PatientId = 1, Status = "IN_PROGRESS" });

        _db.SaveChanges();
    }

    [Theory]
    [InlineData("pat-0001", 1)]
    [InlineData("NGUYỄN", 1)]
    [InlineData("0987", 1)]
    [InlineData("PAT-", 4)]
    [InlineData("zzz", 0)]
    public async Task Search_MatchesCodeNameAndPhoneCaseInsensitive(string query, int expected)
    {
        var result = await _service.SearchPatientsAsync(new SearchPatientsRequest { Query = query });

        Assert.Equal(expected, result.TotalCount);
        Assert.Equal(expected, result.Items.Count);
    }

    [Fact]
    public async Task Search_TwelveDigitQuery_MatchesIdentityHash()
    {
        var result = await _service.SearchPatientsAsync(new SearchPatientsRequest { Query = "012345678901" });

        var item = Assert.Single(result.Items);
        Assert.Equal(1, item.Id);
        Assert.NotNull(item.IdentityCardMasked);
        Assert.DoesNotContain("012345678901", item.IdentityCardMasked);
    }

    [Fact]
    public async Task Search_IncludesTodayAppointmentAndPastVisitCount()
    {
        var result = await _service.SearchPatientsAsync(new SearchPatientsRequest { Query = "PAT-0001" });

        var item = Assert.Single(result.Items);
        Assert.Equal(2, item.TotalPastVisits);
        Assert.NotNull(item.TodayAppointment);
        Assert.Equal(1, item.TodayAppointment!.Id);
        Assert.Equal("08:00", item.TodayAppointment.Time);
        Assert.Equal("BS. Trần Minh", item.TodayAppointment.DoctorName);
        Assert.Equal("Khoa Tiêu hóa", item.TodayAppointment.Department);
        Assert.Equal("Phòng 101", item.TodayAppointment.RoomName);
        Assert.Equal("BOOKED", item.TodayAppointment.Status);
    }

    [Theory]
    [InlineData("ALL", 4)]
    [InlineData(null, 4)]
    [InlineData("HAS_APPOINTMENT", 2)]
    [InlineData("CHECKED_IN", 1)]
    [InlineData("NO_APPOINTMENT", 2)]
    public async Task Search_TabFilters(string? tab, int expected)
    {
        var result = await _service.SearchPatientsAsync(new SearchPatientsRequest { Tab = tab });

        Assert.Equal(expected, result.TotalCount);
        Assert.Equal(expected, result.Items.Count);
    }

    [Fact]
    public async Task Search_CountsIgnoreSelectedTab()
    {
        var result = await _service.SearchPatientsAsync(new SearchPatientsRequest { Tab = "CHECKED_IN" });

        Assert.Equal(new SearchTabCountsDto(4, 2, 1, 2), result.Counts);
    }

    [Fact]
    public async Task Search_Pagination()
    {
        var page2 = await _service.SearchPatientsAsync(new SearchPatientsRequest { Page = 2, PageSize = 3 });

        Assert.Equal(4, page2.TotalCount);
        Assert.Equal(2, page2.Page);
        Assert.Equal(3, page2.PageSize);
        Assert.Single(page2.Items);
    }

    [Theory]
    [InlineData("BOGUS", 1, 20)]
    [InlineData("ALL", 0, 20)]
    [InlineData("ALL", 1, 0)]
    [InlineData("ALL", 1, 101)]
    public async Task Search_InvalidInput_ThrowsValidation(string tab, int page, int pageSize)
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.SearchPatientsAsync(new SearchPatientsRequest { Tab = tab, Page = page, PageSize = pageSize }));
    }

    [Fact]
    public async Task GetDashboardSummaryAsync_ClassifiesStatusesStrictly_ExcludesAwaitingResultsAndMissingCompletedAt()
    {
        var options = new DbContextOptionsBuilder<SearchTestDbContext>()
            .UseInMemoryDatabase($"dashboard-kpi-{Guid.NewGuid():N}")
            .Options;
        using var db = new SearchTestDbContext(options);

        db.Hospitals.Add(new Hospital { Id = 1, HospitalCode = "H1", Name = "Cơ sở 1", IsActive = true });
        db.Departments.Add(new Department { Id = 1, HospitalId = 1, DepartmentCode = "NOI", Name = "Khoa Nội", IsActive = true });
        db.HospitalRooms.Add(new HospitalRoom { Id = 101, HospitalId = 1, DepartmentId = 1, RoomCode = "P101", Name = "Phòng 101", Status = "ACTIVE" });
        db.Services.Add(new Service { Id = 1, HospitalId = 1, DepartmentId = 1, ServiceCode = "SV1", Name = "Khám Nội", IsActive = true });

        db.Roles.Add(new Role { Id = 1, Code = RoleNames.Reception, Name = "Tiếp đón" });
        db.Permissions.Add(new Permission { Id = 1, Code = PermissionCodes.PatientCheckin });
        db.RolePermissions.Add(new RolePermission { RoleId = 1, PermissionId = 1 });
        db.Users.Add(new User { Id = 50, Email = "rec@h1.test", AccountStatus = AccountStatus.ACTIVE });
        db.UserRoles.Add(new UserRole { UserId = 50, RoleId = 1 });
        db.Staff.Add(new Staff { Id = 5, UserId = 50, HospitalId = 1, DepartmentId = 1, StaffCode = "ST-REC", FullName = "Lễ tân 1", Status = "ACTIVE" });

        for (var i = 1; i <= 12; i++)
        {
            db.Patients.Add(new Patient { Id = i, PatientCode = $"PAT-{i:0000}", FullName = $"BN {i}", Status = "ACTIVE" });
        }

        db.ExaminationSlots.Add(new ExaminationSlot
        {
            Id = 10,
            ServiceId = 1,
            RoomId = 101,
            StartsAt = Today.AddHours(8),
            EndsAt = Today.AddHours(11),
            Status = "OPEN",
        });

        // Appointments today:
        // 1: BOOKED (no visit) -> WaitingCheckIn (+1)
        // 2: CONFIRMED (no visit) -> WaitingCheckIn (+1)
        // 3: BOOKED but already has active visit -> NOT WaitingCheckIn
        // 4: CHECKED_IN -> NOT WaitingCheckIn
        // 5: CANCELLED -> NOT WaitingCheckIn
        // 6: NO_SHOW -> NOT WaitingCheckIn
        db.Appointments.AddRange(
            new Appointment { Id = 1, PatientId = 1, SlotId = 10, Status = "BOOKED" },
            new Appointment { Id = 2, PatientId = 2, SlotId = 10, Status = "CONFIRMED" },
            new Appointment { Id = 3, PatientId = 3, SlotId = 10, Status = "BOOKED" },
            new Appointment { Id = 4, PatientId = 4, SlotId = 10, Status = "CHECKED_IN" },
            new Appointment { Id = 5, PatientId = 5, SlotId = 10, Status = "CANCELLED" },
            new Appointment { Id = 6, PatientId = 6, SlotId = 10, Status = "NO_SHOW" });

        // Visits today (Room 101, Hospital 1):
        // InQueue (+4): CHECKED_IN, WAITING, CALLED, RETURN_CONSULTATION (including walk-in without AppointmentId)
        // InConsultation (+1): IN_PROGRESS
        // Excluded from InConsultation & InQueue: AWAITING_RESULTS, CANCELLED
        // CompletedToday (+2): COMPLETED with CompletedAt today (even if CheckedInAt was yesterday);
        // Excluded from CompletedToday: COMPLETED with null CompletedAt, or CompletedAt yesterday!
        db.Visits.AddRange(
            new Visit { Id = 1, PatientId = 3, AppointmentId = 3, ExaminationRoomId = 101, Status = "CHECKED_IN", CheckedInAt = Today.AddHours(8.1) },
            new Visit { Id = 2, PatientId = 4, AppointmentId = 4, ExaminationRoomId = 101, Status = "WAITING", CheckedInAt = Today.AddHours(8.2) },
            new Visit { Id = 3, PatientId = 7, ExaminationRoomId = 101, Status = "CALLED", CheckedInAt = Today.AddHours(8.3) },
            new Visit { Id = 4, PatientId = 8, ExaminationRoomId = 101, Status = "RETURN_CONSULTATION", CheckedInAt = Today.AddHours(8.4) },
            new Visit { Id = 5, PatientId = 9, ExaminationRoomId = 101, Status = "IN_PROGRESS", CheckedInAt = Today.AddHours(8.5) },
            new Visit { Id = 6, PatientId = 10, ExaminationRoomId = 101, Status = "AWAITING_RESULTS", CheckedInAt = Today.AddHours(8.6) },
            new Visit { Id = 7, PatientId = 11, ExaminationRoomId = 101, Status = "COMPLETED", CheckedInAt = Today.AddHours(7.5), CompletedAt = Today.AddHours(8.9) },
            new Visit { Id = 8, PatientId = 12, ExaminationRoomId = 101, Status = "COMPLETED", CheckedInAt = Today.AddDays(-1).AddHours(23), CompletedAt = Today.AddHours(0.5) },
            new Visit { Id = 9, PatientId = 1, ExaminationRoomId = 101, Status = "COMPLETED", CheckedInAt = Today.AddHours(8.0), CompletedAt = null },
            new Visit { Id = 10, PatientId = 2, ExaminationRoomId = 101, Status = "COMPLETED", CheckedInAt = Today.AddDays(-1).AddHours(9), CompletedAt = Today.AddDays(-1).AddHours(10) },
            new Visit { Id = 11, PatientId = 5, ExaminationRoomId = 101, Status = "CANCELLED", CheckedInAt = Today.AddHours(8.0) });

        await db.SaveChangesAsync();

        var sut = new ReceptionPatientSearchService(db, new FixedTimeProvider(FixedUtcNow));
        var summary = await sut.GetDashboardSummaryAsync(50);
        var searchRes = await sut.SearchPatientsAsync(new SearchPatientsRequest(), actorUserId: 50);

        Assert.Equal(2, summary.WaitingCheckIn);
        Assert.Equal(4, summary.InQueue);
        Assert.Equal(1, summary.InConsultation);
        Assert.Equal(2, summary.CompletedToday);
        Assert.Equal(summary, searchRes.DashboardSummary);
    }

    [Fact]
    public async Task GetDashboardSummaryAsync_EnforcesHospitalIsolationAndTwoLayerAuthz()
    {
        var options = new DbContextOptionsBuilder<SearchTestDbContext>()
            .UseInMemoryDatabase($"dashboard-hosp-{Guid.NewGuid():N}")
            .Options;
        using var db = new SearchTestDbContext(options);

        db.Hospitals.AddRange(
            new Hospital { Id = 1, HospitalCode = "H1", Name = "Cơ sở 1", IsActive = true },
            new Hospital { Id = 2, HospitalCode = "H2", Name = "Cơ sở 2", IsActive = true });
        db.Departments.AddRange(
            new Department { Id = 1, HospitalId = 1, DepartmentCode = "D1", Name = "Khoa 1", IsActive = true },
            new Department { Id = 2, HospitalId = 2, DepartmentCode = "D2", Name = "Khoa 2", IsActive = true });
        db.HospitalRooms.AddRange(
            new HospitalRoom { Id = 101, HospitalId = 1, DepartmentId = 1, RoomCode = "P101", Name = "Phòng 101", Status = "ACTIVE" },
            new HospitalRoom { Id = 201, HospitalId = 2, DepartmentId = 2, RoomCode = "P201", Name = "Phòng 201", Status = "ACTIVE" });
        db.Services.AddRange(
            new Service { Id = 1, HospitalId = 1, DepartmentId = 1, ServiceCode = "S1", Name = "DV 1", IsActive = true },
            new Service { Id = 2, HospitalId = 2, DepartmentId = 2, ServiceCode = "S2", Name = "DV 2", IsActive = true });

        db.Roles.AddRange(
            new Role { Id = 1, Code = RoleNames.Reception, Name = "Tiếp đón" },
            new Role { Id = 2, Code = RoleNames.Patient, Name = "Bệnh nhân" });
        db.Permissions.Add(new Permission { Id = 1, Code = PermissionCodes.PatientCheckin });
        db.RolePermissions.Add(new RolePermission { RoleId = 1, PermissionId = 1 });

        db.Users.AddRange(
            new User { Id = 101, Email = "rec1@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 102, Email = "rec2@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 103, Email = "pat@medflow.test", AccountStatus = AccountStatus.ACTIVE });
        db.UserRoles.AddRange(
            new UserRole { UserId = 101, RoleId = 1 },
            new UserRole { UserId = 102, RoleId = 1 },
            new UserRole { UserId = 103, RoleId = 2 });
        db.Staff.AddRange(
            new Staff { Id = 11, UserId = 101, HospitalId = 1, DepartmentId = 1, StaffCode = "ST1", FullName = "LT H1", Status = "ACTIVE" },
            new Staff { Id = 12, UserId = 102, HospitalId = 2, DepartmentId = 2, StaffCode = "ST2", FullName = "LT H2", Status = "ACTIVE" });

        db.Patients.AddRange(
            new Patient { Id = 1, PatientCode = "P1", FullName = "BN 1", Status = "ACTIVE" },
            new Patient { Id = 2, PatientCode = "P2", FullName = "BN 2", Status = "ACTIVE" });

        db.ExaminationSlots.AddRange(
            new ExaminationSlot { Id = 1, ServiceId = 1, RoomId = 101, StartsAt = Today.AddHours(8), EndsAt = Today.AddHours(9), Status = "OPEN" },
            new ExaminationSlot { Id = 2, ServiceId = 2, RoomId = 201, StartsAt = Today.AddHours(8), EndsAt = Today.AddHours(9), Status = "OPEN" });

        db.Appointments.Add(new Appointment { Id = 1, PatientId = 1, SlotId = 1, Status = "BOOKED" });
        db.Visits.AddRange(
            new Visit { Id = 1, PatientId = 1, ExaminationRoomId = 101, Status = "IN_PROGRESS", CheckedInAt = Today.AddHours(8.2) },
            new Visit { Id = 2, PatientId = 2, ExaminationRoomId = 201, Status = "WAITING", CheckedInAt = Today.AddHours(8.3) },
            new Visit { Id = 3, PatientId = 2, ExaminationRoomId = 201, Status = "COMPLETED", CheckedInAt = Today.AddHours(7.5), CompletedAt = Today.AddHours(8.4) });

        await db.SaveChangesAsync();

        var sut = new ReceptionPatientSearchService(db, new FixedTimeProvider(FixedUtcNow));

        var h1 = await sut.GetDashboardSummaryAsync(101);
        Assert.Equal(new ReceptionDashboardSummaryDto(1, 0, 1, 0), h1);

        var h2 = await sut.GetDashboardSummaryAsync(102);
        Assert.Equal(new ReceptionDashboardSummaryDto(0, 1, 0, 1), h2);

        await Assert.ThrowsAsync<ForbiddenException>(() => sut.GetDashboardSummaryAsync(103));
    }
}
