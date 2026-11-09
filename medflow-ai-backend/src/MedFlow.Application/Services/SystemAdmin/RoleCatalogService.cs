using System.Globalization;
using System.Net;
using System.Text.Json;
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
    private const string UserManagePermissionCode = "USER_MANAGE";
    private const string PrescriptionIssuePermissionCode = "PRESCRIPTION_ISSUE";
    private const string MedicineDispensePermissionCode = "MEDICINE_DISPENSE";
    private const string RolePermissionsUpdatedAction = "ROLE_PERMISSIONS_UPDATED";
    private const string RoleUpdatedAction = "ROLE_UPDATED";
    private const int MaxAuditNoteLength = 250;
    private const int MaxRoleNameLength = 120;
    private const int MaxRoleDescriptionLength = 500;
    private const int MaxSegregationNoteLength = 500;

    private static readonly JsonSerializerOptions AuditJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly string[] CanonicalPermissionOrder =
    [
        "PATIENT_PROFILE_READ",
        "APPOINTMENT_BOOK",
        "PATIENT_CHECKIN",
        "VISIT_ASSIGN",
        "QUEUE_MANAGE",
        "MEDICAL_RECORD_WRITE",
        "DIAGNOSTIC_ORDER",
        "DIAGNOSTIC_RESULT_WRITE",
        "PRESCRIPTION_ISSUE",
        "MEDICINE_DISPENSE",
        "HOSPITAL_CONFIG",
        "USER_MANAGE",
        "AUDIT_READ",
        "PAYMENT_VIEW",
    ];
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

    private sealed record RoleAuditMetadataPayload(
        string? Scope = null,
        string? Status = null,
        string? Description = null,
        string? SegregationNote = null);

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

    public async Task<RolePermissionsCatalogResponseDto> GetRolePermissionsCatalogAsync(
        long actorUserId,
        CancellationToken ct = default)
    {
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        await EnsureSystemAdminAccessAsync(actorUserId, nowUtc, ct);

        var (allRoleItems, _) = await LoadCatalogItemsAsync(nowUtc, ct);

        var permissions = await _db.Permissions
            .AsNoTracking()
            .ToListAsync(ct);

        var mappedPermissions = permissions
            .GroupBy(p => p.Code, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(p => GetCanonicalPermissionSortOrder(p.Code))
            .ThenBy(p => p.Code, StringComparer.Ordinal)
            .Select(MapPermissionItem)
            .ToList();

        var metrics = new RoleMetricsDto(
            TotalRoles: allRoleItems.Count,
            ActiveRoles: allRoleItems.Count(r => string.Equals(r.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase)),
            InactiveRoles: allRoleItems.Count(r => string.Equals(r.Status, "INACTIVE", StringComparison.OrdinalIgnoreCase)),
            SystemRoles: allRoleItems.Count(r => r.IsSystemRole),
            CustomRoles: allRoleItems.Count(r => !r.IsSystemRole),
            TotalAssignedAccounts: allRoleItems.Sum(r => r.AssignedUsersCount),
            ActiveAssignedAccounts: allRoleItems.Sum(r => r.ActiveUsersCount),
            TotalPermissions: mappedPermissions.Count);

        return new RolePermissionsCatalogResponseDto(
            Roles: allRoleItems,
            Permissions: mappedPermissions,
            Metrics: metrics);
    }

    public async Task<RoleListItemDto> AssignRolePermissionsAsync(
        long actorUserId,
        string roleIdentifier,
        AssignRolePermissionsRequest request,
        string? ipAddress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (parsedRoleId, parsedRoleCode) = ParseRoleIdentifier(roleIdentifier);

        string? normalizedRequestRoleCode = null;
        if (request.RoleCode is not null)
        {
            if (string.IsNullOrWhiteSpace(request.RoleCode))
            {
                throw new ValidationException("Mã vai trò không hợp lệ.");
            }

            normalizedRequestRoleCode = request.RoleCode.Trim().ToUpperInvariant();
            if (!IsValidRoleCodeToken(normalizedRequestRoleCode))
            {
                throw new ValidationException("Mã vai trò không hợp lệ.");
            }

            if (parsedRoleCode is not null &&
                !string.Equals(parsedRoleCode, normalizedRequestRoleCode, StringComparison.OrdinalIgnoreCase))
            {
                throw new ValidationException("Mã vai trò (roleCode) không khớp với vai trò cần phân quyền.");
            }
        }

        var trimmedNote = string.IsNullOrWhiteSpace(request.Note)
            ? null
            : request.Note.Trim();

        if (trimmedNote is not null && trimmedNote.Length > MaxAuditNoteLength)
        {
            throw new ValidationException("Ghi chú kiểm toán không được vượt quá 250 ký tự.");
        }

        if (request.PermissionCodes is null || request.PermissionCodes.Count == 0)
        {
            throw new ValidationException("Vai trò phải được gán ít nhất 1 quyền hạn hoạt động.");
        }

        var codeSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawCode in request.PermissionCodes)
        {
            if (string.IsNullOrWhiteSpace(rawCode))
            {
                throw new ValidationException("Mã quyền hạn không nằm trong danh mục hệ thống.");
            }

            var candidate = rawCode.Trim().ToUpperInvariant();
            if (!PermissionCatalog.ContainsKey(candidate))
            {
                throw new ValidationException("Mã quyền hạn không nằm trong danh mục hệ thống.");
            }

            codeSet.Add(candidate);
        }

        var normalizedPermissionCodes = codeSet
            .OrderBy(GetCanonicalPermissionSortOrder)
            .ThenBy(c => c, StringComparer.Ordinal)
            .ToList();

        if (codeSet.Contains(PrescriptionIssuePermissionCode) &&
            codeSet.Contains(MedicineDispensePermissionCode))
        {
            throw new ValidationException(
                "Vi phạm nguyên tắc phân tách nhiệm vụ (SoD): Không thể gán đồng thời quyền Kê đơn thuốc (PRESCRIPTION_ISSUE) và Cấp phát thuốc (MEDICINE_DISPENSE) cho cùng một vai trò.");
        }

        if ((string.Equals(parsedRoleCode, SystemAdminRoleCode, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(normalizedRequestRoleCode, SystemAdminRoleCode, StringComparison.OrdinalIgnoreCase)) &&
            !codeSet.Contains(UserManagePermissionCode))
        {
            throw new ValidationException(
                "Vai trò SYSTEM_ADMIN bắt buộc giữ quyền USER_MANAGE để tránh khóa quyền quản trị hệ thống.");
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        var actorHospitalId = await EnsureSystemAdminAccessAsync(actorUserId, nowUtc, ct);

        Role? targetRole;
        if (parsedRoleId.HasValue)
        {
            var roleIdValue = parsedRoleId.Value;
            targetRole = await _db.Roles
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == roleIdValue, ct);
        }
        else
        {
            targetRole = await _db.Roles
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Code.ToUpper() == parsedRoleCode, ct);
        }

        if (targetRole is null)
        {
            throw new NotFoundException("Role", roleIdentifier.Trim());
        }

        if (normalizedRequestRoleCode is not null &&
            !string.Equals(targetRole.Code, normalizedRequestRoleCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException("Mã vai trò (roleCode) không khớp với vai trò cần phân quyền.");
        }

        if (string.Equals(targetRole.Code, SystemAdminRoleCode, StringComparison.OrdinalIgnoreCase) &&
            !codeSet.Contains(UserManagePermissionCode))
        {
            throw new ValidationException(
                "Vai trò SYSTEM_ADMIN bắt buộc giữ quyền USER_MANAGE để tránh khóa quyền quản trị hệ thống.");
        }

        var allPermissions = await _db.Permissions
            .AsNoTracking()
            .ToListAsync(ct);

        var permissionsByCode = allPermissions
            .GroupBy(p => p.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var targetPermissions = new List<Permission>(normalizedPermissionCodes.Count);
        foreach (var code in normalizedPermissionCodes)
        {
            if (!permissionsByCode.TryGetValue(code, out var permEntity))
            {
                throw new ValidationException("Mã quyền hạn không nằm trong danh mục hệ thống.");
            }

            targetPermissions.Add(permEntity);
        }

        var existingRolePermissions = await _db.RolePermissions
            .Where(rp => rp.RoleId == targetRole.Id)
            .ToListAsync(ct);

        var permissionsById = allPermissions.ToDictionary(p => p.Id);
        var previousCodes = existingRolePermissions
            .Where(rp => permissionsById.ContainsKey(rp.PermissionId))
            .Select(rp => permissionsById[rp.PermissionId].Code.ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(GetCanonicalPermissionSortOrder)
            .ThenBy(c => c, StringComparer.Ordinal)
            .ToList();

        var addedCodes = normalizedPermissionCodes
            .Where(c => !previousCodes.Contains(c, StringComparer.OrdinalIgnoreCase))
            .ToList();

        var revokedCodes = previousCodes
            .Where(c => !codeSet.Contains(c))
            .ToList();

        var targetPermissionIds = targetPermissions
            .Select(p => p.Id)
            .ToHashSet();

        var toRemove = existingRolePermissions
            .Where(rp => !targetPermissionIds.Contains(rp.PermissionId))
            .ToList();

        if (toRemove.Count > 0)
        {
            _db.RolePermissions.RemoveRange(toRemove);
        }

        var existingPermissionIds = existingRolePermissions
            .Select(rp => rp.PermissionId)
            .ToHashSet();

        foreach (var perm in targetPermissions)
        {
            if (!existingPermissionIds.Contains(perm.Id))
            {
                _db.RolePermissions.Add(new RolePermission
                {
                    RoleId = targetRole.Id,
                    PermissionId = perm.Id,
                });
            }
        }

        var changePayload = JsonSerializer.Serialize(new
        {
            roleId = targetRole.Id,
            roleCode = targetRole.Code,
            previousPermissions = previousCodes,
            assignedPermissions = normalizedPermissionCodes,
            addedPermissions = addedCodes,
            revokedPermissions = revokedCodes,
            note = trimmedNote,
        });

        _db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actorUserId,
            HospitalId = actorHospitalId,
            Action = RolePermissionsUpdatedAction,
            EntityType = "Role",
            EntityId = targetRole.Id.ToString(CultureInfo.InvariantCulture),
            Reason = trimmedNote,
            ChangeData = changePayload,
            IpAddress = !string.IsNullOrWhiteSpace(ipAddress) && IPAddress.TryParse(ipAddress, out var parsedIp)
                ? parsedIp
                : null,
            OccurredAt = nowUtc,
        });

        await _db.SaveChangesAsync(ct);

        var (allRoleItems, _) = await LoadCatalogItemsAsync(nowUtc, ct);
        return allRoleItems.First(r => r.RoleId == targetRole.Id);
    }

    public async Task<RoleListItemDto> UpdateRoleAsync(
        long actorUserId,
        string roleIdentifier,
        UpdateRoleRequest request,
        string? ipAddress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (parsedRoleId, parsedRoleCode) = ParseRoleIdentifier(roleIdentifier);

        if (request.Code is null &&
            request.RoleCode is null &&
            request.Name is null &&
            request.RoleName is null &&
            request.Description is null &&
            request.Scope is null &&
            request.Status is null &&
            !request.IsActive.HasValue &&
            request.SegregationNote is null &&
            request.PermissionCodes is null)
        {
            throw new ValidationException("Yêu cầu cập nhật vai trò phải cung cấp ít nhất một thông tin thay đổi.");
        }

        if ((request.Code is not null && string.IsNullOrWhiteSpace(request.Code)) ||
            (request.RoleCode is not null && string.IsNullOrWhiteSpace(request.RoleCode)))
        {
            throw new ValidationException("Mã vai trò không hợp lệ.");
        }

        if (!string.IsNullOrWhiteSpace(request.Code) &&
            !string.IsNullOrWhiteSpace(request.RoleCode) &&
            !string.Equals(request.Code.Trim(), request.RoleCode.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException("Mã vai trò (code và roleCode) không đồng nhất.");
        }

        string? normalizedRequestRoleCode = null;
        var rawCode = !string.IsNullOrWhiteSpace(request.Code) ? request.Code : request.RoleCode;
        if (rawCode is not null)
        {
            normalizedRequestRoleCode = rawCode.Trim().ToUpperInvariant();
            if (!IsValidRoleCodeToken(normalizedRequestRoleCode))
            {
                throw new ValidationException(
                    "Mã vai trò không hợp lệ. Mã phải từ 2 đến 50 ký tự, bắt đầu bằng chữ cái và chỉ chứa chữ cái không dấu, chữ số hoặc dấu gạch dưới (_).");
            }
        }

        if ((request.Name is not null && string.IsNullOrWhiteSpace(request.Name)) ||
            (request.RoleName is not null && string.IsNullOrWhiteSpace(request.RoleName)))
        {
            throw new ValidationException("Tên vai trò không được để trống.");
        }

        if (!string.IsNullOrWhiteSpace(request.Name) &&
            !string.IsNullOrWhiteSpace(request.RoleName) &&
            !string.Equals(request.Name.Trim(), request.RoleName.Trim(), StringComparison.Ordinal))
        {
            throw new ValidationException("Tên vai trò (name và roleName) không đồng nhất.");
        }

        string? normalizedName = null;
        var rawName = !string.IsNullOrWhiteSpace(request.Name) ? request.Name : request.RoleName;
        if (rawName is not null)
        {
            normalizedName = rawName.Trim();
            if (normalizedName.Length is < 2 or > MaxRoleNameLength)
            {
                throw new ValidationException("Tên vai trò phải từ 2 đến 120 ký tự.");
            }
        }

        string? requestedScope = null;
        if (request.Scope is not null)
        {
            if (string.IsNullOrWhiteSpace(request.Scope))
            {
                throw new ValidationException(
                    "Nhóm phân hệ vai trò (scope) không hợp lệ. Chỉ chấp nhận SYSTEM, CLINICAL, OPERATIONAL hoặc PORTAL.");
            }

            requestedScope = request.Scope.Trim().ToUpperInvariant();
            if (!ValidScopes.Contains(requestedScope))
            {
                throw new ValidationException(
                    "Nhóm phân hệ vai trò (scope) không hợp lệ. Chỉ chấp nhận SYSTEM, CLINICAL, OPERATIONAL hoặc PORTAL.");
            }
        }

        string? requestedStatus = null;
        if (request.Status is not null)
        {
            if (string.IsNullOrWhiteSpace(request.Status))
            {
                throw new ValidationException(
                    "Trạng thái vai trò (status) không hợp lệ. Chỉ chấp nhận ACTIVE hoặc INACTIVE.");
            }

            var candidateStatus = request.Status.Trim().ToUpperInvariant();
            if (!ValidStatuses.Contains(candidateStatus))
            {
                throw new ValidationException(
                    "Trạng thái vai trò (status) không hợp lệ. Chỉ chấp nhận ACTIVE hoặc INACTIVE.");
            }

            requestedStatus = candidateStatus;
        }

        if (request.IsActive.HasValue)
        {
            var statusFromBool = request.IsActive.Value ? "ACTIVE" : "INACTIVE";
            if (requestedStatus is not null &&
                !string.Equals(requestedStatus, statusFromBool, StringComparison.OrdinalIgnoreCase))
            {
                throw new ValidationException("Trạng thái vai trò (status và isActive) không đồng nhất.");
            }

            requestedStatus ??= statusFromBool;
        }

        string? trimmedDescription = null;
        if (request.Description is not null)
        {
            if (string.IsNullOrWhiteSpace(request.Description))
            {
                throw new ValidationException("Mô tả vai trò không được để trống.");
            }

            trimmedDescription = request.Description.Trim();
            if (trimmedDescription.Length > MaxRoleDescriptionLength)
            {
                throw new ValidationException("Mô tả vai trò không được vượt quá 500 ký tự.");
            }
        }

        string? trimmedSegregationNote = null;
        if (request.SegregationNote is not null)
        {
            if (string.IsNullOrWhiteSpace(request.SegregationNote))
            {
                throw new ValidationException("Ghi chú phân tách nhiệm vụ (SoD) không được để trống.");
            }

            trimmedSegregationNote = request.SegregationNote.Trim();
            if (trimmedSegregationNote.Length > MaxSegregationNoteLength)
            {
                throw new ValidationException("Ghi chú phân tách nhiệm vụ (SoD) không được vượt quá 500 ký tự.");
            }
        }

        if (!string.IsNullOrWhiteSpace(request.Note) &&
            !string.IsNullOrWhiteSpace(request.Reason) &&
            !string.Equals(request.Note.Trim(), request.Reason.Trim(), StringComparison.Ordinal))
        {
            throw new ValidationException("Ghi chú kiểm toán (note và reason) không đồng nhất.");
        }

        var trimmedNote = !string.IsNullOrWhiteSpace(request.Note)
            ? request.Note.Trim()
            : (!string.IsNullOrWhiteSpace(request.Reason) ? request.Reason.Trim() : null);

        if (trimmedNote is { Length: > MaxAuditNoteLength })
        {
            throw new ValidationException("Ghi chú kiểm toán không được vượt quá 250 ký tự.");
        }

        List<string>? normalizedPermissionCodes = null;
        HashSet<string>? codeSet = null;
        if (request.PermissionCodes is not null)
        {
            if (request.PermissionCodes.Count == 0)
            {
                throw new ValidationException("Vai trò phải được gán ít nhất 1 quyền hạn hoạt động.");
            }

            codeSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rawPermissionCode in request.PermissionCodes)
            {
                if (string.IsNullOrWhiteSpace(rawPermissionCode))
                {
                    throw new ValidationException("Mã quyền hạn không nằm trong danh mục hệ thống.");
                }

                var candidate = rawPermissionCode.Trim().ToUpperInvariant();
                if (!PermissionCatalog.ContainsKey(candidate))
                {
                    throw new ValidationException("Mã quyền hạn không nằm trong danh mục hệ thống.");
                }

                codeSet.Add(candidate);
            }

            normalizedPermissionCodes = codeSet
                .OrderBy(GetCanonicalPermissionSortOrder)
                .ThenBy(c => c, StringComparer.Ordinal)
                .ToList();

            if (codeSet.Contains(PrescriptionIssuePermissionCode) &&
                codeSet.Contains(MedicineDispensePermissionCode))
            {
                throw new ValidationException(
                    "Vi phạm nguyên tắc phân tách nhiệm vụ (SoD): Không thể gán đồng thời quyền Kê đơn thuốc (PRESCRIPTION_ISSUE) và Cấp phát thuốc (MEDICINE_DISPENSE) cho cùng một vai trò.");
            }

            if ((string.Equals(parsedRoleCode, SystemAdminRoleCode, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(normalizedRequestRoleCode, SystemAdminRoleCode, StringComparison.OrdinalIgnoreCase)) &&
                !codeSet.Contains(UserManagePermissionCode))
            {
                throw new ValidationException(
                    "Vai trò SYSTEM_ADMIN bắt buộc giữ quyền USER_MANAGE để tránh khóa quyền quản trị hệ thống.");
            }
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        var actorHospitalId = await EnsureSystemAdminAccessAsync(actorUserId, nowUtc, ct);

        Role? targetRole;
        if (parsedRoleId.HasValue)
        {
            var roleIdValue = parsedRoleId.Value;
            targetRole = await _db.Roles
                .FirstOrDefaultAsync(r => r.Id == roleIdValue, ct);
        }
        else
        {
            targetRole = await _db.Roles
                .FirstOrDefaultAsync(r => r.Code.ToUpper() == parsedRoleCode, ct);
        }

        if (targetRole is null)
        {
            throw new NotFoundException("Role", roleIdentifier.Trim());
        }

        var roleIdString = targetRole.Id.ToString(CultureInfo.InvariantCulture);
        var roleAudits = await _db.AuditLogs
            .AsNoTracking()
            .Where(a => a.EntityType == "Role")
            .ToListAsync(ct);

        var existingRoleAudits = roleAudits
            .Where(a =>
                string.Equals(a.EntityId, roleIdString, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a.EntityId, targetRole.Code, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(a => a.OccurredAt)
            .ThenByDescending(a => a.Id)
            .ToList();

        var currentMeta = ResolveRoleMetadata(targetRole, existingRoleAudits);

        if (normalizedRequestRoleCode is not null &&
            !string.Equals(targetRole.Code, normalizedRequestRoleCode, StringComparison.OrdinalIgnoreCase))
        {
            if (currentMeta.IsSystemRole)
            {
                throw new ValidationException("Không được phép thay đổi mã định danh (code) của vai trò hệ thống mặc định.");
            }

            var collidesWithSystemPreset = RoleMetadataCatalog.TryGetValue(normalizedRequestRoleCode, out var presetMeta) &&
                                           presetMeta.IsSystemRole;
            var codeExists = collidesWithSystemPreset ||
                             await _db.Roles
                                 .AsNoTracking()
                                 .AnyAsync(r => r.Id != targetRole.Id && r.Code.ToUpper() == normalizedRequestRoleCode, ct);

            if (codeExists)
            {
                throw new ConflictException($"Mã vai trò '{normalizedRequestRoleCode}' đã tồn tại trong hệ thống.");
            }
        }

        if (normalizedName is not null)
        {
            var normalizedNameLower = normalizedName.ToLowerInvariant();
            var nameExists = await _db.Roles
                .AsNoTracking()
                .AnyAsync(r => r.Id != targetRole.Id && r.Name.ToLower() == normalizedNameLower, ct);

            if (nameExists)
            {
                throw new ConflictException($"Tên vai trò '{normalizedName}' đã tồn tại trong hệ thống.");
            }
        }

        if (string.Equals(requestedStatus, "INACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(targetRole.Code, SystemAdminRoleCode, StringComparison.OrdinalIgnoreCase))
            {
                throw new ValidationException(
                    "Không thể ngừng hoạt động vai trò SYSTEM_ADMIN để tránh khóa quyền quản trị hệ thống.");
            }

            if (currentMeta.IsSystemRole)
            {
                throw new ValidationException("Không thể ngừng hoạt động vai trò hệ thống mặc định.");
            }
        }

        if (string.Equals(targetRole.Code, SystemAdminRoleCode, StringComparison.OrdinalIgnoreCase))
        {
            if (requestedScope is not null &&
                !string.Equals(requestedScope, "SYSTEM", StringComparison.OrdinalIgnoreCase))
            {
                throw new ValidationException("Vai trò SYSTEM_ADMIN bắt buộc thuộc nhóm phân hệ SYSTEM.");
            }

            if (normalizedPermissionCodes is not null &&
                !normalizedPermissionCodes.Contains(UserManagePermissionCode, StringComparer.OrdinalIgnoreCase))
            {
                throw new ValidationException(
                    "Vai trò SYSTEM_ADMIN bắt buộc giữ quyền USER_MANAGE để tránh khóa quyền quản trị hệ thống.");
            }
        }

        var previousCode = targetRole.Code;
        var previousName = targetRole.Name;

        targetRole.Code = normalizedRequestRoleCode ?? targetRole.Code;
        targetRole.Name = normalizedName ?? targetRole.Name;

        var effectiveScope = requestedScope ?? currentMeta.Scope;
        var effectiveStatus = requestedStatus ?? currentMeta.Status;
        var effectiveDescription = trimmedDescription ?? currentMeta.Description;
        var effectiveSegregationNote = trimmedSegregationNote ?? currentMeta.SegregationNote;

        var allPermissions = await _db.Permissions
            .AsNoTracking()
            .ToListAsync(ct);

        var existingRolePermissions = await _db.RolePermissions
            .Where(rp => rp.RoleId == targetRole.Id)
            .ToListAsync(ct);

        var permissionsById = allPermissions.ToDictionary(p => p.Id);
        var previousPermissionCodes = existingRolePermissions
            .Where(rp => permissionsById.ContainsKey(rp.PermissionId))
            .Select(rp => permissionsById[rp.PermissionId].Code.ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(GetCanonicalPermissionSortOrder)
            .ThenBy(c => c, StringComparer.Ordinal)
            .ToList();

        IReadOnlyList<string> effectivePermissionCodes;
        if (normalizedPermissionCodes is not null)
        {
            var permissionsByCode = allPermissions
                .GroupBy(p => p.Code, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var targetPermissions = new List<Permission>(normalizedPermissionCodes.Count);
            foreach (var code in normalizedPermissionCodes)
            {
                if (!permissionsByCode.TryGetValue(code, out var permEntity))
                {
                    throw new ValidationException("Mã quyền hạn không nằm trong danh mục hệ thống.");
                }

                targetPermissions.Add(permEntity);
            }

            var targetPermissionIds = targetPermissions
                .Select(p => p.Id)
                .ToHashSet();

            var toRemove = existingRolePermissions
                .Where(rp => !targetPermissionIds.Contains(rp.PermissionId))
                .ToList();

            if (toRemove.Count > 0)
            {
                _db.RolePermissions.RemoveRange(toRemove);
            }

            var existingPermissionIds = existingRolePermissions
                .Select(rp => rp.PermissionId)
                .ToHashSet();

            foreach (var perm in targetPermissions)
            {
                if (!existingPermissionIds.Contains(perm.Id))
                {
                    _db.RolePermissions.Add(new RolePermission
                    {
                        RoleId = targetRole.Id,
                        PermissionId = perm.Id,
                    });
                }
            }

            effectivePermissionCodes = normalizedPermissionCodes;
        }
        else
        {
            effectivePermissionCodes = previousPermissionCodes;
        }

        var changePayload = JsonSerializer.Serialize(new
        {
            roleId = targetRole.Id,
            roleCode = targetRole.Code,
            previousRoleCode = previousCode,
            name = targetRole.Name,
            previousName = previousName,
            scope = effectiveScope,
            previousScope = currentMeta.Scope,
            status = effectiveStatus,
            previousStatus = currentMeta.Status,
            description = effectiveDescription,
            previousDescription = currentMeta.Description,
            segregationNote = effectiveSegregationNote,
            previousSegregationNote = currentMeta.SegregationNote,
            isSystemRole = currentMeta.IsSystemRole,
            previousPermissions = previousPermissionCodes,
            assignedPermissions = effectivePermissionCodes,
            note = trimmedNote,
        });

        _db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actorUserId,
            HospitalId = actorHospitalId,
            Action = RoleUpdatedAction,
            EntityType = "Role",
            EntityId = targetRole.Id.ToString(CultureInfo.InvariantCulture),
            Reason = trimmedNote ?? $"Cập nhật thông tin vai trò {targetRole.Code}",
            ChangeData = changePayload,
            IpAddress = !string.IsNullOrWhiteSpace(ipAddress) && IPAddress.TryParse(ipAddress, out var parsedIp)
                ? parsedIp
                : null,
            OccurredAt = nowUtc,
        });

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            throw new ConflictException($"Mã vai trò '{targetRole.Code}' đã tồn tại trong hệ thống.");
        }

        var (allRoleItems, _) = await LoadCatalogItemsAsync(nowUtc, ct);
        return allRoleItems.First(r => r.RoleId == targetRole.Id);
    }

    private async Task<long?> EnsureSystemAdminAccessAsync(
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

        return actorStaff?.HospitalId;
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
            var roleIdString = role.Id.ToString(CultureInfo.InvariantCulture);
            var matchingAudits = roleAuditLogs
                .Where(a =>
                    string.Equals(a.EntityId, roleIdString, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(a.EntityId, role.Code, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(a => a.OccurredAt)
                .ThenByDescending(a => a.Id)
                .ToList();

            var metadata = ResolveRoleMetadata(role, matchingAudits);

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

            var latestAudit = matchingAudits.FirstOrDefault();

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

    private static RoleCatalogMetadata ResolveRoleMetadata(
        Role role,
        IReadOnlyList<AuditLog> matchingAudits)
    {
        var baseMeta = GetRoleMetadata(role);

        string? resolvedScope = null;
        string? resolvedStatus = null;
        string? resolvedDescription = null;
        string? resolvedSegregationNote = null;

        foreach (var audit in matchingAudits)
        {
            if (string.IsNullOrWhiteSpace(audit.ChangeData))
            {
                continue;
            }

            try
            {
                var payload = JsonSerializer.Deserialize<RoleAuditMetadataPayload>(audit.ChangeData, AuditJsonOptions);
                if (payload is null)
                {
                    continue;
                }

                if (resolvedScope is null &&
                    !string.IsNullOrWhiteSpace(payload.Scope) &&
                    ValidScopes.Contains(payload.Scope.Trim()))
                {
                    resolvedScope = payload.Scope.Trim().ToUpperInvariant();
                }

                if (resolvedStatus is null &&
                    !string.IsNullOrWhiteSpace(payload.Status) &&
                    ValidStatuses.Contains(payload.Status.Trim()))
                {
                    resolvedStatus = payload.Status.Trim().ToUpperInvariant();
                }

                if (resolvedDescription is null &&
                    !string.IsNullOrWhiteSpace(payload.Description))
                {
                    resolvedDescription = payload.Description.Trim();
                }

                if (resolvedSegregationNote is null &&
                    !string.IsNullOrWhiteSpace(payload.SegregationNote))
                {
                    resolvedSegregationNote = payload.SegregationNote.Trim();
                }

                if (resolvedScope is not null &&
                    resolvedStatus is not null &&
                    resolvedDescription is not null &&
                    resolvedSegregationNote is not null)
                {
                    break;
                }
            }
            catch (JsonException)
            {
                // Ignore non-JSON or incompatible ChangeData payloads
            }
        }

        return baseMeta with
        {
            Scope = resolvedScope ?? baseMeta.Scope,
            Status = resolvedStatus ?? baseMeta.Status,
            Description = resolvedDescription ?? baseMeta.Description,
            SegregationNote = resolvedSegregationNote ?? baseMeta.SegregationNote,
        };
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

    private static (long? RoleId, string? RoleCode) ParseRoleIdentifier(string roleIdentifier)
    {
        if (string.IsNullOrWhiteSpace(roleIdentifier))
        {
            throw new ValidationException("Mã định danh vai trò không hợp lệ.");
        }

        var trimmed = roleIdentifier.Trim();

        if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numericId))
        {
            if (numericId <= 0)
            {
                throw new ValidationException("Mã định danh vai trò không hợp lệ.");
            }

            return (numericId, null);
        }

        if (trimmed.StartsWith("ROL-", StringComparison.OrdinalIgnoreCase))
        {
            var suffix = trimmed[4..];
            if (!long.TryParse(suffix, NumberStyles.Integer, CultureInfo.InvariantCulture, out var prefixedId) ||
                prefixedId <= 0)
            {
                throw new ValidationException("Mã định danh vai trò không hợp lệ.");
            }

            return (prefixedId, null);
        }

        if (!IsValidRoleCodeToken(trimmed))
        {
            throw new ValidationException("Mã định danh vai trò không hợp lệ.");
        }

        return (null, trimmed.ToUpperInvariant());
    }

    private static bool IsValidRoleCodeToken(string token)
    {
        if (token.Length is < 2 or > 50 || !char.IsAsciiLetter(token[0]))
        {
            return false;
        }

        return token.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '_');
    }

    private static int GetCanonicalPermissionSortOrder(string code)
    {
        for (var i = 0; i < CanonicalPermissionOrder.Length; i++)
        {
            if (string.Equals(CanonicalPermissionOrder[i], code, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return int.MaxValue;
    }
}
