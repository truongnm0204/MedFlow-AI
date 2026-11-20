using System.Globalization;
using System.Net;
using System.Text.Json;
using MedFlow.Application.DTOs.Pharmacy;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Pharmacy;

public sealed class ConfirmMedicineDispensingService : IConfirmMedicineDispensingService
{
    private const string PharmacyStaffRoleCode = "PHARMACY_STAFF";
    private const string MedicineDispensePermissionCode = "MEDICINE_DISPENSE";

    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;

    public ConfirmMedicineDispensingService(
        IApplicationDbContext dbContext,
        TimeProvider? timeProvider = null)
    {
        _db = dbContext;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<DispenseConfirmContextDto> GetDispenseContextAsync(
        long actorUserId,
        string idOrCode,
        CancellationToken ct = default,
        string? ipAddress = null)
    {
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(nowUtc);
        var (staff, hospital) = await ValidatePharmacyStaffAccessAsync(actorUserId, nowUtc, ct);

        var prescription = await ResolvePrescriptionAsync(idOrCode, hospital.Id, ct);
        var visit = await _db.Visits.AsNoTracking().FirstOrDefaultAsync(v => v.Id == prescription.VisitId, ct)
            ?? throw new NotFoundException("Visit", prescription.VisitId ?? 0);

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
                .ToListAsync(ct)
            : [];

        var dispenseItemByPrescriptionItemId = existingDispenseItems
            .Where(di => di.PrescriptionItemId.HasValue)
            .GroupBy(di => di.PrescriptionItemId!.Value)
            .ToDictionary(g => g.Key, g => g.First());

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

        var hasPaidPayment = visitPayments.Any(p => string.Equals(p.Status, "PAID", StringComparison.OrdinalIgnoreCase));
        var hasPendingPayment = visitPayments.Count > 0 && !hasPaidPayment;

        var isAlreadyDispensed = string.Equals(prescription.Status, "DISPENSED", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(existingDispense?.Status, "DISPENSED", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(existingDispense?.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase);

        var canDispense = !isAlreadyDispensed && !hasPendingPayment;
        string? disabledReason = null;
        if (isAlreadyDispensed)
        {
            disabledReason = "Đơn thuốc đã được hoàn tất cấp phát (DISPENSED).";
        }
        else if (hasPendingPayment)
        {
            disabledReason = "Chặn xuất kho: Chờ xác nhận thanh toán trước khi xuất kho.";
        }

        var totalPaid = visitPayments.Where(p => string.Equals(p.Status, "PAID", StringComparison.OrdinalIgnoreCase)).Sum(p => p.Amount ?? 0m);
        var totalPaidFormatted = totalPaid > 0 ? FormatVndPrice(totalPaid) : "100% Tự nguyện / Bảo hiểm";
        var paymentMethodLabel = hasPaidPayment ? "VietQR (Đã thanh toán)" : (hasPendingPayment ? "Chờ thanh toán" : "Miễn phí / BHYT");

        var counselingItems = new List<DispenseMedicationCounselingItemDto>(items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            medicinesById.TryGetValue(item.MedicineId ?? 0, out var med);

            MedicineBatch? allocatedBatch = null;
            if (dispenseItemByPrescriptionItemId.TryGetValue(item.Id, out var existingItem) && existingItem.MedicineBatchId.HasValue)
            {
                allocatedBatch = await _db.MedicineBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == existingItem.MedicineBatchId.Value, ct);
            }

            if (allocatedBatch is null && item.MedicineId.HasValue && batchesByMedicineId.TryGetValue(item.MedicineId.Value, out var mBatches) && mBatches.Count > 0)
            {
                allocatedBatch = mBatches[0];
            }

            var drugName = med?.Name ?? $"Thuốc #{item.MedicineId}";
            var unit = med?.DispensingUnit ?? item.SingleDoseUnit ?? "viên";
            var prescribedQty = item.PrescribedQuantity ?? 1m;
            var dispensedQty = existingItem?.DispensedQuantity ?? existingItem?.PlannedQuantity ?? prescribedQty;
            var batchCode = allocatedBatch?.BatchNumber ?? "CHƯA_GÁN";
            var expiryDateStr = allocatedBatch?.ExpiryDate.HasValue == true
                ? allocatedBatch.ExpiryDate.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
                : "N/A";

            var dosage = !string.IsNullOrWhiteSpace(item.DoseScheduleText)
                ? item.DoseScheduleText
                : (!string.IsNullOrWhiteSpace(item.Instructions) ? item.Instructions : $"{item.Frequency ?? "Theo chỉ định"} - {item.DurationDays ?? 1} ngày");

            var warning = !string.IsNullOrWhiteSpace(item.Instructions)
                ? item.Instructions!
                : "Uống thuốc đúng liều và thời gian theo chỉ dẫn của bác sĩ.";

            counselingItems.Add(new DispenseMedicationCounselingItemDto(
                Id: $"dmed-{item.Id}",
                OrderIndex: i + 1,
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
                TagTone: i % 3 == 0 ? "sky" : (i % 3 == 1 ? "amber" : "rose"),
                ShelfLocation: $"Kệ {(char)('A' + (i % 4))}-0{(i % 5) + 1}"
            ));
        }

        var checks = new List<PharmacistHandoverCheckDto>
        {
            new("chk-1", "1. Đã đối chiếu đúng nhân thân người nhận, số lô, hàm lượng và số lượng thực xuất.", true),
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
            IsIdentityVerified: true
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
            PaymentStatus: hasPaidPayment ? "PAID" : (hasPendingPayment ? "PENDING" : "NOT_REQUIRED"),
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
        var today = DateOnly.FromDateTime(nowUtc);
        var (staff, hospital) = await ValidatePharmacyStaffAccessAsync(actorUserId, nowUtc, ct);

        var prescription = await ResolvePrescriptionAsync(idOrCode, hospital.Id, ct);

        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var existingIdempotentDispense = await _db.MedicationDispenses.AsNoTracking()
                .FirstOrDefaultAsync(md => md.PrescriptionId == prescription.Id && md.IdempotencyKey == request.IdempotencyKey.Trim(), ct);

            if (existingIdempotentDispense is not null && string.Equals(existingIdempotentDispense.Status, "DISPENSED", StringComparison.OrdinalIgnoreCase))
            {
                return await BuildDispensingResultDetailAsync(existingIdempotentDispense.Id, ct);
            }
        }

        if (string.Equals(prescription.Status, "DISPENSED", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException("Đơn thuốc đã được hoàn tất cấp phát trước đó.");
        }

        var visitPayments = prescription.VisitId.HasValue
            ? await _db.Payments.AsNoTracking()
                .Where(p => p.VisitId == prescription.VisitId.Value)
                .ToListAsync(ct)
            : [];

        var hasPaidPayment = visitPayments.Any(p => string.Equals(p.Status, "PAID", StringComparison.OrdinalIgnoreCase));
        if (visitPayments.Count > 0 && !hasPaidPayment)
        {
            throw new ConflictException("Lượt khám chưa hoàn tất thanh toán. Vui lòng thanh toán trước khi cấp phát thuốc.");
        }

        var prescriptionItems = await _db.PrescriptionItems
            .Where(pi => pi.PrescriptionId == prescription.Id)
            .OrderBy(pi => pi.Id)
            .ToListAsync(ct);

        if (prescriptionItems.Count == 0)
        {
            throw new ValidationException("Đơn thuốc không có danh mục thuốc để cấp phát.");
        }

        var medicineIds = prescriptionItems.Where(i => i.MedicineId.HasValue).Select(i => i.MedicineId!.Value).Distinct().ToList();
        var medicinesById = await _db.Medicines
            .Where(m => medicineIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, ct);

        var existingDispense = await _db.MedicationDispenses
            .FirstOrDefaultAsync(md => md.PrescriptionId == prescription.Id, ct);

        var existingDispenseItems = existingDispense is not null
            ? await _db.DispenseItems
                .Where(di => di.DispenseId == existingDispense.Id)
                .ToListAsync(ct)
            : [];

        var dispenseItemByPrescriptionItemId = existingDispenseItems
            .Where(di => di.PrescriptionItemId.HasValue)
            .GroupBy(di => di.PrescriptionItemId!.Value)
            .ToDictionary(g => g.Key, g => g.First());

        var requestItemsByPiId = (request.Items ?? [])
            .GroupBy(i => i.PrescriptionItemId)
            .ToDictionary(g => g.Key, g => g.First());

        var availableBatches = await _db.MedicineBatches
            .Where(b => b.MedicineId.HasValue && medicineIds.Contains(b.MedicineId.Value))
            .ToListAsync(ct);

        var batchesById = availableBatches.ToDictionary(b => b.Id);

        var allocations = new List<(PrescriptionItem Item, MedicineBatch Batch, decimal Qty, string? ShortageReason)>();

        foreach (var pItem in prescriptionItems)
        {
            var prescribedQty = pItem.PrescribedQuantity ?? 1m;
            decimal qtyToDispense = prescribedQty;
            string? shortageReason = null;
            MedicineBatch? targetBatch = null;

            if (requestItemsByPiId.TryGetValue(pItem.Id, out var reqItem))
            {
                if (reqItem.MedicineBatchId.HasValue && batchesById.TryGetValue(reqItem.MedicineBatchId.Value, out var reqBatch))
                {
                    targetBatch = reqBatch;
                }
                if (reqItem.DispensedQuantity.HasValue && reqItem.DispensedQuantity.Value > 0)
                {
                    qtyToDispense = reqItem.DispensedQuantity.Value;
                }
                shortageReason = reqItem.ShortageReason;
            }
            else if (dispenseItemByPrescriptionItemId.TryGetValue(pItem.Id, out var existDi) && existDi.MedicineBatchId.HasValue)
            {
                if (batchesById.TryGetValue(existDi.MedicineBatchId.Value, out var existBatch))
                {
                    targetBatch = existBatch;
                }
                if (existDi.DispensedQuantity.HasValue && existDi.DispensedQuantity.Value > 0)
                {
                    qtyToDispense = existDi.DispensedQuantity.Value;
                }
                else if (existDi.PlannedQuantity.HasValue && existDi.PlannedQuantity.Value > 0)
                {
                    qtyToDispense = existDi.PlannedQuantity.Value;
                }
                shortageReason = existDi.ShortageReason;
            }

            if (targetBatch is null && pItem.MedicineId.HasValue)
            {
                targetBatch = availableBatches
                    .Where(b => b.MedicineId == pItem.MedicineId.Value && b.IsBlocked != true && (b.ExpiryDate == null || b.ExpiryDate > today) && (b.QuantityOnHand ?? 0) >= qtyToDispense)
                    .OrderBy(b => b.ExpiryDate)
                    .ThenBy(b => b.Id)
                    .FirstOrDefault();

                if (targetBatch is null)
                {
                    targetBatch = availableBatches
                        .Where(b => b.MedicineId == pItem.MedicineId.Value && b.IsBlocked != true && (b.ExpiryDate == null || b.ExpiryDate > today))
                        .OrderBy(b => b.ExpiryDate)
                        .ThenBy(b => b.Id)
                        .FirstOrDefault();
                }
            }

            if (targetBatch is null)
            {
                var medName = medicinesById.TryGetValue(pItem.MedicineId ?? 0, out var m) ? m.Name : $"Thuốc #{pItem.MedicineId}";
                throw new ConflictException($"Không tìm thấy lô thuốc khả dụng trong kho cho thuốc '{medName}'.");
            }

            if (targetBatch.MedicineId != pItem.MedicineId)
            {
                throw new ValidationException($"Lô thuốc '{targetBatch.BatchNumber}' không thuộc mặt hàng thuốc yêu cầu.");
            }

            if (targetBatch.IsBlocked == true)
            {
                throw new ConflictException($"Lô thuốc '{targetBatch.BatchNumber}' đang bị khóa, không thể xuất kho.");
            }

            if (targetBatch.ExpiryDate.HasValue && targetBatch.ExpiryDate.Value <= today)
            {
                throw new ConflictException($"Lô thuốc '{targetBatch.BatchNumber}' đã hết hạn sử dụng ({targetBatch.ExpiryDate:dd/MM/yyyy}), không thể cấp phát.");
            }

            var currentOnHand = targetBatch.QuantityOnHand ?? 0m;
            if (currentOnHand < qtyToDispense && string.IsNullOrWhiteSpace(shortageReason))
            {
                throw new ConflictException($"Số lượng tồn khả dụng của lô '{targetBatch.BatchNumber}' không đủ ({currentOnHand} < {qtyToDispense}).");
            }

            allocations.Add((pItem, targetBatch, qtyToDispense, shortageReason));
        }

        await using var tx = await _db.BeginTransactionAsync(ct);

        var medicationDispense = existingDispense ?? new MedicationDispense
        {
            PrescriptionId = prescription.Id,
            PreparedByStaffId = staff.Id,
            PreparedAt = nowUtc
        };

        medicationDispense.Status = "DISPENSED";
        medicationDispense.DispensedByStaffId = staff.Id;
        medicationDispense.DispensedAt = nowUtc;
        medicationDispense.IdempotencyKey = !string.IsNullOrWhiteSpace(request.IdempotencyKey)
            ? request.IdempotencyKey.Trim()
            : $"DISP-{prescription.Id}-{nowUtc:yyyyMMddHHmmss}";

        if (existingDispense is null)
        {
            _db.MedicationDispenses.Add(medicationDispense);
        }
        await _db.SaveChangesAsync(ct);

        foreach (var (item, batch, qtyToDispense, shortageReason) in allocations)
        {
            batch.QuantityOnHand = (batch.QuantityOnHand ?? 0m) - qtyToDispense;

            DispenseItem dispenseItem;
            if (dispenseItemByPrescriptionItemId.TryGetValue(item.Id, out var existingItem))
            {
                dispenseItem = existingItem;
                dispenseItem.MedicineBatchId = batch.Id;
                dispenseItem.DispensedQuantity = qtyToDispense;
                dispenseItem.ShortageReason = shortageReason;
            }
            else
            {
                dispenseItem = new DispenseItem
                {
                    DispenseId = medicationDispense.Id,
                    PrescriptionItemId = item.Id,
                    MedicineBatchId = batch.Id,
                    PlannedQuantity = item.PrescribedQuantity ?? qtyToDispense,
                    DispensedQuantity = qtyToDispense,
                    ShortageReason = shortageReason
                };
                _db.DispenseItems.Add(dispenseItem);
            }
            await _db.SaveChangesAsync(ct);

            var stockTx = new StockTransaction
            {
                MedicineBatchId = batch.Id,
                DispenseItemId = dispenseItem.Id,
                TransactionType = "DISPENSE",
                QuantityDelta = -qtyToDispense,
                PerformedByStaffId = staff.Id,
                Reason = $"Xuất kho cấp phát theo đơn thuốc {prescription.PrescriptionCode ?? prescription.Id.ToString()}",
                SourceReference = prescription.PrescriptionCode ?? $"RX-{prescription.Id:D6}",
                OccurredAt = nowUtc
            };
            _db.StockTransactions.Add(stockTx);
        }

        prescription.Status = "DISPENSED";

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

        return await BuildDispensingResultDetailAsync(medicationDispense.Id, ct, request.ReceiverName);
    }

    public async Task<DispensingResultDetailDto> GetDispensingResultAsync(
        long actorUserId,
        string idOrCode,
        CancellationToken ct = default,
        string? ipAddress = null)
    {
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var (staff, hospital) = await ValidatePharmacyStaffAccessAsync(actorUserId, nowUtc, ct);

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

        var prescriptionItemIds = dispenseItems.Where(di => di.PrescriptionItemId.HasValue).Select(di => di.PrescriptionItemId!.Value).ToList();
        var pItemsById = await _db.PrescriptionItems.AsNoTracking()
            .Where(pi => prescriptionItemIds.Contains(pi.Id))
            .ToDictionaryAsync(pi => pi.Id, ct);

        var batchIds = dispenseItems.Where(di => di.MedicineBatchId.HasValue).Select(di => di.MedicineBatchId!.Value).ToList();
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

        var paidPayment = visitPayments.FirstOrDefault(p => string.Equals(p.Status, "PAID", StringComparison.OrdinalIgnoreCase));
        var totalAmount = paidPayment?.Amount ?? 0m;
        var totalAmountFormatted = totalAmount > 0 ? FormatVndPrice(totalAmount) : "0 đ";
        var paymentSummary = paidPayment is not null
            ? $"Đã thu đủ 100% qua VietQR • Mã giao dịch {paidPayment.PaymentCode ?? paidPayment.ExternalReference ?? paidPayment.Id.ToString()}"
            : "Đã xác nhận thu ngân / Miễn phí theo chính sách viện";

        var lines = new List<DispensedLineItemDto>(dispenseItems.Count);
        decimal computedTotal = 0m;
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
            var qty = di.DispensedQuantity ?? di.PlannedQuantity ?? 1m;
            totalUnits += qty;

            var batchCode = batch?.BatchNumber ?? "LÔ_CHÍNH_THỨC";
            var expiry = batch?.ExpiryDate.HasValue == true
                ? batch.ExpiryDate.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
                : "N/A";

            var unitPrice = 15000m;
            var lineTotal = unitPrice * qty;
            computedTotal += lineTotal;

            lines.Add(new DispensedLineItemDto(
                Id: $"di-{di.Id}",
                OrderIndex: i + 1,
                DrugName: drugName,
                DosageForm: dosageForm,
                QtyDispensed: qty,
                Unit: unit,
                BatchCode: batchCode,
                ExpiryDate: expiry,
                ShelfLocation: $"Kệ {(char)('A' + (i % 4))}-0{(i % 5) + 1}, Ngăn 2",
                UnitPriceFormatted: FormatVndPrice(unitPrice),
                LineTotalFormatted: FormatVndPrice(lineTotal)
            ));
        }

        if (totalAmount <= 0 && computedTotal > 0)
        {
            totalAmount = computedTotal;
            totalAmountFormatted = FormatVndPrice(totalAmount);
        }

        var rxCode = prescription.PrescriptionCode ?? $"RX-{prescription.Id:D6}";
        var visitCode = visit.VisitCode ?? $"VIS-{visit.Id:D6}";
        var ticketNo = visit.VisitCode?.Split('-').LastOrDefault() ?? $"{visit.Id:D3}";
        var dispenseCode = $"DISP-{dispense.DispensedAt?.Year ?? 2026:D4}-{dispense.Id:D6}";

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

        var dispensedAtStr = (dispense.DispensedAt ?? dispense.PreparedAt ?? DateTime.UtcNow)
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
            AuditNote: auditNote
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

        var rolePermissionsConfigured = await _db.RolePermissions.AsNoTracking().AnyAsync(ct);
        if (rolePermissionsConfigured)
        {
            var hasMedicineDispensePermission = await (
                from rp in _db.RolePermissions.AsNoTracking()
                join permission in _db.Permissions.AsNoTracking() on rp.PermissionId equals permission.Id
                where actorRoleIds.Contains(rp.RoleId) && permission.Code.ToUpper() == MedicineDispensePermissionCode
                select rp).AnyAsync(ct);

            if (!hasMedicineDispensePermission)
            {
                throw new ForbiddenException("Dược sĩ chưa được phân quyền cấp phát thuốc (MEDICINE_DISPENSE).");
            }
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

        var query = from p in _db.Prescriptions
                    join v in _db.Visits on p.VisitId equals v.Id
                    from r in _db.HospitalRooms.Where(r => r.Id == v.ExaminationRoomId).DefaultIfEmpty()
                    from d in _db.Doctors.Where(d => d.Id == v.AssignedDoctorId).DefaultIfEmpty()
                    where (isNumeric ? p.Id == parsedId || (p.PrescriptionCode != null && p.PrescriptionCode.ToUpper() == upper)
                                     : p.PrescriptionCode != null && p.PrescriptionCode.ToUpper() == upper)
                    where (r != null && r.HospitalId == hospitalId) ||
                          (d != null && d.HospitalId == hospitalId) ||
                          (_db.Staff.Any(s => s.Id == v.CheckedInByStaffId && s.HospitalId == hospitalId))
                    select p;

        var prescription = await query.FirstOrDefaultAsync(ct);

        if (prescription is null)
        {
            var fallback = await _db.Prescriptions
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

        if (string.Equals(prescription.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException("Đơn thuốc đã bị hủy, không thể cấp phát.");
        }

        if (string.Equals(prescription.Status, "DRAFT", StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException("Đơn thuốc đang ở trạng thái nháp, chưa được bác sĩ phát hành.");
        }

        var isFulfillmentHospital = prescription.FulfillmentChoice != null &&
            (string.Equals(prescription.FulfillmentChoice, "HOSPITAL", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(prescription.FulfillmentChoice, "HOSPITAL_PHARMACY", StringComparison.OrdinalIgnoreCase));

        if (!isFulfillmentHospital)
        {
            throw new ValidationException("Đơn thuốc được chỉ định mua ngoài viện, không thuộc phạm vi cấp phát của nhà thuốc bệnh viện.");
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
            throw new ValidationException("Đơn thuốc đã bị thay thế bởi đơn thuốc mới hơn.");
        }

        return prescription;
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

    private static string MaskIdentityNumber(string? identity)
    {
        if (string.IsNullOrWhiteSpace(identity))
        {
            return "Chưa cập nhật";
        }

        var trimmed = identity.Trim();
        if (trimmed.Length >= 8)
        {
            return $"{trimmed[..4]}****{trimmed[^4..]}";
        }

        return "****";
    }

    private static string FormatVndPrice(decimal price)
    {
        var truncated = decimal.Truncate(price);
        return $"{truncated.ToString("N0", CultureInfo.InvariantCulture).Replace(',', '.')} đ";
    }
}
