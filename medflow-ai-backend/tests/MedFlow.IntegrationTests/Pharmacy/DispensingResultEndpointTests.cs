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

public sealed class DispensingResultEndpointTests : IClassFixture<TestWebAppFactory>, IDisposable
{
    public sealed class IntegrationDispenseDbContext : DbContext, IApplicationDbContext
    {
        public IntegrationDispenseDbContext(DbContextOptions<IntegrationDispenseDbContext> options)
            : base(options)
        {
        }

        private static NotSupportedException NotUsed() =>
            new("Not needed for DispensingResultEndpointTests.");

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
        public DbSet<QueueEntry> QueueEntries => Set<QueueEntry>();
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

            modelBuilder.Entity<User>().HasKey(u => u.Id);
            modelBuilder.Entity<Role>().HasKey(r => r.Id);
            modelBuilder.Entity<Permission>().HasKey(p => p.Id);
            modelBuilder.Entity<UserRole>().HasKey(ur => new { ur.UserId, ur.RoleId });
            modelBuilder.Entity<RolePermission>().HasKey(rp => new { rp.RoleId, rp.PermissionId });
            modelBuilder.Entity<Staff>().HasKey(s => s.Id);
            modelBuilder.Entity<Hospital>().HasKey(h => h.Id);
            modelBuilder.Entity<HospitalRoom>().HasKey(r => r.Id);
            modelBuilder.Entity<Doctor>().HasKey(d => d.Id);
            modelBuilder.Entity<Patient>().HasKey(p => p.Id);
            modelBuilder.Entity<Visit>().HasKey(v => v.Id);
            modelBuilder.Entity<QueueEntry>().HasKey(qe => qe.Id);
            modelBuilder.Entity<Prescription>().HasKey(p => p.Id);
            modelBuilder.Entity<PrescriptionItem>().HasKey(pi => pi.Id);
            modelBuilder.Entity<MedicationDispense>().HasKey(md => md.Id);
            modelBuilder.Entity<DispenseItem>().HasKey(di => di.Id);
            modelBuilder.Entity<MedicineBatch>().HasKey(mb => mb.Id);
            modelBuilder.Entity<Medicine>().HasKey(m => m.Id);
            modelBuilder.Entity<Payment>().HasKey(p => p.Id);
            modelBuilder.Entity<AuditLog>().HasKey(a => a.Id);
        }
    }

    private readonly WebApplicationFactory<Program> _customFactory;
    private readonly HttpClient _client;

    public DispensingResultEndpointTests(TestWebAppFactory factory)
    {
        var dbName = $"DispensingResultEndpointTests_{Guid.NewGuid()}";
        var inMemoryOptions = new DbContextOptionsBuilder<IntegrationDispenseDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        using (var seedCtx = new IntegrationDispenseDbContext(inMemoryOptions))
        {
            SeedEndpointData(seedCtx);
        }

        _customFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IApplicationDbContext>();
                services.AddScoped<IApplicationDbContext>(_ => new IntegrationDispenseDbContext(inMemoryOptions));
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

    [Fact]
    public async Task GetDispense_Unauthenticated_Returns401()
    {
        var response = await _client.GetAsync("/api/v1/pharmacy/dispenses/1");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("PATIENT")]
    [InlineData("DOCTOR")]
    [InlineData("RECEPTION")]
    [InlineData("DIAGNOSTIC_STAFF")]
    [InlineData("HOSPITAL_ADMIN")]
    [InlineData("SYSTEM_ADMIN")]
    public async Task GetDispense_ForbiddenRoles_Returns403(string role)
    {
        var token = GenerateToken(20, $"{role.ToLowerInvariant()}@medflow.test", role);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/pharmacy/dispenses/1");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetDispense_NegativeOrZeroId_Returns400()
    {
        var token = GenerateToken(10, "pharmacy01@medflow.test", "PHARMACY_STAFF");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/pharmacy/dispenses/0");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetDispense_NotFound_Returns404()
    {
        var token = GenerateToken(10, "pharmacy01@medflow.test", "PHARMACY_STAFF");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/pharmacy/dispenses/9999");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetDispense_Success_Returns200WithFullGraph()
    {
        var token = GenerateToken(10, "pharmacy01@medflow.test", "PHARMACY_STAFF");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/pharmacy/dispenses/1");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var detail = await response.Content.ReadFromJsonAsync<DispensingResultDetailDto>();
        Assert.NotNull(detail);
        Assert.Equal(1, detail.DispenseId);
        Assert.Equal("RX-202611-0001", detail.RxCode);
        Assert.Equal("VIS-202611-0001", detail.VisitCode);
        Assert.Equal("Nguyễn Văn An", detail.PatientName);
        Assert.Equal("****** (Đã xác thực CCCD)", detail.ReceiverIdentityMasked);
        Assert.Equal("DISPENSED & CLOSED", detail.StatusLabel);
        Assert.Equal("Chưa có dữ liệu", detail.TotalAmountFormatted);
        Assert.Null(detail.TotalAmount);
        Assert.NotEmpty(detail.Items);
    }

    [Fact]
    public async Task GetDispensingResult_ViaAliasRoute_Returns200()
    {
        var token = GenerateToken(10, "pharmacy01@medflow.test", "PHARMACY_STAFF");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/pharmacy/dispensing-results/DISP-2026-1114-0001");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var detail = await response.Content.ReadFromJsonAsync<DispensingResultDetailDto>();
        Assert.NotNull(detail);
        Assert.Equal(1, detail.DispenseId);
    }

    [Fact]
    public async Task GetDispensingResult_ViaPrescriptionRoute_Returns200()
    {
        var token = GenerateToken(10, "pharmacy01@medflow.test", "PHARMACY_STAFF");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/pharmacy/prescriptions/RX-202611-0001/dispense-result");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var detail = await response.Content.ReadFromJsonAsync<DispensingResultDetailDto>();
        Assert.NotNull(detail);
        Assert.Equal(1, detail.DispenseId);
        Assert.Equal("RX-202611-0001", detail.RxCode);
    }

    [Fact]
    public async Task GetDispense_CrossHospital_Returns404()
    {
        var token = GenerateToken(10, "pharmacy01@medflow.test", "PHARMACY_STAFF");

        // Dispense 2 belongs to hospital 2, pharmacist 10 belongs to hospital 1
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/pharmacy/dispenses/2");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static void SeedEndpointData(IntegrationDispenseDbContext db)
    {
        var h1 = new Hospital { Id = 1, Name = "Bệnh viện 1", IsActive = true };
        var h2 = new Hospital { Id = 2, Name = "Bệnh viện 2", IsActive = true };
        db.Hospitals.AddRange(h1, h2);

        var rStaff = new Role { Id = 1, Code = "PHARMACY_STAFF", Name = "Dược sĩ" };
        var rPatient = new Role { Id = 2, Code = "PATIENT", Name = "Bệnh nhân" };
        db.Roles.AddRange(rStaff, rPatient);

        var userStaff = new User { Id = 10, Email = "pharmacy01@medflow.test", AccountStatus = AccountStatus.ACTIVE };
        var userOther = new User { Id = 20, Email = "other@medflow.test", AccountStatus = AccountStatus.ACTIVE };
        db.Users.AddRange(userStaff, userOther);

        db.UserRoles.Add(new UserRole { UserId = 10, RoleId = 1 });
        db.UserRoles.Add(new UserRole { UserId = 20, RoleId = 2 });

        var staff1 = new Staff { Id = 1, UserId = 10, HospitalId = 1, FullName = "Đỗ Thị Lan Anh", Status = "ACTIVE" };
        db.Staff.Add(staff1);

        var perm = new Permission { Id = 1, Code = "MEDICINE_DISPENSE" };
        db.Permissions.Add(perm);
        db.RolePermissions.Add(new RolePermission { RoleId = 1, PermissionId = 1 });

        // Hospital 1 dispense
        var room1 = new HospitalRoom { Id = 101, HospitalId = 1, Name = "Phòng 101", RoomCode = "P101", Status = "ACTIVE" };
        db.HospitalRooms.Add(room1);

        var docStaff1 = new Staff { Id = 2, HospitalId = 1, FullName = "Trần Quốc Tuấn", Status = "ACTIVE" };
        db.Staff.Add(docStaff1);

        var doc1 = new Doctor { Id = 1, HospitalId = 1, StaffId = 2, ProfessionalTitle = "BS.CKII", Staff = docStaff1 };
        db.Doctors.Add(doc1);

        var pat1 = new Patient { Id = 1, PatientCode = "PAT-0001", FullName = "Nguyễn Văn An", IdentificationHash = "hash-1", Status = "ACTIVE" };
        db.Patients.Add(pat1);

        var v1 = new Visit { Id = 1, VisitCode = "VIS-202611-0001", PatientId = 1, AssignedDoctorId = 1, ExaminationRoomId = 101, Status = "COMPLETED" };
        db.Visits.Add(v1);
        db.QueueEntries.Add(new QueueEntry { Id = 1, VisitId = 1, QueueNumber = 12, CompletedAt = new DateTime(2026, 11, 14, 9, 30, 0, DateTimeKind.Utc) });

        var rx1 = new Prescription { Id = 1, PrescriptionCode = "RX-202611-0001", VisitId = 1, PrescribingDoctorId = 1, Status = "DISPENSED" };
        db.Prescriptions.Add(rx1);

        var med1 = new Medicine { Id = 1, HospitalId = 1, MedicineCode = "MED-01", Name = "Omeprazole", DosageForm = "Viên nang", DispensingUnit = "Viên" };
        db.Medicines.Add(med1);

        var batch1 = new MedicineBatch { Id = 1, MedicineId = 1, BatchNumber = "LO-01", ExpiryDate = new DateOnly(2027, 10, 10), QuantityOnHand = 100 };
        db.MedicineBatches.Add(batch1);

        var pi1 = new PrescriptionItem { Id = 1, PrescriptionId = 1, MedicineId = 1, PrescribedQuantity = 14 };
        db.PrescriptionItems.Add(pi1);

        var disp1 = new MedicationDispense { Id = 1, PrescriptionId = 1, PreparedByStaffId = 1, DispensedByStaffId = 1, Status = "COMPLETED", DispensedAt = new DateTime(2026, 11, 14, 10, 0, 0, DateTimeKind.Utc) };
        db.MedicationDispenses.Add(disp1);

        var di1 = new DispenseItem { Id = 1, DispenseId = 1, PrescriptionItemId = 1, MedicineBatchId = 1, DispensedQuantity = 14 };
        db.DispenseItems.Add(di1);

        var pay1 = new Payment { Id = 1, PaymentCode = "HD-01", VisitId = 1, Amount = 290000m, ExternalReference = "VIETQR-01", Status = "PAID" };
        db.Payments.Add(pay1);

        // Hospital 2 dispense (for cross-hospital isolation test)
        var room2 = new HospitalRoom { Id = 201, HospitalId = 2, Name = "Phòng 201", RoomCode = "P201", Status = "ACTIVE" };
        db.HospitalRooms.Add(room2);

        var v2 = new Visit { Id = 2, VisitCode = "VIS-2", PatientId = 1, ExaminationRoomId = 201, Status = "COMPLETED" };
        db.Visits.Add(v2);

        var rx2 = new Prescription { Id = 2, PrescriptionCode = "RX-2", VisitId = 2, Status = "DISPENSED" };
        db.Prescriptions.Add(rx2);

        var disp2 = new MedicationDispense { Id = 2, PrescriptionId = 2, Status = "COMPLETED", DispensedAt = DateTime.UtcNow };
        db.MedicationDispenses.Add(disp2);

        db.SaveChanges();
    }
}
