using MedFlow.Application.DTOs.SystemAdmin;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.SystemAdmin;

public sealed class SystemAdminDashboardService : ISystemAdminDashboardService
{
    private const string SystemAdminRoleCode = "SYSTEM_ADMIN";
    private const string DefaultSystemHospitalName = "Toàn hệ thống MedFlow";

    private static readonly string[] FallbackSystemAdminPermissions =
    [
        "AUDIT_READ",
        "HOSPITAL_CONFIG",
        "USER_MANAGE",
    ];

    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;

    public SystemAdminDashboardService(
        IApplicationDbContext dbContext,
        TimeProvider? timeProvider = null)
    {
        _db = dbContext;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<SystemAdminDashboardResponseDto> GetDashboardAsync(
        long actorUserId,
        GetSystemAdminDashboardRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.RecentAuditLimit is < 1 or > 50)
        {
            throw new ValidationException("Số lượng nhật ký hệ thống hiển thị phải từ 1 đến 50.");
        }

        if (request.HospitalId.HasValue && request.HospitalId.Value <= 0)
        {
            throw new ValidationException("Mã bệnh viện lọc không hợp lệ.");
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        var actorUser = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == actorUserId, ct)
            ?? throw new NotFoundException("User", actorUserId);

        if (actorUser.AccountStatus != AccountStatus.ACTIVE)
        {
            throw new ForbiddenException("Tài khoản quản trị hệ thống không ở trạng thái hoạt động.");
        }

        if (actorUser.LockedUntil.HasValue && actorUser.LockedUntil.Value > nowUtc)
        {
            throw new ForbiddenException("Tài khoản quản trị hệ thống đang bị tạm khóa.");
        }

        var allRoles = await _db.Roles
            .AsNoTracking()
            .OrderBy(r => r.Id)
            .ToListAsync(ct);

        var allUserRoles = await _db.UserRoles
            .AsNoTracking()
            .ToListAsync(ct);

        var actorRoleIds = allUserRoles
            .Where(ur => ur.UserId == actorUserId)
            .Select(ur => ur.RoleId)
            .ToHashSet();

        var actorRoles = allRoles
            .Where(r => actorRoleIds.Contains(r.Id))
            .ToList();

        var actorRoleCodes = actorRoles
            .Select(r => r.Code)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();

        if (!actorRoleCodes.Any(c => string.Equals(c, SystemAdminRoleCode, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ForbiddenException("Người dùng không có quyền truy cập bảng điều khiển Quản trị hệ thống.");
        }

        var actorStaff = await _db.Staff
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == actorUserId, ct);

        if (actorStaff is not null &&
            !string.Equals(actorStaff.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            throw new ForbiddenException("Hồ sơ nhân sự của quản trị viên đã ngừng hoạt động.");
        }

        var allHospitals = await _db.Hospitals
            .AsNoTracking()
            .ToListAsync(ct);

        if (request.HospitalId.HasValue &&
            allHospitals.All(h => h.Id != request.HospitalId.Value))
        {
            throw new NotFoundException("Hospital", request.HospitalId.Value);
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
            actorPermissionCodes = [.. FallbackSystemAdminPermissions];
        }

        var effectiveHospitalId = request.HospitalId ?? actorStaff?.HospitalId;
        var hospitalName = effectiveHospitalId.HasValue
            ? allHospitals.FirstOrDefault(h => h.Id == effectiveHospitalId.Value)?.Name ?? DefaultSystemHospitalName
            : DefaultSystemHospitalName;

        var fullName = !string.IsNullOrWhiteSpace(actorStaff?.FullName)
            ? actorStaff.FullName
            : actorUser.Email;

        var adminProfile = new SystemAdminProfileDto(
            UserId: actorUser.Id,
            Email: actorUser.Email,
            FullName: fullName,
            StaffCode: actorStaff?.StaffCode,
            HospitalId: effectiveHospitalId,
            HospitalName: hospitalName,
            Roles: actorRoleCodes,
            Permissions: actorPermissionCodes);

        var allUsers = await _db.Users
            .AsNoTracking()
            .ToListAsync(ct);

        var usersById = allUsers.ToDictionary(u => u.Id);

        var totalUsers = allUsers.Count;
        var activeUsers = allUsers.Count(u => IsUserActive(u, nowUtc));
        var lockedOrInactiveUsers = totalUsers - activeUsers;

        var staffQuery = _db.Staff.AsNoTracking();
        if (request.HospitalId.HasValue)
        {
            staffQuery = staffQuery.Where(s => s.HospitalId == request.HospitalId.Value);
        }

        var scopedStaff = await staffQuery.ToListAsync(ct);
        var totalStaffAccounts = scopedStaff.Count;
        var activeStaffAccounts = scopedStaff.Count(s =>
            string.Equals(s.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase) &&
            (!s.UserId.HasValue || (usersById.TryGetValue(s.UserId.Value, out var linkedUser) && IsUserActive(linkedUser, nowUtc))));
        var inactiveOrLockedStaffAccounts = totalStaffAccounts - activeStaffAccounts;

        var departmentsQuery = _db.Departments.AsNoTracking();
        if (request.HospitalId.HasValue)
        {
            departmentsQuery = departmentsQuery.Where(d => d.HospitalId == request.HospitalId.Value);
        }

        var scopedDepartments = await departmentsQuery.ToListAsync(ct);
        var activeDepartments = scopedDepartments.Count(d => d.IsActive == true);
        var totalHospitals = allHospitals.Count(h => h.IsActive != false);

        var auditQuery = _db.AuditLogs.AsNoTracking();
        if (request.HospitalId.HasValue)
        {
            auditQuery = auditQuery.Where(a => a.HospitalId == request.HospitalId.Value);
        }

        var since24hUtc = nowUtc.AddHours(-24);
        var totalAuditLogs = await auditQuery.CountAsync(ct);
        var recentAuditCount24h = await auditQuery.CountAsync(a => a.OccurredAt >= since24hUtc, ct);

        var metrics = new SystemAdminMetricsDto(
            TotalUsers: totalUsers,
            ActiveUsers: activeUsers,
            LockedOrInactiveUsers: lockedOrInactiveUsers,
            TotalStaffAccounts: totalStaffAccounts,
            ActiveStaffAccounts: activeStaffAccounts,
            InactiveOrLockedStaffAccounts: inactiveOrLockedStaffAccounts,
            TotalRoles: allRoles.Count,
            TotalPermissions: allPermissions.Count,
            TotalHospitals: totalHospitals,
            ActiveDepartments: activeDepartments,
            TotalAuditLogs: totalAuditLogs,
            RecentAuditCount24h: recentAuditCount24h);

        var allowedModules = BuildAllowedModules(actorPermissionCodes, metrics);
        var roleSummaries = BuildRoleSummaries(
            allRoles,
            allUserRoles,
            usersById,
            allRolePermissions,
            permissionsById,
            nowUtc);

        var recentAuditEntities = await auditQuery
            .OrderByDescending(a => a.OccurredAt)
            .ThenByDescending(a => a.Id)
            .Take(request.RecentAuditLimit)
            .ToListAsync(ct);

        var recentAuditLogs = recentAuditEntities
            .Select(a => new SystemAdminAuditLogItemDto(
                Id: a.Id,
                ActorUserId: a.ActorUserId,
                ActorEmail: a.ActorUserId.HasValue && usersById.TryGetValue(a.ActorUserId.Value, out var actor)
                    ? actor.Email
                    : null,
                HospitalId: a.HospitalId,
                Action: a.Action,
                EntityType: a.EntityType,
                EntityId: a.EntityId,
                Reason: a.Reason,
                IpAddress: a.IpAddress?.ToString(),
                OccurredAt: a.OccurredAt))
            .ToList();

        return new SystemAdminDashboardResponseDto(
            AdminProfile: adminProfile,
            Metrics: metrics,
            AllowedModules: allowedModules,
            RoleSummaries: roleSummaries,
            RecentAuditLogs: recentAuditLogs,
            GeneratedAtUtc: nowUtc);
    }

    private static bool IsUserActive(User user, DateTime nowUtc)
        => user.AccountStatus == AccountStatus.ACTIVE &&
           (!user.LockedUntil.HasValue || user.LockedUntil.Value <= nowUtc);

    private static IReadOnlyList<SystemAdminAllowedModuleDto> BuildAllowedModules(
        IReadOnlyCollection<string> permissionCodes,
        SystemAdminMetricsDto metrics)
    {
        var permissionSet = new HashSet<string>(permissionCodes, StringComparer.OrdinalIgnoreCase);

        var catalog = new SystemAdminAllowedModuleDto[]
        {
            new(
                ModuleCode: "STAFF_ACCOUNTS",
                Title: "Quản lý tài khoản nhân sự",
                Description: "Tạo mới, cập nhật, khóa/mở khóa tài khoản nhân viên y tế và vận hành.",
                Route: "/admin/staff",
                RequiredPermission: "USER_MANAGE",
                BadgeCount: metrics.TotalStaffAccounts),
            new(
                ModuleCode: "ROLES_PERMISSIONS",
                Title: "Vai trò & Phân quyền (RBAC)",
                Description: "Cấu hình danh mục vai trò và gán quyền truy cập chức năng hệ thống.",
                Route: "/admin/roles",
                RequiredPermission: "USER_MANAGE",
                BadgeCount: metrics.TotalRoles),
            new(
                ModuleCode: "HOSPITAL_DEPARTMENTS",
                Title: "Cấu hình Khoa phòng & Cơ sở",
                Description: "Quản lý danh mục khoa phòng, buồng khám và thông số vận hành cơ sở.",
                Route: "/admin/departments",
                RequiredPermission: "HOSPITAL_CONFIG",
                BadgeCount: metrics.ActiveDepartments),
            new(
                ModuleCode: "APPOINTMENT_POLICIES",
                Title: "Chính sách & Tích hợp hệ thống",
                Description: "Thiết lập quy tắc đặt lịch, hàng đợi và cấu hình tích hợp dịch vụ.",
                Route: "/admin/reports",
                RequiredPermission: "HOSPITAL_CONFIG",
                BadgeCount: metrics.TotalHospitals),
            new(
                ModuleCode: "AUDIT_LOGS",
                Title: "Nhật ký kiểm toán (Audit Log)",
                Description: "Tra cứu lịch sử truy cập, xác thực và thay đổi dữ liệu toàn hệ thống.",
                Route: "/admin/audit-logs",
                RequiredPermission: "AUDIT_READ",
                BadgeCount: metrics.TotalAuditLogs),
        };

        return catalog
            .Where(m => permissionSet.Contains(m.RequiredPermission))
            .ToList();
    }

    private static IReadOnlyList<SystemAdminRoleSummaryDto> BuildRoleSummaries(
        IReadOnlyList<Role> roles,
        IReadOnlyList<UserRole> userRoles,
        IReadOnlyDictionary<long, User> usersById,
        IReadOnlyList<RolePermission> rolePermissions,
        IReadOnlyDictionary<long, string> permissionsById,
        DateTime nowUtc)
    {
        var summaries = new List<SystemAdminRoleSummaryDto>(roles.Count);

        foreach (var role in roles)
        {
            var assignedUserIds = userRoles
                .Where(ur => ur.RoleId == role.Id)
                .Select(ur => ur.UserId)
                .Distinct()
                .ToList();

            var activeUserCount = assignedUserIds.Count(uid =>
                usersById.TryGetValue(uid, out var u) && IsUserActive(u, nowUtc));

            var rolePermCodes = rolePermissions
                .Where(rp => rp.RoleId == role.Id && permissionsById.TryGetValue(rp.PermissionId, out _))
                .Select(rp => permissionsById[rp.PermissionId])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(code => code, StringComparer.Ordinal)
                .ToList();

            summaries.Add(new SystemAdminRoleSummaryDto(
                RoleId: role.Id,
                Code: role.Code,
                Name: role.Name,
                AssignedUserCount: assignedUserIds.Count,
                ActiveUserCount: activeUserCount,
                PermissionCount: rolePermCodes.Count,
                Permissions: rolePermCodes));
        }

        return summaries;
    }
}
