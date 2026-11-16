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

public sealed class PrepareMedicinesServiceTests : IDisposable
{
    private sealed class FixedTimeProvider(DateTimeOffset nowUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => nowUtc;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class PrepareTestDbContext : DbContext, IApplicationDbContext
    {
        public PrepareTestDbContext(DbContextOptions<PrepareTestDbContext> options)
            : base(options)
        {
        }

        private static NotSupportedException NotUsed() =>
            new("Not needed for PrepareMedicinesService unit tests.");

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
        public DbSet<VisitDiagnosis> VisitDiagnoses => Set<VisitDiagnosis>();
        public DbSet<Icd10Code> Icd10Codes => Set<Icd10Code>();
        public DbSet<MedicalRecord> MedicalRecords => Set<MedicalRecord>();
        public DbSet<MedicalRecordRevision> MedicalRecordRevisions => Set<MedicalRecordRevision>();

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
            modelBuilder.Ignore<ClinicalAddendum>();
            modelBuilder.Ignore<DiagnosticOrder>();
            modelBuilder.Ignore<DiagnosticResult>();
            modelBuilder.Ignore<DiagnosticResultVersion>();
            modelBuilder.Ignore<QueueServicePoint>();
            modelBuilder.Ignore<QueueFairnessState>();
            modelBuilder.Entity<User>().HasKey(u => u.Id);
            modelBuilder.Entity<Role>().HasKey(r => r.Id);
            modelBuilder.Entity<Permission>().HasKey(p => p.Id);
            modelBuilder.Entity<UserRole>().HasKey(ur => new { ur.UserId, ur.RoleId });
            modelBuilder.Entity<RolePermission>().HasKey(rp => new { rp.RoleId, rp.PermissionId });
            modelBuilder.Entity<Patient>().HasKey(p => p.Id);
            modelBuilder.Entity<Staff>().HasKey(s => s.Id);
            modelBuilder.Entity<Doctor>().HasKey(d => d.Id);
            modelBuilder.Entity<AuditLog>().HasKey(a => a.Id);
            modelBuilder.Entity<Hospital>().HasKey(h => h.Id);
            modelBuilder.Entity<Department>().HasKey(d => d.Id);
            modelBuilder.Entity<HospitalRoom>().HasKey(hr => hr.Id);
            modelBuilder.Entity<Specialty>().HasKey(s => s.Id);
            modelBuilder.Entity<Visit>().HasKey(v => v.Id);
            modelBuilder.Entity<Payment>().HasKey(p => p.Id);
            modelBuilder.Entity<Medicine>().HasKey(m => m.Id);
            modelBuilder.Entity<MedicineBatch>().HasKey(mb => mb.Id);
            modelBuilder.Entity<Prescription>().HasKey(p => p.Id);
            modelBuilder.Entity<PrescriptionItem>().HasKey(pi => pi.Id);
            modelBuilder.Entity<MedicationDispense>().HasKey(md => md.Id);
            modelBuilder.Entity<DispenseItem>().HasKey(di => di.Id);
            modelBuilder.Entity<StockTransaction>().HasKey(st => st.Id);
            modelBuilder.Entity<VisitDiagnosis>().HasKey(vd => vd.Id);
            modelBuilder.Entity<Icd10Code>().HasKey(icd => icd.Id);
            modelBuilder.Entity<MedicalRecord>().HasKey(mr => mr.Id);
            modelBuilder.Entity<MedicalRecordRevision>().HasKey(mrr => mrr.Id);
        }
    }

    private readonly PrepareTestDbContext _db;
    private readonly FixedTimeProvider _timeProvider;
    private readonly PrepareMedicinesService _service;
    private readonly DateTimeOffset _fixedNow = new(2026, 11, 12, 9, 30, 0, TimeSpan.Zero);

    public PrepareMedicinesServiceTests()
    {
        var options = new DbContextOptionsBuilder<PrepareTestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        _db = new PrepareTestDbContext(options);
        _timeProvider = new FixedTimeProvider(_fixedNow);
        _service = new PrepareMedicinesService(_db, _timeProvider);

        SeedTestData();
    }

    public void Dispose()
    {
        _db.Database.EnsureDeleted();
        _db.Dispose();
    }

    private void SeedTestData()
    {
        _db.Hospitals.Add(new Hospital
        {
            Id = 1,
            HospitalCode = "HOSP01",
            Name = "Bệnh viện Đa khoa Quốc tế MedFlow",
            IsActive = true,
        });

        _db.Hospitals.Add(new Hospital
        {
            Id = 2,
            HospitalCode = "HOSP02",
            Name = "Bệnh viện Cơ sở 2",
            IsActive = true,
        });

        _db.Departments.Add(new Department
        {
            Id = 1,
            HospitalId = 1,
            DepartmentCode = "DEP-KKB",
            Name = "Khoa Khám bệnh Ngoại trú",
        });

        _db.HospitalRooms.Add(new HospitalRoom
        {
            Id = 101,
            HospitalId = 1,
            DepartmentId = 1,
            RoomCode = "204",
            Name = "Phòng khám 204",
        });

        _db.Users.AddRange(
            new User { Id = 10, Email = "pharmacist01@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 11, Email = "inactive_pharmacist@medflow.test", AccountStatus = AccountStatus.INACTIVE },
            new User { Id = 12, Email = "locked_pharmacist@medflow.test", AccountStatus = AccountStatus.ACTIVE, LockedUntil = _fixedNow.UtcDateTime.AddHours(2) },
            new User { Id = 13, Email = "doctor01@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 14, Email = "other_hospital_pharmacist@medflow.test", AccountStatus = AccountStatus.ACTIVE });

        _db.Roles.AddRange(
            new Role { Id = 1, Code = "PHARMACY_STAFF", Name = "Dược sĩ" },
            new Role { Id = 2, Code = "DOCTOR", Name = "Bác sĩ" });

        _db.UserRoles.AddRange(
            new UserRole { UserId = 10, RoleId = 1 },
            new UserRole { UserId = 11, RoleId = 1 },
            new UserRole { UserId = 12, RoleId = 1 },
            new UserRole { UserId = 13, RoleId = 2 },
            new UserRole { UserId = 14, RoleId = 1 });

        _db.Permissions.Add(new Permission { Id = 1, Code = "MEDICINE_DISPENSE" });
        _db.RolePermissions.Add(new RolePermission { RoleId = 1, PermissionId = 1 });

        _db.Staff.AddRange(
            new Staff { Id = 20, UserId = 10, HospitalId = 1, StaffCode = "STF-PHA-01", FullName = "Dược sĩ Nguyễn Văn Hòa", Status = "ACTIVE" },
            new Staff { Id = 21, UserId = 13, HospitalId = 1, StaffCode = "STF-DOC-01", FullName = "BS.CKII Trần Quốc Tuấn", Status = "ACTIVE" },
            new Staff { Id = 22, UserId = 14, HospitalId = 2, StaffCode = "STF-PHA-02", FullName = "Dược sĩ Lê Văn Khác", Status = "ACTIVE" });

        _db.Doctors.Add(new Doctor { Id = 30, StaffId = 21, HospitalId = 1, DoctorCode = "DOC-01" });

        _db.Patients.Add(new Patient
        {
            Id = 50,
            PatientCode = "PAT-2024-0108",
            FullName = "Nguyễn Văn An",
            Gender = "MALE",
            DateOfBirth = new DateOnly(1990, 5, 20),
            Status = "ACTIVE",
        });

        _db.Visits.Add(new Visit
        {
            Id = 60,
            VisitCode = "VIS-2026-0415-0108",
            PatientId = 50,
            AssignedDoctorId = 30,
            ExaminationRoomId = 101,
            Status = "COMPLETED",
        });

        _db.VisitDiagnoses.AddRange(
            new VisitDiagnosis
            {
                Id = 1,
                VisitId = 60,
                IsPrimary = true,
                CodeSnapshot = "K29.5",
                NameSnapshot = "Viêm dạ dày mạn tính thể teo (HP+)",
            },
            new VisitDiagnosis
            {
                Id = 2,
                VisitId = 60,
                IsPrimary = false,
                CodeSnapshot = "K21.0",
                NameSnapshot = "GERD độ A (Trào ngược dạ dày thực quản)",
            });

        _db.MedicalRecords.Add(new MedicalRecord
        {
            Id = 1,
            VisitId = 60,
            Status = "FINALIZED",
        });

        _db.MedicalRecordRevisions.Add(new MedicalRecordRevision
        {
            Id = 1,
            MedicalRecordId = 1,
            RevisionNo = 1,
            RelevantHistoryAllergies = "Dị ứng Penicillin, NSAIDs",
        });

        _db.Payments.Add(new Payment
        {
            Id = 1,
            VisitId = 60,
            Status = "COMPLETED",
            Amount = 250000,
        });

        _db.Medicines.AddRange(
            new Medicine
            {
                Id = 1,
                HospitalId = 1,
                MedicineCode = "MED-001",
                Name = "Nexium 40mg",
                ActiveIngredient = "Esomeprazole",
                Strength = "40mg",
                DosageForm = "Viên nén bao tan trong ruột",
                DispensingUnit = "viên",
                IsActive = true,
            },
            new Medicine
            {
                Id = 2,
                HospitalId = 1,
                MedicineCode = "MED-002",
                Name = "Bismuth subsalicylate 300mg",
                ActiveIngredient = "Bismuth subsalicylate",
                Strength = "300mg",
                DosageForm = "Viên nhai",
                DispensingUnit = "viên",
                IsActive = true,
            },
            new Medicine
            {
                Id = 3,
                HospitalId = 2,
                MedicineCode = "MED-003",
                Name = "Thuốc cơ sở khác",
                IsActive = true,
            });

        var todayDate = DateOnly.FromDateTime(_fixedNow.UtcDateTime);

        _db.MedicineBatches.AddRange(
            // Nexium batches (FEFO test)
            new MedicineBatch
            {
                Id = 10,
                MedicineId = 1,
                BatchNumber = "NX-2027",
                ExpiryDate = todayDate.AddMonths(12), // 2027
                QuantityOnHand = 200,
                IsBlocked = false,
            },
            new MedicineBatch
            {
                Id = 11,
                MedicineId = 1,
                BatchNumber = "NX-2026-EARLIEST",
                ExpiryDate = todayDate.AddMonths(3), // Earlier expiry -> FEFO recommended!
                QuantityOnHand = 150,
                IsBlocked = false,
            },
            new MedicineBatch
            {
                Id = 12,
                MedicineId = 1,
                BatchNumber = "NX-EXPIRED",
                ExpiryDate = todayDate.AddDays(-5), // Expired!
                QuantityOnHand = 100,
                IsBlocked = false,
            },
            new MedicineBatch
            {
                Id = 13,
                MedicineId = 1,
                BatchNumber = "NX-BLOCKED",
                ExpiryDate = todayDate.AddMonths(6),
                QuantityOnHand = 100,
                IsBlocked = true, // Blocked!
            },
            new MedicineBatch
            {
                Id = 14,
                MedicineId = 1,
                BatchNumber = "NX-ZERO-STOCK",
                ExpiryDate = todayDate.AddMonths(2),
                QuantityOnHand = 0, // Zero stock
                IsBlocked = false,
            },
            // Bismuth batch
            new MedicineBatch
            {
                Id = 20,
                MedicineId = 2,
                BatchNumber = "BM-2027",
                ExpiryDate = todayDate.AddMonths(18),
                QuantityOnHand = 500,
                IsBlocked = false,
            });

        _db.Prescriptions.Add(new Prescription
        {
            Id = 100,
            PrescriptionCode = "RX-2026-0415-0088",
            VisitId = 60,
            PrescribingDoctorId = 30,
            FulfillmentChoice = "HOSPITAL",
            Status = "ISSUED",
            IssuedAt = _fixedNow.UtcDateTime.AddMinutes(-30),
            CreatedAt = _fixedNow.UtcDateTime.AddMinutes(-30),
        });

        _db.PrescriptionItems.AddRange(
            new PrescriptionItem
            {
                Id = 201,
                PrescriptionId = 100,
                MedicineId = 1,
                PrescribedQuantity = 28,
                Instructions = "Uống 1 viên/lần x 2 lần/ngày trước ăn 30 phút",
            },
            new PrescriptionItem
            {
                Id = 202,
                PrescriptionId = 100,
                MedicineId = 2,
                PrescribedQuantity = 56,
                Instructions = "Nhai 2 viên/lần x 2 lần/ngày trước ăn 30 phút",
            });

        _db.SaveChanges();
    }

    [Fact]
    public async Task GetPreparationContext_HappyPath_ReturnsContextWithFefoSorting()
    {
        var context = await _service.GetPreparationContextAsync(10, "RX-2026-0415-0088");

        Assert.NotNull(context);
        Assert.Equal(100, context.PrescriptionId);
        Assert.Equal("RX-2026-0415-0088", context.RxCode);
        Assert.Equal("Nguyễn Văn An", context.PatientName);
        Assert.Equal("Nam", context.Gender);
        Assert.Equal("BS.CKII Trần Quốc Tuấn", context.DoctorName);
        Assert.Contains("K29.5", context.PrimaryDiagnosis);
        Assert.Contains("K21.0", context.SecondaryDiagnosis);
        Assert.Contains("Penicillin", context.CriticalAllergy);
        Assert.Equal("VIỆN PHÍ ĐÃ THANH TOÁN", context.PaymentBadge);
        Assert.Equal("ISSUED", context.Status);

        Assert.Equal(2, context.Items.Count);

        // Check Nexium item & FEFO batch selection
        var nexiumItem = context.Items.First(i => i.MedicineId == 1);
        Assert.Equal("Nexium 40mg", nexiumItem.DrugName);
        Assert.Equal(28, nexiumItem.PrescribedQty);
        Assert.Equal(28, nexiumItem.PickedQty);

        // Expired (id:12) and blocked (id:13) batches MUST be excluded
        Assert.DoesNotContain(nexiumItem.AvailableBatches, b => b.BatchCode == "NX-EXPIRED");
        Assert.DoesNotContain(nexiumItem.AvailableBatches, b => b.BatchCode == "NX-BLOCKED");

        // Valid batches should be present
        Assert.Contains(nexiumItem.AvailableBatches, b => b.BatchCode == "NX-2026-EARLIEST");
        Assert.Contains(nexiumItem.AvailableBatches, b => b.BatchCode == "NX-2027");

        // Earliest expiry non-blocked batch with stock > 0 should be FEFO recommended
        var fefoRecommended = nexiumItem.AvailableBatches.First(b => b.IsFefoRecommended);
        Assert.Equal("NX-2026-EARLIEST", fefoRecommended.BatchCode);
        Assert.Equal("NX-2026-EARLIEST", nexiumItem.SelectedBatchCode);

        // 4 GPP Checks
        Assert.Equal(4, context.GppChecks.Count);

        // Audit Log verification
        var audit = await _db.AuditLogs.FirstOrDefaultAsync(a => a.Action == "PRESCRIPTION_PREPARE_VIEW");
        Assert.NotNull(audit);
        Assert.Equal("100", audit.EntityId);
    }

    [Fact]
    public async Task GetPreparationContext_ByNumericId_ReturnsSuccessfully()
    {
        var context = await _service.GetPreparationContextAsync(10, "100");

        Assert.NotNull(context);
        Assert.Equal("RX-2026-0415-0088", context.RxCode);
    }

    [Fact]
    public async Task PreparePrescription_HappyPath_CreatesDispenseAndTransitionsToReady()
    {
        var request = new PreparePrescriptionRequest(
            Items:
            [
                new PrepareMedicineItemRequest(
                    PrescriptionItemId: 201,
                    MedicineBatchId: 11,
                    BatchCode: "NX-2026-EARLIEST",
                    PickedQuantity: 28),
                new PrepareMedicineItemRequest(
                    PrescriptionItemId: 202,
                    MedicineBatchId: 20,
                    BatchCode: "BM-2027",
                    PickedQuantity: 56),
            ],
            Notes: "Đã kiểm tra đối chiếu GPP đủ 4 tiêu chuẩn");

        var response = await _service.PreparePrescriptionAsync(10, "RX-2026-0415-0088", request);

        Assert.NotNull(response);
        Assert.Equal(100, response.PrescriptionId);
        Assert.Equal("READY", response.Status);
        Assert.Equal(2, response.PreparedItemCount);
        Assert.True(response.DispenseId > 0);

        // Check Prescription entity updated
        var p = await _db.Prescriptions.FindAsync(100L);
        Assert.NotNull(p);
        Assert.Equal("READY", p.Status);

        // Check MedicationDispense created
        var md = await _db.MedicationDispenses.FirstOrDefaultAsync(d => d.PrescriptionId == 100);
        Assert.NotNull(md);
        Assert.Equal("READY", md.Status);
        Assert.Equal(20, md.PreparedByStaffId);
        Assert.NotNull(md.PreparedAt);

        // Check DispenseItems created
        var items = await _db.DispenseItems.Where(di => di.DispenseId == md.Id).ToListAsync();
        Assert.Equal(2, items.Count);

        var nexiumDisp = items.First(i => i.PrescriptionItemId == 201);
        Assert.Equal(11, nexiumDisp.MedicineBatchId);
        Assert.Equal(28, nexiumDisp.PlannedQuantity);
        Assert.Equal(0, nexiumDisp.DispensedQuantity);

        // Check Audit Log
        var audit = await _db.AuditLogs.FirstOrDefaultAsync(a => a.Action == "PRESCRIPTION_PREPARED");
        Assert.NotNull(audit);
        Assert.Equal("100", audit.EntityId);
    }

    [Fact]
    public async Task PreparePrescription_WithValidShortageReason_Succeeds()
    {
        var request = new PreparePrescriptionRequest(
            Items:
            [
                new PrepareMedicineItemRequest(
                    PrescriptionItemId: 201,
                    MedicineBatchId: 11,
                    BatchCode: "NX-2026-EARLIEST",
                    PickedQuantity: 14, // Prescribed was 28
                    ShortageReason: "Kho dược tạm hết số lượng lớn, cấp trước 14 viên"),
                new PrepareMedicineItemRequest(
                    PrescriptionItemId: 202,
                    MedicineBatchId: 20,
                    BatchCode: "BM-2027",
                    PickedQuantity: 56),
            ]);

        var response = await _service.PreparePrescriptionAsync(10, "RX-2026-0415-0088", request);

        Assert.NotNull(response);
        Assert.Equal("READY", response.Status);
    }

    [Fact]
    public async Task PreparePrescription_MismatchedQuantityWithoutShortageReason_ThrowsValidationException()
    {
        var request = new PreparePrescriptionRequest(
            Items:
            [
                new PrepareMedicineItemRequest(
                    PrescriptionItemId: 201,
                    MedicineBatchId: 11,
                    BatchCode: "NX-2026-EARLIEST",
                    PickedQuantity: 14), // Mismatched without shortage reason!
                new PrepareMedicineItemRequest(
                    PrescriptionItemId: 202,
                    MedicineBatchId: 20,
                    BatchCode: "BM-2027",
                    PickedQuantity: 56),
            ]);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _service.PreparePrescriptionAsync(10, "RX-2026-0415-0088", request));

        Assert.Contains("chưa khớp y lệnh", ex.Message);
    }

    [Fact]
    public async Task PreparePrescription_MissingItems_ThrowsValidationException()
    {
        var request = new PreparePrescriptionRequest(
            Items:
            [
                new PrepareMedicineItemRequest(
                    PrescriptionItemId: 201,
                    MedicineBatchId: 11,
                    BatchCode: "NX-2026-EARLIEST",
                    PickedQuantity: 28),
                // Missing item 202!
            ]);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _service.PreparePrescriptionAsync(10, "RX-2026-0415-0088", request));

        Assert.Contains("chưa được soạn đủ", ex.Message);
    }

    [Fact]
    public async Task PreparePrescription_BlockedBatch_ThrowsConflictException()
    {
        var request = new PreparePrescriptionRequest(
            Items:
            [
                new PrepareMedicineItemRequest(
                    PrescriptionItemId: 201,
                    MedicineBatchId: 13, // Blocked batch!
                    BatchCode: "NX-BLOCKED",
                    PickedQuantity: 28),
                new PrepareMedicineItemRequest(
                    PrescriptionItemId: 202,
                    MedicineBatchId: 20,
                    BatchCode: "BM-2027",
                    PickedQuantity: 56),
            ]);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            _service.PreparePrescriptionAsync(10, "RX-2026-0415-0088", request));

        Assert.Contains("tạm khóa", ex.Message);
    }

    [Fact]
    public async Task PreparePrescription_ExpiredBatch_ThrowsConflictException()
    {
        var request = new PreparePrescriptionRequest(
            Items:
            [
                new PrepareMedicineItemRequest(
                    PrescriptionItemId: 201,
                    MedicineBatchId: 12, // Expired batch!
                    BatchCode: "NX-EXPIRED",
                    PickedQuantity: 28),
                new PrepareMedicineItemRequest(
                    PrescriptionItemId: 202,
                    MedicineBatchId: 20,
                    BatchCode: "BM-2027",
                    PickedQuantity: 56),
            ]);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            _service.PreparePrescriptionAsync(10, "RX-2026-0415-0088", request));

        Assert.Contains("hết hạn sử dụng", ex.Message);
    }

    [Fact]
    public async Task PreparePrescription_InsufficientStock_ThrowsConflictException()
    {
        var request = new PreparePrescriptionRequest(
            Items:
            [
                new PrepareMedicineItemRequest(
                    PrescriptionItemId: 201,
                    MedicineBatchId: 14, // Zero stock!
                    BatchCode: "NX-ZERO-STOCK",
                    PickedQuantity: 28),
                new PrepareMedicineItemRequest(
                    PrescriptionItemId: 202,
                    MedicineBatchId: 20,
                    BatchCode: "BM-2027",
                    PickedQuantity: 56),
            ]);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            _service.PreparePrescriptionAsync(10, "RX-2026-0415-0088", request));

        Assert.Contains("không đủ tồn kho", ex.Message);
    }

    [Fact]
    public async Task PreparePrescription_AlreadyDispensed_ThrowsConflictException()
    {
        var p = await _db.Prescriptions.FindAsync(100L);
        p!.Status = "DISPENSED";
        await _db.SaveChangesAsync();

        var request = new PreparePrescriptionRequest(
            Items:
            [
                new PrepareMedicineItemRequest(
                    PrescriptionItemId: 201,
                    MedicineBatchId: 11,
                    BatchCode: "NX-2026-EARLIEST",
                    PickedQuantity: 28),
                new PrepareMedicineItemRequest(
                    PrescriptionItemId: 202,
                    MedicineBatchId: 20,
                    BatchCode: "BM-2027",
                    PickedQuantity: 56),
            ]);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            _service.PreparePrescriptionAsync(10, "RX-2026-0415-0088", request));

        Assert.Contains("hoàn tất cấp phát", ex.Message);
    }

    [Fact]
    public async Task GetPreparationContext_HospitalIsolation_OtherHospitalStaff_ThrowsNotFound()
    {
        // User 14 belongs to Hospital 2
        var ex = await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.GetPreparationContextAsync(14, "RX-2026-0415-0088"));

        Assert.Contains("Prescription", ex.Message);
    }

    [Fact]
    public async Task ValidateAccess_UserSuspended_ThrowsForbiddenException()
    {
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetPreparationContextAsync(11, "RX-2026-0415-0088"));

        Assert.Contains("không ở trạng thái hoạt động", ex.Message);
    }

    [Fact]
    public async Task ValidateAccess_UserLocked_ThrowsForbiddenException()
    {
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetPreparationContextAsync(12, "RX-2026-0415-0088"));

        Assert.Contains("tạm khóa", ex.Message);
    }

    [Fact]
    public async Task ValidateAccess_MissingPharmacyRole_ThrowsForbiddenException()
    {
        // User 13 is DOCTOR
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetPreparationContextAsync(13, "RX-2026-0415-0088"));

        Assert.Contains("không có quyền truy cập", ex.Message);
    }

    [Fact]
    public async Task PreparePrescription_RePrepare_UpdatesExistingDispenseWithoutDuplicates()
    {
        var request1 = new PreparePrescriptionRequest(
            Items:
            [
                new PrepareMedicineItemRequest(201, 11, "NX-2026-EARLIEST", 28),
                new PrepareMedicineItemRequest(202, 20, "BM-2027", 56),
            ]);

        var res1 = await _service.PreparePrescriptionAsync(10, "RX-2026-0415-0088", request1);

        // Second preparation with switched batch for Nexium
        var request2 = new PreparePrescriptionRequest(
            Items:
            [
                new PrepareMedicineItemRequest(201, 10, "NX-2027", 28),
                new PrepareMedicineItemRequest(202, 20, "BM-2027", 56),
            ],
            Notes: "Cập nhật lại lô thuốc Nexium");

        var res2 = await _service.PreparePrescriptionAsync(10, "RX-2026-0415-0088", request2);

        Assert.Equal(res1.DispenseId, res2.DispenseId);

        var totalDispenses = await _db.MedicationDispenses.CountAsync(d => d.PrescriptionId == 100);
        Assert.Equal(1, totalDispenses);

        var totalDispenseItems = await _db.DispenseItems.CountAsync(di => di.DispenseId == res1.DispenseId);
        Assert.Equal(2, totalDispenseItems);

        var nexiumDisp = await _db.DispenseItems.FirstAsync(di => di.DispenseId == res1.DispenseId && di.PrescriptionItemId == 201);
        Assert.Equal(10, nexiumDisp.MedicineBatchId); // Updated to batch 10!
    }

    [Fact]
    public async Task PreparePrescription_EmptyRequestItems_ThrowsValidationException()
    {
        var request = new PreparePrescriptionRequest(Items: []);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _service.PreparePrescriptionAsync(10, "RX-2026-0415-0088", request));

        Assert.Contains("không được để trống", ex.Message);
    }

    [Fact]
    public async Task PreparePrescription_UnknownPrescriptionItemId_ThrowsValidationException()
    {
        var request = new PreparePrescriptionRequest(
            Items:
            [
                new PrepareMedicineItemRequest(9999, 11, "NX-2026-EARLIEST", 28),
                new PrepareMedicineItemRequest(202, 20, "BM-2027", 56),
            ]);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _service.PreparePrescriptionAsync(10, "RX-2026-0415-0088", request));

        Assert.Contains("không thuộc đơn thuốc", ex.Message);
    }

    [Fact]
    public async Task PreparePrescription_ByBatchCodeOnly_ResolvesBatchSuccessfully()
    {
        var request = new PreparePrescriptionRequest(
            Items:
            [
                new PrepareMedicineItemRequest(
                    PrescriptionItemId: 201,
                    MedicineBatchId: null, // Null ID, resolved by code!
                    BatchCode: "NX-2026-EARLIEST",
                    PickedQuantity: 28),
                new PrepareMedicineItemRequest(
                    PrescriptionItemId: 202,
                    MedicineBatchId: null,
                    BatchCode: "BM-2027",
                    PickedQuantity: 56),
            ]);

        var response = await _service.PreparePrescriptionAsync(10, "RX-2026-0415-0088", request);

        Assert.NotNull(response);
        Assert.Equal("READY", response.Status);
    }

    [Fact]
    public async Task GetPreparationContext_ReplacedPrescription_ThrowsNotFoundException()
    {
        // Add a newer replacement prescription that replaces RX 100
        _db.Prescriptions.Add(new Prescription
        {
            Id = 101,
            PrescriptionCode = "RX-2026-0415-0088-V2",
            VisitId = 60,
            ReplacesPrescriptionId = 100,
            Status = "ISSUED",
        });
        await _db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.GetPreparationContextAsync(10, "RX-2026-0415-0088"));

        Assert.Contains("Prescription", ex.Message);
    }

    [Fact]
    public async Task GetPreparationContext_UnpaidVisit_ReturnsChoThanhToanBadge()
    {
        // Change payment status to PENDING
        var payment = await _db.Payments.FirstAsync(p => p.VisitId == 60);
        payment.Status = "PENDING";
        await _db.SaveChangesAsync();

        var context = await _service.GetPreparationContextAsync(10, "RX-2026-0415-0088");

        Assert.Equal("VIỆN PHÍ CHỜ THANH TOÁN / TỰ NGUYỆN", context.PaymentBadge);
    }
}
