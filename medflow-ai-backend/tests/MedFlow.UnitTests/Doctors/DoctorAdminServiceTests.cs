using MedFlow.Application.DTOs.Doctors;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Doctors;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
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
        public DbSet<Role> Roles => Set<Role>();
        public DbSet<Permission> Permissions => throw NotUsed();
        public DbSet<UserRole> UserRoles => Set<UserRole>();
        public DbSet<RolePermission> RolePermissions => throw NotUsed();
        public DbSet<Patient> Patients => throw NotUsed();
        public DbSet<Staff> Staff => Set<Staff>();
        public DbSet<Doctor> Doctors => Set<Doctor>();
        public DbSet<DoctorServiceAuthorization> DoctorServiceAuthorizations => throw NotUsed();
        public DbSet<ExaminationSlot> ExaminationSlots => Set<ExaminationSlot>();
        public DbSet<AuthSession> AuthSessions => throw NotUsed();
        public DbSet<AuthVerificationChallenge> AuthVerificationChallenges => throw NotUsed();
        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
        public DbSet<Hospital> Hospitals => Set<Hospital>();
        public DbSet<Department> Departments => Set<Department>();
        public DbSet<HospitalRoom> HospitalRooms => throw NotUsed();
        public DbSet<Specialty> Specialties => Set<Specialty>();
        public DbSet<Service> Services => throw NotUsed();
        public DbSet<ServicePrice> ServicePrices => throw NotUsed();
        public DbSet<HospitalPolicy> HospitalPolicies => throw NotUsed();
        public DbSet<Appointment> Appointments => Set<Appointment>();
        public DbSet<PatientFamilyLink> PatientFamilyLinks => throw NotUsed();
        public DbSet<Visit> Visits => Set<Visit>();
        public DbSet<QueueEntry> QueueEntries => throw NotUsed();
        public DbSet<Payment> Payments => throw NotUsed();

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default)
            => Database.BeginTransactionAsync(ct);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Ignore<Permission>();
            modelBuilder.Ignore<RolePermission>();
            modelBuilder.Ignore<Patient>();
            modelBuilder.Ignore<AuthSession>();
            modelBuilder.Ignore<AuthVerificationChallenge>();
            modelBuilder.Ignore<HospitalRoom>();
            modelBuilder.Ignore<Service>();
            modelBuilder.Ignore<ServicePrice>();
            modelBuilder.Ignore<HospitalPolicy>();
            modelBuilder.Ignore<DoctorServiceAuthorization>();
            modelBuilder.Ignore<PatientFamilyLink>();
            modelBuilder.Ignore<QueueEntry>();
            modelBuilder.Ignore<Payment>();

            modelBuilder.Entity<User>().HasKey(u => u.Id);
            modelBuilder.Entity<Role>().HasKey(r => r.Id);
            modelBuilder.Entity<UserRole>().HasKey(ur => new { ur.UserId, ur.RoleId });
            modelBuilder.Entity<Hospital>().HasKey(h => h.Id);
            modelBuilder.Entity<Department>().HasKey(d => d.Id);
            modelBuilder.Entity<Specialty>().HasKey(s => s.Id);
            modelBuilder.Entity<ExaminationSlot>().HasKey(s => s.Id);
            modelBuilder.Entity<Appointment>().HasKey(a => a.Id);
            modelBuilder.Entity<Visit>().HasKey(v => v.Id);
            modelBuilder.Entity<AuditLog>().HasKey(a => a.Id);
            modelBuilder.Entity<AuditLog>().Ignore(a => a.IpAddress);
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
        var hosp1 = new Hospital { Id = 1, HospitalCode = "MEDFLOW-01", Name = "Bệnh viện MedFlow Cơ sở 1", IsActive = true };
        var hosp2 = new Hospital { Id = 2, HospitalCode = "MEDFLOW-02", Name = "Bệnh viện MedFlow Cơ sở 2", IsActive = true };

        var roleSysAdmin = new Role { Id = 1, Code = "SYSTEM_ADMIN", Name = "Quản trị hệ thống" };
        var roleHospAdmin = new Role { Id = 2, Code = "HOSPITAL_ADMIN", Name = "Quản trị bệnh viện" };
        var roleDoctor = new Role { Id = 3, Code = "DOCTOR", Name = "Bác sĩ" };

        var dept1 = new Department { Id = 1, HospitalId = 1, DepartmentCode = "KNT", Name = "Khoa Nội Tiêu hóa", IsActive = true };
        var dept2 = new Department { Id = 2, HospitalId = 1, DepartmentCode = "KTM", Name = "Khoa Tim Mạch", IsActive = true };
        var dept3Inactive = new Department { Id = 3, HospitalId = 1, DepartmentCode = "KCLS", Name = "Khoa Chẩn đoán Hình ảnh", IsActive = false };
        var dept4Hosp2 = new Department { Id = 4, HospitalId = 2, DepartmentCode = "KNHI", Name = "Khoa Nhi Cơ sở 2", IsActive = true };

        var spec1 = new Specialty { Id = 1, SpecialtyCode = "TH", Name = "Tiêu hóa - Gan Mật", IsActive = true };
        var spec2 = new Specialty { Id = 2, SpecialtyCode = "TM", Name = "Tim mạch can thiệp", IsActive = true };
        var spec3Inactive = new Specialty { Id = 3, SpecialtyCode = "DL", Name = "Da liễu", IsActive = false };

        var user1 = new User { Id = 1, Email = "minh.lh@medflow.vn", AccountStatus = AccountStatus.ACTIVE };
        var user2 = new User { Id = 2, Email = "lan.nt@medflow.vn", AccountStatus = AccountStatus.ACTIVE };
        var user3 = new User { Id = 3, Email = "hung.tran@medflow.vn", AccountStatus = AccountStatus.ACTIVE };
        var userHospAdmin1 = new User { Id = 11, Email = "hospital_admin01@medflow.vn", AccountStatus = AccountStatus.ACTIVE };
        var userHospAdmin2 = new User { Id = 12, Email = "hospital_admin02@medflow.vn", AccountStatus = AccountStatus.ACTIVE };
        var userSysAdmin = new User { Id = 13, Email = "system_admin01@medflow.vn", AccountStatus = AccountStatus.ACTIVE };
        var user4 = new User { Id = 4, Email = "duy.pq@medflow.vn", AccountStatus = AccountStatus.ACTIVE };

        var staff1 = new Staff { Id = 1, UserId = 1, HospitalId = 1, DepartmentId = 1, StaffCode = "NV-1001", FullName = "BS. Lê Hoàng Minh", Status = "ACTIVE" };
        var staff2 = new Staff { Id = 2, UserId = 2, HospitalId = 1, DepartmentId = 1, StaffCode = "NV-1002", FullName = "ThS. Nguyễn Thị Lan", Status = "ACTIVE" };
        var staff3 = new Staff { Id = 3, UserId = 3, HospitalId = 1, DepartmentId = 2, StaffCode = "NV-1003", FullName = "PGS.TS Trần Văn Hùng", Status = "ACTIVE" };
        var staff4 = new Staff { Id = 4, UserId = 4, HospitalId = 1, DepartmentId = 1, StaffCode = "NV-1004", FullName = "BS. Phạm Quang Duy", Status = "ACTIVE" };
        var staff5 = new Staff { Id = 5, HospitalId = 1, DepartmentId = 1, StaffCode = "NV-1005", FullName = "BS. Tạm Ngừng", Status = "INACTIVE" };
        var staffAdmin1 = new Staff { Id = 11, UserId = 11, HospitalId = 1, DepartmentId = 1, StaffCode = "NV-ADM-01", FullName = "Quản trị BV 1", Status = "ACTIVE" };
        var staffAdmin2 = new Staff { Id = 12, UserId = 12, HospitalId = 2, DepartmentId = 4, StaffCode = "NV-ADM-02", FullName = "Quản trị BV 2", Status = "ACTIVE" };

        var doctor1 = new Doctor
        {
            Id = 1,
            StaffId = 1,
            HospitalId = 1,
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
            HospitalId = 1,
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
            HospitalId = 1,
            DoctorCode = "DOC-0003",
            SpecialtyId = 2,
            ProfessionalTitle = "Phó Giáo sư",
            IsActive = true,
            LicenseNumber = "001122/BYT-CCHN",
            YearsOfExperience = 25,
            ProfileImageUrl = "avatar3.png"
        };

        _db.Hospitals.AddRange(hosp1, hosp2);
        _db.Roles.AddRange(roleSysAdmin, roleHospAdmin, roleDoctor);
        _db.Departments.AddRange(dept1, dept2, dept3Inactive, dept4Hosp2);
        _db.Specialties.AddRange(spec1, spec2, spec3Inactive);
        _db.Users.AddRange(user1, user2, user3, user4, userHospAdmin1, userHospAdmin2, userSysAdmin);
        _db.UserRoles.AddRange(
            new UserRole { UserId = 1, RoleId = 3 },
            new UserRole { UserId = 2, RoleId = 3 },
            new UserRole { UserId = 3, RoleId = 3 },
            new UserRole { UserId = 11, RoleId = 2 },
            new UserRole { UserId = 12, RoleId = 2 },
            new UserRole { UserId = 13, RoleId = 1 });
        _db.Staff.AddRange(staff1, staff2, staff3, staff4, staff5, staffAdmin1, staffAdmin2);
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
    [InlineData("  nv-1002  ", "DOC-0002")]
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

    [Theory]
    [InlineData(0, 0, 1, 10)]
    [InlineData(-5, 101, 1, 100)]
    [InlineData(1, 100, 1, 100)]
    public async Task GetDoctorList_OutOfRangePaging_ClampsPageAndPageSize(
        int page,
        int pageSize,
        int expectedPage,
        int expectedPageSize)
    {
        var result = await _service.GetDoctorListAsync(
            new GetDoctorListAdminRequest(Page: page, PageSize: pageSize));

        Assert.Equal(expectedPage, result.Page);
        Assert.Equal(expectedPageSize, result.PageSize);
        Assert.Equal(3, result.Total);
    }

    [Fact]
    public async Task GetDoctorList_NoMatch_ReturnsZeroTotalPages()
    {
        var result = await _service.GetDoctorListAsync(
            new GetDoctorListAdminRequest(Keyword: "khong-ton-tai"));

        Assert.Equal(0, result.Total);
        Assert.Equal(0, result.TotalPages);
        Assert.Empty(result.Data);
        Assert.Equal(1, result.Page);
    }

    [Fact]
    public async Task GetDoctorList_PageBeyondLast_ReturnsEmptyDataWithTotals()
    {
        var result = await _service.GetDoctorListAsync(
            new GetDoctorListAdminRequest(Page: 5, PageSize: 2));

        Assert.Empty(result.Data);
        Assert.Equal(3, result.Total);
        Assert.Equal(2, result.TotalPages);
        Assert.Equal(5, result.Page);
    }

    [Fact]
    public async Task GetDoctorList_CombinedFilters_AppliesAllConditions()
    {
        var activeInSpec1 = await _service.GetDoctorListAsync(
            new GetDoctorListAdminRequest(SpecialtyId: 1, IsActive: true));

        Assert.Single(activeInSpec1.Data);
        Assert.Equal("DOC-0001", activeInSpec1.Data[0].DoctorCode);

        var bytInDept2 = await _service.GetDoctorListAsync(
            new GetDoctorListAdminRequest(Keyword: "BYT", DepartmentId: 2));

        Assert.Single(bytInDept2.Data);
        Assert.Equal("DOC-0003", bytInDept2.Data[0].DoctorCode);
    }

    [Fact]
    public async Task CreateDoctor_NullRequest_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.CreateDoctorAsync(null!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateDoctor_EmptyDoctorCode_ThrowsArgumentException(string? doctorCode)
    {
        var request = new CreateDoctorRequest(DoctorCode: doctorCode, StaffId: 4);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateDoctorAsync(request));
        Assert.Equal("DoctorCode", ex.ParamName);
    }

    [Fact]
    public async Task CreateDoctor_DoctorCodeTooShortOrTooLong_ThrowsArgumentException()
    {
        var tooShort = new CreateDoctorRequest(DoctorCode: "A", StaffId: 4);
        var exShort = await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateDoctorAsync(tooShort));
        Assert.Equal("DoctorCode", exShort.ParamName);

        var tooLong = new CreateDoctorRequest(DoctorCode: new string('X', 41), StaffId: 4);
        var exLong = await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateDoctorAsync(tooLong));
        Assert.Equal("DoctorCode", exLong.ParamName);
    }

    [Fact]
    public async Task CreateDoctor_MissingStaffIdAndStaffCode_ThrowsArgumentException()
    {
        var request = new CreateDoctorRequest(DoctorCode: "DOC-0099");

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateDoctorAsync(request));
        Assert.Equal("StaffId", ex.ParamName);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public async Task CreateDoctor_InvalidStaffId_ThrowsArgumentException(long staffId)
    {
        var request = new CreateDoctorRequest(DoctorCode: "DOC-0099", StaffId: staffId);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateDoctorAsync(request));
        Assert.Equal("StaffId", ex.ParamName);
    }

    [Fact]
    public async Task CreateDoctor_NonExistentStaff_ThrowsKeyNotFoundException()
    {
        var byId = new CreateDoctorRequest(DoctorCode: "DOC-0099", StaffId: 9999);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.CreateDoctorAsync(byId));

        var byCode = new CreateDoctorRequest(DoctorCode: "DOC-0099", StaffCode: "NV-9999");
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.CreateDoctorAsync(byCode));
    }

    [Fact]
    public async Task CreateDoctor_InactiveStaff_ThrowsArgumentException()
    {
        var request = new CreateDoctorRequest(DoctorCode: "DOC-0099", StaffId: 5);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateDoctorAsync(request));
        Assert.Equal("StaffId", ex.ParamName);
    }

    [Fact]
    public async Task CreateDoctor_DuplicateDoctorCode_ThrowsInvalidOperationException()
    {
        var request = new CreateDoctorRequest(DoctorCode: "doc-0001", StaffId: 4);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CreateDoctorAsync(request));
    }

    [Fact]
    public async Task CreateDoctor_StaffAlreadyLinkedToDoctor_ThrowsInvalidOperationException()
    {
        var request = new CreateDoctorRequest(DoctorCode: "DOC-0099", StaffId: 1);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CreateDoctorAsync(request));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(71)]
    public async Task CreateDoctor_ExperienceOutOfRange_ThrowsArgumentException(short experience)
    {
        var request = new CreateDoctorRequest(
            DoctorCode: "DOC-0099",
            StaffId: 4,
            YearsOfExperience: experience);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateDoctorAsync(request));
        Assert.Equal("YearsOfExperience", ex.ParamName);
    }

    [Fact]
    public async Task CreateDoctor_FieldLengthsExceeded_ThrowsArgumentException()
    {
        var longTitle = new CreateDoctorRequest(
            DoctorCode: "DOC-0099",
            StaffId: 4,
            ProfessionalTitle: new string('T', 121));
        var exTitle = await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateDoctorAsync(longTitle));
        Assert.Equal("ProfessionalTitle", exTitle.ParamName);

        var longLicense = new CreateDoctorRequest(
            DoctorCode: "DOC-0099",
            StaffId: 4,
            LicenseNumber: new string('L', 101));
        var exLicense = await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateDoctorAsync(longLicense));
        Assert.Equal("LicenseNumber", exLicense.ParamName);

        var longBio = new CreateDoctorRequest(
            DoctorCode: "DOC-0099",
            StaffId: 4,
            ProfessionalBio: new string('B', 1001));
        var exBio = await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateDoctorAsync(longBio));
        Assert.Equal("ProfessionalBio", exBio.ParamName);

        var longQualifications = new CreateDoctorRequest(
            DoctorCode: "DOC-0099",
            StaffId: 4,
            QualificationsSummary: new string('Q', 2001));
        var exQual = await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateDoctorAsync(longQualifications));
        Assert.Equal("QualificationsSummary", exQual.ParamName);
    }

    [Fact]
    public async Task CreateDoctor_InvalidStatus_ThrowsArgumentException()
    {
        var request = new CreateDoctorRequest(
            DoctorCode: "DOC-0099",
            StaffId: 4,
            Status: "UNKNOWN");

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateDoctorAsync(request));
        Assert.Equal("Status", ex.ParamName);
    }

    [Fact]
    public async Task CreateDoctor_NonExistentDepartmentOrSpecialty_ThrowsKeyNotFoundException()
    {
        var missingDept = new CreateDoctorRequest(
            DoctorCode: "DOC-0099",
            StaffId: 4,
            DepartmentId: 999);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.CreateDoctorAsync(missingDept));

        var missingSpec = new CreateDoctorRequest(
            DoctorCode: "DOC-0099",
            StaffId: 4,
            SpecialtyId: 999);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.CreateDoctorAsync(missingSpec));
    }

    [Fact]
    public async Task CreateDoctor_InactiveDepartmentOrSpecialty_ThrowsArgumentException()
    {
        var inactiveDept = new CreateDoctorRequest(
            DoctorCode: "DOC-0099",
            StaffId: 4,
            DepartmentId: 3);
        var exDept = await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateDoctorAsync(inactiveDept));
        Assert.Equal("DepartmentId", exDept.ParamName);

        var inactiveSpec = new CreateDoctorRequest(
            DoctorCode: "DOC-0099",
            StaffId: 4,
            SpecialtyId: 3);
        var exSpec = await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateDoctorAsync(inactiveSpec));
        Assert.Equal("SpecialtyId", exSpec.ParamName);
    }

    [Fact]
    public async Task CreateDoctor_ValidCanonicalRequest_PersistsAndReturnsDto()
    {
        var request = new CreateDoctorRequest(
            DoctorCode: "  doc-0245  ",
            StaffId: 4,
            SpecialtyId: 1,
            ProfessionalTitle: "  Bác sĩ Chuyên khoa I  ",
            LicenseNumber: "  018492/BYT-CCHN  ",
            YearsOfExperience: 10,
            ProfessionalBio: "Chuyên gia nội tiêu hóa",
            QualificationsSummary: "Nội soi tiêu hóa can thiệp",
            ProfileImageUrl: "https://cdn.medflow.vn/doctors/doc-0245.png",
            Phone: "0901234567",
            IsActive: true);

        var result = await _service.CreateDoctorAsync(request);

        Assert.NotNull(result);
        Assert.True(result.Id > 0);
        Assert.Equal("DOC-0245", result.DoctorCode);
        Assert.Equal(4L, result.StaffId);
        Assert.Equal("NV-1004", result.StaffCode);
        Assert.Equal("BS. Phạm Quang Duy", result.FullName);
        Assert.Equal("duy.pq@medflow.vn", result.Email);
        Assert.Equal("0901234567", result.Phone);
        Assert.Equal("Bác sĩ Chuyên khoa I", result.Degree);
        Assert.Equal(1L, result.SpecialtyId);
        Assert.Equal("Tiêu hóa - Gan Mật", result.SpecialtyName);
        Assert.Equal(1L, result.DepartmentId);
        Assert.Equal("Khoa Nội Tiêu hóa", result.DepartmentName);
        Assert.Equal("018492/BYT-CCHN", result.LicenseNumber);
        Assert.Equal((short)10, result.YearsOfExperience);
        Assert.True(result.IsActive);
        Assert.Equal("https://cdn.medflow.vn/doctors/doc-0245.png", result.ProfileImageUrl);
        Assert.Equal("Chuyên gia nội tiêu hóa", result.ProfessionalBio);
        Assert.Equal("Nội soi tiêu hóa can thiệp", result.QualificationsSummary);

        var persisted = await _db.Doctors.FirstOrDefaultAsync(d => d.Id == result.Id);
        Assert.NotNull(persisted);
        Assert.Equal("DOC-0245", persisted.DoctorCode);
        Assert.Equal(4L, persisted.StaffId);
        Assert.Equal(1L, persisted.HospitalId);
    }

    [Fact]
    public async Task CreateDoctor_ValidAliasRequestWithStaffCodeAndDepartmentName_UpdatesStaffDepartmentAndReturnsDto()
    {
        var request = new CreateDoctorRequest(
            Code: "doc-0246",
            StaffCode: "nv-1004",
            Department: "Khoa Tim Mạch",
            Specialty: "Tim mạch can thiệp",
            Degree: "Thạc sĩ Y học",
            License: "029911/BYT-CCHN",
            Experience: 14,
            Biography: "Bác sĩ tim mạch can thiệp",
            Scope: "Đặt stent mạch vành",
            Portrait: "avatar-0246.png",
            Status: "SUSPENDED");

        var result = await _service.CreateDoctorAsync(request);

        Assert.NotNull(result);
        Assert.Equal("DOC-0246", result.DoctorCode);
        Assert.Equal(4L, result.StaffId);
        Assert.Equal("NV-1004", result.StaffCode);
        Assert.Equal(2L, result.DepartmentId);
        Assert.Equal("Khoa Tim Mạch", result.DepartmentName);
        Assert.Equal(2L, result.SpecialtyId);
        Assert.Equal("Tim mạch can thiệp", result.SpecialtyName);
        Assert.Equal("Thạc sĩ Y học", result.Degree);
        Assert.Equal("029911/BYT-CCHN", result.LicenseNumber);
        Assert.Equal((short)14, result.YearsOfExperience);
        Assert.False(result.IsActive);
        Assert.Equal("avatar-0246.png", result.ProfileImageUrl);
        Assert.Equal("Bác sĩ tim mạch can thiệp", result.ProfessionalBio);
        Assert.Equal("Đặt stent mạch vành", result.QualificationsSummary);

        var updatedStaff = await _db.Staff.FirstAsync(s => s.Id == 4);
        Assert.Equal(2L, updatedStaff.DepartmentId);
    }

    [Fact]
    public async Task UpdateDoctorAsync_ByNumericId_UpdatesSpecialtyDepartmentDegreeStatusAndLogsAudit()
    {
        var request = new UpdateDoctorAdminRequest(
            SpecialtyId: 2,
            DepartmentId: 2,
            ProfessionalTitle: "PGS.TS",
            IsActive: false,
            FullName: "PGS.TS Lê Hoàng Minh",
            LicenseNumber: "009999/BYT-CCHN",
            YearsOfExperience: 18,
            ProfileImageUrl: "https://cdn.medflow.vn/doctors/minh-new.png",
            ProfessionalBio: "Chuyên gia tim mạch can thiệp hơn 18 năm kinh nghiệm.",
            QualificationsSummary: "PGS.TS Đại học Y Dược",
            Reason: "Điều chuyển chuyên khoa và cập nhật học hàm");

        var result = await _service.UpdateDoctorAsync(
            actorUserId: 11,
            id: 1,
            request: request,
            ipAddress: "10.0.0.25");

        Assert.Equal(1L, result.Id);
        Assert.Equal("DOC-0001", result.DoctorCode);
        Assert.Equal(2L, result.SpecialtyId);
        Assert.Equal("Tim mạch can thiệp", result.SpecialtyName);
        Assert.Equal(2L, result.DepartmentId);
        Assert.Equal("Khoa Tim Mạch", result.DepartmentName);
        Assert.Equal("PGS.TS", result.Degree);
        Assert.False(result.IsActive);
        Assert.Equal("PGS.TS Lê Hoàng Minh", result.FullName);
        Assert.Equal("009999/BYT-CCHN", result.LicenseNumber);
        Assert.Equal((short)18, result.YearsOfExperience);
        Assert.Equal("https://cdn.medflow.vn/doctors/minh-new.png", result.ProfileImageUrl);
        Assert.Equal("Chuyên gia tim mạch can thiệp hơn 18 năm kinh nghiệm.", result.ProfessionalBio);
        Assert.Equal("PGS.TS Đại học Y Dược", result.QualificationsSummary);

        var audit = await _db.AuditLogs.SingleAsync(a => a.Action == "DOCTOR_UPDATED");
        Assert.Equal(11L, audit.ActorUserId);
        Assert.Equal(1L, audit.HospitalId);
        Assert.Equal("Doctor", audit.EntityType);
        Assert.Equal("1", audit.EntityId);
        Assert.Equal("Điều chuyển chuyên khoa và cập nhật học hàm", audit.Reason);
        Assert.Contains("DOC-0001", audit.ChangeData);
    }

    [Fact]
    public async Task UpdateDoctorAsync_ByDoctorCodeAndAliasFields_ResolvesByCodeOrNameAndUpdates()
    {
        var request = new UpdateDoctorAdminRequest(
            Specialty: "TM",
            Department: "Khoa Tim Mạch",
            Degree: "BS.CKII",
            Status: "ACTIVE",
            Name: "BS.CKII Nguyễn Thị Lan",
            License: "005923/BYT-CCHN",
            Biography: "Chuyên gia điều trị nội tim mạch.");

        var result = await _service.UpdateDoctorAsync(
            actorUserId: 13,
            idOrCode: "  doc-0002  ",
            request: request);

        Assert.Equal(2L, result.Id);
        Assert.Equal("DOC-0002", result.DoctorCode);
        Assert.Equal(2L, result.SpecialtyId);
        Assert.Equal("Tim mạch can thiệp", result.SpecialtyName);
        Assert.Equal(2L, result.DepartmentId);
        Assert.Equal("Khoa Tim Mạch", result.DepartmentName);
        Assert.Equal("BS.CKII", result.Degree);
        Assert.True(result.IsActive);
        Assert.Equal("BS.CKII Nguyễn Thị Lan", result.FullName);
        Assert.Equal("Chuyên gia điều trị nội tim mạch.", result.ProfessionalBio);
    }

    [Fact]
    public async Task UpdateDoctorAsync_CrossHospitalByHospitalAdmin_ThrowsForbiddenException()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.UpdateDoctorAsync(
                actorUserId: 12,
                id: 1,
                request: new UpdateDoctorAdminRequest(ProfessionalTitle: "TS.BS")));
    }

    [Fact]
    public async Task UpdateDoctorAsync_SystemAdminCanUpdateAcrossHospitals()
    {
        var doctor1 = await _db.Doctors.FirstAsync(d => d.Id == 1);
        doctor1.HospitalId = 2;
        await _db.SaveChangesAsync();

        var result = await _service.UpdateDoctorAsync(
            actorUserId: 13,
            id: 1,
            request: new UpdateDoctorAdminRequest(ProfessionalTitle: "GS.TS"));

        Assert.Equal("GS.TS", result.Degree);
    }

    [Fact]
    public async Task UpdateDoctorAsync_AuthNegativePaths_ThrowExpectedExceptions()
    {
        // Unknown actor user -> NotFoundException
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.UpdateDoctorAsync(999, 1, new UpdateDoctorAdminRequest(ProfessionalTitle: "BS.CKI")));

        // Non-admin role (DOCTOR) -> ForbiddenException
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.UpdateDoctorAsync(1, 1, new UpdateDoctorAdminRequest(ProfessionalTitle: "BS.CKI")));

        // Inactive admin user -> ForbiddenException
        _db.Users.Add(new User { Id = 20, Email = "inactive_admin@medflow.vn", AccountStatus = AccountStatus.INACTIVE });
        _db.UserRoles.Add(new UserRole { UserId = 20, RoleId = 2 });
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.UpdateDoctorAsync(20, 1, new UpdateDoctorAdminRequest(ProfessionalTitle: "BS.CKI")));

        // Temporarily locked admin user -> ForbiddenException
        _db.Users.Add(new User
        {
            Id = 21,
            Email = "locked_admin@medflow.vn",
            AccountStatus = AccountStatus.ACTIVE,
            LockedUntil = DateTime.UtcNow.AddMinutes(30)
        });
        _db.UserRoles.Add(new UserRole { UserId = 21, RoleId = 2 });
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.UpdateDoctorAsync(21, 1, new UpdateDoctorAdminRequest(ProfessionalTitle: "BS.CKI")));

        // Hospital admin without staff/hospital assignment -> ForbiddenException
        _db.Users.Add(new User { Id = 22, Email = "unassigned_admin@medflow.vn", AccountStatus = AccountStatus.ACTIVE });
        _db.UserRoles.Add(new UserRole { UserId = 22, RoleId = 2 });
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.UpdateDoctorAsync(22, 1, new UpdateDoctorAdminRequest(ProfessionalTitle: "BS.CKI")));

        // Admin with inactive staff profile -> ForbiddenException
        _db.Users.Add(new User { Id = 23, Email = "inactive_staff_admin@medflow.vn", AccountStatus = AccountStatus.ACTIVE });
        _db.UserRoles.Add(new UserRole { UserId = 23, RoleId = 2 });
        _db.Staff.Add(new Staff
        {
            Id = 23,
            UserId = 23,
            HospitalId = 1,
            StaffCode = "NV-ADM-23",
            FullName = "Admin Ngừng việc",
            Status = "INACTIVE"
        });
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.UpdateDoctorAsync(23, 1, new UpdateDoctorAdminRequest(ProfessionalTitle: "BS.CKI")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("DOC@INVALID!")]
    public async Task UpdateDoctorAsync_InvalidRouteIdentifier_ThrowsValidationException(string invalidIdOrCode)
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateDoctorAsync(11, invalidIdOrCode, new UpdateDoctorAdminRequest(ProfessionalTitle: "BS.CKI")));
    }

    [Fact]
    public async Task UpdateDoctorAsync_InvalidInputFields_ThrowsValidationException()
    {
        // Numeric overload id <= 0
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateDoctorAsync(11, 0L, new UpdateDoctorAdminRequest(ProfessionalTitle: "BS.CKI")));

        // Empty payload
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateDoctorAsync(11, 1L, new UpdateDoctorAdminRequest(Reason: "Chỉ có lý do")));

        // Invalid SpecialtyId / DepartmentId
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateDoctorAsync(11, 1L, new UpdateDoctorAdminRequest(SpecialtyId: 0)));
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateDoctorAsync(11, 1L, new UpdateDoctorAdminRequest(DepartmentId: -1)));
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateDoctorAsync(11, 1L, new UpdateDoctorAdminRequest(Specialty: "   ")));
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateDoctorAsync(11, 1L, new UpdateDoctorAdminRequest(Department: "   ")));

        // Blank or overly long ProfessionalTitle
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateDoctorAsync(11, 1L, new UpdateDoctorAdminRequest(ProfessionalTitle: "   ")));
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateDoctorAsync(11, 1L, new UpdateDoctorAdminRequest(ProfessionalTitle: new string('A', 121))));

        // Invalid Status or conflicting IsActive & Status
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateDoctorAsync(11, 1L, new UpdateDoctorAdminRequest(Status: "PENDING")));
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateDoctorAsync(11, 1L, new UpdateDoctorAdminRequest(IsActive: true, Status: "SUSPENDED")));

        // Out-of-range YearsOfExperience
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateDoctorAsync(11, 1L, new UpdateDoctorAdminRequest(YearsOfExperience: -1)));
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateDoctorAsync(11, 1L, new UpdateDoctorAdminRequest(YearsOfExperience: 71)));

        // Invalid FullName / LicenseNumber / Bio / Qualifications / Reason
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateDoctorAsync(11, 1L, new UpdateDoctorAdminRequest(FullName: "A")));
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateDoctorAsync(11, 1L, new UpdateDoctorAdminRequest(LicenseNumber: "AB")));
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateDoctorAsync(11, 1L, new UpdateDoctorAdminRequest(ProfessionalBio: new string('B', 1001))));
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateDoctorAsync(11, 1L, new UpdateDoctorAdminRequest(QualificationsSummary: new string('Q', 501))));
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateDoctorAsync(11, 1L, new UpdateDoctorAdminRequest(ProfileImageUrl: new string('U', 501))));
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateDoctorAsync(11, 1L, new UpdateDoctorAdminRequest(ProfessionalTitle: "BS", Reason: new string('R', 251))));
    }

    [Fact]
    public async Task UpdateDoctorAsync_CatalogAndLicenseConflicts_ThrowExpectedExceptions()
    {
        // Doctor not found -> NotFoundException
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.UpdateDoctorAsync(11, 999L, new UpdateDoctorAdminRequest(ProfessionalTitle: "BS.CKI")));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.UpdateDoctorAsync(11, "DOC-9999", new UpdateDoctorAdminRequest(ProfessionalTitle: "BS.CKI")));

        // Specialty not found -> NotFoundException
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.UpdateDoctorAsync(11, 1L, new UpdateDoctorAdminRequest(SpecialtyId: 999)));

        // Department not found -> NotFoundException
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.UpdateDoctorAsync(11, 1L, new UpdateDoctorAdminRequest(DepartmentId: 999)));

        // Inactive specialty -> ValidationException
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateDoctorAsync(11, 1L, new UpdateDoctorAdminRequest(SpecialtyId: 3)));

        // Inactive department -> ValidationException
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateDoctorAsync(11, 1L, new UpdateDoctorAdminRequest(DepartmentId: 3)));

        // Cross-hospital department -> ValidationException
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateDoctorAsync(11, 1L, new UpdateDoctorAdminRequest(DepartmentId: 4)));

        // Duplicate license number -> ConflictException
        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.UpdateDoctorAsync(11, 1L, new UpdateDoctorAdminRequest(LicenseNumber: "005923/BYT-CCHN")));
    }

    [Fact]
    public async Task UpdateDoctorAsync_DeactivatingWithActiveVisitOrUpcomingAppointment_ThrowsConflictException()
    {
        // 1. Active visit blocks deactivation
        var visit = new Visit
        {
            Id = 100,
            VisitCode = "VIS-100",
            AssignedDoctorId = 1,
            Status = "IN_PROGRESS"
        };
        _db.Visits.Add(visit);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.UpdateDoctorAsync(11, 1L, new UpdateDoctorAdminRequest(IsActive: false)));

        // Complete visit -> now test upcoming appointment blocking deactivation
        visit.Status = "COMPLETED";
        var futureSlot = new ExaminationSlot
        {
            Id = 200,
            DoctorId = 1,
            StartsAt = DateTime.UtcNow.AddDays(2),
            EndsAt = DateTime.UtcNow.AddDays(2).AddHours(1),
            Status = "OPEN"
        };
        var appointment = new Appointment
        {
            Id = 300,
            AppointmentCode = "APT-300",
            SlotId = 200,
            Status = "BOOKED"
        };
        _db.ExaminationSlots.Add(futureSlot);
        _db.Appointments.Add(appointment);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.UpdateDoctorAsync(11, 1L, new UpdateDoctorAdminRequest(Status: "SUSPENDED")));

        // Cancel appointment -> deactivation succeeds
        appointment.Status = "CANCELLED";
        await _db.SaveChangesAsync();

        var updated = await _service.UpdateDoctorAsync(11, 1L, new UpdateDoctorAdminRequest(Status: "SUSPENDED"));
        Assert.False(updated.IsActive);
    }

    [Fact]
    public async Task UpdateDoctorAsync_ActivatingDoctorWithInactiveStaff_ThrowsConflictException()
    {
        var staff2 = await _db.Staff.FirstAsync(s => s.Id == 2);
        staff2.Status = "INACTIVE";
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.UpdateDoctorAsync(11, 2L, new UpdateDoctorAdminRequest(IsActive: true)));
    }
}
