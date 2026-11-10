using MedFlow.Application.DTOs.Consultation;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Consultation;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.UnitTests.Consultation;

public sealed class ConsultationWorkspaceServiceTests : IDisposable
{
    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class ConsultationTestDbContext : DbContext, IApplicationDbContext
    {
        public ConsultationTestDbContext(DbContextOptions<ConsultationTestDbContext> options)
            : base(options)
        {
        }

        private static NotSupportedException NotUsed() =>
            new("Not needed for ConsultationWorkspaceService unit tests.");

        public DbSet<User> Users => Set<User>();
        public DbSet<Role> Roles => Set<Role>();
        public DbSet<Permission> Permissions => Set<Permission>();
        public DbSet<UserRole> UserRoles => Set<UserRole>();
        public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
        public DbSet<Patient> Patients => Set<Patient>();
        public DbSet<Staff> Staff => Set<Staff>();
        public DbSet<Doctor> Doctors => Set<Doctor>();
        public DbSet<DoctorServiceAuthorization> DoctorServiceAuthorizations => Set<DoctorServiceAuthorization>();
        public DbSet<ExaminationSlot> ExaminationSlots => Set<ExaminationSlot>();
        public DbSet<AuthSession> AuthSessions => throw NotUsed();
        public DbSet<AuthVerificationChallenge> AuthVerificationChallenges => throw NotUsed();
        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
        public DbSet<Hospital> Hospitals => Set<Hospital>();
        public DbSet<Department> Departments => Set<Department>();
        public DbSet<HospitalRoom> HospitalRooms => Set<HospitalRoom>();
        public DbSet<Specialty> Specialties => Set<Specialty>();
        public DbSet<Service> Services => Set<Service>();
        public DbSet<ServicePrice> ServicePrices => Set<ServicePrice>();
        public DbSet<HospitalPolicy> HospitalPolicies => Set<HospitalPolicy>();
        public DbSet<Appointment> Appointments => Set<Appointment>();
        public DbSet<PatientFamilyLink> PatientFamilyLinks => throw NotUsed();
        public DbSet<Visit> Visits => Set<Visit>();
        public DbSet<QueueEntry> QueueEntries => Set<QueueEntry>();
        public DbSet<Payment> Payments => throw NotUsed();
        public DbSet<Prescription> Prescriptions => Set<Prescription>();
        public DbSet<PrescriptionItem> PrescriptionItems => Set<PrescriptionItem>();
        public DbSet<QueueServicePoint> QueueServicePoints => Set<QueueServicePoint>();
        public DbSet<QueueFairnessState> QueueFairnessStates => Set<QueueFairnessState>();
        public DbSet<MedicalRecord> MedicalRecords => Set<MedicalRecord>();
        public DbSet<MedicalRecordRevision> MedicalRecordRevisions => Set<MedicalRecordRevision>();
        public DbSet<VisitDiagnosis> VisitDiagnoses => Set<VisitDiagnosis>();
        public DbSet<Icd10Code> Icd10Codes => Set<Icd10Code>();
        public DbSet<DiagnosticOrder> DiagnosticOrders => Set<DiagnosticOrder>();
        public DbSet<DiagnosticResult> DiagnosticResults => Set<DiagnosticResult>();
        public DbSet<DiagnosticResultVersion> DiagnosticResultVersions => Set<DiagnosticResultVersion>();

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default)
            => Database.BeginTransactionAsync(ct);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Ignore<AuthSession>();
            modelBuilder.Ignore<AuthVerificationChallenge>();
            modelBuilder.Entity<ServicePrice>().HasKey(sp => sp.Id);
            modelBuilder.Entity<DoctorServiceAuthorization>().HasKey(a => new { a.DoctorId, a.ServiceId });
            modelBuilder.Ignore<PatientFamilyLink>();
            modelBuilder.Ignore<Payment>();
            modelBuilder.Ignore<Medicine>();
            modelBuilder.Ignore<MedicineBatch>();
            modelBuilder.Ignore<MedicationDispense>();
            modelBuilder.Ignore<DispenseItem>();
            modelBuilder.Ignore<StockTransaction>();

            modelBuilder.Entity<User>().HasKey(u => u.Id);
            modelBuilder.Entity<Role>().HasKey(r => r.Id);
            modelBuilder.Entity<Permission>().HasKey(p => p.Id);
            modelBuilder.Entity<UserRole>().HasKey(ur => new { ur.UserId, ur.RoleId });
            modelBuilder.Entity<RolePermission>().HasKey(rp => new { rp.RoleId, rp.PermissionId });
            modelBuilder.Entity<Patient>().HasKey(p => p.Id);
            modelBuilder.Entity<Hospital>().HasKey(h => h.Id);
            modelBuilder.Entity<Department>().HasKey(d => d.Id);
            modelBuilder.Entity<Specialty>().HasKey(s => s.Id);
            modelBuilder.Entity<Service>().HasKey(s => s.Id);
            modelBuilder.Entity<HospitalPolicy>().HasKey(p => p.Id);
            modelBuilder.Entity<HospitalRoom>(b =>
            {
                b.HasKey(r => r.Id);
                b.HasOne(r => r.Department).WithMany(d => d.HospitalRooms).HasForeignKey(r => r.DepartmentId);
            });
            modelBuilder.Entity<Staff>(b =>
            {
                b.HasKey(s => s.Id);
                b.HasOne(s => s.User).WithOne(u => u.Staff).HasForeignKey<Staff>(s => s.UserId);
                b.HasOne(s => s.Department).WithMany().HasForeignKey(s => s.DepartmentId);
            });
            modelBuilder.Entity<Doctor>(b =>
            {
                b.HasKey(d => d.Id);
                b.HasOne(d => d.Staff).WithOne(s => s.Doctor).HasForeignKey<Doctor>(d => d.StaffId);
                b.HasOne(d => d.Specialty).WithMany().HasForeignKey(d => d.SpecialtyId);
            });
            modelBuilder.Entity<ExaminationSlot>(b =>
            {
                b.HasKey(s => s.Id);
                b.HasOne(s => s.Service).WithMany().HasForeignKey(s => s.ServiceId);
                b.HasOne(s => s.Doctor).WithMany(d => d.ExaminationSlots).HasForeignKey(s => s.DoctorId);
                b.HasOne(s => s.Room).WithMany().HasForeignKey(s => s.RoomId);
            });
            modelBuilder.Entity<Appointment>(b =>
            {
                b.HasKey(a => a.Id);
                b.HasOne(a => a.Patient).WithMany().HasForeignKey(a => a.PatientId);
                b.HasOne(a => a.Slot).WithMany(s => s.Appointments).HasForeignKey(a => a.SlotId);
            });
            modelBuilder.Entity<Visit>().HasKey(v => v.Id);
            modelBuilder.Entity<QueueEntry>().HasKey(q => q.Id);
            modelBuilder.Entity<QueueServicePoint>().HasKey(sp => sp.Id);
            modelBuilder.Entity<QueueFairnessState>().HasKey(s => new { s.ServicePointId, s.QueueDate });
            modelBuilder.Entity<MedicalRecord>().HasKey(m => m.Id);
            modelBuilder.Entity<MedicalRecordRevision>().HasKey(r => r.Id);
            modelBuilder.Entity<VisitDiagnosis>().HasKey(d => d.Id);
            modelBuilder.Entity<Icd10Code>().HasKey(c => c.Id);
            modelBuilder.Entity<DiagnosticOrder>().HasKey(o => o.Id);
            modelBuilder.Entity<DiagnosticResult>().HasKey(r => r.Id);
            modelBuilder.Entity<DiagnosticResultVersion>().HasKey(v => v.Id);
            modelBuilder.Entity<Prescription>().HasKey(p => p.Id);
            modelBuilder.Entity<PrescriptionItem>().HasKey(i => i.Id);
            modelBuilder.Entity<AuditLog>(b =>
            {
                b.HasKey(a => a.Id);
                b.Ignore(a => a.IpAddress);
            });
        }
    }

    private readonly DbContextOptions<ConsultationTestDbContext> _dbOptions;
    private readonly ConsultationTestDbContext _db;
    private readonly FixedTimeProvider _timeProvider;
    private readonly ConsultationWorkspaceService _service;

    public ConsultationWorkspaceServiceTests()
    {
        _dbOptions = new DbContextOptionsBuilder<ConsultationTestDbContext>()
            .UseInMemoryDatabase($"ConsultationWorkspaceServiceTests_{Guid.NewGuid():N}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _db = new ConsultationTestDbContext(_dbOptions);
        // 2026-10-22 02:00 UTC == 2026-10-22 09:00 Vietnam time
        _timeProvider = new FixedTimeProvider(new DateTimeOffset(2026, 10, 22, 2, 0, 0, TimeSpan.Zero));
        _service = new ConsultationWorkspaceService(_db, _timeProvider);

        SeedTestData();
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    private void SeedTestData()
    {
        _db.Roles.AddRange(
            new Role { Id = 1, Code = "PATIENT", Name = "Bệnh nhân" },
            new Role { Id = 2, Code = "RECEPTION", Name = "Lễ tân" },
            new Role { Id = 3, Code = "DOCTOR", Name = "Bác sĩ" },
            new Role { Id = 4, Code = "DIAGNOSTIC_STAFF", Name = "Nhân viên CLS" },
            new Role { Id = 5, Code = "PHARMACY_STAFF", Name = "Dược sĩ" },
            new Role { Id = 6, Code = "HOSPITAL_ADMIN", Name = "Quản trị bệnh viện" },
            new Role { Id = 7, Code = "SYSTEM_ADMIN", Name = "Quản trị hệ thống" });

        _db.Permissions.AddRange(
            new Permission { Id = 1, Code = "MEDICAL_RECORD_WRITE" },
            new Permission { Id = 2, Code = "DIAGNOSTIC_ORDER" },
            new Permission { Id = 3, Code = "PRESCRIPTION_ISSUE" },
            new Permission { Id = 4, Code = "PAYMENT_VIEW" });

        _db.RolePermissions.AddRange(
            new RolePermission { RoleId = 3, PermissionId = 1 },
            new RolePermission { RoleId = 3, PermissionId = 2 },
            new RolePermission { RoleId = 3, PermissionId = 3 },
            new RolePermission { RoleId = 3, PermissionId = 4 });

        _db.Hospitals.AddRange(
            new Hospital { Id = 1, HospitalCode = "HOSP-01", Name = "Bệnh viện Đa khoa MedFlow Sài Gòn", IsActive = true },
            new Hospital { Id = 2, HospitalCode = "HOSP-02", Name = "Bệnh viện MedFlow Hà Nội", IsActive = true });

        _db.Departments.AddRange(
            new Department { Id = 1, HospitalId = 1, DepartmentCode = "DEPT-NOI", Name = "Khoa Nội Tổng Quát", IsActive = true },
            new Department { Id = 2, HospitalId = 1, DepartmentCode = "DEPT-TIM", Name = "Khoa Tim Mạch", IsActive = true },
            new Department { Id = 3, HospitalId = 2, DepartmentCode = "DEPT-HN", Name = "Khoa Nội Hà Nội", IsActive = true });

        _db.Specialties.AddRange(
            new Specialty { Id = 1, SpecialtyCode = "SPEC-NOI", Name = "Nội tổng quát", IsActive = true },
            new Specialty { Id = 2, SpecialtyCode = "SPEC-TIM", Name = "Tim mạch", IsActive = true });

        _db.Services.AddRange(
            new Service
            {
                Id = 1,
                HospitalId = 1,
                DepartmentId = 1,
                SpecialtyId = 1,
                ServiceCode = "SVC-NOI-01",
                Name = "Khám Nội tổng quát",
                ServiceType = "CONSULTATION",
                IsActive = true,
            },
            new Service
            {
                Id = 2,
                HospitalId = 1,
                DepartmentId = 2,
                SpecialtyId = 2,
                ServiceCode = "SVC-TIM-01",
                Name = "Khám Tim mạch",
                ServiceType = "CONSULTATION",
                IsActive = true,
            },
            new Service
            {
                Id = 5,
                HospitalId = 1,
                DepartmentId = 1,
                SpecialtyId = 1,
                ServiceCode = "DIA-CBC",
                Name = "Công thức máu",
                ServiceType = "DIAGNOSTIC",
                RequiresResultVerification = true,
                IsActive = true,
            },
            new Service
            {
                Id = 6,
                HospitalId = 1,
                DepartmentId = 1,
                SpecialtyId = 1,
                ServiceCode = "DIA-US",
                Name = "Siêu âm bụng",
                ServiceType = "DIAGNOSTIC",
                RequiresResultVerification = true,
                IsActive = true,
            },
            new Service
            {
                Id = 7,
                HospitalId = 1,
                DepartmentId = 2,
                SpecialtyId = 2,
                ServiceCode = "DIA-ECG",
                Name = "Điện tâm đồ",
                ServiceType = "DIAGNOSTIC",
                RequiresResultVerification = true,
                IsActive = true,
            });

        _db.ServicePrices.AddRange(
            new ServicePrice
            {
                Id = 5,
                ServiceId = 5,
                PriceAmount = 90000m,
                EffectiveFrom = new DateTime(2026, 1, 1),
            },
            new ServicePrice
            {
                Id = 6,
                ServiceId = 6,
                PriceAmount = 180000m,
                EffectiveFrom = new DateTime(2026, 1, 1),
            });

        _db.DoctorServiceAuthorizations.AddRange(
            new DoctorServiceAuthorization { DoctorId = 1, ServiceId = 1, IsActive = true },
            new DoctorServiceAuthorization { DoctorId = 1, ServiceId = 5, IsActive = true },
            new DoctorServiceAuthorization { DoctorId = 1, ServiceId = 6, IsActive = true },
            new DoctorServiceAuthorization { DoctorId = 2, ServiceId = 7, IsActive = true });

        _db.HospitalRooms.AddRange(
            new HospitalRoom
            {
                Id = 1,
                HospitalId = 1,
                DepartmentId = 1,
                RoomCode = "RM-101",
                Name = "Phòng khám Nội 101",
                RoomType = "EXAMINATION",
                Location = "Tầng 1 - Khu A",
                Capacity = 20,
                Status = "ACTIVE",
            },
            new HospitalRoom
            {
                Id = 2,
                HospitalId = 1,
                DepartmentId = 2,
                RoomCode = "RM-102",
                Name = "Phòng khám Tim mạch 102",
                RoomType = "EXAMINATION",
                Location = "Tầng 1 - Khu B",
                Capacity = 20,
                Status = "ACTIVE",
            },
            new HospitalRoom
            {
                Id = 3,
                HospitalId = 2,
                DepartmentId = 3,
                RoomCode = "RM-201",
                Name = "Phòng khám Nội Hà Nội 201",
                RoomType = "EXAMINATION",
                Location = "Tầng 2",
                Capacity = 20,
                Status = "ACTIVE",
            },
            new HospitalRoom
            {
                Id = 5,
                HospitalId = 1,
                DepartmentId = 1,
                RoomCode = "RM-205",
                Name = "Phòng Xét nghiệm 205",
                RoomType = "DIAGNOSTIC",
                Location = "Tầng 2 - Khu CLS",
                Capacity = 20,
                Status = "ACTIVE",
            },
            new HospitalRoom
            {
                Id = 6,
                HospitalId = 1,
                DepartmentId = 1,
                RoomCode = "RM-206",
                Name = "Phòng Siêu âm 206",
                RoomType = "DIAGNOSTIC",
                Location = "Tầng 2 - Khu CLS",
                Capacity = 20,
                Status = "ACTIVE",
            });

        // Users: 3 = Doctor 1 (Hospital 1), 4 = Doctor 2 (Hospital 1), 5 = Doctor 3 (Hospital 2), 6 = Reception
        _db.Users.AddRange(
            new User { Id = 3, Email = "doctor01@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 4, Email = "doctor02@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 5, Email = "doctor03@medflow.test", AccountStatus = AccountStatus.ACTIVE },
            new User { Id = 6, Email = "reception@medflow.test", AccountStatus = AccountStatus.ACTIVE });

        _db.UserRoles.AddRange(
            new UserRole { UserId = 3, RoleId = 3 },
            new UserRole { UserId = 4, RoleId = 3 },
            new UserRole { UserId = 5, RoleId = 3 },
            new UserRole { UserId = 6, RoleId = 2 });

        _db.Staff.AddRange(
            new Staff
            {
                Id = 1,
                UserId = 3,
                HospitalId = 1,
                DepartmentId = 1,
                StaffCode = "STF-001",
                FullName = "Nguyễn Văn Minh",
                Status = "ACTIVE",
            },
            new Staff
            {
                Id = 2,
                UserId = 4,
                HospitalId = 1,
                DepartmentId = 2,
                StaffCode = "STF-002",
                FullName = "Trần Thị Lan",
                Status = "ACTIVE",
            },
            new Staff
            {
                Id = 3,
                UserId = 5,
                HospitalId = 2,
                DepartmentId = 3,
                StaffCode = "STF-003",
                FullName = "Lê Hoàng Nam",
                Status = "ACTIVE",
            });

        _db.Doctors.AddRange(
            new Doctor
            {
                Id = 1,
                StaffId = 1,
                HospitalId = 1,
                SpecialtyId = 1,
                DoctorCode = "DOC-001",
                ProfessionalTitle = "BS.CKI",
                IsActive = true,
            },
            new Doctor
            {
                Id = 2,
                StaffId = 2,
                HospitalId = 1,
                SpecialtyId = 2,
                DoctorCode = "DOC-002",
                ProfessionalTitle = "ThS.BS",
                IsActive = true,
            },
            new Doctor
            {
                Id = 3,
                StaffId = 3,
                HospitalId = 2,
                SpecialtyId = 1,
                DoctorCode = "DOC-003",
                ProfessionalTitle = "BS",
                IsActive = true,
            });

        _db.QueueServicePoints.AddRange(
            new QueueServicePoint
            {
                Id = 1,
                HospitalId = 1,
                RoomId = 1,
                DoctorId = 1,
                ServiceId = 1,
                PointType = "EXAMINATION_ROOM",
                Name = "Buồng khám Nội 101",
                IsActive = true,
            },
            new QueueServicePoint
            {
                Id = 2,
                HospitalId = 1,
                RoomId = 2,
                DoctorId = 2,
                ServiceId = 2,
                PointType = "EXAMINATION_ROOM",
                Name = "Buồng khám Tim mạch 102",
                IsActive = true,
            },
            new QueueServicePoint
            {
                Id = 3,
                HospitalId = 2,
                RoomId = 3,
                DoctorId = 3,
                ServiceId = 1,
                PointType = "EXAMINATION_ROOM",
                Name = "Buồng khám Nội HN 201",
                IsActive = true,
            },
            new QueueServicePoint
            {
                Id = 5,
                HospitalId = 1,
                RoomId = 5,
                ServiceId = 5,
                PointType = "DIAGNOSTIC",
                Name = "Điểm Xét nghiệm 205",
                IsActive = true,
            },
            new QueueServicePoint
            {
                Id = 6,
                HospitalId = 1,
                RoomId = 6,
                ServiceId = 6,
                PointType = "DIAGNOSTIC",
                Name = "Điểm Siêu âm 206",
                IsActive = true,
            });

        _db.Patients.AddRange(
            new Patient
            {
                Id = 1,
                PatientCode = "PAT-0001",
                FullName = "Phạm Thị Hoa",
                DateOfBirth = new DateOnly(1985, 5, 14),
                Gender = "FEMALE",
                Phone = "0901234567",
                Email = "hoa.pham@medflow.test",
                Address = "123 Nguyễn Thị Minh Khai, Quận 1, TP.HCM",
                BloodType = "O+",
                IdentificationHash = "secret-hash-001",
                Status = "ACTIVE",
            },
            new Patient
            {
                Id = 2,
                PatientCode = "PAT-0002",
                FullName = "Đặng Quốc Bảo",
                DateOfBirth = new DateOnly(1978, 8, 20),
                Gender = "MALE",
                Phone = "0902345678",
                Email = "bao.dang@medflow.test",
                Address = "456 Lê Lợi, Quận 1, TP.HCM",
                BloodType = "A+",
                IdentificationHash = "secret-hash-002",
                Status = "ACTIVE",
            });

        _db.ExaminationSlots.AddRange(
            new ExaminationSlot
            {
                Id = 1,
                ServiceId = 1,
                DoctorId = 1,
                RoomId = 1,
                StartsAt = new DateTime(2026, 10, 15, 10, 0, 0),
                EndsAt = new DateTime(2026, 10, 15, 10, 30, 0),
                Capacity = 1,
                Status = "BOOKED",
            },
            new ExaminationSlot
            {
                Id = 11,
                ServiceId = 1,
                DoctorId = 1,
                RoomId = 1,
                StartsAt = new DateTime(2026, 10, 22, 9, 0, 0),
                EndsAt = new DateTime(2026, 10, 22, 9, 30, 0),
                Capacity = 1,
                Status = "BOOKED",
            });

        _db.Appointments.AddRange(
            new Appointment
            {
                Id = 1,
                AppointmentCode = "APT-202610-0001",
                PatientId = 1,
                SlotId = 1,
                ReasonForVisit = "Đau thượng vị",
                Status = "COMPLETED",
            },
            new Appointment
            {
                Id = 11,
                AppointmentCode = "APT-202610-0011",
                PatientId = 1,
                SlotId = 11,
                ReasonForVisit = "Đau vùng thượng vị kéo dài 3 ngày",
                Status = "CHECKED_IN",
            });

        // Visit 1: Completed visit with finalized medical record, vitals JSONB, diagnosis, prescription
        // Visit 2: Doctor 2's visit in Hospital 1
        // Visit 11: CALLED visit owned by Doctor 1 ready to open workspace
        // Visit 21: Doctor 3's visit in Hospital 2
        _db.Visits.AddRange(
            new Visit
            {
                Id = 1,
                VisitCode = "VIS-202610-0001",
                PatientId = 1,
                AppointmentId = 1,
                AssignedDoctorId = 1,
                ExaminationRoomId = 1,
                ReasonForVisit = "Đau thượng vị",
                Status = "COMPLETED",
                CheckedInAt = new DateTime(2026, 10, 15, 9, 45, 0),
                CompletedAt = new DateTime(2026, 10, 15, 11, 25, 0),
                CompletedByDoctorId = 1,
            },
            new Visit
            {
                Id = 2,
                VisitCode = "VIS-202610-0002",
                PatientId = 2,
                AssignedDoctorId = 2,
                ExaminationRoomId = 2,
                ReasonForVisit = "Đau ngực không điển hình",
                Status = "CALLED",
                CheckedInAt = new DateTime(2026, 10, 22, 8, 30, 0),
            },
            new Visit
            {
                Id = 11,
                VisitCode = "VIS-202610-0011",
                PatientId = 1,
                AppointmentId = 11,
                AssignedDoctorId = 1,
                ExaminationRoomId = 1,
                ReasonForVisit = "Đau vùng thượng vị kéo dài 3 ngày",
                Status = "CALLED",
                CheckedInAt = new DateTime(2026, 10, 22, 8, 45, 0),
            },
            new Visit
            {
                Id = 21,
                VisitCode = "VIS-202610-0021",
                PatientId = 2,
                AssignedDoctorId = 3,
                ExaminationRoomId = 3,
                ReasonForVisit = "Khám nội tổng quát",
                Status = "CALLED",
                CheckedInAt = new DateTime(2026, 10, 22, 8, 40, 0),
            });

        _db.QueueEntries.AddRange(
            new QueueEntry
            {
                Id = 1,
                VisitId = 1,
                AppointmentId = 1,
                ServicePointId = 1,
                CurrentServicePointId = 1,
                QueueDate = new DateOnly(2026, 10, 15),
                QueueNumber = 1,
                QueueType = "EXAMINATION",
                Status = "COMPLETED",
                PresenceStatus = "PRESENT",
                ArrivalClass = "ON_TIME",
                ManualPriority = "NORMAL",
                StateVersion = 4,
                ScheduledAt = new DateTime(2026, 10, 15, 10, 0, 0),
                ArrivedAt = new DateTime(2026, 10, 15, 9, 45, 0),
                ReadyAt = new DateTime(2026, 10, 15, 9, 46, 0),
                CalledAt = new DateTime(2026, 10, 15, 10, 2, 0),
                StartedAt = new DateTime(2026, 10, 15, 10, 5, 0),
                CompletedAt = new DateTime(2026, 10, 15, 11, 25, 0),
            },
            new QueueEntry
            {
                Id = 2,
                VisitId = 2,
                ServicePointId = 2,
                CurrentServicePointId = 2,
                QueueDate = new DateOnly(2026, 10, 22),
                QueueNumber = 1,
                QueueType = "EXAMINATION",
                Status = "CALLED",
                PresenceStatus = "PRESENT",
                ArrivalClass = "WALK_IN",
                ManualPriority = "NORMAL",
                StateVersion = 2,
                ArrivedAt = new DateTime(2026, 10, 22, 8, 30, 0),
                ReadyAt = new DateTime(2026, 10, 22, 8, 31, 0),
                CalledAt = new DateTime(2026, 10, 22, 8, 55, 0),
            },
            new QueueEntry
            {
                Id = 11,
                VisitId = 11,
                AppointmentId = 11,
                ServicePointId = 1,
                CurrentServicePointId = 1,
                QueueDate = new DateOnly(2026, 10, 22),
                QueueNumber = 1,
                QueueType = "EXAMINATION",
                Status = "CALLED",
                PresenceStatus = "PRESENT",
                ArrivalClass = "ON_TIME",
                ManualPriority = "NORMAL",
                StateVersion = 2,
                ScheduledAt = new DateTime(2026, 10, 22, 9, 0, 0),
                ArrivedAt = new DateTime(2026, 10, 22, 8, 45, 0),
                ReadyAt = new DateTime(2026, 10, 22, 8, 46, 0),
                CalledAt = new DateTime(2026, 10, 22, 8, 58, 0),
            },
            new QueueEntry
            {
                Id = 21,
                VisitId = 21,
                ServicePointId = 3,
                CurrentServicePointId = 3,
                QueueDate = new DateOnly(2026, 10, 22),
                QueueNumber = 1,
                QueueType = "EXAMINATION",
                Status = "CALLED",
                PresenceStatus = "PRESENT",
                ArrivalClass = "WALK_IN",
                ManualPriority = "NORMAL",
                StateVersion = 2,
                ArrivedAt = new DateTime(2026, 10, 22, 8, 40, 0),
                ReadyAt = new DateTime(2026, 10, 22, 8, 41, 0),
                CalledAt = new DateTime(2026, 10, 22, 8, 50, 0),
            });

        _db.MedicalRecords.Add(new MedicalRecord
        {
            Id = 1,
            VisitId = 1,
            CreatedByDoctorId = 1,
            Status = "FINALIZED",
            FinalizedAt = new DateTime(2026, 10, 15, 11, 25, 0),
        });

        _db.MedicalRecordRevisions.Add(new MedicalRecordRevision
        {
            Id = 1,
            MedicalRecordId = 1,
            RevisionNo = 1,
            PresentingComplaint = "Đau thượng vị",
            SymptomHistory = "Triệu chứng mức độ nhẹ, theo dõi ngoại trú (demo).",
            RelevantHistoryAllergies = "Chưa ghi nhận dị ứng (chỉ là dữ liệu demo, không phải xác nhận y tế).",
            VitalSigns = """{"bp_systolic_mmhg":119,"bp_diastolic_mmhg":76,"pulse_bpm":73,"temperature_c":36.6,"spo2_percent":98,"height_cm":161,"weight_kg":56}""",
            ClinicalFindings = "Khám lâm sàng ổn định (nội dung mô phỏng).",
            ClinicalConclusion = "Viêm dạ dày mạn tính - kết quả minh họa.",
            EditedByDoctorId = 1,
            RecordedAt = new DateTime(2026, 10, 15, 11, 20, 0),
            IsConfirmed = true,
            AmendmentReason = null,
        });

        _db.Icd10Codes.AddRange(
            new Icd10Code
            {
                Id = 1,
                Code = "K29.5",
                NameVi = "Viêm dạ dày mạn tính",
                CatalogVersion = "DEMO-ICD10",
                NodeType = "LEAF",
                IsSelectable = true,
                NotPrimaryDx = false,
            },
            new Icd10Code
            {
                Id = 2,
                Code = "I10",
                NameVi = "Tăng huyết áp vô căn (nguyên phát)",
                CatalogVersion = "DEMO-ICD10",
                NodeType = "LEAF",
                IsSelectable = true,
                NotPrimaryDx = false,
            },
            new Icd10Code
            {
                Id = 3,
                Code = "K21.9",
                NameVi = "Bệnh trào ngược dạ dày - thực quản không có viêm thực quản",
                CatalogVersion = "DEMO-ICD10",
                NodeType = "LEAF",
                IsSelectable = true,
                NotPrimaryDx = false,
            },
            new Icd10Code
            {
                Id = 4,
                Code = "K76.0",
                NameVi = "Gan nhiễm mỡ, không phân loại nơi khác",
                CatalogVersion = "DEMO-ICD10",
                NodeType = "LEAF",
                IsSelectable = true,
                NotPrimaryDx = false,
            },
            new Icd10Code
            {
                Id = 5,
                Code = "K29",
                NameVi = "Viêm dạ dày và tá tràng (Nhóm chương mục)",
                CatalogVersion = "DEMO-ICD10",
                NodeType = "CATEGORY",
                IsSelectable = false,
                NotPrimaryDx = false,
            },
            new Icd10Code
            {
                Id = 6,
                Code = "B96.81",
                NameVi = "Helicobacter pylori là nguyên nhân gây bệnh phân loại ở chương khác",
                CatalogVersion = "DEMO-ICD10",
                NodeType = "LEAF",
                IsSelectable = true,
                NotPrimaryDx = true,
            });

        _db.VisitDiagnoses.Add(new VisitDiagnosis
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
        });

        _db.Prescriptions.Add(new Prescription
        {
            Id = 1,
            PrescriptionCode = "RX-202610-0001",
            VisitId = 1,
            PrescribingDoctorId = 1,
            FulfillmentChoice = "HOSPITAL_PHARMACY",
            Status = "DISPENSED",
            IssuedAt = new DateTime(2026, 10, 15, 11, 22, 0),
            CreatedAt = new DateTime(2026, 10, 15, 11, 20, 0),
        });

        _db.PrescriptionItems.AddRange(
            new PrescriptionItem { Id = 1, PrescriptionId = 1, MedicineId = 1, PrescribedQuantity = 14 },
            new PrescriptionItem { Id = 2, PrescriptionId = 1, MedicineId = 2, PrescribedQuantity = 20 });

        _db.SaveChanges();
    }

    // -------------------------------------------------------------------------
    // 1. Authorization & Access Control
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task GetAndOpenWorkspace_InvalidActorUserId_ThrowsUnauthorizedAccessException(long actorUserId)
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _service.GetWorkspaceAsync(actorUserId, "VIS-202610-0011"));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _service.OpenWorkspaceAsync(actorUserId, "VIS-202610-0011"));
    }

    [Fact]
    public async Task GetWorkspace_UnknownUser_ThrowsNotFoundException()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => _service.GetWorkspaceAsync(999, "VIS-202610-0011"));
    }

    [Fact]
    public async Task GetWorkspace_InactiveUser_ThrowsForbiddenException()
    {
        var user = await _db.Users.FirstAsync(u => u.Id == 3);
        user.AccountStatus = AccountStatus.INACTIVE;
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _service.GetWorkspaceAsync(3, "VIS-202610-0011"));
    }

    [Fact]
    public async Task GetWorkspace_LockedUser_ThrowsForbiddenException()
    {
        var user = await _db.Users.FirstAsync(u => u.Id == 3);
        user.LockedUntil = _timeProvider.GetUtcNow().UtcDateTime.AddMinutes(15);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _service.GetWorkspaceAsync(3, "VIS-202610-0011"));
    }

    [Fact]
    public async Task GetWorkspace_UserWithoutDoctorRole_ThrowsForbiddenException()
    {
        await Assert.ThrowsAsync<ForbiddenException>(
            () => _service.GetWorkspaceAsync(6, "VIS-202610-0011"));
    }

    [Fact]
    public async Task GetWorkspace_InactiveStaffOrDoctor_ThrowsForbiddenException()
    {
        var doctor = await _db.Doctors.FirstAsync(d => d.Id == 1);
        doctor.IsActive = false;
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _service.GetWorkspaceAsync(3, "VIS-202610-0011"));
    }

    [Fact]
    public async Task OpenWorkspace_MissingMedicalRecordWritePermission_ThrowsForbiddenException()
    {
        var writePerm = await _db.RolePermissions
            .FirstAsync(rp => rp.RoleId == 3 && rp.PermissionId == 1);
        _db.RolePermissions.Remove(writePerm);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _service.OpenWorkspaceAsync(3, "VIS-202610-0011"));
    }

    // -------------------------------------------------------------------------
    // 2. Ownership Isolation (Hospital & Doctor/ServicePoint)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetAndOpenWorkspace_CrossHospitalVisit_ThrowsForbiddenException()
    {
        await Assert.ThrowsAsync<ForbiddenException>(
            () => _service.GetWorkspaceAsync(3, "VIS-202610-0021"));

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _service.OpenWorkspaceAsync(3, "VIS-202610-0021"));
    }

    [Fact]
    public async Task GetAndOpenWorkspace_OtherDoctorVisitInSameHospital_ThrowsForbiddenException()
    {
        await Assert.ThrowsAsync<ForbiddenException>(
            () => _service.GetWorkspaceAsync(3, "VIS-202610-0002"));

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _service.OpenWorkspaceAsync(3, "VIS-202610-0002"));
    }

    // -------------------------------------------------------------------------
    // 3. Input Validation & NotFound
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("q-0")]
    [InlineData("VIS-202610-0001-EXTRA-LONG-IDENTIFIER-OVER-40-CHARS")]
    [InlineData("VIS<script>")]
    public async Task GetAndOpenWorkspace_InvalidIdentifier_ThrowsValidationException(string invalidId)
    {
        await Assert.ThrowsAsync<ValidationException>(
            () => _service.GetWorkspaceAsync(3, invalidId));

        await Assert.ThrowsAsync<ValidationException>(
            () => _service.OpenWorkspaceAsync(3, invalidId));
    }

    [Fact]
    public async Task OpenWorkspace_InvalidExpectedStateVersionOrNotes_ThrowsValidationException()
    {
        await Assert.ThrowsAsync<ValidationException>(
            () => _service.OpenWorkspaceAsync(3, "VIS-202610-0011", new OpenConsultationWorkspaceRequest(ExpectedStateVersion: 0)));

        await Assert.ThrowsAsync<ValidationException>(
            () => _service.OpenWorkspaceAsync(3, "VIS-202610-0011", new OpenConsultationWorkspaceRequest(Notes: new string('a', 301))));

        await Assert.ThrowsAsync<ValidationException>(
            () => _service.OpenWorkspaceAsync(3, "VIS-202610-0011", new OpenConsultationWorkspaceRequest(Notes: "ghi chú <script>")));
    }

    [Fact]
    public async Task GetAndOpenWorkspace_NonExistentVisitOrQueueEntry_ThrowsNotFoundException()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => _service.GetWorkspaceAsync(3, "VIS-202610-9999"));

        await Assert.ThrowsAsync<NotFoundException>(
            () => _service.OpenWorkspaceAsync(3, "q-999"));
    }

    // -------------------------------------------------------------------------
    // 4. OpenWorkspace State Transitions & Guards
    // -------------------------------------------------------------------------

    [Fact]
    public async Task OpenWorkspace_CalledVisitByVisitCode_TransitionsToInProgress_BootstrapsDraftMedicalRecordAndWritesAudit()
    {
        var result = await _service.OpenWorkspaceAsync(
            actorUserId: 3,
            visitIdOrCode: "VIS-202610-0011",
            request: new OpenConsultationWorkspaceRequest(ExpectedStateVersion: 2, Notes: "Bắt đầu khám ngoại trú"),
            ipAddress: "127.0.0.1");

        Assert.Equal("OPENED", result.ActionOutcome);
        Assert.Equal("IN_PROGRESS", result.WorkspaceState);
        Assert.True(result.CanEditClinical);
        Assert.Null(result.ReadOnlyReason);
        Assert.Equal("VIS-202610-0011", result.Visit.VisitCode);
        Assert.Equal("IN_PROGRESS", result.Visit.Status);
        Assert.NotNull(result.Queue);
        Assert.Equal("IN_PROGRESS", result.Queue!.Status);
        Assert.Equal(3, result.Queue.StateVersion);
        Assert.Equal("09:00", result.Queue.StartedAt);

        Assert.NotNull(result.ClinicalContext.MedicalRecordId);
        Assert.Equal("DRAFT", result.ClinicalContext.MedicalRecordStatus);
        Assert.Equal(1, result.ClinicalContext.CurrentRevisionNo);
        Assert.False(result.ClinicalContext.IsConfirmed);
        Assert.Equal("Đau vùng thượng vị kéo dài 3 ngày", result.ClinicalContext.PresentingComplaint);

        var updatedVisit = await _db.Visits.FirstAsync(v => v.Id == 11);
        var updatedEntry = await _db.QueueEntries.FirstAsync(q => q.Id == 11);
        Assert.Equal("IN_PROGRESS", updatedVisit.Status);
        Assert.Equal("IN_PROGRESS", updatedEntry.Status);
        Assert.Equal(3, updatedEntry.StateVersion);

        var audit = await _db.AuditLogs.SingleOrDefaultAsync(a =>
            a.Action == "CONSULTATION_WORKSPACE_OPENED" &&
            a.EntityId == "VIS-202610-0011");
        Assert.NotNull(audit);
        Assert.Equal(3, audit!.ActorUserId);
        Assert.Equal(1, audit.HospitalId);
        Assert.Equal("Bắt đầu khám ngoại trú", audit.Reason);
    }

    [Fact]
    public async Task OpenWorkspace_CalledVisitByQueueEntryId_TransitionsToInProgressAndbecomesIdempotentOnSecondCall()
    {
        var firstOpen = await _service.OpenWorkspaceAsync(3, "q-011");
        Assert.Equal("OPENED", firstOpen.ActionOutcome);
        Assert.Equal(3, firstOpen.Queue!.StateVersion);

        var secondOpen = await _service.OpenWorkspaceAsync(3, "11", new OpenConsultationWorkspaceRequest(ExpectedStateVersion: 3));
        Assert.Equal("ALREADY_IN_PROGRESS", secondOpen.ActionOutcome);
        Assert.Equal("IN_PROGRESS", secondOpen.WorkspaceState);
        Assert.True(secondOpen.CanEditClinical);
        Assert.Equal(3, secondOpen.Queue!.StateVersion);

        var auditCount = await _db.AuditLogs.CountAsync(a => a.Action == "CONSULTATION_WORKSPACE_OPENED");
        Assert.Equal(1, auditCount);
    }

    [Theory]
    [InlineData("WAITING")]
    [InlineData("READY")]
    [InlineData("SKIPPED")]
    public async Task OpenWorkspace_WhenQueueEntryNotCalled_ThrowsConflictException(string queueStatus)
    {
        var entry = await _db.QueueEntries.FirstAsync(q => q.Id == 11);
        entry.Status = queueStatus;
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(
            () => _service.OpenWorkspaceAsync(3, "VIS-202610-0011"));
    }

    [Fact]
    public async Task OpenWorkspace_WhenAnotherEntryAlreadyInProgressInSameServicePoint_ThrowsConflictException()
    {
        _db.Visits.Add(new Visit
        {
            Id = 12,
            VisitCode = "VIS-202610-0012",
            PatientId = 2,
            AssignedDoctorId = 1,
            ExaminationRoomId = 1,
            Status = "IN_PROGRESS",
            CheckedInAt = new DateTime(2026, 10, 22, 8, 20, 0),
        });
        _db.QueueEntries.Add(new QueueEntry
        {
            Id = 12,
            VisitId = 12,
            ServicePointId = 1,
            CurrentServicePointId = 1,
            QueueDate = new DateOnly(2026, 10, 22),
            QueueNumber = 2,
            QueueType = "EXAMINATION",
            Status = "IN_PROGRESS",
            PresenceStatus = "PRESENT",
            StartedAt = new DateTime(2026, 10, 22, 8, 50, 0),
        });
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(
            () => _service.OpenWorkspaceAsync(3, "VIS-202610-0011"));
    }

    [Fact]
    public async Task OpenWorkspace_ReturnConsultationWithPendingMandatoryDiagnostic_ThrowsConflictException()
    {
        var entry = await _db.QueueEntries.FirstAsync(q => q.Id == 11);
        entry.QueueType = "RETURN_CONSULTATION";

        _db.QueueEntries.Add(new QueueEntry
        {
            Id = 110,
            VisitId = 11,
            QueueDate = new DateOnly(2026, 10, 22),
            QueueNumber = 5,
            QueueType = "DIAGNOSTIC",
            Status = "IN_PROGRESS",
            PresenceStatus = "PRESENT",
        });
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(
            () => _service.OpenWorkspaceAsync(3, "VIS-202610-0011"));
    }

    [Theory]
    [InlineData("NOT_ARRIVED")]
    [InlineData("LEFT")]
    public async Task OpenWorkspace_WhenPatientNotPresent_ThrowsConflictException(string presenceStatus)
    {
        var entry = await _db.QueueEntries.FirstAsync(q => q.Id == 11);
        entry.PresenceStatus = presenceStatus;
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(
            () => _service.OpenWorkspaceAsync(3, "VIS-202610-0011"));
    }

    [Fact]
    public async Task OpenWorkspace_WhenRoomInMaintenanceOrServicePointInactive_ThrowsConflictException()
    {
        var room = await _db.HospitalRooms.FirstAsync(r => r.Id == 1);
        room.Status = "MAINTENANCE";
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(
            () => _service.OpenWorkspaceAsync(3, "VIS-202610-0011"));

        room.Status = "ACTIVE";
        var sp = await _db.QueueServicePoints.FirstAsync(s => s.Id == 1);
        sp.IsActive = false;
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(
            () => _service.OpenWorkspaceAsync(3, "VIS-202610-0011"));
    }

    [Theory]
    [InlineData("COMPLETED")]
    [InlineData("CANCELLED")]
    [InlineData("NO_SHOW")]
    public async Task OpenWorkspace_TerminalVisitStatus_ThrowsConflictException(string terminalStatus)
    {
        var visit = await _db.Visits.FirstAsync(v => v.Id == 11);
        visit.Status = terminalStatus;
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(
            () => _service.OpenWorkspaceAsync(3, "VIS-202610-0011"));
    }

    [Fact]
    public async Task OpenWorkspace_StateVersionMismatch_ThrowsConflictException()
    {
        await Assert.ThrowsAsync<ConflictException>(
            () => _service.OpenWorkspaceAsync(
                3,
                "VIS-202610-0011",
                new OpenConsultationWorkspaceRequest(ExpectedStateVersion: 99)));
    }

    // -------------------------------------------------------------------------
    // 5. GetWorkspace Clinical Context Projection & AllowedActions
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetWorkspace_SeededCompletedVisit_ReturnsVitalSignsDiagnosesPrescriptionsAndReadOnlyActions()
    {
        var result = await _service.GetWorkspaceAsync(3, "VIS-202610-0001");

        Assert.Equal("VIEWED", result.ActionOutcome);
        Assert.Equal("COMPLETED", result.WorkspaceState);
        Assert.False(result.CanEditClinical);
        Assert.NotNull(result.ReadOnlyReason);

        Assert.Equal("VIS-202610-0001", result.Visit.VisitCode);
        Assert.Equal("PAT-0001", result.Patient.PatientCode);
        Assert.Equal("Phạm Thị Hoa", result.Patient.FullName);

        var vitals = result.ClinicalContext.VitalSigns;
        Assert.Equal(119, vitals.BpSystolicMmhg);
        Assert.Equal(76, vitals.BpDiastolicMmhg);
        Assert.Equal("119/76 mmHg", vitals.BloodPressureLabel);
        Assert.Equal(73, vitals.PulseBpm);
        Assert.Equal(36.6m, vitals.TemperatureC);
        Assert.Equal(98, vitals.Spo2Percent);
        Assert.Equal(161m, vitals.HeightCm);
        Assert.Equal(56m, vitals.WeightKg);
        Assert.Equal(21.6m, vitals.Bmi);

        var diagnosis = Assert.Single(result.ClinicalContext.Diagnoses);
        Assert.Equal("K29.5", diagnosis.Code);
        Assert.Equal("Viêm dạ dày mạn tính", diagnosis.Name);
        Assert.True(diagnosis.IsPrimary);
        Assert.Equal("CONFIRMED", diagnosis.Status);

        Assert.Equal(1, result.ClinicalContext.PrescriptionSummary.TotalPrescriptions);
        Assert.Equal("RX-202610-0001", result.ClinicalContext.PrescriptionSummary.LatestPrescriptionCode);
        Assert.Equal(2, result.ClinicalContext.PrescriptionSummary.ItemCount);

        var historyAction = Assert.Single(result.AllowedActions, a => a.Id == "view-medical-history");
        Assert.True(historyAction.Enabled);

        var editActions = result.AllowedActions.Where(a => a.Id != "view-medical-history");
        Assert.All(editActions, a =>
        {
            Assert.False(a.Enabled);
            Assert.False(string.IsNullOrWhiteSpace(a.DisabledReason));
        });
    }

    // -------------------------------------------------------------------------
    // 6. RecordClinicalExamination (UC33 / #110)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RecordClinicalExamination_InProgressVisit_AppendsRevisionComputesVitalsUpdatesAllergiesAndWritesAudit()
    {
        var opened = await _service.OpenWorkspaceAsync(3, "VIS-202610-0011");
        Assert.Equal("OPENED", opened.ActionOutcome);
        Assert.Equal(1, opened.ClinicalContext.CurrentRevisionNo);

        var recordId = opened.ClinicalContext.MedicalRecordId!.Value;

        var result = await _service.RecordClinicalExaminationAsync(
            actorUserId: 3,
            visitIdOrCode: "VIS-202610-0011",
            request: new RecordClinicalExaminationRequest(
                PresentingComplaint: "Đau vùng thượng vị sau ăn cay",
                SymptomHistory: "Đau âm ỉ vùng thượng vị sau ăn 3 ngày nay, kèm ợ chua",
                RelevantHistoryAllergies: "Dị ứng hải sản và Ibuprofen",
                VitalSigns: new RecordClinicalVitalSignsRequest(
                    BpSystolicMmhg: 120,
                    BpDiastolicMmhg: 80,
                    PulseBpm: 76,
                    TemperatureC: 36.8m,
                    Spo2Percent: 98,
                    HeightCm: 165m,
                    WeightKg: 60m),
                ClinicalFindings: "Bụng mềm, ấn đau vùng thượng vị, gan lách không sờ chạm",
                ClinicalConclusion: "Theo dõi viêm dạ dày - tá tràng cấp",
                AmendmentReason: "Ghi nhận khám lâm sàng lần đầu",
                IsConfirmed: true,
                ExpectedStateVersion: 3,
                ExpectedRevisionNo: 1),
            ipAddress: "127.0.0.1");

        Assert.Equal("CLINICAL_EXAMINATION_RECORDED", result.ActionOutcome);
        Assert.Equal("IN_PROGRESS", result.WorkspaceState);
        Assert.True(result.CanEditClinical);
        Assert.Null(result.ReadOnlyReason);

        Assert.Equal(recordId, result.ClinicalContext.MedicalRecordId);
        Assert.Equal("DRAFT", result.ClinicalContext.MedicalRecordStatus);
        Assert.Equal(2, result.ClinicalContext.CurrentRevisionNo);
        Assert.True(result.ClinicalContext.IsConfirmed);
        Assert.Equal("Đau vùng thượng vị sau ăn cay", result.ClinicalContext.PresentingComplaint);
        Assert.Equal("Đau âm ỉ vùng thượng vị sau ăn 3 ngày nay, kèm ợ chua", result.ClinicalContext.SymptomHistory);
        Assert.Equal("Dị ứng hải sản và Ibuprofen", result.ClinicalContext.RelevantHistoryAllergies);
        Assert.Equal("Dị ứng hải sản và Ibuprofen", result.Patient.AllergiesSummary);
        Assert.Equal("Bụng mềm, ấn đau vùng thượng vị, gan lách không sờ chạm", result.ClinicalContext.ClinicalFindings);
        Assert.Equal("Theo dõi viêm dạ dày - tá tràng cấp", result.ClinicalContext.ClinicalConclusion);
        Assert.Equal("Ghi nhận khám lâm sàng lần đầu", result.ClinicalContext.AmendmentReason);

        var vitals = result.ClinicalContext.VitalSigns;
        Assert.Equal(120, vitals.BpSystolicMmhg);
        Assert.Equal(80, vitals.BpDiastolicMmhg);
        Assert.Equal("120/80 mmHg", vitals.BloodPressureLabel);
        Assert.Equal(76, vitals.PulseBpm);
        Assert.Equal(36.8m, vitals.TemperatureC);
        Assert.Equal(98, vitals.Spo2Percent);
        Assert.Equal(165m, vitals.HeightCm);
        Assert.Equal(60m, vitals.WeightKg);
        Assert.Equal(22.0m, vitals.Bmi);

        var revisions = await _db.MedicalRecordRevisions
            .Where(r => r.MedicalRecordId == recordId)
            .OrderBy(r => r.RevisionNo)
            .ToListAsync();
        Assert.Equal(2, revisions.Count);
        Assert.Equal(1, revisions[0].RevisionNo);
        Assert.Equal(2, revisions[1].RevisionNo);
        Assert.True(revisions[1].IsConfirmed);

        var audit = await _db.AuditLogs.SingleOrDefaultAsync(a =>
            a.Action == "CLINICAL_EXAMINATION_RECORDED" &&
            a.EntityId == recordId.ToString());
        Assert.NotNull(audit);
        Assert.Equal(3, audit!.ActorUserId);
        Assert.Equal(1, audit.HospitalId);
        Assert.Equal("MEDICAL_RECORD", audit.EntityType);
        Assert.Equal("Ghi nhận khám lâm sàng lần đầu", audit.Reason);
    }

    [Fact]
    public async Task RecordClinicalExamination_SubsequentPartialUpdate_PreservesPriorFieldsAndIncrementsRevisionNo()
    {
        await _service.OpenWorkspaceAsync(3, "VIS-202610-0011");

        var firstSave = await _service.RecordClinicalExaminationAsync(
            3,
            "VIS-202610-0011",
            new RecordClinicalExaminationRequest(
                PresentingComplaint: "Đau thượng vị",
                SymptomHistory: "Đau âm ỉ 3 ngày",
                RelevantHistoryAllergies: "Dị ứng Penicillin",
                BpSystolicMmhg: 120,
                BpDiastolicMmhg: 80,
                PulseBpm: 72,
                HeightCm: 170m,
                WeightKg: 68m,
                ClinicalFindings: "Bụng mềm",
                ClinicalConclusion: "Theo dõi viêm dạ dày"));

        Assert.Equal(2, firstSave.ClinicalContext.CurrentRevisionNo);

        var secondSave = await _service.RecordClinicalExaminationAsync(
            3,
            "q-011",
            new RecordClinicalExaminationRequest(
                TemperatureC: 37.2m,
                Spo2Percent: 99,
                ClinicalConclusion: "Viêm dạ dày cấp, chỉ định nội soi",
                AmendmentReason: "Bổ sung nhiệt độ, SpO2 và kết luận",
                ExpectedRevisionNo: 2));

        Assert.Equal("CLINICAL_EXAMINATION_RECORDED", secondSave.ActionOutcome);
        Assert.Equal(3, secondSave.ClinicalContext.CurrentRevisionNo);
        Assert.Equal("Đau thượng vị", secondSave.ClinicalContext.PresentingComplaint);
        Assert.Equal("Đau âm ỉ 3 ngày", secondSave.ClinicalContext.SymptomHistory);
        Assert.Equal("Dị ứng Penicillin", secondSave.ClinicalContext.RelevantHistoryAllergies);
        Assert.Equal("Bụng mềm", secondSave.ClinicalContext.ClinicalFindings);
        Assert.Equal("Viêm dạ dày cấp, chỉ định nội soi", secondSave.ClinicalContext.ClinicalConclusion);
        Assert.Equal("Bổ sung nhiệt độ, SpO2 và kết luận", secondSave.ClinicalContext.AmendmentReason);

        var vitals = secondSave.ClinicalContext.VitalSigns;
        Assert.Equal(120, vitals.BpSystolicMmhg);
        Assert.Equal(80, vitals.BpDiastolicMmhg);
        Assert.Equal("120/80 mmHg", vitals.BloodPressureLabel);
        Assert.Equal(72, vitals.PulseBpm);
        Assert.Equal(37.2m, vitals.TemperatureC);
        Assert.Equal(99, vitals.Spo2Percent);
        Assert.Equal(170m, vitals.HeightCm);
        Assert.Equal(68m, vitals.WeightKg);
        Assert.Equal(23.5m, vitals.Bmi);
    }

    [Fact]
    public async Task RecordClinicalExamination_EmptyOrNullPayload_ThrowsValidationException()
    {
        await _service.OpenWorkspaceAsync(3, "VIS-202610-0011");

        await Assert.ThrowsAsync<ValidationException>(
            () => _service.RecordClinicalExaminationAsync(3, "VIS-202610-0011", null!));

        await Assert.ThrowsAsync<ValidationException>(
            () => _service.RecordClinicalExaminationAsync(
                3,
                "VIS-202610-0011",
                new RecordClinicalExaminationRequest(
                    PresentingComplaint: "   ",
                    AmendmentReason: "Chỉ có lý do cập nhật nhưng không có dữ liệu lâm sàng")));
    }

    [Theory]
    [InlineData(120, null)]
    [InlineData(null, 80)]
    [InlineData(35, 80)]
    [InlineData(305, 80)]
    [InlineData(120, 15)]
    [InlineData(120, 210)]
    [InlineData(100, 100)]
    [InlineData(90, 100)]
    public async Task RecordClinicalExamination_InvalidBloodPressure_ThrowsValidationException(
        int? systolic,
        int? diastolic)
    {
        await _service.OpenWorkspaceAsync(3, "VIS-202610-0011");

        await Assert.ThrowsAsync<ValidationException>(
            () => _service.RecordClinicalExaminationAsync(
                3,
                "VIS-202610-0011",
                new RecordClinicalExaminationRequest(
                    BpSystolicMmhg: systolic,
                    BpDiastolicMmhg: diastolic)));
    }

    [Fact]
    public async Task RecordClinicalExamination_OutOfRangeVitalsOrInvalidText_ThrowsValidationException()
    {
        await _service.OpenWorkspaceAsync(3, "VIS-202610-0011");

        await Assert.ThrowsAsync<ValidationException>(
            () => _service.RecordClinicalExaminationAsync(
                3,
                "VIS-202610-0011",
                new RecordClinicalExaminationRequest(PulseBpm: 15)));

        await Assert.ThrowsAsync<ValidationException>(
            () => _service.RecordClinicalExaminationAsync(
                3,
                "VIS-202610-0011",
                new RecordClinicalExaminationRequest(TemperatureC: 46.0m)));

        await Assert.ThrowsAsync<ValidationException>(
            () => _service.RecordClinicalExaminationAsync(
                3,
                "VIS-202610-0011",
                new RecordClinicalExaminationRequest(Spo2Percent: 45)));

        await Assert.ThrowsAsync<ValidationException>(
            () => _service.RecordClinicalExaminationAsync(
                3,
                "VIS-202610-0011",
                new RecordClinicalExaminationRequest(HeightCm: 20m)));

        await Assert.ThrowsAsync<ValidationException>(
            () => _service.RecordClinicalExaminationAsync(
                3,
                "VIS-202610-0011",
                new RecordClinicalExaminationRequest(WeightKg: 0.5m)));

        await Assert.ThrowsAsync<ValidationException>(
            () => _service.RecordClinicalExaminationAsync(
                3,
                "VIS-202610-0011",
                new RecordClinicalExaminationRequest(ClinicalFindings: "Bụng mềm <script>alert(1)</script>")));

        await Assert.ThrowsAsync<ValidationException>(
            () => _service.RecordClinicalExaminationAsync(
                3,
                "VIS-202610-0011",
                new RecordClinicalExaminationRequest(PresentingComplaint: new string('a', 1001))));

        await Assert.ThrowsAsync<ValidationException>(
            () => _service.RecordClinicalExaminationAsync(
                3,
                "VIS-202610-0011",
                new RecordClinicalExaminationRequest(
                    ClinicalFindings: "Bụng mềm",
                    ExpectedStateVersion: 0)));

        await Assert.ThrowsAsync<ValidationException>(
            () => _service.RecordClinicalExaminationAsync(
                3,
                "VIS-202610-0011",
                new RecordClinicalExaminationRequest(
                    ClinicalFindings: "Bụng mềm",
                    ExpectedRevisionNo: 0)));
    }

    [Fact]
    public async Task RecordClinicalExamination_UnauthorizedOrForbiddenDoctor_ThrowsAndDoesNotPersistRevisionOrAudit()
    {
        await _service.OpenWorkspaceAsync(3, "VIS-202610-0011");
        var baselineRevisions = await _db.MedicalRecordRevisions.CountAsync();
        var baselineAudits = await _db.AuditLogs.CountAsync(a => a.Action == "CLINICAL_EXAMINATION_RECORDED");

        var validRequest = new RecordClinicalExaminationRequest(ClinicalFindings: "Khám bụng mềm");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _service.RecordClinicalExaminationAsync(0, "VIS-202610-0011", validRequest));

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _service.RecordClinicalExaminationAsync(6, "VIS-202610-0011", validRequest));

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _service.RecordClinicalExaminationAsync(3, "VIS-202610-0021", validRequest));

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _service.RecordClinicalExaminationAsync(3, "VIS-202610-0002", validRequest));

        var writePerm = await _db.RolePermissions
            .FirstAsync(rp => rp.RoleId == 3 && rp.PermissionId == 1);
        _db.RolePermissions.Remove(writePerm);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _service.RecordClinicalExaminationAsync(3, "VIS-202610-0011", validRequest));

        Assert.Equal(baselineRevisions, await _db.MedicalRecordRevisions.CountAsync());
        Assert.Equal(baselineAudits, await _db.AuditLogs.CountAsync(a => a.Action == "CLINICAL_EXAMINATION_RECORDED"));
    }

    [Theory]
    [InlineData("CALLED")]
    [InlineData("WAITING")]
    [InlineData("READY")]
    [InlineData("SKIPPED")]
    public async Task RecordClinicalExamination_WhenVisitNotInProgress_ThrowsConflictException(string queueStatus)
    {
        var entry = await _db.QueueEntries.FirstAsync(q => q.Id == 11);
        entry.Status = queueStatus;
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(
            () => _service.RecordClinicalExaminationAsync(
                3,
                "VIS-202610-0011",
                new RecordClinicalExaminationRequest(ClinicalFindings: "Bụng mềm")));
    }

    [Theory]
    [InlineData("COMPLETED")]
    [InlineData("CANCELLED")]
    [InlineData("NO_SHOW")]
    [InlineData("AWAITING_RESULTS")]
    public async Task RecordClinicalExamination_NonEditableVisitStatus_ThrowsConflictException(string visitStatus)
    {
        await _service.OpenWorkspaceAsync(3, "VIS-202610-0011");

        var visit = await _db.Visits.FirstAsync(v => v.Id == 11);
        visit.Status = visitStatus;
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(
            () => _service.RecordClinicalExaminationAsync(
                3,
                "VIS-202610-0011",
                new RecordClinicalExaminationRequest(ClinicalFindings: "Bụng mềm")));
    }

    [Fact]
    public async Task RecordClinicalExamination_FinalizedRecordOrConcurrencyMismatch_ThrowsConflictException()
    {
        var opened = await _service.OpenWorkspaceAsync(3, "VIS-202610-0011");
        var recordId = opened.ClinicalContext.MedicalRecordId!.Value;

        await Assert.ThrowsAsync<ConflictException>(
            () => _service.RecordClinicalExaminationAsync(
                3,
                "VIS-202610-0011",
                new RecordClinicalExaminationRequest(
                    ClinicalFindings: "Bụng mềm",
                    ExpectedStateVersion: 99)));

        await Assert.ThrowsAsync<ConflictException>(
            () => _service.RecordClinicalExaminationAsync(
                3,
                "VIS-202610-0011",
                new RecordClinicalExaminationRequest(
                    ClinicalFindings: "Bụng mềm",
                    ExpectedRevisionNo: 99)));

        var record = await _db.MedicalRecords.FirstAsync(m => m.Id == recordId);
        record.Status = "FINALIZED";
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(
            () => _service.RecordClinicalExaminationAsync(
                3,
                "VIS-202610-0011",
                new RecordClinicalExaminationRequest(ClinicalFindings: "Bụng mềm")));
    }

    // -------------------------------------------------------------------------
    // 7. Create Diagnostic Order & Catalog (UC34 / #120)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CreateDiagnosticOrder_SingleOrderByServiceId_CreatesOrderAndQueueEntryAndUpdatesSummaryAndAudit()
    {
        var opened = await _service.OpenWorkspaceAsync(3, "VIS-202610-0011");
        Assert.Equal("IN_PROGRESS", opened.WorkspaceState);
        Assert.Equal(3, opened.Queue!.StateVersion);

        var result = await _service.CreateDiagnosticOrderAsync(
            3,
            "VIS-202610-0011",
            new CreateDiagnosticOrderRequest(
                ServiceId: 5,
                Instruction: "Nhịn ăn sáng trước khi lấy mẫu máu",
                Priority: "URGENT",
                RequiredForConclusion: true,
                ExpectedStateVersion: 3,
                ClinicalNote: "Nghi ngờ thiếu máu kèm viêm dạ dày"),
            ipAddress: "127.0.0.1");

        Assert.Equal("DIAGNOSTIC_ORDER_CREATED", result.ActionOutcome);
        Assert.Equal("IN_PROGRESS", result.WorkspaceState);
        Assert.True(result.CanEditClinical);
        Assert.Equal(4, result.Queue!.StateVersion);

        Assert.NotNull(result.CreatedDiagnosticOrders);
        var createdDto = Assert.Single(result.CreatedDiagnosticOrders!);
        Assert.Equal("DIA-202610-0001", createdDto.OrderCode);
        Assert.Equal(11, createdDto.VisitId);
        Assert.Equal("VIS-202610-0011", createdDto.VisitCode);
        Assert.Equal(5, createdDto.ServiceId);
        Assert.Equal("DIA-CBC", createdDto.ServiceCode);
        Assert.Equal("Công thức máu", createdDto.ServiceName);
        Assert.Equal("LABORATORY", createdDto.Category);
        Assert.Equal(90000m, createdDto.UnitPrice);
        Assert.Equal("URGENT", createdDto.Priority);
        Assert.Equal("ORDERED", createdDto.Status);
        Assert.True(createdDto.RequiredForConclusion);
        Assert.Equal("Nhịn ăn sáng trước khi lấy mẫu máu", createdDto.Instruction);
        Assert.Equal(1, createdDto.QueueNumber);
        Assert.Equal("C001", createdDto.TicketNo);
        Assert.Equal("WAITING", createdDto.QueueStatus);
        Assert.Equal(5, createdDto.ServicePointId);
        Assert.Equal("RM-205", createdDto.RoomCode);
        Assert.False(createdDto.IsResultFinalized);

        var summary = result.ClinicalContext.DiagnosticSummary;
        Assert.Equal(1, summary.TotalQueueEntries);
        Assert.Equal(1, summary.OpenQueueEntries);
        Assert.Equal(0, summary.CompletedQueueEntries);
        Assert.False(summary.AllMandatoryResultsReady);

        Assert.NotNull(result.ClinicalContext.DiagnosticOrders);
        Assert.Single(result.ClinicalContext.DiagnosticOrders!);

        Assert.True(result.AllowedActions.Single(a => a.Id == "create-diagnostic-order").Enabled);
        Assert.False(result.AllowedActions.Single(a => a.Id == "record-diagnosis").Enabled);
        Assert.False(result.AllowedActions.Single(a => a.Id == "create-prescription").Enabled);
        Assert.False(result.AllowedActions.Single(a => a.Id == "complete-visit").Enabled);

        var dbOrder = await _db.DiagnosticOrders.SingleAsync(o => o.VisitId == 11);
        Assert.Equal("DIA-202610-0001", dbOrder.OrderCode);
        Assert.Equal("URGENT", dbOrder.Priority);
        Assert.Equal("ORDERED", dbOrder.Status);
        Assert.True(dbOrder.RequiredForConclusion);

        var dbDiagQueue = await _db.QueueEntries.SingleAsync(q => q.Id == dbOrder.QueueEntryId);
        Assert.Equal("DIAGNOSTIC", dbDiagQueue.QueueType);
        Assert.Equal("WAITING", dbDiagQueue.Status);
        Assert.Equal("NORMAL", dbDiagQueue.ManualPriority);
        Assert.Equal(1, dbDiagQueue.QueueNumber);
        Assert.Equal(5, dbDiagQueue.ServicePointId);

        var audit = await _db.AuditLogs.SingleAsync(a => a.Action == "DIAGNOSTIC_ORDER_CREATED");
        Assert.Equal("DIAGNOSTIC_ORDER", audit.EntityType);
        Assert.Equal(dbOrder.Id.ToString(), audit.EntityId);
        Assert.Equal("Nghi ngờ thiếu máu kèm viêm dạ dày", audit.Reason);
    }

    [Fact]
    public async Task CreateDiagnosticOrder_BatchItemsAndTransitionToAwaitingResults_TransitionsVisitAndCompletesClinicalQueueEntry()
    {
        await _service.OpenWorkspaceAsync(3, "VIS-202610-0011");

        var result = await _service.CreateDiagnosticOrderAsync(
            3,
            "VIS-202610-0011",
            new CreateDiagnosticOrderRequest(
                Items:
                [
                    new CreateDiagnosticOrderItemRequest(
                        ServiceCode: "dia-cbc",
                        Instruction: "Xét nghiệm tổng phân tích tế bào máu",
                        Priority: "NORMAL",
                        RequiredForConclusion: true),
                    new CreateDiagnosticOrderItemRequest(
                        ServiceId: 6,
                        Instruction: "Siêu âm ổ bụng tổng quát",
                        Priority: "STAT",
                        RequiredForConclusion: true),
                ],
                TransitionToAwaitingResults: true));

        Assert.Equal("DIAGNOSTIC_ORDER_CREATED", result.ActionOutcome);
        Assert.Equal("COMPLETED", result.WorkspaceState);
        Assert.NotNull(result.CreatedDiagnosticOrders);
        Assert.Equal(2, result.CreatedDiagnosticOrders!.Count);
        Assert.Equal("LABORATORY", result.CreatedDiagnosticOrders[0].Category);
        Assert.Equal("IMAGING", result.CreatedDiagnosticOrders[1].Category);
        Assert.Equal(180000m, result.CreatedDiagnosticOrders[1].UnitPrice);
        Assert.Equal("STAT", result.CreatedDiagnosticOrders[1].Priority);

        var visit = await _db.Visits.FirstAsync(v => v.Id == 11);
        Assert.Equal("AWAITING_RESULTS", visit.Status);

        var clinicalQueue = await _db.QueueEntries.FirstAsync(q => q.Id == 11);
        Assert.Equal("COMPLETED", clinicalQueue.Status);
        Assert.NotNull(clinicalQueue.CompletedAt);
        Assert.Equal(4, clinicalQueue.StateVersion);

        Assert.Equal(2, result.ClinicalContext.DiagnosticSummary.TotalQueueEntries);
        Assert.Equal(2, result.ClinicalContext.DiagnosticSummary.OpenQueueEntries);
        Assert.False(result.ClinicalContext.DiagnosticSummary.AllMandatoryResultsReady);
    }

    [Fact]
    public async Task CreateDiagnosticOrder_NonMandatoryOrder_KeepsAllMandatoryResultsReadyTrue()
    {
        await _service.OpenWorkspaceAsync(3, "VIS-202610-0011");

        var result = await _service.CreateDiagnosticOrderAsync(
            3,
            "VIS-202610-0011",
            new CreateDiagnosticOrderRequest(
                ServiceId: 5,
                Instruction: "Kiểm tra bổ sung không bắt buộc trước kết luận",
                Priority: "ROUTINE",
                RequiredForConclusion: false));

        Assert.Equal(1, result.ClinicalContext.DiagnosticSummary.TotalQueueEntries);
        Assert.Equal(1, result.ClinicalContext.DiagnosticSummary.OpenQueueEntries);
        Assert.True(result.ClinicalContext.DiagnosticSummary.AllMandatoryResultsReady);
        Assert.True(result.AllowedActions.Single(a => a.Id == "record-diagnosis").Enabled);
        Assert.True(result.AllowedActions.Single(a => a.Id == "create-prescription").Enabled);
        Assert.True(result.AllowedActions.Single(a => a.Id == "complete-visit").Enabled);
    }

    [Fact]
    public async Task CreateDiagnosticOrder_InvalidInputs_ThrowsValidationException()
    {
        await _service.OpenWorkspaceAsync(3, "VIS-202610-0011");

        await Assert.ThrowsAsync<ValidationException>(
            () => _service.CreateDiagnosticOrderAsync(3, "VIS-202610-0011", null!));

        await Assert.ThrowsAsync<ValidationException>(
            () => _service.CreateDiagnosticOrderAsync(
                3,
                "VIS-202610-0011",
                new CreateDiagnosticOrderRequest()));

        await Assert.ThrowsAsync<ValidationException>(
            () => _service.CreateDiagnosticOrderAsync(
                3,
                "VIS-202610-0011",
                new CreateDiagnosticOrderRequest(ServiceId: 0)));

        await Assert.ThrowsAsync<ValidationException>(
            () => _service.CreateDiagnosticOrderAsync(
                3,
                "VIS-202610-0011",
                new CreateDiagnosticOrderRequest(ServiceCode: "   ")));

        await Assert.ThrowsAsync<ValidationException>(
            () => _service.CreateDiagnosticOrderAsync(
                3,
                "VIS-202610-0011",
                new CreateDiagnosticOrderRequest(ServiceCode: "<script>")));

        // Non-DIAGNOSTIC service (ServiceId = 1 is CONSULTATION)
        await Assert.ThrowsAsync<ValidationException>(
            () => _service.CreateDiagnosticOrderAsync(
                3,
                "VIS-202610-0011",
                new CreateDiagnosticOrderRequest(ServiceId: 1)));

        await Assert.ThrowsAsync<ValidationException>(
            () => _service.CreateDiagnosticOrderAsync(
                3,
                "VIS-202610-0011",
                new CreateDiagnosticOrderRequest(ServiceId: 5, Priority: "SUPER_HIGH")));

        await Assert.ThrowsAsync<ValidationException>(
            () => _service.CreateDiagnosticOrderAsync(
                3,
                "VIS-202610-0011",
                new CreateDiagnosticOrderRequest(ServiceId: 5, Instruction: "<script>alert(1)</script>")));

        await Assert.ThrowsAsync<ValidationException>(
            () => _service.CreateDiagnosticOrderAsync(
                3,
                "VIS-202610-0011",
                new CreateDiagnosticOrderRequest(ServiceId: 5, Instruction: new string('A', 1001))));

        await Assert.ThrowsAsync<ValidationException>(
            () => _service.CreateDiagnosticOrderAsync(
                3,
                "VIS-202610-0011",
                new CreateDiagnosticOrderRequest(
                    Items:
                    [
                        new CreateDiagnosticOrderItemRequest(ServiceId: 5),
                        new CreateDiagnosticOrderItemRequest(ServiceCode: "DIA-CBC"),
                    ])));

        await Assert.ThrowsAsync<ValidationException>(
            () => _service.CreateDiagnosticOrderAsync(
                3,
                "VIS-202610-0011",
                new CreateDiagnosticOrderRequest(ServiceId: 5, ExpectedStateVersion: 0)));
    }

    [Fact]
    public async Task CreateDiagnosticOrder_UnauthorizedOrForbiddenDoctorOrService_ThrowsAndDoesNotPersist()
    {
        await _service.OpenWorkspaceAsync(3, "VIS-202610-0011");
        var validRequest = new CreateDiagnosticOrderRequest(ServiceId: 5);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _service.CreateDiagnosticOrderAsync(0, "VIS-202610-0011", validRequest));

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _service.CreateDiagnosticOrderAsync(6, "VIS-202610-0011", validRequest));

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _service.CreateDiagnosticOrderAsync(3, "VIS-202610-0021", validRequest));

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _service.CreateDiagnosticOrderAsync(3, "VIS-202610-0002", validRequest));

        // Unauthorized diagnostic service (ServiceId = 7 is only authorized for Doctor 2)
        await Assert.ThrowsAsync<ForbiddenException>(
            () => _service.CreateDiagnosticOrderAsync(
                3,
                "VIS-202610-0011",
                new CreateDiagnosticOrderRequest(ServiceId: 7)));

        // Remove DIAGNOSTIC_ORDER permission (PermissionId = 2)
        var diagPerm = await _db.RolePermissions
            .FirstAsync(rp => rp.RoleId == 3 && rp.PermissionId == 2);
        _db.RolePermissions.Remove(diagPerm);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _service.CreateDiagnosticOrderAsync(3, "VIS-202610-0011", validRequest));

        Assert.Equal(0, await _db.DiagnosticOrders.CountAsync());
        Assert.Equal(0, await _db.AuditLogs.CountAsync(a => a.Action == "DIAGNOSTIC_ORDER_CREATED"));
    }

    [Fact]
    public async Task CreateDiagnosticOrder_NotFoundAndConflictGuards_ThrowExpectedExceptions()
    {
        var opened = await _service.OpenWorkspaceAsync(3, "VIS-202610-0011");
        var recordId = opened.ClinicalContext.MedicalRecordId!.Value;

        // Unknown service -> 404
        await Assert.ThrowsAsync<NotFoundException>(
            () => _service.CreateDiagnosticOrderAsync(
                3,
                "VIS-202610-0011",
                new CreateDiagnosticOrderRequest(ServiceId: 999)));

        // ExpectedStateVersion mismatch -> 409
        await Assert.ThrowsAsync<ConflictException>(
            () => _service.CreateDiagnosticOrderAsync(
                3,
                "VIS-202610-0011",
                new CreateDiagnosticOrderRequest(ServiceId: 5, ExpectedStateVersion: 99)));

        // Inactive diagnostic service -> 409
        var svc6 = await _db.Services.FirstAsync(s => s.Id == 6);
        svc6.IsActive = false;
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(
            () => _service.CreateDiagnosticOrderAsync(
                3,
                "VIS-202610-0011",
                new CreateDiagnosticOrderRequest(ServiceId: 6)));

        svc6.IsActive = true;
        var sp6 = await _db.QueueServicePoints.FirstAsync(sp => sp.Id == 6);
        sp6.IsActive = false;
        await _db.SaveChangesAsync();

        // Inactive diagnostic service point -> 409
        await Assert.ThrowsAsync<ConflictException>(
            () => _service.CreateDiagnosticOrderAsync(
                3,
                "VIS-202610-0011",
                new CreateDiagnosticOrderRequest(ServiceId: 6)));

        sp6.IsActive = true;
        var room6 = await _db.HospitalRooms.FirstAsync(r => r.Id == 6);
        room6.Status = "MAINTENANCE";
        await _db.SaveChangesAsync();

        // Inactive diagnostic room -> 409
        await Assert.ThrowsAsync<ConflictException>(
            () => _service.CreateDiagnosticOrderAsync(
                3,
                "VIS-202610-0011",
                new CreateDiagnosticOrderRequest(ServiceId: 6)));

        room6.Status = "ACTIVE";
        await _db.SaveChangesAsync();

        // First order for Service 5 succeeds
        await _service.CreateDiagnosticOrderAsync(
            3,
            "VIS-202610-0011",
            new CreateDiagnosticOrderRequest(ServiceId: 5));

        // Duplicate active order for Service 5 in same visit -> 409
        await Assert.ThrowsAsync<ConflictException>(
            () => _service.CreateDiagnosticOrderAsync(
                3,
                "VIS-202610-0011",
                new CreateDiagnosticOrderRequest(ServiceId: 5)));

        // Finalized medical record -> 409
        var record = await _db.MedicalRecords.FirstAsync(m => m.Id == recordId);
        record.Status = "FINALIZED";
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(
            () => _service.CreateDiagnosticOrderAsync(
                3,
                "VIS-202610-0011",
                new CreateDiagnosticOrderRequest(ServiceId: 6)));
    }

    [Theory]
    [InlineData("CALLED")]
    [InlineData("WAITING")]
    [InlineData("COMPLETED")]
    [InlineData("CANCELLED")]
    [InlineData("AWAITING_RESULTS")]
    public async Task CreateDiagnosticOrder_WhenVisitOrQueueNotInProgress_ThrowsConflictException(string status)
    {
        var visit = await _db.Visits.FirstAsync(v => v.Id == 11);
        var entry = await _db.QueueEntries.FirstAsync(q => q.Id == 11);
        visit.Status = status;
        entry.Status = status == "AWAITING_RESULTS" ? "WAITING" : status;
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(
            () => _service.CreateDiagnosticOrderAsync(
                3,
                "VIS-202610-0011",
                new CreateDiagnosticOrderRequest(ServiceId: 5)));
    }

    [Fact]
    public async Task GetDiagnosticOrderCatalog_ReturnsHospitalScopedDiagnosticServicesWithAuthorizationPriceCategoryAndActiveOrders()
    {
        await _service.OpenWorkspaceAsync(3, "VIS-202610-0011");
        await _service.CreateDiagnosticOrderAsync(
            3,
            "VIS-202610-0011",
            new CreateDiagnosticOrderRequest(ServiceId: 5, Priority: "URGENT"));

        var catalog = await _service.GetDiagnosticOrderCatalogAsync(3, "VIS-202610-0011");

        Assert.Equal(11, catalog.VisitId);
        Assert.Equal("VIS-202610-0011", catalog.VisitCode);
        Assert.Equal("IN_PROGRESS", catalog.WorkspaceState);
        Assert.True(catalog.CanCreateDiagnosticOrder);
        Assert.Null(catalog.DisabledReason);
        Assert.Equal(3, catalog.Services.Count);

        var cbc = catalog.Services.Single(s => s.ServiceCode == "DIA-CBC");
        Assert.Equal("LABORATORY", cbc.Category);
        Assert.Equal(90000m, cbc.UnitPrice);
        Assert.True(cbc.IsDoctorAuthorized);
        Assert.True(cbc.HasActiveOrderInVisit);
        Assert.Equal("RM-205", cbc.RoomCode);

        var us = catalog.Services.Single(s => s.ServiceCode == "DIA-US");
        Assert.Equal("IMAGING", us.Category);
        Assert.Equal(180000m, us.UnitPrice);
        Assert.True(us.IsDoctorAuthorized);
        Assert.False(us.HasActiveOrderInVisit);
        Assert.Equal("RM-206", us.RoomCode);

        var ecg = catalog.Services.Single(s => s.ServiceCode == "DIA-ECG");
        Assert.Equal("FUNCTIONAL", ecg.Category);
        Assert.False(ecg.IsDoctorAuthorized);
        Assert.False(ecg.HasActiveOrderInVisit);

        var existingOrder = Assert.Single(catalog.ExistingOrders);
        Assert.Equal("DIA-202610-0001", existingOrder.OrderCode);
        Assert.Equal("DIA-CBC", existingOrder.ServiceCode);
    }

    // 7. RecordDiagnosis & SearchIcd10Codes (UC36 / #112)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RecordDiagnosis_InProgressVisitWithFlatFields_PersistsDiagnosesAppendsRevisionAndWritesAudit()
    {
        var opened = await _service.OpenWorkspaceAsync(3, "VIS-202610-0011");
        Assert.Equal("OPENED", opened.ActionOutcome);
        Assert.Equal(1, opened.ClinicalContext.CurrentRevisionNo);

        var recordId = opened.ClinicalContext.MedicalRecordId!.Value;

        var result = await _service.RecordDiagnosisAsync(
            actorUserId: 3,
            visitIdOrCode: "VIS-202610-0011",
            request: new RecordDiagnosisRequest(
                PrimaryIcd10Code: "K29.5",
                PrimaryClinicalDiagnosis: "Viêm dạ dày mạn tính đợt cấp",
                SecondaryIcd10Codes: ["K76.0", "B96.81"],
                ClinicalConclusion: "Viêm dạ dày mạn tính đợt cấp có H. pylori (+) kèm gan nhiễm mỡ độ I",
                DifferentialNotes: "Phân biệt loét hành tá tràng",
                LifestyleInstructions: "Kiêng rượu bia, thức ăn chua cay, ăn đúng giờ",
                WarningSigns: "Đau bụng dữ dội, nôn ra máu hoặc đi ngoài phân đen",
                AmendmentReason: "Ghi nhận chẩn đoán xác định và kết luận",
                IsConfirmed: true,
                ExpectedStateVersion: 3,
                ExpectedRevisionNo: 1),
            ipAddress: "127.0.0.1");

        Assert.Equal("DIAGNOSIS_RECORDED", result.ActionOutcome);
        Assert.Equal("IN_PROGRESS", result.WorkspaceState);
        Assert.True(result.CanEditClinical);
        Assert.Equal(recordId, result.ClinicalContext.MedicalRecordId);
        Assert.Equal(2, result.ClinicalContext.CurrentRevisionNo);
        Assert.True(result.ClinicalContext.IsConfirmed);
        Assert.Equal(
            "Viêm dạ dày mạn tính đợt cấp có H. pylori (+) kèm gan nhiễm mỡ độ I",
            result.ClinicalContext.ClinicalConclusion);
        Assert.Equal("Ghi nhận chẩn đoán xác định và kết luận", result.ClinicalContext.AmendmentReason);

        Assert.Equal(3, result.ClinicalContext.Diagnoses.Count);
        var primaryDx = result.ClinicalContext.Diagnoses[0];
        Assert.True(primaryDx.IsPrimary);
        Assert.Equal(1L, primaryDx.Icd10CodeId);
        Assert.Equal("K29.5", primaryDx.Code);
        Assert.Equal("Viêm dạ dày mạn tính", primaryDx.Name);
        Assert.Equal("Viêm dạ dày mạn tính đợt cấp", primaryDx.ClinicalDiagnosis);
        Assert.Equal("CONFIRMED", primaryDx.Status);
        Assert.NotNull(primaryDx.ConfirmedAt);

        var secondaryCodes = result.ClinicalContext.Diagnoses
            .Where(d => !d.IsPrimary)
            .Select(d => d.Code)
            .ToList();
        Assert.Contains("K76.0", secondaryCodes);
        Assert.Contains("B96.81", secondaryCodes);

        var audit = await _db.AuditLogs.SingleOrDefaultAsync(a =>
            a.Action == "DIAGNOSIS_RECORDED" &&
            a.EntityId == recordId.ToString());
        Assert.NotNull(audit);
        Assert.Equal(3, audit!.ActorUserId);
        Assert.Equal(1, audit.HospitalId);
        Assert.Equal("MEDICAL_RECORD", audit.EntityType);
        Assert.Equal("Ghi nhận chẩn đoán xác định và kết luận", audit.Reason);
        using var changeDoc = System.Text.Json.JsonDocument.Parse(audit.ChangeData!);
        Assert.Equal("K29.5", changeDoc.RootElement.GetProperty("primaryIcd10Code").GetString());
        Assert.Equal("Phân biệt loét hành tá tràng", changeDoc.RootElement.GetProperty("differentialNotes").GetString());
    }

    [Fact]
    public async Task RecordDiagnosis_StructuredDiagnosesAndSubsequentUpdate_SynchronizesVisitDiagnosesAndPreservesPriorVitals()
    {
        await _service.OpenWorkspaceAsync(3, "VIS-202610-0011");

        await _service.RecordClinicalExaminationAsync(
            3,
            "VIS-202610-0011",
            new RecordClinicalExaminationRequest(
                PresentingComplaint: "Đau thượng vị, ợ chua",
                BpSystolicMmhg: 125,
                BpDiastolicMmhg: 82,
                PulseBpm: 74,
                ClinicalFindings: "Ấn đau thượng vị nhẹ"));

        var firstDx = await _service.RecordDiagnosisAsync(
            3,
            "VIS-202610-0011",
            new RecordDiagnosisRequest(
                Diagnoses:
                [
                    new RecordDiagnosisItemRequest(
                        Icd10CodeId: 1,
                        Code: "K29.5",
                        IsPrimary: true,
                        ClinicalDiagnosis: "Theo dõi viêm dạ dày mạn tính",
                        Status: "PRELIMINARY"),
                    new RecordDiagnosisItemRequest(
                        Icd10CodeId: 3,
                        Code: "K21.9",
                        IsPrimary: false,
                        ClinicalDiagnosis: "Trào ngược dạ dày - thực quản",
                        Status: "PRELIMINARY"),
                ],
                ClinicalConclusion: "Theo dõi viêm dạ dày mạn tính kèm trào ngược, chờ nội soi",
                IsConfirmed: false,
                ExpectedRevisionNo: 2));

        Assert.Equal("DIAGNOSIS_RECORDED", firstDx.ActionOutcome);
        Assert.Equal(3, firstDx.ClinicalContext.CurrentRevisionNo);
        Assert.False(firstDx.ClinicalContext.IsConfirmed);
        Assert.Equal(2, firstDx.ClinicalContext.Diagnoses.Count);
        Assert.All(firstDx.ClinicalContext.Diagnoses, d =>
        {
            Assert.Equal("PRELIMINARY", d.Status);
            Assert.Null(d.ConfirmedAt);
        });
        Assert.Equal(125, firstDx.ClinicalContext.VitalSigns.BpSystolicMmhg);
        Assert.Equal("Ấn đau thượng vị nhẹ", firstDx.ClinicalContext.ClinicalFindings);

        var retainedPrimaryId = firstDx.ClinicalContext.Diagnoses.Single(d => d.Code == "K29.5").Id;

        var secondDx = await _service.RecordDiagnosisAsync(
            3,
            "q-011",
            new RecordDiagnosisRequest(
                Diagnoses:
                [
                    new RecordDiagnosisItemRequest(
                        Code: "K29.5",
                        IsPrimary: true,
                        ClinicalDiagnosis: "Viêm dạ dày mạn tính đã xác định",
                        Status: "CONFIRMED"),
                    new RecordDiagnosisItemRequest(
                        Code: "I10",
                        IsPrimary: false,
                        ClinicalDiagnosis: "Tăng huyết áp độ 1",
                        Status: "CONFIRMED"),
                ],
                ClinicalConclusion: "Viêm dạ dày mạn tính kèm tăng huyết áp độ 1, điều trị nội khoa",
                AmendmentReason: "Cập nhật chẩn đoán chính thức sau khi hội chẩn",
                IsConfirmed: true,
                ExpectedRevisionNo: 3));

        Assert.Equal("DIAGNOSIS_RECORDED", secondDx.ActionOutcome);
        Assert.Equal(4, secondDx.ClinicalContext.CurrentRevisionNo);
        Assert.True(secondDx.ClinicalContext.IsConfirmed);
        Assert.Equal(2, secondDx.ClinicalContext.Diagnoses.Count);

        var updatedPrimary = secondDx.ClinicalContext.Diagnoses.Single(d => d.IsPrimary);
        Assert.Equal(retainedPrimaryId, updatedPrimary.Id);
        Assert.Equal("K29.5", updatedPrimary.Code);
        Assert.Equal("Viêm dạ dày mạn tính đã xác định", updatedPrimary.ClinicalDiagnosis);
        Assert.Equal("CONFIRMED", updatedPrimary.Status);
        Assert.NotNull(updatedPrimary.ConfirmedAt);

        var updatedSecondary = secondDx.ClinicalContext.Diagnoses.Single(d => !d.IsPrimary);
        Assert.Equal("I10", updatedSecondary.Code);
        Assert.Equal("Tăng huyết áp vô căn (nguyên phát)", updatedSecondary.Name);
        Assert.DoesNotContain(secondDx.ClinicalContext.Diagnoses, d => d.Code == "K21.9");

        var dbDiagnoses = await _db.VisitDiagnoses.Where(d => d.VisitId == 11).ToListAsync();
        Assert.Equal(2, dbDiagnoses.Count);
    }

    [Fact]
    public async Task RecordDiagnosis_InvalidPayloadOrIcd10Constraints_ThrowsValidationException()
    {
        await _service.OpenWorkspaceAsync(3, "VIS-202610-0011");

        // Null request
        await Assert.ThrowsAsync<ValidationException>(
            () => _service.RecordDiagnosisAsync(3, "VIS-202610-0011", null!));

        // Empty diagnosis items
        await Assert.ThrowsAsync<ValidationException>(
            () => _service.RecordDiagnosisAsync(
                3,
                "VIS-202610-0011",
                new RecordDiagnosisRequest(ClinicalConclusion: "Chưa chọn mã ICD-10")));

        // Missing primary diagnosis (all false)
        await Assert.ThrowsAsync<ValidationException>(
            () => _service.RecordDiagnosisAsync(
                3,
                "VIS-202610-0011",
                new RecordDiagnosisRequest(
                    Diagnoses:
                    [
                        new RecordDiagnosisItemRequest(Code: "K29.5", IsPrimary: false),
                        new RecordDiagnosisItemRequest(Code: "I10", IsPrimary: false),
                    ])));

        // Multiple primary diagnoses
        await Assert.ThrowsAsync<ValidationException>(
            () => _service.RecordDiagnosisAsync(
                3,
                "VIS-202610-0011",
                new RecordDiagnosisRequest(
                    Diagnoses:
                    [
                        new RecordDiagnosisItemRequest(Code: "K29.5", IsPrimary: true),
                        new RecordDiagnosisItemRequest(Code: "I10", IsPrimary: true),
                    ])));

        // Duplicate ICD-10 code (direct)
        await Assert.ThrowsAsync<ValidationException>(
            () => _service.RecordDiagnosisAsync(
                3,
                "VIS-202610-0011",
                new RecordDiagnosisRequest(
                    PrimaryIcd10Code: "K29.5",
                    SecondaryIcd10Codes: ["k29.5"])));

        // Duplicate ICD-10 code (by ID + code resolving to same entry)
        await Assert.ThrowsAsync<ValidationException>(
            () => _service.RecordDiagnosisAsync(
                3,
                "VIS-202610-0011",
                new RecordDiagnosisRequest(
                    Diagnoses:
                    [
                        new RecordDiagnosisItemRequest(Icd10CodeId: 1, IsPrimary: true),
                        new RecordDiagnosisItemRequest(Code: "K29.5", IsPrimary: false),
                    ])));

        // Invalid ICD-10 code format
        await Assert.ThrowsAsync<ValidationException>(
            () => _service.RecordDiagnosisAsync(
                3,
                "VIS-202610-0011",
                new RecordDiagnosisRequest(PrimaryIcd10Code: "INVALID_CODE")));

        // Unknown ICD-10 code in catalog
        await Assert.ThrowsAsync<ValidationException>(
            () => _service.RecordDiagnosisAsync(
                3,
                "VIS-202610-0011",
                new RecordDiagnosisRequest(PrimaryIcd10Code: "Z99.9")));

        // Non-selectable ICD-10 category node (K29 has IsSelectable = false)
        await Assert.ThrowsAsync<ValidationException>(
            () => _service.RecordDiagnosisAsync(
                3,
                "VIS-202610-0011",
                new RecordDiagnosisRequest(PrimaryIcd10Code: "K29")));

        // NotPrimaryDx = true code used as primary diagnosis (B96.81)
        await Assert.ThrowsAsync<ValidationException>(
            () => _service.RecordDiagnosisAsync(
                3,
                "VIS-202610-0011",
                new RecordDiagnosisRequest(PrimaryIcd10Code: "B96.81")));

        // Invalid DiagnosisStatus
        await Assert.ThrowsAsync<ValidationException>(
            () => _service.RecordDiagnosisAsync(
                3,
                "VIS-202610-0011",
                new RecordDiagnosisRequest(
                    PrimaryIcd10Code: "K29.5",
                    DiagnosisStatus: "INVALID_STATUS")));

        // ClinicalConclusion with < or > or too short
        await Assert.ThrowsAsync<ValidationException>(
            () => _service.RecordDiagnosisAsync(
                3,
                "VIS-202610-0011",
                new RecordDiagnosisRequest(
                    PrimaryIcd10Code: "K29.5",
                    ClinicalConclusion: "AB")));

        await Assert.ThrowsAsync<ValidationException>(
            () => _service.RecordDiagnosisAsync(
                3,
                "VIS-202610-0011",
                new RecordDiagnosisRequest(
                    PrimaryIcd10Code: "K29.5",
                    ClinicalConclusion: "Viêm dạ dày <script>")));
    }

    [Fact]
    public async Task RecordDiagnosis_UnauthorizedOrForbiddenDoctor_ThrowsAndDoesNotPersistDiagnosesOrAudit()
    {
        await _service.OpenWorkspaceAsync(3, "VIS-202610-0011");
        var baselineDiagnoses = await _db.VisitDiagnoses.CountAsync();
        var baselineRevisions = await _db.MedicalRecordRevisions.CountAsync();
        var baselineAudits = await _db.AuditLogs.CountAsync(a => a.Action == "DIAGNOSIS_RECORDED");

        var validRequest = new RecordDiagnosisRequest(
            PrimaryIcd10Code: "K29.5",
            ClinicalConclusion: "Viêm dạ dày mạn tính");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _service.RecordDiagnosisAsync(0, "VIS-202610-0011", validRequest));

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _service.RecordDiagnosisAsync(6, "VIS-202610-0011", validRequest));

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _service.RecordDiagnosisAsync(3, "VIS-202610-0021", validRequest));

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _service.RecordDiagnosisAsync(3, "VIS-202610-0002", validRequest));

        var writePerm = await _db.RolePermissions
            .FirstAsync(rp => rp.RoleId == 3 && rp.PermissionId == 1);
        _db.RolePermissions.Remove(writePerm);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _service.RecordDiagnosisAsync(3, "VIS-202610-0011", validRequest));

        Assert.Equal(baselineDiagnoses, await _db.VisitDiagnoses.CountAsync());
        Assert.Equal(baselineRevisions, await _db.MedicalRecordRevisions.CountAsync());
        Assert.Equal(baselineAudits, await _db.AuditLogs.CountAsync(a => a.Action == "DIAGNOSIS_RECORDED"));
    }

    [Fact]
    public async Task RecordDiagnosis_WhenMandatoryDiagnosticResultsPending_ThrowsConflictException()
    {
        await _service.OpenWorkspaceAsync(3, "VIS-202610-0011");

        // Case 1: Open DIAGNOSTIC queue entry while FinalResultRequired policy is enabled (default true)
        var diagQueue = new QueueEntry
        {
            Id = 901,
            VisitId = 11,
            ServicePointId = 1,
            CurrentServicePointId = 1,
            QueueDate = new DateOnly(2026, 10, 22),
            QueueNumber = 50,
            QueueType = "DIAGNOSTIC",
            Status = "IN_PROGRESS",
            PresenceStatus = "PRESENT",
        };
        _db.QueueEntries.Add(diagQueue);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(
            () => _service.RecordDiagnosisAsync(
                3,
                "VIS-202610-0011",
                new RecordDiagnosisRequest(
                    PrimaryIcd10Code: "K29.5",
                    ClinicalConclusion: "Viêm dạ dày mạn tính")));

        // Complete the diagnostic queue entry, but add a pending mandatory DiagnosticOrder
        diagQueue.Status = "COMPLETED";
        diagQueue.CompletedAt = new DateTime(2026, 10, 22, 9, 15, 0);
        var mandatoryOrder = new DiagnosticOrder
        {
            Id = 501,
            OrderCode = "CLS-202610-0501",
            VisitId = 11,
            OrderingDoctorId = 1,
            ServiceId = 1,
            RequiredForConclusion = true,
            Status = "IN_PROGRESS",
            OrderedAt = new DateTime(2026, 10, 22, 9, 5, 0),
        };
        _db.DiagnosticOrders.Add(mandatoryOrder);
        await _db.SaveChangesAsync();

        var workspaceWhileOrderPending = await _service.GetWorkspaceAsync(3, "VIS-202610-0011");
        Assert.False(workspaceWhileOrderPending.ClinicalContext.DiagnosticSummary.AllMandatoryResultsReady);
        Assert.False(workspaceWhileOrderPending.AllowedActions.Single(a => a.Id == "record-diagnosis").Enabled);

        await Assert.ThrowsAsync<ConflictException>(
            () => _service.RecordDiagnosisAsync(
                3,
                "VIS-202610-0011",
                new RecordDiagnosisRequest(
                    PrimaryIcd10Code: "K29.5",
                    ClinicalConclusion: "Viêm dạ dày mạn tính")));

        // Once the mandatory order is COMPLETED, RecordDiagnosis succeeds
        mandatoryOrder.Status = "COMPLETED";
        await _db.SaveChangesAsync();

        var recorded = await _service.RecordDiagnosisAsync(
            3,
            "VIS-202610-0011",
            new RecordDiagnosisRequest(
                PrimaryIcd10Code: "K29.5",
                ClinicalConclusion: "Viêm dạ dày mạn tính sau khi có kết quả CLS"));
        Assert.Equal("DIAGNOSIS_RECORDED", recorded.ActionOutcome);
    }

    [Fact]
    public async Task SearchIcd10Codes_FiltersByQueryPrimaryOnlyAndSelectable()
    {
        var allSelectable = await _service.SearchIcd10CodesAsync(3, query: null, primaryOnly: false, limit: 50);
        Assert.DoesNotContain(allSelectable, c => c.Code == "K29"); // IsSelectable = false excluded
        Assert.Contains(allSelectable, c => c.Code == "B96.81");

        var primaryOnlyList = await _service.SearchIcd10CodesAsync(3, query: null, primaryOnly: true, limit: 50);
        Assert.DoesNotContain(primaryOnlyList, c => c.Code == "B96.81"); // NotPrimaryDx = true excluded
        Assert.Contains(primaryOnlyList, c => c.Code == "K29.5");

        var byDiacriticFreeQuery = await _service.SearchIcd10CodesAsync(3, query: "gan nhiem mo", primaryOnly: true, limit: 10);
        var matched = Assert.Single(byDiacriticFreeQuery);
        Assert.Equal("K76.0", matched.Code);

        await Assert.ThrowsAsync<ValidationException>(
            () => _service.SearchIcd10CodesAsync(3, query: null, primaryOnly: false, limit: 0));

        await Assert.ThrowsAsync<ValidationException>(
            () => _service.SearchIcd10CodesAsync(3, query: "<script>", primaryOnly: false, limit: 10));
    }

    // -------------------------------------------------------------------------
    // 8. CompleteVisit (UC38 / #118)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CompleteVisit_InProgressVisitWithRecordedDiagnosis_FinalizesMedicalRecordCompletesVisitAndQueueAndWritesAudit()
    {
        var opened = await _service.OpenWorkspaceAsync(3, "VIS-202610-0011");
        Assert.Equal("OPENED", opened.ActionOutcome);
        var recordId = opened.ClinicalContext.MedicalRecordId!.Value;

        await _service.RecordClinicalExaminationAsync(
            3,
            "VIS-202610-0011",
            new RecordClinicalExaminationRequest(
                PresentingComplaint: "Đau vùng thượng vị sau ăn",
                BpSystolicMmhg: 120,
                BpDiastolicMmhg: 78,
                PulseBpm: 72,
                TemperatureC: 36.7m,
                ClinicalFindings: "Bụng mềm, ấn tức nhẹ thượng vị"));

        var dxResult = await _service.RecordDiagnosisAsync(
            3,
            "VIS-202610-0011",
            new RecordDiagnosisRequest(
                Diagnoses:
                [
                    new RecordDiagnosisItemRequest(
                        Code: "K29.5",
                        IsPrimary: true,
                        ClinicalDiagnosis: "Viêm dạ dày mạn tính đợt cấp",
                        Status: "PRELIMINARY"),
                    new RecordDiagnosisItemRequest(
                        Code: "K76.0",
                        IsPrimary: false,
                        ClinicalDiagnosis: "Gan nhiễm mỡ độ I",
                        Status: "PRELIMINARY"),
                ],
                ClinicalConclusion: "Viêm dạ dày mạn tính đợt cấp kèm gan nhiễm mỡ độ I",
                IsConfirmed: false,
                ExpectedStateVersion: 3,
                ExpectedRevisionNo: 2));

        Assert.Equal(3, dxResult.ClinicalContext.CurrentRevisionNo);
        Assert.All(dxResult.ClinicalContext.Diagnoses, d => Assert.Equal("PRELIMINARY", d.Status));

        var completed = await _service.CompleteVisitAsync(
            actorUserId: 3,
            visitIdOrCode: "VIS-202610-0011",
            request: new CompleteVisitRequest(
                ClinicalConclusion: "Viêm dạ dày mạn tính đợt cấp đã ổn định, điều trị ngoại trú 14 ngày",
                DischargeInstructions: "Ăn chín uống sôi, kiêng rượu bia và đồ chua cay",
                FollowUpInstructions: "Tái khám tại Phòng 101 sau 2 tuần",
                FollowUpDate: new DateOnly(2026, 11, 5),
                WarningSigns: "Nôn ra máu, đi ngoài phân đen hoặc đau bụng dữ dội",
                CompletionNote: "Hoàn tất khám và cấp toa ngoại trú",
                ConfirmNoPrescription: true,
                ExpectedStateVersion: 3,
                ExpectedRevisionNo: 3),
            ipAddress: "127.0.0.1");

        Assert.Equal("VISIT_COMPLETED", completed.ActionOutcome);
        Assert.Equal("COMPLETED", completed.WorkspaceState);
        Assert.False(completed.CanEditClinical);
        Assert.Equal("Hồ sơ bệnh án đã được khóa hoàn tất (FINALIZED).", completed.ReadOnlyReason);

        Assert.Equal("COMPLETED", completed.Visit.Status);
        Assert.Equal(new DateTime(2026, 10, 22, 9, 0, 0), completed.Visit.CompletedAt);
        Assert.NotNull(completed.Queue);
        Assert.Equal("COMPLETED", completed.Queue!.Status);
        Assert.Equal("09:00", completed.Queue.CompletedAt);
        Assert.Equal(4, completed.Queue.StateVersion);

        Assert.Equal("FINALIZED", completed.ClinicalContext.MedicalRecordStatus);
        Assert.Equal(new DateTime(2026, 10, 22, 9, 0, 0), completed.ClinicalContext.FinalizedAt);
        Assert.Equal(4, completed.ClinicalContext.CurrentRevisionNo);
        Assert.True(completed.ClinicalContext.IsConfirmed);
        Assert.Equal(
            "Viêm dạ dày mạn tính đợt cấp đã ổn định, điều trị ngoại trú 14 ngày",
            completed.ClinicalContext.ClinicalConclusion);

        Assert.Equal(2, completed.ClinicalContext.Diagnoses.Count);
        Assert.All(completed.ClinicalContext.Diagnoses, d =>
        {
            Assert.Equal("CONFIRMED", d.Status);
            Assert.NotNull(d.ConfirmedAt);
        });

        var editActions = completed.AllowedActions.Where(a => a.Id != "view-medical-history");
        Assert.All(editActions, a =>
        {
            Assert.False(a.Enabled);
            Assert.False(string.IsNullOrWhiteSpace(a.DisabledReason));
        });

        var dbVisit = await _db.Visits.FirstAsync(v => v.Id == 11);
        Assert.Equal("COMPLETED", dbVisit.Status);
        Assert.NotNull(dbVisit.CompletedAt);
        Assert.Equal(1L, dbVisit.CompletedByDoctorId);

        var dbRecord = await _db.MedicalRecords.FirstAsync(m => m.Id == recordId);
        Assert.Equal("FINALIZED", dbRecord.Status);
        Assert.NotNull(dbRecord.FinalizedAt);

        var audit = await _db.AuditLogs.SingleOrDefaultAsync(a =>
            a.Action == "VISIT_COMPLETED" &&
            a.EntityId == "VIS-202610-0011");
        Assert.NotNull(audit);
        Assert.Equal(3, audit!.ActorUserId);
        Assert.Equal(1, audit.HospitalId);
        Assert.Equal("VISIT", audit.EntityType);
        Assert.Equal("Hoàn tất khám và cấp toa ngoại trú", audit.Reason);

        using var changeDoc = System.Text.Json.JsonDocument.Parse(audit.ChangeData!);
        Assert.Equal("FINALIZED", changeDoc.RootElement.GetProperty("medicalRecordStatus").GetString());
        Assert.Equal("K29.5", changeDoc.RootElement.GetProperty("primaryIcd10Code").GetString());
        Assert.Equal("2026-11-05", changeDoc.RootElement.GetProperty("followUpDate").GetString());
        Assert.True(changeDoc.RootElement.GetProperty("confirmNoPrescription").GetBoolean());
    }

    [Fact]
    public async Task CompleteVisit_WithDraftPrescriptionAndNoBody_PromotesPrescriptionToIssuedAndFinalizesVisit()
    {
        await _service.OpenWorkspaceAsync(3, "VIS-202610-0011");
        await _service.RecordDiagnosisAsync(
            3,
            "VIS-202610-0011",
            new RecordDiagnosisRequest(
                PrimaryIcd10Code: "K29.5",
                ClinicalConclusion: "Viêm dạ dày mạn tính, kê đơn điều trị ngoại trú"));

        _db.Prescriptions.Add(new Prescription
        {
            Id = 50,
            PrescriptionCode = "RX-202610-0050",
            VisitId = 11,
            PrescribingDoctorId = 1,
            FulfillmentChoice = "HOSPITAL",
            Status = "DRAFT",
            CreatedAt = new DateTime(2026, 10, 22, 8, 55, 0),
        });
        _db.PrescriptionItems.Add(new PrescriptionItem
        {
            Id = 501,
            PrescriptionId = 50,
            MedicineId = 1,
            PrescribedQuantity = 14,
        });
        await _db.SaveChangesAsync();

        var completed = await _service.CompleteVisitAsync(3, "q-011", request: null);

        Assert.Equal("VISIT_COMPLETED", completed.ActionOutcome);
        Assert.Equal("COMPLETED", completed.WorkspaceState);
        Assert.Equal("FINALIZED", completed.ClinicalContext.MedicalRecordStatus);
        Assert.Equal(2, completed.ClinicalContext.CurrentRevisionNo);
        Assert.True(completed.ClinicalContext.IsConfirmed);
        Assert.Equal(1, completed.ClinicalContext.PrescriptionSummary.TotalPrescriptions);
        Assert.Equal("ISSUED", completed.ClinicalContext.PrescriptionSummary.LatestPrescriptionStatus);

        var dbRx = await _db.Prescriptions.FirstAsync(p => p.Id == 50);
        Assert.Equal("ISSUED", dbRx.Status);
        Assert.NotNull(dbRx.IssuedAt);
    }

    [Fact]
    public async Task CompleteVisit_MissingPrimaryDiagnosisOrEmptyDraftPrescription_ThrowsConflictException()
    {
        await _service.OpenWorkspaceAsync(3, "VIS-202610-0011");

        // Missing primary diagnosis right after OpenWorkspace -> 409
        await Assert.ThrowsAsync<ConflictException>(
            () => _service.CompleteVisitAsync(3, "VIS-202610-0011"));

        var dbVisitBefore = await _db.Visits.FirstAsync(v => v.Id == 11);
        Assert.Equal("IN_PROGRESS", dbVisitBefore.Status);
        Assert.Null(dbVisitBefore.CompletedAt);

        // Record diagnosis, then add an empty DRAFT prescription (0 items) -> 409
        await _service.RecordDiagnosisAsync(
            3,
            "VIS-202610-0011",
            new RecordDiagnosisRequest(
                PrimaryIcd10Code: "K29.5",
                ClinicalConclusion: "Viêm dạ dày mạn tính"));

        _db.Prescriptions.Add(new Prescription
        {
            Id = 51,
            PrescriptionCode = "RX-202610-0051",
            VisitId = 11,
            PrescribingDoctorId = 1,
            Status = "DRAFT",
            CreatedAt = new DateTime(2026, 10, 22, 8, 56, 0),
        });
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(
            () => _service.CompleteVisitAsync(3, "VIS-202610-0011"));
    }

    [Fact]
    public async Task CompleteVisit_WhenDiagnosticQueueOrOrderPending_ThrowsConflictException()
    {
        await _service.OpenWorkspaceAsync(3, "VIS-202610-0011");
        await _service.RecordDiagnosisAsync(
            3,
            "VIS-202610-0011",
            new RecordDiagnosisRequest(
                PrimaryIcd10Code: "K29.5",
                ClinicalConclusion: "Viêm dạ dày mạn tính"));

        // Create a non-mandatory diagnostic order (status = ORDERED, queue = WAITING)
        await _service.CreateDiagnosticOrderAsync(
            3,
            "VIS-202610-0011",
            new CreateDiagnosticOrderRequest(
                ServiceId: 5,
                Instruction: "Kiểm tra công thức máu",
                RequiredForConclusion: false));

        // Even if non-mandatory for diagnosis, CompleteVisit must reject while diagnostic step is still open
        await Assert.ThrowsAsync<ConflictException>(
            () => _service.CompleteVisitAsync(3, "VIS-202610-0011"));

        // Complete both diagnostic queue entry and diagnostic order -> CompleteVisit succeeds
        var diagOrder = await _db.DiagnosticOrders.SingleAsync(o => o.VisitId == 11);
        diagOrder.Status = "COMPLETED";
        var diagQueue = await _db.QueueEntries.SingleAsync(q => q.Id == diagOrder.QueueEntryId);
        diagQueue.Status = "COMPLETED";
        diagQueue.CompletedAt = new DateTime(2026, 10, 22, 9, 5, 0);
        await _db.SaveChangesAsync();

        var completed = await _service.CompleteVisitAsync(3, "VIS-202610-0011");
        Assert.Equal("VISIT_COMPLETED", completed.ActionOutcome);
        Assert.Equal("COMPLETED", completed.Visit.Status);
    }

    [Fact]
    public async Task CompleteVisit_InvalidInputs_ThrowsValidationException()
    {
        await _service.OpenWorkspaceAsync(3, "VIS-202610-0011");

        await Assert.ThrowsAsync<ValidationException>(
            () => _service.CompleteVisitAsync(
                3,
                "VIS-202610-0011",
                new CompleteVisitRequest(ExpectedStateVersion: 0)));

        await Assert.ThrowsAsync<ValidationException>(
            () => _service.CompleteVisitAsync(
                3,
                "VIS-202610-0011",
                new CompleteVisitRequest(ExpectedRevisionNo: 0)));

        await Assert.ThrowsAsync<ValidationException>(
            () => _service.CompleteVisitAsync(
                3,
                "VIS-202610-0011",
                new CompleteVisitRequest(ClinicalConclusion: "AB")));

        await Assert.ThrowsAsync<ValidationException>(
            () => _service.CompleteVisitAsync(
                3,
                "VIS-202610-0011",
                new CompleteVisitRequest(ClinicalConclusion: "Kết luận <script>")));

        await Assert.ThrowsAsync<ValidationException>(
            () => _service.CompleteVisitAsync(
                3,
                "VIS-202610-0011",
                new CompleteVisitRequest(DischargeInstructions: "Dặn dò <script>")));

        await Assert.ThrowsAsync<ValidationException>(
            () => _service.CompleteVisitAsync(
                3,
                "VIS-202610-0011",
                new CompleteVisitRequest(FollowUpDate: new DateOnly(2026, 10, 22))));
    }

    [Fact]
    public async Task CompleteVisit_UnauthorizedOrForbiddenDoctor_ThrowsAndDoesNotMutateVisitOrAudit()
    {
        await _service.OpenWorkspaceAsync(3, "VIS-202610-0011");
        await _service.RecordDiagnosisAsync(
            3,
            "VIS-202610-0011",
            new RecordDiagnosisRequest(
                PrimaryIcd10Code: "K29.5",
                ClinicalConclusion: "Viêm dạ dày mạn tính"));

        var baselineAudits = await _db.AuditLogs.CountAsync(a => a.Action == "VISIT_COMPLETED");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _service.CompleteVisitAsync(0, "VIS-202610-0011"));

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _service.CompleteVisitAsync(6, "VIS-202610-0011"));

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _service.CompleteVisitAsync(3, "VIS-202610-0021"));

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _service.CompleteVisitAsync(3, "VIS-202610-0002"));

        var writePerm = await _db.RolePermissions
            .FirstAsync(rp => rp.RoleId == 3 && rp.PermissionId == 1);
        _db.RolePermissions.Remove(writePerm);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _service.CompleteVisitAsync(3, "VIS-202610-0011"));

        var visit = await _db.Visits.FirstAsync(v => v.Id == 11);
        var record = await _db.MedicalRecords.FirstAsync(m => m.VisitId == 11);
        Assert.Equal("IN_PROGRESS", visit.Status);
        Assert.Null(visit.CompletedAt);
        Assert.Equal("DRAFT", record.Status);
        Assert.Equal(baselineAudits, await _db.AuditLogs.CountAsync(a => a.Action == "VISIT_COMPLETED"));
    }

    [Fact]
    public async Task CompleteVisit_WhenVisitNotInProgressOrConcurrencyMismatch_ThrowsConflictException()
    {
        await _service.OpenWorkspaceAsync(3, "VIS-202610-0011");
        await _service.RecordDiagnosisAsync(
            3,
            "VIS-202610-0011",
            new RecordDiagnosisRequest(
                PrimaryIcd10Code: "K29.5",
                ClinicalConclusion: "Viêm dạ dày mạn tính"));

        // ExpectedStateVersion mismatch -> 409
        await Assert.ThrowsAsync<ConflictException>(
            () => _service.CompleteVisitAsync(
                3,
                "VIS-202610-0011",
                new CompleteVisitRequest(ExpectedStateVersion: 99)));

        // ExpectedRevisionNo mismatch -> 409
        await Assert.ThrowsAsync<ConflictException>(
            () => _service.CompleteVisitAsync(
                3,
                "VIS-202610-0011",
                new CompleteVisitRequest(ExpectedRevisionNo: 99)));

        // First CompleteVisit succeeds
        await _service.CompleteVisitAsync(3, "VIS-202610-0011");

        // Second CompleteVisit on already COMPLETED visit -> 409
        await Assert.ThrowsAsync<ConflictException>(
            () => _service.CompleteVisitAsync(3, "VIS-202610-0011"));
    }

    // -------------------------------------------------------------------------
    // 9. ReviewDiagnosticResults (UC35 / #114)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetDiagnosticResults_VisitWithFinalizedAndPendingOrders_ReturnsResultsVersionHistoryStaffAttributionAndFilters()
    {
        await _service.OpenWorkspaceAsync(3, "VIS-202610-0011");

        _db.Staff.AddRange(
            new Staff
            {
                Id = 10,
                HospitalId = 1,
                DepartmentId = 1,
                StaffCode = "STF-CLS-01",
                FullName = "KTV Lê Hoàng Nam",
                Status = "ACTIVE",
            },
            new Staff
            {
                Id = 11,
                HospitalId = 1,
                DepartmentId = 1,
                StaffCode = "STF-CLS-02",
                FullName = "BS.CKI Phạm Minh Tuấn",
                Status = "ACTIVE",
            });
        await _db.SaveChangesAsync();

        await _service.CreateDiagnosticOrderAsync(
            3,
            "VIS-202610-0011",
            new CreateDiagnosticOrderRequest(
                Items:
                [
                    new CreateDiagnosticOrderItemRequest(
                        ServiceId: 5,
                        Instruction: "Xét nghiệm công thức máu lúc đói",
                        Priority: "URGENT",
                        RequiredForConclusion: true),
                    new CreateDiagnosticOrderItemRequest(
                        ServiceId: 6,
                        Instruction: "Siêu âm ổ bụng tổng quát",
                        Priority: "NORMAL",
                        RequiredForConclusion: false),
                ]));

        var orders = await _db.DiagnosticOrders
            .Where(o => o.VisitId == 11)
            .OrderBy(o => o.Id)
            .ToListAsync();
        Assert.Equal(2, orders.Count);
        var cbcOrder = orders.Single(o => o.ServiceId == 5);
        var usOrder = orders.Single(o => o.ServiceId == 6);

        cbcOrder.Status = "COMPLETED";
        cbcOrder.ConfirmedByStaffId = 10;
        cbcOrder.ConfirmedAt = new DateTime(2026, 10, 22, 8, 52, 0);

        var cbcQueue = await _db.QueueEntries.SingleAsync(q => q.Id == cbcOrder.QueueEntryId);
        cbcQueue.Status = "COMPLETED";
        cbcQueue.CompletedAt = new DateTime(2026, 10, 22, 9, 0, 0);

        var cbcResult = new DiagnosticResult
        {
            Id = 501,
            DiagnosticOrderId = cbcOrder.Id,
            Status = "FINALIZED",
            FinalizedAt = new DateTime(2026, 10, 22, 9, 0, 0),
            FinalizedByStaffId = 11,
        };
        _db.DiagnosticResults.Add(cbcResult);

        _db.DiagnosticResultVersions.AddRange(
            new DiagnosticResultVersion
            {
                Id = 601,
                DiagnosticResultId = 501,
                VersionNo = 1,
                ResultText = "WBC 7.0 G/L, Hgb 136 g/L, PLT 240 G/L",
                Conclusion = "Kết quả sơ bộ bình thường",
                ReportFileUrl = null,
                EnteredByStaffId = 10,
                EnteredAt = new DateTime(2026, 10, 22, 8, 56, 0),
                IsFinalized = false,
            },
            new DiagnosticResultVersion
            {
                Id = 602,
                DiagnosticResultId = 501,
                VersionNo = 2,
                ResultText = "WBC 7.2 G/L, Hgb 138 g/L, PLT 245 G/L",
                Conclusion = "Công thức máu trong giới hạn bình thường",
                ReportFileUrl = "https://cdn.medflow.vn/reports/DIA-202610-0001-v2.pdf",
                EnteredByStaffId = 10,
                EnteredAt = new DateTime(2026, 10, 22, 8, 58, 0),
                IsFinalized = true,
                VerifiedByStaffId = 11,
                VerifiedAt = new DateTime(2026, 10, 22, 9, 0, 0),
                CorrectionReason = "Hiệu chỉnh chỉ số sau kiểm chuẩn máy huyết học",
            });
        await _db.SaveChangesAsync();

        var response = await _service.GetDiagnosticResultsAsync(3, "VIS-202610-0011");

        Assert.Equal(11, response.VisitId);
        Assert.Equal("VIS-202610-0011", response.VisitCode);
        Assert.Equal("IN_PROGRESS", response.WorkspaceState);
        Assert.True(response.CanViewDiagnosticResults);
        Assert.True(response.CanAcknowledgeDiagnosticResults);
        Assert.Null(response.DisabledReason);
        Assert.Equal(2, response.TotalOrdersCount);
        Assert.Equal(1, response.ReadyResultsCount);
        Assert.Equal(1, response.PendingResultsCount);
        Assert.Equal(1, response.MandatoryOrdersCount);
        Assert.Equal(1, response.MandatoryReadyCount);
        Assert.Equal(0, response.MandatoryPendingCount);
        Assert.Equal(2, response.Items.Count);

        var cbcItem = Assert.Single(response.Items, i => i.OrderId == cbcOrder.Id);
        Assert.Equal("LABORATORY", cbcItem.Category);
        Assert.True(cbcItem.HasResult);
        Assert.True(cbcItem.IsResultReady);
        Assert.True(cbcItem.IsResultFinalized);
        Assert.Equal(501, cbcItem.ResultId);
        Assert.Equal("FINALIZED", cbcItem.ResultStatus);
        Assert.Equal(2, cbcItem.LatestVersionNo);
        Assert.Equal("WBC 7.2 G/L, Hgb 138 g/L, PLT 245 G/L", cbcItem.ResultText);
        Assert.Equal("Công thức máu trong giới hạn bình thường", cbcItem.Conclusion);
        Assert.Equal("https://cdn.medflow.vn/reports/DIA-202610-0001-v2.pdf", cbcItem.ReportFileUrl);
        Assert.Equal("KTV Lê Hoàng Nam", cbcItem.EnteredByStaffName);
        Assert.Equal("BS.CKI Phạm Minh Tuấn", cbcItem.VerifiedByStaffName);
        Assert.Equal("Hiệu chỉnh chỉ số sau kiểm chuẩn máy huyết học", cbcItem.CorrectionReason);
        Assert.Equal(2, cbcItem.Versions.Count);
        Assert.Equal(2, cbcItem.Versions[0].VersionNo);
        Assert.Equal(1, cbcItem.Versions[1].VersionNo);

        var usItem = Assert.Single(response.Items, i => i.OrderId == usOrder.Id);
        Assert.Equal("IMAGING", usItem.Category);
        Assert.False(usItem.HasResult);
        Assert.False(usItem.IsResultReady);
        Assert.False(usItem.IsResultFinalized);
        Assert.Equal("PENDING", usItem.ResultStatus);
        Assert.Empty(usItem.Versions);

        var readyOnlyResponse = await _service.GetDiagnosticResultsAsync(
            3,
            "VIS-202610-0011",
            new GetDiagnosticResultsRequest(ReadyOnly: true));
        Assert.Single(readyOnlyResponse.Items);
        Assert.Equal(cbcOrder.Id, readyOnlyResponse.Items[0].OrderId);
        Assert.Equal(2, readyOnlyResponse.TotalOrdersCount);

        var imagingResponse = await _service.GetDiagnosticResultsAsync(
            3,
            "VIS-202610-0011",
            new GetDiagnosticResultsRequest(Category: "IMAGING", Status: "PENDING"));
        Assert.Single(imagingResponse.Items);
        Assert.Equal(usOrder.Id, imagingResponse.Items[0].OrderId);

        var detailByCode = await _service.GetDiagnosticResultByOrderAsync(
            3,
            "VIS-202610-0011",
            cbcOrder.OrderCode!);
        Assert.Equal(cbcOrder.Id, detailByCode.OrderId);
        Assert.Equal(2, detailByCode.Versions.Count);

        var detailById = await _service.GetDiagnosticResultByOrderAsync(
            3,
            "VIS-202610-0011",
            cbcOrder.Id.ToString());
        Assert.Equal(cbcOrder.OrderCode, detailById.OrderCode);
    }

    [Fact]
    public async Task ReviewDiagnosticResults_InProgressVisitWithReadyResults_AppendsClinicalInterpretationRevisionIncrementsStateVersionAndWritesAudit()
    {
        await _service.OpenWorkspaceAsync(3, "VIS-202610-0011");

        await _service.CreateDiagnosticOrderAsync(
            3,
            "VIS-202610-0011",
            new CreateDiagnosticOrderRequest(
                ServiceId: 5,
                Instruction: "Công thức máu kiểm tra viêm",
                Priority: "URGENT",
                RequiredForConclusion: true));

        var order = await _db.DiagnosticOrders.SingleAsync(o => o.VisitId == 11);
        order.Status = "COMPLETED";
        var diagQueue = await _db.QueueEntries.SingleAsync(q => q.Id == order.QueueEntryId);
        diagQueue.Status = "COMPLETED";
        diagQueue.CompletedAt = new DateTime(2026, 10, 22, 8, 58, 0);

        _db.DiagnosticResults.Add(new DiagnosticResult
        {
            Id = 510,
            DiagnosticOrderId = order.Id,
            Status = "FINALIZED",
            FinalizedAt = new DateTime(2026, 10, 22, 8, 58, 0),
            FinalizedByStaffId = 1,
        });
        _db.DiagnosticResultVersions.Add(new DiagnosticResultVersion
        {
            Id = 610,
            DiagnosticResultId = 510,
            VersionNo = 1,
            ResultText = "WBC 6.8 G/L, Hgb 140 g/L, PLT 255 G/L",
            Conclusion = "Các chỉ số huyết học bình thường",
            IsFinalized = true,
            EnteredByStaffId = 1,
            EnteredAt = new DateTime(2026, 10, 22, 8, 57, 0),
            VerifiedByStaffId = 1,
            VerifiedAt = new DateTime(2026, 10, 22, 8, 58, 0),
        });
        await _db.SaveChangesAsync();

        var workspaceBefore = await _service.GetWorkspaceAsync(3, "VIS-202610-0011");
        var reviewAction = Assert.Single(workspaceBefore.AllowedActions, a => a.Id == "review-diagnostic-results");
        Assert.Equal("UC35", reviewAction.UcCode);
        Assert.True(reviewAction.Enabled);
        Assert.Null(reviewAction.DisabledReason);

        var orderDtoBefore = Assert.Single(workspaceBefore.ClinicalContext.DiagnosticOrders!);
        Assert.True(orderDtoBefore.IsResultFinalized);
        Assert.Equal(510, orderDtoBefore.ResultId);
        Assert.Equal("FINALIZED", orderDtoBefore.ResultStatus);
        Assert.Equal("WBC 6.8 G/L, Hgb 140 g/L, PLT 255 G/L", orderDtoBefore.ResultText);
        Assert.Equal("Các chỉ số huyết học bình thường", orderDtoBefore.Conclusion);

        var stateVersionBefore = workspaceBefore.Queue!.StateVersion;
        var revisionNoBefore = workspaceBefore.ClinicalContext.CurrentRevisionNo ?? 1;

        var reviewed = await _service.ReviewDiagnosticResultsAsync(
            3,
            "VIS-202610-0011",
            new ReviewDiagnosticResultsRequest(
                OrderIds: [order.Id],
                ClinicalInterpretation: "Công thức máu bình thường, loại trừ tình trạng nhiễm trùng cấp tính",
                ClinicalConclusion: "Theo dõi viêm dạ dày mạn tính, đáp ứng tốt",
                AmendmentReason: "Cập nhật nhận định sau khi có kết quả công thức máu",
                ExpectedStateVersion: stateVersionBefore,
                ExpectedRevisionNo: revisionNoBefore),
            ipAddress: "127.0.0.1");

        Assert.Equal("DIAGNOSTIC_RESULTS_REVIEWED", reviewed.ActionOutcome);
        Assert.Equal("IN_PROGRESS", reviewed.WorkspaceState);
        Assert.Equal(stateVersionBefore + 1, reviewed.Queue!.StateVersion);
        Assert.Equal(revisionNoBefore + 1, reviewed.ClinicalContext.CurrentRevisionNo);
        Assert.Equal(
            "Công thức máu bình thường, loại trừ tình trạng nhiễm trùng cấp tính",
            reviewed.ClinicalContext.ClinicalFindings);
        Assert.Equal(
            "Theo dõi viêm dạ dày mạn tính, đáp ứng tốt",
            reviewed.ClinicalContext.ClinicalConclusion);
        Assert.Equal(
            "Cập nhật nhận định sau khi có kết quả công thức máu",
            reviewed.ClinicalContext.AmendmentReason);

        var audit = await _db.AuditLogs
            .OrderByDescending(a => a.Id)
            .FirstOrDefaultAsync(a => a.Action == "DIAGNOSTIC_RESULTS_REVIEWED");
        Assert.NotNull(audit);
        Assert.Equal(3, audit!.ActorUserId);
        Assert.Equal("VISIT", audit.EntityType);
        Assert.Equal("VIS-202610-0011", audit.EntityId);
        Assert.Contains(order.OrderCode!, audit.ChangeData);
    }

    [Fact]
    public async Task ReviewDiagnosticResults_WhenResultsPendingOrOrderNotFoundOrVisitNotInProgress_ThrowsExpectedExceptions()
    {
        // 1. Visit is CALLED (not IN_PROGRESS) -> 409 ConflictException
        await Assert.ThrowsAsync<ConflictException>(
            () => _service.ReviewDiagnosticResultsAsync(3, "VIS-202610-0011"));

        // 2. Open workspace, no diagnostic orders yet -> 409 ConflictException
        await _service.OpenWorkspaceAsync(3, "VIS-202610-0011");
        await Assert.ThrowsAsync<ConflictException>(
            () => _service.ReviewDiagnosticResultsAsync(3, "VIS-202610-0011"));

        // 3. Create order (still ORDERED / pending)
        await _service.CreateDiagnosticOrderAsync(
            3,
            "VIS-202610-0011",
            new CreateDiagnosticOrderRequest(
                ServiceId: 5,
                Instruction: "Công thức máu",
                Priority: "NORMAL",
                RequiredForConclusion: true));

        var order = await _db.DiagnosticOrders.SingleAsync(o => o.VisitId == 11);

        // Unknown OrderId or OrderCode -> 404 NotFoundException
        await Assert.ThrowsAsync<NotFoundException>(
            () => _service.GetDiagnosticResultByOrderAsync(3, "VIS-202610-0011", "DIA-202610-9999"));
        await Assert.ThrowsAsync<NotFoundException>(
            () => _service.ReviewDiagnosticResultsAsync(
                3,
                "VIS-202610-0011",
                new ReviewDiagnosticResultsRequest(OrderIds: [9999])));
        await Assert.ThrowsAsync<NotFoundException>(
            () => _service.ReviewDiagnosticResultsAsync(
                3,
                "VIS-202610-0011",
                new ReviewDiagnosticResultsRequest(OrderCodes: ["DIA-202610-9999"])));

        // Order is still pending (no result) -> 409 ConflictException
        await Assert.ThrowsAsync<ConflictException>(
            () => _service.ReviewDiagnosticResultsAsync(3, "VIS-202610-0011"));
        await Assert.ThrowsAsync<ConflictException>(
            () => _service.ReviewDiagnosticResultsAsync(
                3,
                "VIS-202610-0011",
                new ReviewDiagnosticResultsRequest(OrderIds: [order.Id])));

        // Add DRAFT (unfinalized) DiagnosticResult -> still not ready -> 409 ConflictException
        _db.DiagnosticResults.Add(new DiagnosticResult
        {
            Id = 520,
            DiagnosticOrderId = order.Id,
            Status = "DRAFT",
            FinalizedAt = null,
        });
        _db.DiagnosticResultVersions.Add(new DiagnosticResultVersion
        {
            Id = 620,
            DiagnosticResultId = 520,
            VersionNo = 1,
            ResultText = "Kết quả nháp chưa duyệt",
            IsFinalized = false,
        });
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(
            () => _service.ReviewDiagnosticResultsAsync(
                3,
                "VIS-202610-0011",
                new ReviewDiagnosticResultsRequest(OrderIds: [order.Id])));

        // Finalize result -> now ready, test concurrency mismatches
        var draftResult = await _db.DiagnosticResults.SingleAsync(r => r.Id == 520);
        draftResult.Status = "FINALIZED";
        draftResult.FinalizedAt = new DateTime(2026, 10, 22, 9, 2, 0);
        var draftVer = await _db.DiagnosticResultVersions.SingleAsync(v => v.Id == 620);
        draftVer.IsFinalized = true;
        draftVer.VerifiedAt = new DateTime(2026, 10, 22, 9, 2, 0);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(
            () => _service.ReviewDiagnosticResultsAsync(
                3,
                "VIS-202610-0011",
                new ReviewDiagnosticResultsRequest(ExpectedStateVersion: 99)));

        await Assert.ThrowsAsync<ConflictException>(
            () => _service.ReviewDiagnosticResultsAsync(
                3,
                "VIS-202610-0011",
                new ReviewDiagnosticResultsRequest(ExpectedRevisionNo: 99)));

        // Seeded COMPLETED visit VIS-202610-0001 -> 409 ConflictException on review
        await Assert.ThrowsAsync<ConflictException>(
            () => _service.ReviewDiagnosticResultsAsync(3, "VIS-202610-0001"));
    }

    [Fact]
    public async Task GetAndReviewDiagnosticResults_InvalidInputs_ThrowsValidationException()
    {
        await _service.OpenWorkspaceAsync(3, "VIS-202610-0011");

        await Assert.ThrowsAsync<ValidationException>(
            () => _service.GetDiagnosticResultsAsync(
                3,
                "VIS-202610-0011",
                new GetDiagnosticResultsRequest(Category: "INVALID_CATEGORY")));

        await Assert.ThrowsAsync<ValidationException>(
            () => _service.GetDiagnosticResultsAsync(
                3,
                "VIS-202610-0011",
                new GetDiagnosticResultsRequest(Status: "INVALID_STATUS")));

        await Assert.ThrowsAsync<ValidationException>(
            () => _service.GetDiagnosticResultsAsync(
                3,
                "VIS-202610-0011",
                new GetDiagnosticResultsRequest(OrderId: 0)));

        await Assert.ThrowsAsync<ValidationException>(
            () => _service.GetDiagnosticResultsAsync(
                3,
                "VIS-202610-0011",
                new GetDiagnosticResultsRequest(OrderCode: "<script>")));

        await Assert.ThrowsAsync<ValidationException>(
            () => _service.GetDiagnosticResultByOrderAsync(3, "VIS-202610-0011", "0"));

        await Assert.ThrowsAsync<ValidationException>(
            () => _service.ReviewDiagnosticResultsAsync(
                3,
                "VIS-202610-0011",
                new ReviewDiagnosticResultsRequest(ExpectedStateVersion: 0)));

        await Assert.ThrowsAsync<ValidationException>(
            () => _service.ReviewDiagnosticResultsAsync(
                3,
                "VIS-202610-0011",
                new ReviewDiagnosticResultsRequest(ExpectedRevisionNo: 0)));

        await Assert.ThrowsAsync<ValidationException>(
            () => _service.ReviewDiagnosticResultsAsync(
                3,
                "VIS-202610-0011",
                new ReviewDiagnosticResultsRequest(OrderIds: [0])));

        await Assert.ThrowsAsync<ValidationException>(
            () => _service.ReviewDiagnosticResultsAsync(
                3,
                "VIS-202610-0011",
                new ReviewDiagnosticResultsRequest(OrderIds: [1, 1])));

        await Assert.ThrowsAsync<ValidationException>(
            () => _service.ReviewDiagnosticResultsAsync(
                3,
                "VIS-202610-0011",
                new ReviewDiagnosticResultsRequest(OrderCodes: ["<script>"])));

        await Assert.ThrowsAsync<ValidationException>(
            () => _service.ReviewDiagnosticResultsAsync(
                3,
                "VIS-202610-0011",
                new ReviewDiagnosticResultsRequest(ClinicalInterpretation: "Nhận định <script>")));

        await Assert.ThrowsAsync<ValidationException>(
            () => _service.ReviewDiagnosticResultsAsync(
                3,
                "VIS-202610-0011",
                new ReviewDiagnosticResultsRequest(ClinicalConclusion: "AB")));

        await Assert.ThrowsAsync<ValidationException>(
            () => _service.ReviewDiagnosticResultsAsync(
                3,
                "VIS-202610-0011",
                new ReviewDiagnosticResultsRequest(AmendmentReason: "Lý do <script>")));
    }

    [Fact]
    public async Task GetAndReviewDiagnosticResults_UnauthorizedOrForbiddenDoctor_ThrowsAndDoesNotMutateOrAudit()
    {
        await _service.OpenWorkspaceAsync(3, "VIS-202610-0011");
        var baselineAudits = await _db.AuditLogs.CountAsync(a => a.Action == "DIAGNOSTIC_RESULTS_REVIEWED");

        // 401 on actorUserId <= 0
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _service.GetDiagnosticResultsAsync(0, "VIS-202610-0011"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _service.GetDiagnosticResultByOrderAsync(0, "VIS-202610-0011", "DIA-202610-0001"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => _service.ReviewDiagnosticResultsAsync(0, "VIS-202610-0011"));

        // 403 on non-doctor role (user 6 = RECEPTION)
        await Assert.ThrowsAsync<ForbiddenException>(
            () => _service.GetDiagnosticResultsAsync(6, "VIS-202610-0011"));
        await Assert.ThrowsAsync<ForbiddenException>(
            () => _service.ReviewDiagnosticResultsAsync(6, "VIS-202610-0011"));

        // 403 on cross-hospital visit (VIS-202610-0021)
        await Assert.ThrowsAsync<ForbiddenException>(
            () => _service.GetDiagnosticResultsAsync(3, "VIS-202610-0021"));
        await Assert.ThrowsAsync<ForbiddenException>(
            () => _service.ReviewDiagnosticResultsAsync(3, "VIS-202610-0021"));

        // 403 on cross-doctor visit (VIS-202610-0002)
        await Assert.ThrowsAsync<ForbiddenException>(
            () => _service.GetDiagnosticResultsAsync(3, "VIS-202610-0002"));
        await Assert.ThrowsAsync<ForbiddenException>(
            () => _service.ReviewDiagnosticResultsAsync(3, "VIS-202610-0002"));

        // Remove MEDICAL_RECORD_WRITE permission -> GetDiagnosticResults still allowed, ReviewDiagnosticResults throws 403
        var writePerm = await _db.RolePermissions
            .FirstAsync(rp => rp.RoleId == 3 && rp.PermissionId == 1);
        _db.RolePermissions.Remove(writePerm);
        await _db.SaveChangesAsync();

        var readResponse = await _service.GetDiagnosticResultsAsync(3, "VIS-202610-0011");
        Assert.True(readResponse.CanViewDiagnosticResults);
        Assert.False(readResponse.CanAcknowledgeDiagnosticResults);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _service.ReviewDiagnosticResultsAsync(3, "VIS-202610-0011"));

        Assert.Equal(baselineAudits, await _db.AuditLogs.CountAsync(a => a.Action == "DIAGNOSTIC_RESULTS_REVIEWED"));
    }

    // -------------------------------------------------------------------------
    // 11. CreateAdditionalDiagnosticOrder (UC34 / #116 / M3-008)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CreateAdditionalDiagnosticOrder_AfterFinalizedInitialOrder_LinksFollowUpOrderAppendsRevisionRelocksSummaryAndWritesAudit()
    {
        await _service.OpenWorkspaceAsync(3, "VIS-202610-0011");

        var initialResult = await _service.CreateDiagnosticOrderAsync(
            3,
            "VIS-202610-0011",
            new CreateDiagnosticOrderRequest(
                ServiceId: 5,
                Instruction: "Xét nghiệm công thức máu ban đầu",
                Priority: "NORMAL",
                RequiredForConclusion: true));

        var initialOrder = Assert.Single(initialResult.CreatedDiagnosticOrders!);
        Assert.False(initialOrder.IsAdditionalOrder);
        Assert.Equal(1, initialOrder.OrderSequenceNo);

        // Simulate CLS staff finalizing the initial diagnostic result
        var dbInitialOrder = await _db.DiagnosticOrders.SingleAsync(o => o.Id == initialOrder.OrderId);
        dbInitialOrder.Status = "COMPLETED";
        var dbInitialQueue = await _db.QueueEntries.SingleAsync(q => q.Id == dbInitialOrder.QueueEntryId);
        dbInitialQueue.Status = "COMPLETED";
        dbInitialQueue.CompletedAt = new DateTime(2026, 10, 22, 9, 0, 0);

        var diagResult = new DiagnosticResult
        {
            Id = 501,
            DiagnosticOrderId = dbInitialOrder.Id,
            Status = "FINALIZED",
            FinalizedByStaffId = 1,
            FinalizedAt = new DateTime(2026, 10, 22, 9, 0, 0),
        };
        _db.DiagnosticResults.Add(diagResult);
        _db.DiagnosticResultVersions.Add(new DiagnosticResultVersion
        {
            Id = 5001,
            DiagnosticResultId = diagResult.Id,
            VersionNo = 1,
            ResultText = "WBC: 14.8 G/L [HIGH] (Ref: 4.0-10.0)",
            Conclusion = "Bạch cầu tăng cao, cần khảo sát hình ảnh ổ bụng",
            EnteredByStaffId = 1,
            EnteredAt = new DateTime(2026, 10, 22, 8, 58, 0),
            IsFinalized = true,
            VerifiedByStaffId = 1,
            VerifiedAt = new DateTime(2026, 10, 22, 9, 0, 0),
        });
        await _db.SaveChangesAsync();

        var catalogBeforeAdditional = await _service.GetDiagnosticOrderCatalogAsync(3, "VIS-202610-0011");
        Assert.True(catalogBeforeAdditional.CanCreateDiagnosticOrder);
        Assert.True(catalogBeforeAdditional.CanCreateAdditionalDiagnosticOrder);
        Assert.Equal(1, catalogBeforeAdditional.CompletedOrdersCount);
        Assert.True(catalogBeforeAdditional.DiagnosticSummary.AllMandatoryResultsReady);

        var cbcCatalogItem = Assert.Single(catalogBeforeAdditional.Services, s => s.ServiceId == 5);
        Assert.False(cbcCatalogItem.HasActiveOrderInVisit);
        Assert.True(cbcCatalogItem.HasCompletedOrderInVisit);
        Assert.Equal(1, cbcCatalogItem.PreviousOrderCountInVisit);
        Assert.Equal(initialOrder.OrderCode, cbcCatalogItem.LatestOrderCodeInVisit);
        Assert.Equal("FINALIZED", cbcCatalogItem.LatestOrderStatusInVisit);

        var additionalResponse = await _service.CreateAdditionalDiagnosticOrderAsync(
            actorUserId: 3,
            visitIdOrCode: "VIS-202610-0011",
            request: new CreateAdditionalDiagnosticOrderRequest(
                ServiceId: 6,
                Instruction: "Siêu âm ổ bụng khảo sát vùng hạ sườn phải và đường mật",
                Priority: "URGENT",
                RequiredForConclusion: true,
                FollowUpToOrderCode: initialOrder.OrderCode,
                AdditionalOrderReason: "Cần siêu âm ổ bụng kiểm tra thêm nguyên nhân bạch cầu tăng",
                ClinicalProgressionNote: "Bệnh nhân còn đau tức hạ sườn phải sau khi có KQ máu",
                ExpectedStateVersion: 4,
                ExpectedRevisionNo: 1),
            ipAddress: "127.0.0.1");

        Assert.Equal("ADDITIONAL_DIAGNOSTIC_ORDER_CREATED", additionalResponse.ActionOutcome);
        Assert.Equal("IN_PROGRESS", additionalResponse.WorkspaceState);
        Assert.Equal(5, additionalResponse.Queue!.StateVersion);
        Assert.Equal(2, additionalResponse.ClinicalContext.CurrentRevisionNo);
        Assert.Equal(
            "Bệnh nhân còn đau tức hạ sườn phải sau khi có KQ máu",
            additionalResponse.ClinicalContext.ClinicalFindings);
        Assert.Equal(
            "Cần siêu âm ổ bụng kiểm tra thêm nguyên nhân bạch cầu tăng",
            additionalResponse.ClinicalContext.AmendmentReason);

        var createdAdditional = Assert.Single(additionalResponse.CreatedDiagnosticOrders!);
        Assert.Equal(6, createdAdditional.ServiceId);
        Assert.Equal("DIA-US", createdAdditional.ServiceCode);
        Assert.True(createdAdditional.IsAdditionalOrder);
        Assert.Equal(2, createdAdditional.OrderSequenceNo);
        Assert.Equal(initialOrder.OrderId, createdAdditional.FollowUpToOrderId);
        Assert.Equal(initialOrder.OrderCode, createdAdditional.FollowUpToOrderCode);
        Assert.Equal(
            "Cần siêu âm ổ bụng kiểm tra thêm nguyên nhân bạch cầu tăng",
            createdAdditional.AdditionalOrderReason);

        var summaryAfter = additionalResponse.ClinicalContext.DiagnosticSummary;
        Assert.Equal(2, summaryAfter.TotalQueueEntries);
        Assert.Equal(1, summaryAfter.OpenQueueEntries);
        Assert.Equal(1, summaryAfter.CompletedQueueEntries);
        Assert.False(summaryAfter.AllMandatoryResultsReady);

        Assert.True(additionalResponse.AllowedActions.Single(a => a.Id == "create-additional-diagnostic-order").Enabled);
        Assert.False(additionalResponse.AllowedActions.Single(a => a.Id == "record-diagnosis").Enabled);
        Assert.False(additionalResponse.AllowedActions.Single(a => a.Id == "create-prescription").Enabled);
        Assert.False(additionalResponse.AllowedActions.Single(a => a.Id == "complete-visit").Enabled);

        // Verify metadata reconstruction from audit logs in ClinicalContext.DiagnosticOrders
        var workspaceOrders = additionalResponse.ClinicalContext.DiagnosticOrders!;
        Assert.Equal(2, workspaceOrders.Count);
        var reconstructedAdditional = Assert.Single(workspaceOrders, o => o.OrderId == createdAdditional.OrderId);
        Assert.True(reconstructedAdditional.IsAdditionalOrder);
        Assert.Equal(2, reconstructedAdditional.OrderSequenceNo);
        Assert.Equal(initialOrder.OrderId, reconstructedAdditional.FollowUpToOrderId);
        Assert.Equal(initialOrder.OrderCode, reconstructedAdditional.FollowUpToOrderCode);
        Assert.Equal(
            "Cần siêu âm ổ bụng kiểm tra thêm nguyên nhân bạch cầu tăng",
            reconstructedAdditional.AdditionalOrderReason);

        var audit = await _db.AuditLogs.SingleAsync(a =>
            a.Action == "ADDITIONAL_DIAGNOSTIC_ORDER_CREATED" &&
            a.EntityId == createdAdditional.OrderId.ToString());
        Assert.Equal("DIAGNOSTIC_ORDER", audit.EntityType);
        Assert.Contains("followUpToOrderCode", audit.ChangeData);
        Assert.Contains(initialOrder.OrderCode, audit.ChangeData!);
    }

    [Fact]
    public async Task CreateAdditionalDiagnosticOrder_RepeatSameService_RejectedWhilePendingOrDraft_AllowedAfterFinalized()
    {
        await _service.OpenWorkspaceAsync(3, "VIS-202610-0011");

        var firstResponse = await _service.CreateDiagnosticOrderAsync(
            3,
            "VIS-202610-0011",
            new CreateDiagnosticOrderRequest(
                ServiceId: 5,
                Instruction: "Công thức máu lần 1"));
        var firstOrder = Assert.Single(firstResponse.CreatedDiagnosticOrders!);

        // 1. While first order is ORDERED -> duplicate same service is rejected with 409
        await Assert.ThrowsAsync<ConflictException>(
            () => _service.CreateAdditionalDiagnosticOrderAsync(
                3,
                "VIS-202610-0011",
                new CreateAdditionalDiagnosticOrderRequest(
                    ServiceId: 5,
                    Instruction: "Làm lại công thức máu khi chưa có kết quả",
                    AdditionalOrderReason: "Kiểm tra lại")));

        // 2. While first order has a DRAFT result -> duplicate same service is still rejected with 409
        var draftResult = new DiagnosticResult
        {
            Id = 502,
            DiagnosticOrderId = firstOrder.OrderId,
            Status = "DRAFT",
        };
        _db.DiagnosticResults.Add(draftResult);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(
            () => _service.CreateAdditionalDiagnosticOrderAsync(
                3,
                "VIS-202610-0011",
                new CreateAdditionalDiagnosticOrderRequest(
                    ServiceId: 5,
                    Instruction: "Làm lại khi kết quả mới ở trạng thái nháp",
                    AdditionalOrderReason: "Kiểm tra lại")));

        // 3. Once the first order's result is FINALIZED -> repeat order for the same service is allowed and auto-linked
        draftResult.Status = "FINALIZED";
        draftResult.FinalizedAt = new DateTime(2026, 10, 22, 9, 12, 0);
        draftResult.FinalizedByStaffId = 1;
        await _db.SaveChangesAsync();

        var repeatResponse = await _service.CreateAdditionalDiagnosticOrderAsync(
            3,
            "VIS-202610-0011",
            new CreateAdditionalDiagnosticOrderRequest(
                ServiceId: 5,
                Instruction: "Xét nghiệm lại công thức máu để đối chiếu mẫu huyết tán",
                Priority: "STAT",
                AdditionalOrderReason: "Mẫu lần 1 nghi ngờ huyết tán nhẹ, cần kiểm tra lại"));

        Assert.Equal("ADDITIONAL_DIAGNOSTIC_ORDER_CREATED", repeatResponse.ActionOutcome);
        var repeatOrder = Assert.Single(repeatResponse.CreatedDiagnosticOrders!);
        Assert.Equal(5, repeatOrder.ServiceId);
        Assert.True(repeatOrder.IsAdditionalOrder);
        Assert.Equal(2, repeatOrder.OrderSequenceNo);
        Assert.Equal(firstOrder.OrderId, repeatOrder.FollowUpToOrderId);
        Assert.Equal(firstOrder.OrderCode, repeatOrder.FollowUpToOrderCode);
    }

    [Fact]
    public async Task CreateAdditionalDiagnosticOrder_InReturnConsultationWithTransitionToAwaitingResults_TransitionsVisitAndCompletesReturnQueueEntry()
    {
        // Seed a visit in RETURN_CONSULTATION (IN_PROGRESS) after round 1 diagnostic order completed
        var visit = await _db.Visits.FirstAsync(v => v.Id == 11);
        visit.Status = "IN_PROGRESS";

        var initialExamQueue = await _db.QueueEntries.FirstAsync(q => q.Id == 11);
        initialExamQueue.Status = "COMPLETED";
        initialExamQueue.CompletedAt = new DateTime(2026, 10, 22, 8, 25, 0);

        var round1DiagQueue = new QueueEntry
        {
            Id = 201,
            VisitId = 11,
            ServicePointId = 5,
            CurrentServicePointId = 5,
            QueueDate = new DateOnly(2026, 10, 22),
            QueueNumber = 1,
            QueueType = "DIAGNOSTIC",
            Status = "COMPLETED",
            PresenceStatus = "PRESENT",
            ArrivedAt = new DateTime(2026, 10, 22, 8, 26, 0),
            ReadyAt = new DateTime(2026, 10, 22, 8, 26, 0),
            CompletedAt = new DateTime(2026, 10, 22, 8, 40, 0),
            ArrivalClass = "WALK_IN",
            ManualPriority = "NORMAL",
            StateVersion = 2,
        };
        var round1Order = new DiagnosticOrder
        {
            Id = 301,
            OrderCode = "DIA-202610-0301",
            VisitId = 11,
            OrderingDoctorId = 1,
            ServiceId = 5,
            QueueEntryId = 201,
            Priority = "NORMAL",
            Status = "COMPLETED",
            RequiredForConclusion = true,
            OrderedAt = new DateTime(2026, 10, 22, 8, 26, 0),
        };
        var returnQueueRound1 = new QueueEntry
        {
            Id = 202,
            VisitId = 11,
            ServicePointId = 1,
            CurrentServicePointId = 1,
            QueueDate = new DateOnly(2026, 10, 22),
            QueueNumber = 8,
            QueueType = "RETURN_CONSULTATION",
            Status = "IN_PROGRESS",
            PresenceStatus = "PRESENT",
            ArrivedAt = new DateTime(2026, 10, 22, 8, 41, 0),
            ReadyAt = new DateTime(2026, 10, 22, 8, 41, 0),
            CalledAt = new DateTime(2026, 10, 22, 8, 44, 0),
            StartedAt = new DateTime(2026, 10, 22, 8, 45, 0),
            ArrivalClass = "WALK_IN",
            ManualPriority = "NORMAL",
            StateVersion = 2,
        };
        _db.QueueEntries.AddRange(round1DiagQueue, returnQueueRound1);
        _db.DiagnosticOrders.Add(round1Order);
        await _db.SaveChangesAsync();

        var response = await _service.CreateAdditionalDiagnosticOrderAsync(
            3,
            "VIS-202610-0011",
            new CreateAdditionalDiagnosticOrderRequest(
                ServiceId: 6,
                Instruction: "Siêu âm ổ bụng kiểm tra bổ sung sau đọc kết quả đợt 1",
                Priority: "URGENT",
                FollowUpToOrderId: 301,
                AdditionalOrderReason: "Kết quả xét nghiệm máu đợt 1 gợi ý viêm túi mật cấp",
                TransitionToAwaitingResults: true,
                ExpectedStateVersion: 2));

        Assert.Equal("ADDITIONAL_DIAGNOSTIC_ORDER_CREATED", response.ActionOutcome);
        Assert.Equal("COMPLETED", response.WorkspaceState);
        var createdOrder = Assert.Single(response.CreatedDiagnosticOrders!);
        Assert.Equal(301, createdOrder.FollowUpToOrderId);
        Assert.Equal("DIA-202610-0301", createdOrder.FollowUpToOrderCode);

        var updatedVisit = await _db.Visits.FirstAsync(v => v.Id == 11);
        Assert.Equal("AWAITING_RESULTS", updatedVisit.Status);

        var updatedReturnQueue = await _db.QueueEntries.FirstAsync(q => q.Id == 202);
        Assert.Equal("COMPLETED", updatedReturnQueue.Status);
        Assert.NotNull(updatedReturnQueue.CompletedAt);
        Assert.Equal(3, updatedReturnQueue.StateVersion);
    }

    [Fact]
    public async Task CreateAdditionalDiagnosticOrder_ValidationAuthNotFoundAndConflictGuards_ThrowsExpectedExceptions()
    {
        await _service.OpenWorkspaceAsync(3, "VIS-202610-0011");

        // 1. 409 Conflict when visit has NO prior diagnostic orders, NO recorded clinical exam, and NO justification
        var exNoPrereq = await Assert.ThrowsAsync<ConflictException>(
            () => _service.CreateAdditionalDiagnosticOrderAsync(
                3,
                "VIS-202610-0011",
                new CreateAdditionalDiagnosticOrderRequest(ServiceId: 5)));
        Assert.Contains("UC33", exNoPrereq.Message);

        // 2. 400 ValidationException on invalid additional-order fields
        await Assert.ThrowsAsync<ValidationException>(
            () => _service.CreateAdditionalDiagnosticOrderAsync(3, "VIS-202610-0011", null!));

        await Assert.ThrowsAsync<ValidationException>(
            () => _service.CreateAdditionalDiagnosticOrderAsync(
                3,
                "VIS-202610-0011",
                new CreateAdditionalDiagnosticOrderRequest(ServiceId: 5, FollowUpToOrderId: 0)));

        await Assert.ThrowsAsync<ValidationException>(
            () => _service.CreateAdditionalDiagnosticOrderAsync(
                3,
                "VIS-202610-0011",
                new CreateAdditionalDiagnosticOrderRequest(ServiceId: 5, FollowUpToOrderCode: "<script>")));

        await Assert.ThrowsAsync<ValidationException>(
            () => _service.CreateAdditionalDiagnosticOrderAsync(
                3,
                "VIS-202610-0011",
                new CreateAdditionalDiagnosticOrderRequest(
                    ServiceId: 5,
                    AdditionalOrderReason: "Lý do <script>")));

        await Assert.ThrowsAsync<ValidationException>(
            () => _service.CreateAdditionalDiagnosticOrderAsync(
                3,
                "VIS-202610-0011",
                new CreateAdditionalDiagnosticOrderRequest(
                    ServiceId: 5,
                    ClinicalProgressionNote: "Diễn biến <script>")));

        await Assert.ThrowsAsync<ValidationException>(
            () => _service.CreateAdditionalDiagnosticOrderAsync(
                3,
                "VIS-202610-0011",
                new CreateAdditionalDiagnosticOrderRequest(
                    ServiceId: 5,
                    ExpectedRevisionNo: 0)));

        // 3. 404 NotFoundException when FollowUpToOrderId / FollowUpToOrderCode does not exist in visit
        await Assert.ThrowsAsync<NotFoundException>(
            () => _service.CreateAdditionalDiagnosticOrderAsync(
                3,
                "VIS-202610-0011",
                new CreateAdditionalDiagnosticOrderRequest(
                    ServiceId: 5,
                    FollowUpToOrderId: 9999,
                    AdditionalOrderReason: "Chỉ định bổ sung")));

        await Assert.ThrowsAsync<NotFoundException>(
            () => _service.CreateAdditionalDiagnosticOrderAsync(
                3,
                "VIS-202610-0011",
                new CreateAdditionalDiagnosticOrderRequest(
                    ServiceId: 5,
                    FollowUpToOrderCode: "DIA-202610-9999",
                    AdditionalOrderReason: "Chỉ định bổ sung")));

        // 4. Seed a cancelled order and a valid order to test mismatched ID/Code and cancelled follow-up guard
        _db.DiagnosticOrders.AddRange(
            new DiagnosticOrder
            {
                Id = 401,
                OrderCode = "DIA-202610-0401",
                VisitId = 11,
                OrderingDoctorId = 1,
                ServiceId = 5,
                Priority = "NORMAL",
                Status = "CANCELLED",
                RequiredForConclusion = true,
                OrderedAt = new DateTime(2026, 10, 22, 8, 35, 0),
            },
            new DiagnosticOrder
            {
                Id = 402,
                OrderCode = "DIA-202610-0402",
                VisitId = 11,
                OrderingDoctorId = 1,
                ServiceId = 5,
                Priority = "NORMAL",
                Status = "COMPLETED",
                RequiredForConclusion = true,
                OrderedAt = new DateTime(2026, 10, 22, 8, 40, 0),
            });
        await _db.SaveChangesAsync();

        // Mismatched FollowUpToOrderId (401) and FollowUpToOrderCode (DIA-202610-0402) -> 400 ValidationException
        await Assert.ThrowsAsync<ValidationException>(
            () => _service.CreateAdditionalDiagnosticOrderAsync(
                3,
                "VIS-202610-0011",
                new CreateAdditionalDiagnosticOrderRequest(
                    ServiceId: 6,
                    FollowUpToOrderId: 401,
                    FollowUpToOrderCode: "DIA-202610-0402",
                    AdditionalOrderReason: "Chỉ định bổ sung")));

        // Referencing a CANCELLED order -> 409 ConflictException
        await Assert.ThrowsAsync<ConflictException>(
            () => _service.CreateAdditionalDiagnosticOrderAsync(
                3,
                "VIS-202610-0011",
                new CreateAdditionalDiagnosticOrderRequest(
                    ServiceId: 6,
                    FollowUpToOrderId: 401,
                    AdditionalOrderReason: "Chỉ định bổ sung từ phiếu đã hủy")));

        // ExpectedRevisionNo mismatch -> 409 ConflictException
        await Assert.ThrowsAsync<ConflictException>(
            () => _service.CreateAdditionalDiagnosticOrderAsync(
                3,
                "VIS-202610-0011",
                new CreateAdditionalDiagnosticOrderRequest(
                    ServiceId: 6,
                    FollowUpToOrderId: 402,
                    AdditionalOrderReason: "Chỉ định bổ sung",
                    ExpectedRevisionNo: 99)));

        // Missing MEDICAL_RECORD_WRITE when ClinicalProgressionNote is supplied -> 403 ForbiddenException
        var writePerm = await _db.RolePermissions
            .FirstAsync(rp => rp.RoleId == 3 && rp.PermissionId == 1);
        _db.RolePermissions.Remove(writePerm);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _service.CreateAdditionalDiagnosticOrderAsync(
                3,
                "VIS-202610-0011",
                new CreateAdditionalDiagnosticOrderRequest(
                    ServiceId: 6,
                    FollowUpToOrderId: 402,
                    AdditionalOrderReason: "Chỉ định bổ sung",
                    ClinicalProgressionNote: "Cập nhật diễn biến nhưng không có quyền ghi bệnh án")));
    }
}
