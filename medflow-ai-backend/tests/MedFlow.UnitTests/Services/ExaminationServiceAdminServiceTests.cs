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
}
