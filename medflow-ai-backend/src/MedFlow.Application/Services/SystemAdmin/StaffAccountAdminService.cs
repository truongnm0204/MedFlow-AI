using System.Globalization;
using System.Net;
using System.Text.Json;
using MedFlow.Application.Common.Time;
using MedFlow.Application.DTOs.SystemAdmin;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.SystemAdmin;

public sealed class StaffAccountAdminService : IStaffAccountAdminService
{
    private const string DeactivatedAction = "STAFF_ACCOUNT_DEACTIVATED";
    private const string StaffEntityType = "STAFF";

    private static readonly string[] ActiveVisitStatuses =
    [
        "WAITING",
        "CALLED",
        "IN_PROGRESS",
        "IN_CONSULTATION",
    ];

    private static readonly string[] ClosedSlotStatuses =
    [
        "CANCELLED",
        "COMPLETED",
        "INACTIVE",
    ];

    private static readonly JsonSerializerOptions AuditJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;
    private readonly IRealtimeAccessRevoker _accessRevoker;

    public StaffAccountAdminService(
        IApplicationDbContext db,
        TimeProvider? timeProvider = null,
        IRealtimeAccessRevoker? accessRevoker = null)
    {
        _db = db;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _accessRevoker = accessRevoker ?? MedFlow.Application.Services.Realtime.NullRealtimeAccessRevoker.Instance;
    }

    public async Task<DeactivateStaffAccountResponseDto> DeactivateAsync(
        long actorUserId,
        long staffId,
        DeactivateStaffAccountRequest? request,
        string? ipAddress = null,
        CancellationToken ct = default)
    {
        if (staffId <= 0)
        {
            throw new ValidationException("Mã nhân viên không hợp lệ.");
        }

        var reason = request?.Reason?.Trim() ?? string.Empty;
        if (request is null || reason.Length is < 5 or > 500)
        {
            throw new ValidationException("Lý do vô hiệu hóa phải từ 5 đến 500 ký tự.");
        }

        var nowUtc = HospitalClock.GetUtcNow(_timeProvider);
        await EnsureSystemAdminAccessAsync(actorUserId, nowUtc, ct);

        var target = await _db.Staff
            .Include(s => s.User)
            .Include(s => s.Doctor)
            .FirstOrDefaultAsync(s => s.Id == staffId, ct)
            ?? throw new NotFoundException("Staff", staffId);

        if (!target.UserId.HasValue || target.User is null)
        {
            throw new ConflictException("Hồ sơ nhân viên chưa được liên kết với tài khoản người dùng.");
        }

        if (target.UserId.Value == actorUserId)
        {
            throw new ForbiddenException("Quản trị viên không thể tự vô hiệu hóa tài khoản của mình.");
        }

        if (target.User.AccountStatus == AccountStatus.INACTIVE ||
            string.Equals(target.Status, "INACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException("Tài khoản nhân viên đã bị vô hiệu hóa trước đó.");
        }

        if (target.User.AccountStatus != AccountStatus.ACTIVE ||
            !string.Equals(target.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException("Chỉ có thể vô hiệu hóa tài khoản nhân viên đang hoạt động.");
        }

        var activeVisitCount = 0;
        var futureSlotCount = 0;
        var activeServicePointCount = 0;
        if (target.Doctor is not null)
        {
            activeVisitCount = await _db.Visits.CountAsync(
                v => v.AssignedDoctorId == target.Doctor.Id &&
                     v.Status != null &&
                     ActiveVisitStatuses.Contains(v.Status.ToUpper()),
                ct);

            var nowLocal = HospitalClock.GetNow(_timeProvider);
            futureSlotCount = await _db.ExaminationSlots.CountAsync(
                s => s.DoctorId == target.Doctor.Id &&
                     s.StartsAt.HasValue &&
                     s.StartsAt.Value > nowLocal &&
                     !ClosedSlotStatuses.Contains(s.Status.ToUpper()),
                ct);

            activeServicePointCount = await _db.QueueServicePoints.CountAsync(
                sp => sp.DoctorId == target.Doctor.Id && sp.IsActive != false,
                ct);
        }

        if (activeVisitCount > 0 || futureSlotCount > 0 || activeServicePointCount > 0)
        {
            throw new ConflictException(
                $"Không thể vô hiệu hóa: còn {activeVisitCount} lượt khám đang hoạt động, " +
                $"{futureSlotCount} slot khám tương lai và {activeServicePointCount} điểm phục vụ đang hoạt động.");
        }

        var activeSessions = await _db.AuthSessions
            .Where(s => s.UserId == target.UserId.Value && s.RevokedAt == null && s.ExpiresAt > nowUtc)
            .ToListAsync(ct);
        var parsedIp = ParseIp(ipAddress);

        target.User.AccountStatus = AccountStatus.INACTIVE;
        target.Status = "INACTIVE";
        if (target.Doctor is not null)
        {
            target.Doctor.IsActive = false;
        }

        foreach (var session in activeSessions)
        {
            session.RevokedAt = nowUtc;
            session.RevokedByIp = parsedIp?.ToString();
        }

        _db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actorUserId,
            HospitalId = target.HospitalId,
            Action = DeactivatedAction,
            EntityType = StaffEntityType,
            EntityId = target.Id.ToString(CultureInfo.InvariantCulture),
            Reason = reason,
            ChangeData = JsonSerializer.Serialize(new
            {
                staffId = target.Id,
                userId = target.UserId.Value,
                target.StaffCode,
                previousAccountStatus = AccountStatus.ACTIVE.ToString(),
                accountStatus = AccountStatus.INACTIVE.ToString(),
                previousStaffStatus = "ACTIVE",
                staffStatus = target.Status,
                doctorProfileDeactivated = target.Doctor is not null,
                revokedSessionCount = activeSessions.Count,
            }, AuditJsonOptions),
            IpAddress = parsedIp,
            OccurredAt = nowUtc,
        });

        await _db.SaveChangesAsync(ct);

        await MedFlow.Application.Services.Realtime.RealtimeRevokeSafeExtensions.TryRevokeUserConnectionsSafeAsync(
            _accessRevoker,
            target.UserId.Value,
            "STAFF_DEACTIVATED",
            ct);

        return new DeactivateStaffAccountResponseDto(
            target.Id,
            target.UserId.Value,
            target.StaffCode,
            target.User.AccountStatus.ToString(),
            target.Status,
            target.Doctor is not null,
            activeSessions.Count,
            nowUtc);
    }

    private async Task EnsureSystemAdminAccessAsync(
        long actorUserId,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var actor = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == actorUserId, ct)
            ?? throw new NotFoundException("User", actorUserId);

        if (actor.AccountStatus != AccountStatus.ACTIVE)
        {
            throw new ForbiddenException("Tài khoản quản trị hệ thống không ở trạng thái hoạt động.");
        }

        if (actor.LockedUntil.HasValue && actor.LockedUntil.Value > nowUtc)
        {
            throw new ForbiddenException("Tài khoản quản trị hệ thống đang bị tạm khóa.");
        }

        var hasSystemAdminRole = await (
            from userRole in _db.UserRoles.AsNoTracking()
            join role in _db.Roles.AsNoTracking() on userRole.RoleId equals role.Id
            where userRole.UserId == actorUserId && role.Code.ToUpper() == RoleNames.SystemAdmin
            select userRole).AnyAsync(ct);

        if (!hasSystemAdminRole)
        {
            throw new ForbiddenException("Người dùng không có vai trò quản trị hệ thống.");
        }

        var hasUserManagePermission = await (
            from userRole in _db.UserRoles.AsNoTracking()
            join rolePermission in _db.RolePermissions.AsNoTracking() on userRole.RoleId equals rolePermission.RoleId
            join permission in _db.Permissions.AsNoTracking() on rolePermission.PermissionId equals permission.Id
            where userRole.UserId == actorUserId && permission.Code.ToUpper() == PermissionCodes.UserManage
            select userRole).AnyAsync(ct);

        if (!hasUserManagePermission)
        {
            throw new ForbiddenException("Người dùng không có quyền USER_MANAGE.");
        }

        var actorStaffStatus = await _db.Staff
            .AsNoTracking()
            .Where(s => s.UserId == actorUserId)
            .Select(s => s.Status)
            .FirstOrDefaultAsync(ct);

        if (actorStaffStatus is not null &&
            !string.Equals(actorStaffStatus, "ACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            throw new ForbiddenException("Hồ sơ nhân sự của quản trị viên đã ngừng hoạt động.");
        }
    }

    private static IPAddress? ParseIp(string? ipAddress)
        => IPAddress.TryParse(ipAddress, out var parsed) ? parsed : null;
}
