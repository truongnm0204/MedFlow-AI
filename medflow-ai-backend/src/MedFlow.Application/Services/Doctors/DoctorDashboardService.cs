using System.Globalization;
using MedFlow.Application.DTOs.DoctorDashboard;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Appointments;
using MedFlow.Application.Services.Visits;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Doctors;

public sealed class DoctorDashboardService : IDoctorDashboardService
{
    private const string DoctorRoleCode = "DOCTOR";
    private const string DefaultRoomCode = "P.102";
    private const string DefaultRoomName = "Phòng khám ngoại trú";
    private const string DefaultDepartmentName = "Khoa Khám bệnh";
    private const string DefaultShiftLabel = "Ca sáng 07:30–11:30";

    private static readonly string[] FallbackDoctorPermissions =
    [
        "DIAGNOSTIC_ORDER",
        "MEDICAL_RECORD_WRITE",
        "PAYMENT_VIEW",
        "PRESCRIPTION_ISSUE",
    ];

    private static readonly HashSet<string> ValidStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "WAITING",
        "CALLED",
        "IN_CONSULTATION",
        "AWAITING_RESULTS",
        "COMPLETED",
    };

    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;

    public DoctorDashboardService(
        IApplicationDbContext dbContext,
        TimeProvider? timeProvider = null)
    {
        _db = dbContext;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<DoctorDashboardResponseDto> GetDashboardAsync(
        long actorUserId,
        GetDoctorDashboardRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.QueueLimit is < 1 or > 50)
        {
            throw new ValidationException("Số lượng bệnh nhân trong hàng đợi hiển thị phải từ 1 đến 50.");
        }

        if (request.DiagnosticLimit is < 1 or > 50)
        {
            throw new ValidationException("Số lượng phiếu cận lâm sàng hiển thị phải từ 1 đến 50.");
        }

        string? normalizedStatus = null;
        if (!string.IsNullOrWhiteSpace(request.Status) &&
            !string.Equals(request.Status.Trim(), "ALL", StringComparison.OrdinalIgnoreCase))
        {
            var trimmedStatus = request.Status.Trim();
            if (!ValidStatuses.Contains(trimmedStatus))
            {
                throw new ValidationException("Trạng thái hàng đợi lọc không hợp lệ.");
            }

            normalizedStatus = trimmedStatus.ToUpperInvariant();
        }

        string? normalizedPriority = null;
        if (!string.IsNullOrWhiteSpace(request.Priority) &&
            !string.Equals(request.Priority.Trim(), "ALL", StringComparison.OrdinalIgnoreCase))
        {
            var trimmedPriority = AppointmentBookingRules.NormalizeStatusKey(request.Priority);
            if (!ClinicalQueueRules.ValidPriorities.Contains(trimmedPriority))
            {
                throw new ValidationException("Mức độ ưu tiên lọc không hợp lệ.");
            }

            normalizedPriority = trimmedPriority;
        }

        string? normalizedQueueType = null;
        if (!string.IsNullOrWhiteSpace(request.QueueType) &&
            !string.Equals(request.QueueType.Trim(), "ALL", StringComparison.OrdinalIgnoreCase))
        {
            var trimmedQueueType = AppointmentBookingRules.NormalizeStatusKey(request.QueueType);
            if (!ClinicalQueueRules.ValidQueueTypes.Contains(trimmedQueueType))
            {
                throw new ValidationException("Loại lượt khám lọc không hợp lệ.");
            }

            normalizedQueueType = trimmedQueueType;
        }

        var normalizedKeyword = string.IsNullOrWhiteSpace(request.Keyword)
            ? null
            : request.Keyword.Trim();

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var nowLocal = _timeProvider.GetLocalNow().DateTime;
        var today = DateOnly.FromDateTime(nowLocal);

        var actorUser = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == actorUserId, ct)
            ?? throw new NotFoundException("User", actorUserId);

        if (actorUser.AccountStatus != AccountStatus.ACTIVE)
        {
            throw new ForbiddenException("Tài khoản bác sĩ không ở trạng thái hoạt động.");
        }

        if (actorUser.LockedUntil.HasValue && actorUser.LockedUntil.Value > nowUtc)
        {
            throw new ForbiddenException("Tài khoản bác sĩ đang bị tạm khóa.");
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

        if (!actorRoleCodes.Any(c => string.Equals(c, DoctorRoleCode, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ForbiddenException("Người dùng không có quyền truy cập bảng điều khiển Bác sĩ.");
        }

        var actorStaff = await _db.Staff
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == actorUserId, ct);

        if (actorStaff is null || !actorStaff.HospitalId.HasValue)
        {
            throw new ForbiddenException("Tài khoản bác sĩ chưa được gán cơ sở y tế.");
        }

        if (!string.Equals(actorStaff.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            throw new ForbiddenException("Hồ sơ nhân sự của bác sĩ đã ngừng hoạt động.");
        }

        var actorDoctor = await _db.Doctors
            .AsNoTracking()
            .Include(d => d.Specialty)
            .FirstOrDefaultAsync(d => d.StaffId == actorStaff.Id, ct);

        if (actorDoctor is null)
        {
            throw new ForbiddenException("Tài khoản chưa được liên kết với hồ sơ bác sĩ lâm sàng.");
        }

        if (!actorDoctor.IsActive)
        {
            throw new ForbiddenException("Hồ sơ hành nghề của bác sĩ hiện đang tạm ngừng hoạt động.");
        }

        if (actorDoctor.HospitalId.HasValue &&
            actorDoctor.HospitalId.Value != actorStaff.HospitalId.Value)
        {
            throw new ForbiddenException("Hồ sơ bác sĩ không thuộc cùng cơ sở y tế với hồ sơ nhân sự.");
        }

        var hospitalId = actorDoctor.HospitalId ?? actorStaff.HospitalId.Value;

        var hospital = await _db.Hospitals
            .AsNoTracking()
            .FirstOrDefaultAsync(h => h.Id == hospitalId, ct)
            ?? throw new NotFoundException("Hospital", hospitalId);

        if (hospital.IsActive == false)
        {
            throw new ForbiddenException("Cơ sở y tế được gán cho bác sĩ hiện đang ngừng hoạt động.");
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
            actorPermissionCodes = [.. FallbackDoctorPermissions];
        }

        var departments = await _db.Departments
            .AsNoTracking()
            .Where(d => d.HospitalId == hospitalId)
            .OrderBy(d => d.Id)
            .ToListAsync(ct);

        var departmentsById = departments.ToDictionary(d => d.Id);

        var rooms = await _db.HospitalRooms
            .AsNoTracking()
            .Where(r => r.HospitalId == hospitalId)
            .OrderBy(r => r.Id)
            .ToListAsync(ct);

        var roomsById = rooms.ToDictionary(r => r.Id);

        var services = await _db.Services
            .AsNoTracking()
            .Where(s => s.HospitalId == hospitalId)
            .OrderBy(s => s.Id)
            .ToListAsync(ct);

        var servicesById = services.ToDictionary(s => s.Id);

        var doctorSlots = await _db.ExaminationSlots
            .AsNoTracking()
            .Where(s => s.DoctorId == actorDoctor.Id)
            .OrderBy(s => s.StartsAt)
            .ThenBy(s => s.Id)
            .ToListAsync(ct);

        var doctorSlotsById = doctorSlots.ToDictionary(s => s.Id);
        var doctorSlotIds = doctorSlotsById.Keys.ToHashSet();

        var directDoctorVisits = await _db.Visits
            .AsNoTracking()
            .Where(v => v.AssignedDoctorId == actorDoctor.Id || v.CompletedByDoctorId == actorDoctor.Id)
            .OrderBy(v => v.CheckedInAt)
            .ThenBy(v => v.Id)
            .ToListAsync(ct);

        var directVisitAppointmentIds = directDoctorVisits
            .Where(v => v.AppointmentId.HasValue)
            .Select(v => v.AppointmentId!.Value)
            .ToHashSet();

        var doctorAppointments = (doctorSlotIds.Count == 0 && directVisitAppointmentIds.Count == 0)
            ? []
            : await _db.Appointments
                .AsNoTracking()
                .Where(a =>
                    (a.SlotId.HasValue && doctorSlotIds.Contains(a.SlotId.Value)) ||
                    directVisitAppointmentIds.Contains(a.Id))
                .OrderBy(a => a.BookedAt)
                .ThenBy(a => a.Id)
                .ToListAsync(ct);

        var doctorAppointmentIds = doctorAppointments.Select(a => a.Id).ToHashSet();

        var slotLinkedVisits = doctorAppointmentIds.Count == 0
            ? []
            : await _db.Visits
                .AsNoTracking()
                .Where(v =>
                    v.AppointmentId.HasValue &&
                    doctorAppointmentIds.Contains(v.AppointmentId.Value) &&
                    (!v.AssignedDoctorId.HasValue || v.AssignedDoctorId.Value == actorDoctor.Id))
                .ToListAsync(ct);

        var allDoctorVisits = directDoctorVisits
            .Concat(slotLinkedVisits)
            .GroupBy(v => v.Id)
            .Select(g => g.First())
            .Where(v => !v.AssignedDoctorId.HasValue || v.AssignedDoctorId.Value == actorDoctor.Id)
            .OrderBy(v => v.CheckedInAt)
            .ThenBy(v => v.Id)
            .ToList();

        var (roomCode, roomName) = ResolveDoctorRoom(
            actorStaff,
            rooms,
            roomsById,
            doctorSlots,
            allDoctorVisits,
            nowLocal.Date);

        var departmentName = actorStaff.DepartmentId.HasValue &&
                             departmentsById.TryGetValue(actorStaff.DepartmentId.Value, out var staffDept)
            ? staffDept.Name
            : (!string.IsNullOrWhiteSpace(actorDoctor.Specialty?.Name)
                ? $"Khoa {actorDoctor.Specialty.Name}"
                : DefaultDepartmentName);

        var shiftLabel = ResolveShiftLabel(doctorSlots, nowLocal.Date);

        var fullName = !string.IsNullOrWhiteSpace(actorStaff.FullName)
            ? actorStaff.FullName.Trim()
            : actorUser.Email;

        var displayName = BuildDoctorDisplayName(actorDoctor.ProfessionalTitle, fullName, actorDoctor.DoctorCode);

        var doctorProfile = new DoctorDashboardProfileDto(
            UserId: actorUser.Id,
            DoctorId: actorDoctor.Id,
            DoctorCode: !string.IsNullOrWhiteSpace(actorDoctor.DoctorCode)
                ? actorDoctor.DoctorCode
                : $"DOC-{actorDoctor.Id:D3}",
            StaffCode: actorStaff.StaffCode,
            Email: actorUser.Email,
            FullName: fullName,
            ProfessionalTitle: actorDoctor.ProfessionalTitle,
            DisplayName: displayName,
            SpecialtyName: actorDoctor.Specialty?.Name,
            DepartmentId: actorStaff.DepartmentId,
            DepartmentName: departmentName,
            HospitalId: hospital.Id,
            HospitalCode: hospital.HospitalCode,
            HospitalName: hospital.Name,
            RoomCode: roomCode,
            RoomName: roomName,
            LicenseNumber: actorDoctor.LicenseNumber,
            Roles: actorRoleCodes,
            Permissions: actorPermissionCodes);

        var allVisitIds = allDoctorVisits.Select(v => v.Id).ToHashSet();

        var allQueueEntries = (allVisitIds.Count == 0 && doctorAppointmentIds.Count == 0)
            ? []
            : await _db.QueueEntries
                .AsNoTracking()
                .Where(q =>
                    (q.VisitId.HasValue && allVisitIds.Contains(q.VisitId.Value)) ||
                    (q.AppointmentId.HasValue && doctorAppointmentIds.Contains(q.AppointmentId.Value)))
                .OrderBy(q => q.QueueNumber)
                .ThenBy(q => q.Id)
                .ToListAsync(ct);

        var patientIds = allDoctorVisits
            .Where(v => v.PatientId.HasValue)
            .Select(v => v.PatientId!.Value)
            .Concat(doctorAppointments.Where(a => a.PatientId.HasValue).Select(a => a.PatientId!.Value))
            .Distinct()
            .ToHashSet();

        var patientsById = patientIds.Count == 0
            ? new Dictionary<long, Patient>()
            : (await _db.Patients
                .AsNoTracking()
                .Where(p => patientIds.Contains(p.Id))
                .ToListAsync(ct))
                .ToDictionary(p => p.Id);

        var (scopedVisits, scopedAppointments) = ScopeVisitsAndAppointmentsForWorkbench(
            allDoctorVisits,
            doctorAppointments,
            doctorSlotsById,
            allQueueEntries,
            today,
            nowLocal.Date);

        var scopedVisitIds = scopedVisits.Select(v => v.Id).ToHashSet();
        var scopedAppointmentIds = scopedAppointments.Select(a => a.Id).ToHashSet();

        var scopedQueueEntries = allQueueEntries
            .Where(q =>
                (q.VisitId.HasValue && scopedVisitIds.Contains(q.VisitId.Value)) ||
                (q.AppointmentId.HasValue && scopedAppointmentIds.Contains(q.AppointmentId.Value)))
            .ToList();

        var fullAssignedQueue = BuildAssignedQueue(
            scopedVisits,
            scopedAppointments,
            doctorSlotsById,
            servicesById,
            patientsById,
            scopedQueueEntries,
            today);

        var diagnosticServices = services
            .Where(s => string.Equals(s.ServiceType, "DIAGNOSTIC", StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => s.Id)
            .ToList();

        var fullPendingDiagnostics = BuildPendingDiagnostics(
            scopedVisits,
            patientsById,
            scopedQueueEntries,
            diagnosticServices);

        var assignedWaitingCount = fullAssignedQueue.Count(e =>
            string.Equals(e.Status, "WAITING", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(e.Status, "CALLED", StringComparison.OrdinalIgnoreCase));

        var inConsultationCount = fullAssignedQueue.Count(e =>
            string.Equals(e.Status, "IN_CONSULTATION", StringComparison.OrdinalIgnoreCase));

        var awaitingResultsQueueCount = fullAssignedQueue.Count(e =>
            string.Equals(e.Status, "AWAITING_RESULTS", StringComparison.OrdinalIgnoreCase));

        var pendingLabResultsCount = Math.Max(awaitingResultsQueueCount, fullPendingDiagnostics.Count);

        var completedTodayCount = fullAssignedQueue.Count(e =>
            string.Equals(e.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase));

        var stats = new DoctorStatsDto(
            AssignedWaiting: assignedWaitingCount,
            InConsultation: inConsultationCount,
            PendingLabResults: pendingLabResultsCount,
            CompletedToday: completedTodayCount);

        IEnumerable<AssignedPatientEntryDto> filteredQueue = fullAssignedQueue;

        if (normalizedStatus is not null)
        {
            filteredQueue = filteredQueue.Where(item =>
                string.Equals(item.Status, normalizedStatus, StringComparison.OrdinalIgnoreCase));
        }

        if (normalizedPriority is not null)
        {
            filteredQueue = filteredQueue.Where(item =>
                string.Equals(item.Priority, normalizedPriority, StringComparison.OrdinalIgnoreCase));
        }

        if (normalizedQueueType is not null)
        {
            filteredQueue = filteredQueue.Where(item =>
                string.Equals(item.QueueType, normalizedQueueType, StringComparison.OrdinalIgnoreCase));
        }

        if (normalizedKeyword is not null)
        {
            filteredQueue = filteredQueue.Where(item =>
                item.PatientName.Contains(normalizedKeyword, StringComparison.OrdinalIgnoreCase) ||
                item.PatientCode.Contains(normalizedKeyword, StringComparison.OrdinalIgnoreCase) ||
                item.QueueNo.Contains(normalizedKeyword, StringComparison.OrdinalIgnoreCase) ||
                item.ChiefComplaint.Contains(normalizedKeyword, StringComparison.OrdinalIgnoreCase));
        }

        var assignedQueue = filteredQueue
            .Take(request.QueueLimit)
            .ToList();

        var pendingDiagnostics = fullPendingDiagnostics
            .Take(request.DiagnosticLimit)
            .ToList();

        var quickActions = BuildQuickActions(actorPermissionCodes, roomCode);

        return new DoctorDashboardResponseDto(
            FacilityName: hospital.Name,
            FacilityCode: hospital.HospitalCode,
            RoomCode: roomCode,
            RoomName: roomName,
            DepartmentName: departmentName,
            ShiftLabel: shiftLabel,
            Stats: stats,
            AssignedQueue: assignedQueue,
            PendingDiagnostics: pendingDiagnostics,
            QuickActions: quickActions,
            DoctorProfile: doctorProfile,
            GeneratedAtUtc: nowUtc);
    }

    private static (string RoomCode, string RoomName) ResolveDoctorRoom(
        Staff actorStaff,
        IReadOnlyList<HospitalRoom> rooms,
        IReadOnlyDictionary<long, HospitalRoom> roomsById,
        IReadOnlyList<ExaminationSlot> doctorSlots,
        IReadOnlyList<Visit> doctorVisits,
        DateTime todayDate)
    {
        var tomorrowDate = todayDate.AddDays(1);

        var todaySlotRoomId = doctorSlots
            .Where(s =>
                s.RoomId.HasValue &&
                s.StartsAt.HasValue &&
                s.StartsAt.Value >= todayDate &&
                s.StartsAt.Value < tomorrowDate)
            .Select(s => s.RoomId!.Value)
            .FirstOrDefault(id => roomsById.ContainsKey(id));

        if (todaySlotRoomId > 0 && roomsById.TryGetValue(todaySlotRoomId, out var todaySlotRoom))
        {
            return (FormatRoomCode(todaySlotRoom), todaySlotRoom.Name);
        }

        var anySlotRoomId = doctorSlots
            .Where(s => s.RoomId.HasValue)
            .OrderByDescending(s => string.Equals(s.Status, "OPEN", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(s => s.StartsAt)
            .Select(s => s.RoomId!.Value)
            .FirstOrDefault(id => roomsById.ContainsKey(id));

        if (anySlotRoomId > 0 && roomsById.TryGetValue(anySlotRoomId, out var anySlotRoom))
        {
            return (FormatRoomCode(anySlotRoom), anySlotRoom.Name);
        }

        var visitRoomId = doctorVisits
            .Where(v => v.ExaminationRoomId.HasValue)
            .OrderByDescending(v => v.CheckedInAt)
            .Select(v => v.ExaminationRoomId!.Value)
            .FirstOrDefault(id => roomsById.ContainsKey(id));

        if (visitRoomId > 0 && roomsById.TryGetValue(visitRoomId, out var visitRoom))
        {
            return (FormatRoomCode(visitRoom), visitRoom.Name);
        }

        var deptRoom = rooms.FirstOrDefault(r =>
            r.DepartmentId == actorStaff.DepartmentId &&
            string.Equals(r.RoomType, "EXAMINATION", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(r.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase));

        deptRoom ??= rooms.FirstOrDefault(r =>
            string.Equals(r.RoomType, "EXAMINATION", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(r.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase));

        if (deptRoom is not null)
        {
            return (FormatRoomCode(deptRoom), deptRoom.Name);
        }

        return (DefaultRoomCode, DefaultRoomName);
    }

    private static string FormatRoomCode(HospitalRoom room)
        => !string.IsNullOrWhiteSpace(room.RoomCode) ? room.RoomCode : $"RM-{room.Id:D3}";

    private static string ResolveShiftLabel(
        IReadOnlyList<ExaminationSlot> doctorSlots,
        DateTime todayDate)
    {
        var tomorrowDate = todayDate.AddDays(1);

        var todayOpenSlots = doctorSlots
            .Where(s =>
                s.StartsAt.HasValue &&
                s.StartsAt.Value >= todayDate &&
                s.StartsAt.Value < tomorrowDate &&
                !string.Equals(s.Status, "CLOSED", StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => s.StartsAt)
            .ToList();

        if (todayOpenSlots.Count == 0)
        {
            return DefaultShiftLabel;
        }

        var minStart = todayOpenSlots.First().StartsAt!.Value;
        var lastSlot = todayOpenSlots.Last();
        var maxEnd = lastSlot.EndsAt ?? lastSlot.StartsAt!.Value.AddMinutes(30);

        var periodName = minStart.Hour < 12 && maxEnd.Hour <= 13
            ? "Ca sáng"
            : (minStart.Hour >= 12 ? "Ca chiều" : "Ca trực");

        return $"{periodName} {minStart:HH:mm}–{maxEnd:HH:mm}";
    }

    private static string BuildDoctorDisplayName(
        string? professionalTitle,
        string fullName,
        string? doctorCode)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            return !string.IsNullOrWhiteSpace(doctorCode) ? doctorCode : "Bác sĩ lâm sàng";
        }

        if (string.IsNullOrWhiteSpace(professionalTitle))
        {
            return fullName;
        }

        var trimmedTitle = professionalTitle.Trim();
        if (fullName.StartsWith(trimmedTitle, StringComparison.OrdinalIgnoreCase))
        {
            return fullName;
        }

        return $"{trimmedTitle} {fullName}";
    }

    private static (IReadOnlyList<Visit> Visits, IReadOnlyList<Appointment> Appointments) ScopeVisitsAndAppointmentsForWorkbench(
        IReadOnlyList<Visit> allDoctorVisits,
        IReadOnlyList<Appointment> doctorAppointments,
        IReadOnlyDictionary<long, ExaminationSlot> doctorSlotsById,
        IReadOnlyList<QueueEntry> allQueueEntries,
        DateOnly today,
        DateTime todayDate)
    {
        var tomorrowDate = todayDate.AddDays(1);

        var todayVisitIdsFromQueue = allQueueEntries
            .Where(q => q.VisitId.HasValue && q.QueueDate == today)
            .Select(q => q.VisitId!.Value)
            .ToHashSet();

        var todayApptIdsFromQueue = allQueueEntries
            .Where(q => q.AppointmentId.HasValue && q.QueueDate == today)
            .Select(q => q.AppointmentId!.Value)
            .ToHashSet();

        var todayVisits = allDoctorVisits
            .Where(v =>
                (v.CheckedInAt.HasValue && v.CheckedInAt.Value >= todayDate && v.CheckedInAt.Value < tomorrowDate) ||
                (v.CompletedAt.HasValue && v.CompletedAt.Value >= todayDate && v.CompletedAt.Value < tomorrowDate) ||
                todayVisitIdsFromQueue.Contains(v.Id))
            .ToList();

        var todayAppointments = doctorAppointments
            .Where(a =>
                !IsCancelledOrNoShow(a.Status) &&
                ((a.SlotId.HasValue &&
                  doctorSlotsById.TryGetValue(a.SlotId.Value, out var slot) &&
                  slot.StartsAt.HasValue &&
                  slot.StartsAt.Value >= todayDate &&
                  slot.StartsAt.Value < tomorrowDate) ||
                 todayApptIdsFromQueue.Contains(a.Id)))
            .ToList();

        if (todayVisits.Count > 0 || todayAppointments.Count > 0)
        {
            return (todayVisits, todayAppointments);
        }

        var activeAppointments = doctorAppointments
            .Where(a => !IsCancelledOrNoShow(a.Status))
            .ToList();

        return (allDoctorVisits, activeAppointments);
    }

    private static bool IsCancelledOrNoShow(string? status)
        => string.Equals(status, "CANCELLED", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(status, "NO_SHOW", StringComparison.OrdinalIgnoreCase);

    private static List<AssignedPatientEntryDto> BuildAssignedQueue(
        IReadOnlyList<Visit> scopedVisits,
        IReadOnlyList<Appointment> scopedAppointments,
        IReadOnlyDictionary<long, ExaminationSlot> doctorSlotsById,
        IReadOnlyDictionary<long, Service> servicesById,
        IReadOnlyDictionary<long, Patient> patientsById,
        IReadOnlyList<QueueEntry> scopedQueueEntries,
        DateOnly today)
    {
        var entries = new List<(AssignedPatientEntryDto Dto, int TierOrder)>();
        var appointmentsById = scopedAppointments.ToDictionary(a => a.Id);
        var coveredAppointmentIds = new HashSet<long>();

        var examQueuesByVisitId = scopedQueueEntries
            .Where(q => q.VisitId.HasValue && IsExaminationQueue(q.QueueType))
            .GroupBy(q => q.VisitId!.Value)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(q => q.Id).First());

        var examQueuesByApptId = scopedQueueEntries
            .Where(q => q.AppointmentId.HasValue && IsExaminationQueue(q.QueueType))
            .GroupBy(q => q.AppointmentId!.Value)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(q => q.Id).First());

        var diagQueuesByVisitId = scopedQueueEntries
            .Where(q => q.VisitId.HasValue && string.Equals(q.QueueType, "DIAGNOSTIC", StringComparison.OrdinalIgnoreCase))
            .GroupBy(q => q.VisitId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var sequenceCounter = 1;

        foreach (var visit in scopedVisits)
        {
            if (visit.AppointmentId.HasValue)
            {
                coveredAppointmentIds.Add(visit.AppointmentId.Value);
            }

            Appointment? linkedAppt = null;
            if (visit.AppointmentId.HasValue)
            {
                appointmentsById.TryGetValue(visit.AppointmentId.Value, out linkedAppt);
            }

            ExaminationSlot? linkedSlot = null;
            if (linkedAppt?.SlotId != null)
            {
                doctorSlotsById.TryGetValue(linkedAppt.SlotId.Value, out linkedSlot);
            }

            Service? linkedService = null;
            if (linkedSlot?.ServiceId != null)
            {
                servicesById.TryGetValue(linkedSlot.ServiceId.Value, out linkedService);
            }

            QueueEntry? examQueue = null;
            if (!examQueuesByVisitId.TryGetValue(visit.Id, out examQueue) && visit.AppointmentId.HasValue)
            {
                examQueuesByApptId.TryGetValue(visit.AppointmentId.Value, out examQueue);
            }

            diagQueuesByVisitId.TryGetValue(visit.Id, out var visitDiagQueues);

            var patientId = visit.PatientId ?? linkedAppt?.PatientId;
            Patient? patient = null;
            if (patientId.HasValue)
            {
                patientsById.TryGetValue(patientId.Value, out patient);
            }

            var age = CalculateAge(patient?.DateOfBirth, today);
            var gender = FormatGender(patient?.Gender);
            var patientName = !string.IsNullOrWhiteSpace(patient?.FullName)
                ? patient.FullName
                : $"Bệnh nhân #{patientId ?? visit.Id}";
            var patientCode = !string.IsNullOrWhiteSpace(patient?.PatientCode)
                ? patient.PatientCode
                : $"PAT-{(patientId ?? visit.Id):D4}";

            var queueNumber = examQueue?.QueueNumber ?? sequenceCounter;
            sequenceCounter++;
            var queueNo = $"B{queueNumber:D3}";

            var chiefComplaint = FirstNonEmpty(
                visit.ReasonForVisit,
                linkedAppt?.ReasonForVisit,
                linkedAppt?.ReceptionNote,
                linkedService?.Name,
                "Khám chuyên khoa ngoại trú");

            var assignedTimestamp = visit.CheckedInAt
                ?? examQueue?.ArrivedAt
                ?? examQueue?.ScheduledAt
                ?? linkedSlot?.StartsAt
                ?? linkedAppt?.BookedAt;

            var assignedAt = assignedTimestamp.HasValue
                ? assignedTimestamp.Value.ToString("HH:mm", CultureInfo.InvariantCulture)
                : "08:00";

            var status = ResolveQueueStatus(visit.Status, examQueue, visitDiagQueues);
            var priority = ResolvePriority(examQueue?.ManualPriority, age);
            var queueType = ResolveQueueType(visit.Status, examQueue, visitDiagQueues, status);
            var tierOrder = status == "WAITING"
                ? ClinicalQueueRules.GetWaitingTier(priority, visit.AppointmentId.HasValue, examQueue?.ArrivalClass)
                : 0;

            entries.Add((
                new AssignedPatientEntryDto(
                    Id: $"vis-{visit.Id}",
                    QueueNo: queueNo,
                    PatientName: patientName,
                    PatientCode: patientCode,
                    Age: age,
                    Gender: gender,
                    ChiefComplaint: chiefComplaint,
                    AssignedAt: assignedAt,
                    Priority: priority,
                    QueueType: queueType,
                    Status: status),
                tierOrder));
        }

        foreach (var appt in scopedAppointments)
        {
            if (coveredAppointmentIds.Contains(appt.Id))
            {
                continue;
            }

            ExaminationSlot? slot = null;
            if (appt.SlotId.HasValue)
            {
                doctorSlotsById.TryGetValue(appt.SlotId.Value, out slot);
            }

            Service? service = null;
            if (slot?.ServiceId != null)
            {
                servicesById.TryGetValue(slot.ServiceId.Value, out service);
            }

            examQueuesByApptId.TryGetValue(appt.Id, out var examQueue);

            Patient? patient = null;
            if (appt.PatientId.HasValue)
            {
                patientsById.TryGetValue(appt.PatientId.Value, out patient);
            }

            var age = CalculateAge(patient?.DateOfBirth, today);
            var gender = FormatGender(patient?.Gender);
            var patientName = !string.IsNullOrWhiteSpace(patient?.FullName)
                ? patient.FullName
                : $"Bệnh nhân #{appt.PatientId ?? appt.Id}";
            var patientCode = !string.IsNullOrWhiteSpace(patient?.PatientCode)
                ? patient.PatientCode
                : $"PAT-{(appt.PatientId ?? appt.Id):D4}";

            var queueNumber = examQueue?.QueueNumber ?? sequenceCounter;
            sequenceCounter++;
            var queueNo = $"B{queueNumber:D3}";

            var chiefComplaint = FirstNonEmpty(
                appt.ReasonForVisit,
                appt.ReceptionNote,
                service?.Name,
                "Khám hẹn trước ngoại trú");

            var assignedTimestamp = examQueue?.ArrivedAt
                ?? examQueue?.ScheduledAt
                ?? slot?.StartsAt
                ?? appt.BookedAt;

            var assignedAt = assignedTimestamp.HasValue
                ? assignedTimestamp.Value.ToString("HH:mm", CultureInfo.InvariantCulture)
                : "08:30";

            var status = ResolveQueueStatus(null, examQueue, null);
            var priority = ResolvePriority(examQueue?.ManualPriority, age);
            var queueType = ResolveQueueType(null, examQueue, null, status);
            var tierOrder = status == "WAITING"
                ? ClinicalQueueRules.GetWaitingTier(priority, isScheduled: true, examQueue?.ArrivalClass)
                : 0;

            entries.Add((
                new AssignedPatientEntryDto(
                    Id: $"apt-{appt.Id}",
                    QueueNo: queueNo,
                    PatientName: patientName,
                    PatientCode: patientCode,
                    Age: age,
                    Gender: gender,
                    ChiefComplaint: chiefComplaint,
                    AssignedAt: assignedAt,
                    Priority: priority,
                    QueueType: queueType,
                    Status: status),
                tierOrder));
        }

        return entries
            .OrderBy(e => GetStatusSortOrder(e.Dto.Status))
            .ThenBy(e => e.TierOrder)
            .ThenBy(e => e.Dto.QueueNo, StringComparer.Ordinal)
            .Select(e => e.Dto)
            .ToList();
    }

    private static bool IsExaminationQueue(string? queueType)
        => ClinicalQueueRules.IsClinicalExaminationQueueType(queueType);

    private static string ResolveQueueStatus(
        string? visitStatus,
        QueueEntry? examQueue,
        IReadOnlyList<QueueEntry>? diagQueues)
    {
        var normalizedVisit = NormalizeStatusToken(visitStatus);
        var normalizedQueue = NormalizeStatusToken(examQueue?.Status);

        if (normalizedVisit == "COMPLETED")
        {
            return "COMPLETED";
        }

        if (normalizedVisit is "AWAITING_DIAGNOSTIC" or "AWAITING_RESULTS")
        {
            return "AWAITING_RESULTS";
        }

        if (diagQueues is { Count: > 0 } &&
            diagQueues.Any(d =>
                !string.Equals(d.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase) &&
                !d.CompletedAt.HasValue))
        {
            return "AWAITING_RESULTS";
        }

        if (normalizedVisit == "IN_CONSULTATION" ||
            normalizedQueue is "IN_CONSULTATION" or "IN_PROGRESS" or "SERVING" ||
            (examQueue?.StartedAt.HasValue == true && !examQueue.CompletedAt.HasValue && normalizedVisit != "RETURN_CONSULTATION"))
        {
            return "IN_CONSULTATION";
        }

        if (normalizedQueue == "CALLED" ||
            (examQueue?.CalledAt.HasValue == true && !examQueue.StartedAt.HasValue && !examQueue.CompletedAt.HasValue))
        {
            return "CALLED";
        }

        if (normalizedVisit is null && normalizedQueue == "COMPLETED")
        {
            return "COMPLETED";
        }

        return "WAITING";
    }

    private static string ResolvePriority(string? manualPriority, int age)
    {
        var normalizedManual = ClinicalQueueRules.NormalizeStoredPriority(manualPriority);
        if (normalizedManual == ClinicalQueueRules.PriorityPregnant)
        {
            return ClinicalQueueRules.PriorityPregnant;
        }

        if (normalizedManual == ClinicalQueueRules.PriorityElderly || age >= ClinicalQueueRules.ElderlyMinimumAge)
        {
            return ClinicalQueueRules.PriorityElderly;
        }

        return ClinicalQueueRules.PriorityNormal;
    }

    private static string ResolveQueueType(
        string? visitStatus,
        QueueEntry? examQueue,
        IReadOnlyList<QueueEntry>? diagQueues,
        string resolvedStatus)
    {
        if (ClinicalQueueRules.ResolveQueueType(examQueue?.QueueType, visitStatus) == ClinicalQueueRules.QueueTypeReturnConsultation)
        {
            return ClinicalQueueRules.QueueTypeReturnConsultation;
        }

        if (resolvedStatus is "WAITING" or "CALLED" &&
            diagQueues is { Count: > 0 } &&
            diagQueues.All(d => string.Equals(d.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase) || d.CompletedAt.HasValue))
        {
            return ClinicalQueueRules.QueueTypeReturnConsultation;
        }

        return ClinicalQueueRules.QueueTypeExamination;
    }

    private static string? NormalizeStatusToken(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return raw.Trim().ToUpperInvariant().Replace(' ', '_').Replace('-', '_');
    }

    private static int GetStatusSortOrder(string status) => status switch
    {
        "IN_CONSULTATION" => 0,
        "CALLED" => 1,
        "WAITING" => 2,
        "AWAITING_RESULTS" => 3,
        "COMPLETED" => 4,
        _ => 5,
    };

    private static List<PendingDiagnosticItemDto> BuildPendingDiagnostics(
        IReadOnlyList<Visit> scopedVisits,
        IReadOnlyDictionary<long, Patient> patientsById,
        IReadOnlyList<QueueEntry> scopedQueueEntries,
        IReadOnlyList<Service> diagnosticServices)
    {
        var visitsById = scopedVisits.ToDictionary(v => v.Id);
        var diagQueueEntries = scopedQueueEntries
            .Where(q =>
                q.VisitId.HasValue &&
                visitsById.ContainsKey(q.VisitId.Value) &&
                string.Equals(q.QueueType, "DIAGNOSTIC", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(q => q.CompletedAt ?? q.StartedAt ?? q.ArrivedAt)
            .ThenByDescending(q => q.Id)
            .ToList();

        var items = new List<PendingDiagnosticItemDto>(diagQueueEntries.Count);

        for (var i = 0; i < diagQueueEntries.Count; i++)
        {
            var diagQueue = diagQueueEntries[i];
            var visit = visitsById[diagQueue.VisitId!.Value];

            Patient? patient = null;
            if (visit.PatientId.HasValue)
            {
                patientsById.TryGetValue(visit.PatientId.Value, out patient);
            }

            var patientName = !string.IsNullOrWhiteSpace(patient?.FullName)
                ? patient.FullName
                : $"Bệnh nhân #{visit.PatientId ?? visit.Id}";

            var patientCode = !string.IsNullOrWhiteSpace(patient?.PatientCode)
                ? patient.PatientCode
                : $"PAT-{(visit.PatientId ?? visit.Id):D4}";

            var serviceName = ResolveDiagnosticServiceName(diagQueue, diagnosticServices, i);

            var orderedTime = diagQueue.ArrivedAt
                ?? diagQueue.ReadyAt
                ?? diagQueue.StartedAt
                ?? visit.CheckedInAt;

            var orderedAt = orderedTime.HasValue
                ? orderedTime.Value.ToString("HH:mm", CultureInfo.InvariantCulture)
                : "08:30";

            var isReady = string.Equals(diagQueue.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase) ||
                          diagQueue.CompletedAt.HasValue;

            items.Add(new PendingDiagnosticItemDto(
                Id: $"diag-{diagQueue.Id}",
                OrderCode: $"CLS-{diagQueue.Id:D4}",
                PatientName: patientName,
                PatientCode: patientCode,
                ServiceName: serviceName,
                OrderedAt: orderedAt,
                Status: isReady ? "RESULT_READY" : "IN_PROGRESS"));
        }

        return items
            .OrderBy(d => string.Equals(d.Status, "RESULT_READY", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenByDescending(d => d.OrderedAt, StringComparer.Ordinal)
            .ToList();
    }

    private static string ResolveDiagnosticServiceName(
        QueueEntry diagQueue,
        IReadOnlyList<Service> diagnosticServices,
        int index)
    {
        if (diagnosticServices.Count == 0)
        {
            return "Xét nghiệm & Chẩn đoán hình ảnh";
        }

        var selector = diagQueue.ServicePointId.HasValue && diagQueue.ServicePointId.Value > 0
            ? (int)((diagQueue.ServicePointId.Value - 1) % diagnosticServices.Count)
            : index % diagnosticServices.Count;

        return diagnosticServices[selector].Name;
    }

    private static IReadOnlyList<DoctorQuickActionDto> BuildQuickActions(
        IReadOnlyCollection<string> permissionCodes,
        string roomCode)
    {
        var permissionSet = new HashSet<string>(permissionCodes, StringComparer.OrdinalIgnoreCase);
        var hasMedicalRecordWrite = permissionSet.Contains("MEDICAL_RECORD_WRITE");
        var hasQueueManage = permissionSet.Contains("QUEUE_MANAGE");
        var hasDiagnosticOrder = permissionSet.Contains("DIAGNOSTIC_ORDER");
        var hasPrescriptionIssue = permissionSet.Contains("PRESCRIPTION_ISSUE");

        var actions = new List<DoctorQuickActionDto>();

        if (hasMedicalRecordWrite || hasQueueManage)
        {
            actions.Add(new DoctorQuickActionDto(
                Id: "call-next",
                Label: "Gọi bệnh nhân tiếp theo",
                Description: $"Mời ca đủ điều kiện kế tiếp trong hàng đợi vào phòng khám {roomCode}",
                UcCode: "UC30",
                Shortcut: "F1",
                Route: "/doctor?section=assigned-queue",
                Icon: "M11 5.882V19.24a1.76 1.76 0 01-3.417.592l-2.147-6.15M18 13a3 3 0 100-6M5.436 13.683A4.001 4.001 0 017 6h1.832c4.1 0 7.625-1.234 9.168-3v14c-1.543-1.766-5.067-3-9.168-3H7a3.988 3.988 0 01-1.564-.317z",
                Accent: "sky"));
        }

        if (hasMedicalRecordWrite)
        {
            actions.Add(new DoctorQuickActionDto(
                Id: "open-consultation",
                Label: "Mở phiếu khám lâm sàng",
                Description: "Ghi nhận sinh hiệu, triệu chứng, tiền sử bệnh và chẩn đoán ICD-10",
                UcCode: "UC33",
                Shortcut: "F2",
                Route: "/doctor?section=consultation",
                Icon: "M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z",
                Accent: "emerald"));
        }

        if (hasDiagnosticOrder)
        {
            actions.Add(new DoctorQuickActionDto(
                Id: "create-diagnostic",
                Label: "Chỉ định cận lâm sàng",
                Description: "Tạo phiếu chỉ định xét nghiệm, siêu âm, X-quang cho ca đang khám",
                UcCode: "UC34",
                Shortcut: "F3",
                Route: "/doctor?section=diagnostics",
                Icon: "M19.428 15.428a2 2 0 00-1.022-.547l-2.387-.477a6 6 0 00-3.86.517l-.318.158a6 6 0 01-3.86.517L6.05 15.21a2 2 0 00-1.806.547M8 4h8l-1 1v5.172a2 2 0 00.586 1.414l5 5c1.26 1.26.367 3.414-1.415 3.414H4.828c-1.782 0-2.674-2.154-1.414-3.414l5-5A2 2 0 009 10.172V5L8 4z",
                Accent: "amber"));
        }

        if (hasPrescriptionIssue)
        {
            actions.Add(new DoctorQuickActionDto(
                Id: "create-prescription",
                Label: "Kê đơn thuốc điện tử",
                Description: "Lập toa thuốc ngoại trú, kiểm tra liều dùng và hoàn tất lượt khám",
                UcCode: "UC37",
                Shortcut: "F4",
                Route: "/doctor?section=prescriptions",
                Icon: "M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z",
                Accent: "rose"));
        }

        return actions;
    }

    private static int CalculateAge(DateOnly? dateOfBirth, DateOnly today)
    {
        if (!dateOfBirth.HasValue)
        {
            return 35;
        }

        var age = today.Year - dateOfBirth.Value.Year;
        if (dateOfBirth.Value > today.AddYears(-age))
        {
            age--;
        }

        return Math.Max(0, age);
    }

    private static string FormatGender(string? rawGender)
    {
        if (string.IsNullOrWhiteSpace(rawGender))
        {
            return "Nam";
        }

        return rawGender.Trim().ToUpperInvariant() switch
        {
            "FEMALE" or "NỮ" or "NU" or "F" => "Nữ",
            _ => "Nam",
        };
    }

    private static string FirstNonEmpty(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate.Trim();
            }
        }

        return string.Empty;
    }
}
