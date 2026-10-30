using MedFlow.Application.DTOs.Departments;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Departments;
using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.UnitTests.Departments;

public sealed class DepartmentServiceTests : IDisposable
{
    private sealed class DepartmentTestDbContext : DbContext, IApplicationDbContext
    {
        public DepartmentTestDbContext(DbContextOptions<DepartmentTestDbContext> options)
            : base(options)
        {
        }

        private static NotSupportedException NotUsed() => new("Not needed for DepartmentService unit tests.");

        public DbSet<User> Users => throw NotUsed();
        public DbSet<Role> Roles => throw NotUsed();
        public DbSet<Permission> Permissions => throw NotUsed();
        public DbSet<UserRole> UserRoles => throw NotUsed();
        public DbSet<RolePermission> RolePermissions => throw NotUsed();
        public DbSet<Patient> Patients => throw NotUsed();
        public DbSet<Staff> Staff => throw NotUsed();
        public DbSet<AuthSession> AuthSessions => throw NotUsed();
        public DbSet<AuthVerificationChallenge> AuthVerificationChallenges => throw NotUsed();
        public DbSet<AuditLog> AuditLogs => throw NotUsed();
        public DbSet<Department> Departments => Set<Department>();
        public DbSet<HospitalRoom> HospitalRooms => throw NotUsed();
        public DbSet<Specialty> Specialties => throw NotUsed();
        public DbSet<Service> Services => throw NotUsed();
        public DbSet<ServicePrice> ServicePrices => throw NotUsed();

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
            modelBuilder.Ignore<AuthSession>();
            modelBuilder.Ignore<AuthVerificationChallenge>();
            modelBuilder.Ignore<AuditLog>();
            modelBuilder.Ignore<HospitalRoom>();
            modelBuilder.Ignore<Specialty>();
            modelBuilder.Ignore<Service>();
            modelBuilder.Ignore<ServicePrice>();

            modelBuilder.Entity<Department>().HasKey(d => d.Id);
        }
    }

    private readonly DepartmentTestDbContext _dbContext;
    private readonly DepartmentService _service;

    public DepartmentServiceTests()
    {
        var options = new DbContextOptionsBuilder<DepartmentTestDbContext>()
            .UseInMemoryDatabase(databaseName: $"DepartmentTests_{Guid.NewGuid()}")
            .Options;

        _dbContext = new DepartmentTestDbContext(options);
        _dbContext.Database.EnsureCreated();

        SeedTestData();

        _service = new DepartmentService(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Database.EnsureDeleted();
        _dbContext.Dispose();
    }

    private void SeedTestData()
    {
        _dbContext.Departments.AddRange(
            new Department { Id = 1, HospitalId = 1, DepartmentCode = "DEP-001", Name = "Khoa Nội Tiêu hóa", IsActive = true },
            new Department { Id = 2, HospitalId = 1, DepartmentCode = "DEP-002", Name = "Khoa Khám bệnh", IsActive = true },
            new Department { Id = 3, HospitalId = 1, DepartmentCode = "DEP-003", Name = "Khoa Nội tiết", IsActive = true },
            new Department { Id = 4, HospitalId = 1, DepartmentCode = "DEP-004", Name = "Khoa Chẩn đoán hình ảnh", IsActive = true },
            new Department { Id = 5, HospitalId = 1, DepartmentCode = "DEP-005", Name = "Khoa Xét nghiệm", IsActive = true },
            new Department { Id = 6, HospitalId = 1, DepartmentCode = "DEP-006", Name = "Khoa Tim mạch", IsActive = true },
            new Department { Id = 7, HospitalId = 1, DepartmentCode = "DEP-007", Name = "Khoa Nhi", IsActive = true },
            new Department { Id = 8, HospitalId = 1, DepartmentCode = "DEP-008", Name = "Khoa Sản", IsActive = true },
            new Department { Id = 9, HospitalId = 1, DepartmentCode = "DEP-009", Name = "Khoa Ngoại tổng hợp", IsActive = true },
            new Department { Id = 10, HospitalId = 1, DepartmentCode = "DEP-010", Name = "Khoa Tai Mũi Họng", IsActive = true },
            new Department { Id = 11, HospitalId = 1, DepartmentCode = "DEP-011", Name = "Khoa Răng Hàm Mặt", IsActive = true },
            new Department { Id = 12, HospitalId = 1, DepartmentCode = "DEP-012", Name = "Khoa Mắt", IsActive = true },
            new Department { Id = 13, HospitalId = 1, DepartmentCode = "DEP-013", Name = "Khoa Da liễu", IsActive = false },
            new Department { Id = 14, HospitalId = 2, DepartmentCode = "DEP-014", Name = "Khoa Y học cổ truyền", IsActive = false }
        );
        _dbContext.SaveChanges();
    }

    [Fact]
    public async Task GetDepartmentListAsync_ReturnsAllDepartmentsAndCorrectMetrics_WhenNoFilter()
    {
        var request = new GetDepartmentListRequest(Page: 1, PageSize: 10);

        var result = await _service.GetDepartmentListAsync(request);

        Assert.NotNull(result);
        Assert.Equal(14, result.Total);
        Assert.Equal(1, result.Page);
        Assert.Equal(10, result.PageSize);
        Assert.Equal(2, result.TotalPages);
        Assert.Equal(10, result.Data.Count);

        // Verify Metrics
        Assert.Equal(14, result.Metrics.Total);
        Assert.Equal(12, result.Metrics.Active);
        Assert.Equal(2, result.Metrics.Inactive);
        Assert.Equal(2, result.Metrics.Facilities);
    }

    [Fact]
    public async Task GetDepartmentListAsync_FiltersByKeyword_MatchesCodeOrName()
    {
        var requestByCode = new GetDepartmentListRequest(Keyword: "DEP-003");
        var resultByCode = await _service.GetDepartmentListAsync(requestByCode);

        Assert.Single(resultByCode.Data);
        Assert.Equal("DEP-003", resultByCode.Data[0].Code);
        Assert.Equal("Khoa Nội tiết", resultByCode.Data[0].Name);

        var requestByName = new GetDepartmentListRequest(Keyword: "tiêu hóa");
        var resultByName = await _service.GetDepartmentListAsync(requestByName);

        Assert.Single(resultByName.Data);
        Assert.Equal("Khoa Nội Tiêu hóa", resultByName.Data[0].Name);
    }

    [Fact]
    public async Task GetDepartmentListAsync_FiltersByIsActive_TrueAndFalse()
    {
        var activeRequest = new GetDepartmentListRequest(IsActive: true, PageSize: 20);
        var activeResult = await _service.GetDepartmentListAsync(activeRequest);

        Assert.Equal(12, activeResult.Total);
        Assert.All(activeResult.Data, item => Assert.True(item.IsActive));
        Assert.All(activeResult.Data, item => Assert.Equal("ACTIVE", item.Status));

        var inactiveRequest = new GetDepartmentListRequest(IsActive: false);
        var inactiveResult = await _service.GetDepartmentListAsync(inactiveRequest);

        Assert.Equal(2, inactiveResult.Total);
        Assert.All(inactiveResult.Data, item => Assert.False(item.IsActive));
        Assert.All(inactiveResult.Data, item => Assert.Equal("INACTIVE", item.Status));
    }

    [Fact]
    public async Task GetDepartmentListAsync_FiltersByStatus_ActiveAndInactive()
    {
        var statusActiveRequest = new GetDepartmentListRequest(Status: "ACTIVE", PageSize: 20);
        var statusActiveResult = await _service.GetDepartmentListAsync(statusActiveRequest);

        Assert.Equal(12, statusActiveResult.Total);

        var statusInactiveRequest = new GetDepartmentListRequest(Status: "INACTIVE");
        var statusInactiveResult = await _service.GetDepartmentListAsync(statusInactiveRequest);

        Assert.Equal(2, statusInactiveResult.Total);
    }

    [Fact]
    public async Task GetDepartmentListAsync_FiltersByHospitalId()
    {
        var requestHospital2 = new GetDepartmentListRequest(HospitalId: 2);
        var result = await _service.GetDepartmentListAsync(requestHospital2);

        Assert.Single(result.Data);
        Assert.Equal("DEP-014", result.Data[0].Code);
        Assert.Equal("MedFlow Cơ sở 2", result.Data[0].Facility);
    }

    [Fact]
    public async Task GetDepartmentListAsync_PaginatesToSecondPage()
    {
        var requestPage2 = new GetDepartmentListRequest(Page: 2, PageSize: 10);
        var result = await _service.GetDepartmentListAsync(requestPage2);

        Assert.Equal(2, result.Page);
        Assert.Equal(4, result.Data.Count);
        Assert.Equal("DEP-011", result.Data[0].Code);
        Assert.Equal("DEP-014", result.Data[3].Code);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(-1, 10)]
    public async Task GetDepartmentListAsync_InvalidPage_ThrowsArgumentException(int page, int pageSize)
    {
        var request = new GetDepartmentListRequest(Page: page, PageSize: pageSize);

        await Assert.ThrowsAsync<ArgumentException>(() => _service.GetDepartmentListAsync(request));
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(1, -5)]
    [InlineData(1, 101)]
    public async Task GetDepartmentListAsync_InvalidPageSize_ThrowsArgumentException(int page, int pageSize)
    {
        var request = new GetDepartmentListRequest(Page: page, PageSize: pageSize);

        await Assert.ThrowsAsync<ArgumentException>(() => _service.GetDepartmentListAsync(request));
    }

    [Fact]
    public async Task GetDepartmentListAsync_NoMatch_ReturnsEmptyDataAndCorrectTotal()
    {
        var request = new GetDepartmentListRequest(Keyword: "Không Tồn Tại 9999");
        var result = await _service.GetDepartmentListAsync(request);

        Assert.Empty(result.Data);
        Assert.Equal(0, result.Total);
        Assert.Equal(1, result.TotalPages);
        // Overall metrics are still calculated across system
        Assert.Equal(14, result.Metrics.Total);
    }
}

