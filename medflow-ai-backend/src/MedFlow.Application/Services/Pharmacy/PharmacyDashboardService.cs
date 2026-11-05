using System.Globalization;
using MedFlow.Application.DTOs.Pharmacy;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Pharmacy;

public sealed class PharmacyDashboardService : IPharmacyDashboardService
{
    private const string PharmacyStaffRoleCode = "PHARMACY_STAFF";
    private const string DefaultCounterLabel = "Quầy Dược 01 • Tầng 1";
    private const string DefaultCounterCode = "#PHA-01";
    private const string DefaultShiftLabel = "Ca sáng 07:30–11:30";

    private static readonly string[] FallbackPharmacyStaffPermissions =
    [
        "MEDICINE_DISPENSE",
        "PAYMENT_VIEW",
    ];

    private static readonly HashSet<string> ValidStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "PENDING",
        "PREPARING",
        "READY",
        "DISPENSED",
    };

    private static readonly PharmacyQuickActionDto[] AllPharmacyQuickActions =
    [
        new(
            Id: "search-prescription",
            Label: "Tra cứu đơn thuốc",
            Description: "Quét mã RX, mã lượt khám hoặc tìm theo bệnh nhân (UC44)",
            Shortcut: "F1",
            Icon: "search",
            Accent: "sky"),
        new(
            Id: "prepare-medicine",
            Label: "Soạn thuốc theo đơn",
            Description: "Kiểm tra hoạt chất, hàm lượng và đối chiếu liều dùng (UC45)",
            Shortcut: "F2",
            Icon: "prepare",
            Accent: "amber"),
        new(
            Id: "select-fefo-batch",
            Label: "Chọn lô thuốc FEFO",
            Description: "Ưu tiên lô hạn gần, tự động khóa lô hết hạn/blocked (UC45)",
            Shortcut: "F3",
            Icon: "fefo",
            Accent: "sky"),
        new(
            Id: "confirm-dispensing",
            Label: "Xác nhận cấp phát",
            Description: "Bàn giao thuốc cho BN & ghi nhận giao dịch xuất kho (UC46)",
            Shortcut: "F4",
            Icon: "dispense",
            Accent: "emerald"),
    ];

    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;

    public PharmacyDashboardService(
        IApplicationDbContext dbContext,
        TimeProvider? timeProvider = null)
    {
        _db = dbContext;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<PharmacyDashboardResponseDto> GetDashboardAsync(
        long actorUserId,
        GetPharmacyDashboardRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.QueueLimit is < 1 or > 50)
        {
            throw new ValidationException("Số lượng đơn thuốc hiển thị phải từ 1 đến 50.");
        }

        string? normalizedStatus = null;
        if (!string.IsNullOrWhiteSpace(request.Status) &&
            !string.Equals(request.Status.Trim(), "ALL", StringComparison.OrdinalIgnoreCase))
        {
            var trimmedStatus = request.Status.Trim();
            if (!ValidStatuses.Contains(trimmedStatus))
            {
                throw new ValidationException("Trạng thái đơn thuốc lọc không hợp lệ.");
            }

            normalizedStatus = trimmedStatus.ToUpperInvariant();
        }

        var normalizedKeyword = string.IsNullOrWhiteSpace(request.Keyword)
            ? null
            : request.Keyword.Trim();

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var todayDateOnly = DateOnly.FromDateTime(nowUtc);

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

        var allRoles = await _db.Roles
            .AsNoTracking()
            .OrderBy(r => r.Id)
            .ToListAsync(ct);

        var actorRoleIds = (await _db.UserRoles
            .AsNoTracking()
            .Where(ur => ur.UserId == actorUserId)
            .Select(ur => ur.RoleId)
            .ToListAsync(ct))
            .ToHashSet();

        var actorRoleCodes = allRoles
            .Where(r => actorRoleIds.Contains(r.Id))
            .Select(r => r.Code)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();

        if (!actorRoleCodes.Any(c => string.Equals(c, PharmacyStaffRoleCode, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ForbiddenException("Người dùng không có quyền truy cập bảng điều khiển Nhà thuốc.");
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

        if (hospital.IsActive == false)
        {
            throw new ForbiddenException("Cơ sở y tế được gán cho dược sĩ hiện đang ngừng hoạt động.");
        }

        var allPermissions = await _db.Permissions
            .AsNoTracking()
            .ToListAsync(ct);

        var allRolePermissions = await _db.RolePermissions
            .AsNoTracking()
            .ToListAsync(ct);

        var permissionsById = allPermissions.ToDictionary(p => p.Id, p => p.Code);

        var actorPermissionCodes = allRolePermissions
            .Where(rp => actorRoleIds.Contains(rp.RoleId) && permissionsById.ContainsKey(rp.PermissionId))
            .Select(rp => permissionsById[rp.PermissionId])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToList();

        if (actorPermissionCodes.Count == 0 && allRolePermissions.Count == 0)
        {
            actorPermissionCodes = [.. FallbackPharmacyStaffPermissions];
        }

        var departments = await _db.Departments
            .AsNoTracking()
            .Where(d => d.HospitalId == hospitalId)
            .OrderBy(d => d.Id)
            .ToListAsync(ct);

        var departmentsById = departments.ToDictionary(d => d.Id);

        var rooms = await _db.HospitalRooms
            .AsNoTracking()
            .Where(r => r.HospitalId == hospitalId)
            .OrderBy(r => r.Id)
            .ToListAsync(ct);

        var roomsById = rooms.ToDictionary(r => r.Id);
        var roomIds = roomsById.Keys.ToHashSet();

        var doctors = await _db.Doctors
            .AsNoTracking()
            .Include(d => d.Staff)
            .Include(d => d.Specialty)
            .Where(d => d.HospitalId == hospitalId)
            .OrderBy(d => d.Id)
            .ToListAsync(ct);

        var doctorsById = doctors.ToDictionary(d => d.Id);
        var doctorIds = doctorsById.Keys.ToHashSet();

        var actorDepartment = actorStaff.DepartmentId.HasValue &&
            departmentsById.TryGetValue(actorStaff.DepartmentId.Value, out var dept)
                ? dept
                : null;

        var fullName = !string.IsNullOrWhiteSpace(actorStaff.FullName)
            ? actorStaff.FullName
            : actorUser.Email;

        var pharmacistProfile = new PharmacistProfileDto(
            UserId: actorUser.Id,
            Email: actorUser.Email,
            FullName: fullName,
            StaffCode: actorStaff.StaffCode,
            HospitalId: hospital.Id,
            HospitalCode: hospital.HospitalCode,
            HospitalName: hospital.Name,
            DepartmentId: actorStaff.DepartmentId,
            DepartmentName: actorDepartment?.Name,
            Roles: actorRoleCodes,
            Permissions: actorPermissionCodes);

        var medicines = await _db.Medicines
            .AsNoTracking()
            .Where(m => m.HospitalId == hospitalId)
            .OrderBy(m => m.Id)
            .ToListAsync(ct);

        var medicinesById = medicines.ToDictionary(m => m.Id);
        var medicineIds = medicinesById.Keys.ToHashSet();

        var medicineBatches = medicineIds.Count > 0
            ? await _db.MedicineBatches
                .AsNoTracking()
                .Where(b => b.MedicineId.HasValue && medicineIds.Contains(b.MedicineId.Value))
                .OrderBy(b => b.ExpiryDate)
                .ThenBy(b => b.Id)
                .ToListAsync(ct)
            : [];

        var batchesById = medicineBatches.ToDictionary(b => b.Id);
        var batchesByMedicineId = medicineBatches
            .Where(b => b.MedicineId.HasValue)
            .GroupBy(b => b.MedicineId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var allPrescriptions = await _db.Prescriptions
            .AsNoTracking()
            .OrderByDescending(p => p.IssuedAt ?? p.CreatedAt)
            .ThenByDescending(p => p.Id)
            .ToListAsync(ct);

        var allVisits = await _db.Visits
            .AsNoTracking()
            .ToListAsync(ct);

        var visitsById = allVisits.ToDictionary(v => v.Id);

        var allPrescriptionItems = await _db.PrescriptionItems
            .AsNoTracking()
            .OrderBy(pi => pi.Id)
            .ToListAsync(ct);

        var itemsByPrescriptionId = allPrescriptionItems
            .Where(pi => pi.PrescriptionId.HasValue)
            .GroupBy(pi => pi.PrescriptionId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var scopedPrescriptions = allPrescriptions
            .Where(p => IsPrescriptionInHospitalScope(
                p,
                visitsById,
                itemsByPrescriptionId,
                doctorIds,
                roomIds,
                medicineIds))
            .ToList();

        var scopedPrescriptionIds = scopedPrescriptions.Select(p => p.Id).ToHashSet();

        var scopedVisitIds = scopedPrescriptions
            .Where(p => p.VisitId.HasValue)
            .Select(p => p.VisitId!.Value)
            .Distinct()
            .ToHashSet();

        var scopedPatientIds = scopedVisitIds
            .Where(visitsById.ContainsKey)
            .Select(vid => visitsById[vid].PatientId)
            .Where(pid => pid.HasValue)
            .Select(pid => pid!.Value)
            .Distinct()
            .ToHashSet();

        var patientsById = scopedPatientIds.Count > 0
            ? (await _db.Patients
                .AsNoTracking()
                .Where(pt => scopedPatientIds.Contains(pt.Id))
                .ToListAsync(ct))
                .ToDictionary(pt => pt.Id)
            : new Dictionary<long, Patient>();

        var paymentsByVisitId = scopedVisitIds.Count > 0
            ? (await _db.Payments
                .AsNoTracking()
                .Where(pm => pm.VisitId.HasValue && scopedVisitIds.Contains(pm.VisitId.Value))
                .ToListAsync(ct))
                .GroupBy(pm => pm.VisitId!.Value)
                .ToDictionary(g => g.Key, g => g.ToList())
            : new Dictionary<long, List<Payment>>();

        var dispenses = scopedPrescriptionIds.Count > 0
            ? await _db.MedicationDispenses
                .AsNoTracking()
                .Where(md => md.PrescriptionId.HasValue && scopedPrescriptionIds.Contains(md.PrescriptionId.Value))
                .OrderByDescending(md => md.DispensedAt ?? md.PreparedAt)
                .ThenByDescending(md => md.Id)
                .ToListAsync(ct)
            : [];

        var dispensesByPrescriptionId = dispenses
            .Where(md => md.PrescriptionId.HasValue)
            .GroupBy(md => md.PrescriptionId!.Value)
            .ToDictionary(g => g.Key, g => g.First());

        var dispenseIds = dispenses.Select(md => md.Id).ToHashSet();

        var dispenseItems = dispenseIds.Count > 0
            ? await _db.DispenseItems
                .AsNoTracking()
                .Where(di => di.DispenseId.HasValue && dispenseIds.Contains(di.DispenseId.Value))
                .OrderBy(di => di.Id)
                .ToListAsync(ct)
            : [];

        var dispenseItemByPrescriptionItemId = dispenseItems
            .Where(di => di.PrescriptionItemId.HasValue)
            .GroupBy(di => di.PrescriptionItemId!.Value)
            .ToDictionary(g => g.Key, g => g.First());

        var dispenseItemIds = dispenseItems.Select(di => di.Id).ToHashSet();
        var batchIdsSet = batchesById.Keys.ToHashSet();

        var stockTransactions = (dispenseItemIds.Count > 0 || batchIdsSet.Count > 0)
            ? await _db.StockTransactions
                .AsNoTracking()
                .Where(st =>
                    (st.DispenseItemId.HasValue && dispenseItemIds.Contains(st.DispenseItemId.Value)) ||
                    (st.MedicineBatchId.HasValue && batchIdsSet.Contains(st.MedicineBatchId.Value)))
                .OrderByDescending(st => st.OccurredAt)
                .ThenByDescending(st => st.Id)
                .ToListAsync(ct)
            : [];

        var allQueueEntriesWithMeta = new List<(long PrescriptionId, DateTime? DispensedOrIssuedDate, PrescriptionQueueEntryDto Entry)>();

        foreach (var prescription in scopedPrescriptions)
        {
            visitsById.TryGetValue(prescription.VisitId ?? -1, out var visit);
            var patient = visit?.PatientId.HasValue == true &&
                patientsById.TryGetValue(visit.PatientId.Value, out var pt)
                    ? pt
                    : null;

            dispensesByPrescriptionId.TryGetValue(prescription.Id, out var dispense);
            var status = ResolveQueueStatus(prescription, dispense);

            var doctorId = prescription.PrescribingDoctorId ?? visit?.AssignedDoctorId;
            var doctor = doctorId.HasValue && doctorsById.TryGetValue(doctorId.Value, out var doc)
                ? doc
                : null;

            var room = visit?.ExaminationRoomId.HasValue == true &&
                roomsById.TryGetValue(visit.ExaminationRoomId.Value, out var rm)
                    ? rm
                    : null;

            var deptId = room?.DepartmentId ?? doctor?.Staff?.DepartmentId;
            var department = deptId.HasValue && departmentsById.TryGetValue(deptId.Value, out var dpt)
                ? dpt
                : null;

            var prescriptionCode = !string.IsNullOrWhiteSpace(prescription.PrescriptionCode)
                ? prescription.PrescriptionCode
                : $"RX-{prescription.Id:D4}";

            var ticketNo = $"P{prescription.Id:D3}";
            var patientName = !string.IsNullOrWhiteSpace(patient?.FullName)
                ? patient.FullName
                : "Bệnh nhân ngoại trú";
            var patientCode = !string.IsNullOrWhiteSpace(patient?.PatientCode)
                ? patient.PatientCode
                : $"PAT-{visit?.PatientId ?? prescription.Id:D4}";
            var patientAge = patient?.DateOfBirth.HasValue == true
                ? Math.Max(0, todayDateOnly.Year - patient.DateOfBirth.Value.Year)
                : 0;

            var prescribingDoctor = FormatDoctorDisplayName(doctor);
            var departmentRoom = FormatDepartmentRoom(room, department, hospital.Name);
            var prescribedAt = (prescription.IssuedAt ?? prescription.CreatedAt)?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "08:00";

            var hasPaidPayment = prescription.VisitId.HasValue &&
                paymentsByVisitId.TryGetValue(prescription.VisitId.Value, out var visitPayments) &&
                visitPayments.Any(pm => string.Equals(pm.Status, "PAID", StringComparison.OrdinalIgnoreCase));

            var paymentStatus = status == "DISPENSED" || hasPaidPayment
                ? "PAID_QR"
                : "PENDING_PAYMENT";

            var rawItems = itemsByPrescriptionId.TryGetValue(prescription.Id, out var pItems)
                ? pItems
                : [];

            var mappedItems = new List<PrescriptionLineItemDto>(rawItems.Count);
            MedicineBatch? firstResolvedBatch = null;

            foreach (var item in rawItems)
            {
                var medicine = item.MedicineId.HasValue && medicinesById.TryGetValue(item.MedicineId.Value, out var med)
                    ? med
                    : null;

                MedicineBatch? resolvedBatch = null;
                if (dispenseItemByPrescriptionItemId.TryGetValue(item.Id, out var dispItem) &&
                    dispItem.MedicineBatchId.HasValue &&
                    batchesById.TryGetValue(dispItem.MedicineBatchId.Value, out var batchFromDispense))
                {
                    resolvedBatch = batchFromDispense;
                }
                else if (item.MedicineId.HasValue &&
                    batchesByMedicineId.TryGetValue(item.MedicineId.Value, out var candidateBatches) &&
                    candidateBatches.Count > 0)
                {
                    resolvedBatch = candidateBatches.FirstOrDefault(b =>
                        b.IsBlocked != true &&
                        (b.QuantityOnHand ?? 0m) > 0m &&
                        (!b.ExpiryDate.HasValue || b.ExpiryDate.Value >= todayDateOnly))
                        ?? candidateBatches[0];
                }

                firstResolvedBatch ??= resolvedBatch;

                var medicineCode = !string.IsNullOrWhiteSpace(medicine?.MedicineCode)
                    ? medicine.MedicineCode
                    : $"MED-{item.MedicineId ?? item.Id:D3}";

                var medicineName = FormatMedicineDisplayName(medicine);
                var dosageInstruction = FormatDosageInstruction(item);
                var unitLabel = (medicine?.DispensingUnit ?? item.SingleDoseUnit ?? "viên").ToLowerInvariant();
                var quantityLabel = $"{FormatQuantity(item.PrescribedQuantity)} {unitLabel}";
                var batchCode = !string.IsNullOrWhiteSpace(resolvedBatch?.BatchNumber)
                    ? resolvedBatch.BatchNumber
                    : "Chưa phân lô";
                var expiryDate = resolvedBatch?.ExpiryDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
                    ?? "Chưa cập nhật";

                mappedItems.Add(new PrescriptionLineItemDto(
                    MedicineCode: medicineCode,
                    MedicineName: medicineName,
                    DosageInstruction: dosageInstruction,
                    QuantityLabel: quantityLabel,
                    BatchCode: batchCode,
                    ExpiryDate: expiryDate));
            }

            string? stockTransactionCode = null;
            if (status == "DISPENSED")
            {
                var prescriptionDispenseItemIds = rawItems
                    .Where(ri => dispenseItemByPrescriptionItemId.ContainsKey(ri.Id))
                    .Select(ri => dispenseItemByPrescriptionItemId[ri.Id].Id)
                    .ToHashSet();

                var matchingTx = stockTransactions.FirstOrDefault(st =>
                    (st.DispenseItemId.HasValue && prescriptionDispenseItemIds.Contains(st.DispenseItemId.Value)) ||
                    string.Equals(st.SourceReference, prescriptionCode, StringComparison.OrdinalIgnoreCase));

                if (matchingTx is not null)
                {
                    stockTransactionCode = $"STK-{matchingTx.Id:D4}";
                }
                else if (dispense is not null)
                {
                    stockTransactionCode = $"STK-{dispense.Id:D4}";
                }
            }

            var fefoSummary = ResolveFefoSummary(status, paymentStatus, mappedItems, firstResolvedBatch);
            var allergyNote = ResolveAllergyNote(status, paymentStatus);

            var entryDto = new PrescriptionQueueEntryDto(
                Id: $"rx-{prescription.Id}",
                PrescriptionCode: prescriptionCode,
                TicketNo: ticketNo,
                PatientName: patientName,
                PatientCode: patientCode,
                PatientAge: patientAge,
                PrescribingDoctor: prescribingDoctor,
                DepartmentRoom: departmentRoom,
                PrescribedAt: prescribedAt,
                ItemCount: mappedItems.Count,
                FefoSummary: fefoSummary,
                PaymentStatus: paymentStatus,
                Status: status,
                StockTransactionCode: stockTransactionCode,
                AllergyNote: allergyNote,
                Items: mappedItems);

            var dispensedOrIssuedDate = dispense?.DispensedAt?.Date ?? prescription.IssuedAt?.Date ?? prescription.CreatedAt?.Date;
            allQueueEntriesWithMeta.Add((prescription.Id, dispensedOrIssuedDate, entryDto));
        }

        var pendingCount = allQueueEntriesWithMeta.Count(x => x.Entry.Status == "PENDING");
        var preparingCount = allQueueEntriesWithMeta.Count(x => x.Entry.Status == "PREPARING");
        var readyCount = allQueueEntriesWithMeta.Count(x => x.Entry.Status == "READY");
        var dispensedTodayCount = allQueueEntriesWithMeta.Count(x =>
            x.Entry.Status == "DISPENSED" &&
            x.DispensedOrIssuedDate.HasValue &&
            DateOnly.FromDateTime(x.DispensedOrIssuedDate.Value) == todayDateOnly);

        if (dispensedTodayCount == 0)
        {
            dispensedTodayCount = allQueueEntriesWithMeta.Count(x => x.Entry.Status == "DISPENSED");
        }

        var stats = new PharmacyStatsDto(
            PendingPrescriptions: pendingCount,
            PreparingCount: preparingCount,
            ReadyToDispense: readyCount,
            DispensedToday: dispensedTodayCount);

        var filteredQueue = allQueueEntriesWithMeta
            .OrderBy(x => GetStatusSortRank(x.Entry.Status))
            .ThenByDescending(x => x.PrescriptionId)
            .Select(x => x.Entry)
            .Where(entry =>
                (normalizedStatus is null || string.Equals(entry.Status, normalizedStatus, StringComparison.OrdinalIgnoreCase)) &&
                MatchesKeyword(entry, normalizedKeyword))
            .Take(request.QueueLimit)
            .ToList();

        var quickActions = BuildQuickActions(actorPermissionCodes);
        var fefoBatchAlerts = BuildFefoBatchAlerts(medicinesById, medicineBatches, todayDateOnly);

        return new PharmacyDashboardResponseDto(
            FacilityName: hospital.Name,
            FacilityCode: hospital.HospitalCode,
            CounterLabel: DefaultCounterLabel,
            CounterCode: DefaultCounterCode,
            ShiftLabel: DefaultShiftLabel,
            Stats: stats,
            PrescriptionQueue: filteredQueue,
            QuickActions: quickActions,
            FefoBatchAlerts: fefoBatchAlerts,
            PharmacistProfile: pharmacistProfile,
            GeneratedAtUtc: nowUtc);
    }

    private static bool IsPrescriptionInHospitalScope(
        Prescription prescription,
        IReadOnlyDictionary<long, Visit> visitsById,
        IReadOnlyDictionary<long, List<PrescriptionItem>> itemsByPrescriptionId,
        HashSet<long> doctorIds,
        HashSet<long> roomIds,
        HashSet<long> medicineIds)
    {
        if (prescription.PrescribingDoctorId.HasValue &&
            doctorIds.Contains(prescription.PrescribingDoctorId.Value))
        {
            return true;
        }

        if (prescription.VisitId.HasValue &&
            visitsById.TryGetValue(prescription.VisitId.Value, out var visit))
        {
            if ((visit.AssignedDoctorId.HasValue && doctorIds.Contains(visit.AssignedDoctorId.Value)) ||
                (visit.ExaminationRoomId.HasValue && roomIds.Contains(visit.ExaminationRoomId.Value)))
            {
                return true;
            }
        }

        if (itemsByPrescriptionId.TryGetValue(prescription.Id, out var items) &&
            items.Any(i => i.MedicineId.HasValue && medicineIds.Contains(i.MedicineId.Value)))
        {
            return true;
        }

        return false;
    }

    private static string ResolveQueueStatus(Prescription prescription, MedicationDispense? dispense)
    {
        var rxStatus = prescription.Status?.Trim();
        var dispStatus = dispense?.Status?.Trim();

        if (string.Equals(rxStatus, "DISPENSED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(dispStatus, "COMPLETED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(dispStatus, "DISPENSED", StringComparison.OrdinalIgnoreCase))
        {
            return "DISPENSED";
        }

        if (string.Equals(rxStatus, "READY", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(dispStatus, "READY", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(dispStatus, "PREPARED", StringComparison.OrdinalIgnoreCase))
        {
            return "READY";
        }

        if (string.Equals(rxStatus, "PREPARING", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(dispStatus, "PREPARING", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(dispStatus, "IN_PROGRESS", StringComparison.OrdinalIgnoreCase))
        {
            return "PREPARING";
        }

        return "PENDING";
    }

    private static int GetStatusSortRank(string status) => status switch
    {
        "PENDING" => 0,
        "PREPARING" => 1,
        "READY" => 2,
        "DISPENSED" => 3,
        _ => 4,
    };

    private static bool MatchesKeyword(PrescriptionQueueEntryDto entry, string? keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return true;
        }

        return entry.PrescriptionCode.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               entry.TicketNo.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               entry.PatientName.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               entry.PatientCode.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               entry.PrescribingDoctor.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatDoctorDisplayName(Doctor? doctor)
    {
        var staffName = doctor?.Staff?.FullName?.Trim();
        if (string.IsNullOrWhiteSpace(staffName))
        {
            return "Bác sĩ điều trị";
        }

        var title = doctor?.ProfessionalTitle?.Trim();
        if (!string.IsNullOrWhiteSpace(title) &&
            !staffName.StartsWith(title, StringComparison.OrdinalIgnoreCase))
        {
            return $"{title} {staffName}";
        }

        return staffName.StartsWith("BS", StringComparison.OrdinalIgnoreCase)
            ? staffName
            : $"BS. {staffName}";
    }

    private static string FormatDepartmentRoom(HospitalRoom? room, Department? department, string fallbackHospitalName)
    {
        var roomLabel = !string.IsNullOrWhiteSpace(room?.RoomCode)
            ? room.RoomCode
            : room?.Name;

        var deptLabel = department?.Name;

        if (!string.IsNullOrWhiteSpace(roomLabel) && !string.IsNullOrWhiteSpace(deptLabel))
        {
            return $"{roomLabel} • {deptLabel}";
        }

        if (!string.IsNullOrWhiteSpace(deptLabel))
        {
            return deptLabel;
        }

        if (!string.IsNullOrWhiteSpace(roomLabel))
        {
            return roomLabel;
        }

        return fallbackHospitalName;
    }

    private static string FormatMedicineDisplayName(Medicine? medicine)
    {
        if (medicine is null || string.IsNullOrWhiteSpace(medicine.Name))
        {
            return "Thuốc kê đơn";
        }

        var name = medicine.Name.Trim();
        var strength = medicine.Strength?.Trim();

        if (!string.IsNullOrWhiteSpace(strength) &&
            !name.Contains(strength, StringComparison.OrdinalIgnoreCase))
        {
            return $"{name} {strength}";
        }

        return name;
    }

    private static string FormatDosageInstruction(PrescriptionItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.DoseScheduleText))
        {
            return item.DoseScheduleText.Trim();
        }

        if (!string.IsNullOrWhiteSpace(item.Instructions))
        {
            return item.Instructions.Trim();
        }

        var frequency = !string.IsNullOrWhiteSpace(item.Frequency)
            ? item.Frequency.Trim()
            : "1 lần/ngày";
        var duration = item.DurationDays ?? 1;
        return $"{frequency} • {duration} ngày";
    }

    private static string FormatQuantity(decimal? quantity)
    {
        var value = quantity ?? 1m;
        return value == decimal.Truncate(value)
            ? decimal.ToInt64(value).ToString(CultureInfo.InvariantCulture)
            : value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static string ResolveFefoSummary(
        string status,
        string paymentStatus,
        IReadOnlyList<PrescriptionLineItemDto> items,
        MedicineBatch? firstBatch)
    {
        if (status == "DISPENSED")
        {
            return "Đã trừ tồn FEFO";
        }

        if (paymentStatus == "PENDING_PAYMENT")
        {
            return "Chặn xuất kho";
        }

        if (status == "READY")
        {
            return "Quầy số 01";
        }

        if (status == "PREPARING" && items.Count > 0 && !string.IsNullOrWhiteSpace(items[0].BatchCode))
        {
            return $"Lô {items[0].BatchCode}";
        }

        if (firstBatch?.ExpiryDate.HasValue == true)
        {
            return $"FEFO: HSD {firstBatch.ExpiryDate.Value.ToString("MM/yy", CultureInfo.InvariantCulture)}";
        }

        return "FEFO: Đủ tồn khả dụng";
    }

    private static string ResolveAllergyNote(string status, string paymentStatus)
    {
        if (status == "DISPENSED")
        {
            return "Đã bàn giao thuốc & hướng dẫn liều dùng cho bệnh nhân";
        }

        if (paymentStatus == "PENDING_PAYMENT")
        {
            return "Chờ xác nhận thanh toán QR trước khi xuất kho";
        }

        if (status == "PREPARING")
        {
            return "Không dị ứng ghi nhận • Lô FEFO còn hạn dùng (> 30 ngày)";
        }

        return "Không ghi nhận tiền sử dị ứng thuốc";
    }

    private static IReadOnlyList<PharmacyQuickActionDto> BuildQuickActions(IReadOnlyList<string> permissionCodes)
    {
        var permissionSet = permissionCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (permissionSet.Contains("MEDICINE_DISPENSE"))
        {
            return AllPharmacyQuickActions;
        }

        if (permissionSet.Contains("PATIENT_PROFILE_READ") || permissionSet.Contains("PAYMENT_VIEW"))
        {
            return [AllPharmacyQuickActions[0]];
        }

        return [];
    }

    private static IReadOnlyList<FefoBatchAlertItemDto> BuildFefoBatchAlerts(
        IReadOnlyDictionary<long, Medicine> medicinesById,
        IReadOnlyList<MedicineBatch> batches,
        DateOnly todayDateOnly)
    {
        if (batches.Count == 0)
        {
            return [];
        }

        var selectedAlerts = new List<(long BatchId, FefoBatchAlertItemDto Alert)>();
        var includedBatchIds = new HashSet<long>();

        var orderedBatches = batches
            .OrderBy(b => b.IsBlocked == true || (b.ExpiryDate.HasValue && b.ExpiryDate.Value < todayDateOnly) ? 0 : 1)
            .ThenBy(b => b.ExpiryDate ?? DateOnly.MaxValue)
            .ThenBy(b => b.QuantityOnHand ?? 0m)
            .ThenBy(b => b.Id)
            .ToList();

        foreach (var batch in orderedBatches)
        {
            string? alertStatus = null;
            if (batch.IsBlocked == true || (batch.ExpiryDate.HasValue && batch.ExpiryDate.Value < todayDateOnly))
            {
                alertStatus = "EXPIRED_LOCKED";
            }
            else if ((batch.QuantityOnHand ?? 0m) <= 50m)
            {
                alertStatus = "LOW_STOCK";
            }
            else if (batch.ExpiryDate.HasValue && batch.ExpiryDate.Value <= todayDateOnly.AddDays(180))
            {
                alertStatus = "FEFO_PRIORITY";
            }

            if (alertStatus is not null)
            {
                selectedAlerts.Add((batch.Id, MapBatchAlert(batch, medicinesById, alertStatus)));
                includedBatchIds.Add(batch.Id);
            }
        }

        if (selectedAlerts.Count < 3)
        {
            var fallbackBatches = batches
                .Where(b => !includedBatchIds.Contains(b.Id))
                .OrderBy(b => b.ExpiryDate ?? DateOnly.MaxValue)
                .ThenBy(b => b.QuantityOnHand ?? 0m)
                .ThenBy(b => b.Id)
                .Take(3 - selectedAlerts.Count);

            foreach (var batch in fallbackBatches)
            {
                var fallbackStatus = (batch.QuantityOnHand ?? 0m) <= 170m
                    ? "LOW_STOCK"
                    : "FEFO_PRIORITY";

                selectedAlerts.Add((batch.Id, MapBatchAlert(batch, medicinesById, fallbackStatus)));
                includedBatchIds.Add(batch.Id);
            }
        }

        return selectedAlerts
            .Take(6)
            .Select(x => x.Alert)
            .ToList();
    }

    private static FefoBatchAlertItemDto MapBatchAlert(
        MedicineBatch batch,
        IReadOnlyDictionary<long, Medicine> medicinesById,
        string status)
    {
        var medicine = batch.MedicineId.HasValue && medicinesById.TryGetValue(batch.MedicineId.Value, out var med)
            ? med
            : null;

        var medicineName = FormatMedicineDisplayName(medicine);
        var batchCode = !string.IsNullOrWhiteSpace(batch.BatchNumber)
            ? batch.BatchNumber
            : $"LOT-{batch.Id:D3}";
        var expiryDate = batch.ExpiryDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
            ?? "Chưa cập nhật";
        var unitLabel = (medicine?.DispensingUnit ?? "đơn vị").ToLowerInvariant();
        var availableStock = $"{FormatQuantity(batch.QuantityOnHand ?? 0m)} {unitLabel}";

        return new FefoBatchAlertItemDto(
            Id: $"batch-{batch.Id}",
            MedicineName: medicineName,
            BatchCode: batchCode,
            ExpiryDate: expiryDate,
            AvailableStock: availableStock,
            Status: status);
    }
}
