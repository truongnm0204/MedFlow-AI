using MedFlow.Application.DTOs.Doctors;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Doctors;
using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.UnitTests.Doctors;

public sealed class DoctorAdminServiceTests : IDisposable
{
    private sealed class DoctorAdminTestDbContext : DbContext, IApplicationDbContext
    {
        public DoctorAdminTestDbContext(DbContextOptions<DoctorAdminTestDbContext> options)
            : base(options)
        {
        }

        private static NotSupportedException NotUsed() => new("Not needed for DoctorAdminService unit tests.");

        public DbSet<User> Users => Set<User>();
        public DbSet<Role> Roles => throw NotUsed();
        public DbSet<Permission> Permissions => throw NotUsed();
        public DbSet<UserRole> UserRoles => throw NotUsed();
        public DbSet<RolePermission> RolePermissions => throw NotUsed();
        public DbSet<Patient> Patients => throw NotUsed();
        public DbSet<Staff> Staff => Set<Staff>();
        public DbSet<Doctor> Doctors => Set<Doctor>();
        public DbSet<AuthSession> AuthSessions => throw NotUsed();
        public DbSet<AuthVerificationChallenge> AuthVerificationChallenges => throw NotUsed();
        public DbSet<AuditLog> AuditLogs => throw NotUsed();
        public DbSet<Department> Departments => Set<Department>();
        public DbSet<Specialty> Specialties => Set<Specialty>();
        public DbSet<Service> Services => throw NotUsed();
        public DbSet<ServicePrice> ServicePrices => throw NotUsed();

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default)
            => Database.BeginTransactionAsync(ct);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Ignore<Role>();
            modelBuilder.Ignore<Permission>();
            modelBuilder.Ignore<UserRole>();
            modelBuilder.Ignore<RolePermission>();
            modelBuilder.Ignore<Patient>();
            modelBuilder.Ignore<AuthSession>();
            modelBuilder.Ignore<AuthVerificationChallenge>();
            modelBuilder.Ignore<AuditLog>();
            modelBuilder.Ignore<Service>();
            modelBuilder.Ignore<ServicePrice>();

            modelBuilder.Entity<User>().HasKey(u => u.Id);
            modelBuilder.Entity<Department>().HasKey(d => d.Id);
            modelBuilder.Entity<Specialty>().HasKey(s => s.Id);
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
        }
    }

    private readonly DoctorAdminTestDbContext _db;
    private readonly DoctorAdminService _service;

    public DoctorAdminServiceTests()
    {
        var options = new DbContextOptionsBuilder<DoctorAdminTestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new DoctorAdminTestDbContext(options);
        _service = new DoctorAdminService(_db);

        SeedTestData();
    }

    public void Dispose() => _db.Dispose();

    private void SeedTestData()
    {
        var dept1 = new Department { Id = 1, DepartmentCode = "KNT", Name = "Khoa Nội Tiêu hóa", IsActive = true };
        var dept2 = new Department { Id = 2, DepartmentCode = "KTM", Name = "Khoa Tim Mạch", IsActive = true };

        var spec1 = new Specialty { Id = 1, SpecialtyCode = "TH", Name = "Tiêu hóa - Gan Mật", IsActive = true };
        var spec2 = new Specialty { Id = 2, SpecialtyCode = "TM", Name = "Tim mạch can thiệp", IsActive = true };

        var user1 = new User { Id = 1, Email = "minh.lh@medflow.vn" };
        var user2 = new User { Id = 2, Email = "lan.nt@medflow.vn" };
        var user3 = new User { Id = 3, Email = "hung.tran@medflow.vn" };

        var staff1 = new Staff { Id = 1, UserId = 1, DepartmentId = 1, StaffCode = "NV-1001", FullName = "BS. Lê Hoàng Minh", Status = "ACTIVE" };
        var staff2 = new Staff { Id = 2, UserId = 2, DepartmentId = 1, StaffCode = "NV-1002", FullName = "ThS. Nguyễn Thị Lan", Status = "ACTIVE" };
        var staff3 = new Staff { Id = 3, UserId = 3, DepartmentId = 2, StaffCode = "NV-1003", FullName = "PGS.TS Trần Văn Hùng", Status = "ACTIVE" };

        var doctor1 = new Doctor
        {
            Id = 1,
            StaffId = 1,
            DoctorCode = "DOC-0001",
            SpecialtyId = 1,
            ProfessionalTitle = "Bác sĩ Chuyên khoa I",
            IsActive = true,
            LicenseNumber = "004812/BYT-CCHN",
            YearsOfExperience = 12,
            ProfileImageUrl = "avatar1.png"
        };

        var doctor2 = new Doctor
        {
            Id = 2,
            StaffId = 2,
            DoctorCode = "DOC-0002",
            SpecialtyId = 1,
            ProfessionalTitle = "Thạc sĩ Y học",
            IsActive = false, // Tạm ngừng
            LicenseNumber = "005923/BYT-CCHN",
            YearsOfExperience = 8,
            ProfileImageUrl = "avatar2.png"
        };

        var doctor3 = new Doctor
        {
            Id = 3,
            StaffId = 3,
            DoctorCode = "DOC-0003",
            SpecialtyId = 2,
            ProfessionalTitle = "Phó Giáo sư",
            IsActive = true,
            LicenseNumber = "001122/BYT-CCHN",
            YearsOfExperience = 25,
            ProfileImageUrl = "avatar3.png"
        };

        _db.Departments.AddRange(dept1, dept2);
        _db.Specialties.AddRange(spec1, spec2);
        _db.Users.AddRange(user1, user2, user3);
        _db.Staff.AddRange(staff1, staff2, staff3);
        _db.Doctors.AddRange(doctor1, doctor2, doctor3);
        _db.SaveChanges();
    }

    [Fact]
    public async Task GetDoctorList_AllDoctors_ReturnsFullPaginatedList()
    {
        // Act
        var result = await _service.GetDoctorListAsync(new GetDoctorListAdminRequest());

        // Assert
        Assert.NotNull(result);
        Assert.Equal(3, result.Total);
        Assert.Equal(3, result.Data.Count);
        Assert.Equal(1, result.Page);
        Assert.Equal(1, result.TotalPages);

        var first = result.Data.First(d => d.DoctorCode == "DOC-0001");
        Assert.Equal("BS. Lê Hoàng Minh", first.FullName);
        Assert.Equal("minh.lh@medflow.vn", first.Email);
        Assert.Equal("Khoa Nội Tiêu hóa", first.DepartmentName);
        Assert.Equal("Tiêu hóa - Gan Mật", first.SpecialtyName);
        Assert.Equal("Bác sĩ Chuyên khoa I", first.Degree);
        Assert.True(first.IsActive);
    }

    [Theory]
    [InlineData("minh", "DOC-0001")]
    [InlineData("005923", "DOC-0002")]
    [InlineData("DOC-0003", "DOC-0003")]
    [InlineData("Tim mạch", "DOC-0003")]
    public async Task GetDoctorList_FilterByKeyword_ReturnsMatchingDoctors(string keyword, string expectedDoctorCode)
    {
        // Act
        var result = await _service.GetDoctorListAsync(new GetDoctorListAdminRequest(Keyword: keyword));

        // Assert
        Assert.Single(result.Data);
        Assert.Equal(expectedDoctorCode, result.Data[0].DoctorCode);
    }

    [Fact]
    public async Task GetDoctorList_FilterByDepartmentId_ReturnsOnlyDepartmentDoctors()
    {
        // Act: Filter by Khoa Tim Mạch (Id = 2)
        var result = await _service.GetDoctorListAsync(new GetDoctorListAdminRequest(DepartmentId: 2));

        // Assert
        Assert.Single(result.Data);
        Assert.Equal("DOC-0003", result.Data[0].DoctorCode);
        Assert.Equal("Khoa Tim Mạch", result.Data[0].DepartmentName);
    }

    [Fact]
    public async Task GetDoctorList_FilterBySpecialtyId_ReturnsOnlySpecialtyDoctors()
    {
        // Act: Filter by Tiêu hóa - Gan Mật (Id = 1)
        var result = await _service.GetDoctorListAsync(new GetDoctorListAdminRequest(SpecialtyId: 1));

        // Assert
        Assert.Equal(2, result.Total);
        Assert.All(result.Data, d => Assert.Equal("Tiêu hóa - Gan Mật", d.SpecialtyName));
    }

    [Fact]
    public async Task GetDoctorList_FilterByIsActive_ReturnsMatchingActiveStatus()
    {
        // Act: Lọc bác sĩ tạm ngừng nhận lịch (IsActive = false)
        var result = await _service.GetDoctorListAsync(new GetDoctorListAdminRequest(IsActive: false));

        // Assert
        Assert.Single(result.Data);
        Assert.Equal("DOC-0002", result.Data[0].DoctorCode);
        Assert.False(result.Data[0].IsActive);
    }

    [Fact]
    public async Task GetDoctorList_Pagination_ReturnsCorrectPageAndCount()
    {
        // Act: Page 1, PageSize 2
        var page1 = await _service.GetDoctorListAsync(new GetDoctorListAdminRequest(Page: 1, PageSize: 2));
        var page2 = await _service.GetDoctorListAsync(new GetDoctorListAdminRequest(Page: 2, PageSize: 2));

        // Assert
        Assert.Equal(3, page1.Total);
        Assert.Equal(2, page1.TotalPages);
        Assert.Equal(2, page1.Data.Count);

        Assert.Equal(3, page2.Total);
        Assert.Single(page2.Data);
        Assert.Equal("DOC-0003", page2.Data[0].DoctorCode);
    }

    [Fact]
    public async Task GetDoctorList_NullRequest_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.GetDoctorListAsync(null!));
    }
}
