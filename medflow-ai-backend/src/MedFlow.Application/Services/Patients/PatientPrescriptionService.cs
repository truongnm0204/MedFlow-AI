using System.Globalization;
using MedFlow.Application.DTOs.Patients;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Patients;

public sealed class PatientPrescriptionService : IPatientPrescriptionService
{
    private const int MaxIdentifierLength = 40;
    private const int MaxPageSize = 50;
    private const string NotFoundMessage = "Không tìm thấy đơn thuốc.";

    private static readonly string[] VisibleStatuses = ["ISSUED", "PREPARING", "READY", "DISPENSED"];

    private readonly IApplicationDbContext _db;
    private readonly IAuditLogger _auditLogger;
    private readonly TimeProvider _timeProvider;

    public PatientPrescriptionService(
        IApplicationDbContext db,
        IAuditLogger auditLogger,
        TimeProvider timeProvider)
    {
        _db = db;
        _auditLogger = auditLogger;
        _timeProvider = timeProvider;
    }

    public async Task<PatientPrescriptionListResponseDto> GetMyPrescriptionsAsync(
        long actorUserId,
        GetPatientPrescriptionsRequest? request,
        CancellationToken ct = default,
        string? ipAddress = null)
    {
        EnsureActor(actorUserId);
        var paging = NormalizeListRequest(request);
        var patientId = await ResolveActivePatientIdAsync(actorUserId, ct);
        var query = BuildVisibleQuery(patientId);
        var totalCount = await query.CountAsync(ct);
        var prescriptions = await query
            .OrderByDescending(p => p.IssuedAt ?? p.CreatedAt)
            .ThenByDescending(p => p.Id)
            .Skip((paging.Page - 1) * paging.PageSize)
            .Take(paging.PageSize)
            .ToListAsync(ct);

        var items = await MapListItemsAsync(prescriptions, ct);
        _auditLogger.Add(
            actorUserId,
            "PATIENT_PRESCRIPTIONS_VIEWED",
            "PATIENT",
            patientId.ToString(CultureInfo.InvariantCulture),
            new { page = paging.Page, pageSize = paging.PageSize, resultCount = items.Count },
            ipAddress);
        await _db.SaveChangesAsync(ct);

        return new PatientPrescriptionListResponseDto(items, paging.Page, paging.PageSize, totalCount);
    }

    public async Task<PatientPrescriptionDetailDto> GetMyPrescriptionAsync(
        long actorUserId,
        string? idOrCode,
        CancellationToken ct = default,
        string? ipAddress = null)
    {
        EnsureActor(actorUserId);
        var identifier = ParseIdentifier(idOrCode);
        var patientId = await ResolveActivePatientIdAsync(actorUserId, ct);
        var query = BuildVisibleQuery(patientId);
        var prescription = identifier.Id is long id
            ? await query.FirstOrDefaultAsync(p => p.Id == id, ct)
            : await query.FirstOrDefaultAsync(
                p => p.PrescriptionCode != null && p.PrescriptionCode.ToUpper() == identifier.Code,
                ct);
        if (prescription is null)
            throw new NotFoundException(NotFoundMessage);

        var summary = (await MapListItemsAsync([prescription], ct)).Single();
        var prescriptionItems = await _db.PrescriptionItems
            .AsNoTracking()
            .Where(item => item.PrescriptionId == prescription.Id)
            .OrderBy(item => item.Id)
            .ToListAsync(ct);
        var medicineIds = prescriptionItems.Select(item => item.MedicineId!.Value).Distinct().ToList();
        var medicines = await _db.Medicines
            .AsNoTracking()
            .Where(medicine => medicineIds.Contains(medicine.Id))
            .ToDictionaryAsync(medicine => medicine.Id, ct);
        var items = prescriptionItems.Select(item =>
        {
            var medicine = medicines[item.MedicineId!.Value];
            return new PatientPrescriptionMedicineDto(
                item.Id,
                BlankToNull(medicine.MedicineCode),
                medicine.Name.Trim(),
                BlankToNull(medicine.ActiveIngredient),
                BlankToNull(medicine.Strength),
                BlankToNull(medicine.DosageForm),
                item.PrescribedQuantity!.Value,
                BlankToNull(medicine.DispensingUnit),
                item.SingleDoseQuantity,
                BlankToNull(item.SingleDoseUnit),
                BlankToNull(item.DoseScheduleText),
                BlankToNull(item.Frequency),
                item.DurationDays,
                BlankToNull(item.AdministrationRoute),
                BlankToNull(item.Instructions));
        }).ToList();

        _auditLogger.Add(
            actorUserId,
            "PATIENT_PRESCRIPTION_VIEWED",
            "PRESCRIPTION",
            prescription.Id.ToString(CultureInfo.InvariantCulture),
            new { prescription.VisitId },
            ipAddress);
        await _db.SaveChangesAsync(ct);

        return new PatientPrescriptionDetailDto(summary, items);
    }

    private IQueryable<Prescription> BuildVisibleQuery(long patientId) =>
        _db.Prescriptions
            .AsNoTracking()
            .Where(prescription =>
                prescription.VisitId != null &&
                prescription.Status != null &&
                VisibleStatuses.Contains(prescription.Status.ToUpper()) &&
                _db.Visits.Any(visit => visit.Id == prescription.VisitId && visit.PatientId == patientId) &&
                _db.PrescriptionItems.Any(item =>
                    item.PrescriptionId == prescription.Id &&
                    item.MedicineId != null &&
                    item.PrescribedQuantity > 0) &&
                !_db.PrescriptionItems.Any(item =>
                    item.PrescriptionId == prescription.Id &&
                    (item.MedicineId == null ||
                     item.PrescribedQuantity == null ||
                     item.PrescribedQuantity <= 0 ||
                     !_db.Medicines.Any(medicine => medicine.Id == item.MedicineId))) &&
                !_db.Prescriptions.Any(replacement =>
                    replacement.ReplacesPrescriptionId == prescription.Id &&
                    replacement.Status != null &&
                    VisibleStatuses.Contains(replacement.Status.ToUpper())));

    private async Task<IReadOnlyList<PatientPrescriptionListItemDto>> MapListItemsAsync(
        IReadOnlyList<Prescription> prescriptions,
        CancellationToken ct)
    {
        if (prescriptions.Count == 0)
            return [];

        var visitIds = prescriptions.Select(p => p.VisitId!.Value).Distinct().ToList();
        var visits = await _db.Visits.AsNoTracking()
            .Where(visit => visitIds.Contains(visit.Id))
            .ToDictionaryAsync(visit => visit.Id, ct);
        var prescriptionIds = prescriptions.Select(p => p.Id).ToList();
        var itemCounts = await _db.PrescriptionItems.AsNoTracking()
            .Where(item => item.PrescriptionId != null && prescriptionIds.Contains(item.PrescriptionId.Value))
            .GroupBy(item => item.PrescriptionId!.Value)
            .Select(group => new { PrescriptionId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.PrescriptionId, item => item.Count, ct);
        var doctorIds = prescriptions
            .Select(p => p.PrescribingDoctorId ?? visits[p.VisitId!.Value].AssignedDoctorId)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
        var doctors = await _db.Doctors.AsNoTracking()
            .Where(doctor => doctorIds.Contains(doctor.Id))
            .ToDictionaryAsync(doctor => doctor.Id, ct);
        var staffIds = doctors.Values
            .Where(doctor => doctor.StaffId.HasValue)
            .Select(doctor => doctor.StaffId!.Value)
            .Distinct()
            .ToList();
        var staff = await _db.Staff.AsNoTracking()
            .Where(item => staffIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, ct);
        var hospitalIds = doctors.Values
            .Where(doctor => doctor.HospitalId.HasValue)
            .Select(doctor => doctor.HospitalId!.Value)
            .Distinct()
            .ToList();
        var hospitals = await _db.Hospitals.AsNoTracking()
            .Where(hospital => hospitalIds.Contains(hospital.Id))
            .ToDictionaryAsync(hospital => hospital.Id, ct);

        return prescriptions.Select(prescription =>
        {
            var visit = visits[prescription.VisitId!.Value];
            var doctorId = prescription.PrescribingDoctorId ?? visit.AssignedDoctorId;
            doctors.TryGetValue(doctorId ?? 0, out var doctor);
            staff.TryGetValue(doctor?.StaffId ?? 0, out var doctorStaff);
            hospitals.TryGetValue(doctor?.HospitalId ?? 0, out var hospital);
            return new PatientPrescriptionListItemDto(
                prescription.Id,
                BlankToNull(prescription.PrescriptionCode),
                visit.Id,
                BlankToNull(visit.VisitCode),
                prescription.Status!.Trim().ToUpperInvariant(),
                prescription.IssuedAt,
                doctorStaff is null ? null : BlankToNull(doctorStaff.FullName),
                hospital is null ? null : BlankToNull(hospital.Name),
                itemCounts[prescription.Id]);
        }).ToList();
    }

    private async Task<long> ResolveActivePatientIdAsync(long actorUserId, CancellationToken ct)
    {
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var user = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == actorUserId, ct)
            ?? throw new NotFoundException("User", actorUserId);
        if (user.AccountStatus != AccountStatus.ACTIVE ||
            user.LockedUntil.HasValue && user.LockedUntil.Value > nowUtc)
        {
            throw new UnauthorizedAccessException("Tài khoản bệnh nhân chưa kích hoạt hoặc đang bị khóa.");
        }

        var patients = await _db.Patients.AsNoTracking()
            .Where(patient => patient.UserId == actorUserId)
            .Select(patient => new { patient.Id, patient.Status })
            .Take(2)
            .ToListAsync(ct);
        if (patients.Count == 0)
            throw new NotFoundException("Patient", actorUserId);
        if (patients.Count > 1)
            throw new ConflictException(PatientSelfResolver.AmbiguousMessage);
        if (!string.Equals(patients[0].Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Hồ sơ bệnh nhân đang ngừng hoạt động.");

        return patients[0].Id;
    }

    private static ListRequest NormalizeListRequest(GetPatientPrescriptionsRequest? request)
    {
        var page = request?.Page ?? 1;
        var pageSize = request?.PageSize ?? 20;
        if (page < 1 || pageSize < 1 || pageSize > MaxPageSize || page > int.MaxValue / pageSize)
            throw new ValidationException("Tham số phân trang không hợp lệ.");

        return new ListRequest(page, pageSize);
    }

    private static PrescriptionIdentifier ParseIdentifier(string? idOrCode)
    {
        if (string.IsNullOrWhiteSpace(idOrCode))
            throw new ValidationException("Mã đơn thuốc không hợp lệ.");

        var trimmed = idOrCode.Trim();
        if (trimmed.StartsWith('#'))
            trimmed = trimmed[1..].TrimStart();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxIdentifierLength)
            throw new ValidationException("Mã đơn thuốc không hợp lệ.");

        if (trimmed.All(char.IsAsciiDigit))
        {
            if (!long.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
                throw new ValidationException("Mã đơn thuốc không hợp lệ.");
            return new PrescriptionIdentifier(id, null);
        }

        return new PrescriptionIdentifier(null, trimmed.ToUpperInvariant());
    }

    private static void EnsureActor(long actorUserId)
    {
        if (actorUserId <= 0)
            throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");
    }

    private static string? BlankToNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record ListRequest(int Page, int PageSize);
    private sealed record PrescriptionIdentifier(long? Id, string? Code);
}
