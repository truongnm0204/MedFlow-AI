using System.Globalization;
using MedFlow.Application.DTOs.Pharmacy;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Pharmacy;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
namespace MedFlow.UnitTests.Pharmacy;

public sealed class ConfirmMedicineDispensingServiceTests : IDisposable
{
    private sealed class FixedTimeProvider(DateTimeOffset nowUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => nowUtc;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    public sealed class TestPharmacyDbContext : DbContext, IApplicationDbContext
    {
        public TestPharmacyDbContext(DbContextOptions<TestPharmacyDbContext> options)
            : base(options)
        {
        }

        private static NotSupportedException NotUsed() =>
            new("Not needed for ConfirmMedicineDispensingService unit tests.");

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
    private readonly TestPharmacyDbContext _db;
    private readonly ConfirmMedicineDispensingService _service;

    public ConfirmMedicineDispensingServiceTests()
    {
        var options = new DbContextOptionsBuilder<TestPharmacyDbContext>()
            .UseInMemoryDatabase($"ConfirmMedicineDispensingTests_{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _db = new TestPharmacyDbContext(options);
        _service = new ConfirmMedicineDispensingService(_db, new FixedTimeProvider(FixedNowUtc));
        SeedDatabase();
    }

    public void Dispose()
    {
        _db.Database.EnsureDeleted();
        _db.Dispose();
    }

    private void SeedDatabase()
    {
        _db.Hospitals.AddRange(
            new Hospital { Id = 1, Name = "Bệnh viện Đa khoa Quốc tế MedFlow", IsActive = true },
            new Hospital { Id = 2, Name = "Bệnh viện Ngoại vụ", IsActive = true }
        );

        _db.Departments.Add(new Department { Id = 1, HospitalId = 1, Name = "Khoa Dược" });
        _db.HospitalRooms.Add(new HospitalRoom { Id = 1, HospitalId = 1, DepartmentId = 1, RoomCode = "PHARM-01", Name = "Quầy phát thuốc 01" });

        _db.Roles.AddRange(
            new Role { Id = 1, Code = "PHARMACY_STAFF", Name = "Nhân viên Dược" },
            new Role { Id = 2, Code = "DOCTOR", Name = "Bác sĩ" }
        );

        _db.Permissions.Add(new Permission { Id = 1, Code = "MEDICINE_DISPENSE" });
        _db.RolePermissions.Add(new RolePermission { RoleId = 1, PermissionId = 1 });

        // Staff 10: Pharmacist in Hospital 1
        _db.Users.Add(new User { Id = 10, Email = "pharmacist@medflow.test", AccountStatus = AccountStatus.ACTIVE });
        _db.UserRoles.Add(new UserRole { UserId = 10, RoleId = 1 });
        _db.Staff.Add(new Staff { Id = 10, UserId = 10, HospitalId = 1, FullName = "Đỗ Thị Lan Anh", Status = "ACTIVE" });

        // Staff 11: Inactive user
        _db.Users.Add(new User { Id = 11, Email = "inactive@medflow.test", AccountStatus = AccountStatus.INACTIVE });
        _db.UserRoles.Add(new UserRole { UserId = 11, RoleId = 1 });
        _db.Staff.Add(new Staff { Id = 11, UserId = 11, HospitalId = 1, FullName = "Dược sĩ nghỉ việc", Status = "INACTIVE" });

        // Doctor 20
        _db.Users.Add(new User { Id = 20, Email = "doctor@medflow.test", AccountStatus = AccountStatus.ACTIVE });
        _db.UserRoles.Add(new UserRole { UserId = 20, RoleId = 2 });
        _db.Staff.Add(new Staff { Id = 20, UserId = 20, HospitalId = 1, FullName = "Trần Quốc Tuấn", Status = "ACTIVE" });
        _db.Doctors.Add(new Doctor { Id = 20, StaffId = 20, HospitalId = 1, ProfessionalTitle = "BS.CKII", DoctorCode = "DOC-0020" });

        // Patients
        _db.Patients.AddRange(
            new Patient
            {
                Id = 100,
                PatientCode = "PAT-2026-0001",
                FullName = "Nguyễn Văn An",
                Gender = "Nam",
                DateOfBirth = new DateOnly(1990, 5, 20),
                Phone = "0988123456",
                IdentificationHash = "hash123",
                Status = "ACTIVE"
            },
            new Patient
            {
                Id = 101,
                PatientCode = "PAT-2026-0002",
                FullName = "Lê Thị Bình",
                Gender = "Nữ",
                DateOfBirth = new DateOnly(1985, 3, 15),
                Phone = "0977654321",
                IdentificationHashHmac = "hmachash456",
                Status = "ACTIVE"
            }
        );

        // Medicines
        _db.Medicines.AddRange(
            new Medicine
            {
                Id = 1,
                HospitalId = 1,
                MedicineCode = "MED-001",
                Name = "Paracetamol 500mg",
                DosageForm = "Viên nén",
                DispensingUnit = "viên",
                IsActive = true
            },
            new Medicine
            {
                Id = 2,
                HospitalId = 1,
                MedicineCode = "MED-002",
                Name = "Amoxicillin 500mg",
                DosageForm = "Viên nang",
                DispensingUnit = "viên",
                IsActive = true
            }
        );

        // Batches for Medicine 1
        _db.MedicineBatches.AddRange(
            new MedicineBatch
            {
                Id = 101,
                MedicineId = 1,
                BatchNumber = "LOT-PARA-2027",
                ExpiryDate = new DateOnly(2027, 12, 31),
                QuantityOnHand = 100m,
                IsBlocked = false
            },
            new MedicineBatch
            {
                Id = 102,
                MedicineId = 1,
                BatchNumber = "LOT-PARA-2028",
                ExpiryDate = new DateOnly(2028, 6, 30),
                QuantityOnHand = 200m,
                IsBlocked = false
            },
            new MedicineBatch
            {
                Id = 103,
                MedicineId = 1,
                BatchNumber = "LOT-PARA-BLOCKED",
                ExpiryDate = new DateOnly(2027, 8, 1),
                QuantityOnHand = 50m,
                IsBlocked = true
            },
            new MedicineBatch
            {
                Id = 104,
                MedicineId = 1,
                BatchNumber = "LOT-PARA-EXPIRED",
                ExpiryDate = new DateOnly(2026, 1, 1),
                QuantityOnHand = 50m,
                IsBlocked = false
            }
        );

        // Batches for Medicine 2
        _db.MedicineBatches.Add(
            new MedicineBatch
            {
                Id = 201,
                MedicineId = 2,
                BatchNumber = "LOT-AMOX-2027",
                ExpiryDate = new DateOnly(2027, 9, 30),
                QuantityOnHand = 50m,
                IsBlocked = false
            }
        );

        // Visits
        _db.Visits.AddRange(
            new Visit
            {
                Id = 501,
                VisitCode = "VIS-2026-0001",
                PatientId = 100,
                AssignedDoctorId = 20,
                ExaminationRoomId = 1,
                Status = "COMPLETED"
            },
            new Visit
            {
                Id = 502,
                VisitCode = "VIS-2026-0002",
                PatientId = 101,
                AssignedDoctorId = 20,
                ExaminationRoomId = 1,
                Status = "COMPLETED"
            }
        );

        // Payments: Visit 501 is PAID, Visit 502 is PENDING
        _db.Payments.AddRange(
            new Payment
            {
                Id = 1,
                PaymentCode = "PAY-0001",
                VisitId = 501,
                Amount = 150000m,
                Status = "PAID",
                PaidAt = FixedNowUtc.AddMinutes(-30)
            },
            new Payment
            {
                Id = 2,
                PaymentCode = "PAY-0002",
                VisitId = 502,
                Amount = 200000m,
                Status = "PENDING"
            }
        );

        // Prescriptions
        // 1. Ready to dispense prescription for Visit 501 (PAID)
        _db.Prescriptions.Add(new Prescription
        {
            Id = 1001,
            PrescriptionCode = "RX-2026-0001",
            VisitId = 501,
            PrescribingDoctorId = 20,
            FulfillmentChoice = "HOSPITAL",
            Status = "ISSUED",
            IssuedAt = FixedNowUtc.AddMinutes(-45),
            CreatedAt = FixedNowUtc.AddMinutes(-50)
        });

        _db.PrescriptionItems.AddRange(
            new PrescriptionItem
            {
                Id = 10,
                PrescriptionId = 1001,
                MedicineId = 1,
                PrescribedQuantity = 20m,
                SingleDoseQuantity = 1m,
                SingleDoseUnit = "viên",
                DoseScheduleText = "Sáng 1, Tối 1",
                DurationDays = 10
            },
            new PrescriptionItem
            {
                Id = 11,
                PrescriptionId = 1001,
                MedicineId = 2,
                PrescribedQuantity = 10m,
                SingleDoseQuantity = 1m,
                SingleDoseUnit = "viên",
                DoseScheduleText = "Sáng 1 sau ăn",
                DurationDays = 10
            }
        );

        // 2. Prescription for Visit 502 (PENDING PAYMENT)
        _db.Prescriptions.Add(new Prescription
        {
            Id = 1002,
            PrescriptionCode = "RX-2026-0002",
            VisitId = 502,
            PrescribingDoctorId = 20,
            FulfillmentChoice = "HOSPITAL",
            Status = "ISSUED",
            IssuedAt = FixedNowUtc.AddMinutes(-20),
            CreatedAt = FixedNowUtc.AddMinutes(-25)
        });

        _db.PrescriptionItems.Add(new PrescriptionItem
        {
            Id = 20,
            PrescriptionId = 1002,
            MedicineId = 1,
            PrescribedQuantity = 10m,
            SingleDoseQuantity = 1m,
            SingleDoseUnit = "viên"
        });

        // 3. Already Dispensed prescription
        _db.Prescriptions.Add(new Prescription
        {
            Id = 1003,
            PrescriptionCode = "RX-2026-0003",
            VisitId = 501,
            PrescribingDoctorId = 20,
            FulfillmentChoice = "HOSPITAL",
            Status = "DISPENSED",
            IssuedAt = FixedNowUtc.AddHours(-2),
            CreatedAt = FixedNowUtc.AddHours(-3)
        });

        _db.PrescriptionItems.Add(new PrescriptionItem
        {
            Id = 30,
            PrescriptionId = 1003,
            MedicineId = 1,
            PrescribedQuantity = 5m,
            SingleDoseQuantity = 1m,
            SingleDoseUnit = "viên"
        });

        _db.MedicationDispenses.Add(new MedicationDispense
        {
            Id = 55,
            PrescriptionId = 1003,
            PreparedByStaffId = 10,
            DispensedByStaffId = 10,
            Status = "DISPENSED",
            PreparedAt = FixedNowUtc.AddHours(-2),
            DispensedAt = FixedNowUtc.AddHours(-1),
            IdempotencyKey = "disp-key-existing"
        });

        _db.DispenseItems.Add(new DispenseItem
        {
            Id = 77,
            DispenseId = 55,
            PrescriptionItemId = 30,
            MedicineBatchId = 101,
            PlannedQuantity = 5m,
            DispensedQuantity = 5m
        });

        _db.SaveChanges();
    }

    [Fact]
    public async Task GetDispenseContextAsync_ValidIssuedPrescription_ReturnsFullContext()
    {
        var result = await _service.GetDispenseContextAsync(10, "RX-2026-0001");

        Assert.NotNull(result);
        Assert.Equal(1001, result.PrescriptionId);
        Assert.Equal("RX-2026-0001", result.RxCode);
        Assert.Equal("Nguyễn Văn An", result.Receiver.FullName);
        Assert.True(result.CanDispense);
        Assert.Null(result.DisabledReason);
        Assert.Equal(2, result.Medications.Count);
        Assert.Contains(result.Medications, m => m.DrugName.Contains("Paracetamol"));
        Assert.Contains(result.Medications, m => m.DrugName.Contains("Amoxicillin"));
        Assert.Equal(3, result.Checks.Count);

        // FEFO batch allocated for Paracetamol is LOT-PARA-2027 (earliest non-expired, non-blocked)
        var paraItem = result.Medications.First(m => m.DrugName.Contains("Paracetamol"));
        Assert.Equal("LOT-PARA-2027", paraItem.BatchCode);

        // Audit log created
        var audit = await _db.AuditLogs.FirstOrDefaultAsync(a => a.Action == "DISPENSE_CONTEXT_VIEWED");
        Assert.NotNull(audit);
        Assert.Equal(10, audit.ActorUserId);
    }

    [Fact]
    public async Task GetDispenseContextAsync_PendingPayment_BlocksDispensing()
    {
        var result = await _service.GetDispenseContextAsync(10, "RX-2026-0002");

        Assert.NotNull(result);
        Assert.False(result.CanDispense);
        Assert.Contains("Chặn xuất kho", result.DisabledReason);
    }

    [Fact]
    public async Task GetDispenseContextAsync_AlreadyDispensed_DisablesAction()
    {
        var result = await _service.GetDispenseContextAsync(10, "RX-2026-0003");

        Assert.NotNull(result);
        Assert.False(result.CanDispense);
        Assert.Equal("DISPENSED", result.DispenseStatus);
        Assert.Contains("DISPENSED", result.DisabledReason);
    }

    [Fact]
    public async Task GetDispenseContextAsync_PrescriptionNotFound_ThrowsNotFoundException()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.GetDispenseContextAsync(10, "RX-NON-EXISTENT"));
    }

    [Fact]
    public async Task GetDispenseContextAsync_InactiveUser_ThrowsForbiddenException()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetDispenseContextAsync(11, "RX-2026-0001"));
    }

    [Fact]
    public async Task ConfirmDispenseAsync_HappyPath_DeductsStockAndUpdatesStatuses()
    {
        var initialBatch101 = await _db.MedicineBatches.AsNoTracking().FirstAsync(b => b.Id == 101);
        var initialBatch201 = await _db.MedicineBatches.AsNoTracking().FirstAsync(b => b.Id == 201);
        Assert.Equal(100m, initialBatch101.QuantityOnHand);
        Assert.Equal(50m, initialBatch201.QuantityOnHand);

        var request = new ConfirmDispenseRequest(
            ReceiverName: "Nguyễn Văn An",
            Relationship: "Bản thân",
            IsIdentityVerified: true,
            ChecksConfirmed: true,
            IdempotencyKey: "test-idempotency-001"
        );

        var result = await _service.ConfirmDispenseAsync(10, "RX-2026-0001", request);

        Assert.NotNull(result);
        Assert.Equal("DISPENSED & CLOSED", result.StatusLabel);
        Assert.Equal("RX-2026-0001", result.RxCode);
        Assert.Equal("Nguyễn Văn An", result.ReceiverName);
        Assert.Equal(2, result.Items.Count);

        // Stock deduction verified
        var updatedBatch101 = await _db.MedicineBatches.AsNoTracking().FirstAsync(b => b.Id == 101);
        var updatedBatch201 = await _db.MedicineBatches.AsNoTracking().FirstAsync(b => b.Id == 201);
        Assert.Equal(80m, updatedBatch101.QuantityOnHand); // 100 - 20
        Assert.Equal(40m, updatedBatch201.QuantityOnHand); // 50 - 10

        // Stock transactions verified
        var stockTxs = await _db.StockTransactions.AsNoTracking()
            .Where(st => st.PerformedByStaffId == 10)
            .ToListAsync();
        Assert.Equal(2, stockTxs.Count);
        Assert.All(stockTxs, st =>
        {
            Assert.Equal("DISPENSE", st.TransactionType);
            Assert.True(st.QuantityDelta < 0);
            Assert.Equal("RX-2026-0001", st.SourceReference);
        });

        // Prescription and Dispense status verified
        var updatedRx = await _db.Prescriptions.AsNoTracking().FirstAsync(p => p.Id == 1001);
        Assert.Equal("DISPENSED", updatedRx.Status);

        var dispense = await _db.MedicationDispenses.AsNoTracking().FirstAsync(md => md.PrescriptionId == 1001);
        Assert.Equal("DISPENSED", dispense.Status);
        Assert.Equal(10, dispense.DispensedByStaffId);
        Assert.NotNull(dispense.DispensedAt);

        // Audit log verified
        var audit = await _db.AuditLogs.FirstOrDefaultAsync(a => a.Action == "MEDICINE_DISPENSED");
        Assert.NotNull(audit);
        Assert.Equal(10, audit.ActorUserId);
    }

    [Fact]
    public async Task ConfirmDispenseAsync_Idempotency_ReturnsExistingResult()
    {
        var request = new ConfirmDispenseRequest(
            ReceiverName: "Nguyễn Văn An",
            IsIdentityVerified: true,
            ChecksConfirmed: true,
            IdempotencyKey: "disp-key-existing"
        );

        // Prescription 1003 already dispensed with this key
        var result = await _service.ConfirmDispenseAsync(10, "RX-2026-0003", request);

        Assert.NotNull(result);
        Assert.Equal("RX-2026-0003", result.RxCode);
        Assert.Equal("DISPENSED & CLOSED", result.StatusLabel);
    }

    [Fact]
    public async Task ConfirmDispenseAsync_AlreadyDispensedDifferentKey_ThrowsConflictException()
    {
        var request = new ConfirmDispenseRequest(
            ReceiverName: "Nguyễn Văn An",
            IsIdentityVerified: true,
            ChecksConfirmed: true,
            IdempotencyKey: "different-key-123"
        );

        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.ConfirmDispenseAsync(10, "RX-2026-0003", request));
    }

    [Fact]
    public async Task ConfirmDispenseAsync_UnverifiedIdentity_ThrowsValidationException()
    {
        var request = new ConfirmDispenseRequest(
            IsIdentityVerified: false,
            ChecksConfirmed: true
        );

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.ConfirmDispenseAsync(10, "RX-2026-0001", request));
    }

    [Fact]
    public async Task ConfirmDispenseAsync_UncheckedHandoverChecks_ThrowsValidationException()
    {
        var request = new ConfirmDispenseRequest(
            IsIdentityVerified: true,
            ChecksConfirmed: false
        );

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.ConfirmDispenseAsync(10, "RX-2026-0001", request));
    }

    [Fact]
    public async Task ConfirmDispenseAsync_PendingPayment_ThrowsConflictException()
    {
        var request = new ConfirmDispenseRequest(
            IsIdentityVerified: true,
            ChecksConfirmed: true
        );

        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.ConfirmDispenseAsync(10, "RX-2026-0002", request));
    }

    [Fact]
    public async Task ConfirmDispenseAsync_BlockedBatch_ThrowsConflictException()
    {
        var request = new ConfirmDispenseRequest(
            IsIdentityVerified: true,
            ChecksConfirmed: true,
            Items:
            [
                new ConfirmDispenseItemRequest(PrescriptionItemId: 10, MedicineBatchId: 103, DispensedQuantity: 20m)
            ]
        );

        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.ConfirmDispenseAsync(10, "RX-2026-0001", request));
    }

    [Fact]
    public async Task ConfirmDispenseAsync_ExpiredBatch_ThrowsConflictException()
    {
        var request = new ConfirmDispenseRequest(
            IsIdentityVerified: true,
            ChecksConfirmed: true,
            Items:
            [
                new ConfirmDispenseItemRequest(PrescriptionItemId: 10, MedicineBatchId: 104, DispensedQuantity: 20m)
            ]
        );

        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.ConfirmDispenseAsync(10, "RX-2026-0001", request));
    }

    [Fact]
    public async Task GetDispensingResultAsync_ValidPrescriptionCode_ReturnsResult()
    {
        var result = await _service.GetDispensingResultAsync(10, "RX-2026-0003");

        Assert.NotNull(result);
        Assert.Equal("RX-2026-0003", result.RxCode);
        Assert.Equal("DISPENSED & CLOSED", result.StatusLabel);
        Assert.Single(result.Items);
        Assert.Equal("LOT-PARA-2027", result.Items[0].BatchCode);

        var audit = await _db.AuditLogs.FirstOrDefaultAsync(a => a.Action == "DISPENSE_RESULT_VIEWED");
        Assert.NotNull(audit);
    }

    [Fact]
    public async Task GetDispensingResultAsync_ValidDispenseNumericId_ReturnsResult()
    {
        var result = await _service.GetDispensingResultAsync(10, "55");

        Assert.NotNull(result);
        Assert.Equal("RX-2026-0003", result.RxCode);
        Assert.Equal(55, result.DispenseId);
    }
}
