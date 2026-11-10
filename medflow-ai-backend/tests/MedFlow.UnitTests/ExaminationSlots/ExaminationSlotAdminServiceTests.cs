using System.Globalization;
using MedFlow.Application.DTOs.ExaminationSlots;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.ExaminationSlots;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.UnitTests.ExaminationSlots;

public sealed class ExaminationSlotAdminServiceTests : IDisposable
{
    private sealed class ExaminationSlotTestDbContext : DbContext, IApplicationDbContext
    {
        public ExaminationSlotTestDbContext(DbContextOptions<ExaminationSlotTestDbContext> options)
            : base(options)
        {
        }

        private static NotSupportedException NotUsed() => new("Not needed for ExaminationSlotAdminService unit tests.");

        public DbSet<User> Users => Set<User>();
        public DbSet<Role> Roles => Set<Role>();
        public DbSet<Permission> Permissions => throw NotUsed();
        public DbSet<UserRole> UserRoles => Set<UserRole>();
        public DbSet<RolePermission> RolePermissions => throw NotUsed();
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
        public DbSet<Visit> Visits => throw NotUsed();
        public DbSet<QueueEntry> QueueEntries => throw NotUsed();
        public DbSet<Payment> Payments => throw NotUsed();

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default)
            => Database.BeginTransactionAsync(ct);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Ignore<Permission>();
            modelBuilder.Ignore<RolePermission>();
            modelBuilder.Ignore<DoctorServiceAuthorization>();
            modelBuilder.Ignore<AuthSession>();
            modelBuilder.Ignore<AuthVerificationChallenge>();
            modelBuilder.Ignore<ServicePrice>();
            modelBuilder.Ignore<HospitalPolicy>();
            modelBuilder.Ignore<PatientFamilyLink>();
            modelBuilder.Ignore<Visit>();
            modelBuilder.Ignore<QueueEntry>();
            modelBuilder.Ignore<Payment>();

            modelBuilder.Entity<User>(b => b.HasKey(u => u.Id));
            modelBuilder.Entity<Role>(b => b.HasKey(r => r.Id));
            modelBuilder.Entity<UserRole>(b => b.HasKey(ur => new { ur.UserId, ur.RoleId }));
            modelBuilder.Entity<Patient>(b => b.HasKey(p => p.Id));
            modelBuilder.Entity<Hospital>(b => b.HasKey(h => h.Id));
            modelBuilder.Entity<AuditLog>(b =>
            {
                b.HasKey(a => a.Id);
                b.Ignore(a => a.IpAddress);
            });
            modelBuilder.Entity<Specialty>(b => b.HasKey(s => s.Id));
            modelBuilder.Entity<Department>(b => b.HasKey(d => d.Id));
            modelBuilder.Entity<Staff>(b => b.HasKey(s => s.Id));
            modelBuilder.Entity<HospitalRoom>(b =>
            {
                b.HasKey(r => r.Id);
                b.HasOne(r => r.Department).WithMany(d => d.HospitalRooms).HasForeignKey(r => r.DepartmentId);
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
                b.HasOne(s => s.Department).WithMany().HasForeignKey(s => s.DepartmentId);
                b.HasOne(s => s.Specialty).WithMany().HasForeignKey(s => s.SpecialtyId);
            });
            modelBuilder.Entity<ExaminationSlot>(b =>
            {
                b.HasKey(s => s.Id);
                b.HasOne(s => s.Doctor).WithMany(d => d.ExaminationSlots).HasForeignKey(s => s.DoctorId);
                b.HasOne(s => s.Service).WithMany().HasForeignKey(s => s.ServiceId);
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

    private readonly ExaminationSlotTestDbContext _db;
    private readonly ExaminationSlotAdminService _service;
    private readonly DateTime _baseDate = new(2026, 10, 27, 8, 0, 0, DateTimeKind.Utc);

    public ExaminationSlotAdminServiceTests()
    {
        var options = new DbContextOptionsBuilder<ExaminationSlotTestDbContext>()
            .UseInMemoryDatabase(databaseName: $"SlotAdminTests_{Guid.NewGuid()}")
            .Options;

        _db = new ExaminationSlotTestDbContext(options);
        _db.Database.EnsureCreated();

        SeedTestData();

        var timeProvider = new FixedTimeProvider(new DateTimeOffset(_baseDate));
        _service = new ExaminationSlotAdminService(_db, timeProvider);
    }

    public void Dispose()
    {
        _db.Database.EnsureDeleted();
        _db.Dispose();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private void SeedTestData()
    {
        var hosp1 = new Hospital { Id = 1, HospitalCode = "MEDFLOW-01", Name = "MedFlow Cơ sở 1", IsActive = true };
        var hosp2 = new Hospital { Id = 2, HospitalCode = "MEDFLOW-02", Name = "MedFlow Cơ sở 2", IsActive = true };
        _db.Hospitals.AddRange(hosp1, hosp2);

        var rolePatient = new Role { Id = 1, Code = "PATIENT", Name = "Bệnh nhân" };
        var roleDoctor = new Role { Id = 2, Code = "DOCTOR", Name = "Bác sĩ" };
        var roleHospAdmin = new Role { Id = 6, Code = "HOSPITAL_ADMIN", Name = "Quản trị bệnh viện" };
        var roleSysAdmin = new Role { Id = 7, Code = "SYSTEM_ADMIN", Name = "Quản trị hệ thống" };
        _db.Roles.AddRange(rolePatient, roleDoctor, roleHospAdmin, roleSysAdmin);

        var userPatient = new User { Id = 1, Email = "patient01@medflow.test", AccountStatus = AccountStatus.ACTIVE };
        var userDoctor = new User { Id = 3, Email = "doctor01@medflow.test", AccountStatus = AccountStatus.ACTIVE };
        var userHospAdmin1 = new User { Id = 11, Email = "hospital_admin01@medflow.test", AccountStatus = AccountStatus.ACTIVE };
        var userHospAdmin2 = new User { Id = 12, Email = "hospital_admin02@medflow.test", AccountStatus = AccountStatus.ACTIVE };
        var userSysAdmin = new User { Id = 13, Email = "system_admin01@medflow.test", AccountStatus = AccountStatus.ACTIVE };
        var userInactiveAdmin = new User { Id = 15, Email = "inactive_admin@medflow.test", AccountStatus = AccountStatus.INACTIVE };
        var userLockedAdmin = new User
        {
            Id = 16,
            Email = "locked_admin@medflow.test",
            AccountStatus = AccountStatus.ACTIVE,
            LockedUntil = _baseDate.AddHours(2),
        };
        _db.Users.AddRange(userPatient, userDoctor, userHospAdmin1, userHospAdmin2, userSysAdmin, userInactiveAdmin, userLockedAdmin);

        _db.UserRoles.AddRange(
            new UserRole { UserId = 1, RoleId = 1 },
            new UserRole { UserId = 3, RoleId = 2 },
            new UserRole { UserId = 11, RoleId = 6 },
            new UserRole { UserId = 12, RoleId = 6 },
            new UserRole { UserId = 13, RoleId = 7 },
            new UserRole { UserId = 15, RoleId = 6 },
            new UserRole { UserId = 16, RoleId = 6 });

        var specEndocrine = new Specialty { Id = 1, Name = "Nội tiết" };
        var specCardio = new Specialty { Id = 2, Name = "Tim mạch" };
        _db.Specialties.AddRange(specEndocrine, specCardio);

        var deptInternal = new Department { Id = 1, HospitalId = 1, Name = "Khoa Nội", DepartmentCode = "K-NOI", IsActive = true };
        var deptCardio = new Department { Id = 2, HospitalId = 1, Name = "Khoa Tim mạch", DepartmentCode = "K-TIM", IsActive = true };
        var deptHosp2 = new Department { Id = 3, HospitalId = 2, Name = "Khoa Nội Cơ sở 2", DepartmentCode = "K-NOI-2", IsActive = true };
        _db.Departments.AddRange(deptInternal, deptCardio, deptHosp2);

        var staff1 = new Staff { Id = 1, HospitalId = 1, DepartmentId = 1, FullName = "BS. Nguyễn Thu Lan", StaffCode = "ST-001", Status = "ACTIVE" };
        var staff2 = new Staff { Id = 2, HospitalId = 1, DepartmentId = 2, FullName = "BS. Trần Hoài Nam", StaffCode = "ST-002", Status = "ACTIVE" };
        var staffAdmin1 = new Staff { Id = 11, UserId = 11, HospitalId = 1, DepartmentId = 1, FullName = "QLBV Cơ sở 1", StaffCode = "ST-ADM-01", Status = "ACTIVE" };
        var staffAdmin2 = new Staff { Id = 12, UserId = 12, HospitalId = 2, DepartmentId = 3, FullName = "QLBV Cơ sở 2", StaffCode = "ST-ADM-02", Status = "ACTIVE" };
        var staffSysAdmin = new Staff { Id = 13, UserId = 13, HospitalId = 1, DepartmentId = 1, FullName = "QTHT", StaffCode = "ST-SYS-01", Status = "ACTIVE" };
        _db.Staff.AddRange(staff1, staff2, staffAdmin1, staffAdmin2, staffSysAdmin);

        var doc1 = new Doctor { Id = 1, HospitalId = 1, StaffId = 1, SpecialtyId = 1, DoctorCode = "DOC-001", IsActive = true };
        var doc2 = new Doctor { Id = 2, HospitalId = 1, StaffId = 2, SpecialtyId = 2, DoctorCode = "DOC-002", IsActive = true };
        _db.Doctors.AddRange(doc1, doc2);
        var srvEndocrine = new Service
        {
            Id = 1,
            ServiceCode = "EX-1-01",
            Name = "Khám Nội tiết",
            ServiceType = "EXAMINATION",
            DepartmentId = 1,
            SpecialtyId = 1,
            IsActive = true
        };
        var srvCardio = new Service
        {
            Id = 2,
            ServiceCode = "EX-2-03",
            Name = "Khám Tim mạch",
            ServiceType = "EXAMINATION",
            DepartmentId = 2,
            SpecialtyId = 2,
            IsActive = true
        };
        _db.Services.AddRange(srvEndocrine, srvCardio);

        var room102 = new HospitalRoom { Id = 1, HospitalId = 1, RoomCode = "PK-102", Name = "Phòng khám 102", DepartmentId = 1, Status = "ACTIVE", Capacity = 10 };
        var room108 = new HospitalRoom { Id = 2, HospitalId = 1, RoomCode = "PK-108", Name = "Phòng khám 108", DepartmentId = 2, Status = "ACTIVE", Capacity = 10 };
        _db.HospitalRooms.AddRange(room102, room108);

        // Slots:
        // Slot 1: 2026-10-27 08:00 - 08:30 (today), Doc 1, Srv 1, Room 1, Capacity 8, Status OPEN, 2 active appointments
        var slot1 = new ExaminationSlot
        {
            Id = 1,
            DoctorId = 1,
            ServiceId = 1,
            RoomId = 1,
            StartsAt = new DateTime(2026, 10, 27, 8, 0, 0),
            EndsAt = new DateTime(2026, 10, 27, 8, 30, 0),
            Capacity = 8,
            Status = "OPEN"
        };

        // Slot 2: 2026-10-27 08:30 - 09:00, Doc 2, Srv 2, Room 2, Capacity 2, Status OPEN, 2 active appointments (FULL)
        var slot2 = new ExaminationSlot
        {
            Id = 2,
            DoctorId = 2,
            ServiceId = 2,
            RoomId = 2,
            StartsAt = new DateTime(2026, 10, 27, 8, 30, 0),
            EndsAt = new DateTime(2026, 10, 27, 9, 0, 0),
            Capacity = 2,
            Status = "OPEN"
        };

        // Slot 3: 2026-10-28 09:00 - 09:30 (tomorrow), Doc 1, Srv 1, Room 1, Capacity 5, Status CLOSED
        var slot3 = new ExaminationSlot
        {
            Id = 3,
            DoctorId = 1,
            ServiceId = 1,
            RoomId = 1,
            StartsAt = new DateTime(2026, 10, 28, 9, 0, 0),
            EndsAt = new DateTime(2026, 10, 28, 9, 30, 0),
            Capacity = 5,
            Status = "CLOSED"
        };

        // Slot 4: 2026-11-15 10:00 - 10:30 (future > 7 days), Doc 2, Srv 2, Room 2, Capacity 6, Status OPEN, 0 appointments
        var slot4 = new ExaminationSlot
        {
            Id = 4,
            DoctorId = 2,
            ServiceId = 2,
            RoomId = 2,
            StartsAt = new DateTime(2026, 11, 15, 10, 0, 0),
            EndsAt = new DateTime(2026, 11, 15, 10, 30, 0),
            Capacity = 6,
            Status = "OPEN"
        };

        // Slot 5: Missing room and doctor (NeedsReview)
        var slot5 = new ExaminationSlot
        {
            Id = 5,
            DoctorId = null,
            ServiceId = 1,
            RoomId = null,
            StartsAt = new DateTime(2026, 10, 27, 13, 30, 0),
            EndsAt = new DateTime(2026, 10, 27, 14, 0, 0),
            Capacity = 5,
            Status = "OPEN"
        };

        _db.ExaminationSlots.AddRange(slot1, slot2, slot3, slot4, slot5);

        // Appointments:
        // Slot 1: 2 BOOKED + 1 CANCELLED -> Active: 2
        var pat1 = new Patient { Id = 1, PatientCode = "PAT-0001", FullName = "Nguyễn Văn An", Status = "ACTIVE" };
        var pat2 = new Patient { Id = 2, PatientCode = "PAT-0002", FullName = "Trần Thị Bình", Status = "ACTIVE" };
        _db.Patients.AddRange(pat1, pat2);

        var appt1 = new Appointment { Id = 1, AppointmentCode = "APT-202610-0001", PatientId = 1, SlotId = 1, Status = "BOOKED" };
        var appt2 = new Appointment { Id = 2, AppointmentCode = "APT-202610-0002", PatientId = 2, SlotId = 1, Status = "CHECKED_IN" };
        var appt3 = new Appointment { Id = 3, AppointmentCode = "APT-202610-0003", PatientId = 1, SlotId = 1, Status = "CANCELLED" };

        // Slot 2: 2 BOOKED -> Active: 2 (Capacity = 2 -> FULL)
        var appt4 = new Appointment { Id = 4, AppointmentCode = "APT-202610-0004", PatientId = 1, SlotId = 2, Status = "BOOKED" };
        var appt5 = new Appointment { Id = 5, AppointmentCode = "APT-202610-0005", PatientId = 2, SlotId = 2, Status = "BOOKED" };

        _db.Appointments.AddRange(appt1, appt2, appt3, appt4, appt5);
        _db.SaveChanges();
    }

    [Fact]
    public async Task GetExaminationSlotList_NullRequest_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.GetExaminationSlotListAsync(null!));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task GetExaminationSlotList_InvalidPage_ThrowsArgumentException(int page)
    {
        var request = new GetExaminationSlotListRequest(Page: page);
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.GetExaminationSlotListAsync(request));
        Assert.Contains("Trang yêu cầu", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(101)]
    public async Task GetExaminationSlotList_InvalidPageSize_ThrowsArgumentException(int pageSize)
    {
        var request = new GetExaminationSlotListRequest(PageSize: pageSize);
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.GetExaminationSlotListAsync(request));
        Assert.Contains("Số lượng bản ghi trên một trang", ex.Message);
    }

    [Fact]
    public async Task GetExaminationSlotList_DefaultQuery_ReturnsAllSlotsAndMetrics()
    {
        var request = new GetExaminationSlotListRequest();
        var result = await _service.GetExaminationSlotListAsync(request);

        Assert.NotNull(result);
        Assert.Equal(5, result.Total);
        Assert.Equal(5, result.Data.Count);
        Assert.Equal(1, result.Page);
        Assert.Equal(20, result.PageSize);
        Assert.Equal(1, result.TotalPages);

        // Check metrics
        Assert.NotNull(result.Metrics);
        Assert.Equal(5, result.Metrics.Total);
        Assert.Equal(4, result.Metrics.OpenCount); // slots 1, 2, 4, 5
        Assert.Equal(2, result.Metrics.BookedCount); // slots 1 and 2
        Assert.Equal(4, result.Metrics.UpcomingCount); // slots 1, 2, 3, 5 within 7 days
        Assert.Equal(2, result.Metrics.ReviewCount); // slot 3 (CLOSED) and slot 5 (missing doctor/room)
    }

    [Fact]
    public async Task GetExaminationSlotList_CalculatesActiveAppointmentsAndCapacityCorrectly()
    {
        var request = new GetExaminationSlotListRequest();
        var result = await _service.GetExaminationSlotListAsync(request);

        var slot1Dto = result.Data.First(s => s.Id == 1);
        Assert.Equal("SLT-001", slot1Dto.Code);
        Assert.Equal(2, slot1Dto.CapacityBooked); // cancelled excluded
        Assert.Equal(8, slot1Dto.CapacityMax);
        Assert.Equal("OPEN", slot1Dto.Status);
        Assert.Equal("BS. Nguyễn Thu Lan", slot1Dto.DoctorName);
        Assert.Equal("Khám Nội tiết", slot1Dto.ServiceName);
        Assert.Equal("EX-1-01", slot1Dto.ServiceCode);
        Assert.Equal("OUTPATIENT", slot1Dto.ServiceType);
        Assert.Equal("Nội tiết", slot1Dto.Specialty);
        Assert.Equal("PK-102", slot1Dto.Room);
        Assert.Equal("2026-10-27", slot1Dto.Date);
        Assert.Equal("27/10", slot1Dto.DateDisplay);
        Assert.Equal("08:00", slot1Dto.StartTime);
        Assert.Equal("08:30", slot1Dto.EndTime);
        Assert.False(slot1Dto.NeedsReview);

        // Slot 2 capacity booked = 2, max = 2 => FULL
        var slot2Dto = result.Data.First(s => s.Id == 2);
        Assert.Equal(2, slot2Dto.CapacityBooked);
        Assert.Equal(2, slot2Dto.CapacityMax);
        Assert.Equal("FULL", slot2Dto.Status);

        // Slot 3 status is CLOSED
        var slot3Dto = result.Data.First(s => s.Id == 3);
        Assert.Equal("CLOSED", slot3Dto.Status);

        // Slot 5 needs review
        var slot5Dto = result.Data.First(s => s.Id == 5);
        Assert.True(slot5Dto.NeedsReview);
    }

    [Fact]
    public async Task GetExaminationSlotList_FilterByKeyword_DoctorName()
    {
        var request = new GetExaminationSlotListRequest(Keyword: "Nguyễn Thu Lan");
        var result = await _service.GetExaminationSlotListAsync(request);

        Assert.Equal(2, result.Total);
        Assert.All(result.Data, s => Assert.Equal("BS. Nguyễn Thu Lan", s.DoctorName));
    }

    [Fact]
    public async Task GetExaminationSlotList_FilterByKeyword_SlotCode()
    {
        var request = new GetExaminationSlotListRequest(Keyword: "SLT-002");
        var result = await _service.GetExaminationSlotListAsync(request);

        Assert.Single(result.Data);
        Assert.Equal(2, result.Data[0].Id);
    }

    [Fact]
    public async Task GetExaminationSlotList_FilterByKeyword_ServiceName()
    {
        var request = new GetExaminationSlotListRequest(Keyword: "Tim mạch");
        var result = await _service.GetExaminationSlotListAsync(request);

        Assert.Equal(2, result.Total); // slots 2 and 4
        Assert.All(result.Data, s => Assert.Equal("Khám Tim mạch", s.ServiceName));
    }

    [Fact]
    public async Task GetExaminationSlotList_FilterByKeyword_RoomCode()
    {
        var request = new GetExaminationSlotListRequest(Keyword: "PK-108");
        var result = await _service.GetExaminationSlotListAsync(request);

        Assert.Equal(2, result.Total); // slots 2 and 4
        Assert.All(result.Data, s => Assert.Equal("PK-108", s.Room));
    }

    [Fact]
    public async Task GetExaminationSlotList_FilterByDate_ExactMatch()
    {
        var request = new GetExaminationSlotListRequest(Date: "2026-10-27");
        var result = await _service.GetExaminationSlotListAsync(request);

        Assert.Equal(3, result.Total); // slots 1, 2, 5
        Assert.All(result.Data, s => Assert.Equal("2026-10-27", s.Date));
    }

    [Fact]
    public async Task GetExaminationSlotList_FilterByDateRange()
    {
        var request = new GetExaminationSlotListRequest(FromDate: "2026-10-28", ToDate: "2026-10-29");
        var result = await _service.GetExaminationSlotListAsync(request);

        Assert.Single(result.Data);
        Assert.Equal(3, result.Data[0].Id);
    }

    [Fact]
    public async Task GetExaminationSlotList_FilterByDoctorId()
    {
        var request = new GetExaminationSlotListRequest(DoctorId: 2);
        var result = await _service.GetExaminationSlotListAsync(request);

        Assert.Equal(2, result.Total);
        Assert.All(result.Data, s => Assert.Equal(2, s.DoctorId));
    }

    [Fact]
    public async Task GetExaminationSlotList_FilterByServiceId()
    {
        var request = new GetExaminationSlotListRequest(ServiceId: 1);
        var result = await _service.GetExaminationSlotListAsync(request);

        Assert.Equal(3, result.Total); // slots 1, 3, 5
        Assert.All(result.Data, s => Assert.Equal(1, s.ServiceId));
    }

    [Fact]
    public async Task GetExaminationSlotList_FilterByRoomId()
    {
        var request = new GetExaminationSlotListRequest(RoomId: 1);
        var result = await _service.GetExaminationSlotListAsync(request);

        Assert.Equal(2, result.Total); // slots 1, 3
        Assert.All(result.Data, s => Assert.Equal(1, s.RoomId));
    }

    [Fact]
    public async Task GetExaminationSlotList_FilterBySpecialty()
    {
        var request = new GetExaminationSlotListRequest(Specialty: "Tim mạch");
        var result = await _service.GetExaminationSlotListAsync(request);

        Assert.Equal(2, result.Total);
        Assert.All(result.Data, s => Assert.Equal("Tim mạch", s.Specialty));
    }

    [Fact]
    public async Task GetExaminationSlotList_FilterByStatus_Closed()
    {
        var request = new GetExaminationSlotListRequest(Status: "CLOSED");
        var result = await _service.GetExaminationSlotListAsync(request);

        Assert.Single(result.Data);
        Assert.Equal(3, result.Data[0].Id);
        Assert.Equal("CLOSED", result.Data[0].Status);
    }

    [Fact]
    public async Task GetExaminationSlotList_FilterByStatus_Open()
    {
        var request = new GetExaminationSlotListRequest(Status: "OPEN");
        var result = await _service.GetExaminationSlotListAsync(request);

        Assert.Equal(4, result.Total);
        Assert.DoesNotContain(result.Data, s => s.Id == 3);
    }

    [Fact]
    public async Task GetExaminationSlotList_Pagination_Page1AndPage2()
    {
        var page1Req = new GetExaminationSlotListRequest(Page: 1, PageSize: 2);
        var page1 = await _service.GetExaminationSlotListAsync(page1Req);

        Assert.Equal(5, page1.Total);
        Assert.Equal(2, page1.Data.Count);
        Assert.Equal(3, page1.TotalPages);

        var page2Req = new GetExaminationSlotListRequest(Page: 2, PageSize: 2);
        var page2 = await _service.GetExaminationSlotListAsync(page2Req);

        Assert.Equal(2, page2.Data.Count);
        Assert.NotEqual(page1.Data[0].Id, page2.Data[0].Id);
    }

    [Fact]
    public async Task GetExaminationSlotList_NoMatches_ReturnsEmptyListAndTotalZero()
    {
        var request = new GetExaminationSlotListRequest(Keyword: "KhongTonTai12345");
        var result = await _service.GetExaminationSlotListAsync(request);

        Assert.Equal(0, result.Total);
        Assert.Empty(result.Data);
        Assert.Equal(1, result.TotalPages);
        Assert.Equal(5, result.Metrics.Total); // Metrics still reflect overall DB count
    }

    // ─────────────────────────────────────────────────────────────
    // Create Examination Slot Tests
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateExaminationSlot_NullRequest_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.CreateExaminationSlotAsync(null!));
    }

    [Fact]
    public async Task CreateExaminationSlot_MissingTimeWindow_ThrowsArgumentException()
    {
        var request = new CreateExaminationSlotRequest(ServiceId: 1, DoctorId: 1);
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateExaminationSlotAsync(request));
        Assert.Contains("Thời gian bắt đầu và kết thúc", ex.Message);
    }

    [Fact]
    public async Task CreateExaminationSlot_InvalidDateFormat_ThrowsArgumentException()
    {
        var request = new CreateExaminationSlotRequest(
            ServiceId: 1,
            DoctorId: 1,
            Date: "invalid-date",
            StartTime: "09:00",
            EndTime: "09:30"
        );
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateExaminationSlotAsync(request));
        Assert.Contains("Định dạng ngày không hợp lệ", ex.Message);
    }

    [Fact]
    public async Task CreateExaminationSlot_EndTimeBeforeStartTime_ThrowsArgumentException()
    {
        var request = new CreateExaminationSlotRequest(
            ServiceId: 1,
            DoctorId: 1,
            Date: "2026-10-30",
            StartTime: "10:00",
            EndTime: "09:00"
        );
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateExaminationSlotAsync(request));
        Assert.Contains("Giờ kết thúc phải sau giờ bắt đầu", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    [InlineData(101)]
    public async Task CreateExaminationSlot_CapacityOutOfRange_ThrowsArgumentException(int capacity)
    {
        var request = new CreateExaminationSlotRequest(
            ServiceId: 1,
            DoctorId: 1,
            Date: "2026-10-30",
            StartTime: "10:00",
            EndTime: "10:30",
            Capacity: capacity
        );
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateExaminationSlotAsync(request));
        Assert.Contains("Sức chứa phải từ 1 đến 100", ex.Message);
    }

    [Fact]
    public async Task CreateExaminationSlot_InvalidStatus_ThrowsArgumentException()
    {
        var request = new CreateExaminationSlotRequest(
            ServiceId: 1,
            DoctorId: 1,
            Date: "2026-10-30",
            StartTime: "10:00",
            EndTime: "10:30",
            Status: "INVALID_STATUS"
        );
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateExaminationSlotAsync(request));
        Assert.Contains("Trạng thái khung giờ không hợp lệ", ex.Message);
    }

    [Fact]
    public async Task CreateExaminationSlot_NoServiceAndNoDoctor_ThrowsArgumentException()
    {
        var request = new CreateExaminationSlotRequest(
            Date: "2026-10-30",
            StartTime: "10:00",
            EndTime: "10:30"
        );
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateExaminationSlotAsync(request));
        Assert.Contains("Vui lòng chỉ định dịch vụ khám hoặc bác sĩ", ex.Message);
    }

    [Fact]
    public async Task CreateExaminationSlot_NonExistentServiceId_ThrowsKeyNotFoundException()
    {
        var request = new CreateExaminationSlotRequest(
            ServiceId: 9999,
            DoctorId: 1,
            Date: "2026-10-30",
            StartTime: "10:00",
            EndTime: "10:30"
        );
        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.CreateExaminationSlotAsync(request));
        Assert.Contains("Không tìm thấy dịch vụ khám", ex.Message);
    }

    [Fact]
    public async Task CreateExaminationSlot_NonExistentDoctorId_ThrowsKeyNotFoundException()
    {
        var request = new CreateExaminationSlotRequest(
            ServiceId: 1,
            DoctorId: 9999,
            Date: "2026-10-30",
            StartTime: "10:00",
            EndTime: "10:30"
        );
        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.CreateExaminationSlotAsync(request));
        Assert.Contains("Không tìm thấy bác sĩ", ex.Message);
    }

    [Fact]
    public async Task CreateExaminationSlot_NonExistentRoomId_ThrowsKeyNotFoundException()
    {
        var request = new CreateExaminationSlotRequest(
            ServiceId: 1,
            DoctorId: 1,
            RoomId: 9999,
            Date: "2026-10-30",
            StartTime: "10:00",
            EndTime: "10:30"
        );
        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.CreateExaminationSlotAsync(request));
        Assert.Contains("Không tìm thấy buồng khám", ex.Message);
    }

    [Fact]
    public async Task CreateExaminationSlot_InactiveService_ThrowsArgumentException()
    {
        var inactiveService = new Service
        {
            Id = 50,
            Name = "Dịch vụ ngừng hoạt động",
            ServiceCode = "EX-INACT",
            IsActive = false
        };
        _db.Services.Add(inactiveService);
        await _db.SaveChangesAsync();

        var request = new CreateExaminationSlotRequest(
            ServiceId: 50,
            DoctorId: 1,
            Date: "2026-10-30",
            StartTime: "10:00",
            EndTime: "10:30"
        );
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateExaminationSlotAsync(request));
        Assert.Contains("tạm ngưng hoặc không hoạt động", ex.Message);
    }

    [Fact]
    public async Task CreateExaminationSlot_InactiveDoctor_ThrowsArgumentException()
    {
        var inactiveDoctor = new Doctor
        {
            Id = 50,
            DoctorCode = "DOC-INACT",
            IsActive = false
        };
        _db.Doctors.Add(inactiveDoctor);
        await _db.SaveChangesAsync();

        var request = new CreateExaminationSlotRequest(
            ServiceId: 1,
            DoctorId: 50,
            Date: "2026-10-30",
            StartTime: "10:00",
            EndTime: "10:30"
        );
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateExaminationSlotAsync(request));
        Assert.Contains("không ở trạng thái hoạt động", ex.Message);
    }

    [Fact]
    public async Task CreateExaminationSlot_InactiveRoom_ThrowsArgumentException()
    {
        var inactiveRoom = new HospitalRoom
        {
            Id = 50,
            RoomCode = "PK-INACT",
            Name = "Phòng bảo trì",
            Status = "INACTIVE"
        };
        _db.HospitalRooms.Add(inactiveRoom);
        await _db.SaveChangesAsync();

        var request = new CreateExaminationSlotRequest(
            ServiceId: 1,
            DoctorId: 1,
            RoomId: 50,
            Date: "2026-10-30",
            StartTime: "10:00",
            EndTime: "10:30"
        );
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateExaminationSlotAsync(request));
        Assert.Contains("Buồng khám đang ngừng hoạt động", ex.Message);
    }

    [Fact]
    public async Task CreateExaminationSlot_DoctorScheduleConflict_ThrowsInvalidOperationException()
    {
        // Doctor 1 already has slot 1 at 2026-10-27 08:00 - 08:30
        var request = new CreateExaminationSlotRequest(
            ServiceId: 1,
            DoctorId: 1,
            Date: "2026-10-27",
            StartTime: "08:15",
            EndTime: "08:45"
        );
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CreateExaminationSlotAsync(request));
        Assert.Contains("Xung đột lịch bác sĩ", ex.Message);
    }

    [Fact]
    public async Task CreateExaminationSlot_RoomScheduleConflict_ThrowsInvalidOperationException()
    {
        // Room 2 (PK-108) already has slot 2 at 2026-10-27 08:30 - 09:00
        var request = new CreateExaminationSlotRequest(
            ServiceId: 1,
            DoctorId: 1, // Doc 1 is free at this time
            RoomId: 2,   // But Room 2 is occupied by slot 2
            Date: "2026-10-27",
            StartTime: "08:45",
            EndTime: "09:15"
        );
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CreateExaminationSlotAsync(request));
        Assert.Contains("Xung đột buồng khám", ex.Message);
    }

    [Fact]
    public async Task CreateExaminationSlot_ValidDoctorAndService_Success()
    {
        var request = new CreateExaminationSlotRequest(
            ServiceId: 1,
            DoctorId: 1,
            RoomId: 1,
            Date: "2026-10-30",
            StartTime: "14:00",
            EndTime: "14:30",
            Capacity: 6,
            Status: "OPEN"
        );

        var result = await _service.CreateExaminationSlotAsync(request);

        Assert.NotNull(result);
        Assert.True(result.Id > 0);
        Assert.Equal($"SLT-{result.Id:D3}", result.Code);
        Assert.Equal(1, result.DoctorId);
        Assert.Equal("BS. Nguyễn Thu Lan", result.DoctorName);
        Assert.Equal(1, result.ServiceId);
        Assert.Equal("Khám Nội tiết", result.ServiceName);
        Assert.Equal("EX-1-01", result.ServiceCode);
        Assert.Equal("OUTPATIENT", result.ServiceType);
        Assert.Equal("Nội tiết", result.Specialty);
        Assert.Equal(1, result.RoomId);
        Assert.Equal("PK-102", result.Room);
        Assert.Equal(6, result.CapacityMax);
        Assert.Equal(0, result.CapacityBooked);
        Assert.Equal("2026-10-30", result.Date);
        Assert.Equal("30/10", result.DateDisplay);
        Assert.Equal("14:00", result.StartTime);
        Assert.Equal("14:30", result.EndTime);
        Assert.Equal("OPEN", result.Status);
        Assert.False(result.NeedsReview);
    }

    [Fact]
    public async Task CreateExaminationSlot_ServiceBasedOnly_Success()
    {
        // Diagnostic service without specific doctor assigned
        var request = new CreateExaminationSlotRequest(
            ServiceId: 2,
            RoomId: 2,
            Date: "2026-10-30",
            StartTime: "15:00",
            EndTime: "15:30",
            Capacity: 10
        );

        var result = await _service.CreateExaminationSlotAsync(request);

        Assert.NotNull(result);
        Assert.True(result.Id > 0);
        Assert.Null(result.DoctorId);
        Assert.Equal(string.Empty, result.DoctorName);
        Assert.Equal(2, result.ServiceId);
        Assert.Equal("Khám Tim mạch", result.ServiceName);
        Assert.Equal(10, result.CapacityMax);
        Assert.Equal("OPEN", result.Status);
    }

    [Fact]
    public async Task CreateExaminationSlot_LookupByCodes_Success()
    {
        var request = new CreateExaminationSlotRequest(
            ServiceCode: "EX-1-01",
            DoctorCode: "DOC-001",
            RoomCode: "PK-102",
            Date: "2026-10-31",
            StartTime: "09:00",
            EndTime: "09:30"
        );

        var result = await _service.CreateExaminationSlotAsync(request);

        Assert.NotNull(result);
        Assert.Equal(1, result.DoctorId);
        Assert.Equal(1, result.ServiceId);
        Assert.Equal(1, result.RoomId);
        Assert.Equal("BS. Nguyễn Thu Lan", result.DoctorName);
        Assert.Equal("Khám Nội tiết", result.ServiceName);
        Assert.Equal("PK-102", result.Room);
    }

    [Fact]
    public async Task CreateExaminationSlot_WithDirectStartsAtEndsAt_Success()
    {
        var start = new DateTime(2026, 11, 1, 8, 0, 0);
        var end = new DateTime(2026, 11, 1, 8, 30, 0);

        var request = new CreateExaminationSlotRequest(
            ServiceId: 1,
            DoctorId: 1,
            StartsAt: start,
            EndsAt: end,
            Capacity: 5
        );

        var result = await _service.CreateExaminationSlotAsync(request);

        Assert.NotNull(result);
        Assert.Equal("2026-11-01", result.Date);
        Assert.Equal("08:00", result.StartTime);
        Assert.Equal("08:30", result.EndTime);
        Assert.Equal(5, result.CapacityMax);
    }

    // ─────────────────────────────────────────────────────────────
    // Update Examination Slot Tests
    // ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task UpdateExaminationSlot_InvalidId_ThrowsArgumentException(long id)
    {
        var request = new UpdateExaminationSlotRequest(Capacity: 10);
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateExaminationSlotAsync(id, request));
        Assert.Contains("Mã khung giờ khám không hợp lệ", ex.Message);
    }

    [Fact]
    public async Task UpdateExaminationSlot_NullRequest_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.UpdateExaminationSlotAsync(1, null!));
    }

    [Fact]
    public async Task UpdateExaminationSlot_NonExistentSlotId_ThrowsKeyNotFoundException()
    {
        var request = new UpdateExaminationSlotRequest(Capacity: 10);
        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.UpdateExaminationSlotAsync(9999, request));
        Assert.Contains("Không tìm thấy khung giờ khám", ex.Message);
    }

    [Fact]
    public async Task UpdateExaminationSlot_EndTimeBeforeStartTime_ThrowsArgumentException()
    {
        var request = new UpdateExaminationSlotRequest(
            StartTime: "11:00",
            EndTime: "10:00"
        );
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateExaminationSlotAsync(1, request));
        Assert.Contains("Giờ kết thúc phải sau giờ bắt đầu", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(105)]
    public async Task UpdateExaminationSlot_CapacityOutOfRange_ThrowsArgumentException(int capacity)
    {
        var request = new UpdateExaminationSlotRequest(Capacity: capacity);
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateExaminationSlotAsync(1, request));
        Assert.Contains("Sức chứa phải từ 1 đến 100", ex.Message);
    }

    [Fact]
    public async Task UpdateExaminationSlot_ReduceCapacityBelowActiveBookings_ThrowsArgumentException()
    {
        // Slot 1 has 2 active appointments (appt 1 and appt 2)
        var request = new UpdateExaminationSlotRequest(Capacity: 1);
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateExaminationSlotAsync(1, request));
        Assert.Contains("Không thể giảm sức chứa xuống 1 vì đã có 2 lượt đặt chỗ", ex.Message);
    }

    [Fact]
    public async Task UpdateExaminationSlot_InvalidStatus_ThrowsArgumentException()
    {
        var request = new UpdateExaminationSlotRequest(Status: "UNKNOWN");
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateExaminationSlotAsync(1, request));
        Assert.Contains("Trạng thái khung giờ không hợp lệ", ex.Message);
    }

    [Fact]
    public async Task UpdateExaminationSlot_NonExistentServiceId_ThrowsKeyNotFoundException()
    {
        var request = new UpdateExaminationSlotRequest(ServiceId: 9999);
        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.UpdateExaminationSlotAsync(1, request));
        Assert.Contains("Không tìm thấy dịch vụ khám", ex.Message);
    }

    [Fact]
    public async Task UpdateExaminationSlot_NonExistentDoctorId_ThrowsKeyNotFoundException()
    {
        var request = new UpdateExaminationSlotRequest(DoctorId: 9999);
        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.UpdateExaminationSlotAsync(1, request));
        Assert.Contains("Không tìm thấy bác sĩ", ex.Message);
    }

    [Fact]
    public async Task UpdateExaminationSlot_NonExistentRoomId_ThrowsKeyNotFoundException()
    {
        var request = new UpdateExaminationSlotRequest(RoomId: 9999);
        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.UpdateExaminationSlotAsync(1, request));
        Assert.Contains("Không tìm thấy buồng khám", ex.Message);
    }

    [Fact]
    public async Task UpdateExaminationSlot_InactiveService_ThrowsArgumentException()
    {
        var inactiveService = new Service
        {
            Id = 60,
            Name = "Dịch vụ ngưng",
            ServiceCode = "EX-INACT2",
            IsActive = false
        };
        _db.Services.Add(inactiveService);
        await _db.SaveChangesAsync();

        var request = new UpdateExaminationSlotRequest(ServiceId: 60);
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateExaminationSlotAsync(1, request));
        Assert.Contains("tạm ngưng hoặc không hoạt động", ex.Message);
    }

    [Fact]
    public async Task UpdateExaminationSlot_InactiveDoctor_ThrowsArgumentException()
    {
        var inactiveDoctor = new Doctor
        {
            Id = 60,
            DoctorCode = "DOC-INACT2",
            IsActive = false
        };
        _db.Doctors.Add(inactiveDoctor);
        await _db.SaveChangesAsync();

        var request = new UpdateExaminationSlotRequest(DoctorId: 60);
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateExaminationSlotAsync(1, request));
        Assert.Contains("không ở trạng thái hoạt động", ex.Message);
    }

    [Fact]
    public async Task UpdateExaminationSlot_InactiveRoom_ThrowsArgumentException()
    {
        var inactiveRoom = new HospitalRoom
        {
            Id = 60,
            RoomCode = "PK-INACT2",
            Name = "Phòng sửa chữa",
            Status = "INACTIVE"
        };
        _db.HospitalRooms.Add(inactiveRoom);
        await _db.SaveChangesAsync();

        var request = new UpdateExaminationSlotRequest(RoomId: 60);
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateExaminationSlotAsync(1, request));
        Assert.Contains("Buồng khám đang ngừng hoạt động", ex.Message);
    }

    [Fact]
    public async Task UpdateExaminationSlot_DoctorScheduleConflict_ThrowsInvalidOperationException()
    {
        // Slot 2 has Doctor 2 at 2026-10-27 08:30 - 09:00
        // Attempting to move Slot 1 to overlap with Doctor 2 while assigning Doctor 2
        var request = new UpdateExaminationSlotRequest(
            DoctorId: 2,
            Date: "2026-10-27",
            StartTime: "08:45",
            EndTime: "09:15"
        );
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.UpdateExaminationSlotAsync(1, request));
        Assert.Contains("Xung đột lịch bác sĩ", ex.Message);
    }

    [Fact]
    public async Task UpdateExaminationSlot_RoomScheduleConflict_ThrowsInvalidOperationException()
    {
        // Slot 2 has Room 2 at 2026-10-27 08:30 - 09:00
        // Attempting to move Slot 1 to Room 2 at overlapping time
        var request = new UpdateExaminationSlotRequest(
            RoomId: 2,
            Date: "2026-10-27",
            StartTime: "08:45",
            EndTime: "09:15"
        );
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.UpdateExaminationSlotAsync(1, request));
        Assert.Contains("Xung đột buồng khám", ex.Message);
    }

    [Fact]
    public async Task UpdateExaminationSlot_UpdateCapacityAndStatus_Success()
    {
        // Slot 1 has capacity 8, activeBookings 2. Update to capacity 12, CLOSED.
        var request = new UpdateExaminationSlotRequest(
            Capacity: 12,
            Status: "CLOSED"
        );

        var result = await _service.UpdateExaminationSlotAsync(1, request);

        Assert.NotNull(result);
        Assert.Equal(1, result.Id);
        Assert.Equal(12, result.CapacityMax);
        Assert.Equal(2, result.CapacityBooked);
        Assert.Equal("CLOSED", result.Status);
    }

    [Fact]
    public async Task UpdateExaminationSlot_UpdateDoctorAndRoom_Success()
    {
        // Move slot 4 (at future 2026-11-15 10:00 - 10:30) to Doctor 1 and Room 1
        var request = new UpdateExaminationSlotRequest(
            DoctorId: 1,
            RoomId: 1
        );

        var result = await _service.UpdateExaminationSlotAsync(4, request);

        Assert.NotNull(result);
        Assert.Equal(4, result.Id);
        Assert.Equal(1, result.DoctorId);
        Assert.Equal("BS. Nguyễn Thu Lan", result.DoctorName);
        Assert.Equal(1, result.RoomId);
        Assert.Equal("PK-102", result.Room);
    }

    [Fact]
    public async Task UpdateExaminationSlot_UpdateDirectStartsAtEndsAt_Success()
    {
        var newStart = new DateTime(2026, 11, 25, 9, 0, 0);
        var newEnd = new DateTime(2026, 11, 25, 9, 30, 0);

        var request = new UpdateExaminationSlotRequest(
            StartsAt: newStart,
            EndsAt: newEnd,
            Capacity: 10
        );

        var result = await _service.UpdateExaminationSlotAsync(4, request);

        Assert.NotNull(result);
        Assert.Equal("2026-11-25", result.Date);
        Assert.Equal("09:00", result.StartTime);
        Assert.Equal("09:30", result.EndTime);
        Assert.Equal(10, result.CapacityMax);
    }

    [Fact]
    public async Task UpdateExaminationSlot_SelfNotConflictingWithOwnTime_Success()
    {
        // Updating slot 1 capacity without changing its own time shouldn't conflict with itself
        var request = new UpdateExaminationSlotRequest(
            Capacity: 10
        );

        var result = await _service.UpdateExaminationSlotAsync(1, request);

        Assert.NotNull(result);
        Assert.Equal(10, result.CapacityMax);
        Assert.Equal("2026-10-27", result.Date);
        Assert.Equal("08:00", result.StartTime);
        Assert.Equal("08:30", result.EndTime);
    }

    // ─────────────────────────────────────────────────────────────
    // Close / Batch-Close / Reopen Examination Slot Tests (M2-216)
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task CloseExaminationSlot_NullRequest_ThrowsValidationException()
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.CloseExaminationSlotAsync(11, "1", null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("INVALID-CODE")]
    public async Task CloseExaminationSlot_InvalidSlotIdentifier_ThrowsValidationException(string idOrCode)
    {
        var req = new CloseExaminationSlotRequest(ReasonCode: "DOCTOR_UNAVAILABLE");
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.CloseExaminationSlotAsync(11, idOrCode, req));
    }

    [Fact]
    public async Task CloseExaminationSlot_NonExistentSlot_ThrowsNotFoundException()
    {
        var req = new CloseExaminationSlotRequest(ReasonCode: "DOCTOR_UNAVAILABLE");
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.CloseExaminationSlotAsync(11, "9999", req));
    }

    [Fact]
    public async Task CloseExaminationSlot_InvalidReasonCode_ThrowsValidationException()
    {
        var req = new CloseExaminationSlotRequest(ReasonCode: "UNKNOWN_REASON");
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _service.CloseExaminationSlotAsync(11, "4", req));
        Assert.Contains("Mã lý do đóng khung giờ không hợp lệ", ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    public async Task CloseExaminationSlot_ReasonOtherWithShortNote_ThrowsValidationException(string? shortNote)
    {
        var req = new CloseExaminationSlotRequest(
            ReasonCode: "OTHER",
            Note: shortNote);
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _service.CloseExaminationSlotAsync(11, "4", req));
        Assert.Contains("tối thiểu 5 ký tự", ex.Message);
    }

    [Fact]
    public async Task CloseExaminationSlot_InvalidAffectedBookingsAction_ThrowsValidationException()
    {
        var req = new CloseExaminationSlotRequest(
            ReasonCode: "DOCTOR_UNAVAILABLE",
            AffectedBookingsAction: "DELETE_ALL");
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _service.CloseExaminationSlotAsync(11, "4", req));
        Assert.Contains("KEEP_EXISTING hoặc RESCHEDULE_REQUIRED", ex.Message);
    }

    [Fact]
    public async Task CloseExaminationSlot_AlreadyClosedSlot_ThrowsConflictException()
    {
        // Slot 3 is already CLOSED
        var req = new CloseExaminationSlotRequest(ReasonCode: "DOCTOR_UNAVAILABLE");
        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            _service.CloseExaminationSlotAsync(11, "SLT-003", req));
        Assert.Contains("đã ở trạng thái đóng", ex.Message);
    }

    [Fact]
    public async Task CloseExaminationSlot_EmptySlot_ClosesCleanlyAndWritesAuditLogAndReconstructsMetadata()
    {
        // Slot 4 has 0 appointments
        var req = new CloseExaminationSlotRequest(
            ReasonCode: "EQUIPMENT_MAINTENANCE",
            Note: "Bảo trì máy siêu âm định kỳ",
            NotifyDoctor: true,
            NotifyPatients: false);

        var response = await _service.CloseExaminationSlotAsync(11, "SLT-004", req, "127.0.0.1");

        Assert.True(response.Success);
        Assert.Equal(4, response.Id);
        Assert.Equal("SLT-004", response.Code);
        Assert.Equal("CLOSED", response.Status);
        Assert.False(response.HasDependentBookings);
        Assert.Equal(0, response.AffectedBookingsCount);
        Assert.Equal("EQUIPMENT_MAINTENANCE", response.CloseReasonCode);
        Assert.Contains("Bảo trì máy siêu âm định kỳ", response.CloseReason);
        Assert.Equal("KEEP_EXISTING", response.AffectedBookingsAction);
        Assert.NotNull(response.ClosedAt);

        var auditLog = await _db.AuditLogs.FirstOrDefaultAsync(a =>
            a.EntityType == ExaminationSlotAdminService.SlotAuditEntityType &&
            a.EntityId == "4" &&
            a.Action == ExaminationSlotAdminService.SlotClosedAuditAction);
        Assert.NotNull(auditLog);
        Assert.Equal(11, auditLog.ActorUserId);
        Assert.Equal(1, auditLog.HospitalId);

        // Verify metadata reconstruction on GetExaminationSlotListAsync
        var listResponse = await _service.GetExaminationSlotListAsync(new GetExaminationSlotListRequest(Status: "CLOSED"));
        var closedItem = Assert.Single(listResponse.Data, s => s.Id == 4);
        Assert.Equal("CLOSED", closedItem.Status);
        Assert.Equal("EQUIPMENT_MAINTENANCE", closedItem.CloseReasonCode);
        Assert.Equal("Bảo trì máy siêu âm định kỳ", closedItem.CloseNote);
        Assert.Equal("KEEP_EXISTING", closedItem.AffectedBookingsAction);
        Assert.NotNull(closedItem.ClosedAt);
    }

    [Fact]
    public async Task CloseExaminationSlot_WithDependentBookings_Unconfirmed_ThrowsConflictExceptionWithWarning()
    {
        // Slot 1 has 2 active bookings (appt 1 BOOKED, appt 2 CHECKED_IN) + 1 CANCELLED
        var unconfirmedReq = new CloseExaminationSlotRequest(
            ReasonCode: "DOCTOR_UNAVAILABLE",
            ConfirmUnderstood: false,
            AffectedBookingsAction: "KEEP_EXISTING");

        var ex1 = await Assert.ThrowsAsync<ConflictException>(() =>
            _service.CloseExaminationSlotAsync(11, "1", unconfirmedReq));
        Assert.Contains("2 lượt bệnh nhân đã đặt hẹn", ex1.Message);

        // Also blocked if neither ConfirmUnderstood=true nor AffectedBookingsAction is supplied
        var bareReq = new CloseExaminationSlotRequest(ReasonCode: "DOCTOR_UNAVAILABLE");
        var ex2 = await Assert.ThrowsAsync<ConflictException>(() =>
            _service.CloseExaminationSlotAsync(11, "1", bareReq));
        Assert.Contains("2 lượt bệnh nhân đã đặt hẹn", ex2.Message);

        // Slot 1 remains OPEN in DB
        var slot1 = await _db.ExaminationSlots.FindAsync(1L);
        Assert.Equal("OPEN", slot1!.Status);
    }

    [Fact]
    public async Task CloseExaminationSlot_WithDependentBookings_KeepExisting_ClosesSlotAndPreservesBookings()
    {
        var req = new CloseExaminationSlotRequest(
            ReasonCode: "DOCTOR_UNAVAILABLE",
            Note: "Bác sĩ hội chẩn viện",
            AffectedBookingsAction: "KEEP_EXISTING",
            ConfirmUnderstood: true);

        var response = await _service.CloseExaminationSlotAsync(11, "SLT-001", req);

        Assert.True(response.Success);
        Assert.Equal("CLOSED", response.Status);
        Assert.True(response.HasDependentBookings);
        Assert.Equal(2, response.AffectedBookingsCount);
        Assert.Equal("KEEP_EXISTING", response.AffectedBookingsAction);
        Assert.NotNull(response.WarningMessage);
        Assert.Contains("KEEP_EXISTING", response.WarningMessage);
        Assert.NotNull(response.AffectedBookings);
        Assert.Equal(2, response.AffectedBookings.Count);

        var appt1 = await _db.Appointments.FindAsync(1L);
        Assert.Equal("BOOKED", appt1!.Status);
        Assert.Null(appt1.ReceptionNote);
    }

    [Fact]
    public async Task CloseExaminationSlot_WithDependentBookings_RescheduleRequired_AnnotatesModifiableBookingsReceptionNote()
    {
        var req = new CloseExaminationSlotRequest(
            ReasonCode: "OTHER",
            Note: "Phòng khám khử khuẩn đột xuất",
            AffectedBookingsAction: "RESCHEDULE_REQUIRED");

        var response = await _service.CloseExaminationSlotAsync(11, "1", req);

        Assert.True(response.Success);
        Assert.Equal("CLOSED", response.Status);
        Assert.True(response.HasDependentBookings);
        Assert.Equal(2, response.AffectedBookingsCount);
        Assert.Equal("RESCHEDULE_REQUIRED", response.AffectedBookingsAction);
        Assert.NotNull(response.WarningMessage);
        Assert.Contains("RESCHEDULE_REQUIRED", response.WarningMessage);

        // Appt 1 (BOOKED) gets reception note marker
        var appt1 = await _db.Appointments.FindAsync(1L);
        Assert.Equal("BOOKED", appt1!.Status);
        Assert.NotNull(appt1.ReceptionNote);
        Assert.Contains("[RESCHEDULE_REQUIRED]", appt1.ReceptionNote);
        Assert.Contains("Phòng khám khử khuẩn đột xuất", appt1.ReceptionNote);

        // Appt 2 (CHECKED_IN) is not overwritten with reschedule marker; Appt 3 (CANCELLED) untouched
        var appt2 = await _db.Appointments.FindAsync(2L);
        Assert.Equal("CHECKED_IN", appt2!.Status);
        Assert.Null(appt2.ReceptionNote);
    }

    [Fact]
    public async Task CloseExaminationSlot_Layer2Authorization_EnforcesRoleStatusAndHospitalOwnership()
    {
        var req = new CloseExaminationSlotRequest(ReasonCode: "DOCTOR_UNAVAILABLE");

        // Invalid userId <= 0 -> 401
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _service.CloseExaminationSlotAsync(0, "4", req));

        // Non-existent user -> 404
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.CloseExaminationSlotAsync(9999, "4", req));

        // Disabled admin account -> 403
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.CloseExaminationSlotAsync(15, "4", req));

        // Locked admin account -> 403
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.CloseExaminationSlotAsync(16, "4", req));

        // Non-admin role (PATIENT / DOCTOR) -> 403
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.CloseExaminationSlotAsync(1, "4", req));
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.CloseExaminationSlotAsync(3, "4", req));

        // Hospital Admin of Hospital 2 attempting to close Slot 4 of Hospital 1 -> 403
        var crossHospEx = await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.CloseExaminationSlotAsync(12, "4", req));
        Assert.Contains("cơ sở y tế khác", crossHospEx.Message);

        // System Admin (user 13) can close Slot 4 of Hospital 1 -> 200 OK
        var sysAdminRes = await _service.CloseExaminationSlotAsync(13, "4", req);
        Assert.Equal("CLOSED", sysAdminRes.Status);
    }

    [Fact]
    public async Task BatchCloseExaminationSlots_ValidatesAndClosesMultipleSlots()
    {
        // Empty list -> 400
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.BatchCloseExaminationSlotsAsync(11, new BatchCloseExaminationSlotsRequest()));

        // Slots 1 and 4: Slot 1 has 2 active bookings; without confirmation -> 409
        var unconfirmedBatch = new BatchCloseExaminationSlotsRequest(
            Ids: ["SLT-001", "SLT-004"],
            ReasonCode: "SCHEDULE_ADJUSTMENT",
            ConfirmUnderstood: false);
        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.BatchCloseExaminationSlotsAsync(11, unconfirmedBatch));

        // Confirmed batch close with RESCHEDULE_REQUIRED -> closes both slots
        var confirmedBatch = new BatchCloseExaminationSlotsRequest(
            Ids: ["SLT-001", "SLT-004"],
            ReasonCode: "SCHEDULE_ADJUSTMENT",
            Note: "Điều chỉnh lịch tuần tới",
            AffectedBookingsAction: "RESCHEDULE_REQUIRED",
            ConfirmUnderstood: true);

        var batchResult = await _service.BatchCloseExaminationSlotsAsync(11, confirmedBatch);
        Assert.True(batchResult.Success);
        Assert.Equal(2, batchResult.Count);
        Assert.Equal(2, batchResult.TotalAffectedBookings);
        Assert.All(batchResult.Slots, s => Assert.Equal("CLOSED", s.Status));
    }

    [Fact]
    public async Task ReopenExaminationSlot_ValidatesConflictsAndRestoresOpenOrFullStatus()
    {
        // Reopening an already OPEN slot (Slot 4) -> 409
        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.ReopenExaminationSlotAsync(11, "SLT-004"));

        // Reopening Slot 3 (currently CLOSED, 0 bookings, capacity 5) -> becomes OPEN
        var reopened3 = await _service.ReopenExaminationSlotAsync(
            11,
            "SLT-003",
            new ReopenExaminationSlotRequest(Note: "Bác sĩ đã quay lại làm việc"));
        Assert.True(reopened3.Success);
        Assert.Equal("OPEN", reopened3.Status);
        Assert.Null(reopened3.ClosedAt);
        Assert.Null(reopened3.CloseReason);


        var reopenAudit = await _db.AuditLogs.FirstOrDefaultAsync(a =>
            a.EntityType == ExaminationSlotAdminService.SlotAuditEntityType &&
            a.EntityId == "3" &&
            a.Action == ExaminationSlotAdminService.SlotReopenedAuditAction);
        Assert.NotNull(reopenAudit);
        Assert.Contains("Bác sĩ đã quay lại làm việc", reopenAudit.Reason);
        // Close Slot 2 (2 active bookings, capacity 2 -> FULL when active), then reopen -> becomes FULL
        await _service.CloseExaminationSlotAsync(
            11,
            "SLT-002",
            new CloseExaminationSlotRequest(
                ReasonCode: "EMERGENCY",
                AffectedBookingsAction: "KEEP_EXISTING",
                ConfirmUnderstood: true));

        var reopened2 = await _service.ReopenExaminationSlotAsync(11, "2");
        Assert.Equal("FULL", reopened2.Status);
    }

    [Fact]
    public async Task ReopenExaminationSlot_WhenOverlappingOpenSlotExists_ThrowsConflictException()
    {
        // Slot 3 is CLOSED at 2026-10-28 09:00 - 09:30 for Doctor 1, Room 1.
        // Create a new OPEN slot at the same time for Doctor 1, Room 1 (allowed because Slot 3 is CLOSED).
        var replacement = await _service.CreateExaminationSlotAsync(new CreateExaminationSlotRequest(
            ServiceId: 1,
            DoctorId: 1,
            RoomId: 1,
            Date: "2026-10-28",
            StartTime: "09:00",
            EndTime: "09:30",
            Capacity: 6));
        Assert.Equal("OPEN", replacement.Status);

        // Attempting to reopen Slot 3 now conflicts with the replacement slot -> 409
        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            _service.ReopenExaminationSlotAsync(11, "3"));
        Assert.Contains("Xung đột lịch bác sĩ", ex.Message);
    }

    [Fact]
    public async Task CloseAndReopenExaminationSlot_NoteExceedingMaxLength_AndInactiveDependenciesAndRoomConflict_AreEnforced()
    {
        var tooLongNote = new string('x', 501);

        // Close with Note > 500 chars -> 400
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.CloseExaminationSlotAsync(
                11,
                "4",
                new CloseExaminationSlotRequest(ReasonCode: "DOCTOR_UNAVAILABLE", Note: tooLongNote)));

        // Reopen with Note > 500 chars -> 400
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.ReopenExaminationSlotAsync(
                11,
                "3",
                new ReopenExaminationSlotRequest(Note: tooLongNote)));

        // Reopen when assigned Room is INACTIVE -> 409
        var room1 = await _db.HospitalRooms.FindAsync(1L);
        room1!.Status = "INACTIVE";
        await _db.SaveChangesAsync();
        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.ReopenExaminationSlotAsync(11, "3"));
        room1.Status = "ACTIVE";
        await _db.SaveChangesAsync();

        // Reopen when assigned Doctor is inactive -> 409
        var doc1 = await _db.Doctors.FindAsync(1L);
        doc1!.IsActive = false;
        await _db.SaveChangesAsync();
        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.ReopenExaminationSlotAsync(11, "3"));
        doc1.IsActive = true;
        await _db.SaveChangesAsync();

        // Reopen when assigned Service is inactive -> 409
        var srv1 = await _db.Services.FindAsync(1L);
        srv1!.IsActive = false;
        await _db.SaveChangesAsync();
        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.ReopenExaminationSlotAsync(11, "3"));
        srv1.IsActive = true;
        await _db.SaveChangesAsync();

        // Reopen when Room 1 is occupied by another doctor (Doctor 2) at 2026-10-28 09:00 - 09:30 -> 409 Room conflict
        await _service.CreateExaminationSlotAsync(new CreateExaminationSlotRequest(
            ServiceId: 2,
            DoctorId: 2,
            RoomId: 1,
            Date: "2026-10-28",
            StartTime: "09:00",
            EndTime: "09:30",
            Capacity: 6));

        var roomConflictEx = await Assert.ThrowsAsync<ConflictException>(() =>
            _service.ReopenExaminationSlotAsync(11, "3"));
        Assert.Contains("Xung đột buồng khám", roomConflictEx.Message);
    }
}
