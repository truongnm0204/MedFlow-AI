using System.Globalization;
using MedFlow.Application.Common.Time;
using MedFlow.Application.DTOs.SystemAdmin;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.SystemAdmin;

public sealed class AuditLogQueryService : IAuditLogQueryService
{
    private const int MaxDateRangeDays = 366;

    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;

    public AuditLogQueryService(
        IApplicationDbContext db,
        TimeProvider? timeProvider = null)
    {
        _db = db;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<AuditLogListResponseDto> GetAuditLogsAsync(
        long actorUserId,
        GetAuditLogsRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (actorUserId <= 0)
        {
            throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");
        }

        ValidateListRequest(request);

        var nowUtc = HospitalClock.GetUtcNow(_timeProvider);
        var access = await EnsureAuditReadAccessAsync(actorUserId, nowUtc, ct);
        var effectiveHospitalId = ResolveHospitalScope(request.HospitalId, access);

        if (effectiveHospitalId.HasValue &&
            !await _db.Hospitals.AsNoTracking().AnyAsync(h => h.Id == effectiveHospitalId.Value, ct))
        {
            throw new NotFoundException("Hospital", effectiveHospitalId.Value);
        }

        var availableHospitalsQuery = _db.Hospitals
            .AsNoTracking()
            .Where(h => h.IsActive != false);

        if (!access.IsSystemAdmin && access.HospitalId.HasValue)
        {
            availableHospitalsQuery = availableHospitalsQuery.Where(h => h.Id == access.HospitalId.Value);
        }

        var allHospitals = await _db.Hospitals
            .AsNoTracking()
            .OrderBy(h => h.Id)
            .ToListAsync(ct);

        var hospitalsById = allHospitals.ToDictionary(h => h.Id, h => h.Name);

        var availableHospitals = (await availableHospitalsQuery
            .OrderBy(h => h.Id)
            .ToListAsync(ct))
            .Select(h => new AuditLogHospitalOptionDto(h.Id, h.HospitalCode, h.Name))
            .ToArray();

        var scopedQuery = _db.AuditLogs.AsNoTracking();
        if (effectiveHospitalId.HasValue)
        {
            scopedQuery = scopedQuery.Where(a => a.HospitalId == effectiveHospitalId.Value);
        }

        var metrics = await BuildMetricsAsync(scopedQuery, nowUtc, ct);

        var availableActions = await scopedQuery
            .Select(a => a.Action)
            .Distinct()
            .OrderBy(a => a)
            .ToListAsync(ct);

        var availableEntityTypes = await scopedQuery
            .Select(a => a.EntityType)
            .Distinct()
            .OrderBy(e => e)
            .ToListAsync(ct);

        var filteredQuery = await ApplyFiltersAsync(scopedQuery, request, ct);
        var total = await filteredQuery.CountAsync(ct);

        var rows = await filteredQuery
            .OrderByDescending(a => a.OccurredAt)
            .ThenByDescending(a => a.Id)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(ct);

        var actorIds = rows
            .Where(r => r.ActorUserId.HasValue)
            .Select(r => r.ActorUserId!.Value)
            .Distinct()
            .ToArray();

        var (emailsById, fullNamesById) = await LoadActorMetadataAsync(actorIds, ct);

        var items = rows
            .Select(r => MapListItem(r, emailsById, fullNamesById, hospitalsById))
            .ToArray();

        return new AuditLogListResponseDto(
            Data: items,
            Items: items,
            Total: total,
            Page: request.Page,
            PageSize: request.PageSize,
            TotalPages: total == 0 ? 0 : (int)Math.Ceiling(total / (double)request.PageSize),
            Metrics: metrics,
            AvailableActions: availableActions,
            AvailableEntityTypes: availableEntityTypes,
            AvailableHospitals: availableHospitals);
    }

    public async Task<AuditLogDetailDto> GetAuditLogByIdAsync(
        long actorUserId,
        long auditLogId,
        CancellationToken ct = default)
    {
        if (actorUserId <= 0)
        {
            throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");
        }

        if (auditLogId <= 0)
        {
            throw new ValidationException("Mã nhật ký kiểm toán không hợp lệ.");
        }

        var nowUtc = HospitalClock.GetUtcNow(_timeProvider);
        var access = await EnsureAuditReadAccessAsync(actorUserId, nowUtc, ct);

        var entity = await _db.AuditLogs
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == auditLogId, ct)
            ?? throw new NotFoundException("AuditLog", auditLogId);

        if (!access.IsSystemAdmin && entity.HospitalId != access.HospitalId)
        {
            throw new ForbiddenException("Không được phép xem chi tiết nhật ký kiểm toán ngoài phạm vi cơ sở y tế được phân công.");
        }

        var actorIds = entity.ActorUserId.HasValue ? new[] { entity.ActorUserId.Value } : [];
        var (emailsById, fullNamesById) = await LoadActorMetadataAsync(actorIds, ct);

        string? hospitalName = null;
        if (entity.HospitalId.HasValue)
        {
            hospitalName = await _db.Hospitals
                .AsNoTracking()
                .Where(h => h.Id == entity.HospitalId.Value)
                .Select(h => h.Name)
                .FirstOrDefaultAsync(ct);
        }

        var occurredAtUtc = DateTime.SpecifyKind(entity.OccurredAt, DateTimeKind.Utc);
        var occurredAtLocal = HospitalClock.ConvertUtcToHospitalLocal(occurredAtUtc);
        var (maskedChangeData, redactedFields) = SensitiveDataMasker.MaskJsonMetadata(entity.ChangeData);

        return new AuditLogDetailDto(
            Id: entity.Id,
            AuditCode: FormatAuditCode(entity.Id),
            ActorUserId: entity.ActorUserId,
            ActorEmail: entity.ActorUserId.HasValue && emailsById.TryGetValue(entity.ActorUserId.Value, out var email)
                ? email
                : null,
            ActorFullName: entity.ActorUserId.HasValue && fullNamesById.TryGetValue(entity.ActorUserId.Value, out var name)
                ? name
                : null,
            HospitalId: entity.HospitalId,
            HospitalName: hospitalName,
            Action: entity.Action,
            Category: ClassifyAction(entity.Action),
            EntityType: entity.EntityType,
            EntityId: entity.EntityId,
            Reason: SensitiveDataMasker.MaskPlainText(entity.Reason),
            IpAddress: entity.IpAddress?.ToString(),
            OccurredAt: occurredAtUtc,
            OccurredAtHospitalLocal: occurredAtLocal,
            ChangeData: maskedChangeData,
            RedactedFields: redactedFields);
    }

    private static void ValidateListRequest(GetAuditLogsRequest request)
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

        if (request.ActorUserId is <= 0)
        {
            throw new ValidationException("Mã người thực hiện lọc không hợp lệ.");
        }

        if (request.HospitalId is <= 0)
        {
            throw new ValidationException("Mã cơ sở y tế lọc không hợp lệ.");
        }

        var keyword = ResolveKeyword(request);
        if (keyword is { Length: > 200 })
        {
            throw new ValidationException("Từ khóa tìm kiếm không được vượt quá 200 ký tự.");
        }

        if (request.Action?.Trim() is { Length: > 100 })
        {
            throw new ValidationException("Mã hành động lọc không được vượt quá 100 ký tự.");
        }

        if (request.EntityType?.Trim() is { Length: > 100 })
        {
            throw new ValidationException("Loại đối tượng lọc không được vượt quá 100 ký tự.");
        }

        if (request.FromDate.HasValue && request.ToDate.HasValue)
        {
            if (request.FromDate.Value > request.ToDate.Value)
            {
                throw new ValidationException("Ngày bắt đầu tra cứu không được lớn hơn ngày kết thúc.");
            }

            if (request.ToDate.Value.DayNumber - request.FromDate.Value.DayNumber > MaxDateRangeDays)
            {
                throw new ValidationException("Khoảng thời gian tra cứu nhật ký kiểm toán tối đa 366 ngày.");
            }
        }
    }

    private async Task<AuditAccessScope> EnsureAuditReadAccessAsync(
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
            string.Equals(code, RoleNames.SystemAdmin, StringComparison.OrdinalIgnoreCase));
        var isHospitalAdmin = actorRoleCodes.Any(code =>
            string.Equals(code, RoleNames.HospitalAdmin, StringComparison.OrdinalIgnoreCase));

        if (!isSystemAdmin && !isHospitalAdmin)
        {
            throw new ForbiddenException("Người dùng không có quyền xem nhật ký kiểm toán hệ thống.");
        }

        var hasAuditRead = actorRoleIds.Count > 0 && await (
            from rp in _db.RolePermissions.AsNoTracking()
            join p in _db.Permissions.AsNoTracking() on rp.PermissionId equals p.Id
            where actorRoleIds.Contains(rp.RoleId) &&
                  p.Code.ToUpper() == PermissionCodes.AuditRead
            select rp.PermissionId).AnyAsync(ct);

        if (!hasAuditRead)
        {
            throw new ForbiddenException("Tài khoản quản trị chưa được cấp quyền xem nhật ký kiểm toán (AUDIT_READ).");
        }

        var actorStaff = await _db.Staff
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == actorUserId, ct);

        if (actorStaff is not null &&
            !string.Equals(actorStaff.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            throw new ForbiddenException("Hồ sơ nhân sự của quản trị viên đã ngừng hoạt động.");
        }

        if (!isSystemAdmin)
        {
            if (actorStaff?.HospitalId is null)
            {
                throw new ForbiddenException("Quản trị viên bệnh viện chưa được gán phạm vi cơ sở y tế.");
            }

            var hospital = await _db.Hospitals
                .AsNoTracking()
                .FirstOrDefaultAsync(h => h.Id == actorStaff.HospitalId.Value, ct)
                ?? throw new NotFoundException("Hospital", actorStaff.HospitalId.Value);

            if (hospital.IsActive == false)
            {
                throw new ForbiddenException("Cơ sở y tế được gán cho quản trị viên hiện đang ngừng hoạt động.");
            }

            return new AuditAccessScope(false, hospital.Id);
        }

        return new AuditAccessScope(true, actorStaff?.HospitalId);
    }

    private static long? ResolveHospitalScope(long? requestedHospitalId, AuditAccessScope access)
    {
        if (access.IsSystemAdmin)
        {
            return requestedHospitalId;
        }

        if (requestedHospitalId.HasValue && requestedHospitalId.Value != access.HospitalId)
        {
            throw new ForbiddenException("Không được phép xem nhật ký kiểm toán ngoài cơ sở y tế được phân công.");
        }

        return access.HospitalId;
    }

    private async Task<IQueryable<AuditLog>> ApplyFiltersAsync(
        IQueryable<AuditLog> query,
        GetAuditLogsRequest request,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(request.Action))
        {
            var normalizedAction = request.Action.Trim().ToUpperInvariant();
            query = query.Where(a => a.Action.ToUpper() == normalizedAction);
        }

        if (!string.IsNullOrWhiteSpace(request.EntityType))
        {
            var normalizedEntity = request.EntityType.Trim().ToUpperInvariant();
            query = query.Where(a => a.EntityType.ToUpper() == normalizedEntity);
        }

        if (request.ActorUserId.HasValue)
        {
            query = query.Where(a => a.ActorUserId == request.ActorUserId.Value);
        }

        if (request.FromDate.HasValue)
        {
            var fromUtc = HospitalClock.ConvertHospitalLocalToUtc(
                request.FromDate.Value.ToDateTime(TimeOnly.MinValue));
            query = query.Where(a => a.OccurredAt >= fromUtc);
        }

        if (request.ToDate.HasValue)
        {
            var toExclusiveUtc = HospitalClock.ConvertHospitalLocalToUtc(
                request.ToDate.Value.AddDays(1).ToDateTime(TimeOnly.MinValue));
            query = query.Where(a => a.OccurredAt < toExclusiveUtc);
        }

        var rawKeyword = ResolveKeyword(request);
        if (!string.IsNullOrWhiteSpace(rawKeyword))
        {
            var kw = rawKeyword.ToLowerInvariant();
            long? parsedAuditId = TryParseAuditId(rawKeyword);

            var matchingUserIds = await _db.Users
                .AsNoTracking()
                .Where(u => u.Email.ToLower().Contains(kw))
                .Select(u => u.Id)
                .ToListAsync(ct);

            var matchingStaffUserIds = await _db.Staff
                .AsNoTracking()
                .Where(s => s.UserId.HasValue &&
                            (s.FullName.ToLower().Contains(kw) || s.StaffCode.ToLower().Contains(kw)))
                .Select(s => s.UserId!.Value)
                .ToListAsync(ct);

            var actorMatchIds = matchingUserIds
                .Concat(matchingStaffUserIds)
                .Distinct()
                .ToArray();

            query = query.Where(a =>
                (parsedAuditId.HasValue && a.Id == parsedAuditId.Value) ||
                a.Action.ToLower().Contains(kw) ||
                a.EntityType.ToLower().Contains(kw) ||
                (a.EntityId != null && a.EntityId.ToLower().Contains(kw)) ||
                (a.Reason != null && a.Reason.ToLower().Contains(kw)) ||
                (a.ActorUserId.HasValue && actorMatchIds.Contains(a.ActorUserId.Value)));
        }

        return query;
    }

    private static async Task<AuditLogMetricsDto> BuildMetricsAsync(
        IQueryable<AuditLog> scopedQuery,
        DateTime nowUtcUnspecified,
        CancellationToken ct)
    {
        var since24h = nowUtcUnspecified.AddHours(-24);
        var totalLogs = await scopedQuery.CountAsync(ct);
        var last24h = await scopedQuery.CountAsync(a => a.OccurredAt >= since24h, ct);

        var actionGroups = await scopedQuery
            .GroupBy(a => a.Action)
            .Select(g => new { Action = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var authCount = 0;
        var configAndRbacCount = 0;
        foreach (var group in actionGroups)
        {
            var category = ClassifyAction(group.Action);
            if (category == "AUTH")
            {
                authCount += group.Count;
            }
            else if (category is "RBAC" or "CONFIG")
            {
                configAndRbacCount += group.Count;
            }
        }

        return new AuditLogMetricsDto(
            TotalLogs: totalLogs,
            Last24HoursCount: last24h,
            AuthEventsCount: authCount,
            ConfigAndRbacEventsCount: configAndRbacCount);
    }

    private async Task<(Dictionary<long, string> EmailsById, Dictionary<long, string> FullNamesById)> LoadActorMetadataAsync(
        long[] actorIds,
        CancellationToken ct)
    {
        if (actorIds.Length == 0)
        {
            return ([], []);
        }

        var emailsById = await _db.Users
            .AsNoTracking()
            .Where(u => actorIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Email, ct);

        var staffRows = await _db.Staff
            .AsNoTracking()
            .Where(s => s.UserId.HasValue && actorIds.Contains(s.UserId.Value))
            .Select(s => new { UserId = s.UserId!.Value, s.FullName })
            .ToListAsync(ct);

        var fullNamesById = new Dictionary<long, string>();
        foreach (var s in staffRows)
        {
            if (!string.IsNullOrWhiteSpace(s.FullName))
            {
                fullNamesById[s.UserId] = s.FullName;
            }
        }

        return (emailsById, fullNamesById);
    }

    private static AuditLogListItemDto MapListItem(
        AuditLog entity,
        IReadOnlyDictionary<long, string> emailsById,
        IReadOnlyDictionary<long, string> fullNamesById,
        IReadOnlyDictionary<long, string> hospitalsById)
    {
        var occurredAtUtc = DateTime.SpecifyKind(entity.OccurredAt, DateTimeKind.Utc);
        var occurredAtLocal = HospitalClock.ConvertUtcToHospitalLocal(occurredAtUtc);
        var (maskedChangeData, _) = SensitiveDataMasker.MaskJsonMetadata(entity.ChangeData);

        return new AuditLogListItemDto(
            Id: entity.Id,
            AuditCode: FormatAuditCode(entity.Id),
            ActorUserId: entity.ActorUserId,
            ActorEmail: entity.ActorUserId.HasValue && emailsById.TryGetValue(entity.ActorUserId.Value, out var email)
                ? email
                : null,
            ActorFullName: entity.ActorUserId.HasValue && fullNamesById.TryGetValue(entity.ActorUserId.Value, out var fullName)
                ? fullName
                : null,
            HospitalId: entity.HospitalId,
            HospitalName: entity.HospitalId.HasValue && hospitalsById.TryGetValue(entity.HospitalId.Value, out var hospName)
                ? hospName
                : null,
            Action: entity.Action,
            Category: ClassifyAction(entity.Action),
            EntityType: entity.EntityType,
            EntityId: entity.EntityId,
            Reason: SensitiveDataMasker.MaskPlainText(entity.Reason),
            IpAddress: entity.IpAddress?.ToString(),
            OccurredAt: occurredAtUtc,
            OccurredAtHospitalLocal: occurredAtLocal,
            HasMetadata: !string.IsNullOrWhiteSpace(maskedChangeData),
            ChangeData: maskedChangeData);
    }

    private static string FormatAuditCode(long id)
        => $"AUD-{id.ToString("D4", CultureInfo.InvariantCulture)}";

    private static long? TryParseAuditId(string rawKeyword)
    {
        var trimmed = rawKeyword.Trim();
        if (trimmed.StartsWith("AUD-", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[4..];
        }

        return long.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0
            ? id
            : null;
    }

    private static string ClassifyAction(string action)
    {
        var upper = (action ?? string.Empty).ToUpperInvariant();
        if (upper.Contains("AUTH", StringComparison.Ordinal) ||
            upper.Contains("LOGIN", StringComparison.Ordinal) ||
            upper.Contains("LOGOUT", StringComparison.Ordinal) ||
            upper.Contains("OTP", StringComparison.Ordinal) ||
            upper.Contains("REFRESH", StringComparison.Ordinal) ||
            upper.Contains("REGISTER", StringComparison.Ordinal))
        {
            return "AUTH";
        }

        if (upper.Contains("ROLE", StringComparison.Ordinal) ||
            upper.Contains("PERMISSION", StringComparison.Ordinal) ||
            upper.Contains("STAFF", StringComparison.Ordinal) ||
            upper.Contains("USER", StringComparison.Ordinal))
        {
            return "RBAC";
        }

        if (upper.Contains("POLICY", StringComparison.Ordinal) ||
            upper.Contains("INTEGRATION", StringComparison.Ordinal) ||
            upper.Contains("DEPARTMENT", StringComparison.Ordinal) ||
            upper.Contains("ROOM", StringComparison.Ordinal) ||
            upper.Contains("SERVICE", StringComparison.Ordinal) ||
            upper.Contains("SLOT", StringComparison.Ordinal) ||
            upper.Contains("DOCTOR", StringComparison.Ordinal) ||
            upper.Contains("PRICE", StringComparison.Ordinal))
        {
            return "CONFIG";
        }

        return "OPERATIONS";
    }

    private static string? ResolveKeyword(GetAuditLogsRequest request)
        => !string.IsNullOrWhiteSpace(request.Keyword)
            ? request.Keyword.Trim()
            : request.Query?.Trim();

    private sealed record AuditAccessScope(bool IsSystemAdmin, long? HospitalId);
}
