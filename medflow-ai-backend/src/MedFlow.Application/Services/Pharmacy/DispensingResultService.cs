using System.Globalization;
using System.Net;
using MedFlow.Application.DTOs.Pharmacy;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Patients;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Pharmacy;

public sealed class DispensingResultService : IDispensingResultService
{
    private const string PharmacyStaffRoleCode = "PHARMACY_STAFF";
    private const string MedicineDispensePermissionCode = "MEDICINE_DISPENSE";
    private const string AuditAction = "MEDICATION_DISPENSE_VIEW";
    private const string AuditEntityType = "MEDICATION_DISPENSE";
    private const string AuditReason = "Xem biên nhận kết quả cấp phát thuốc (UC46 / SCR-PHA-04)";

    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;

    public DispensingResultService(
        IApplicationDbContext dbContext,
        TimeProvider? timeProvider = null)
    {
        _db = dbContext;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<DispensingResultDetailDto> GetDispensingResultAsync(
        long actorUserId,
        string idOrCode,
        CancellationToken ct = default,
        string? ipAddress = null)
    {
        if (string.IsNullOrWhiteSpace(idOrCode))
        {
            throw new ValidationException("Mã hoặc định danh cấp phát thuốc không được để trống.");
        }

        var trimmedIdOrCode = idOrCode.Trim();
        if (trimmedIdOrCode.Length > 100)
        {
            throw new ValidationException("Mã hoặc định danh cấp phát thuốc không được vượt quá 100 ký tự.");
        }

        var isNumeric = long.TryParse(trimmedIdOrCode, out var parsedId);
        if (isNumeric && parsedId <= 0)
        {
            throw new ValidationException("Định danh cấp phát thuốc không hợp lệ.");
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var hospitalId = await ValidatePharmacyStaffAccessAsync(actorUserId, nowUtc, ct);

        var upperIdOrCode = trimmedIdOrCode.ToUpperInvariant();

        // 1. Locate MedicationDispense candidate
        MedicationDispense? dispense = null;

        if (isNumeric)
        {
            dispense = await _db.MedicationDispenses
                .AsNoTracking()
                .FirstOrDefaultAsync(md => md.Id == parsedId, ct);

            if (dispense is null)
            {
                dispense = await _db.MedicationDispenses
                    .AsNoTracking()
                    .Where(md => md.PrescriptionId == parsedId)
                    .OrderByDescending(md => md.DispensedAt ?? md.PreparedAt)
                    .ThenByDescending(md => md.Id)
                    .FirstOrDefaultAsync(ct);
            }
        }

        if (dispense is null && upperIdOrCode.StartsWith("DISP-", StringComparison.Ordinal))
        {
            var lastDash = upperIdOrCode.LastIndexOf('-');
            if (lastDash >= 0 &&
                long.TryParse(upperIdOrCode[(lastDash + 1)..], out var extractedId) &&
                extractedId > 0)
            {
                var candidate = await _db.MedicationDispenses
                    .AsNoTracking()
                    .FirstOrDefaultAsync(md => md.Id == extractedId, ct);

                if (candidate is not null &&
                    string.Equals(FormatDispenseCode(candidate, nowUtc), upperIdOrCode, StringComparison.Ordinal))
                {
                    dispense = candidate;
                }
            }
        }

        if (dispense is null)
        {
            // Try resolving by PrescriptionCode
            var prescriptionByCode = await _db.Prescriptions
                .AsNoTracking()
                .Where(p => p.PrescriptionCode != null && p.PrescriptionCode.ToUpper() == upperIdOrCode)
                .OrderByDescending(p => p.Id)
                .FirstOrDefaultAsync(ct);

            if (prescriptionByCode is not null)
            {
                dispense = await _db.MedicationDispenses
                    .AsNoTracking()
                    .Where(md => md.PrescriptionId == prescriptionByCode.Id)
                    .OrderByDescending(md => md.DispensedAt ?? md.PreparedAt)
                    .ThenByDescending(md => md.Id)
                    .FirstOrDefaultAsync(ct);
            }
        }

        if (dispense is null)
        {
            throw new NotFoundException("MedicationDispense", idOrCode);
        }

        if (!string.Equals(dispense.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(dispense.Status, "DISPENSED", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotFoundException("MedicationDispense", idOrCode);
        }

        // 2. Validate Prescription & Hospital Isolation
        if (!dispense.PrescriptionId.HasValue)
        {
            throw new NotFoundException("MedicationDispense", idOrCode);
        }

        var prescription = await _db.Prescriptions
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == dispense.PrescriptionId.Value, ct);

        if (prescription is null || !prescription.VisitId.HasValue)
        {
            throw new NotFoundException("MedicationDispense", idOrCode);
        }

        var visit = await _db.Visits
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == prescription.VisitId.Value, ct);

        if (visit is null)
        {
            throw new NotFoundException("MedicationDispense", idOrCode);
        }

        // Hospital boundary check
        var isHospitalMatch = await IsVisitInHospitalScopeAsync(visit, prescription, hospitalId, ct);
        if (!isHospitalMatch)
        {
            throw new NotFoundException("MedicationDispense", idOrCode);
        }

        // 3. Load all supporting data
        var patient = visit.PatientId.HasValue
            ? await _db.Patients.AsNoTracking().FirstOrDefaultAsync(p => p.Id == visit.PatientId.Value, ct)
            : null;
        if (patient is null)
        {
            throw new NotFoundException("MedicationDispense", idOrCode);
        }

        Doctor? doctor = null;
        var doctorId = prescription.PrescribingDoctorId ?? visit.AssignedDoctorId;
        if (doctorId.HasValue)
        {
            doctor = await _db.Doctors
                .AsNoTracking()
                .Include(d => d.Staff)
                .FirstOrDefaultAsync(d => d.Id == doctorId.Value, ct);
        }

        HospitalRoom? room = null;
        if (visit.ExaminationRoomId.HasValue)
        {
            room = await _db.HospitalRooms
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == visit.ExaminationRoomId.Value, ct);
        }

        Staff? dispensingStaff = null;
        if (dispense.DispensedByStaffId.HasValue)
        {
            dispensingStaff = await _db.Staff
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == dispense.DispensedByStaffId.Value, ct);
        }

        if (dispensingStaff?.HospitalId != hospitalId)
        {
            throw new NotFoundException("MedicationDispense", idOrCode);
        }

        var dispenseItems = await _db.DispenseItems
            .AsNoTracking()
            .Where(di => di.DispenseId == dispense.Id)
            .OrderBy(di => di.Id)
            .ToListAsync(ct);

        var prescriptionItems = await _db.PrescriptionItems
            .AsNoTracking()
            .Where(pi => pi.PrescriptionId == prescription.Id)
            .ToListAsync(ct);

        var prescriptionItemsById = prescriptionItems.ToDictionary(pi => pi.Id);

        var batchIds = dispenseItems
            .Where(di => di.MedicineBatchId.HasValue)
            .Select(di => di.MedicineBatchId!.Value)
            .Distinct()
            .ToList();

        var batchesById = batchIds.Count > 0
            ? (await _db.MedicineBatches
                .AsNoTracking()
                .Where(b => batchIds.Contains(b.Id))
                .ToListAsync(ct))
                .ToDictionary(b => b.Id)
            : new Dictionary<long, MedicineBatch>();

        var medicineIds = prescriptionItems
            .Where(pi => pi.MedicineId.HasValue)
            .Select(pi => pi.MedicineId!.Value)
            .Union(batchesById.Values.Where(b => b.MedicineId.HasValue).Select(b => b.MedicineId!.Value))
            .Distinct()
            .ToList();

        var medicinesById = medicineIds.Count > 0
            ? (await _db.Medicines
                .AsNoTracking()
                .Where(m => medicineIds.Contains(m.Id))
                .ToListAsync(ct))
                .ToDictionary(m => m.Id)
            : new Dictionary<long, Medicine>();

        var hasInvalidItemGraph = dispenseItems.Count == 0 || dispenseItems.Any(di =>
            !di.PrescriptionItemId.HasValue ||
            !prescriptionItemsById.TryGetValue(di.PrescriptionItemId.Value, out var prescriptionItem) ||
            !prescriptionItem.MedicineId.HasValue ||
            !medicinesById.TryGetValue(prescriptionItem.MedicineId.Value, out var medicine) ||
            medicine.HospitalId != hospitalId ||
            !di.MedicineBatchId.HasValue ||
            !batchesById.TryGetValue(di.MedicineBatchId.Value, out var batch) ||
            batch.MedicineId != medicine.Id ||
            (di.DispensedQuantity ?? di.PlannedQuantity ?? 0m) <= 0m);

        if (hasInvalidItemGraph)
        {
            throw new NotFoundException("MedicationDispense", idOrCode);
        }

        var payment = await _db.Payments
            .AsNoTracking()
            .Where(pm => pm.VisitId == visit.Id)
            .OrderByDescending(pm => pm.PaidAt ?? pm.CreatedAt)
            .ThenByDescending(pm => pm.Id)
            .FirstOrDefaultAsync(ct);

        var queueNumber = await _db.QueueEntries
            .AsNoTracking()
            .Where(qe => qe.VisitId == visit.Id && qe.QueueNumber.HasValue)
            .OrderByDescending(qe => qe.CompletedAt ?? qe.StartedAt ?? qe.CalledAt ?? qe.ArrivedAt)
            .ThenByDescending(qe => qe.Id)
            .Select(qe => qe.QueueNumber)
            .FirstOrDefaultAsync(ct);

        // 4. Construct response DTO
        var dispenseCode = FormatDispenseCode(dispense, nowUtc);
        var rxCode = prescription.PrescriptionCode ?? $"RX-{prescription.Id:D4}";
        var visitCode = visit.VisitCode ?? $"VIS-{visit.Id:D4}";
        var ticketNo = queueNumber.HasValue ? $"P{queueNumber.Value:D3}" : "Chưa cập nhật";
        var patientName = patient.FullName;
        var patientCode = patient.PatientCode;
        var receiverName = $"{patientName} (Chính chủ người bệnh)";

        var receiverIdentityMasked = !string.IsNullOrWhiteSpace(patient.IdentificationHash)
            ? PatientProfileService.MaskedVerifiedIdentityText
            : "Chưa cập nhật CCCD";

        var prescribingDoctorFormatted = FormatDoctorDisplayName(doctor, room);
        var dispensingPharmacistFormatted = !string.IsNullOrWhiteSpace(dispensingStaff.FullName)
            ? (dispensingStaff.FullName.StartsWith("DS", StringComparison.OrdinalIgnoreCase)
                ? dispensingStaff.FullName
                : $"DS. {dispensingStaff.FullName}")
            : "Chưa cập nhật";

        var dispensedAtFormatted = (dispense.DispensedAt ?? dispense.PreparedAt)?
            .ToString("dd/MM/yyyy • HH:mm:ss", CultureInfo.InvariantCulture)
            ?? nowUtc.ToString("dd/MM/yyyy • HH:mm:ss", CultureInfo.InvariantCulture);

        var statusLabel = (dispense.Status?.ToUpperInvariant() == "COMPLETED" ||
                           dispense.Status?.ToUpperInvariant() == "DISPENSED")
            ? "DISPENSED & CLOSED"
            : (dispense.Status ?? "DISPENSED & CLOSED");

        var hasPaid = payment is not null && string.Equals(payment.Status, "PAID", StringComparison.OrdinalIgnoreCase);
        decimal? totalAmount = null;
        const string totalAmountFormatted = "Chưa có dữ liệu";

        var paymentSummary = hasPaid
            ? $"Đã thanh toán viện phí {FormatVnd(payment!.Amount ?? 0m)} qua {(payment.ExternalReference != null ? "VietQR" : "hệ thống viện phí")} • Hóa đơn {payment.PaymentCode ?? payment.Id.ToString(CultureInfo.InvariantCulture)}"
            : "Chưa ghi nhận thanh toán viện phí";

        var mappedItems = new List<DispensedLineItemDto>(dispenseItems.Count);
        for (var i = 0; i < dispenseItems.Count; i++)
        {
            var di = dispenseItems[i];
            prescriptionItemsById.TryGetValue(di.PrescriptionItemId ?? 0, out var pi);

            Medicine? medicine = null;
            if (pi?.MedicineId.HasValue == true && medicinesById.TryGetValue(pi.MedicineId.Value, out var m))
            {
                medicine = m;
            }

            batchesById.TryGetValue(di.MedicineBatchId ?? 0, out var batch);
            if (medicine is null && batch?.MedicineId.HasValue == true && medicinesById.TryGetValue(batch.MedicineId.Value, out var mBatch))
            {
                medicine = mBatch;
            }

            var drugName = !string.IsNullOrWhiteSpace(medicine?.Name)
                ? medicine.Name
                : medicine?.MedicineCode ?? "Chưa cập nhật";

            var dosageForm = !string.IsNullOrWhiteSpace(medicine?.DosageForm)
                ? medicine.DosageForm
                : "Chưa cập nhật";

            var qty = di.DispensedQuantity ?? di.PlannedQuantity ?? pi?.PrescribedQuantity ?? 0m;
            var unit = (medicine?.DispensingUnit ?? pi?.SingleDoseUnit ?? "Chưa cập nhật").ToLowerInvariant();

            var batchCode = !string.IsNullOrWhiteSpace(batch?.BatchNumber)
                ? batch.BatchNumber
                : "Chưa cập nhật";

            var expiryDateFormatted = batch?.ExpiryDate.HasValue == true
                ? batch.ExpiryDate.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
                : "Chưa cập nhật";

            mappedItems.Add(new DispensedLineItemDto(
                Id: $"di-{di.Id}",
                OrderIndex: i + 1,
                DrugName: drugName,
                DosageForm: dosageForm,
                QtyDispensed: qty,
                Unit: unit,
                BatchCode: batchCode,
                ExpiryDate: expiryDateFormatted,
                ShelfLocation: "Chưa cập nhật",
                UnitPriceFormatted: "Chưa có dữ liệu",
                LineTotalFormatted: "Chưa có dữ liệu"));
        }

        var distinctBatchesCount = dispenseItems
            .Where(di => di.MedicineBatchId.HasValue)
            .Select(di => di.MedicineBatchId!.Value)
            .Distinct()
            .Count();

        var auditNote = $"Hệ thống HIS đã ghi nhận {mappedItems.Count} dòng thuốc từ {distinctBatchesCount} lô FEFO và khóa đơn thuốc không cho cấp phát trùng lặp.";

        // 5. Write audit log
        var audit = new AuditLog
        {
            ActorUserId = actorUserId,
            HospitalId = hospitalId,
            Action = AuditAction,
            EntityType = AuditEntityType,
            EntityId = dispense.Id.ToString(CultureInfo.InvariantCulture),
            Reason = AuditReason,
            IpAddress = ParseIpAddress(ipAddress),
            OccurredAt = nowUtc,
        };

        _db.AuditLogs.Add(audit);
        await _db.SaveChangesAsync(ct);

        return new DispensingResultDetailDto(
            DispenseCode: dispenseCode,
            RxCode: rxCode,
            VisitCode: visitCode,
            TicketNo: ticketNo,
            PatientName: patientName,
            PatientCode: patientCode,
            ReceiverName: receiverName,
            ReceiverIdentityMasked: receiverIdentityMasked,
            PrescribingDoctor: prescribingDoctorFormatted,
            DispensingPharmacist: dispensingPharmacistFormatted,
            DispensedAt: dispensedAtFormatted,
            StatusLabel: statusLabel,
            PaymentSummary: paymentSummary,
            TotalAmountFormatted: totalAmountFormatted,
            Items: mappedItems,
            AuditNote: auditNote,
            DispenseId: dispense.Id,
            PrescriptionId: prescription.Id,
            TotalAmount: totalAmount);
    }

    private async Task<long> ValidatePharmacyStaffAccessAsync(
        long actorUserId,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var user = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == actorUserId, ct);

        if (user is null || user.AccountStatus != MedFlow.Domain.Enums.AccountStatus.ACTIVE)
        {
            throw new UnauthorizedAccessException("Người dùng không hợp lệ hoặc đã bị vô hiệu hóa.");
        }

        if (user.LockedUntil.HasValue && user.LockedUntil.Value > nowUtc)
        {
            throw new UnauthorizedAccessException("Tài khoản người dùng hiện đang bị tạm khóa.");
        }

        var actorRoles = await (
            from ur in _db.UserRoles.AsNoTracking()
            join r in _db.Roles.AsNoTracking() on ur.RoleId equals r.Id
            where ur.UserId == actorUserId
            select new { r.Id, r.Code }).ToListAsync(ct);

        var hasPharmacyStaffRole = actorRoles.Any(r =>
            string.Equals(r.Code, PharmacyStaffRoleCode, StringComparison.OrdinalIgnoreCase));

        if (!hasPharmacyStaffRole)
        {
            throw new ForbiddenException("Người dùng không có vai trò nhân viên nhà thuốc.");
        }

        var actorRoleIds = actorRoles.Select(r => r.Id).ToList();

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

        var rolePermissionsConfigured = await _db.RolePermissions
            .AsNoTracking()
            .AnyAsync(ct);

        if (rolePermissionsConfigured)
        {
            var hasMedicineDispensePermission = await (
                from rp in _db.RolePermissions.AsNoTracking()
                join permission in _db.Permissions.AsNoTracking()
                    on rp.PermissionId equals permission.Id
                where actorRoleIds.Contains(rp.RoleId) &&
                      permission.Code.ToUpper() == MedicineDispensePermissionCode
                select rp).AnyAsync(ct);

            if (!hasMedicineDispensePermission)
            {
                throw new ForbiddenException("Người dùng không có quyền xem biên nhận kết quả cấp phát thuốc.");
            }
        }

        return hospitalId;
    }

    private async Task<bool> IsVisitInHospitalScopeAsync(
        Visit visit,
        Prescription prescription,
        long hospitalId,
        CancellationToken ct)
    {
        var hasHospitalProof = false;

        if (visit.ExaminationRoomId.HasValue)
        {
            var room = await _db.HospitalRooms
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == visit.ExaminationRoomId.Value, ct);

            if (room?.HospitalId != hospitalId)
            {
                return false;
            }

            hasHospitalProof = true;
        }

        if (visit.AssignedDoctorId.HasValue)
        {
            var doctor = await _db.Doctors
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == visit.AssignedDoctorId.Value, ct);

            if (doctor?.HospitalId != hospitalId)
            {
                return false;
            }

            hasHospitalProof = true;
        }

        if (prescription.PrescribingDoctorId.HasValue)
        {
            var doctor = await _db.Doctors
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == prescription.PrescribingDoctorId.Value, ct);

            if (doctor?.HospitalId != hospitalId)
            {
                return false;
            }

            hasHospitalProof = true;
        }

        return hasHospitalProof;
    }

    private static string FormatDoctorDisplayName(Doctor? doctor, HospitalRoom? room)
    {
        if (doctor is null)
        {
            return "Chưa cập nhật";
        }

        var fullName = doctor.Staff?.FullName?.Trim();
        var title = doctor.ProfessionalTitle?.Trim();

        string doctorName;
        if (!string.IsNullOrWhiteSpace(fullName) && !string.IsNullOrWhiteSpace(title))
        {
            doctorName = fullName.StartsWith(title, StringComparison.OrdinalIgnoreCase)
                ? fullName
                : $"{title} {fullName}";
        }
        else if (!string.IsNullOrWhiteSpace(fullName))
        {
            doctorName = fullName.StartsWith("BS", StringComparison.OrdinalIgnoreCase)
                ? fullName
                : $"BS. {fullName}";
        }
        else
        {
            doctorName = !string.IsNullOrWhiteSpace(doctor.DoctorCode) ? doctor.DoctorCode : "Chưa cập nhật";
        }

        var roomLabel = !string.IsNullOrWhiteSpace(room?.Name) ? room.Name : room?.RoomCode;
        if (!string.IsNullOrWhiteSpace(roomLabel))
        {
            return $"{doctorName} ({roomLabel})";
        }

        return doctorName;
    }

    private static string FormatDispenseCode(MedicationDispense dispense, DateTime fallbackUtc)
    {
        var dispenseDate = dispense.DispensedAt ?? dispense.PreparedAt ?? fallbackUtc;
        return $"DISP-{dispenseDate:yyyy-MMdd}-{dispense.Id:D4}";
    }

    private static string FormatVnd(decimal amount)
    {
        var viCulture = CultureInfo.GetCultureInfo("vi-VN");
        return $"{amount.ToString("#,##0", viCulture)} đ";
    }

    private static IPAddress? ParseIpAddress(string? rawIp)
    {
        if (string.IsNullOrWhiteSpace(rawIp))
        {
            return null;
        }

        var candidate = rawIp.Split(',')[0].Trim();
        return IPAddress.TryParse(candidate, out var parsed) ? parsed : null;
    }
}
