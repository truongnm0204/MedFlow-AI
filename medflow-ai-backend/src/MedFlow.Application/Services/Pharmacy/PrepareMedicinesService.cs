using System.Globalization;
using System.Net;
using System.Text.Json;
using MedFlow.Application.Common.Time;
using MedFlow.Application.DTOs.Pharmacy;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Pharmacy;

public sealed class PrepareMedicinesService : IPrepareMedicinesService
{
    private const string PharmacyStaffRoleCode = RoleNames.PharmacyStaff;
    private const string MedicineDispensePermissionCode = PermissionCodes.MedicineDispense;
    private const string AuditEntityType = "PRESCRIPTION";
    private const string AuditPrepareViewAction = "PRESCRIPTION_PREPARE_VIEW";
    private const string AuditPrepareAction = "PRESCRIPTION_PREPARED";
    private const string AuditPrepareViewReason = "Xem thông tin đơn thuốc để soạn theo FEFO (UC45)";
    private const string AuditPrepareReason = "Soạn thuốc theo đơn và chọn lô FEFO hoàn tất (UC45)";

    private static readonly GppCheckItemDto[] StandardGppChecks =
    [
        new(
            Id: "gpp-1",
            Title: "Kiểm tra tính hợp lệ & tương tác thuốc",
            Description: "Đúng tên thuốc, dạng dùng, hàm lượng và không có tương tác bất lợi nghiêm trọng.",
            Checked: false),
        new(
            Id: "gpp-2",
            Title: "Đối chiếu liều lượng & đường dùng",
            Description: "Liều dùng, số lượng khớp chính xác với chỉ định lâm sàng của bác sĩ.",
            Checked: false),
        new(
            Id: "gpp-3",
            Title: "Kiểm tra cảm quan & hạn dùng",
            Description: "Bao bì nguyên vẹn, thuốc không biến chất, hạn dùng còn tối thiểu theo quy định.",
            Checked: false),
        new(
            Id: "gpp-4",
            Title: "Quy cách đóng gói & nhãn hướng dẫn",
            Description: "Đóng gói theo đơn, dán nhãn ghi rõ tên người bệnh, liều dùng và lưu ý đặc biệt.",
            Checked: false),
    ];

    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;

    public PrepareMedicinesService(
        IApplicationDbContext dbContext,
        TimeProvider? timeProvider = null)
    {
        _db = dbContext;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<PreparePrescriptionContextDto> GetPreparationContextAsync(
        long actorUserId,
        string idOrCode,
        CancellationToken ct = default,
        string? ipAddress = null)
    {
        ValidateIdOrCode(idOrCode);

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var nowLocal = HospitalClock.GetNow(_timeProvider);
        var todayDateOnly = HospitalClock.GetToday(_timeProvider);
        var staffContext = await ValidatePharmacyStaffAccessAsync(actorUserId, nowUtc, ct);

        var prescription = await ResolveScopedPrescriptionAsync(idOrCode, staffContext.HospitalId, ct);

        var visit = await _db.Visits
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == prescription.VisitId, ct)
            ?? throw new NotFoundException("Visit", prescription.VisitId ?? 0);

        var patient = visit.PatientId.HasValue
            ? await _db.Patients.AsNoTracking().FirstOrDefaultAsync(p => p.Id == visit.PatientId.Value, ct)
            : null;

        Doctor? doctor = null;
        if (prescription.PrescribingDoctorId.HasValue)
        {
            doctor = await _db.Doctors
                .AsNoTracking()
                .Include(d => d.Staff)
                .FirstOrDefaultAsync(d => d.Id == prescription.PrescribingDoctorId.Value && d.HospitalId == staffContext.HospitalId, ct);
        }

        HospitalRoom? room = null;
        if (visit.ExaminationRoomId.HasValue)
        {
            room = await _db.HospitalRooms
                .AsNoTracking()
                .Include(r => r.Department)
                .FirstOrDefaultAsync(r => r.Id == visit.ExaminationRoomId.Value && r.HospitalId == staffContext.HospitalId, ct);
        }

        // Diagnoses
        var diagnoses = await _db.VisitDiagnoses
            .AsNoTracking()
            .Where(vd => vd.VisitId == visit.Id)
            .OrderBy(vd => vd.IsPrimary == true ? 0 : 1)
            .ThenBy(vd => vd.Id)
            .Select(vd => new
            {
                IsPrimary = vd.IsPrimary == true,
                Code = vd.CodeSnapshot ?? "N/A",
                Description = vd.NameSnapshot ?? vd.ClinicalDiagnosis ?? "Chẩn đoán lâm sàng",
            })
            .ToListAsync(ct);

        var primaryDiag = diagnoses.FirstOrDefault(d => d.IsPrimary) ?? diagnoses.FirstOrDefault();
        var primaryDiagText = primaryDiag != null
            ? $"{primaryDiag.Code} - {primaryDiag.Description}".Trim(' ', '-')
            : "Chưa ghi nhận chẩn đoán chính";

        var secondaryDiags = diagnoses.Where(d => d != primaryDiag).ToList();
        var secondaryDiagText = secondaryDiags.Count > 0
            ? string.Join("; ", secondaryDiags.Select(d => $"{d.Code} - {d.Description}".Trim(' ', '-')))
            : "Không có chẩn đoán phụ";

        // Payment status badge
        var payments = await _db.Payments
            .AsNoTracking()
            .Where(p => p.VisitId == visit.Id)
            .ToListAsync(ct);

        var isPaid = payments.Any(p => string.Equals(p.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase) ||
                                       string.Equals(p.Status, "PAID", StringComparison.OrdinalIgnoreCase));
        var paymentBadge = isPaid
            ? "VIỆN PHÍ ĐÃ THANH TOÁN"
            : "VIỆN PHÍ CHỜ THANH TOÁN / TỰ NGUYỆN";

        // Allergy from medical record revision if available
        var allergy = await (
            from mr in _db.MedicalRecords.AsNoTracking()
            join mrr in _db.MedicalRecordRevisions.AsNoTracking() on mr.Id equals mrr.MedicalRecordId
            where mr.VisitId == visit.Id && mrr.RelevantHistoryAllergies != null
            orderby mrr.RevisionNo descending
            select mrr.RelevantHistoryAllergies).FirstOrDefaultAsync(ct);

        var allergyText = !string.IsNullOrWhiteSpace(allergy)
            ? allergy
            : "Chưa ghi nhận dị ứng thuốc đặc biệt";

        // Prescription line items
        var rawItems = await (
            from pi in _db.PrescriptionItems.AsNoTracking()
            join m in _db.Medicines.AsNoTracking() on pi.MedicineId equals (long?)m.Id
            where pi.PrescriptionId == prescription.Id && m.HospitalId == staffContext.HospitalId
            orderby pi.Id
            select new
            {
                Item = pi,
                Medicine = m,
            }).ToListAsync(ct);

        if (rawItems.Count == 0)
        {
            throw new NotFoundException("PrescriptionItems", prescription.Id);
        }

        // Existing dispense record if any
        var existingDispense = await _db.MedicationDispenses
            .AsNoTracking()
            .Where(md => md.PrescriptionId == prescription.Id)
            .OrderByDescending(md => md.PreparedAt)
            .FirstOrDefaultAsync(ct);

        var existingDispenseItems = existingDispense != null
            ? await _db.DispenseItems
                .AsNoTracking()
                .Where(di => di.DispenseId == existingDispense.Id)
                .ToListAsync(ct)
            : new List<DispenseItem>();

        var dispenseItemsByPrescriptionItemId = existingDispenseItems
            .Where(di => di.PrescriptionItemId.HasValue)
            .GroupBy(di => di.PrescriptionItemId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderBy(di => di.Id).ToList());

        // Fetch batches for all medicines in this prescription
        var medicineIds = rawItems.Select(r => r.Medicine.Id).Distinct().ToList();
        var allBatches = await _db.MedicineBatches
            .AsNoTracking()
            .Where(b => b.MedicineId.HasValue &&
                        medicineIds.Contains(b.MedicineId.Value) &&
                        b.IsBlocked != true &&
                        b.ExpiryDate > todayDateOnly)
            .OrderBy(b => b.ExpiryDate)
            .ThenBy(b => b.Id)
            .ToListAsync(ct);

        var batchesByMedicineId = allBatches
            .GroupBy(b => b.MedicineId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var itemDtos = new List<PrepareMedicineItemDto>(rawItems.Count);
        for (var i = 0; i < rawItems.Count; i++)
        {
            var raw = rawItems[i];
            var pi = raw.Item;
            var med = raw.Medicine;

            batchesByMedicineId.TryGetValue(med.Id, out var candidateBatches);
            candidateBatches ??= new List<MedicineBatch>();

            // Determine FEFO recommended batch: earliest non-expired, non-blocked batch with stock > 0
            var fefoBatch = candidateBatches.FirstOrDefault(b => (b.QuantityOnHand ?? 0) > 0);

            var batchOptions = candidateBatches.Select(b => new MedicineBatchOptionDto(
                BatchId: b.Id,
                BatchCode: b.BatchNumber ?? $"LO-{b.Id}",
                ExpiryDate: b.ExpiryDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "N/A",
                AvailableStock: b.QuantityOnHand ?? 0,
                ShelfLocation: "Chưa cập nhật",
                IsFefoRecommended: fefoBatch != null && b.Id == fefoBatch.Id
            )).ToList();

            dispenseItemsByPrescriptionItemId.TryGetValue(pi.Id, out var existingDis);
            var existingDi = existingDis?.FirstOrDefault();

            var selectedBatchId = existingDi?.MedicineBatchId ?? fefoBatch?.Id;
            var selectedBatchCode = candidateBatches.FirstOrDefault(b => b.Id == selectedBatchId)?.BatchNumber
                ?? fefoBatch?.BatchNumber
                ?? (candidateBatches.Count > 0 ? candidateBatches[0].BatchNumber : null);

            var prescribedQty = pi.PrescribedQuantity ?? 0;
            var pickedQty = existingDis is { Count: > 0 }
                ? existingDis.Sum(di => di.PlannedQuantity ?? 0m)
                : prescribedQty;
            var isPrepared = existingDi != null || (pickedQty == prescribedQty && selectedBatchId.HasValue);

            var usageInstruction = string.Join(" • ", new[]
            {
                pi.Instructions,
                pi.DoseScheduleText,
                pi.Frequency,
                pi.AdministrationRoute,
            }.Where(s => !string.IsNullOrWhiteSpace(s)));

            if (string.IsNullOrWhiteSpace(usageInstruction))
            {
                usageInstruction = "Theo chỉ dẫn của bác sĩ điều trị";
            }

            itemDtos.Add(new PrepareMedicineItemDto(
                Id: pi.Id,
                OrderIndex: i + 1,
                MedicineId: med.Id,
                MedicineCode: med.MedicineCode,
                DrugName: med.Name,
                ActiveIngredient: med.ActiveIngredient,
                Strength: med.Strength,
                DosageForm: med.DosageForm ?? "Viên",
                UsageInstruction: usageInstruction,
                PrescribedQty: prescribedQty,
                PickedQty: pickedQty,
                Unit: med.DispensingUnit ?? "viên",
                SelectedBatchId: selectedBatchId,
                SelectedBatchCode: selectedBatchCode,
                IsPrepared: isPrepared,
                AvailableBatches: batchOptions));
        }

        var ageLabel = patient?.DateOfBirth.HasValue == true
            ? $"{todayDateOnly.Year - patient.DateOfBirth.Value.Year} tuổi"
            : "N/A";

        var doctorName = doctor?.Staff?.FullName ?? "Bác sĩ điều trị";
        var roomLabel = room != null
            ? $"{room.Name} - {room.Department?.Name ?? "Khoa Khám bệnh"}"
            : "Phòng khám ngoại trú";

        var gppChecks = StandardGppChecks.Select(c => c with
        {
            Checked = existingDispense != null && string.Equals(existingDispense.Status, "READY", StringComparison.OrdinalIgnoreCase),
        }).ToList();

        var resultDto = new PreparePrescriptionContextDto(
            PrescriptionId: prescription.Id,
            RxCode: prescription.PrescriptionCode ?? $"RX-{prescription.Id:D8}",
            VisitId: visit.Id,
            VisitCode: visit.VisitCode ?? $"VIS-{visit.Id:D8}",
            PatientCode: patient?.PatientCode ?? $"PAT-{patient?.Id:D8}",
            PatientName: patient?.FullName ?? "Bệnh nhân",
            Gender: patient?.Gender != null && (string.Equals(patient.Gender, "MALE", StringComparison.OrdinalIgnoreCase) || string.Equals(patient.Gender, "Nam", StringComparison.OrdinalIgnoreCase))
                ? "Nam"
                : patient?.Gender != null && (string.Equals(patient.Gender, "FEMALE", StringComparison.OrdinalIgnoreCase) || string.Equals(patient.Gender, "Nữ", StringComparison.OrdinalIgnoreCase))
                    ? "Nữ"
                    : patient?.Gender ?? "Khác",
            AgeLabel: ageLabel,
            DoctorName: doctorName,
            RoomLabel: roomLabel,
            PrimaryDiagnosis: primaryDiagText,
            SecondaryDiagnosis: secondaryDiagText,
            CriticalAllergy: allergyText,
            PaymentBadge: paymentBadge,
            Status: prescription.Status ?? "ISSUED",
            DispenseId: existingDispense?.Id,
            IssuedAt: prescription.IssuedAt,
            Items: itemDtos,
            GppChecks: gppChecks);

        // Audit Log for view
        _db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actorUserId,
            HospitalId = staffContext.HospitalId,
            Action = AuditPrepareViewAction,
            EntityType = AuditEntityType,
            EntityId = prescription.Id.ToString(CultureInfo.InvariantCulture),
            Reason = AuditPrepareViewReason,
            ChangeData = JsonSerializer.Serialize(new
            {
                prescriptionId = prescription.Id,
                prescriptionCode = prescription.PrescriptionCode,
                visitId = visit.Id,
                itemCount = itemDtos.Count,
            }),
            IpAddress = IPAddress.TryParse(ipAddress, out var parsedIp) ? parsedIp : null,
            OccurredAt = nowUtc,
        });
        await _db.SaveChangesAsync(ct);

        return resultDto;
    }

    public async Task<PreparePrescriptionResponseDto> PreparePrescriptionAsync(
        long actorUserId,
        string idOrCode,
        PreparePrescriptionRequest request,
        CancellationToken ct = default,
        string? ipAddress = null)
    {
        ValidateIdOrCode(idOrCode);
        ArgumentNullException.ThrowIfNull(request);

        if (request.Items == null || request.Items.Count == 0)
        {
            throw new ValidationException("Danh sách thuốc cần soạn không được để trống.");
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var nowLocal = HospitalClock.GetNow(_timeProvider);
        var todayDateOnly = HospitalClock.GetToday(_timeProvider);
        var staffContext = await ValidatePharmacyStaffAccessAsync(actorUserId, nowUtc, ct);

        var preCheckPrescription = await ResolveScopedPrescriptionAsync(idOrCode, staffContext.HospitalId, ct);
        var visitId = preCheckPrescription.VisitId
            ?? throw new NotFoundException("Visit", 0);

        await using var tx = await _db.BeginTransactionAsync(ct);

        await _db.TryLockVisitForUpdateAsync(visitId, ct);
        await _db.TryLockPrescriptionForUpdateAsync(preCheckPrescription.Id, ct);

        if (_db is DbContext trackerCtx)
        {
            trackerCtx.ChangeTracker.Clear();
        }

        var visit = await _db.Visits.AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == visitId, ct)
            ?? throw new NotFoundException("Visit", visitId);
        EnsureVisitActiveForPharmacy(visit);

        var prescription = await _db.Prescriptions
            .FirstOrDefaultAsync(p => p.Id == preCheckPrescription.Id, ct)
            ?? throw new NotFoundException("Prescription", preCheckPrescription.Id);
        EnsurePrescriptionEligibleForPreparation(prescription, allowAlreadyDispensedRead: false);

        // Query all items for this prescription
        var prescriptionItems = await (
            from pi in _db.PrescriptionItems
            join m in _db.Medicines on pi.MedicineId equals (long?)m.Id
            where pi.PrescriptionId == prescription.Id && m.HospitalId == staffContext.HospitalId
            select new
            {
                Item = pi,
                Medicine = m,
            }).ToListAsync(ct);

        if (prescriptionItems.Count == 0)
        {
            throw new NotFoundException("PrescriptionItems", prescription.Id);
        }

        var itemsById = prescriptionItems.ToDictionary(x => x.Item.Id);

        // Ensure every request item corresponds to a valid prescription item
        foreach (var reqItem in request.Items)
        {
            if (!itemsById.ContainsKey(reqItem.PrescriptionItemId))
            {
                throw new ValidationException($"Khoản thuốc {reqItem.PrescriptionItemId} không thuộc đơn thuốc {prescription.PrescriptionCode}.");
            }

            if (reqItem.PickedQuantity <= 0)
            {
                throw new ValidationException($"Số lượng thực soạn cho khoản thuốc {reqItem.PrescriptionItemId} phải lớn hơn 0.");
            }
        }

        // Verify all prescription items are accounted for
        var requestedItemIds = request.Items.Select(i => i.PrescriptionItemId).ToHashSet();
        var missingItemIds = itemsById.Keys.Where(id => !requestedItemIds.Contains(id)).ToList();
        if (missingItemIds.Count > 0)
        {
            throw new ValidationException($"Còn {missingItemIds.Count} khoản thuốc trong đơn chưa được soạn đủ.");
        }

        var medicineIds = prescriptionItems.Select(x => x.Medicine.Id).Distinct().ToList();
        var candidateBatchIds = await _db.MedicineBatches.AsNoTracking()
            .Where(b => b.MedicineId.HasValue && medicineIds.Contains(b.MedicineId.Value))
            .OrderBy(b => b.Id)
            .Select(b => b.Id)
            .ToListAsync(ct);

        if (candidateBatchIds.Count > 0)
        {
            await _db.TryLockMedicineBatchesForUpdateAsync(candidateBatchIds, ct);
        }

        var lockedBatches = await _db.MedicineBatches.AsNoTracking()
            .Where(b => b.MedicineId.HasValue && medicineIds.Contains(b.MedicineId.Value))
            .ToListAsync(ct);

        // Validate batches and per-batch aggregated stocks
        var preparedItemsData = new List<(PrescriptionItem Item, Medicine Medicine, MedicineBatch Batch, decimal PickedQty, string? ShortageReason)>();
        var totalPickedByBatchId = new Dictionary<long, decimal>();

        foreach (var reqItem in request.Items)
        {
            var pair = itemsById[reqItem.PrescriptionItemId];
            var pi = pair.Item;
            var med = pair.Medicine;

            MedicineBatch? batch = null;
            if (reqItem.MedicineBatchId.HasValue)
            {
                batch = lockedBatches
                    .FirstOrDefault(b => b.Id == reqItem.MedicineBatchId.Value && b.MedicineId == med.Id);
            }
            else if (!string.IsNullOrWhiteSpace(reqItem.BatchCode))
            {
                var trimmedCode = reqItem.BatchCode.Trim();
                batch = lockedBatches
                    .FirstOrDefault(b => b.BatchNumber == trimmedCode && b.MedicineId == med.Id);
            }

            if (batch == null)
            {
                throw new ValidationException($"Không tìm thấy lô thuốc hợp lệ cho thuốc {med.Name}.");
            }

            if (preparedItemsData.Any(p => p.Item.Id == pi.Id && p.Batch.Id == batch.Id))
            {
                throw new ValidationException($"Không được chọn trùng lô {batch.BatchNumber} cho cùng khoản thuốc {med.Name}.");
            }

            if (batch.IsBlocked == true)
            {
                throw new ConflictException($"Lô thuốc {batch.BatchNumber} của thuốc {med.Name} đang bị tạm khóa.");
            }

            if (batch.ExpiryDate.HasValue && batch.ExpiryDate.Value <= todayDateOnly)
            {
                throw new ConflictException($"Lô thuốc {batch.BatchNumber} của thuốc {med.Name} đã hết hạn sử dụng ({batch.ExpiryDate.Value:dd/MM/yyyy}).");
            }

            totalPickedByBatchId.TryGetValue(batch.Id, out var priorPickedForBatch);
            var cumulativeForBatch = priorPickedForBatch + reqItem.PickedQuantity;
            var availableStock = batch.QuantityOnHand ?? 0;
            if (availableStock < cumulativeForBatch)
            {
                throw new ConflictException($"Lô thuốc {batch.BatchNumber} không đủ tồn kho khả dụng ({availableStock} < {cumulativeForBatch}) cho thuốc {med.Name}.");
            }

            totalPickedByBatchId[batch.Id] = cumulativeForBatch;
            preparedItemsData.Add((pi, med, batch, reqItem.PickedQuantity, reqItem.ShortageReason));
        }

        foreach (var group in preparedItemsData.GroupBy(x => x.Item.Id))
        {
            var first = group.First();
            var prescribedQty = first.Item.PrescribedQuantity ?? 0m;
            var totalPickedForItem = group.Sum(x => x.PickedQty);
            var hasShortageReason = group.Any(x => !string.IsNullOrWhiteSpace(x.ShortageReason));
            if (totalPickedForItem != prescribedQty && !hasShortageReason)
            {
                throw new ValidationException($"Số lượng thực soạn ({totalPickedForItem}) chưa khớp y lệnh ({prescribedQty}) cho thuốc {first.Medicine.Name}. Vui lòng nhập lý do nếu thiếu thuốc.");
            }
        }

        // Find or create MedicationDispense
        var dispense = await _db.MedicationDispenses
            .FirstOrDefaultAsync(md => md.PrescriptionId == prescription.Id, ct);

        if (dispense != null &&
            (string.Equals(dispense.Status, "DISPENSED", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(dispense.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ConflictException("Đơn thuốc đã được hoàn tất cấp phát, không thể thực hiện soạn lại.");
        }

        if (dispense == null)
        {
            dispense = new MedicationDispense
            {
                PrescriptionId = prescription.Id,
                PreparedByStaffId = staffContext.Staff.Id,
                Status = "READY",
                PreparedAt = nowLocal,
            };
            _db.MedicationDispenses.Add(dispense);
            await _db.SaveChangesAsync(ct);
        }
        else
        {
            dispense.PreparedByStaffId = staffContext.Staff.Id;
            dispense.Status = "READY";
            dispense.PreparedAt = nowLocal;
        }

        // Manage DispenseItems (preserve multi-batch FEFO allocations and store DispensedQuantity = null!)
        var existingDispenseItems = await _db.DispenseItems
            .Where(di => di.DispenseId == dispense.Id)
            .OrderBy(di => di.Id)
            .ToListAsync(ct);

        var retainedItems = new HashSet<DispenseItem>();

        foreach (var prep in preparedItemsData)
        {
            var existingMatch = existingDispenseItems.FirstOrDefault(di =>
                !retainedItems.Contains(di) &&
                di.PrescriptionItemId == prep.Item.Id &&
                di.MedicineBatchId == prep.Batch.Id);

            existingMatch ??= existingDispenseItems.FirstOrDefault(di =>
                !retainedItems.Contains(di) &&
                di.PrescriptionItemId == prep.Item.Id &&
                preparedItemsData.Count(p => p.Item.Id == prep.Item.Id) == 1);

            if (existingMatch != null)
            {
                retainedItems.Add(existingMatch);
                existingMatch.MedicineBatchId = prep.Batch.Id;
                existingMatch.PlannedQuantity = prep.PickedQty;
                existingMatch.DispensedQuantity = null;
                existingMatch.ShortageReason = prep.ShortageReason;
            }
            else
            {
                var newDi = new DispenseItem
                {
                    DispenseId = dispense.Id,
                    PrescriptionItemId = prep.Item.Id,
                    MedicineBatchId = prep.Batch.Id,
                    PlannedQuantity = prep.PickedQty,
                    DispensedQuantity = null,
                    ShortageReason = prep.ShortageReason,
                };
                _db.DispenseItems.Add(newDi);
            }
        }

        var obsoleteItems = existingDispenseItems
            .Where(di => !retainedItems.Contains(di))
            .ToList();
        if (obsoleteItems.Count > 0)
        {
            _db.DispenseItems.RemoveRange(obsoleteItems);
        }

        // Update Prescription Status to READY
        prescription.Status = "READY";

        // Audit Log
        _db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actorUserId,
            HospitalId = staffContext.HospitalId,
            Action = AuditPrepareAction,
            EntityType = AuditEntityType,
            EntityId = prescription.Id.ToString(CultureInfo.InvariantCulture),
            Reason = AuditPrepareReason,
            ChangeData = JsonSerializer.Serialize(new
            {
                prescriptionId = prescription.Id,
                prescriptionCode = prescription.PrescriptionCode,
                dispenseId = dispense.Id,
                preparedItemCount = preparedItemsData.Count,
                staffId = staffContext.Staff.Id,
                notes = request.Notes,
            }),
            IpAddress = IPAddress.TryParse(ipAddress, out var prepIp) ? prepIp : null,
            OccurredAt = nowUtc,
        });

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        var staffName = !string.IsNullOrWhiteSpace(staffContext.Staff.FullName)
            ? staffContext.Staff.FullName
            : staffContext.User.Email;

        return new PreparePrescriptionResponseDto(
            PrescriptionId: prescription.Id,
            PrescriptionCode: prescription.PrescriptionCode ?? $"RX-{prescription.Id:D8}",
            DispenseId: dispense.Id,
            Status: "READY",
            PreparedAt: nowUtc,
            PreparedByStaffName: staffName,
            PreparedItemCount: preparedItemsData.Count,
            Message: $"Đã hoàn tất soạn {preparedItemsData.Count} khoản thuốc theo nguyên tắc FEFO. Đơn thuốc đã sẵn sàng chuyển sang quầy phát.");
    }

    private static void ValidateIdOrCode(string idOrCode)
    {
        if (string.IsNullOrWhiteSpace(idOrCode))
        {
            throw new ValidationException("Mã hoặc định danh đơn thuốc không được để trống.");
        }

        if (idOrCode.Trim().Length > 100)
        {
            throw new ValidationException("Mã hoặc định danh đơn thuốc không được vượt quá 100 ký tự.");
        }
    }

    private async Task<(Staff Staff, long HospitalId, User User)> ValidatePharmacyStaffAccessAsync(
        long actorUserId,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var actorUser = await _db.Users
            .AsNoTracking()
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

        var actorRoleIds = await _db.UserRoles
            .AsNoTracking()
            .Where(ur => ur.UserId == actorUserId)
            .Select(ur => ur.RoleId)
            .ToListAsync(ct);

        var hasPharmacyStaffRole = await _db.Roles
            .AsNoTracking()
            .AnyAsync(
                r => actorRoleIds.Contains(r.Id) &&
                     r.Code.ToUpper() == PharmacyStaffRoleCode,
                ct);

        if (!hasPharmacyStaffRole)
        {
            throw new ForbiddenException("Người dùng không có quyền truy cập nhà thuốc.");
        }

        var actorStaff = await _db.Staff
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == actorUserId, ct);

        if (actorStaff == null || !actorStaff.HospitalId.HasValue)
        {
            throw new ForbiddenException("Tài khoản nhân viên nhà thuốc chưa được gán cơ sở y tế.");
        }

        if (!string.Equals(actorStaff.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            throw new ForbiddenException("Hồ sơ nhân sự của dược sĩ đã ngừng hoạt động.");
        }

        var hospitalId = actorStaff.HospitalId.Value;

        var hospital = await _db.Hospitals
            .AsNoTracking()
            .FirstOrDefaultAsync(h => h.Id == hospitalId, ct)
            ?? throw new NotFoundException("Hospital", hospitalId);

        if (hospital.IsActive != true)
        {
            throw new ForbiddenException("Cơ sở y tế được gán cho dược sĩ hiện đang ngừng hoạt động.");
        }

        var hasMedicineDispensePermission = await (
            from rp in _db.RolePermissions.AsNoTracking()
            join permission in _db.Permissions.AsNoTracking() on rp.PermissionId equals permission.Id
            where actorRoleIds.Contains(rp.RoleId) &&
                  permission.Code.ToUpper() == MedicineDispensePermissionCode
            select rp).AnyAsync(ct);

        if (!hasMedicineDispensePermission)
        {
            throw new ForbiddenException("Người dùng không có quyền thực hiện soạn thuốc theo đơn.");
        }

        return (actorStaff, hospitalId, actorUser);
    }

    private async Task<Prescription> ResolveScopedPrescriptionAsync(
        string idOrCode,
        long hospitalId,
        CancellationToken ct)
    {
        var trimmed = idOrCode.Trim();
        var isNumericId = long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedId) && parsedId > 0;

        var query =
            from prescription in _db.Prescriptions
            join visit in _db.Visits on prescription.VisitId equals visit.Id
            join room in _db.HospitalRooms on visit.ExaminationRoomId equals (long?)room.Id into rooms
            from room in rooms.DefaultIfEmpty()
            join assignedDoctor in _db.Doctors on visit.AssignedDoctorId equals (long?)assignedDoctor.Id into doctors
            from assignedDoctor in doctors.DefaultIfEmpty()
            where isNumericId
                ? prescription.Id == parsedId || (prescription.PrescriptionCode != null && prescription.PrescriptionCode!.ToUpper() == trimmed.ToUpper())
                : prescription.PrescriptionCode != null && prescription.PrescriptionCode!.ToUpper() == trimmed.ToUpper()
            where (room != null && room.HospitalId == hospitalId) ||
                  (assignedDoctor != null && assignedDoctor.HospitalId == hospitalId) ||
                  (prescription.PrescribingDoctorId.HasValue && _db.Doctors.Any(d => d.Id == prescription.PrescribingDoctorId!.Value && d.HospitalId == hospitalId))
            where !_db.Prescriptions.Any(replacement =>
                replacement.ReplacesPrescriptionId == prescription.Id &&
                replacement.Status != null &&
                replacement.Status!.ToUpper() != "CANCELLED")
            select new { Prescription = prescription, Visit = visit };

        var resolved = await query.FirstOrDefaultAsync(ct);
        if (resolved == null)
        {
            throw new NotFoundException("Prescription", idOrCode);
        }

        EnsureVisitActiveForPharmacy(resolved.Visit);
        EnsurePrescriptionEligibleForPreparation(resolved.Prescription, allowAlreadyDispensedRead: true);

        return resolved.Prescription;
    }

    private static void EnsureVisitActiveForPharmacy(Visit visit)
    {
        var visitStatus = visit.Status?.Trim().ToUpperInvariant();
        if (visitStatus is "CANCELLED" or "NO_SHOW")
        {
            throw new ConflictException("Lượt khám đã hủy hoặc không đến, không thể soạn thuốc.");
        }
    }

    private static void EnsurePrescriptionEligibleForPreparation(
        Prescription prescription,
        bool allowAlreadyDispensedRead)
    {
        var currentStatus = prescription.Status?.Trim().ToUpperInvariant();
        if (currentStatus == "DRAFT")
        {
            throw new ConflictException("Đơn thuốc đang ở trạng thái nháp (DRAFT), chưa được bác sĩ phát hành.");
        }

        if (currentStatus == "CANCELLED")
        {
            throw new ConflictException("Đơn thuốc đã bị hủy, không thể thực hiện soạn thuốc.");
        }

        if (!allowAlreadyDispensedRead && currentStatus == "DISPENSED")
        {
            throw new ConflictException("Đơn thuốc đã được hoàn tất cấp phát, không thể thực hiện soạn lại.");
        }

        if (!string.IsNullOrWhiteSpace(prescription.FulfillmentChoice))
        {
            var choice = prescription.FulfillmentChoice.Trim().ToUpperInvariant();
            if (choice is not ("HOSPITAL" or "HOSPITAL_PHARMACY"))
            {
                throw new ConflictException("Đơn thuốc mua ngoài viện (EXTERNAL_PHARMACY), không thuộc phạm vi soạn thuốc tại bệnh viện.");
            }
        }
    }
}
