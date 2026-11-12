using System.Globalization;
using System.Text.Json;
using MedFlow.Application.DTOs.Pharmacy;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Pharmacy;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.UnitTests.Pharmacy;

public sealed class PrescriptionSearchServiceTests : IDisposable
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
            new("Not needed for PrescriptionSearchService unit tests.");

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

    private readonly TestPharmacyDbContext _dbContext;
    private readonly PrescriptionSearchService _service;

    public PrescriptionSearchServiceTests()
    {
        var options = new DbContextOptionsBuilder<TestPharmacyDbContext>()
            .UseInMemoryDatabase(databaseName: $"PrescriptionSearchTests_{Guid.NewGuid()}")
            .Options;

        _dbContext = new TestPharmacyDbContext(options);
        SeedData(_dbContext);

        var timeProvider = new FixedTimeProvider(new DateTimeOffset(FixedNowUtc));
        _service = new PrescriptionSearchService(_dbContext, timeProvider);
    }

    public void Dispose()
    {
        _dbContext.Database.EnsureDeleted();
        _dbContext.Dispose();
    }

    private static void SeedData(TestPharmacyDbContext db)
    {
        db.Hospitals.AddRange(
            new Hospital { Id = 1, HospitalCode = "HOS-001", Name = "Bệnh viện Đa khoa Quốc tế MedFlow", IsActive = true },
            new Hospital { Id = 2, HospitalCode = "HOS-002", Name = "Bệnh viện Chi nhánh 2", IsActive = true },
            new Hospital { Id = 3, HospitalCode = "HOS-003", Name = "Bệnh viện Ngừng Hoạt Động", IsActive = false });

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
            new Role { Id = 5, Code = "HOSPITAL_ADMIN", Name = "Quản trị bệnh viện" },
            new Role { Id = 6, Code = "OTHER_ROLE", Name = "Nhân viên khác" },
            new Role { Id = 7, Code = "PHARMACY_STAFF_FALLBACK", Name = "Dược sĩ không có role permission" });

        db.Permissions.AddRange(
            new Permission { Id = 1, Code = "MEDICINE_DISPENSE" },
            new Permission { Id = 2, Code = "PAYMENT_VIEW" },
            new Permission { Id = 3, Code = "PATIENT_PROFILE_READ" },
            new Permission { Id = 4, Code = "HOSPITAL_CONFIG" });

        db.RolePermissions.AddRange(
            new RolePermission { RoleId = 4, PermissionId = 1 },
            new RolePermission { RoleId = 4, PermissionId = 2 },
            new RolePermission { RoleId = 4, PermissionId = 3 },
            new RolePermission { RoleId = 5, PermissionId = 4 },
            new RolePermission { RoleId = 6, PermissionId = 4 });

        // Users
        db.Users.AddRange(
            new User { Id = 10, Email = "pharmacy_staff01@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 11, Email = "pharmacy_staff02@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 12, Email = "pharmacy_staff_h2@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 13, Email = "doctor01@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 14, Email = "inactive_pharmacist@medflow.test", AccountStatus = AccountStatus.INACTIVE },
            new User { Id = 15, Email = "locked_pharmacist@medflow.test", AccountStatus = AccountStatus.ACTIVE, LockedUntil = FixedNowUtc.AddHours(2) },
            new User { Id = 16, Email = "no_staff_pharmacist@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 17, Email = "no_hospital_pharmacist@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 18, Email = "inactive_staff_pharmacist@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 19, Email = "inactive_hospital_pharmacist@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 20, Email = "unauthorized_role_user@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 21, Email = "doc_staff_user1@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 22, Email = "doc_staff_user2@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 23, Email = "doc_staff_user3@medflow.test", AccountStatus = AccountStatus.ACTIVE });

        db.UserRoles.AddRange(
            new UserRole { UserId = 10, RoleId = 4 },
            new UserRole { UserId = 11, RoleId = 4 },
            new UserRole { UserId = 12, RoleId = 4 },
            new UserRole { UserId = 13, RoleId = 3 },
            new UserRole { UserId = 14, RoleId = 4 },
            new UserRole { UserId = 15, RoleId = 4 },
            new UserRole { UserId = 16, RoleId = 4 },
            new UserRole { UserId = 17, RoleId = 4 },
            new UserRole { UserId = 18, RoleId = 4 },
            new UserRole { UserId = 19, RoleId = 4 },
            new UserRole { UserId = 20, RoleId = 6 });

        db.Staff.AddRange(
            new Staff { Id = 10, UserId = 10, HospitalId = 1, DepartmentId = 2, StaffCode = "STF-010", FullName = "Dược sĩ Trần Mai", Status = "ACTIVE" },
            new Staff { Id = 11, UserId = 11, HospitalId = 1, DepartmentId = 2, StaffCode = "STF-011", FullName = "Dược sĩ Lê Hùng", Status = "ACTIVE" },
            new Staff { Id = 12, UserId = 12, HospitalId = 2, DepartmentId = 3, StaffCode = "STF-012", FullName = "Dược sĩ Phạm Lan", Status = "ACTIVE" },
            new Staff { Id = 14, UserId = 14, HospitalId = 1, DepartmentId = 2, StaffCode = "STF-014", FullName = "Dược sĩ Khóa Tài Khoản", Status = "ACTIVE" },
            new Staff { Id = 15, UserId = 15, HospitalId = 1, DepartmentId = 2, StaffCode = "STF-015", FullName = "Dược sĩ Bị Khóa", Status = "ACTIVE" },
            new Staff { Id = 17, UserId = 17, HospitalId = null, DepartmentId = null, StaffCode = "STF-017", FullName = "Dược sĩ Không Viện", Status = "ACTIVE" },
            new Staff { Id = 18, UserId = 18, HospitalId = 1, DepartmentId = 2, StaffCode = "STF-018", FullName = "Dược sĩ Hồ Sơ Tắt", Status = "INACTIVE" },
            new Staff { Id = 19, UserId = 19, HospitalId = 3, DepartmentId = null, StaffCode = "STF-019", FullName = "Dược sĩ Viện Ngừng", Status = "ACTIVE" },
            new Staff { Id = 20, UserId = 20, HospitalId = 1, DepartmentId = 1, StaffCode = "STF-020", FullName = "Nhân viên phòng ban khác", Status = "ACTIVE" },
            new Staff { Id = 101, UserId = 21, HospitalId = 1, DepartmentId = 1, StaffCode = "DOC-STF-01", FullName = "Nguyễn Văn An", Status = "ACTIVE" },
            new Staff { Id = 102, UserId = 22, HospitalId = 1, DepartmentId = 1, StaffCode = "DOC-STF-02", FullName = "Trần Thị Bình", Status = "ACTIVE" },
            new Staff { Id = 103, UserId = 23, HospitalId = 2, DepartmentId = 3, StaffCode = "DOC-STF-03", FullName = "Lê Quang Cường", Status = "ACTIVE" });

        db.Doctors.AddRange(
            new Doctor { Id = 1, StaffId = 101, HospitalId = 1, SpecialtyId = 1, DoctorCode = "DOC-001", ProfessionalTitle = "BS. CKI" },
            new Doctor { Id = 2, StaffId = 102, HospitalId = 1, SpecialtyId = 2, DoctorCode = "DOC-002", ProfessionalTitle = "ThS. BS" },
            new Doctor { Id = 3, StaffId = 103, HospitalId = 2, SpecialtyId = 1, DoctorCode = "DOC-003", ProfessionalTitle = "BS" });

        db.Patients.AddRange(
            new Patient
            {
                Id = 1001,
                PatientCode = "PAT-001",
                FullName = "Nguyễn Văn Bệnh Nhân",
                Gender = "MALE",
                DateOfBirth = new DateOnly(1990, 5, 15),
                Status = "ACTIVE",
                Phone = "0901234567",
                Email = "pat01@medflow.test",
                IdentificationHash = "hash1001",
            },
            new Patient
            {
                Id = 1002,
                PatientCode = "PAT-002",
                FullName = "Trần Thị Khách",
                Gender = "FEMALE",
                DateOfBirth = new DateOnly(1985, 8, 20),
                Status = "ACTIVE",
                Phone = "0909876543",
                Email = "pat02@medflow.test",
                IdentificationHash = "hash1002",
            },
            new Patient
            {
                Id = 1003,
                PatientCode = "PAT-003",
                FullName = "Lê Hoàng Em",
                Gender = "MALE",
                DateOfBirth = new DateOnly(2000, 1, 1),
                Status = "ACTIVE",
                Phone = "0911223344",
                Email = "pat03@medflow.test",
                IdentificationHash = "hash1003",
            });

        db.Visits.AddRange(
            new Visit { Id = 2001, VisitCode = "VIS-001", PatientId = 1001, AssignedDoctorId = 1, ExaminationRoomId = 101, Status = "COMPLETED" },
            new Visit { Id = 2002, VisitCode = "VIS-002", PatientId = 1002, AssignedDoctorId = 2, ExaminationRoomId = 102, Status = "COMPLETED" },
            new Visit { Id = 2003, VisitCode = "VIS-003", PatientId = 1003, AssignedDoctorId = 3, ExaminationRoomId = 201, Status = "COMPLETED" },
            new Visit { Id = 2004, VisitCode = "VIS-004", PatientId = 1001, AssignedDoctorId = 1, ExaminationRoomId = 101, Status = "IN_PROGRESS" },
            new Visit { Id = 2005, VisitCode = "VIS-005", PatientId = 1001, AssignedDoctorId = 3, ExaminationRoomId = 101, Status = "COMPLETED" },
            new Visit { Id = 2006, VisitCode = "VIS-006", PatientId = 99999, AssignedDoctorId = 1, ExaminationRoomId = 101, Status = "COMPLETED" });

        db.Medicines.AddRange(
            new Medicine { Id = 3001, HospitalId = 1, MedicineCode = "MED-PAR-500", Name = "Paracetamol 500mg", DispensingUnit = "Viên", IsActive = true },
            new Medicine { Id = 3002, HospitalId = 1, MedicineCode = "MED-IBU-400", Name = "Ibuprofen 400mg", DispensingUnit = "Viên", IsActive = true },
            new Medicine { Id = 3003, HospitalId = 1, MedicineCode = "MED-AMO-500", Name = "Amoxicillin 500mg", DispensingUnit = "Viên", IsActive = true },
            new Medicine { Id = 3004, HospitalId = 2, MedicineCode = "MED-CEF-200", Name = "Cefixime 200mg", DispensingUnit = "Viên", IsActive = true });

        // Prescriptions
        // 1. Valid ISSUED, no dispense -> Effective PENDING
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

        // 2. Valid ISSUED, MedicationDispense PREPARING -> Effective PREPARING
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
            Instructions = "Uống khi sốt trên 38.5",
        });
        db.MedicationDispenses.Add(new MedicationDispense
        {
            Id = 501,
            PrescriptionId = 102,
            Status = "PREPARING",
            PreparedAt = FixedNowUtc.AddHours(-1),
        });

        // 3. Valid ISSUED, MedicationDispense READY -> Effective READY
        db.Prescriptions.Add(new Prescription
        {
            Id = 103,
            PrescriptionCode = "RX-2026-003",
            VisitId = 2002,
            PrescribingDoctorId = 2,
            Status = "ISSUED",
            FulfillmentChoice = "HOSPITAL",
            IssuedAt = FixedNowUtc.AddHours(-3),
            CreatedAt = FixedNowUtc.AddHours(-3),
        });
        db.PrescriptionItems.Add(new PrescriptionItem
        {
            Id = 3,
            PrescriptionId = 103,
            MedicineId = 3003,
            PrescribedQuantity = 14,
            SingleDoseQuantity = 1,
            SingleDoseUnit = "Viên",
            Instructions = "Uống đều đặn 7 ngày",
        });
        db.MedicationDispenses.Add(new MedicationDispense
        {
            Id = 502,
            PrescriptionId = 103,
            Status = "READY",
            PreparedAt = FixedNowUtc.AddHours(-2),
        });

        // 4. Valid ISSUED, MedicationDispense DISPENSED -> Effective DISPENSED
        db.Prescriptions.Add(new Prescription
        {
            Id = 104,
            PrescriptionCode = "RX-2026-004",
            VisitId = 2002,
            PrescribingDoctorId = 2,
            Status = "ISSUED",
            FulfillmentChoice = "HOSPITAL",
            IssuedAt = FixedNowUtc.AddHours(-4),
            CreatedAt = FixedNowUtc.AddHours(-4),
        });
        db.PrescriptionItems.Add(new PrescriptionItem
        {
            Id = 4,
            PrescriptionId = 104,
            MedicineId = 3001,
            PrescribedQuantity = 30,
            SingleDoseQuantity = 1,
            SingleDoseUnit = "Viên",
        });
        db.MedicationDispenses.Add(new MedicationDispense
        {
            Id = 503,
            PrescriptionId = 104,
            Status = "DISPENSED",
            DispensedAt = FixedNowUtc.AddHours(-3),
        });

        // 5. Excluded: DRAFT
        db.Prescriptions.Add(new Prescription
        {
            Id = 105,
            PrescriptionCode = "RX-2026-DRAFT",
            VisitId = 2001,
            PrescribingDoctorId = 1,
            Status = "DRAFT",
            FulfillmentChoice = "HOSPITAL",
            IssuedAt = null,
            CreatedAt = FixedNowUtc.AddHours(-5),
        });
        db.PrescriptionItems.Add(new PrescriptionItem { Id = 5, PrescriptionId = 105, MedicineId = 3001, PrescribedQuantity = 10 });

        // 6. Excluded: CANCELLED
        db.Prescriptions.Add(new Prescription
        {
            Id = 106,
            PrescriptionCode = "RX-2026-CANCELLED",
            VisitId = 2001,
            PrescribingDoctorId = 1,
            Status = "CANCELLED",
            FulfillmentChoice = "HOSPITAL",
            IssuedAt = FixedNowUtc.AddHours(-5),
            CreatedAt = FixedNowUtc.AddHours(-5),
        });
        db.PrescriptionItems.Add(new PrescriptionItem { Id = 6, PrescriptionId = 106, MedicineId = 3001, PrescribedQuantity = 10 });

        // 7. Excluded: EXTERNAL fulfillment
        db.Prescriptions.Add(new Prescription
        {
            Id = 107,
            PrescriptionCode = "RX-2026-EXTERNAL",
            VisitId = 2001,
            PrescribingDoctorId = 1,
            Status = "ISSUED",
            FulfillmentChoice = "EXTERNAL",
            IssuedAt = FixedNowUtc.AddHours(-5),
            CreatedAt = FixedNowUtc.AddHours(-5),
        });
        db.PrescriptionItems.Add(new PrescriptionItem { Id = 7, PrescriptionId = 107, MedicineId = 3001, PrescribedQuantity = 10 });

        // 8. Excluded: Replaced by Rx 109
        db.Prescriptions.Add(new Prescription
        {
            Id = 108,
            PrescriptionCode = "RX-2026-REPLACED",
            VisitId = 2001,
            PrescribingDoctorId = 1,
            Status = "ISSUED",
            FulfillmentChoice = "HOSPITAL",
            IssuedAt = FixedNowUtc.AddHours(-6),
            CreatedAt = FixedNowUtc.AddHours(-6),
        });
        db.PrescriptionItems.Add(new PrescriptionItem { Id = 8, PrescriptionId = 108, MedicineId = 3001, PrescribedQuantity = 10 });

        // 9. Valid: Newer prescription replacing Rx 108
        db.Prescriptions.Add(new Prescription
        {
            Id = 109,
            PrescriptionCode = "RX-2026-NEWER",
            VisitId = 2001,
            PrescribingDoctorId = 1,
            Status = "ISSUED",
            FulfillmentChoice = "HOSPITAL",
            IssuedAt = FixedNowUtc.AddHours(-5),
            CreatedAt = FixedNowUtc.AddHours(-5),
            ReplacesPrescriptionId = 108,
        });
        db.PrescriptionItems.Add(new PrescriptionItem { Id = 9, PrescriptionId = 109, MedicineId = 3001, PrescribedQuantity = 15 });

        // 10. Excluded: Empty items
        db.Prescriptions.Add(new Prescription
        {
            Id = 110,
            PrescriptionCode = "RX-2026-EMPTY",
            VisitId = 2001,
            PrescribingDoctorId = 1,
            Status = "ISSUED",
            FulfillmentChoice = "HOSPITAL",
            IssuedAt = FixedNowUtc.AddHours(-5),
            CreatedAt = FixedNowUtc.AddHours(-5),
        });

        // 11. Foreign hospital: Hospital 2
        db.Prescriptions.Add(new Prescription
        {
            Id = 111,
            PrescriptionCode = "RX-2026-FOREIGN",
            VisitId = 2003,
            PrescribingDoctorId = 3,
            Status = "ISSUED",
            FulfillmentChoice = "HOSPITAL",
            IssuedAt = FixedNowUtc.AddHours(-5),
            CreatedAt = FixedNowUtc.AddHours(-5),
        });
        db.PrescriptionItems.Add(new PrescriptionItem { Id = 11, PrescriptionId = 111, MedicineId = 3004, PrescribedQuantity = 10 });

        // 12. Excluded: Visit not completed (IN_PROGRESS)
        db.Prescriptions.Add(new Prescription
        {
            Id = 112,
            PrescriptionCode = "RX-2026-UNCOMPLETED-VISIT",
            VisitId = 2004,
            PrescribingDoctorId = 1,
            Status = "ISSUED",
            FulfillmentChoice = "HOSPITAL",
            IssuedAt = FixedNowUtc.AddHours(-5),
            CreatedAt = FixedNowUtc.AddHours(-5),
        });
        db.PrescriptionItems.Add(new PrescriptionItem { Id = 12, PrescriptionId = 112, MedicineId = 3001, PrescribedQuantity = 10 });

        // 13. Excluded: Conflicting hospital links (Room H1, Doctor H2)
        db.Prescriptions.Add(new Prescription
        {
            Id = 113,
            PrescriptionCode = "RX-2026-CONFLICT-HOSPITAL",
            VisitId = 2005,
            PrescribingDoctorId = 1,
            Status = "ISSUED",
            FulfillmentChoice = "HOSPITAL",
            IssuedAt = FixedNowUtc.AddHours(-5),
            CreatedAt = FixedNowUtc.AddHours(-5),
        });
        db.PrescriptionItems.Add(new PrescriptionItem { Id = 13, PrescriptionId = 113, MedicineId = 3001, PrescribedQuantity = 10 });

        // 14. Excluded: Raw PENDING status (pre-issued in UC38)
        db.Prescriptions.Add(new Prescription
        {
            Id = 114,
            PrescriptionCode = "RX-2026-RAW-PENDING",
            VisitId = 2001,
            PrescribingDoctorId = 1,
            Status = "PENDING",
            FulfillmentChoice = "HOSPITAL",
            IssuedAt = FixedNowUtc.AddHours(-5),
            CreatedAt = FixedNowUtc.AddHours(-5),
        });
        db.PrescriptionItems.Add(new PrescriptionItem { Id = 14, PrescriptionId = 114, MedicineId = 3001, PrescribedQuantity = 10 });

        // 15. Excluded: Missing patient record
        db.Prescriptions.Add(new Prescription
        {
            Id = 115,
            PrescriptionCode = "RX-2026-MISSING-PATIENT",
            VisitId = 2006,
            PrescribingDoctorId = 1,
            Status = "ISSUED",
            FulfillmentChoice = "HOSPITAL",
            IssuedAt = FixedNowUtc.AddHours(-5),
            CreatedAt = FixedNowUtc.AddHours(-5),
        });
        db.PrescriptionItems.Add(new PrescriptionItem { Id = 15, PrescriptionId = 115, MedicineId = 3001, PrescribedQuantity = 10 });

        db.SaveChanges();
    }

    [Fact]
    public async Task SearchAsync_ValidPharmacyStaff_ReturnsScopedPrescriptionsForAssignedHospitalOnly()
    {
        var request = new GetPrescriptionSearchRequest();
        var result = await _service.SearchAsync(10, request);

        Assert.NotNull(result);
        Assert.Equal(5, result.TotalCount);
        Assert.Equal(5, result.Items.Count);

        var codes = result.Items.Select(i => i.PrescriptionCode).ToList();
        Assert.Contains("RX-2026-001", codes);
        Assert.Contains("RX-2026-002", codes);
        Assert.Contains("RX-2026-003", codes);
        Assert.Contains("RX-2026-004", codes);
        Assert.Contains("RX-2026-NEWER", codes);

        Assert.DoesNotContain("RX-2026-FOREIGN", codes);
    }

    [Fact]
    public async Task SearchAsync_Hospital2PharmacyStaff_ReturnsOnlyHospital2Prescriptions()
    {
        var request = new GetPrescriptionSearchRequest();
        var result = await _service.SearchAsync(12, request);

        Assert.NotNull(result);
        Assert.Equal(1, result.TotalCount);
        Assert.Single(result.Items);
        Assert.Equal("RX-2026-FOREIGN", result.Items[0].PrescriptionCode);
    }

    [Theory]
    [InlineData("RX-2026-001", "RX-2026-001")]
    [InlineData("rx-2026-002", "RX-2026-002")]
    [InlineData("NEWER", "RX-2026-NEWER")]
    public async Task SearchAsync_QueryFilter_MatchesPrescriptionCode_CaseInsensitive(string query, string expectedCode)
    {
        var request = new GetPrescriptionSearchRequest(Query: query);
        var result = await _service.SearchAsync(10, request);

        Assert.Contains(result.Items, i => i.PrescriptionCode == expectedCode);
    }

    [Theory]
    [InlineData("VIS-001", 3)] // Rx 101, 102, 109
    [InlineData("vis-002", 2)] // Rx 103, 104
    public async Task SearchAsync_QueryFilter_MatchesVisitCode_CaseInsensitive(string query, int expectedCount)
    {
        var request = new GetPrescriptionSearchRequest(Query: query);
        var result = await _service.SearchAsync(10, request);

        Assert.Equal(expectedCount, result.TotalCount);
    }

    [Theory]
    [InlineData("PAT-001", 3)]
    [InlineData("pat-002", 2)]
    public async Task SearchAsync_QueryFilter_MatchesPatientCode_CaseInsensitive(string query, int expectedCount)
    {
        var request = new GetPrescriptionSearchRequest(Query: query);
        var result = await _service.SearchAsync(10, request);

        Assert.Equal(expectedCount, result.TotalCount);
    }

    [Theory]
    [InlineData("Bệnh Nhân", 3)]
    [InlineData("khách", 2)]
    public async Task SearchAsync_QueryFilter_MatchesPatientName_CaseInsensitive(string query, int expectedCount)
    {
        var request = new GetPrescriptionSearchRequest(Query: query);
        var result = await _service.SearchAsync(10, request);

        Assert.Equal(expectedCount, result.TotalCount);
    }

    [Fact]
    public async Task SearchAsync_StatusFilter_Pending_ReturnsIssuedPrescriptionsWithoutDispense()
    {
        var request = new GetPrescriptionSearchRequest(Status: "PENDING");
        var result = await _service.SearchAsync(10, request);

        Assert.All(result.Items, item => Assert.Equal("PENDING", item.Status));
        var codes = result.Items.Select(i => i.PrescriptionCode).ToList();
        Assert.Contains("RX-2026-001", codes);
        Assert.Contains("RX-2026-NEWER", codes);
    }

    [Fact]
    public async Task SearchAsync_StatusFilter_Preparing_ReturnsPreparingPrescriptions()
    {
        var request = new GetPrescriptionSearchRequest(Status: "PREPARING");
        var result = await _service.SearchAsync(10, request);

        Assert.Single(result.Items);
        Assert.Equal("RX-2026-002", result.Items[0].PrescriptionCode);
        Assert.Equal("PREPARING", result.Items[0].Status);
    }

    [Fact]
    public async Task SearchAsync_StatusFilter_Ready_ReturnsReadyPrescriptions()
    {
        var request = new GetPrescriptionSearchRequest(Status: "READY");
        var result = await _service.SearchAsync(10, request);

        Assert.Single(result.Items);
        Assert.Equal("RX-2026-003", result.Items[0].PrescriptionCode);
        Assert.Equal("READY", result.Items[0].Status);
    }

    [Fact]
    public async Task SearchAsync_StatusFilter_Dispensed_ReturnsDispensedPrescriptions()
    {
        var request = new GetPrescriptionSearchRequest(Status: "DISPENSED");
        var result = await _service.SearchAsync(10, request);

        Assert.Single(result.Items);
        Assert.Equal("RX-2026-004", result.Items[0].PrescriptionCode);
        Assert.Equal("DISPENSED", result.Items[0].Status);
    }

    [Theory]
    [InlineData("ALL")]
    [InlineData("all")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SearchAsync_StatusFilter_AllOrNull_ReturnsAllStatuses(string? status)
    {
        var request = new GetPrescriptionSearchRequest(Status: status);
        var result = await _service.SearchAsync(10, request);

        Assert.Equal(5, result.TotalCount);
        Assert.Equal(5, result.Items.Count);
    }

    [Fact]
    public async Task SearchAsync_Pagination_AppliesPagingAndDeterministicOrdering()
    {
        var page1 = await _service.SearchAsync(10, new GetPrescriptionSearchRequest(Page: 1, PageSize: 2));
        var page2 = await _service.SearchAsync(10, new GetPrescriptionSearchRequest(Page: 2, PageSize: 2));
        var page3 = await _service.SearchAsync(10, new GetPrescriptionSearchRequest(Page: 3, PageSize: 2));

        Assert.Equal(5, page1.TotalCount);
        Assert.Equal(2, page1.Items.Count);
        Assert.Equal(2, page2.Items.Count);
        Assert.Single(page3.Items);

        // Disjoint items across pages
        var page1Ids = page1.Items.Select(i => i.Id).ToList();
        var page2Ids = page2.Items.Select(i => i.Id).ToList();
        var page3Ids = page3.Items.Select(i => i.Id).ToList();

        Assert.Empty(page1Ids.Intersect(page2Ids));
        Assert.Empty(page2Ids.Intersect(page3Ids));

        // Ordering is deterministic: IssuedAt desc, Id desc
        Assert.Equal("RX-2026-001", page1.Items[0].PrescriptionCode); // IssuedAt: -1h
        Assert.Equal("RX-2026-002", page1.Items[1].PrescriptionCode); // IssuedAt: -2h
    }

    [Fact]
    public async Task SearchAsync_ExcludesInvalidAndNonHospitalPrescriptions()
    {
        var result = await _service.SearchAsync(10, new GetPrescriptionSearchRequest(PageSize: 100));
        var codes = result.Items.Select(i => i.PrescriptionCode).ToList();

        Assert.DoesNotContain("RX-2026-DRAFT", codes);
        Assert.DoesNotContain("RX-2026-CANCELLED", codes);
        Assert.DoesNotContain("RX-2026-EXTERNAL", codes);
        Assert.DoesNotContain("RX-2026-REPLACED", codes);
        Assert.DoesNotContain("RX-2026-EMPTY", codes);
        Assert.DoesNotContain("RX-2026-FOREIGN", codes);
        Assert.DoesNotContain("RX-2026-UNCOMPLETED-VISIT", codes);
        Assert.DoesNotContain("RX-2026-CONFLICT-HOSPITAL", codes);
        Assert.DoesNotContain("RX-2026-RAW-PENDING", codes);
        Assert.DoesNotContain("RX-2026-MISSING-PATIENT", codes);
    }

    [Fact]
    public async Task SearchAsync_DraftReplacement_DoesNotSupersedeIssuedPrescription()
    {
        _dbContext.Prescriptions.Add(new Prescription
        {
            Id = 116,
            PrescriptionCode = "RX-2026-DRAFT-REPLACEMENT",
            VisitId = 2001,
            PrescribingDoctorId = 1,
            Status = "DRAFT",
            FulfillmentChoice = "HOSPITAL",
            CreatedAt = FixedNowUtc,
            ReplacesPrescriptionId = 101,
        });
        _dbContext.PrescriptionItems.Add(new PrescriptionItem
        {
            Id = 16,
            PrescriptionId = 116,
            MedicineId = 3001,
            PrescribedQuantity = 10,
        });
        await _dbContext.SaveChangesAsync();

        var result = await _service.SearchAsync(
            10,
            new GetPrescriptionSearchRequest(Query: "RX-2026-001"));

        Assert.Single(result.Items);
        Assert.Equal("RX-2026-001", result.Items[0].PrescriptionCode);

        var detail = await _service.GetByIdAsync(10, "101");
        Assert.Equal("RX-2026-001", detail.Prescription.PrescriptionCode);
    }

    [Theory]
    [InlineData(117L, "RX-2026-FOREIGN-MEDICINE", 3004L, 1L)]
    [InlineData(118L, "RX-2026-MISSING-MEDICINE", null, 1L)]
    [InlineData(119L, "RX-2026-FOREIGN-PRESCRIBER", 3001L, 3L)]
    public async Task SearchAndDetail_InvalidClinicalLinks_AreExcluded(
        long prescriptionId,
        string prescriptionCode,
        long? medicineId,
        long prescribingDoctorId)
    {
        _dbContext.Prescriptions.Add(new Prescription
        {
            Id = prescriptionId,
            PrescriptionCode = prescriptionCode,
            VisitId = 2001,
            PrescribingDoctorId = prescribingDoctorId,
            Status = "ISSUED",
            FulfillmentChoice = "HOSPITAL",
            IssuedAt = FixedNowUtc,
            CreatedAt = FixedNowUtc,
        });
        _dbContext.PrescriptionItems.Add(new PrescriptionItem
        {
            Id = prescriptionId,
            PrescriptionId = prescriptionId,
            MedicineId = medicineId,
            PrescribedQuantity = 10,
        });
        await _dbContext.SaveChangesAsync();

        var search = await _service.SearchAsync(
            10,
            new GetPrescriptionSearchRequest(Query: prescriptionCode));

        Assert.Empty(search.Items);
        await Assert.ThrowsAsync<NotFoundException>(
            () => _service.GetByIdAsync(10, prescriptionId.ToString(CultureInfo.InvariantCulture)));
    }

    [Fact]
    public async Task SearchAsync_DtoOmitsSensitivePhi()
    {
        var result = await _service.SearchAsync(10, new GetPrescriptionSearchRequest());
        var firstItem = result.Items.First(i => i.PrescriptionCode == "RX-2026-001");

        Assert.Equal("PAT-001", firstItem.PatientCode);
        Assert.Equal("Nguyễn Văn Bệnh Nhân", firstItem.PatientName);
        Assert.Equal("MALE", firstItem.Gender);
        Assert.Equal(new DateOnly(1990, 5, 15), firstItem.DateOfBirth);
        Assert.Equal(1, firstItem.ItemCount);

        // Assert DTO type does not declare Phone, Email or IdentificationHash properties
        var properties = typeof(PrescriptionSearchItemDto).GetProperties().Select(p => p.Name).ToList();
        Assert.DoesNotContain("Phone", properties);
        Assert.DoesNotContain("Email", properties);
        Assert.DoesNotContain("IdentificationHash", properties);
        Assert.DoesNotContain("IdentityNumber", properties);
    }

    [Fact]
    public async Task SearchAsync_QueryLengthExceeded_ThrowsValidationException()
    {
        var longQuery = new string('A', 101);
        var request = new GetPrescriptionSearchRequest(Query: longQuery);

        await Assert.ThrowsAsync<ValidationException>(() => _service.SearchAsync(10, request));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task SearchAsync_InvalidPage_ThrowsValidationException(int page)
    {
        var request = new GetPrescriptionSearchRequest(Page: page);
        await Assert.ThrowsAsync<ValidationException>(() => _service.SearchAsync(10, request));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(101)]
    public async Task SearchAsync_InvalidPageSize_ThrowsValidationException(int pageSize)
    {
        var request = new GetPrescriptionSearchRequest(PageSize: pageSize);
        await Assert.ThrowsAsync<ValidationException>(() => _service.SearchAsync(10, request));
    }

    [Fact]
    public async Task SearchAsync_InvalidStatus_ThrowsValidationException()
    {
        var request = new GetPrescriptionSearchRequest(Status: "UNKNOWN_STATUS");
        await Assert.ThrowsAsync<ValidationException>(() => _service.SearchAsync(10, request));
    }

    [Fact]
    public async Task SearchAsync_NonExistentUser_ThrowsNotFoundException()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _service.SearchAsync(99999, new GetPrescriptionSearchRequest()));
    }

    [Fact]
    public async Task SearchAsync_InactiveUserAccount_ThrowsForbiddenException()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() => _service.SearchAsync(14, new GetPrescriptionSearchRequest()));
    }

    [Fact]
    public async Task SearchAsync_LockedUserAccount_ThrowsForbiddenException()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() => _service.SearchAsync(15, new GetPrescriptionSearchRequest()));
    }

    [Fact]
    public async Task SearchAsync_UserWithoutPharmacyStaffRole_ThrowsForbiddenException()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() => _service.SearchAsync(13, new GetPrescriptionSearchRequest()));
    }

    [Fact]
    public async Task SearchAsync_UserWithoutStaffRecord_ThrowsForbiddenException()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() => _service.SearchAsync(16, new GetPrescriptionSearchRequest()));
    }

    [Fact]
    public async Task SearchAsync_StaffWithoutHospital_ThrowsForbiddenException()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() => _service.SearchAsync(17, new GetPrescriptionSearchRequest()));
    }

    [Fact]
    public async Task SearchAsync_StaffInactive_ThrowsForbiddenException()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() => _service.SearchAsync(18, new GetPrescriptionSearchRequest()));
    }

    [Fact]
    public async Task SearchAsync_HospitalInactive_ThrowsForbiddenException()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() => _service.SearchAsync(19, new GetPrescriptionSearchRequest()));
    }

    [Fact]
    public async Task SearchAsync_HospitalWithUnknownActiveState_ThrowsForbiddenException()
    {
        var hospital = await _dbContext.Hospitals.SingleAsync(h => h.Id == 1);
        hospital.IsActive = null;
        await _dbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _service.SearchAsync(10, new GetPrescriptionSearchRequest()));
    }

    [Fact]
    public async Task SearchAsync_UserMissingRequiredPermissions_ThrowsForbiddenException()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() => _service.SearchAsync(20, new GetPrescriptionSearchRequest()));
    }

    [Fact]
    public async Task SearchAsync_PaymentViewWithoutMedicineDispense_ThrowsForbiddenException()
    {
        var dispensePermission = await _dbContext.Permissions
            .SingleAsync(p => p.Code == "MEDICINE_DISPENSE");
        var patientReadPermission = await _dbContext.Permissions
            .SingleAsync(p => p.Code == "PATIENT_PROFILE_READ");
        var rolePermissions = await _dbContext.RolePermissions
            .Where(rp =>
                rp.RoleId == 4 &&
                (rp.PermissionId == dispensePermission.Id ||
                 rp.PermissionId == patientReadPermission.Id))
            .ToListAsync();
        _dbContext.RolePermissions.RemoveRange(rolePermissions);
        await _dbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _service.SearchAsync(10, new GetPrescriptionSearchRequest()));
    }

    [Fact]
    public async Task SearchAsync_FallbackPermissions_GrantedWhenRolePermissionsTableEmpty()
    {
        // Create an isolated context with no role permissions seeded
        var options = new DbContextOptionsBuilder<TestPharmacyDbContext>()
            .UseInMemoryDatabase(databaseName: $"FallbackPermTests_{Guid.NewGuid()}")
            .Options;

        using var emptyRolePermDb = new TestPharmacyDbContext(options);
        emptyRolePermDb.Hospitals.Add(new Hospital { Id = 1, HospitalCode = "HOS-001", Name = "MedFlow", IsActive = true });
        emptyRolePermDb.Roles.Add(new Role { Id = 4, Code = "PHARMACY_STAFF", Name = "Dược sĩ" });
        emptyRolePermDb.Users.Add(new User { Id = 50, Email = "fallback_staff@medflow.test", AccountStatus = AccountStatus.ACTIVE });
        emptyRolePermDb.UserRoles.Add(new UserRole { UserId = 50, RoleId = 4 });
        emptyRolePermDb.Staff.Add(new Staff { Id = 50, UserId = 50, HospitalId = 1, Status = "ACTIVE" });
        emptyRolePermDb.SaveChanges();

        var service = new PrescriptionSearchService(emptyRolePermDb);
        var result = await service.SearchAsync(50, new GetPrescriptionSearchRequest());

        Assert.NotNull(result);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task SearchAsync_LogsAuditEntry_WithoutSensitiveQueryString_AndDoesNotMutateState()
    {
        var initialRxStatus = await _dbContext.Prescriptions.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Status);
        var initialDispStatus = await _dbContext.MedicationDispenses.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.Status);
        var initialAuditCount = await _dbContext.AuditLogs.CountAsync();

        var request = new GetPrescriptionSearchRequest(Query: "SECRET_PATIENT_TERM", Status: "PENDING", Page: 1, PageSize: 10);
        var result = await _service.SearchAsync(10, request);

        // Verify state is NOT mutated
        var currentRxStatus = await _dbContext.Prescriptions.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Status);
        var currentDispStatus = await _dbContext.MedicationDispenses.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.Status);
        Assert.Equal(initialRxStatus, currentRxStatus);
        Assert.Equal(initialDispStatus, currentDispStatus);

        // Verify audit log added
        var latestAudit = await _dbContext.AuditLogs.OrderByDescending(a => a.Id).FirstOrDefaultAsync();
        Assert.NotNull(latestAudit);
        Assert.Equal(initialAuditCount + 1, await _dbContext.AuditLogs.CountAsync());
        Assert.Equal("PRESCRIPTION_SEARCH", latestAudit.Action);
        Assert.Equal("PRESCRIPTION", latestAudit.EntityType);
        Assert.Null(latestAudit.EntityId);
        Assert.Equal(10, latestAudit.ActorUserId);
        Assert.Equal(1, latestAudit.HospitalId);

        // Verify query string is NOT logged in ChangeData or Reason (no PHI / sensitive terms)
        Assert.NotNull(latestAudit.ChangeData);
        Assert.DoesNotContain("SECRET_PATIENT_TERM", latestAudit.ChangeData);
        Assert.DoesNotContain("SECRET_PATIENT_TERM", latestAudit.Reason ?? string.Empty);

        using var doc = JsonDocument.Parse(latestAudit.ChangeData);
        Assert.True(doc.RootElement.TryGetProperty("page", out var pageProp) && pageProp.GetInt32() == 1);
        Assert.True(doc.RootElement.TryGetProperty("pageSize", out var pageSizeProp) && pageSizeProp.GetInt32() == 10);
        Assert.True(doc.RootElement.TryGetProperty("status", out var statusProp) && statusProp.GetString() == "PENDING");
    }

    [Fact]
    public async Task GetByIdAsync_ByNumericId_ReturnsPrescriptionWithLineItems()
    {
        var result = await _service.GetByIdAsync(10, "101");

        Assert.NotNull(result);
        Assert.Equal(101, result.Prescription.Id);
        Assert.Equal("RX-2026-001", result.Prescription.PrescriptionCode);
        Assert.Equal("VIS-001", result.Prescription.VisitCode);
        Assert.Equal("PAT-001", result.Prescription.PatientCode);
        Assert.Equal("Nguyễn Văn Bệnh Nhân", result.Prescription.PatientName);
        Assert.Equal("PENDING", result.Prescription.Status);

        Assert.Single(result.Items);
        var item = result.Items[0];
        Assert.Equal("MED-PAR-500", item.MedicineCode);
        Assert.Equal("Paracetamol 500mg", item.MedicineName);
        Assert.Equal("Viên", item.DispensingUnit);
        Assert.Equal(20m, item.PrescribedQuantity);
        Assert.Equal(1m, item.SingleDoseQuantity);
        Assert.Equal("Viên", item.SingleDoseUnit);
        Assert.Equal("Sáng 1 viên, tối 1 viên", item.DoseScheduleText);
        Assert.Equal("2 lần/ngày", item.Frequency);
        Assert.Equal(10, item.DurationDays);
        Assert.Equal("Đường uống", item.AdministrationRoute);
        Assert.Equal("Uống sau ăn", item.Instructions);
    }

    [Theory]
    [InlineData("RX-2026-001")]
    [InlineData("rx-2026-001")]
    [InlineData("Rx-2026-001")]
    public async Task GetByIdAsync_ByPrescriptionCode_CaseInsensitive_ReturnsPrescription(string code)
    {
        var result = await _service.GetByIdAsync(10, code);

        Assert.NotNull(result);
        Assert.Equal(101, result.Prescription.Id);
        Assert.Equal("RX-2026-001", result.Prescription.PrescriptionCode);
    }

    [Fact]
    public async Task GetByIdAsync_ForeignHospitalPrescription_ThrowsNotFoundException()
    {
        // Hospital 1 pharmacist cannot view Hospital 2 prescription
        await Assert.ThrowsAsync<NotFoundException>(() => _service.GetByIdAsync(10, "111"));
    }

    [Fact]
    public async Task GetByIdAsync_NonExistentPrescription_ThrowsNotFoundException()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _service.GetByIdAsync(10, "99999"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null!)]
    public async Task GetByIdAsync_EmptyOrWhitespaceId_ThrowsValidationException(string? idOrCode)
    {
        await Assert.ThrowsAsync<ValidationException>(() => _service.GetByIdAsync(10, idOrCode!));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("-100")]
    public async Task GetByIdAsync_InvalidNumericId_ThrowsValidationException(string idOrCode)
    {
        await Assert.ThrowsAsync<ValidationException>(() => _service.GetByIdAsync(10, idOrCode));
    }

    [Fact]
    public async Task GetByIdAsync_IdExceedingMaxLength_ThrowsValidationException()
    {
        var longCode = new string('X', 101);
        await Assert.ThrowsAsync<ValidationException>(() => _service.GetByIdAsync(10, longCode));
    }

    [Theory]
    [InlineData("105")] // DRAFT
    [InlineData("106")] // CANCELLED
    [InlineData("107")] // EXTERNAL
    [InlineData("108")] // REPLACED
    [InlineData("110")] // EMPTY ITEMS
    [InlineData("112")] // UNCOMPLETED VISIT
    [InlineData("113")] // CONFLICTING HOSPITAL
    [InlineData("114")] // RAW PENDING
    [InlineData("115")] // MISSING PATIENT
    public async Task GetByIdAsync_ExcludedPrescriptions_ThrowNotFoundException(string idOrCode)
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _service.GetByIdAsync(10, idOrCode));
    }

    [Fact]
    public async Task GetByIdAsync_LogsAuditEntry_AndDoesNotMutateData()
    {
        var initialAuditCount = await _dbContext.AuditLogs.CountAsync();

        var result = await _service.GetByIdAsync(10, "102");
        Assert.NotNull(result);

        var latestAudit = await _dbContext.AuditLogs.OrderByDescending(a => a.Id).FirstOrDefaultAsync();
        Assert.NotNull(latestAudit);
        Assert.Equal(initialAuditCount + 1, await _dbContext.AuditLogs.CountAsync());
        Assert.Equal("PRESCRIPTION_VIEW", latestAudit.Action);
        Assert.Equal("PRESCRIPTION", latestAudit.EntityType);
        Assert.Equal("102", latestAudit.EntityId);
        Assert.Equal(10, latestAudit.ActorUserId);
        Assert.Equal(1, latestAudit.HospitalId);

        Assert.NotNull(latestAudit.ChangeData);
        using var doc = JsonDocument.Parse(latestAudit.ChangeData);
        Assert.True(doc.RootElement.TryGetProperty("prescriptionId", out var idProp) && idProp.GetInt64() == 102);
        Assert.False(doc.RootElement.TryGetProperty("prescriptionCode", out _));
        Assert.True(doc.RootElement.TryGetProperty("itemCount", out var countProp) && countProp.GetInt32() == 1);
    }
}
