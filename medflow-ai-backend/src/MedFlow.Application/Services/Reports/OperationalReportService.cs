using System.Globalization;
using MedFlow.Application.DTOs.Reports;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Reports;

public sealed class OperationalReportService : IOperationalReportService
{
    private const string HospitalAdminRoleCode = "HOSPITAL_ADMIN";
    private const string SystemAdminRoleCode = "SYSTEM_ADMIN";
    private const int MaxReportRangeDays = 366;
    private static readonly TimeSpan VietnamUtcOffset = TimeSpan.FromHours(7);

    private static readonly HashSet<string> AllowedReportPermissions = new(StringComparer.OrdinalIgnoreCase)
    {
        "REPORT_VIEW",
        "HOSPITAL_CONFIG",
        "PAYMENT_VIEW",
        "QUEUE_MANAGE",
    };

    private static readonly int[] OperationalHours = [7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17];

    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;

    public OperationalReportService(
        IApplicationDbContext dbContext,
        TimeProvider? timeProvider = null)
    {
        _db = dbContext;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<OperationalReportResponseDto> GetOperationalReportAsync(
        long actorUserId,
        GetOperationalReportRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (actorUserId <= 0)
        {
            throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");
        }

        if (request.DepartmentId.HasValue && request.DepartmentId.Value <= 0)
        {
            throw new ValidationException("Mã khoa phòng lọc báo cáo không hợp lệ.");
        }

        if (request.ServiceId.HasValue && request.ServiceId.Value <= 0)
        {
            throw new ValidationException("Mã dịch vụ lọc báo cáo không hợp lệ.");
        }

        if (request.HospitalId.HasValue && request.HospitalId.Value <= 0)
        {
            throw new ValidationException("Mã cơ sở y tế lọc báo cáo không hợp lệ.");
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var todayVn = DateOnly.FromDateTime(ToVietnamLocal(nowUtc));

        var effectiveTo = request.ToDate ?? (request.FromDate.HasValue
            ? (request.FromDate.Value.AddDays(6) > todayVn && request.FromDate.Value <= todayVn
                ? todayVn
                : request.FromDate.Value.AddDays(6))
            : todayVn);
        var effectiveFrom = request.FromDate ?? effectiveTo.AddDays(-6);

        if (effectiveFrom > effectiveTo)
        {
            throw new ValidationException("Ngày bắt đầu báo cáo không được lớn hơn ngày kết thúc.");
        }

        if (effectiveTo.DayNumber - effectiveFrom.DayNumber > MaxReportRangeDays)
        {
            throw new ValidationException("Khoảng thời gian truy vấn báo cáo vận hành tối đa 366 ngày.");
        }

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

        var actorRoleIds = (await _db.UserRoles
            .AsNoTracking()
            .Where(ur => ur.UserId == actorUserId)
            .Select(ur => ur.RoleId)
            .ToListAsync(ct))
            .ToHashSet();

        var actorRoleCodes = await _db.Roles
            .AsNoTracking()
            .Where(r => actorRoleIds.Contains(r.Id))
            .Select(r => r.Code)
            .ToListAsync(ct);

        var isSystemAdmin = actorRoleCodes.Any(c =>
            string.Equals(c, SystemAdminRoleCode, StringComparison.OrdinalIgnoreCase));
        var isHospitalAdmin = actorRoleCodes.Any(c =>
            string.Equals(c, HospitalAdminRoleCode, StringComparison.OrdinalIgnoreCase));

        if (!isSystemAdmin && !isHospitalAdmin)
        {
            throw new ForbiddenException("Người dùng không có quyền xem báo cáo vận hành (UC56).");
        }

        if (!isSystemAdmin)
        {
            var allRolePermissions = await _db.RolePermissions
                .AsNoTracking()
                .Where(rp => actorRoleIds.Contains(rp.RoleId))
                .ToListAsync(ct);

            if (allRolePermissions.Count > 0)
            {
                var permIds = allRolePermissions.Select(rp => rp.PermissionId).ToHashSet();
                var permCodes = await _db.Permissions
                    .AsNoTracking()
                    .Where(p => permIds.Contains(p.Id))
                    .Select(p => p.Code)
                    .ToListAsync(ct);

                if (!permCodes.Any(code => AllowedReportPermissions.Contains(code)))
                {
                    throw new ForbiddenException("Vai trò hiện tại chưa được cấp quyền truy cập báo cáo vận hành.");
                }
            }
        }

        long? scopedHospitalId;
        string facilityCode;
        string facilityName;

        if (!isSystemAdmin)
        {
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

            if (request.HospitalId.HasValue && request.HospitalId.Value != actorStaff.HospitalId.Value)
            {
                throw new ForbiddenException("Quản trị viên bệnh viện không được phép xem báo cáo của cơ sở y tế khác.");
            }

            scopedHospitalId = actorStaff.HospitalId.Value;

            var hospital = await _db.Hospitals
                .AsNoTracking()
                .FirstOrDefaultAsync(h => h.Id == scopedHospitalId.Value, ct)
                ?? throw new NotFoundException("Hospital", scopedHospitalId.Value);

            if (hospital.IsActive == false)
            {
                throw new ForbiddenException("Cơ sở y tế được gán hiện đang ngừng hoạt động.");
            }

            facilityCode = hospital.HospitalCode;
            facilityName = hospital.Name;
        }
        else if (request.HospitalId.HasValue)
        {
            scopedHospitalId = request.HospitalId.Value;
            var hospital = await _db.Hospitals
                .AsNoTracking()
                .FirstOrDefaultAsync(h => h.Id == scopedHospitalId.Value, ct)
                ?? throw new NotFoundException("Hospital", scopedHospitalId.Value);

            facilityCode = hospital.HospitalCode;
            facilityName = hospital.Name;
        }
        else
        {
            scopedHospitalId = null;
            facilityCode = "ALL";
            facilityName = "Toàn hệ thống MedFlow";
        }

        var departmentsQuery = _db.Departments.AsNoTracking();
        if (scopedHospitalId.HasValue)
        {
            departmentsQuery = departmentsQuery.Where(d => d.HospitalId == scopedHospitalId.Value);
        }

        var departments = await departmentsQuery
            .OrderBy(d => d.Id)
            .ToListAsync(ct);
        var departmentsById = departments.ToDictionary(d => d.Id);

        if (request.DepartmentId.HasValue)
        {
            var deptEntity = await _db.Departments
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == request.DepartmentId.Value, ct)
                ?? throw new NotFoundException("Department", request.DepartmentId.Value);

            if (scopedHospitalId.HasValue && deptEntity.HospitalId != scopedHospitalId.Value)
            {
                throw new ForbiddenException("Khoa phòng được chọn không thuộc cơ sở y tế của bạn.");
            }
        }

        if (request.ServiceId.HasValue)
        {
            var srvEntity = await _db.Services
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == request.ServiceId.Value, ct)
                ?? throw new NotFoundException("Service", request.ServiceId.Value);

            if (scopedHospitalId.HasValue && srvEntity.HospitalId != scopedHospitalId.Value)
            {
                throw new ForbiddenException("Dịch vụ được chọn không thuộc cơ sở y tế của bạn.");
            }
        }

        var rooms = await _db.HospitalRooms
            .AsNoTracking()
            .Where(r => !scopedHospitalId.HasValue || r.HospitalId == scopedHospitalId.Value)
            .ToListAsync(ct);
        var roomsById = rooms.ToDictionary(r => r.Id);

        var staffMembers = await _db.Staff
            .AsNoTracking()
            .Where(s => !scopedHospitalId.HasValue || s.HospitalId == scopedHospitalId.Value)
            .ToListAsync(ct);
        var staffById = staffMembers.ToDictionary(s => s.Id);

        var doctors = await _db.Doctors
            .AsNoTracking()
            .Include(d => d.Staff)
            .Where(d =>
                !scopedHospitalId.HasValue ||
                d.HospitalId == scopedHospitalId.Value ||
                (d.HospitalId == null && d.Staff != null && d.Staff.HospitalId == scopedHospitalId.Value))
            .ToListAsync(ct);
        var doctorsById = doctors.ToDictionary(d => d.Id);

        var services = await _db.Services
            .AsNoTracking()
            .Where(s => !scopedHospitalId.HasValue || s.HospitalId == scopedHospitalId.Value)
            .ToListAsync(ct);
        var servicesById = services.ToDictionary(s => s.Id);

        var allSlots = await _db.ExaminationSlots
            .AsNoTracking()
            .ToListAsync(ct);

        var scopedSlotsById = allSlots
            .Where(slot => IsSlotInHospitalScope(slot, scopedHospitalId, servicesById, doctorsById, roomsById))
            .ToDictionary(slot => slot.Id);

        var allAppointments = await _db.Appointments
            .AsNoTracking()
            .ToListAsync(ct);

        var appointmentsById = allAppointments.ToDictionary(a => a.Id);

        var filteredAppointments = allAppointments
            .Where(a =>
            {
                ExaminationSlot? slot = null;
                if (a.SlotId.HasValue && !scopedSlotsById.TryGetValue(a.SlotId.Value, out slot))
                {
                    return false;
                }

                if (!a.SlotId.HasValue && scopedHospitalId.HasValue)
                {
                    return false;
                }

                var refTimestamp = slot?.StartsAt ?? a.BookedAt;
                if (!refTimestamp.HasValue)
                {
                    return false;
                }

                var localDate = DateOnly.FromDateTime(ToVietnamLocal(refTimestamp.Value));
                if (localDate < effectiveFrom || localDate > effectiveTo)
                {
                    return false;
                }

                var resolvedDeptId = ResolveSlotDepartmentId(slot, servicesById, roomsById, doctorsById);
                if (request.DepartmentId.HasValue && resolvedDeptId != request.DepartmentId.Value)
                {
                    return false;
                }

                if (request.ServiceId.HasValue && slot?.ServiceId != request.ServiceId.Value)
                {
                    return false;
                }

                return true;
            })
            .ToList();

        var allVisits = await _db.Visits
            .AsNoTracking()
            .Where(v => v.CheckedInAt.HasValue)
            .ToListAsync(ct);

        var resolvedVisits = new List<ResolvedVisitRecord>();

        foreach (var visit in allVisits)
        {
            if (string.Equals(visit.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var localCheckIn = ToVietnamLocal(visit.CheckedInAt!.Value);
            var localDate = DateOnly.FromDateTime(localCheckIn);
            if (localDate < effectiveFrom || localDate > effectiveTo)
            {
                continue;
            }

            Appointment? linkedAppointment = null;
            ExaminationSlot? linkedSlot = null;
            if (visit.AppointmentId.HasValue &&
                appointmentsById.TryGetValue(visit.AppointmentId.Value, out linkedAppointment) &&
                linkedAppointment.SlotId.HasValue)
            {
                scopedSlotsById.TryGetValue(linkedAppointment.SlotId.Value, out linkedSlot);
            }

            var visitHospitalId = ResolveVisitHospitalId(
                visit,
                linkedSlot,
                roomsById,
                doctorsById,
                staffById,
                servicesById);

            if (scopedHospitalId.HasValue && visitHospitalId != scopedHospitalId.Value)
            {
                continue;
            }

            var visitDeptId = ResolveVisitDepartmentId(
                visit,
                linkedSlot,
                roomsById,
                doctorsById,
                staffById,
                servicesById);

            if (request.DepartmentId.HasValue && visitDeptId != request.DepartmentId.Value)
            {
                continue;
            }

            var visitServiceId = linkedSlot?.ServiceId;
            if (request.ServiceId.HasValue)
            {
                if (visitServiceId.HasValue)
                {
                    if (visitServiceId.Value != request.ServiceId.Value)
                    {
                        continue;
                    }
                }
                else if (servicesById.TryGetValue(request.ServiceId.Value, out var targetService))
                {
                    if (targetService.DepartmentId != visitDeptId)
                    {
                        continue;
                    }
                }
                else
                {
                    continue;
                }
            }

            resolvedVisits.Add(new ResolvedVisitRecord(
                Visit: visit,
                LocalCheckIn: localCheckIn,
                LocalDate: localDate,
                DepartmentId: visitDeptId));
        }

        var visitIds = resolvedVisits.Select(rv => rv.Visit.Id).ToHashSet();

        var queueEntries = visitIds.Count == 0
            ? []
            : await _db.QueueEntries
                .AsNoTracking()
                .Where(q => q.VisitId.HasValue && visitIds.Contains(q.VisitId.Value))
                .ToListAsync(ct);

        var waitMinutesByVisitId = new Dictionary<long, List<double>>();
        var allWaitSamples = new List<double>();

        foreach (var q in queueEntries)
        {
            if (!q.VisitId.HasValue || !q.ArrivedAt.HasValue)
            {
                continue;
            }

            var endMark = q.CalledAt ?? q.StartedAt;
            if (!endMark.HasValue || endMark.Value < q.ArrivedAt.Value)
            {
                continue;
            }

            var minutes = (endMark.Value - q.ArrivedAt.Value).TotalMinutes;
            allWaitSamples.Add(minutes);

            if (!waitMinutesByVisitId.TryGetValue(q.VisitId.Value, out var list))
            {
                list = [];
                waitMinutesByVisitId[q.VisitId.Value] = list;
            }

            list.Add(minutes);
        }

        var totalVisits = resolvedVisits.Count;
        var totalAppointments = filteredAppointments.Count(a =>
            !string.Equals(a.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase));
        var completedVisits = resolvedVisits.Count(rv =>
            string.Equals(rv.Visit.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase));
        var inProgressVisits = resolvedVisits.Count(rv => IsInProgressStatus(rv.Visit.Status));
        var waitingVisits = Math.Max(0, totalVisits - completedVisits - inProgressVisits);
        var walkInVisits = resolvedVisits.Count(rv => !rv.Visit.AppointmentId.HasValue);
        var noShowAppointments = filteredAppointments.Count(a => IsNoShowStatus(a.Status));
        var completionRate = totalVisits > 0
            ? Math.Round(completedVisits * 100.0 / totalVisits, 1)
            : 0.0;

        var averageWait = ComputeAverage(allWaitSamples);
        var medianWait = ComputeMedian(allWaitSamples);

        var summary = new OperationalReportSummaryDto(
            TotalVisits: totalVisits,
            TotalAppointments: totalAppointments,
            CompletedVisits: completedVisits,
            InProgressVisits: inProgressVisits,
            WaitingVisits: waitingVisits,
            WalkInVisits: walkInVisits,
            NoShowAppointments: noShowAppointments,
            CompletionRatePercent: completionRate,
            AverageWaitMinutes: averageWait,
            MedianWaitMinutes: medianWait);

        var periodLabel = effectiveFrom == effectiveTo
            ? effectiveFrom.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
            : $"{effectiveFrom:dd/MM/yyyy} - {effectiveTo:dd/MM/yyyy}";

        var hasAnyActivity = totalVisits > 0 || totalAppointments > 0 || noShowAppointments > 0;

        IReadOnlyList<OperationalDailyVisitPointDto> dailyVisits = !hasAnyActivity
            ? []
            : BuildDailyVisits(effectiveFrom, effectiveTo, resolvedVisits);

        IReadOnlyList<OperationalHourlyIntakePointDto> hourlyIntake = totalVisits == 0
            ? []
            : BuildHourlyIntake(resolvedVisits);

        IReadOnlyList<OperationalDepartmentVisitItemDto> departmentVisits = totalVisits == 0
            ? []
            : BuildDepartmentVisits(resolvedVisits, departmentsById);

        IReadOnlyList<OperationalVisitStatusSegmentDto> statusSegments = totalVisits == 0
            ? []
            : BuildStatusSegments(totalVisits, completedVisits, inProgressVisits, waitingVisits);

        IReadOnlyList<OperationalDepartmentRowDto> departmentRows = !hasAnyActivity
            ? []
            : BuildDepartmentRows(
                resolvedVisits,
                filteredAppointments,
                scopedSlotsById,
                servicesById,
                roomsById,
                doctorsById,
                departmentsById,
                waitMinutesByVisitId,
                facilityName,
                periodLabel);

        var availableDepartments = departments
            .Select(d => new OperationalReportDepartmentOptionDto(
                Id: d.Id,
                Code: !string.IsNullOrWhiteSpace(d.DepartmentCode) ? d.DepartmentCode : $"DEP-{d.Id:D3}",
                Name: d.Name))
            .ToList();

        return new OperationalReportResponseDto(
            HospitalId: scopedHospitalId,
            FacilityCode: facilityCode,
            FacilityName: facilityName,
            FromDate: effectiveFrom,
            ToDate: effectiveTo,
            PeriodLabel: periodLabel,
            SelectedDepartmentId: request.DepartmentId,
            SelectedServiceId: request.ServiceId,
            GeneratedAtUtc: nowUtc,
            Summary: summary,
            DailyVisits: dailyVisits,
            HourlyIntake: hourlyIntake,
            DepartmentVisits: departmentVisits,
            VisitStatusSegments: statusSegments,
            DepartmentRows: departmentRows,
            AvailableDepartments: availableDepartments);
    }

    private sealed record ResolvedVisitRecord(
        Visit Visit,
        DateTime LocalCheckIn,
        DateOnly LocalDate,
        long? DepartmentId);

    private static DateTime ToVietnamLocal(DateTime timestamp)
    {
        if (timestamp.Kind == DateTimeKind.Local)
        {
            return timestamp.ToUniversalTime().Add(VietnamUtcOffset);
        }

        return timestamp.Add(VietnamUtcOffset);
    }

    private static bool IsSlotInHospitalScope(
        ExaminationSlot slot,
        long? scopedHospitalId,
        IReadOnlyDictionary<long, Service> servicesById,
        IReadOnlyDictionary<long, Doctor> doctorsById,
        IReadOnlyDictionary<long, HospitalRoom> roomsById)
    {
        if (!scopedHospitalId.HasValue)
        {
            return true;
        }

        return (slot.ServiceId.HasValue && servicesById.ContainsKey(slot.ServiceId.Value)) ||
               (slot.DoctorId.HasValue && doctorsById.ContainsKey(slot.DoctorId.Value)) ||
               (slot.RoomId.HasValue && roomsById.ContainsKey(slot.RoomId.Value));
    }

    private static long? ResolveSlotDepartmentId(
        ExaminationSlot? slot,
        IReadOnlyDictionary<long, Service> servicesById,
        IReadOnlyDictionary<long, HospitalRoom> roomsById,
        IReadOnlyDictionary<long, Doctor> doctorsById)
    {
        if (slot is null)
        {
            return null;
        }

        if (slot.RoomId.HasValue && roomsById.TryGetValue(slot.RoomId.Value, out var room) && room.DepartmentId.HasValue)
        {
            return room.DepartmentId.Value;
        }

        if (slot.ServiceId.HasValue && servicesById.TryGetValue(slot.ServiceId.Value, out var service) && service.DepartmentId.HasValue)
        {
            return service.DepartmentId.Value;
        }

        if (slot.DoctorId.HasValue && doctorsById.TryGetValue(slot.DoctorId.Value, out var doctor) && doctor.Staff?.DepartmentId != null)
        {
            return doctor.Staff.DepartmentId;
        }

        return null;
    }

    private static long? ResolveVisitHospitalId(
        Visit visit,
        ExaminationSlot? linkedSlot,
        IReadOnlyDictionary<long, HospitalRoom> roomsById,
        IReadOnlyDictionary<long, Doctor> doctorsById,
        IReadOnlyDictionary<long, Staff> staffById,
        IReadOnlyDictionary<long, Service> servicesById)
    {
        if (visit.ExaminationRoomId.HasValue &&
            roomsById.TryGetValue(visit.ExaminationRoomId.Value, out var room) &&
            room.HospitalId.HasValue)
        {
            return room.HospitalId.Value;
        }

        if (linkedSlot is not null)
        {
            if (linkedSlot.RoomId.HasValue &&
                roomsById.TryGetValue(linkedSlot.RoomId.Value, out var slotRoom) &&
                slotRoom.HospitalId.HasValue)
            {
                return slotRoom.HospitalId.Value;
            }

            if (linkedSlot.ServiceId.HasValue &&
                servicesById.TryGetValue(linkedSlot.ServiceId.Value, out var slotService) &&
                slotService.HospitalId.HasValue)
            {
                return slotService.HospitalId.Value;
            }

            if (linkedSlot.DoctorId.HasValue &&
                doctorsById.TryGetValue(linkedSlot.DoctorId.Value, out var slotDoc))
            {
                return slotDoc.HospitalId ?? slotDoc.Staff?.HospitalId;
            }
        }

        if (visit.AssignedDoctorId.HasValue &&
            doctorsById.TryGetValue(visit.AssignedDoctorId.Value, out var doctor))
        {
            var docHosp = doctor.HospitalId ?? doctor.Staff?.HospitalId;
            if (docHosp.HasValue)
            {
                return docHosp.Value;
            }
        }

        if (visit.CheckedInByStaffId.HasValue &&
            staffById.TryGetValue(visit.CheckedInByStaffId.Value, out var staff) &&
            staff.HospitalId.HasValue)
        {
            return staff.HospitalId.Value;
        }

        return null;
    }

    private static long? ResolveVisitDepartmentId(
        Visit visit,
        ExaminationSlot? linkedSlot,
        IReadOnlyDictionary<long, HospitalRoom> roomsById,
        IReadOnlyDictionary<long, Doctor> doctorsById,
        IReadOnlyDictionary<long, Staff> staffById,
        IReadOnlyDictionary<long, Service> servicesById)
    {
        if (visit.ExaminationRoomId.HasValue &&
            roomsById.TryGetValue(visit.ExaminationRoomId.Value, out var room) &&
            room.DepartmentId.HasValue)
        {
            return room.DepartmentId.Value;
        }

        var slotDept = ResolveSlotDepartmentId(linkedSlot, servicesById, roomsById, doctorsById);
        if (slotDept.HasValue)
        {
            return slotDept.Value;
        }

        if (visit.AssignedDoctorId.HasValue &&
            doctorsById.TryGetValue(visit.AssignedDoctorId.Value, out var doctor) &&
            doctor.Staff?.DepartmentId != null)
        {
            return doctor.Staff.DepartmentId;
        }

        if (visit.CheckedInByStaffId.HasValue &&
            staffById.TryGetValue(visit.CheckedInByStaffId.Value, out var staff) &&
            staff.DepartmentId.HasValue)
        {
            return staff.DepartmentId.Value;
        }

        return null;
    }

    private static bool IsInProgressStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return false;
        }

        return status.Trim().ToUpperInvariant() is
            "IN_PROGRESS" or "IN_CONSULTATION" or "EXAMINING" or "CALLED";
    }

    private static bool IsNoShowStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return false;
        }

        return status.Trim().ToUpperInvariant() is "NO_SHOW" or "MISSED";
    }

    private static double? ComputeAverage(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return null;
        }

        return Math.Round(values.Average(), 1);
    }

    private static double? ComputeMedian(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return null;
        }

        var sorted = values.OrderBy(v => v).ToArray();
        var mid = sorted.Length / 2;
        var median = sorted.Length % 2 == 1
            ? sorted[mid]
            : (sorted[mid - 1] + sorted[mid]) / 2.0;

        return Math.Round(median, 1);
    }

    private static IReadOnlyList<OperationalDailyVisitPointDto> BuildDailyVisits(
        DateOnly fromDate,
        DateOnly toDate,
        IReadOnlyList<ResolvedVisitRecord> visits)
    {
        var grouped = visits
            .GroupBy(v => v.LocalDate)
            .ToDictionary(g => g.Key, g => g.ToList());

        var result = new List<OperationalDailyVisitPointDto>();
        for (var d = fromDate; d <= toDate; d = d.AddDays(1))
        {
            grouped.TryGetValue(d, out var dayVisits);
            var total = dayVisits?.Count ?? 0;
            var completed = dayVisits?.Count(v =>
                string.Equals(v.Visit.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase)) ?? 0;
            var walkIns = dayVisits?.Count(v => !v.Visit.AppointmentId.HasValue) ?? 0;

            result.Add(new OperationalDailyVisitPointDto(
                Date: d,
                DateLabel: d.ToString("dd/MM", CultureInfo.InvariantCulture),
                TotalVisits: total,
                CompletedVisits: completed,
                WalkInVisits: walkIns));
        }

        return result;
    }

    private static IReadOnlyList<OperationalHourlyIntakePointDto> BuildHourlyIntake(
        IReadOnlyList<ResolvedVisitRecord> visits)
    {
        var countsByHour = visits
            .GroupBy(v => v.LocalCheckIn.Hour)
            .ToDictionary(g => g.Key, g => g.Count());

        var hoursSet = OperationalHours.ToHashSet();
        foreach (var hourKey in countsByHour.Keys)
        {
            hoursSet.Add(hourKey);
        }

        var orderedHours = hoursSet.OrderBy(h => h).ToList();
        var maxCount = countsByHour.Values.DefaultIfEmpty(0).Max();

        return orderedHours
            .Select(h =>
            {
                var count = countsByHour.TryGetValue(h, out var c) ? c : 0;
                return new OperationalHourlyIntakePointDto(
                    Hour: $"{h:D2}:00",
                    Count: count,
                    IsPeak: maxCount > 0 && count == maxCount);
            })
            .ToList();
    }

    private static IReadOnlyList<OperationalDepartmentVisitItemDto> BuildDepartmentVisits(
        IReadOnlyList<ResolvedVisitRecord> visits,
        IReadOnlyDictionary<long, Department> departmentsById)
    {
        return visits
            .GroupBy(v => v.DepartmentId)
            .Select(g =>
            {
                Department? dept = null;
                if (g.Key.HasValue)
                {
                    departmentsById.TryGetValue(g.Key.Value, out dept);
                }

                var code = dept is not null && !string.IsNullOrWhiteSpace(dept.DepartmentCode)
                    ? dept.DepartmentCode
                    : (g.Key.HasValue ? $"DEP-{g.Key.Value:D3}" : "UNASSIGNED");
                var name = dept?.Name ?? "Chưa phân khoa";

                return new OperationalDepartmentVisitItemDto(
                    DepartmentId: g.Key,
                    DepartmentCode: code,
                    DepartmentName: name,
                    Count: g.Count());
            })
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.DepartmentName, StringComparer.Ordinal)
            .ToList();
    }

    private static IReadOnlyList<OperationalVisitStatusSegmentDto> BuildStatusSegments(
        int totalVisits,
        int completedVisits,
        int inProgressVisits,
        int waitingVisits)
    {
        var completedPct = Math.Round(completedVisits * 100.0 / totalVisits, 1);
        var inProgressPct = Math.Round(inProgressVisits * 100.0 / totalVisits, 1);
        var waitingPct = Math.Max(0.0, Math.Round(100.0 - completedPct - inProgressPct, 1));

        return
        [
            new OperationalVisitStatusSegmentDto("COMPLETED", "Hoàn tất khám", completedVisits, completedPct),
            new OperationalVisitStatusSegmentDto("IN_PROGRESS", "Đang khám", inProgressVisits, inProgressPct),
            new OperationalVisitStatusSegmentDto("WAITING", "Chờ khám", waitingVisits, waitingPct),
        ];
    }

    private static IReadOnlyList<OperationalDepartmentRowDto> BuildDepartmentRows(
        IReadOnlyList<ResolvedVisitRecord> visits,
        IReadOnlyList<Appointment> appointments,
        IReadOnlyDictionary<long, ExaminationSlot> slotsById,
        IReadOnlyDictionary<long, Service> servicesById,
        IReadOnlyDictionary<long, HospitalRoom> roomsById,
        IReadOnlyDictionary<long, Doctor> doctorsById,
        IReadOnlyDictionary<long, Department> departmentsById,
        IReadOnlyDictionary<long, List<double>> waitMinutesByVisitId,
        string facilityName,
        string periodLabel)
    {
        const long unassignedKey = -1L;
        var deptKeys = new HashSet<long>();
        foreach (var v in visits)
        {
            deptKeys.Add(v.DepartmentId ?? unassignedKey);
        }

        var appointmentsByDept = new Dictionary<long, List<Appointment>>();
        foreach (var apt in appointments)
        {
            ExaminationSlot? slot = null;
            if (apt.SlotId.HasValue)
            {
                slotsById.TryGetValue(apt.SlotId.Value, out slot);
            }

            var deptId = ResolveSlotDepartmentId(slot, servicesById, roomsById, doctorsById);
            var key = deptId ?? unassignedKey;
            deptKeys.Add(key);

            if (!appointmentsByDept.TryGetValue(key, out var list))
            {
                list = [];
                appointmentsByDept[key] = list;
            }

            list.Add(apt);
        }

        var visitsByDept = visits
            .GroupBy(v => v.DepartmentId ?? unassignedKey)
            .ToDictionary(g => g.Key, g => g.ToList());

        var rows = new List<OperationalDepartmentRowDto>();
        foreach (var key in deptKeys)
        {
            long? deptId = key == unassignedKey ? null : key;
            visitsByDept.TryGetValue(key, out var deptVisits);
            appointmentsByDept.TryGetValue(key, out var deptApts);

            deptVisits ??= [];
            deptApts ??= [];

            var totalVis = deptVisits.Count;
            var completedVis = deptVisits.Count(v =>
                string.Equals(v.Visit.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase));
            var walkInVis = deptVisits.Count(v => !v.Visit.AppointmentId.HasValue);
            var totalApt = deptApts.Count(a =>
                !string.Equals(a.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase));
            var noShowApt = deptApts.Count(a => IsNoShowStatus(a.Status));

            if (totalVis == 0 && totalApt == 0 && noShowApt == 0)
            {
                continue;
            }

            var deptWaits = new List<double>();
            foreach (var v in deptVisits)
            {
                if (waitMinutesByVisitId.TryGetValue(v.Visit.Id, out var samples))
                {
                    deptWaits.AddRange(samples);
                }
            }

            var avgWait = ComputeAverage(deptWaits);
            var medWait = ComputeMedian(deptWaits);

            Department? dept = null;
            if (deptId.HasValue)
            {
                departmentsById.TryGetValue(deptId.Value, out dept);
            }

            var deptCode = dept is not null && !string.IsNullOrWhiteSpace(dept.DepartmentCode)
                ? dept.DepartmentCode
                : (deptId.HasValue ? $"DEP-{deptId.Value:D3}" : "UNASSIGNED");
            var deptName = dept?.Name ?? "Chưa phân khoa";
            var reportCode = $"RPT-{deptCode}";

            var status = (medWait.HasValue && medWait.Value > 30.0) || noShowApt > 0
                ? "NEEDS_REVIEW"
                : "COMPLETED";

            rows.Add(new OperationalDepartmentRowDto(
                Id: deptId.HasValue ? $"rpt-dept-{deptId.Value}" : "rpt-dept-unassigned",
                Code: reportCode,
                Name: $"Tổng hợp vận hành {deptName}",
                DepartmentId: deptId,
                DepartmentName: deptName,
                FacilityName: facilityName,
                PeriodLabel: periodLabel,
                TotalVisits: totalVis,
                CompletedVisits: completedVis,
                WalkInVisits: walkInVis,
                TotalAppointments: totalApt,
                NoShowAppointments: noShowApt,
                AverageWaitMinutes: avgWait,
                MedianWaitMinutes: medWait,
                Status: status));
        }

        return rows
            .OrderByDescending(r => r.TotalVisits)
            .ThenByDescending(r => r.TotalAppointments)
            .ThenBy(r => r.DepartmentName, StringComparer.Ordinal)
            .ToList();
    }
}
