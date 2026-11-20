using System.Globalization;
using MedFlow.Application.DTOs.Pharmacy;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Pharmacy;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.UnitTests.Pharmacy;

public sealed class DispensingResultServiceTests : IDisposable
{
    private sealed class FixedTimeProvider(DateTimeOffset nowUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => nowUtc;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    public sealed class TestDispenseDbContext : DbContext, IApplicationDbContext
    {
        public TestDispenseDbContext(DbContextOptions<TestDispenseDbContext> options)
            : base(options)
        {
        }

        private static NotSupportedException NotUsed() =>
            new("Not needed for DispensingResultService unit tests.");

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

    private readonly TestDispenseDbContext _db;
    private readonly FixedTimeProvider _timeProvider;
    private readonly DispensingResultService _service;
    private readonly DateTime _nowUtc;

    public DispensingResultServiceTests()
    {
        var options = new DbContextOptionsBuilder<TestDispenseDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new TestDispenseDbContext(options);
        _nowUtc = new DateTime(2026, 11, 14, 10, 0, 0, DateTimeKind.Utc);
        _timeProvider = new FixedTimeProvider(_nowUtc);
        _service = new DispensingResultService(_db, _timeProvider);
    }

    public void Dispose()
    {
        _db.Database.EnsureDeleted();
        _db.Dispose();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetDispensingResult_EmptyOrWhitespace_ThrowsValidationException(string? idOrCode)
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.GetDispensingResultAsync(1, idOrCode!));
    }

    [Fact]
    public async Task GetDispensingResult_Exceeds100Chars_ThrowsValidationException()
    {
        var longInput = new string('A', 101);
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.GetDispensingResultAsync(1, longInput));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("-999")]
    public async Task GetDispensingResult_ZeroOrNegativeNumericId_ThrowsValidationException(string invalidId)
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.GetDispensingResultAsync(1, invalidId));
    }

    [Fact]
    public async Task GetDispensingResult_UserNotFound_ThrowsUnauthorizedAccessException()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _service.GetDispensingResultAsync(999, "1"));
    }

    [Fact]
    public async Task GetDispensingResult_UserInactive_ThrowsUnauthorizedAccessException()
    {
        _db.Users.Add(new User { Id = 1, AccountStatus = AccountStatus.INACTIVE });
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _service.GetDispensingResultAsync(1, "1"));
    }

    [Fact]
    public async Task GetDispensingResult_UserLocked_ThrowsUnauthorizedAccessException()
    {
        _db.Users.Add(new User
        {
            Id = 1,
            AccountStatus = AccountStatus.ACTIVE,
            LockedUntil = _nowUtc.AddHours(1)
        });
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _service.GetDispensingResultAsync(1, "1"));
    }

    [Fact]
    public async Task GetDispensingResult_UserWithoutPharmacyRole_ThrowsForbiddenException()
    {
        _db.Users.Add(new User { Id = 1, AccountStatus = AccountStatus.ACTIVE });
        _db.Roles.Add(new Role { Id = 1, Code = "DOCTOR", Name = "Bác sĩ" });
        _db.UserRoles.Add(new UserRole { UserId = 1, RoleId = 1 });
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetDispensingResultAsync(1, "1"));
    }

    [Fact]
    public async Task GetDispensingResult_StaffNotFound_ThrowsForbiddenException()
    {
        _db.Users.Add(new User { Id = 1, AccountStatus = AccountStatus.ACTIVE });
        _db.Roles.Add(new Role { Id = 2, Code = "PHARMACY_STAFF", Name = "Dược sĩ" });
        _db.UserRoles.Add(new UserRole { UserId = 1, RoleId = 2 });
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetDispensingResultAsync(1, "1"));
    }

    [Fact]
    public async Task GetDispensingResult_HospitalInactive_ThrowsForbiddenException()
    {
        _db.Users.Add(new User { Id = 1, AccountStatus = AccountStatus.ACTIVE });
        _db.Roles.Add(new Role { Id = 2, Code = "PHARMACY_STAFF", Name = "Dược sĩ" });
        _db.UserRoles.Add(new UserRole { UserId = 1, RoleId = 2 });
        _db.Hospitals.Add(new Hospital { Id = 10, Name = "Bệnh viện Test", IsActive = false });
        _db.Staff.Add(new Staff { Id = 1, UserId = 1, HospitalId = 10, Status = "ACTIVE" });
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetDispensingResultAsync(1, "1"));
    }

    [Fact]
    public async Task GetDispensingResult_PermissionNotGranted_ThrowsForbiddenException()
    {
        _db.Users.Add(new User { Id = 1, AccountStatus = AccountStatus.ACTIVE });
        _db.Roles.Add(new Role { Id = 2, Code = "PHARMACY_STAFF", Name = "Dược sĩ" });
        _db.UserRoles.Add(new UserRole { UserId = 1, RoleId = 2 });
        _db.Hospitals.Add(new Hospital { Id = 10, Name = "Bệnh viện Test", IsActive = true });
        _db.Staff.Add(new Staff { Id = 1, UserId = 1, HospitalId = 10, Status = "ACTIVE" });

        _db.Permissions.Add(new Permission { Id = 1, Code = "MEDICINE_DISPENSE" });
        _db.Permissions.Add(new Permission { Id = 2, Code = "OTHER_PERMISSION" });
        _db.RolePermissions.Add(new RolePermission { RoleId = 2, PermissionId = 2 }); // missing MEDICINE_DISPENSE
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetDispensingResultAsync(1, "1"));
    }

    [Fact]
    public async Task GetDispensingResult_DispenseNotFound_ThrowsNotFoundException()
    {
        await SeedValidPharmacyStaffAsync();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.GetDispensingResultAsync(1, "DISP-9999"));
    }

    [Fact]
    public async Task GetDispensingResult_CrossHospitalDispense_ThrowsNotFoundException()
    {
        await SeedValidPharmacyStaffAsync(hospitalId: 10);
        // Seed dispense belonging to hospital 20
        _db.Hospitals.Add(new Hospital { Id = 20, Name = "Bệnh viện Khác", IsActive = true });
        _db.HospitalRooms.Add(new HospitalRoom { Id = 201, HospitalId = 20, Name = "Phòng 201", RoomCode = "R201" });
        _db.Visits.Add(new Visit { Id = 2001, VisitCode = "VIS-2001", ExaminationRoomId = 201 });
        _db.Prescriptions.Add(new Prescription { Id = 3001, PrescriptionCode = "RX-3001", VisitId = 2001 });
        _db.MedicationDispenses.Add(new MedicationDispense
        {
            Id = 5001,
            PrescriptionId = 3001,
            Status = "COMPLETED",
            DispensedAt = _nowUtc
        });
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.GetDispensingResultAsync(1, "5001"));
    }

    [Fact]
    public async Task GetDispensingResult_ValidNumericId_ReturnsDispensingResultDetailAndWritesAuditLog()
    {
        await SeedValidPharmacyStaffAsync(hospitalId: 10);
        await SeedFullDispenseGraphAsync(hospitalId: 10, dispenseId: 1, rxId: 1, visitId: 1);

        var result = await _service.GetDispensingResultAsync(1, "1", ipAddress: "192.168.1.100");

        Assert.NotNull(result);
        Assert.Equal("DISP-2026-1114-0001", result.DispenseCode);
        Assert.Equal("RX-202611-0001", result.RxCode);
        Assert.Equal("VIS-0001", result.VisitCode);
        Assert.Equal("P001", result.TicketNo);
        Assert.Equal("Nguyễn Văn An", result.PatientName);
        Assert.Equal("PAT-0001", result.PatientCode);
        Assert.Equal("Nguyễn Văn An (Chính chủ người bệnh)", result.ReceiverName);
        Assert.Equal("****** (Đã xác thực CCCD)", result.ReceiverIdentityMasked);
        Assert.Equal("BS.CKII Trần Quốc Tuấn (Phòng 101)", result.PrescribingDoctor);
        Assert.Equal("DS. Đỗ Thị Lan Anh", result.DispensingPharmacist);
        Assert.Equal("14/11/2026 • 10:00:00", result.DispensedAt);
        Assert.Equal("DISPENSED & CLOSED", result.StatusLabel);
        Assert.Contains("VietQR", result.PaymentSummary);
        Assert.Equal("Chưa có dữ liệu", result.TotalAmountFormatted);
        Assert.Null(result.TotalAmount);
        Assert.Equal(2, result.Items.Count);

        // Check line items
        var item1 = result.Items[0];
        Assert.Equal("di-1", item1.Id);
        Assert.Equal(1, item1.OrderIndex);
        Assert.Equal("Omeprazole 20mg", item1.DrugName);
        Assert.Equal("Viên nang", item1.DosageForm);
        Assert.Equal(14m, item1.QtyDispensed);
        Assert.Equal("viên", item1.Unit);
        Assert.Equal("LO-2026A", item1.BatchCode);
        Assert.Equal("15/12/2027", item1.ExpiryDate);
        Assert.Equal("Chưa cập nhật", item1.ShelfLocation);

        // Check audit log write
        var audit = await _db.AuditLogs.FirstOrDefaultAsync(a => a.EntityId == "1");
        Assert.NotNull(audit);
        Assert.Equal("MEDICATION_DISPENSE_VIEW", audit.Action);
        Assert.Equal("MEDICATION_DISPENSE", audit.EntityType);
        Assert.Equal(1, audit.ActorUserId);
        Assert.Equal(10, audit.HospitalId);
        Assert.Equal("192.168.1.100", audit.IpAddress?.ToString());
    }

    [Fact]
    public async Task GetDispensingResult_LookupByDispenseCode_ResolvesSuccessfully()
    {
        await SeedValidPharmacyStaffAsync(hospitalId: 10);
        await SeedFullDispenseGraphAsync(hospitalId: 10, dispenseId: 42, rxId: 88, visitId: 108);

        var result = await _service.GetDispensingResultAsync(1, "DISP-2026-1114-0042");

        Assert.NotNull(result);
        Assert.Equal(42, result.DispenseId);
        Assert.Equal(88, result.PrescriptionId);
    }

    [Fact]
    public async Task GetDispensingResult_LookupByPrescriptionCode_ResolvesSuccessfully()
    {
        await SeedValidPharmacyStaffAsync(hospitalId: 10);
        await SeedFullDispenseGraphAsync(hospitalId: 10, dispenseId: 15, rxId: 99, visitId: 200, rxCode: "RX-CUSTOM-0099");

        var result = await _service.GetDispensingResultAsync(1, "RX-CUSTOM-0099");

        Assert.NotNull(result);
        Assert.Equal(15, result.DispenseId);
        Assert.Equal("RX-CUSTOM-0099", result.RxCode);
    }

    [Fact]
    public async Task GetDispensingResult_WithoutPaidPayment_ReturnsUnavailablePricingAndUnpaidSummary()
    {
        await SeedValidPharmacyStaffAsync(hospitalId: 10);
        await SeedFullDispenseGraphAsync(hospitalId: 10, dispenseId: 2, rxId: 2, visitId: 2, withPayment: false);

        var result = await _service.GetDispensingResultAsync(1, "2");

        Assert.NotNull(result);
        Assert.Null(result.TotalAmount);
        Assert.Equal("Chưa có dữ liệu", result.TotalAmountFormatted);
        Assert.Equal("Chưa ghi nhận thanh toán viện phí", result.PaymentSummary);
        Assert.All(result.Items, i =>
        {
            Assert.Equal("Chưa có dữ liệu", i.UnitPriceFormatted);
            Assert.Equal("Chưa có dữ liệu", i.LineTotalFormatted);
        });
    }

    private async Task SeedValidPharmacyStaffAsync(long hospitalId = 10, long userId = 1)
    {
        _db.Users.Add(new User { Id = userId, AccountStatus = AccountStatus.ACTIVE });
        _db.Roles.Add(new Role { Id = 2, Code = "PHARMACY_STAFF", Name = "Dược sĩ" });
        _db.UserRoles.Add(new UserRole { UserId = userId, RoleId = 2 });

        if (!await _db.Hospitals.AnyAsync(h => h.Id == hospitalId))
        {
            _db.Hospitals.Add(new Hospital { Id = hospitalId, Name = "Bệnh viện Đa khoa Quốc tế", IsActive = true });
        }

        _db.Staff.Add(new Staff
        {
            Id = 1,
            UserId = userId,
            HospitalId = hospitalId,
            FullName = "Đỗ Thị Lan Anh",
            Status = "ACTIVE"
        });

        _db.Permissions.Add(new Permission { Id = 1, Code = "MEDICINE_DISPENSE" });
        _db.RolePermissions.Add(new RolePermission { RoleId = 2, PermissionId = 1 });

        await _db.SaveChangesAsync();
    }

    private async Task SeedFullDispenseGraphAsync(
        long hospitalId,
        long dispenseId,
        long rxId,
        long visitId,
        string? rxCode = null,
        bool withPayment = true)
    {
        var room = new HospitalRoom
        {
            Id = 101,
            HospitalId = hospitalId,
            Name = "Phòng 101",
            RoomCode = "ROOM-101",
            Status = "AVAILABLE"
        };
        _db.HospitalRooms.Add(room);

        var doctorStaff = new Staff
        {
            Id = 20,
            HospitalId = hospitalId,
            FullName = "Trần Quốc Tuấn",
            Status = "ACTIVE"
        };
        _db.Staff.Add(doctorStaff);

        var doctor = new Doctor
        {
            Id = 5,
            HospitalId = hospitalId,
            StaffId = doctorStaff.Id,
            ProfessionalTitle = "BS.CKII",
            Staff = doctorStaff
        };
        _db.Doctors.Add(doctor);

        var patient = new Patient
        {
            Id = 100,
            PatientCode = "PAT-0001",
            FullName = "Nguyễn Văn An",
            IdentificationHash = "mock-sha256-hash-cccd",
            Status = "ACTIVE"
        };
        _db.Patients.Add(patient);

        var visit = new Visit
        {
            Id = visitId,
            VisitCode = $"VIS-{visitId:D4}",
            PatientId = patient.Id,
            AssignedDoctorId = doctor.Id,
            ExaminationRoomId = room.Id,
            Status = "COMPLETED"
        };
        _db.Visits.Add(visit);

        var prescription = new Prescription
        {
            Id = rxId,
            PrescriptionCode = rxCode ?? "RX-202611-0001",
            VisitId = visit.Id,
            PrescribingDoctorId = doctor.Id,
            Status = "DISPENSED",
            IssuedAt = _nowUtc.AddHours(-1)
        };
        _db.Prescriptions.Add(prescription);

        var med1 = new Medicine
        {
            Id = 1,
            HospitalId = hospitalId,
            MedicineCode = "MED-001",
            Name = "Omeprazole 20mg",
            DosageForm = "Viên nang",
            DispensingUnit = "Viên"
        };
        var med2 = new Medicine
        {
            Id = 2,
            HospitalId = hospitalId,
            MedicineCode = "MED-002",
            Name = "Simethicone 80mg",
            DosageForm = "Viên nhai",
            DispensingUnit = "Viên"
        };
        _db.Medicines.AddRange(med1, med2);

        var batch1 = new MedicineBatch
        {
            Id = 10,
            MedicineId = med1.Id,
            BatchNumber = "LO-2026A",
            ExpiryDate = new DateOnly(2027, 12, 15),
            QuantityOnHand = 500m
        };
        var batch2 = new MedicineBatch
        {
            Id = 11,
            MedicineId = med2.Id,
            BatchNumber = "LO-2026B",
            ExpiryDate = new DateOnly(2028, 6, 20),
            QuantityOnHand = 300m
        };
        _db.MedicineBatches.AddRange(batch1, batch2);

        var pItem1 = new PrescriptionItem
        {
            Id = 1,
            PrescriptionId = prescription.Id,
            MedicineId = med1.Id,
            PrescribedQuantity = 14m,
            SingleDoseUnit = "viên"
        };
        var pItem2 = new PrescriptionItem
        {
            Id = 2,
            PrescriptionId = prescription.Id,
            MedicineId = med2.Id,
            PrescribedQuantity = 20m,
            SingleDoseUnit = "viên"
        };
        _db.PrescriptionItems.AddRange(pItem1, pItem2);

        var dispense = new MedicationDispense
        {
            Id = dispenseId,
            PrescriptionId = prescription.Id,
            PreparedByStaffId = 1,
            DispensedByStaffId = 1,
            Status = "COMPLETED",
            PreparedAt = _nowUtc.AddMinutes(-30),
            DispensedAt = _nowUtc
        };
        _db.MedicationDispenses.Add(dispense);
        _db.QueueEntries.Add(new QueueEntry
        {
            Id = visitId,
            VisitId = visit.Id,
            QueueNumber = 1,
            CompletedAt = _nowUtc.AddMinutes(-45)
        });

        var dItem1 = new DispenseItem
        {
            Id = 1,
            DispenseId = dispense.Id,
            PrescriptionItemId = pItem1.Id,
            MedicineBatchId = batch1.Id,
            DispensedQuantity = 14m
        };
        var dItem2 = new DispenseItem
        {
            Id = 2,
            DispenseId = dispense.Id,
            PrescriptionItemId = pItem2.Id,
            MedicineBatchId = batch2.Id,
            DispensedQuantity = 20m
        };
        _db.DispenseItems.AddRange(dItem1, dItem2);

        if (withPayment)
        {
            _db.Payments.Add(new Payment
            {
                Id = 1,
                PaymentCode = "HDDT-2026-0001",
                VisitId = visit.Id,
                Amount = 290000m,
                ExternalReference = "SEPAY-0001",
                Status = "PAID",
                PaidAt = _nowUtc.AddMinutes(-40)
            });
        }

        await _db.SaveChangesAsync();
    }
}
