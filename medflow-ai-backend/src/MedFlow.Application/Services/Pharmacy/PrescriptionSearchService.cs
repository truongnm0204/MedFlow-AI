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

public sealed class PrescriptionSearchService : IPrescriptionSearchService
{
    private const string PharmacyStaffRoleCode = "PHARMACY_STAFF";
    private const string AuditEntityType = "PRESCRIPTION";
    private const string AuditSearchAction = "PRESCRIPTION_SEARCH";
    private const string AuditViewAction = "PRESCRIPTION_VIEW";
    private const string AuditSearchReason = "Tra cứu danh sách đơn thuốc nhà thuốc (UC44)";
    private const string AuditViewReason = "Xem chi tiết đơn thuốc nhà thuốc (UC44)";

    private const string MedicineDispensePermissionCode = "MEDICINE_DISPENSE";

    private static readonly HashSet<string> ValidStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "PENDING",
        "PREPARING",
        "READY",
        "DISPENSED",
    };

    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;

    public PrescriptionSearchService(
        IApplicationDbContext dbContext,
        TimeProvider? timeProvider = null)
    {
        _db = dbContext;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<PrescriptionSearchResponseDto> SearchAsync(
        long actorUserId,
        GetPrescriptionSearchRequest request,
        CancellationToken ct = default,
        string? ipAddress = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Query != null && request.Query.Trim().Length > 100)
        {
            throw new ValidationException("Từ khóa tìm kiếm không được vượt quá 100 ký tự.");
        }

        if (request.Page < 1)
        {
            throw new ValidationException("Trang phải lớn hơn hoặc bằng 1.");
        }

        if (request.PageSize is < 1 or > 100)
        {
            throw new ValidationException("Số lượng mục trên mỗi trang phải từ 1 đến 100.");
        }

        if (request.Page > int.MaxValue / request.PageSize)
        {
            throw new ValidationException("Trang vượt quá phạm vi hỗ trợ.");
        }

        string? normalizedStatus = null;
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var trimmedStatus = request.Status.Trim();
            if (!string.Equals(trimmedStatus, "ALL", StringComparison.OrdinalIgnoreCase))
            {
                if (!ValidStatuses.Contains(trimmedStatus))
                {
                    throw new ValidationException("Trạng thái đơn thuốc lọc không hợp lệ.");
                }

                normalizedStatus = trimmedStatus.ToUpperInvariant();
            }
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var hospitalId = await ValidatePharmacyStaffAccessAsync(actorUserId, nowUtc, ct);

        var scopedPrescriptionIds = BuildScopedPrescriptionIds(hospitalId);
        var baseScopedQuery =
            from prescription in _db.Prescriptions.AsNoTracking()
            join visit in _db.Visits on prescription.VisitId equals visit.Id
            join patient in _db.Patients on visit.PatientId equals (long?)patient.Id
            where scopedPrescriptionIds.Contains(prescription.Id)
            select new
            {
                Prescription = prescription,
                Visit = visit,
                Patient = patient,
            };

        if (!string.IsNullOrWhiteSpace(request.Query))
        {
            var q = request.Query.Trim().ToUpper();
            baseScopedQuery = baseScopedQuery.Where(x =>
                (x.Prescription.PrescriptionCode != null && x.Prescription.PrescriptionCode.ToUpper().Contains(q)) ||
                (x.Visit.VisitCode != null && x.Visit.VisitCode.ToUpper().Contains(q)) ||
                (x.Patient != null && x.Patient.PatientCode != null && x.Patient.PatientCode.ToUpper().Contains(q)) ||
                (x.Patient != null && x.Patient.FullName != null && x.Patient.FullName.ToUpper().Contains(q)));
        }

        var dispenseQuery = baseScopedQuery.Select(x => new
        {
            x.Prescription,
            x.Visit,
            x.Patient,
            LatestDispenseStatus = _db.MedicationDispenses
                .Where(md => md.PrescriptionId == x.Prescription.Id)
                .OrderByDescending(md => md.DispensedAt ?? md.PreparedAt)
                .ThenByDescending(md => md.Id)
                .Select(md => md.Status)
                .FirstOrDefault(),
            ItemCount = _db.PrescriptionItems.Count(pi => pi.PrescriptionId == x.Prescription.Id),
        });

        var candidateQuery = dispenseQuery.Select(x => new
        {
            x.Prescription,
            x.Visit,
            x.Patient,
            x.LatestDispenseStatus,
            x.ItemCount,
            IsDispensed =
                x.Prescription.Status != null && x.Prescription.Status.ToUpper() == "DISPENSED" ||
                x.LatestDispenseStatus != null &&
                (x.LatestDispenseStatus.ToUpper() == "COMPLETED" || x.LatestDispenseStatus.ToUpper() == "DISPENSED"),
            IsReady =
                x.Prescription.Status != null && x.Prescription.Status.ToUpper() == "READY" ||
                x.LatestDispenseStatus != null &&
                (x.LatestDispenseStatus.ToUpper() == "READY" || x.LatestDispenseStatus.ToUpper() == "PREPARED"),
            IsPreparing =
                x.Prescription.Status != null && x.Prescription.Status.ToUpper() == "PREPARING" ||
                x.LatestDispenseStatus != null &&
                (x.LatestDispenseStatus.ToUpper() == "PREPARING" || x.LatestDispenseStatus.ToUpper() == "IN_PROGRESS"),
        });

        candidateQuery = normalizedStatus switch
        {
            "DISPENSED" => candidateQuery.Where(x => x.IsDispensed),
            "READY" => candidateQuery.Where(x => !x.IsDispensed && x.IsReady),
            "PREPARING" => candidateQuery.Where(x => !x.IsDispensed && !x.IsReady && x.IsPreparing),
            "PENDING" => candidateQuery.Where(x => !x.IsDispensed && !x.IsReady && !x.IsPreparing),
            _ => candidateQuery,
        };

        candidateQuery = candidateQuery
            .OrderByDescending(x => x.Prescription.IssuedAt ?? x.Prescription.CreatedAt)
            .ThenByDescending(x => x.Prescription.Id);

        var totalCount = await candidateQuery.CountAsync(ct);

        var pagedItems = await candidateQuery
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(ct);

        var doctorIds = pagedItems
            .Select(x => x.Prescription.PrescribingDoctorId ?? x.Visit.AssignedDoctorId)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        var doctorsById = doctorIds.Count > 0
            ? (await _db.Doctors
                .AsNoTracking()
                .Include(d => d.Staff)
                .Where(d => doctorIds.Contains(d.Id) && d.HospitalId == hospitalId)
                .ToListAsync(ct))
                .ToDictionary(d => d.Id)
            : new Dictionary<long, Doctor>();

        var items = pagedItems.Select(x =>
        {
            var doctorId = x.Prescription.PrescribingDoctorId ?? x.Visit.AssignedDoctorId;
            var doctor = doctorId.HasValue && doctorsById.TryGetValue(doctorId.Value, out var found)
                ? found
                : null;

            return MapPrescriptionItem(
                x.Prescription,
                x.Visit,
                x.Patient,
                doctor,
                ResolveEffectiveStatus(x.Prescription.Status, x.LatestDispenseStatus),
                x.ItemCount);
        }).ToList();

        var response = new PrescriptionSearchResponseDto(
            Items: items,
            TotalCount: totalCount,
            Page: request.Page,
            PageSize: request.PageSize);

        var changeData = JsonSerializer.Serialize(new
        {
            page = request.Page,
            pageSize = request.PageSize,
            status = normalizedStatus,
            totalCount,
        });

        _db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actorUserId,
            HospitalId = hospitalId,
            Action = AuditSearchAction,
            EntityType = AuditEntityType,
            EntityId = null,
            Reason = AuditSearchReason,
            ChangeData = changeData,
            IpAddress = IPAddress.TryParse(ipAddress, out var searchIp) ? searchIp : null,
            OccurredAt = nowUtc,
        });
        await _db.SaveChangesAsync(ct);

        return response;
    }

    public async Task<PrescriptionSearchDetailDto> GetByIdAsync(
        long actorUserId,
        string idOrCode,
        CancellationToken ct = default,
        string? ipAddress = null)
    {
        if (string.IsNullOrWhiteSpace(idOrCode))
        {
            throw new ValidationException("Mã hoặc định danh đơn thuốc không được để trống.");
        }

        var trimmedIdOrCode = idOrCode.Trim();
        if (trimmedIdOrCode.Length > 100)
        {
            throw new ValidationException("Mã hoặc định danh đơn thuốc không được vượt quá 100 ký tự.");
        }

        var isNumeric = long.TryParse(trimmedIdOrCode, out var parsedId);
        if (isNumeric && parsedId <= 0)
        {
            throw new ValidationException("Định danh đơn thuốc không hợp lệ.");
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var hospitalId = await ValidatePharmacyStaffAccessAsync(actorUserId, nowUtc, ct);

        var upperIdOrCode = trimmedIdOrCode.ToUpper();

        var scopedPrescriptionIds = BuildScopedPrescriptionIds(hospitalId);
        var baseScopedQuery =
            from prescription in _db.Prescriptions.AsNoTracking()
            join visit in _db.Visits on prescription.VisitId equals visit.Id
            join patient in _db.Patients on visit.PatientId equals (long?)patient.Id
            where scopedPrescriptionIds.Contains(prescription.Id) &&
                  (isNumeric
                      ? prescription.Id == parsedId ||
                        prescription.PrescriptionCode != null &&
                        prescription.PrescriptionCode.ToUpper() == upperIdOrCode
                      : prescription.PrescriptionCode != null &&
                        prescription.PrescriptionCode.ToUpper() == upperIdOrCode)
            select new
            {
                Prescription = prescription,
                Visit = visit,
                Patient = patient,
            };

        var record = await baseScopedQuery.OrderByDescending(x => x.Prescription.Id).FirstOrDefaultAsync(ct);
        if (record is null)
        {
            throw new NotFoundException("Prescription", idOrCode);
        }

        var pEntity = record.Prescription;
        var vEntity = record.Visit;
        var ptEntity = record.Patient;

        var latestDispense = await _db.MedicationDispenses
            .AsNoTracking()
            .Where(md => md.PrescriptionId == pEntity.Id)
            .OrderByDescending(md => md.DispensedAt ?? md.PreparedAt)
            .ThenByDescending(md => md.Id)
            .FirstOrDefaultAsync(ct);

        var effectiveStatus = ResolveEffectiveStatus(pEntity.Status, latestDispense?.Status);

        var docId = pEntity.PrescribingDoctorId ?? vEntity.AssignedDoctorId;
        Doctor? doctor = null;
        if (docId.HasValue)
        {
            doctor = await _db.Doctors
                .AsNoTracking()
                .Include(d => d.Staff)
                .FirstOrDefaultAsync(d => d.Id == docId.Value && d.HospitalId == hospitalId, ct);
        }

        var medicineDtos = await (
            from item in _db.PrescriptionItems.AsNoTracking()
            join medicine in _db.Medicines.AsNoTracking()
                on item.MedicineId equals (long?)medicine.Id
            where item.PrescriptionId == pEntity.Id &&
                  medicine.HospitalId == hospitalId
            orderby item.Id
            select new PrescriptionSearchMedicineDto(
                item.Id,
                medicine.MedicineCode,
                medicine.Name,
                item.PrescribedQuantity,
                medicine.DispensingUnit,
                item.SingleDoseQuantity,
                item.SingleDoseUnit,
                item.DoseScheduleText,
                item.Frequency,
                item.DurationDays,
                item.AdministrationRoute,
                item.Instructions))
            .ToListAsync(ct);

        var itemDto = MapPrescriptionItem(
            pEntity,
            vEntity,
            ptEntity,
            doctor,
            effectiveStatus,
            medicineDtos.Count);
        var detailDto = new PrescriptionSearchDetailDto(itemDto, medicineDtos);

        var changeData = JsonSerializer.Serialize(new
        {
            prescriptionId = pEntity.Id,
            visitId = pEntity.VisitId,
            itemCount = medicineDtos.Count,
        });

        _db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actorUserId,
            HospitalId = hospitalId,
            Action = AuditViewAction,
            EntityType = AuditEntityType,
            EntityId = pEntity.Id.ToString(CultureInfo.InvariantCulture),
            Reason = AuditViewReason,
            ChangeData = changeData,
            IpAddress = IPAddress.TryParse(ipAddress, out var viewIp) ? viewIp : null,
            OccurredAt = nowUtc,
        });
        await _db.SaveChangesAsync(ct);

        return detailDto;
    }

    private async Task<long> ValidatePharmacyStaffAccessAsync(
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
                throw new ForbiddenException("Người dùng không có quyền thực hiện tra cứu đơn thuốc.");
            }
        }

        return hospitalId;
    }

    private IQueryable<long> BuildScopedPrescriptionIds(long hospitalId) =>
        from prescription in _db.Prescriptions.AsNoTracking()
        join visit in _db.Visits on prescription.VisitId equals visit.Id
        join room in _db.HospitalRooms on visit.ExaminationRoomId equals (long?)room.Id into rooms
        from room in rooms.DefaultIfEmpty()
        join assignedDoctor in _db.Doctors on visit.AssignedDoctorId equals (long?)assignedDoctor.Id into doctors
        from assignedDoctor in doctors.DefaultIfEmpty()
        join patient in _db.Patients on visit.PatientId equals (long?)patient.Id
        where prescription.FulfillmentChoice != null &&
              (prescription.FulfillmentChoice!.ToUpper() == "HOSPITAL" ||
               prescription.FulfillmentChoice!.ToUpper() == "HOSPITAL_PHARMACY")
        where prescription.Status != null &&
              (prescription.Status!.ToUpper() == "ISSUED" ||
               prescription.Status!.ToUpper() == "PREPARING" ||
               prescription.Status!.ToUpper() == "READY" ||
               prescription.Status!.ToUpper() == "DISPENSED")
        where visit.Status != null && visit.Status!.ToUpper() == "COMPLETED"
        where (room != null && room.HospitalId == hospitalId ||
               assignedDoctor != null && assignedDoctor.HospitalId == hospitalId) &&
              (visit.ExaminationRoomId == null || room != null && room.HospitalId == hospitalId) &&
              (visit.AssignedDoctorId == null || assignedDoctor != null && assignedDoctor.HospitalId == hospitalId)
        where !_db.Prescriptions.Any(replacement =>
            replacement.ReplacesPrescriptionId == prescription.Id &&
            replacement.Status != null &&
            (replacement.Status!.ToUpper() == "ISSUED" ||
             replacement.Status!.ToUpper() == "PREPARING" ||
             replacement.Status!.ToUpper() == "READY" ||
             replacement.Status!.ToUpper() == "DISPENSED"))
        where !prescription.PrescribingDoctorId.HasValue ||
              _db.Doctors.Any(prescribingDoctor =>
                  prescribingDoctor.Id == prescription.PrescribingDoctorId.GetValueOrDefault() &&
                  prescribingDoctor.HospitalId == hospitalId)
        where _db.PrescriptionItems.Any(item => item.PrescriptionId == prescription.Id)
        where !_db.PrescriptionItems.Any(item =>
            item.PrescriptionId == prescription.Id &&
            (!item.MedicineId.HasValue ||
             !_db.Medicines.Any(medicine =>
                 medicine.Id == item.MedicineId.Value &&
                 medicine.HospitalId == hospitalId)))
        select prescription.Id;

    private static PrescriptionSearchItemDto MapPrescriptionItem(
        Prescription prescription,
        Visit visit,
        Patient patient,
        Doctor? doctor,
        string status,
        int itemCount) =>
        new(
            prescription.Id,
            prescription.PrescriptionCode ?? string.Empty,
            visit.VisitCode ?? string.Empty,
            patient.PatientCode,
            patient.FullName,
            patient.Gender,
            patient.DateOfBirth,
            FormatDoctorDisplayName(doctor),
            status,
            prescription.IssuedAt ?? prescription.CreatedAt,
            itemCount);

    private static string ResolveEffectiveStatus(string? rxStatus, string? dispStatus)
    {
        var normalizedRx = rxStatus?.Trim().ToUpperInvariant();
        var normalizedDisp = dispStatus?.Trim().ToUpperInvariant();

        if (normalizedRx == "DISPENSED" || normalizedDisp == "COMPLETED" || normalizedDisp == "DISPENSED")
        {
            return "DISPENSED";
        }

        if (normalizedRx == "READY" || normalizedDisp == "READY" || normalizedDisp == "PREPARED")
        {
            return "READY";
        }

        if (normalizedRx == "PREPARING" || normalizedDisp == "PREPARING" || normalizedDisp == "IN_PROGRESS")
        {
            return "PREPARING";
        }

        return "PENDING";
    }

    private static string? FormatDoctorDisplayName(Doctor? doctor)
    {
        if (doctor is null)
        {
            return null;
        }

        var fullName = doctor.Staff?.FullName?.Trim();
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

        return !string.IsNullOrWhiteSpace(doctor.DoctorCode) ? doctor.DoctorCode : null;
    }
}
