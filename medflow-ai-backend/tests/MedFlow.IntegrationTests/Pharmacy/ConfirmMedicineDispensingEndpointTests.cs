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
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MedFlow.IntegrationTests.Pharmacy;

public sealed class ConfirmMedicineDispensingEndpointTests : IClassFixture<TestWebAppFactory>, IDisposable
{
    public sealed class IntegrationPharmacyDbContext : DbContext, IApplicationDbContext
    {
        public IntegrationPharmacyDbContext(DbContextOptions<IntegrationPharmacyDbContext> options)
            : base(options)
        {
        }

        private static NotSupportedException NotUsed() =>
            new("Not needed for ConfirmMedicineDispensingEndpointTests.");

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

    private static readonly DateTime FixedNowUtc = new(2026, 11, 14, 10, 0, 0, DateTimeKind.Utc);
    private readonly WebApplicationFactory<Program> _customFactory;
    private readonly HttpClient _client;
    private readonly string _dbName;

    public ConfirmMedicineDispensingEndpointTests(TestWebAppFactory factory)
    {
        _dbName = $"ConfirmMedicineDispensingEndpointTests_{Guid.NewGuid()}";

        var inMemoryOptions = new DbContextOptionsBuilder<IntegrationPharmacyDbContext>()
            .UseInMemoryDatabase(_dbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
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
            new Hospital { Id = 1, Name = "Bệnh viện Đa khoa Quốc tế MedFlow", IsActive = true },
            new Hospital { Id = 2, Name = "Bệnh viện Chi nhánh 2", IsActive = true });

        db.Departments.Add(new Department { Id = 1, HospitalId = 1, Name = "Khoa Dược", IsActive = true });
        db.HospitalRooms.Add(new HospitalRoom { Id = 101, HospitalId = 1, DepartmentId = 1, RoomCode = "P.101", Name = "Quầy phát thuốc 01", RoomType = "PHARMACY", Status = "ACTIVE" });

        db.Roles.AddRange(
            new Role { Id = 1, Code = "PHARMACY_STAFF", Name = "Dược sĩ" },
            new Role { Id = 2, Code = "DOCTOR", Name = "Bác sĩ" });

        db.Permissions.Add(new Permission { Id = 1, Code = "MEDICINE_DISPENSE" });
        db.RolePermissions.Add(new RolePermission { RoleId = 1, PermissionId = 1 });

        // Staff 10: Valid Pharmacist
        db.Users.Add(new User { Id = 10, Email = "pharmacist@medflow.test", AccountStatus = AccountStatus.ACTIVE });
        db.UserRoles.Add(new UserRole { UserId = 10, RoleId = 1 });
        db.Staff.Add(new Staff { Id = 10, UserId = 10, HospitalId = 1, FullName = "Đỗ Thị Lan Anh", Status = "ACTIVE" });

        // Doctor 20
        db.Users.Add(new User { Id = 20, Email = "doctor@medflow.test", AccountStatus = AccountStatus.ACTIVE });
        db.UserRoles.Add(new UserRole { UserId = 20, RoleId = 2 });
        db.Staff.Add(new Staff { Id = 20, UserId = 20, HospitalId = 1, FullName = "Trần Quốc Tuấn", Status = "ACTIVE" });
        db.Doctors.Add(new Doctor { Id = 20, StaffId = 20, HospitalId = 1, DoctorCode = "DOC-0020", ProfessionalTitle = "BS.CKII" });

        // Patient 100
        db.Patients.Add(new Patient
        {
            Id = 100,
            PatientCode = "PAT-0001",
            FullName = "Nguyễn Văn An",
            Gender = "Nam",
            IdentificationHash = "hash123",
            Status = "ACTIVE"
        });

        // Medicines & Batches
        db.Medicines.Add(new Medicine
        {
            Id = 1,
            HospitalId = 1,
            MedicineCode = "MED-001",
            Name = "Bismuth subsalicylate 300mg",
            DosageForm = "Viên nhai",
            DispensingUnit = "viên",
            IsActive = true
        });

        db.MedicineBatches.Add(new MedicineBatch
        {
            Id = 1,
            MedicineId = 1,
            BatchNumber = "LO-2405",
            ExpiryDate = new DateOnly(2027, 9, 18),
            QuantityOnHand = 100m,
            IsBlocked = false
        });

        // Visit 501
        db.Visits.Add(new Visit
        {
            Id = 501,
            VisitCode = "VIS-2026-0108",
            PatientId = 100,
            AssignedDoctorId = 20,
            ExaminationRoomId = 101,
            Status = "COMPLETED"
        });

        db.Payments.Add(new Payment
        {
            Id = 1,
            VisitId = 501,
            PaymentCode = "PAY-001",
            Amount = 1293600m,
            Status = "PAID",
            PaidAt = FixedNowUtc.AddMinutes(-30)
        });

        // Prescription 1001
        db.Prescriptions.Add(new Prescription
        {
            Id = 1001,
            PrescriptionCode = "RX-2026-0088",
            VisitId = 501,
            PrescribingDoctorId = 20,
            FulfillmentChoice = "HOSPITAL",
            Status = "ISSUED",
            IssuedAt = FixedNowUtc.AddMinutes(-40),
            CreatedAt = FixedNowUtc.AddMinutes(-50)
        });

        db.PrescriptionItems.Add(new PrescriptionItem
        {
            Id = 1,
            PrescriptionId = 1001,
            MedicineId = 1,
            PrescribedQuantity = 56m,
            SingleDoseQuantity = 2m,
            SingleDoseUnit = "viên",
            Instructions = "Sáng 2 viên, Chiều 2 viên uống trước ăn 30 phút"
        });

        db.SaveChanges();
    }

    [Fact]
    public async Task GetDispenseContext_NoToken_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/v1/pharmacy/prescriptions/RX-2026-0088/dispense");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetDispenseContext_WrongRole_ReturnsForbidden()
    {
        var token = GenerateToken(20, "doctor@medflow.test", "DOCTOR");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/pharmacy/prescriptions/RX-2026-0088/dispense");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetDispenseContext_PharmacyStaff_ReturnsOkWithContext()
    {
        var token = GenerateToken(10, "pharmacist@medflow.test", "PHARMACY_STAFF");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/pharmacy/prescriptions/RX-2026-0088/dispense");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<DispenseConfirmContextDto>();
        Assert.NotNull(dto);
        Assert.Equal("RX-2026-0088", dto.RxCode);
        Assert.True(dto.CanDispense);
        Assert.Single(dto.Medications);
        Assert.Equal("LO-2405", dto.Medications[0].BatchCode);
    }

    [Fact]
    public async Task GetDispenseContext_AliasRoute_ReturnsOk()
    {
        var token = GenerateToken(10, "pharmacist@medflow.test", "PHARMACY_STAFF");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/pharmacy/dispense/RX-2026-0088");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ConfirmDispense_ValidPayload_ReturnsOkAndDeductsStock()
    {
        var token = GenerateToken(10, "pharmacist@medflow.test", "PHARMACY_STAFF");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/pharmacy/prescriptions/RX-2026-0088/dispense");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new ConfirmDispenseRequest(
            ReceiverName: "Nguyễn Văn An",
            Relationship: "Bản thân",
            IsIdentityVerified: true,
            ChecksConfirmed: true,
            IdempotencyKey: "test-integration-001"
        ));

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<DispensingResultDetailDto>();
        Assert.NotNull(result);
        Assert.Equal("RX-2026-0088", result.RxCode);
        Assert.Equal("DISPENSED & CLOSED", result.StatusLabel);
        Assert.Single(result.Items);
        Assert.Equal(56m, result.Items[0].QtyDispensed);

        // Verify result endpoint
        using var resultRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/pharmacy/prescriptions/RX-2026-0088/dispense-result");
        resultRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resultResponse = await _client.SendAsync(resultRequest);
        Assert.Equal(HttpStatusCode.OK, resultResponse.StatusCode);

        var receiptDto = await resultResponse.Content.ReadFromJsonAsync<DispensingResultDetailDto>();
        Assert.NotNull(receiptDto);
        Assert.Equal(result.DispenseCode, receiptDto.DispenseCode);
    }

    [Fact]
    public async Task ConfirmDispense_UncheckedChecklist_ReturnsBadRequest()
    {
        var token = GenerateToken(10, "pharmacist@medflow.test", "PHARMACY_STAFF");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/pharmacy/prescriptions/RX-2026-0088/dispense");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new ConfirmDispenseRequest(
            ReceiverName: "Nguyễn Văn An",
            IsIdentityVerified: true,
            ChecksConfirmed: false
        ));

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
