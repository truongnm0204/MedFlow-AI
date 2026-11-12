using MedFlow.Application.DTOs.Diagnostic;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Consultation;
using MedFlow.Application.Services.Diagnostic;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.UnitTests.Diagnostic;

public sealed class DiagnosticResultEntryServiceTests : IDisposable
{
    private sealed class FixedTimeProvider(DateTimeOffset nowUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => nowUtc;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class DiagnosticResultEntryTestDbContext : DbContext, IApplicationDbContext
    {
        public DiagnosticResultEntryTestDbContext(DbContextOptions<DiagnosticResultEntryTestDbContext> options)
            : base(options)
        {
        }

        private static NotSupportedException NotUsed() =>
            new("Not needed for DiagnosticResultEntryService unit tests.");

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
        public DbSet<Payment> Payments => Set<Payment>();
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
            modelBuilder.Entity<Payment>().HasKey(p => p.Id);
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

    private readonly DiagnosticResultEntryTestDbContext _dbContext;
    private readonly DiagnosticResultEntryService _service;
    private readonly ConsultationWorkspaceService _consultationService;

    public DiagnosticResultEntryServiceTests()
    {
        var options = new DbContextOptionsBuilder<DiagnosticResultEntryTestDbContext>()
            .UseInMemoryDatabase(databaseName: $"DiagnosticResultEntryTests_{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _dbContext = new DiagnosticResultEntryTestDbContext(options);
        SeedData(_dbContext);

        var timeProvider = new FixedTimeProvider(new DateTimeOffset(FixedNowUtc));
        _service = new DiagnosticResultEntryService(_dbContext, timeProvider);
        _consultationService = new ConsultationWorkspaceService(_dbContext, timeProvider);
    }

    private static void SeedData(DiagnosticResultEntryTestDbContext db)
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
            new HospitalRoom
            {
                Id = 101,
                HospitalId = 1,
                DepartmentId = 1,
                RoomCode = "DIA-201",
                Name = "Phòng Xét nghiệm Huyết học 201",
                RoomType = "DIAGNOSTIC",
                Location = "Tầng 2 - Khu A",
                Capacity = 3,
                Status = "ACTIVE",
            },
            new HospitalRoom
            {
                Id = 102,
                HospitalId = 1,
                DepartmentId = 1,
                RoomCode = "DIA-202",
                Name = "Phòng X-quang Kỹ thuật số 202",
                RoomType = "DIAGNOSTIC",
                Location = "Tầng 2 - Khu B",
                Capacity = 2,
                Status = "ACTIVE",
            },
            new HospitalRoom
            {
                Id = 103,
                HospitalId = 1,
                DepartmentId = 1,
                RoomCode = "DIA-203",
                Name = "Phòng CLS Bảo trì 203",
                RoomType = "DIAGNOSTIC",
                Location = "Tầng 2 - Khu C",
                Capacity = 1,
                Status = "MAINTENANCE",
            },
            new HospitalRoom
            {
                Id = 104,
                HospitalId = 1,
                DepartmentId = 1,
                RoomCode = "EXM-101",
                Name = "Phòng Khám Nội 101",
                RoomType = "CONSULTATION",
                Location = "Tầng 1",
                Capacity = 1,
                Status = "ACTIVE",
            },
            new HospitalRoom
            {
                Id = 105,
                HospitalId = 1,
                DepartmentId = 1,
                RoomCode = "DIA-205",
                Name = "Phòng Điện tâm đồ 205",
                RoomType = "DIAGNOSTIC",
                Location = "Tầng 2 - Khu D",
                Capacity = 2,
                Status = "ACTIVE",
            },
            new HospitalRoom
            {
                Id = 201,
                HospitalId = 2,
                DepartmentId = 3,
                RoomCode = "DIA-HCM-01",
                Name = "Phòng CLS Cơ sở 2",
                RoomType = "DIAGNOSTIC",
                Location = "Tầng 1",
                Capacity = 2,
                Status = "ACTIVE",
            });

        db.QueueServicePoints.AddRange(
            new QueueServicePoint
            {
                Id = 11,
                HospitalId = 1,
                RoomId = 101,
                ServiceId = 10,
                PointType = "DIAGNOSTIC",
                Name = "Điểm lấy mẫu & Xét nghiệm máu 201",
                IsActive = true,
            },
            new QueueServicePoint
            {
                Id = 12,
                HospitalId = 1,
                RoomId = 102,
                ServiceId = 11,
                PointType = "DIAGNOSTIC",
                Name = "Phòng X-quang Kỹ thuật số 202",
                IsActive = true,
            },
            new QueueServicePoint
            {
                Id = 13,
                HospitalId = 1,
                RoomId = 105,
                ServiceId = 12,
                PointType = "DIAGNOSTIC",
                Name = "Phòng Điện tâm đồ 205",
                IsActive = true,
            },
            new QueueServicePoint
            {
                Id = 14,
                HospitalId = 1,
                RoomId = 103,
                ServiceId = null,
                PointType = "DIAGNOSTIC",
                Name = "Điểm CLS bảo trì 203",
                IsActive = true,
            },
            new QueueServicePoint
            {
                Id = 15,
                HospitalId = 1,
                RoomId = 101,
                ServiceId = null,
                PointType = "DIAGNOSTIC",
                Name = "Điểm CLS ngừng hoạt động",
                IsActive = false,
            },
            new QueueServicePoint
            {
                Id = 1,
                HospitalId = 1,
                RoomId = 104,
                ServiceId = 13,
                PointType = "CONSULTATION",
                Name = "Bàn khám Nội 101",
                IsActive = true,
            },
            new QueueServicePoint
            {
                Id = 21,
                HospitalId = 2,
                RoomId = 201,
                ServiceId = 20,
                PointType = "DIAGNOSTIC",
                Name = "Điểm CLS Cơ sở 2",
                IsActive = true,
            });

        db.Services.AddRange(
            new Service
            {
                Id = 10,
                HospitalId = 1,
                DepartmentId = 1,
                SpecialtyId = 1,
                ServiceCode = "LAB-CBC-01",
                Name = "Tổng phân tích tế bào máu ngoại vi",
                ServiceType = "DIAGNOSTIC",
                RequiresResultVerification = true,
                IsActive = true,
            },
            new Service
            {
                Id = 11,
                HospitalId = 1,
                DepartmentId = 1,
                SpecialtyId = 4,
                ServiceCode = "IMG-XRAY-01",
                Name = "Chụp X-quang ngực thẳng",
                ServiceType = "DIAGNOSTIC",
                RequiresResultVerification = true,
                IsActive = true,
            },
            new Service
            {
                Id = 12,
                HospitalId = 1,
                DepartmentId = 1,
                SpecialtyId = 2,
                ServiceCode = "CARD-ECG-01",
                Name = "Điện tâm đồ (ECG) 12 chuyển đạo",
                ServiceType = "DIAGNOSTIC",
                RequiresResultVerification = false,
                IsActive = true,
            },
            new Service
            {
                Id = 13,
                HospitalId = 1,
                DepartmentId = 1,
                SpecialtyId = 1,
                ServiceCode = "CON-GEN-01",
                Name = "Khám Nội tổng quát",
                ServiceType = "CONSULTATION",
                IsActive = true,
            },
            new Service
            {
                Id = 14,
                HospitalId = 1,
                DepartmentId = 1,
                SpecialtyId = 1,
                ServiceCode = "LAB-OFF-01",
                Name = "Xét nghiệm ngừng triển khai",
                ServiceType = "DIAGNOSTIC",
                IsActive = false,
            },
            new Service
            {
                Id = 20,
                HospitalId = 2,
                DepartmentId = 3,
                SpecialtyId = 1,
                ServiceCode = "LAB-HCM-01",
                Name = "Xét nghiệm máu Cơ sở 2",
                ServiceType = "DIAGNOSTIC",
                IsActive = true,
            });

        db.ServicePrices.AddRange(
            new ServicePrice { Id = 1, ServiceId = 10, PriceAmount = 120000m },
            new ServicePrice { Id = 2, ServiceId = 11, PriceAmount = 180000m },
            new ServicePrice { Id = 3, ServiceId = 12, PriceAmount = 150000m });

        db.Roles.AddRange(
            new Role { Id = 3, Code = "DOCTOR", Name = "Doctor" },
            new Role { Id = 4, Code = "DIAGNOSTIC_STAFF", Name = "Diagnostic Staff" },
            new Role { Id = 5, Code = "DIAGNOSTIC_READ_ONLY", Name = "Diagnostic Read Only" });

        db.Permissions.AddRange(
            new Permission { Id = 6, Code = "QUEUE_MANAGE" },
            new Permission { Id = 7, Code = "MEDICAL_RECORD_WRITE" },
            new Permission { Id = 8, Code = "DIAGNOSTIC_ORDER" },
            new Permission { Id = 9, Code = "DIAGNOSTIC_RESULT_WRITE" },
            new Permission { Id = 10, Code = "PRESCRIPTION_WRITE" });

        db.RolePermissions.AddRange(
            new RolePermission { RoleId = 3, PermissionId = 6 },
            new RolePermission { RoleId = 3, PermissionId = 7 },
            new RolePermission { RoleId = 3, PermissionId = 8 },
            new RolePermission { RoleId = 3, PermissionId = 10 },
            new RolePermission { RoleId = 4, PermissionId = 6 },
            new RolePermission { RoleId = 4, PermissionId = 9 },
            new RolePermission { RoleId = 5, PermissionId = 6 }); // QUEUE_MANAGE only, no DIAGNOSTIC_RESULT_WRITE

        db.Users.AddRange(
            new User
            {
                Id = 3,
                Email = "doctor01@medflow.test",
                PasswordHash = "hash",
                AccountStatus = AccountStatus.ACTIVE,
            },
            new User
            {
                Id = 7,
                Email = "diagnostic_staff01@medflow.test",
                PasswordHash = "hash",
                AccountStatus = AccountStatus.ACTIVE,
            },
            new User
            {
                Id = 8,
                Email = "diag_inactive_user@medflow.test",
                PasswordHash = "hash",
                AccountStatus = AccountStatus.INACTIVE,
            },
            new User
            {
                Id = 9,
                Email = "diag_locked_user@medflow.test",
                PasswordHash = "hash",
                AccountStatus = AccountStatus.ACTIVE,
                LockedUntil = FixedNowUtc.AddMinutes(30),
            },
            new User
            {
                Id = 10,
                Email = "diag_no_write@medflow.test",
                PasswordHash = "hash",
                AccountStatus = AccountStatus.ACTIVE,
            },
            new User
            {
                Id = 11,
                Email = "diag_no_staff@medflow.test",
                PasswordHash = "hash",
                AccountStatus = AccountStatus.ACTIVE,
            },
            new User
            {
                Id = 12,
                Email = "diag_inactive_staff@medflow.test",
                PasswordHash = "hash",
                AccountStatus = AccountStatus.ACTIVE,
            },
            new User
            {
                Id = 13,
                Email = "diag_inactive_hosp@medflow.test",
                PasswordHash = "hash",
                AccountStatus = AccountStatus.ACTIVE,
            },
            new User
            {
                Id = 14,
                Email = "diag_inactive_dept@medflow.test",
                PasswordHash = "hash",
                AccountStatus = AccountStatus.ACTIVE,
            });

        db.UserRoles.AddRange(
            new UserRole { UserId = 3, RoleId = 3 },
            new UserRole { UserId = 7, RoleId = 4 },
            new UserRole { UserId = 8, RoleId = 4 },
            new UserRole { UserId = 9, RoleId = 4 },
            new UserRole { UserId = 10, RoleId = 4 },
            new UserRole { UserId = 10, RoleId = 5 },
            new UserRole { UserId = 11, RoleId = 4 },
            new UserRole { UserId = 12, RoleId = 4 },
            new UserRole { UserId = 13, RoleId = 4 },
            new UserRole { UserId = 14, RoleId = 4 });

        db.Staff.AddRange(
            new Staff
            {
                Id = 1,
                UserId = 3,
                HospitalId = 1,
                DepartmentId = 1,
                StaffCode = "DOC-001",
                FullName = "Nguyễn Minh Khoa",
                Status = "ACTIVE",
            },
            new Staff
            {
                Id = 5,
                UserId = 7,
                HospitalId = 1,
                DepartmentId = 1,
                StaffCode = "DIA-001",
                FullName = "Hoàng Văn Long",
                Status = "ACTIVE",
            },
            new Staff
            {
                Id = 6,
                UserId = 10,
                HospitalId = 1,
                DepartmentId = 1,
                StaffCode = "DIA-002",
                FullName = "KTV Chỉ Quản Lý Hàng Đợi",
                Status = "ACTIVE",
            },
            new Staff
            {
                Id = 7,
                UserId = 12,
                HospitalId = 1,
                DepartmentId = 1,
                StaffCode = "DIA-003",
                FullName = "KTV Ngừng Việc",
                Status = "INACTIVE",
            },
            new Staff
            {
                Id = 8,
                UserId = 13,
                HospitalId = 3,
                DepartmentId = null,
                StaffCode = "DIA-004",
                FullName = "KTV Cơ Sở Ngừng Hoạt Động",
                Status = "ACTIVE",
            },
            new Staff
            {
                Id = 9,
                UserId = 14,
                HospitalId = 1,
                DepartmentId = 2,
                StaffCode = "DIA-005",
                FullName = "KTV Khoa Ngừng Hoạt Động",
                Status = "ACTIVE",
            });

        db.Doctors.Add(
            new Doctor
            {
                Id = 1,
                StaffId = 1,
                HospitalId = 1,
                SpecialtyId = 1,
                DoctorCode = "BS-001",
                ProfessionalTitle = "BS.CKI",
                IsActive = true,
            });

        db.Patients.AddRange(
            new Patient
            {
                Id = 10,
                PatientCode = "PAT-0010",
                FullName = "Lê Thị Hồng",
                DateOfBirth = new DateOnly(1985, 5, 12),
                Gender = "FEMALE",
                Phone = "0901234567",
                BloodType = "O+",
                Status = "ACTIVE",
            },
            new Patient
            {
                Id = 11,
                PatientCode = "PAT-0011",
                FullName = "Trần Văn Bình",
                DateOfBirth = new DateOnly(1955, 3, 20),
                Gender = "MALE",
                Phone = "0909888777",
                BloodType = "A+",
                Status = "ACTIVE",
            },
            new Patient
            {
                Id = 12,
                PatientCode = "PAT-0012",
                FullName = "Bệnh nhân Ngừng Hoạt Động",
                DateOfBirth = new DateOnly(1990, 1, 1),
                Gender = "MALE",
                Status = "INACTIVE",
            });

        db.Visits.AddRange(
            new Visit
            {
                Id = 100,
                VisitCode = "VIS-202610-0100",
                PatientId = 10,
                AssignedDoctorId = 1,
                ExaminationRoomId = 104,
                ReasonForVisit = "Ho kéo dài, sốt nhẹ về chiều",
                Status = "AWAITING_RESULTS",
                CheckedInAt = new DateTime(2026, 10, 20, 7, 30, 0),
            },
            new Visit
            {
                Id = 101,
                VisitCode = "VIS-202610-0101",
                PatientId = 11,
                AssignedDoctorId = 1,
                ExaminationRoomId = 104,
                ReasonForVisit = "Đau ngực trái khi gắng sức",
                Status = "IN_PROGRESS",
                CheckedInAt = new DateTime(2026, 10, 20, 8, 0, 0),
            },
            new Visit
            {
                Id = 102,
                VisitCode = "VIS-202610-0102",
                PatientId = 10,
                AssignedDoctorId = 1,
                ExaminationRoomId = 104,
                ReasonForVisit = "Lượt khám đã hủy",
                Status = "CANCELLED",
                CheckedInAt = new DateTime(2026, 10, 20, 7, 0, 0),
            },
            new Visit
            {
                Id = 103,
                VisitCode = "VIS-202610-0103",
                PatientId = 10,
                AssignedDoctorId = 1,
                ExaminationRoomId = 104,
                ReasonForVisit = "Lượt khám đã hoàn tất",
                Status = "COMPLETED",
                CheckedInAt = new DateTime(2026, 10, 20, 6, 30, 0),
            },
            new Visit
            {
                Id = 104,
                VisitCode = "VIS-202610-0104",
                PatientId = 10,
                AssignedDoctorId = 1,
                ExaminationRoomId = 104,
                ReasonForVisit = "Lượt khám đã khóa bệnh án",
                Status = "IN_PROGRESS",
                CheckedInAt = new DateTime(2026, 10, 20, 7, 15, 0),
            });

        db.MedicalRecords.AddRange(
            new MedicalRecord
            {
                Id = 50,
                VisitId = 100,
                Status = "DRAFT",
            },
            new MedicalRecord
            {
                Id = 51,
                VisitId = 104,
                Status = "FINALIZED",
            });

        db.MedicalRecordRevisions.Add(
            new MedicalRecordRevision
            {
                Id = 500,
                MedicalRecordId = 50,
                RevisionNo = 1,
                PresentingComplaint = "Ho khan 2 tuần kèm sốt nhẹ về chiều",
                ClinicalFindings = "Phổi rì rào phế nang giảm nhẹ đáy phổi phải",
                RelevantHistoryAllergies = "Không tiền sử dị ứng thuốc",
                ClinicalConclusion = "Theo dõi viêm phổi không điển hình",
            });

        db.VisitDiagnoses.Add(
            new VisitDiagnosis
            {
                Id = 600,
                VisitId = 100,
                CodeSnapshot = "J18.9",
                NameSnapshot = "Viêm phổi, không đặc hiệu",
                ClinicalDiagnosis = "Theo dõi viêm phổi cộng đồng",
                IsPrimary = true,
                RecordedAt = new DateTime(2026, 10, 20, 7, 50, 0),
            });

        db.QueueEntries.AddRange(
            new QueueEntry
            {
                Id = 1,
                VisitId = 100,
                ServicePointId = 1,
                CurrentServicePointId = 1,
                QueueDate = new DateOnly(2026, 10, 20),
                QueueNumber = 1,
                QueueType = "CONSULTATION",
                Status = "IN_PROGRESS",
                PresenceStatus = "PRESENT",
                ArrivedAt = new DateTime(2026, 10, 20, 7, 30, 0),
                ReadyAt = new DateTime(2026, 10, 20, 7, 30, 0),
                CalledAt = new DateTime(2026, 10, 20, 7, 40, 0),
                StartedAt = new DateTime(2026, 10, 20, 7, 45, 0),
                StateVersion = 2,
            },
            new QueueEntry
            {
                Id = 11,
                VisitId = 100,
                ServicePointId = 11,
                CurrentServicePointId = 11,
                QueueDate = new DateOnly(2026, 10, 20),
                QueueNumber = 1,
                QueueType = "DIAGNOSTIC",
                Status = "COMPLETED",
                PresenceStatus = "PRESENT",
                ArrivedAt = new DateTime(2026, 10, 20, 7, 55, 0),
                ReadyAt = new DateTime(2026, 10, 20, 7, 56, 0),
                CalledAt = new DateTime(2026, 10, 20, 7, 58, 0),
                StartedAt = new DateTime(2026, 10, 20, 8, 0, 0),
                CompletedAt = new DateTime(2026, 10, 20, 8, 20, 0),
                StateVersion = 4,
            },
            new QueueEntry
            {
                Id = 12,
                VisitId = 100,
                ServicePointId = 12,
                CurrentServicePointId = 12,
                QueueDate = new DateOnly(2026, 10, 20),
                QueueNumber = 2,
                QueueType = "DIAGNOSTIC",
                Status = "IN_PROGRESS",
                PresenceStatus = "PRESENT",
                ArrivedAt = new DateTime(2026, 10, 20, 8, 5, 0),
                ReadyAt = new DateTime(2026, 10, 20, 8, 10, 0),
                CalledAt = new DateTime(2026, 10, 20, 8, 15, 0),
                StartedAt = new DateTime(2026, 10, 20, 8, 20, 0),
                StateVersion = 3,
            },
            new QueueEntry
            {
                Id = 13,
                VisitId = 101,
                ServicePointId = 13,
                CurrentServicePointId = 13,
                QueueDate = new DateOnly(2026, 10, 20),
                QueueNumber = 3,
                QueueType = "DIAGNOSTIC",
                Status = "IN_PROGRESS",
                PresenceStatus = "PRESENT",
                ArrivedAt = new DateTime(2026, 10, 20, 8, 12, 0),
                ReadyAt = new DateTime(2026, 10, 20, 8, 15, 0),
                CalledAt = new DateTime(2026, 10, 20, 8, 18, 0),
                StartedAt = new DateTime(2026, 10, 20, 8, 22, 0),
                StateVersion = 2,
            },
            new QueueEntry
            {
                Id = 14,
                VisitId = 101,
                ServicePointId = 11,
                CurrentServicePointId = 11,
                QueueDate = new DateOnly(2026, 10, 20),
                QueueNumber = 4,
                QueueType = "DIAGNOSTIC",
                Status = "COMPLETED",
                PresenceStatus = "PRESENT",
                ArrivedAt = new DateTime(2026, 10, 20, 8, 20, 0),
                ReadyAt = new DateTime(2026, 10, 20, 8, 22, 0),
                CalledAt = new DateTime(2026, 10, 20, 8, 25, 0),
                StartedAt = new DateTime(2026, 10, 20, 8, 28, 0),
                CompletedAt = new DateTime(2026, 10, 20, 8, 45, 0),
                StateVersion = 5,
            },
            new QueueEntry
            {
                Id = 15,
                VisitId = 101,
                ServicePointId = 11,
                CurrentServicePointId = 11,
                QueueDate = new DateOnly(2026, 10, 20),
                QueueNumber = 5,
                QueueType = "DIAGNOSTIC",
                Status = "READY",
                PresenceStatus = "PRESENT",
                ArrivedAt = new DateTime(2026, 10, 20, 8, 30, 0),
                ReadyAt = new DateTime(2026, 10, 20, 8, 32, 0),
                StateVersion = 2,
            });

        // Order 1: COMPLETED with finalized result
        // Order 2:ORDERED (not confirmed/started)
        // Order 3: CONFIRMED with Queue READY (not started in UC42)
        // Order 4: IN_PROGRESS (no result yet)
        // Order 5: IN_PROGRESS with unfinalized PENDING_VERIFY v1 result (DIA-202610-0005)
        // Order 6: COMPLETED in UC42 (Queue COMPLETED), no DiagnosticResult row yet
        // Order 7: CANCELLED
        // Order 8: Cross-hospital order (Hospital 2)
        db.DiagnosticOrders.AddRange(
            new DiagnosticOrder
            {
                Id = 1,
                OrderCode = "DIA-202610-0001",
                VisitId = 100,
                OrderingDoctorId = 1,
                ServiceId = 10,
                Priority = "NORMAL",
                Status = "COMPLETED",
                RequiredForConclusion = true,
                Instruction = "Lấy máu tĩnh mạch kiểm tra bạch cầu",
                OrderedAt = new DateTime(2026, 10, 20, 7, 50, 0),
                ConfirmedByStaffId = 5,
                ConfirmedAt = new DateTime(2026, 10, 20, 7, 55, 0),
                QueueEntryId = 11,
            },
            new DiagnosticOrder
            {
                Id = 2,
                OrderCode = "DIA-202610-0002",
                VisitId = 101,
                OrderingDoctorId = 1,
                ServiceId = 10,
                Priority = "NORMAL",
                Status = "ORDERED",
                RequiredForConclusion = false,
                Instruction = "Chờ xác nhận chỉ định",
                OrderedAt = new DateTime(2026, 10, 20, 8, 35, 0),
            },
            new DiagnosticOrder
            {
                Id = 3,
                OrderCode = "DIA-202610-0003",
                VisitId = 101,
                OrderingDoctorId = 1,
                ServiceId = 10,
                Priority = "URGENT",
                Status = "CONFIRMED",
                RequiredForConclusion = true,
                Instruction = "Đã tiếp nhận nhưng chưa bắt đầu thực hiện kỹ thuật",
                OrderedAt = new DateTime(2026, 10, 20, 8, 30, 0),
                ConfirmedByStaffId = 5,
                ConfirmedAt = new DateTime(2026, 10, 20, 8, 31, 0),
                QueueEntryId = 15,
            },
            new DiagnosticOrder
            {
                Id = 4,
                OrderCode = "DIA-202610-0004",
                VisitId = 101,
                OrderingDoctorId = 1,
                ServiceId = 12,
                Priority = "STAT",
                Status = "IN_PROGRESS",
                RequiredForConclusion = true,
                Instruction = "Đo ECG 12 chuyển đạo cấp cứu",
                OrderedAt = new DateTime(2026, 10, 20, 8, 10, 0),
                ConfirmedByStaffId = 5,
                ConfirmedAt = new DateTime(2026, 10, 20, 8, 12, 0),
                QueueEntryId = 13,
            },
            new DiagnosticOrder
            {
                Id = 5,
                OrderCode = "DIA-202610-0005",
                VisitId = 100,
                OrderingDoctorId = 1,
                ServiceId = 11,
                Priority = "URGENT",
                Status = "IN_PROGRESS",
                RequiredForConclusion = true,
                Instruction = "Chụp X-quang ngực thẳng đứng",
                OrderedAt = new DateTime(2026, 10, 20, 8, 0, 0),
                ConfirmedByStaffId = 5,
                ConfirmedAt = new DateTime(2026, 10, 20, 8, 5, 0),
                QueueEntryId = 12,
            },
            new DiagnosticOrder
            {
                Id = 6,
                OrderCode = "DIA-202610-0006",
                VisitId = 101,
                OrderingDoctorId = 1,
                ServiceId = 10,
                Priority = "NORMAL",
                Status = "COMPLETED",
                RequiredForConclusion = true,
                Instruction = "Đã chạy máy huyết học xong, chờ nhập kết quả",
                OrderedAt = new DateTime(2026, 10, 20, 8, 18, 0),
                ConfirmedByStaffId = 5,
                ConfirmedAt = new DateTime(2026, 10, 20, 8, 20, 0),
                QueueEntryId = 14,
            },
            new DiagnosticOrder
            {
                Id = 7,
                OrderCode = "DIA-202610-0007",
                VisitId = 101,
                OrderingDoctorId = 1,
                ServiceId = 10,
                Priority = "NORMAL",
                Status = "CANCELLED",
                RequiredForConclusion = false,
                Instruction = "Chỉ định đã hủy",
                OrderedAt = new DateTime(2026, 10, 20, 8, 40, 0),
            },
            new DiagnosticOrder
            {
                Id = 8,
                OrderCode = "DIA-202610-0008",
                VisitId = 101,
                OrderingDoctorId = 1,
                ServiceId = 20,
                Priority = "NORMAL",
                Status = "IN_PROGRESS",
                RequiredForConclusion = false,
                Instruction = "Chỉ định thuộc Cơ sở 2",
                OrderedAt = new DateTime(2026, 10, 20, 8, 45, 0),
            });

        db.DiagnosticResults.AddRange(
            new DiagnosticResult
            {
                Id = 1,
                DiagnosticOrderId = 1,
                Status = "FINALIZED",
                FinalizedAt = new DateTime(2026, 10, 20, 8, 20, 0),
                FinalizedByStaffId = 5,
            },
            new DiagnosticResult
            {
                Id = 2,
                DiagnosticOrderId = 5,
                Status = "PENDING_VERIFY",
                FinalizedAt = null,
                FinalizedByStaffId = null,
            });

        db.DiagnosticResultVersions.AddRange(
            new DiagnosticResultVersion
            {
                Id = 1,
                DiagnosticResultId = 1,
                VersionNo = 1,
                ResultText = "WBC: 8.2 G/L; RBC: 4.6 T/L; HGB: 142 g/L; PLT: 250 G/L.",
                Conclusion = "Công thức máu trong giới hạn bình thường.",
                ReportFileUrl = "https://cdn.medflow.vn/reports/cbc-0001.pdf",
                EnteredByStaffId = 5,
                EnteredAt = new DateTime(2026, 10, 20, 8, 18, 0),
                IsFinalized = true,
                VerifiedByStaffId = 5,
                VerifiedAt = new DateTime(2026, 10, 20, 8, 20, 0),
            },
            new DiagnosticResultVersion
            {
                Id = 2,
                DiagnosticResultId = 2,
                VersionNo = 1,
                ResultText = "Bản sơ bộ: Bóng tim không to, góc sườn hoành hai bên sáng.",
                Conclusion = "Chờ bác sĩ CĐHA duyệt kết luận cuối.",
                ReportFileUrl = "/reports/xray-0005-draft.pdf",
                EnteredByStaffId = 5,
                EnteredAt = new DateTime(2026, 10, 20, 8, 35, 0),
                IsFinalized = false,
                VerifiedByStaffId = null,
                VerifiedAt = null,
            });

        db.SaveChanges();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    [Fact]
    public async Task GetOrdersForResultEntryAsync_ReturnsHospitalScopedItemsAndSummaryStats()
    {
        var response = await _service.GetOrdersForResultEntryAsync(actorUserId: 7);

        Assert.Equal(1, response.HospitalId);
        Assert.Equal("HOS-DEMO-01", response.HospitalCode);
        Assert.Equal(7, response.StaffProfile.UserId);
        Assert.Contains("DIAGNOSTIC_RESULT_WRITE", response.StaffProfile.Permissions);

        // Hospital 1 has orders 1..7 (order 8 belongs to Hospital 2)
        Assert.Equal(7, response.Summary.TotalOrders);
        Assert.Equal(3, response.Summary.ReadyForResultEntryCount); // orders 4, 5, 6
        Assert.Equal(3, response.Summary.InProgressExecutionCount);
        Assert.Equal(1, response.Summary.PendingVerificationCount); // order 5
        Assert.Equal(1, response.Summary.FinalizedCount); // order 1
        Assert.Equal(1, response.Summary.CancelledCount); // order 7
        Assert.DoesNotContain(response.Items, i => i.OrderId == 8);

        // Ready-to-enter STAT/URGENT orders are prioritized at top
        Assert.Equal(4, response.Items[0].OrderId); // STAT + CanEnterResult
        Assert.Equal(5, response.Items[1].OrderId); // URGENT + CanEnterResult
        Assert.Equal(6, response.Items[2].OrderId); // NORMAL + CanEnterResult
    }

    [Fact]
    public async Task GetOrdersForResultEntryAsync_OperationalFilters_FilterCorrectly()
    {
        var readyOnly = await _service.GetOrdersForResultEntryAsync(
            7,
            new GetDiagnosticOrdersForResultEntryRequest(ReadyForResultEntryOnly: true));
        Assert.Equal(3, readyOnly.Items.Count);
        Assert.All(readyOnly.Items, i => Assert.True(i.CanEnterResult));

        var pendingFinalization = await _service.GetOrdersForResultEntryAsync(
            7,
            new GetDiagnosticOrdersForResultEntryRequest(PendingFinalizationOnly: true));
        Assert.Single(pendingFinalization.Items);
        Assert.Equal(5, pendingFinalization.Items[0].OrderId);
        Assert.Equal("PENDING_VERIFY", pendingFinalization.Items[0].ResultStatus);

        var byCategory = await _service.GetOrdersForResultEntryAsync(
            7,
            new GetDiagnosticOrdersForResultEntryRequest(Category: "IMAGING"));
        Assert.Single(byCategory.Items);
        Assert.Equal("DIA-202610-0005", byCategory.Items[0].OrderCode);

        var byResultStatus = await _service.GetOrdersForResultEntryAsync(
            7,
            new GetDiagnosticOrdersForResultEntryRequest(ResultStatus: "FINALIZED"));
        Assert.Single(byResultStatus.Items);
        Assert.Equal(1, byResultStatus.Items[0].OrderId);

        var byKeyword = await _service.GetOrdersForResultEntryAsync(
            7,
            new GetDiagnosticOrdersForResultEntryRequest(Keyword: "góc sườn hoành"));
        Assert.Single(byKeyword.Items);
        Assert.Equal(5, byKeyword.Items[0].OrderId);
    }

    [Theory]
    [InlineData("5")]
    [InlineData("DIA-202610-0005")]
    [InlineData("do-0005")]
    [InlineData("dr-0002")]
    [InlineData("q-012")]
    [InlineData("C002")]
    [InlineData("VIS-202610-0100")]
    public async Task GetOrderResultByIdOrCodeAsync_SupportsAllIdentifiersAndReturnsClinicalContextAndVersions(string identifier)
    {
        var detail = await _service.GetOrderResultByIdOrCodeAsync(7, identifier);

        Assert.Equal(5, detail.Order.OrderId);
        Assert.Equal("DIA-202610-0005", detail.Order.OrderCode);
        Assert.True(detail.Order.HasResult);
        Assert.False(detail.Order.IsResultFinalized);
        Assert.Equal("PENDING_VERIFY", detail.Order.ResultStatus);
        Assert.Single(detail.Versions);
        Assert.Equal(1, detail.Versions[0].VersionNo);
        Assert.NotEmpty(detail.StatusHistory);
        Assert.Equal("J18.9", detail.ClinicalContext.PrimaryDiagnosisCode);
        Assert.Equal("Ho khan 2 tuần kèm sốt nhẹ về chiều", detail.ClinicalContext.PresentingComplaint);
    }

    [Fact]
    public async Task EnterDiagnosticResultAsync_DraftThenFinalize_CreatesVersionsUpdatesOrderAndQueuePreservesVisitStatusAndUnblocksDoctor()
    {
        // Before finalization on Visit 100: Order 1 is finalized, Order 5 is PENDING_VERIFY -> AllMandatoryResultsReady is false
        var workspaceBefore = await _consultationService.GetDiagnosticResultsAsync(3, "VIS-202610-0100");
        Assert.False(workspaceBefore.DiagnosticSummary.AllMandatoryResultsReady);

        // Finalize Order 5 (which already has v1 PENDING_VERIFY) -> creates v2 FINALIZED
        var finalizeResponse = await _service.EnterDiagnosticResultAsync(
            actorUserId: 7,
            orderIdOrCode: "DIA-202610-0005",
            request: new EnterDiagnosticResultRequest(
                ResultText: "Nhu mô phổi hai bên sáng đều, không thấy tổn thương đông đặc.",
                Conclusion: "X-quang ngực thẳng trong giới hạn bình thường.",
                ReportFileUrl: "https://cdn.medflow.vn/reports/xray-0005-final.pdf",
                Status: "FINALIZED",
                IsFinalized: true,
                CorrectionReason: "Bác sĩ CĐHA đã thẩm định và ký số kết quả.",
                ExpectedStateVersion: 3),
            ipAddress: "127.0.0.1");

        Assert.Equal(DiagnosticResultEntryService.DiagnosticResultFinalizedAction, finalizeResponse.ActionOutcome);
        Assert.True(finalizeResponse.IsFinalized);
        Assert.Equal("FINALIZED", finalizeResponse.ResultStatus);
        Assert.Equal(2, finalizeResponse.VersionNo);
        Assert.Equal("IN_PROGRESS", finalizeResponse.PreviousOrderStatus);
        Assert.Equal("COMPLETED", finalizeResponse.OrderStatus);
        Assert.Equal("IN_PROGRESS", finalizeResponse.PreviousQueueStatus);
        Assert.Equal("COMPLETED", finalizeResponse.QueueStatus);
        Assert.Equal(3, finalizeResponse.PreviousStateVersion);
        Assert.Equal(4, finalizeResponse.StateVersion);
        Assert.NotNull(finalizeResponse.FinalizedAt);
        Assert.Equal(2, finalizeResponse.Versions.Count);
        Assert.Equal(2, finalizeResponse.LatestVersion.VersionNo);
        Assert.True(finalizeResponse.LatestVersion.IsFinalized);
        Assert.Equal(5, finalizeResponse.Order.FinalizedByStaffId);
        Assert.False(finalizeResponse.Order.CanEnterResult);

        // Verify Visit.Status was NOT overwritten
        var visitAfter = await _dbContext.Visits.AsNoTracking().FirstAsync(v => v.Id == 100);
        Assert.Equal("AWAITING_RESULTS", visitAfter.Status);

        // Verify AuditLog was persisted
        var auditLog = await _dbContext.AuditLogs
            .AsNoTracking()
            .FirstOrDefaultAsync(a =>
                a.EntityType == DiagnosticResultEntryService.DiagnosticResultEntityType &&
                a.Action == DiagnosticResultEntryService.DiagnosticResultFinalizedAction);
        Assert.NotNull(auditLog);
        Assert.Equal(7, auditLog.ActorUserId);

        // Verify downstream ConsultationWorkspaceService now sees AllMandatoryResultsReady == true
        var workspaceAfter = await _consultationService.GetDiagnosticResultsAsync(3, "VIS-202610-0100");
        Assert.True(workspaceAfter.DiagnosticSummary.AllMandatoryResultsReady);

        // Attempting to enter result again on now-finalized Order 5 must throw 409 Conflict
        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.EnterDiagnosticResultAsync(
                actorUserId: 7,
                orderIdOrCode: "DIA-202610-0005",
                request: new EnterDiagnosticResultRequest(
                    ResultText: "Nhập lại kết quả trên phiếu đã khóa cuối.",
                    Conclusion: "Kết luận trùng lặp.")));
    }

    [Fact]
    public async Task EnterDiagnosticResultAsync_SaveDraftOnInProgressOrder_KeepsOrderInProgressAndAllowsSubsequentVerify()
    {
        // Step 1: Enter DRAFT result on IN_PROGRESS Order 4 (ECG)
        var draftResponse = await _service.EnterDiagnosticResultAsync(
            actorUserId: 7,
            orderIdOrCode: "DIA-202610-0004",
            request: new EnterDiagnosticResultRequest(
                Findings: "Nhịp xoang tần số 78 chu kỳ/phút, trục trung gian, không biến đổi ST-T.",
                Impression: "Điện tâm đồ sơ bộ bình thường.",
                Status: "DRAFT",
                IsFinalized: false,
                ExpectedStateVersion: 2));

        Assert.Equal(DiagnosticResultEntryService.DiagnosticResultEnteredAction, draftResponse.ActionOutcome);
        Assert.False(draftResponse.IsFinalized);
        Assert.Equal("DRAFT", draftResponse.ResultStatus);
        Assert.Equal(1, draftResponse.VersionNo);
        Assert.Equal("IN_PROGRESS", draftResponse.OrderStatus);
        Assert.Equal("IN_PROGRESS", draftResponse.QueueStatus);
        Assert.Equal(3, draftResponse.StateVersion);
        Assert.Null(draftResponse.FinalizedAt);
        Assert.True(draftResponse.Order.CanEnterResult);

        // Step 2: Enter VERIFIED result via body OrderCode (orderIdOrCode = null)
        var verifyResponse = await _service.EnterDiagnosticResultAsync(
            actorUserId: 7,
            orderIdOrCode: null,
            request: new EnterDiagnosticResultRequest(
                OrderCode: "DIA-202610-0004",
                ResultText: "Nhịp xoang đều 78 ck/phút, PR 0.16s, QRS 0.08s, ST đẳng điện.",
                Conclusion: "Điện tâm đồ 12 chuyển đạo trong giới hạn bình thường.",
                Status: "VERIFIED",
                IsVerified: true,
                ExpectedStateVersion: 3));

        Assert.Equal(DiagnosticResultEntryService.DiagnosticResultFinalizedAction, verifyResponse.ActionOutcome);
        Assert.True(verifyResponse.IsFinalized);
        Assert.Equal("VERIFIED", verifyResponse.ResultStatus);
        Assert.Equal(2, verifyResponse.VersionNo);
        Assert.Equal("COMPLETED", verifyResponse.OrderStatus);
        Assert.Equal("COMPLETED", verifyResponse.QueueStatus);
        Assert.Equal(4, verifyResponse.StateVersion);
        Assert.NotNull(verifyResponse.FinalizedAt);
    }

    [Fact]
    public async Task EnterDiagnosticResultAsync_OnCompletedExecutionOrderWithoutResultRow_CreatesFinalizedResult()
    {
        // Order 6 has Status = COMPLETED and QueueEntry.Status = COMPLETED from UC42, but no DiagnosticResult row yet
        var response = await _service.EnterDiagnosticResultAsync(
            actorUserId: 7,
            orderIdOrCode: null,
            request: new EnterDiagnosticResultRequest(
                OrderId: 6,
                ResultText: "WBC: 7.4 G/L; RBC: 4.8 T/L; HGB: 145 g/L; PLT: 230 G/L.",
                Conclusion: "Các chỉ số huyết học trong giới hạn bình thường."));

        Assert.Equal(DiagnosticResultEntryService.DiagnosticResultFinalizedAction, response.ActionOutcome);
        Assert.True(response.IsFinalized);
        Assert.Equal("FINALIZED", response.ResultStatus);
        Assert.Equal(1, response.VersionNo);
        Assert.Equal("COMPLETED", response.OrderStatus);
        Assert.False(response.Order.CanEnterResult);
    }

    [Fact]
    public async Task AuthorizationAndOwnershipGuards_ThrowExpected401_403_404()
    {
        var validRequest = new EnterDiagnosticResultRequest(
            ResultText: "Kết quả xét nghiệm hợp lệ.",
            Conclusion: "Kết luận xét nghiệm hợp lệ.");

        // 401: invalid userId
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _service.GetOrdersForResultEntryAsync(actorUserId: 0));

        // 404: non-existent user
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.GetOrdersForResultEntryAsync(actorUserId: 9999));

        // 403: Doctor role (not DIAGNOSTIC_STAFF)
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetOrdersForResultEntryAsync(actorUserId: 3));

        // 403: Inactive user
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetOrdersForResultEntryAsync(actorUserId: 8));

        // 403: Locked user
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetOrdersForResultEntryAsync(actorUserId: 9));

        // 403: Missing staff profile
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetOrdersForResultEntryAsync(actorUserId: 11));

        // 403: Inactive staff profile
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetOrdersForResultEntryAsync(actorUserId: 12));

        // 403: Inactive hospital
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetOrdersForResultEntryAsync(actorUserId: 13));

        // 403: Inactive department
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetOrdersForResultEntryAsync(actorUserId: 14));

        // User 10 has QUEUE_MANAGE only (Role 5) when Role 4 is removed -> can read list, but cannot write result
        var role4Link = await _dbContext.UserRoles.FirstAsync(ur => ur.UserId == 10 && ur.RoleId == 4);
        _dbContext.UserRoles.Remove(role4Link);
        var role5 = await _dbContext.Roles.FirstAsync(r => r.Id == 5);
        role5.Code = "DIAGNOSTIC_STAFF";
        await _dbContext.SaveChangesAsync();

        var readList = await _service.GetOrdersForResultEntryAsync(actorUserId: 10);
        Assert.NotNull(readList);
        Assert.All(readList.Items, i => Assert.False(i.CanEnterResult));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.EnterDiagnosticResultAsync(10, "DIA-202610-0004", validRequest));

        // 403: Cross-hospital order access (Order 8 belongs to Hospital 2)
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.GetOrderResultByIdOrCodeAsync(7, "DIA-202610-0008"));
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.EnterDiagnosticResultAsync(7, "DIA-202610-0008", validRequest));
    }

    [Fact]
    public async Task ValidationGuards_InvalidInputs_ThrowValidationException()
    {
        // Invalid list filters
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.GetOrdersForResultEntryAsync(7, new GetDiagnosticOrdersForResultEntryRequest(Limit: 0)));
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.GetOrdersForResultEntryAsync(7, new GetDiagnosticOrdersForResultEntryRequest(Limit: 101)));
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.GetOrdersForResultEntryAsync(7, new GetDiagnosticOrdersForResultEntryRequest(Category: "INVALID")));
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.GetOrdersForResultEntryAsync(7, new GetDiagnosticOrdersForResultEntryRequest(Status: "INVALID")));
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.GetOrdersForResultEntryAsync(7, new GetDiagnosticOrdersForResultEntryRequest(ResultStatus: "INVALID")));
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.GetOrdersForResultEntryAsync(7, new GetDiagnosticOrdersForResultEntryRequest(QueueStatus: "INVALID")));
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.GetOrdersForResultEntryAsync(7, new GetDiagnosticOrdersForResultEntryRequest(Priority: "INVALID")));
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.GetOrdersForResultEntryAsync(7, new GetDiagnosticOrdersForResultEntryRequest(VisitId: 0)));
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.GetOrdersForResultEntryAsync(7, new GetDiagnosticOrdersForResultEntryRequest(Keyword: "<script>")));

        // Null request or missing identifier
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.EnterDiagnosticResultAsync(7, "DIA-202610-0004", null));
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.EnterDiagnosticResultAsync(
                7,
                null,
                new EnterDiagnosticResultRequest(ResultText: "Kết quả hợp lệ", Conclusion: "Kết luận hợp lệ")));

        // Empty or too short ResultText / Conclusion
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.EnterDiagnosticResultAsync(
                7,
                "DIA-202610-0004",
                new EnterDiagnosticResultRequest(ResultText: "", Conclusion: "Kết luận hợp lệ")));
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.EnterDiagnosticResultAsync(
                7,
                "DIA-202610-0004",
                new EnterDiagnosticResultRequest(ResultText: "Kết quả hợp lệ", Conclusion: "AB")));

        // XSS in ResultText, Conclusion, CorrectionReason
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.EnterDiagnosticResultAsync(
                7,
                "DIA-202610-0004",
                new EnterDiagnosticResultRequest(ResultText: "<script>alert(1)</script>", Conclusion: "Kết luận hợp lệ")));
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.EnterDiagnosticResultAsync(
                7,
                "DIA-202610-0004",
                new EnterDiagnosticResultRequest(ResultText: "Kết quả hợp lệ", Conclusion: "<b>Kết luận</b>")));
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.EnterDiagnosticResultAsync(
                7,
                "DIA-202610-0004",
                new EnterDiagnosticResultRequest(
                    ResultText: "Kết quả hợp lệ",
                    Conclusion: "Kết luận hợp lệ",
                    CorrectionReason: "<img src=x>")));

        // Invalid ReportFileUrl
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.EnterDiagnosticResultAsync(
                7,
                "DIA-202610-0004",
                new EnterDiagnosticResultRequest(
                    ResultText: "Kết quả hợp lệ",
                    Conclusion: "Kết luận hợp lệ",
                    ReportFileUrl: "javascript:alert(1)")));

        // Incompatible Status and IsFinalized / IsVerified
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.EnterDiagnosticResultAsync(
                7,
                "DIA-202610-0004",
                new EnterDiagnosticResultRequest(
                    ResultText: "Kết quả hợp lệ",
                    Conclusion: "Kết luận hợp lệ",
                    Status: "DRAFT",
                    IsFinalized: true)));
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.EnterDiagnosticResultAsync(
                7,
                "DIA-202610-0004",
                new EnterDiagnosticResultRequest(
                    ResultText: "Kết quả hợp lệ",
                    Conclusion: "Kết luận hợp lệ",
                    Status: "FINALIZED",
                    IsFinalized: false)));
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.EnterDiagnosticResultAsync(
                7,
                "DIA-202610-0004",
                new EnterDiagnosticResultRequest(
                    ResultText: "Kết quả hợp lệ",
                    Conclusion: "Kết luận hợp lệ",
                    IsFinalized: false,
                    IsVerified: true)));

        // Invalid ExpectedStateVersion
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.EnterDiagnosticResultAsync(
                7,
                "DIA-202610-0004",
                new EnterDiagnosticResultRequest(
                    ResultText: "Kết quả hợp lệ",
                    Conclusion: "Kết luận hợp lệ",
                    ExpectedStateVersion: 0)));
    }

    [Fact]
    public async Task StateMachineAndConflictGuards_ThrowConflictException()
    {
        var validRequest = new EnterDiagnosticResultRequest(
            ResultText: "Kết quả cận lâm sàng đầy đủ.",
            Conclusion: "Kết luận cận lâm sàng bình thường.");

        // 404: Unknown order
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.EnterDiagnosticResultAsync(7, "DIA-202610-9999", validRequest));

        // 409: Already finalized order (Order 1)
        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.EnterDiagnosticResultAsync(7, "DIA-202610-0001", validRequest));

        // 409: ORDERED order not yet confirmed/started (Order 2)
        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.EnterDiagnosticResultAsync(7, "DIA-202610-0002", validRequest));

        // 409: CONFIRMED order with QueueEntry in READY (UC42 execution not started yet) (Order 3)
        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.EnterDiagnosticResultAsync(7, "DIA-202610-0003", validRequest));

        // 409: CANCELLED order (Order 7)
        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.EnterDiagnosticResultAsync(7, "DIA-202610-0007", validRequest));

        // 409: ExpectedStateVersion mismatch on Order 4 (current state version is 2)
        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.EnterDiagnosticResultAsync(
                7,
                "DIA-202610-0004",
                validRequest with { ExpectedStateVersion = 99 }));

        // 409: Visit CANCELLED
        var order4 = await _dbContext.DiagnosticOrders.FirstAsync(o => o.Id == 4);
        order4.VisitId = 102;
        await _dbContext.SaveChangesAsync();
        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.EnterDiagnosticResultAsync(7, "DIA-202610-0004", validRequest));

        // 409: Visit COMPLETED
        order4.VisitId = 103;
        await _dbContext.SaveChangesAsync();
        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.EnterDiagnosticResultAsync(7, "DIA-202610-0004", validRequest));

        // 409: MedicalRecord FINALIZED
        order4.VisitId = 104;
        await _dbContext.SaveChangesAsync();
        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.EnterDiagnosticResultAsync(7, "DIA-202610-0004", validRequest));

        // Restore VisitId = 101 and test inactive service
        order4.VisitId = 101;
        order4.ServiceId = 14; // Inactive service
        await _dbContext.SaveChangesAsync();
        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.EnterDiagnosticResultAsync(7, "DIA-202610-0004", validRequest));

        // Maintenance room guard
        order4.ServiceId = 12;
        var qe13 = await _dbContext.QueueEntries.FirstAsync(q => q.Id == 13);
        qe13.CurrentServicePointId = 14; // ServicePoint 14 is in Room 103 (MAINTENANCE)
        await _dbContext.SaveChangesAsync();
        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.EnterDiagnosticResultAsync(7, "DIA-202610-0004", validRequest));
    }

    [Fact]
    public async Task UpdateDiagnosticResultAsync_OnFinalizedOrder_AppendsVersionPreservesHeaderLifecycleAndVisit()
    {
        var before = await _service.GetOrderResultByIdOrCodeAsync(7, "DIA-202610-0001");
        Assert.True(before.Order.IsResultFinalized);
        Assert.False(before.Order.CanEnterResult);
        Assert.True(before.Order.CanAmendResult);
        Assert.Null(before.Order.AmendDisabledReason);

        var originalFinalizedAt = new DateTime(2026, 10, 20, 8, 20, 0);
        var response = await _service.UpdateDiagnosticResultAsync(
            actorUserId: 7,
            orderIdOrCode: "DIA-202610-0001",
            request: new UpdateDiagnosticResultRequest(
                ResultText: "WBC: 9.1 G/L; RBC: 4.4 T/L; HGB: 138 g/L; PLT: 240 G/L.",
                Conclusion: "Công thức máu sau hiệu đính vẫn trong giới hạn theo dõi.",
                CorrectionReason: "Sửa số WBC do nhập nhầm đơn vị.",
                ExpectedStateVersion: 4),
            ipAddress: "127.0.0.1");

        Assert.Equal(DiagnosticResultEntryService.DiagnosticResultAmendedAction, response.ActionOutcome);
        Assert.True(response.IsFinalized);
        Assert.Equal(2, response.VersionNo);
        Assert.Equal("COMPLETED", response.PreviousOrderStatus);
        Assert.Equal("COMPLETED", response.OrderStatus);
        Assert.Equal("COMPLETED", response.PreviousQueueStatus);
        Assert.Equal("COMPLETED", response.QueueStatus);
        Assert.Equal(4, response.PreviousStateVersion);
        Assert.Equal(5, response.StateVersion);
        Assert.Equal(originalFinalizedAt, response.FinalizedAt);
        Assert.Equal(5, response.Order.FinalizedByStaffId);
        Assert.False(response.Order.CanEnterResult);
        Assert.True(response.Order.CanAmendResult);
        Assert.Equal("Sửa số WBC do nhập nhầm đơn vị.", response.LatestVersion.CorrectionReason);
        Assert.Equal("https://cdn.medflow.vn/reports/cbc-0001.pdf", response.LatestVersion.ReportFileUrl);
        Assert.Equal(2, response.Versions.Count);

        var result = await _dbContext.DiagnosticResults.AsNoTracking().FirstAsync(r => r.Id == 1);
        Assert.Equal("FINALIZED", result.Status);
        Assert.Equal(originalFinalizedAt, result.FinalizedAt);
        Assert.Equal(5, result.FinalizedByStaffId);

        var order = await _dbContext.DiagnosticOrders.AsNoTracking().FirstAsync(o => o.Id == 1);
        Assert.Equal("COMPLETED", order.Status);
        var queue = await _dbContext.QueueEntries.AsNoTracking().FirstAsync(q => q.Id == 11);
        Assert.Equal("COMPLETED", queue.Status);
        Assert.Equal("PRESENT", queue.PresenceStatus);
        Assert.Equal(5, queue.StateVersion);
        Assert.Null(queue.BlockedReason);
        var visit = await _dbContext.Visits.AsNoTracking().FirstAsync(v => v.Id == 100);
        Assert.Equal("AWAITING_RESULTS", visit.Status);

        var versionCount = await _dbContext.DiagnosticResultVersions.CountAsync(v => v.DiagnosticResultId == 1);
        Assert.Equal(2, versionCount);

        var audit = await _dbContext.AuditLogs.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Action == DiagnosticResultEntryService.DiagnosticResultAmendedAction);
        Assert.NotNull(audit);
        Assert.Equal(7, audit.ActorUserId);
        Assert.Equal("1", audit.EntityId);

        var detail = await _service.GetOrderResultByIdOrCodeAsync(7, "DIA-202610-0001");
        Assert.Contains(detail.StatusHistory, h => h.Action == DiagnosticResultEntryService.DiagnosticResultAmendedAction);

        var doctorView = await _consultationService.GetDiagnosticResultsAsync(3, "VIS-202610-0100");
        Assert.False(doctorView.DiagnosticSummary.AllMandatoryResultsReady);
        var cbc = Assert.Single(doctorView.Items, i => i.OrderCode == "DIA-202610-0001");
        Assert.True(cbc.IsResultFinalized);
        Assert.Equal("WBC: 9.1 G/L; RBC: 4.4 T/L; HGB: 138 g/L; PLT: 240 G/L.", cbc.ResultText);

        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.EnterDiagnosticResultAsync(
                7,
                "DIA-202610-0001",
                new EnterDiagnosticResultRequest(
                    ResultText: "Nhập lại sau khi đã sửa bản khóa.",
                    Conclusion: "Không được đi qua Enter.")));
    }

    [Fact]
    public async Task UpdateDiagnosticResultAsync_ClearReportUrl_StoresNullUrl()
    {
        var response = await _service.UpdateDiagnosticResultAsync(
            7,
            "DIA-202610-0001",
            new UpdateDiagnosticResultRequest(
                ResultText: "WBC: 8.0 G/L sau khi gỡ file đính kèm.",
                Conclusion: "Công thức máu bình thường, không kèm file.",
                ReportFileUrl: "",
                CorrectionReason: "Gỡ file báo cáo đính kèm sai."));

        Assert.Null(response.LatestVersion.ReportFileUrl);
    }

    [Fact]
    public async Task UpdateDiagnosticResultAsync_Guards_RejectInvalidOrClosedMutations()
    {
        var changed = new UpdateDiagnosticResultRequest(
            ResultText: "Nội dung kết quả sau sửa đổi.",
            Conclusion: "Kết luận sau sửa đổi.",
            CorrectionReason: "Lý do sửa hợp lệ.");

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateDiagnosticResultAsync(7, "DIA-202610-0001", null));
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateDiagnosticResultAsync(
                7,
                "DIA-202610-0001",
                changed with { CorrectionReason = null }));
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateDiagnosticResultAsync(
                7,
                "DIA-202610-0001",
                changed with { CorrectionReason = "abcd" }));
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateDiagnosticResultAsync(
                7,
                "DIA-202610-0001",
                changed with { CorrectionReason = "<script>" }));
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.UpdateDiagnosticResultAsync(
                7,
                "DIA-202610-0001",
                changed with { ResultText = "" }));

        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.UpdateDiagnosticResultAsync(7, "DIA-202610-0005", changed));

        var versionCountBefore = await _dbContext.DiagnosticResultVersions.CountAsync(v => v.DiagnosticResultId == 1);
        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.UpdateDiagnosticResultAsync(
                7,
                "DIA-202610-0001",
                new UpdateDiagnosticResultRequest(
                    ResultText: "WBC: 8.2 G/L; RBC: 4.6 T/L; HGB: 142 g/L; PLT: 250 G/L.",
                    Conclusion: "Công thức máu trong giới hạn bình thường.",
                    CorrectionReason: "Gửi lại đúng nội dung cũ.")));
        Assert.Equal(versionCountBefore, await _dbContext.DiagnosticResultVersions.CountAsync(v => v.DiagnosticResultId == 1));

        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.UpdateDiagnosticResultAsync(
                7,
                "DIA-202610-0001",
                changed with { ExpectedStateVersion = 99 }));

        var queue = await _dbContext.QueueEntries.FirstAsync(q => q.Id == 11);
        queue.Status = "SKIPPED";
        await _dbContext.SaveChangesAsync();
        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.UpdateDiagnosticResultAsync(7, "DIA-202610-0001", changed));
        queue.Status = "COMPLETED";
        await _dbContext.SaveChangesAsync();

        var order = await _dbContext.DiagnosticOrders.FirstAsync(o => o.Id == 1);
        order.Status = "CANCELLED";
        await _dbContext.SaveChangesAsync();
        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.UpdateDiagnosticResultAsync(7, "DIA-202610-0001", changed));
        order.Status = "COMPLETED";
        await _dbContext.SaveChangesAsync();

        order.VisitId = 103;
        await _dbContext.SaveChangesAsync();
        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.UpdateDiagnosticResultAsync(7, "DIA-202610-0001", changed));

        order.VisitId = 104;
        await _dbContext.SaveChangesAsync();
        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.UpdateDiagnosticResultAsync(7, "DIA-202610-0001", changed));

        order.VisitId = 100;
        await _dbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.UpdateDiagnosticResultAsync(3, "DIA-202610-0001", changed));
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _service.UpdateDiagnosticResultAsync(7, "DIA-202610-0008", changed));

        Assert.Equal(versionCountBefore, await _dbContext.DiagnosticResultVersions.CountAsync(v => v.DiagnosticResultId == 1));
    }
}
