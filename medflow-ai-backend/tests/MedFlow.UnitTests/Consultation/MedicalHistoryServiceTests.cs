using System.Text.Json;
using MedFlow.Application.DTOs.Consultation;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Consultation;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.UnitTests.Consultation;

public sealed class MedicalHistoryServiceTests : IDisposable
{
    private sealed class FixedTimeProvider(DateTimeOffset nowUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => nowUtc;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class MedicalHistoryTestDbContext : DbContext, IApplicationDbContext
    {
        public MedicalHistoryTestDbContext(DbContextOptions<MedicalHistoryTestDbContext> options)
            : base(options)
        {
        }

        private static NotSupportedException NotUsed() =>
            new("Not needed for MedicalHistoryService unit tests.");

        public DbSet<User> Users => Set<User>();
        public DbSet<Role> Roles => Set<Role>();
        public DbSet<Permission> Permissions => Set<Permission>();
        public DbSet<UserRole> UserRoles => Set<UserRole>();
        public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
        public DbSet<Patient> Patients => Set<Patient>();
        public DbSet<Staff> Staff => Set<Staff>();
        public DbSet<Doctor> Doctors => Set<Doctor>();
        public DbSet<DoctorServiceAuthorization> DoctorServiceAuthorizations => throw NotUsed();
        public DbSet<ExaminationSlot> ExaminationSlots => Set<ExaminationSlot>();
        public DbSet<AuthSession> AuthSessions => throw NotUsed();
        public DbSet<AuthVerificationChallenge> AuthVerificationChallenges => throw NotUsed();
        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
        public DbSet<Hospital> Hospitals => Set<Hospital>();
        public DbSet<Department> Departments => Set<Department>();
        public DbSet<HospitalRoom> HospitalRooms => Set<HospitalRoom>();
        public DbSet<Specialty> Specialties => Set<Specialty>();
        public DbSet<Service> Services => Set<Service>();
        public DbSet<ServicePrice> ServicePrices => throw NotUsed();
        public DbSet<HospitalPolicy> HospitalPolicies => throw NotUsed();
        public DbSet<Appointment> Appointments => Set<Appointment>();
        public DbSet<PatientFamilyLink> PatientFamilyLinks => throw NotUsed();
        public DbSet<Visit> Visits => Set<Visit>();
        public DbSet<QueueEntry> QueueEntries => Set<QueueEntry>();
        public DbSet<Payment> Payments => throw NotUsed();
        public DbSet<Medicine> Medicines => Set<Medicine>();
        public DbSet<Prescription> Prescriptions => Set<Prescription>();
        public DbSet<PrescriptionItem> PrescriptionItems => Set<PrescriptionItem>();
        public DbSet<QueueServicePoint> QueueServicePoints => Set<QueueServicePoint>();
        public DbSet<Icd10Code> Icd10Codes => Set<Icd10Code>();
        public DbSet<MedicalRecord> MedicalRecords => Set<MedicalRecord>();
        public DbSet<MedicalRecordRevision> MedicalRecordRevisions => Set<MedicalRecordRevision>();
        public DbSet<VisitDiagnosis> VisitDiagnoses => Set<VisitDiagnosis>();
        public DbSet<ClinicalAddendum> ClinicalAddenda => Set<ClinicalAddendum>();
        public DbSet<DiagnosticOrder> DiagnosticOrders => Set<DiagnosticOrder>();
        public DbSet<DiagnosticResult> DiagnosticResults => Set<DiagnosticResult>();
        public DbSet<DiagnosticResultVersion> DiagnosticResultVersions => Set<DiagnosticResultVersion>();

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default)
            => Database.BeginTransactionAsync(ct);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Ignore<DoctorServiceAuthorization>();
            modelBuilder.Ignore<AuthSession>();
            modelBuilder.Ignore<AuthVerificationChallenge>();
            modelBuilder.Ignore<ServicePrice>();
            modelBuilder.Ignore<HospitalPolicy>();
            modelBuilder.Ignore<PatientFamilyLink>();
            modelBuilder.Ignore<Payment>();

            modelBuilder.Entity<User>().HasKey(u => u.Id);
            modelBuilder.Entity<Role>().HasKey(r => r.Id);
            modelBuilder.Entity<Permission>().HasKey(p => p.Id);
            modelBuilder.Entity<UserRole>().HasKey(ur => new { ur.UserId, ur.RoleId });
            modelBuilder.Entity<RolePermission>().HasKey(rp => new { rp.RoleId, rp.PermissionId });
            modelBuilder.Entity<Patient>().HasKey(p => p.Id);
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
            modelBuilder.Entity<Service>().HasKey(s => s.Id);
            modelBuilder.Entity<ExaminationSlot>().HasKey(es => es.Id);
            modelBuilder.Entity<Appointment>().HasKey(a => a.Id);
            modelBuilder.Entity<Visit>().HasKey(v => v.Id);
            modelBuilder.Entity<QueueEntry>().HasKey(q => q.Id);
            modelBuilder.Entity<QueueServicePoint>().HasKey(sp => sp.Id);
            modelBuilder.Entity<AuditLog>().HasKey(a => a.Id);
            modelBuilder.Entity<Medicine>().HasKey(m => m.Id);
            modelBuilder.Entity<Prescription>().HasKey(p => p.Id);
            modelBuilder.Entity<PrescriptionItem>().HasKey(pi => pi.Id);
            modelBuilder.Entity<Icd10Code>().HasKey(c => c.Id);
            modelBuilder.Entity<MedicalRecord>().HasKey(r => r.Id);
            modelBuilder.Entity<MedicalRecordRevision>().HasKey(rev => rev.Id);
            modelBuilder.Entity<VisitDiagnosis>().HasKey(vd => vd.Id);
            modelBuilder.Entity<ClinicalAddendum>().HasKey(ca => ca.Id);
            modelBuilder.Entity<DiagnosticOrder>().HasKey(o => o.Id);
            modelBuilder.Entity<DiagnosticResult>().HasKey(dr => dr.Id);
            modelBuilder.Entity<DiagnosticResultVersion>().HasKey(drv => drv.Id);
        }
    }

    private static readonly DateTime FixedNowUtc = new(2026, 10, 25, 9, 0, 0, DateTimeKind.Utc);

    private readonly MedicalHistoryTestDbContext _dbContext;
    private readonly MedicalHistoryService _service;

    public MedicalHistoryServiceTests()
    {
        var options = new DbContextOptionsBuilder<MedicalHistoryTestDbContext>()
            .UseInMemoryDatabase(databaseName: $"MedicalHistoryTests_{Guid.NewGuid()}")
            .Options;

        _dbContext = new MedicalHistoryTestDbContext(options);
        SeedData(_dbContext);

        var timeProvider = new FixedTimeProvider(new DateTimeOffset(FixedNowUtc));
        _service = new MedicalHistoryService(_dbContext, timeProvider);
    }

    private static void SeedData(MedicalHistoryTestDbContext db)
    {
        db.Hospitals.AddRange(
            new Hospital { Id = 1, HospitalCode = "MEDFLOW-HN-01", Name = "Bệnh viện Trung ương MedFlow", IsActive = true },
            new Hospital { Id = 2, HospitalCode = "MEDFLOW-HCM-02", Name = "Bệnh viện MedFlow Cơ sở 2", IsActive = true },
            new Hospital { Id = 3, HospitalCode = "MEDFLOW-INACTIVE", Name = "Bệnh viện Tạm Ngừng", IsActive = false });

        db.Departments.AddRange(
            new Department { Id = 1, HospitalId = 1, DepartmentCode = "NOI", Name = "Khoa Nội Tổng hợp", IsActive = true },
            new Department { Id = 2, HospitalId = 1, DepartmentCode = "TIM", Name = "Khoa Tim mạch", IsActive = true },
            new Department { Id = 3, HospitalId = 2, DepartmentCode = "NOI-CS2", Name = "Khoa Nội CS2", IsActive = true });

        db.Specialties.AddRange(
            new Specialty { Id = 1, SpecialtyCode = "GASTRO", Name = "Tiêu hóa", IsActive = true },
            new Specialty { Id = 2, SpecialtyCode = "CARDIO", Name = "Tim mạch", IsActive = true });

        db.HospitalRooms.AddRange(
            new HospitalRoom { Id = 1, HospitalId = 1, DepartmentId = 1, RoomCode = "P101", Name = "Phòng khám Nội 101", RoomType = "EXAMINATION", Status = "ACTIVE" },
            new HospitalRoom { Id = 2, HospitalId = 1, DepartmentId = 2, RoomCode = "P102", Name = "Phòng khám Tim mạch 102", RoomType = "EXAMINATION", Status = "ACTIVE" },
            new HospitalRoom { Id = 3, HospitalId = 2, DepartmentId = 3, RoomCode = "P201", Name = "Phòng khám CS2 201", RoomType = "EXAMINATION", Status = "ACTIVE" });

        db.Services.AddRange(
            new Service { Id = 1, HospitalId = 1, DepartmentId = 1, SpecialtyId = 1, ServiceCode = "KHAM-TH", Name = "Khám Nội Tiêu hóa", ServiceType = "EXAMINATION", IsActive = true },
            new Service { Id = 2, HospitalId = 1, DepartmentId = 2, SpecialtyId = 2, ServiceCode = "KHAM-TM", Name = "Khám Nội Tim mạch", ServiceType = "EXAMINATION", IsActive = true },
            new Service { Id = 5, HospitalId = 1, DepartmentId = 1, SpecialtyId = 1, ServiceCode = "CLS-NS", Name = "Nội soi dạ dày - tá tràng", ServiceType = "DIAGNOSTIC", IsActive = true },
            new Service { Id = 6, HospitalId = 1, DepartmentId = 2, SpecialtyId = 2, ServiceCode = "CLS-ECG", Name = "Điện tâm đồ 12 chuyển đạo", ServiceType = "DIAGNOSTIC", IsActive = true });

        db.Roles.AddRange(
            new Role { Id = 1, Code = "PATIENT", Name = "Bệnh nhân" },
            new Role { Id = 2, Code = "DOCTOR", Name = "Bác sĩ" },
            new Role { Id = 3, Code = "RECEPTION", Name = "Lễ tân" });

        db.Permissions.AddRange(
            new Permission { Id = 5, Code = "PATIENT_PROFILE_READ" },
            new Permission { Id = 6, Code = "MEDICAL_RECORD_WRITE" },
            new Permission { Id = 99, Code = "OTHER_PERMISSION" });

        db.RolePermissions.AddRange(
            new RolePermission { RoleId = 2, PermissionId = 5 },
            new RolePermission { RoleId = 2, PermissionId = 6 });

        db.Users.AddRange(
            new User { Id = 3, Email = "doctor01@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 4, Email = "doctor02@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 10, Email = "reception@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 14, Email = "locked.doctor@medflow.test", AccountStatus = AccountStatus.ACTIVE, LockedUntil = FixedNowUtc.AddMinutes(30) },
            new User { Id = 15, Email = "inactive.doctor@medflow.test", AccountStatus = AccountStatus.INACTIVE },
            new User { Id = 16, Email = "inactivestaff.doctor@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 17, Email = "nostaff.doctor@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 18, Email = "nodoctor.doctor@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 19, Email = "inactivedoctor@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 20, Email = "inactivehospital.doctor@medflow.test", AccountStatus = AccountStatus.ACTIVE });

        db.UserRoles.AddRange(
            new UserRole { UserId = 3, RoleId = 2 },
            new UserRole { UserId = 4, RoleId = 2 },
            new UserRole { UserId = 10, RoleId = 3 },
            new UserRole { UserId = 14, RoleId = 2 },
            new UserRole { UserId = 15, RoleId = 2 },
            new UserRole { UserId = 16, RoleId = 2 },
            new UserRole { UserId = 17, RoleId = 2 },
            new UserRole { UserId = 18, RoleId = 2 },
            new UserRole { UserId = 19, RoleId = 2 },
            new UserRole { UserId = 20, RoleId = 2 });

        db.Staff.AddRange(
            new Staff { Id = 1, UserId = 3, HospitalId = 1, DepartmentId = 1, StaffCode = "STF-001", FullName = "Nguyễn Hoàng Nam", Status = "ACTIVE" },
            new Staff { Id = 2, UserId = 4, HospitalId = 1, DepartmentId = 2, StaffCode = "STF-002", FullName = "Trần Minh Quân", Status = "ACTIVE" },
            new Staff { Id = 16, UserId = 16, HospitalId = 1, DepartmentId = 1, StaffCode = "STF-016", FullName = "Bác sĩ Ngừng Nhân Sự", Status = "INACTIVE" },
            new Staff { Id = 18, UserId = 18, HospitalId = 1, DepartmentId = 1, StaffCode = "STF-018", FullName = "Nhân sự Chưa Có Hồ Sơ BS", Status = "ACTIVE" },
            new Staff { Id = 19, UserId = 19, HospitalId = 1, DepartmentId = 1, StaffCode = "STF-019", FullName = "Bác sĩ Tạm Ngừng Hành Nghề", Status = "ACTIVE" },
            new Staff { Id = 20, UserId = 20, HospitalId = 3, DepartmentId = 1, StaffCode = "STF-020", FullName = "Bác sĩ Cơ Sở Ngừng", Status = "ACTIVE" });

        db.Doctors.AddRange(
            new Doctor { Id = 1, StaffId = 1, HospitalId = 1, DoctorCode = "DOC-001", SpecialtyId = 1, ProfessionalTitle = "BS.CKI", IsActive = true },
            new Doctor { Id = 2, StaffId = 2, HospitalId = 1, DoctorCode = "DOC-002", SpecialtyId = 2, ProfessionalTitle = "ThS.BS", IsActive = true },
            new Doctor { Id = 19, StaffId = 19, HospitalId = 1, DoctorCode = "DOC-019", SpecialtyId = 1, ProfessionalTitle = "BS", IsActive = false },
            new Doctor { Id = 20, StaffId = 20, HospitalId = 3, DoctorCode = "DOC-020", SpecialtyId = 1, ProfessionalTitle = "BS", IsActive = true });

        db.Patients.AddRange(
            new Patient
            {
                Id = 1,
                PatientCode = "PAT-0001",
                FullName = "Lê Thị Mai",
                DateOfBirth = new DateOnly(1990, 5, 10),
                Gender = "FEMALE",
                Phone = "0901234567",
                Email = "patient01@medflow.test",
                IdentificationHash = "secret-hash-0001",
                Address = "123 Nguyễn Trãi, Hà Nội",
                BloodType = "O+",
                Status = "ACTIVE",
            },
            new Patient
            {
                Id = 2,
                PatientCode = "PAT-0002",
                FullName = "Phạm Văn Hùng",
                DateOfBirth = new DateOnly(1982, 8, 20),
                Gender = "MALE",
                BloodType = "A+",
                Status = "ACTIVE",
            });

        db.Visits.AddRange(
            new Visit
            {
                Id = 1,
                VisitCode = "VIS-202610-0001",
                PatientId = 1,
                AssignedDoctorId = 1,
                CompletedByDoctorId = 1,
                ExaminationRoomId = 1,
                ReasonForVisit = "Đau thượng vị kéo dài",
                Status = "COMPLETED",
                CheckedInAt = new DateTime(2026, 10, 15, 8, 30, 0),
                CompletedAt = new DateTime(2026, 10, 15, 11, 25, 0),
            },
            new Visit
            {
                Id = 3,
                VisitCode = "VIS-202610-0003",
                PatientId = 1,
                AssignedDoctorId = 2,
                CompletedByDoctorId = 2,
                ExaminationRoomId = 2,
                ReasonForVisit = "Khám tim mạch phối hợp",
                Status = "COMPLETED",
                CheckedInAt = new DateTime(2026, 10, 20, 9, 0, 0),
                CompletedAt = new DateTime(2026, 10, 20, 11, 0, 0),
            },
            new Visit
            {
                Id = 2,
                VisitCode = "VIS-202610-0002",
                PatientId = 2,
                AssignedDoctorId = 2,
                CompletedByDoctorId = 2,
                ExaminationRoomId = 2,
                ReasonForVisit = "Theo dõi huyết áp",
                Status = "COMPLETED",
                CheckedInAt = new DateTime(2026, 10, 16, 8, 30, 0),
                CompletedAt = new DateTime(2026, 10, 16, 11, 25, 0),
            },
            new Visit
            {
                Id = 99,
                VisitCode = "VIS-202610-0099",
                PatientId = 1,
                AssignedDoctorId = 1,
                ExaminationRoomId = 1,
                ReasonForVisit = "Lượt khám đã hủy",
                Status = "CANCELLED",
                CheckedInAt = new DateTime(2026, 10, 18, 8, 0, 0),
            });

        db.Icd10Codes.AddRange(
            new Icd10Code { Id = 1, Code = "K29.5", NameVi = "Viêm dạ dày mạn tính", CatalogVersion = "DEMO-ICD10", IsSelectable = true },
            new Icd10Code { Id = 2, Code = "I10", NameVi = "Tăng huyết áp vô căn (nguyên phát)", CatalogVersion = "DEMO-ICD10", IsSelectable = true });

        db.MedicalRecords.AddRange(
            new MedicalRecord
            {
                Id = 1,
                VisitId = 1,
                CreatedByDoctorId = 1,
                Status = "FINALIZED",
                FinalizedAt = new DateTime(2026, 10, 15, 11, 25, 0),
            },
            new MedicalRecord
            {
                Id = 3,
                VisitId = 3,
                CreatedByDoctorId = 2,
                Status = "FINALIZED",
                FinalizedAt = new DateTime(2026, 10, 20, 11, 0, 0),
            },
            new MedicalRecord
            {
                Id = 2,
                VisitId = 2,
                CreatedByDoctorId = 2,
                Status = "FINALIZED",
                FinalizedAt = new DateTime(2026, 10, 16, 11, 25, 0),
            });

        db.MedicalRecordRevisions.AddRange(
            new MedicalRecordRevision
            {
                Id = 1,
                MedicalRecordId = 1,
                RevisionNo = 1,
                PresentingComplaint = "Đau thượng vị",
                SymptomHistory = "Triệu chứng mức độ nhẹ, theo dõi ngoại trú (demo).",
                RelevantHistoryAllergies = "Dị ứng penicillin (cần lưu ý)",
                VitalSigns = """{"bp_systolic_mmhg":119,"bp_diastolic_mmhg":76,"pulse_bpm":73,"temperature_c":36.6,"spo2_percent":98,"height_cm":161,"weight_kg":56}""",
                ClinicalFindings = "Khám lâm sàng ổn định (nội dung mô phỏng).",
                ClinicalConclusion = "Viêm dạ dày mạn tính - kết quả minh họa.",
                EditedByDoctorId = 1,
                RecordedAt = new DateTime(2026, 10, 15, 11, 20, 0),
                IsConfirmed = true,
            },
            new MedicalRecordRevision
            {
                Id = 3,
                MedicalRecordId = 3,
                RevisionNo = 1,
                PresentingComplaint = "Hồi hộp nhẹ",
                SymptomHistory = "Theo dõi huyết áp ngoại trú.",
                RelevantHistoryAllergies = "Dị ứng penicillin (cần lưu ý)",
                VitalSigns = """{"bp_systolic_mmhg":128,"bp_diastolic_mmhg":82,"pulse_bpm":78,"temperature_c":36.7,"spo2_percent":99,"height_cm":161,"weight_kg":56}""",
                ClinicalFindings = "Tim đều, phổi trong.",
                ClinicalConclusion = "Tăng huyết áp độ 1.",
                EditedByDoctorId = 2,
                RecordedAt = new DateTime(2026, 10, 20, 10, 50, 0),
                IsConfirmed = true,
            });

        db.ClinicalAddenda.Add(
            new ClinicalAddendum
            {
                Id = 1,
                VisitId = 1,
                RecordType = "MEDICAL_RECORD",
                TargetRecordId = 1,
                CorrectedContent = """{"note":"Đính chính lỗi chính tả trong ghi chú demo.","original_record_preserved":true}""",
                Reason = "Đính chính mô tả không làm thay đổi kết luận.",
                AuthorDoctorId = 1,
                RecordedAt = new DateTime(2026, 10, 15, 14, 0, 0),
            });

        db.VisitDiagnoses.AddRange(
            new VisitDiagnosis
            {
                Id = 1,
                VisitId = 1,
                Icd10CodeId = 1,
                IsPrimary = true,
                CodeSnapshot = "K29.5",
                NameSnapshot = "Viêm dạ dày mạn tính",
                CatalogVersion = "DEMO-ICD10",
                ClinicalDiagnosis = "Viêm dạ dày mạn tính (demo)",
                Status = "CONFIRMED",
                ConfirmedAt = new DateTime(2026, 10, 15, 11, 20, 0),
                RecordedByDoctorId = 1,
                RecordedAt = new DateTime(2026, 10, 15, 11, 15, 0),
            },
            new VisitDiagnosis
            {
                Id = 3,
                VisitId = 3,
                Icd10CodeId = 2,
                IsPrimary = true,
                CodeSnapshot = "I10",
                NameSnapshot = "Tăng huyết áp vô căn (nguyên phát)",
                CatalogVersion = "DEMO-ICD10",
                ClinicalDiagnosis = "Tăng huyết áp vô căn",
                Status = "CONFIRMED",
                ConfirmedAt = new DateTime(2026, 10, 20, 10, 55, 0),
                RecordedByDoctorId = 2,
                RecordedAt = new DateTime(2026, 10, 20, 10, 50, 0),
            });

        db.DiagnosticOrders.Add(
            new DiagnosticOrder
            {
                Id = 1,
                OrderCode = "DIA-202610-0001",
                VisitId = 1,
                OrderingDoctorId = 1,
                ServiceId = 5,
                QueueEntryId = 11,
                Instruction = "Thực hiện theo quy trình an toàn của cơ sở (demo).",
                RequiredForConclusion = true,
                Priority = "NORMAL",
                Status = "COMPLETED",
                ConfirmedByStaffId = 1,
                ConfirmedAt = new DateTime(2026, 10, 15, 9, 35, 0),
                OrderedAt = new DateTime(2026, 10, 15, 9, 30, 0),
            });

        db.DiagnosticResults.Add(
            new DiagnosticResult
            {
                Id = 1,
                DiagnosticOrderId = 1,
                Status = "FINALIZED",
                FinalizedAt = new DateTime(2026, 10, 15, 10, 35, 0),
                FinalizedByStaffId = 1,
            });

        db.DiagnosticResultVersions.Add(
            new DiagnosticResultVersion
            {
                Id = 1,
                DiagnosticResultId = 1,
                VersionNo = 1,
                ResultText = "Niêm mạc hang vị xung huyết nhẹ.",
                Conclusion = "Viêm dạ dày mạn tính.",
                ReportFileUrl = "https://cdn.medflow.test/reports/dia-0001.pdf",
                EnteredByStaffId = 1,
                EnteredAt = new DateTime(2026, 10, 15, 10, 25, 0),
                IsFinalized = true,
                VerifiedByStaffId = 1,
                VerifiedAt = new DateTime(2026, 10, 15, 10, 35, 0),
            });

        db.Medicines.Add(
            new Medicine
            {
                Id = 1,
                HospitalId = 1,
                MedicineCode = "MED-001",
                Name = "Omeprazole",
                ActiveIngredient = "Omeprazole",
                Strength = "20 mg",
                DosageForm = "Viên nang",
                DispensingUnit = "Viên",
                ContentQuantity = 1,
                ContentUnit = "viên",
                IsActive = true,
            });

        db.Prescriptions.Add(
            new Prescription
            {
                Id = 1,
                PrescriptionCode = "RX-202610-0001",
                VisitId = 1,
                PrescribingDoctorId = 1,
                FulfillmentChoice = "HOSPITAL",
                Status = "DISPENSED",
                IssuedAt = new DateTime(2026, 10, 15, 11, 15, 0),
                CreatedAt = new DateTime(2026, 10, 15, 11, 5, 0),
            });

        db.PrescriptionItems.Add(
            new PrescriptionItem
            {
                Id = 1,
                PrescriptionId = 1,
                MedicineId = 1,
                PrescribedQuantity = 14,
                SingleDoseQuantity = 1,
                SingleDoseUnit = "Viên",
                DoseScheduleText = "1 viên trước ăn sáng 30 phút",
                Frequency = "1 lần/ngày",
                DurationDays = 14,
                AdministrationRoute = "ORAL",
                Instructions = "Uống nguyên viên với nước ấm.",
            });

        db.SaveChanges();
    }

    [Fact]
    public async Task GetByPatientAsync_ValidDoctorAndAssignedPatient_ReturnsCompleteClinicalHistoryAndWritesAuditLog()
    {
        var result = await _service.GetByPatientAsync(
            actorUserId: 3,
            patientIdOrCode: "PAT-0001",
            request: new GetPatientMedicalHistoryRequest(),
            ipAddress: "127.0.0.1");

        Assert.Equal("Bệnh viện Trung ương MedFlow", result.FacilityName);
        Assert.Equal("MEDFLOW-HN-01", result.FacilityCode);

        // Patient summary (no raw PII phone/email/address/identificationHash exposed on DTO)
        Assert.Equal(1, result.Patient.PatientId);
        Assert.Equal("PAT-0001", result.Patient.PatientCode);
        Assert.Equal("Lê Thị Mai", result.Patient.FullName);
        Assert.Equal("FEMALE", result.Patient.Gender);
        Assert.Equal("1990-05-10", result.Patient.DateOfBirth);
        Assert.Equal(36, result.Patient.Age);
        Assert.Equal("O+", result.Patient.BloodType);
        Assert.Equal("Dị ứng penicillin (cần lưu ý)", result.Patient.AllergiesSummary);
        Assert.Equal(2, result.Patient.ChronicConditions.Count);

        var serializedJson = JsonSerializer.Serialize(result);
        Assert.DoesNotContain("0901234567", serializedJson);
        Assert.DoesNotContain("secret-hash-0001", serializedJson);

        // Stats include both non-cancelled hospital visits (Visit 3 and Visit 1), excluding cancelled Visit 99
        Assert.Equal(2, result.Stats.TotalVisits);
        Assert.Equal(2, result.Stats.CompletedVisits);
        Assert.Equal(2, result.Stats.FinalizedRecordsCount);
        Assert.Equal(2, result.Stats.TotalDiagnosesCount);
        Assert.Equal(1, result.Stats.TotalDiagnosticOrdersCount);
        Assert.Equal(1, result.Stats.TotalPrescriptionsCount);

        // Timeline ordered newest first: Visit 3 (2026-10-20) then Visit 1 (2026-10-15)
        Assert.Equal(2, result.Visits.Count);
        Assert.Equal("VIS-202610-0003", result.Visits[0].VisitCode);
        Assert.False(result.Visits[0].IsAssignedToCurrentDoctor);

        var visit1 = result.Visits[1];
        Assert.Equal("VIS-202610-0001", visit1.VisitCode);
        Assert.True(visit1.IsAssignedToCurrentDoctor);
        Assert.NotNull(visit1.MedicalRecord);
        Assert.Equal("FINALIZED", visit1.MedicalRecord!.Status);
        Assert.NotNull(visit1.MedicalRecord.VitalSigns);
        Assert.Equal("119/76 mmHg", visit1.MedicalRecord.VitalSigns!.BloodPressureLabel);
        Assert.Equal(21.6m, visit1.MedicalRecord.VitalSigns.Bmi);

        var addendum = Assert.Single(visit1.MedicalRecord.Addenda);
        Assert.Equal("Đính chính lỗi chính tả trong ghi chú demo.", addendum.Note);

        var diagnosis = Assert.Single(visit1.Diagnoses);
        Assert.Equal("K29.5", diagnosis.Icd10Code);
        Assert.Equal("Viêm dạ dày mạn tính", diagnosis.Icd10NameVi);
        Assert.True(diagnosis.IsPrimary);

        var diagOrder = Assert.Single(visit1.DiagnosticOrders);
        Assert.Equal("DIA-202610-0001", diagOrder.OrderCode);
        Assert.True(diagOrder.IsResultFinalized);
        Assert.Equal("Viêm dạ dày mạn tính.", diagOrder.Conclusion);

        var prescription = Assert.Single(visit1.Prescriptions);
        Assert.Equal("RX-202610-0001", prescription.PrescriptionCode);
        var rxItem = Assert.Single(prescription.Items);
        Assert.Equal("Omeprazole", rxItem.MedicineName);
        Assert.Equal(14m, rxItem.PrescribedQuantity);

        // Verify PHI AuditLog written
        var auditLog = Assert.Single(_dbContext.AuditLogs.Where(a => a.Action == "MEDICAL_HISTORY_VIEWED"));
        Assert.Equal(3, auditLog.ActorUserId);
        Assert.Equal(1, auditLog.HospitalId);
        Assert.Equal("PATIENT", auditLog.EntityType);
        Assert.Equal("1", auditLog.EntityId);
    }

    [Fact]
    public async Task GetByVisitAsync_ValidVisitCode_ResolvesPatientAndMarksCurrentVisit()
    {
        var withCurrent = await _service.GetByVisitAsync(
            actorUserId: 3,
            visitIdOrCode: "VIS-202610-0001",
            request: new GetPatientMedicalHistoryRequest(IncludeCurrentVisit: true));

        Assert.NotNull(withCurrent.CurrentVisit);
        Assert.Equal("VIS-202610-0001", withCurrent.CurrentVisit!.VisitCode);
        Assert.Equal(2, withCurrent.Visits.Count);
        Assert.Contains(withCurrent.Visits, v => v.VisitCode == "VIS-202610-0001" && v.IsCurrentVisit);

        var withoutCurrent = await _service.GetByVisitAsync(
            actorUserId: 3,
            visitIdOrCode: "VIS-202610-0001",
            request: new GetPatientMedicalHistoryRequest(IncludeCurrentVisit: false));

        var remaining = Assert.Single(withoutCurrent.Visits);
        Assert.Equal("VIS-202610-0003", remaining.VisitCode);
        Assert.Equal(2, withoutCurrent.Stats.TotalVisits);
    }

    [Fact]
    public async Task GetByPatientAsync_FiltersByDateDepartmentSpecialtyStatusKeywordAndLimit_ReturnsMatchingEntries()
    {
        var gastroOnly = await _service.GetByPatientAsync(
            actorUserId: 3,
            patientIdOrCode: "PAT-0001",
            request: new GetPatientMedicalHistoryRequest(
                DepartmentId: 1,
                SpecialtyCode: "GASTRO",
                Status: "FINALIZED",
                FromDate: "2026-10-01",
                ToDate: "2026-10-16",
                Keyword: "Omeprazole",
                Limit: 5));

        var entry = Assert.Single(gastroOnly.Visits);
        Assert.Equal("VIS-202610-0001", entry.VisitCode);
        Assert.Equal(2, gastroOnly.Stats.TotalVisits);
    }

    [Fact]
    public async Task GetByPatientAsync_PatientBelongsOnlyToAnotherDoctor_ThrowsForbiddenException()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetByPatientAsync(
                actorUserId: 3,
                patientIdOrCode: "PAT-0002"));
    }

    [Fact]
    public async Task GetByVisitAsync_VisitAssignedToAnotherDoctor_ThrowsForbiddenException()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetByVisitAsync(
                actorUserId: 3,
                visitIdOrCode: "VIS-202610-0002"));
    }

    [Fact]
    public async Task GetByVisitAsync_CancelledVisit_ThrowsConflictException()
    {
        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.GetByVisitAsync(
                actorUserId: 3,
                visitIdOrCode: "VIS-202610-0099"));
    }

    [Theory]
    [InlineData(10)] // Non-doctor (RECEPTION)
    [InlineData(14)] // Locked user
    [InlineData(15)] // Suspended user
    [InlineData(16)] // Inactive staff
    [InlineData(17)] // Missing staff
    [InlineData(18)] // Missing doctor profile
    [InlineData(19)] // Inactive doctor profile
    [InlineData(20)] // Inactive hospital
    public async Task GetByPatientAsync_InactiveDoctorOrLockedUserOrMissingRole_ThrowsForbiddenException(long actorUserId)
    {
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetByPatientAsync(
                actorUserId: actorUserId,
                patientIdOrCode: "PAT-0001"));
    }

    [Fact]
    public async Task GetByPatientAsync_MissingClinicalPermissions_ThrowsForbiddenException()
    {
        _dbContext.RolePermissions.RemoveRange(_dbContext.RolePermissions.ToList());
        _dbContext.RolePermissions.Add(new RolePermission { RoleId = 2, PermissionId = 99 });
        await _dbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetByPatientAsync(
                actorUserId: 3,
                patientIdOrCode: "PAT-0001"));
    }

    [Theory]
    [InlineData("", 20, null, null, null, null, null)]
    [InlineData("0", 20, null, null, null, null, null)]
    [InlineData("-5", 20, null, null, null, null, null)]
    [InlineData("PAT-0001", 0, null, null, null, null, null)]
    [InlineData("PAT-0001", 51, null, null, null, null, null)]
    [InlineData("PAT-0001", 20, "INVALID_STATUS", null, null, null, null)]
    [InlineData("PAT-0001", 20, null, "2026-13-40", null, null, null)]
    [InlineData("PAT-0001", 20, null, "2026-10-20", "2026-10-10", null, null)]
    [InlineData("PAT-0001", 20, null, null, null, "VIS-202610-0002", null)]
    public async Task GetByPatientAsync_InvalidInputs_ThrowsValidationException(
        string patientIdOrCode,
        int limit,
        string? status,
        string? fromDate,
        string? toDate,
        string? visitIdOrCode,
        string? keyword)
    {
        var request = new GetPatientMedicalHistoryRequest(
            VisitIdOrCode: visitIdOrCode,
            Status: status,
            FromDate: fromDate,
            ToDate: toDate,
            Keyword: keyword,
            Limit: limit);

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.GetByPatientAsync(
                actorUserId: 3,
                patientIdOrCode: patientIdOrCode,
                request: request));
    }

    [Fact]
    public async Task GetByPatientAsync_TooLongKeyword_ThrowsValidationException()
    {
        var request = new GetPatientMedicalHistoryRequest(Keyword: new string('A', 101));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.GetByPatientAsync(
                actorUserId: 3,
                patientIdOrCode: "PAT-0001",
                request: request));
    }

    [Fact]
    public async Task GetByPatientAsync_NonExistentPatientOrVisit_ThrowsNotFoundException()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.GetByPatientAsync(
                actorUserId: 3,
                patientIdOrCode: "PAT-9999"));

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.GetByVisitAsync(
                actorUserId: 3,
                visitIdOrCode: "VIS-202610-9999"));
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}
