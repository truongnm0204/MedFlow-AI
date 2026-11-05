using System.Globalization;
using System.Text.Json;
using MedFlow.Application.DTOs.HospitalAdmin;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Policies;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.HospitalAdmin;

public sealed class HospitalAdminDashboardService : IHospitalAdminDashboardService
{
    private const string HospitalAdminRoleCode = "HOSPITAL_ADMIN";
    private const string DefaultShiftLabel = "Ca hành chính • Đồng bộ cấu hình HIS trực tuyến";

    private static readonly string[] FallbackHospitalAdminPermissions =
    [
        "HOSPITAL_CONFIG",
        "PAYMENT_VIEW",
        "QUEUE_MANAGE",
        "VISIT_ASSIGN",
    ];

    private static readonly HashSet<string> ValidCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "DEPARTMENT_ROOM",
        "DOCTOR_SERVICE",
        "SLOT_SCHEDULE",
        "POLICY_REPORT",
    };

    private static readonly HashSet<string> ValidStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "ACTIVE",
        "NEEDS_REVIEW",
        "MAINTENANCE",
    };

    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;

    public HospitalAdminDashboardService(
        IApplicationDbContext dbContext,
        TimeProvider? timeProvider = null)
    {
        _db = dbContext;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<HospitalAdminDashboardResponseDto> GetDashboardAsync(
        long actorUserId,
        GetHospitalAdminDashboardRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.WorklistLimit is < 1 or > 50)
        {
            throw new ValidationException("Số lượng hạng mục cấu hình hiển thị phải từ 1 đến 50.");
        }

        string? normalizedCategory = null;
        if (!string.IsNullOrWhiteSpace(request.Category) &&
            !string.Equals(request.Category.Trim(), "ALL", StringComparison.OrdinalIgnoreCase))
        {
            var trimmedCategory = request.Category.Trim();
            if (!ValidCategories.Contains(trimmedCategory))
            {
                throw new ValidationException("Nhóm danh mục cấu hình lọc không hợp lệ.");
            }

            normalizedCategory = trimmedCategory.ToUpperInvariant();
        }

        string? normalizedStatus = null;
        if (!string.IsNullOrWhiteSpace(request.Status) &&
            !string.Equals(request.Status.Trim(), "ALL", StringComparison.OrdinalIgnoreCase))
        {
            var trimmedStatus = request.Status.Trim();
            if (!ValidStatuses.Contains(trimmedStatus))
            {
                throw new ValidationException("Trạng thái cấu hình lọc không hợp lệ.");
            }

            normalizedStatus = trimmedStatus.ToUpperInvariant();
        }

        var normalizedKeyword = string.IsNullOrWhiteSpace(request.Keyword)
            ? null
            : request.Keyword.Trim();

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        var actorUser = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == actorUserId, ct)
            ?? throw new NotFoundException("User", actorUserId);

        if (actorUser.AccountStatus != AccountStatus.ACTIVE)
        {
            throw new ForbiddenException("Tài khoản quản trị bệnh viện không ở trạng thái hoạt động.");
        }

        if (actorUser.LockedUntil.HasValue && actorUser.LockedUntil.Value > nowUtc)
        {
            throw new ForbiddenException("Tài khoản quản trị bệnh viện đang bị tạm khóa.");
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

        if (!actorRoleCodes.Any(c => string.Equals(c, HospitalAdminRoleCode, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ForbiddenException("Người dùng không có quyền truy cập bảng điều khiển Quản trị bệnh viện.");
        }

        var actorStaff = await _db.Staff
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == actorUserId, ct);

        if (actorStaff is null || !actorStaff.HospitalId.HasValue)
        {
            throw new ForbiddenException("Tài khoản quản trị bệnh viện chưa được gán cơ sở y tế.");
        }

        if (!string.Equals(actorStaff.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            throw new ForbiddenException("Hồ sơ nhân sự của quản trị viên bệnh viện đã ngừng hoạt động.");
        }

        var hospitalId = actorStaff.HospitalId.Value;

        var hospital = await _db.Hospitals
            .AsNoTracking()
            .FirstOrDefaultAsync(h => h.Id == hospitalId, ct)
            ?? throw new NotFoundException("Hospital", hospitalId);

        if (hospital.IsActive == false)
        {
            throw new ForbiddenException("Cơ sở y tế được gán cho quản trị viên hiện đang ngừng hoạt động.");
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
            actorPermissionCodes = [.. FallbackHospitalAdminPermissions];
        }

        var fullName = !string.IsNullOrWhiteSpace(actorStaff.FullName)
            ? actorStaff.FullName
            : actorUser.Email;

        var adminProfile = new HospitalAdminProfileDto(
            UserId: actorUser.Id,
            Email: actorUser.Email,
            FullName: fullName,
            StaffCode: actorStaff.StaffCode,
            HospitalId: hospital.Id,
            HospitalCode: hospital.HospitalCode,
            HospitalName: hospital.Name,
            Roles: actorRoleCodes,
            Permissions: actorPermissionCodes);

        var departments = await _db.Departments
            .AsNoTracking()
            .Where(d => d.HospitalId == hospitalId)
            .OrderBy(d => d.Id)
            .ToListAsync(ct);

        var departmentsById = departments.ToDictionary(d => d.Id);
        var totalDepartments = departments.Count;
        var activeDepartments = departments.Count(d => d.IsActive == true);

        var rooms = await _db.HospitalRooms
            .AsNoTracking()
            .Where(r => r.HospitalId == hospitalId)
            .OrderBy(r => r.Id)
            .ToListAsync(ct);

        var roomsById = rooms.ToDictionary(r => r.Id);
        var roomIds = roomsById.Keys.ToHashSet();
        var totalRooms = rooms.Count;
        var activeRooms = rooms.Count(r => string.Equals(r.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase));

        var doctors = await _db.Doctors
            .AsNoTracking()
            .Include(d => d.Staff)
            .Include(d => d.Specialty)
            .Where(d => d.HospitalId == hospitalId ||
                        (d.HospitalId == null && d.Staff != null && d.Staff.HospitalId == hospitalId))
            .OrderBy(d => d.Id)
            .ToListAsync(ct);

        var doctorsById = doctors.ToDictionary(d => d.Id);
        var doctorIds = doctorsById.Keys.ToHashSet();
        var activeDoctors = doctors.Count(d =>
            d.IsActive &&
            (d.Staff == null || string.Equals(d.Staff.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase)));

        var services = await _db.Services
            .AsNoTracking()
            .Include(s => s.ServicePrices)
            .Where(s => s.HospitalId == hospitalId)
            .OrderBy(s => s.Id)
            .ToListAsync(ct);

        var servicesById = services.ToDictionary(s => s.Id);
        var serviceIds = servicesById.Keys.ToHashSet();
        var onlineServicesCount = services.Count(s => (s.IsActive ?? true) && s.AllowsOnlineBooking == true);

        var allSlots = await _db.ExaminationSlots
            .AsNoTracking()
            .OrderBy(s => s.StartsAt)
            .ThenBy(s => s.Id)
            .ToListAsync(ct);

        var scopedSlots = allSlots
            .Where(s =>
                (s.ServiceId.HasValue && serviceIds.Contains(s.ServiceId.Value)) ||
                (s.DoctorId.HasValue && doctorIds.Contains(s.DoctorId.Value)) ||
                (s.RoomId.HasValue && roomIds.Contains(s.RoomId.Value)))
            .ToList();

        var scopedSlotIds = scopedSlots.Select(s => s.Id).ToHashSet();

        var appointments = scopedSlotIds.Count == 0
            ? []
            : await _db.Appointments
                .AsNoTracking()
                .Where(a => a.SlotId.HasValue && scopedSlotIds.Contains(a.SlotId.Value))
                .ToListAsync(ct);

        var appointmentCountMap = appointments
            .Where(a => a.SlotId.HasValue && !string.Equals(a.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
            .GroupBy(a => a.SlotId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        var nowLocal = _timeProvider.GetLocalNow().DateTime;
        var today = nowLocal.Date;
        var tomorrow = today.AddDays(1);

        var openSlotsTodayCount = scopedSlots.Count(s =>
            string.Equals(s.Status, "OPEN", StringComparison.OrdinalIgnoreCase) &&
            s.StartsAt.HasValue &&
            s.StartsAt.Value >= today &&
            s.StartsAt.Value < tomorrow);

        if (openSlotsTodayCount == 0)
        {
            openSlotsTodayCount = scopedSlots.Count(s =>
                string.Equals(s.Status, "OPEN", StringComparison.OrdinalIgnoreCase));
        }

        var reviewSlotsCount = scopedSlots.Count(s => IsSlotNeedsReview(s, appointmentCountMap));

        var stats = new HospitalAdminStatsDto(
            ActiveDepartments: activeDepartments,
            TotalDepartments: totalDepartments,
            ActiveRooms: activeRooms,
            TotalRooms: totalRooms,
            ActiveDoctors: activeDoctors,
            OnlineServicesCount: onlineServicesCount,
            OpenSlotsToday: openSlotsTodayCount,
            ReviewSlotsCount: reviewSlotsCount);

        var policyRows = await _db.HospitalPolicies
            .AsNoTracking()
            .Where(p => p.HospitalId == hospitalId &&
                        p.PolicyType == AppointmentPolicyKeys.PolicyType &&
                        p.QueueServicePointId == null)
            .ToListAsync(ct);

        var currentPolicies = policyRows
            .Where(r => (r.EffectiveFrom == null || r.EffectiveFrom <= nowUtc) &&
                        (r.EffectiveTo == null || r.EffectiveTo > nowUtc))
            .GroupBy(r => r.PolicyKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(r => r.PolicyVersion ?? 0)
                      .ThenByDescending(r => r.Id)
                      .First(),
                StringComparer.OrdinalIgnoreCase);

        currentPolicies.TryGetValue(AppointmentPolicyKeys.BookingWindowDays, out var bookingWindowRow);
        currentPolicies.TryGetValue(AppointmentPolicyKeys.CancelHours, out var cancelHoursRow);

        var bookingWindowDays = ReadPolicyInt(
            bookingWindowRow,
            AppointmentPolicyKeys.PropMaxDaysAhead,
            AppointmentPolicyKeys.DefaultBookingWindowDays);

        var cancelHours = ReadPolicyInt(
            cancelHoursRow,
            AppointmentPolicyKeys.PropMinimumHours,
            AppointmentPolicyKeys.DefaultCancelMinimumHours);

        var policyUpdatedLabel = currentPolicies.Values
            .Where(r => r.EffectiveFrom.HasValue)
            .Select(r => r.EffectiveFrom!.Value)
            .DefaultIfEmpty(nowUtc)
            .Max()
            .ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

        var scopedAppointmentIds = appointments.Select(a => a.Id).ToHashSet();
        var hospitalVisitCount = scopedAppointmentIds.Count == 0
            ? 0
            : await _db.Visits
                .AsNoTracking()
                .CountAsync(v => v.AppointmentId.HasValue && scopedAppointmentIds.Contains(v.AppointmentId.Value), ct);

        if (hospitalVisitCount == 0 && appointments.Count > 0)
        {
            hospitalVisitCount = appointments.Count(a => !string.Equals(a.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase));
        }

        var fullWorklist = BuildWorklist(
            hospital,
            departments,
            departmentsById,
            rooms,
            roomsById,
            doctors,
            services,
            servicesById,
            scopedSlots,
            appointmentCountMap,
            bookingWindowDays,
            cancelHours,
            policyUpdatedLabel,
            hospitalVisitCount,
            reviewSlotsCount,
            nowUtc);

        IEnumerable<HospitalConfigWorklistItemDto> filteredWorklist = fullWorklist;

        if (normalizedCategory is not null)
        {
            filteredWorklist = filteredWorklist.Where(item =>
                string.Equals(item.Category, normalizedCategory, StringComparison.OrdinalIgnoreCase));
        }

        if (normalizedStatus is not null)
        {
            filteredWorklist = filteredWorklist.Where(item =>
                string.Equals(item.Status, normalizedStatus, StringComparison.OrdinalIgnoreCase));
        }

        if (normalizedKeyword is not null)
        {
            filteredWorklist = filteredWorklist.Where(item =>
                item.Code.Contains(normalizedKeyword, StringComparison.OrdinalIgnoreCase) ||
                item.Name.Contains(normalizedKeyword, StringComparison.OrdinalIgnoreCase) ||
                item.DepartmentOrScope.Contains(normalizedKeyword, StringComparison.OrdinalIgnoreCase) ||
                item.MetricLabel.Contains(normalizedKeyword, StringComparison.OrdinalIgnoreCase));
        }

        var worklist = filteredWorklist
            .Take(request.WorklistLimit)
            .ToList();

        var quickActions = BuildQuickActions(actorPermissionCodes);
        var policyAlerts = BuildPolicyAlerts(
            rooms,
            roomsById,
            servicesById,
            scopedSlots,
            appointmentCountMap,
            bookingWindowDays,
            cancelHours);

        return new HospitalAdminDashboardResponseDto(
            FacilityName: hospital.Name,
            FacilityCode: hospital.HospitalCode,
            ShiftLabel: DefaultShiftLabel,
            Stats: stats,
            Worklist: worklist,
            QuickActions: quickActions,
            PolicyAlerts: policyAlerts,
            AdminProfile: adminProfile,
            GeneratedAtUtc: nowUtc);
    }

    private static List<HospitalConfigWorklistItemDto> BuildWorklist(
        Hospital hospital,
        IReadOnlyList<Department> departments,
        IReadOnlyDictionary<long, Department> departmentsById,
        IReadOnlyList<HospitalRoom> rooms,
        IReadOnlyDictionary<long, HospitalRoom> roomsById,
        IReadOnlyList<Doctor> doctors,
        IReadOnlyList<Service> services,
        IReadOnlyDictionary<long, Service> servicesById,
        IReadOnlyList<ExaminationSlot> scopedSlots,
        IReadOnlyDictionary<long, int> appointmentCountMap,
        int bookingWindowDays,
        int cancelHours,
        string policyUpdatedLabel,
        int hospitalVisitCount,
        int reviewSlotsCount,
        DateTime nowUtc)
    {
        var items = new List<HospitalConfigWorklistItemDto>();

        foreach (var dept in departments)
        {
            var deptRoomCount = rooms.Count(r => r.DepartmentId == dept.Id);
            var deptDoctorCount = doctors.Count(d => d.Staff != null && d.Staff.DepartmentId == dept.Id);
            var isActive = dept.IsActive == true;
            var code = !string.IsNullOrWhiteSpace(dept.DepartmentCode)
                ? dept.DepartmentCode
                : $"DEP-{dept.Id:D3}";

            items.Add(new HospitalConfigWorklistItemDto(
                Id: $"cfg-dep-{dept.Id:D3}",
                Code: code,
                Name: dept.Name,
                Category: "DEPARTMENT_ROOM",
                DepartmentOrScope: hospital.Name,
                MetricLabel: isActive
                    ? $"{deptRoomCount:D2} buồng khám • {deptDoctorCount:D2} BS"
                    : "Tạm ngừng hoạt động khoa phòng",
                UpdatedAt: "Hôm nay",
                Status: isActive ? "ACTIVE" : "MAINTENANCE",
                Route: "/admin/departments"));
        }

        foreach (var room in rooms)
        {
            var isRoomActive = string.Equals(room.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase);
            var code = !string.IsNullOrWhiteSpace(room.RoomCode)
                ? room.RoomCode
                : $"RM-{room.Id:D3}";

            var deptName = room.DepartmentId.HasValue && departmentsById.TryGetValue(room.DepartmentId.Value, out var dept)
                ? dept.Name
                : hospital.Name;

            var scope = !string.IsNullOrWhiteSpace(room.Location)
                ? $"{deptName} • {room.Location}"
                : deptName;

            items.Add(new HospitalConfigWorklistItemDto(
                Id: $"cfg-rm-{room.Id:D3}",
                Code: code,
                Name: room.Name,
                Category: "DEPARTMENT_ROOM",
                DepartmentOrScope: scope,
                MetricLabel: isRoomActive
                    ? $"Sức chứa {room.Capacity ?? 1} • Đang hoạt động"
                    : "Tạm ngừng bảo trì thiết bị",
                UpdatedAt: "Hôm nay",
                Status: isRoomActive ? "ACTIVE" : "MAINTENANCE",
                Route: "/admin/rooms"));
        }

        foreach (var doc in doctors)
        {
            var isDoctorActive = doc.IsActive &&
                (doc.Staff == null || string.Equals(doc.Staff.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase));

            var code = !string.IsNullOrWhiteSpace(doc.DoctorCode)
                ? doc.DoctorCode
                : $"DOC-{doc.Id:D4}";

            var rawStaffName = doc.Staff?.FullName;
            var doctorName = !string.IsNullOrWhiteSpace(rawStaffName)
                ? (!string.IsNullOrWhiteSpace(doc.ProfessionalTitle) &&
                   !rawStaffName.StartsWith(doc.ProfessionalTitle, StringComparison.OrdinalIgnoreCase)
                    ? $"{doc.ProfessionalTitle} {rawStaffName}"
                    : rawStaffName)
                : code;

            var scope = doc.Staff?.DepartmentId != null &&
                        departmentsById.TryGetValue(doc.Staff.DepartmentId.Value, out var docDept)
                ? docDept.Name
                : (!string.IsNullOrWhiteSpace(doc.Specialty?.Name) ? doc.Specialty.Name : hospital.Name);

            var licenseLabel = !string.IsNullOrWhiteSpace(doc.LicenseNumber)
                ? doc.LicenseNumber
                : "Đang cập nhật";

            items.Add(new HospitalConfigWorklistItemDto(
                Id: $"cfg-doc-{doc.Id:D4}",
                Code: code,
                Name: doctorName,
                Category: "DOCTOR_SERVICE",
                DepartmentOrScope: scope,
                MetricLabel: isDoctorActive
                    ? $"CCHN {licenseLabel} • Đang mở"
                    : "Tạm ngừng lịch khám",
                UpdatedAt: "Hôm nay",
                Status: isDoctorActive ? "ACTIVE" : "MAINTENANCE",
                Route: "/admin/doctors"));
        }

        foreach (var service in services)
        {
            var isServiceActive = service.IsActive ?? true;
            var code = !string.IsNullOrWhiteSpace(service.ServiceCode)
                ? service.ServiceCode
                : $"SRV-{service.Id:D3}";

            var scope = service.DepartmentId.HasValue &&
                        departmentsById.TryGetValue(service.DepartmentId.Value, out var srvDept)
                ? srvDept.Name
                : hospital.Name;

            var activePrice = ResolveActivePrice(service.ServicePrices, nowUtc);
            var bookingModeLabel = (service.AllowsOnlineBooking ?? false)
                ? "Mở đặt hẹn online"
                : "Đặt tại quầy";

            items.Add(new HospitalConfigWorklistItemDto(
                Id: $"cfg-srv-{service.Id:D3}",
                Code: code,
                Name: service.Name,
                Category: "DOCTOR_SERVICE",
                DepartmentOrScope: scope,
                MetricLabel: isServiceActive
                    ? $"{FormatVndPrice(activePrice)} • {bookingModeLabel}"
                    : "Tạm ngừng cung cấp dịch vụ",
                UpdatedAt: "Hôm nay",
                Status: isServiceActive ? "ACTIVE" : "MAINTENANCE",
                Route: "/admin/services"));
        }

        var representativeSlots = scopedSlots
            .OrderByDescending(s => IsSlotNeedsReview(s, appointmentCountMap))
            .ThenBy(s => s.StartsAt)
            .ThenBy(s => s.Id)
            .Take(3);

        foreach (var slot in representativeSlots)
        {
            var needsReview = IsSlotNeedsReview(slot, appointmentCountMap);
            var srvName = slot.ServiceId.HasValue && servicesById.TryGetValue(slot.ServiceId.Value, out var slotSrv)
                ? slotSrv.Name
                : "Khám chuyên khoa";

            var timeWindow = slot.StartsAt.HasValue && slot.EndsAt.HasValue
                ? $"({slot.StartsAt.Value:HH:mm} - {slot.EndsAt.Value:HH:mm})"
                : string.Empty;

            var slotName = string.IsNullOrEmpty(timeWindow)
                ? $"Khung giờ {srvName}"
                : $"Khung giờ {srvName} {timeWindow}";

            var roomLabel = slot.RoomId.HasValue && roomsById.TryGetValue(slot.RoomId.Value, out var slotRoom)
                ? slotRoom.RoomCode
                : "Chưa gán phòng";

            var deptName = slot.RoomId.HasValue &&
                           roomsById.TryGetValue(slot.RoomId.Value, out var r2) &&
                           r2.DepartmentId.HasValue &&
                           departmentsById.TryGetValue(r2.DepartmentId.Value, out var d2)
                ? d2.Name
                : hospital.Name;

            var bookedCount = appointmentCountMap.TryGetValue(slot.Id, out var b) ? b : 0;
            var capacity = slot.Capacity ?? 0;

            items.Add(new HospitalConfigWorklistItemDto(
                Id: $"cfg-slt-{slot.Id:D3}",
                Code: $"SLT-{slot.Id:D3}",
                Name: slotName,
                Category: "SLOT_SCHEDULE",
                DepartmentOrScope: $"{deptName} • {roomLabel}",
                MetricLabel: needsReview
                    ? $"Đã đặt {bookedCount}/{capacity} • Cần rà soát sức chứa"
                    : $"Đã đặt {bookedCount}/{capacity} • Đang mở nhận khách",
                UpdatedAt: "Hôm nay",
                Status: needsReview ? "NEEDS_REVIEW" : "ACTIVE",
                Route: "/admin/slots"));
        }

        items.Add(new HospitalConfigWorklistItemDto(
            Id: "cfg-pol-apt",
            Code: "POL-APT",
            Name: "Chính sách đặt lịch & hủy hẹn ngoại trú",
            Category: "POLICY_REPORT",
            DepartmentOrScope: $"Toàn {hospital.Name}",
            MetricLabel: $"Đặt trước {bookingWindowDays} ngày • Hủy trước {cancelHours} giờ",
            UpdatedAt: policyUpdatedLabel,
            Status: "ACTIVE",
            Route: "/admin/slots"));

        items.Add(new HospitalConfigWorklistItemDto(
            Id: "cfg-rpt-ops",
            Code: "RPT-OPS",
            Name: "Báo cáo lượt khám & công suất theo khoa",
            Category: "POLICY_REPORT",
            DepartmentOrScope: $"{hospital.Name} • {hospitalVisitCount} lượt",
            MetricLabel: reviewSlotsCount > 0
                ? $"Có {reviewSlotsCount} slot cần rà soát • Cần làm mới"
                : "Vận hành ổn định • Đã đồng bộ",
            UpdatedAt: "Hôm nay",
            Status: reviewSlotsCount > 0 ? "NEEDS_REVIEW" : "ACTIVE",
            Route: "/admin/reports"));

        return items;
    }

    private static IReadOnlyList<HospitalAdminQuickActionDto> BuildQuickActions(
        IReadOnlyCollection<string> permissionCodes)
    {
        var permissionSet = new HashSet<string>(permissionCodes, StringComparer.OrdinalIgnoreCase);
        var hasHospitalConfig = permissionSet.Contains("HOSPITAL_CONFIG");
        var hasReportsAccess = hasHospitalConfig ||
                               permissionSet.Contains("PAYMENT_VIEW") ||
                               permissionSet.Contains("QUEUE_MANAGE");

        var actions = new List<HospitalAdminQuickActionDto>();

        if (hasHospitalConfig)
        {
            actions.AddRange(
            [
                new HospitalAdminQuickActionDto(
                    Id: "manage-departments",
                    Label: "Quản lý Khoa phòng",
                    Description: "Cấu hình danh mục chuyên khoa & trạng thái hoạt động",
                    UcCode: "UC47",
                    Shortcut: "F1",
                    Route: "/admin/departments",
                    Accent: "sky",
                    Icon: "departments"),
                new HospitalAdminQuickActionDto(
                    Id: "manage-rooms",
                    Label: "Quản lý Buồng khám",
                    Description: "Phân bổ buồng khám ngoại trú, vị trí tầng & bảo trì",
                    UcCode: "UC48",
                    Shortcut: "F2",
                    Route: "/admin/rooms",
                    Accent: "emerald",
                    Icon: "rooms"),
                new HospitalAdminQuickActionDto(
                    Id: "manage-doctors",
                    Label: "Quản lý Bác sĩ",
                    Description: "Hồ sơ bác sĩ, chứng chỉ hành nghề & phân công khoa",
                    UcCode: "UC49",
                    Shortcut: "F3",
                    Route: "/admin/doctors",
                    Accent: "sky",
                    Icon: "doctors"),
                new HospitalAdminQuickActionDto(
                    Id: "manage-services",
                    Label: "Quản lý Dịch vụ khám",
                    Description: "Danh mục dịch vụ khám, kỹ thuật CLS & biểu phí niêm yết",
                    UcCode: "UC50",
                    Shortcut: "F4",
                    Route: "/admin/services",
                    Accent: "amber",
                    Icon: "services"),
                new HospitalAdminQuickActionDto(
                    Id: "manage-slots",
                    Label: "Quản lý Slot khám",
                    Description: "Khung giờ khám theo bác sĩ, sức chứa & chính sách hẹn",
                    UcCode: "UC53",
                    Shortcut: "F5",
                    Route: "/admin/slots",
                    Accent: "emerald",
                    Icon: "slots"),
            ]);
        }

        if (hasReportsAccess)
        {
            actions.Add(new HospitalAdminQuickActionDto(
                Id: "view-operational-reports",
                Label: "Báo cáo vận hành",
                Description: "Thống kê lượt tiếp nhận, thời gian chờ & công suất phòng",
                UcCode: "UC56",
                Shortcut: "F6",
                Route: "/admin/reports",
                Accent: "rose",
                Icon: "reports"));
        }

        return actions;
    }

    private static IReadOnlyList<HospitalPolicyAlertItemDto> BuildPolicyAlerts(
        IReadOnlyList<HospitalRoom> rooms,
        IReadOnlyDictionary<long, HospitalRoom> roomsById,
        IReadOnlyDictionary<long, Service> servicesById,
        IReadOnlyList<ExaminationSlot> scopedSlots,
        IReadOnlyDictionary<long, int> appointmentCountMap,
        int bookingWindowDays,
        int cancelHours)
    {
        var alerts = new List<HospitalPolicyAlertItemDto>();

        var alertSlot = scopedSlots.FirstOrDefault(s => IsSlotNeedsReview(s, appointmentCountMap));
        if (alertSlot is not null)
        {
            var booked = appointmentCountMap.TryGetValue(alertSlot.Id, out var count) ? count : 0;
            var capacity = alertSlot.Capacity ?? 0;
            var roomCode = alertSlot.RoomId.HasValue && roomsById.TryGetValue(alertSlot.RoomId.Value, out var room)
                ? room.RoomCode
                : "Chưa gán phòng";
            var srvName = alertSlot.ServiceId.HasValue && servicesById.TryGetValue(alertSlot.ServiceId.Value, out var srv)
                ? srv.Name
                : "Khám ngoại trú";
            var timeWindow = alertSlot.StartsAt.HasValue && alertSlot.EndsAt.HasValue
                ? $"{alertSlot.StartsAt.Value:HH:mm} - {alertSlot.EndsAt.Value:HH:mm}"
                : "Hôm nay";

            alerts.Add(new HospitalPolicyAlertItemDto(
                Id: $"alert-slt-{alertSlot.Id:D3}",
                Code: $"SLT-{alertSlot.Id:D3}",
                Title: $"Khung giờ {srvName} {timeWindow} cần rà soát vận hành",
                Detail: $"Đã đặt {booked}/{capacity} lượt tại {roomCode} • Khuyến nghị rà soát sức chứa hoặc mở thêm slot dự phòng.",
                Severity: "WARNING",
                Route: "/admin/slots"));
        }

        var maintenanceRoom = rooms.FirstOrDefault(r => !string.Equals(r.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase));
        if (maintenanceRoom is not null)
        {
            var roomCode = !string.IsNullOrWhiteSpace(maintenanceRoom.RoomCode)
                ? maintenanceRoom.RoomCode
                : $"RM-{maintenanceRoom.Id:D3}";

            alerts.Add(new HospitalPolicyAlertItemDto(
                Id: $"alert-rm-{maintenanceRoom.Id:D3}",
                Code: roomCode,
                Title: $"{maintenanceRoom.Name} đang tạm ngừng bảo trì",
                Detail: $"Cần điều phối bệnh nhân đặt hẹn tại {roomCode} sang buồng khám dự phòng cùng chuyên khoa.",
                Severity: "WARNING",
                Route: "/admin/rooms"));
        }

        alerts.Add(new HospitalPolicyAlertItemDto(
            Id: "alert-pol-apt",
            Code: "POL-APT",
            Title: $"Chính sách đặt lịch trước {bookingWindowDays} ngày & hủy trước {cancelHours} giờ đang hiệu lực",
            Detail: "Áp dụng đồng bộ trên Cổng Bệnh nhân và Quầy Tiếp đón ngoại trú.",
            Severity: "ACTIVE",
            Route: "/admin/reports"));

        return alerts;
    }

    private static bool IsSlotNeedsReview(
        ExaminationSlot slot,
        IReadOnlyDictionary<long, int> appointmentCountMap)
    {
        if (string.Equals(slot.Status, "CLOSED", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!slot.DoctorId.HasValue || !slot.ServiceId.HasValue || !slot.RoomId.HasValue)
        {
            return true;
        }

        if (slot.Capacity.HasValue &&
            slot.Capacity.Value > 0 &&
            appointmentCountMap.TryGetValue(slot.Id, out var booked) &&
            booked >= slot.Capacity.Value)
        {
            return true;
        }

        return false;
    }

    private static decimal? ResolveActivePrice(IEnumerable<ServicePrice>? prices, DateTime nowUtc)
    {
        if (prices is null)
        {
            return null;
        }

        var activePrice = prices
            .Where(p => p.PriceAmount.HasValue &&
                        (!p.EffectiveFrom.HasValue || p.EffectiveFrom.Value <= nowUtc) &&
                        (!p.EffectiveTo.HasValue || p.EffectiveTo.Value > nowUtc))
            .OrderByDescending(p => p.EffectiveFrom ?? DateTime.MinValue)
            .ThenByDescending(p => p.Id)
            .FirstOrDefault();

        activePrice ??= prices
            .Where(p => p.PriceAmount.HasValue)
            .OrderByDescending(p => p.EffectiveFrom ?? DateTime.MinValue)
            .ThenByDescending(p => p.Id)
            .FirstOrDefault();

        return activePrice?.PriceAmount;
    }

    private static string FormatVndPrice(decimal? price)
    {
        if (!price.HasValue || price.Value <= 0)
        {
            return "Chưa niêm yết";
        }

        return string.Format(CultureInfo.GetCultureInfo("vi-VN"), "{0:N0}đ", price.Value);
    }

    private static int ReadPolicyInt(HospitalPolicy? row, string propertyName, int defaultValue)
    {
        if (row is null || string.IsNullOrWhiteSpace(row.PolicyValue))
        {
            return defaultValue;
        }

        try
        {
            using var doc = JsonDocument.Parse(row.PolicyValue);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty(propertyName, out var prop) &&
                prop.TryGetInt32(out var value))
            {
                return value;
            }
        }
        catch (JsonException)
        {
            // Fallback to defaultValue when JSON is malformed.
        }

        return defaultValue;
    }
}
