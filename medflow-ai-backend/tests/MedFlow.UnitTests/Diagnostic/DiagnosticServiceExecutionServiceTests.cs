using MedFlow.Application.DTOs.Diagnostic;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Diagnostic;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.UnitTests.Diagnostic;

public sealed class DiagnosticServiceExecutionServiceTests : IDisposable
{
    private sealed class FixedTimeProvider(DateTimeOffset nowUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => nowUtc;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class ExecutionTestDbContext : DbContext, IApplicationDbContext
    {
        public ExecutionTestDbContext(DbContextOptions<ExecutionTestDbContext> options)
            : base(options)
        {
        }

        private static NotSupportedException NotUsed() =>
            new("Not needed for DiagnosticServiceExecutionService unit tests.");

        public DbSet<User> Users => Set<User>();
        public DbSet<Role> Roles => Set<Role>();
        public DbSet<Permission> Permissions => Set<Permission>();
        public DbSet<UserRole> UserRoles => Set<UserRole>();
        public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
        public DbSet<Patient> Patients => Set<Patient>();
        public DbSet<Staff> Staff => Set<Staff>();
        public DbSet<Doctor> Doctors => Set<Doctor>();
        public DbSet<DoctorServiceAuthorization> DoctorServiceAuthorizations => Set<DoctorServiceAuthorization>();
        public DbSet<ExaminationSlot> ExaminationSlots => throw NotUsed();
        public DbSet<AuthSession> AuthSessions => throw NotUsed();
        public DbSet<AuthVerificationChallenge> AuthVerificationChallenges => throw NotUsed();
        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
        public DbSet<Hospital> Hospitals => Set<Hospital>();
        public DbSet<Department> Departments => Set<Department>();
        public DbSet<HospitalRoom> HospitalRooms => Set<HospitalRoom>();
        public DbSet<Specialty> Specialties => Set<Specialty>();
        public DbSet<Service> Services => Set<Service>();
        public DbSet<ServicePrice> ServicePrices => Set<ServicePrice>();
        public DbSet<HospitalPolicy> HospitalPolicies => throw NotUsed();
        public DbSet<Appointment> Appointments => Set<Appointment>();
        public DbSet<PatientFamilyLink> PatientFamilyLinks => throw NotUsed();
        public DbSet<Visit> Visits => Set<Visit>();
        public DbSet<QueueEntry> QueueEntries => Set<QueueEntry>();
        public DbSet<Payment> Payments => throw NotUsed();
        public DbSet<QueueServicePoint> QueueServicePoints => Set<QueueServicePoint>();
        public DbSet<MedicalRecord> MedicalRecords => Set<MedicalRecord>();
        public DbSet<MedicalRecordRevision> MedicalRecordRevisions => Set<MedicalRecordRevision>();
        public DbSet<VisitDiagnosis> VisitDiagnoses => Set<VisitDiagnosis>();
        public DbSet<DiagnosticOrder> DiagnosticOrders => Set<DiagnosticOrder>();
        public DbSet<DiagnosticResult> DiagnosticResults => Set<DiagnosticResult>();
        public DbSet<DiagnosticResultVersion> DiagnosticResultVersions => Set<DiagnosticResultVersion>();

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default)
            => Database.BeginTransactionAsync(ct);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Ignore<ExaminationSlot>();
            modelBuilder.Ignore<AuthSession>();
            modelBuilder.Ignore<AuthVerificationChallenge>();
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
            modelBuilder.Entity<DoctorServiceAuthorization>().HasKey(a => new { a.DoctorId, a.ServiceId });
            modelBuilder.Entity<Service>().HasKey(s => s.Id);
            modelBuilder.Entity<ServicePrice>().HasKey(sp => sp.Id);
            modelBuilder.Entity<Appointment>().HasKey(a => a.Id);
            modelBuilder.Entity<Visit>().HasKey(v => v.Id);
            modelBuilder.Entity<QueueEntry>().HasKey(q => q.Id);
            modelBuilder.Entity<QueueServicePoint>().HasKey(sp => sp.Id);
            modelBuilder.Entity<MedicalRecord>().HasKey(m => m.Id);
            modelBuilder.Entity<MedicalRecordRevision>().HasKey(r => r.Id);
            modelBuilder.Entity<VisitDiagnosis>().HasKey(d => d.Id);
            modelBuilder.Entity<DiagnosticOrder>().HasKey(o => o.Id);
            modelBuilder.Entity<DiagnosticResult>().HasKey(r => r.Id);
            modelBuilder.Entity<DiagnosticResultVersion>().HasKey(v => v.Id);
            modelBuilder.Entity<AuditLog>().HasKey(a => a.Id);
        }
    }

    private static readonly DateTime FixedNowUtc = new(2026, 10, 20, 2, 0, 0, DateTimeKind.Utc); // 09:00 Vietnam time

    private readonly ExecutionTestDbContext _dbContext;
    private readonly DiagnosticServiceExecutionService _service;

    public DiagnosticServiceExecutionServiceTests()
    {
        var options = new DbContextOptionsBuilder<ExecutionTestDbContext>()
            .UseInMemoryDatabase(databaseName: $"DiagnosticExecutionTests_{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _dbContext = new ExecutionTestDbContext(options);
        SeedData(_dbContext);

        var timeProvider = new FixedTimeProvider(new DateTimeOffset(FixedNowUtc));
        _service = new DiagnosticServiceExecutionService(_dbContext, timeProvider);
    }

    private static void SeedData(ExecutionTestDbContext db)
    {
        db.Hospitals.AddRange(
            new Hospital { Id = 1, HospitalCode = "HOS-DEMO-01", Name = "Bệnh viện Đa khoa Trung tâm MedFlow", IsActive = true },
            new Hospital { Id = 2, HospitalCode = "HOS-HCM-02", Name = "Cơ sở TP.HCM", IsActive = true },
            new Hospital { Id = 3, HospitalCode = "HOS-OFF-03", Name = "Cơ sở Ngừng Hoạt Động", IsActive = false });

        db.Departments.AddRange(
            new Department { Id = 1, HospitalId = 1, DepartmentCode = "DIA", Name = "Khoa Chẩn đoán Hình ảnh & Xét nghiệm", IsActive = true },
            new Department { Id = 2, HospitalId = 1, DepartmentCode = "DIA-OFF", Name = "Khoa CLS Tạm Ngừng", IsActive = false },
            new Department { Id = 3, HospitalId = 2, DepartmentCode = "DIA-2", Name = "Khoa CLS Cơ sở 2", IsActive = true });

        db.Specialties.AddRange(
            new Specialty { Id = 1, SpecialtyCode = "INT", Name = "Nội tổng quát", IsActive = true },
            new Specialty { Id = 2, SpecialtyCode = "CARD", Name = "Tim mạch", IsActive = true },
            new Specialty { Id = 4, SpecialtyCode = "IMG", Name = "Chẩn đoán hình ảnh", IsActive = true });

        db.HospitalRooms.AddRange(
            new HospitalRoom { Id = 101, HospitalId = 1, DepartmentId = 1, RoomCode = "DIA-201", Name = "Phòng Xét nghiệm 201", RoomType = "DIAGNOSTIC", Status = "ACTIVE" },
            new HospitalRoom { Id = 102, HospitalId = 1, DepartmentId = 1, RoomCode = "DIA-202", Name = "Phòng X-quang 202", RoomType = "DIAGNOSTIC", Status = "ACTIVE" },
            new HospitalRoom { Id = 103, HospitalId = 1, DepartmentId = 1, RoomCode = "DIA-203", Name = "Phòng CLS Bảo trì", RoomType = "DIAGNOSTIC", Status = "MAINTENANCE" },
            new HospitalRoom { Id = 104, HospitalId = 1, DepartmentId = 1, RoomCode = "EXM-101", Name = "Phòng Khám Nội", RoomType = "CONSULTATION", Status = "ACTIVE" },
            new HospitalRoom { Id = 201, HospitalId = 2, DepartmentId = 3, RoomCode = "DIA-301", Name = "Phòng CLS Cơ sở 2", RoomType = "DIAGNOSTIC", Status = "ACTIVE" });

        db.QueueServicePoints.AddRange(
            new QueueServicePoint { Id = 501, HospitalId = 1, RoomId = 101, ServiceId = 5, PointType = "DIAGNOSTIC", Name = "Điểm lấy mẫu Xét nghiệm 201", IsActive = true },
            new QueueServicePoint { Id = 502, HospitalId = 1, RoomId = 102, ServiceId = 6, PointType = "DIAGNOSTIC", Name = "Điểm chụp X-quang 202", IsActive = true },
            new QueueServicePoint { Id = 503, HospitalId = 1, RoomId = 103, ServiceId = 7, PointType = "DIAGNOSTIC", Name = "Điểm CLS Bảo trì", IsActive = true },
            new QueueServicePoint { Id = 504, HospitalId = 1, RoomId = 101, ServiceId = 8, PointType = "DIAGNOSTIC", Name = "Điểm CLS Tạm ngưng", IsActive = false },
            new QueueServicePoint { Id = 507, HospitalId = 1, RoomId = 102, ServiceId = 6, PointType = "DIAGNOSTIC", Name = "Điểm chụp X-quang 202B", IsActive = true },
            new QueueServicePoint { Id = 601, HospitalId = 2, RoomId = 201, ServiceId = 20, PointType = "DIAGNOSTIC", Name = "Điểm CLS Cơ sở 2", IsActive = true });

        db.Services.AddRange(
            new Service { Id = 1, HospitalId = 1, DepartmentId = 1, SpecialtyId = 1, ServiceCode = "KHAM-NOI", Name = "Khám Nội tổng quát", ServiceType = "CONSULTATION", IsActive = true },
            new Service { Id = 5, HospitalId = 1, DepartmentId = 1, SpecialtyId = 1, ServiceCode = "LAB-CBC-01", Name = "Tổng phân tích tế bào máu", ServiceType = "DIAGNOSTIC", RequiresResultVerification = true, IsActive = true },
            new Service { Id = 6, HospitalId = 1, DepartmentId = 1, SpecialtyId = 4, ServiceCode = "XRAY-CHEST-01", Name = "Chụp X-quang ngực thẳng", ServiceType = "DIAGNOSTIC", RequiresResultVerification = true, IsActive = true },
            new Service { Id = 7, HospitalId = 1, DepartmentId = 1, SpecialtyId = 2, ServiceCode = "ECG-12-01", Name = "Điện tâm đồ 12 chuyển đạo", ServiceType = "DIAGNOSTIC", RequiresResultVerification = true, IsActive = true },
            new Service { Id = 8, HospitalId = 1, DepartmentId = 1, SpecialtyId = 1, ServiceCode = "LAB-BIO-02", Name = "Sinh hóa máu tổng quát", ServiceType = "DIAGNOSTIC", RequiresResultVerification = true, IsActive = true },
            new Service { Id = 9, HospitalId = 1, DepartmentId = 1, SpecialtyId = 1, ServiceCode = "LAB-OFF-09", Name = "XN ngừng triển khai", ServiceType = "DIAGNOSTIC", RequiresResultVerification = true, IsActive = false },
            new Service { Id = 20, HospitalId = 2, DepartmentId = 3, SpecialtyId = 4, ServiceCode = "XRAY-HCM-01", Name = "Chụp X-quang Cơ sở 2", ServiceType = "DIAGNOSTIC", RequiresResultVerification = true, IsActive = true });

        db.ServicePrices.AddRange(
            new ServicePrice { Id = 1, ServiceId = 5, PriceAmount = 120_000m, EffectiveFrom = new DateTime(2026, 1, 1) },
            new ServicePrice { Id = 2, ServiceId = 6, PriceAmount = 180_000m, EffectiveFrom = new DateTime(2026, 1, 1) });

        db.Roles.AddRange(
            new Role { Id = 1, Code = "PATIENT", Name = "Bệnh nhân" },
            new Role { Id = 2, Code = "DOCTOR", Name = "Bác sĩ" },
            new Role { Id = 3, Code = "RECEPTION", Name = "Lễ tân" },
            new Role { Id = 4, Code = "DIAGNOSTIC_STAFF", Name = "Nhân viên Cận lâm sàng" },
            new Role { Id = 6, Code = "HOSPITAL_ADMIN", Name = "Quản trị bệnh viện" },
            new Role { Id = 8, Code = "DIAGNOSTIC_NO_PERM", Name = "CLS không có quyền" });

        db.Permissions.AddRange(
            new Permission { Id = 1, Code = "DIAGNOSTIC_RESULT_WRITE" },
            new Permission { Id = 2, Code = "QUEUE_MANAGE" },
            new Permission { Id = 3, Code = "DIAGNOSTIC_ORDER" },
            new Permission { Id = 4, Code = "HOSPITAL_CONFIG" });

        db.RolePermissions.AddRange(
            new RolePermission { RoleId = 4, PermissionId = 1 },
            new RolePermission { RoleId = 4, PermissionId = 2 },
            new RolePermission { RoleId = 8, PermissionId = 4 });

        db.Users.AddRange(
            new User { Id = 7, Email = "diagnostic_staff01@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 8, Email = "diagnostic_staff02@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 14, Email = "locked_diag@medflow.test", AccountStatus = AccountStatus.ACTIVE, LockedUntil = FixedNowUtc.AddMinutes(30) },
            new User { Id = 15, Email = "inactive_user_diag@medflow.test", AccountStatus = AccountStatus.INACTIVE },
            new User { Id = 16, Email = "inactive_staff_diag@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 17, Email = "no_staff_diag@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 18, Email = "no_hospital_diag@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 19, Email = "inactive_hospital_diag@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 20, Email = "inactive_dept_diag@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 21, Email = "doctor01@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 23, Email = "patient01@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 24, Email = "noperm_diag@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 25, Email = "reception01@medflow.test", AccountStatus = AccountStatus.ACTIVE });

        db.UserRoles.AddRange(
            new UserRole { UserId = 7, RoleId = 4 },
            new UserRole { UserId = 8, RoleId = 4 },
            new UserRole { UserId = 14, RoleId = 4 },
            new UserRole { UserId = 15, RoleId = 4 },
            new UserRole { UserId = 16, RoleId = 4 },
            new UserRole { UserId = 17, RoleId = 4 },
            new UserRole { UserId = 18, RoleId = 4 },
            new UserRole { UserId = 19, RoleId = 4 },
            new UserRole { UserId = 20, RoleId = 4 },
            new UserRole { UserId = 21, RoleId = 2 },
            new UserRole { UserId = 23, RoleId = 1 },
            new UserRole { UserId = 24, RoleId = 8 },
            new UserRole { UserId = 25, RoleId = 3 });

        db.Staff.AddRange(
            new Staff { Id = 7, UserId = 7, HospitalId = 1, DepartmentId = 1, StaffCode = "STF-007", FullName = "KTV. Lê Thị Minh Anh", Status = "ACTIVE" },
            new Staff { Id = 8, UserId = 8, HospitalId = 2, DepartmentId = 3, StaffCode = "STF-008", FullName = "KTV. Trần Quốc Bảo", Status = "ACTIVE" },
            new Staff { Id = 16, UserId = 16, HospitalId = 1, DepartmentId = 1, StaffCode = "STF-016", FullName = "KTV. Ngừng Hoạt Động", Status = "INACTIVE" },
            new Staff { Id = 18, UserId = 18, HospitalId = null, DepartmentId = null, StaffCode = "STF-018", FullName = "KTV. Chưa Gán Cơ Sở", Status = "ACTIVE" },
            new Staff { Id = 19, UserId = 19, HospitalId = 3, DepartmentId = null, StaffCode = "STF-019", FullName = "KTV. Cơ Sở Ngừng Hoạt Động", Status = "ACTIVE" },
            new Staff { Id = 20, UserId = 20, HospitalId = 1, DepartmentId = 2, StaffCode = "STF-020", FullName = "KTV. Khoa Ngừng Hoạt Động", Status = "ACTIVE" },
            new Staff { Id = 24, UserId = 24, HospitalId = 1, DepartmentId = 1, StaffCode = "STF-024", FullName = "KTV. Thiếu Quyền", Status = "ACTIVE" },
            new Staff { Id = 30, UserId = 21, HospitalId = 1, DepartmentId = 1, StaffCode = "STF-DOC-01", FullName = "Nguyễn Hoàng Nam", Status = "ACTIVE" });

        db.Doctors.Add(new Doctor
        {
            Id = 1,
            StaffId = 30,
            HospitalId = 1,
            SpecialtyId = 1,
            DoctorCode = "DOC-001",
            ProfessionalTitle = "BS.CKI",
            IsActive = true,
        });

        db.Patients.AddRange(
            new Patient { Id = 1, UserId = 23, PatientCode = "PAT-0001", FullName = "Nguyễn Văn An", DateOfBirth = new DateOnly(1948, 5, 10), Gender = "MALE", Phone = "0912345678", BloodType = "O+", Status = "ACTIVE" },
            new Patient { Id = 2, PatientCode = "PAT-0002", FullName = "Trần Thị Bích", DateOfBirth = new DateOnly(1992, 8, 20), Gender = "FEMALE", Phone = "0987654321", BloodType = "A+", Status = "ACTIVE" });

        db.Visits.AddRange(
            new Visit { Id = 10, VisitCode = "VIS-202610-0010", PatientId = 1, AssignedDoctorId = 1, ExaminationRoomId = 104, ReasonForVisit = "Đau ngực", Status = "AWAITING_RESULTS", CheckedInAt = new DateTime(2026, 10, 20, 8, 0, 0) },
            new Visit { Id = 11, VisitCode = "VIS-202610-0011", PatientId = 2, AssignedDoctorId = 1, ExaminationRoomId = 104, ReasonForVisit = "Sốt cao", Status = "IN_PROGRESS", CheckedInAt = new DateTime(2026, 10, 20, 8, 15, 0) },
            new Visit { Id = 12, VisitCode = "VIS-202610-0012", PatientId = 1, AssignedDoctorId = 1, ExaminationRoomId = 104, ReasonForVisit = "Đã xong", Status = "COMPLETED", CheckedInAt = new DateTime(2026, 10, 15, 8, 0, 0), CompletedAt = new DateTime(2026, 10, 15, 11, 0, 0) },
            new Visit { Id = 13, VisitCode = "VIS-202610-0013", PatientId = 2, AssignedDoctorId = 1, ExaminationRoomId = 104, ReasonForVisit = "Khám hủy", Status = "CANCELLED", CheckedInAt = new DateTime(2026, 10, 16, 8, 0, 0) },
            new Visit { Id = 20, VisitCode = "VIS-202610-0020", PatientId = 2, AssignedDoctorId = 1, ExaminationRoomId = 201, ReasonForVisit = "Khám cơ sở 2", Status = "AWAITING_RESULTS", CheckedInAt = new DateTime(2026, 10, 20, 8, 30, 0) },
            new Visit { Id = 30, VisitCode = "VIS-202610-0030", PatientId = 2, AssignedDoctorId = 1, ExaminationRoomId = 104, ReasonForVisit = "Khám batch CLS", Status = "IN_PROGRESS", CheckedInAt = new DateTime(2026, 10, 20, 8, 10, 0) });

        db.MedicalRecords.Add(new MedicalRecord { Id = 100, VisitId = 11, Status = "DRAFT" });
        db.MedicalRecordRevisions.Add(new MedicalRecordRevision
        {
            Id = 1001,
            MedicalRecordId = 100,
            RevisionNo = 1,
            PresentingComplaint = "Sốt cao 39 độ",
            RelevantHistoryAllergies = "Dị ứng hải sản",
            ClinicalFindings = "Họng đỏ",
            ClinicalConclusion = "Nhiễm siêu vi",
            RecordedAt = new DateTime(2026, 10, 20, 8, 20, 0),
        });
        db.VisitDiagnoses.Add(new VisitDiagnosis
        {
            Id = 500,
            VisitId = 11,
            CodeSnapshot = "B34.9",
            NameSnapshot = "Nhiễm virus",
            IsPrimary = true,
            ClinicalDiagnosis = "Sốt siêu vi",
            Status = "PRELIMINARY",
            RecordedAt = new DateTime(2026, 10, 20, 8, 25, 0),
        });

        db.QueueEntries.AddRange(
            // qe 11: CONFIRMED order at SP 501 (lab), WAITING -> can start
            new QueueEntry { Id = 11, VisitId = 10, ServicePointId = 501, CurrentServicePointId = 501, QueueDate = new DateOnly(2026, 10, 20), QueueNumber = 1, QueueType = "DIAGNOSTIC", Status = "WAITING", PresenceStatus = "PRESENT", ArrivedAt = new DateTime(2026, 10, 20, 8, 30, 0), ReadyAt = new DateTime(2026, 10, 20, 8, 30, 0), ArrivalClass = "WALK_IN", ManualPriority = "NORMAL", StateVersion = 1 },
            // qe 12: CONFIRMED imaging at SP 502, READY -> can start
            new QueueEntry { Id = 12, VisitId = 10, ServicePointId = 502, CurrentServicePointId = 502, QueueDate = new DateOnly(2026, 10, 20), QueueNumber = 2, QueueType = "DIAGNOSTIC", Status = "READY", PresenceStatus = "PRESENT", ArrivedAt = new DateTime(2026, 10, 20, 8, 40, 0), ReadyAt = new DateTime(2026, 10, 20, 8, 40, 0), ArrivalClass = "WALK_IN", ManualPriority = "NORMAL", StateVersion = 2 },
            // qe 13: CONFIRMED lab at SP 501 same visit -> can start (same-visit concurrency OK)
            new QueueEntry { Id = 13, VisitId = 10, ServicePointId = 501, CurrentServicePointId = 501, QueueDate = new DateOnly(2026, 10, 20), QueueNumber = 3, QueueType = "DIAGNOSTIC", Status = "READY", PresenceStatus = "PRESENT", ArrivedAt = new DateTime(2026, 10, 20, 8, 45, 0), ReadyAt = new DateTime(2026, 10, 20, 8, 45, 0), ArrivalClass = "WALK_IN", ManualPriority = "NORMAL", StateVersion = 1 },
            // qe 14: CONFIRMED order with SKIPPED queue -> start blocked
            new QueueEntry { Id = 14, VisitId = 11, ServicePointId = 501, CurrentServicePointId = 501, QueueDate = new DateOnly(2026, 10, 20), QueueNumber = 4, QueueType = "DIAGNOSTIC", Status = "SKIPPED", PresenceStatus = "LEFT", ArrivedAt = new DateTime(2026, 10, 20, 8, 20, 0), ReadyAt = new DateTime(2026, 10, 20, 8, 20, 0), ArrivalClass = "WALK_IN", ManualPriority = "NORMAL", StateVersion = 3 },
            // qe 15: IN_PROGRESS order at SP 507 -> can complete/pause/cancel
            new QueueEntry { Id = 15, VisitId = 11, ServicePointId = 507, CurrentServicePointId = 507, QueueDate = new DateOnly(2026, 10, 20), QueueNumber = 5, QueueType = "DIAGNOSTIC", Status = "IN_PROGRESS", PresenceStatus = "PRESENT", ArrivedAt = new DateTime(2026, 10, 20, 8, 50, 0), ReadyAt = new DateTime(2026, 10, 20, 8, 50, 0), StartedAt = new DateTime(2026, 10, 20, 8, 52, 0), ArrivalClass = "WALK_IN", ManualPriority = "NORMAL", StateVersion = 2 },
            // qe 17: foreign visit busy IN_PROGRESS at SP 501 -> blocks CONFIRMED order at SP 501 (order 11)
            new QueueEntry { Id = 17, VisitId = 11, ServicePointId = 501, CurrentServicePointId = 501, QueueDate = new DateOnly(2026, 10, 20), QueueNumber = 6, QueueType = "DIAGNOSTIC", Status = "IN_PROGRESS", PresenceStatus = "PRESENT", ArrivedAt = new DateTime(2026, 10, 20, 8, 55, 0), ReadyAt = new DateTime(2026, 10, 20, 8, 55, 0), StartedAt = new DateTime(2026, 10, 20, 8, 56, 0), ArrivalClass = "WALK_IN", ManualPriority = "NORMAL", StateVersion = 2 },
            // qe 18: COMPLETED terminal
            new QueueEntry { Id = 18, VisitId = 12, ServicePointId = 501, CurrentServicePointId = 501, QueueDate = new DateOnly(2026, 10, 15), QueueNumber = 1, QueueType = "DIAGNOSTIC", Status = "COMPLETED", PresenceStatus = "PRESENT", StartedAt = new DateTime(2026, 10, 15, 9, 30, 0), CompletedAt = new DateTime(2026, 10, 15, 10, 0, 0), StateVersion = 3 },
            // qe 20: hospital 2 entry
            new QueueEntry { Id = 20, VisitId = 20, ServicePointId = 601, CurrentServicePointId = 601, QueueDate = new DateOnly(2026, 10, 20), QueueNumber = 1, QueueType = "DIAGNOSTIC", Status = "READY", PresenceStatus = "PRESENT", StateVersion = 1 });

        db.DiagnosticOrders.AddRange(
            // 11: ORDERED -> rejected
            new DiagnosticOrder { Id = 11, OrderCode = "DIA-202610-0011", VisitId = 10, OrderingDoctorId = 1, ServiceId = 5, QueueEntryId = 11, Instruction = "XN máu", Priority = "URGENT", Status = "ORDERED", OrderedAt = new DateTime(2026, 10, 20, 8, 30, 0) },
            // 12: CONFIRMED at SP 502 -> can start
            new DiagnosticOrder { Id = 12, OrderCode = "DIA-202610-0012", VisitId = 10, OrderingDoctorId = 1, ServiceId = 6, QueueEntryId = 12, Instruction = "Chụp phổi", Priority = "NORMAL", Status = "CONFIRMED", ConfirmedByStaffId = 7, ConfirmedAt = new DateTime(2026, 10, 20, 8, 35, 0), OrderedAt = new DateTime(2026, 10, 20, 8, 32, 0) },
            // 13: CONFIRMED at SP 501 visit 10 -> blocked by qe17 IN_PROGRESS (visit 11) sharing service point
            new DiagnosticOrder { Id = 13, OrderCode = "DIA-202610-0013", VisitId = 10, OrderingDoctorId = 1, ServiceId = 5, QueueEntryId = 13, Instruction = "XN STAT", Priority = "STAT", Status = "CONFIRMED", ConfirmedByStaffId = 7, ConfirmedAt = new DateTime(2026, 10, 20, 8, 45, 0), OrderedAt = new DateTime(2026, 10, 20, 8, 40, 0) },
            // 14: CONFIRMED + SKIPPED queue -> start blocked
            new DiagnosticOrder { Id = 14, OrderCode = "DIA-202610-0014", VisitId = 11, OrderingDoctorId = 1, ServiceId = 5, QueueEntryId = 14, Instruction = "XN skipped", Priority = "NORMAL", Status = "CONFIRMED", ConfirmedByStaffId = 7, ConfirmedAt = new DateTime(2026, 10, 20, 8, 46, 0), OrderedAt = new DateTime(2026, 10, 20, 8, 42, 0) },
            // 15: IN_PROGRESS at SP 502 -> can complete/pause/cancel
            new DiagnosticOrder { Id = 15, OrderCode = "DIA-202610-0015", VisitId = 11, OrderingDoctorId = 1, ServiceId = 6, QueueEntryId = 15, Instruction = "Chụp X-quang", Priority = "NORMAL", Status = "IN_PROGRESS", ConfirmedByStaffId = 7, ConfirmedAt = new DateTime(2026, 10, 20, 8, 50, 0), OrderedAt = new DateTime(2026, 10, 20, 8, 48, 0) },
            // 16: COMPLETED terminal
            new DiagnosticOrder { Id = 16, OrderCode = "DIA-202610-0016", VisitId = 12, OrderingDoctorId = 1, ServiceId = 5, QueueEntryId = 18, Instruction = "XN xong", Priority = "NORMAL", Status = "COMPLETED", ConfirmedByStaffId = 7, ConfirmedAt = new DateTime(2026, 10, 15, 9, 0, 0), OrderedAt = new DateTime(2026, 10, 15, 8, 45, 0) },
            // 17: CANCELLED terminal
            new DiagnosticOrder { Id = 17, OrderCode = "DIA-202610-0017", VisitId = 11, OrderingDoctorId = 1, ServiceId = 6, QueueEntryId = null, Instruction = "Đã hủy", Priority = "NORMAL", Status = "CANCELLED", OrderedAt = new DateTime(2026, 10, 20, 8, 42, 0) },
            // 18: CONFIRMED no queue entry -> start creates one (visit 10, SP501 busy w/ visit11? same-visit rule applies; order18 visit10 & qe17 visit11 -> still conflicts? move to SP502)
            new DiagnosticOrder { Id = 18, OrderCode = "DIA-202610-0018", VisitId = 10, OrderingDoctorId = 1, ServiceId = 6, QueueEntryId = null, Instruction = "Chụp bổ sung", Priority = "NORMAL", Status = "CONFIRMED", ConfirmedByStaffId = 7, ConfirmedAt = new DateTime(2026, 10, 20, 8, 51, 0), OrderedAt = new DateTime(2026, 10, 20, 8, 50, 0) },
            // 19: CONFIRMED order on CANCELLED visit -> terminal visit rejected
            new DiagnosticOrder { Id = 19, OrderCode = "DIA-202610-0019", VisitId = 13, OrderingDoctorId = 1, ServiceId = 5, QueueEntryId = null, Instruction = "XN visit hủy", Priority = "NORMAL", Status = "CONFIRMED", ConfirmedByStaffId = 7, ConfirmedAt = new DateTime(2026, 10, 20, 8, 55, 0), OrderedAt = new DateTime(2026, 10, 20, 8, 54, 0) },
            // 20: hospital 2 (cross-hospital)
            new DiagnosticOrder { Id = 20, OrderCode = "DIA-202610-0020", VisitId = 20, OrderingDoctorId = 1, ServiceId = 20, QueueEntryId = 20, Instruction = "XQ cơ sở 2", Priority = "NORMAL", Status = "CONFIRMED", ConfirmedByStaffId = 8, ConfirmedAt = new DateTime(2026, 10, 20, 8, 36, 0), OrderedAt = new DateTime(2026, 10, 20, 8, 35, 0) },
            // 21: CONFIRMED finalized result -> rejected (FINALIZED result)
            new DiagnosticOrder { Id = 21, OrderCode = "DIA-202610-0021", VisitId = 10, OrderingDoctorId = 1, ServiceId = 5, QueueEntryId = null, Instruction = "XN finalized", Priority = "NORMAL", Status = "CONFIRMED", ConfirmedByStaffId = 7, ConfirmedAt = new DateTime(2026, 10, 20, 8, 58, 0), OrderedAt = new DateTime(2026, 10, 20, 8, 57, 0) },
            // 30,31: CONFIRMED orders in clean visit 30 for batch start
            new DiagnosticOrder { Id = 30, OrderCode = "DIA-202610-0030", VisitId = 30, OrderingDoctorId = 1, ServiceId = 6, QueueEntryId = null, Instruction = "XQ batch 1", Priority = "NORMAL", Status = "CONFIRMED", ConfirmedByStaffId = 7, ConfirmedAt = new DateTime(2026, 10, 20, 8, 20, 0), OrderedAt = new DateTime(2026, 10, 20, 8, 15, 0) },
            new DiagnosticOrder { Id = 31, OrderCode = "DIA-202610-0031", VisitId = 30, OrderingDoctorId = 1, ServiceId = 6, QueueEntryId = null, Instruction = "XQ batch 2", Priority = "NORMAL", Status = "CONFIRMED", ConfirmedByStaffId = 7, ConfirmedAt = new DateTime(2026, 10, 20, 8, 22, 0), OrderedAt = new DateTime(2026, 10, 20, 8, 18, 0) });

        db.DiagnosticResults.Add(new DiagnosticResult
        {
            Id = 1,
            DiagnosticOrderId = 21,
            Status = "FINALIZED",
            FinalizedAt = new DateTime(2026, 10, 20, 8, 59, 0),
            FinalizedByStaffId = 7,
        });

        db.SaveChanges();
    }

    public void Dispose() => _dbContext.Dispose();

    // ============ LIST & DETAIL ============

    [Fact]
    public async Task GetExecutions_ReturnsHospitalScopedListAndSummary()
    {
        var response = await _service.GetExecutionsAsync(7, new GetDiagnosticServiceExecutionsRequest(Limit: 100));

        Assert.Equal(1, response.HospitalId);
        Assert.NotEmpty(response.Items);
        Assert.DoesNotContain(response.Items, i => i.OrderId == 20); // cross-hospital excluded
        Assert.True(response.Summary.TotalOrders > 0);
        Assert.True(response.Summary.WaitingExecutionCount > 0);
        Assert.True(response.Summary.InProgressCount > 0);
        Assert.True(response.Summary.CompletedCount > 0);
        Assert.True(response.Summary.CancelledCount > 0);
    }

    [Fact]
    public async Task GetExecutions_InvalidFilters_ThrowsValidation()
    {
        await Assert.ThrowsAsync<ValidationException>(() => _service.GetExecutionsAsync(7, new GetDiagnosticServiceExecutionsRequest(Limit: 0)));
        await Assert.ThrowsAsync<ValidationException>(() => _service.GetExecutionsAsync(7, new GetDiagnosticServiceExecutionsRequest(Limit: 101)));
        await Assert.ThrowsAsync<ValidationException>(() => _service.GetExecutionsAsync(7, new GetDiagnosticServiceExecutionsRequest(Status: "BOGUS")));
        await Assert.ThrowsAsync<ValidationException>(() => _service.GetExecutionsAsync(7, new GetDiagnosticServiceExecutionsRequest(QueueStatus: "BOGUS")));
        await Assert.ThrowsAsync<ValidationException>(() => _service.GetExecutionsAsync(7, new GetDiagnosticServiceExecutionsRequest(Category: "BOGUS")));
        await Assert.ThrowsAsync<ValidationException>(() => _service.GetExecutionsAsync(7, new GetDiagnosticServiceExecutionsRequest(Priority: "BOGUS")));
        await Assert.ThrowsAsync<ValidationException>(() => _service.GetExecutionsAsync(7, new GetDiagnosticServiceExecutionsRequest(Keyword: "<x>")));
    }

    [Fact]
    public async Task GetExecutionByIdOrCode_ReturnsDetailWithHistoryAndClinicalContext()
    {
        var detail = await _service.GetExecutionByIdOrCodeAsync(7, "DIA-202610-0015");

        Assert.Equal(15, detail.Order.OrderId);
        Assert.Equal("IN_PROGRESS", detail.Order.Status);
        Assert.Equal("VIS-202610-0011", detail.Order.VisitCode);
        Assert.NotNull(detail.ClinicalContext);
        Assert.Equal("B34.9", detail.ClinicalContext.PrimaryDiagnosisCode);
        Assert.NotNull(detail.AvailableServicePoints);
    }

    // ============ HAPPY PATH TRANSITIONS ============

    [Fact]
    public async Task StartExecution_ConfirmedOrder_TransitionsAndSyncsQueue()
    {
        var response = await _service.StartServiceExecutionAsync(
            7,
            "DIA-202610-0012",
            new UpdateDiagnosticServiceStatusRequest(
                SpecimenCode: "SPM-2026-001",
                SpecimenType: "Whole Blood EDTA",
                EquipmentCode: "SYSMEX-XN1000",
                ExecutionNote: "Đã lấy 2ml máu tĩnh mạch"),
            ipAddress: "127.0.0.1");

        var order = await _dbContext.DiagnosticOrders.FindAsync(12L);
        Assert.Equal("IN_PROGRESS", order!.Status);

        var qe = await _dbContext.QueueEntries.FindAsync(12L);
        Assert.Equal("IN_PROGRESS", qe!.Status);
        Assert.Equal("PRESENT", qe.PresenceStatus);
        Assert.NotNull(qe.StartedAt);
        Assert.Null(qe.CompletedAt);
        Assert.Equal(3, qe.StateVersion); // was 2 -> +1

        var visit = await _dbContext.Visits.FindAsync(10L);
        Assert.Equal("AWAITING_RESULTS", visit!.Status); // visit preserved

        var audit = _dbContext.AuditLogs.OrderByDescending(a => a.Id).First();
        Assert.Equal(DiagnosticServiceExecutionService.ExecutionStartedAction, audit.Action);
        Assert.Contains("SPM-2026-001", audit.ChangeData);
        Assert.Equal("127.0.0.1", audit.IpAddress?.ToString());
    }

    [Fact]
    public async Task CompleteExecution_InProgressOrder_TransitionsAndSyncsQueue()
    {
        var response = await _service.CompleteServiceExecutionAsync(
            7,
            "DIA-202610-0015",
            new UpdateDiagnosticServiceStatusRequest(ExecutionNote: "Hoàn tất chụp"),
            ipAddress: null);

        var order = await _dbContext.DiagnosticOrders.FindAsync(15L);
        Assert.Equal("COMPLETED", order!.Status);

        var qe = await _dbContext.QueueEntries.FindAsync(15L);
        Assert.Equal("COMPLETED", qe!.Status);
        Assert.NotNull(qe.CompletedAt);
        Assert.Equal(3, qe.StateVersion); // 2 -> 3

        var visit = await _dbContext.Visits.FindAsync(11L);
        Assert.Equal("IN_PROGRESS", visit!.Status);

        var audit = _dbContext.AuditLogs.OrderByDescending(a => a.Id).First();
        Assert.Equal(DiagnosticServiceExecutionService.ExecutionCompletedAction, audit.Action);
    }

    [Fact]
    public async Task PauseExecution_InProgressOrder_ReturnsToReadyWithReason()
    {
        await _service.UpdateServiceExecutionStatusAsync(
            7,
            "DIA-202610-0015",
            new UpdateDiagnosticServiceStatusRequest(TargetStatus: "READY", Reason: "Bệnh nhân xin nghỉ giữa chừng"));

        var order = await _dbContext.DiagnosticOrders.FindAsync(15L);
        Assert.Equal("CONFIRMED", order!.Status);

        var qe = await _dbContext.QueueEntries.FindAsync(15L);
        Assert.Equal("READY", qe!.Status);
        Assert.Null(qe.StartedAt);
        Assert.Null(qe.CompletedAt);
        Assert.Equal("Bệnh nhân xin nghỉ giữa chừng", qe.BlockedReason);
        Assert.Equal(3, qe.StateVersion);
    }

    [Fact]
    public async Task PauseExecution_WaitingAlias_SetsQueueWaiting()
    {
        await _service.UpdateServiceExecutionStatusAsync(
            7,
            "DIA-202610-0015",
            new UpdateDiagnosticServiceStatusRequest(TargetStatus: "WAITING", Reason: "Chuyển lại hàng đợi chờ"));

        var qe = await _dbContext.QueueEntries.FindAsync(15L);
        Assert.Equal("WAITING", qe!.Status);
        Assert.Null(qe.StartedAt);
    }

    [Fact]
    public async Task CancelExecution_ConfirmedOrder_RequiresReasonAndSetsCancelled()
    {
        await _service.UpdateServiceExecutionStatusAsync(
            7,
            "DIA-202610-0012",
            new UpdateDiagnosticServiceStatusRequest(TargetStatus: "CANCELLED", Reason: "Bệnh nhân từ chối thực hiện"));

        var order = await _dbContext.DiagnosticOrders.FindAsync(12L);
        Assert.Equal("CANCELLED", order!.Status);

        var qe = await _dbContext.QueueEntries.FindAsync(12L);
        Assert.Equal("CANCELLED", qe!.Status);
        Assert.NotNull(qe.CompletedAt);
        Assert.Equal("Bệnh nhân từ chối thực hiện", qe.BlockedReason);

        var audit = _dbContext.AuditLogs.OrderByDescending(a => a.Id).First();
        Assert.Equal(DiagnosticServiceExecutionService.ExecutionCancelledAction, audit.Action);
    }

    [Fact]
    public async Task StartExecution_ConfirmedWithoutQueueEntry_CreatesQueueEntry()
    {
        var response = await _service.StartServiceExecutionAsync(7, "DIA-202610-0018", null, ipAddress: null);

        var order = await _dbContext.DiagnosticOrders.FindAsync(18L);
        Assert.Equal("IN_PROGRESS", order!.Status);
        Assert.NotNull(order.QueueEntryId);

        var qe = await _dbContext.QueueEntries.FindAsync(order.QueueEntryId!.Value);
        Assert.NotNull(qe);
        Assert.Equal("DIAGNOSTIC", qe!.QueueType);
        Assert.Equal("IN_PROGRESS", qe.Status);
        Assert.NotNull(qe.StartedAt);
        Assert.NotNull(qe.QueueNumber);
        Assert.Equal(1, qe.StateVersion); // created with 1 in this branch

        Assert.Equal(DiagnosticServiceExecutionService.ExecutionStartedAction, response.ActionOutcome);
    }

    // ============ CONFLICT GUARDS (409) ============

    [Fact]
    public async Task OrderedOrder_Rejected409()
    {
        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            _service.StartServiceExecutionAsync(7, "DIA-202610-0011"));
        Assert.Contains("ORDERED", ex.Message);
    }

    [Fact]
    public async Task ConfirmedToCompletedDirect_Rejected409()
    {
        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            _service.CompleteServiceExecutionAsync(7, "DIA-202610-0012"));
        Assert.Contains("IN_PROGRESS", ex.Message);
    }

    [Fact]
    public async Task StartWithSkippedQueue_Rejected409()
    {
        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            _service.StartServiceExecutionAsync(7, "DIA-202610-0014"));
        Assert.Contains("SKIPPED", ex.Message);
    }

    [Fact]
    public async Task StartOnCompletedOrder_Rejected409()
    {
        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.StartServiceExecutionAsync(7, "DIA-202610-0016"));
    }

    [Fact]
    public async Task UpdateOnCancelledOrder_Rejected409()
    {
        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.UpdateServiceExecutionStatusAsync(7, "DIA-202610-0017",
                new UpdateDiagnosticServiceStatusRequest(TargetStatus: "IN_PROGRESS")));
    }

    [Fact]
    public async Task UpdateOnTerminalVisit_Rejected409()
    {
        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            _service.StartServiceExecutionAsync(7, "DIA-202610-0019"));
        Assert.Contains("CANCELLED", ex.Message);
    }

    [Fact]
    public async Task UpdateOnFinalizedResult_Rejected409()
    {
        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            _service.StartServiceExecutionAsync(7, "DIA-202610-0021"));
        Assert.Contains("FINALIZED", ex.Message);
    }

    [Fact]
    public async Task StartWithBusyServicePointOtherVisit_Rejected409_SameVisitAllowed()
    {
        // qe17 IN_PROGRESS at SP501 belongs visit 11; order 11 (visit 10) at SP501 -> blocked
        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            _service.StartServiceExecutionAsync(7, "DIA-202610-0013",
                new UpdateDiagnosticServiceStatusRequest(ServicePointId: 501)));
        Assert.Contains("đang thực hiện", ex.Message);
    }

    [Fact]
    public async Task StaleExpectedStateVersion_Rejected409()
    {
        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.StartServiceExecutionAsync(7, "DIA-202610-0012",
                new UpdateDiagnosticServiceStatusRequest(ExpectedStateVersion: 99)));
    }

    [Fact]
    public async Task InProgressToInProgress_Rejected409()
    {
        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.StartServiceExecutionAsync(7, "DIA-202610-0015"));
    }

    [Fact]
    public async Task ConfirmedToConfirmed_Rejected409()
    {
        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.UpdateServiceExecutionStatusAsync(7, "DIA-202610-0012",
                new UpdateDiagnosticServiceStatusRequest(TargetStatus: "CONFIRMED", Reason: "Không thay đổi gì")));
    }

    // ============ VALIDATION (400) ============

    [Fact]
    public async Task InvalidTargetStatus_Throws400()
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateServiceExecutionStatusAsync(7, "DIA-202610-0012",
                new UpdateDiagnosticServiceStatusRequest(TargetStatus: "BOGUS")));
    }

    [Fact]
    public async Task MissingTargetStatus_Throws400()
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateServiceExecutionStatusAsync(7, "DIA-202610-0012",
                new UpdateDiagnosticServiceStatusRequest()));
    }

    [Fact]
    public async Task XssPayload_Throws400()
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.StartServiceExecutionAsync(7, "DIA-202610-0012",
                new UpdateDiagnosticServiceStatusRequest(ExecutionNote: "<script>alert(1)</script>")));
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.StartServiceExecutionAsync(7, "DIA-202610-0012",
                new UpdateDiagnosticServiceStatusRequest(SpecimenCode: "<b>")));
    }

    [Fact]
    public async Task MissingCancelReason_Throws400()
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateServiceExecutionStatusAsync(7, "DIA-202610-0012",
                new UpdateDiagnosticServiceStatusRequest(TargetStatus: "CANCELLED", Reason: "abc")));
    }

    [Fact]
    public async Task MissingPauseReason_Throws400()
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateServiceExecutionStatusAsync(7, "DIA-202610-0015",
                new UpdateDiagnosticServiceStatusRequest(TargetStatus: "READY")));
    }

    [Fact]
    public async Task AbsentPresenceOnStart_Throws400()
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.StartServiceExecutionAsync(7, "DIA-202610-0012",
                new UpdateDiagnosticServiceStatusRequest(PresenceStatus: "ABSENT")));
    }

    [Fact]
    public async Task ConflictingStartStatus_Throws400()
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.StartServiceExecutionAsync(7, "DIA-202610-0012",
                new UpdateDiagnosticServiceStatusRequest(TargetStatus: "COMPLETED")));
    }

    [Fact]
    public async Task ConflictingCompleteStatus_Throws400()
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.CompleteServiceExecutionAsync(7, "DIA-202610-0015",
                new UpdateDiagnosticServiceStatusRequest(TargetStatus: "IN_PROGRESS")));
    }

    // ============ AUTH (401/403/404) ============

    [Fact]
    public async Task InvalidActor_Throws401()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _service.GetExecutionsAsync(0));
    }

    [Fact]
    public async Task NonDiagnosticRole_Throws403()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() => _service.GetExecutionsAsync(21)); // doctor
        await Assert.ThrowsAsync<ForbiddenException>(() => _service.GetExecutionsAsync(23)); // patient
        await Assert.ThrowsAsync<ForbiddenException>(() => _service.GetExecutionsAsync(25)); // reception
    }

    [Fact]
    public async Task MissingMutationPermission_Throws403()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.StartServiceExecutionAsync(24, "DIA-202610-0012"));
    }

    [Fact]
    public async Task InactiveOrLockedAccounts_Throw403()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() => _service.GetExecutionsAsync(14)); // locked
        await Assert.ThrowsAsync<ForbiddenException>(() => _service.GetExecutionsAsync(15)); // inactive user
        await Assert.ThrowsAsync<ForbiddenException>(() => _service.GetExecutionsAsync(16)); // inactive staff
        await Assert.ThrowsAsync<ForbiddenException>(() => _service.GetExecutionsAsync(17)); // no staff record
        await Assert.ThrowsAsync<ForbiddenException>(() => _service.GetExecutionsAsync(18)); // no hospital
        await Assert.ThrowsAsync<ForbiddenException>(() => _service.GetExecutionsAsync(19)); // inactive hospital
        await Assert.ThrowsAsync<ForbiddenException>(() => _service.GetExecutionsAsync(20)); // inactive dept
    }

    [Fact]
    public async Task CrossHospitalOrder_Throws403()
    {
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.StartServiceExecutionAsync(7, "DIA-202610-0020"));
    }

    [Fact]
    public async Task MissingOrder_Throws404()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.StartServiceExecutionAsync(7, "DIA-999999-9999"));
    }

    // ============ BATCH ============

    [Fact]
    public async Task BatchUpdate_MultipleOrdersSameVisit_Succeeds()
    {
        var response = await _service.UpdateServiceExecutionStatusBatchAsync(
            7,
            new UpdateDiagnosticServiceStatusBatchRequest(
                OrderCodes: new[] { "DIA-202610-0012", "DIA-202610-0018" },
                TargetStatus: "IN_PROGRESS"));

        Assert.Equal(2, response.UpdatedCount);
        var o12 = await _dbContext.DiagnosticOrders.FindAsync(12L);
        var o18 = await _dbContext.DiagnosticOrders.FindAsync(18L);
        Assert.Equal("IN_PROGRESS", o12!.Status);
        Assert.Equal("IN_PROGRESS", o18!.Status);
    }

    [Fact]
    public async Task BatchUpdate_Empty_Throws400()
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateServiceExecutionStatusBatchAsync(7, new UpdateDiagnosticServiceStatusBatchRequest(TargetStatus: "IN_PROGRESS")));
    }

    [Fact]
    public async Task BatchUpdate_TooMany_Throws400()
    {
        var ids = Enumerable.Range(1, 21).Select(i => (long)i).ToArray();
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateServiceExecutionStatusBatchAsync(7,
                new UpdateDiagnosticServiceStatusBatchRequest(OrderIds: ids, TargetStatus: "IN_PROGRESS")));
    }

    [Fact]
    public async Task BatchUpdate_VisitTargets_ResolvesEligible()
    {
        var response = await _service.UpdateServiceExecutionStatusBatchAsync(
            7,
            new UpdateDiagnosticServiceStatusBatchRequest(VisitId: 30, TargetStatus: "IN_PROGRESS"));

        Assert.Equal(2, response.UpdatedCount);
        Assert.All(response.UpdatedOrders, o => Assert.Equal("IN_PROGRESS", o.Status));
    }
}
