using MedFlow.Application.DTOs.Doctors;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Doctors;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.UnitTests.Doctors;

public sealed class DoctorCatalogServiceTests : IDisposable
{
    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private static readonly DateTimeOffset DefaultUtcNow = new(2026, 10, 27, 1, 0, 0, TimeSpan.Zero);

    private sealed class DoctorTestDbContext : DbContext, IApplicationDbContext
    {
        public DoctorTestDbContext(DbContextOptions<DoctorTestDbContext> options)
            : base(options)
        {
        }

        public DbSet<User> Users => throw NotUsed();
        public DbSet<Role> Roles => throw NotUsed();
        public DbSet<Permission> Permissions => throw NotUsed();
        public DbSet<UserRole> UserRoles => throw NotUsed();
        public DbSet<RolePermission> RolePermissions => throw NotUsed();
        public DbSet<Patient> Patients => throw NotUsed();
        public DbSet<AuthSession> AuthSessions => throw NotUsed();
        public DbSet<AuthVerificationChallenge> AuthVerificationChallenges => throw NotUsed();
        public DbSet<AuditLog> AuditLogs => throw NotUsed();
        public DbSet<HospitalPolicy> HospitalPolicies => throw NotUsed();
        public DbSet<Appointment> Appointments => throw NotUsed();
        public DbSet<PatientFamilyLink> PatientFamilyLinks => throw NotUsed();
        public DbSet<Visit> Visits => throw NotUsed();
        public DbSet<QueueEntry> QueueEntries => throw NotUsed();
        public DbSet<Payment> Payments => throw NotUsed();

        public DbSet<Staff> Staff => Set<Staff>();
        public DbSet<Doctor> Doctors => Set<Doctor>();
        public DbSet<DoctorServiceAuthorization> DoctorServiceAuthorizations => Set<DoctorServiceAuthorization>();
        public DbSet<ExaminationSlot> ExaminationSlots => Set<ExaminationSlot>();
        public DbSet<Department> Departments => Set<Department>();
        public DbSet<HospitalRoom> HospitalRooms => Set<HospitalRoom>();
        public DbSet<Specialty> Specialties => Set<Specialty>();
        public DbSet<Service> Services => Set<Service>();
        public DbSet<ServicePrice> ServicePrices => Set<ServicePrice>();

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
            modelBuilder.Ignore<AuthSession>();
            modelBuilder.Ignore<AuthVerificationChallenge>();
            modelBuilder.Ignore<AuditLog>();
            modelBuilder.Ignore<HospitalPolicy>();
            modelBuilder.Ignore<Appointment>();
            modelBuilder.Ignore<PatientFamilyLink>();
            modelBuilder.Ignore<Visit>();
            modelBuilder.Ignore<QueueEntry>();
            modelBuilder.Ignore<Payment>();

            modelBuilder.Entity<Department>().HasKey(d => d.Id);
            modelBuilder.Entity<HospitalRoom>(b =>
            {
                b.HasKey(r => r.Id);
                b.HasOne(r => r.Department).WithMany(d => d.HospitalRooms).HasForeignKey(r => r.DepartmentId);
            });
            modelBuilder.Entity<Staff>(b =>
            {
                b.HasKey(s => s.Id);
                b.HasOne(s => s.Department).WithMany().HasForeignKey(s => s.DepartmentId);
            });
            modelBuilder.Entity<Specialty>().HasKey(s => s.Id);
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
                b.HasMany(s => s.ServicePrices).WithOne(p => p.Service).HasForeignKey(p => p.ServiceId);
            });
            modelBuilder.Entity<ServicePrice>().HasKey(p => p.Id);
            modelBuilder.Entity<DoctorServiceAuthorization>(b =>
            {
                b.HasKey(a => new { a.DoctorId, a.ServiceId });
                b.HasOne(a => a.Doctor).WithMany(d => d.ServiceAuthorizations).HasForeignKey(a => a.DoctorId);
                b.HasOne(a => a.Service).WithMany().HasForeignKey(a => a.ServiceId);
            });
            modelBuilder.Entity<ExaminationSlot>(b =>
            {
                b.HasKey(s => s.Id);
                b.HasOne(s => s.Doctor).WithMany(d => d.ExaminationSlots).HasForeignKey(s => s.DoctorId);
                b.HasOne(s => s.Service).WithMany().HasForeignKey(s => s.ServiceId);
                b.HasOne(s => s.Room).WithMany().HasForeignKey(s => s.RoomId);
            });
        }

        private static NotSupportedException NotUsed()
            => new("DbSet này không được dùng trong DoctorCatalogServiceTests.");
    }

    private readonly DoctorTestDbContext _db;
    private readonly DoctorCatalogService _sut;

    public DoctorCatalogServiceTests()
    {
        var options = new DbContextOptionsBuilder<DoctorTestDbContext>()
            .UseInMemoryDatabase($"DoctorCatalogTests_{Guid.NewGuid()}")
            .Options;

        _db = new DoctorTestDbContext(options);
        SeedDoctorData(_db);
        _sut = new DoctorCatalogService(_db, new FixedTimeProvider(DefaultUtcNow));
    }

    public void Dispose() => _db.Dispose();

    private static void SeedDoctorData(DoctorTestDbContext db)
    {
        db.Departments.AddRange(
            new Department { Id = 2, HospitalId = 1, DepartmentCode = "GI", Name = "Khoa Tiêu hóa", IsActive = true },
            new Department { Id = 3, HospitalId = 1, DepartmentCode = "CARD", Name = "Khoa Tim mạch", IsActive = true },
            new Department { Id = 4, HospitalId = 1, DepartmentCode = "DIA", Name = "Khoa Cận lâm sàng", IsActive = true });

        db.HospitalRooms.AddRange(
            new HospitalRoom
            {
                Id = 1,
                HospitalId = 1,
                DepartmentId = 2,
                RoomCode = "RM-001",
                Name = "Khám Tiêu hóa 101",
                RoomType = "EXAMINATION",
                Location = "Tầng 1, Khu Demo",
                Capacity = 2,
                Status = "ACTIVE",
            },
            new HospitalRoom
            {
                Id = 3,
                HospitalId = 1,
                DepartmentId = 3,
                RoomCode = "RM-003",
                Name = "Khám Tim mạch 201",
                RoomType = "EXAMINATION",
                Location = "Tầng 1, Khu Demo",
                Capacity = 2,
                Status = "ACTIVE",
            });

        db.Specialties.AddRange(
            new Specialty { Id = 1, SpecialtyCode = "GI", Name = "Tiêu hóa", IsActive = true },
            new Specialty { Id = 2, SpecialtyCode = "CARD", Name = "Tim mạch", IsActive = true });

        db.Staff.AddRange(
            new Staff
            {
                Id = 1,
                UserId = 3,
                HospitalId = 1,
                DepartmentId = 2,
                StaffCode = "STF-001",
                FullName = "BS Nguyễn Minh Đức",
                Status = "ACTIVE",
            },
            new Staff
            {
                Id = 2,
                UserId = 4,
                HospitalId = 1,
                DepartmentId = 3,
                StaffCode = "STF-002",
                FullName = "BS Trần Thu Hà",
                Status = "ACTIVE",
            },
            new Staff
            {
                Id = 90,
                UserId = 90,
                HospitalId = 1,
                DepartmentId = 2,
                StaffCode = "STF-090",
                FullName = "BS Tạm Ngưng",
                Status = "INACTIVE",
            },
            new Staff
            {
                Id = 91,
                UserId = 91,
                HospitalId = 1,
                DepartmentId = 2,
                StaffCode = "STF-091",
                FullName = "BS Ngưng Khám",
                Status = "ACTIVE",
            },
            new Staff
            {
                Id = 92,
                UserId = 92,
                HospitalId = 1,
                DepartmentId = 2,
                StaffCode = "STF-092",
                FullName = "BS Chỉ Cận Lâm Sàng",
                Status = "ACTIVE",
            });

        db.Doctors.AddRange(
            new Doctor
            {
                Id = 1,
                StaffId = 1,
                HospitalId = 1,
                DoctorCode = "DOC-001",
                SpecialtyId = 1,
                ProfessionalTitle = "BS.CKI",
                IsActive = true,
                ProfileImageUrl = null,
                YearsOfExperience = 9,
                LicenseNumber = "DEMO-CCHN-001",
                ProfessionalBio = "Bác sĩ khám ngoại trú chuyên khoa Tiêu hóa (dữ liệu demo).",
                QualificationsSummary = "Bác sĩ chuyên khoa I",
            },
            new Doctor
            {
                Id = 2,
                StaffId = 2,
                HospitalId = 1,
                DoctorCode = "DOC-002",
                SpecialtyId = 2,
                ProfessionalTitle = "ThS.BS",
                IsActive = true,
                ProfileImageUrl = null,
                YearsOfExperience = 11,
                LicenseNumber = "DEMO-CCHN-002",
                ProfessionalBio = "Bác sĩ khám ngoại trú chuyên khoa Tim mạch (dữ liệu demo).",
                QualificationsSummary = "Thạc sĩ y khoa",
            },
            new Doctor
            {
                Id = 90,
                StaffId = 90,
                HospitalId = 1,
                DoctorCode = "DOC-INACTIVE-STAFF",
                SpecialtyId = 1,
                ProfessionalTitle = "BS.CKI",
                IsActive = true,
                YearsOfExperience = 5,
                LicenseNumber = "DEMO-CCHN-090",
            },
            new Doctor
            {
                Id = 91,
                StaffId = 91,
                HospitalId = 1,
                DoctorCode = "DOC-INACTIVE",
                SpecialtyId = 1,
                ProfessionalTitle = "BS.CKI",
                IsActive = false,
                YearsOfExperience = 5,
                LicenseNumber = "DEMO-CCHN-091",
            },
            new Doctor
            {
                Id = 92,
                StaffId = 92,
                HospitalId = 1,
                DoctorCode = "DOC-NO-ONLINE",
                SpecialtyId = 1,
                ProfessionalTitle = "BS.CKI",
                IsActive = true,
                YearsOfExperience = 3,
            });

        db.Services.AddRange(
            new Service
            {
                Id = 1,
                HospitalId = 1,
                DepartmentId = 2,
                SpecialtyId = 1,
                ServiceCode = "EX-GI-01",
                Name = "Khám Tiêu hóa lần đầu",
                ServiceType = "EXAMINATION",
                AllowsOnlineBooking = true,
                IsActive = true,
            },
            new Service
            {
                Id = 2,
                HospitalId = 1,
                DepartmentId = 3,
                SpecialtyId = 2,
                ServiceCode = "EX-CARD-01",
                Name = "Khám Tim mạch lần đầu",
                ServiceType = "EXAMINATION",
                AllowsOnlineBooking = true,
                IsActive = true,
            },
            new Service
            {
                Id = 3,
                HospitalId = 1,
                DepartmentId = 2,
                SpecialtyId = 1,
                ServiceCode = "EX-GI-02",
                Name = "Khám Tiêu hóa tái khám",
                ServiceType = "EXAMINATION",
                AllowsOnlineBooking = true,
                IsActive = true,
            },
            new Service
            {
                Id = 5,
                HospitalId = 1,
                DepartmentId = 4,
                SpecialtyId = 1,
                ServiceCode = "DIA-CBC",
                Name = "Công thức máu",
                ServiceType = "DIAGNOSTIC",
                AllowsOnlineBooking = false,
                IsActive = true,
            });

        db.ServicePrices.AddRange(
            new ServicePrice { Id = 1, ServiceId = 1, PriceAmount = 200_000m, EffectiveFrom = new DateTime(2026, 1, 1) },
            new ServicePrice { Id = 2, ServiceId = 2, PriceAmount = 220_000m, EffectiveFrom = new DateTime(2026, 1, 1) },
            new ServicePrice { Id = 3, ServiceId = 3, PriceAmount = 170_000m, EffectiveFrom = new DateTime(2026, 1, 1) },
            new ServicePrice { Id = 5, ServiceId = 5, PriceAmount = 90_000m, EffectiveFrom = new DateTime(2026, 1, 1) });

        db.DoctorServiceAuthorizations.AddRange(
            new DoctorServiceAuthorization { DoctorId = 1, ServiceId = 5, IsActive = true },
            new DoctorServiceAuthorization { DoctorId = 1, ServiceId = 3, IsActive = true },
            new DoctorServiceAuthorization { DoctorId = 1, ServiceId = 1, IsActive = true },
            new DoctorServiceAuthorization { DoctorId = 2, ServiceId = 2, IsActive = true },
            new DoctorServiceAuthorization { DoctorId = 92, ServiceId = 5, IsActive = true });

        db.ExaminationSlots.AddRange(
            new ExaminationSlot
            {
                Id = 9,
                ServiceId = 1,
                DoctorId = 1,
                RoomId = 1,
                StartsAt = new DateTime(2026, 10, 27, 9, 0, 0),
                EndsAt = new DateTime(2026, 10, 27, 9, 30, 0),
                Capacity = 3,
                Status = "OPEN",
            },
            new ExaminationSlot
            {
                Id = 10,
                ServiceId = 2,
                DoctorId = 2,
                RoomId = 3,
                StartsAt = new DateTime(2026, 10, 28, 9, 0, 0),
                EndsAt = new DateTime(2026, 10, 28, 9, 30, 0),
                Capacity = 3,
                Status = "OPEN",
            });

        db.SaveChanges();
    }

    [Theory]
    [InlineData("1", "DOC-001")]
    [InlineData("2", "DOC-002")]
    [InlineData("DOC-001", "DOC-001")]
    [InlineData("doc-002", "DOC-002")]
    [InlineData("doc-01", "DOC-001")]
    [InlineData("doc-02", "DOC-002")]
    [InlineData("STF-001", "DOC-001")]
    [InlineData("stf-002", "DOC-002")]
    public async Task GetByIdOrCodeAsync_ResolvesByNumericIdDoctorCodeStaffCodeOrLegacySlug(
        string idOrCode,
        string expectedDoctorCode)
    {
        var dto = await _sut.GetByIdOrCodeAsync(idOrCode);

        Assert.Equal(expectedDoctorCode, dto.DoctorCode);
    }

    [Fact]
    public async Task GetByIdOrCodeAsync_ReturnsMappedProfileRoomPriceEducationAndSchedule()
    {
        var giDoctor = await _sut.GetByIdOrCodeAsync("DOC-001");

        Assert.Equal("1", giDoctor.Id);
        Assert.Equal("DOC-001", giDoctor.DoctorCode);
        Assert.Equal("STF-001", giDoctor.StaffCode);
        Assert.Equal("BS.CKI Nguyễn Minh Đức", giDoctor.Name);
        Assert.Equal("BSCKI", giDoctor.Title);
        Assert.Equal("Bác sĩ chuyên khoa I - Khoa Tiêu hóa", giDoctor.RoleTitle);
        Assert.Equal("Khoa Tiêu hóa", giDoctor.Specialty);
        Assert.Equal("GI", giDoctor.SpecialtyCode);
        Assert.Equal("Phòng 101 - Tầng 1, Khu Demo", giDoctor.Room);
        Assert.Equal("Đang tiếp nhận lịch khám ngoại trú", giDoctor.StatusText);
        Assert.Equal(200_000m, giDoctor.Price);
        Assert.Equal(9, giDoctor.YearsOfExperience);
        Assert.Equal("DEMO-CCHN-001", giDoctor.LicenseNumber);
        Assert.Equal(3, giDoctor.Education.Count);
        Assert.Contains(giDoctor.Education, e => e.Badge == "CCHN: DEMO-CCHN-001");
        Assert.Contains("9 năm kinh nghiệm", giDoctor.Biography);
        Assert.Contains("#NoiSoiTieuHoa", giDoctor.Hashtags);
        Assert.Equal(3, giDoctor.WeeklySchedule.Count);

        var cardDoctor = await _sut.GetByIdOrCodeAsync("DOC-002");

        Assert.Equal("2", cardDoctor.Id);
        Assert.Equal("DOC-002", cardDoctor.DoctorCode);
        Assert.Equal("ThS.BS Trần Thu Hà", cardDoctor.Name);
        Assert.Equal("ThS", cardDoctor.Title);
        Assert.Equal("Thạc sĩ y khoa - Khoa Tim mạch", cardDoctor.RoleTitle);
        Assert.Equal("Phòng 201 - Tầng 1, Khu Demo", cardDoctor.Room);
        Assert.Equal(220_000m, cardDoctor.Price);
        Assert.Contains("#TimMach", cardDoctor.Hashtags);
    }

    [Fact]
    public async Task GetByIdOrCodeAsync_ThrowsValidationException_OnInvalidIdentifier()
    {
        await Assert.ThrowsAsync<ValidationException>(() => _sut.GetByIdOrCodeAsync(""));
        await Assert.ThrowsAsync<ValidationException>(() => _sut.GetByIdOrCodeAsync("   "));
        await Assert.ThrowsAsync<ValidationException>(() => _sut.GetByIdOrCodeAsync(new string('A', 65)));
        await Assert.ThrowsAsync<ValidationException>(() => _sut.GetByIdOrCodeAsync("DOC-001@invalid"));
    }

    [Fact]
    public async Task GetByIdOrCodeAsync_ThrowsNotFoundException_WhenMissingOrInactive()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetByIdOrCodeAsync("DOC-999"));
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetByIdOrCodeAsync("999"));
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetByIdOrCodeAsync("DOC-INACTIVE"));
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetByIdOrCodeAsync("DOC-INACTIVE-STAFF"));
    }

    [Fact]
    public async Task SearchAsync_DefaultRequest_ReturnsOnlyBookableDoctorsOrderedById()
    {
        var res = await _sut.SearchAsync(new SearchDoctorsRequest());

        Assert.Equal(["1", "2"], res.Items.Select(x => x.Id));
        Assert.Equal(2, res.Total);
        Assert.Equal(1, res.Page);
        Assert.Equal(6, res.PageSize);
        Assert.Equal(1, res.TotalPages);
    }

    [Fact]
    public async Task SearchAsync_MapsCardFieldsFromProfileAndUpcomingSlots()
    {
        var res = await _sut.SearchAsync(new SearchDoctorsRequest());

        var doc1 = Assert.Single(res.Items, x => x.Id == "1");
        Assert.Equal("BS.CKI Nguyễn Minh Đức", doc1.Name);
        Assert.Equal("BSCKI", doc1.Title);
        Assert.Equal("Bác sĩ chuyên khoa I", doc1.RoleTag);
        Assert.Equal("Khoa Tiêu hóa", doc1.Specialty);
        Assert.Equal("tieu-hoa", doc1.SpecialtyId);
        Assert.Equal(9, doc1.YearsOfExperience);
        Assert.Equal("Phòng 101 - Tầng 1, Khu Demo", doc1.Room);
        Assert.Equal(200_000m, doc1.Price);
        Assert.Equal("Hôm nay: 09:00 - 09:30", doc1.NearestSchedule);
        Assert.Equal("AVAILABLE", doc1.ScheduleStatus);
        Assert.Equal("Đang nhận lịch", doc1.ScheduleStatusText);
        Assert.True(doc1.HasScheduleToday);
        Assert.True(doc1.HasScheduleThisWeek);

        var doc2 = Assert.Single(res.Items, x => x.Id == "2");
        Assert.Equal("ThS", doc2.Title);
        Assert.Equal("tim-mach", doc2.SpecialtyId);
        Assert.Equal(220_000m, doc2.Price);
        Assert.Equal("Ngày mai: 09:00 - 09:30", doc2.NearestSchedule);
        Assert.Equal("FEW_SLOTS", doc2.ScheduleStatus);
        Assert.Equal("Còn 3 lượt", doc2.ScheduleStatusText);
        Assert.False(doc2.HasScheduleToday);
        Assert.True(doc2.HasScheduleThisWeek);
    }

    [Theory]
    [InlineData("thu hà", "ALL", "ALL", false, new[] { "2" })]
    [InlineData("tiêu hóa", "ALL", "ALL", false, new[] { "1" })]
    [InlineData("doc-002", "ALL", "ALL", false, new[] { "2" })]
    [InlineData(null, "tim-mach", "ALL", false, new[] { "2" })]
    [InlineData(null, "GI", "ALL", false, new[] { "1" })]
    [InlineData(null, "nhi-khoa", "ALL", false, new string[0])]
    [InlineData(null, "ALL", "ThS", false, new[] { "2" })]
    [InlineData(null, "ALL", "bscki", false, new[] { "1" })]
    [InlineData(null, "ALL", "ALL", true, new[] { "1" })]
    public async Task SearchAsync_AppliesFilters(
        string? keyword,
        string specialty,
        string degree,
        bool hasScheduleToday,
        string[] expectedIds)
    {
        var res = await _sut.SearchAsync(new SearchDoctorsRequest
        {
            Keyword = keyword,
            Specialty = specialty,
            Degree = degree,
            HasScheduleToday = hasScheduleToday,
        });

        Assert.Equal(expectedIds, res.Items.Select(x => x.Id));
    }

    [Fact]
    public async Task SearchAsync_NoUpcomingSlots_ReturnsAdvanceStatusAndFailsScheduleFilters()
    {
        var sut = new DoctorCatalogService(
            _db,
            new FixedTimeProvider(new DateTimeOffset(2026, 11, 2, 1, 0, 0, TimeSpan.Zero)));

        var all = await sut.SearchAsync(new SearchDoctorsRequest());
        Assert.Equal(2, all.Items.Count);
        Assert.All(all.Items, item =>
        {
            Assert.Equal("ADVANCE_24H", item.ScheduleStatus);
            Assert.Equal("Chưa mở lịch khám", item.ScheduleStatusText);
            Assert.Equal("Chưa có lịch trống", item.NearestSchedule);
            Assert.False(item.HasScheduleToday);
            Assert.False(item.HasScheduleThisWeek);
        });

        var thisWeekOnly = await sut.SearchAsync(new SearchDoctorsRequest
        {
            HasScheduleThisWeek = true,
        });
        Assert.Empty(thisWeekOnly.Items);
        Assert.Equal(0, thisWeekOnly.Total);
        Assert.Equal(1, thisWeekOnly.TotalPages);
    }

    [Fact]
    public async Task SearchAsync_WeekendAndAlmostFullStatuses()
    {
        _db.ExaminationSlots.AddRange(
            new ExaminationSlot
            {
                Id = 20,
                ServiceId = 2,
                DoctorId = 2,
                RoomId = 3,
                StartsAt = new DateTime(2026, 10, 31, 8, 0, 0),
                EndsAt = new DateTime(2026, 10, 31, 11, 30, 0),
                Capacity = 10,
                Status = "OPEN",
            },
            new ExaminationSlot
            {
                Id = 21,
                ServiceId = 1,
                DoctorId = 1,
                RoomId = 1,
                StartsAt = new DateTime(2026, 11, 2, 8, 0, 0),
                EndsAt = new DateTime(2026, 11, 2, 8, 30, 0),
                Capacity = 2,
                Status = "OPEN",
            });
        _db.SaveChanges();

        var sut = new DoctorCatalogService(
            _db,
            new FixedTimeProvider(new DateTimeOffset(2026, 10, 30, 1, 0, 0, TimeSpan.Zero)));

        var res = await sut.SearchAsync(new SearchDoctorsRequest());

        var doc2 = Assert.Single(res.Items, x => x.Id == "2");
        Assert.Equal("WEEKEND", doc2.ScheduleStatus);
        Assert.Equal("Lịch khám cuối tuần", doc2.ScheduleStatusText);
        Assert.Equal("Ngày mai: 08:00 - 11:30", doc2.NearestSchedule);
        Assert.True(doc2.HasScheduleThisWeek);

        var doc1 = Assert.Single(res.Items, x => x.Id == "1");
        Assert.Equal("ALMOST_FULL", doc1.ScheduleStatus);
        Assert.Equal("Sắp kín chỗ", doc1.ScheduleStatusText);
        Assert.Equal("02/11/2026: 08:00 - 08:30", doc1.NearestSchedule);
        Assert.False(doc1.HasScheduleThisWeek);
        Assert.False(doc1.HasScheduleToday);
    }

    [Fact]
    public async Task SearchAsync_Paginates()
    {
        var page2 = await _sut.SearchAsync(new SearchDoctorsRequest { PageSize = 1, Page = 2 });
        Assert.Equal(["2"], page2.Items.Select(x => x.Id));
        Assert.Equal(2, page2.Total);
        Assert.Equal(2, page2.TotalPages);
        Assert.Equal(2, page2.Page);

        var page3 = await _sut.SearchAsync(new SearchDoctorsRequest { PageSize = 1, Page = 3 });
        Assert.Empty(page3.Items);
        Assert.Equal(2, page3.Total);
    }

    [Theory]
    [InlineData(101, "ALL", "ALL", 1, 6)]
    [InlineData(0, "ALL", "ALL", 0, 6)]
    [InlineData(0, "ALL", "ALL", 1, 0)]
    [InlineData(0, "ALL", "ALL", 1, 51)]
    [InlineData(0, "ALL", "XYZ", 1, 6)]
    [InlineData(0, "tim mach!", "ALL", 1, 6)]
    [InlineData(0, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "ALL", 1, 6)]
    public async Task SearchAsync_InvalidRequest_ThrowsValidationException(
        int keywordLength,
        string specialty,
        string degree,
        int page,
        int pageSize)
    {
        var request = new SearchDoctorsRequest
        {
            Keyword = keywordLength > 0 ? new string('a', keywordLength) : null,
            Specialty = specialty,
            Degree = degree,
            Page = page,
            PageSize = pageSize,
        };

        await Assert.ThrowsAsync<ValidationException>(() => _sut.SearchAsync(request));
    }
}
