using MedFlow.Application.DTOs.Pharmacy;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Pharmacy;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.UnitTests.Pharmacy;

public sealed class PharmacyDashboardServiceTests : IDisposable
{
    private sealed class FixedTimeProvider(DateTimeOffset nowUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => nowUtc;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class PharmacyTestDbContext : DbContext, IApplicationDbContext
    {
        public PharmacyTestDbContext(DbContextOptions<PharmacyTestDbContext> options)
            : base(options)
        {
        }

        private static NotSupportedException NotUsed() =>
            new("Not needed for PharmacyDashboardService unit tests.");

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
        public DbSet<AuditLog> AuditLogs => throw NotUsed();
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
            modelBuilder.Ignore<AuditLog>();
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
        }
    }

    private static readonly DateTime FixedNowUtc = new(2026, 10, 23, 9, 30, 0, DateTimeKind.Utc);

    private readonly PharmacyTestDbContext _dbContext;
    private readonly PharmacyDashboardService _service;

    public PharmacyDashboardServiceTests()
    {
        var options = new DbContextOptionsBuilder<PharmacyTestDbContext>()
            .UseInMemoryDatabase(databaseName: $"PharmacyDashboardTests_{Guid.NewGuid()}")
            .Options;

        _dbContext = new PharmacyTestDbContext(options);
        SeedData(_dbContext);

        var timeProvider = new FixedTimeProvider(new DateTimeOffset(FixedNowUtc));
        _service = new PharmacyDashboardService(_dbContext, timeProvider);
    }

    private static void SeedData(PharmacyTestDbContext db)
    {
        db.Hospitals.AddRange(
            new Hospital { Id = 1, HospitalCode = "HOS-DEMO-01", Name = "Bệnh viện MedFlow (giả lập)", IsActive = true },
            new Hospital { Id = 2, HospitalCode = "HOS-DEMO-02", Name = "Bệnh viện Cơ sở 2", IsActive = true },
            new Hospital { Id = 3, HospitalCode = "HOS-OFF-03", Name = "Bệnh viện Ngừng Hoạt Động", IsActive = false });

        db.Departments.AddRange(
            new Department { Id = 1, HospitalId = 1, DepartmentCode = "DEP-001", Name = "Khoa Nội Tiêu hóa", IsActive = true },
            new Department { Id = 2, HospitalId = 1, DepartmentCode = "DEP-002", Name = "Khoa Tim mạch", IsActive = true },
            new Department { Id = 3, HospitalId = 1, DepartmentCode = "DEP-PHA", Name = "Khoa Dược ngoại trú", IsActive = true },
            new Department { Id = 4, HospitalId = 2, DepartmentCode = "DEP-201", Name = "Khoa Ngoại Cơ sở 2", IsActive = true });

        db.HospitalRooms.AddRange(
            new HospitalRoom
            {
                Id = 101,
                HospitalId = 1,
                DepartmentId = 1,
                RoomCode = "P.101",
                Name = "Phòng khám Nội Tiêu hóa 1",
                RoomType = "EXAMINATION",
                Status = "ACTIVE",
            },
            new HospitalRoom
            {
                Id = 102,
                HospitalId = 1,
                DepartmentId = 2,
                RoomCode = "P.102",
                Name = "Phòng khám Tim mạch 1",
                RoomType = "EXAMINATION",
                Status = "ACTIVE",
            },
            new HospitalRoom
            {
                Id = 201,
                HospitalId = 2,
                DepartmentId = 4,
                RoomCode = "P.201",
                Name = "Phòng khám Ngoại Cơ sở 2",
                RoomType = "EXAMINATION",
                Status = "ACTIVE",
            });

        db.Specialties.AddRange(
            new Specialty { Id = 1, SpecialtyCode = "GI", Name = "Nội Tiêu hóa", IsActive = true },
            new Specialty { Id = 2, SpecialtyCode = "CARD", Name = "Tim mạch", IsActive = true });

        db.Roles.AddRange(
            new Role { Id = 1, Code = "PATIENT", Name = "Bệnh nhân" },
            new Role { Id = 2, Code = "RECEPTION", Name = "Tiếp đón" },
            new Role { Id = 3, Code = "DOCTOR", Name = "Bác sĩ" },
            new Role { Id = 4, Code = "DIAGNOSTIC_STAFF", Name = "Cận lâm sàng" },
            new Role { Id = 5, Code = "PHARMACY_STAFF", Name = "Nhân viên nhà thuốc" },
            new Role { Id = 6, Code = "HOSPITAL_ADMIN", Name = "Quản trị bệnh viện" },
            new Role { Id = 7, Code = "SYSTEM_ADMIN", Name = "Quản trị hệ thống" },
            new Role { Id = 8, Code = "PHARMACY_READONLY", Name = "Dược sĩ tra cứu" });

        db.Permissions.AddRange(
            new Permission { Id = 1, Code = "MEDICINE_DISPENSE" },
            new Permission { Id = 2, Code = "PAYMENT_VIEW" },
            new Permission { Id = 3, Code = "PATIENT_PROFILE_READ" },
            new Permission { Id = 4, Code = "HOSPITAL_CONFIG" });

        db.RolePermissions.AddRange(
            new RolePermission { RoleId = 5, PermissionId = 1 },
            new RolePermission { RoleId = 5, PermissionId = 2 },
            new RolePermission { RoleId = 6, PermissionId = 4 });

        db.Users.AddRange(
            new User { Id = 9, Email = "pharmacy_staff01@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 10, Email = "pharmacy_staff02@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 11, Email = "hospital_admin01@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 12, Email = "patient01@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 13, Email = "doctor01@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 14, Email = "inactive_pharmacist@medflow.test", AccountStatus = AccountStatus.INACTIVE },
            new User
            {
                Id = 15,
                Email = "locked_pharmacist@medflow.test",
                AccountStatus = AccountStatus.ACTIVE,
                LockedUntil = FixedNowUtc.AddMinutes(30),
            },
            new User { Id = 16, Email = "no_staff_pharmacist@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 17, Email = "no_hospital_pharmacist@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 18, Email = "inactive_staff_pharmacist@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 19, Email = "inactive_hosp_pharmacist@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 30, Email = "doc_staff1@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 31, Email = "doc_staff2@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 32, Email = "doc_staff_h2@medflow.test", AccountStatus = AccountStatus.ACTIVE });

        db.UserRoles.AddRange(
            new UserRole { UserId = 9, RoleId = 5 },
            new UserRole { UserId = 10, RoleId = 5 },
            new UserRole { UserId = 11, RoleId = 6 },
            new UserRole { UserId = 12, RoleId = 1 },
            new UserRole { UserId = 13, RoleId = 3 },
            new UserRole { UserId = 14, RoleId = 5 },
            new UserRole { UserId = 15, RoleId = 5 },
            new UserRole { UserId = 16, RoleId = 5 },
            new UserRole { UserId = 17, RoleId = 5 },
            new UserRole { UserId = 18, RoleId = 5 },
            new UserRole { UserId = 19, RoleId = 5 });

        db.Staff.AddRange(
            new Staff
            {
                Id = 7,
                UserId = 9,
                HospitalId = 1,
                DepartmentId = 3,
                StaffCode = "STF-007",
                FullName = "DS. Trần Thị Mai",
                Status = "ACTIVE",
            },
            new Staff
            {
                Id = 8,
                UserId = 10,
                HospitalId = 2,
                DepartmentId = 4,
                StaffCode = "STF-008",
                FullName = "DS. Cơ sở 2",
                Status = "ACTIVE",
            },
            new Staff
            {
                Id = 17,
                UserId = 17,
                HospitalId = null,
                DepartmentId = null,
                StaffCode = "STF-NO-HOSP",
                FullName = "DS. Chưa Gán Cơ Sở",
                Status = "ACTIVE",
            },
            new Staff
            {
                Id = 18,
                UserId = 18,
                HospitalId = 1,
                DepartmentId = 3,
                StaffCode = "STF-INACT",
                FullName = "DS. Ngừng Công Tác",
                Status = "INACTIVE",
            },
            new Staff
            {
                Id = 19,
                UserId = 19,
                HospitalId = 3,
                DepartmentId = null,
                StaffCode = "STF-HOSP3",
                FullName = "DS. Cơ Sở Đóng",
                Status = "ACTIVE",
            },
            new Staff
            {
                Id = 30,
                UserId = 30,
                HospitalId = 1,
                DepartmentId = 1,
                StaffCode = "STF-DOC-01",
                FullName = "Nguyễn Minh Đức",
                Status = "ACTIVE",
            },
            new Staff
            {
                Id = 31,
                UserId = 31,
                HospitalId = 1,
                DepartmentId = 2,
                StaffCode = "STF-DOC-02",
                FullName = "Lê Hoàng Tâm",
                Status = "ACTIVE",
            },
            new Staff
            {
                Id = 32,
                UserId = 32,
                HospitalId = 2,
                DepartmentId = 4,
                StaffCode = "STF-DOC-H2",
                FullName = "Trần Ngoại Khoa",
                Status = "ACTIVE",
            });

        db.Doctors.AddRange(
            new Doctor
            {
                Id = 1,
                StaffId = 30,
                HospitalId = 1,
                DoctorCode = "DOC-001",
                SpecialtyId = 1,
                ProfessionalTitle = "BS.CKI",
                IsActive = true,
            },
            new Doctor
            {
                Id = 2,
                StaffId = 31,
                HospitalId = 1,
                DoctorCode = "DOC-002",
                SpecialtyId = 2,
                ProfessionalTitle = "ThS.BS",
                IsActive = true,
            },
            new Doctor
            {
                Id = 3,
                StaffId = 32,
                HospitalId = 2,
                DoctorCode = "DOC-201",
                SpecialtyId = 1,
                ProfessionalTitle = "BS.",
                IsActive = true,
            });

        db.Patients.AddRange(
            new Patient
            {
                Id = 1,
                PatientCode = "PAT-2041",
                FullName = "Nguyễn Văn An",
                DateOfBirth = new DateOnly(1972, 5, 10),
                Status = "ACTIVE",
            },
            new Patient
            {
                Id = 2,
                PatientCode = "PAT-1988",
                FullName = "Trần Thị Bích",
                DateOfBirth = new DateOnly(1959, 8, 20),
                Status = "ACTIVE",
            },
            new Patient
            {
                Id = 3,
                PatientCode = "PAT-2105",
                FullName = "Lê Hoàng Nam",
                DateOfBirth = new DateOnly(1995, 2, 14),
                Status = "ACTIVE",
            },
            new Patient
            {
                Id = 4,
                PatientCode = "PAT-1790",
                FullName = "Võ Thị Ngọc Lan",
                DateOfBirth = new DateOnly(1997, 11, 3),
                Status = "ACTIVE",
            },
            new Patient
            {
                Id = 5,
                PatientCode = "PAT-H2-99",
                FullName = "Bệnh nhân Cơ sở 2",
                DateOfBirth = new DateOnly(1985, 1, 1),
                Status = "ACTIVE",
            });

        db.Visits.AddRange(
            new Visit
            {
                Id = 1,
                VisitCode = "VIS-001",
                PatientId = 1,
                AssignedDoctorId = 1,
                ExaminationRoomId = 101,
                Status = "COMPLETED",
            },
            new Visit
            {
                Id = 2,
                VisitCode = "VIS-002",
                PatientId = 2,
                AssignedDoctorId = 2,
                ExaminationRoomId = 102,
                Status = "COMPLETED",
            },
            new Visit
            {
                Id = 3,
                VisitCode = "VIS-003",
                PatientId = 3,
                AssignedDoctorId = 1,
                ExaminationRoomId = 101,
                Status = "COMPLETED",
            },
            new Visit
            {
                Id = 4,
                VisitCode = "VIS-004",
                PatientId = 4,
                AssignedDoctorId = 2,
                ExaminationRoomId = 102,
                Status = "COMPLETED",
            },
            new Visit
            {
                Id = 20,
                VisitCode = "VIS-H2-01",
                PatientId = 5,
                AssignedDoctorId = 3,
                ExaminationRoomId = 201,
                Status = "COMPLETED",
            });

        db.Payments.AddRange(
            new Payment { Id = 1, VisitId = 1, Amount = 250000m, Status = "PENDING" },
            new Payment { Id = 2, VisitId = 2, Amount = 300000m, Status = "PAID", PaidAt = FixedNowUtc.AddMinutes(-25) },
            new Payment { Id = 3, VisitId = 3, Amount = 180000m, Status = "PAID", PaidAt = FixedNowUtc.AddMinutes(-40) },
            new Payment { Id = 4, VisitId = 4, Amount = 320000m, Status = "PAID", PaidAt = FixedNowUtc.AddMinutes(-60) });

        db.Medicines.AddRange(
            new Medicine
            {
                Id = 1,
                HospitalId = 1,
                MedicineCode = "MED-001",
                Name = "Omeprazole",
                Strength = "20 mg",
                DispensingUnit = "Viên",
                IsActive = true,
            },
            new Medicine
            {
                Id = 2,
                HospitalId = 1,
                MedicineCode = "MED-006",
                Name = "Amlodipine",
                Strength = "5 mg",
                DispensingUnit = "Viên",
                IsActive = true,
            },
            new Medicine
            {
                Id = 3,
                HospitalId = 1,
                MedicineCode = "MED-008",
                Name = "Bisoprolol",
                Strength = "2.5 mg",
                DispensingUnit = "Viên",
                IsActive = true,
            },
            new Medicine
            {
                Id = 20,
                HospitalId = 2,
                MedicineCode = "MED-H2-01",
                Name = "Cefixime",
                Strength = "200 mg",
                DispensingUnit = "Viên",
                IsActive = true,
            });

        db.MedicineBatches.AddRange(
            new MedicineBatch
            {
                Id = 1,
                MedicineId = 1,
                BatchNumber = "LOT-OME-1126",
                ExpiryDate = new DateOnly(2026, 11, 15),
                QuantityOnHand = 120m,
                IsBlocked = false,
            },
            new MedicineBatch
            {
                Id = 2,
                MedicineId = 2,
                BatchNumber = "LOT-AML-LOW",
                ExpiryDate = new DateOnly(2027, 6, 30),
                QuantityOnHand = 24m,
                IsBlocked = false,
            },
            new MedicineBatch
            {
                Id = 3,
                MedicineId = 3,
                BatchNumber = "LOT-BIS-EXP",
                ExpiryDate = new DateOnly(2026, 10, 1),
                QuantityOnHand = 15m,
                IsBlocked = true,
            },
            new MedicineBatch
            {
                Id = 4,
                MedicineId = 3,
                BatchNumber = "LOT-BIS-2027",
                ExpiryDate = new DateOnly(2027, 8, 20),
                QuantityOnHand = 180m,
                IsBlocked = false,
            },
            new MedicineBatch
            {
                Id = 20,
                MedicineId = 20,
                BatchNumber = "LOT-H2-01",
                ExpiryDate = new DateOnly(2027, 12, 31),
                QuantityOnHand = 200m,
                IsBlocked = false,
            });

        db.Prescriptions.AddRange(
            new Prescription
            {
                Id = 1,
                PrescriptionCode = "RX-202610-0014",
                VisitId = 1,
                PrescribingDoctorId = 1,
                FulfillmentChoice = "HOSPITAL",
                Status = "PENDING",
                IssuedAt = FixedNowUtc.AddMinutes(-15),
                CreatedAt = FixedNowUtc.AddMinutes(-20),
            },
            new Prescription
            {
                Id = 2,
                PrescriptionCode = "RX-202610-0013",
                VisitId = 2,
                PrescribingDoctorId = 2,
                FulfillmentChoice = "HOSPITAL",
                Status = "PREPARING",
                IssuedAt = FixedNowUtc.AddMinutes(-22),
                CreatedAt = FixedNowUtc.AddMinutes(-25),
            },
            new Prescription
            {
                Id = 3,
                PrescriptionCode = "RX-202610-0011",
                VisitId = 3,
                PrescribingDoctorId = 1,
                FulfillmentChoice = "HOSPITAL",
                Status = "READY",
                IssuedAt = FixedNowUtc.AddMinutes(-35),
                CreatedAt = FixedNowUtc.AddMinutes(-40),
            },
            new Prescription
            {
                Id = 4,
                PrescriptionCode = "RX-202610-0006",
                VisitId = 4,
                PrescribingDoctorId = 2,
                FulfillmentChoice = "HOSPITAL",
                Status = "DISPENSED",
                IssuedAt = FixedNowUtc.AddMinutes(-68),
                CreatedAt = FixedNowUtc.AddMinutes(-75),
            },
            new Prescription
            {
                Id = 20,
                PrescriptionCode = "RX-H2-0099",
                VisitId = 20,
                PrescribingDoctorId = 3,
                FulfillmentChoice = "HOSPITAL",
                Status = "PENDING",
                IssuedAt = FixedNowUtc.AddMinutes(-10),
                CreatedAt = FixedNowUtc.AddMinutes(-12),
            });

        db.PrescriptionItems.AddRange(
            new PrescriptionItem
            {
                Id = 1,
                PrescriptionId = 1,
                MedicineId = 1,
                PrescribedQuantity = 14m,
                SingleDoseQuantity = 1m,
                SingleDoseUnit = "Viên",
                DoseScheduleText = "Uống 1 viên trước ăn sáng 30 phút",
                Frequency = "1 lần/ngày",
                DurationDays = 14,
            },
            new PrescriptionItem
            {
                Id = 2,
                PrescriptionId = 2,
                MedicineId = 2,
                PrescribedQuantity = 30m,
                SingleDoseQuantity = 1m,
                SingleDoseUnit = "Viên",
                DoseScheduleText = "Uống 1 viên buổi sáng sau ăn",
                Frequency = "1 lần/ngày",
                DurationDays = 30,
            },
            new PrescriptionItem
            {
                Id = 3,
                PrescriptionId = 3,
                MedicineId = 1,
                PrescribedQuantity = 10m,
                SingleDoseQuantity = 1m,
                SingleDoseUnit = "Viên",
                DoseScheduleText = "Uống 1 viên buổi tối",
                Frequency = "1 lần/ngày",
                DurationDays = 10,
            },
            new PrescriptionItem
            {
                Id = 4,
                PrescriptionId = 4,
                MedicineId = 3,
                PrescribedQuantity = 30m,
                SingleDoseQuantity = 1m,
                SingleDoseUnit = "Viên",
                DoseScheduleText = "Uống 1 viên buổi sáng",
                Frequency = "1 lần/ngày",
                DurationDays = 30,
            },
            new PrescriptionItem
            {
                Id = 20,
                PrescriptionId = 20,
                MedicineId = 20,
                PrescribedQuantity = 14m,
                SingleDoseQuantity = 1m,
                SingleDoseUnit = "Viên",
                DoseScheduleText = "Uống ngày 2 lần",
                Frequency = "2 lần/ngày",
                DurationDays = 7,
            });

        db.MedicationDispenses.AddRange(
            new MedicationDispense
            {
                Id = 1,
                PrescriptionId = 2,
                PreparedByStaffId = 7,
                Status = "PREPARING",
                PreparedAt = FixedNowUtc.AddMinutes(-18),
            },
            new MedicationDispense
            {
                Id = 2,
                PrescriptionId = 3,
                PreparedByStaffId = 7,
                Status = "READY",
                PreparedAt = FixedNowUtc.AddMinutes(-28),
            },
            new MedicationDispense
            {
                Id = 3,
                PrescriptionId = 4,
                PreparedByStaffId = 7,
                DispensedByStaffId = 7,
                Status = "COMPLETED",
                PreparedAt = FixedNowUtc.AddMinutes(-60),
                DispensedAt = FixedNowUtc.AddMinutes(-50),
                IdempotencyKey = "dispense-004",
            });

        db.DispenseItems.AddRange(
            new DispenseItem
            {
                Id = 1,
                DispenseId = 1,
                PrescriptionItemId = 2,
                MedicineBatchId = 2,
                PlannedQuantity = 30m,
                DispensedQuantity = 0m,
            },
            new DispenseItem
            {
                Id = 2,
                DispenseId = 2,
                PrescriptionItemId = 3,
                MedicineBatchId = 1,
                PlannedQuantity = 10m,
                DispensedQuantity = 0m,
            },
            new DispenseItem
            {
                Id = 3,
                DispenseId = 3,
                PrescriptionItemId = 4,
                MedicineBatchId = 4,
                PlannedQuantity = 30m,
                DispensedQuantity = 30m,
            });

        db.StockTransactions.AddRange(
            new StockTransaction
            {
                Id = 88,
                MedicineBatchId = 4,
                DispenseItemId = 3,
                TransactionType = "DISPENSE",
                QuantityDelta = -30m,
                PerformedByStaffId = 7,
                Reason = "Xuất thuốc ngoại trú",
                SourceReference = "RX-202610-0006",
                OccurredAt = FixedNowUtc.AddMinutes(-50),
            });

        db.SaveChanges();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    [Fact]
    public async Task GetDashboardAsync_ValidPharmacyStaff_ReturnsHospitalScopedDashboardWithStatsQueueQuickActionsAndFefoAlerts()
    {
        var result = await _service.GetDashboardAsync(9, new GetPharmacyDashboardRequest());

        Assert.Equal("Bệnh viện MedFlow (giả lập)", result.FacilityName);
        Assert.Equal("HOS-DEMO-01", result.FacilityCode);
        Assert.Equal("Quầy Dược 01 • Tầng 1", result.CounterLabel);
        Assert.Equal("#PHA-01", result.CounterCode);

        Assert.Equal(9, result.PharmacistProfile.UserId);
        Assert.Equal("pharmacy_staff01@medflow.test", result.PharmacistProfile.Email);
        Assert.Equal("DS. Trần Thị Mai", result.PharmacistProfile.FullName);
        Assert.Equal("STF-007", result.PharmacistProfile.StaffCode);
        Assert.Equal(1, result.PharmacistProfile.HospitalId);
        Assert.Equal("Khoa Dược ngoại trú", result.PharmacistProfile.DepartmentName);
        Assert.Contains("PHARMACY_STAFF", result.PharmacistProfile.Roles);
        Assert.Contains("MEDICINE_DISPENSE", result.PharmacistProfile.Permissions);

        Assert.Equal(1, result.Stats.PendingPrescriptions);
        Assert.Equal(1, result.Stats.PreparingCount);
        Assert.Equal(1, result.Stats.ReadyToDispense);
        Assert.Equal(1, result.Stats.DispensedToday);

        Assert.Equal(4, result.PrescriptionQueue.Count);
        Assert.DoesNotContain(result.PrescriptionQueue, q => q.PrescriptionCode == "RX-H2-0099");

        var dispensedEntry = Assert.Single(result.PrescriptionQueue, q => q.Status == "DISPENSED");
        Assert.Equal("STK-0088", dispensedEntry.StockTransactionCode);
        Assert.Equal("PAID_QR", dispensedEntry.PaymentStatus);

        var pendingEntry = Assert.Single(result.PrescriptionQueue, q => q.Status == "PENDING");
        Assert.Equal("PENDING_PAYMENT", pendingEntry.PaymentStatus);
        Assert.Equal("Chặn xuất kho", pendingEntry.FefoSummary);

        Assert.Equal(4, result.QuickActions.Count);
        Assert.Equal(
            ["search-prescription", "prepare-medicine", "select-fefo-batch", "confirm-dispensing"],
            result.QuickActions.Select(a => a.Id).ToArray());

        Assert.NotEmpty(result.FefoBatchAlerts);
        Assert.Contains(result.FefoBatchAlerts, b => b.Status == "EXPIRED_LOCKED" && b.BatchCode == "LOT-BIS-EXP");
        Assert.Contains(result.FefoBatchAlerts, b => b.Status == "LOW_STOCK" && b.BatchCode == "LOT-AML-LOW");
        Assert.Contains(result.FefoBatchAlerts, b => b.Status == "FEFO_PRIORITY" && b.BatchCode == "LOT-OME-1126");
        Assert.DoesNotContain(result.FefoBatchAlerts, b => b.BatchCode == "LOT-H2-01");
    }

    [Theory]
    [InlineData("PENDING", "RX-202610-0014")]
    [InlineData("PREPARING", "RX-202610-0013")]
    [InlineData("READY", "RX-202610-0011")]
    [InlineData("DISPENSED", "RX-202610-0006")]
    public async Task GetDashboardAsync_FilterByStatus_ReturnsMatchingEntriesOnly(string status, string expectedCode)
    {
        var result = await _service.GetDashboardAsync(9, new GetPharmacyDashboardRequest(Status: status));

        var single = Assert.Single(result.PrescriptionQueue);
        Assert.Equal(expectedCode, single.PrescriptionCode);
        Assert.Equal(status, single.Status);
    }

    [Fact]
    public async Task GetDashboardAsync_FilterByAllStatusAndKeyword_MatchesPatientAndPrescriptionCode()
    {
        var byCode = await _service.GetDashboardAsync(
            9,
            new GetPharmacyDashboardRequest(Status: "ALL", Keyword: "RX-202610-0011"));

        var matchedByCode = Assert.Single(byCode.PrescriptionQueue);
        Assert.Equal("Lê Hoàng Nam", matchedByCode.PatientName);

        var byPatient = await _service.GetDashboardAsync(
            9,
            new GetPharmacyDashboardRequest(Keyword: "Võ Thị Ngọc Lan"));

        var matchedByPatient = Assert.Single(byPatient.PrescriptionQueue);
        Assert.Equal("RX-202610-0006", matchedByPatient.PrescriptionCode);
    }

    [Fact]
    public async Task GetDashboardAsync_RespectsQueueLimit()
    {
        var result = await _service.GetDashboardAsync(9, new GetPharmacyDashboardRequest(QueueLimit: 2));

        Assert.Equal(2, result.PrescriptionQueue.Count);
        Assert.Equal(1, result.Stats.PendingPrescriptions);
        Assert.Equal(1, result.Stats.DispensedToday);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(51)]
    public async Task GetDashboardAsync_InvalidQueueLimit_ThrowsValidationException(int invalidLimit)
    {
        await Assert.ThrowsAsync<ValidationException>(
            () => _service.GetDashboardAsync(9, new GetPharmacyDashboardRequest(QueueLimit: invalidLimit)));
    }

    [Fact]
    public async Task GetDashboardAsync_InvalidStatusFilter_ThrowsValidationException()
    {
        await Assert.ThrowsAsync<ValidationException>(
            () => _service.GetDashboardAsync(9, new GetPharmacyDashboardRequest(Status: "INVALID_STATUS")));
    }

    [Fact]
    public async Task GetDashboardAsync_UnknownUser_ThrowsNotFoundException()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => _service.GetDashboardAsync(9999, new GetPharmacyDashboardRequest()));
    }

    [Theory]
    [InlineData(11)] // HOSPITAL_ADMIN
    [InlineData(12)] // PATIENT
    [InlineData(13)] // DOCTOR
    public async Task GetDashboardAsync_NonPharmacyStaffUser_ThrowsForbiddenException(long userId)
    {
        await Assert.ThrowsAsync<ForbiddenException>(
            () => _service.GetDashboardAsync(userId, new GetPharmacyDashboardRequest()));
    }

    [Fact]
    public async Task GetDashboardAsync_InactiveUserAccount_ThrowsForbiddenException()
    {
        await Assert.ThrowsAsync<ForbiddenException>(
            () => _service.GetDashboardAsync(14, new GetPharmacyDashboardRequest()));
    }

    [Fact]
    public async Task GetDashboardAsync_LockedUserAccount_ThrowsForbiddenException()
    {
        await Assert.ThrowsAsync<ForbiddenException>(
            () => _service.GetDashboardAsync(15, new GetPharmacyDashboardRequest()));
    }

    [Fact]
    public async Task GetDashboardAsync_MissingStaffRecord_ThrowsForbiddenException()
    {
        await Assert.ThrowsAsync<ForbiddenException>(
            () => _service.GetDashboardAsync(16, new GetPharmacyDashboardRequest()));
    }

    [Fact]
    public async Task GetDashboardAsync_StaffWithoutAssignedHospital_ThrowsForbiddenException()
    {
        await Assert.ThrowsAsync<ForbiddenException>(
            () => _service.GetDashboardAsync(17, new GetPharmacyDashboardRequest()));
    }

    [Fact]
    public async Task GetDashboardAsync_InactiveStaffRecord_ThrowsForbiddenException()
    {
        await Assert.ThrowsAsync<ForbiddenException>(
            () => _service.GetDashboardAsync(18, new GetPharmacyDashboardRequest()));
    }

    [Fact]
    public async Task GetDashboardAsync_InactiveAssignedHospital_ThrowsForbiddenException()
    {
        await Assert.ThrowsAsync<ForbiddenException>(
            () => _service.GetDashboardAsync(19, new GetPharmacyDashboardRequest()));
    }

    [Fact]
    public async Task GetDashboardAsync_WhenRolePermissionsTableEmpty_UsesFallbackPharmacyPermissions()
    {
        _dbContext.RolePermissions.RemoveRange(_dbContext.RolePermissions);
        await _dbContext.SaveChangesAsync();

        var result = await _service.GetDashboardAsync(9, new GetPharmacyDashboardRequest());

        Assert.Contains("MEDICINE_DISPENSE", result.PharmacistProfile.Permissions);
        Assert.Contains("PAYMENT_VIEW", result.PharmacistProfile.Permissions);
        Assert.Equal(4, result.QuickActions.Count);
    }

    [Fact]
    public async Task GetDashboardAsync_ReadOnlyPermissionWithoutMedicineDispense_RestrictsQuickActionsToSearchOnly()
    {
        _dbContext.RolePermissions.RemoveRange(_dbContext.RolePermissions.Where(rp => rp.RoleId == 5));
        _dbContext.RolePermissions.Add(new RolePermission { RoleId = 5, PermissionId = 3 }); // PATIENT_PROFILE_READ
        await _dbContext.SaveChangesAsync();

        var result = await _service.GetDashboardAsync(9, new GetPharmacyDashboardRequest());

        var singleAction = Assert.Single(result.QuickActions);
        Assert.Equal("search-prescription", singleAction.Id);
    }
}
