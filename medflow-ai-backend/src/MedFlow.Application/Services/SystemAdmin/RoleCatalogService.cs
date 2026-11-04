using System.Globalization;
using MedFlow.Application.DTOs.SystemAdmin;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.SystemAdmin;

public sealed class RoleCatalogService : IRoleCatalogService
{
    private const string SystemAdminRoleCode = "SYSTEM_ADMIN";

    private static readonly HashSet<string> ValidScopes = new(StringComparer.OrdinalIgnoreCase)
    {
        "SYSTEM",
        "CLINICAL",
        "OPERATIONAL",
        "PORTAL",
    };

    private static readonly HashSet<string> ValidStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "ACTIVE",
        "INACTIVE",
    };

    private sealed record PermissionMetadata(string Name, string Module);

    private sealed record RoleCatalogMetadata(
        string Scope,
        bool IsSystemRole,
        string Status,
        string Description,
        string SegregationNote,
        string DefaultUpdatedAt);

    private static readonly Dictionary<string, PermissionMetadata> PermissionCatalog =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["PATIENT_PROFILE_READ"] = new("Xem hồ sơ bệnh nhân", "Hồ sơ & Tiếp đón"),
            ["APPOINTMENT_BOOK"] = new("Đặt lịch khám", "Lịch hẹn"),
            ["PATIENT_CHECKIN"] = new("Tiếp nhận & Check-in", "Tiếp đón"),
            ["VISIT_ASSIGN"] = new("Phân buồng & Điều phối khám", "Tiếp đón & Hàng đợi"),
            ["QUEUE_MANAGE"] = new("Quản lý hàng đợi khám", "Hàng đợi"),
            ["MEDICAL_RECORD_WRITE"] = new("Ghi nhận bệnh án lâm sàng", "Khám lâm sàng"),
            ["DIAGNOSTIC_ORDER"] = new("Chỉ định cận lâm sàng", "Cận lâm sàng"),
            ["DIAGNOSTIC_RESULT_WRITE"] = new("Cập nhật kết quả CLS", "Cận lâm sàng"),
            ["PRESCRIPTION_ISSUE"] = new("Kê đơn thuốc điện tử", "Dược & Đơn thuốc"),
            ["MEDICINE_DISPENSE"] = new("Cấp phát thuốc nhà thuốc", "Dược & Đơn thuốc"),
            ["HOSPITAL_CONFIG"] = new("Cấu hình danh mục bệnh viện", "Quản trị cơ sở"),
            ["USER_MANAGE"] = new("Quản lý tài khoản & vai trò", "Quản trị hệ thống"),
            ["AUDIT_READ"] = new("Tra cứu nhật ký kiểm toán", "An ninh & Kiểm toán"),
            ["PAYMENT_VIEW"] = new("Tra cứu viện phí & thanh toán", "Viện phí"),
        };

    private static readonly Dictionary<string, RoleCatalogMetadata> RoleMetadataCatalog =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["SYSTEM_ADMIN"] = new(
                Scope: "SYSTEM",
                IsSystemRole: true,
                Status: "ACTIVE",
                Description: "Toàn quyền cấu hình kỹ thuật, quản lý tài khoản nhân sự, vai trò RBAC và nhật ký kiểm toán.",
                SegregationNote: "Tách biệt với quyền sửa dữ liệu lâm sàng trực tiếp (SoD).",
                DefaultUpdatedAt: "15/03/2026"),
            ["HOSPITAL_ADMIN"] = new(
                Scope: "SYSTEM",
                IsSystemRole: true,
                Status: "ACTIVE",
                Description: "Quản lý danh mục khoa phòng, buồng khám, dịch vụ, slot khám và điều phối vận hành.",
                SegregationNote: "Không bao gồm quyền cấp phát hoặc thu hồi vai trò SYSTEM_ADMIN.",
                DefaultUpdatedAt: "14/03/2026"),
            ["DOCTOR"] = new(
                Scope: "CLINICAL",
                IsSystemRole: true,
                Status: "ACTIVE",
                Description: "Thực hiện khám bệnh, ghi nhận bệnh án điện tử, chỉ định cận lâm sàng và kê đơn thuốc.",
                SegregationNote: "Chỉ truy cập hồ sơ bệnh nhân thuộc lượt khám được phân công.",
                DefaultUpdatedAt: "12/03/2026"),
            ["RECEPTION"] = new(
                Scope: "OPERATIONAL",
                IsSystemRole: true,
                Status: "ACTIVE",
                Description: "Tiếp nhận bệnh nhân tại quầy, xác thực check-in, phân buồng khám và điều phối hàng đợi.",
                SegregationNote: "Không có quyền xem hoặc chỉnh sửa chẩn đoán lâm sàng chi tiết.",
                DefaultUpdatedAt: "10/03/2026"),
            ["DIAGNOSTIC_STAFF"] = new(
                Scope: "CLINICAL",
                IsSystemRole: true,
                Status: "ACTIVE",
                Description: "Tiếp nhận phiếu chỉ định xét nghiệm / chẩn đoán hình ảnh và trả kết quả CLS.",
                SegregationNote: "Giới hạn trong phạm vi phiếu chỉ định cận lâm sàng được giao.",
                DefaultUpdatedAt: "10/03/2026"),
            ["PHARMACY_STAFF"] = new(
                Scope: "CLINICAL",
                IsSystemRole: true,
                Status: "ACTIVE",
                Description: "Kiểm duyệt đơn thuốc điện tử, đối chiếu viện phí và thực hiện cấp phát thuốc.",
                SegregationNote: "Tách biệt giữa quyền kê đơn (DOCTOR) và quyền cấp phát thuốc (PHARMACY_STAFF).",
                DefaultUpdatedAt: "09/03/2026"),
            ["PATIENT"] = new(
                Scope: "PORTAL",
                IsSystemRole: true,
                Status: "ACTIVE",
                Description: "Tra cứu hồ sơ sức khỏe cá nhân, đặt lịch khám trực tuyến và theo dõi viện phí.",
                SegregationNote: "Chỉ truy cập hồ sơ cá nhân hoặc hồ sơ người thân được ủy quyền.",
                DefaultUpdatedAt: "08/03/2026"),
            ["HEAD_DOCTOR"] = new(
                Scope: "CLINICAL",
                IsSystemRole: false,
                Status: "ACTIVE",
                Description: "Phụ trách chuyên môn khoa, điều phối buồng khám trong khoa và phê duyệt hội chẩn.",
                SegregationNote: "Giới hạn điều phối trong phạm vi chuyên khoa phụ trách.",
                DefaultUpdatedAt: "06/03/2026"),
            ["CLINICAL_NURSE"] = new(
                Scope: "OPERATIONAL",
                IsSystemRole: false,
                Status: "ACTIVE",
                Description: "Hỗ trợ đo sinh hiệu, tiếp nhận bệnh nhân tại buồng khám và quản lý hàng đợi buồng.",
                SegregationNote: "Không được phép ký số đơn thuốc hoặc kết luận bệnh án.",
                DefaultUpdatedAt: "05/03/2026"),
            ["SECURITY_AUDITOR"] = new(
                Scope: "SYSTEM",
                IsSystemRole: false,
                Status: "INACTIVE",
                Description: "Định kỳ rà soát nhật ký truy cập hồ sơ bệnh án và kiểm tra tuân thủ bảo mật.",
                SegregationNote: "Vai trò chỉ đọc (Read-only), kích hoạt theo đợt kiểm toán định kỳ.",
                DefaultUpdatedAt: "01/03/2026"),
        };

    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;

    public RoleCatalogService(
        IApplicationDbContext dbContext,
        TimeProvider? timeProvider = null)
    {
        _db = dbContext;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<RoleListResponseDto> GetRoleListAsync(
        long actorUserId,
        GetRoleListRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Page < 1)
        {
            throw new ValidationException("Trang yêu cầu phải lớn hơn hoặc bằng 1.");
        }

        if (request.PageSize is < 1 or > 100)
        {
            throw new ValidationException("Kích thước trang phải từ 1 đến 100.");
        }

        var scopeFilter = string.IsNullOrWhiteSpace(request.Scope)
            ? null
            : request.Scope.Trim().ToUpperInvariant();

        if (scopeFilter is not null && !ValidScopes.Contains(scopeFilter))
        {
            throw new ValidationException(
                "Nhóm phân hệ vai trò (scope) không hợp lệ. Chỉ chấp nhận SYSTEM, CLINICAL, OPERATIONAL hoặc PORTAL.");
        }

        var statusFilter = string.IsNullOrWhiteSpace(request.Status)
            ? null
            : request.Status.Trim().ToUpperInvariant();

        if (statusFilter is not null && !ValidStatuses.Contains(statusFilter))
        {
            throw new ValidationException(
                "Trạng thái vai trò (status) không hợp lệ. Chỉ chấp nhận ACTIVE hoặc INACTIVE.");
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        await EnsureSystemAdminAccessAsync(actorUserId, nowUtc, ct);

        var (allRoleItems, allPermissionsCount) = await LoadCatalogItemsAsync(nowUtc, ct);

        var metrics = new RoleMetricsDto(
            TotalRoles: allRoleItems.Count,
            ActiveRoles: allRoleItems.Count(r => string.Equals(r.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase)),
            InactiveRoles: allRoleItems.Count(r => string.Equals(r.Status, "INACTIVE", StringComparison.OrdinalIgnoreCase)),
            SystemRoles: allRoleItems.Count(r => r.IsSystemRole),
            CustomRoles: allRoleItems.Count(r => !r.IsSystemRole),
            TotalAssignedAccounts: allRoleItems.Sum(r => r.AssignedUsersCount),
            ActiveAssignedAccounts: allRoleItems.Sum(r => r.ActiveUsersCount),
            TotalPermissions: allPermissionsCount);

        IEnumerable<RoleListItemDto> filteredQuery = allRoleItems;

        var search = !string.IsNullOrWhiteSpace(request.Keyword)
            ? request.Keyword.Trim()
            : request.Query?.Trim();

        if (!string.IsNullOrWhiteSpace(search))
        {
            filteredQuery = filteredQuery.Where(r =>
                r.Code.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                r.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                r.Description.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                r.Id.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                r.Permissions.Any(p =>
                    p.Code.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    p.Name.Contains(search, StringComparison.OrdinalIgnoreCase)));
        }

        if (scopeFilter is not null)
        {
            filteredQuery = filteredQuery.Where(r =>
                r.Scope.Equals(scopeFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (statusFilter is not null)
        {
            filteredQuery = filteredQuery.Where(r =>
                r.Status.Equals(statusFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (request.IsSystemRole.HasValue)
        {
            filteredQuery = filteredQuery.Where(r =>
                r.IsSystemRole == request.IsSystemRole.Value);
        }

        var filtered = filteredQuery.ToList();
        var totalFiltered = filtered.Count;
        var totalPages = totalFiltered == 0
            ? 1
            : (int)Math.Ceiling((double)totalFiltered / request.PageSize);

        var paged = filtered
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToList();

        return new RoleListResponseDto(
            Data: paged,
            Items: paged,
            Total: totalFiltered,
            Page: request.Page,
            PageSize: request.PageSize,
            TotalPages: totalPages,
            Metrics: metrics);
    }

    public async Task<RoleListItemDto> GetRoleByIdAsync(
        long actorUserId,
        long roleId,
        CancellationToken ct = default)
    {
        if (roleId <= 0)
        {
            throw new ValidationException("Mã định danh vai trò không hợp lệ.");
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        await EnsureSystemAdminAccessAsync(actorUserId, nowUtc, ct);

        var roleExists = await _db.Roles
            .AsNoTracking()
            .AnyAsync(r => r.Id == roleId, ct);

        if (!roleExists)
        {
            throw new NotFoundException("Role", roleId);
        }

        var (allRoleItems, _) = await LoadCatalogItemsAsync(nowUtc, ct);

        return allRoleItems.First(r => r.RoleId == roleId);
    }

    private async Task EnsureSystemAdminAccessAsync(
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
            throw new ForbiddenException("Tài khoản quản trị hệ thống không ở trạng thái hoạt động.");
        }

        if (actorUser.LockedUntil.HasValue && actorUser.LockedUntil.Value > nowUtc)
        {
            throw new ForbiddenException("Tài khoản quản trị hệ thống đang bị tạm khóa.");
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

        if (!actorRoleCodes.Any(code =>
                string.Equals(code, SystemAdminRoleCode, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ForbiddenException("Người dùng không có quyền truy cập danh mục vai trò hệ thống.");
        }

        var actorStaff = await _db.Staff
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == actorUserId, ct);

        if (actorStaff is not null &&
            !string.Equals(actorStaff.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            throw new ForbiddenException("Hồ sơ nhân sự của quản trị viên đã ngừng hoạt động.");
        }
    }

    private async Task<(IReadOnlyList<RoleListItemDto> Items, int TotalPermissions)> LoadCatalogItemsAsync(
        DateTime nowUtc,
        CancellationToken ct)
    {
        var roles = await _db.Roles
            .AsNoTracking()
            .OrderBy(r => r.Id)
            .ToListAsync(ct);

        var permissions = await _db.Permissions
            .AsNoTracking()
            .ToListAsync(ct);

        var rolePermissions = await _db.RolePermissions
            .AsNoTracking()
            .ToListAsync(ct);

        var userRoles = await _db.UserRoles
            .AsNoTracking()
            .ToListAsync(ct);

        var users = await _db.Users
            .AsNoTracking()
            .ToListAsync(ct);

        var roleAuditLogs = await _db.AuditLogs
            .AsNoTracking()
            .Where(a => a.EntityType == "Role")
            .ToListAsync(ct);

        var permissionsById = permissions.ToDictionary(p => p.Id);
        var usersById = users.ToDictionary(u => u.Id);

        var items = new List<RoleListItemDto>(roles.Count);

        foreach (var role in roles)
        {
            var metadata = GetRoleMetadata(role);

            var assignedUserIds = userRoles
                .Where(ur => ur.RoleId == role.Id)
                .Select(ur => ur.UserId)
                .Distinct()
                .ToList();

            var activeUsersCount = assignedUserIds.Count(uid =>
                usersById.TryGetValue(uid, out var user) && IsUserActive(user, nowUtc));

            var mappedPermissions = rolePermissions
                .Where(rp => rp.RoleId == role.Id && permissionsById.ContainsKey(rp.PermissionId))
                .Select(rp => permissionsById[rp.PermissionId])
                .GroupBy(p => p.Code, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(p => p.Code, StringComparer.Ordinal)
                .Select(MapPermissionItem)
                .ToList();

            var roleIdString = role.Id.ToString(CultureInfo.InvariantCulture);
            var latestAudit = roleAuditLogs
                .Where(a =>
                    string.Equals(a.EntityId, roleIdString, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(a.EntityId, role.Code, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(a => a.OccurredAt)
                .FirstOrDefault();

            var updatedAt = latestAudit is not null
                ? latestAudit.OccurredAt.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
                : metadata.DefaultUpdatedAt;

            items.Add(new RoleListItemDto(
                RoleId: role.Id,
                Id: $"ROL-{role.Id:D2}",
                Code: role.Code,
                Name: role.Name,
                Description: metadata.Description,
                Scope: metadata.Scope,
                IsSystemRole: metadata.IsSystemRole,
                AssignedUsersCount: assignedUserIds.Count,
                ActiveUsersCount: activeUsersCount,
                PermissionCount: mappedPermissions.Count,
                Permissions: mappedPermissions,
                Status: metadata.Status,
                UpdatedAt: updatedAt,
                SegregationNote: metadata.SegregationNote));
        }

        return (items, permissions.Count);
    }

    private static RoleCatalogMetadata GetRoleMetadata(Role role)
    {
        if (RoleMetadataCatalog.TryGetValue(role.Code, out var metadata))
        {
            return metadata;
        }

        return new RoleCatalogMetadata(
            Scope: "OPERATIONAL",
            IsSystemRole: false,
            Status: "ACTIVE",
            Description: $"Vai trò cấu hình mở rộng: {role.Name}.",
            SegregationNote: "Tuân thủ nguyên tắc phân quyền tối thiểu (Least Privilege).",
            DefaultUpdatedAt: "01/03/2026");
    }

    private static RolePermissionItemDto MapPermissionItem(Permission permission)
    {
        if (PermissionCatalog.TryGetValue(permission.Code, out var meta))
        {
            return new RolePermissionItemDto(
                PermissionId: permission.Id,
                Code: permission.Code,
                Name: meta.Name,
                Module: meta.Module);
        }

        return new RolePermissionItemDto(
            PermissionId: permission.Id,
            Code: permission.Code,
            Name: permission.Code,
            Module: "Hệ thống chung");
    }

    private static bool IsUserActive(User user, DateTime nowUtc)
    {
        if (user.AccountStatus != AccountStatus.ACTIVE)
        {
            return false;
        }

        return !user.LockedUntil.HasValue || user.LockedUntil.Value <= nowUtc;
    }
}
