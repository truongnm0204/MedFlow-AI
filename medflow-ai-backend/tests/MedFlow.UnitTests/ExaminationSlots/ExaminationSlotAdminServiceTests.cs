using System.Globalization;
using MedFlow.Application.DTOs.ExaminationSlots;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.ExaminationSlots;
using MedFlow.Domain.Entities;
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

        public DbSet<User> Users => throw NotUsed();
        public DbSet<Role> Roles => throw NotUsed();
        public DbSet<Permission> Permissions => throw NotUsed();
        public DbSet<UserRole> UserRoles => throw NotUsed();
        public DbSet<RolePermission> RolePermissions => throw NotUsed();
        public DbSet<Patient> Patients => throw NotUsed();
        public DbSet<Staff> Staff => Set<Staff>();
        public DbSet<Doctor> Doctors => Set<Doctor>();
        public DbSet<DoctorServiceAuthorization> DoctorServiceAuthorizations => throw NotUsed();
        public DbSet<ExaminationSlot> ExaminationSlots => Set<ExaminationSlot>();
        public DbSet<AuthSession> AuthSessions => throw NotUsed();
        public DbSet<AuthVerificationChallenge> AuthVerificationChallenges => throw NotUsed();
        public DbSet<AuditLog> AuditLogs => throw NotUsed();
        public DbSet<Hospital> Hospitals => throw NotUsed();
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
            modelBuilder.Ignore<User>();
            modelBuilder.Ignore<Role>();
            modelBuilder.Ignore<Permission>();
            modelBuilder.Ignore<UserRole>();
            modelBuilder.Ignore<RolePermission>();
            modelBuilder.Ignore<Patient>();
            modelBuilder.Ignore<DoctorServiceAuthorization>();
            modelBuilder.Ignore<AuthSession>();
            modelBuilder.Ignore<AuthVerificationChallenge>();
            modelBuilder.Ignore<AuditLog>();
            modelBuilder.Ignore<Hospital>();
            modelBuilder.Ignore<ServicePrice>();
            modelBuilder.Ignore<HospitalPolicy>();
            modelBuilder.Ignore<PatientFamilyLink>();
            modelBuilder.Ignore<Visit>();
            modelBuilder.Ignore<QueueEntry>();
            modelBuilder.Ignore<Payment>();

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
        var specEndocrine = new Specialty { Id = 1, Name = "Nội tiết" };
        var specCardio = new Specialty { Id = 2, Name = "Tim mạch" };
        _db.Specialties.AddRange(specEndocrine, specCardio);

        var deptInternal = new Department { Id = 1, Name = "Khoa Nội", DepartmentCode = "K-NOI", IsActive = true };
        var deptCardio = new Department { Id = 2, Name = "Khoa Tim mạch", DepartmentCode = "K-TIM", IsActive = true };
        _db.Departments.AddRange(deptInternal, deptCardio);

        var staff1 = new Staff { Id = 1, FullName = "BS. Nguyễn Thu Lan", StaffCode = "ST-001", Status = "ACTIVE" };
        var staff2 = new Staff { Id = 2, FullName = "BS. Trần Hoài Nam", StaffCode = "ST-002", Status = "ACTIVE" };
        _db.Staff.AddRange(staff1, staff2);

        var doc1 = new Doctor { Id = 1, StaffId = 1, SpecialtyId = 1, DoctorCode = "DOC-001", IsActive = true };
        var doc2 = new Doctor { Id = 2, StaffId = 2, SpecialtyId = 2, DoctorCode = "DOC-002", IsActive = true };
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

        var room102 = new HospitalRoom { Id = 1, RoomCode = "PK-102", Name = "Phòng khám 102", DepartmentId = 1, Status = "ACTIVE", Capacity = 10 };
        var room108 = new HospitalRoom { Id = 2, RoomCode = "PK-108", Name = "Phòng khám 108", DepartmentId = 2, Status = "ACTIVE", Capacity = 10 };
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
        var appt1 = new Appointment { Id = 1, SlotId = 1, Status = "BOOKED" };
        var appt2 = new Appointment { Id = 2, SlotId = 1, Status = "CHECKED_IN" };
        var appt3 = new Appointment { Id = 3, SlotId = 1, Status = "CANCELLED" };

        // Slot 2: 2 BOOKED -> Active: 2 (Capacity = 2 -> FULL)
        var appt4 = new Appointment { Id = 4, SlotId = 2, Status = "BOOKED" };
        var appt5 = new Appointment { Id = 5, SlotId = 2, Status = "BOOKED" };

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
}
