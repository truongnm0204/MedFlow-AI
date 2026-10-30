using MedFlow.Application.DTOs.Services;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Services;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.UnitTests.Services;

public sealed class ExaminationServiceCatalogServiceTests : IDisposable
{
    private sealed class CatalogTestDbContext : DbContext, IApplicationDbContext
    {
        public CatalogTestDbContext(DbContextOptions<CatalogTestDbContext> options)
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
        public DbSet<AuthSession> AuthSessions => throw NotUsed();
        public DbSet<AuthVerificationChallenge> AuthVerificationChallenges => throw NotUsed();
        public DbSet<AuditLog> AuditLogs => throw NotUsed();

        public DbSet<Department> Departments => Set<Department>();
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
            modelBuilder.Ignore<Staff>();
            modelBuilder.Ignore<AuthSession>();
            modelBuilder.Ignore<AuthVerificationChallenge>();
            modelBuilder.Ignore<AuditLog>();

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
            => new("DbSet này không được dùng trong ExaminationServiceCatalogServiceTests.");
    }

    private readonly CatalogTestDbContext _db;
    private readonly ExaminationServiceCatalogService _sut;

    public ExaminationServiceCatalogServiceTests()
    {
        var options = new DbContextOptionsBuilder<CatalogTestDbContext>()
            .UseInMemoryDatabase($"catalog-{Guid.NewGuid():N}")
            .Options;

        _db = new CatalogTestDbContext(options);
        SeedCatalogData(_db);
        _sut = new ExaminationServiceCatalogService(_db);
    }

    public void Dispose() => _db.Dispose();

    private static void SeedCatalogData(CatalogTestDbContext db)
    {
        db.Departments.AddRange(
            new Department { Id = 1, HospitalId = 1, DepartmentCode = "OPD", Name = "Khoa Khám bệnh", IsActive = true },
            new Department { Id = 2, HospitalId = 1, DepartmentCode = "GI", Name = "Khoa Tiêu hóa", IsActive = true },
            new Department { Id = 3, HospitalId = 1, DepartmentCode = "CARD", Name = "Khoa Tim mạch", IsActive = true },
            new Department { Id = 4, HospitalId = 1, DepartmentCode = "DIA", Name = "Khoa Cận lâm sàng", IsActive = true });

        db.Specialties.AddRange(
            new Specialty { Id = 1, SpecialtyCode = "GI", Name = "Tiêu hóa", IsActive = true },
            new Specialty { Id = 2, SpecialtyCode = "CARD", Name = "Tim mạch", IsActive = true },
            new Specialty { Id = 5, SpecialtyCode = "GEN", Name = "Nội tổng quát", IsActive = true });

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
                Description = "Khám chuyên sâu dạ dày, đại tràng, trào ngược thực quản GERD",
                AllowsOnlineBooking = true,
                RequiresResultVerification = false,
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
                Description = "Tầm soát tăng huyết áp, thiếu máu cơ tim",
                AllowsOnlineBooking = true,
                RequiresResultVerification = false,
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
                Description = "Khám Tiêu hóa tái khám theo hẹn",
                AllowsOnlineBooking = true,
                RequiresResultVerification = false,
                IsActive = true,
            },
            new Service
            {
                Id = 4,
                HospitalId = 1,
                DepartmentId = 3,
                SpecialtyId = 2,
                ServiceCode = "EX-CARD-02",
                Name = "Khám Tim mạch tái khám",
                ServiceType = "EXAMINATION",
                Description = "Khám Tim mạch tái khám theo hẹn",
                AllowsOnlineBooking = true,
                RequiresResultVerification = false,
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
                Description = "Xét nghiệm công thức máu",
                AllowsOnlineBooking = false,
                RequiresResultVerification = true,
                IsActive = true,
            },
            new Service
            {
                Id = 6,
                HospitalId = 1,
                DepartmentId = 2,
                SpecialtyId = 1,
                ServiceCode = "EX-GI-INACTIVE",
                Name = "Dịch vụ ngưng hoạt động",
                ServiceType = "EXAMINATION",
                Description = "Không hiển thị",
                AllowsOnlineBooking = false,
                RequiresResultVerification = false,
                IsActive = false,
            });

        db.ServicePrices.AddRange(
            // Expired historical price for service 1 — must be ignored in favor of active price
            new ServicePrice
            {
                Id = 100,
                ServiceId = 1,
                PriceAmount = 150_000m,
                EffectiveFrom = new DateTime(2025, 1, 1),
                EffectiveTo = new DateTime(2025, 12, 31),
            },
            new ServicePrice
            {
                Id = 1,
                ServiceId = 1,
                PriceAmount = 200_000m,
                EffectiveFrom = new DateTime(2026, 1, 1),
                EffectiveTo = null,
            },
            new ServicePrice
            {
                Id = 2,
                ServiceId = 2,
                PriceAmount = 220_000m,
                EffectiveFrom = new DateTime(2026, 1, 1),
                EffectiveTo = null,
            },
            new ServicePrice
            {
                Id = 3,
                ServiceId = 3,
                PriceAmount = 170_000m,
                EffectiveFrom = new DateTime(2026, 1, 1),
                EffectiveTo = null,
            },
            new ServicePrice
            {
                Id = 4,
                ServiceId = 4,
                PriceAmount = 180_000m,
                EffectiveFrom = new DateTime(2026, 1, 1),
                EffectiveTo = null,
            });

        db.SaveChanges();
    }

    [Fact]
    public async Task SearchAsync_ReturnsOnlyActiveExaminationServices_WithActivePriceAndFormatting()
    {
        var result = await _sut.SearchAsync(new SearchExaminationServicesRequest());

        Assert.Equal(4, result.Total);
        Assert.Equal(1, result.Page);
        Assert.Equal(6, result.PageSize);
        Assert.Equal(1, result.TotalPages);
        Assert.Equal(["EX-GI-01", "EX-CARD-01", "EX-GI-02", "EX-CARD-02"], result.Data.Select(x => x.ServiceCode));

        var first = result.Data[0];
        Assert.Equal("1", first.Id);
        Assert.Equal("dept-th", first.DepartmentId);
        Assert.Equal("Khoa Tiêu hóa", first.DepartmentName);
        Assert.Equal(200_000m, first.Price);
        Assert.Equal("200.000 VNĐ", first.PriceFormatted);
        Assert.Equal(25, first.DurationMinutes);
        Assert.Equal("specialty", first.Category);
        Assert.Equal("online", first.BadgeType);
        Assert.Equal(4, first.StandardSteps.Count);
    }

    [Theory]
    [InlineData("GERD", new[] { "EX-GI-01" })]
    [InlineData("ex-card", new[] { "EX-CARD-01", "EX-CARD-02" })]
    [InlineData("Khoa Tiêu hóa", new[] { "EX-GI-01", "EX-GI-02" })]
    public async Task SearchAsync_FiltersByQueryAcrossNameCodeDepartmentAndDescription(
        string query,
        string[] expectedCodes)
    {
        var result = await _sut.SearchAsync(new SearchExaminationServicesRequest { Query = query });

        Assert.Equal(expectedCodes.Length, result.Total);
        Assert.Equal(expectedCodes, result.Data.Select(x => x.ServiceCode).ToArray());
    }

    [Theory]
    [InlineData("dept-th", new[] { "EX-GI-01", "EX-GI-02" })]
    [InlineData("GI", new[] { "EX-GI-01", "EX-GI-02" })]
    [InlineData("dept-tm", new[] { "EX-CARD-01", "EX-CARD-02" })]
    [InlineData("3", new[] { "EX-CARD-01", "EX-CARD-02" })]
    public async Task SearchAsync_FiltersByDepartmentAliasCodeOrId(
        string department,
        string[] expectedCodes)
    {
        var result = await _sut.SearchAsync(new SearchExaminationServicesRequest { Department = department });

        Assert.Equal(expectedCodes, result.Data.Select(x => x.ServiceCode).ToArray());
    }

    [Theory]
    [InlineData("under-200k", new[] { "EX-GI-01", "EX-GI-02", "EX-CARD-02" })]
    [InlineData("200k-250k", new[] { "EX-CARD-01" })]
    [InlineData("over-250k", new string[0])]
    [InlineData("duration-under-20", new[] { "EX-GI-02", "EX-CARD-02" })]
    [InlineData("duration-over-20", new[] { "EX-GI-01", "EX-CARD-01" })]
    public async Task SearchAsync_FiltersByPriceAndDurationRanges(
        string range,
        string[] expectedCodes)
    {
        var result = await _sut.SearchAsync(new SearchExaminationServicesRequest { PriceDurationRange = range });

        Assert.Equal(expectedCodes, result.Data.Select(x => x.ServiceCode).ToArray());
    }

    [Fact]
    public async Task SearchAsync_PaginatesResults_AndHandlesOutOfRangePage()
    {
        var page1 = await _sut.SearchAsync(new SearchExaminationServicesRequest { Page = 1, PageSize = 2 });
        Assert.Equal(4, page1.Total);
        Assert.Equal(2, page1.TotalPages);
        Assert.Equal(["EX-GI-01", "EX-CARD-01"], page1.Data.Select(x => x.ServiceCode));

        var page2 = await _sut.SearchAsync(new SearchExaminationServicesRequest { Page = 2, PageSize = 2 });
        Assert.Equal(["EX-GI-02", "EX-CARD-02"], page2.Data.Select(x => x.ServiceCode));

        var page5 = await _sut.SearchAsync(new SearchExaminationServicesRequest { Page = 5, PageSize = 2 });
        Assert.Empty(page5.Data);
        Assert.Equal(4, page5.Total);
        Assert.Equal(2, page5.TotalPages);
    }

    [Fact]
    public async Task SearchAsync_ThrowsValidationException_OnInvalidParameters()
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.SearchAsync(new SearchExaminationServicesRequest { Page = 0 }));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.SearchAsync(new SearchExaminationServicesRequest { PageSize = 51 }));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.SearchAsync(new SearchExaminationServicesRequest { Query = new string('a', 101) }));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.SearchAsync(new SearchExaminationServicesRequest { Category = "invalid-cat" }));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.SearchAsync(new SearchExaminationServicesRequest { PriceDurationRange = "invalid-range" }));
    }

    [Theory]
    [InlineData("1", "EX-GI-01")]
    [InlineData("ex-gi-01", "EX-GI-01")]
    [InlineData("srv-th-01", "EX-GI-01")]
    [InlineData("srv-tm-01", "EX-CARD-01")]
    public async Task GetByIdOrCodeAsync_ResolvesByNumericIdServiceCodeOrLegacySlug(
        string idOrCode,
        string expectedServiceCode)
    {
        var dto = await _sut.GetByIdOrCodeAsync(idOrCode);

        Assert.Equal(expectedServiceCode, dto.ServiceCode);
    }

    [Fact]
    public async Task GetByIdOrCodeAsync_ThrowsNotFoundException_WhenMissingOrDiagnostic()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetByIdOrCodeAsync("DIA-CBC"));
        await Assert.ThrowsAsync<NotFoundException>(() => _sut.GetByIdOrCodeAsync("999"));
    }
}
