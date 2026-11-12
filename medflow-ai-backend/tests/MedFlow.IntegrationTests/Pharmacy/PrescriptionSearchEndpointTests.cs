using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MedFlow.Application.DTOs.Pharmacy;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.IntegrationTests.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MedFlow.IntegrationTests.Pharmacy;

public sealed class PrescriptionSearchEndpointTests : IClassFixture<TestWebAppFactory>, IDisposable
{
    public sealed class IntegrationPharmacyDbContext : DbContext, IApplicationDbContext
    {
        public IntegrationPharmacyDbContext(DbContextOptions<IntegrationPharmacyDbContext> options)
            : base(options)
        {
        }

        private static NotSupportedException NotUsed() =>
            new("Not needed for PrescriptionSearchEndpointTests.");

        public DbSet<User> Users => Set<User>();
        public DbSet<Role> Roles => Set<Role>();
        public DbSet<Permission> Permissions => Set<Permission>();
        public DbSet<UserRole> UserRoles => Set<UserRole>();
        public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
        public DbSet<Patient> Patients => Set<Patient>();
        public DbSet<Staff> Staff => Set<Staff>();
        public DbSet<Doctor> Doctors => Set<Doctor>();
        public DbSet<DoctorServiceAuthorization> DoctorServiceAuthorizations => throw NotUsed();
        public DbSet<ExaminationSlot> ExaminationSlots => throw NotUsed();
        public DbSet<AuthSession> AuthSessions => throw NotUsed();
        public DbSet<AuthVerificationChallenge> AuthVerificationChallenges => throw NotUsed();
        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
        public DbSet<Hospital> Hospitals => Set<Hospital>();
        public DbSet<Department> Departments => Set<Department>();
        public DbSet<HospitalRoom> HospitalRooms => Set<HospitalRoom>();
        public DbSet<Specialty> Specialties => Set<Specialty>();
        public DbSet<Service> Services => throw NotUsed();
        public DbSet<ServicePrice> ServicePrices => throw NotUsed();
        public DbSet<HospitalPolicy> HospitalPolicies => throw NotUsed();
        public DbSet<Appointment> Appointments => throw NotUsed();
        public DbSet<PatientFamilyLink> PatientFamilyLinks => throw NotUsed();
        public DbSet<Visit> Visits => Set<Visit>();
        public DbSet<QueueEntry> QueueEntries => throw NotUsed();
        public DbSet<Payment> Payments => Set<Payment>();
        public DbSet<Medicine> Medicines => Set<Medicine>();
        public DbSet<MedicineBatch> MedicineBatches => Set<MedicineBatch>();
        public DbSet<Prescription> Prescriptions => Set<Prescription>();
        public DbSet<PrescriptionItem> PrescriptionItems => Set<PrescriptionItem>();
        public DbSet<MedicationDispense> MedicationDispenses => Set<MedicationDispense>();
        public DbSet<DispenseItem> DispenseItems => Set<DispenseItem>();
        public DbSet<StockTransaction> StockTransactions => Set<StockTransaction>();

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default)
            => Database.BeginTransactionAsync(ct);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Ignore<DoctorServiceAuthorization>();
            modelBuilder.Ignore<ExaminationSlot>();
            modelBuilder.Ignore<AuthSession>();
            modelBuilder.Ignore<AuthVerificationChallenge>();
            modelBuilder.Ignore<Service>();
            modelBuilder.Ignore<ServicePrice>();
            modelBuilder.Ignore<HospitalPolicy>();
            modelBuilder.Ignore<Appointment>();
            modelBuilder.Ignore<PatientFamilyLink>();
            modelBuilder.Ignore<QueueEntry>();

            modelBuilder.Entity<User>().HasKey(u => u.Id);
            modelBuilder.Entity<Role>().HasKey(r => r.Id);
            modelBuilder.Entity<Permission>().HasKey(p => p.Id);
            modelBuilder.Entity<UserRole>().HasKey(ur => new { ur.UserId, ur.RoleId });
            modelBuilder.Entity<RolePermission>().HasKey(rp => new { rp.RoleId, rp.PermissionId });
            modelBuilder.Entity<Patient>().HasKey(pt => pt.Id);
            modelBuilder.Entity<Staff>().HasKey(s => s.Id);
            modelBuilder.Entity<Hospital>().HasKey(h => h.Id);
            modelBuilder.Entity<Department>().HasKey(d => d.Id);
            modelBuilder.Entity<HospitalRoom>().HasKey(r => r.Id);
            modelBuilder.Entity<Specialty>().HasKey(sp => sp.Id);
            modelBuilder.Entity<Doctor>().HasKey(d => d.Id);
            modelBuilder.Entity<Doctor>()
                .HasOne(d => d.Staff)
                .WithOne(s => s.Doctor)
                .HasForeignKey<Doctor>(d => d.StaffId);
            modelBuilder.Entity<Doctor>()
                .HasOne(d => d.Specialty)
                .WithMany()
                .HasForeignKey(d => d.SpecialtyId);
            modelBuilder.Entity<Visit>().HasKey(v => v.Id);
            modelBuilder.Entity<Payment>().HasKey(p => p.Id);
            modelBuilder.Entity<Medicine>().HasKey(m => m.Id);
            modelBuilder.Entity<MedicineBatch>().HasKey(mb => mb.Id);
            modelBuilder.Entity<Prescription>().HasKey(pr => pr.Id);
            modelBuilder.Entity<PrescriptionItem>().HasKey(pi => pi.Id);
            modelBuilder.Entity<MedicationDispense>().HasKey(md => md.Id);
            modelBuilder.Entity<DispenseItem>().HasKey(di => di.Id);
            modelBuilder.Entity<StockTransaction>().HasKey(st => st.Id);

            modelBuilder.Entity<AuditLog>(b =>
            {
                b.HasKey(a => a.Id);
                b.Ignore(a => a.IpAddress);
            });
        }
    }

    private static readonly DateTime FixedNowUtc = new(2026, 11, 10, 10, 0, 0, DateTimeKind.Utc);
    private readonly WebApplicationFactory<Program> _customFactory;
    private readonly HttpClient _client;
    private readonly string _dbName;

    public PrescriptionSearchEndpointTests(TestWebAppFactory factory)
    {
        _dbName = $"PrescriptionSearchEndpointTests_{Guid.NewGuid()}";

        var inMemoryOptions = new DbContextOptionsBuilder<IntegrationPharmacyDbContext>()
            .UseInMemoryDatabase(_dbName)
            .Options;

        using (var seedCtx = new IntegrationPharmacyDbContext(inMemoryOptions))
        {
            SeedEndpointData(seedCtx);
        }

        _customFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IApplicationDbContext>();
                services.AddScoped<IApplicationDbContext>(_ => new IntegrationPharmacyDbContext(inMemoryOptions));
            });
        });

        _client = _customFactory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _customFactory.Dispose();
    }

    private string GenerateToken(long userId, string email, params string[] roles)
    {
        using var scope = _customFactory.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var user = new User { Id = userId, Email = email };
        return jwtService.GenerateAccessToken(user, roles).AccessToken;
    }

    private static void SeedEndpointData(IntegrationPharmacyDbContext db)
    {
        db.Hospitals.AddRange(
            new Hospital { Id = 1, HospitalCode = "HOS-001", Name = "Bệnh viện Đa khoa Quốc tế MedFlow", IsActive = true },
            new Hospital { Id = 2, HospitalCode = "HOS-002", Name = "Bệnh viện Chi nhánh 2", IsActive = true });

        db.Departments.AddRange(
            new Department { Id = 1, HospitalId = 1, DepartmentCode = "DEP-01", Name = "Khoa Khám bệnh", IsActive = true },
            new Department { Id = 2, HospitalId = 1, DepartmentCode = "DEP-PHA", Name = "Khoa Dược ngoại trú", IsActive = true },
            new Department { Id = 3, HospitalId = 2, DepartmentCode = "DEP-02", Name = "Khoa Khám bệnh Chi nhánh 2", IsActive = true });

        db.HospitalRooms.AddRange(
            new HospitalRoom { Id = 101, HospitalId = 1, DepartmentId = 1, RoomCode = "P.101", Name = "Phòng khám 101", RoomType = "EXAMINATION", Status = "ACTIVE" },
            new HospitalRoom { Id = 102, HospitalId = 1, DepartmentId = 1, RoomCode = "P.102", Name = "Phòng khám 102", RoomType = "EXAMINATION", Status = "ACTIVE" },
            new HospitalRoom { Id = 201, HospitalId = 2, DepartmentId = 3, RoomCode = "P.201", Name = "Phòng khám 201", RoomType = "EXAMINATION", Status = "ACTIVE" });

        db.Specialties.AddRange(
            new Specialty { Id = 1, SpecialtyCode = "GI", Name = "Nội Tiêu hóa", IsActive = true },
            new Specialty { Id = 2, SpecialtyCode = "CARD", Name = "Tim mạch", IsActive = true });

        db.Roles.AddRange(
            new Role { Id = 1, Code = "PATIENT", Name = "Bệnh nhân" },
            new Role { Id = 2, Code = "RECEPTION", Name = "Tiếp đón" },
            new Role { Id = 3, Code = "DOCTOR", Name = "Bác sĩ" },
            new Role { Id = 4, Code = "PHARMACY_STAFF", Name = "Nhân viên nhà thuốc" },
            new Role { Id = 5, Code = "HOSPITAL_ADMIN", Name = "Quản trị bệnh viện" });

        db.Permissions.AddRange(
            new Permission { Id = 1, Code = "MEDICINE_DISPENSE" },
            new Permission { Id = 2, Code = "PAYMENT_VIEW" },
            new Permission { Id = 3, Code = "PATIENT_PROFILE_READ" });

        db.RolePermissions.AddRange(
            new RolePermission { RoleId = 4, PermissionId = 1 },
            new RolePermission { RoleId = 4, PermissionId = 2 },
            new RolePermission { RoleId = 4, PermissionId = 3 });

        db.Users.AddRange(
            new User { Id = 10, Email = "pharmacy_staff01@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 12, Email = "pharmacy_staff_h2@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 13, Email = "doctor01@medflow.test", AccountStatus = AccountStatus.ACTIVE });

        db.UserRoles.AddRange(
            new UserRole { UserId = 10, RoleId = 4 },
            new UserRole { UserId = 12, RoleId = 4 },
            new UserRole { UserId = 13, RoleId = 3 });

        db.Staff.AddRange(
            new Staff { Id = 10, UserId = 10, HospitalId = 1, DepartmentId = 2, StaffCode = "STF-010", FullName = "Dược sĩ Trần Mai", Status = "ACTIVE" },
            new Staff { Id = 12, UserId = 12, HospitalId = 2, DepartmentId = 3, StaffCode = "STF-012", FullName = "Dược sĩ Phạm Lan", Status = "ACTIVE" },
            new Staff { Id = 101, UserId = 13, HospitalId = 1, DepartmentId = 1, StaffCode = "DOC-STF-01", FullName = "BS. Nguyễn Văn An", Status = "ACTIVE" });

        db.Doctors.Add(new Doctor { Id = 1, StaffId = 101, HospitalId = 1, SpecialtyId = 1, DoctorCode = "DOC-001", ProfessionalTitle = "BS. CKI" });

        db.Patients.AddRange(
            new Patient { Id = 1001, PatientCode = "PAT-001", FullName = "Nguyễn Văn Bệnh Nhân", Gender = "MALE", DateOfBirth = new DateOnly(1990, 5, 15), Status = "ACTIVE" },
            new Patient { Id = 1002, PatientCode = "PAT-002", FullName = "Trần Thị Khách", Gender = "FEMALE", DateOfBirth = new DateOnly(1985, 8, 20), Status = "ACTIVE" });

        db.Visits.AddRange(
            new Visit { Id = 2001, VisitCode = "VIS-001", PatientId = 1001, AssignedDoctorId = 1, ExaminationRoomId = 101, Status = "COMPLETED" },
            new Visit { Id = 2002, VisitCode = "VIS-002", PatientId = 1002, AssignedDoctorId = 1, ExaminationRoomId = 102, Status = "COMPLETED" },
            new Visit { Id = 2003, VisitCode = "VIS-003", PatientId = 1001, AssignedDoctorId = 1, ExaminationRoomId = 201, Status = "COMPLETED" });

        db.Medicines.AddRange(
            new Medicine { Id = 3001, HospitalId = 1, MedicineCode = "MED-PAR-500", Name = "Paracetamol 500mg", DispensingUnit = "Viên", IsActive = true },
            new Medicine { Id = 3002, HospitalId = 1, MedicineCode = "MED-IBU-400", Name = "Ibuprofen 400mg", DispensingUnit = "Viên", IsActive = true });

        // Prescriptions for Hospital 1
        db.Prescriptions.Add(new Prescription
        {
            Id = 101,
            PrescriptionCode = "RX-2026-001",
            VisitId = 2001,
            PrescribingDoctorId = 1,
            Status = "ISSUED",
            FulfillmentChoice = "HOSPITAL",
            IssuedAt = FixedNowUtc.AddHours(-1),
            CreatedAt = FixedNowUtc.AddHours(-1),
        });
        db.PrescriptionItems.Add(new PrescriptionItem
        {
            Id = 1,
            PrescriptionId = 101,
            MedicineId = 3001,
            PrescribedQuantity = 20,
            SingleDoseQuantity = 1,
            SingleDoseUnit = "Viên",
            DoseScheduleText = "Sáng 1 viên, tối 1 viên",
            Frequency = "2 lần/ngày",
            DurationDays = 10,
            AdministrationRoute = "Đường uống",
            Instructions = "Uống sau ăn",
        });

        db.Prescriptions.Add(new Prescription
        {
            Id = 102,
            PrescriptionCode = "RX-2026-002",
            VisitId = 2001,
            PrescribingDoctorId = 1,
            Status = "ISSUED",
            FulfillmentChoice = "HOSPITAL",
            IssuedAt = FixedNowUtc.AddHours(-2),
            CreatedAt = FixedNowUtc.AddHours(-2),
        });
        db.PrescriptionItems.Add(new PrescriptionItem
        {
            Id = 2,
            PrescriptionId = 102,
            MedicineId = 3002,
            PrescribedQuantity = 10,
            SingleDoseQuantity = 1,
            SingleDoseUnit = "Viên",
            Instructions = "Uống khi sốt",
        });
        db.MedicationDispenses.Add(new MedicationDispense
        {
            Id = 501,
            PrescriptionId = 102,
            Status = "PREPARING",
            PreparedAt = FixedNowUtc.AddHours(-1),
        });

        // Foreign prescription for Hospital 2
        db.Prescriptions.Add(new Prescription
        {
            Id = 111,
            PrescriptionCode = "RX-2026-FOREIGN",
            VisitId = 2003,
            PrescribingDoctorId = 1,
            Status = "ISSUED",
            FulfillmentChoice = "HOSPITAL",
            IssuedAt = FixedNowUtc.AddHours(-5),
            CreatedAt = FixedNowUtc.AddHours(-5),
        });
        db.PrescriptionItems.Add(new PrescriptionItem { Id = 11, PrescriptionId = 111, MedicineId = 3001, PrescribedQuantity = 10 });

        db.SaveChanges();
    }

    [Fact]
    public async Task Search_Unauthenticated_Returns401()
    {
        var response = await _client.GetAsync("/api/v1/pharmacy/prescriptions");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("PATIENT")]
    [InlineData("DOCTOR")]
    [InlineData("RECEPTION")]
    [InlineData("HOSPITAL_ADMIN")]
    public async Task Search_WrongRoles_Returns403(string role)
    {
        var token = GenerateToken(99, $"{role.ToLowerInvariant()}@medflow.test", role);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/pharmacy/prescriptions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Search_ForgedRoleToken_UserIsDoctorInDb_Returns403()
    {
        // Token claims PHARMACY_STAFF for user 13, but user 13 in DB has role DOCTOR
        var token = GenerateToken(13, "doctor01@medflow.test", "PHARMACY_STAFF");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/pharmacy/prescriptions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Search_PharmacyRoleWithPaymentViewOnly_Returns403()
    {
        using (var scope = _customFactory.Services.CreateScope())
        {
            var db = (IntegrationPharmacyDbContext)scope.ServiceProvider
                .GetRequiredService<IApplicationDbContext>();
            var dispensePermission = await db.Permissions
                .SingleAsync(p => p.Code == "MEDICINE_DISPENSE");
            var patientReadPermission = await db.Permissions
                .SingleAsync(p => p.Code == "PATIENT_PROFILE_READ");
            var rolePermissions = await db.RolePermissions
                .Where(rp =>
                    rp.RoleId == 4 &&
                    (rp.PermissionId == dispensePermission.Id ||
                     rp.PermissionId == patientReadPermission.Id))
                .ToListAsync();
            db.RolePermissions.RemoveRange(rolePermissions);
            await db.SaveChangesAsync();
        }

        var token = GenerateToken(10, "pharmacy_staff01@medflow.test", "PHARMACY_STAFF");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/pharmacy/prescriptions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("?page=0")]
    [InlineData("?pageSize=0")]
    [InlineData("?pageSize=101")]
    [InlineData("?status=INVALID_STATUS")]
    public async Task Search_MalformedFilters_Returns400(string queryString)
    {
        var token = GenerateToken(10, "pharmacy_staff01@medflow.test", "PHARMACY_STAFF");

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/pharmacy/prescriptions{queryString}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Search_QueryExceeding100Chars_Returns400()
    {
        var token = GenerateToken(10, "pharmacy_staff01@medflow.test", "PHARMACY_STAFF");
        var longQuery = new string('A', 101);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/pharmacy/prescriptions?query={longQuery}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Search_ValidPharmacyStaff_Returns200WithScopedPrescriptions()
    {
        var token = GenerateToken(10, "pharmacy_staff01@medflow.test", "PHARMACY_STAFF");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/pharmacy/prescriptions?page=1&pageSize=10");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<PrescriptionSearchResponseDto>();
        Assert.NotNull(payload);
        Assert.Equal(2, payload.TotalCount);
        Assert.Equal(2, payload.Items.Count);
        Assert.Equal(1, payload.Page);
        Assert.Equal(10, payload.PageSize);

        var codes = payload.Items.Select(i => i.PrescriptionCode).ToList();
        Assert.Contains("RX-2026-001", codes);
        Assert.Contains("RX-2026-002", codes);
        Assert.DoesNotContain("RX-2026-FOREIGN", codes);
    }

    [Fact]
    public async Task Search_WithQueryFilter_Returns200WithMatchingItemsOnly()
    {
        var token = GenerateToken(10, "pharmacy_staff01@medflow.test", "PHARMACY_STAFF");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/pharmacy/prescriptions?query=RX-2026-001");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<PrescriptionSearchResponseDto>();
        Assert.NotNull(payload);
        Assert.Equal(1, payload.TotalCount);
        Assert.Single(payload.Items);
        Assert.Equal("RX-2026-001", payload.Items[0].PrescriptionCode);
    }

    [Fact]
    public async Task Search_WithStatusFilter_Returns200WithFilteredStatus()
    {
        var token = GenerateToken(10, "pharmacy_staff01@medflow.test", "PHARMACY_STAFF");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/pharmacy/prescriptions?status=PREPARING");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<PrescriptionSearchResponseDto>();
        Assert.NotNull(payload);
        Assert.Equal(1, payload.TotalCount);
        Assert.Single(payload.Items);
        Assert.Equal("RX-2026-002", payload.Items[0].PrescriptionCode);
        Assert.Equal("PREPARING", payload.Items[0].Status);
    }

    [Fact]
    public async Task GetById_Unauthenticated_Returns401()
    {
        var response = await _client.GetAsync("/api/v1/pharmacy/prescriptions/101");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("PATIENT")]
    [InlineData("DOCTOR")]
    [InlineData("RECEPTION")]
    [InlineData("HOSPITAL_ADMIN")]
    public async Task GetById_WrongRoles_Returns403(string role)
    {
        var token = GenerateToken(99, $"{role.ToLowerInvariant()}@medflow.test", role);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/pharmacy/prescriptions/101");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("   ")]
    public async Task GetById_MalformedId_Returns400(string idOrCode)
    {
        var token = GenerateToken(10, "pharmacy_staff01@medflow.test", "PHARMACY_STAFF");

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/pharmacy/prescriptions/{Uri.EscapeDataString(idOrCode)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetById_ValidNumericId_Returns200WithDetail()
    {
        var token = GenerateToken(10, "pharmacy_staff01@medflow.test", "PHARMACY_STAFF");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/pharmacy/prescriptions/101");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<PrescriptionSearchDetailDto>();
        Assert.NotNull(payload);
        Assert.Equal(101, payload.Prescription.Id);
        Assert.Equal("RX-2026-001", payload.Prescription.PrescriptionCode);
        Assert.Equal("PAT-001", payload.Prescription.PatientCode);
        Assert.Equal("Nguyễn Văn Bệnh Nhân", payload.Prescription.PatientName);
        Assert.Equal("PENDING", payload.Prescription.Status);

        Assert.Single(payload.Items);
        var item = payload.Items[0];
        Assert.Equal("MED-PAR-500", item.MedicineCode);
        Assert.Equal("Paracetamol 500mg", item.MedicineName);
        Assert.Equal(20m, item.PrescribedQuantity);
        Assert.Equal("Viên", item.DispensingUnit);
        Assert.Equal("Uống sau ăn", item.Instructions);
    }

    [Fact]
    public async Task GetById_ValidPrescriptionCode_CaseInsensitive_Returns200WithDetail()
    {
        var token = GenerateToken(10, "pharmacy_staff01@medflow.test", "PHARMACY_STAFF");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/pharmacy/prescriptions/rx-2026-001");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<PrescriptionSearchDetailDto>();
        Assert.NotNull(payload);
        Assert.Equal(101, payload.Prescription.Id);
        Assert.Equal("RX-2026-001", payload.Prescription.PrescriptionCode);
    }

    [Fact]
    public async Task GetById_ForeignHospitalPrescription_Returns404()
    {
        // Pharmacist at Hospital 1 attempts to access prescription 111 at Hospital 2
        var token = GenerateToken(10, "pharmacy_staff01@medflow.test", "PHARMACY_STAFF");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/pharmacy/prescriptions/111");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetById_NonExistentPrescription_Returns404()
    {
        var token = GenerateToken(10, "pharmacy_staff01@medflow.test", "PHARMACY_STAFF");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/pharmacy/prescriptions/99999");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
