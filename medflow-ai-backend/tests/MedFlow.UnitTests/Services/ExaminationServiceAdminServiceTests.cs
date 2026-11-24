using MedFlow.Application.DTOs.Services;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Services;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.UnitTests.Services;

public sealed class ExaminationServiceAdminServiceTests : IDisposable
{
    private sealed class AdminServiceTestDbContext : DbContext, IApplicationDbContext
    {
        public AdminServiceTestDbContext(DbContextOptions<AdminServiceTestDbContext> options)
            : base(options)
        {
        }

        public DbSet<User> Users => Set<User>();
        public DbSet<Role> Roles => Set<Role>();
        public DbSet<Permission> Permissions => Set<Permission>();
        public DbSet<UserRole> UserRoles => Set<UserRole>();
        public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
        public DbSet<Patient> Patients => throw NotUsed();
        public DbSet<Staff> Staff => Set<Staff>();
        public DbSet<Doctor> Doctors => throw NotUsed();
        public DbSet<DoctorServiceAuthorization> DoctorServiceAuthorizations => throw NotUsed();
        public DbSet<ExaminationSlot> ExaminationSlots => Set<ExaminationSlot>();
        public DbSet<AuthSession> AuthSessions => throw NotUsed();
        public DbSet<AuthVerificationChallenge> AuthVerificationChallenges => throw NotUsed();
        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

        public DbSet<Hospital> Hospitals => Set<Hospital>();
        public DbSet<Department> Departments => Set<Department>();
        public DbSet<HospitalRoom> HospitalRooms => throw NotUsed();
        public DbSet<Specialty> Specialties => Set<Specialty>();
        public DbSet<Service> Services => Set<Service>();
        public DbSet<ServicePrice> ServicePrices => Set<ServicePrice>();
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
            modelBuilder.Ignore<Patient>();
            modelBuilder.Ignore<Doctor>();
            modelBuilder.Ignore<DoctorServiceAuthorization>();
            modelBuilder.Ignore<AuthSession>();
            modelBuilder.Ignore<AuthVerificationChallenge>();
            modelBuilder.Ignore<HospitalRoom>();
            modelBuilder.Ignore<HospitalPolicy>();
            modelBuilder.Ignore<PatientFamilyLink>();
            modelBuilder.Ignore<Visit>();
            modelBuilder.Ignore<QueueEntry>();
            modelBuilder.Ignore<Payment>();

            modelBuilder.Entity<Hospital>().HasKey(h => h.Id);
            modelBuilder.Entity<User>().HasKey(u => u.Id);
            modelBuilder.Entity<Role>().HasKey(r => r.Id);
            modelBuilder.Entity<Permission>().HasKey(p => p.Id);
            modelBuilder.Entity<UserRole>().HasKey(ur => new { ur.UserId, ur.RoleId });
            modelBuilder.Entity<RolePermission>().HasKey(rp => new { rp.RoleId, rp.PermissionId });
            modelBuilder.Entity<Staff>().HasKey(st => st.Id);
            modelBuilder.Entity<Department>().HasKey(d => d.Id);
            modelBuilder.Entity<Specialty>().HasKey(s => s.Id);
            modelBuilder.Entity<Service>(b =>
            {
                b.HasKey(s => s.Id);
                b.HasOne(s => s.Department).WithMany(d => d.Services).HasForeignKey(s => s.DepartmentId);
                b.HasOne(s => s.Specialty).WithMany(sp => sp.Services).HasForeignKey(s => s.SpecialtyId);
                b.HasMany(s => s.ServicePrices).WithOne(p => p.Service).HasForeignKey(p => p.ServiceId);
            });
            modelBuilder.Entity<ServicePrice>().HasKey(p => p.Id);
            modelBuilder.Entity<ExaminationSlot>(b =>
            {
                b.HasKey(es => es.Id);
                b.Ignore(es => es.Doctor);
                b.Ignore(es => es.Room);
                b.HasOne(es => es.Service).WithMany().HasForeignKey(es => es.ServiceId);
            });
            modelBuilder.Entity<Appointment>(b =>
            {
                b.HasKey(a => a.Id);
                b.Ignore(a => a.Patient);
                b.HasOne(a => a.Slot).WithMany(s => s.Appointments).HasForeignKey(a => a.SlotId);
            });
            modelBuilder.Entity<AuditLog>().HasKey(al => al.Id);
        }

        private static NotSupportedException NotUsed()
            => new("DbSet này không được dùng trong AdminServiceTestDbContext.");
    }

    private readonly AdminServiceTestDbContext _db;
    private readonly ExaminationServiceAdminService _sut;

    public ExaminationServiceAdminServiceTests()
    {
        var options = new DbContextOptionsBuilder<AdminServiceTestDbContext>()
            .UseInMemoryDatabase($"admin-service-tests-{Guid.NewGuid():N}")
            .Options;

        _db = new AdminServiceTestDbContext(options);
        _sut = new ExaminationServiceAdminService(_db);

        SeedTestData();
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    private void SeedTestData()
    {
        _db.Hospitals.AddRange(
            new Hospital { Id = 1, HospitalCode = "HOS-01", Name = "Bệnh viện Đa khoa MedFlow Hà Nội", IsActive = true },
            new Hospital { Id = 2, HospitalCode = "HOS-02", Name = "Bệnh viện Đa khoa MedFlow Đà Nẵng", IsActive = true }
        );

        _db.Roles.AddRange(
            new Role { Id = 1, Code = "HOSPITAL_ADMIN", Name = "Quản trị bệnh viện" },
            new Role { Id = 2, Code = "SYSTEM_ADMIN", Name = "Quản trị hệ thống" },
            new Role { Id = 3, Code = "DOCTOR", Name = "Bác sĩ" }
        );

        _db.Permissions.Add(new Permission { Id = 1, Code = "HOSPITAL_CONFIG" });
        _db.RolePermissions.AddRange(
            new RolePermission { RoleId = 1, PermissionId = 1 },
            new RolePermission { RoleId = 2, PermissionId = 1 }
        );

        _db.Users.AddRange(
            new User { Id = 1, Email = "hadmin1@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 2, Email = "hadmin2@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 13, Email = "sysadmin@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 20, Email = "doctor@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 21, Email = "unassigned_hadmin@medflow.test", AccountStatus = AccountStatus.ACTIVE }
        );

        _db.UserRoles.AddRange(
            new UserRole { UserId = 1, RoleId = 1 },
            new UserRole { UserId = 2, RoleId = 1 },
            new UserRole { UserId = 13, RoleId = 2 },
            new UserRole { UserId = 20, RoleId = 3 },
            new UserRole { UserId = 21, RoleId = 1 }
        );

        _db.Staff.AddRange(
            new Staff { Id = 1, UserId = 1, HospitalId = 1, StaffCode = "STF-001", FullName = "Admin CS1", Status = "ACTIVE" },
            new Staff { Id = 2, UserId = 2, HospitalId = 2, StaffCode = "STF-002", FullName = "Admin CS2", Status = "ACTIVE" },
            new Staff { Id = 3, UserId = 21, HospitalId = null, StaffCode = "STF-021", FullName = "Unassigned Admin", Status = "ACTIVE" }
        );

        var dept1 = new Department { Id = 1, DepartmentCode = "KKB", Name = "Khoa Khám bệnh", IsActive = true };
        var dept2 = new Department { Id = 2, DepartmentCode = "KTM", Name = "Khoa Tim mạch", IsActive = true };

        var spec1 = new Specialty { Id = 1, SpecialtyCode = "NTQ", Name = "Nội tổng quát", IsActive = true };
        var spec2 = new Specialty { Id = 2, SpecialtyCode = "TMCT", Name = "Tim mạch can thiệp", IsActive = true };

        _db.Departments.AddRange(dept1, dept2);
        _db.Specialties.AddRange(spec1, spec2);
        _db.SaveChanges();

        var s1 = new Service
        {
            Id = 1,
            ServiceCode = "KB-01",
            Name = "Khám Nội tổng quát",
            Description = "Khám lâm sàng tổng quát",
            ServiceType = "EXAMINATION",
            DepartmentId = 1,
            SpecialtyId = 1,
            HospitalId = 1,
            AllowsOnlineBooking = true,
            IsActive = true,
            RequiresResultVerification = false
        };

        var s2 = new Service
        {
            Id = 2,
            ServiceCode = "KB-02",
            Name = "Hội chẩn Tim mạch chuyên sâu",
            Description = "Hội chẩn cùng chuyên gia",
            ServiceType = "CONSULTATION",
            DepartmentId = 2,
            SpecialtyId = 2,
            HospitalId = 1,
            AllowsOnlineBooking = false,
            IsActive = true,
            RequiresResultVerification = true
        };

        var s3 = new Service
        {
            Id = 3,
            ServiceCode = "CLS-01",
            Name = "Điện tâm đồ ECG",
            Description = "Đo điện tim 12 chuyển đạo",
            ServiceType = "DIAGNOSTIC",
            DepartmentId = 2,
            SpecialtyId = 2,
            HospitalId = 2,
            AllowsOnlineBooking = false,
            IsActive = false,
            RequiresResultVerification = true
        };

        var s4 = new Service
        {
            Id = 4,
            ServiceCode = "KB-03",
            Name = "Khám Nhi khoa tổng hợp",
            Description = "Khám tổng quát cho trẻ",
            ServiceType = "EXAMINATION",
            DepartmentId = 1,
            SpecialtyId = 1,
            HospitalId = 1,
            AllowsOnlineBooking = true,
            IsActive = false,
            RequiresResultVerification = false
        };

        _db.Services.AddRange(s1, s2, s3, s4);
        _db.SaveChanges();

        var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

        var p1 = new ServicePrice
        {
            Id = 1,
            ServiceId = 1,
            PriceAmount = 150000m,
            EffectiveFrom = now.AddDays(-30),
            EffectiveTo = now.AddDays(30)
        };

        var p2 = new ServicePrice
        {
            Id = 2,
            ServiceId = 2,
            PriceAmount = 500000m,
            EffectiveFrom = now.AddDays(-10),
            EffectiveTo = null
        };

        var p3 = new ServicePrice
        {
            Id = 3,
            ServiceId = 3,
            PriceAmount = 100000m,
            EffectiveFrom = now.AddDays(-60),
            EffectiveTo = now.AddDays(-1) // expired, fallback to latest
        };

        _db.ServicePrices.AddRange(p1, p2, p3);
        _db.SaveChanges();
    }

    [Fact]
    public async Task GetExaminationServiceListAsync_NullRequest_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _sut.GetExaminationServiceListAsync(13, null!));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task GetExaminationServiceListAsync_InvalidPage_ThrowsArgumentException(int page)
    {
        var request = new GetExaminationServiceListAdminRequest { Page = page, PageSize = 10 };
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.GetExaminationServiceListAsync(13, request));

        Assert.Contains("Trang yêu cầu", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(101)]
    public async Task GetExaminationServiceListAsync_InvalidPageSize_ThrowsArgumentException(int pageSize)
    {
        var request = new GetExaminationServiceListAdminRequest { Page = 1, PageSize = pageSize };
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.GetExaminationServiceListAsync(13, request));

        Assert.Contains("Số lượng bản ghi", ex.Message);
    }

    [Fact]
    public async Task GetExaminationServiceListAsync_DefaultRequest_ReturnsAllServicesWithCorrectMetrics()
    {
        var request = new GetExaminationServiceListAdminRequest();

        var result = await _sut.GetExaminationServiceListAsync(13, request);

        Assert.NotNull(result);
        Assert.Equal(4, result.Total);
        Assert.Equal(1, result.Page);
        Assert.Equal(10, result.PageSize);
        Assert.Equal(1, result.TotalPages);
        Assert.Equal(4, result.Data.Count);

        // Verify metrics
        Assert.NotNull(result.Metrics);
        Assert.Equal(4, result.Metrics.Total);
        Assert.Equal(2, result.Metrics.Active);
        Assert.Equal(2, result.Metrics.Suspended);
        Assert.Equal(2, result.Metrics.OnlineBookingCount);
        Assert.Equal(2, result.Metrics.DepartmentsCount);

        // Verify first item mappings
        var item1 = result.Data.First(x => x.Id == 1);
        Assert.Equal("KB-01", item1.ServiceCode);
        Assert.Equal("Khám Nội tổng quát", item1.Name);
        Assert.Equal("Khoa Khám bệnh", item1.DepartmentName);
        Assert.Equal("KKB", item1.DepartmentCode);
        Assert.Equal("Nội tổng quát", item1.SpecialtyName);
        Assert.Equal("NTQ", item1.SpecialtyCode);
        Assert.Equal(150000m, item1.Price);
        Assert.Equal("150.000 đ", item1.PriceFormatted);
        Assert.True(item1.AllowsOnlineBooking);
        Assert.Equal("ONLINE_ALLOWED", item1.BookingChannel);
        Assert.True(item1.IsActive);
        Assert.Equal("ACTIVE", item1.Status);
        Assert.Equal("Khám Lâm sàng", item1.Category);
        Assert.Equal("Bệnh viện Đa khoa MedFlow Hà Nội", item1.Facility);
    }

    [Theory]
    [InlineData("KB-01", 1)]
    [InlineData("Điện tâm đồ", 1)]
    [InlineData("chuyên gia", 1)]
    [InlineData("không tìm thấy", 0)]
    public async Task GetExaminationServiceListAsync_FilterByKeyword_ReturnsMatching(string keyword, int expectedCount)
    {
        var request = new GetExaminationServiceListAdminRequest { Keyword = keyword };

        var result = await _sut.GetExaminationServiceListAsync(13, request);

        Assert.Equal(expectedCount, result.Total);
        Assert.Equal(expectedCount, result.Data.Count);
        // Metrics should still report overall total
        Assert.Equal(4, result.Metrics.Total);
    }

    [Fact]
    public async Task GetExaminationServiceListAsync_FilterByDepartmentId_ReturnsOnlyServicesInDepartment()
    {
        var request = new GetExaminationServiceListAdminRequest { DepartmentId = 2 };

        var result = await _sut.GetExaminationServiceListAsync(13, request);

        Assert.Equal(2, result.Total);
        Assert.All(result.Data, item => Assert.Equal(2, item.DepartmentId));
    }

    [Fact]
    public async Task GetExaminationServiceListAsync_FilterBySpecialtyId_ReturnsOnlyServicesInSpecialty()
    {
        var request = new GetExaminationServiceListAdminRequest { SpecialtyId = 1 };

        var result = await _sut.GetExaminationServiceListAsync(13, request);

        Assert.Equal(2, result.Total);
        Assert.All(result.Data, item => Assert.Equal(1, item.SpecialtyId));
    }

    [Fact]
    public async Task GetExaminationServiceListAsync_FilterByHospitalId_ReturnsMatchingHospital()
    {
        var request = new GetExaminationServiceListAdminRequest { HospitalId = 2 };

        var result = await _sut.GetExaminationServiceListAsync(13, request);

        Assert.Single(result.Data);
        Assert.Equal("CLS-01", result.Data[0].ServiceCode);
        Assert.Equal("Bệnh viện Đa khoa MedFlow Đà Nẵng", result.Data[0].Facility);
    }

    [Theory]
    [InlineData("Khám Lâm sàng", 2)]
    [InlineData("examination", 2)]
    [InlineData("Hội chẩn Lâm sàng", 1)]
    [InlineData("consultation", 1)]
    [InlineData("Cận lâm sàng kỹ thuật", 1)]
    [InlineData("diagnostic", 1)]
    public async Task GetExaminationServiceListAsync_FilterByCategoryOrServiceType_ReturnsMatching(string category, int expectedCount)
    {
        var request = new GetExaminationServiceListAdminRequest { Category = category };

        var result = await _sut.GetExaminationServiceListAsync(13, request);

        Assert.Equal(expectedCount, result.Total);
    }

    [Theory]
    [InlineData("ONLINE_ALLOWED", 2)]
    [InlineData("COUNTER_ONLY", 2)]
    public async Task GetExaminationServiceListAsync_FilterByBookingChannel_ReturnsMatching(string channel, int expectedCount)
    {
        var request = new GetExaminationServiceListAdminRequest { BookingChannel = channel };

        var result = await _sut.GetExaminationServiceListAsync(13, request);

        Assert.Equal(expectedCount, result.Total);
        Assert.All(result.Data, item => Assert.Equal(channel, item.BookingChannel));
    }

    [Theory]
    [InlineData(true, 2)]
    [InlineData(false, 2)]
    public async Task GetExaminationServiceListAsync_FilterByAllowsOnlineBooking_ReturnsMatching(bool allowsOnline, int expectedCount)
    {
        var request = new GetExaminationServiceListAdminRequest { AllowsOnlineBooking = allowsOnline };

        var result = await _sut.GetExaminationServiceListAsync(13, request);

        Assert.Equal(expectedCount, result.Total);
        Assert.All(result.Data, item => Assert.Equal(allowsOnline, item.AllowsOnlineBooking));
    }

    [Theory]
    [InlineData("ACTIVE", 2)]
    [InlineData("SUSPENDED", 2)]
    [InlineData("INACTIVE", 2)]
    public async Task GetExaminationServiceListAsync_FilterByStatus_ReturnsMatching(string status, int expectedCount)
    {
        var request = new GetExaminationServiceListAdminRequest { Status = status };

        var result = await _sut.GetExaminationServiceListAsync(13, request);

        Assert.Equal(expectedCount, result.Total);
    }

    [Theory]
    [InlineData(true, 2)]
    [InlineData(false, 2)]
    public async Task GetExaminationServiceListAsync_FilterByIsActive_ReturnsMatching(bool isActive, int expectedCount)
    {
        var request = new GetExaminationServiceListAdminRequest { IsActive = isActive };

        var result = await _sut.GetExaminationServiceListAsync(13, request);

        Assert.Equal(expectedCount, result.Total);
        Assert.All(result.Data, item => Assert.Equal(isActive, item.IsActive));
    }

    [Fact]
    public async Task GetExaminationServiceListAsync_Pagination_ComputesPagesAndSlicesCorrectly()
    {
        var request = new GetExaminationServiceListAdminRequest { Page = 2, PageSize = 2 };

        var result = await _sut.GetExaminationServiceListAsync(13, request);

        Assert.Equal(4, result.Total);
        Assert.Equal(2, result.Page);
        Assert.Equal(2, result.PageSize);
        Assert.Equal(2, result.TotalPages);
        Assert.Equal(2, result.Data.Count);
        Assert.Equal(3, result.Data[0].Id);
        Assert.Equal(4, result.Data[1].Id);
    }

    [Fact]
    public async Task GetExaminationServiceListAsync_ServiceWithoutPrice_DefaultsToZeroPrice()
    {
        var request = new GetExaminationServiceListAdminRequest { Keyword = "KB-03" };

        var result = await _sut.GetExaminationServiceListAsync(13, request);

        var item = Assert.Single(result.Data);
        Assert.Equal(0m, item.Price);
        Assert.Equal("0 đ", item.PriceFormatted);
    }

    [Fact]
    public async Task CreateExaminationServiceAsync_NullRequest_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _sut.CreateExaminationServiceAsync(13, null!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateExaminationServiceAsync_EmptyServiceCode_ThrowsArgumentException(string? code)
    {
        var request = new CreateExaminationServiceRequest(ServiceCode: code, Name: "Dịch vụ mẫu");
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.CreateExaminationServiceAsync(13, request));

        Assert.Contains("Mã dịch vụ khám không được để trống", ex.Message);
    }

    [Fact]
    public async Task CreateExaminationServiceAsync_ServiceCodeTooShort_ThrowsArgumentException()
    {
        var request = new CreateExaminationServiceRequest(ServiceCode: "A", Name: "Dịch vụ mẫu");
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.CreateExaminationServiceAsync(13, request));

        Assert.Contains("ít nhất 2 ký tự", ex.Message);
    }

    [Fact]
    public async Task CreateExaminationServiceAsync_ServiceCodeTooLong_ThrowsArgumentException()
    {
        var longCode = new string('X', 41);
        var request = new CreateExaminationServiceRequest(ServiceCode: longCode, Name: "Dịch vụ mẫu");
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.CreateExaminationServiceAsync(13, request));

        Assert.Contains("vượt quá 40 ký tự", ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateExaminationServiceAsync_EmptyName_ThrowsArgumentException(string? name)
    {
        var request = new CreateExaminationServiceRequest(ServiceCode: "EX-TEST", Name: name);
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.CreateExaminationServiceAsync(13, request));

        Assert.Contains("Tên dịch vụ khám không được để trống", ex.Message);
    }

    [Fact]
    public async Task CreateExaminationServiceAsync_NameTooShort_ThrowsArgumentException()
    {
        var request = new CreateExaminationServiceRequest(ServiceCode: "EX-TEST", Name: "AB");
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.CreateExaminationServiceAsync(13, request));

        Assert.Contains("ít nhất 3 ký tự", ex.Message);
    }

    [Fact]
    public async Task CreateExaminationServiceAsync_NameTooLong_ThrowsArgumentException()
    {
        var longName = new string('A', 201);
        var request = new CreateExaminationServiceRequest(ServiceCode: "EX-TEST", Name: longName);
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.CreateExaminationServiceAsync(13, request));

        Assert.Contains("vượt quá 200 ký tự", ex.Message);
    }

    [Fact]
    public async Task CreateExaminationServiceAsync_InvalidCategory_ThrowsArgumentException()
    {
        var request = new CreateExaminationServiceRequest(
            ServiceCode: "EX-TEST",
            Name: "Dịch vụ thử nghiệm",
            Category: "Danh mục không tồn tại"
        );
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.CreateExaminationServiceAsync(13, request));

        Assert.Contains("Phân loại dịch vụ không hợp lệ", ex.Message);
    }

    [Fact]
    public async Task CreateExaminationServiceAsync_NonExistentDepartmentId_ThrowsKeyNotFoundException()
    {
        var request = new CreateExaminationServiceRequest(
            ServiceCode: "EX-TEST",
            Name: "Dịch vụ thử nghiệm",
            DepartmentId: 9999
        );
        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _sut.CreateExaminationServiceAsync(13, request));

        Assert.Contains("Không tìm thấy khoa phòng", ex.Message);
    }

    [Fact]
    public async Task CreateExaminationServiceAsync_NonExistentDepartmentName_ThrowsKeyNotFoundException()
    {
        var request = new CreateExaminationServiceRequest(
            ServiceCode: "EX-TEST",
            Name: "Dịch vụ thử nghiệm",
            Department: "Khoa Không Tồn Tại"
        );
        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _sut.CreateExaminationServiceAsync(13, request));

        Assert.Contains("Không tìm thấy khoa phòng", ex.Message);
    }

    [Fact]
    public async Task CreateExaminationServiceAsync_NonExistentSpecialtyId_ThrowsKeyNotFoundException()
    {
        var request = new CreateExaminationServiceRequest(
            ServiceCode: "EX-TEST",
            Name: "Dịch vụ thử nghiệm",
            SpecialtyId: 9999
        );
        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _sut.CreateExaminationServiceAsync(13, request));

        Assert.Contains("Không tìm thấy chuyên khoa", ex.Message);
    }

    [Fact]
    public async Task CreateExaminationServiceAsync_DuplicateServiceCode_ThrowsInvalidOperationException()
    {
        var request = new CreateExaminationServiceRequest(
            ServiceCode: "kb-01", // Already exists
            Name: "Khám lại nội tổng quát",
            HospitalId: 1
        );
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.CreateExaminationServiceAsync(13, request));

        Assert.Contains("đã tồn tại trong cơ sở y tế", ex.Message);
    }

    [Fact]
    public async Task CreateExaminationServiceAsync_NegativePrice_ThrowsArgumentException()
    {
        var request = new CreateExaminationServiceRequest(
            ServiceCode: "EX-TEST-PRICE",
            Name: "Dịch vụ giá âm",
            Price: -1000m
        );
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.CreateExaminationServiceAsync(13, request));

        Assert.Contains("không được là số âm", ex.Message);
    }

    [Fact]
    public async Task CreateExaminationServiceAsync_ValidExaminationRequest_CreatesServiceAndReturnsDto()
    {
        var request = new CreateExaminationServiceRequest(
            Code: "ex-2-09",
            Name: "Khám Chuyên khoa Nội Tiêu hóa & Gan Mật",
            Category: "Khám Lâm sàng",
            DepartmentId: 1,
            SpecialtyId: 1,
            HospitalId: 1,
            Description: "Thăm khám, chẩn đoán phân biệt và tư vấn phác đồ điều trị chuyên sâu",
            Price: 300000m,
            OnlineBooking: true,
            Status: "ACTIVE"
        );

        var result = await _sut.CreateExaminationServiceAsync(13, request);

        Assert.NotNull(result);
        Assert.True(result.Id > 0);
        Assert.Equal("EX-2-09", result.ServiceCode);
        Assert.Equal("Khám Chuyên khoa Nội Tiêu hóa & Gan Mật", result.Name);
        Assert.Equal("Khám Lâm sàng", result.Category);
        Assert.Equal("EXAMINATION", result.ServiceType);
        Assert.Equal(1, result.DepartmentId);
        Assert.Equal("Khoa Khám bệnh", result.DepartmentName);
        Assert.Equal("KKB", result.DepartmentCode);
        Assert.Equal(1, result.SpecialtyId);
        Assert.Equal("Nội tổng quát", result.SpecialtyName);
        Assert.Equal("NTQ", result.SpecialtyCode);
        Assert.Equal(300000m, result.Price);
        Assert.Equal("300.000 đ", result.PriceFormatted);
        Assert.True(result.AllowsOnlineBooking);
        Assert.Equal("ONLINE_ALLOWED", result.BookingChannel);
        Assert.False(result.RequiresResultVerification);
        Assert.True(result.IsActive);
        Assert.Equal("ACTIVE", result.Status);
        Assert.Equal("Bệnh viện Đa khoa MedFlow Hà Nội", result.Facility);

        // Verify entity persisted in database
        var inDb = await _db.Services.FirstOrDefaultAsync(s => s.Id == result.Id);
        Assert.NotNull(inDb);
        Assert.Equal("EX-2-09", inDb.ServiceCode);

        // Verify price entity persisted
        var priceInDb = await _db.ServicePrices.FirstOrDefaultAsync(p => p.ServiceId == result.Id);
        Assert.NotNull(priceInDb);
        Assert.Equal(300000m, priceInDb.PriceAmount);
    }

    [Fact]
    public async Task CreateExaminationServiceAsync_DiagnosticWithDepartmentName_CreatesServiceWithAutoVerification()
    {
        var request = new CreateExaminationServiceRequest(
            ServiceCode: "CLS-XRAY-01",
            Name: "Chụp X-quang tim phổi thẳng",
            Category: "Cận lâm sàng kỹ thuật",
            Department: "Khoa Tim mạch",
            SpecialtyId: 2,
            HospitalId: 2,
            AllowsOnlineBooking: false,
            Status: "ACTIVE",
            Price: 120000m
        );

        var result = await _sut.CreateExaminationServiceAsync(13, request);

        Assert.NotNull(result);
        Assert.Equal("CLS-XRAY-01", result.ServiceCode);
        Assert.Equal("Cận lâm sàng kỹ thuật", result.Category);
        Assert.Equal("DIAGNOSTIC", result.ServiceType);
        Assert.Equal(2, result.DepartmentId);
        Assert.Equal("Khoa Tim mạch", result.DepartmentName);
        Assert.False(result.AllowsOnlineBooking);
        Assert.Equal("COUNTER_ONLY", result.BookingChannel);
        Assert.True(result.RequiresResultVerification);
        Assert.Equal("Bệnh viện Đa khoa MedFlow Đà Nẵng", result.Facility);
    }

    [Fact]
    public async Task CreateExaminationServiceAsync_SuspendedStatus_CreatesInactiveService()
    {
        var request = new CreateExaminationServiceRequest(
            ServiceCode: "EX-SUSP-01",
            Name: "Dịch vụ tạm ngưng ngay khi tạo",
            HospitalId: 1,
            Status: "SUSPENDED"
        );

        var result = await _sut.CreateExaminationServiceAsync(13, request);

        Assert.NotNull(result);
        Assert.False(result.IsActive);
        Assert.Equal("SUSPENDED", result.Status);
    }

    [Fact]
    public async Task CreateExaminationServiceAsync_WithoutPrice_DefaultsToZeroPrice()
    {
        var request = new CreateExaminationServiceRequest(
            ServiceCode: "EX-FREE-01",
            Name: "Dịch vụ miễn phí tư vấn",
            HospitalId: 1
        );

        var result = await _sut.CreateExaminationServiceAsync(13, request);

        Assert.NotNull(result);
        Assert.Equal(0m, result.Price);
        Assert.Equal("0 đ", result.PriceFormatted);

        var prices = await _db.ServicePrices.Where(p => p.ServiceId == result.Id).ToListAsync();
        Assert.Empty(prices);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task UpdateExaminationServiceAsync_InvalidId_ThrowsArgumentException(long id)
    {
        var request = new UpdateExaminationServiceRequest(Name: "Tên mới");
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.UpdateExaminationServiceAsync(13, id, request));

        Assert.Contains("ID dịch vụ khám phải lớn hơn 0", ex.Message);
    }

    [Fact]
    public async Task UpdateExaminationServiceAsync_NullRequest_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _sut.UpdateExaminationServiceAsync(13, 1, null!));
    }

    [Fact]
    public async Task UpdateExaminationServiceAsync_NotFoundId_ThrowsKeyNotFoundException()
    {
        var request = new UpdateExaminationServiceRequest(Name: "Tên mới");
        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _sut.UpdateExaminationServiceAsync(13, 9999, request));

        Assert.Contains("Không tìm thấy dịch vụ khám", ex.Message);
    }

    [Fact]
    public async Task UpdateExaminationServiceAsync_ServiceCodeTooShort_ThrowsArgumentException()
    {
        var request = new UpdateExaminationServiceRequest(ServiceCode: "A");
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.UpdateExaminationServiceAsync(13, 1, request));

        Assert.Contains("ít nhất 2 ký tự", ex.Message);
    }

    [Fact]
    public async Task UpdateExaminationServiceAsync_ServiceCodeTooLong_ThrowsArgumentException()
    {
        var longCode = new string('A', 41);
        var request = new UpdateExaminationServiceRequest(ServiceCode: longCode);
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.UpdateExaminationServiceAsync(13, 1, request));

        Assert.Contains("vượt quá 40 ký tự", ex.Message);
    }

    [Fact]
    public async Task UpdateExaminationServiceAsync_DuplicateServiceCode_ThrowsInvalidOperationException()
    {
        // Try updating service 2's code to "KB-01" which is already used by service 1
        var request = new UpdateExaminationServiceRequest(ServiceCode: "kb-01");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.UpdateExaminationServiceAsync(13, 2, request));

        Assert.Contains("đã tồn tại trong cơ sở y tế", ex.Message);
    }

    [Fact]
    public async Task UpdateExaminationServiceAsync_KeepExistingServiceCode_DoesNotThrowDuplicateConflict()
    {
        // Updating service 1 keeping its own code "KB-01"
        var request = new UpdateExaminationServiceRequest(ServiceCode: "KB-01", Name: "Khám Nội tổng quát Đã Cập Nhật");
        var result = await _sut.UpdateExaminationServiceAsync(13, 1, request);

        Assert.NotNull(result);
        Assert.Equal("KB-01", result.ServiceCode);
        Assert.Equal("Khám Nội tổng quát Đã Cập Nhật", result.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task UpdateExaminationServiceAsync_EmptyName_ThrowsArgumentException(string emptyName)
    {
        var request = new UpdateExaminationServiceRequest(Name: emptyName);
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.UpdateExaminationServiceAsync(13, 1, request));

        Assert.Contains("Tên dịch vụ khám không được để trống", ex.Message);
    }

    [Fact]
    public async Task UpdateExaminationServiceAsync_NameTooShort_ThrowsArgumentException()
    {
        var request = new UpdateExaminationServiceRequest(Name: "AB");
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.UpdateExaminationServiceAsync(13, 1, request));

        Assert.Contains("ít nhất 3 ký tự", ex.Message);
    }

    [Fact]
    public async Task UpdateExaminationServiceAsync_NameTooLong_ThrowsArgumentException()
    {
        var longName = new string('A', 201);
        var request = new UpdateExaminationServiceRequest(Name: longName);
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.UpdateExaminationServiceAsync(13, 1, request));

        Assert.Contains("vượt quá 200 ký tự", ex.Message);
    }

    [Fact]
    public async Task UpdateExaminationServiceAsync_InvalidCategory_ThrowsArgumentException()
    {
        var request = new UpdateExaminationServiceRequest(Category: "Không hợp lệ");
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.UpdateExaminationServiceAsync(13, 1, request));

        Assert.Contains("Phân loại dịch vụ không hợp lệ", ex.Message);
    }

    [Fact]
    public async Task UpdateExaminationServiceAsync_NonExistentDepartmentId_ThrowsKeyNotFoundException()
    {
        var request = new UpdateExaminationServiceRequest(DepartmentId: 8888);
        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _sut.UpdateExaminationServiceAsync(13, 1, request));

        Assert.Contains("Không tìm thấy khoa phòng", ex.Message);
    }

    [Fact]
    public async Task UpdateExaminationServiceAsync_NonExistentDepartmentName_ThrowsKeyNotFoundException()
    {
        var request = new UpdateExaminationServiceRequest(Department: "Khoa Giả Định Không Tồn Tại");
        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _sut.UpdateExaminationServiceAsync(13, 1, request));

        Assert.Contains("Không tìm thấy khoa phòng", ex.Message);
    }

    [Fact]
    public async Task UpdateExaminationServiceAsync_NonExistentSpecialtyId_ThrowsKeyNotFoundException()
    {
        var request = new UpdateExaminationServiceRequest(SpecialtyId: 8888);
        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _sut.UpdateExaminationServiceAsync(13, 1, request));

        Assert.Contains("Không tìm thấy chuyên khoa", ex.Message);
    }

    [Fact]
    public async Task UpdateExaminationServiceAsync_NegativePrice_ThrowsArgumentException()
    {
        var request = new UpdateExaminationServiceRequest(Price: -50000m);
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.UpdateExaminationServiceAsync(13, 1, request));

        Assert.Contains("không được là số âm", ex.Message);
    }

    [Fact]
    public async Task UpdateExaminationServiceAsync_ValidRequest_UpdatesFieldsAndAddsNewPrice()
    {
        var request = new UpdateExaminationServiceRequest(
            Name: "Khám Nội tổng quát và Tiêu hóa Chuyên sâu",
            Category: "Hội chẩn Lâm sàng",
            DepartmentId: 2,
            SpecialtyId: 2,
            Description: "Phạm vi khám mở rộng có hội chẩn chuyên sâu",
            AllowsOnlineBooking: false,
            Status: "SUSPENDED",
            Price: 450000m
        );

        var result = await _sut.UpdateExaminationServiceAsync(13, 1, request);

        Assert.NotNull(result);
        Assert.Equal(1, result.Id);
        Assert.Equal("KB-01", result.ServiceCode);
        Assert.Equal("Khám Nội tổng quát và Tiêu hóa Chuyên sâu", result.Name);
        Assert.Equal("Hội chẩn Lâm sàng", result.Category);
        Assert.Equal("CONSULTATION", result.ServiceType);
        Assert.Equal(2, result.DepartmentId);
        Assert.Equal("Khoa Tim mạch", result.DepartmentName);
        Assert.Equal(2, result.SpecialtyId);
        Assert.Equal("Tim mạch can thiệp", result.SpecialtyName);
        Assert.Equal("Phạm vi khám mở rộng có hội chẩn chuyên sâu", result.Description);
        Assert.False(result.AllowsOnlineBooking);
        Assert.Equal("COUNTER_ONLY", result.BookingChannel);
        Assert.False(result.IsActive);
        Assert.Equal("SUSPENDED", result.Status);
        Assert.Equal(450000m, result.Price);
        Assert.Equal("450.000 đ", result.PriceFormatted);

        // Verify old prices have EffectiveTo set and new price is in database
        var prices = await _db.ServicePrices.Where(p => p.ServiceId == 1).ToListAsync();
        Assert.True(prices.Count >= 2);
        var activePrice = prices.FirstOrDefault(p => !p.EffectiveTo.HasValue);
        Assert.NotNull(activePrice);
        Assert.Equal(450000m, activePrice.PriceAmount);
    }

    [Fact]
    public async Task UpdateExaminationServiceAsync_DiagnosticWithDepartmentName_SetsDepartmentAndVerification()
    {
        var request = new UpdateExaminationServiceRequest(
            Category: "Cận lâm sàng kỹ thuật",
            Department: "Khoa Khám bệnh"
        );

        var result = await _sut.UpdateExaminationServiceAsync(13, 1, request);

        Assert.NotNull(result);
        Assert.Equal("Cận lâm sàng kỹ thuật", result.Category);
        Assert.Equal("DIAGNOSTIC", result.ServiceType);
        Assert.Equal(1, result.DepartmentId);
        Assert.Equal("Khoa Khám bệnh", result.DepartmentName);
    }

    // =========================================================================
    // UC50 — Deactivate & Reactivate Examination Service Tests (#196 / M2-064)
    // =========================================================================

    [Fact]
    public async Task DeactivateExaminationServiceAsync_EmptyIdOrCode_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.DeactivateExaminationServiceAsync(1, "   "));
    }

    [Fact]
    public async Task DeactivateExaminationServiceAsync_ServiceNotFound_ThrowsNotFoundException()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _sut.DeactivateExaminationServiceAsync(1, "99999"));
    }

    [Fact]
    public async Task DeactivateExaminationServiceAsync_AlreadySuspended_ThrowsConflictException()
    {
        var service = new Service
        {
            Id = 501,
            ServiceCode = "EX-SUSPENDED",
            Name = "Dịch vụ đã ngừng",
            ServiceType = "EXAMINATION",
            HospitalId = 1,
            IsActive = false
        };
        _db.Services.Add(service);
        await _db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            _sut.DeactivateExaminationServiceAsync(1, "501"));

        Assert.Contains("ngừng hoạt động", ex.Message);
    }

    [Fact]
    public async Task DeactivateExaminationServiceAsync_HasActiveBookingsWithoutConfirmation_ThrowsConflictException()
    {
        var service = new Service
        {
            Id = 502,
            ServiceCode = "EX-WITH-BOOKINGS",
            Name = "Dịch vụ có lịch đặt",
            ServiceType = "EXAMINATION",
            HospitalId = 1,
            IsActive = true
        };
        var nowLocal = MedFlow.Application.Common.Time.HospitalClock.GetNow(TimeProvider.System);
        var slot = new ExaminationSlot
        {
            Id = 5021,
            ServiceId = 502,
            Status = "OPEN",
            StartsAt = nowLocal.AddHours(2),
            EndsAt = nowLocal.AddHours(3)
        };
        var appointment = new Appointment
        {
            Id = 50211,
            SlotId = 5021,
            Status = "CONFIRMED"
        };

        _db.Services.Add(service);
        _db.ExaminationSlots.Add(slot);
        _db.Appointments.Add(appointment);
        await _db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            _sut.DeactivateExaminationServiceAsync(1, "502", new DeactivateExaminationServiceRequest(ConfirmUnderstood: false)));

        Assert.Contains("Không thể ngừng hoạt động dịch vụ khám khi còn", ex.Message);
        Assert.Contains("lịch hẹn khám", ex.Message);
    }

    [Fact]
    public async Task DeactivateExaminationServiceAsync_HasActiveBookingsWithConfirmation_ClosesSlotsAndSuspendsService()
    {
        var service = new Service
        {
            Id = 503,
            ServiceCode = "EX-WITH-CONFIRM",
            Name = "Dịch vụ xác nhận đóng",
            ServiceType = "EXAMINATION",
            HospitalId = 1,
            IsActive = true
        };
        var nowLocal = MedFlow.Application.Common.Time.HospitalClock.GetNow(TimeProvider.System);
        var slot = new ExaminationSlot
        {
            Id = 5031,
            ServiceId = 503,
            Status = "OPEN",
            StartsAt = nowLocal.AddHours(1),
            EndsAt = nowLocal.AddHours(2)
        };
        var appointment = new Appointment
        {
            Id = 50311,
            SlotId = 5031,
            Status = "CONFIRMED"
        };

        _db.Services.Add(service);
        _db.ExaminationSlots.Add(slot);
        _db.Appointments.Add(appointment);
        await _db.SaveChangesAsync();

        var request = new DeactivateExaminationServiceRequest(
            ReasonCode: "POLICY_CHANGE",
            Reason: "Thay đổi quy trình khám",
            ConfirmUnderstood: true,
            CloseFutureSlots: true
        );

        var result = await _sut.DeactivateExaminationServiceAsync(1, "503", request);

        Assert.NotNull(result);
        Assert.False(result.IsActive);
        Assert.Equal("SUSPENDED", result.Status);
        Assert.Equal(1, result.ActiveBookingsCount);
        Assert.Equal(1, result.ActiveSlotsCount);

        var dbService = await _db.Services.FindAsync(503L);
        Assert.NotNull(dbService);
        Assert.False(dbService.IsActive);

        var dbSlot = await _db.ExaminationSlots.FindAsync(5031L);
        Assert.NotNull(dbSlot);
        Assert.Equal("CLOSED", dbSlot.Status);

        var auditLog = await _db.AuditLogs.FirstOrDefaultAsync(al => al.Action == "EXAMINATION_SERVICE_DEACTIVATED" && al.EntityId == "503");
        Assert.NotNull(auditLog);
        Assert.Equal("Service", auditLog.EntityType);
    }

    [Fact]
    public async Task DeactivateExaminationServiceAsync_ByServiceCode_DeactivatesSuccessfully()
    {
        var service = new Service
        {
            Id = 504,
            ServiceCode = "EX-CODE-TEST",
            Name = "Dịch vụ tìm theo code",
            ServiceType = "EXAMINATION",
            HospitalId = 1,
            IsActive = true
        };
        _db.Services.Add(service);
        await _db.SaveChangesAsync();

        var result = await _sut.DeactivateExaminationServiceAsync(1, "EX-CODE-TEST");

        Assert.NotNull(result);
        Assert.False(result.IsActive);
        Assert.Equal("SUSPENDED", result.Status);
        Assert.Equal("EX-CODE-TEST", result.ServiceCode);
    }

    [Fact]
    public async Task ReactivateExaminationServiceAsync_ServiceNotFound_ThrowsNotFoundException()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _sut.ReactivateExaminationServiceAsync(1, "99999"));
    }

    [Fact]
    public async Task ReactivateExaminationServiceAsync_AlreadyActive_ThrowsConflictException()
    {
        var service = new Service
        {
            Id = 505,
            ServiceCode = "EX-ALREADY-ACTIVE",
            Name = "Dịch vụ đang hoạt động",
            ServiceType = "EXAMINATION",
            HospitalId = 1,
            IsActive = true
        };
        _db.Services.Add(service);
        await _db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            _sut.ReactivateExaminationServiceAsync(1, "505"));

        Assert.Contains("đang ở trạng thái hoạt động", ex.Message);
    }

    [Fact]
    public async Task ReactivateExaminationServiceAsync_SuspendedService_ReactivatesSuccessfully()
    {
        var service = new Service
        {
            Id = 506,
            ServiceCode = "EX-REACTIVATE",
            Name = "Dịch vụ mở lại",
            ServiceType = "EXAMINATION",
            HospitalId = 1,
            IsActive = false
        };
        _db.Services.Add(service);
        await _db.SaveChangesAsync();

        var result = await _sut.ReactivateExaminationServiceAsync(1, "506", new ReactivateExaminationServiceRequest(Reason: "Mở lại phục vụ bệnh nhân"));

        Assert.NotNull(result);
        Assert.True(result.IsActive);
        Assert.Equal("ACTIVE", result.Status);

        var dbService = await _db.Services.FindAsync(506L);
        Assert.NotNull(dbService);
        Assert.True(dbService.IsActive);

        var auditLog = await _db.AuditLogs.FirstOrDefaultAsync(al => al.Action == "EXAMINATION_SERVICE_REACTIVATED" && al.EntityId == "506");
        Assert.NotNull(auditLog);
    }

    [Fact]
    public async Task GetExaminationServiceListAsync_PopulatesActiveBookingsFlagsCorrectly()
    {
        var service = new Service
        {
            Id = 507,
            ServiceCode = "EX-WITH-ACTIVE-FLAG",
            Name = "Dịch vụ test cờ active bookings",
            ServiceType = "EXAMINATION",
            HospitalId = 1,
            IsActive = true
        };
        var slot = new ExaminationSlot
        {
            Id = 5071,
            ServiceId = 507,
            Status = "OPEN",
            StartsAt = DateTime.UtcNow.AddDays(1),
            EndsAt = DateTime.UtcNow.AddDays(1).AddHours(1)
        };
        var appointment = new Appointment
        {
            Id = 50711,
            SlotId = 5071,
            Status = "PENDING"
        };

        _db.Services.Add(service);
        _db.ExaminationSlots.Add(slot);
        _db.Appointments.Add(appointment);
        await _db.SaveChangesAsync();

        var response = await _sut.GetExaminationServiceListAsync(13, new GetExaminationServiceListAdminRequest(Keyword: "EX-WITH-ACTIVE-FLAG"));

        Assert.NotNull(response);
        Assert.Single(response.Data);
        var item = response.Data[0];
        Assert.True(item.HasActiveBookings);
        Assert.Equal(1, item.ActiveBookingsCount);
    }

    [Fact]
    public async Task CreateExaminationServiceAsync_SystemAdminWithoutHospitalId_ThrowsValidationException()
    {
        var request = new CreateExaminationServiceRequest(
            ServiceCode: "EX-NO-HOSP",
            Name: "Dịch vụ thiếu mã cơ sở",
            HospitalId: null
        );

        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.CreateExaminationServiceAsync(13, request));
    }

    [Fact]
    public async Task CreateExaminationServiceAsync_NonExistentHospitalId_ThrowsNotFoundException()
    {
        var request = new CreateExaminationServiceRequest(
            ServiceCode: "EX-MISSING-HOSP",
            Name: "Dịch vụ cơ sở không tồn tại",
            HospitalId: 999
        );

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _sut.CreateExaminationServiceAsync(13, request));
    }

    [Fact]
    public async Task CreateExaminationServiceAsync_HospitalAdminMissingHospitalOrCrossHospital_ThrowsForbiddenException()
    {
        var crossHospitalReq = new CreateExaminationServiceRequest(
            ServiceCode: "EX-CROSS-HOSP",
            Name: "Dịch vụ khác cơ sở",
            HospitalId: 2
        );
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.CreateExaminationServiceAsync(1, crossHospitalReq));

        var unassignedReq = new CreateExaminationServiceRequest(
            ServiceCode: "EX-UNASSIGNED",
            Name: "Dịch vụ quản trị chưa gán viện",
            HospitalId: null
        );
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.CreateExaminationServiceAsync(21, unassignedReq));
    }

    [Fact]
    public async Task DeactivateExaminationServiceAsync_NonAdminRoleOrMissingPermission_FailsClosedWithForbiddenException()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.DeactivateExaminationServiceAsync(20, "1"));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.DeactivateExaminationServiceAsync(1, "3")); // Service 3 belongs to Hospital 2

        _db.RolePermissions.RemoveRange(_db.RolePermissions);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.DeactivateExaminationServiceAsync(1, "1"));
    }
}
