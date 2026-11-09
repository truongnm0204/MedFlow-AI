using System.Globalization;
using System.Net;
using System.Text.Json;
using MedFlow.Application.DTOs.Visits;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Appointments;
using MedFlow.Application.Services.Policies;
using MedFlow.Application.Services.Visits.Queueing;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Visits;

public sealed class VisitRoomAssignmentService : IVisitRoomAssignmentService
{
    public const string VisitRoomAssignedAction = "VISIT_EXAMINATION_ROOM_ASSIGNED";
    public const string VisitAssignPermissionCode = "VISIT_ASSIGN";

    private static readonly HashSet<string> ActiveConsultationStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "IN_CONSULTATION",
        "IN_PROGRESS",
        "AWAITING_DIAGNOSTIC",
        "AWAITING_RESULTS",
        "RETURN_CONSULTATION",
    };

    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;

    public VisitRoomAssignmentService(
        IApplicationDbContext db,
        TimeProvider timeProvider)
    {
        _db = db;
        _timeProvider = timeProvider;
    }

    public Task<AssignExaminationRoomResponseDto> AssignExaminationRoomAsync(
        long actorUserId,
        long visitId,
        AssignExaminationRoomRequest request,
        string? ipAddress = null,
        CancellationToken ct = default)
        => AssignExaminationRoomAsync(
            actorUserId,
            visitId.ToString(CultureInfo.InvariantCulture),
            request,
            ipAddress,
            ct);

    public async Task<AssignExaminationRoomResponseDto> AssignExaminationRoomAsync(
        long actorUserId,
        string visitIdOrCode,
        AssignExaminationRoomRequest request,
        string? ipAddress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var parsedVisit = ParseVisitIdentifier(visitIdOrCode);

        var targetRoomId = request.ExaminationRoomId ?? request.RoomId;
        var rawRoomCode = request.RoomCode;

        if (targetRoomId.HasValue && targetRoomId.Value <= 0)
        {
            throw new ValidationException("ID buồng khám phải lớn hơn 0.");
        }

        if (rawRoomCode is not null && string.IsNullOrWhiteSpace(rawRoomCode) && !targetRoomId.HasValue)
        {
            throw new ValidationException("Mã buồng khám không được để trống.");
        }

        if (!targetRoomId.HasValue && string.IsNullOrWhiteSpace(rawRoomCode))
        {
            throw new ValidationException("Vui lòng chọn buồng khám cần gán (ExaminationRoomId hoặc RoomCode).");
        }

        string? targetRoomCode = null;
        if (!string.IsNullOrWhiteSpace(rawRoomCode))
        {
            targetRoomCode = rawRoomCode.Trim().ToUpperInvariant();
            if (targetRoomCode.Length > 40 ||
                !targetRoomCode.All(ch => char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' || ch == '.'))
            {
                throw new ValidationException("Mã buồng khám không hợp lệ.");
            }
        }

        string? normalizedPriority = null;
        if (request.Priority is not null)
        {
            if (string.IsNullOrWhiteSpace(request.Priority))
            {
                throw new ValidationException("Mức độ ưu tiên không được để trống.");
            }

            normalizedPriority = ClinicalQueueRules.NormalizePriorityOrThrow(request.Priority);
        }

        var rawReason = request.Reason ?? request.Note;
        var trimmedReason = string.IsNullOrWhiteSpace(rawReason) ? null : rawReason.Trim();
        if (trimmedReason is not null && trimmedReason.Length > 500)
        {
            throw new ValidationException("Lý do / ghi chú phân phòng không được vượt quá 500 ký tự.");
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var actor = await EnsureRoutingActorAccessAsync(actorUserId, nowUtc, ct);

        var visit = await LoadVisitAsync(parsedVisit, asNoTracking: false, ct)
            ?? throw new NotFoundException("Visit", visitIdOrCode.Trim());

        var appointment = await LoadAppointmentWithSlotAsync(visit.AppointmentId, ct);

        var normalizedVisitStatus = AppointmentBookingRules.NormalizeStatusKey(visit.Status);
        EnsureVisitCanBeAssignedRoom(visit, normalizedVisitStatus);

        var effectiveDoctorId = visit.AssignedDoctorId ?? appointment?.Slot?.DoctorId;
        if (!effectiveDoctorId.HasValue)
        {
            throw new ConflictException(
                "Lượt khám chưa được phân công bác sĩ phụ trách (UC26). Vui lòng gán bác sĩ trước khi phân buồng khám.");
        }

        var assignedDoctor = await _db.Doctors
            .AsNoTracking()
            .Include(d => d.Staff)
                .ThenInclude(s => s!.Department)
            .Include(d => d.Specialty)
            .FirstOrDefaultAsync(d => d.Id == effectiveDoctorId.Value, ct)
            ?? throw new NotFoundException("Doctor", effectiveDoctorId.Value);

        if (!assignedDoctor.IsActive ||
            (assignedDoctor.Staff is not null &&
             !string.Equals(assignedDoctor.Staff.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ConflictException("Bác sĩ phụ trách lượt khám hiện đang tạm ngưng hoạt động.");
        }

        if (!visit.AssignedDoctorId.HasValue)
        {
            visit.AssignedDoctorId = assignedDoctor.Id;
        }

        var visitHospitalId = await ResolveVisitHospitalIdAsync(visit, appointment, assignedDoctor, ct);
        if (!actor.IsSystemAdmin &&
            visitHospitalId.HasValue &&
            visitHospitalId.Value != actor.ActorHospitalId!.Value)
        {
            throw new ForbiddenException("Bạn không có quyền thao tác trên lượt khám thuộc cơ sở y tế khác.");
        }

        var effectiveHospitalId = visitHospitalId ?? actor.ActorHospitalId;

        var room = await ResolveTargetRoomAsync(targetRoomId, targetRoomCode, effectiveHospitalId, ct)
            ?? throw new NotFoundException(
                "HospitalRoom",
                targetRoomId?.ToString(CultureInfo.InvariantCulture) ?? targetRoomCode!);

        if (room.Department is null && room.DepartmentId.HasValue)
        {
            room.Department = await _db.Departments
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == room.DepartmentId.Value, ct);
        }

        if (!actor.IsSystemAdmin &&
            room.HospitalId.HasValue &&
            room.HospitalId.Value != actor.ActorHospitalId!.Value)
        {
            throw new ForbiddenException("Bạn không có quyền gán buồng khám thuộc cơ sở y tế khác.");
        }

        if (visitHospitalId.HasValue &&
            room.HospitalId.HasValue &&
            room.HospitalId.Value != visitHospitalId.Value)
        {
            throw new ValidationException("Buồng khám không thuộc cùng cơ sở y tế với lượt khám.");
        }

        if (!string.Equals(room.RoomType, "EXAMINATION", StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException(
                $"Phòng '{room.RoomCode}' (loại '{room.RoomType}') không phải là buồng khám lâm sàng (EXAMINATION).");
        }

        if (!string.Equals(room.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException(
                $"Buồng khám '{room.RoomCode}' hiện không khả dụng (trạng thái: {room.Status}).");
        }

        if (room.Department is not null && room.Department.IsActive == false)
        {
            throw new ConflictException(
                $"Khoa phòng '{room.Department.Name}' của buồng khám '{room.RoomCode}' đang tạm ngưng hoạt động.");
        }

        var serviceDepartmentId = appointment?.Slot?.Service?.DepartmentId;
        var doctorDepartmentId = assignedDoctor.Staff?.DepartmentId;

        if (serviceDepartmentId.HasValue &&
            room.DepartmentId.HasValue &&
            room.DepartmentId.Value != serviceDepartmentId.Value)
        {
            throw new ValidationException(
                $"Buồng khám '{room.RoomCode}' không thuộc cùng khoa phòng với dịch vụ khám của lượt khám.");
        }

        if (doctorDepartmentId.HasValue &&
            room.DepartmentId.HasValue &&
            room.DepartmentId.Value != doctorDepartmentId.Value)
        {
            throw new ValidationException(
                $"Buồng khám '{room.RoomCode}' không tương thích với khoa phòng của bác sĩ phụ trách '{assignedDoctor.DoctorCode}'.");
        }

        var activeOccupancyExcludingCurrent = await CountActiveRoomOccupancyAsync(room.Id, visit.Id, ct);
        if (room.Capacity.HasValue &&
            room.Capacity.Value > 0 &&
            activeOccupancyExcludingCurrent >= room.Capacity.Value)
        {
            throw new ConflictException(
                $"Buồng khám '{room.RoomCode}' đã đầy ({activeOccupancyExcludingCurrent}/{room.Capacity.Value} ca đang hoạt động). Vui lòng chọn buồng khám khác.");
        }

        var previousRoomId = visit.ExaminationRoomId;
        var previousStatus = visit.Status;
        string? previousRoomCode = null;
        if (previousRoomId.HasValue)
        {
            previousRoomCode = previousRoomId.Value == room.Id
                ? room.RoomCode
                : await _db.HospitalRooms
                    .AsNoTracking()
                    .Where(r => r.Id == previousRoomId.Value)
                    .Select(r => r.RoomCode)
                    .FirstOrDefaultAsync(ct);
        }

        var nowLocal = AppointmentBookingRules.GetVietnamNow(_timeProvider);
        var today = DateOnly.FromDateTime(nowLocal);

        visit.ExaminationRoomId = room.Id;
        visit.Status = string.Equals(normalizedVisitStatus, "CALLED", StringComparison.OrdinalIgnoreCase)
            ? "CALLED"
            : "WAITING";
        visit.CheckedInAt ??= nowLocal;
        if (!visit.CheckedInByStaffId.HasValue && actor.ActorStaffId.HasValue)
        {
            visit.CheckedInByStaffId = actor.ActorStaffId.Value;
        }

        var servicePoint = await ResolveQueueServicePointAsync(room.Id, assignedDoctor.Id, effectiveHospitalId, ct);
        var existingEntry = await FindExistingExaminationQueueEntryAsync(visit, ct);

        Patient? patient = appointment?.Patient;
        if (patient is null && visit.PatientId.HasValue)
        {
            patient = await _db.Patients
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == visit.PatientId.Value, ct);
        }

        if (normalizedPriority is not null &&
            ClinicalQueueRules.IsElevated(normalizedPriority) &&
            normalizedPriority != ClinicalQueueRules.NormalizeStoredPriority(existingEntry?.ManualPriority))
        {
            if (trimmedReason is null)
            {
                throw new ValidationException("Vui lòng nhập lý do khi áp dụng mức ưu tiên ELDERLY/PREGNANT.");
            }

            ClinicalQueueRules.EnsurePatientEligible(normalizedPriority, patient, today);

            var verificationRequired = await QueuePolicyReader.IsManualPriorityVerificationRequiredAsync(
                _db,
                effectiveHospitalId,
                servicePoint?.Id,
                nowUtc,
                ct);

            if (verificationRequired && request.PriorityVerificationConfirmed != true)
            {
                throw new ValidationException("Chính sách bệnh viện yêu cầu xác minh giấy tờ trước khi áp dụng ưu tiên ELDERLY/PREGNANT (priorityVerificationConfirmed = true).");
            }
        }

        var graceMinutes = effectiveHospitalId.HasValue
            ? (await QueueOrderingPolicyLoader.LoadAsync(_db, effectiveHospitalId.Value, nowUtc, ct)).ForServicePoint(servicePoint?.Id).GraceMinutes
            : Queueing.QueuePolicyKeys.DefaultGraceMinutes;

        var queueEntry = await UpsertExaminationQueueEntryAsync(
            visit,
            existingEntry,
            appointment,
            servicePoint,
            normalizedPriority,
            today,
            nowLocal,
            graceMinutes,
            ct);

        var effectiveHospitalForAudit = visitHospitalId ?? room.HospitalId ?? actor.ActorHospitalId;
        var changeData = JsonSerializer.Serialize(new
        {
            visitCode = visit.VisitCode,
            patientId = visit.PatientId,
            appointmentId = visit.AppointmentId,
            assignedDoctorId = visit.AssignedDoctorId,
            previous = new
            {
                examinationRoomId = previousRoomId,
                roomCode = previousRoomCode,
                status = previousStatus,
            },
            updated = new
            {
                examinationRoomId = room.Id,
                roomCode = room.RoomCode,
                roomName = room.Name,
                status = visit.Status,
                servicePointId = queueEntry.CurrentServicePointId ?? queueEntry.ServicePointId,
                queueNumber = queueEntry.QueueNumber,
                priority = queueEntry.ManualPriority,
            },
            reason = trimmedReason,
            priorityVerificationConfirmed = request.PriorityVerificationConfirmed == true,
        });

        _db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actorUserId,
            HospitalId = effectiveHospitalForAudit,
            Action = VisitRoomAssignedAction,
            EntityType = "Visit",
            EntityId = visit.Id.ToString(CultureInfo.InvariantCulture),
            Reason = trimmedReason,
            ChangeData = changeData,
            IpAddress = !string.IsNullOrWhiteSpace(ipAddress) && IPAddress.TryParse(ipAddress, out var parsedIp)
                ? parsedIp
                : null,
            OccurredAt = nowUtc,
        });

        await _db.SaveChangesAsync(ct);

        var visitCode = !string.IsNullOrWhiteSpace(visit.VisitCode)
            ? visit.VisitCode
            : $"VIS-{visit.Id:D4}";

        var effectiveServicePointId = queueEntry.CurrentServicePointId ?? queueEntry.ServicePointId;
        var servicePointName = servicePoint?.Name;
        if (servicePointName is null && effectiveServicePointId.HasValue)
        {
            servicePointName = await TryGetServicePointNameAsync(effectiveServicePointId.Value, ct);
        }

        return new AssignExaminationRoomResponseDto(
            VisitId: visit.Id,
            VisitCode: visitCode,
            PatientId: visit.PatientId,
            PatientCode: patient?.PatientCode,
            PatientName: patient?.FullName,
            AppointmentId: visit.AppointmentId,
            AppointmentCode: appointment?.AppointmentCode,
            AssignedDoctorId: assignedDoctor.Id,
            AssignedDoctorCode: assignedDoctor.DoctorCode,
            AssignedDoctorName: assignedDoctor.Staff?.FullName,
            ExaminationRoomId: room.Id,
            RoomCode: room.RoomCode,
            RoomName: room.Name,
            RoomType: room.RoomType,
            Location: room.Location,
            DepartmentId: room.DepartmentId,
            DepartmentCode: room.Department?.DepartmentCode,
            DepartmentName: room.Department?.Name,
            HospitalId: effectiveHospitalForAudit,
            VisitStatus: visit.Status!,
            PreviousRoomId: previousRoomId,
            PreviousRoomCode: previousRoomCode,
            QueueEntryId: queueEntry.Id,
            QueueNumber: queueEntry.QueueNumber,
            QueueCode: queueEntry.QueueNumber.HasValue ? $"B{queueEntry.QueueNumber.Value:D3}" : null,
            QueueStatus: queueEntry.Status,
            Priority: queueEntry.ManualPriority,
            ServicePointId: effectiveServicePointId,
            ServicePointName: servicePointName,
            RoomCapacity: room.Capacity,
            CurrentRoomOccupancy: activeOccupancyExcludingCurrent + 1,
            CheckedInByStaffId: visit.CheckedInByStaffId,
            CheckedInAt: visit.CheckedInAt,
            AssignedAtUtc: nowUtc);
    }

    public async Task<CompatibleExaminationRoomListResponseDto> GetCompatibleRoomsAsync(
        long actorUserId,
        string visitIdOrCode,
        CancellationToken ct = default)
    {
        var parsedVisit = ParseVisitIdentifier(visitIdOrCode);
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var actor = await EnsureRoutingActorAccessAsync(actorUserId, nowUtc, ct);

        var visit = await LoadVisitAsync(parsedVisit, asNoTracking: true, ct)
            ?? throw new NotFoundException("Visit", visitIdOrCode.Trim());

        var appointment = await LoadAppointmentWithSlotAsync(visit.AppointmentId, ct);

        Doctor? assignedDoctor = null;
        var effectiveDoctorId = visit.AssignedDoctorId ?? appointment?.Slot?.DoctorId;
        if (effectiveDoctorId.HasValue)
        {
            assignedDoctor = await _db.Doctors
                .AsNoTracking()
                .Include(d => d.Staff)
                    .ThenInclude(s => s!.Department)
                .FirstOrDefaultAsync(d => d.Id == effectiveDoctorId.Value, ct);
        }

        var visitHospitalId = await ResolveVisitHospitalIdAsync(visit, appointment, assignedDoctor, ct);
        if (!actor.IsSystemAdmin &&
            visitHospitalId.HasValue &&
            visitHospitalId.Value != actor.ActorHospitalId!.Value)
        {
            throw new ForbiddenException("Bạn không có quyền thao tác trên lượt khám thuộc cơ sở y tế khác.");
        }

        var effectiveHospitalId = visitHospitalId ?? actor.ActorHospitalId;
        var requiredDepartmentId = appointment?.Slot?.Service?.DepartmentId ?? assignedDoctor?.Staff?.DepartmentId;
        string? requiredDepartmentName = null;
        if (requiredDepartmentId.HasValue)
        {
            requiredDepartmentName = assignedDoctor?.Staff?.Department?.Id == requiredDepartmentId.Value
                ? assignedDoctor.Staff.Department.Name
                : await _db.Departments
                    .AsNoTracking()
                    .Where(d => d.Id == requiredDepartmentId.Value)
                    .Select(d => d.Name)
                    .FirstOrDefaultAsync(ct);
        }

        string? currentRoomCode = null;
        if (visit.ExaminationRoomId.HasValue)
        {
            currentRoomCode = await _db.HospitalRooms
                .AsNoTracking()
                .Where(r => r.Id == visit.ExaminationRoomId.Value)
                .Select(r => r.RoomCode)
                .FirstOrDefaultAsync(ct);
        }

        var roomsQuery = _db.HospitalRooms
            .AsNoTracking()
            .Include(r => r.Department)
            .Where(r => r.RoomType != null && r.RoomType.ToUpper() == "EXAMINATION");

        if (effectiveHospitalId.HasValue)
        {
            roomsQuery = roomsQuery.Where(r => r.HospitalId == effectiveHospitalId.Value);
        }

        var rooms = await roomsQuery
            .OrderBy(r => r.RoomCode)
            .ToListAsync(ct);

        var roomIds = rooms.Select(r => r.Id).ToList();
        var occupancyByRoomId = new Dictionary<long, int>();
        if (roomIds.Count > 0)
        {
            var occupancyRows = await _db.Visits
                .AsNoTracking()
                .Where(v =>
                    v.Id != visit.Id &&
                    v.ExaminationRoomId.HasValue &&
                    roomIds.Contains(v.ExaminationRoomId.Value) &&
                    v.CompletedAt == null &&
                    v.Status != null &&
                    (v.Status.ToUpper() == "CHECKED_IN" ||
                     v.Status.ToUpper() == "CONFIRMED" ||
                     v.Status.ToUpper() == "ROUTED" ||
                     v.Status.ToUpper() == "WAITING" ||
                     v.Status.ToUpper() == "CALLED" ||
                     v.Status.ToUpper() == "IN_PROGRESS" ||
                     v.Status.ToUpper() == "IN_CONSULTATION"))
                .GroupBy(v => v.ExaminationRoomId!.Value)
                .Select(g => new { RoomId = g.Key, Count = g.Count() })
                .ToListAsync(ct);

            foreach (var row in occupancyRows)
            {
                occupancyByRoomId[row.RoomId] = row.Count;
            }
        }

        var items = new List<AssignableExaminationRoomItemDto>(rooms.Count);
        foreach (var room in rooms)
        {
            occupancyByRoomId.TryGetValue(room.Id, out var currentOccupancy);
            var capacity = room.Capacity;
            var availableCapacity = capacity.HasValue
                ? Math.Max(0, capacity.Value - currentOccupancy)
                : int.MaxValue;

            var isCompatible = !requiredDepartmentId.HasValue ||
                               !room.DepartmentId.HasValue ||
                               room.DepartmentId.Value == requiredDepartmentId.Value;

            string? unavailableReason = null;
            if (!isCompatible)
            {
                unavailableReason = "Buồng khám không thuộc cùng khoa phòng với bác sĩ / dịch vụ khám.";
            }
            else if (!string.Equals(room.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
            {
                unavailableReason = $"Buồng khám đang ở trạng thái {room.Status}.";
            }
            else if (room.Department is not null && room.Department.IsActive == false)
            {
                unavailableReason = $"Khoa phòng '{room.Department.Name}' đang tạm ngưng hoạt động.";
            }
            else if (capacity.HasValue && capacity.Value > 0 && currentOccupancy >= capacity.Value)
            {
                unavailableReason = $"Buồng khám đã đầy ({currentOccupancy}/{capacity.Value} ca).";
            }

            var isAvailable = isCompatible && unavailableReason is null;

            items.Add(new AssignableExaminationRoomItemDto(
                Id: room.Id,
                RoomCode: room.RoomCode,
                Name: room.Name,
                RoomType: room.RoomType,
                Location: room.Location,
                HospitalId: room.HospitalId,
                DepartmentId: room.DepartmentId,
                DepartmentCode: room.Department?.DepartmentCode,
                DepartmentName: room.Department?.Name,
                Capacity: capacity,
                CurrentOccupancy: currentOccupancy,
                AvailableCapacity: capacity.HasValue ? availableCapacity : 0,
                Status: room.Status,
                IsCompatible: isCompatible,
                IsAvailable: isAvailable,
                UnavailableReason: unavailableReason));
        }

        var visitCode = !string.IsNullOrWhiteSpace(visit.VisitCode)
            ? visit.VisitCode
            : $"VIS-{visit.Id:D4}";

        return new CompatibleExaminationRoomListResponseDto(
            VisitId: visit.Id,
            VisitCode: visitCode,
            VisitStatus: visit.Status ?? string.Empty,
            HospitalId: effectiveHospitalId,
            AssignedDoctorId: assignedDoctor?.Id ?? visit.AssignedDoctorId,
            AssignedDoctorCode: assignedDoctor?.DoctorCode,
            AssignedDoctorName: assignedDoctor?.Staff?.FullName,
            CurrentExaminationRoomId: visit.ExaminationRoomId,
            CurrentExaminationRoomCode: currentRoomCode,
            RequiredDepartmentId: requiredDepartmentId,
            RequiredDepartmentName: requiredDepartmentName,
            Rooms: items);
    }

    private static (long? Id, string? Code) ParseVisitIdentifier(string visitIdOrCode)
    {
        if (string.IsNullOrWhiteSpace(visitIdOrCode))
        {
            throw new ValidationException("Mã định danh lượt khám không được để trống.");
        }

        var trimmed = visitIdOrCode.Trim();
        if (trimmed.StartsWith('#'))
        {
            trimmed = trimmed[1..].TrimStart();
        }

        if (string.IsNullOrEmpty(trimmed))
        {
            throw new ValidationException("Mã định danh lượt khám không được để trống.");
        }

        if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numericId))
        {
            if (numericId <= 0)
            {
                throw new ValidationException("ID lượt khám phải lớn hơn 0.");
            }

            return (numericId, null);
        }

        if (trimmed.Length > 40 ||
            !trimmed.All(ch => char.IsLetterOrDigit(ch) || ch == '-' || ch == '_'))
        {
            throw new ValidationException("Mã định danh lượt khám không hợp lệ.");
        }

        return (null, trimmed.ToUpperInvariant());
    }

    private static void EnsureVisitCanBeAssignedRoom(Visit visit, string normalizedVisitStatus)
    {
        if (visit.CompletedAt.HasValue ||
            string.Equals(normalizedVisitStatus, "COMPLETED", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException("Lượt khám đã hoàn tất, không thể gán buồng khám.");
        }

        if (string.Equals(normalizedVisitStatus, "CANCELLED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedVisitStatus, "NO_SHOW", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException("Lượt khám đã hủy hoặc vắng mặt, không thể gán buồng khám.");
        }

        if (ActiveConsultationStatuses.Contains(normalizedVisitStatus))
        {
            throw new ConflictException("Bệnh nhân đang trong quá trình khám lâm sàng, không thể gán lại buồng khám.");
        }
    }

    private sealed record RoutingActorContext(
        bool IsSystemAdmin,
        long? ActorHospitalId,
        long? ActorStaffId);

    private async Task<RoutingActorContext> EnsureRoutingActorAccessAsync(
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
            throw new ForbiddenException("Tài khoản người dùng không ở trạng thái hoạt động.");
        }

        if (actorUser.LockedUntil.HasValue && actorUser.LockedUntil.Value > nowUtc)
        {
            throw new ForbiddenException("Tài khoản người dùng đang bị tạm khóa.");
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

        var isSystemAdmin = actorRoleCodes.Any(c =>
            string.Equals(c, "SYSTEM_ADMIN", StringComparison.OrdinalIgnoreCase));
        var isHospitalAdmin = actorRoleCodes.Any(c =>
            string.Equals(c, "HOSPITAL_ADMIN", StringComparison.OrdinalIgnoreCase));
        var isReception = actorRoleCodes.Any(c =>
            string.Equals(c, "RECEPTION", StringComparison.OrdinalIgnoreCase));

        if (!isSystemAdmin && !isHospitalAdmin && !isReception)
        {
            throw new ForbiddenException("Người dùng không có quyền phân phòng khám cho lượt khám.");
        }

        if (!isSystemAdmin && actorRoleIds.Count > 0)
        {
            var rolePermissionIds = await _db.RolePermissions
                .AsNoTracking()
                .Where(rp => actorRoleIds.Contains(rp.RoleId))
                .Select(rp => rp.PermissionId)
                .ToListAsync(ct);

            if (rolePermissionIds.Count > 0)
            {
                var permissionCodes = await _db.Permissions
                    .AsNoTracking()
                    .Where(p => rolePermissionIds.Contains(p.Id))
                    .Select(p => p.Code)
                    .ToListAsync(ct);

                var hasVisitAssignPermission = permissionCodes.Any(c =>
                    string.Equals(c, VisitAssignPermissionCode, StringComparison.OrdinalIgnoreCase));

                if (!hasVisitAssignPermission)
                {
                    throw new ForbiddenException(
                        "Vai trò hiện tại chưa được cấp quyền phân công lượt khám (VISIT_ASSIGN).");
                }
            }
        }

        var actorStaff = await _db.Staff
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == actorUserId, ct);

        if (actorStaff is not null &&
            !string.Equals(actorStaff.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            throw new ForbiddenException("Hồ sơ nhân sự của người dùng không ở trạng thái hoạt động.");
        }

        if (!isSystemAdmin)
        {
            if (actorStaff is null || !actorStaff.HospitalId.HasValue)
            {
                throw new ForbiddenException("Tài khoản nhân sự chưa được gán cơ sở y tế.");
            }

            var hospital = await _db.Hospitals
                .AsNoTracking()
                .FirstOrDefaultAsync(h => h.Id == actorStaff.HospitalId.Value, ct)
                ?? throw new NotFoundException("Hospital", actorStaff.HospitalId.Value);

            if (hospital.IsActive == false)
            {
                throw new ForbiddenException("Cơ sở y tế của nhân sự hiện đang ngừng hoạt động.");
            }
        }

        return new RoutingActorContext(isSystemAdmin, actorStaff?.HospitalId, actorStaff?.Id);
    }

    private async Task<Visit?> LoadVisitAsync(
        (long? Id, string? Code) parsedVisit,
        bool asNoTracking,
        CancellationToken ct)
    {
        var query = asNoTracking ? _db.Visits.AsNoTracking() : _db.Visits.AsQueryable();

        if (parsedVisit.Id.HasValue)
        {
            return await query.FirstOrDefaultAsync(v => v.Id == parsedVisit.Id.Value, ct);
        }

        return await query.FirstOrDefaultAsync(
            v => v.VisitCode != null && v.VisitCode.ToUpper() == parsedVisit.Code,
            ct);
    }

    private async Task<Appointment?> LoadAppointmentWithSlotAsync(long? appointmentId, CancellationToken ct)
    {
        if (!appointmentId.HasValue)
        {
            return null;
        }

        return await _db.Appointments
            .AsNoTracking()
            .Include(a => a.Patient)
            .Include(a => a.Slot)
                .ThenInclude(s => s!.Service)
            .Include(a => a.Slot)
                .ThenInclude(s => s!.Room)
            .Include(a => a.Slot)
                .ThenInclude(s => s!.Doctor)
                    .ThenInclude(d => d!.Staff)
            .FirstOrDefaultAsync(a => a.Id == appointmentId.Value, ct);
    }

    private async Task<long?> ResolveVisitHospitalIdAsync(
        Visit visit,
        Appointment? appointment,
        Doctor? assignedDoctor,
        CancellationToken ct)
    {
        var hospitalId = assignedDoctor?.HospitalId
            ?? assignedDoctor?.Staff?.HospitalId
            ?? appointment?.Slot?.Service?.HospitalId
            ?? appointment?.Slot?.Room?.HospitalId
            ?? appointment?.Slot?.Doctor?.HospitalId
            ?? appointment?.Slot?.Doctor?.Staff?.HospitalId;

        if (hospitalId.HasValue)
        {
            return hospitalId.Value;
        }

        if (visit.ExaminationRoomId.HasValue)
        {
            hospitalId = await _db.HospitalRooms
                .AsNoTracking()
                .Where(r => r.Id == visit.ExaminationRoomId.Value)
                .Select(r => r.HospitalId)
                .FirstOrDefaultAsync(ct);

            if (hospitalId.HasValue)
            {
                return hospitalId.Value;
            }
        }

        if (visit.CheckedInByStaffId.HasValue)
        {
            hospitalId = await _db.Staff
                .AsNoTracking()
                .Where(s => s.Id == visit.CheckedInByStaffId.Value)
                .Select(s => s.HospitalId)
                .FirstOrDefaultAsync(ct);
        }

        return hospitalId;
    }

    private async Task<HospitalRoom?> ResolveTargetRoomAsync(
        long? targetRoomId,
        string? targetRoomCode,
        long? effectiveHospitalId,
        CancellationToken ct)
    {
        if (targetRoomId.HasValue)
        {
            return await _db.HospitalRooms
                .AsNoTracking()
                .Include(r => r.Department)
                .FirstOrDefaultAsync(r => r.Id == targetRoomId.Value, ct);
        }

        if (!string.IsNullOrWhiteSpace(targetRoomCode))
        {
            if (effectiveHospitalId.HasValue)
            {
                var scopedRoom = await _db.HospitalRooms
                    .AsNoTracking()
                    .Include(r => r.Department)
                    .FirstOrDefaultAsync(
                        r => r.RoomCode.ToUpper() == targetRoomCode &&
                             (r.HospitalId == effectiveHospitalId.Value || r.HospitalId == null),
                        ct);

                if (scopedRoom is not null)
                {
                    return scopedRoom;
                }
            }

            return await _db.HospitalRooms
                .AsNoTracking()
                .Include(r => r.Department)
                .FirstOrDefaultAsync(r => r.RoomCode.ToUpper() == targetRoomCode, ct);
        }

        return null;
    }

    private Task<int> CountActiveRoomOccupancyAsync(
        long roomId,
        long currentVisitId,
        CancellationToken ct)
        => _db.Visits
            .AsNoTracking()
            .CountAsync(
                v => v.Id != currentVisitId &&
                     v.ExaminationRoomId == roomId &&
                     v.CompletedAt == null &&
                     v.Status != null &&
                     (v.Status.ToUpper() == "CHECKED_IN" ||
                      v.Status.ToUpper() == "CONFIRMED" ||
                      v.Status.ToUpper() == "ROUTED" ||
                      v.Status.ToUpper() == "WAITING" ||
                      v.Status.ToUpper() == "CALLED" ||
                      v.Status.ToUpper() == "IN_PROGRESS" ||
                      v.Status.ToUpper() == "IN_CONSULTATION"),
                ct);

    private async Task<QueueServicePoint?> ResolveQueueServicePointAsync(
        long roomId,
        long doctorId,
        long? hospitalId,
        CancellationToken ct)
    {
        try
        {
            var candidates = await _db.QueueServicePoints
                .AsNoTracking()
                .Where(sp =>
                    sp.IsActive != false &&
                    (!hospitalId.HasValue || sp.HospitalId == null || sp.HospitalId == hospitalId.Value) &&
                    (sp.RoomId == roomId || sp.DoctorId == doctorId))
                .ToListAsync(ct);

            return candidates.FirstOrDefault(sp => sp.RoomId == roomId && sp.DoctorId == doctorId)
                ?? candidates.FirstOrDefault(sp => sp.RoomId == roomId)
                ?? candidates.FirstOrDefault(sp => sp.DoctorId == doctorId);
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private async Task<string?> TryGetServicePointNameAsync(long servicePointId, CancellationToken ct)
    {
        try
        {
            return await _db.QueueServicePoints
                .AsNoTracking()
                .Where(sp => sp.Id == servicePointId)
                .Select(sp => sp.Name)
                .FirstOrDefaultAsync(ct);
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private async Task<QueueEntry?> FindExistingExaminationQueueEntryAsync(
        Visit visit,
        CancellationToken ct)
    {
        var existingEntries = await _db.QueueEntries
            .Where(q =>
                (q.VisitId == visit.Id ||
                 (visit.AppointmentId.HasValue && q.AppointmentId == visit.AppointmentId.Value)) &&
                (q.QueueType == null ||
                 q.QueueType.ToUpper() == "EXAMINATION" ||
                 q.QueueType.ToUpper() == "RETURN_CONSULTATION") &&
                (q.Status == null || q.Status.ToUpper() != "CANCELLED"))
            .OrderByDescending(q => q.Id)
            .ToListAsync(ct);

        return existingEntries.FirstOrDefault(q => q.VisitId == visit.Id)
            ?? existingEntries.FirstOrDefault();
    }

    private async Task<QueueEntry> UpsertExaminationQueueEntryAsync(
        Visit visit,
        QueueEntry? existingEntry,
        Appointment? appointment,
        QueueServicePoint? servicePoint,
        string? normalizedPriority,
        DateOnly today,
        DateTime nowLocal,
        int graceMinutes,
        CancellationToken ct)
    {
        var queueEntry = existingEntry;

        if (queueEntry is not null)
        {
            queueEntry.VisitId = visit.Id;
            if (string.IsNullOrWhiteSpace(queueEntry.QueueType))
            {
                queueEntry.QueueType = ClinicalQueueRules.QueueTypeExamination;
            }

            if (servicePoint is not null)
            {
                queueEntry.ServicePointId ??= servicePoint.Id;
                queueEntry.CurrentServicePointId = servicePoint.Id;
            }

            if (string.IsNullOrWhiteSpace(queueEntry.Status) ||
                string.Equals(queueEntry.Status, "RESERVED", StringComparison.OrdinalIgnoreCase))
            {
                queueEntry.Status = "WAITING";
            }

            if (string.IsNullOrWhiteSpace(queueEntry.PresenceStatus) ||
                string.Equals(queueEntry.PresenceStatus, "NOT_ARRIVED", StringComparison.OrdinalIgnoreCase))
            {
                queueEntry.PresenceStatus = "PRESENT";
            }

            queueEntry.QueueDate ??= today;
            queueEntry.ArrivedAt ??= visit.CheckedInAt ?? nowLocal;
            queueEntry.ReadyAt ??= nowLocal;
            queueEntry.ArrivalClass = QueueArrivalClassifier.Classify(
                visit.AppointmentId.HasValue || queueEntry.AppointmentId.HasValue,
                queueEntry.ScheduledAt ?? appointment?.Slot?.StartsAt,
                queueEntry.ArrivedAt,
                graceMinutes,
                queueEntry.ArrivalClass ?? "ON_TIME_OR_GRACE");

            if (normalizedPriority is not null)
            {
                if (normalizedPriority != ClinicalQueueRules.NormalizeStoredPriority(queueEntry.ManualPriority))
                {
                    queueEntry.StateVersion = (queueEntry.StateVersion is > 0 ? queueEntry.StateVersion.Value : 1) + 1;
                }

                queueEntry.ManualPriority = normalizedPriority;
            }
            else
            {
                queueEntry.ManualPriority ??= ClinicalQueueRules.PriorityNormal;
            }

            return queueEntry;
        }

        var nextQueueNumber = 1;
        var existingMaxNumber = await _db.QueueEntries
            .AsNoTracking()
            .Where(q =>
                q.QueueDate == today &&
                (q.QueueType == null ||
                 q.QueueType.ToUpper() == "EXAMINATION" ||
                 q.QueueType.ToUpper() == "RETURN_CONSULTATION") &&
                (servicePoint == null ||
                 q.ServicePointId == servicePoint.Id ||
                 q.CurrentServicePointId == servicePoint.Id))
            .Select(q => q.QueueNumber)
            .MaxAsync(ct);

        if (existingMaxNumber.HasValue && existingMaxNumber.Value > 0)
        {
            nextQueueNumber = existingMaxNumber.Value + 1;
        }

        queueEntry = new QueueEntry
        {
            VisitId = visit.Id,
            AppointmentId = visit.AppointmentId,
            ServicePointId = servicePoint?.Id,
            CurrentServicePointId = servicePoint?.Id,
            QueueDate = today,
            QueueNumber = nextQueueNumber,
            QueueType = "EXAMINATION",
            Status = "WAITING",
            ScheduledAt = appointment?.Slot?.StartsAt,
            PresenceStatus = "PRESENT",
            ArrivedAt = visit.CheckedInAt ?? nowLocal,
            ArrivalClass = QueueArrivalClassifier.Classify(
                visit.AppointmentId.HasValue,
                appointment?.Slot?.StartsAt,
                visit.CheckedInAt ?? nowLocal,
                graceMinutes,
                "ON_TIME_OR_GRACE"),
            ManualPriority = normalizedPriority ?? "NORMAL",
            ReadyAt = nowLocal,
        };

        _db.QueueEntries.Add(queueEntry);
        return queueEntry;
    }
}
