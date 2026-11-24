using System.Globalization;
using System.Net;
using System.Text.Json;
using MedFlow.Application.Common.Time;
using MedFlow.Application.DTOs.Pharmacy;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Pharmacy;

public sealed class FefoBatchSelectionService : IFefoBatchSelectionService
{
    public const string FefoBatchSelectedAction = "FEFO_BATCH_SELECTED";
    public const string FefoBatchViewedAction = "FEFO_BATCH_VIEWED";

    private const string AuditEntityType = "PRESCRIPTION";
    private const string UnknownShelfFallback = "Chưa cập nhật";
    private const int MinOverrideReasonLength = 5;
    private const int MaxOverrideReasonLength = 500;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;

    public FefoBatchSelectionService(
        IApplicationDbContext db,
        TimeProvider? timeProvider = null)
    {
        _db = db;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<FefoBatchSelectionContextDto> GetFefoBatchesAsync(
        long actorUserId,
        string idOrCode,
        bool includeUnavailable = true,
        CancellationToken ct = default,
        string? ipAddress = null)
    {
        ValidateIdOrCode(idOrCode);

        var nowUtc = HospitalClock.GetUtcNow(_timeProvider);
        var today = HospitalClock.GetToday(_timeProvider);
        var staffContext = await ValidatePharmacyStaffAccessAsync(actorUserId, nowUtc, ct);

        var prescription = await ResolveScopedPrescriptionAsync(idOrCode, staffContext.HospitalId, ct);

        var visit = await _db.Visits
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == prescription.VisitId, ct)
            ?? throw new NotFoundException("Visit", prescription.VisitId ?? 0);

        var patient = visit.PatientId.HasValue
            ? await _db.Patients.AsNoTracking().FirstOrDefaultAsync(p => p.Id == visit.PatientId.Value, ct)
            : null;

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

        var existingDispense = await _db.MedicationDispenses
            .AsNoTracking()
            .Where(md => md.PrescriptionId == prescription.Id)
            .OrderByDescending(md => md.PreparedAt)
            .ThenByDescending(md => md.Id)
            .FirstOrDefaultAsync(ct);

        var existingDispenseItems = existingDispense is not null
            ? await _db.DispenseItems
                .AsNoTracking()
                .Where(di => di.DispenseId == existingDispense.Id)
                .ToListAsync(ct)
            : [];

        var dispenseItemByPrescriptionItemId = existingDispenseItems
            .Where(di => di.PrescriptionItemId.HasValue)
            .GroupBy(di => di.PrescriptionItemId!.Value)
            .ToDictionary(g => g.Key, g => g.First());

        var savedOverrideReasons = await LoadSavedOverrideReasonsAsync(prescription.Id, ct);

        var medicineIds = rawItems.Select(r => r.Medicine.Id).Distinct().ToList();
        var allBatches = await _db.MedicineBatches
            .AsNoTracking()
            .Where(b => b.MedicineId.HasValue && medicineIds.Contains(b.MedicineId.Value))
            .OrderBy(b => b.ExpiryDate ?? DateOnly.MaxValue)
            .ThenBy(b => b.Id)
            .ToListAsync(ct);

        var batchesByMedicineId = allBatches
            .GroupBy(b => b.MedicineId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var requirementDtos = new List<FefoPrescriptionItemRequirementDto>(rawItems.Count);
        for (var i = 0; i < rawItems.Count; i++)
        {
            var pi = rawItems[i].Item;
            var med = rawItems[i].Medicine;
            var requiredQty = pi.PrescribedQuantity ?? 1m;

            batchesByMedicineId.TryGetValue(med.Id, out var rawBatches);
            rawBatches ??= [];

            var selectable = new List<(MedicineBatch Batch, int DaysUntilExpiry)>();
            var unavailable = new List<FefoBatchCandidateDto>();

            foreach (var b in rawBatches)
            {
                var batchCode = !string.IsNullOrWhiteSpace(b.BatchNumber)
                    ? b.BatchNumber.Trim()
                    : $"LO-{b.Id}";
                var stock = b.QuantityOnHand ?? 0m;
                var days = b.ExpiryDate.HasValue
                    ? b.ExpiryDate.Value.DayNumber - today.DayNumber
                    : -1;
                var expiryDisplay = b.ExpiryDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "Chưa cập nhật";
                var expiryIso = b.ExpiryDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

                if (!b.ExpiryDate.HasValue)
                {
                    unavailable.Add(new FefoBatchCandidateDto(
                        BatchId: b.Id,
                        BatchCode: batchCode,
                        ExpiryDate: expiryDisplay,
                        ExpiryDateIso: expiryIso,
                        DaysUntilExpiry: days,
                        AvailableStock: stock,
                        ShelfLocation: UnknownShelfFallback,
                        Status: "EXPIRED",
                        IsFefoRecommended: false,
                        IsSelectable: false,
                        BlockReason: "Lô thuốc thiếu thông tin hạn sử dụng — Hệ thống khóa tự động"));
                }
                else if (b.ExpiryDate.Value < today)
                {
                    unavailable.Add(new FefoBatchCandidateDto(
                        BatchId: b.Id,
                        BatchCode: batchCode,
                        ExpiryDate: expiryDisplay,
                        ExpiryDateIso: expiryIso,
                        DaysUntilExpiry: days,
                        AvailableStock: stock,
                        ShelfLocation: UnknownShelfFallback,
                        Status: "EXPIRED",
                        IsFefoRecommended: false,
                        IsSelectable: false,
                        BlockReason: $"Lô đã quá hạn sử dụng ({expiryDisplay}) theo kiểm định GPP — Hệ thống khóa tự động"));
                }
                else if (med.IsActive == false)
                {
                    unavailable.Add(new FefoBatchCandidateDto(
                        BatchId: b.Id,
                        BatchCode: batchCode,
                        ExpiryDate: expiryDisplay,
                        ExpiryDateIso: expiryIso,
                        DaysUntilExpiry: days,
                        AvailableStock: stock,
                        ShelfLocation: UnknownShelfFallback,
                        Status: "BLOCKED",
                        IsFefoRecommended: false,
                        IsSelectable: false,
                        BlockReason: "Danh mục thuốc đang ngừng hoạt động (INACTIVE)"));
                }
                else if (b.IsBlocked == true)
                {
                    unavailable.Add(new FefoBatchCandidateDto(
                        BatchId: b.Id,
                        BatchCode: batchCode,
                        ExpiryDate: expiryDisplay,
                        ExpiryDateIso: expiryIso,
                        DaysUntilExpiry: days,
                        AvailableStock: stock,
                        ShelfLocation: UnknownShelfFallback,
                        Status: "BLOCKED",
                        IsFefoRecommended: false,
                        IsSelectable: false,
                        BlockReason: "Lô đang bị khóa / niêm phong kiểm định"));
                }
                else if (stock <= 0m)
                {
                    unavailable.Add(new FefoBatchCandidateDto(
                        BatchId: b.Id,
                        BatchCode: batchCode,
                        ExpiryDate: expiryDisplay,
                        ExpiryDateIso: expiryIso,
                        DaysUntilExpiry: days,
                        AvailableStock: stock,
                        ShelfLocation: UnknownShelfFallback,
                        Status: "BLOCKED",
                        IsFefoRecommended: false,
                        IsSelectable: false,
                        BlockReason: "Lô không còn tồn kho khả dụng"));
                }
                else if (stock < requiredQty)
                {
                    unavailable.Add(new FefoBatchCandidateDto(
                        BatchId: b.Id,
                        BatchCode: batchCode,
                        ExpiryDate: expiryDisplay,
                        ExpiryDateIso: expiryIso,
                        DaysUntilExpiry: days,
                        AvailableStock: stock,
                        ShelfLocation: UnknownShelfFallback,
                        Status: "BLOCKED",
                        IsFefoRecommended: false,
                        IsSelectable: false,
                        BlockReason: $"Tồn kho lô ({stock:0.###}) không đủ số lượng kê đơn ({requiredQty:0.###})"));
                }
                else
                {
                    selectable.Add((b, days));
                }
            }

            var orderedSelectable = selectable
                .OrderBy(x => x.Batch.ExpiryDate!.Value)
                .ThenBy(x => x.Batch.Id)
                .Select((entry, idx) =>
                {
                    var isFefo = idx == 0;
                    var code = !string.IsNullOrWhiteSpace(entry.Batch.BatchNumber)
                        ? entry.Batch.BatchNumber.Trim()
                        : $"LO-{entry.Batch.Id}";

                    return new FefoBatchCandidateDto(
                        BatchId: entry.Batch.Id,
                        BatchCode: code,
                        ExpiryDate: entry.Batch.ExpiryDate!.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                        ExpiryDateIso: entry.Batch.ExpiryDate!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        DaysUntilExpiry: entry.DaysUntilExpiry,
                        AvailableStock: entry.Batch.QuantityOnHand ?? 0m,
                        ShelfLocation: UnknownShelfFallback,
                        Status: isFefo ? "FEFO_RECOMMENDED" : "AVAILABLE",
                        IsFefoRecommended: isFefo,
                        IsSelectable: true,
                        BlockReason: null);
                })
                .ToList();

            var combinedBatches = includeUnavailable
                ? orderedSelectable.Concat(unavailable).ToList()
                : orderedSelectable;

            dispenseItemByPrescriptionItemId.TryGetValue(pi.Id, out var existingDi);

            var preselected = orderedSelectable.FirstOrDefault(b => existingDi?.MedicineBatchId == b.BatchId)
                ?? orderedSelectable.FirstOrDefault(b => b.IsFefoRecommended);

            savedOverrideReasons.TryGetValue(pi.Id, out var savedReason);

            requirementDtos.Add(new FefoPrescriptionItemRequirementDto(
                PrescriptionItemId: pi.Id,
                OrderIndex: i + 1,
                MedicineId: med.Id,
                MedicineCode: med.MedicineCode,
                DrugName: med.Name,
                ActiveIngredient: med.ActiveIngredient,
                Strength: med.Strength,
                DosageForm: med.DosageForm ?? "Viên",
                RequiredQuantity: requiredQty,
                Unit: med.DispensingUnit ?? pi.SingleDoseUnit ?? "viên",
                SelectedBatchId: preselected?.BatchId,
                SelectedBatchCode: preselected?.BatchCode,
                OverrideReason: savedReason,
                Batches: combinedBatches));
        }

        var response = new FefoBatchSelectionContextDto(
            PrescriptionId: prescription.Id,
            PrescriptionCode: prescription.PrescriptionCode ?? $"RX-{prescription.Id:D8}",
            VisitId: visit.Id,
            VisitCode: visit.VisitCode ?? $"VIS-{visit.Id:D8}",
            PatientCode: patient?.PatientCode ?? $"PAT-{patient?.Id ?? 0:D8}",
            PatientName: patient?.FullName ?? "Bệnh nhân",
            Status: prescription.Status ?? "ISSUED",
            DispenseId: existingDispense?.Id,
            TodayHospitalDate: today,
            Items: requirementDtos);

        _db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actorUserId,
            HospitalId = staffContext.HospitalId,
            Action = FefoBatchViewedAction,
            EntityType = AuditEntityType,
            EntityId = prescription.Id.ToString(CultureInfo.InvariantCulture),
            Reason = "Xem danh sách lô thuốc khả dụng theo chuẩn FEFO (UC45)",
            ChangeData = JsonSerializer.Serialize(new
            {
                prescriptionId = prescription.Id,
                prescriptionCode = prescription.PrescriptionCode,
                itemCount = requirementDtos.Count,
                today = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            }, JsonOptions),
            IpAddress = IPAddress.TryParse(ipAddress, out var parsedIp) ? parsedIp : null,
            OccurredAt = nowUtc,
        });
        await _db.SaveChangesAsync(ct);

        return response;
    }

    public async Task<SelectFefoBatchesResponseDto> SelectFefoBatchesAsync(
        long actorUserId,
        string idOrCode,
        SelectFefoBatchesRequest? request,
        CancellationToken ct = default,
        string? ipAddress = null)
    {
        ValidateIdOrCode(idOrCode);

        if (request is null || request.Items is null || request.Items.Count == 0)
        {
            throw new ValidationException("Danh sách lựa chọn lô thuốc FEFO không được để trống.");
        }

        var duplicateItemIds = request.Items
            .GroupBy(i => i.PrescriptionItemId)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicateItemIds.Count > 0)
        {
            throw new ValidationException("Không được gửi trùng lặp khoản thuốc trong cùng một yêu cầu chọn lô FEFO.");
        }

        var nowUtc = HospitalClock.GetUtcNow(_timeProvider);
        var nowLocal = HospitalClock.GetNow(_timeProvider);
        var today = HospitalClock.GetToday(_timeProvider);
        var staffContext = await ValidatePharmacyStaffAccessAsync(actorUserId, nowUtc, ct);

        var prescription = await ResolveScopedPrescriptionAsync(idOrCode, staffContext.HospitalId, ct);

        var currentStatus = prescription.Status?.Trim().ToUpperInvariant();
        if (currentStatus == "DISPENSED")
        {
            throw new ConflictException("Đơn thuốc đã được hoàn tất cấp phát, không thể thay đổi lô thuốc FEFO.");
        }

        if (currentStatus == "CANCELLED")
        {
            throw new ConflictException("Đơn thuốc đã bị hủy, không thể thực hiện chọn lô thuốc FEFO.");
        }

        var prescriptionItems = await (
            from pi in _db.PrescriptionItems
            join m in _db.Medicines on pi.MedicineId equals (long?)m.Id
            where pi.PrescriptionId == prescription.Id && m.HospitalId == staffContext.HospitalId
            orderby pi.Id
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

        foreach (var reqItem in request.Items)
        {
            if (!itemsById.ContainsKey(reqItem.PrescriptionItemId))
            {
                throw new ValidationException($"Khoản thuốc {reqItem.PrescriptionItemId} không thuộc đơn thuốc {prescription.PrescriptionCode}.");
            }
        }

        var requestedIds = request.Items.Select(i => i.PrescriptionItemId).ToHashSet();
        var missingCount = itemsById.Keys.Count(id => !requestedIds.Contains(id));
        if (missingCount > 0)
        {
            throw new ValidationException($"Còn {missingCount} khoản thuốc trong đơn chưa được chọn lô xuất kho FEFO.");
        }

        var medicineIds = prescriptionItems.Select(x => x.Medicine.Id).Distinct().ToList();
        var allMedicineBatches = await _db.MedicineBatches
            .Where(b => b.MedicineId.HasValue && medicineIds.Contains(b.MedicineId.Value))
            .OrderBy(b => b.ExpiryDate ?? DateOnly.MaxValue)
            .ThenBy(b => b.Id)
            .ToListAsync(ct);

        var batchesByMedicineId = allMedicineBatches
            .GroupBy(b => b.MedicineId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var validatedSelections = new List<(
            PrescriptionItem Item,
            Medicine Medicine,
            MedicineBatch Batch,
            decimal AllocatedQty,
            bool IsFefoRecommended,
            bool IsFefoOverride,
            string? OverrideReason)>(request.Items.Count);

        foreach (var reqItem in request.Items)
        {
            var pair = itemsById[reqItem.PrescriptionItemId];
            var pi = pair.Item;
            var med = pair.Medicine;

            if (med.IsActive == false)
            {
                throw new ConflictException($"Thuốc {med.Name} hiện đang ngừng hoạt động (INACTIVE), không thể chọn lô xuất kho.");
            }

            var prescribedQty = pi.PrescribedQuantity ?? 1m;
            var allocatedQty = reqItem.PickedQuantity ?? reqItem.Quantity ?? prescribedQty;
            if (allocatedQty <= 0m)
            {
                throw new ValidationException($"Số lượng phân bổ cho thuốc {med.Name} phải lớn hơn 0.");
            }

            batchesByMedicineId.TryGetValue(med.Id, out var medBatches);
            medBatches ??= [];

            MedicineBatch? selectedBatch = null;
            if (reqItem.MedicineBatchId.HasValue && reqItem.MedicineBatchId.Value > 0)
            {
                selectedBatch = medBatches.FirstOrDefault(b => b.Id == reqItem.MedicineBatchId.Value);
            }
            else if (!string.IsNullOrWhiteSpace(reqItem.BatchCode))
            {
                var trimmedCode = reqItem.BatchCode.Trim();
                selectedBatch = medBatches.FirstOrDefault(b =>
                    b.BatchNumber != null &&
                    string.Equals(b.BatchNumber.Trim(), trimmedCode, StringComparison.OrdinalIgnoreCase));
            }

            if (selectedBatch is null)
            {
                throw new ValidationException($"Không tìm thấy lô thuốc hợp lệ cho thuốc {med.Name}.");
            }

            var batchLabel = selectedBatch.BatchNumber ?? $"LO-{selectedBatch.Id}";

            if (selectedBatch.IsBlocked == true)
            {
                throw new ConflictException($"Lô thuốc {batchLabel} của thuốc {med.Name} đang bị khóa / niêm phong.");
            }

            if (!selectedBatch.ExpiryDate.HasValue || selectedBatch.ExpiryDate.Value < today)
            {
                var expStr = selectedBatch.ExpiryDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "N/A";
                throw new ConflictException($"Lô thuốc {batchLabel} của thuốc {med.Name} đã hết hạn sử dụng ({expStr}).");
            }

            var availableStock = selectedBatch.QuantityOnHand ?? 0m;
            if (availableStock < allocatedQty)
            {
                throw new ConflictException(
                    $"Lô thuốc {batchLabel} không đủ tồn kho khả dụng ({availableStock:0.###} < {allocatedQty:0.###}) cho thuốc {med.Name}.");
            }

            var eligibleBatches = medBatches
                .Where(b =>
                    b.IsBlocked != true &&
                    b.ExpiryDate.HasValue &&
                    b.ExpiryDate.Value >= today &&
                    (b.QuantityOnHand ?? 0m) >= allocatedQty)
                .OrderBy(b => b.ExpiryDate!.Value)
                .ThenBy(b => b.Id)
                .ToList();

            var earliestBatch = eligibleBatches.First();
            var isFefoRecommended = selectedBatch.Id == earliestBatch.Id;
            var isFefoOverride = selectedBatch.ExpiryDate.Value > earliestBatch.ExpiryDate!.Value;

            var rawOverrideReason = FirstNonEmpty(
                reqItem.OverrideReason,
                reqItem.Reason,
                request.OverrideReason,
                request.Notes);

            string? normalizedReason = null;
            if (!string.IsNullOrWhiteSpace(rawOverrideReason))
            {
                normalizedReason = rawOverrideReason.Trim();
                if (normalizedReason.Length > MaxOverrideReasonLength)
                {
                    throw new ValidationException($"Lý do chọn lô ngoài ưu tiên FEFO không được vượt quá {MaxOverrideReasonLength} ký tự.");
                }
            }

            if (isFefoOverride &&
                (string.IsNullOrWhiteSpace(normalizedReason) || normalizedReason.Length < MinOverrideReasonLength))
            {
                var chosenExp = selectedBatch.ExpiryDate.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
                var earliestCode = earliestBatch.BatchNumber ?? $"LO-{earliestBatch.Id}";
                var earliestExp = earliestBatch.ExpiryDate.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
                throw new ValidationException(
                    $"Lô {batchLabel} (HSD {chosenExp}) có hạn dùng xa hơn lô ưu tiên FEFO {earliestCode} (HSD {earliestExp}) đang còn đủ tồn kho. Vui lòng nhập lý do hợp lệ (tối thiểu {MinOverrideReasonLength} ký tự).");
            }

            validatedSelections.Add((
                pi,
                med,
                selectedBatch,
                allocatedQty,
                isFefoRecommended,
                isFefoOverride,
                isFefoOverride ? normalizedReason : normalizedReason));
        }

        await using var tx = await _db.BeginTransactionAsync(ct);

        var nextStatus = string.Equals(currentStatus, "READY", StringComparison.OrdinalIgnoreCase)
            ? "READY"
            : "PREPARING";

        var dispense = await _db.MedicationDispenses
            .FirstOrDefaultAsync(md => md.PrescriptionId == prescription.Id, ct);

        if (dispense is null)
        {
            dispense = new MedicationDispense
            {
                PrescriptionId = prescription.Id,
                PreparedByStaffId = staffContext.Staff.Id,
                Status = nextStatus,
                PreparedAt = nowLocal,
            };
            _db.MedicationDispenses.Add(dispense);
            await _db.SaveChangesAsync(ct);
        }
        else
        {
            dispense.PreparedByStaffId = staffContext.Staff.Id;
            if (!string.Equals(dispense.Status, "READY", StringComparison.OrdinalIgnoreCase))
            {
                dispense.Status = nextStatus;
            }

            dispense.PreparedAt = nowLocal;
        }

        var existingDispenseItems = await _db.DispenseItems
            .Where(di => di.DispenseId == dispense.Id)
            .ToListAsync(ct);

        var existingByPrescriptionItemId = existingDispenseItems
            .Where(di => di.PrescriptionItemId.HasValue)
            .GroupBy(di => di.PrescriptionItemId!.Value)
            .ToDictionary(g => g.Key, g => g.First());

        var allocationDtos = new List<SelectedFefoBatchItemResultDto>(validatedSelections.Count);

        foreach (var sel in validatedSelections)
        {
            if (existingByPrescriptionItemId.TryGetValue(sel.Item.Id, out var existingDi))
            {
                existingDi.MedicineBatchId = sel.Batch.Id;
                existingDi.PlannedQuantity = sel.AllocatedQty;
                existingDi.DispensedQuantity ??= 0m;
            }
            else
            {
                _db.DispenseItems.Add(new DispenseItem
                {
                    DispenseId = dispense.Id,
                    PrescriptionItemId = sel.Item.Id,
                    MedicineBatchId = sel.Batch.Id,
                    PlannedQuantity = sel.AllocatedQty,
                    DispensedQuantity = 0m,
                });
            }

            allocationDtos.Add(new SelectedFefoBatchItemResultDto(
                PrescriptionItemId: sel.Item.Id,
                MedicineId: sel.Medicine.Id,
                DrugName: sel.Medicine.Name,
                MedicineBatchId: sel.Batch.Id,
                BatchCode: sel.Batch.BatchNumber ?? $"LO-{sel.Batch.Id}",
                ExpiryDate: sel.Batch.ExpiryDate!.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                AllocatedQuantity: sel.AllocatedQty,
                IsFefoRecommended: sel.IsFefoRecommended,
                IsFefoOverride: sel.IsFefoOverride,
                OverrideReason: sel.OverrideReason));
        }

        prescription.Status = nextStatus;

        var auditReason = validatedSelections.Any(s => s.IsFefoOverride)
            ? $"Xác nhận chọn lô thuốc có điều chỉnh ngoài ưu tiên FEFO (UC45)"
            : "Xác nhận chọn lô thuốc theo đúng ưu tiên FEFO (UC45)";

        _db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actorUserId,
            HospitalId = staffContext.HospitalId,
            Action = FefoBatchSelectedAction,
            EntityType = AuditEntityType,
            EntityId = prescription.Id.ToString(CultureInfo.InvariantCulture),
            Reason = auditReason,
            ChangeData = JsonSerializer.Serialize(new FefoSelectionAuditPayload(
                PrescriptionId: prescription.Id,
                PrescriptionCode: prescription.PrescriptionCode ?? $"RX-{prescription.Id:D8}",
                DispenseId: dispense.Id,
                StaffId: staffContext.Staff.Id,
                Allocations: allocationDtos), JsonOptions),
            IpAddress = IPAddress.TryParse(ipAddress, out var parsedIp) ? parsedIp : null,
            OccurredAt = nowUtc,
        });

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        var staffName = !string.IsNullOrWhiteSpace(staffContext.Staff.FullName)
            ? staffContext.Staff.FullName
            : staffContext.User.Email;

        return new SelectFefoBatchesResponseDto(
            PrescriptionId: prescription.Id,
            PrescriptionCode: prescription.PrescriptionCode ?? $"RX-{prescription.Id:D8}",
            DispenseId: dispense.Id,
            Status: nextStatus,
            SelectedAt: nowUtc,
            SelectedByStaffName: staffName,
            ItemCount: allocationDtos.Count,
            Allocations: allocationDtos,
            Message: $"Đã xác nhận phân bổ lô thuốc theo nguyên tắc FEFO cho {allocationDtos.Count} khoản thuốc.");
    }

    private async Task<Dictionary<long, string>> LoadSavedOverrideReasonsAsync(
        long prescriptionId,
        CancellationToken ct)
    {
        var result = new Dictionary<long, string>();
        var entityIdText = prescriptionId.ToString(CultureInfo.InvariantCulture);

        var latestLog = await _db.AuditLogs
            .AsNoTracking()
            .Where(a => a.Action == FefoBatchSelectedAction && a.EntityId == entityIdText)
            .OrderByDescending(a => a.OccurredAt)
            .ThenByDescending(a => a.Id)
            .FirstOrDefaultAsync(ct);

        if (latestLog is null || string.IsNullOrWhiteSpace(latestLog.ChangeData))
        {
            return result;
        }

        try
        {
            var payload = JsonSerializer.Deserialize<FefoSelectionAuditPayload>(latestLog.ChangeData, JsonOptions);
            if (payload?.Allocations is null)
            {
                return result;
            }

            foreach (var alloc in payload.Allocations)
            {
                if (!string.IsNullOrWhiteSpace(alloc.OverrideReason))
                {
                    result[alloc.PrescriptionItemId] = alloc.OverrideReason.Trim();
                }
            }
        }
        catch (JsonException)
        {
            // Ignore malformed legacy audit JSON
        }

        return result;
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

    private static string? FirstNonEmpty(params string?[] candidates)
        => candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));

    private async Task<(Staff Staff, long HospitalId, User User)> ValidatePharmacyStaffAccessAsync(
        long actorUserId,
        DateTime nowUtc,
        CancellationToken ct)
    {
        if (actorUserId <= 0)
        {
            throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");
        }

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

        var hasPharmacyStaffRole = actorRoleIds.Count > 0 && await _db.Roles
            .AsNoTracking()
            .AnyAsync(
                r => actorRoleIds.Contains(r.Id) &&
                     r.Code.ToUpper() == RoleNames.PharmacyStaff,
                ct);

        if (!hasPharmacyStaffRole)
        {
            throw new ForbiddenException("Người dùng không có quyền truy cập phân hệ Nhà thuốc.");
        }

        var actorStaff = await _db.Staff
            .AsNoTracking()
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
                  permission.Code.ToUpper() == PermissionCodes.MedicineDispense
            select rp.PermissionId).AnyAsync(ct);

        if (!hasMedicineDispensePermission)
        {
            throw new ForbiddenException("Người dùng không có quyền thực hiện chọn lô thuốc theo đơn (MEDICINE_DISPENSE).");
        }

        return (actorStaff, hospitalId, actorUser);
    }

    private async Task<Prescription> ResolveScopedPrescriptionAsync(
        string idOrCode,
        long hospitalId,
        CancellationToken ct)
    {
        var trimmed = idOrCode.Trim();
        var upper = trimmed.ToUpperInvariant();
        var isNumericId = long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedId) && parsedId > 0;

        var candidate = await _db.Prescriptions
            .FirstOrDefaultAsync(
                p => isNumericId
                    ? p.Id == parsedId || (p.PrescriptionCode != null && p.PrescriptionCode.ToUpper() == upper)
                    : p.PrescriptionCode != null && p.PrescriptionCode.ToUpper() == upper,
                ct);

        if (candidate is null)
        {
            throw new NotFoundException("Prescription", trimmed);
        }

        var isReplaced = await _db.Prescriptions
            .AsNoTracking()
            .AnyAsync(
                replacement =>
                    replacement.ReplacesPrescriptionId == candidate.Id &&
                    replacement.Status != null &&
                    replacement.Status.ToUpper() != "CANCELLED",
                ct);

        if (isReplaced)
        {
            throw new NotFoundException("Prescription", trimmed);
        }

        if (!string.IsNullOrWhiteSpace(candidate.FulfillmentChoice))
        {
            var choice = candidate.FulfillmentChoice.Trim().ToUpperInvariant();
            if (choice is not ("HOSPITAL" or "HOSPITAL_PHARMACY"))
            {
                throw new NotFoundException("Prescription", trimmed);
            }
        }

        var visit = candidate.VisitId.HasValue
            ? await _db.Visits.AsNoTracking().FirstOrDefaultAsync(v => v.Id == candidate.VisitId.Value, ct)
            : null;

        long? resolvedHospitalId = null;

        if (visit?.ExaminationRoomId.HasValue == true)
        {
            resolvedHospitalId = await _db.HospitalRooms
                .AsNoTracking()
                .Where(r => r.Id == visit.ExaminationRoomId.Value)
                .Select(r => r.HospitalId)
                .FirstOrDefaultAsync(ct);
        }

        if (!resolvedHospitalId.HasValue && visit?.AssignedDoctorId.HasValue == true)
        {
            resolvedHospitalId = await _db.Doctors
                .AsNoTracking()
                .Where(d => d.Id == visit.AssignedDoctorId.Value)
                .Select(d => d.HospitalId)
                .FirstOrDefaultAsync(ct);
        }

        if (!resolvedHospitalId.HasValue && candidate.PrescribingDoctorId.HasValue)
        {
            resolvedHospitalId = await _db.Doctors
                .AsNoTracking()
                .Where(d => d.Id == candidate.PrescribingDoctorId.Value)
                .Select(d => d.HospitalId)
                .FirstOrDefaultAsync(ct);
        }

        if (!resolvedHospitalId.HasValue)
        {
            resolvedHospitalId = await (
                from pi in _db.PrescriptionItems.AsNoTracking()
                join m in _db.Medicines.AsNoTracking() on pi.MedicineId equals (long?)m.Id
                where pi.PrescriptionId == candidate.Id && m.HospitalId.HasValue
                select m.HospitalId).FirstOrDefaultAsync(ct);
        }

        if (!resolvedHospitalId.HasValue)
        {
            throw new NotFoundException("Prescription", trimmed);
        }

        if (resolvedHospitalId.Value != hospitalId)
        {
            throw new ForbiddenException("Bạn không có quyền truy cập đơn thuốc thuộc cơ sở y tế khác.");
        }

        return candidate;
    }

    private sealed record FefoSelectionAuditPayload(
        long PrescriptionId,
        string PrescriptionCode,
        long DispenseId,
        long StaffId,
        IReadOnlyList<SelectedFefoBatchItemResultDto> Allocations);
}
