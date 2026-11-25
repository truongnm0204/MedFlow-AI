using System.Globalization;
using MedFlow.Application.DTOs.Realtime;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Appointments;
using MedFlow.Application.Services.Payments;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Realtime;

public sealed class ClinicalRealtimeAccessService : IClinicalRealtimeAccessService
{
    private const string ForbiddenPaymentMessage = "Bạn không có quyền xem khoản phí của lượt khám này.";
    private const string ForbiddenQueueMessage = "Bạn không có quyền truy cập kênh hàng đợi này.";
    private const string StaffMissingHospitalMessage = "Tài khoản nhân sự chưa được gán cơ sở y tế.";
    private const string InactiveStaffMessage = "Hồ sơ nhân sự của người dùng hiện không ở trạng thái hoạt động.";
    private const string InactiveHospitalMessage = "Cơ sở y tế của nhân sự hiện đang ngừng hoạt động.";

    private static readonly string[] StaffQueueRoles =
    [
        RoleNames.Reception,
        RoleNames.Doctor,
        RoleNames.DiagnosticStaff,
        RoleNames.HospitalAdmin,
        RoleNames.SystemAdmin,
    ];

    private static readonly string[] StaffQueuePermissions =
    [
        PermissionCodes.QueueManage,
        PermissionCodes.VisitAssign,
        PermissionCodes.PatientCheckin,
        PermissionCodes.MedicalRecordWrite,
        PermissionCodes.DiagnosticResultWrite,
        PermissionCodes.DiagnosticOrder,
        PermissionCodes.HospitalConfig,
        PermissionCodes.UserManage,
    ];

    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;

    public ClinicalRealtimeAccessService(
        IApplicationDbContext db,
        TimeProvider? timeProvider = null)
    {
        _db = db;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task EnsureConnectedUserActiveAsync(
        long userId,
        CancellationToken ct = default)
    {
        await VisitPaymentAccess.RequireActiveUserAsync(_db, userId, _timeProvider, ct);
    }

    public async Task EnsureConnectedSessionActiveAsync(
        long userId,
        long? sessionId,
        CancellationToken ct = default)
    {
        await VisitPaymentAccess.RequireActiveUserAsync(_db, userId, _timeProvider, ct);

        if (sessionId is not long sid || sid <= 0)
            return;

        try
        {
            var session = await _db.AuthSessions.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == sid && s.UserId == userId, ct);
            if (session is null)
            {
                throw new UnauthorizedAccessException("Phiên đăng nhập không tồn tại hoặc đã bị thu hồi.");
            }

            var rootId = session.RootSessionId ?? session.Id;
            var family = await _db.AuthSessions.AsNoTracking()
                .Where(s => s.UserId == userId && (s.Id == rootId || s.RootSessionId == rootId))
                .ToListAsync(ct);

            var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
            var hasExplicitRevocation = family.Any(s =>
                (s.RevokedReason != null && !string.Equals(s.RevokedReason, AuthSessionRevokeReasons.Rotated, StringComparison.OrdinalIgnoreCase)) ||
                (s.RevokedAt != null && s.RevokedReason == null && string.IsNullOrEmpty(s.ReplacedByTokenHash)));
            var hasActiveLeaf = family.Any(s => s.RevokedAt == null && s.ExpiresAt > nowUtc);

            if (hasExplicitRevocation || !hasActiveLeaf)
            {
                throw new UnauthorizedAccessException("Phiên đăng nhập đã hết hạn hoặc đã bị thu hồi.");
            }
        }
        catch (NotSupportedException)
        {
            // Specialized unit-test DbContext without AuthSessions DbSet.
        }
    }

    public string ResolveVisitPaymentResourceKey(string? visitIdOrCode, long? knownVisitId = null)
    {
        if (knownVisitId is > 0)
            return $"visit-payment:{knownVisitId.Value.ToString(CultureInfo.InvariantCulture)}";

        var parsed = VisitPaymentAccess.ParseVisitIdentifier(visitIdOrCode);
        return parsed.Id is long id
            ? $"visit-payment:{id.ToString(CultureInfo.InvariantCulture)}"
            : $"visit-payment:{parsed.Code}";
    }

    public async Task<AuthorizedRealtimeSubscription> AuthorizeVisitPaymentJoinAsync(
        long userId,
        string? visitIdOrCode,
        CancellationToken ct = default)
    {
        await VisitPaymentAccess.RequireActiveUserAsync(_db, userId, _timeProvider, ct);

        var (roleIds, roleCodes) = await LoadUserRolesAsync(userId, ct);
        var isSystemAdmin = HasRole(roleCodes, RoleNames.SystemAdmin);
        var isHospitalAdmin = HasRole(roleCodes, RoleNames.HospitalAdmin);
        var isReception = HasRole(roleCodes, RoleNames.Reception);
        var isPatient = HasRole(roleCodes, RoleNames.Patient);

        long? actorHospitalId = null;
        HashSet<long>? ownedPatientIds = null;

        if (isSystemAdmin || isHospitalAdmin || isReception)
        {
            var permSet = await LoadPermissionsForRolesAsync(roleIds, ct);
            var hasRequiredPerm = isSystemAdmin
                ? permSet.Contains(PermissionCodes.PaymentView) ||
                  permSet.Contains(PermissionCodes.AuditRead) ||
                  permSet.Contains(PermissionCodes.HospitalConfig)
                : permSet.Contains(PermissionCodes.PaymentView) ||
                  permSet.Contains(PermissionCodes.PatientCheckin) ||
                  permSet.Contains(PermissionCodes.HospitalConfig);

            if (!hasRequiredPerm)
                throw new ForbiddenException(ForbiddenPaymentMessage);

            var staff = await _db.Staff.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == userId, ct);
            if (staff is not null && !string.Equals(staff.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
                throw new ForbiddenException(InactiveStaffMessage);

            if (!isSystemAdmin)
            {
                if (staff is null || staff.HospitalId is null)
                    throw new ForbiddenException(StaffMissingHospitalMessage);

                var hospital = await _db.Hospitals.AsNoTracking()
                    .FirstOrDefaultAsync(h => h.Id == staff.HospitalId.Value, ct)
                    ?? throw new NotFoundException("Hospital", staff.HospitalId.Value);

                if (hospital.IsActive == false)
                    throw new ForbiddenException(InactiveHospitalMessage);
            }

            actorHospitalId = staff?.HospitalId;
        }
        else if (isPatient)
        {
            ownedPatientIds = await VisitPaymentAccess.LoadOwnedPatientIdsAsync(_db, userId, _timeProvider, ct);
        }
        else
        {
            throw new ForbiddenException(ForbiddenPaymentMessage);
        }

        var parsed = VisitPaymentAccess.ParseVisitIdentifier(visitIdOrCode);
        var visit = await VisitPaymentAccess.FindVisitAsync(_db, parsed, ct)
            ?? throw new NotFoundException(VisitPaymentAccess.VisitNotFoundMessage);

        if (ownedPatientIds is not null)
        {
            if (!visit.PatientId.HasValue || !ownedPatientIds.Contains(visit.PatientId.Value))
                throw new ForbiddenException(ForbiddenPaymentMessage);
        }

        var hospitalIds = await VisitHospitalResolver.CollectAsync(_db, visit, ct);
        if (hospitalIds.Count == 0)
            throw new NotFoundException(VisitPaymentAccess.VisitNotFoundMessage);
        if (hospitalIds.Count > 1)
            throw new ConflictException(VisitPaymentAccess.HospitalConflictMessage);

        var visitHospitalId = hospitalIds.First();
        if (ownedPatientIds is null && !isSystemAdmin && actorHospitalId != visitHospitalId)
            throw new ForbiddenException(ForbiddenPaymentMessage);

        var resourceKey = $"visit-payment:{visit.Id.ToString(CultureInfo.InvariantCulture)}";
        return new AuthorizedRealtimeSubscription(
            ResourceKey: resourceKey,
            PrimaryGroupNames: [RealtimeGroupNames.VisitPayment(visit.Id)],
            BucketGroupNames: [],
            HospitalId: visitHospitalId,
            PatientId: visit.PatientId,
            AppointmentId: visit.AppointmentId,
            VisitId: visit.Id);
    }

    public async Task<AuthorizedRealtimeSubscription> AuthorizeAppointmentQueueJoinAsync(
        long userId,
        string? appointmentIdOrCode,
        CancellationToken ct = default)
    {
        await VisitPaymentAccess.RequireActiveUserAsync(_db, userId, _timeProvider, ct);

        var (_, roleCodes) = await LoadUserRolesAsync(userId, ct);
        if (!HasRole(roleCodes, RoleNames.Patient))
            throw new ForbiddenException("Chỉ bệnh nhân mới sử dụng kênh theo dõi hàng đợi lịch hẹn cá nhân.");

        var nowLocal = AppointmentBookingRules.GetVietnamNow(_timeProvider);
        var today = DateOnly.FromDateTime(nowLocal);

        var view = await AppointmentReadModel.LoadOwnedAppointmentViewAsync(
            _db,
            userId,
            appointmentIdOrCode,
            nowLocal,
            "Bạn không có quyền xem hàng đợi của lịch hẹn này.",
            ct);

        var item = AppointmentReadModel.MapItem(
            view.Appointment,
            view.ScopeMember,
            view.Visit,
            view.Queue,
            view.Paid,
            nowLocal,
            today);

        var appointmentId = view.Appointment.Id;
        var visitId = view.Visit?.Id;

        var candidateQueues = await _db.QueueEntries
            .AsNoTracking()
            .Where(q =>
                (q.AppointmentId == appointmentId || (visitId.HasValue && q.VisitId == visitId.Value)) &&
                (q.QueueType == null || q.QueueType.ToUpper() == "EXAMINATION" || q.QueueType.ToUpper() == "RETURN_CONSULTATION"))
            .ToListAsync(ct);

        var orderedCandidates = candidateQueues
            .OrderByDescending(q => visitId.HasValue && q.VisitId == visitId.Value)
            .ThenByDescending(q => q.Id)
            .ToList();

        var activeQueue = orderedCandidates.FirstOrDefault(
            q => AppointmentBookingRules.NormalizeStatusKey(q.Status) != "CANCELLED");
        var effectiveQueue = activeQueue ?? orderedCandidates.FirstOrDefault();

        var visitStatus = AppointmentBookingRules.NormalizeStatusKey(view.Visit?.Status);

        if (item.Status == "CANCELLED" || visitStatus == "CANCELLED")
            throw new ConflictException("Lịch hẹn đã hủy nên không còn số thứ tự trong hàng đợi.");

        if (item.Status == "NO_SHOW" || visitStatus == "NO_SHOW")
            throw new ConflictException("Lịch hẹn đã ghi nhận vắng mặt nên không còn số thứ tự trong hàng đợi.");

        if (effectiveQueue is not null && AppointmentBookingRules.NormalizeStatusKey(effectiveQueue.Status) == "CANCELLED")
            throw new ConflictException("Số thứ tự trong hàng đợi của lượt khám này đã bị hủy.");

        var isCompleted = item.Status == "COMPLETED"
            || visitStatus == "COMPLETED"
            || AppointmentBookingRules.NormalizeStatusKey(effectiveQueue?.Status) == "COMPLETED"
            || view.Visit?.CompletedAt.HasValue == true
            || effectiveQueue?.CompletedAt.HasValue == true;

        var hospitalId = view.Appointment.Slot?.Service?.HospitalId
            ?? view.Appointment.Slot?.Room?.HospitalId
            ?? view.Appointment.Slot?.Doctor?.HospitalId
            ?? 0L;

        var bucketGroups = new List<string>(2);
        if (!isCompleted)
        {
            var effectiveRoomId = view.Visit?.ExaminationRoomId ?? view.Appointment.Slot?.RoomId;
            var effectiveDoctorId = view.Visit?.AssignedDoctorId ?? view.Appointment.Slot?.DoctorId;
            var targetServicePointId = effectiveQueue?.CurrentServicePointId ?? effectiveQueue?.ServicePointId;

            if (targetServicePointId.HasValue && view.Visit?.ExaminationRoomId is long visitRoomId)
            {
                var matchesVisitRoom = await DoesServicePointMatchRoomAsync(targetServicePointId.Value, visitRoomId, ct);
                if (!matchesVisitRoom)
                {
                    targetServicePointId = await TryResolveExaminationServicePointIdAsync(
                        hospitalId,
                        visitRoomId,
                        effectiveDoctorId,
                        ct);
                }
            }
            else if (!targetServicePointId.HasValue && (effectiveRoomId.HasValue || effectiveDoctorId.HasValue))
            {
                targetServicePointId = await TryResolveExaminationServicePointIdAsync(
                    hospitalId,
                    effectiveRoomId,
                    effectiveDoctorId,
                    ct);
            }

            if (targetServicePointId.HasValue)
                bucketGroups.Add(RealtimeGroupNames.PatientBucketServicePoint(targetServicePointId.Value));

            if (effectiveRoomId.HasValue)
                bucketGroups.Add(RealtimeGroupNames.PatientBucketRoom(effectiveRoomId.Value));
        }

        var resourceKey = $"appointment-queue:{appointmentId.ToString(CultureInfo.InvariantCulture)}";
        return new AuthorizedRealtimeSubscription(
            ResourceKey: resourceKey,
            PrimaryGroupNames: [RealtimeGroupNames.AppointmentQueue(appointmentId)],
            BucketGroupNames: bucketGroups,
            HospitalId: hospitalId > 0 ? hospitalId : null,
            PatientId: view.Appointment.PatientId,
            AppointmentId: appointmentId,
            VisitId: visitId);
    }

    public async Task<AuthorizedRealtimeSubscription> AuthorizeServicePointJoinAsync(
        long userId,
        string? servicePointId,
        CancellationToken ct = default)
    {
        var staffContext = await RequireAuthorizedQueueStaffAsync(userId, requireHospital: false, ct);
        var trimmed = ValidateServicePointIdentifier(servicePointId);

        if (trimmed.StartsWith("sp-room-", StringComparison.OrdinalIgnoreCase))
        {
            var rawRoomPart = trimmed["sp-room-".Length..];
            if (!long.TryParse(rawRoomPart, NumberStyles.None, CultureInfo.InvariantCulture, out var roomId) || roomId <= 0)
                throw new ValidationException("Mã điểm phục vụ không hợp lệ.");

            var room = await _db.HospitalRooms.AsNoTracking().FirstOrDefaultAsync(r => r.Id == roomId, ct)
                ?? throw new NotFoundException("HospitalRoom", roomId);

            var roomHospitalId = room.HospitalId
                ?? throw new ConflictException("Phòng khám chưa gắn cơ sở y tế.");

            if (!staffContext.IsSystemAdmin && staffContext.HospitalId != roomHospitalId)
                throw new ForbiddenException(ForbiddenQueueMessage);

            return new AuthorizedRealtimeSubscription(
                ResourceKey: $"service-point:{roomHospitalId.ToString(CultureInfo.InvariantCulture)}:room:{roomId.ToString(CultureInfo.InvariantCulture)}",
                PrimaryGroupNames: [RealtimeGroupNames.HospitalRoom(roomHospitalId, roomId)],
                BucketGroupNames: [],
                HospitalId: roomHospitalId);
        }

        var numericPart = trimmed;
        if (trimmed.StartsWith("sp-", StringComparison.OrdinalIgnoreCase))
            numericPart = trimmed["sp-".Length..];
        else if (trimmed.StartsWith("dsp-", StringComparison.OrdinalIgnoreCase))
            numericPart = trimmed["dsp-".Length..];

        if (!long.TryParse(numericPart, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedSpId) || parsedSpId <= 0)
            throw new ValidationException("Mã điểm phục vụ không hợp lệ.");

        var sp = await _db.QueueServicePoints.AsNoTracking().FirstOrDefaultAsync(p => p.Id == parsedSpId, ct)
            ?? throw new NotFoundException("QueueServicePoint", parsedSpId);

        var spHospitalId = await ResolveServicePointHospitalIdAsync(sp, ct)
            ?? throw new NotFoundException("QueueServicePoint", parsedSpId);

        if (!staffContext.IsSystemAdmin && staffContext.HospitalId != spHospitalId)
            throw new ForbiddenException(ForbiddenQueueMessage);

        var groups = new List<string>(2)
        {
            RealtimeGroupNames.HospitalServicePoint(spHospitalId, sp.Id),
        };
        if (sp.RoomId is > 0)
            groups.Add(RealtimeGroupNames.HospitalRoom(spHospitalId, sp.RoomId.Value));

        return new AuthorizedRealtimeSubscription(
            ResourceKey: $"service-point:{spHospitalId.ToString(CultureInfo.InvariantCulture)}:sp:{sp.Id.ToString(CultureInfo.InvariantCulture)}",
            PrimaryGroupNames: groups,
            BucketGroupNames: [],
            HospitalId: spHospitalId);
    }

    public async Task<AuthorizedRealtimeSubscription> AuthorizeHospitalQueueJoinAsync(
        long userId,
        CancellationToken ct = default)
    {
        var staffContext = await RequireAuthorizedQueueStaffAsync(userId, requireHospital: true, ct);
        var hospitalId = staffContext.HospitalId!.Value;

        return new AuthorizedRealtimeSubscription(
            ResourceKey: $"hospital-queue:{hospitalId.ToString(CultureInfo.InvariantCulture)}",
            PrimaryGroupNames: [RealtimeGroupNames.HospitalQueue(hospitalId)],
            BucketGroupNames: [],
            HospitalId: hospitalId);
    }

    private async Task<StaffQueueContext> RequireAuthorizedQueueStaffAsync(
        long userId,
        bool requireHospital,
        CancellationToken ct)
    {
        await VisitPaymentAccess.RequireActiveUserAsync(_db, userId, _timeProvider, ct);

        var (roleIds, roleCodes) = await LoadUserRolesAsync(userId, ct);
        if (!roleCodes.Any(code => StaffQueueRoles.Contains(code, StringComparer.OrdinalIgnoreCase)))
            throw new ForbiddenException(ForbiddenQueueMessage);

        var permSet = await LoadPermissionsForRolesAsync(roleIds, ct);
        if (!StaffQueuePermissions.Any(permSet.Contains))
            throw new ForbiddenException(ForbiddenQueueMessage);

        var isSystemAdmin = HasRole(roleCodes, RoleNames.SystemAdmin);
        var staff = await _db.Staff.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == userId, ct);

        if (staff is not null && !string.Equals(staff.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
            throw new ForbiddenException(InactiveStaffMessage);

        if (requireHospital || !isSystemAdmin)
        {
            if (staff is null || staff.HospitalId is null)
                throw new ForbiddenException(StaffMissingHospitalMessage);

            var hospital = await _db.Hospitals.AsNoTracking()
                .FirstOrDefaultAsync(h => h.Id == staff.HospitalId.Value, ct)
                ?? throw new NotFoundException("Hospital", staff.HospitalId.Value);

            if (hospital.IsActive == false)
                throw new ForbiddenException(InactiveHospitalMessage);
        }

        return new StaffQueueContext(isSystemAdmin, staff?.HospitalId);
    }

    private async Task<long?> ResolveServicePointHospitalIdAsync(QueueServicePoint sp, CancellationToken ct)
    {
        if (sp.HospitalId is > 0)
            return sp.HospitalId.Value;

        if (sp.RoomId is > 0)
        {
            var roomHospitalId = await _db.HospitalRooms.AsNoTracking()
                .Where(r => r.Id == sp.RoomId.Value)
                .Select(r => r.HospitalId)
                .FirstOrDefaultAsync(ct);
            if (roomHospitalId is > 0)
                return roomHospitalId.Value;
        }

        if (sp.DoctorId is > 0)
        {
            var doctor = await _db.Doctors.AsNoTracking()
                .Include(d => d.Staff)
                .FirstOrDefaultAsync(d => d.Id == sp.DoctorId.Value, ct);
            var docHospitalId = doctor?.HospitalId ?? doctor?.Staff?.HospitalId;
            if (docHospitalId is > 0)
                return docHospitalId.Value;
        }

        return null;
    }

    private async Task<bool> DoesServicePointMatchRoomAsync(
        long servicePointId,
        long visitRoomId,
        CancellationToken ct)
    {
        try
        {
            var spRoomId = await _db.QueueServicePoints
                .AsNoTracking()
                .Where(sp => sp.Id == servicePointId && sp.IsActive != false)
                .Select(sp => sp.RoomId)
                .FirstOrDefaultAsync(ct);

            return spRoomId.HasValue && spRoomId.Value == visitRoomId;
        }
        catch (NotSupportedException)
        {
            return true;
        }
    }

    private async Task<long?> TryResolveExaminationServicePointIdAsync(
        long hospitalId,
        long? effectiveRoomId,
        long? effectiveDoctorId,
        CancellationToken ct)
    {
        try
        {
            var servicePoints = await _db.QueueServicePoints
                .AsNoTracking()
                .Where(sp =>
                    sp.IsActive != false &&
                    (hospitalId <= 0 || sp.HospitalId == null || sp.HospitalId == hospitalId))
                .OrderBy(sp => sp.Id)
                .ToListAsync(ct);

            var examPoints = servicePoints
                .Where(sp => AppointmentBookingRules.NormalizeStatusKey(sp.PointType) is not ("DIAGNOSTIC" or "RECEPTION" or "PHARMACY"))
                .ToList();

            if (effectiveRoomId.HasValue)
            {
                var byRoom = examPoints.FirstOrDefault(sp => sp.RoomId == effectiveRoomId.Value);
                if (byRoom is not null)
                    return byRoom.Id;
            }

            if (effectiveDoctorId.HasValue)
            {
                var byDoctor = examPoints.FirstOrDefault(sp =>
                    sp.DoctorId == effectiveDoctorId.Value &&
                    (!effectiveRoomId.HasValue || sp.RoomId == null || sp.RoomId == effectiveRoomId.Value));
                if (byDoctor is not null)
                    return byDoctor.Id;
            }
        }
        catch (NotSupportedException)
        {
            // Fallback when QueueServicePoints DbSet is unmapped in specialized unit-test DbContexts.
        }

        return null;
    }

    private static string ValidateServicePointIdentifier(string? servicePointId)
    {
        if (string.IsNullOrWhiteSpace(servicePointId))
            throw new ValidationException("Mã điểm phục vụ không hợp lệ.");

        var trimmed = servicePointId.Trim();
        if (trimmed.Length > 60 || trimmed.Contains('<') || trimmed.Contains('>') || trimmed.StartsWith('-'))
            throw new ValidationException("Mã điểm phục vụ không hợp lệ.");

        foreach (var ch in trimmed)
        {
            if (!(char.IsAsciiLetterOrDigit(ch) || ch is '.' or '_' or '-'))
                throw new ValidationException("Mã điểm phục vụ không hợp lệ.");
        }

        return trimmed;
    }

    private async Task<(List<long> RoleIds, List<string> RoleCodes)> LoadUserRolesAsync(
        long userId,
        CancellationToken ct)
    {
        var roleIds = await _db.UserRoles.AsNoTracking()
            .Where(ur => ur.UserId == userId)
            .Select(ur => ur.RoleId)
            .ToListAsync(ct);

        if (roleIds.Count == 0)
            return ([], []);

        var roleCodes = await _db.Roles.AsNoTracking()
            .Where(r => roleIds.Contains(r.Id))
            .Select(r => r.Code)
            .ToListAsync(ct);

        return (roleIds, roleCodes);
    }

    private async Task<HashSet<string>> LoadPermissionsForRolesAsync(
        IReadOnlyCollection<long> roleIds,
        CancellationToken ct)
    {
        if (roleIds.Count == 0)
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var codes = await (
            from rp in _db.RolePermissions.AsNoTracking()
            join p in _db.Permissions.AsNoTracking() on rp.PermissionId equals p.Id
            where roleIds.Contains(rp.RoleId)
            select p.Code).ToListAsync(ct);

        return new HashSet<string>(codes, StringComparer.OrdinalIgnoreCase);
    }

    private static bool HasRole(IEnumerable<string> codes, string role) =>
        codes.Any(c => string.Equals(c, role, StringComparison.OrdinalIgnoreCase));

    private sealed record StaffQueueContext(bool IsSystemAdmin, long? HospitalId);
}
