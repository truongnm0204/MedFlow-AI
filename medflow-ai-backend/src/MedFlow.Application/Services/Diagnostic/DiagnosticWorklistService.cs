using System.Globalization;
using System.Text.Json;
using MedFlow.Application.DTOs.Diagnostic;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Appointments;
using MedFlow.Application.Services.Doctors;
using MedFlow.Application.Services.Visits;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Diagnostic;

public sealed class DiagnosticWorklistService : IDiagnosticWorklistService
{
    private const string DiagnosticStaffRoleCode = "DIAGNOSTIC_STAFF";
    private const string DefaultShiftLabel = "Ca sáng • Đồng bộ trực tiếp với phòng khám";
    private const string DefaultDepartmentName = "Khoa Cận lâm sàng";
    private const int MaxIdentifierLength = 60;
    private const int MaxKeywordLength = 100;

    private static readonly string[] FallbackDiagnosticPermissions =
    [
        "DIAGNOSTIC_RESULT_WRITE",
        "QUEUE_MANAGE",
    ];

    private static readonly HashSet<string> ValidCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "LABORATORY",
        "IMAGING",
        "FUNCTIONAL",
    };

    private static readonly HashSet<string> ValidStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "ORDERED",
        "CONFIRMED",
        "IN_PROGRESS",
        "COMPLETED",
        "CANCELLED",
    };

    private static readonly HashSet<string> ValidQueueStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "WAITING",
        "READY",
        "CALLED",
        "IN_PROGRESS",
        "COMPLETED",
        "SKIPPED",
        "CANCELLED",
    };

    private static readonly HashSet<string> ValidResultStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "PENDING",
        "DRAFT",
        "PRELIMINARY",
        "PENDING_VERIFY",
        "FINALIZED",
        "VERIFIED",
        "CANCELLED",
    };

    private static readonly HashSet<string> ValidPriorities = new(StringComparer.OrdinalIgnoreCase)
    {
        "NORMAL",
        "URGENT",
        "STAT",
    };

    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;

    public DiagnosticWorklistService(
        IApplicationDbContext dbContext,
        TimeProvider? timeProvider = null)
    {
        _db = dbContext;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<DiagnosticWorklistResponseDto> GetWorklistAsync(
        long actorUserId,
        GetDiagnosticWorklistRequest? request = null,
        CancellationToken ct = default)
    {
        var effectiveRequest = request ?? new GetDiagnosticWorklistRequest();
        var filter = ValidateAndNormalizeRequest(effectiveRequest);

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var nowLocal = AppointmentBookingRules.GetVietnamNow(_timeProvider);
        var todayLocal = DateOnly.FromDateTime(nowLocal);

        var actorContext = await ResolveAuthorizedDiagnosticStaffAsync(actorUserId, nowUtc, ct);
        var dataset = await LoadHospitalWorklistDatasetAsync(
            actorContext,
            nowLocal,
            todayLocal,
            ct);

        var allHospitalItems = dataset.HospitalItems;

        var dateScopedItems = filter.FilterDate.HasValue
            ? allHospitalItems
                .Where(item => MatchesDateFilter(item, filter.FilterDate.Value))
                .ToList()
            : allHospitalItems;

        var sortedDateScopedItems = SortWorklistItems(dateScopedItems);

        var effectiveReferenceDate = filter.FilterDate ?? todayLocal;
        var stats = BuildWorklistStats(sortedDateScopedItems, effectiveReferenceDate);
        var servicePoints = BuildServicePointBuckets(
            dataset.HospitalDiagnosticServicePoints,
            dataset.RoomsById,
            dataset.ServicesById,
            dataset.SpecialtiesById,
            sortedDateScopedItems);

        var filteredItems = ApplyEntryFilters(sortedDateScopedItems, filter);

        var totalCount = filteredItems.Count;
        var totalPages = totalCount == 0
            ? 0
            : (int)Math.Ceiling(totalCount / (double)filter.PageSize);

        var pagedItems = filteredItems
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToList();

        var quickActions = BuildQuickActions(actorContext.StaffProfile.Permissions);
        var effectiveDateString = filter.FilterDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        return new DiagnosticWorklistResponseDto(
            FacilityName: actorContext.Hospital.Name,
            FacilityCode: actorContext.Hospital.HospitalCode,
            DepartmentName: actorContext.DepartmentName,
            ShiftLabel: DefaultShiftLabel,
            EffectiveDate: effectiveDateString,
            GeneratedAtUtc: nowUtc,
            TotalCount: totalCount,
            Page: filter.Page,
            PageSize: filter.PageSize,
            TotalPages: totalPages,
            Stats: stats,
            ServicePoints: servicePoints,
            Items: pagedItems,
            Orders: pagedItems,
            Queue: pagedItems,
            QuickActions: quickActions,
            StaffProfile: actorContext.StaffProfile);
    }

    public async Task<DiagnosticWorklistItemDto> GetWorklistOrderDetailAsync(
        long actorUserId,
        string orderIdOrCode,
        CancellationToken ct = default)
    {
        if (actorUserId <= 0)
        {
            throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");
        }

        var normalizedIdentifier = ValidateOrderIdentifier(orderIdOrCode);

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var nowLocal = AppointmentBookingRules.GetVietnamNow(_timeProvider);
        var todayLocal = DateOnly.FromDateTime(nowLocal);

        var actorContext = await ResolveAuthorizedDiagnosticStaffAsync(actorUserId, nowUtc, ct);
        var dataset = await LoadHospitalWorklistDatasetAsync(
            actorContext,
            nowLocal,
            todayLocal,
            ct);

        var matchedHospitalItem = dataset.HospitalItems
            .FirstOrDefault(item => MatchesOrderItemIdentifier(item, normalizedIdentifier));

        if (matchedHospitalItem is not null)
        {
            return matchedHospitalItem;
        }

        foreach (var foreignOrder in dataset.ForeignOrders)
        {
            dataset.QueueEntriesById.TryGetValue(foreignOrder.QueueEntryId ?? 0, out var foreignQueueEntry);
            dataset.VisitsById.TryGetValue(foreignOrder.VisitId ?? 0, out var foreignVisit);
            var referenceDate = ResolveOrderReferenceDate(foreignOrder, foreignQueueEntry, foreignVisit, todayLocal);

            if (MatchesRawOrderIdentifier(foreignOrder, foreignQueueEntry, referenceDate, normalizedIdentifier))
            {
                throw new ForbiddenException(
                    "Phiếu chỉ định cận lâm sàng không thuộc cơ sở y tế của nhân viên cận lâm sàng.");
            }
        }

        throw new NotFoundException("DiagnosticOrder", normalizedIdentifier);
    }

    private static ValidatedWorklistFilter ValidateAndNormalizeRequest(GetDiagnosticWorklistRequest request)
    {
        if (request.Page < 1)
        {
            throw new ValidationException("Số trang hiển thị phải lớn hơn hoặc bằng 1.");
        }

        if (request.Limit is < 1 or > 100 ||
            (request.QueueLimit.HasValue && request.QueueLimit.Value is < 1 or > 100) ||
            (request.PageSize.HasValue && request.PageSize.Value is < 1 or > 100))
        {
            throw new ValidationException("Số lượng chỉ định cận lâm sàng hiển thị phải từ 1 đến 100.");
        }

        var pageSize = request.PageSize ?? request.QueueLimit ?? request.Limit;
        if (pageSize is < 1 or > 100)
        {
            throw new ValidationException("Số lượng chỉ định cận lâm sàng hiển thị phải từ 1 đến 100.");
        }

        var normalizedCategory = NormalizeEnumFilter(
            request.Category,
            ValidCategories,
            "Nhóm cận lâm sàng lọc không hợp lệ.");

        var normalizedStatus = NormalizeEnumFilter(
            request.Status,
            ValidStatuses,
            "Trạng thái chỉ định cận lâm sàng lọc không hợp lệ.");

        var normalizedQueueStatus = NormalizeEnumFilter(
            request.QueueStatus,
            ValidQueueStatuses,
            "Trạng thái hàng đợi cận lâm sàng lọc không hợp lệ.");

        var normalizedResultStatus = NormalizeEnumFilter(
            request.ResultStatus,
            ValidResultStatuses,
            "Trạng thái kết quả cận lâm sàng lọc không hợp lệ.");

        var normalizedPriority = NormalizeEnumFilter(
            request.Priority,
            ValidPriorities,
            "Mức độ ưu tiên lọc không hợp lệ.");

        var parsedOrderDate = ParseDateFilter(request.OrderDate);
        var parsedQueueDate = ParseDateFilter(request.QueueDate);

        if (parsedOrderDate.HasValue &&
            parsedQueueDate.HasValue &&
            parsedOrderDate.Value != parsedQueueDate.Value)
        {
            throw new ValidationException(
                "Ngày lọc danh sách chỉ định cận lâm sàng không hợp lệ (định dạng YYYY-MM-DD).");
        }

        var filterDate = parsedOrderDate ?? parsedQueueDate;

        var servicePointFilter = NormalizeFilterIdentifier(
            request.ServicePointId,
            "Điểm phục vụ cận lâm sàng lọc không hợp lệ.");
        var roomFilter = NormalizeFilterIdentifier(
            request.RoomId,
            "Phòng thực hiện cận lâm sàng lọc không hợp lệ.");
        var serviceFilter = NormalizeFilterIdentifier(
            request.ServiceId,
            "Dịch vụ cận lâm sàng lọc không hợp lệ.");
        var visitFilter = NormalizeFilterIdentifier(
            request.VisitId,
            "Mã lượt khám lọc không hợp lệ.");
        var patientFilter = NormalizeFilterIdentifier(
            request.PatientId,
            "Mã bệnh nhân lọc không hợp lệ.");
        var doctorFilter = NormalizeFilterIdentifier(
            request.DoctorId,
            "Mã bác sĩ chỉ định lọc không hợp lệ.");

        string? normalizedKeyword = null;
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            if (request.Keyword.Contains('<') || request.Keyword.Contains('>'))
            {
                throw new ValidationException("Từ khóa tìm kiếm chứa ký tự không hợp lệ.");
            }

            var trimmedKeyword = request.Keyword.Trim();
            if (trimmedKeyword.Length > MaxKeywordLength)
            {
                throw new ValidationException("Từ khóa tìm kiếm không được vượt quá 100 ký tự.");
            }

            normalizedKeyword = trimmedKeyword;
        }

        return new ValidatedWorklistFilter(
            Page: request.Page,
            PageSize: pageSize,
            Category: normalizedCategory,
            Status: normalizedStatus,
            QueueStatus: normalizedQueueStatus,
            ResultStatus: normalizedResultStatus,
            Priority: normalizedPriority,
            FilterDate: filterDate,
            ServicePointId: servicePointFilter,
            RoomId: roomFilter,
            ServiceId: serviceFilter,
            VisitId: visitFilter,
            PatientId: patientFilter,
            DoctorId: doctorFilter,
            RequiredForConclusion: request.RequiredForConclusion,
            Keyword: normalizedKeyword);
    }

    private static string? NormalizeEnumFilter(
        string? rawValue,
        HashSet<string> allowedValues,
        string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        var trimmed = rawValue.Trim();
        if (string.Equals(trimmed, "ALL", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!allowedValues.Contains(trimmed))
        {
            throw new ValidationException(errorMessage);
        }

        return trimmed.ToUpperInvariant();
    }

    private static DateOnly? ParseDateFilter(string? rawDate)
    {
        if (string.IsNullOrWhiteSpace(rawDate))
        {
            return null;
        }

        var trimmed = rawDate.Trim();
        if (string.Equals(trimmed, "ALL", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!DateOnly.TryParseExact(
                trimmed,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            throw new ValidationException(
                "Ngày lọc danh sách chỉ định cận lâm sàng không hợp lệ (định dạng YYYY-MM-DD).");
        }

        return parsed;
    }

    private static string? NormalizeFilterIdentifier(string? rawIdentifier, string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(rawIdentifier))
        {
            return null;
        }

        var trimmed = rawIdentifier.Trim();
        if (string.Equals(trimmed, "ALL", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (trimmed.StartsWith('#'))
        {
            trimmed = trimmed[1..].TrimStart();
        }

        if (string.IsNullOrEmpty(trimmed) ||
            trimmed.Length > MaxIdentifierLength ||
            trimmed.Contains('<') ||
            trimmed.Contains('>') ||
            trimmed.StartsWith('-') ||
            !IsSafeIdentifier(trimmed))
        {
            throw new ValidationException(errorMessage);
        }

        if (trimmed.All(char.IsAsciiDigit))
        {
            if (!long.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedId) ||
                parsedId <= 0)
            {
                throw new ValidationException(errorMessage);
            }
        }

        return trimmed;
    }

    private static string ValidateOrderIdentifier(string? orderIdOrCode)
    {
        const string errorMessage = "Mã hoặc định danh phiếu chỉ định cận lâm sàng không hợp lệ.";

        if (string.IsNullOrWhiteSpace(orderIdOrCode))
        {
            throw new ValidationException(errorMessage);
        }

        var trimmed = orderIdOrCode.Trim();
        if (trimmed.StartsWith('#'))
        {
            trimmed = trimmed[1..].TrimStart();
        }

        if (string.IsNullOrEmpty(trimmed) ||
            trimmed.Length > MaxIdentifierLength ||
            trimmed.Contains('<') ||
            trimmed.Contains('>') ||
            trimmed.StartsWith('-') ||
            !IsSafeIdentifier(trimmed))
        {
            throw new ValidationException(errorMessage);
        }

        if (trimmed.All(char.IsAsciiDigit))
        {
            if (!long.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedId) ||
                parsedId <= 0)
            {
                throw new ValidationException(errorMessage);
            }
        }
        else if (trimmed.StartsWith("do-", StringComparison.OrdinalIgnoreCase))
        {
            var suffix = trimmed[3..];
            if (string.IsNullOrEmpty(suffix) ||
                (suffix.All(char.IsAsciiDigit) &&
                 (!long.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out var doId) || doId <= 0)))
            {
                throw new ValidationException(errorMessage);
            }
        }
        else if (trimmed.StartsWith("q-", StringComparison.OrdinalIgnoreCase))
        {
            var suffix = trimmed[2..];
            if (string.IsNullOrEmpty(suffix) ||
                (suffix.All(char.IsAsciiDigit) &&
                 (!long.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out var qId) || qId <= 0)))
            {
                throw new ValidationException(errorMessage);
            }
        }

        return trimmed;
    }

    private static bool IsSafeIdentifier(string value)
        => value.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '.' or '_' or '-' or '#');

    private async Task<AuthorizedDiagnosticStaffContext> ResolveAuthorizedDiagnosticStaffAsync(
        long actorUserId,
        DateTime nowUtc,
        CancellationToken ct)
    {
        if (actorUserId <= 0)
        {
            throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");
        }

        var actorUser = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == actorUserId, ct)
            ?? throw new NotFoundException("User", actorUserId);

        if (actorUser.AccountStatus != AccountStatus.ACTIVE)
        {
            throw new ForbiddenException("Tài khoản nhân viên cận lâm sàng không ở trạng thái hoạt động.");
        }

        if (actorUser.LockedUntil.HasValue && actorUser.LockedUntil.Value > nowUtc)
        {
            throw new ForbiddenException("Tài khoản nhân viên cận lâm sàng đang bị tạm khóa.");
        }

        var allRoles = await _db.Roles
            .AsNoTracking()
            .OrderBy(r => r.Id)
            .ToListAsync(ct);

        var actorRoleIds = (await _db.UserRoles
            .AsNoTracking()
            .Where(ur => ur.UserId == actorUserId)
            .Select(ur => ur.RoleId)
            .ToListAsync(ct))
            .ToHashSet();

        var actorRoleCodes = allRoles
            .Where(r => actorRoleIds.Contains(r.Id))
            .Select(r => r.Code)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();

        if (!actorRoleCodes.Any(c => string.Equals(c, DiagnosticStaffRoleCode, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ForbiddenException("Người dùng không có quyền truy cập danh sách công việc Cận lâm sàng.");
        }

        var actorStaff = await _db.Staff
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == actorUserId, ct);

        if (actorStaff is null || !actorStaff.HospitalId.HasValue)
        {
            throw new ForbiddenException("Tài khoản nhân viên cận lâm sàng chưa được gán cơ sở y tế.");
        }

        if (!string.Equals(actorStaff.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            throw new ForbiddenException("Hồ sơ nhân sự của nhân viên cận lâm sàng đã ngừng hoạt động.");
        }

        var hospitalId = actorStaff.HospitalId.Value;

        var hospital = await _db.Hospitals
            .AsNoTracking()
            .FirstOrDefaultAsync(h => h.Id == hospitalId, ct)
            ?? throw new NotFoundException("Hospital", hospitalId);

        if (hospital.IsActive == false)
        {
            throw new ForbiddenException("Cơ sở y tế được gán cho nhân viên cận lâm sàng hiện đang ngừng hoạt động.");
        }

        var departments = await _db.Departments
            .AsNoTracking()
            .Where(d => d.HospitalId == hospitalId)
            .OrderBy(d => d.Id)
            .ToListAsync(ct);

        var departmentsById = departments.ToDictionary(d => d.Id);

        Department? staffDept = null;
        if (actorStaff.DepartmentId.HasValue &&
            departmentsById.TryGetValue(actorStaff.DepartmentId.Value, out staffDept) &&
            staffDept.IsActive == false)
        {
            throw new ForbiddenException("Khoa cận lâm sàng được phân công hiện đang ngừng hoạt động.");
        }

        var departmentName = !string.IsNullOrWhiteSpace(staffDept?.Name)
            ? staffDept.Name
            : departments
                .FirstOrDefault(d => string.Equals(d.DepartmentCode, "DIA", StringComparison.OrdinalIgnoreCase))
                ?.Name ?? DefaultDepartmentName;

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
            actorPermissionCodes = [.. FallbackDiagnosticPermissions];
        }
        else if (!actorPermissionCodes.Any(code =>
                     string.Equals(code, "QUEUE_MANAGE", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(code, "DIAGNOSTIC_RESULT_WRITE", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(code, "DIAGNOSTIC_ORDER", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ForbiddenException("Nhân viên cận lâm sàng không có quyền truy cập danh sách chỉ định cận lâm sàng.");
        }

        var fullName = !string.IsNullOrWhiteSpace(actorStaff.FullName)
            ? actorStaff.FullName
            : actorUser.Email;

        var staffProfile = new DiagnosticStaffProfileDto(
            UserId: actorUser.Id,
            Email: actorUser.Email,
            FullName: fullName,
            StaffCode: actorStaff.StaffCode,
            HospitalId: hospital.Id,
            HospitalCode: hospital.HospitalCode,
            HospitalName: hospital.Name,
            DepartmentId: actorStaff.DepartmentId,
            DepartmentName: departmentName,
            Roles: actorRoleCodes,
            Permissions: actorPermissionCodes);

        return new AuthorizedDiagnosticStaffContext(
            ActorUser: actorUser,
            ActorStaff: actorStaff,
            Hospital: hospital,
            StaffDepartment: staffDept,
            DepartmentName: departmentName,
            DepartmentsById: departmentsById,
            StaffProfile: staffProfile);
    }

    private async Task<HospitalWorklistDataset> LoadHospitalWorklistDatasetAsync(
        AuthorizedDiagnosticStaffContext actorContext,
        DateTime nowLocal,
        DateOnly todayLocal,
        CancellationToken ct)
    {
        var hospitalId = actorContext.Hospital.Id;

        List<DiagnosticOrder> allOrders;
        try
        {
            allOrders = await _db.DiagnosticOrders
                .AsNoTracking()
                .OrderBy(o => o.Id)
                .ToListAsync(ct);
        }
        catch (NotSupportedException)
        {
            allOrders = [];
        }

        var allDepartments = await _db.Departments
            .AsNoTracking()
            .OrderBy(d => d.Id)
            .ToListAsync(ct);
        var allDepartmentsById = allDepartments.ToDictionary(d => d.Id);

        var allServices = await _db.Services
            .AsNoTracking()
            .OrderBy(s => s.Id)
            .ToListAsync(ct);
        var servicesById = allServices.ToDictionary(s => s.Id);

        var specialties = await _db.Specialties
            .AsNoTracking()
            .OrderBy(sp => sp.Id)
            .ToListAsync(ct);
        var specialtiesById = specialties.ToDictionary(sp => sp.Id);

        var allRooms = await _db.HospitalRooms
            .AsNoTracking()
            .OrderBy(r => r.Id)
            .ToListAsync(ct);
        var roomsById = allRooms.ToDictionary(r => r.Id);

        var allDoctors = await _db.Doctors
            .AsNoTracking()
            .Include(d => d.Staff)
            .OrderBy(d => d.Id)
            .ToListAsync(ct);
        var doctorsById = allDoctors.ToDictionary(d => d.Id);

        var allStaff = await _db.Staff
            .AsNoTracking()
            .OrderBy(s => s.Id)
            .ToListAsync(ct);
        var staffById = allStaff.ToDictionary(s => s.Id);

        List<QueueServicePoint> allServicePoints;
        try
        {
            allServicePoints = await _db.QueueServicePoints
                .AsNoTracking()
                .OrderBy(sp => sp.Id)
                .ToListAsync(ct);
        }
        catch (NotSupportedException)
        {
            allServicePoints = [];
        }

        var servicePointsById = allServicePoints.ToDictionary(sp => sp.Id);

        var allSlots = await _db.ExaminationSlots
            .AsNoTracking()
            .OrderBy(s => s.Id)
            .ToListAsync(ct);
        var slotsById = allSlots.ToDictionary(s => s.Id);

        var allAppointments = await _db.Appointments
            .AsNoTracking()
            .OrderBy(a => a.Id)
            .ToListAsync(ct);
        var appointmentsById = allAppointments.ToDictionary(a => a.Id);

        var allVisits = await _db.Visits
            .AsNoTracking()
            .OrderBy(v => v.Id)
            .ToListAsync(ct);
        var visitsById = allVisits.ToDictionary(v => v.Id);

        var allQueueEntries = await _db.QueueEntries
            .AsNoTracking()
            .OrderBy(q => q.Id)
            .ToListAsync(ct);
        var queueEntriesById = allQueueEntries.ToDictionary(q => q.Id);

        long? ResolveServiceHospitalId(Service? svc)
        {
            if (svc is null) return null;
            if (svc.HospitalId.HasValue) return svc.HospitalId.Value;
            if (svc.DepartmentId.HasValue && allDepartmentsById.TryGetValue(svc.DepartmentId.Value, out var dept))
            {
                return dept.HospitalId;
            }
            return null;
        }

        long? ResolveRoomHospitalId(HospitalRoom? rm)
        {
            if (rm is null) return null;
            if (rm.HospitalId.HasValue) return rm.HospitalId.Value;
            if (rm.DepartmentId.HasValue && allDepartmentsById.TryGetValue(rm.DepartmentId.Value, out var dept))
            {
                return dept.HospitalId;
            }
            return null;
        }

        long? ResolveDoctorHospitalId(Doctor? doc)
        {
            if (doc is null) return null;
            if (doc.HospitalId.HasValue) return doc.HospitalId.Value;
            if (doc.Staff?.HospitalId != null) return doc.Staff.HospitalId;
            if (doc.StaffId.HasValue && staffById.TryGetValue(doc.StaffId.Value, out var docStaff) && docStaff.HospitalId.HasValue)
            {
                return docStaff.HospitalId.Value;
            }
            return null;
        }

        long? ResolveServicePointHospitalId(QueueServicePoint? sp)
        {
            if (sp is null) return null;
            if (sp.HospitalId.HasValue) return sp.HospitalId.Value;
            if (sp.RoomId.HasValue && roomsById.TryGetValue(sp.RoomId.Value, out var rm))
            {
                var rmHosp = ResolveRoomHospitalId(rm);
                if (rmHosp.HasValue) return rmHosp.Value;
            }
            if (sp.ServiceId.HasValue && servicesById.TryGetValue(sp.ServiceId.Value, out var svc))
            {
                var svcHosp = ResolveServiceHospitalId(svc);
                if (svcHosp.HasValue) return svcHosp.Value;
            }
            if (sp.DoctorId.HasValue && doctorsById.TryGetValue(sp.DoctorId.Value, out var doc))
            {
                var docHosp = ResolveDoctorHospitalId(doc);
                if (docHosp.HasValue) return docHosp.Value;
            }
            return null;
        }

        long? ResolveSlotHospitalId(ExaminationSlot? slot)
        {
            if (slot is null) return null;
            if (slot.RoomId.HasValue && roomsById.TryGetValue(slot.RoomId.Value, out var rm))
            {
                var rmHosp = ResolveRoomHospitalId(rm);
                if (rmHosp.HasValue) return rmHosp.Value;
            }
            if (slot.ServiceId.HasValue && servicesById.TryGetValue(slot.ServiceId.Value, out var svc))
            {
                var svcHosp = ResolveServiceHospitalId(svc);
                if (svcHosp.HasValue) return svcHosp.Value;
            }
            if (slot.DoctorId.HasValue && doctorsById.TryGetValue(slot.DoctorId.Value, out var doc))
            {
                var docHosp = ResolveDoctorHospitalId(doc);
                if (docHosp.HasValue) return docHosp.Value;
            }
            return null;
        }

        long? ResolveAppointmentHospitalId(Appointment? appt)
        {
            if (appt is null) return null;
            if (appt.SlotId.HasValue && slotsById.TryGetValue(appt.SlotId.Value, out var slot))
            {
                return ResolveSlotHospitalId(slot);
            }
            return null;
        }

        long? ResolveVisitHospitalId(Visit? visit)
        {
            if (visit is null) return null;
            if (visit.ExaminationRoomId.HasValue && roomsById.TryGetValue(visit.ExaminationRoomId.Value, out var rm))
            {
                var rmHosp = ResolveRoomHospitalId(rm);
                if (rmHosp.HasValue) return rmHosp.Value;
            }
            if (visit.AssignedDoctorId.HasValue && doctorsById.TryGetValue(visit.AssignedDoctorId.Value, out var doc))
            {
                var docHosp = ResolveDoctorHospitalId(doc);
                if (docHosp.HasValue) return docHosp.Value;
            }
            if (visit.CheckedInByStaffId.HasValue && staffById.TryGetValue(visit.CheckedInByStaffId.Value, out var stf) && stf.HospitalId.HasValue)
            {
                return stf.HospitalId.Value;
            }
            if (visit.AppointmentId.HasValue && appointmentsById.TryGetValue(visit.AppointmentId.Value, out var appt))
            {
                var apptHosp = ResolveAppointmentHospitalId(appt);
                if (apptHosp.HasValue) return apptHosp.Value;
            }
            if (visit.CompletedByDoctorId.HasValue && doctorsById.TryGetValue(visit.CompletedByDoctorId.Value, out var compDoc))
            {
                var compDocHosp = ResolveDoctorHospitalId(compDoc);
                if (compDocHosp.HasValue) return compDocHosp.Value;
            }
            return null;
        }

        long? ResolveQueueEntryHospitalId(QueueEntry? qe)
        {
            if (qe is null) return null;
            var spId = qe.CurrentServicePointId ?? qe.ServicePointId;
            if (spId.HasValue && servicePointsById.TryGetValue(spId.Value, out var sp))
            {
                var spHosp = ResolveServicePointHospitalId(sp);
                if (spHosp.HasValue) return spHosp.Value;
            }
            if (qe.VisitId.HasValue && visitsById.TryGetValue(qe.VisitId.Value, out var vis))
            {
                var visHosp = ResolveVisitHospitalId(vis);
                if (visHosp.HasValue) return visHosp.Value;
            }
            if (qe.AppointmentId.HasValue && appointmentsById.TryGetValue(qe.AppointmentId.Value, out var appt))
            {
                var apptHosp = ResolveAppointmentHospitalId(appt);
                if (apptHosp.HasValue) return apptHosp.Value;
            }
            return null;
        }

        var hospitalDiagnosticServicePoints = allServicePoints
            .Where(sp =>
                string.Equals(sp.PointType, "DIAGNOSTIC", StringComparison.OrdinalIgnoreCase) &&
                ResolveServicePointHospitalId(sp) == hospitalId)
            .OrderBy(sp => sp.Id)
            .ToList();

        var hospitalDiagnosticRooms = allRooms
            .Where(r =>
                ResolveRoomHospitalId(r) == hospitalId &&
                string.Equals(r.RoomType, "DIAGNOSTIC", StringComparison.OrdinalIgnoreCase))
            .OrderBy(r => r.Id)
            .ToList();

        var hospitalOrders = new List<DiagnosticOrder>();
        var foreignOrders = new List<DiagnosticOrder>();

        foreach (var order in allOrders)
        {
            servicesById.TryGetValue(order.ServiceId ?? 0, out var svc);
            queueEntriesById.TryGetValue(order.QueueEntryId ?? 0, out var qe);
            doctorsById.TryGetValue(order.OrderingDoctorId ?? 0, out var doc);
            visitsById.TryGetValue(order.VisitId ?? 0, out var vis);
            staffById.TryGetValue(order.ConfirmedByStaffId ?? 0, out var cStaff);

            var svcHosp = ResolveServiceHospitalId(svc);
            var qeHosp = ResolveQueueEntryHospitalId(qe);
            var docHosp = ResolveDoctorHospitalId(doc);
            var visHosp = ResolveVisitHospitalId(vis);
            var staffHosp = cStaff?.HospitalId;

            var primaryHosp = svcHosp ?? qeHosp ?? docHosp ?? visHosp ?? staffHosp;
            var hasForeignLink =
                (svcHosp.HasValue && svcHosp.Value != hospitalId) ||
                (qeHosp.HasValue && qeHosp.Value != hospitalId) ||
                (docHosp.HasValue && docHosp.Value != hospitalId) ||
                (visHosp.HasValue && visHosp.Value != hospitalId);

            if (primaryHosp == hospitalId && !hasForeignLink)
            {
                hospitalOrders.Add(order);
            }
            else
            {
                foreignOrders.Add(order);
            }
        }

        if (hospitalOrders.Count == 0)
        {
            return new HospitalWorklistDataset(
                HospitalItems: [],
                ForeignOrders: foreignOrders,
                HospitalDiagnosticServicePoints: hospitalDiagnosticServicePoints,
                RoomsById: roomsById,
                ServicesById: servicesById,
                SpecialtiesById: specialtiesById,
                VisitsById: visitsById,
                QueueEntriesById: queueEntriesById);
        }

        var hospitalOrdersById = hospitalOrders.ToDictionary(o => o.Id);

        var referencedPatientIds = hospitalOrders
            .Select(o => o.VisitId.HasValue && visitsById.TryGetValue(o.VisitId.Value, out var v) ? v.PatientId : null)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        var patientsById = referencedPatientIds.Count == 0
            ? new Dictionary<long, Patient>()
            : (await _db.Patients
                .AsNoTracking()
                .Where(p => referencedPatientIds.Contains(p.Id))
                .ToListAsync(ct))
                .ToDictionary(p => p.Id);

        var hospitalServiceIds = hospitalOrders
            .Where(o => o.ServiceId.HasValue)
            .Select(o => o.ServiceId!.Value)
            .Distinct()
            .ToList();

        var activePricesByServiceId = await LoadActiveServicePricesAsync(hospitalServiceIds, nowLocal, ct);

        var referencedVisitIds = hospitalOrders
            .Where(o => o.VisitId.HasValue)
            .Select(o => o.VisitId!.Value)
            .Distinct()
            .ToList();

        var latestRevisionsByVisitId = await LoadLatestMedicalRevisionsByVisitAsync(referencedVisitIds, ct);

        var resultsBundle = await LoadDiagnosticResultsBundleAsync(hospitalOrders, staffById, ct);

        var orderIdStrings = hospitalOrders
            .Select(o => o.Id.ToString(CultureInfo.InvariantCulture))
            .Distinct()
            .ToList();
        var auditMetadataByOrderId = await TryLoadDiagnosticOrderAuditMetadataAsync(orderIdStrings, ct);

        var sequenceByOrderId = ComputeVisitOrderSequences(hospitalOrders);

        var permissionSet = new HashSet<string>(actorContext.StaffProfile.Permissions, StringComparer.OrdinalIgnoreCase);
        var hasQueueManage = permissionSet.Contains("QUEUE_MANAGE");
        var hasResultWrite = permissionSet.Contains("DIAGNOSTIC_RESULT_WRITE");
        var hasDiagnosticOrder = permissionSet.Contains("DIAGNOSTIC_ORDER");
        var hasConfirmPermission = hasQueueManage || hasResultWrite || hasDiagnosticOrder;

        var items = new List<DiagnosticWorklistItemDto>(hospitalOrders.Count);

        foreach (var order in hospitalOrders)
        {
            servicesById.TryGetValue(order.ServiceId ?? 0, out var service);
            visitsById.TryGetValue(order.VisitId ?? 0, out var visit);
            var appointment = visit?.AppointmentId.HasValue == true &&
                              appointmentsById.TryGetValue(visit.AppointmentId.Value, out var foundAppt)
                ? foundAppt
                : null;

            var patientId = visit?.PatientId ?? appointment?.PatientId ?? 0;
            patientsById.TryGetValue(patientId, out var patient);

            var orderingDoctorId = order.OrderingDoctorId ?? visit?.AssignedDoctorId;
            var orderingDoctor = orderingDoctorId.HasValue &&
                                 doctorsById.TryGetValue(orderingDoctorId.Value, out var foundDoc)
                ? foundDoc
                : null;

            var queueEntry = order.QueueEntryId.HasValue &&
                             queueEntriesById.TryGetValue(order.QueueEntryId.Value, out var foundQe)
                ? foundQe
                : null;

            var referenceDate = ResolveOrderReferenceDate(order, queueEntry, visit, todayLocal);

            var spId = queueEntry?.CurrentServicePointId ?? queueEntry?.ServicePointId;
            QueueServicePoint? resolvedSp = null;
            if (spId.HasValue && servicePointsById.TryGetValue(spId.Value, out var explicitSp))
            {
                resolvedSp = explicitSp;
            }
            else if (order.ServiceId.HasValue)
            {
                resolvedSp = hospitalDiagnosticServicePoints
                    .FirstOrDefault(sp => sp.ServiceId == order.ServiceId.Value && (sp.IsActive ?? true))
                    ?? hospitalDiagnosticServicePoints.FirstOrDefault(sp => sp.ServiceId == order.ServiceId.Value);
            }

            HospitalRoom? resolvedRoom = null;
            if (resolvedSp?.RoomId.HasValue == true && roomsById.TryGetValue(resolvedSp.RoomId.Value, out var spRoom))
            {
                resolvedRoom = spRoom;
            }
            else if (hospitalDiagnosticRooms.Count > 0)
            {
                resolvedRoom = hospitalDiagnosticRooms[(int)(order.Id % hospitalDiagnosticRooms.Count)];
            }

            var serviceDept = service?.DepartmentId.HasValue == true &&
                              allDepartmentsById.TryGetValue(service.DepartmentId.Value, out var foundDept)
                ? foundDept
                : actorContext.StaffDepartment;

            var specialty = service?.SpecialtyId.HasValue == true &&
                            specialtiesById.TryGetValue(service.SpecialtyId.Value, out var foundSpec)
                ? foundSpec
                : null;

            var category = service is not null
                ? ResolveDiagnosticCategory(service, specialtiesById)
                : "LABORATORY";
            var categoryLabel = FormatCategoryLabel(category);

            var (priority, priorityLabel) = NormalizePriority(order.Priority);

            resultsBundle.ResultsByOrderId.TryGetValue(order.Id, out var result);
            DiagnosticResultVersion? latestVersion = null;
            if (result is not null &&
                resultsBundle.VersionsByResultId.TryGetValue(result.Id, out var versions) &&
                versions.Count > 0)
            {
                latestVersion = versions[0];
            }

            var requiresVerification = service?.RequiresResultVerification ?? true;
            var resultState = ResolveDiagnosticOrderResultState(
                order,
                queueEntry,
                result,
                latestVersion,
                requiresVerification);

            var rawOrderStatus = string.IsNullOrWhiteSpace(order.Status)
                ? "ORDERED"
                : AppointmentBookingRules.NormalizeStatusKey(order.Status);
            var normalizedQueueStatus = queueEntry is not null
                ? NormalizeQueueStatus(queueEntry.Status)
                : null;
            var normalizedVisitStatus = !string.IsNullOrWhiteSpace(visit?.Status)
                ? AppointmentBookingRules.NormalizeStatusKey(visit.Status)
                : "IN_PROGRESS";

            var effectiveStatus = ResolveEffectiveWorklistStatus(
                order,
                rawOrderStatus,
                normalizedQueueStatus,
                normalizedVisitStatus,
                resultState);
            var statusLabel = FormatWorklistStatusLabel(effectiveStatus);

            var confirmedStaff = order.ConfirmedByStaffId.HasValue &&
                                 resultsBundle.StaffById.TryGetValue(order.ConfirmedByStaffId.Value, out var cStaff)
                ? cStaff
                : null;
            var finalizedStaff = result?.FinalizedByStaffId.HasValue == true &&
                                 resultsBundle.StaffById.TryGetValue(result.FinalizedByStaffId.Value, out var fStaff)
                ? fStaff
                : null;
            var enteredStaff = latestVersion?.EnteredByStaffId.HasValue == true &&
                               resultsBundle.StaffById.TryGetValue(latestVersion.EnteredByStaffId.Value, out var eStaff)
                ? eStaff
                : null;
            var verifiedStaff = latestVersion?.VerifiedByStaffId.HasValue == true &&
                                resultsBundle.StaffById.TryGetValue(latestVersion.VerifiedByStaffId.Value, out var vStaff)
                ? vStaff
                : null;

            latestRevisionsByVisitId.TryGetValue(order.VisitId ?? 0, out var latestRevision);

            auditMetadataByOrderId.TryGetValue(order.Id, out var auditMeta);
            var orderSequenceNo = auditMeta?.OrderSequenceNo
                ?? (sequenceByOrderId.TryGetValue(order.Id, out var computedSeq) ? computedSeq : 1);
            var isAdditionalOrder = auditMeta is not null
                ? auditMeta.IsAdditionalOrder
                : orderSequenceNo > 1;
            var followUpToOrderCode = auditMeta?.FollowUpToOrderCode;
            if (string.IsNullOrWhiteSpace(followUpToOrderCode) &&
                auditMeta?.FollowUpToOrderId.HasValue == true &&
                hospitalOrdersById.TryGetValue(auditMeta.FollowUpToOrderId.Value, out var followUpOrd))
            {
                followUpToOrderCode = FormatOrderCode(followUpOrd, referenceDate);
            }

            var unitPrice = service is not null && activePricesByServiceId.TryGetValue(service.Id, out var foundPrice)
                ? foundPrice
                : 0m;

            var (patientGender, patientGenderLabel) = NormalizeGender(patient?.Gender);
            var patientAge = ClinicalQueueRules.CalculateAge(patient?.DateOfBirth, todayLocal);

            var orderCode = FormatOrderCode(order, referenceDate);
            var visitCode = !string.IsNullOrWhiteSpace(visit?.VisitCode)
                ? visit.VisitCode.Trim()
                : $"VIS-{referenceDate:yyyyMM}-{(visit?.Id ?? order.VisitId ?? order.Id):D4}";

            var patientCode = !string.IsNullOrWhiteSpace(patient?.PatientCode)
                ? patient.PatientCode.Trim()
                : (patientId > 0 ? $"PAT-{patientId:D4}" : "PAT-0000");
            var patientName = !string.IsNullOrWhiteSpace(patient?.FullName)
                ? patient.FullName.Trim()
                : (patientId > 0 ? $"Bệnh nhân #{patientId}" : "Bệnh nhân CLS");

            var formattedDoctorName = FormatDoctorName(orderingDoctor);
            var effectiveQueueEntryId = queueEntry?.Id ?? order.QueueEntryId;
            var queueNumber = queueEntry?.QueueNumber;
            var ticketNo = queueNumber is > 0
                ? $"C{queueNumber.Value:D3}"
                : $"C{order.Id:D3}";
            var queueDateString = (queueEntry?.QueueDate ?? referenceDate)
                .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var presenceStatus = queueEntry is not null
                ? (string.IsNullOrWhiteSpace(queueEntry.PresenceStatus)
                    ? "PRESENT"
                    : queueEntry.PresenceStatus.Trim().ToUpperInvariant())
                : null;
            var clinicalPriority = queueEntry is not null
                ? ClinicalQueueRules.ResolveEffectivePriority(
                    queueEntry.ManualPriority,
                    patient?.DateOfBirth,
                    queueEntry.QueueDate ?? referenceDate)
                : null;
            var stateVersion = queueEntry?.StateVersion is > 0 ? queueEntry.StateVersion.Value : 1;

            var effectiveSpId = resolvedSp?.Id ?? spId;
            var effectiveRoomId = resolvedRoom?.Id ?? resolvedSp?.RoomId;
            var roomCode = !string.IsNullOrWhiteSpace(resolvedRoom?.RoomCode)
                ? resolvedRoom.RoomCode.Trim()
                : null;
            var roomDisplay = roomCode
                ?? (!string.IsNullOrWhiteSpace(resolvedRoom?.Name) ? resolvedRoom.Name.Trim() : "P.CLS-201");

            var orderedAtTimestamp = order.OrderedAt
                ?? queueEntry?.ScheduledAt
                ?? queueEntry?.ArrivedAt
                ?? visit?.CheckedInAt
                ?? nowLocal;
            var orderedAtFormatted = orderedAtTimestamp.ToString("HH:mm", CultureInfo.InvariantCulture);

            var isVisitActive = normalizedVisitStatus != "CANCELLED";
            var isServiceActive = service is null || (service.IsActive ?? true);
            var isRoomActive = resolvedRoom is null ||
                               string.Equals(resolvedRoom.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase);
            var isServicePointActive = resolvedSp is null || (resolvedSp.IsActive ?? true);

            var (canConfirmOrder, confirmDisabledReason) = EvaluateConfirmOrderEligibility(
                effectiveStatus,
                isVisitActive,
                isServiceActive,
                isServicePointActive,
                isRoomActive,
                hasConfirmPermission);

            var canReceivePatient = effectiveStatus == "CONFIRMED" &&
                                    (normalizedQueueStatus is "WAITING" or "RESERVED" or null) &&
                                    isVisitActive &&
                                    hasQueueManage;

            var canUpdateExecutionStatus = effectiveStatus is "CONFIRMED" or "IN_PROGRESS" &&
                                           isVisitActive &&
                                           (hasQueueManage || hasResultWrite);

            var canEnterResult = effectiveStatus is "IN_PROGRESS" or "COMPLETED" &&
                                 !resultState.IsResultFinalized &&
                                 isVisitActive &&
                                 hasResultWrite;

            var canViewResult = resultState.HasResult || effectiveStatus == "COMPLETED";

            var allowedActions = BuildAllowedActions(
                canConfirmOrder,
                confirmDisabledReason,
                canReceivePatient,
                canUpdateExecutionStatus,
                canEnterResult,
                canViewResult);

            items.Add(new DiagnosticWorklistItemDto(
                Id: $"do-{order.Id:D4}",
                OrderId: order.Id,
                OrderCode: orderCode,
                VisitId: visit?.Id ?? order.VisitId ?? 0,
                VisitCode: visitCode,
                VisitStatus: normalizedVisitStatus,
                PatientId: patientId,
                PatientCode: patientCode,
                PatientName: patientName,
                PatientGender: patientGender,
                PatientGenderLabel: patientGenderLabel,
                PatientDateOfBirth: patient?.DateOfBirth,
                PatientAge: patientAge,
                BloodType: patient?.BloodType,
                AllergiesSummary: latestRevision?.RelevantHistoryAllergies,
                ServiceId: service?.Id ?? order.ServiceId ?? 0,
                ServiceCode: service?.ServiceCode ?? $"SRV-{(order.ServiceId ?? order.Id):D3}",
                ServiceName: service?.Name ?? "Dịch vụ cận lâm sàng",
                Category: category,
                CategoryLabel: categoryLabel,
                DepartmentId: serviceDept?.Id ?? service?.DepartmentId ?? actorContext.ActorStaff.DepartmentId,
                DepartmentCode: serviceDept?.DepartmentCode ?? "DIA",
                DepartmentName: serviceDept?.Name ?? actorContext.DepartmentName,
                SpecialtyId: specialty?.Id ?? service?.SpecialtyId,
                SpecialtyCode: specialty?.SpecialtyCode,
                SpecialtyName: specialty?.Name ?? categoryLabel,
                UnitPrice: unitPrice,
                RequiresResultVerification: requiresVerification,
                OrderingDoctorId: orderingDoctor?.Id ?? orderingDoctorId,
                OrderingDoctorCode: orderingDoctor?.DoctorCode,
                OrderingDoctor: formattedDoctorName,
                QueueEntryId: effectiveQueueEntryId,
                FormattedQueueEntryId: effectiveQueueEntryId.HasValue ? $"q-{effectiveQueueEntryId.Value:D3}" : null,
                TicketNo: ticketNo,
                QueueNumber: queueNumber,
                QueueDate: queueDateString,
                QueueStatus: normalizedQueueStatus,
                PresenceStatus: presenceStatus,
                ClinicalPriority: clinicalPriority,
                StateVersion: stateVersion,
                ServicePointId: effectiveSpId,
                FormattedServicePointId: effectiveSpId.HasValue ? $"sp-{effectiveSpId.Value:D3}" : null,
                ServicePointName: resolvedSp?.Name ?? (resolvedRoom is not null ? $"Điểm CLS - {resolvedRoom.Name}" : null),
                RoomId: effectiveRoomId,
                Room: roomDisplay,
                RoomCode: roomCode,
                RoomName: resolvedRoom?.Name,
                RoomLocation: resolvedRoom?.Location,
                Priority: priority,
                PriorityLabel: priorityLabel,
                Status: effectiveStatus,
                OrderStatus: rawOrderStatus,
                StatusLabel: statusLabel,
                RequiredForConclusion: order.RequiredForConclusion ?? true,
                Instruction: order.Instruction,
                ReasonForVisit: visit?.ReasonForVisit ?? appointment?.ReasonForVisit,
                PresentingComplaint: latestRevision?.PresentingComplaint,
                ClinicalFindings: latestRevision?.ClinicalFindings,
                OrderedAt: orderedAtFormatted,
                OrderedAtTimestamp: orderedAtTimestamp,
                ConfirmedAt: order.ConfirmedAt ?? queueEntry?.ReadyAt,
                ConfirmedByStaffId: order.ConfirmedByStaffId,
                ConfirmedByStaffCode: confirmedStaff?.StaffCode,
                ConfirmedByStaffName: confirmedStaff?.FullName,
                StartedAt: queueEntry?.StartedAt,
                CompletedAt: queueEntry?.CompletedAt ?? resultState.ResultFinalizedAt,
                HasResult: resultState.HasResult,
                IsResultReady: resultState.IsResultReady,
                IsResultFinalized: resultState.IsResultFinalized,
                ResultId: result?.Id,
                ResultStatus: resultState.ResultStatus,
                ResultFinalizedAt: resultState.ResultFinalizedAt,
                ResultVersionNo: latestVersion?.VersionNo ?? (resultState.IsResultFinalized ? 1 : null),
                ResultText: latestVersion?.ResultText,
                Conclusion: latestVersion?.Conclusion,
                ReportFileUrl: latestVersion?.ReportFileUrl,
                PerformedByStaffName: enteredStaff?.FullName ?? auditMeta?.PerformedByStaffName ?? confirmedStaff?.FullName,
                VerifiedByStaffName: verifiedStaff?.FullName ?? finalizedStaff?.FullName,
                IsAdditionalOrder: isAdditionalOrder,
                OrderSequenceNo: orderSequenceNo,
                FollowUpToOrderId: auditMeta?.FollowUpToOrderId,
                FollowUpToOrderCode: followUpToOrderCode,
                AdditionalOrderReason: auditMeta?.AdditionalOrderReason,
                CanConfirmOrder: canConfirmOrder,
                ConfirmDisabledReason: confirmDisabledReason,
                CanReceivePatient: canReceivePatient,
                CanUpdateExecutionStatus: canUpdateExecutionStatus,
                CanEnterResult: canEnterResult,
                CanViewResult: canViewResult,
                AllowedActions: allowedActions));
        }

        return new HospitalWorklistDataset(
            HospitalItems: items,
            ForeignOrders: foreignOrders,
            HospitalDiagnosticServicePoints: hospitalDiagnosticServicePoints,
            RoomsById: roomsById,
            ServicesById: servicesById,
            SpecialtiesById: specialtiesById,
            VisitsById: visitsById,
            QueueEntriesById: queueEntriesById);
    }

    private async Task<Dictionary<long, decimal>> LoadActiveServicePricesAsync(
        IReadOnlyList<long> serviceIds,
        DateTime nowLocal,
        CancellationToken ct)
    {
        var result = new Dictionary<long, decimal>();
        if (serviceIds.Count == 0)
        {
            return result;
        }

        try
        {
            var prices = await _db.ServicePrices
                .AsNoTracking()
                .Where(p =>
                    p.ServiceId.HasValue &&
                    serviceIds.Contains(p.ServiceId.Value) &&
                    p.PriceAmount.HasValue &&
                    (p.EffectiveFrom == null || p.EffectiveFrom <= nowLocal) &&
                    (p.EffectiveTo == null || p.EffectiveTo >= nowLocal))
                .OrderByDescending(p => p.EffectiveFrom ?? DateTime.MinValue)
                .ThenByDescending(p => p.Id)
                .ToListAsync(ct);

            foreach (var price in prices)
            {
                var svcId = price.ServiceId!.Value;
                if (!result.ContainsKey(svcId) && price.PriceAmount.HasValue)
                {
                    result[svcId] = price.PriceAmount.Value;
                }
            }
        }
        catch (NotSupportedException)
        {
            return result;
        }

        return result;
    }

    private async Task<Dictionary<long, MedicalRecordRevision>> LoadLatestMedicalRevisionsByVisitAsync(
        IReadOnlyList<long> visitIds,
        CancellationToken ct)
    {
        var result = new Dictionary<long, MedicalRecordRevision>();
        if (visitIds.Count == 0)
        {
            return result;
        }

        try
        {
            var records = await _db.MedicalRecords
                .AsNoTracking()
                .Where(mr => mr.VisitId.HasValue && visitIds.Contains(mr.VisitId.Value))
                .OrderByDescending(mr => mr.Id)
                .ToListAsync(ct);

            if (records.Count == 0)
            {
                return result;
            }

            var recordIds = records.Select(r => r.Id).Distinct().ToList();
            var revisions = await _db.MedicalRecordRevisions
                .AsNoTracking()
                .Where(rev => rev.MedicalRecordId.HasValue && recordIds.Contains(rev.MedicalRecordId.Value))
                .OrderByDescending(rev => rev.RevisionNo ?? 0)
                .ThenByDescending(rev => rev.RecordedAt ?? DateTime.MinValue)
                .ThenByDescending(rev => rev.Id)
                .ToListAsync(ct);

            var latestRevByRecordId = revisions
                .Where(rev => rev.MedicalRecordId.HasValue)
                .GroupBy(rev => rev.MedicalRecordId!.Value)
                .ToDictionary(g => g.Key, g => g.First());

            foreach (var record in records)
            {
                var vId = record.VisitId!.Value;
                if (!result.ContainsKey(vId) &&
                    latestRevByRecordId.TryGetValue(record.Id, out var latestRev))
                {
                    result[vId] = latestRev;
                }
            }
        }
        catch (NotSupportedException)
        {
            return result;
        }

        return result;
    }

    private async Task<DiagnosticResultsBundle> LoadDiagnosticResultsBundleAsync(
        IReadOnlyList<DiagnosticOrder> orders,
        IReadOnlyDictionary<long, Staff> existingStaffById,
        CancellationToken ct)
    {
        if (orders.Count == 0)
        {
            return new DiagnosticResultsBundle(
                ResultsByOrderId: new Dictionary<long, DiagnosticResult>(),
                VersionsByResultId: new Dictionary<long, IReadOnlyList<DiagnosticResultVersion>>(),
                StaffById: existingStaffById);
        }

        var orderIds = orders.Select(o => o.Id).Distinct().ToList();
        List<DiagnosticResult> rawResults;
        try
        {
            rawResults = await _db.DiagnosticResults
                .AsNoTracking()
                .Where(r => r.DiagnosticOrderId.HasValue && orderIds.Contains(r.DiagnosticOrderId.Value))
                .ToListAsync(ct);
        }
        catch (NotSupportedException)
        {
            rawResults = [];
        }

        var resultsByOrderId = rawResults
            .Where(r => r.DiagnosticOrderId.HasValue)
            .GroupBy(r => r.DiagnosticOrderId!.Value)
            .ToDictionary(
                g => g.Key,
                g => g
                    .OrderByDescending(r =>
                        r.FinalizedAt.HasValue ||
                        AppointmentBookingRules.NormalizeStatusKey(r.Status) is "FINALIZED" or "VERIFIED" or "COMPLETED"
                            ? 1
                            : 0)
                    .ThenByDescending(r => r.FinalizedAt ?? DateTime.MinValue)
                    .ThenByDescending(r => r.Id)
                    .First());

        var resultIds = resultsByOrderId.Values.Select(r => r.Id).Distinct().ToList();
        List<DiagnosticResultVersion> rawVersions = [];
        if (resultIds.Count > 0)
        {
            try
            {
                rawVersions = await _db.DiagnosticResultVersions
                    .AsNoTracking()
                    .Where(v => v.DiagnosticResultId.HasValue && resultIds.Contains(v.DiagnosticResultId.Value))
                    .ToListAsync(ct);
            }
            catch (NotSupportedException)
            {
                rawVersions = [];
            }
        }

        var versionsByResultId = rawVersions
            .Where(v => v.DiagnosticResultId.HasValue)
            .GroupBy(v => v.DiagnosticResultId!.Value)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<DiagnosticResultVersion>)g
                    .OrderByDescending(v => v.VersionNo ?? 0)
                    .ThenByDescending(v => v.IsFinalized == true ? 1 : 0)
                    .ThenByDescending(v => v.VerifiedAt ?? v.EnteredAt ?? DateTime.MinValue)
                    .ThenByDescending(v => v.Id)
                    .ToList());

        return new DiagnosticResultsBundle(
            ResultsByOrderId: resultsByOrderId,
            VersionsByResultId: versionsByResultId,
            StaffById: existingStaffById);
    }

    private async Task<Dictionary<long, DiagnosticOrderAuditMetadata>> TryLoadDiagnosticOrderAuditMetadataAsync(
        IReadOnlyList<string> orderIdStrings,
        CancellationToken ct)
    {
        var result = new Dictionary<long, DiagnosticOrderAuditMetadata>();
        if (orderIdStrings.Count == 0)
        {
            return result;
        }

        try
        {
            var logs = await _db.AuditLogs
                .AsNoTracking()
                .Where(a =>
                    a.EntityType == "DIAGNOSTIC_ORDER" &&
                    a.EntityId != null &&
                    orderIdStrings.Contains(a.EntityId) &&
                    (a.Action == "ADDITIONAL_DIAGNOSTIC_ORDER_CREATED" || a.Action == "DIAGNOSTIC_ORDER_CREATED" ||
                     a.Action == "DIAGNOSTIC_SERVICE_EXECUTION_STARTED" || a.Action == "DIAGNOSTIC_SERVICE_EXECUTION_COMPLETED" ||
                     a.Action == "DIAGNOSTIC_SERVICE_STATUS_UPDATED" || a.Action == "DIAGNOSTIC_SERVICE_EXECUTION_CANCELLED"))
                .OrderByDescending(a => a.OccurredAt)
                .ThenByDescending(a => a.Id)
                .ToListAsync(ct);

            var performedByStaffNameByOrderId = new Dictionary<long, string>();

            foreach (var log in logs)
            {
                if (!long.TryParse(log.EntityId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var orderId))
                {
                    continue;
                }

                var performedByStaffName = performedByStaffNameByOrderId.TryGetValue(orderId, out var prevName)
                    ? prevName
                    : null;

                var isAdditional = string.Equals(
                    log.Action,
                    "ADDITIONAL_DIAGNOSTIC_ORDER_CREATED",
                    StringComparison.OrdinalIgnoreCase);
                int? seqNo = null;
                long? followUpId = null;
                string? followUpCode = null;
                string? additionalReason = null;
                if (!string.IsNullOrWhiteSpace(log.ChangeData))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(log.ChangeData);
                        if (doc.RootElement.ValueKind == JsonValueKind.Object)
                        {
                            var root = doc.RootElement;
                            if (root.TryGetProperty("isAdditionalOrder", out var addProp) &&
                                addProp.ValueKind is JsonValueKind.True or JsonValueKind.False)
                            {
                                isAdditional = isAdditional || addProp.GetBoolean();
                            }

                            if (root.TryGetProperty("orderSequenceNo", out var seqProp) &&
                                seqProp.ValueKind == JsonValueKind.Number &&
                                seqProp.TryGetInt32(out var parsedSeq) &&
                                parsedSeq > 0)
                            {
                                seqNo = parsedSeq;
                            }

                            if (root.TryGetProperty("followUpToOrderId", out var fIdProp) &&
                                fIdProp.ValueKind == JsonValueKind.Number &&
                                fIdProp.TryGetInt64(out var parsedFollowUpId) &&
                                parsedFollowUpId > 0)
                            {
                                followUpId = parsedFollowUpId;
                            }

                            if (root.TryGetProperty("followUpToOrderCode", out var fCodeProp) &&
                                fCodeProp.ValueKind == JsonValueKind.String)
                            {
                                var rawCode = fCodeProp.GetString();
                                if (!string.IsNullOrWhiteSpace(rawCode))
                                {
                                    followUpCode = rawCode.Trim();
                                }
                            }

                            if (root.TryGetProperty("additionalOrderReason", out var reasonProp) &&
                                reasonProp.ValueKind == JsonValueKind.String)
                            {
                                var rawReason = reasonProp.GetString();
                                if (!string.IsNullOrWhiteSpace(rawReason))
                                {
                                    additionalReason = rawReason.Trim();
                                }
                            }

                            if (performedByStaffName is null &&
                                root.TryGetProperty("performedByStaffName", out var perfNameProp) &&
                                perfNameProp.ValueKind == JsonValueKind.String)
                            {
                                var rawPerfName = perfNameProp.GetString();
                                if (!string.IsNullOrWhiteSpace(rawPerfName))
                                {
                                    performedByStaffName = rawPerfName.Trim();
                                }
                            }
                        }
                    }
                    catch (JsonException)
                    {
                        // Ignore malformed JSON in historical audit logs
                    }
                }

                if (performedByStaffName is not null && !performedByStaffNameByOrderId.ContainsKey(orderId))
                {
                    performedByStaffNameByOrderId[orderId] = performedByStaffName;
                }

                if (result.ContainsKey(orderId))
                {
                    continue;
                }

                result[orderId] = new DiagnosticOrderAuditMetadata(
                    IsAdditionalOrder: isAdditional,
                    OrderSequenceNo: seqNo,
                    FollowUpToOrderId: followUpId,
                    FollowUpToOrderCode: followUpCode,
                    AdditionalOrderReason: additionalReason,
                    PerformedByStaffName: performedByStaffName);
            }

            foreach (var kvp in performedByStaffNameByOrderId)
            {
                if (result.TryGetValue(kvp.Key, out var meta) && meta.PerformedByStaffName is null)
                {
                    result[kvp.Key] = meta with { PerformedByStaffName = kvp.Value };
                }
            }
        }
        catch (NotSupportedException)
        {
            return result;
        }

        return result;
    }

    private static Dictionary<long, int> ComputeVisitOrderSequences(IReadOnlyList<DiagnosticOrder> orders)
    {
        var sequenceByOrderId = new Dictionary<long, int>(orders.Count);

        foreach (var group in orders.GroupBy(o => o.VisitId ?? o.Id))
        {
            var chronological = group
                .Where(o => AppointmentBookingRules.NormalizeStatusKey(o.Status) != "CANCELLED")
                .OrderBy(o => o.OrderedAt ?? DateTime.MinValue)
                .ThenBy(o => o.Id)
                .ToList();

            for (var i = 0; i < chronological.Count; i++)
            {
                sequenceByOrderId[chronological[i].Id] = i + 1;
            }
        }

        return sequenceByOrderId;
    }

    private static DateOnly ResolveOrderReferenceDate(
        DiagnosticOrder order,
        QueueEntry? queueEntry,
        Visit? visit,
        DateOnly fallbackDate)
    {
        if (queueEntry?.QueueDate.HasValue == true)
        {
            return queueEntry.QueueDate.Value;
        }

        if (order.OrderedAt.HasValue)
        {
            return DateOnly.FromDateTime(order.OrderedAt.Value);
        }

        if (visit?.CheckedInAt.HasValue == true)
        {
            return DateOnly.FromDateTime(visit.CheckedInAt.Value);
        }

        return fallbackDate;
    }

    private static string FormatOrderCode(DiagnosticOrder order, DateOnly referenceDate)
        => !string.IsNullOrWhiteSpace(order.OrderCode)
            ? order.OrderCode.Trim()
            : $"DIA-{referenceDate:yyyyMM}-{order.Id:D4}";

    private static string ResolveDiagnosticCategory(
        Service service,
        IReadOnlyDictionary<long, Specialty> specialtiesById)
    {
        var specCode = service.SpecialtyId.HasValue &&
                       specialtiesById.TryGetValue(service.SpecialtyId.Value, out var spec)
            ? spec.SpecialtyCode
            : null;

        var code = service.ServiceCode ?? string.Empty;
        var name = service.Name ?? string.Empty;

        if (string.Equals(specCode, "IMG", StringComparison.OrdinalIgnoreCase) ||
            code.Contains("XRAY", StringComparison.OrdinalIgnoreCase) ||
            code.Contains("XQ", StringComparison.OrdinalIgnoreCase) ||
            code.Contains("US", StringComparison.OrdinalIgnoreCase) ||
            code.Contains("SA", StringComparison.OrdinalIgnoreCase) ||
            code.Contains("CT", StringComparison.OrdinalIgnoreCase) ||
            code.Contains("MRI", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("X-quang", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Siêu âm", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Chụp", StringComparison.OrdinalIgnoreCase))
        {
            return "IMAGING";
        }

        if (string.Equals(specCode, "CARD", StringComparison.OrdinalIgnoreCase) ||
            code.Contains("ECG", StringComparison.OrdinalIgnoreCase) ||
            code.Contains("EEG", StringComparison.OrdinalIgnoreCase) ||
            code.Contains("TDCN", StringComparison.OrdinalIgnoreCase) ||
            code.Contains("ENDO", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Điện tâm", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Nội soi", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Thăm dò", StringComparison.OrdinalIgnoreCase))
        {
            return "FUNCTIONAL";
        }

        return "LABORATORY";
    }

    private static string FormatCategoryLabel(string category) => category switch
    {
        "IMAGING" => "Chẩn đoán hình ảnh",
        "FUNCTIONAL" => "Thăm dò chức năng",
        _ => "Xét nghiệm",
    };

    private static (string Code, string Label) NormalizePriority(string? rawPriority)
    {
        var key = AppointmentBookingRules.NormalizeStatusKey(rawPriority);
        return key switch
        {
            "STAT" or "EMERGENCY" => ("STAT", "Cấp cứu STAT"),
            "URGENT" or "HIGH" => ("URGENT", "Khẩn"),
            _ => ("NORMAL", "Bình thường"),
        };
    }

    private static (string Code, string Label) NormalizeGender(string? rawGender)
    {
        var key = AppointmentBookingRules.NormalizeStatusKey(rawGender);
        return key switch
        {
            "MALE" or "NAM" => ("MALE", "Nam"),
            "FEMALE" or "NU" or "NỮ" => ("FEMALE", "Nữ"),
            "OTHER" or "KHAC" or "KHÁC" => ("OTHER", "Khác"),
            _ => ("UNKNOWN", "Chưa xác định"),
        };
    }

    private static string NormalizeQueueStatus(string? rawStatus)
    {
        var key = AppointmentBookingRules.NormalizeStatusKey(rawStatus);
        return string.IsNullOrEmpty(key) ? "WAITING" : key;
    }

    private static DiagnosticOrderResultState ResolveDiagnosticOrderResultState(
        DiagnosticOrder order,
        QueueEntry? queueEntry,
        DiagnosticResult? result,
        DiagnosticResultVersion? latestVersion,
        bool requiresResultVerification)
    {
        var orderStatus = string.IsNullOrWhiteSpace(order.Status)
            ? "ORDERED"
            : AppointmentBookingRules.NormalizeStatusKey(order.Status);
        var queueStatus = queueEntry is not null ? NormalizeQueueStatus(queueEntry.Status) : null;

        if (result is not null)
        {
            var rawResultStatus = AppointmentBookingRules.NormalizeStatusKey(result.Status);
            var isResultFinalized = result.FinalizedAt.HasValue ||
                rawResultStatus is "FINALIZED" or "VERIFIED" or "COMPLETED" ||
                latestVersion?.IsFinalized == true ||
                latestVersion?.VerifiedAt.HasValue == true;
            var isResultReady = isResultFinalized && orderStatus != "CANCELLED";

            string resultStatus;
            if (orderStatus == "CANCELLED" || rawResultStatus == "CANCELLED")
            {
                resultStatus = "CANCELLED";
            }
            else if (isResultFinalized)
            {
                resultStatus = rawResultStatus == "VERIFIED" ? "VERIFIED" : "FINALIZED";
            }
            else if (!string.IsNullOrEmpty(rawResultStatus))
            {
                resultStatus = rawResultStatus;
            }
            else
            {
                resultStatus = requiresResultVerification ? "PENDING_VERIFY" : "DRAFT";
            }

            var finalizedAt = result.FinalizedAt
                ?? latestVersion?.VerifiedAt
                ?? (isResultFinalized ? (queueEntry?.CompletedAt ?? latestVersion?.EnteredAt) : null);

            return new DiagnosticOrderResultState(
                HasResult: true,
                IsResultReady: isResultReady,
                IsResultFinalized: isResultFinalized,
                ResultStatus: resultStatus,
                ResultFinalizedAt: finalizedAt);
        }
        else
        {
            var isResultFinalized = orderStatus is "COMPLETED" or "FINALIZED" or "VERIFIED" or "DONE" ||
                queueEntry?.CompletedAt.HasValue == true ||
                queueStatus == "COMPLETED";
            var isResultReady = isResultFinalized && orderStatus != "CANCELLED";
            var resultStatus = orderStatus == "CANCELLED" || queueStatus == "CANCELLED"
                ? "CANCELLED"
                : isResultFinalized
                    ? "FINALIZED"
                    : "PENDING";
            var finalizedAt = isResultFinalized ? queueEntry?.CompletedAt : null;

            return new DiagnosticOrderResultState(
                HasResult: isResultFinalized,
                IsResultReady: isResultReady,
                IsResultFinalized: isResultFinalized,
                ResultStatus: resultStatus,
                ResultFinalizedAt: finalizedAt);
        }
    }

    private static string ResolveEffectiveWorklistStatus(
        DiagnosticOrder order,
        string rawOrderStatus,
        string? normalizedQueueStatus,
        string normalizedVisitStatus,
        DiagnosticOrderResultState resultState)
    {
        if (rawOrderStatus == "CANCELLED" ||
            normalizedQueueStatus == "CANCELLED" ||
            normalizedVisitStatus == "CANCELLED")
        {
            return "CANCELLED";
        }

        if (rawOrderStatus is "COMPLETED" or "DONE" or "FINALIZED" or "VERIFIED" ||
            resultState.IsResultFinalized ||
            normalizedQueueStatus == "COMPLETED")
        {
            return "COMPLETED";
        }

        if (rawOrderStatus == "IN_PROGRESS" ||
            normalizedQueueStatus == "IN_PROGRESS")
        {
            return "IN_PROGRESS";
        }

        if (rawOrderStatus == "CONFIRMED" ||
            order.ConfirmedAt.HasValue ||
            order.ConfirmedByStaffId.HasValue ||
            normalizedQueueStatus is "READY" or "CALLED")
        {
            return "CONFIRMED";
        }

        return "ORDERED";
    }

    private static string FormatWorklistStatusLabel(string status) => status switch
    {
        "ORDERED" => "Chờ xác nhận",
        "CONFIRMED" => "Đã tiếp nhận",
        "IN_PROGRESS" => "Đang thực hiện",
        "COMPLETED" => "Đã có kết quả",
        "CANCELLED" => "Đã hủy",
        _ => "Chờ xác nhận",
    };

    private static (bool CanConfirm, string? DisabledReason) EvaluateConfirmOrderEligibility(
        string effectiveStatus,
        bool isVisitActive,
        bool isServiceActive,
        bool isServicePointActive,
        bool isRoomActive,
        bool hasConfirmPermission)
    {
        if (effectiveStatus != "ORDERED")
        {
            var statusReason = effectiveStatus switch
            {
                "CONFIRMED" => "Chỉ định cận lâm sàng đã được xác nhận tiếp nhận.",
                "IN_PROGRESS" => "Chỉ định cận lâm sàng đang được thực hiện.",
                "COMPLETED" => "Chỉ định cận lâm sàng đã hoàn tất trả kết quả.",
                "CANCELLED" => "Chỉ định cận lâm sàng đã bị hủy.",
                _ => "Chỉ định không ở trạng thái chờ xác nhận.",
            };
            return (false, statusReason);
        }

        if (!isVisitActive)
        {
            return (false, "Lượt khám của bệnh nhân đã bị hủy.");
        }

        if (!isServiceActive)
        {
            return (false, "Dịch vụ cận lâm sàng hiện đang ngừng hoạt động.");
        }

        if (!isServicePointActive)
        {
            return (false, "Điểm phục vụ cận lâm sàng hiện đang ngừng hoạt động.");
        }

        if (!isRoomActive)
        {
            return (false, "Phòng thực hiện cận lâm sàng hiện đang ngừng hoạt động.");
        }

        if (!hasConfirmPermission)
        {
            return (false, "Nhân viên không có quyền xác nhận chỉ định cận lâm sàng.");
        }

        return (true, null);
    }

    private static IReadOnlyList<DiagnosticWorklistActionDto> BuildAllowedActions(
        bool canConfirmOrder,
        string? confirmDisabledReason,
        bool canReceivePatient,
        bool canUpdateExecutionStatus,
        bool canEnterResult,
        bool canViewResult)
    {
        return
        [
            new DiagnosticWorklistActionDto(
                Id: "confirm-order",
                Label: "Xác nhận chỉ định CLS",
                UcCode: "UC39",
                Enabled: canConfirmOrder,
                DisabledReason: confirmDisabledReason),
            new DiagnosticWorklistActionDto(
                Id: "receive-patient",
                Label: "Tiếp nhận bệnh nhân CLS",
                UcCode: "UC40",
                Enabled: canReceivePatient,
                DisabledReason: canReceivePatient
                    ? null
                    : "Chỉ áp dụng cho chỉ định đã xác nhận và đang chờ gọi vào phòng kỹ thuật."),
            new DiagnosticWorklistActionDto(
                Id: "update-execution",
                Label: "Cập nhật thực hiện kỹ thuật",
                UcCode: "UC42",
                Enabled: canUpdateExecutionStatus,
                DisabledReason: canUpdateExecutionStatus
                    ? null
                    : "Chỉ áp dụng cho chỉ định đã tiếp nhận hoặc đang thực hiện kỹ thuật."),
            new DiagnosticWorklistActionDto(
                Id: "enter-result",
                Label: "Nhập & trả kết quả CLS",
                UcCode: "UC43",
                Enabled: canEnterResult,
                DisabledReason: canEnterResult
                    ? null
                    : "Chỉ áp dụng cho chỉ định đang thực hiện và chưa khóa kết quả cuối."),
            new DiagnosticWorklistActionDto(
                Id: "view-result",
                Label: "Xem kết quả CLS",
                UcCode: "UC35",
                Enabled: canViewResult,
                DisabledReason: canViewResult
                    ? null
                    : "Chưa có kết quả cận lâm sàng để xem."),
        ];
    }

    private static string FormatDoctorName(Doctor? doctor)
    {
        if (doctor is null)
        {
            return "BS. Chỉ định CLS";
        }

        var rawStaffName = doctor.Staff?.FullName;
        if (!string.IsNullOrWhiteSpace(rawStaffName))
        {
            if (!string.IsNullOrWhiteSpace(doctor.ProfessionalTitle) &&
                !rawStaffName.StartsWith(doctor.ProfessionalTitle, StringComparison.OrdinalIgnoreCase))
            {
                return DoctorCatalogService.FormatDisplayName(rawStaffName, doctor.ProfessionalTitle);
            }

            return rawStaffName.Trim();
        }

        return !string.IsNullOrWhiteSpace(doctor.DoctorCode)
            ? doctor.DoctorCode.Trim()
            : $"BS #{doctor.Id}";
    }

    private static List<DiagnosticWorklistItemDto> SortWorklistItems(
        IReadOnlyList<DiagnosticWorklistItemDto> items)
    {
        return items
            .OrderBy(item => GetStatusSortRank(item.Status))
            .ThenBy(item => IsActiveWorklistStatus(item.Status) ? GetPrioritySortRank(item.Priority) : 0)
            .ThenBy(item => IsActiveWorklistStatus(item.Status) ? (item.RequiredForConclusion ? 0 : 1) : 0)
            .ThenBy(item => IsActiveWorklistStatus(item.Status) ? item.OrderedAtTimestamp.Ticks : -item.OrderedAtTimestamp.Ticks)
            .ThenBy(item => IsActiveWorklistStatus(item.Status) ? (item.QueueNumber ?? int.MaxValue) : 0)
            .ThenBy(item => IsActiveWorklistStatus(item.Status) ? item.OrderId : -item.OrderId)
            .ToList();
    }

    private static bool IsActiveWorklistStatus(string status)
        => status is "ORDERED" or "CONFIRMED" or "IN_PROGRESS";

    private static int GetStatusSortRank(string status) => status switch
    {
        "ORDERED" => 0,
        "CONFIRMED" => 1,
        "IN_PROGRESS" => 2,
        "COMPLETED" => 3,
        "CANCELLED" => 4,
        _ => 5,
    };

    private static int GetPrioritySortRank(string priority) => priority switch
    {
        "STAT" => 0,
        "URGENT" => 1,
        _ => 2,
    };

    private static DiagnosticWorklistStatsDto BuildWorklistStats(
        IReadOnlyList<DiagnosticWorklistItemDto> items,
        DateOnly referenceDate)
    {
        var totalOrders = items.Count;
        var pendingOrders = items.Count(x => x.Status == "ORDERED");
        var confirmedOrders = items.Count(x => x.Status == "CONFIRMED");
        var inQueue = items.Count(x =>
            x.Status == "CONFIRMED" ||
            (x.Status is "ORDERED" or "CONFIRMED" &&
             x.QueueStatus is "WAITING" or "READY" or "CALLED"));
        var inExecution = items.Count(x => x.Status == "IN_PROGRESS");
        var completedOrders = items.Count(x => x.Status == "COMPLETED");
        var completedOnRefDate = items.Count(x =>
            x.Status == "COMPLETED" &&
            ((x.CompletedAt.HasValue && DateOnly.FromDateTime(x.CompletedAt.Value) == referenceDate) ||
             DateOnly.FromDateTime(x.OrderedAtTimestamp) == referenceDate));
        var completedToday = completedOnRefDate > 0 ? completedOnRefDate : completedOrders;
        var cancelledOrders = items.Count(x => x.Status == "CANCELLED");
        var urgentOrders = items.Count(x => x.Priority == "URGENT" && x.Status != "CANCELLED");
        var statOrders = items.Count(x => x.Priority == "STAT" && x.Status != "CANCELLED");
        var mandatoryOrders = items.Count(x => x.RequiredForConclusion && x.Status != "CANCELLED");
        var pendingResultVerificationCount = items.Count(x =>
            x.ResultStatus is "PENDING_VERIFY" or "DRAFT" or "PRELIMINARY" ||
            (x.Status == "IN_PROGRESS" && x.RequiresResultVerification && !x.IsResultFinalized));
        var laboratoryCount = items.Count(x => x.Category == "LABORATORY");
        var imagingCount = items.Count(x => x.Category == "IMAGING");
        var functionalCount = items.Count(x => x.Category == "FUNCTIONAL");

        return new DiagnosticWorklistStatsDto(
            TotalOrders: totalOrders,
            PendingOrders: pendingOrders,
            ConfirmedOrders: confirmedOrders,
            InQueue: inQueue,
            InExecution: inExecution,
            CompletedOrders: completedOrders,
            CompletedToday: completedToday,
            CancelledOrders: cancelledOrders,
            UrgentOrders: urgentOrders,
            StatOrders: statOrders,
            MandatoryOrders: mandatoryOrders,
            PendingResultVerificationCount: pendingResultVerificationCount,
            LaboratoryCount: laboratoryCount,
            ImagingCount: imagingCount,
            FunctionalCount: functionalCount);
    }

    private static IReadOnlyList<DiagnosticServicePointWorklistDto> BuildServicePointBuckets(
        IReadOnlyList<QueueServicePoint> hospitalServicePoints,
        IReadOnlyDictionary<long, HospitalRoom> roomsById,
        IReadOnlyDictionary<long, Service> servicesById,
        IReadOnlyDictionary<long, Specialty> specialtiesById,
        IReadOnlyList<DiagnosticWorklistItemDto> sortedItems)
    {
        var buckets = new List<DiagnosticServicePointWorklistDto>();
        var handledSpIds = new HashSet<long>();

        foreach (var sp in hospitalServicePoints)
        {
            handledSpIds.Add(sp.Id);
            roomsById.TryGetValue(sp.RoomId ?? 0, out var room);
            servicesById.TryGetValue(sp.ServiceId ?? 0, out var service);

            var spItems = sortedItems
                .Where(item => item.ServicePointId == sp.Id)
                .ToList();

            var category = service is not null
                ? ResolveDiagnosticCategory(service, specialtiesById)
                : (spItems.FirstOrDefault()?.Category ?? "LABORATORY");

            var currentServingTicketNo = spItems
                .FirstOrDefault(x => x.Status == "IN_PROGRESS" || x.QueueStatus is "IN_PROGRESS" or "CALLED")
                ?.TicketNo;

            var nextWaitingTicketNo = spItems
                .FirstOrDefault(x => x.Status is "CONFIRMED" or "ORDERED" && x.QueueStatus is "WAITING" or "READY")
                ?.TicketNo;

            buckets.Add(new DiagnosticServicePointWorklistDto(
                ServicePointId: sp.Id,
                FormattedServicePointId: $"sp-{sp.Id:D3}",
                ServicePointName: !string.IsNullOrWhiteSpace(sp.Name) ? sp.Name : $"Điểm CLS #{sp.Id}",
                RoomId: room?.Id ?? sp.RoomId,
                RoomCode: room?.RoomCode ?? $"RM-{(sp.RoomId ?? sp.Id):D3}",
                RoomName: room?.Name ?? sp.Name,
                RoomLocation: room?.Location,
                ServiceId: service?.Id ?? sp.ServiceId,
                ServiceCode: service?.ServiceCode,
                ServiceName: service?.Name,
                Category: category,
                IsActive: (sp.IsActive ?? true) &&
                          (room is null || string.Equals(room.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase)),
                CurrentServingTicketNo: currentServingTicketNo,
                NextWaitingTicketNo: nextWaitingTicketNo,
                PendingOrdersCount: spItems.Count(x => x.Status == "ORDERED"),
                ConfirmedCount: spItems.Count(x => x.Status == "CONFIRMED"),
                InProgressCount: spItems.Count(x => x.Status == "IN_PROGRESS"),
                CompletedCount: spItems.Count(x => x.Status == "COMPLETED"),
                CancelledCount: spItems.Count(x => x.Status == "CANCELLED"),
                TotalOrdersCount: spItems.Count));
        }

        var unassignedGroups = sortedItems
            .Where(item => !item.ServicePointId.HasValue || !handledSpIds.Contains(item.ServicePointId.Value))
            .GroupBy(item => item.ServicePointId ?? -(item.RoomId ?? item.ServiceId));

        foreach (var group in unassignedGroups)
        {
            var spItems = group.ToList();
            var sample = spItems[0];
            var spId = sample.ServicePointId;

            var currentServingTicketNo = spItems
                .FirstOrDefault(x => x.Status == "IN_PROGRESS" || x.QueueStatus is "IN_PROGRESS" or "CALLED")
                ?.TicketNo;

            var nextWaitingTicketNo = spItems
                .FirstOrDefault(x => x.Status is "CONFIRMED" or "ORDERED" && x.QueueStatus is "WAITING" or "READY")
                ?.TicketNo;

            buckets.Add(new DiagnosticServicePointWorklistDto(
                ServicePointId: spId,
                FormattedServicePointId: sample.FormattedServicePointId ?? $"sp-room-{sample.RoomId ?? sample.ServiceId}",
                ServicePointName: sample.ServicePointName ?? sample.RoomName ?? $"Điểm CLS - {sample.Room}",
                RoomId: sample.RoomId,
                RoomCode: sample.RoomCode ?? sample.Room,
                RoomName: sample.RoomName ?? sample.Room,
                RoomLocation: sample.RoomLocation,
                ServiceId: sample.ServiceId,
                ServiceCode: sample.ServiceCode,
                ServiceName: sample.ServiceName,
                Category: sample.Category,
                IsActive: true,
                CurrentServingTicketNo: currentServingTicketNo,
                NextWaitingTicketNo: nextWaitingTicketNo,
                PendingOrdersCount: spItems.Count(x => x.Status == "ORDERED"),
                ConfirmedCount: spItems.Count(x => x.Status == "CONFIRMED"),
                InProgressCount: spItems.Count(x => x.Status == "IN_PROGRESS"),
                CompletedCount: spItems.Count(x => x.Status == "COMPLETED"),
                CancelledCount: spItems.Count(x => x.Status == "CANCELLED"),
                TotalOrdersCount: spItems.Count));
        }

        return buckets;
    }

    private static List<DiagnosticWorklistItemDto> ApplyEntryFilters(
        IReadOnlyList<DiagnosticWorklistItemDto> items,
        ValidatedWorklistFilter filter)
    {
        IEnumerable<DiagnosticWorklistItemDto> query = items;

        if (filter.Category is not null)
        {
            query = query.Where(item =>
                string.Equals(item.Category, filter.Category, StringComparison.OrdinalIgnoreCase));
        }

        if (filter.Status is not null)
        {
            query = query.Where(item =>
                string.Equals(item.Status, filter.Status, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.OrderStatus, filter.Status, StringComparison.OrdinalIgnoreCase));
        }

        if (filter.QueueStatus is not null)
        {
            query = query.Where(item =>
                string.Equals(item.QueueStatus, filter.QueueStatus, StringComparison.OrdinalIgnoreCase));
        }

        if (filter.ResultStatus is not null)
        {
            query = query.Where(item =>
                string.Equals(item.ResultStatus, filter.ResultStatus, StringComparison.OrdinalIgnoreCase));
        }

        if (filter.Priority is not null)
        {
            query = query.Where(item =>
                string.Equals(item.Priority, filter.Priority, StringComparison.OrdinalIgnoreCase));
        }

        if (filter.ServicePointId is not null)
        {
            query = query.Where(item => MatchesServicePointFilter(item, filter.ServicePointId));
        }

        if (filter.RoomId is not null)
        {
            query = query.Where(item => MatchesRoomFilter(item, filter.RoomId));
        }

        if (filter.ServiceId is not null)
        {
            query = query.Where(item => MatchesServiceFilter(item, filter.ServiceId));
        }

        if (filter.VisitId is not null)
        {
            query = query.Where(item => MatchesVisitFilter(item, filter.VisitId));
        }

        if (filter.PatientId is not null)
        {
            query = query.Where(item => MatchesPatientFilter(item, filter.PatientId));
        }

        if (filter.DoctorId is not null)
        {
            query = query.Where(item => MatchesDoctorFilter(item, filter.DoctorId));
        }

        if (filter.RequiredForConclusion.HasValue)
        {
            query = query.Where(item => item.RequiredForConclusion == filter.RequiredForConclusion.Value);
        }

        if (filter.Keyword is not null)
        {
            var kw = filter.Keyword;
            query = query.Where(item =>
                item.OrderCode.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                item.Id.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                item.TicketNo.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                item.PatientName.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                item.PatientCode.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                item.VisitCode.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                item.ServiceName.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                item.ServiceCode.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                item.Room.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                (item.RoomName is not null && item.RoomName.Contains(kw, StringComparison.OrdinalIgnoreCase)) ||
                (item.ServicePointName is not null && item.ServicePointName.Contains(kw, StringComparison.OrdinalIgnoreCase)) ||
                item.OrderingDoctor.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                (item.OrderingDoctorCode is not null && item.OrderingDoctorCode.Contains(kw, StringComparison.OrdinalIgnoreCase)) ||
                (item.Instruction is not null && item.Instruction.Contains(kw, StringComparison.OrdinalIgnoreCase)) ||
                (item.ReasonForVisit is not null && item.ReasonForVisit.Contains(kw, StringComparison.OrdinalIgnoreCase)));
        }

        return query.ToList();
    }

    private static bool MatchesDateFilter(DiagnosticWorklistItemDto item, DateOnly filterDate)
    {
        var isoDate = filterDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (string.Equals(item.QueueDate, isoDate, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return DateOnly.FromDateTime(item.OrderedAtTimestamp) == filterDate;
    }

    private static bool MatchesServicePointFilter(DiagnosticWorklistItemDto item, string filter)
    {
        if (long.TryParse(filter, NumberStyles.None, CultureInfo.InvariantCulture, out var numericId))
        {
            return item.ServicePointId == numericId;
        }

        if (filter.StartsWith("sp-", StringComparison.OrdinalIgnoreCase) &&
            long.TryParse(filter[3..], NumberStyles.None, CultureInfo.InvariantCulture, out var spNumericId))
        {
            return item.ServicePointId == spNumericId;
        }

        return string.Equals(item.FormattedServicePointId, filter, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(item.ServicePointName, filter, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesRoomFilter(DiagnosticWorklistItemDto item, string filter)
    {
        if (long.TryParse(filter, NumberStyles.None, CultureInfo.InvariantCulture, out var numericId))
        {
            return item.RoomId == numericId;
        }

        return string.Equals(item.RoomCode, filter, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(item.Room, filter, StringComparison.OrdinalIgnoreCase) ||
               (item.RoomId.HasValue && string.Equals($"rm-{item.RoomId.Value:D3}", filter, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesServiceFilter(DiagnosticWorklistItemDto item, string filter)
    {
        if (long.TryParse(filter, NumberStyles.None, CultureInfo.InvariantCulture, out var numericId))
        {
            return item.ServiceId == numericId;
        }

        return string.Equals(item.ServiceCode, filter, StringComparison.OrdinalIgnoreCase) ||
               string.Equals($"srv-{item.ServiceId:D3}", filter, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesVisitFilter(DiagnosticWorklistItemDto item, string filter)
    {
        if (long.TryParse(filter, NumberStyles.None, CultureInfo.InvariantCulture, out var numericId))
        {
            return item.VisitId == numericId;
        }

        return string.Equals(item.VisitCode, filter, StringComparison.OrdinalIgnoreCase) ||
               string.Equals($"vis-{item.VisitId:D4}", filter, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesPatientFilter(DiagnosticWorklistItemDto item, string filter)
    {
        if (long.TryParse(filter, NumberStyles.None, CultureInfo.InvariantCulture, out var numericId))
        {
            return item.PatientId == numericId;
        }

        return string.Equals(item.PatientCode, filter, StringComparison.OrdinalIgnoreCase) ||
               string.Equals($"pat-{item.PatientId:D4}", filter, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesDoctorFilter(DiagnosticWorklistItemDto item, string filter)
    {
        if (long.TryParse(filter, NumberStyles.None, CultureInfo.InvariantCulture, out var numericId))
        {
            return item.OrderingDoctorId == numericId;
        }

        return string.Equals(item.OrderingDoctorCode, filter, StringComparison.OrdinalIgnoreCase) ||
               (item.OrderingDoctorId.HasValue &&
                string.Equals($"doc-{item.OrderingDoctorId.Value:D3}", filter, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesOrderItemIdentifier(DiagnosticWorklistItemDto item, string identifier)
    {
        if (long.TryParse(identifier, NumberStyles.None, CultureInfo.InvariantCulture, out var numericId))
        {
            return item.OrderId == numericId;
        }

        if (identifier.StartsWith("do-", StringComparison.OrdinalIgnoreCase) &&
            long.TryParse(identifier[3..], NumberStyles.None, CultureInfo.InvariantCulture, out var doNumericId))
        {
            return item.OrderId == doNumericId;
        }

        if (identifier.StartsWith("q-", StringComparison.OrdinalIgnoreCase) &&
            long.TryParse(identifier[2..], NumberStyles.None, CultureInfo.InvariantCulture, out var qNumericId))
        {
            return item.QueueEntryId == qNumericId;
        }

        return string.Equals(item.Id, identifier, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(item.OrderCode, identifier, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(item.FormattedQueueEntryId, identifier, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(item.TicketNo, identifier, StringComparison.OrdinalIgnoreCase) ||
               string.Equals($"CLS-{item.OrderedAtTimestamp:yyyy}-{item.OrderId:D4}", identifier, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesRawOrderIdentifier(
        DiagnosticOrder order,
        QueueEntry? queueEntry,
        DateOnly referenceDate,
        string identifier)
    {
        if (long.TryParse(identifier, NumberStyles.None, CultureInfo.InvariantCulture, out var numericId))
        {
            return order.Id == numericId;
        }

        if (identifier.StartsWith("do-", StringComparison.OrdinalIgnoreCase) &&
            long.TryParse(identifier[3..], NumberStyles.None, CultureInfo.InvariantCulture, out var doNumericId))
        {
            return order.Id == doNumericId;
        }

        if (identifier.StartsWith("q-", StringComparison.OrdinalIgnoreCase) &&
            long.TryParse(identifier[2..], NumberStyles.None, CultureInfo.InvariantCulture, out var qNumericId))
        {
            return order.QueueEntryId == qNumericId;
        }

        var formattedCode = FormatOrderCode(order, referenceDate);
        var clsCode = $"CLS-{referenceDate:yyyy}-{order.Id:D4}";
        var ticketNo = queueEntry?.QueueNumber is > 0 ? $"C{queueEntry.QueueNumber.Value:D3}" : null;

        return string.Equals(order.OrderCode?.Trim(), identifier, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(formattedCode, identifier, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(clsCode, identifier, StringComparison.OrdinalIgnoreCase) ||
               (ticketNo is not null && string.Equals(ticketNo, identifier, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<DiagnosticQuickActionDto> BuildQuickActions(
        IReadOnlyCollection<string> permissionCodes)
    {
        var permissionSet = new HashSet<string>(permissionCodes, StringComparer.OrdinalIgnoreCase);
        var hasQueueManage = permissionSet.Contains("QUEUE_MANAGE");
        var hasResultWrite = permissionSet.Contains("DIAGNOSTIC_RESULT_WRITE");
        var hasDiagnosticOrder = permissionSet.Contains("DIAGNOSTIC_ORDER");

        var actions = new List<DiagnosticQuickActionDto>();

        if (hasQueueManage || hasResultWrite || hasDiagnosticOrder)
        {
            actions.Add(new DiagnosticQuickActionDto(
                Id: "confirm-order",
                Label: "Xác nhận chỉ định CLS",
                Description: "Kiểm tra và xác nhận phiếu chỉ định mới từ bác sĩ",
                Icon: "M9 5H7a2 2 0 00-2 2v12a2 2 0 002 2h10a2 2 0 002-2V7a2 2 0 00-2-2h-2M9 5a2 2 0 002 2h2a2 2 0 002-2M9 5a2 2 0 012-2h2a2 2 0 012 2m-6 9l2 2 4-4",
                Accent: "sky",
                UcCode: "UC39"));
        }

        if (hasQueueManage)
        {
            actions.Add(new DiagnosticQuickActionDto(
                Id: "receive-patient",
                Label: "Tiếp nhận bệnh nhân CLS",
                Description: "Quét mã lượt khám và xếp vào hàng đợi phòng kỹ thuật",
                Icon: "M13 5l7 7-7 7M5 5l7 7-7 7",
                Accent: "emerald",
                UcCode: "UC40"));
        }

        if (hasQueueManage || hasResultWrite)
        {
            actions.Add(new DiagnosticQuickActionDto(
                Id: "update-execution",
                Label: "Cập nhật thực hiện kỹ thuật",
                Description: "Ghi nhận trạng thái lấy mẫu, chụp chiếu hoặc thăm dò",
                Icon: "M19.428 15.428a2 2 0 00-1.022-.547l-2.387-.477a6 6 0 00-3.86.517l-.318.158a6 6 0 01-3.86.517L6.05 15.21a2 2 0 00-1.806.547M8 4h8l-1 1v5.172a2 2 0 00.586 1.414l5 5c1.26 1.26.367 3.414-1.415 3.414H4.828c-1.782 0-2.674-2.154-1.414-3.414l5-5A2 2 0 009 10.172V5L8 4z",
                Accent: "amber",
                UcCode: "UC42"));
        }

        if (hasResultWrite)
        {
            actions.Add(new DiagnosticQuickActionDto(
                Id: "enter-result",
                Label: "Nhập & trả kết quả CLS",
                Description: "Nhập chỉ số, kết luận và chuyển kết quả về phòng khám",
                Icon: "M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z",
                Accent: "rose",
                UcCode: "UC43"));
        }

        return actions;
    }

    private sealed record ValidatedWorklistFilter(
        int Page,
        int PageSize,
        string? Category,
        string? Status,
        string? QueueStatus,
        string? ResultStatus,
        string? Priority,
        DateOnly? FilterDate,
        string? ServicePointId,
        string? RoomId,
        string? ServiceId,
        string? VisitId,
        string? PatientId,
        string? DoctorId,
        bool? RequiredForConclusion,
        string? Keyword);

    private sealed record AuthorizedDiagnosticStaffContext(
        User ActorUser,
        Staff ActorStaff,
        Hospital Hospital,
        Department? StaffDepartment,
        string DepartmentName,
        IReadOnlyDictionary<long, Department> DepartmentsById,
        DiagnosticStaffProfileDto StaffProfile);

    private sealed record HospitalWorklistDataset(
        IReadOnlyList<DiagnosticWorklistItemDto> HospitalItems,
        IReadOnlyList<DiagnosticOrder> ForeignOrders,
        IReadOnlyList<QueueServicePoint> HospitalDiagnosticServicePoints,
        IReadOnlyDictionary<long, HospitalRoom> RoomsById,
        IReadOnlyDictionary<long, Service> ServicesById,
        IReadOnlyDictionary<long, Specialty> SpecialtiesById,
        IReadOnlyDictionary<long, Visit> VisitsById,
        IReadOnlyDictionary<long, QueueEntry> QueueEntriesById);

    private sealed record DiagnosticResultsBundle(
        IReadOnlyDictionary<long, DiagnosticResult> ResultsByOrderId,
        IReadOnlyDictionary<long, IReadOnlyList<DiagnosticResultVersion>> VersionsByResultId,
        IReadOnlyDictionary<long, Staff> StaffById);

    private sealed record DiagnosticOrderResultState(
        bool HasResult,
        bool IsResultReady,
        bool IsResultFinalized,
        string ResultStatus,
        DateTime? ResultFinalizedAt);

    private sealed record DiagnosticOrderAuditMetadata(
        bool IsAdditionalOrder,
        int? OrderSequenceNo,
        long? FollowUpToOrderId,
        string? FollowUpToOrderCode,
        string? AdditionalOrderReason,
        string? PerformedByStaffName);
}
