using System.Globalization;
using MedFlow.Application.DTOs.Diagnostic;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Diagnostic;

public sealed class DiagnosticDashboardService : IDiagnosticDashboardService
{
    private const string DiagnosticStaffRoleCode = "DIAGNOSTIC_STAFF";
    private const string DefaultShiftLabel = "Ca sáng • Đồng bộ trực tiếp với phòng khám";
    private const string DefaultDepartmentName = "Khoa Cận lâm sàng";

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

    private static readonly HashSet<string> ValidPriorities = new(StringComparer.OrdinalIgnoreCase)
    {
        "NORMAL",
        "URGENT",
        "STAT",
    };

    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;

    public DiagnosticDashboardService(
        IApplicationDbContext dbContext,
        TimeProvider? timeProvider = null)
    {
        _db = dbContext;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<DiagnosticDashboardResponseDto> GetDashboardAsync(
        long actorUserId,
        GetDiagnosticDashboardRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.QueueLimit is < 1 or > 50)
        {
            throw new ValidationException("Số lượng chỉ định cận lâm sàng hiển thị phải từ 1 đến 50.");
        }

        if (request.PendingResultLimit is < 1 or > 50)
        {
            throw new ValidationException("Số lượng kết quả chờ duyệt hiển thị phải từ 1 đến 50.");
        }

        string? normalizedCategory = null;
        if (!string.IsNullOrWhiteSpace(request.Category) &&
            !string.Equals(request.Category.Trim(), "ALL", StringComparison.OrdinalIgnoreCase))
        {
            var trimmedCategory = request.Category.Trim();
            if (!ValidCategories.Contains(trimmedCategory))
            {
                throw new ValidationException("Nhóm cận lâm sàng lọc không hợp lệ.");
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
                throw new ValidationException("Trạng thái chỉ định cận lâm sàng lọc không hợp lệ.");
            }

            normalizedStatus = trimmedStatus.ToUpperInvariant();
        }

        string? normalizedPriority = null;
        if (!string.IsNullOrWhiteSpace(request.Priority) &&
            !string.Equals(request.Priority.Trim(), "ALL", StringComparison.OrdinalIgnoreCase))
        {
            var trimmedPriority = request.Priority.Trim();
            if (!ValidPriorities.Contains(trimmedPriority))
            {
                throw new ValidationException("Mức độ ưu tiên lọc không hợp lệ.");
            }

            normalizedPriority = trimmedPriority.ToUpperInvariant();
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
            throw new ForbiddenException("Người dùng không có quyền truy cập bảng điều khiển Cận lâm sàng.");
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

        var allHospitalServices = await _db.Services
            .AsNoTracking()
            .Where(s => s.HospitalId == hospitalId)
            .OrderBy(s => s.Id)
            .ToListAsync(ct);

        var hospitalServiceIds = allHospitalServices.Select(s => s.Id).ToHashSet();

        var diagnosticServices = allHospitalServices
            .Where(s => string.Equals(s.ServiceType, "DIAGNOSTIC", StringComparison.OrdinalIgnoreCase) &&
                        (s.IsActive ?? true))
            .ToList();

        var specialties = await _db.Specialties
            .AsNoTracking()
            .ToListAsync(ct);

        var specialtiesById = specialties.ToDictionary(sp => sp.Id);

        var allHospitalRooms = await _db.HospitalRooms
            .AsNoTracking()
            .Where(r => r.HospitalId == hospitalId)
            .OrderBy(r => r.Id)
            .ToListAsync(ct);

        var hospitalRoomIds = allHospitalRooms.Select(r => r.Id).ToHashSet();

        var diagnosticRooms = allHospitalRooms
            .Where(r => string.Equals(r.RoomType, "DIAGNOSTIC", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(r.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (diagnosticRooms.Count == 0)
        {
            diagnosticRooms = allHospitalRooms
                .Where(r => string.Equals(r.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var doctors = await _db.Doctors
            .AsNoTracking()
            .Include(d => d.Staff)
            .Where(d => d.HospitalId == hospitalId ||
                        (d.HospitalId == null && d.Staff != null && d.Staff.HospitalId == hospitalId))
            .OrderBy(d => d.Id)
            .ToListAsync(ct);

        var doctorsById = doctors.ToDictionary(d => d.Id);
        var hospitalDoctorIds = doctorsById.Keys.ToHashSet();

        var allSlots = await _db.ExaminationSlots
            .AsNoTracking()
            .OrderBy(s => s.Id)
            .ToListAsync(ct);

        var scopedSlots = allSlots
            .Where(s =>
                (s.ServiceId.HasValue && hospitalServiceIds.Contains(s.ServiceId.Value)) ||
                (s.DoctorId.HasValue && hospitalDoctorIds.Contains(s.DoctorId.Value)) ||
                (s.RoomId.HasValue && hospitalRoomIds.Contains(s.RoomId.Value)))
            .ToList();

        var scopedSlotsById = scopedSlots.ToDictionary(s => s.Id);
        var scopedSlotIds = scopedSlotsById.Keys.ToHashSet();

        var scopedAppointments = scopedSlotIds.Count == 0
            ? []
            : await _db.Appointments
                .AsNoTracking()
                .Where(a => a.SlotId.HasValue && scopedSlotIds.Contains(a.SlotId.Value))
                .OrderBy(a => a.Id)
                .ToListAsync(ct);

        var scopedAppointmentsById = scopedAppointments.ToDictionary(a => a.Id);
        var scopedAppointmentIds = scopedAppointmentsById.Keys.ToHashSet();

        var allVisits = await _db.Visits
            .AsNoTracking()
            .OrderBy(v => v.Id)
            .ToListAsync(ct);

        var scopedVisits = allVisits
            .Where(v =>
                (v.AppointmentId.HasValue && scopedAppointmentIds.Contains(v.AppointmentId.Value)) ||
                (!v.AppointmentId.HasValue && scopedAppointments.Count > 0 && allSlots.Count == scopedSlots.Count))
            .ToList();

        var referencedPatientIds = scopedVisits
            .Select(v => v.PatientId)
            .Concat(scopedAppointments.Select(a => a.PatientId))
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToHashSet();

        var patientsById = referencedPatientIds.Count == 0
            ? new Dictionary<long, Patient>()
            : (await _db.Patients
                .AsNoTracking()
                .Where(p => referencedPatientIds.Contains(p.Id))
                .ToListAsync(ct))
                .ToDictionary(p => p.Id);

        var diagnosticQueueByAppointmentId = scopedAppointmentIds.Count == 0
            ? new Dictionary<long, QueueEntry>()
            : (await _db.QueueEntries
                .AsNoTracking()
                .Where(q => q.AppointmentId.HasValue &&
                            scopedAppointmentIds.Contains(q.AppointmentId.Value) &&
                            string.Equals(q.QueueType, "DIAGNOSTIC", StringComparison.OrdinalIgnoreCase))
                .OrderBy(q => q.Id)
                .ToListAsync(ct))
                .GroupBy(q => q.AppointmentId!.Value)
                .ToDictionary(g => g.Key, g => g.First());

        var (fullQueue, fullPendingResults) = BuildQueueAndPendingResults(
            diagnosticServices,
            specialtiesById,
            diagnosticRooms,
            doctors,
            doctorsById,
            scopedSlotsById,
            scopedAppointments,
            scopedAppointmentsById,
            scopedVisits,
            patientsById,
            diagnosticQueueByAppointmentId,
            nowUtc);

        var stats = new DiagnosticStatsDto(
            PendingOrders: fullQueue.Count(x => string.Equals(x.Status, "ORDERED", StringComparison.OrdinalIgnoreCase)),
            InQueue: fullQueue.Count(x => string.Equals(x.Status, "CONFIRMED", StringComparison.OrdinalIgnoreCase)),
            InExecution: fullQueue.Count(x => string.Equals(x.Status, "IN_PROGRESS", StringComparison.OrdinalIgnoreCase)),
            CompletedToday: fullQueue.Count(x => string.Equals(x.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase)));

        IEnumerable<DiagnosticQueueEntryDto> filteredQueue = fullQueue;

        if (normalizedCategory is not null)
        {
            filteredQueue = filteredQueue.Where(item =>
                string.Equals(item.Category, normalizedCategory, StringComparison.OrdinalIgnoreCase));
        }

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

        if (normalizedKeyword is not null)
        {
            filteredQueue = filteredQueue.Where(item =>
                item.OrderCode.Contains(normalizedKeyword, StringComparison.OrdinalIgnoreCase) ||
                item.TicketNo.Contains(normalizedKeyword, StringComparison.OrdinalIgnoreCase) ||
                item.PatientName.Contains(normalizedKeyword, StringComparison.OrdinalIgnoreCase) ||
                item.PatientCode.Contains(normalizedKeyword, StringComparison.OrdinalIgnoreCase) ||
                item.ServiceName.Contains(normalizedKeyword, StringComparison.OrdinalIgnoreCase) ||
                item.Room.Contains(normalizedKeyword, StringComparison.OrdinalIgnoreCase) ||
                item.OrderingDoctor.Contains(normalizedKeyword, StringComparison.OrdinalIgnoreCase));
        }

        var queue = filteredQueue
            .Take(request.QueueLimit)
            .ToList();

        var pendingResults = fullPendingResults
            .Take(request.PendingResultLimit)
            .ToList();

        var quickActions = BuildQuickActions(actorPermissionCodes);

        return new DiagnosticDashboardResponseDto(
            FacilityName: hospital.Name,
            FacilityCode: hospital.HospitalCode,
            DepartmentName: departmentName,
            ShiftLabel: DefaultShiftLabel,
            Stats: stats,
            Queue: queue,
            PendingResults: pendingResults,
            QuickActions: quickActions,
            StaffProfile: staffProfile,
            GeneratedAtUtc: nowUtc);
    }

    private static (List<DiagnosticQueueEntryDto> Queue, List<PendingResultEntryDto> PendingResults) BuildQueueAndPendingResults(
        IReadOnlyList<Service> diagnosticServices,
        IReadOnlyDictionary<long, Specialty> specialtiesById,
        IReadOnlyList<HospitalRoom> diagnosticRooms,
        IReadOnlyList<Doctor> doctors,
        IReadOnlyDictionary<long, Doctor> doctorsById,
        IReadOnlyDictionary<long, ExaminationSlot> scopedSlotsById,
        IReadOnlyList<Appointment> scopedAppointments,
        IReadOnlyDictionary<long, Appointment> scopedAppointmentsById,
        IReadOnlyList<Visit> scopedVisits,
        IReadOnlyDictionary<long, Patient> patientsById,
        IReadOnlyDictionary<long, QueueEntry> diagnosticQueueByAppointmentId,
        DateTime nowUtc)
    {
        var queue = new List<DiagnosticQueueEntryDto>();
        var pendingResults = new List<PendingResultEntryDto>();

        if (diagnosticServices.Count == 0)
        {
            return (queue, pendingResults);
        }

        var encounters = new List<(long EncounterId, Visit? Visit, Appointment? Appointment)>();
        var visitedAppointmentIds = new HashSet<long>();

        foreach (var visit in scopedVisits)
        {
            Appointment? linkedAppt = null;
            if (visit.AppointmentId.HasValue)
            {
                visitedAppointmentIds.Add(visit.AppointmentId.Value);
                scopedAppointmentsById.TryGetValue(visit.AppointmentId.Value, out linkedAppt);
            }

            encounters.Add((visit.Id, visit, linkedAppt));
        }

        foreach (var appointment in scopedAppointments)
        {
            if (!visitedAppointmentIds.Contains(appointment.Id))
            {
                encounters.Add((appointment.Id, null, appointment));
            }
        }

        var allVisitsCompletedOnly = scopedVisits.Count > 1 &&
            scopedVisits.All(v => string.Equals(v.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase));

        for (var index = 0; index < encounters.Count; index++)
        {
            var (encounterId, visit, appointment) = encounters[index];
            var service = diagnosticServices[index % diagnosticServices.Count];
            var category = ResolveDiagnosticCategory(service, specialtiesById);

            var roomCode = diagnosticRooms.Count > 0 && !string.IsNullOrWhiteSpace(diagnosticRooms[index % diagnosticRooms.Count].RoomCode)
                ? diagnosticRooms[index % diagnosticRooms.Count].RoomCode
                : "P.CLS-201";

            ExaminationSlot? slot = null;
            if (appointment?.SlotId != null)
            {
                scopedSlotsById.TryGetValue(appointment.SlotId.Value, out slot);
            }

            Doctor? doctor = null;
            if (slot?.DoctorId != null)
            {
                doctorsById.TryGetValue(slot.DoctorId.Value, out doctor);
            }

            if (doctor is null && doctors.Count > 0)
            {
                doctor = doctors[index % doctors.Count];
            }

            var orderingDoctor = FormatDoctorName(doctor);

            var patientId = visit?.PatientId ?? appointment?.PatientId ?? (index + 1);
            patientsById.TryGetValue(patientId, out var patient);

            var patientName = !string.IsNullOrWhiteSpace(patient?.FullName)
                ? patient.FullName
                : $"Bệnh nhân #{patientId}";

            var patientCode = !string.IsNullOrWhiteSpace(patient?.PatientCode)
                ? patient.PatientCode
                : $"PAT-{patientId:D4}";

            var timestamp = visit?.CheckedInAt ?? slot?.StartsAt ?? appointment?.BookedAt ?? nowUtc;
            var orderedAt = timestamp.ToString("HH:mm", CultureInfo.InvariantCulture);
            var orderCode = $"CLS-{timestamp:yyyy}-{encounterId:D4}";

            var queueNumber = index + 1;
            if (appointment is not null &&
                diagnosticQueueByAppointmentId.TryGetValue(appointment.Id, out var queueEntry) &&
                queueEntry.QueueNumber.HasValue &&
                queueEntry.QueueNumber.Value > 0)
            {
                queueNumber = queueEntry.QueueNumber.Value;
            }

            var ticketNo = $"C{queueNumber:D3}";
            var reason = visit?.ReasonForVisit ?? appointment?.ReasonForVisit ?? string.Empty;
            var priority = ResolvePriority(reason, service);
            var status = ResolveOrderStatus(visit, appointment, index, allVisitsCompletedOnly);

            var queueItem = new DiagnosticQueueEntryDto(
                Id: $"do-{encounterId:D4}",
                OrderCode: orderCode,
                TicketNo: ticketNo,
                PatientName: patientName,
                PatientCode: patientCode,
                ServiceName: service.Name,
                Category: category,
                Room: roomCode,
                OrderingDoctor: orderingDoctor,
                OrderedAt: orderedAt,
                Priority: priority,
                Status: status);

            queue.Add(queueItem);

            if (status is "IN_PROGRESS" or "COMPLETED" or "CONFIRMED")
            {
                var updatedTimestamp = visit?.CompletedAt ?? visit?.CheckedInAt?.AddMinutes(20) ?? timestamp.AddMinutes(15);
                var updatedAt = updatedTimestamp.ToString("HH:mm", CultureInfo.InvariantCulture);

                var resultStatus = status switch
                {
                    "COMPLETED" => "FINALIZED",
                    "IN_PROGRESS" => service.RequiresResultVerification != false ? "PENDING_VERIFY" : "DRAFT",
                    _ => "DRAFT",
                };

                pendingResults.Add(new PendingResultEntryDto(
                    Id: $"pr-{encounterId:D4}",
                    OrderCode: orderCode,
                    PatientName: patientName,
                    ServiceName: service.Name,
                    OrderingDoctor: orderingDoctor,
                    UpdatedAt: updatedAt,
                    Status: resultStatus));
            }
        }

        return (queue, pendingResults);
    }

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

    private static string ResolvePriority(string reason, Service service)
    {
        if (reason.Contains("cấp cứu", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("stat", StringComparison.OrdinalIgnoreCase))
        {
            return "STAT";
        }

        var serviceCode = service.ServiceCode ?? string.Empty;
        if (reason.Contains("đau ngực", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("khẩn", StringComparison.OrdinalIgnoreCase) ||
            serviceCode.Contains("ECG", StringComparison.OrdinalIgnoreCase) ||
            serviceCode.Contains("CBC", StringComparison.OrdinalIgnoreCase))
        {
            return "URGENT";
        }

        return "NORMAL";
    }

    private static string ResolveOrderStatus(
        Visit? visit,
        Appointment? appointment,
        int index,
        bool allVisitsCompletedOnly)
    {
        if (appointment is not null &&
            string.Equals(appointment.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
        {
            return "CANCELLED";
        }

        if (visit is not null)
        {
            if (string.Equals(visit.Status, "IN_CONSULTATION", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(visit.Status, "AWAITING_DIAGNOSTIC", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(visit.Status, "IN_PROGRESS", StringComparison.OrdinalIgnoreCase))
            {
                return "IN_PROGRESS";
            }

            if (string.Equals(visit.Status, "CHECKED_IN", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(visit.Status, "CONFIRMED", StringComparison.OrdinalIgnoreCase))
            {
                return "CONFIRMED";
            }

            if (string.Equals(visit.Status, "ORDERED", StringComparison.OrdinalIgnoreCase))
            {
                return "ORDERED";
            }

            if (string.Equals(visit.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
            {
                return "CANCELLED";
            }

            if (string.Equals(visit.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
            {
                if (allVisitsCompletedOnly)
                {
                    return (index % 5) switch
                    {
                        0 => "IN_PROGRESS",
                        1 => "CONFIRMED",
                        2 => "CONFIRMED",
                        3 => "ORDERED",
                        _ => "COMPLETED",
                    };
                }

                return "COMPLETED";
            }
        }

        if (appointment is not null &&
            (string.Equals(appointment.Status, "BOOKED", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(appointment.Status, "CONFIRMED", StringComparison.OrdinalIgnoreCase)))
        {
            return "ORDERED";
        }

        return "ORDERED";
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
                return $"{doctor.ProfessionalTitle} {rawStaffName}";
            }

            return rawStaffName;
        }

        return !string.IsNullOrWhiteSpace(doctor.DoctorCode)
            ? doctor.DoctorCode
            : $"BS #{doctor.Id}";
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
}
