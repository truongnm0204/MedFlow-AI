using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.Infrastructure.Persistence;

public class MedFlowDbContext : DbContext, IApplicationDbContext
{
    private enum LockTargetTable
    {
        Users = 1,
        Patients = 2,
        PatientIdentityClaims = 3,
        Services = 4,
        Doctors = 5,
        HospitalRooms = 6,
        ExaminationSlots = 7,
        Appointments = 8,
        Visits = 9,
        QueueServicePoints = 10,
        QueueEntries = 11,
        DiagnosticOrders = 12,
        Prescriptions = 13,
        MedicineBatches = 14,
        VisitCharges = 15,
        Payments = 16,
        PaymentAllocations = 17,
    }

    public MedFlowDbContext(DbContextOptions<MedFlowDbContext> options) : base(options) { }

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
    public DbSet<PaymentAllocation> PaymentAllocations => Set<PaymentAllocation>();
    public DbSet<PatientIdentityClaim> PatientIdentityClaims => Set<PatientIdentityClaim>();
    public DbSet<PaymentGatewayEvent> PaymentGatewayEvents => Set<PaymentGatewayEvent>();
    public DbSet<VisitCharge> VisitCharges => Set<VisitCharge>();
    public DbSet<Medicine> Medicines => Set<Medicine>();
    public DbSet<MedicineBatch> MedicineBatches => Set<MedicineBatch>();
    public DbSet<Prescription> Prescriptions => Set<Prescription>();
    public DbSet<PrescriptionItem> PrescriptionItems => Set<PrescriptionItem>();
    public DbSet<MedicationDispense> MedicationDispenses => Set<MedicationDispense>();
    public DbSet<DispenseItem> DispenseItems => Set<DispenseItem>();
    public DbSet<StockTransaction> StockTransactions => Set<StockTransaction>();
    public DbSet<QueueServicePoint> QueueServicePoints => Set<QueueServicePoint>();
    public DbSet<QueueFairnessState> QueueFairnessStates => Set<QueueFairnessState>();
    public DbSet<MedicalRecord> MedicalRecords => Set<MedicalRecord>();
    public DbSet<MedicalRecordRevision> MedicalRecordRevisions => Set<MedicalRecordRevision>();
    public DbSet<VisitDiagnosis> VisitDiagnoses => Set<VisitDiagnosis>();
    public DbSet<Icd10Code> Icd10Codes => Set<Icd10Code>();
    public DbSet<ClinicalAddendum> ClinicalAddenda => Set<ClinicalAddendum>();
    public DbSet<DiagnosticOrder> DiagnosticOrders => Set<DiagnosticOrder>();
    public DbSet<DiagnosticResult> DiagnosticResults => Set<DiagnosticResult>();
    public DbSet<DiagnosticResultVersion> DiagnosticResultVersions => Set<DiagnosticResultVersion>();
    public DbSet<Notification> Notifications => Set<Notification>();

    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default)
        => Database.BeginTransactionAsync(ct);

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        NormalizeBillingEntities();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        NormalizeBillingEntities();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void NormalizeBillingEntities()
    {
        var nowVietnam = DateTime.SpecifyKind(DateTime.UtcNow.AddHours(7), DateTimeKind.Unspecified);

        foreach (var entry in ChangeTracker.Entries<VisitCharge>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                if (entry.Entity.DiagnosticOrderId.HasValue)
                {
                    entry.Entity.ChargeType = VisitChargeTypes.Diagnostic;
                }
                else if (string.IsNullOrWhiteSpace(entry.Entity.ChargeType))
                {
                    entry.Entity.ChargeType = VisitChargeTypes.Examination;
                }

                if (string.IsNullOrWhiteSpace(entry.Entity.Status))
                {
                    entry.Entity.Status = VisitChargeStatuses.Active;
                }

                if (entry.State == EntityState.Added && entry.Entity.CreatedAt is null)
                {
                    entry.Entity.CreatedAt = nowVietnam;
                }
            }
        }

        foreach (var entry in ChangeTracker.Entries<Payment>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                if (string.Equals(entry.Entity.PaymentScope, PaymentScopes.Allocated, StringComparison.OrdinalIgnoreCase))
                {
                    entry.Entity.PaymentScope = PaymentScopes.Allocated;
                }
                else if (entry.Entity.VisitChargeId.HasValue)
                {
                    entry.Entity.PaymentScope = PaymentScopes.Charge;
                }
                else if (string.IsNullOrWhiteSpace(entry.Entity.PaymentScope))
                {
                    entry.Entity.PaymentScope = PaymentScopes.LegacyVisit;
                }
            }
        }

        foreach (var entry in ChangeTracker.Entries<PaymentAllocation>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.Status = string.IsNullOrWhiteSpace(entry.Entity.Status)
                    ? PaymentAllocationStatuses.Pending
                    : entry.Entity.Status.Trim().ToUpperInvariant();

                if (entry.State == EntityState.Added && entry.Entity.CreatedAt == default)
                {
                    entry.Entity.CreatedAt = nowVietnam;
                }
            }
        }

        foreach (var entry in ChangeTracker.Entries<DiagnosticOrder>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.SelectionStatus = string.IsNullOrWhiteSpace(entry.Entity.SelectionStatus)
                    ? DiagnosticSelectionStatuses.Accepted
                    : entry.Entity.SelectionStatus.Trim().ToUpperInvariant();
            }
        }
    }

    /// <summary>
    /// Computes a deterministic 64-bit PostgreSQL advisory lock key from SHA-256 (big-endian)
    /// that is identical across all OSes, .NET versions, and horizontally scaled API containers (ADR-0059).
    /// </summary>
    public static long ComputeDeterministicAdvisoryLockKey(string domain, string scopeKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeKey);

        Span<byte> digest = stackalloc byte[32];
        var byteCount = Encoding.UTF8.GetByteCount($"medflow:{domain}:{scopeKey}");
        var buffer = byteCount <= 256 ? stackalloc byte[byteCount] : new byte[byteCount];
        Encoding.UTF8.GetBytes($"medflow:{domain}:{scopeKey}", buffer);
        SHA256.HashData(buffer, digest);
        return BinaryPrimitives.ReadInt64BigEndian(digest[..8]);
    }

    public Task<bool> TryLockUserForUpdateAsync(long userId, CancellationToken ct = default)
        => LockRowsForUpdateAsync(LockTargetTable.Users, [userId], ct);

    public Task<bool> TryLockPatientForUpdateAsync(long patientId, CancellationToken ct = default)
        => LockRowsForUpdateAsync(LockTargetTable.Patients, [patientId], ct);

    public Task<bool> TryLockPatientsForUpdateAsync(IReadOnlyCollection<long> patientIds, CancellationToken ct = default)
        => LockRowsForUpdateAsync(LockTargetTable.Patients, patientIds, ct);

    public Task<bool> TryLockIdentityClaimForUpdateAsync(long claimId, CancellationToken ct = default)
        => LockRowsForUpdateAsync(LockTargetTable.PatientIdentityClaims, [claimId], ct);

    public Task<bool> TryLockServiceForUpdateAsync(long serviceId, CancellationToken ct = default)
        => LockRowsForUpdateAsync(LockTargetTable.Services, [serviceId], ct);

    public Task<bool> TryLockDoctorsForUpdateAsync(IReadOnlyCollection<long> doctorIds, CancellationToken ct = default)
        => LockRowsForUpdateAsync(LockTargetTable.Doctors, doctorIds, ct);

    public Task<bool> TryLockRoomForUpdateAsync(long roomId, CancellationToken ct = default)
        => LockRowsForUpdateAsync(LockTargetTable.HospitalRooms, [roomId], ct);

    public Task<bool> TryLockRoomsForUpdateAsync(IReadOnlyCollection<long> roomIds, CancellationToken ct = default)
        => LockRowsForUpdateAsync(LockTargetTable.HospitalRooms, roomIds, ct);

    public Task<bool> TryLockSlotsForUpdateAsync(IReadOnlyCollection<long> slotIds, CancellationToken ct = default)
        => LockRowsForUpdateAsync(LockTargetTable.ExaminationSlots, slotIds, ct);

    public Task<bool> TryLockAppointmentForUpdateAsync(long appointmentId, CancellationToken ct = default)
        => LockRowsForUpdateAsync(LockTargetTable.Appointments, [appointmentId], ct);

    public Task<bool> TryLockAppointmentsForUpdateAsync(IReadOnlyCollection<long> appointmentIds, CancellationToken ct = default)
        => LockRowsForUpdateAsync(LockTargetTable.Appointments, appointmentIds, ct);

    public Task<bool> TryLockVisitForUpdateAsync(long visitId, CancellationToken ct = default)
        => LockRowsForUpdateAsync(LockTargetTable.Visits, [visitId], ct);

    public Task<bool> TryLockServicePointForUpdateAsync(long servicePointId, CancellationToken ct = default)
        => LockRowsForUpdateAsync(LockTargetTable.QueueServicePoints, [servicePointId], ct);

    public Task<bool> TryLockServicePointsForUpdateAsync(IReadOnlyCollection<long> servicePointIds, CancellationToken ct = default)
        => LockRowsForUpdateAsync(LockTargetTable.QueueServicePoints, servicePointIds, ct);

    public async Task<bool> TryLockUnroutedQueueAdvisoryAsync(long hospitalId, DateOnly queueDate, CancellationToken ct = default)
    {
        if (hospitalId <= 0)
        {
            return false;
        }

        if (!Database.IsRelational())
        {
            return false;
        }

        EnsureActiveRelationalTransaction("advisory:queue-unrouted");
        var key = ComputeDeterministicAdvisoryLockKey("queue-unrouted", $"{hospitalId}:{queueDate:yyyy-MM-dd}");
        await Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})", ct);
        return true;
    }

    public Task<bool> TryLockQueueEntryForUpdateAsync(long queueEntryId, CancellationToken ct = default)
        => LockRowsForUpdateAsync(LockTargetTable.QueueEntries, [queueEntryId], ct);

    public Task<bool> TryLockQueueEntriesForUpdateAsync(IReadOnlyCollection<long> queueEntryIds, CancellationToken ct = default)
        => LockRowsForUpdateAsync(LockTargetTable.QueueEntries, queueEntryIds, ct);

    public Task<bool> TryLockDiagnosticOrderForUpdateAsync(long diagnosticOrderId, CancellationToken ct = default)
        => LockRowsForUpdateAsync(LockTargetTable.DiagnosticOrders, [diagnosticOrderId], ct);

    public Task<bool> TryLockDiagnosticOrdersForUpdateAsync(IReadOnlyCollection<long> diagnosticOrderIds, CancellationToken ct = default)
        => LockRowsForUpdateAsync(LockTargetTable.DiagnosticOrders, diagnosticOrderIds, ct);

    public Task<bool> TryLockPrescriptionForUpdateAsync(long prescriptionId, CancellationToken ct = default)
        => LockRowsForUpdateAsync(LockTargetTable.Prescriptions, [prescriptionId], ct);

    public Task<bool> TryLockMedicineBatchesForUpdateAsync(IReadOnlyCollection<long> batchIds, CancellationToken ct = default)
        => LockRowsForUpdateAsync(LockTargetTable.MedicineBatches, batchIds, ct);

    public Task<bool> TryLockVisitChargeForUpdateAsync(long visitChargeId, CancellationToken ct = default)
        => LockRowsForUpdateAsync(LockTargetTable.VisitCharges, [visitChargeId], ct);

    public Task<bool> TryLockVisitChargesForUpdateAsync(IReadOnlyCollection<long> visitChargeIds, CancellationToken ct = default)
        => LockRowsForUpdateAsync(LockTargetTable.VisitCharges, visitChargeIds, ct);

    public Task<bool> TryLockPaymentForUpdateAsync(long paymentId, CancellationToken ct = default)
        => LockRowsForUpdateAsync(LockTargetTable.Payments, [paymentId], ct);

    public Task<bool> TryLockPaymentsForUpdateAsync(IReadOnlyCollection<long> paymentIds, CancellationToken ct = default)
        => LockRowsForUpdateAsync(LockTargetTable.Payments, paymentIds, ct);

    public Task<bool> TryLockPaymentAllocationForUpdateAsync(long allocationId, CancellationToken ct = default)
        => LockRowsForUpdateAsync(LockTargetTable.PaymentAllocations, [allocationId], ct);

    public Task<bool> TryLockPaymentAllocationsForUpdateAsync(IReadOnlyCollection<long> allocationIds, CancellationToken ct = default)
        => LockRowsForUpdateAsync(LockTargetTable.PaymentAllocations, allocationIds, ct);

    private async Task<bool> LockRowsForUpdateAsync(
        LockTargetTable table,
        IReadOnlyCollection<long> ids,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ids);

        if (!Database.IsRelational())
        {
            return false;
        }

        EnsureActiveRelationalTransaction(table.ToString());

        if (ids.Count == 0)
        {
            return true;
        }

        if (ids.Any(id => id <= 0))
        {
            return false;
        }

        var sortedIds = ids.Distinct().OrderBy(id => id).ToArray();
        var lockedIds = table switch
        {
            LockTargetTable.Users => await Database.SqlQuery<long>(
                $"SELECT id AS \"Value\" FROM users WHERE id = ANY({sortedIds}) ORDER BY id ASC FOR UPDATE").ToListAsync(ct),
            LockTargetTable.Patients => await Database.SqlQuery<long>(
                $"SELECT id AS \"Value\" FROM patients WHERE id = ANY({sortedIds}) ORDER BY id ASC FOR UPDATE").ToListAsync(ct),
            LockTargetTable.PatientIdentityClaims => await Database.SqlQuery<long>(
                $"SELECT id AS \"Value\" FROM patient_identity_claims WHERE id = ANY({sortedIds}) ORDER BY id ASC FOR UPDATE").ToListAsync(ct),
            LockTargetTable.Services => await Database.SqlQuery<long>(
                $"SELECT id AS \"Value\" FROM services WHERE id = ANY({sortedIds}) ORDER BY id ASC FOR UPDATE").ToListAsync(ct),
            LockTargetTable.Doctors => await Database.SqlQuery<long>(
                $"SELECT id AS \"Value\" FROM doctors WHERE id = ANY({sortedIds}) ORDER BY id ASC FOR UPDATE").ToListAsync(ct),
            LockTargetTable.HospitalRooms => await Database.SqlQuery<long>(
                $"SELECT id AS \"Value\" FROM hospital_rooms WHERE id = ANY({sortedIds}) ORDER BY id ASC FOR UPDATE").ToListAsync(ct),
            LockTargetTable.ExaminationSlots => await Database.SqlQuery<long>(
                $"SELECT id AS \"Value\" FROM examination_slots WHERE id = ANY({sortedIds}) ORDER BY id ASC FOR UPDATE").ToListAsync(ct),
            LockTargetTable.Appointments => await Database.SqlQuery<long>(
                $"SELECT id AS \"Value\" FROM appointments WHERE id = ANY({sortedIds}) ORDER BY id ASC FOR UPDATE").ToListAsync(ct),
            LockTargetTable.Visits => await Database.SqlQuery<long>(
                $"SELECT id AS \"Value\" FROM visits WHERE id = ANY({sortedIds}) ORDER BY id ASC FOR UPDATE").ToListAsync(ct),
            LockTargetTable.QueueServicePoints => await Database.SqlQuery<long>(
                $"SELECT id AS \"Value\" FROM queue_service_points WHERE id = ANY({sortedIds}) ORDER BY id ASC FOR UPDATE").ToListAsync(ct),
            LockTargetTable.QueueEntries => await Database.SqlQuery<long>(
                $"SELECT id AS \"Value\" FROM queue_entries WHERE id = ANY({sortedIds}) ORDER BY id ASC FOR UPDATE").ToListAsync(ct),
            LockTargetTable.DiagnosticOrders => await Database.SqlQuery<long>(
                $"SELECT id AS \"Value\" FROM diagnostic_orders WHERE id = ANY({sortedIds}) ORDER BY id ASC FOR UPDATE").ToListAsync(ct),
            LockTargetTable.Prescriptions => await Database.SqlQuery<long>(
                $"SELECT id AS \"Value\" FROM prescriptions WHERE id = ANY({sortedIds}) ORDER BY id ASC FOR UPDATE").ToListAsync(ct),
            LockTargetTable.MedicineBatches => await Database.SqlQuery<long>(
                $"SELECT id AS \"Value\" FROM medicine_batches WHERE id = ANY({sortedIds}) ORDER BY id ASC FOR UPDATE").ToListAsync(ct),
            LockTargetTable.VisitCharges => await Database.SqlQuery<long>(
                $"SELECT id AS \"Value\" FROM visit_charges WHERE id = ANY({sortedIds}) ORDER BY id ASC FOR UPDATE").ToListAsync(ct),
            LockTargetTable.Payments => await Database.SqlQuery<long>(
                $"SELECT id AS \"Value\" FROM payments WHERE id = ANY({sortedIds}) ORDER BY id ASC FOR UPDATE").ToListAsync(ct),
            LockTargetTable.PaymentAllocations => await Database.SqlQuery<long>(
                $"SELECT id AS \"Value\" FROM payment_allocations WHERE id = ANY({sortedIds}) ORDER BY id ASC FOR UPDATE").ToListAsync(ct),
            _ => throw new ArgumentOutOfRangeException(nameof(table), table, "Unsupported lock target table."),
        };

        return lockedIds.Count == sortedIds.Length;
    }

    private void EnsureActiveRelationalTransaction(string target)
    {
        if (Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                $"Acquiring FOR UPDATE lock on '{target}' requires an active database transaction (ADR-0059).");
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        // v4.1 schema lives in `public` — EF default for Npgsql already is public.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MedFlowDbContext).Assembly);
    }
}
