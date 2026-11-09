using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.Infrastructure.Persistence;

public class MedFlowDbContext : DbContext, IApplicationDbContext
{
    public MedFlowDbContext(DbContextOptions<MedFlowDbContext> options)
        : base(options)
    {
    }

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
    public DbSet<AuthSession> AuthSessions => Set<AuthSession>();
    public DbSet<AuthVerificationChallenge> AuthVerificationChallenges => Set<AuthVerificationChallenge>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Hospital> Hospitals => Set<Hospital>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<HospitalRoom> HospitalRooms => Set<HospitalRoom>();
    public DbSet<Specialty> Specialties => Set<Specialty>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<ServicePrice> ServicePrices => Set<ServicePrice>();
    public DbSet<HospitalPolicy> HospitalPolicies => Set<HospitalPolicy>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<PatientFamilyLink> PatientFamilyLinks => Set<PatientFamilyLink>();
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
    public DbSet<QueueServicePoint> QueueServicePoints => Set<QueueServicePoint>();
    public DbSet<QueueFairnessState> QueueFairnessStates => Set<QueueFairnessState>();

    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default)
        => Database.BeginTransactionAsync(ct);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        // v4.1 schema lives in `public` — EF default for Npgsql already is public.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MedFlowDbContext).Assembly);
    }
}
