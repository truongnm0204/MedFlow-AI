using System.Globalization;
using System.Net;
using System.Text.Json;
using MedFlow.Application.Common.Time;
using MedFlow.Application.DTOs.Visits;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Appointments;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Visits;

public sealed class VisitRoomTransferService : IVisitRoomTransferService
{
    public const string VisitRoomTransferAction = "VISIT_ROOM_TRANSFER";
    public const string VisitEntityType = "VISIT";

    private static readonly string[] AllowedOperatorRoles =
    [
        RoleNames.Reception,
        RoleNames.HospitalAdmin,
        RoleNames.SystemAdmin,
    ];

    private static readonly string[] AllowedPermissionCodes =
    [
        PermissionCodes.VisitAssign,
        PermissionCodes.PatientCheckin,
        PermissionCodes.QueueManage,
        PermissionCodes.HospitalConfig,
        PermissionCodes.UserManage,
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;
    private readonly IRealtimeNotificationPublisher _realtimePublisher;
    private readonly IRealtimeAccessRevoker _accessRevoker;

    public VisitRoomTransferService(
        IApplicationDbContext db,
        TimeProvider? timeProvider = null,
        IRealtimeNotificationPublisher? realtimePublisher = null,
        IRealtimeAccessRevoker? accessRevoker = null)
    {
        _db = db;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _realtimePublisher = realtimePublisher ?? MedFlow.Application.Services.Realtime.NullRealtimeNotificationPublisher.Instance;
        _accessRevoker = accessRevoker ?? MedFlow.Application.Services.Realtime.NullRealtimeAccessRevoker.Instance;
    }

    public async Task<VisitCompatibleRoomsResponseDto> GetCompatibleRoomsAsync(
        long actorUserId,
        string idOrCode,
        CancellationToken ct = default)
    {
        if (actorUserId <= 0)
        {
            throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");
        }

        var (visitId, visitCode) = ParseVisitIdentifier(idOrCode);
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var operatorAccess = await EnsureOperatorAccessAsync(actorUserId, nowUtc, ct);

        var visit = visitId.HasValue
            ? await _db.Visits.AsNoTracking().FirstOrDefaultAsync(v => v.Id == visitId.Value, ct)
            : await _db.Visits.AsNoTracking().FirstOrDefaultAsync(
                v => v.VisitCode != null && v.VisitCode.ToUpper() == visitCode,
                ct);

        if (visit is null)
        {
            throw new NotFoundException("Visit", idOrCode.Trim());
        }

        var context = await LoadVisitContextAsync(visit, operatorAccess, ct);
        var activeVisitCountByRoomId = await GetActiveVisitCountByRoomAsync(visit.Id, ct);

        var candidateRooms = await _db.HospitalRooms
            .AsNoTracking()
            .Include(r => r.Department)
            .Where(r => r.HospitalId == context.VisitHospitalId &&
                        r.RoomType != null &&
                        r.RoomType.ToUpper() == "EXAMINATION" &&
                        (!context.RequiredDepartmentId.HasValue || r.DepartmentId == context.RequiredDepartmentId.Value))
            .OrderBy(r => r.RoomCode)
            .ThenBy(r => r.Id)
            .ToListAsync(ct);

        var nowLocal = HospitalClock.GetNow(_timeProvider);
        var roomDoctorsMap = await ResolveActiveDoctorsByRoomAsync(
            context.VisitHospitalId,
            candidateRooms.Select(r => r.Id).ToHashSet(),
            context.RequiredDepartmentId,
            context.Appointment?.Slot?.ServiceId,
            nowLocal,
            ct);

        var compatibleRoomDtos = candidateRooms
            .Select(r => MapCompatibleRoomItem(
                r,
                visit.ExaminationRoomId,
                context.RequiredDepartmentId,
                activeVisitCountByRoomId,
                roomDoctorsMap.GetValueOrDefault(r.Id, [])))
            .ToList();

        var transferHistory = await LoadTransferHistoryAsync(visit.Id, ct);

        return new VisitCompatibleRoomsResponseDto(
            VisitId: visit.Id,
            VisitCode: visit.VisitCode ?? $"VIS-{visit.Id}",
            PatientId: visit.PatientId ?? context.Patient?.Id,
            PatientCode: context.Patient?.PatientCode,
            PatientName: context.Patient?.FullName,
            VisitStatus: string.IsNullOrWhiteSpace(visit.Status) ? "WAITING" : visit.Status,
            CurrentRoomId: context.CurrentRoom?.Id ?? visit.ExaminationRoomId,
            CurrentRoomCode: context.CurrentRoom?.RoomCode,
            CurrentRoomName: context.CurrentRoom?.Name,
            DepartmentId: context.Department?.Id ?? context.RequiredDepartmentId,
            DepartmentCode: context.Department?.DepartmentCode,
            DepartmentName: context.Department?.Name,
            AssignedDoctorId: context.AssignedDoctor?.Id ?? visit.AssignedDoctorId,
            AssignedDoctorCode: context.AssignedDoctor?.DoctorCode,
            AssignedDoctorName: context.AssignedDoctor?.Staff?.FullName,
            QueueNumber: context.ActiveQueueEntry?.QueueNumber,
            QueueStatus: context.ActiveQueueEntry?.Status,
            CompatibleRooms: compatibleRoomDtos,
            TransferHistory: transferHistory);
    }

    public async Task<VisitRoomTransferResponseDto> TransferToCompatibleRoomAsync(
        long actorUserId,
        string idOrCode,
        TransferVisitRoomRequest request,
        string? ipAddress,
        CancellationToken ct = default)
    {
        if (actorUserId <= 0)
        {
            throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");
        }

        ArgumentNullException.ThrowIfNull(request);

        var (visitId, visitCode) = ParseVisitIdentifier(idOrCode);

        if (request.TargetRoomId.HasValue && request.TargetRoomId.Value <= 0)
        {
            throw new ValidationException("ID phòng khám đích phải lớn hơn 0.");
        }

        var targetRoomCode = string.IsNullOrWhiteSpace(request.TargetRoomCode)
            ? null
            : request.TargetRoomCode.Trim().ToUpperInvariant();

        if (!request.TargetRoomId.HasValue && targetRoomCode is null)
        {
            throw new ValidationException("Vui lòng chọn phòng khám đích hợp lệ để chuyển phòng.");
        }

        if (targetRoomCode is not null && targetRoomCode.Length > 40)
        {
            throw new ValidationException("Mã phòng khám đích không được vượt quá 40 ký tự.");
        }

        var trimmedReason = string.IsNullOrWhiteSpace(request.Reason)
            ? null
            : request.Reason.Trim();

        if (trimmedReason is null)
        {
            throw new ValidationException("Vui lòng nhập lý do chuyển phòng.");
        }

        if (trimmedReason.Length > 500)
        {
            throw new ValidationException("Lý do chuyển phòng không được vượt quá 500 ký tự.");
        }

        var trimmedNotes = string.IsNullOrWhiteSpace(request.Notes)
            ? null
            : request.Notes.Trim();

        if (trimmedNotes is not null && trimmedNotes.Length > 500)
        {
            throw new ValidationException("Ghi chú chuyển phòng không được vượt quá 500 ký tự.");
        }

        if (request.TargetDoctorId.HasValue && request.TargetDoctorId.Value <= 0)
        {
            throw new ValidationException("ID bác sĩ phụ trách không hợp lệ.");
        }

        var nowUtc = HospitalClock.GetUtcNow(_timeProvider);
        var nowLocal = HospitalClock.GetNow(_timeProvider);
        var operatorAccess = await EnsureOperatorAccessAsync(actorUserId, nowUtc, ct);

        var preVisit = visitId.HasValue
            ? await _db.Visits.AsNoTracking().FirstOrDefaultAsync(v => v.Id == visitId.Value, ct)
            : await _db.Visits.AsNoTracking().FirstOrDefaultAsync(
                v => v.VisitCode != null && v.VisitCode.ToUpper() == visitCode,
                ct);

        if (preVisit is null)
        {
            throw new NotFoundException("Visit", idOrCode.Trim());
        }

        var preContext = await LoadVisitContextAsync(preVisit, operatorAccess, ct, trackQueueEntry: false);

        EnsureVisitTransferable(preVisit, preContext.ActiveQueueEntry);

        if (!preVisit.ExaminationRoomId.HasValue || preContext.CurrentRoom is null)
        {
            throw new ConflictException("Lượt khám chưa được gán phòng khám ban đầu. Vui lòng gán phòng khám trước khi chuyển phòng.");
        }

        var preCurrentRoom = preContext.CurrentRoom;

        HospitalRoom? preTargetRoom;
        if (request.TargetRoomId.HasValue)
        {
            preTargetRoom = await _db.HospitalRooms
                .AsNoTracking()
                .Include(r => r.Department)
                .FirstOrDefaultAsync(r => r.Id == request.TargetRoomId.Value, ct);

            if (preTargetRoom is null)
            {
                throw new NotFoundException("HospitalRoom", request.TargetRoomId.Value);
            }

            if (targetRoomCode is not null &&
                !string.Equals(preTargetRoom.RoomCode, targetRoomCode, StringComparison.OrdinalIgnoreCase))
            {
                throw new ValidationException("Mã phòng khám đích không khớp với ID phòng khám đã chọn.");
            }
        }
        else
        {
            preTargetRoom = await _db.HospitalRooms
                .AsNoTracking()
                .Include(r => r.Department)
                .Where(r => r.RoomCode.ToUpper() == targetRoomCode)
                .OrderByDescending(r => r.HospitalId == preContext.VisitHospitalId)
                .ThenBy(r => r.Id)
                .FirstOrDefaultAsync(ct);

            if (preTargetRoom is null)
            {
                throw new NotFoundException("HospitalRoom", targetRoomCode!);
            }
        }

        if (!preTargetRoom.HospitalId.HasValue)
        {
            throw new ValidationException("Phòng khám đích chưa được cấu hình cơ sở y tế.");
        }

        var targetHospitalId = preTargetRoom.HospitalId.Value;
        if (!operatorAccess.IsSystemAdmin && targetHospitalId != operatorAccess.ActorHospitalId!.Value)
        {
            throw new ForbiddenException("Không thể chuyển bệnh nhân sang phòng khám thuộc cơ sở y tế khác.");
        }

        if (targetHospitalId != preContext.VisitHospitalId)
        {
            throw new ValidationException("Phòng khám đích phải thuộc cùng cơ sở y tế với lượt khám.");
        }

        if (preTargetRoom.Id == preCurrentRoom.Id)
        {
            throw new ValidationException("Phòng khám đích trùng với phòng khám hiện tại của bệnh nhân.");
        }

        if (!string.Equals(preTargetRoom.RoomType, "EXAMINATION", StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException("Phòng đích phải là buồng khám lâm sàng (EXAMINATION).");
        }

        if (preContext.RequiredDepartmentId.HasValue && preTargetRoom.DepartmentId != preContext.RequiredDepartmentId.Value)
        {
            throw new ValidationException("Phòng khám đích không tương thích chuyên khoa/khoa phòng với lượt khám hiện tại.");
        }

        if (!string.Equals(preTargetRoom.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException("Phòng khám đích hiện không hoạt động hoặc đang bảo trì.");
        }

        var targetDepartment = preTargetRoom.Department
            ?? (preTargetRoom.DepartmentId.HasValue
                ? await _db.Departments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == preTargetRoom.DepartmentId.Value, ct)
                : null);

        if (targetDepartment is not null && targetDepartment.IsActive == false)
        {
            throw new ConflictException("Khoa phòng của buồng khám đích đang ngừng hoạt động.");
        }

        var preTargetDoctor = await ResolveTargetRoomDoctorAsync(
            request.TargetDoctorId,
            preTargetRoom,
            preContext.VisitHospitalId,
            preContext.Appointment?.Slot?.ServiceId,
            nowLocal,
            ct);

        var prePreviousSpId = preContext.ActiveQueueEntry?.CurrentServicePointId ?? preContext.ActiveQueueEntry?.ServicePointId;
        var preTargetSpId = await ResolveTargetServicePointIdAsync(
            preTargetRoom.Id,
            preTargetDoctor?.Id,
            preContext.VisitHospitalId,
            ct);

        var doctorIdsToLock = new SortedSet<long>();
        if (preVisit.AssignedDoctorId.HasValue)
            doctorIdsToLock.Add(preVisit.AssignedDoctorId.Value);
        if (request.TargetDoctorId.HasValue)
            doctorIdsToLock.Add(request.TargetDoctorId.Value);
        if (preTargetDoctor is not null)
            doctorIdsToLock.Add(preTargetDoctor.Id);

        var roomIdsToLock = new[] { preCurrentRoom.Id, preTargetRoom.Id }
            .Distinct()
            .OrderBy(id => id)
            .ToArray();

        var spIdsToLock = new[] { prePreviousSpId, preTargetSpId }
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .OrderBy(id => id)
            .ToArray();

        var queueIdsToLock = preContext.ActiveQueueEntry is not null
            ? new[] { preContext.ActiveQueueEntry.Id }
            : Array.Empty<long>();

        await using var tx = await _db.BeginTransactionAsync(ct);

        // ADR-0059 strict lock order: doctors (05) -> hospital_rooms (06) -> visits (09) -> queue_service_points (10) -> queue_entries (11)
        if (doctorIdsToLock.Count > 0)
        {
            await _db.TryLockDoctorsForUpdateAsync(doctorIdsToLock, ct);
        }

        await _db.TryLockRoomsForUpdateAsync(roomIdsToLock, ct);
        await _db.TryLockVisitForUpdateAsync(preVisit.Id, ct);

        if (spIdsToLock.Length > 0)
        {
            await _db.TryLockServicePointsForUpdateAsync(spIdsToLock, ct);
        }

        if (queueIdsToLock.Length > 0)
        {
            await _db.TryLockQueueEntriesForUpdateAsync(queueIdsToLock, ct);
        }

        if (_db is DbContext dbCtx && dbCtx.Database.ProviderName != "Microsoft.EntityFrameworkCore.InMemory")
        {
            dbCtx.ChangeTracker.Clear();
        }

        var visit = await _db.Visits.FirstOrDefaultAsync(v => v.Id == preVisit.Id, ct)
            ?? throw new NotFoundException("Visit", idOrCode.Trim());

        var context = await LoadVisitContextAsync(visit, operatorAccess, ct, trackQueueEntry: true);
        EnsureVisitTransferable(visit, context.ActiveQueueEntry);

        if (!visit.ExaminationRoomId.HasValue || context.CurrentRoom is null)
        {
            throw new ConflictException("Lượt khám chưa được gán phòng khám ban đầu. Vui lòng gán phòng khám trước khi chuyển phòng.");
        }

        var currentRoom = context.CurrentRoom;
        if (currentRoom.Id == preTargetRoom.Id)
        {
            throw new ValidationException("Phòng khám đích trùng với phòng khám hiện tại của bệnh nhân.");
        }

        var targetRoom = await _db.HospitalRooms
            .AsNoTracking()
            .Include(r => r.Department)
            .FirstOrDefaultAsync(r => r.Id == preTargetRoom.Id, ct)
            ?? throw new NotFoundException("HospitalRoom", preTargetRoom.Id);

        if (!string.Equals(targetRoom.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException("Phòng khám đích hiện không hoạt động hoặc đang bảo trì.");
        }

        var activeVisitCountByRoomId = await GetActiveVisitCountByRoomAsync(visit.Id, ct);
        var targetCapacity = targetRoom.Capacity is > 0 ? targetRoom.Capacity.Value : 1;
        var targetActiveVisits = activeVisitCountByRoomId.GetValueOrDefault(targetRoom.Id, 0);
        if (targetActiveVisits >= targetCapacity)
        {
            throw new ConflictException("Phòng khám đích đã đạt sức chứa tối đa cho các lượt khám đang hoạt động.");
        }

        var effectiveDoctor = await ResolveTargetRoomDoctorAsync(
            request.TargetDoctorId,
            targetRoom,
            context.VisitHospitalId,
            context.Appointment?.Slot?.ServiceId,
            nowLocal,
            ct);

        var previousRoomId = currentRoom.Id;
        var previousRoomCode = currentRoom.RoomCode;
        var previousRoomName = currentRoom.Name;
        var previousDoctorId = visit.AssignedDoctorId;
        var previousVisitStatus = visit.Status;
        var previousSpId = context.ActiveQueueEntry?.CurrentServicePointId ?? context.ActiveQueueEntry?.ServicePointId;

        var targetSpId = await ResolveTargetServicePointIdAsync(
            targetRoom.Id,
            effectiveDoctor?.Id,
            context.VisitHospitalId,
            ct);

        var normalizedVisitStatus = AppointmentBookingRules.NormalizeStatusKey(visit.Status);
        visit.ExaminationRoomId = targetRoom.Id;
        visit.AssignedDoctorId = effectiveDoctor?.Id;

        if (string.IsNullOrWhiteSpace(visit.Status) ||
            string.Equals(normalizedVisitStatus, "CALLED", StringComparison.OrdinalIgnoreCase))
        {
            visit.Status = "WAITING";
        }

        if (context.ActiveQueueEntry is not null)
        {
            var normalizedQueueStatus = AppointmentBookingRules.NormalizeStatusKey(context.ActiveQueueEntry.Status);
            context.ActiveQueueEntry.CurrentServicePointId = targetSpId;
            if (string.Equals(normalizedQueueStatus, "CALLED", StringComparison.OrdinalIgnoreCase))
            {
                context.ActiveQueueEntry.Status = "WAITING";
                context.ActiveQueueEntry.CalledAt = null;
            }

            context.ActiveQueueEntry.StateVersion = (context.ActiveQueueEntry.StateVersion is > 0
                ? context.ActiveQueueEntry.StateVersion.Value
                : 1) + 1;
        }

        var resolvedDepartment = targetDepartment ?? context.Department;
        var auditPayload = new VisitRoomTransferAuditPayload(
            VisitId: visit.Id,
            VisitCode: visit.VisitCode ?? $"VIS-{visit.Id}",
            PatientId: visit.PatientId ?? context.Patient?.Id,
            FromRoomId: previousRoomId,
            FromRoomCode: previousRoomCode,
            FromRoomName: previousRoomName,
            ToRoomId: targetRoom.Id,
            ToRoomCode: targetRoom.RoomCode,
            ToRoomName: targetRoom.Name,
            DepartmentId: resolvedDepartment?.Id ?? targetRoom.DepartmentId,
            DepartmentCode: resolvedDepartment?.DepartmentCode,
            DepartmentName: resolvedDepartment?.Name,
            PreviousDoctorId: previousDoctorId,
            AssignedDoctorId: visit.AssignedDoctorId,
            PreviousVisitStatus: previousVisitStatus,
            UpdatedVisitStatus: visit.Status,
            QueueEntryId: context.ActiveQueueEntry?.Id,
            QueueNumber: context.ActiveQueueEntry?.QueueNumber,
            Reason: trimmedReason,
            Notes: trimmedNotes,
            TransferredByUserId: actorUserId,
            TransferredByStaffCode: operatorAccess.ActorStaff?.StaffCode,
            TransferredByName: operatorAccess.ActorStaff?.FullName ?? operatorAccess.ActorUser.Email,
            TransferredAtUtc: nowUtc);

        var changeDataJson = JsonSerializer.Serialize(auditPayload, JsonOptions);

        _db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actorUserId,
            HospitalId = context.VisitHospitalId,
            Action = VisitRoomTransferAction,
            EntityType = VisitEntityType,
            EntityId = visit.Id.ToString(CultureInfo.InvariantCulture),
            Reason = trimmedReason,
            ChangeData = changeDataJson,
            IpAddress = !string.IsNullOrWhiteSpace(ipAddress) && IPAddress.TryParse(ipAddress, out var parsedIp)
                ? parsedIp
                : null,
            OccurredAt = nowUtc,
        });

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        if (visit.AppointmentId is long transferredApptId)
        {
            await MedFlow.Application.Services.Realtime.RealtimeRevokeSafeExtensions.TryMigrateAppointmentBucketSafeAsync(
                _accessRevoker,
                transferredApptId,
                targetSpId,
                targetRoom.Id,
                ct);
        }

        await MedFlow.Application.Services.Realtime.RealtimePublishSafeExtensions.TryPublishQueueChangedSafeAsync(
            _realtimePublisher,
            new MedFlow.Application.DTOs.Realtime.QueueChangedNotification(
                HospitalId: context.VisitHospitalId,
                ServicePointId: previousSpId,
                RoomId: previousRoomId,
                AppointmentId: visit.AppointmentId),
            ct);

        await MedFlow.Application.Services.Realtime.RealtimePublishSafeExtensions.TryPublishQueueChangedSafeAsync(
            _realtimePublisher,
            new MedFlow.Application.DTOs.Realtime.QueueChangedNotification(
                HospitalId: context.VisitHospitalId,
                ServicePointId: targetSpId,
                RoomId: targetRoom.Id,
                AppointmentId: visit.AppointmentId),
            ct);

        var transferHistory = await LoadTransferHistoryAsync(visit.Id, ct);

        return new VisitRoomTransferResponseDto(
            VisitId: visit.Id,
            VisitCode: visit.VisitCode ?? $"VIS-{visit.Id}",
            PatientId: visit.PatientId ?? context.Patient?.Id,
            PatientCode: context.Patient?.PatientCode,
            PatientName: context.Patient?.FullName,
            VisitStatus: visit.Status ?? "WAITING",
            PreviousRoomId: previousRoomId,
            PreviousRoomCode: previousRoomCode,
            PreviousRoomName: previousRoomName,
            CurrentRoomId: targetRoom.Id,
            CurrentRoomCode: targetRoom.RoomCode,
            CurrentRoomName: targetRoom.Name,
            DepartmentId: resolvedDepartment?.Id ?? targetRoom.DepartmentId,
            DepartmentCode: resolvedDepartment?.DepartmentCode,
            DepartmentName: resolvedDepartment?.Name,
            PreviousDoctorId: previousDoctorId,
            AssignedDoctorId: visit.AssignedDoctorId,
            AssignedDoctorCode: effectiveDoctor?.DoctorCode,
            AssignedDoctorName: effectiveDoctor?.Staff?.FullName,
            QueueNumber: context.ActiveQueueEntry?.QueueNumber,
            QueueStatus: context.ActiveQueueEntry?.Status,
            TransferReason: trimmedReason,
            Notes: trimmedNotes,
            TransferredByUserId: actorUserId,
            TransferredByStaffCode: operatorAccess.ActorStaff?.StaffCode,
            TransferredByName: operatorAccess.ActorStaff?.FullName ?? operatorAccess.ActorUser.Email,
            TransferredAtUtc: nowUtc,
            TransferHistory: transferHistory);
    }

    private static (long? Id, string? Code) ParseVisitIdentifier(string? idOrCode)
    {
        if (string.IsNullOrWhiteSpace(idOrCode))
        {
            throw new ValidationException("Mã định danh lượt khám không hợp lệ.");
        }

        var trimmed = idOrCode.Trim();
        if (trimmed.StartsWith('#'))
        {
            trimmed = trimmed[1..].TrimStart();
        }

        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > 40)
        {
            throw new ValidationException("Mã định danh lượt khám không hợp lệ.");
        }

        if (trimmed.All(char.IsAsciiDigit) ||
            (trimmed.StartsWith('-') && trimmed.Length > 1 && trimmed[1..].All(char.IsAsciiDigit)))
        {
            if (!long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numericId) ||
                numericId <= 0)
            {
                throw new ValidationException("ID lượt khám phải lớn hơn 0.");
            }

            return (numericId, null);
        }

        if (trimmed.StartsWith('-') ||
            !trimmed.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '-' || ch == '_'))
        {
            throw new ValidationException("Mã định danh lượt khám không hợp lệ.");
        }

        return (null, trimmed.ToUpperInvariant());
    }

    private async Task<OperatorAccessContext> EnsureOperatorAccessAsync(
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
            throw new ForbiddenException("Tài khoản người dùng không ở trạng thái hoạt động.");
        }

        if (actorUser.LockedUntil.HasValue && actorUser.LockedUntil.Value > nowUtc)
        {
            throw new ForbiddenException("Tài khoản người dùng đang bị tạm khóa.");
        }

        var actorRoleIds = await _db.UserRoles
            .AsNoTracking()
            .Where(ur => ur.UserId == actorUserId)
            .Select(ur => ur.RoleId)
            .ToListAsync(ct);

        var actorRoleCodes = await _db.Roles
            .AsNoTracking()
            .Where(r => actorRoleIds.Contains(r.Id))
            .Select(r => r.Code)
            .ToListAsync(ct);

        var hasAllowedRole = actorRoleCodes.Any(code =>
            AllowedOperatorRoles.Any(allowed => string.Equals(code, allowed, StringComparison.OrdinalIgnoreCase)));

        if (!hasAllowedRole)
        {
            throw new ForbiddenException("Người dùng không có quyền điều chuyển phòng khám.");
        }

        var isSystemAdmin = actorRoleCodes.Any(code =>
            string.Equals(code, RoleNames.SystemAdmin, StringComparison.OrdinalIgnoreCase));

        var rolePermissionIds = actorRoleIds.Count > 0
            ? await _db.RolePermissions
                .AsNoTracking()
                .Where(rp => actorRoleIds.Contains(rp.RoleId))
                .Select(rp => rp.PermissionId)
                .Distinct()
                .ToListAsync(ct)
            : [];

        if (rolePermissionIds.Count == 0)
        {
            throw new ForbiddenException("Vai trò hiện tại chưa được cấp quyền điều chuyển phòng khám (VISIT_ASSIGN).");
        }

        var permissionCodes = await _db.Permissions
            .AsNoTracking()
            .Where(p => rolePermissionIds.Contains(p.Id))
            .Select(p => p.Code)
            .ToListAsync(ct);

        var hasPermission = permissionCodes.Any(code =>
            AllowedPermissionCodes.Any(allowed =>
                string.Equals(code, allowed, StringComparison.OrdinalIgnoreCase)));

        if (!hasPermission)
        {
            throw new ForbiddenException("Vai trò hiện tại chưa được cấp quyền điều chuyển phòng khám (VISIT_ASSIGN).");
        }

        var actorStaff = await _db.Staff
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == actorUserId, ct);

        if (actorStaff is not null &&
            !string.Equals(actorStaff.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            throw new ForbiddenException("Hồ sơ nhân sự không ở trạng thái hoạt động.");
        }

        if (!isSystemAdmin)
        {
            if (actorStaff is null || !actorStaff.HospitalId.HasValue)
            {
                throw new ForbiddenException("Tài khoản nhân sự chưa được gán cơ sở y tế.");
            }

            var hospital = await _db.Hospitals
                .AsNoTracking()
                .FirstOrDefaultAsync(h => h.Id == actorStaff.HospitalId.Value, ct);

            if (hospital is not null && hospital.IsActive == false)
            {
                throw new ForbiddenException("Cơ sở y tế của nhân sự đang ngừng hoạt động.");
            }
        }

        return new OperatorAccessContext(
            IsSystemAdmin: isSystemAdmin,
            ActorHospitalId: actorStaff?.HospitalId,
            ActorStaff: actorStaff,
            ActorUser: actorUser);
    }

    private async Task<VisitContext> LoadVisitContextAsync(
        Visit visit,
        OperatorAccessContext operatorAccess,
        CancellationToken ct,
        bool trackQueueEntry = false)
    {
        var currentRoom = visit.ExaminationRoomId.HasValue
            ? await _db.HospitalRooms
                .AsNoTracking()
                .Include(r => r.Department)
                .FirstOrDefaultAsync(r => r.Id == visit.ExaminationRoomId.Value, ct)
            : null;

        var appointment = visit.AppointmentId.HasValue
            ? await _db.Appointments
                .AsNoTracking()
                .Include(a => a.Slot)
                    .ThenInclude(s => s!.Service)
                .Include(a => a.Slot)
                    .ThenInclude(s => s!.Doctor)
                .FirstOrDefaultAsync(a => a.Id == visit.AppointmentId.Value, ct)
            : null;

        var resolvedDoctorId = visit.AssignedDoctorId ?? appointment?.Slot?.DoctorId;
        var assignedDoctor = resolvedDoctorId.HasValue
            ? await _db.Doctors
                .AsNoTracking()
                .Include(d => d.Staff)
                    .ThenInclude(s => s!.Department)
                .Include(d => d.Specialty)
                .FirstOrDefaultAsync(d => d.Id == resolvedDoctorId.Value, ct)
            : null;

        var checkedInStaff = visit.CheckedInByStaffId.HasValue
            ? await _db.Staff
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == visit.CheckedInByStaffId.Value, ct)
            : null;

        var visitHospitalId = currentRoom?.HospitalId
            ?? currentRoom?.Department?.HospitalId
            ?? assignedDoctor?.HospitalId
            ?? assignedDoctor?.Staff?.HospitalId
            ?? appointment?.Slot?.Service?.HospitalId
            ?? checkedInStaff?.HospitalId
            ?? operatorAccess.ActorHospitalId
            ?? throw new ValidationException("Không xác định được cơ sở y tế của lượt khám.");

        if (!operatorAccess.IsSystemAdmin && visitHospitalId != operatorAccess.ActorHospitalId!.Value)
        {
            throw new ForbiddenException("Bạn không có quyền thao tác trên lượt khám thuộc cơ sở y tế khác.");
        }

        var patientId = visit.PatientId ?? appointment?.PatientId;
        var patient = patientId.HasValue
            ? await _db.Patients
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == patientId.Value, ct)
            : null;

        var requiredDepartmentId = currentRoom?.DepartmentId
            ?? appointment?.Slot?.Service?.DepartmentId
            ?? assignedDoctor?.Staff?.DepartmentId;

        var department = currentRoom?.Department
            ?? assignedDoctor?.Staff?.Department
            ?? (requiredDepartmentId.HasValue
                ? await _db.Departments
                    .AsNoTracking()
                    .FirstOrDefaultAsync(d => d.Id == requiredDepartmentId.Value, ct)
                : null);

        var queueQuery = trackQueueEntry
            ? _db.QueueEntries.AsQueryable()
            : _db.QueueEntries.AsNoTracking();

        var queueEntries = await queueQuery
            .Where(q => q.VisitId == visit.Id ||
                        (visit.AppointmentId.HasValue && q.AppointmentId == visit.AppointmentId.Value))
            .OrderByDescending(q => q.Id)
            .ToListAsync(ct);

        var activeQueueEntry = queueEntries.FirstOrDefault(q =>
            ClinicalQueueRules.IsClinicalExaminationQueueType(q.QueueType) &&
            AppointmentBookingRules.NormalizeStatusKey(q.Status) is not ("CANCELLED" or "COMPLETED"));

        return new VisitContext(
            VisitHospitalId: visitHospitalId,
            RequiredDepartmentId: requiredDepartmentId,
            CurrentRoom: currentRoom,
            Department: department,
            Appointment: appointment,
            AssignedDoctor: assignedDoctor,
            Patient: patient,
            ActiveQueueEntry: activeQueueEntry);
    }

    private static void EnsureVisitTransferable(Visit visit, QueueEntry? activeQueueEntry)
    {
        var normalizedVisitStatus = AppointmentBookingRules.NormalizeStatusKey(visit.Status);
        if (normalizedVisitStatus is "COMPLETED" or "CANCELLED" or "NO_SHOW" or "DONE" || visit.CompletedAt.HasValue)
        {
            throw new ConflictException("Không thể chuyển phòng cho lượt khám đã hoàn tất hoặc đã hủy.");
        }

        if (normalizedVisitStatus is "IN_CONSULTATION" or "IN_PROGRESS")
        {
            throw new ConflictException("Bệnh nhân đang trong quá trình khám lâm sàng, không thể chuyển phòng.");
        }

        if (activeQueueEntry is not null)
        {
            var normalizedQueueStatus = AppointmentBookingRules.NormalizeStatusKey(activeQueueEntry.Status);
            if ((activeQueueEntry.StartedAt.HasValue && !activeQueueEntry.CompletedAt.HasValue && normalizedQueueStatus != "WAITING") ||
                normalizedQueueStatus is "IN_CONSULTATION" or "IN_PROGRESS")
            {
                throw new ConflictException("Bệnh nhân đã bắt đầu khám tại phòng hiện tại, không thể chuyển phòng.");
            }
        }
    }

    private async Task<Doctor?> ResolveTargetRoomDoctorAsync(
        long? explicitTargetDoctorId,
        HospitalRoom targetRoom,
        long visitHospitalId,
        long? requiredServiceId,
        DateTime nowLocal,
        CancellationToken ct)
    {
        if (explicitTargetDoctorId.HasValue)
        {
            var targetDoctor = await _db.Doctors
                .AsNoTracking()
                .Include(d => d.Staff)
                    .ThenInclude(s => s!.Department)
                .Include(d => d.Specialty)
                .FirstOrDefaultAsync(d => d.Id == explicitTargetDoctorId.Value, ct)
                ?? throw new NotFoundException("Doctor", explicitTargetDoctorId.Value);

            var doctorHospitalId = targetDoctor.HospitalId ?? targetDoctor.Staff?.HospitalId;
            if (!doctorHospitalId.HasValue || doctorHospitalId.Value != visitHospitalId)
            {
                throw new ForbiddenException("Bác sĩ được chọn không thuộc cơ sở y tế của lượt khám.");
            }

            if (!targetDoctor.IsActive ||
                (targetDoctor.Staff is not null &&
                 !string.Equals(targetDoctor.Staff.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase)))
            {
                throw new ConflictException("Bác sĩ được chọn hiện không ở trạng thái hoạt động.");
            }

            if (targetRoom.DepartmentId.HasValue &&
                targetDoctor.Staff?.DepartmentId is long docDeptId &&
                docDeptId != targetRoom.DepartmentId.Value)
            {
                throw new ValidationException("Bác sĩ được chọn không thuộc cùng khoa phòng với buồng khám đích.");
            }

            if (requiredServiceId.HasValue)
            {
                var hasAnyAuth = await _db.DoctorServiceAuthorizations
                    .AsNoTracking()
                    .AnyAsync(a => a.DoctorId == targetDoctor.Id, ct);

                if (hasAnyAuth)
                {
                    var isAuthorized = await _db.DoctorServiceAuthorizations
                        .AsNoTracking()
                        .AnyAsync(
                            a => a.DoctorId == targetDoctor.Id &&
                                 a.ServiceId == requiredServiceId.Value &&
                                 a.IsActive == true,
                            ct);

                    if (!isAuthorized)
                    {
                        throw new ValidationException("Bác sĩ được chọn chưa được phân quyền thực hiện dịch vụ khám của lượt khám này.");
                    }
                }
            }

            return targetDoctor;
        }

        var byRoom = await ResolveActiveDoctorsByRoomAsync(
            visitHospitalId,
            new HashSet<long> { targetRoom.Id },
            targetRoom.DepartmentId,
            requiredServiceId,
            nowLocal,
            ct);

        var roomDoctors = byRoom.GetValueOrDefault(targetRoom.Id, []);
        return roomDoctors.Count == 1 ? roomDoctors[0] : null;
    }

    private async Task<Dictionary<long, List<Doctor>>> ResolveActiveDoctorsByRoomAsync(
        long hospitalId,
        IReadOnlySet<long> roomIds,
        long? requiredDepartmentId,
        long? requiredServiceId,
        DateTime nowLocal,
        CancellationToken ct)
    {
        var result = roomIds.ToDictionary(id => id, _ => new List<Doctor>());
        if (roomIds.Count == 0)
        {
            return result;
        }

        var today = DateOnly.FromDateTime(nowLocal);
        var dayStart = today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var dayEnd = dayStart.AddDays(1);
        var currentShift = HospitalShiftRules.ResolveShift(nowLocal);

        var spMappings = new List<(long RoomId, long DoctorId)>();
        try
        {
            var points = await _db.QueueServicePoints
                .AsNoTracking()
                .Where(sp =>
                    sp.IsActive != false &&
                    sp.RoomId.HasValue &&
                    roomIds.Contains(sp.RoomId.Value) &&
                    sp.DoctorId.HasValue &&
                    (!sp.HospitalId.HasValue || sp.HospitalId.Value == hospitalId) &&
                    (sp.PointType == null ||
                     (sp.PointType.ToUpper() != "DIAGNOSTIC" &&
                      sp.PointType.ToUpper() != "RECEPTION" &&
                      sp.PointType.ToUpper() != "PHARMACY")))
                .ToListAsync(ct);

            foreach (var p in points)
            {
                spMappings.Add((p.RoomId!.Value, p.DoctorId!.Value));
            }
        }
        catch (NotSupportedException)
        {
            // Ignore when QueueServicePoints DbSet is not configured in lightweight test double.
        }

        var todaySlots = await _db.ExaminationSlots
            .AsNoTracking()
            .Where(s =>
                s.RoomId.HasValue &&
                roomIds.Contains(s.RoomId.Value) &&
                s.DoctorId.HasValue &&
                s.StartsAt.HasValue &&
                s.StartsAt.Value >= dayStart &&
                s.StartsAt.Value < dayEnd &&
                (s.Status == null || s.Status.ToUpper() != "CANCELLED"))
            .ToListAsync(ct);

        var candidateDoctorIds = spMappings.Select(m => m.DoctorId)
            .Concat(todaySlots.Select(s => s.DoctorId!.Value))
            .Distinct()
            .ToList();

        if (candidateDoctorIds.Count == 0)
        {
            return result;
        }

        var doctors = await _db.Doctors
            .AsNoTracking()
            .Include(d => d.Staff)
                .ThenInclude(s => s!.Department)
            .Include(d => d.Specialty)
            .Where(d => candidateDoctorIds.Contains(d.Id))
            .ToListAsync(ct);

        HashSet<long>? authorizedDoctorIds = null;
        if (requiredServiceId.HasValue)
        {
            try
            {
                var authRows = await _db.DoctorServiceAuthorizations
                    .AsNoTracking()
                    .Where(a => candidateDoctorIds.Contains(a.DoctorId))
                    .ToListAsync(ct);

                if (authRows.Count > 0)
                {
                    authorizedDoctorIds = candidateDoctorIds
                        .Where(docId =>
                        {
                            var docAuths = authRows.Where(a => a.DoctorId == docId).ToList();
                            return docAuths.Count == 0 || docAuths.Any(a => a.ServiceId == requiredServiceId.Value && a.IsActive == true);
                        })
                        .ToHashSet();
                }
            }
            catch (NotSupportedException)
            {
                authorizedDoctorIds = null;
            }
        }

        var validDoctorsById = doctors
            .Where(d =>
            {
                var docHospId = d.HospitalId ?? d.Staff?.HospitalId;
                if (docHospId.HasValue && docHospId.Value != hospitalId)
                    return false;
                if (!d.IsActive)
                    return false;
                if (d.Staff is not null && !string.Equals(d.Staff.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
                    return false;
                if (requiredDepartmentId.HasValue && d.Staff?.DepartmentId is long deptId && deptId != requiredDepartmentId.Value)
                    return false;
                if (authorizedDoctorIds is not null && !authorizedDoctorIds.Contains(d.Id))
                    return false;
                return true;
            })
            .ToDictionary(d => d.Id);

        foreach (var roomId in roomIds)
        {
            var roomSlots = todaySlots.Where(s => s.RoomId == roomId).ToList();
            var shiftSlots = roomSlots
                .Where(s => HospitalShiftRules.ResolveShift(s.StartsAt!.Value) == currentShift)
                .ToList();
            var effectiveSlots = shiftSlots.Count > 0 ? shiftSlots : roomSlots;

            var doctorIdsForRoom = spMappings
                .Where(m => m.RoomId == roomId)
                .Select(m => m.DoctorId)
                .Concat(effectiveSlots.Select(s => s.DoctorId!.Value))
                .Distinct()
                .OrderBy(id => id)
                .ToList();

            foreach (var docId in doctorIdsForRoom)
            {
                if (validDoctorsById.TryGetValue(docId, out var doc))
                {
                    result[roomId].Add(doc);
                }
            }
        }

        return result;
    }

    private async Task<long?> ResolveTargetServicePointIdAsync(
        long targetRoomId,
        long? targetDoctorId,
        long hospitalId,
        CancellationToken ct)
    {
        try
        {
            var roomPoints = await _db.QueueServicePoints
                .AsNoTracking()
                .Where(sp =>
                    sp.RoomId == targetRoomId &&
                    sp.IsActive != false &&
                    (!sp.HospitalId.HasValue || sp.HospitalId.Value == hospitalId) &&
                    (sp.PointType == null ||
                     (sp.PointType.ToUpper() != "DIAGNOSTIC" &&
                      sp.PointType.ToUpper() != "RECEPTION" &&
                      sp.PointType.ToUpper() != "PHARMACY")))
                .OrderBy(sp => sp.Id)
                .ToListAsync(ct);

            if (roomPoints.Count == 0)
            {
                return null;
            }

            if (targetDoctorId.HasValue)
            {
                var exact = roomPoints.FirstOrDefault(sp => sp.DoctorId == targetDoctorId.Value);
                if (exact is not null)
                {
                    return exact.Id;
                }
            }

            var sharedPoint = roomPoints.FirstOrDefault(sp => !sp.DoctorId.HasValue);
            if (sharedPoint is not null)
            {
                return sharedPoint.Id;
            }

            return targetDoctorId.HasValue || roomPoints.Select(sp => sp.DoctorId).Distinct().Count() <= 1
                ? roomPoints[0].Id
                : null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private async Task<Dictionary<long, int>> GetActiveVisitCountByRoomAsync(
        long currentVisitId,
        CancellationToken ct)
    {
        var openVisits = await _db.Visits
            .AsNoTracking()
            .Where(v => v.Id != currentVisitId &&
                        v.ExaminationRoomId != null &&
                        v.CompletedAt == null)
            .Select(v => new { RoomId = v.ExaminationRoomId!.Value, v.Status })
            .ToListAsync(ct);

        return openVisits
            .Where(v => IsOccupyingRoomStatus(v.Status))
            .GroupBy(v => v.RoomId)
            .ToDictionary(g => g.Key, g => g.Count());
    }

    private static bool IsOccupyingRoomStatus(string? status)
    {
        var normalized = AppointmentBookingRules.NormalizeStatusKey(status);
        return normalized is not ("COMPLETED" or "CANCELLED" or "NO_SHOW" or "DONE");
    }

    private static CompatibleExaminationRoomItemDto MapCompatibleRoomItem(
        HospitalRoom room,
        long? currentRoomId,
        long? requiredDepartmentId,
        IReadOnlyDictionary<long, int> activeVisitCountByRoomId,
        IReadOnlyList<Doctor> roomDoctors)
    {
        var capacity = room.Capacity is > 0 ? room.Capacity.Value : 1;
        var activeCount = activeVisitCountByRoomId.GetValueOrDefault(room.Id, 0);
        var availableCapacity = Math.Max(0, capacity - activeCount);
        var isCurrentRoom = currentRoomId.HasValue && room.Id == currentRoomId.Value;
        var isRoomActive = string.Equals(room.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase);
        var isDeptActive = room.Department?.IsActive ?? true;
        var isCompatible = string.Equals(room.RoomType, "EXAMINATION", StringComparison.OrdinalIgnoreCase) &&
                           (!requiredDepartmentId.HasValue || room.DepartmentId == requiredDepartmentId.Value);

        var isAvailable = isCompatible && !isCurrentRoom && isRoomActive && isDeptActive && activeCount < capacity;

        string? unavailableReason = null;
        if (isCurrentRoom)
        {
            unavailableReason = "Đang là phòng khám hiện tại của bệnh nhân.";
        }
        else if (!isRoomActive)
        {
            unavailableReason = "Phòng khám hiện không hoạt động hoặc đang bảo trì.";
        }
        else if (!isDeptActive)
        {
            unavailableReason = "Khoa phòng đang ngừng hoạt động.";
        }
        else if (activeCount >= capacity)
        {
            unavailableReason = "Phòng khám đã đạt sức chứa tối đa.";
        }

        var singleDoctor = roomDoctors.Count == 1 ? roomDoctors[0] : null;

        return new CompatibleExaminationRoomItemDto(
            RoomId: room.Id,
            RoomCode: room.RoomCode,
            RoomName: room.Name,
            HospitalId: room.HospitalId,
            DepartmentId: room.DepartmentId,
            DepartmentCode: room.Department?.DepartmentCode,
            DepartmentName: room.Department?.Name,
            RoomType: room.RoomType,
            Location: room.Location,
            Capacity: capacity,
            ActiveVisitCount: activeCount,
            AvailableCapacity: availableCapacity,
            Status: room.Status,
            IsCurrentRoom: isCurrentRoom,
            IsCompatible: isCompatible,
            IsAvailable: isAvailable,
            UnavailableReason: unavailableReason,
            DefaultDoctorId: singleDoctor?.Id,
            DefaultDoctorCode: singleDoctor?.DoctorCode,
            DefaultDoctorName: singleDoctor?.Staff?.FullName,
            ActiveDoctorCount: roomDoctors.Count);
    }

    private async Task<IReadOnlyList<VisitRoomTransferHistoryItemDto>> LoadTransferHistoryAsync(
        long visitId,
        CancellationToken ct)
    {
        var visitIdStr = visitId.ToString(CultureInfo.InvariantCulture);
        var auditLogs = await _db.AuditLogs
            .AsNoTracking()
            .Where(a => a.Action == VisitRoomTransferAction && a.EntityId == visitIdStr)
            .OrderByDescending(a => a.OccurredAt)
            .ThenByDescending(a => a.Id)
            .ToListAsync(ct);

        var result = new List<VisitRoomTransferHistoryItemDto>(auditLogs.Count);
        foreach (var log in auditLogs)
        {
            VisitRoomTransferAuditPayload? payload = null;
            if (!string.IsNullOrWhiteSpace(log.ChangeData))
            {
                try
                {
                    payload = JsonSerializer.Deserialize<VisitRoomTransferAuditPayload>(log.ChangeData, JsonOptions);
                }
                catch (JsonException)
                {
                    payload = null;
                }
            }

            result.Add(new VisitRoomTransferHistoryItemDto(
                AuditLogId: log.Id,
                FromRoomId: payload?.FromRoomId,
                FromRoomCode: payload?.FromRoomCode,
                FromRoomName: payload?.FromRoomName,
                ToRoomId: payload?.ToRoomId ?? 0,
                ToRoomCode: payload?.ToRoomCode ?? string.Empty,
                ToRoomName: payload?.ToRoomName ?? string.Empty,
                PreviousDoctorId: payload?.PreviousDoctorId,
                AssignedDoctorId: payload?.AssignedDoctorId,
                Reason: payload?.Reason ?? log.Reason ?? string.Empty,
                Notes: payload?.Notes,
                TransferredByUserId: payload?.TransferredByUserId ?? log.ActorUserId,
                TransferredByStaffCode: payload?.TransferredByStaffCode,
                TransferredByName: payload?.TransferredByName,
                TransferredAtUtc: payload?.TransferredAtUtc ?? log.OccurredAt));
        }

        return result;
    }

    private sealed record OperatorAccessContext(
        bool IsSystemAdmin,
        long? ActorHospitalId,
        Staff? ActorStaff,
        User ActorUser);

    private sealed record VisitContext(
        long VisitHospitalId,
        long? RequiredDepartmentId,
        HospitalRoom? CurrentRoom,
        Department? Department,
        Appointment? Appointment,
        Doctor? AssignedDoctor,
        Patient? Patient,
        QueueEntry? ActiveQueueEntry);

    private sealed record VisitRoomTransferAuditPayload(
        long VisitId,
        string VisitCode,
        long? PatientId,
        long? FromRoomId,
        string? FromRoomCode,
        string? FromRoomName,
        long ToRoomId,
        string ToRoomCode,
        string ToRoomName,
        long? DepartmentId,
        string? DepartmentCode,
        string? DepartmentName,
        long? PreviousDoctorId,
        long? AssignedDoctorId,
        string? PreviousVisitStatus,
        string? UpdatedVisitStatus,
        long? QueueEntryId,
        int? QueueNumber,
        string Reason,
        string? Notes,
        long? TransferredByUserId,
        string? TransferredByStaffCode,
        string? TransferredByName,
        DateTime TransferredAtUtc);
}
