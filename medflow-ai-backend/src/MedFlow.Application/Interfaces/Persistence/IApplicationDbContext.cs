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
    DbSet<PaymentAllocation> PaymentAllocations => throw new NotSupportedException("PaymentAllocations DbSet is not configured in this test context.");
    DbSet<PaymentGatewayEvent> PaymentGatewayEvents => throw new NotSupportedException("PaymentGatewayEvents DbSet is not configured in this test context.");
    DbSet<VisitCharge> VisitCharges => throw new NotSupportedException("VisitCharges DbSet is not configured in this test context.");
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
    DbSet<Notification> Notifications => throw new NotSupportedException("Notifications DbSet is not configured in this test context.");

    Task<int> SaveChangesAsync(CancellationToken ct = default);
    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default);

    // ADR-0059 16-Level Global Lock Hierarchy (01 -> 16).
    // All relational implementations require an active transaction, sort multi-ID sets ASC,
    // and return true iff all requested IDs exist and were locked FOR UPDATE.

    // Level 01: users
    Task<bool> TryLockUserForUpdateAsync(long userId, CancellationToken ct = default)
        => Task.FromResult(false);

    // Level 02: patients
    Task<bool> TryLockPatientForUpdateAsync(long patientId, CancellationToken ct = default)
        => Task.FromResult(false);
    Task<bool> TryLockPatientsForUpdateAsync(IReadOnlyCollection<long> patientIds, CancellationToken ct = default)
        => Task.FromResult(false);

    // Level 03: patient_identity_claims
    Task<bool> TryLockIdentityClaimForUpdateAsync(long claimId, CancellationToken ct = default)
        => Task.FromResult(false);

    // Level 04: services
    Task<bool> TryLockServiceForUpdateAsync(long serviceId, CancellationToken ct = default)
        => Task.FromResult(false);

    // Level 05: doctors
    Task<bool> TryLockDoctorsForUpdateAsync(IReadOnlyCollection<long> doctorIds, CancellationToken ct = default)
        => Task.FromResult(false);

    // Level 06: hospital_rooms (physical v4.1 table name)
    Task<bool> TryLockRoomForUpdateAsync(long roomId, CancellationToken ct = default)
        => Task.FromResult(false);
    Task<bool> TryLockRoomsForUpdateAsync(IReadOnlyCollection<long> roomIds, CancellationToken ct = default)
        => Task.FromResult(false);

    // Level 07: examination_slots
    Task<bool> TryLockSlotsForUpdateAsync(IReadOnlyCollection<long> slotIds, CancellationToken ct = default)
        => Task.FromResult(false);

    // Level 08: appointments
    Task<bool> TryLockAppointmentForUpdateAsync(long appointmentId, CancellationToken ct = default)
        => Task.FromResult(false);
    Task<bool> TryLockAppointmentsForUpdateAsync(IReadOnlyCollection<long> appointmentIds, CancellationToken ct = default)
        => Task.FromResult(false);

    // Level 09: visits (Clinical Aggregate Root Lock)
    Task<bool> TryLockVisitForUpdateAsync(long visitId, CancellationToken ct = default)
        => Task.FromResult(false);

    // Level 10: queue_service_points + deterministic unrouted queue advisory lock
    Task<bool> TryLockServicePointForUpdateAsync(long servicePointId, CancellationToken ct = default)
        => Task.FromResult(false);
    Task<bool> TryLockServicePointsForUpdateAsync(IReadOnlyCollection<long> servicePointIds, CancellationToken ct = default)
        => Task.FromResult(false);
    Task<bool> TryLockUnroutedQueueAdvisoryAsync(long hospitalId, DateOnly queueDate, CancellationToken ct = default)
        => Task.FromResult(false);

    // Level 11: queue_entries
    Task<bool> TryLockQueueEntryForUpdateAsync(long queueEntryId, CancellationToken ct = default)
        => Task.FromResult(false);
    Task<bool> TryLockQueueEntriesForUpdateAsync(IReadOnlyCollection<long> queueEntryIds, CancellationToken ct = default)
        => Task.FromResult(false);

    // Level 12: diagnostic_orders
    Task<bool> TryLockDiagnosticOrderForUpdateAsync(long diagnosticOrderId, CancellationToken ct = default)
        => Task.FromResult(false);
    Task<bool> TryLockDiagnosticOrdersForUpdateAsync(IReadOnlyCollection<long> diagnosticOrderIds, CancellationToken ct = default)
        => Task.FromResult(false);

    // Level 13: prescriptions
    Task<bool> TryLockPrescriptionForUpdateAsync(long prescriptionId, CancellationToken ct = default)
        => Task.FromResult(false);

    // Level 14: medicine_batches
    Task<bool> TryLockMedicineBatchesForUpdateAsync(IReadOnlyCollection<long> batchIds, CancellationToken ct = default)
        => Task.FromResult(false);

    // Level 15: visit_charges
    Task<bool> TryLockVisitChargeForUpdateAsync(long visitChargeId, CancellationToken ct = default)
        => Task.FromResult(false);
    Task<bool> TryLockVisitChargesForUpdateAsync(IReadOnlyCollection<long> visitChargeIds, CancellationToken ct = default)
        => Task.FromResult(false);

    // Level 16: payments
    Task<bool> TryLockPaymentForUpdateAsync(long paymentId, CancellationToken ct = default)
        => Task.FromResult(false);
    Task<bool> TryLockPaymentsForUpdateAsync(IReadOnlyCollection<long> paymentIds, CancellationToken ct = default)
        => Task.FromResult(false);

    // Level 16b: payment_allocations (ADR-0060)
    Task<bool> TryLockPaymentAllocationForUpdateAsync(long allocationId, CancellationToken ct = default)
        => Task.FromResult(false);
    Task<bool> TryLockPaymentAllocationsForUpdateAsync(IReadOnlyCollection<long> allocationIds, CancellationToken ct = default)
        => Task.FromResult(false);
}
