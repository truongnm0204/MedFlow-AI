using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.Application.Interfaces.Persistence;

public interface IApplicationDbContext
{
    DbSet<User> Users { get; }
    DbSet<Role> Roles { get; }
    DbSet<Permission> Permissions { get; }
    DbSet<UserRole> UserRoles { get; }
    DbSet<RolePermission> RolePermissions { get; }
    DbSet<Patient> Patients { get; }
    DbSet<Staff> Staff { get; }
    DbSet<Doctor> Doctors { get; }
    DbSet<DoctorServiceAuthorization> DoctorServiceAuthorizations { get; }
    DbSet<ExaminationSlot> ExaminationSlots { get; }
    DbSet<AuthSession> AuthSessions { get; }
    DbSet<AuthVerificationChallenge> AuthVerificationChallenges { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<Hospital> Hospitals { get; }
    DbSet<Department> Departments { get; }
    DbSet<HospitalRoom> HospitalRooms { get; }
    DbSet<Specialty> Specialties { get; }
    DbSet<Service> Services { get; }
    DbSet<ServicePrice> ServicePrices { get; }
    DbSet<HospitalPolicy> HospitalPolicies { get; }
    DbSet<Appointment> Appointments { get; }
    DbSet<PatientFamilyLink> PatientFamilyLinks { get; }
    DbSet<Visit> Visits { get; }
    DbSet<QueueEntry> QueueEntries { get; }
    DbSet<Payment> Payments { get; }
    DbSet<Medicine> Medicines => throw new NotSupportedException("Medicines DbSet is not configured in this test context.");
    DbSet<MedicineBatch> MedicineBatches => throw new NotSupportedException("MedicineBatches DbSet is not configured in this test context.");
    DbSet<Prescription> Prescriptions => throw new NotSupportedException("Prescriptions DbSet is not configured in this test context.");
    DbSet<PrescriptionItem> PrescriptionItems => throw new NotSupportedException("PrescriptionItems DbSet is not configured in this test context.");
    DbSet<MedicationDispense> MedicationDispenses => throw new NotSupportedException("MedicationDispenses DbSet is not configured in this test context.");
    DbSet<DispenseItem> DispenseItems => throw new NotSupportedException("DispenseItems DbSet is not configured in this test context.");
    DbSet<StockTransaction> StockTransactions => throw new NotSupportedException("StockTransactions DbSet is not configured in this test context.");
    DbSet<QueueServicePoint> QueueServicePoints => throw new NotSupportedException("QueueServicePoints DbSet is not configured in this test context.");
    DbSet<QueueFairnessState> QueueFairnessStates => throw new NotSupportedException("QueueFairnessStates DbSet is not configured in this test context.");
    DbSet<MedicalRecord> MedicalRecords => throw new NotSupportedException("MedicalRecords DbSet is not configured in this test context.");
    DbSet<MedicalRecordRevision> MedicalRecordRevisions => throw new NotSupportedException("MedicalRecordRevisions DbSet is not configured in this test context.");
    DbSet<VisitDiagnosis> VisitDiagnoses => throw new NotSupportedException("VisitDiagnoses DbSet is not configured in this test context.");
    DbSet<Icd10Code> Icd10Codes => throw new NotSupportedException("Icd10Codes DbSet is not configured in this test context.");
    DbSet<ClinicalAddendum> ClinicalAddenda => throw new NotSupportedException("ClinicalAddenda DbSet is not configured in this test context.");
    DbSet<DiagnosticOrder> DiagnosticOrders => throw new NotSupportedException("DiagnosticOrders DbSet is not configured in this test context.");
    DbSet<DiagnosticResult> DiagnosticResults => throw new NotSupportedException("DiagnosticResults DbSet is not configured in this test context.");
    DbSet<DiagnosticResultVersion> DiagnosticResultVersions => throw new NotSupportedException("DiagnosticResultVersions DbSet is not configured in this test context.");

    Task<int> SaveChangesAsync(CancellationToken ct = default);
    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default);
}
