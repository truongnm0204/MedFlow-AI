using MedFlow.Application.DTOs.Patients;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Patients;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.UnitTests.Patients;

public sealed class PatientPrescriptionServiceTests : IDisposable
{
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingAuditLogger : IAuditLogger
    {
        public List<string> Actions { get; } = [];

        public void Add(long? actorUserId, string action, string entityType, string? entityId, object? changeData, string? ipAddress)
            => Actions.Add(action);
    }

    private sealed class PrescriptionTestDbContext(DbContextOptions<PrescriptionTestDbContext> options)
        : DbContext(options), IApplicationDbContext
    {
        public DbSet<User> Users => Set<User>();
        public DbSet<Patient> Patients => Set<Patient>();
        public DbSet<Staff> Staff => Set<Staff>();
        public DbSet<Doctor> Doctors => Set<Doctor>();
        public DbSet<Hospital> Hospitals => Set<Hospital>();
        public DbSet<Visit> Visits => Set<Visit>();
        public DbSet<Medicine> Medicines => Set<Medicine>();
        public DbSet<Prescription> Prescriptions => Set<Prescription>();
        public DbSet<PrescriptionItem> PrescriptionItems => Set<PrescriptionItem>();

        public DbSet<Role> Roles => throw NotUsed();
        public DbSet<Permission> Permissions => throw NotUsed();
        public DbSet<UserRole> UserRoles => throw NotUsed();
        public DbSet<RolePermission> RolePermissions => throw NotUsed();
        public DbSet<DoctorServiceAuthorization> DoctorServiceAuthorizations => throw NotUsed();
        public DbSet<ExaminationSlot> ExaminationSlots => throw NotUsed();
        public DbSet<AuthSession> AuthSessions => throw NotUsed();
        public DbSet<AuthVerificationChallenge> AuthVerificationChallenges => throw NotUsed();
        public DbSet<AuditLog> AuditLogs => throw NotUsed();
        public DbSet<Department> Departments => throw NotUsed();
        public DbSet<HospitalRoom> HospitalRooms => throw NotUsed();
        public DbSet<Specialty> Specialties => throw NotUsed();
        public DbSet<Service> Services => throw NotUsed();
        public DbSet<ServicePrice> ServicePrices => throw NotUsed();
        public DbSet<HospitalPolicy> HospitalPolicies => throw NotUsed();
        public DbSet<Appointment> Appointments => throw NotUsed();
        public DbSet<PatientFamilyLink> PatientFamilyLinks => throw NotUsed();
        public DbSet<QueueEntry> QueueEntries => throw NotUsed();
        public DbSet<Payment> Payments => throw NotUsed();

        public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default)
            => Database.BeginTransactionAsync(ct);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Ignore<Role>();
            modelBuilder.Ignore<Permission>();
            modelBuilder.Ignore<UserRole>();
            modelBuilder.Ignore<RolePermission>();
            modelBuilder.Ignore<DoctorServiceAuthorization>();
            modelBuilder.Ignore<ExaminationSlot>();
            modelBuilder.Ignore<AuthSession>();
            modelBuilder.Ignore<AuthVerificationChallenge>();
            modelBuilder.Ignore<AuditLog>();
            modelBuilder.Ignore<Department>();
            modelBuilder.Ignore<HospitalRoom>();
            modelBuilder.Ignore<Specialty>();
            modelBuilder.Ignore<Service>();
            modelBuilder.Ignore<ServicePrice>();
            modelBuilder.Ignore<HospitalPolicy>();
            modelBuilder.Ignore<Appointment>();
            modelBuilder.Ignore<PatientFamilyLink>();
            modelBuilder.Ignore<QueueEntry>();
            modelBuilder.Ignore<Payment>();

            modelBuilder.Entity<User>(entity =>
            {
                entity.HasKey(item => item.Id);
                entity.Ignore(item => item.UserRoles);
                entity.Ignore(item => item.Patient);
                entity.Ignore(item => item.Staff);
                entity.Ignore(item => item.AuthSessions);
            });
            modelBuilder.Entity<Patient>(entity =>
            {
                entity.HasKey(item => item.Id);
                entity.Ignore(item => item.User);
            });
            modelBuilder.Entity<Staff>(entity =>
            {
                entity.HasKey(item => item.Id);
                entity.Ignore(item => item.User);
                entity.Ignore(item => item.Department);
                entity.Ignore(item => item.Doctor);
            });
            modelBuilder.Entity<Doctor>(entity =>
            {
                entity.HasKey(item => item.Id);
                entity.Ignore(item => item.Staff);
                entity.Ignore(item => item.Specialty);
                entity.Ignore(item => item.ServiceAuthorizations);
                entity.Ignore(item => item.ExaminationSlots);
            });
            modelBuilder.Entity<Hospital>().HasKey(entity => entity.Id);
            modelBuilder.Entity<Visit>().HasKey(entity => entity.Id);
            modelBuilder.Entity<Medicine>().HasKey(entity => entity.Id);
            modelBuilder.Entity<Prescription>().HasKey(entity => entity.Id);
            modelBuilder.Entity<PrescriptionItem>().HasKey(entity => entity.Id);
        }

        private static NotSupportedException NotUsed()
            => new("DbSet này không được dùng trong PatientPrescriptionServiceTests.");
    }

    private static readonly DateTime NowUtc = new(2026, 11, 16, 10, 0, 0, DateTimeKind.Utc);
    private readonly PrescriptionTestDbContext _db;
    private readonly RecordingAuditLogger _audit = new();
    private readonly PatientPrescriptionService _sut;

    public PatientPrescriptionServiceTests()
    {
        var options = new DbContextOptionsBuilder<PrescriptionTestDbContext>()
            .UseInMemoryDatabase($"patient-prescription-{Guid.NewGuid():N}")
            .Options;
        _db = new PrescriptionTestDbContext(options);
        _sut = new PatientPrescriptionService(_db, _audit, new FixedTimeProvider(new DateTimeOffset(NowUtc)));
    }

    [Fact]
    public async Task GetMyPrescriptionAsync_OwnIssuedPrescription_ReturnsMedicineInstructionsAndAudits()
    {
        await SeedActorAsync();
        await SeedPrescriptionAsync(101, 1, "RX-OWN", "ISSUED", "Uống sau ăn", doctorId: 30);

        var result = await _sut.GetMyPrescriptionAsync(10, "rx-own", ipAddress: "127.0.0.1");

        Assert.Equal("RX-OWN", result.Prescription.PrescriptionCode);
        Assert.Equal("Bác sĩ An", result.Prescription.PrescribingDoctorName);
        Assert.Equal("Bệnh viện MedFlow", result.Prescription.HospitalName);
        var medicine = Assert.Single(result.Items);
        Assert.Equal("Paracetamol", medicine.MedicineName);
        Assert.Equal("Uống sau ăn", medicine.Instructions);
        Assert.Contains("PATIENT_PRESCRIPTION_VIEWED", _audit.Actions);
    }

    [Fact]
    public async Task GetMyPrescriptionsAsync_FiltersDraftForeignAndReplacedPrescription()
    {
        await SeedActorAsync();
        await SeedPrescriptionAsync(101, 1, "RX-OLD", "ISSUED", "Cũ");
        await SeedPrescriptionAsync(102, 1, "RX-NEW", "READY", "Mới", replacesPrescriptionId: 101);
        await SeedPrescriptionAsync(103, 1, "RX-DRAFT", "DRAFT", "Nháp");
        await SeedPrescriptionAsync(104, 2, "RX-FOREIGN", "ISSUED", "Không được lộ");

        var result = await _sut.GetMyPrescriptionsAsync(10, new GetPatientPrescriptionsRequest());

        var item = Assert.Single(result.Items);
        Assert.Equal("RX-NEW", item.PrescriptionCode);
        Assert.Equal(1, result.TotalCount);
        Assert.Contains("PATIENT_PRESCRIPTIONS_VIEWED", _audit.Actions);
    }

    [Fact]
    public async Task GetMyPrescriptionAsync_ForeignPrescription_ReturnsNotFound()
    {
        await SeedActorAsync();
        await SeedPrescriptionAsync(104, 2, "RX-FOREIGN", "ISSUED", "Không được lộ");

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _sut.GetMyPrescriptionAsync(10, "RX-FOREIGN"));
    }

    [Fact]
    public async Task GetMyPrescriptionAsync_BlankIdentifier_ThrowsValidation()
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.GetMyPrescriptionAsync(10, "  #  "));
    }

    [Fact]
    public async Task GetMyPrescriptionsAsync_LockedPatientAccount_ThrowsUnauthorized()
    {
        await SeedActorAsync(lockedUntil: NowUtc.AddMinutes(5));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            _sut.GetMyPrescriptionsAsync(10, new GetPatientPrescriptionsRequest()));
    }

    private async Task SeedActorAsync(DateTime? lockedUntil = null)
    {
        _db.Users.Add(new User
        {
            Id = 10,
            Email = "patient@medflow.test",
            AccountStatus = AccountStatus.ACTIVE,
            LockedUntil = lockedUntil,
        });
        _db.Patients.AddRange(
            new Patient { Id = 1, UserId = 10, PatientCode = "PAT-1", FullName = "Nguyễn An", Status = "ACTIVE" },
            new Patient { Id = 2, PatientCode = "PAT-2", FullName = "Người khác", Status = "ACTIVE" });
        _db.Hospitals.Add(new Hospital { Id = 20, HospitalCode = "H20", Name = "Bệnh viện MedFlow", IsActive = true });
        _db.Staff.Add(new Staff { Id = 40, HospitalId = 20, StaffCode = "BS40", FullName = "Bác sĩ An", Status = "ACTIVE" });
        _db.Doctors.Add(new Doctor { Id = 30, StaffId = 40, HospitalId = 20, DoctorCode = "DR30", IsActive = true });
        _db.Medicines.Add(new Medicine
        {
            Id = 50,
            HospitalId = 20,
            MedicineCode = "MED-50",
            Name = "Paracetamol",
            Strength = "500 mg",
            DispensingUnit = "Viên",
            IsActive = true,
        });
        await _db.SaveChangesAsync();
    }

    private async Task SeedPrescriptionAsync(
        long id,
        long patientId,
        string code,
        string status,
        string instructions,
        long? doctorId = null,
        long? replacesPrescriptionId = null)
    {
        _db.Visits.Add(new Visit
        {
            Id = id,
            VisitCode = $"VIS-{id}",
            PatientId = patientId,
            AssignedDoctorId = doctorId,
            Status = "COMPLETED",
        });
        _db.Prescriptions.Add(new Prescription
        {
            Id = id,
            PrescriptionCode = code,
            VisitId = id,
            PrescribingDoctorId = doctorId,
            Status = status,
            IssuedAt = NowUtc.AddMinutes(id),
            ReplacesPrescriptionId = replacesPrescriptionId,
        });
        _db.PrescriptionItems.Add(new PrescriptionItem
        {
            Id = id,
            PrescriptionId = id,
            MedicineId = 50,
            PrescribedQuantity = 10,
            SingleDoseQuantity = 1,
            SingleDoseUnit = "Viên",
            Frequency = "2 lần/ngày",
            DurationDays = 5,
            AdministrationRoute = "Uống",
            Instructions = instructions,
        });
        await _db.SaveChangesAsync();
    }

    public void Dispose() => _db.Dispose();
}
