using System.Globalization;
using System.Net;
using System.Text.Json;
using MedFlow.Application.Common.Persistence;
using MedFlow.Application.Common.Time;
using MedFlow.Application.DTOs.Pharmacy;
using MedFlow.Application.DTOs.Realtime;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Payments;
using MedFlow.Application.Services.Realtime;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.Application.Services.Pharmacy;

public sealed class ConfirmMedicineDispensingService : IConfirmMedicineDispensingService
{
    private const string PharmacyStaffRoleCode = RoleNames.PharmacyStaff;
    private const string MedicineDispensePermissionCode = PermissionCodes.MedicineDispense;

    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;
    private readonly IVisitChargeSettlementReader _settlementReader;
    private readonly IRealtimeNotificationPublisher _realtimePublisher;

    public ConfirmMedicineDispensingService(
        IApplicationDbContext dbContext,
        TimeProvider? timeProvider = null,
        IVisitChargeSettlementReader? settlementReader = null,
        IRealtimeNotificationPublisher? realtimePublisher = null)
    {
        _db = dbContext;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _settlementReader = settlementReader ?? new VisitChargeSettlementReader(dbContext, _timeProvider);
        _realtimePublisher = realtimePublisher ?? NullRealtimeNotificationPublisher.Instance;
    }

    public async Task<DispenseConfirmContextDto> GetDispenseContextAsync(
        long actorUserId,
        string idOrCode,
        CancellationToken ct = default,
        string? ipAddress = null)
    {
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var nowLocal = HospitalClock.GetNow(_timeProvider);
        var today = HospitalClock.GetToday(_timeProvider);
        var (_, hospital) = await ValidatePharmacyStaffAccessAsync(actorUserId, nowUtc, ct);

        var prescription = await ResolvePrescriptionAsync(idOrCode, hospital.Id, ct);
        var visit = await _db.Visits.AsNoTracking().FirstOrDefaultAsync(v => v.Id == prescription.VisitId, ct)
            ?? throw new NotFoundException("Visit", prescription.VisitId ?? 0);

        EnsureVisitActiveForPharmacy(visit);

        var patient = await _db.Patients.AsNoTracking().FirstOrDefaultAsync(p => p.Id == visit.PatientId, ct)
            ?? throw new NotFoundException("Patient", visit.PatientId ?? 0);

        var doctor = prescription.PrescribingDoctorId.HasValue
            ? await (from d in _db.Doctors.AsNoTracking()
                     join s in _db.Staff.AsNoTracking() on d.StaffId equals s.Id into staffGroup
                     from s in staffGroup.DefaultIfEmpty()
                     where d.Id == prescription.PrescribingDoctorId.Value
                     select new { Doctor = d, Staff = s }).FirstOrDefaultAsync(ct)
            : null;

        var items = await _db.PrescriptionItems.AsNoTracking()
            .Where(pi => pi.PrescriptionId == prescription.Id)
            .OrderBy(pi => pi.Id)
            .ToListAsync(ct);

        var medicineIds = items.Where(i => i.MedicineId.HasValue).Select(i => i.MedicineId!.Value).Distinct().ToList();
        var medicinesById = await _db.Medicines.AsNoTracking()
            .Where(m => medicineIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, ct);

        var existingDispense = await _db.MedicationDispenses.AsNoTracking()
            .Where(md => md.PrescriptionId == prescription.Id)
            .OrderByDescending(md => md.DispensedAt ?? md.PreparedAt)
            .ThenByDescending(md => md.Id)
            .FirstOrDefaultAsync(ct);

        var existingDispenseItems = existingDispense is not null
            ? await _db.DispenseItems.AsNoTracking()
                .Where(di => di.DispenseId == existingDispense.Id)
                .OrderBy(di => di.Id)
                .ToListAsync(ct)
            : [];

        var dispenseItemsByPrescriptionItemId = existingDispenseItems
            .Where(di => di.PrescriptionItemId.HasValue)
            .GroupBy(di => di.PrescriptionItemId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var allocatedBatchIds = existingDispenseItems
            .Where(di => di.MedicineBatchId.HasValue)
            .Select(di => di.MedicineBatchId!.Value)
            .Distinct()
            .ToList();

        var allocatedBatchesById = allocatedBatchIds.Count > 0
            ? await _db.MedicineBatches.AsNoTracking()
                .Where(b => allocatedBatchIds.Contains(b.Id))
                .ToDictionaryAsync(b => b.Id, ct)
            : new Dictionary<long, MedicineBatch>();

        var availableBatches = medicineIds.Count > 0
            ? await _db.MedicineBatches.AsNoTracking()
                .Where(b => b.MedicineId.HasValue && medicineIds.Contains(b.MedicineId.Value))
                .Where(b => b.IsBlocked != true && (b.ExpiryDate == null || b.ExpiryDate > today) && (b.QuantityOnHand ?? 0) > 0)
                .OrderBy(b => b.ExpiryDate)
                .ThenBy(b => b.Id)
                .ToListAsync(ct)
            : [];

        var batchesByMedicineId = availableBatches
            .Where(b => b.MedicineId.HasValue)
            .GroupBy(b => b.MedicineId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var visitPayments = prescription.VisitId.HasValue
            ? await _db.Payments.AsNoTracking()
                .Where(p => p.VisitId == prescription.VisitId.Value)
                .ToListAsync(ct)
            : [];

        var clearance = await _settlementReader.CheckPharmacyVisitClearanceAsync(visit.Id, ct);
        if (clearance.IsUnlocked)
        {
            // #866: priced medicine charges of THIS prescription must be settled at the pharmacy counter.
            var medicineClearance = await _settlementReader.CheckPharmacyMedicineEligibilityAsync(
                visit.Id,
                prescription.Id,
                ct);
            if (!medicineClearance.IsUnlocked)
                clearance = medicineClearance;
        }

        var hasPaidPayment = visitPayments.Any(p => string.Equals(p.Status, "PAID", StringComparison.OrdinalIgnoreCase));
        var isBlockedByPayment = !clearance.IsUnlocked;

        var isAlreadyDispensed = string.Equals(prescription.Status, "DISPENSED", StringComparison.OrdinalIgnoreCase) ||
                                 IsDispensedOrCompleted(existingDispense?.Status);

        var canDispense = !isAlreadyDispensed && !isBlockedByPayment;
        string? disabledReason = null;
        if (isAlreadyDispensed)
        {
            disabledReason = "Đơn thuốc đã được hoàn tất cấp phát (DISPENSED).";
        }
        else if (isBlockedByPayment)
        {
            disabledReason = $"Chặn xuất kho: {clearance.Message ?? "Chờ xác nhận thanh toán trước khi xuất kho."}";
        }

        var totalPaid = visitPayments.Where(p => string.Equals(p.Status, "PAID", StringComparison.OrdinalIgnoreCase)).Sum(p => p.Amount ?? 0m);
        var totalPaidFormatted = totalPaid > 0
            ? FormatVndPrice(totalPaid)
            : (clearance.IsUnlocked ? "Miễn phí / BHYT" : "Chưa có dữ liệu");
        var paymentMethodLabel = hasPaidPayment
            ? "VietQR (Đã thanh toán)"
            : (isBlockedByPayment ? "Chờ thanh toán" : "Miễn phí / BHYT");
        var paymentStatus = hasPaidPayment
            ? "PAID"
            : (isBlockedByPayment
                ? (clearance.StatusCode == VisitChargeSettlementReader.StatusMismatch ? "MISMATCH" : "PENDING")
                : "NOT_REQUIRED");

        var counselingItems = new List<DispenseMedicationCounselingItemDto>(items.Count);
        var orderCounter = 0;

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            medicinesById.TryGetValue(item.MedicineId ?? 0, out var med);

            var drugName = med?.Name ?? $"Thuốc #{item.MedicineId}";
            var unit = med?.DispensingUnit ?? item.SingleDoseUnit ?? "viên";
            var prescribedQty = item.PrescribedQuantity ?? 1m;
            var dosage = !string.IsNullOrWhiteSpace(item.DoseScheduleText)
                ? item.DoseScheduleText
                : (!string.IsNullOrWhiteSpace(item.Instructions) ? item.Instructions : $"{item.Frequency ?? "Theo chỉ định"} - {item.DurationDays ?? 1} ngày");

            var warning = !string.IsNullOrWhiteSpace(item.Instructions)
                ? item.Instructions!
                : "Uống thuốc đúng liều và thời gian theo chỉ dẫn của bác sĩ.";

            if (dispenseItemsByPrescriptionItemId.TryGetValue(item.Id, out var preparedRows) && preparedRows.Count > 0)
            {
                for (var rowIdx = 0; rowIdx < preparedRows.Count; rowIdx++)
                {
                    var existingItem = preparedRows[rowIdx];
                    MedicineBatch? allocatedBatch = null;
                    if (existingItem.MedicineBatchId.HasValue)
                    {
                        allocatedBatchesById.TryGetValue(existingItem.MedicineBatchId.Value, out allocatedBatch);
                    }

                    if (allocatedBatch is null && item.MedicineId.HasValue && batchesByMedicineId.TryGetValue(item.MedicineId.Value, out var fallbackBatches) && fallbackBatches.Count > 0)
                    {
                        allocatedBatch = fallbackBatches[0];
                    }

                    var dispensedQty = existingItem.DispensedQuantity ?? existingItem.PlannedQuantity ?? prescribedQty;
                    var batchCode = allocatedBatch?.BatchNumber ?? "CHƯA_GÁN";
                    var expiryDateStr = allocatedBatch?.ExpiryDate.HasValue == true
                        ? allocatedBatch.ExpiryDate.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
                        : "N/A";
                    orderCounter++;

                    counselingItems.Add(new DispenseMedicationCounselingItemDto(
                        Id: preparedRows.Count > 1 ? $"dmed-{item.Id}-b{allocatedBatch?.Id ?? rowIdx}" : $"dmed-{item.Id}",
                        OrderIndex: orderCounter,
                        PrescriptionItemId: item.Id,
                        MedicineId: item.MedicineId ?? 0,
                        DrugName: drugName,
                        QtyLabel: $"{dispensedQty:0.##} {unit}",
                        PrescribedQuantity: prescribedQty,
                        DispensedQuantity: dispensedQty,
                        Unit: unit,
                        BatchCode: batchCode,
                        BatchId: allocatedBatch?.Id,
                        ExpiryDate: expiryDateStr,
                        DosageInstruction: dosage,
                        WarningNote: warning,
                        TagLabel: med?.DosageForm ?? "Thuốc kê đơn",
                        TagTone: (orderCounter - 1) % 3 == 0 ? "sky" : ((orderCounter - 1) % 3 == 1 ? "amber" : "rose"),
                        ShelfLocation: "Chưa cập nhật"
                    ));
                }
            }
            else
            {
                MedicineBatch? allocatedBatch = null;
                if (item.MedicineId.HasValue && batchesByMedicineId.TryGetValue(item.MedicineId.Value, out var mBatches) && mBatches.Count > 0)
                {
                    allocatedBatch = mBatches[0];
                }

                var dispensedQty = prescribedQty;
                var batchCode = allocatedBatch?.BatchNumber ?? "CHƯA_GÁN";
                var expiryDateStr = allocatedBatch?.ExpiryDate.HasValue == true
                    ? allocatedBatch.ExpiryDate.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
                    : "N/A";
                orderCounter++;

                counselingItems.Add(new DispenseMedicationCounselingItemDto(
                    Id: $"dmed-{item.Id}",
                    OrderIndex: orderCounter,
                    PrescriptionItemId: item.Id,
                    MedicineId: item.MedicineId ?? 0,
                    DrugName: drugName,
                    QtyLabel: $"{dispensedQty:0.##} {unit}",
                    PrescribedQuantity: prescribedQty,
                    DispensedQuantity: dispensedQty,
                    Unit: unit,
                    BatchCode: batchCode,
                    BatchId: allocatedBatch?.Id,
                    ExpiryDate: expiryDateStr,
                    DosageInstruction: dosage,
                    WarningNote: warning,
                    TagLabel: med?.DosageForm ?? "Thuốc kê đơn",
                    TagTone: (orderCounter - 1) % 3 == 0 ? "sky" : ((orderCounter - 1) % 3 == 1 ? "amber" : "rose"),
                    ShelfLocation: "Chưa cập nhật"
                ));
            }
        }

        var checks = new List<PharmacistHandoverCheckDto>
        {
            new("chk-1", "1. Đã đối chiếu đúng nhân thân người nhận, số lô, hàm lượng và số lượng thực xuất.", isAlreadyDispensed),
            new("chk-2", "2. Người nhận thuốc đã nghe rõ, hiểu các cảnh báo an toàn & hướng dẫn sử dụng.", canDispense),
            new("chk-3", "3. Đã kiểm tra niêm phong túi thuốc & gắn tem QR định danh đúng chuẩn GPP.", canDispense)
        };

        var doctorName = doctor is not null ? FormatDoctorDisplayName(doctor.Doctor, doctor.Staff) : "Bác sĩ điều trị";

        var hasIdHash = !string.IsNullOrWhiteSpace(patient.IdentificationHash) || !string.IsNullOrWhiteSpace(patient.IdentificationHashHmac);
        var identityCard = hasIdHash ? "****** (Đã đối chiếu CCCD)" : "Chưa đối chiếu CCCD";

        var receiver = new ReceiverVerificationInfoDto(
            FullName: patient.FullName,
            PatientCode: patient.PatientCode,
            IdentityCard: identityCard,
            Phone: patient.Phone ?? "Chưa cập nhật",
            ReceiverName: $"{patient.FullName} (Chính chủ người bệnh)",
            Relationship: "Bản thân",
            IsIdentityVerified: isAlreadyDispensed
        );

        var rxCode = prescription.PrescriptionCode ?? $"RX-{prescription.Id:D6}";
        var visitCode = visit.VisitCode ?? $"VIS-{visit.Id:D6}";
        var ticketNo = visit.VisitCode?.Split('-').LastOrDefault() ?? $"{visit.Id:D3}";

        var response = new DispenseConfirmContextDto(
            PrescriptionId: prescription.Id,
            RxCode: rxCode,
            TicketNo: ticketNo,
            VisitCode: visitCode,
            IssuedAt: (prescription.IssuedAt ?? prescription.CreatedAt)?.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture),
            DoctorName: doctorName,
            TotalPaidFormatted: totalPaidFormatted,
            PaymentMethodLabel: paymentMethodLabel,
            PaymentStatus: paymentStatus,
            RegimenBadge: $"Đơn thuốc điều trị ngoại trú ({items.Count} khoản thuốc)",
            Receiver: receiver,
            Medications: counselingItems,
            Checks: checks,
            CanDispense: canDispense,
            DisabledReason: disabledReason,
            DispenseStatus: isAlreadyDispensed ? "DISPENSED" : (existingDispense?.Status ?? "PENDING")
        );

        _db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actorUserId,
            HospitalId = hospital.Id,
            Action = "DISPENSE_CONTEXT_VIEWED",
            EntityType = "Prescription",
            EntityId = prescription.Id.ToString(CultureInfo.InvariantCulture),
            Reason = "Xem ngữ cảnh xác nhận cấp phát thuốc (UC46)",
            ChangeData = JsonSerializer.Serialize(new { prescriptionId = prescription.Id, rxCode, canDispense }),
            IpAddress = IPAddress.TryParse(ipAddress, out var ip) ? ip : null,
            OccurredAt = nowUtc
        });
        await _db.SaveChangesAsync(ct);

        return response;
    }

    public async Task<DispensingResultDetailDto> ConfirmDispenseAsync(
        long actorUserId,
        string idOrCode,
        ConfirmDispenseRequest request,
        CancellationToken ct = default,
        string? ipAddress = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.IsIdentityVerified)
        {
            throw new ValidationException("Người nhận thuốc chưa được xác minh danh tính.");
        }

        if (!request.ChecksConfirmed)
        {
            throw new ValidationException("Còn mục kiểm tra bắt buộc của Dược sĩ chưa được xác nhận.");
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var nowLocal = HospitalClock.GetNow(_timeProvider);
        var today = HospitalClock.GetToday(_timeProvider);
        var (staff, hospital) = await ValidatePharmacyStaffAccessAsync(actorUserId, nowUtc, ct);

        var preLockPrescription = await ResolvePrescriptionAsync(idOrCode, hospital.Id, ct);
        var visitId = preLockPrescription.VisitId
            ?? throw new NotFoundException("Visit", 0);

        var normalizedIdempotencyKey = string.IsNullOrWhiteSpace(request.IdempotencyKey)
            ? null
            : request.IdempotencyKey.Trim();

        long confirmedDispenseId;
        long? completedPharmacyServicePointId = null;

        await using (var tx = await _db.BeginTransactionAsync(ct))
        {
            try
            {
                // ADR-0061 strict lock order: visits (09) -> queue_service_points (10) -> queue_entries (11) -> prescriptions (13) -> medicine_batches (14, sorted ASC)
                await _db.TryLockVisitForUpdateAsync(visitId, ct);

                try
                {
                    var prePharmacyQueues = await _db.QueueEntries.AsNoTracking()
                        .Where(q => q.VisitId == visitId && q.QueueType != null && q.QueueType.ToUpper() == "PHARMACY")
                        .ToListAsync(ct);
                    var preSpIds = prePharmacyQueues
                        .SelectMany(q => new[] { q.CurrentServicePointId, q.ServicePointId })
                        .Where(id => id.HasValue && id.Value > 0)
                        .Select(id => id!.Value)
                        .Distinct()
                        .OrderBy(id => id)
                        .ToArray();
                    if (preSpIds.Length > 0)
                    {
                        await _db.TryLockServicePointsForUpdateAsync(preSpIds, ct);
                    }

                    var preQeIds = prePharmacyQueues.Select(q => q.Id).OrderBy(id => id).ToArray();
                    if (preQeIds.Length > 0)
                    {
                        await _db.TryLockQueueEntriesForUpdateAsync(preQeIds, ct);
                    }
                }
                catch (NotSupportedException)
                {
                }

                await _db.TryLockPrescriptionForUpdateAsync(preLockPrescription.Id, ct);

                if (_db is DbContext trackerCtx)
                {
                    trackerCtx.ChangeTracker.Clear();
                }

                var (postLockStaff, postLockHospital) = await ValidatePharmacyStaffAccessAsync(actorUserId, nowUtc, ct);
                staff = postLockStaff;
                hospital = postLockHospital;

                var visit = await _db.Visits.AsNoTracking()
                    .FirstOrDefaultAsync(v => v.Id == visitId, ct)
                    ?? throw new NotFoundException("Visit", visitId);

                EnsureVisitActiveForPharmacy(visit);

                var prescription = await _db.Prescriptions
                    .FirstOrDefaultAsync(p => p.Id == preLockPrescription.Id, ct)
                    ?? throw new NotFoundException($"Không tìm thấy đơn thuốc '{idOrCode}'.");

                await ValidatePrescriptionEligibilityAsync(prescription, ct);

                var existingDispense = await _db.MedicationDispenses
                    .Where(md => md.PrescriptionId == prescription.Id)
                    .OrderByDescending(md => md.DispensedAt ?? md.PreparedAt)
                    .ThenByDescending(md => md.Id)
                    .FirstOrDefaultAsync(ct);

                if (existingDispense is not null && IsDispensedOrCompleted(existingDispense.Status))
                {
                    if (normalizedIdempotencyKey is not null &&
                        string.Equals(existingDispense.IdempotencyKey, normalizedIdempotencyKey, StringComparison.Ordinal))
                    {
                        await SafeRollbackAsync(tx, ct);
                        return await BuildDispensingResultDetailAsync(existingDispense.Id, ct, request.ReceiverName);
                    }

                    throw new ConflictException("Đơn thuốc đã được hoàn tất cấp phát trước đó.");
                }

                if (string.Equals(prescription.Status, "DISPENSED", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ConflictException("Đơn thuốc đã được hoàn tất cấp phát trước đó.");
                }

                var clearance = await _settlementReader.CheckPharmacyVisitClearanceAsync(visit.Id, ct);
                if (clearance.IsUnlocked)
                {
                    // #866: priced medicine charges of THIS prescription must be settled before hand-over.
                    var medicineClearance = await _settlementReader.CheckPharmacyMedicineEligibilityAsync(
                        visit.Id,
                        prescription.Id,
                        ct);
                    if (!medicineClearance.IsUnlocked)
                        clearance = medicineClearance;
                }

                if (!clearance.IsUnlocked)
                {
                    throw new ConflictException(
                        clearance.Message ?? "Lượt khám chưa hoàn tất thanh toán. Vui lòng thanh toán trước khi cấp phát thuốc.");
                }

                var prescriptionItems = await _db.PrescriptionItems
                    .Where(pi => pi.PrescriptionId == prescription.Id)
                    .OrderBy(pi => pi.Id)
                    .ToListAsync(ct);

                if (prescriptionItems.Count == 0)
                {
                    throw new ValidationException("Đơn thuốc không có danh mục thuốc để cấp phát.");
                }

                var prescriptionItemsById = prescriptionItems.ToDictionary(pi => pi.Id);
                foreach (var reqLine in request.Items ?? [])
                {
                    if (!prescriptionItemsById.ContainsKey(reqLine.PrescriptionItemId))
                    {
                        throw new ValidationException($"Khoản thuốc #{reqLine.PrescriptionItemId} không thuộc đơn thuốc cần cấp phát.");
                    }

                    if (reqLine.DispensedQuantity is < 0m)
                    {
                        throw new ValidationException("Số lượng thực xuất không được âm.");
                    }
                }

                var medicineIds = prescriptionItems
                    .Where(i => i.MedicineId.HasValue)
                    .Select(i => i.MedicineId!.Value)
                    .Distinct()
                    .ToList();

                var medicinesById = await _db.Medicines.AsNoTracking()
                    .Where(m => medicineIds.Contains(m.Id))
                    .ToDictionaryAsync(m => m.Id, ct);

                var existingDispenseItems = existingDispense is not null
                    ? await _db.DispenseItems
                        .Where(di => di.DispenseId == existingDispense.Id)
                        .OrderBy(di => di.Id)
                        .ToListAsync(ct)
                    : [];

                var requestedBatchIds = (request.Items ?? [])
                    .Where(i => i.MedicineBatchId.HasValue && i.MedicineBatchId.Value > 0)
                    .Select(i => i.MedicineBatchId!.Value);

                var preparedBatchIds = existingDispenseItems
                    .Where(di => di.MedicineBatchId.HasValue && di.MedicineBatchId.Value > 0)
                    .Select(di => di.MedicineBatchId!.Value);

                var medicineBatchIds = await _db.MedicineBatches.AsNoTracking()
                    .Where(b => b.MedicineId.HasValue && medicineIds.Contains(b.MedicineId.Value))
                    .Select(b => b.Id)
                    .ToListAsync(ct);

                var allCandidateBatchIds = medicineBatchIds
                    .Concat(requestedBatchIds)
                    .Concat(preparedBatchIds)
                    .Distinct()
                    .OrderBy(id => id)
                    .ToArray();

                if (allCandidateBatchIds.Length > 0)
                {
                    await _db.TryLockMedicineBatchesForUpdateAsync(allCandidateBatchIds, ct);
                }

                var availableBatches = allCandidateBatchIds.Length > 0
                    ? await _db.MedicineBatches
                        .Where(b => allCandidateBatchIds.Contains(b.Id))
                        .ToListAsync(ct)
                    : [];

                var batchesById = availableBatches.ToDictionary(b => b.Id);

                var dispenseItemsByPiId = existingDispenseItems
                    .Where(di => di.PrescriptionItemId.HasValue)
                    .GroupBy(di => di.PrescriptionItemId!.Value)
                    .ToDictionary(g => g.Key, g => g.ToList());

                var requestItemsByPiId = (request.Items ?? [])
                    .GroupBy(i => i.PrescriptionItemId)
                    .ToDictionary(g => g.Key, g => g.ToList());

                // BR-BF03-03: no partial dispensing — request ShortageReason is kept for contract
                // backward-compatibility but is deliberately ignored (it can never relax quantity checks or be persisted).
                var allocations = new List<(
                    PrescriptionItem Item,
                    MedicineBatch? Batch,
                    decimal Qty,
                    DispenseItem? ExistingItem)>();

                var consumedInAutoAllocationByBatchId = new Dictionary<long, decimal>();

                foreach (var pItem in prescriptionItems)
                {
                    var prescribedQty = pItem.PrescribedQuantity ?? 1m;
                    var medName = medicinesById.TryGetValue(pItem.MedicineId ?? 0, out var medObj)
                        ? medObj.Name
                        : $"Thuốc #{pItem.MedicineId}";

                    dispenseItemsByPiId.TryGetValue(pItem.Id, out var existListForPi);
                    existListForPi ??= [];

                    requestItemsByPiId.TryGetValue(pItem.Id, out var reqListForPi);
                    reqListForPi ??= [];

                    var itemAllocations = new List<(
                        PrescriptionItem Item,
                        MedicineBatch? Batch,
                        decimal Qty,
                        DispenseItem? ExistingItem)>();

                    var usePreparedMultiBatchDirectly =
                        reqListForPi.Count == 1 &&
                        existListForPi.Count > 1 &&
                        !reqListForPi[0].MedicineBatchId.HasValue &&
                        !reqListForPi[0].DispensedQuantity.HasValue;

                    if (reqListForPi.Count > 0 && !usePreparedMultiBatchDirectly)
                    {
                        var matchedExistingSet = new HashSet<DispenseItem>();

                        foreach (var reqLine in reqListForPi)
                        {
                            var matchedExisting = reqLine.MedicineBatchId.HasValue
                                ? existListForPi.FirstOrDefault(di =>
                                    !matchedExistingSet.Contains(di) &&
                                    di.MedicineBatchId == reqLine.MedicineBatchId.Value)
                                : (existListForPi.Count == 1 && reqListForPi.Count == 1
                                    ? existListForPi[0]
                                    : null);

                            if (matchedExisting is not null)
                            {
                                matchedExistingSet.Add(matchedExisting);
                            }

                            // BUG-P3-03: null -> PlannedQuantity ?? PrescribedQuantity; 0 -> explicit zero-dispense (never fallback!, always rejected below); > 0 -> physical qty
                            decimal qtyToDispense = reqLine.DispensedQuantity.HasValue
                                ? reqLine.DispensedQuantity.Value
                                : (matchedExisting?.DispensedQuantity ?? matchedExisting?.PlannedQuantity ?? prescribedQty);

                            MedicineBatch? targetBatch = null;
                            if (reqLine.MedicineBatchId.HasValue)
                            {
                                if (!batchesById.TryGetValue(reqLine.MedicineBatchId.Value, out targetBatch))
                                {
                                    throw new ValidationException($"Không tìm thấy lô thuốc #{reqLine.MedicineBatchId.Value} cho thuốc '{medName}'.");
                                }
                            }
                            else if (matchedExisting?.MedicineBatchId.HasValue == true)
                            {
                                batchesById.TryGetValue(matchedExisting.MedicineBatchId.Value, out targetBatch);
                            }

                            if (targetBatch is null && pItem.MedicineId.HasValue)
                            {
                                targetBatch = SelectSingleFefoBatch(
                                    availableBatches,
                                    pItem.MedicineId.Value,
                                    qtyToDispense,
                                    today,
                                    consumedInAutoAllocationByBatchId);
                            }

                            if (targetBatch is not null && qtyToDispense > 0m)
                            {
                                consumedInAutoAllocationByBatchId.TryGetValue(targetBatch.Id, out var prevAuto);
                                consumedInAutoAllocationByBatchId[targetBatch.Id] = prevAuto + qtyToDispense;
                            }

                            itemAllocations.Add((pItem, targetBatch, qtyToDispense, matchedExisting));
                        }
                    }
                    else if (existListForPi.Count > 0)
                    {
                        foreach (var existDi in existListForPi)
                        {
                            var qtyToDispense = existDi.DispensedQuantity ?? existDi.PlannedQuantity ?? prescribedQty;

                            MedicineBatch? targetBatch = null;
                            if (existDi.MedicineBatchId.HasValue)
                            {
                                batchesById.TryGetValue(existDi.MedicineBatchId.Value, out targetBatch);
                            }

                            if (targetBatch is null && pItem.MedicineId.HasValue)
                            {
                                targetBatch = SelectSingleFefoBatch(
                                    availableBatches,
                                    pItem.MedicineId.Value,
                                    qtyToDispense,
                                    today,
                                    consumedInAutoAllocationByBatchId);
                            }

                            if (targetBatch is not null && qtyToDispense > 0m)
                            {
                                consumedInAutoAllocationByBatchId.TryGetValue(targetBatch.Id, out var prevAuto);
                                consumedInAutoAllocationByBatchId[targetBatch.Id] = prevAuto + qtyToDispense;
                            }

                            itemAllocations.Add((pItem, targetBatch, qtyToDispense, existDi));
                        }
                    }
                    else
                    {
                        var targetBatch = pItem.MedicineId.HasValue
                            ? SelectSingleFefoBatch(
                                availableBatches,
                                pItem.MedicineId.Value,
                                prescribedQty,
                                today,
                                consumedInAutoAllocationByBatchId)
                            : null;

                        if (targetBatch is not null && prescribedQty > 0m)
                        {
                            consumedInAutoAllocationByBatchId.TryGetValue(targetBatch.Id, out var prevAuto);
                            consumedInAutoAllocationByBatchId[targetBatch.Id] = prevAuto + prescribedQty;
                        }

                        itemAllocations.Add((pItem, targetBatch, prescribedQty, null));
                    }

                    // BR-BF03-03: bắt buộc xuất đủ 100% số lượng kê đơn.
                    // Không có lối tắt ShortageReason: thiếu (kể cả 0) hoặc thừa đều bị chặn và đơn không chuyển DISPENSED.
                    var totalDispensedForItem = itemAllocations.Sum(a => a.Qty);

                    if (totalDispensedForItem <= 0m)
                    {
                        throw new ValidationException(
                            $"Khoản thuốc '{medName}' chưa có số lượng thực xuất nên không thể cấp phát. Không đủ tồn kho để cấp phát đầy đủ — vui lòng báo bệnh nhân và kết thúc luồng, đơn thuốc không được chuyển sang DISPENSED.");
                    }

                    if (totalDispensedForItem < prescribedQty)
                    {
                        throw new ValidationException(
                            $"Số lượng thực xuất ({totalDispensedForItem:0.###}) thiếu so với số lượng kê đơn ({prescribedQty:0.###}) cho thuốc '{medName}'. Không đủ tồn kho để cấp phát đầy đủ — vui lòng báo bệnh nhân và kết thúc luồng, đơn thuốc không được chuyển sang DISPENSED.");
                    }

                    if (totalDispensedForItem > prescribedQty)
                    {
                        throw new ValidationException(
                            $"Số lượng thực xuất ({totalDispensedForItem:0.###}) vượt quá số lượng kê đơn ({prescribedQty:0.###}) cho thuốc '{medName}'. Chỉ được xuất đúng 100% số lượng kê đơn.");
                    }

                    foreach (var alloc in itemAllocations)
                    {
                        if (alloc.Batch is null)
                        {
                            if (alloc.Qty == 0m)
                            {
                                continue;
                            }

                            throw new ConflictException($"Không đủ tồn kho để cấp phát đầy đủ cho thuốc '{medName}'. Vui lòng báo bệnh nhân và kết thúc luồng, đơn thuốc không được chuyển sang DISPENSED.");
                        }

                        if (alloc.Batch.MedicineId != pItem.MedicineId)
                        {
                            throw new ValidationException($"Lô thuốc '{alloc.Batch.BatchNumber}' không thuộc mặt hàng thuốc yêu cầu.");
                        }

                        if (alloc.Qty > 0m)
                        {
                            if (alloc.Batch.IsBlocked == true)
                            {
                                throw new ConflictException($"Lô thuốc '{alloc.Batch.BatchNumber}' đang bị khóa, không thể xuất kho.");
                            }

                            if (alloc.Batch.ExpiryDate.HasValue && alloc.Batch.ExpiryDate.Value <= today)
                            {
                                throw new ConflictException($"Lô thuốc '{alloc.Batch.BatchNumber}' đã hết hạn sử dụng ({alloc.Batch.ExpiryDate:dd/MM/yyyy}), không thể cấp phát.");
                            }
                        }
                    }

                    allocations.AddRange(itemAllocations);
                }

                // BUG-P3-01: Aggregate total requested quantity per BatchId across ALL lines and enforce UNCONDITIONAL non-negative stock!
                var totalRequestedByBatchId = allocations
                    .Where(a => a.Qty > 0m && a.Batch is not null)
                    .GroupBy(a => a.Batch!.Id)
                    .Select(g => (Batch: g.First().Batch!, TotalQty: g.Sum(x => x.Qty)))
                    .ToList();

                foreach (var (batch, totalRequested) in totalRequestedByBatchId)
                {
                    var currentOnHand = batch.QuantityOnHand ?? 0m;
                    if (currentOnHand < totalRequested)
                    {
                        throw new ConflictException(
                            $"Số lượng tồn khả dụng của lô '{batch.BatchNumber}' không đủ ({currentOnHand:0.###} < {totalRequested:0.###}). Vui lòng báo bệnh nhân và kết thúc luồng, đơn thuốc không được chuyển sang DISPENSED.");
                    }
                }

                var medicationDispense = existingDispense ?? new MedicationDispense
                {
                    PrescriptionId = prescription.Id,
                    PreparedByStaffId = staff.Id,
                    PreparedAt = nowLocal
                };

                medicationDispense.Status = "DISPENSED";
                medicationDispense.DispensedByStaffId = staff.Id;
                medicationDispense.DispensedAt = nowLocal;
                medicationDispense.IdempotencyKey = normalizedIdempotencyKey
                    ?? $"DISP-{prescription.Id}-{nowUtc:yyyyMMddHHmmss}";

                if (existingDispense is null)
                {
                    _db.MedicationDispenses.Add(medicationDispense);
                }
                await _db.SaveChangesAsync(ct);

                var retainedExistingItems = allocations
                    .Where(a => a.ExistingItem is not null)
                    .Select(a => a.ExistingItem!)
                    .ToHashSet();

                var obsoleteDispenseItems = existingDispenseItems
                    .Where(di => !retainedExistingItems.Contains(di))
                    .ToList();
                if (obsoleteDispenseItems.Count > 0)
                {
                    _db.DispenseItems.RemoveRange(obsoleteDispenseItems);
                }

                foreach (var (item, batch, qtyToDispense, matchedExistingItem) in allocations)
                {
                    if (batch is not null && qtyToDispense > 0m)
                    {
                        batch.QuantityOnHand = (batch.QuantityOnHand ?? 0m) - qtyToDispense;
                    }

                    DispenseItem dispenseItem;
                    if (matchedExistingItem is not null)
                    {
                        dispenseItem = matchedExistingItem;
                        dispenseItem.MedicineBatchId = batch?.Id;
                        dispenseItem.DispensedQuantity = qtyToDispense;
                        dispenseItem.ShortageReason = null;
                    }
                    else
                    {
                        dispenseItem = new DispenseItem
                        {
                            DispenseId = medicationDispense.Id,
                            PrescriptionItemId = item.Id,
                            MedicineBatchId = batch?.Id,
                            PlannedQuantity = item.PrescribedQuantity ?? qtyToDispense,
                            DispensedQuantity = qtyToDispense
                        };
                        _db.DispenseItems.Add(dispenseItem);
                    }

                    await _db.SaveChangesAsync(ct);

                    if (batch is not null && qtyToDispense > 0m)
                    {
                        var stockTx = new StockTransaction
                        {
                            MedicineBatchId = batch.Id,
                            DispenseItemId = dispenseItem.Id,
                            TransactionType = "DISPENSE",
                            QuantityDelta = -qtyToDispense,
                            PerformedByStaffId = staff.Id,
                            Reason = $"Xuất kho cấp phát theo đơn thuốc {prescription.PrescriptionCode ?? prescription.Id.ToString(CultureInfo.InvariantCulture)}",
                            SourceReference = prescription.PrescriptionCode ?? $"RX-{prescription.Id:D6}",
                            OccurredAt = nowLocal
                        };
                        _db.StockTransactions.Add(stockTx);
                    }
                }

                prescription.Status = "DISPENSED";

                // PH-02: Atomically close open PHARMACY queue entry inside the exact same transaction as stock deduction.
                // If FEFO batch validation threw above, we never reach here and the queue entry remains open.
                try
                {
                    var openPharmacyEntries = await _db.QueueEntries
                        .Where(q =>
                            q.VisitId == visit.Id &&
                            q.CompletedAt == null &&
                            q.QueueType != null &&
                            q.QueueType.ToUpper() == "PHARMACY" &&
                            (q.PrescriptionId == prescription.Id || q.PrescriptionId == null))
                        .ToListAsync(ct);

                    foreach (var pq in openPharmacyEntries)
                    {
                        var normStatus = pq.Status?.Trim().ToUpperInvariant();
                        if (normStatus is "COMPLETED" or "DONE" or "CANCELLED" or "DEFERRED")
                        {
                            continue;
                        }

                        pq.PrescriptionId ??= prescription.Id;
                        pq.ArrivedAt ??= nowLocal;
                        pq.ReadyAt ??= nowLocal;
                        pq.CalledAt ??= nowLocal;
                        pq.StartedAt ??= nowLocal;
                        pq.CompletedAt = nowLocal;
                        pq.Status = "COMPLETED";
                        pq.PresenceStatus = "PRESENT";
                        pq.BlockedReason = null;
                        pq.StateVersion = (pq.StateVersion is > 0 ? pq.StateVersion.Value : 1) + 1;
                        completedPharmacyServicePointId ??= pq.CurrentServicePointId ?? pq.ServicePointId;
                    }
                }
                catch (NotSupportedException)
                {
                }

                _db.AuditLogs.Add(new AuditLog
                {
                    ActorUserId = actorUserId,
                    HospitalId = hospital.Id,
                    Action = "MEDICINE_DISPENSED",
                    EntityType = "Prescription",
                    EntityId = prescription.Id.ToString(CultureInfo.InvariantCulture),
                    Reason = "Xác nhận bàn giao thuốc và trừ tồn kho (UC46)",
                    ChangeData = JsonSerializer.Serialize(new
                    {
                        prescriptionId = prescription.Id,
                        prescriptionCode = prescription.PrescriptionCode,
                        dispenseId = medicationDispense.Id,
                        dispensedByStaffId = staff.Id,
                        receiverName = request.ReceiverName,
                        relationship = request.Relationship,
                        itemCount = allocations.Count,
                        dispensedAt = nowUtc
                    }),
                    IpAddress = IPAddress.TryParse(ipAddress, out var confirmIp) ? confirmIp : null,
                    OccurredAt = nowUtc
                });

                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                confirmedDispenseId = medicationDispense.Id;
            }
            catch (DbUpdateException ex) when (PostgresConcurrencyException.IsUniqueViolation(ex))
            {
                await SafeRollbackAsync(tx, ct);
                if (_db is DbContext dbCtx)
                {
                    dbCtx.ChangeTracker.Clear();
                }

                var concurrentDispense = await _db.MedicationDispenses.AsNoTracking()
                    .Where(md => md.PrescriptionId == preLockPrescription.Id)
                    .OrderByDescending(md => md.DispensedAt ?? md.PreparedAt)
                    .ThenByDescending(md => md.Id)
                    .FirstOrDefaultAsync(ct);

                if (concurrentDispense is not null &&
                    IsDispensedOrCompleted(concurrentDispense.Status) &&
                    normalizedIdempotencyKey is not null &&
                    string.Equals(concurrentDispense.IdempotencyKey, normalizedIdempotencyKey, StringComparison.Ordinal))
                {
                    return await BuildDispensingResultDetailAsync(concurrentDispense.Id, ct, request.ReceiverName);
                }

                throw new ConflictException("Đơn thuốc đã được hoàn tất cấp phát trước đó.");
            }
            catch (Exception ex) when (PostgresConcurrencyException.IsDeadlockOrSerializationFailure(ex))
            {
                await SafeRollbackAsync(tx, ct);
                if (_db is DbContext dbCtx)
                {
                    dbCtx.ChangeTracker.Clear();
                }

                throw new ConflictException("Giao dịch cấp phát thuốc đang có xung đột đồng thời, vui lòng thử lại.");
            }
        }

        if (completedPharmacyServicePointId.HasValue)
        {
            await _realtimePublisher.TryPublishQueueChangedSafeAsync(
                new QueueChangedNotification(
                    HospitalId: hospital.Id,
                    ServicePointId: completedPharmacyServicePointId.Value),
                ct);
        }

        return await BuildDispensingResultDetailAsync(confirmedDispenseId, ct, request.ReceiverName);
    }

    public async Task<DispensingResultDetailDto> GetDispensingResultAsync(
        long actorUserId,
        string idOrCode,
        CancellationToken ct = default,
        string? ipAddress = null)
    {
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var (_, hospital) = await ValidatePharmacyStaffAccessAsync(actorUserId, nowUtc, ct);

        long? targetDispenseId = null;
        if (long.TryParse(idOrCode, out var numericId) && numericId > 0)
        {
            var byDispenseId = await _db.MedicationDispenses.AsNoTracking()
                .FirstOrDefaultAsync(md => md.Id == numericId, ct);
            if (byDispenseId is not null)
            {
                targetDispenseId = byDispenseId.Id;
            }
        }

        if (!targetDispenseId.HasValue && PharmacyDispenseCode.TryExtractDispenseId(idOrCode, out var codeDispenseId))
        {
            var candidate = await _db.MedicationDispenses.AsNoTracking()
                .FirstOrDefaultAsync(md => md.Id == codeDispenseId, ct);
            if (candidate is not null && PharmacyDispenseCode.Matches(candidate, idOrCode, nowUtc))
            {
                targetDispenseId = candidate.Id;
            }
        }

        if (!targetDispenseId.HasValue)
        {
            var prescription = await ResolvePrescriptionAsync(idOrCode, hospital.Id, ct);
            var dispense = await _db.MedicationDispenses.AsNoTracking()
                .Where(md => md.PrescriptionId == prescription.Id)
                .OrderByDescending(md => md.DispensedAt ?? md.PreparedAt)
                .ThenByDescending(md => md.Id)
                .FirstOrDefaultAsync(ct)
                ?? throw new NotFoundException($"Không tìm thấy bản ghi cấp phát cho đơn thuốc '{idOrCode}'.");

            targetDispenseId = dispense.Id;
        }

        var result = await BuildDispensingResultDetailAsync(targetDispenseId.Value, ct);

        _db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actorUserId,
            HospitalId = hospital.Id,
            Action = "DISPENSE_RESULT_VIEWED",
            EntityType = "MedicationDispense",
            EntityId = targetDispenseId.Value.ToString(CultureInfo.InvariantCulture),
            Reason = "Xem biên nhận kết quả cấp phát thuốc (UC46 / SCR-PHA-04)",
            ChangeData = JsonSerializer.Serialize(new { dispenseId = targetDispenseId.Value, rxCode = result.RxCode }),
            IpAddress = IPAddress.TryParse(ipAddress, out var viewIp) ? viewIp : null,
            OccurredAt = nowUtc
        });
        await _db.SaveChangesAsync(ct);

        return result;
    }

    private static MedicineBatch? SelectSingleFefoBatch(
        IReadOnlyList<MedicineBatch> availableBatches,
        long medicineId,
        decimal qtyToDispense,
        DateOnly today,
        IReadOnlyDictionary<long, decimal> consumedByBatchId)
    {
        if (qtyToDispense <= 0m)
        {
            return availableBatches
                .Where(b => b.MedicineId == medicineId && b.IsBlocked != true && (b.ExpiryDate == null || b.ExpiryDate > today))
                .OrderBy(b => b.ExpiryDate)
                .ThenBy(b => b.Id)
                .FirstOrDefault();
        }

        var sufficient = availableBatches
            .Where(b =>
            {
                if (b.MedicineId != medicineId || b.IsBlocked == true || (b.ExpiryDate.HasValue && b.ExpiryDate.Value <= today))
                    return false;
                consumedByBatchId.TryGetValue(b.Id, out var consumed);
                return (b.QuantityOnHand ?? 0m) - consumed >= qtyToDispense;
            })
            .OrderBy(b => b.ExpiryDate)
            .ThenBy(b => b.Id)
            .FirstOrDefault();

        if (sufficient is not null)
        {
            return sufficient;
        }

        return availableBatches
            .Where(b => b.MedicineId == medicineId && b.IsBlocked != true && (b.ExpiryDate == null || b.ExpiryDate > today))
            .OrderBy(b => b.ExpiryDate)
            .ThenBy(b => b.Id)
            .FirstOrDefault();
    }

    private async Task<DispensingResultDetailDto> BuildDispensingResultDetailAsync(
        long dispenseId,
        CancellationToken ct,
        string? overrideReceiverName = null)
    {
        var dispense = await _db.MedicationDispenses.AsNoTracking()
            .FirstOrDefaultAsync(md => md.Id == dispenseId, ct)
            ?? throw new NotFoundException("MedicationDispense", dispenseId);

        var prescription = await _db.Prescriptions.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == dispense.PrescriptionId, ct)
            ?? throw new NotFoundException("Prescription", dispense.PrescriptionId ?? 0);

        var visit = await _db.Visits.AsNoTracking().FirstOrDefaultAsync(v => v.Id == prescription.VisitId, ct)
            ?? throw new NotFoundException("Visit", prescription.VisitId ?? 0);

        var patient = await _db.Patients.AsNoTracking().FirstOrDefaultAsync(p => p.Id == visit.PatientId, ct)
            ?? throw new NotFoundException("Patient", visit.PatientId ?? 0);

        var prescribingDoctor = prescription.PrescribingDoctorId.HasValue
            ? await (from d in _db.Doctors.AsNoTracking()
                     join s in _db.Staff.AsNoTracking() on d.StaffId equals s.Id into staffGroup
                     from s in staffGroup.DefaultIfEmpty()
                     where d.Id == prescription.PrescribingDoctorId.Value
                     select new { Doctor = d, Staff = s }).FirstOrDefaultAsync(ct)
            : null;

        var dispensingStaff = dispense.DispensedByStaffId.HasValue
            ? await _db.Staff.AsNoTracking().FirstOrDefaultAsync(s => s.Id == dispense.DispensedByStaffId.Value, ct)
            : null;

        var dispenseItems = await _db.DispenseItems.AsNoTracking()
            .Where(di => di.DispenseId == dispense.Id)
            .OrderBy(di => di.Id)
            .ToListAsync(ct);

        var prescriptionItemIds = dispenseItems
            .Where(di => di.PrescriptionItemId.HasValue)
            .Select(di => di.PrescriptionItemId!.Value)
            .Distinct()
            .ToList();
        var pItemsById = await _db.PrescriptionItems.AsNoTracking()
            .Where(pi => prescriptionItemIds.Contains(pi.Id))
            .ToDictionaryAsync(pi => pi.Id, ct);

        var batchIds = dispenseItems
            .Where(di => di.MedicineBatchId.HasValue)
            .Select(di => di.MedicineBatchId!.Value)
            .Distinct()
            .ToList();
        var batchesById = await _db.MedicineBatches.AsNoTracking()
            .Where(b => batchIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, ct);

        var medicineIds = pItemsById.Values.Where(pi => pi.MedicineId.HasValue).Select(pi => pi.MedicineId!.Value).Distinct().ToList();
        var medicinesById = await _db.Medicines.AsNoTracking()
            .Where(m => medicineIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, ct);

        var visitPayments = prescription.VisitId.HasValue
            ? await _db.Payments.AsNoTracking()
                .Where(p => p.VisitId == prescription.VisitId.Value)
                .ToListAsync(ct)
            : [];

        var paidPayment = visitPayments
            .OrderByDescending(p => p.PaidAt ?? p.CreatedAt)
            .ThenByDescending(p => p.Id)
            .FirstOrDefault(p => string.Equals(p.Status, "PAID", StringComparison.OrdinalIgnoreCase));
        decimal? totalAmount = null;
        const string totalAmountFormatted = "Chưa có dữ liệu";
        var paymentSummary = paidPayment is not null
            ? $"Đã thanh toán viện phí {FormatVndPrice(paidPayment.Amount ?? 0m)} qua {(paidPayment.ExternalReference != null ? "VietQR" : "hệ thống viện phí")} • Hóa đơn {paidPayment.PaymentCode ?? paidPayment.ExternalReference ?? paidPayment.Id.ToString(CultureInfo.InvariantCulture)}"
            : "Chưa ghi nhận thanh toán viện phí";

        var lines = new List<DispensedLineItemDto>(dispenseItems.Count);
        decimal totalUnits = 0m;

        for (var i = 0; i < dispenseItems.Count; i++)
        {
            var di = dispenseItems[i];
            pItemsById.TryGetValue(di.PrescriptionItemId ?? 0, out var pi);
            medicinesById.TryGetValue(pi?.MedicineId ?? 0, out var med);
            batchesById.TryGetValue(di.MedicineBatchId ?? 0, out var batch);

            var drugName = med?.Name ?? $"Thuốc #{pi?.MedicineId}";
            var dosageForm = med?.DosageForm ?? "Viên nén";
            var unit = med?.DispensingUnit ?? pi?.SingleDoseUnit ?? "viên";
            var qty = di.DispensedQuantity ?? di.PlannedQuantity ?? 0m;
            totalUnits += qty;

            var batchCode = batch?.BatchNumber ?? (qty == 0m ? "KHÔNG_XUẤT_KHO" : "LÔ_CHÍNH_THỨC");
            var expiry = batch?.ExpiryDate.HasValue == true
                ? batch.ExpiryDate.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
                : "N/A";

            lines.Add(new DispensedLineItemDto(
                Id: $"di-{di.Id}",
                OrderIndex: i + 1,
                DrugName: drugName,
                DosageForm: dosageForm,
                QtyDispensed: qty,
                Unit: unit,
                BatchCode: batchCode,
                ExpiryDate: expiry,
                ShelfLocation: "Chưa cập nhật",
                UnitPriceFormatted: "Chưa có dữ liệu",
                LineTotalFormatted: "Chưa có dữ liệu",
                UnitPrice: null,
                LineTotal: null
            ));
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var rxCode = prescription.PrescriptionCode ?? $"RX-{prescription.Id:D6}";
        var visitCode = visit.VisitCode ?? $"VIS-{visit.Id:D6}";
        var ticketNo = visit.VisitCode?.Split('-').LastOrDefault() ?? $"{visit.Id:D3}";
        var dispenseCode = PharmacyDispenseCode.Format(dispense, nowUtc);

        var maskedIdentity = (!string.IsNullOrWhiteSpace(patient.IdentificationHash) || !string.IsNullOrWhiteSpace(patient.IdentificationHashHmac))
            ? "****** (Đã đối chiếu CCCD)"
            : "Chưa đối chiếu CCCD";
        var receiverName = !string.IsNullOrWhiteSpace(overrideReceiverName)
            ? overrideReceiverName
            : $"{patient.FullName} (Chính chủ người bệnh)";

        var doctorName = prescribingDoctor is not null
            ? FormatDoctorDisplayName(prescribingDoctor.Doctor, prescribingDoctor.Staff)
            : "Bác sĩ điều trị";

        var pharmacistName = dispensingStaff is not null
            ? $"DS. {dispensingStaff.FullName} (Quầy Dược)"
            : "DS. Dược sĩ cấp phát";

        var dispensedAtStr = (dispense.DispensedAt ?? dispense.PreparedAt ?? nowUtc)
            .ToString("dd/MM/yyyy • HH:mm:ss", CultureInfo.InvariantCulture);

        var auditNote = $"Hệ thống HIS đã tự động trừ tồn kho thực tế của {lines.Count} lô FEFO ({totalUnits:0.##} đơn vị) và khóa đơn thuốc không cho cấp phát trùng lặp.";

        return new DispensingResultDetailDto(
            DispenseId: dispense.Id,
            DispenseCode: dispenseCode,
            RxCode: rxCode,
            VisitCode: visitCode,
            TicketNo: ticketNo,
            PatientName: patient.FullName,
            PatientCode: patient.PatientCode,
            ReceiverName: receiverName,
            ReceiverIdentityMasked: maskedIdentity,
            PrescribingDoctor: doctorName,
            DispensingPharmacist: pharmacistName,
            DispensedAt: dispensedAtStr,
            StatusLabel: "DISPENSED & CLOSED",
            PaymentSummary: paymentSummary,
            TotalAmountFormatted: totalAmountFormatted,
            Items: lines,
            AuditNote: auditNote,
            PrescriptionId: prescription.Id,
            TotalAmount: totalAmount
        );
    }

    private async Task<(Staff Staff, Hospital Hospital)> ValidatePharmacyStaffAccessAsync(
        long actorUserId,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var actorUser = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == actorUserId, ct)
            ?? throw new NotFoundException("User", actorUserId);

        if (actorUser.AccountStatus != AccountStatus.ACTIVE)
        {
            throw new ForbiddenException("Tài khoản nhân viên nhà thuốc không ở trạng thái hoạt động.");
        }

        if (actorUser.LockedUntil.HasValue && actorUser.LockedUntil.Value > nowUtc)
        {
            throw new ForbiddenException("Tài khoản nhân viên nhà thuốc đang bị tạm khóa.");
        }

        var actorRoleIds = await _db.UserRoles.AsNoTracking()
            .Where(ur => ur.UserId == actorUserId)
            .Select(ur => ur.RoleId)
            .ToListAsync(ct);

        var hasPharmacyStaffRole = await _db.Roles.AsNoTracking()
            .AnyAsync(r => actorRoleIds.Contains(r.Id) && r.Code.ToUpper() == PharmacyStaffRoleCode, ct);

        if (!hasPharmacyStaffRole)
        {
            throw new ForbiddenException("Người dùng không có quyền truy cập nhà thuốc.");
        }

        var actorStaff = await _db.Staff.AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == actorUserId, ct);

        if (actorStaff is null || !actorStaff.HospitalId.HasValue)
        {
            throw new ForbiddenException("Tài khoản nhân viên nhà thuốc chưa được gán cơ sở y tế.");
        }

        if (!string.Equals(actorStaff.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            throw new ForbiddenException("Hồ sơ nhân sự của dược sĩ đã ngừng hoạt động.");
        }

        var hospitalId = actorStaff.HospitalId.Value;
        var hospital = await _db.Hospitals.AsNoTracking()
            .FirstOrDefaultAsync(h => h.Id == hospitalId, ct)
            ?? throw new NotFoundException("Hospital", hospitalId);

        if (hospital.IsActive != true)
        {
            throw new ForbiddenException("Cơ sở y tế được gán cho dược sĩ hiện đang ngừng hoạt động.");
        }

        var hasMedicineDispensePermission = await (
            from rp in _db.RolePermissions.AsNoTracking()
            join permission in _db.Permissions.AsNoTracking() on rp.PermissionId equals permission.Id
            where actorRoleIds.Contains(rp.RoleId) && permission.Code.ToUpper() == MedicineDispensePermissionCode
            select rp).AnyAsync(ct);

        if (!hasMedicineDispensePermission)
        {
            throw new ForbiddenException("Dược sĩ chưa được phân quyền cấp phát thuốc (MEDICINE_DISPENSE).");
        }

        if (!actorStaff.DepartmentId.HasValue || actorStaff.DepartmentId.Value <= 0)
        {
            throw new ForbiddenException(
                "[DEPARTMENT_SCOPE_FORBIDDEN] Nhân viên nhà thuốc chưa được phân công khoa Dược, không được phép thực hiện nghiệp vụ chuyên môn.");
        }

        try
        {
            var staffDepartment = await _db.Departments.AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == actorStaff.DepartmentId.Value, ct);
            if (staffDepartment is null ||
                staffDepartment.HospitalId != hospitalId ||
                staffDepartment.IsActive != true)
            {
                throw new ForbiddenException(
                    "[DEPARTMENT_SCOPE_FORBIDDEN] Khoa được phân công của nhân viên nhà thuốc không hợp lệ hoặc đã ngừng hoạt động.");
            }

            var phaDepartmentIds = await _db.Departments.AsNoTracking()
                .Where(d => d.HospitalId == hospitalId && d.IsActive == true && d.DepartmentCode != null && d.DepartmentCode.ToUpper() == "PHA")
                .Select(d => d.Id)
                .ToListAsync(ct);
            if (phaDepartmentIds.Count > 0 && !phaDepartmentIds.Contains(actorStaff.DepartmentId.Value))
            {
                throw new ForbiddenException(
                    "[DEPARTMENT_SCOPE_FORBIDDEN] Nhân viên nhà thuốc chỉ được phép thực hiện nghiệp vụ cấp phát thuốc trong phạm vi Khoa Dược được phân công.");
            }
        }
        catch (NotSupportedException)
        {
        }

        return (actorStaff, hospital);
    }

    private async Task<Prescription> ResolvePrescriptionAsync(
        string idOrCode,
        long hospitalId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idOrCode))
        {
            throw new ValidationException("Mã hoặc định danh đơn thuốc không được để trống.");
        }

        var trimmed = idOrCode.Trim();
        if (trimmed.Length > 100)
        {
            throw new ValidationException("Mã hoặc định danh đơn thuốc không được vượt quá 100 ký tự.");
        }

        var isNumeric = long.TryParse(trimmed, out var parsedId);
        if (isNumeric && parsedId <= 0)
        {
            throw new ValidationException("Định danh đơn thuốc không hợp lệ.");
        }

        var upper = trimmed.ToUpperInvariant();

        var query = from p in _db.Prescriptions.AsNoTracking()
                    join v in _db.Visits.AsNoTracking() on p.VisitId equals v.Id
                    from r in _db.HospitalRooms.AsNoTracking().Where(r => r.Id == v.ExaminationRoomId).DefaultIfEmpty()
                    from d in _db.Doctors.AsNoTracking().Where(d => d.Id == v.AssignedDoctorId).DefaultIfEmpty()
                    where (isNumeric ? p.Id == parsedId || (p.PrescriptionCode != null && p.PrescriptionCode.ToUpper() == upper)
                                     : p.PrescriptionCode != null && p.PrescriptionCode.ToUpper() == upper)
                    where (r != null && r.HospitalId == hospitalId) ||
                          (d != null && d.HospitalId == hospitalId) ||
                          (_db.Staff.Any(s => s.Id == v.CheckedInByStaffId && s.HospitalId == hospitalId))
                    select p;

        var prescription = await query.FirstOrDefaultAsync(ct);

        if (prescription is null)
        {
            var fallback = await _db.Prescriptions.AsNoTracking()
                .FirstOrDefaultAsync(p => (isNumeric ? p.Id == parsedId || (p.PrescriptionCode != null && p.PrescriptionCode.ToUpper() == upper)
                                                    : p.PrescriptionCode != null && p.PrescriptionCode.ToUpper() == upper), ct);

            if (fallback is null)
            {
                throw new NotFoundException($"Không tìm thấy đơn thuốc '{trimmed}'.");
            }

            var hasHospitalMedicine = await _db.PrescriptionItems.AsNoTracking()
                .AnyAsync(pi => pi.PrescriptionId == fallback.Id &&
                                pi.MedicineId.HasValue &&
                                _db.Medicines.Any(m => m.Id == pi.MedicineId.Value && m.HospitalId == hospitalId), ct);

            if (!hasHospitalMedicine)
            {
                throw new NotFoundException($"Không tìm thấy đơn thuốc '{trimmed}'.");
            }

            prescription = fallback;
        }

        await ValidatePrescriptionEligibilityAsync(prescription, ct);

        return prescription;
    }

    private async Task ValidatePrescriptionEligibilityAsync(Prescription prescription, CancellationToken ct)
    {
        if (string.Equals(prescription.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException("Đơn thuốc đã bị hủy, không thể cấp phát.");
        }

        if (string.Equals(prescription.Status, "DRAFT", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException("Đơn thuốc đang ở trạng thái nháp, chưa được bác sĩ phát hành.");
        }

        var isFulfillmentHospital = prescription.FulfillmentChoice != null &&
            (string.Equals(prescription.FulfillmentChoice, "HOSPITAL", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(prescription.FulfillmentChoice, "HOSPITAL_PHARMACY", StringComparison.OrdinalIgnoreCase));

        if (!isFulfillmentHospital)
        {
            throw new ConflictException("Đơn thuốc được chỉ định mua ngoài viện, không thuộc phạm vi cấp phát của nhà thuốc bệnh viện.");
        }

        var isSuperseded = await _db.Prescriptions.AsNoTracking().AnyAsync(replacement =>
            replacement.ReplacesPrescriptionId == prescription.Id &&
            replacement.Status != null &&
            (replacement.Status.ToUpper() == "ISSUED" ||
             replacement.Status.ToUpper() == "PREPARING" ||
             replacement.Status.ToUpper() == "READY" ||
             replacement.Status.ToUpper() == "DISPENSED"), ct);

        if (isSuperseded)
        {
            throw new ConflictException("Đơn thuốc đã bị thay thế bởi đơn thuốc mới hơn.");
        }
    }

    private static void EnsureVisitActiveForPharmacy(Visit visit)
    {
        var status = visit.Status?.Trim().ToUpperInvariant();
        if (status is "CANCELLED" or "NO_SHOW")
        {
            throw new ConflictException("Lượt khám đã hủy hoặc không đến, không thể cấp phát thuốc.");
        }
    }

    private static bool IsDispensedOrCompleted(string? status) =>
        string.Equals(status?.Trim(), "DISPENSED", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status?.Trim(), "COMPLETED", StringComparison.OrdinalIgnoreCase);

    private static async Task SafeRollbackAsync(IDbContextTransaction tx, CancellationToken ct)
    {
        try
        {
            await tx.RollbackAsync(ct);
        }
        catch
        {
            // Transaction already aborted on server.
        }
    }

    private static string FormatDoctorDisplayName(Doctor doctor, Staff? staff)
    {
        var fullName = staff?.FullName?.Trim();
        var title = doctor.ProfessionalTitle?.Trim();

        if (!string.IsNullOrWhiteSpace(fullName) && !string.IsNullOrWhiteSpace(title))
        {
            if (fullName.StartsWith(title, StringComparison.OrdinalIgnoreCase))
            {
                return fullName;
            }
            return $"{title} {fullName}";
        }

        if (!string.IsNullOrWhiteSpace(fullName))
        {
            return fullName.StartsWith("BS", StringComparison.OrdinalIgnoreCase)
                ? fullName
                : $"BS. {fullName}";
        }

        return !string.IsNullOrWhiteSpace(doctor.DoctorCode) ? doctor.DoctorCode : "Bác sĩ điều trị";
    }

    private static string FormatVndPrice(decimal price)
    {
        var truncated = decimal.Truncate(price);
        return $"{truncated.ToString("N0", CultureInfo.InvariantCulture).Replace(',', '.')} đ";
    }
}
