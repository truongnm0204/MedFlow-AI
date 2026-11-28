using System.Globalization;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Appointments;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Payments;

internal static class VisitPaymentAccess
{
    public const string VisitNotFoundMessage = "Không tìm thấy lượt khám.";
    public const string InvalidVisitMessage = "Mã lượt khám không hợp lệ.";
    public const string InvalidActorMessage = "Thông tin định danh người dùng không hợp lệ.";
    public const string InactiveActorMessage = "Tài khoản người dùng hiện không ở trạng thái hoạt động.";
    public const string LockedActorMessage = "Tài khoản người dùng hiện đang bị tạm khóa.";
    public const string HospitalConflictMessage = "Không xác định được cơ sở của lượt khám.";
    public const string InactiveStaffMessage = "Hồ sơ nhân sự của người dùng hiện không ở trạng thái hoạt động.";
    public const string StaffMissingHospitalMessage = "Tài khoản nhân sự chưa được gán cơ sở y tế.";
    public const string InactiveHospitalMessage = "Cơ sở y tế của nhân sự hiện đang ngừng hoạt động.";

    private const int MaxIdentifierLength = 40;

    public static async Task<User> RequireActiveUserAsync(
        IApplicationDbContext db,
        long actorUserId,
        TimeProvider time,
        CancellationToken ct)
    {
        if (actorUserId <= 0)
            throw new UnauthorizedAccessException(InvalidActorMessage);

        var nowUtc = time.GetUtcNow().UtcDateTime;
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == actorUserId, ct)
            ?? throw new UnauthorizedAccessException(InvalidActorMessage);

        if (user.AccountStatus != AccountStatus.ACTIVE)
            throw new UnauthorizedAccessException(InactiveActorMessage);

        if (user.LockedUntil.HasValue && user.LockedUntil.Value > nowUtc)
            throw new UnauthorizedAccessException(LockedActorMessage);

        return user;
    }

    public static async Task<(List<long> RoleIds, HashSet<string> RoleCodes)> LoadActorRolesAsync(
        IApplicationDbContext db,
        long actorUserId,
        CancellationToken ct)
    {
        var roleIds = await db.UserRoles.AsNoTracking()
            .Where(ur => ur.UserId == actorUserId)
            .Select(ur => ur.RoleId)
            .ToListAsync(ct);

        var roleCodes = roleIds.Count == 0
            ? []
            : await db.Roles.AsNoTracking()
                .Where(r => roleIds.Contains(r.Id))
                .Select(r => r.Code)
                .ToListAsync(ct);

        return (roleIds, new HashSet<string>(roleCodes, StringComparer.OrdinalIgnoreCase));
    }

    public static async Task<HashSet<string>> LoadRolePermissionCodesAsync(
        IApplicationDbContext db,
        IReadOnlyCollection<long> roleIds,
        CancellationToken ct)
    {
        if (roleIds.Count == 0)
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var codes = await (
            from rp in db.RolePermissions.AsNoTracking()
            join p in db.Permissions.AsNoTracking() on rp.PermissionId equals p.Id
            where roleIds.Contains(rp.RoleId)
            select p.Code).ToListAsync(ct);

        return new HashSet<string>(codes, StringComparer.OrdinalIgnoreCase);
    }

    public static async Task<(Staff Staff, Doctor Doctor)> RequireActiveDoctorStaffAsync(
        IApplicationDbContext db,
        long actorUserId,
        string forbiddenMessage,
        CancellationToken ct)
    {
        var staff = await db.Staff.AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == actorUserId, ct)
            ?? throw new ForbiddenException(forbiddenMessage);

        if (!string.Equals(staff.Status?.Trim(), "ACTIVE", StringComparison.OrdinalIgnoreCase) ||
            staff.HospitalId is null)
        {
            throw new ForbiddenException(forbiddenMessage);
        }

        var doctor = await db.Doctors.AsNoTracking()
            .FirstOrDefaultAsync(d => d.StaffId == staff.Id, ct)
            ?? throw new ForbiddenException(forbiddenMessage);

        if (doctor.IsActive == false || (doctor.HospitalId.HasValue && doctor.HospitalId != staff.HospitalId))
        {
            throw new ForbiddenException(forbiddenMessage);
        }

        var hospital = await db.Hospitals.AsNoTracking()
            .FirstOrDefaultAsync(h => h.Id == staff.HospitalId.Value, ct)
            ?? throw new ForbiddenException(forbiddenMessage);

        if (hospital.IsActive == false)
        {
            throw new ForbiddenException(InactiveHospitalMessage);
        }

        return (staff, doctor);
    }

    public static async Task EnsureDoctorAssignedToVisitAsync(
        IApplicationDbContext db,
        Doctor doctor,
        Visit visit,
        long visitHospitalId,
        string forbiddenMessage,
        CancellationToken ct)
    {
        if (doctor.HospitalId.HasValue && doctor.HospitalId.Value != visitHospitalId)
            throw new ForbiddenException(forbiddenMessage);

        if (visit.AssignedDoctorId.HasValue)
        {
            if (visit.AssignedDoctorId.Value != doctor.Id)
                throw new ForbiddenException(forbiddenMessage);
            return;
        }

        if (visit.CompletedByDoctorId == doctor.Id)
            return;

        try
        {
            var orderedOnVisit = await db.DiagnosticOrders.AsNoTracking()
                .AnyAsync(o => o.VisitId == visit.Id && o.OrderingDoctorId == doctor.Id, ct);
            if (orderedOnVisit)
                return;
        }
        catch (NotSupportedException)
        {
        }

        if (visit.AppointmentId is long appointmentId)
        {
            var apptSlotId = await db.Appointments.AsNoTracking()
                .Where(a => a.Id == appointmentId)
                .Select(a => a.SlotId)
                .FirstOrDefaultAsync(ct);

            if (apptSlotId is long slotId)
            {
                var slotDoctorId = await db.ExaminationSlots.AsNoTracking()
                    .Where(s => s.Id == slotId)
                    .Select(s => s.DoctorId)
                    .FirstOrDefaultAsync(ct);
                if (slotDoctorId == doctor.Id)
                    return;
            }
        }

        try
        {
            var spIds = await db.QueueEntries.AsNoTracking()
                .Where(q => q.VisitId == visit.Id)
                .Select(q => q.CurrentServicePointId ?? q.ServicePointId)
                .Where(id => id != null)
                .Select(id => id!.Value)
                .Distinct()
                .ToListAsync(ct);

            if (spIds.Count > 0)
            {
                var matchesServicePoint = await db.QueueServicePoints.AsNoTracking()
                    .AnyAsync(sp => spIds.Contains(sp.Id) && sp.DoctorId == doctor.Id && sp.IsActive != false, ct);
                if (matchesServicePoint)
                    return;
            }
        }
        catch (NotSupportedException)
        {
        }

        throw new ForbiddenException(forbiddenMessage);
    }

    public static async Task<PostLockActorVerification> RevalidateActorPostLockAsync(
        IApplicationDbContext db,
        long actorUserId,
        Visit lockedVisit,
        TimeProvider time,
        string forbiddenMessage,
        IReadOnlyCollection<string>? allowedStaffPermissions = null,
        string? missingRequiredPermissionMessage = null,
        bool allowPatient = true,
        bool allowDoctor = true,
        bool allowPharmacy = false,
        CancellationToken ct = default)
    {
        await RequireActiveUserAsync(db, actorUserId, time, ct);
        var (roleIds, roleCodes) = await LoadActorRolesAsync(db, actorUserId, ct);

        var isReception = roleCodes.Contains(RoleNames.Reception);
        var isHospitalAdmin = roleCodes.Contains(RoleNames.HospitalAdmin);
        var isPharmacy = allowPharmacy && roleCodes.Contains(RoleNames.PharmacyStaff);
        var isDoctor = roleCodes.Contains(RoleNames.Doctor);
        var isPatient = roleCodes.Contains(RoleNames.Patient);

        if (!isReception && !isHospitalAdmin && !isPharmacy && !(allowDoctor && isDoctor) && allowPatient && isPatient)
        {
            var owned = await LoadOwnedPatientIdsAsync(db, actorUserId, time, ct);
            if (!owned.Contains(lockedVisit.PatientId ?? 0))
                throw new NotFoundException(VisitNotFoundMessage);

            var patientHospitalIds = await VisitHospitalResolver.CollectAsync(db, lockedVisit, ct);
            if (patientHospitalIds.Count == 0)
                throw new NotFoundException(VisitNotFoundMessage);
            if (patientHospitalIds.Count > 1)
                throw new ConflictException(HospitalConflictMessage);

            return new PostLockActorVerification(true, RoleNames.Patient, patientHospitalIds.First(), null, null, owned);
        }

        var hospitalIds = await VisitHospitalResolver.CollectAsync(db, lockedVisit, ct);
        if (hospitalIds.Count == 0)
            throw new NotFoundException(VisitNotFoundMessage);
        if (hospitalIds.Count > 1)
            throw new ConflictException(HospitalConflictMessage);

        var visitHospitalId = hospitalIds.First();

        if (isReception || isHospitalAdmin || isPharmacy)
        {
            var permSet = await LoadRolePermissionCodesAsync(db, roleIds, ct);
            if (allowedStaffPermissions is { Count: > 0 })
            {
                var hasPerm = allowedStaffPermissions.Any(p => permSet.Contains(p));
                if (!hasPerm)
                    throw new ForbiddenException(missingRequiredPermissionMessage ?? forbiddenMessage);
            }

            var staff = await db.Staff.AsNoTracking()
                .FirstOrDefaultAsync(s => s.UserId == actorUserId, ct)
                ?? throw new ForbiddenException(forbiddenMessage);

            if (!string.Equals(staff.Status?.Trim(), "ACTIVE", StringComparison.OrdinalIgnoreCase) ||
                staff.HospitalId is null)
            {
                throw new ForbiddenException(forbiddenMessage);
            }

            var hospital = await db.Hospitals.AsNoTracking()
                .FirstOrDefaultAsync(h => h.Id == staff.HospitalId.Value, ct)
                ?? throw new ForbiddenException(forbiddenMessage);

            if (hospital.IsActive == false)
                throw new ForbiddenException(InactiveHospitalMessage);

            if (staff.HospitalId.Value != visitHospitalId)
                throw new ForbiddenException(forbiddenMessage);

            var roleLabel = isHospitalAdmin
                ? RoleNames.HospitalAdmin
                : isPharmacy ? RoleNames.PharmacyStaff : RoleNames.Reception;
            return new PostLockActorVerification(false, roleLabel, visitHospitalId, staff, null, []);
        }

        if (allowDoctor && isDoctor)
        {
            var (staff, doctor) = await RequireActiveDoctorStaffAsync(db, actorUserId, forbiddenMessage, ct);
            if (staff.HospitalId != visitHospitalId)
                throw new ForbiddenException(forbiddenMessage);

            await EnsureDoctorAssignedToVisitAsync(
                db,
                doctor,
                lockedVisit,
                visitHospitalId,
                forbiddenMessage,
                ct);

            return new PostLockActorVerification(false, RoleNames.Doctor, visitHospitalId, staff, doctor, []);
        }

        throw new ForbiddenException(forbiddenMessage);
    }

    internal sealed record PostLockActorVerification(
        bool IsPatient,
        string RoleLabel,
        long VisitHospitalId,
        Staff? Staff,
        Doctor? Doctor,
        HashSet<long> OwnedPatientIds);

    public static bool IsMedicineCharge(VisitCharge charge) =>
        string.Equals(
            charge.ChargeType?.Trim(),
            VisitChargeTypes.Medicine,
            StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<long>? ParseExplicitVisitChargeIds(
        long? visitChargeId,
        IReadOnlyList<long>? visitChargeIds,
        string invalidIdMessage)
    {
        if (visitChargeId is <= 0)
            throw new ValidationException(invalidIdMessage);

        if (visitChargeIds is null)
        {
            return visitChargeId.HasValue ? [visitChargeId.Value] : null;
        }

        if (visitChargeIds.Count == 0)
            throw new ValidationException(invalidIdMessage);

        var seen = new HashSet<long>();
        var ordered = new List<long>(visitChargeIds.Count);
        foreach (var id in visitChargeIds)
        {
            if (id <= 0)
                throw new ValidationException(invalidIdMessage);
            if (!seen.Add(id))
                throw new ValidationException("Danh sách khoản phí không được chứa mã trùng lặp.");
            ordered.Add(id);
        }

        if (visitChargeId.HasValue && (ordered.Count != 1 || ordered[0] != visitChargeId.Value))
        {
            throw new ValidationException("Tham số visitChargeId và visitChargeIds không thống nhất.");
        }

        ordered.Sort();
        return ordered;
    }

    public static async Task<HashSet<long>> LoadOwnedPatientIdsAsync(
        IApplicationDbContext db,
        long actorUserId,
        TimeProvider time,
        CancellationToken ct)
    {
        var nowLocal = AppointmentBookingRules.GetVietnamNow(time);
        var owned = await AppointmentBookingRules.LoadOwnedPatientsPhiAsync(db, actorUserId, nowLocal, ct);
        var ids = new HashSet<long> { owned.Self.Id };
        foreach (var link in owned.FamilyLinks)
            ids.Add(link.MemberPatientId);
        return ids;
    }

    public static ParsedVisit ParseVisitIdentifier(string? idOrCode)
    {
        if (string.IsNullOrWhiteSpace(idOrCode))
            throw new ValidationException(InvalidVisitMessage);

        var trimmed = idOrCode.Trim();
        if (trimmed.StartsWith('#'))
            trimmed = trimmed[1..].Trim();

        if (trimmed.Length == 0 || trimmed.Length > MaxIdentifierLength)
            throw new ValidationException(InvalidVisitMessage);

        var numeric = trimmed[0] == '-'
            ? trimmed.Length > 1 && trimmed[1..].All(static c => c is >= '0' and <= '9')
            : trimmed.All(static c => c is >= '0' and <= '9');
        if (numeric)
        {
            if (!long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) || id <= 0)
                throw new ValidationException(InvalidVisitMessage);
            return new ParsedVisit(id, null);
        }

        return new ParsedVisit(null, trimmed.ToUpperInvariant());
    }

    public static async Task<Visit?> FindVisitAsync(
        IApplicationDbContext db,
        ParsedVisit parsed,
        CancellationToken ct)
    {
        if (parsed.Id is long id)
            return await db.Visits.AsNoTracking().FirstOrDefaultAsync(v => v.Id == id, ct);

        var code = parsed.Code!;
        return await db.Visits.AsNoTracking()
            .FirstOrDefaultAsync(v => v.VisitCode != null && v.VisitCode.ToUpper() == code, ct);
    }

    internal readonly record struct ParsedVisit(long? Id, string? Code);
}
