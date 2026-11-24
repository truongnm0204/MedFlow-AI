using System.Globalization;
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

public sealed class StaffAccountListService : IStaffAccountListService
{
    private const string SystemAdminRoleCode = RoleNames.SystemAdmin;
    private const string HospitalAdminRoleCode = RoleNames.HospitalAdmin;
    private const string ActiveStatus = "ACTIVE";
    private const string LockedStatus = "LOCKED";
    private const string DisabledStatus = "DISABLED";
    private const string PendingStatus = "PENDING";

    private static readonly HashSet<string> ValidStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        ActiveStatus,
        LockedStatus,
        DisabledStatus,
        "INACTIVE",
        PendingStatus,
    };

    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;

    public StaffAccountListService(IApplicationDbContext db, TimeProvider? timeProvider = null)
    {
        _db = db;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<StaffAccountListResponseDto> GetStaffAccountsAsync(
        long actorUserId,
        GetStaffAccountListRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);

        var nowUtc = HospitalClock.GetUtcNow(_timeProvider);
        var access = await EnsureAdminAccessAsync(actorUserId, nowUtc, ct);
        var effectiveHospitalId = ResolveHospitalScope(request.HospitalId, access);

        if (effectiveHospitalId.HasValue &&
            !await _db.Hospitals.AsNoTracking().AnyAsync(h => h.Id == effectiveHospitalId.Value, ct))
        {
            throw new NotFoundException("Hospital", effectiveHospitalId.Value);
        }

        var scopedQuery = _db.Staff
            .AsNoTracking()
            .Where(s => s.UserId.HasValue && s.User != null);

        if (effectiveHospitalId.HasValue)
        {
            scopedQuery = scopedQuery.Where(s => s.HospitalId == effectiveHospitalId.Value);
        }

        var metrics = await LoadMetricsAsync(scopedQuery, nowUtc, ct);
        var filteredQuery = ApplyFilters(scopedQuery, request, nowUtc);
        var total = await filteredQuery.CountAsync(ct);

        var staffAccounts = await filteredQuery
            .Include(s => s.User!)
                .ThenInclude(u => u.UserRoles)
                    .ThenInclude(ur => ur.Role)
            .Include(s => s.Department)
            .OrderBy(s => s.StaffCode)
            .ThenBy(s => s.Id)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(ct);

        var hospitalIds = staffAccounts
            .Where(s => s.HospitalId.HasValue)
            .Select(s => s.HospitalId!.Value)
            .Distinct()
            .ToArray();

        var hospitalsById = await _db.Hospitals
            .AsNoTracking()
            .Where(h => hospitalIds.Contains(h.Id))
            .ToDictionaryAsync(h => h.Id, h => h.Name, ct);

        var items = staffAccounts
            .Select(s => MapToDto(s, hospitalsById, nowUtc))
            .ToArray();

        return new StaffAccountListResponseDto(
            Data: items,
            Items: items,
            Total: total,
            Page: request.Page,
            PageSize: request.PageSize,
            TotalPages: total == 0 ? 0 : (int)Math.Ceiling(total / (double)request.PageSize),
            Metrics: metrics);
    }

    private static void ValidateRequest(GetStaffAccountListRequest request)
    {
        if (request.Page < 1)
        {
            throw new ValidationException("Số trang phải lớn hơn hoặc bằng 1.");
        }

        if (request.PageSize is < 1 or > 100)
        {
            throw new ValidationException("Kích thước trang phải từ 1 đến 100.");
        }
        if ((long)(request.Page - 1) * request.PageSize > int.MaxValue)
        {
            throw new ValidationException("Số trang vượt quá phạm vi phân trang được hỗ trợ.");
        }

        if (request.HospitalId is <= 0)
        {
            throw new ValidationException("Mã bệnh viện lọc không hợp lệ.");
        }

        if (request.DepartmentId is <= 0)
        {
            throw new ValidationException("Mã khoa phòng lọc không hợp lệ.");
        }

        var keyword = ResolveKeyword(request);
        if (keyword is { Length: > 200 })
        {
            throw new ValidationException("Từ khóa tìm kiếm không được vượt quá 200 ký tự.");
        }

        if (request.RoleCode?.Trim() is { Length: > 50 })
        {
            throw new ValidationException("Mã vai trò không được vượt quá 50 ký tự.");
        }

        if (!string.IsNullOrWhiteSpace(request.Status) &&
            !ValidStatuses.Contains(request.Status.Trim()))
        {
            throw new ValidationException("Trạng thái tài khoản nhân sự không hợp lệ.");
        }
    }

    private async Task<AdminAccess> EnsureAdminAccessAsync(
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
            throw new ForbiddenException("Tài khoản quản trị không ở trạng thái hoạt động.");
        }

        if (actorUser.LockedUntil.HasValue && actorUser.LockedUntil.Value > nowUtc)
        {
            throw new ForbiddenException("Tài khoản quản trị đang bị tạm khóa.");
        }

        var actorRoleIds = await _db.UserRoles
            .AsNoTracking()
            .Where(ur => ur.UserId == actorUserId)
            .Select(ur => ur.RoleId)
            .ToListAsync(ct);

        var actorRoleCodes = actorRoleIds.Count == 0
            ? []
            : await _db.Roles
                .AsNoTracking()
                .Where(r => actorRoleIds.Contains(r.Id))
                .Select(r => r.Code)
                .ToListAsync(ct);

        var isSystemAdmin = actorRoleCodes.Any(code =>
            string.Equals(code, SystemAdminRoleCode, StringComparison.OrdinalIgnoreCase));
        var isHospitalAdmin = actorRoleCodes.Any(code =>
            string.Equals(code, HospitalAdminRoleCode, StringComparison.OrdinalIgnoreCase));

        if (!isSystemAdmin && !isHospitalAdmin)
        {
            throw new ForbiddenException("Người dùng không có quyền xem danh sách tài khoản nhân sự.");
        }

        var hasAdminListPerm = actorRoleIds.Count > 0 && await (
            from rp in _db.RolePermissions.AsNoTracking()
            join p in _db.Permissions.AsNoTracking() on rp.PermissionId equals p.Id
            where actorRoleIds.Contains(rp.RoleId) &&
                  (p.Code.ToUpper() == PermissionCodes.UserManage || p.Code.ToUpper() == PermissionCodes.HospitalConfig)
            select rp.PermissionId).AnyAsync(ct);

        if (!hasAdminListPerm)
        {
            throw new ForbiddenException("Tài khoản quản trị chưa được cấp quyền xem danh sách tài khoản nhân sự.");
        }

        var actorStaff = await _db.Staff
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == actorUserId, ct);

        if (actorStaff is not null &&
            !string.Equals(actorStaff.Status, ActiveStatus, StringComparison.OrdinalIgnoreCase))
        {
            throw new ForbiddenException("Hồ sơ nhân sự của quản trị viên đã ngừng hoạt động.");
        }

        if (!isSystemAdmin && (actorStaff?.HospitalId is null))
        {
            throw new ForbiddenException("Quản trị viên bệnh viện chưa được gán phạm vi bệnh viện.");
        }

        return new AdminAccess(isSystemAdmin, actorStaff?.HospitalId);
    }

    private static long? ResolveHospitalScope(long? requestedHospitalId, AdminAccess access)
    {
        if (access.IsSystemAdmin)
        {
            return requestedHospitalId;
        }

        if (requestedHospitalId.HasValue && requestedHospitalId != access.HospitalId)
        {
            throw new ForbiddenException("Không được xem tài khoản nhân sự ngoài bệnh viện được phân công.");
        }

        return access.HospitalId;
    }

    private static IQueryable<Staff> ApplyFilters(
        IQueryable<Staff> query,
        GetStaffAccountListRequest request,
        DateTime nowUtc)
    {
        var keyword = ResolveKeyword(request)?.ToLower();
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query = query.Where(s =>
                s.StaffCode.ToLower().Contains(keyword) ||
                s.FullName.ToLower().Contains(keyword) ||
                s.User!.Email.ToLower().Contains(keyword));
        }

        if (request.DepartmentId.HasValue)
        {
            query = query.Where(s => s.DepartmentId == request.DepartmentId.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.RoleCode))
        {
            var roleCode = request.RoleCode.Trim().ToUpper();
            query = query.Where(s => s.User!.UserRoles.Any(ur => ur.Role.Code == roleCode));
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var status = request.Status.Trim().ToUpper();
            if (status == "INACTIVE")
            {
                status = DisabledStatus;
            }

            query = status switch
            {
                ActiveStatus => query.Where(s =>
                    s.User!.AccountStatus == AccountStatus.ACTIVE &&
                    (!s.User.LockedUntil.HasValue || s.User.LockedUntil.Value <= nowUtc) &&
                    s.Status == ActiveStatus),
                LockedStatus => query.Where(s =>
                    s.User!.AccountStatus == AccountStatus.LOCKED ||
                    (s.User.LockedUntil.HasValue && s.User.LockedUntil.Value > nowUtc)),
                DisabledStatus => query.Where(s =>
                    s.User!.AccountStatus != AccountStatus.LOCKED &&
                    (!s.User.LockedUntil.HasValue || s.User.LockedUntil.Value <= nowUtc) &&
                    (s.User.AccountStatus == AccountStatus.INACTIVE || s.Status != ActiveStatus)),
                PendingStatus => query.Where(s =>
                    s.User!.AccountStatus == AccountStatus.PENDING &&
                    (!s.User.LockedUntil.HasValue || s.User.LockedUntil.Value <= nowUtc) &&
                    s.Status == ActiveStatus),
                _ => query,
            };
        }

        return query;
    }

    private static async Task<StaffAccountMetricsDto> LoadMetricsAsync(
        IQueryable<Staff> scopedQuery,
        DateTime nowUtc,
        CancellationToken ct)
    {
        return await scopedQuery
            .GroupBy(_ => 1)
            .Select(group => new StaffAccountMetricsDto(
                group.Count(),
                group.Count(s =>
                    s.User!.AccountStatus == AccountStatus.ACTIVE &&
                    (!s.User.LockedUntil.HasValue || s.User.LockedUntil.Value <= nowUtc) &&
                    s.Status == ActiveStatus),
                group.Count(s =>
                    s.User!.AccountStatus == AccountStatus.LOCKED ||
                    (s.User.LockedUntil.HasValue && s.User.LockedUntil.Value > nowUtc)),
                group.Count(s =>
                    s.User!.AccountStatus != AccountStatus.LOCKED &&
                    (!s.User.LockedUntil.HasValue || s.User.LockedUntil.Value <= nowUtc) &&
                    (s.User.AccountStatus == AccountStatus.INACTIVE || s.Status != ActiveStatus)),
                group.Count(s =>
                    s.User!.AccountStatus == AccountStatus.PENDING &&
                    (!s.User.LockedUntil.HasValue || s.User.LockedUntil.Value <= nowUtc) &&
                    s.Status == ActiveStatus)))
            .FirstOrDefaultAsync(ct)
            ?? new StaffAccountMetricsDto(0, 0, 0, 0, 0);
    }

    private static bool IsLocked(Staff staff, DateTime nowUtc) =>
        staff.User!.AccountStatus == AccountStatus.LOCKED ||
        (staff.User.LockedUntil.HasValue && staff.User.LockedUntil.Value > nowUtc);

    private static StaffAccountListItemDto MapToDto(
        Staff staff,
        IReadOnlyDictionary<long, string> hospitalsById,
        DateTime nowUtc)
    {
        var roles = staff.User!.UserRoles
            .Where(ur => ur.Role is not null)
            .OrderBy(ur => ur.RoleId)
            .Select(ur => new StaffAccountRoleDto(ur.RoleId, ur.Role.Code, ur.Role.Name))
            .ToArray();
        var primaryRole = roles.FirstOrDefault();
        var facility = staff.HospitalId.HasValue && hospitalsById.TryGetValue(staff.HospitalId.Value, out var name)
            ? name
            : string.Empty;

        return new StaffAccountListItemDto(
            StaffId: staff.Id,
            UserId: staff.User.Id,
            Id: staff.Id.ToString(CultureInfo.InvariantCulture),
            Code: staff.StaffCode,
            FullName: staff.FullName,
            Email: staff.User.Email,
            RoleCode: primaryRole?.Code ?? string.Empty,
            Role: primaryRole?.Name ?? string.Empty,
            Roles: roles,
            DepartmentId: staff.DepartmentId,
            Department: staff.Department?.Name ?? string.Empty,
            HospitalId: staff.HospitalId,
            Facility: facility,
            Status: ResolveStatus(staff, nowUtc));
    }

    private static string ResolveStatus(Staff staff, DateTime nowUtc)
    {
        if (IsLocked(staff, nowUtc))
        {
            return LockedStatus;
        }

        if (staff.User!.AccountStatus == AccountStatus.INACTIVE ||
            !string.Equals(staff.Status, ActiveStatus, StringComparison.OrdinalIgnoreCase))
        {
            return DisabledStatus;
        }

        return staff.User.AccountStatus == AccountStatus.PENDING
            ? PendingStatus
            : ActiveStatus;
    }

    private static string? ResolveKeyword(GetStaffAccountListRequest request) =>
        !string.IsNullOrWhiteSpace(request.Keyword)
            ? request.Keyword.Trim()
            : request.Query?.Trim();

    private sealed record AdminAccess(bool IsSystemAdmin, long? HospitalId);
}
