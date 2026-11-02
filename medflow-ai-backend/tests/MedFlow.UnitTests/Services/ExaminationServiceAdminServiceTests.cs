using MedFlow.Application.DTOs.Services;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Services;
using MedFlow.Domain.Entities;
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

        public DbSet<User> Users => throw NotUsed();
        public DbSet<Role> Roles => throw NotUsed();
        public DbSet<Permission> Permissions => throw NotUsed();
        public DbSet<UserRole> UserRoles => throw NotUsed();
        public DbSet<RolePermission> RolePermissions => throw NotUsed();
        public DbSet<Patient> Patients => throw NotUsed();
        public DbSet<Staff> Staff => throw NotUsed();
        public DbSet<Doctor> Doctors => throw NotUsed();
        public DbSet<DoctorServiceAuthorization> DoctorServiceAuthorizations => throw NotUsed();
        public DbSet<ExaminationSlot> ExaminationSlots => throw NotUsed();
        public DbSet<AuthSession> AuthSessions => throw NotUsed();
        public DbSet<AuthVerificationChallenge> AuthVerificationChallenges => throw NotUsed();
        public DbSet<AuditLog> AuditLogs => throw NotUsed();

        public DbSet<Hospital> Hospitals => throw NotUsed();
        public DbSet<Department> Departments => Set<Department>();
        public DbSet<HospitalRoom> HospitalRooms => throw NotUsed();
        public DbSet<Specialty> Specialties => Set<Specialty>();
        public DbSet<Service> Services => Set<Service>();
        public DbSet<ServicePrice> ServicePrices => Set<ServicePrice>();
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
            modelBuilder.Ignore<User>();
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
            modelBuilder.Ignore<AuditLog>();
            modelBuilder.Ignore<Hospital>();
            modelBuilder.Ignore<HospitalRoom>();
            modelBuilder.Ignore<HospitalPolicy>();
            modelBuilder.Ignore<Appointment>();
            modelBuilder.Ignore<PatientFamilyLink>();
            modelBuilder.Ignore<Visit>();
            modelBuilder.Ignore<QueueEntry>();
            modelBuilder.Ignore<Payment>();

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
            _sut.GetExaminationServiceListAsync(null!));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task GetExaminationServiceListAsync_InvalidPage_ThrowsArgumentException(int page)
    {
        var request = new GetExaminationServiceListAdminRequest { Page = page, PageSize = 10 };
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.GetExaminationServiceListAsync(request));

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
            _sut.GetExaminationServiceListAsync(request));

        Assert.Contains("Số lượng bản ghi", ex.Message);
    }

    [Fact]
    public async Task GetExaminationServiceListAsync_DefaultRequest_ReturnsAllServicesWithCorrectMetrics()
    {
        var request = new GetExaminationServiceListAdminRequest();

        var result = await _sut.GetExaminationServiceListAsync(request);

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
        Assert.Equal("MedFlow Cơ sở 1", item1.Facility);
    }

    [Theory]
    [InlineData("KB-01", 1)]
    [InlineData("Điện tâm đồ", 1)]
    [InlineData("chuyên gia", 1)]
    [InlineData("không tìm thấy", 0)]
    public async Task GetExaminationServiceListAsync_FilterByKeyword_ReturnsMatching(string keyword, int expectedCount)
    {
        var request = new GetExaminationServiceListAdminRequest { Keyword = keyword };

        var result = await _sut.GetExaminationServiceListAsync(request);

        Assert.Equal(expectedCount, result.Total);
        Assert.Equal(expectedCount, result.Data.Count);
        // Metrics should still report overall total
        Assert.Equal(4, result.Metrics.Total);
    }

    [Fact]
    public async Task GetExaminationServiceListAsync_FilterByDepartmentId_ReturnsOnlyServicesInDepartment()
    {
        var request = new GetExaminationServiceListAdminRequest { DepartmentId = 2 };

        var result = await _sut.GetExaminationServiceListAsync(request);

        Assert.Equal(2, result.Total);
        Assert.All(result.Data, item => Assert.Equal(2, item.DepartmentId));
    }

    [Fact]
    public async Task GetExaminationServiceListAsync_FilterBySpecialtyId_ReturnsOnlyServicesInSpecialty()
    {
        var request = new GetExaminationServiceListAdminRequest { SpecialtyId = 1 };

        var result = await _sut.GetExaminationServiceListAsync(request);

        Assert.Equal(2, result.Total);
        Assert.All(result.Data, item => Assert.Equal(1, item.SpecialtyId));
    }

    [Fact]
    public async Task GetExaminationServiceListAsync_FilterByHospitalId_ReturnsMatchingHospital()
    {
        var request = new GetExaminationServiceListAdminRequest { HospitalId = 2 };

        var result = await _sut.GetExaminationServiceListAsync(request);

        Assert.Single(result.Data);
        Assert.Equal("CLS-01", result.Data[0].ServiceCode);
        Assert.Equal("MedFlow Cơ sở 2", result.Data[0].Facility);
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

        var result = await _sut.GetExaminationServiceListAsync(request);

        Assert.Equal(expectedCount, result.Total);
    }

    [Theory]
    [InlineData("ONLINE_ALLOWED", 2)]
    [InlineData("COUNTER_ONLY", 2)]
    public async Task GetExaminationServiceListAsync_FilterByBookingChannel_ReturnsMatching(string channel, int expectedCount)
    {
        var request = new GetExaminationServiceListAdminRequest { BookingChannel = channel };

        var result = await _sut.GetExaminationServiceListAsync(request);

        Assert.Equal(expectedCount, result.Total);
        Assert.All(result.Data, item => Assert.Equal(channel, item.BookingChannel));
    }

    [Theory]
    [InlineData(true, 2)]
    [InlineData(false, 2)]
    public async Task GetExaminationServiceListAsync_FilterByAllowsOnlineBooking_ReturnsMatching(bool allowsOnline, int expectedCount)
    {
        var request = new GetExaminationServiceListAdminRequest { AllowsOnlineBooking = allowsOnline };

        var result = await _sut.GetExaminationServiceListAsync(request);

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

        var result = await _sut.GetExaminationServiceListAsync(request);

        Assert.Equal(expectedCount, result.Total);
    }

    [Theory]
    [InlineData(true, 2)]
    [InlineData(false, 2)]
    public async Task GetExaminationServiceListAsync_FilterByIsActive_ReturnsMatching(bool isActive, int expectedCount)
    {
        var request = new GetExaminationServiceListAdminRequest { IsActive = isActive };

        var result = await _sut.GetExaminationServiceListAsync(request);

        Assert.Equal(expectedCount, result.Total);
        Assert.All(result.Data, item => Assert.Equal(isActive, item.IsActive));
    }

    [Fact]
    public async Task GetExaminationServiceListAsync_Pagination_ComputesPagesAndSlicesCorrectly()
    {
        var request = new GetExaminationServiceListAdminRequest { Page = 2, PageSize = 2 };

        var result = await _sut.GetExaminationServiceListAsync(request);

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

        var result = await _sut.GetExaminationServiceListAsync(request);

        var item = Assert.Single(result.Data);
        Assert.Equal(0m, item.Price);
        Assert.Equal("0 đ", item.PriceFormatted);
    }

    [Fact]
    public async Task CreateExaminationServiceAsync_NullRequest_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _sut.CreateExaminationServiceAsync(null!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateExaminationServiceAsync_EmptyServiceCode_ThrowsArgumentException(string? code)
    {
        var request = new CreateExaminationServiceRequest(ServiceCode: code, Name: "Dịch vụ mẫu");
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.CreateExaminationServiceAsync(request));

        Assert.Contains("Mã dịch vụ khám không được để trống", ex.Message);
    }

    [Fact]
    public async Task CreateExaminationServiceAsync_ServiceCodeTooShort_ThrowsArgumentException()
    {
        var request = new CreateExaminationServiceRequest(ServiceCode: "A", Name: "Dịch vụ mẫu");
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.CreateExaminationServiceAsync(request));

        Assert.Contains("ít nhất 2 ký tự", ex.Message);
    }

    [Fact]
    public async Task CreateExaminationServiceAsync_ServiceCodeTooLong_ThrowsArgumentException()
    {
        var longCode = new string('X', 41);
        var request = new CreateExaminationServiceRequest(ServiceCode: longCode, Name: "Dịch vụ mẫu");
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.CreateExaminationServiceAsync(request));

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
            _sut.CreateExaminationServiceAsync(request));

        Assert.Contains("Tên dịch vụ khám không được để trống", ex.Message);
    }

    [Fact]
    public async Task CreateExaminationServiceAsync_NameTooShort_ThrowsArgumentException()
    {
        var request = new CreateExaminationServiceRequest(ServiceCode: "EX-TEST", Name: "AB");
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.CreateExaminationServiceAsync(request));

        Assert.Contains("ít nhất 3 ký tự", ex.Message);
    }

    [Fact]
    public async Task CreateExaminationServiceAsync_NameTooLong_ThrowsArgumentException()
    {
        var longName = new string('A', 201);
        var request = new CreateExaminationServiceRequest(ServiceCode: "EX-TEST", Name: longName);
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.CreateExaminationServiceAsync(request));

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
            _sut.CreateExaminationServiceAsync(request));

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
            _sut.CreateExaminationServiceAsync(request));

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
            _sut.CreateExaminationServiceAsync(request));

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
            _sut.CreateExaminationServiceAsync(request));

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
            _sut.CreateExaminationServiceAsync(request));

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
            _sut.CreateExaminationServiceAsync(request));

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

        var result = await _sut.CreateExaminationServiceAsync(request);

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
        Assert.Equal("MedFlow Cơ sở 1", result.Facility);

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

        var result = await _sut.CreateExaminationServiceAsync(request);

        Assert.NotNull(result);
        Assert.Equal("CLS-XRAY-01", result.ServiceCode);
        Assert.Equal("Cận lâm sàng kỹ thuật", result.Category);
        Assert.Equal("DIAGNOSTIC", result.ServiceType);
        Assert.Equal(2, result.DepartmentId);
        Assert.Equal("Khoa Tim mạch", result.DepartmentName);
        Assert.False(result.AllowsOnlineBooking);
        Assert.Equal("COUNTER_ONLY", result.BookingChannel);
        Assert.True(result.RequiresResultVerification);
        Assert.Equal("MedFlow Cơ sở 2", result.Facility);
    }

    [Fact]
    public async Task CreateExaminationServiceAsync_SuspendedStatus_CreatesInactiveService()
    {
        var request = new CreateExaminationServiceRequest(
            ServiceCode: "EX-SUSP-01",
            Name: "Dịch vụ tạm ngưng ngay khi tạo",
            Status: "SUSPENDED"
        );

        var result = await _sut.CreateExaminationServiceAsync(request);

        Assert.NotNull(result);
        Assert.False(result.IsActive);
        Assert.Equal("SUSPENDED", result.Status);
    }

    [Fact]
    public async Task CreateExaminationServiceAsync_WithoutPrice_DefaultsToZeroPrice()
    {
        var request = new CreateExaminationServiceRequest(
            ServiceCode: "EX-FREE-01",
            Name: "Dịch vụ miễn phí tư vấn"
        );

        var result = await _sut.CreateExaminationServiceAsync(request);

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
            _sut.UpdateExaminationServiceAsync(id, request));

        Assert.Contains("ID dịch vụ khám phải lớn hơn 0", ex.Message);
    }

    [Fact]
    public async Task UpdateExaminationServiceAsync_NullRequest_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _sut.UpdateExaminationServiceAsync(1, null!));
    }

    [Fact]
    public async Task UpdateExaminationServiceAsync_NotFoundId_ThrowsKeyNotFoundException()
    {
        var request = new UpdateExaminationServiceRequest(Name: "Tên mới");
        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _sut.UpdateExaminationServiceAsync(9999, request));

        Assert.Contains("Không tìm thấy dịch vụ khám", ex.Message);
    }

    [Fact]
    public async Task UpdateExaminationServiceAsync_ServiceCodeTooShort_ThrowsArgumentException()
    {
        var request = new UpdateExaminationServiceRequest(ServiceCode: "A");
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.UpdateExaminationServiceAsync(1, request));

        Assert.Contains("ít nhất 2 ký tự", ex.Message);
    }

    [Fact]
    public async Task UpdateExaminationServiceAsync_ServiceCodeTooLong_ThrowsArgumentException()
    {
        var longCode = new string('A', 41);
        var request = new UpdateExaminationServiceRequest(ServiceCode: longCode);
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.UpdateExaminationServiceAsync(1, request));

        Assert.Contains("vượt quá 40 ký tự", ex.Message);
    }

    [Fact]
    public async Task UpdateExaminationServiceAsync_DuplicateServiceCode_ThrowsInvalidOperationException()
    {
        // Try updating service 2's code to "KB-01" which is already used by service 1
        var request = new UpdateExaminationServiceRequest(ServiceCode: "kb-01");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.UpdateExaminationServiceAsync(2, request));

        Assert.Contains("đã tồn tại trong cơ sở y tế", ex.Message);
    }

    [Fact]
    public async Task UpdateExaminationServiceAsync_KeepExistingServiceCode_DoesNotThrowDuplicateConflict()
    {
        // Updating service 1 keeping its own code "KB-01"
        var request = new UpdateExaminationServiceRequest(ServiceCode: "KB-01", Name: "Khám Nội tổng quát Đã Cập Nhật");
        var result = await _sut.UpdateExaminationServiceAsync(1, request);

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
            _sut.UpdateExaminationServiceAsync(1, request));

        Assert.Contains("Tên dịch vụ khám không được để trống", ex.Message);
    }

    [Fact]
    public async Task UpdateExaminationServiceAsync_NameTooShort_ThrowsArgumentException()
    {
        var request = new UpdateExaminationServiceRequest(Name: "AB");
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.UpdateExaminationServiceAsync(1, request));

        Assert.Contains("ít nhất 3 ký tự", ex.Message);
    }

    [Fact]
    public async Task UpdateExaminationServiceAsync_NameTooLong_ThrowsArgumentException()
    {
        var longName = new string('A', 201);
        var request = new UpdateExaminationServiceRequest(Name: longName);
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.UpdateExaminationServiceAsync(1, request));

        Assert.Contains("vượt quá 200 ký tự", ex.Message);
    }

    [Fact]
    public async Task UpdateExaminationServiceAsync_InvalidCategory_ThrowsArgumentException()
    {
        var request = new UpdateExaminationServiceRequest(Category: "Không hợp lệ");
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.UpdateExaminationServiceAsync(1, request));

        Assert.Contains("Phân loại dịch vụ không hợp lệ", ex.Message);
    }

    [Fact]
    public async Task UpdateExaminationServiceAsync_NonExistentDepartmentId_ThrowsKeyNotFoundException()
    {
        var request = new UpdateExaminationServiceRequest(DepartmentId: 8888);
        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _sut.UpdateExaminationServiceAsync(1, request));

        Assert.Contains("Không tìm thấy khoa phòng", ex.Message);
    }

    [Fact]
    public async Task UpdateExaminationServiceAsync_NonExistentDepartmentName_ThrowsKeyNotFoundException()
    {
        var request = new UpdateExaminationServiceRequest(Department: "Khoa Giả Định Không Tồn Tại");
        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _sut.UpdateExaminationServiceAsync(1, request));

        Assert.Contains("Không tìm thấy khoa phòng", ex.Message);
    }

    [Fact]
    public async Task UpdateExaminationServiceAsync_NonExistentSpecialtyId_ThrowsKeyNotFoundException()
    {
        var request = new UpdateExaminationServiceRequest(SpecialtyId: 8888);
        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _sut.UpdateExaminationServiceAsync(1, request));

        Assert.Contains("Không tìm thấy chuyên khoa", ex.Message);
    }

    [Fact]
    public async Task UpdateExaminationServiceAsync_NegativePrice_ThrowsArgumentException()
    {
        var request = new UpdateExaminationServiceRequest(Price: -50000m);
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.UpdateExaminationServiceAsync(1, request));

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

        var result = await _sut.UpdateExaminationServiceAsync(1, request);

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

        var result = await _sut.UpdateExaminationServiceAsync(1, request);

        Assert.NotNull(result);
        Assert.Equal("Cận lâm sàng kỹ thuật", result.Category);
        Assert.Equal("DIAGNOSTIC", result.ServiceType);
        Assert.Equal(1, result.DepartmentId);
        Assert.Equal("Khoa Khám bệnh", result.DepartmentName);
    }
}
