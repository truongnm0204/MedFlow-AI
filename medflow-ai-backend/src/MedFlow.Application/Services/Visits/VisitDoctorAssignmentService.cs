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

public sealed class VisitDoctorAssignmentService : IVisitDoctorAssignmentService
{
    public const string VisitDoctorAssignedAction = "VISIT_DOCTOR_ASSIGNED";
    public const string VisitAssignPermissionCode = "VISIT_ASSIGN";
    public const string WorkloadLow = "LOW";
    public const string WorkloadMedium = "MEDIUM";
    public const string WorkloadHigh = "HIGH";

    private const int DefaultExpectedDurationMinutes = 15;
    private const int LowWorkloadMaxWaiting = 3;
    private const int MediumWorkloadMaxWaiting = 6;

    private static readonly HashSet<string> ActiveConsultationStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "IN_CONSULTATION",
        "IN_PROGRESS",
        "AWAITING_DIAGNOSTIC",
        "AWAITING_RESULTS",
        "RETURN_CONSULTATION",
    };

    private static readonly string[] HospitalAdminAllowedPermissions =
    [
        VisitAssignPermissionCode,
        "PATIENT_CHECKIN",
        "QUEUE_MANAGE",
        "HOSPITAL_CONFIG",
    ];

    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;

    public VisitDoctorAssignmentService(
        IApplicationDbContext db,
        TimeProvider? timeProvider = null)
    {
        _db = db;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<AssignDoctorToVisitResponseDto> AssignDoctorAsync(
        long actorUserId,
        long visitId,
        AssignDoctorToVisitRequest request,
        string? ipAddress = null,
        CancellationToken ct = default)
        => AssignDoctorAsync(
            actorUserId,
            visitId.ToString(CultureInfo.InvariantCulture),
            request,
            ipAddress,
            ct);

    public async Task<AssignableDoctorListResponseDto> GetAssignableDoctorsAsync(
        long actorUserId,
        string visitIdOrCode,
        long? serviceId = null,
        string? serviceCode = null,
        long? departmentId = null,
        CancellationToken ct = default)
    {
        var parsedVisit = ParseVisitIdentifier(visitIdOrCode);

        if (serviceId.HasValue && serviceId.Value <= 0)
        {
            throw new ValidationException("ID dịch vụ khám phải lớn hơn 0.");
        }

        string? normalizedServiceCode = null;
        if (serviceCode is not null)
        {
            if (string.IsNullOrWhiteSpace(serviceCode))
            {
                throw new ValidationException("Mã dịch vụ khám không được để trống.");
            }

            normalizedServiceCode = NormalizeCodeOrThrow(serviceCode, "Mã dịch vụ khám không hợp lệ.");
        }

        if (departmentId.HasValue && departmentId.Value <= 0)
        {
            throw new ValidationException("ID khoa phòng phải lớn hơn 0.");
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var nowLocal = AppointmentBookingRules.GetVietnamNow(_timeProvider);
        var today = DateOnly.FromDateTime(nowLocal);

        var actor = await EnsureRoutingActorAccessAsync(actorUserId, nowUtc, ct);

        var visit = await LoadVisitAsync(parsedVisit, asNoTracking: true, ct)
            ?? throw new NotFoundException("Visit", visitIdOrCode.Trim());

        var appointment = await LoadAppointmentWithSlotAsync(visit.AppointmentId, ct);

        Doctor? currentDoctor = null;
        var currentDoctorId = visit.AssignedDoctorId ?? appointment?.Slot?.DoctorId;
        if (currentDoctorId.HasValue)
        {
            currentDoctor = await _db.Doctors
                .AsNoTracking()
                .Include(d => d.Staff)
                    .ThenInclude(s => s!.Department)
                .Include(d => d.Specialty)
                .FirstOrDefaultAsync(d => d.Id == currentDoctorId.Value, ct);
        }

        HospitalRoom? currentRoom = null;
        if (visit.ExaminationRoomId.HasValue)
        {
            currentRoom = await _db.HospitalRooms
                .AsNoTracking()
                .Include(r => r.Department)
                .FirstOrDefaultAsync(r => r.Id == visit.ExaminationRoomId.Value, ct);
        }

        var visitHospitalId = await ResolveVisitHospitalIdAsync(
            visit,
            appointment,
            currentDoctor,
            currentRoom,
            ct);

        if (!actor.IsSystemAdmin &&
            visitHospitalId.HasValue &&
            visitHospitalId.Value != actor.ActorHospitalId!.Value)
        {
            throw new ForbiddenException("Bạn không có quyền thao tác trên lượt khám thuộc cơ sở y tế khác.");
        }

        var effectiveHospitalId = visitHospitalId ?? actor.ActorHospitalId ?? 1L;

        var existingQueueEntry = await FindExistingExaminationQueueEntryAsync(visit, asNoTracking: true, ct);

        var requiredService = await ResolveRequiredServiceAsync(
            serviceId,
            normalizedServiceCode,
            appointment,
            existingQueueEntry,
            effectiveHospitalId,
            ct);

        Department? explicitDepartment = null;
        if (departmentId.HasValue)
        {
            explicitDepartment = await _db.Departments
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == departmentId.Value, ct)
                ?? throw new NotFoundException("Department", departmentId.Value);

            if (explicitDepartment.HospitalId.HasValue &&
                explicitDepartment.HospitalId.Value != effectiveHospitalId)
            {
                throw new ValidationException("Khoa phòng được chọn không thuộc cùng cơ sở y tế với lượt khám.");
            }
        }

        var requiredDepartmentId = explicitDepartment?.Id
            ?? requiredService?.DepartmentId
            ?? currentRoom?.DepartmentId
            ?? currentDoctor?.Staff?.DepartmentId;

        string? requiredDepartmentName = explicitDepartment?.Name
            ?? requiredService?.Department?.Name
            ?? currentRoom?.Department?.Name
            ?? (currentDoctor?.Staff?.Department?.Id == requiredDepartmentId
                ? currentDoctor?.Staff?.Department?.Name
                : null);

        if (requiredDepartmentId.HasValue && string.IsNullOrWhiteSpace(requiredDepartmentName))
        {
            requiredDepartmentName = await _db.Departments
                .AsNoTracking()
                .Where(d => d.Id == requiredDepartmentId.Value)
                .Select(d => d.Name)
                .FirstOrDefaultAsync(ct);
        }

        var patientSummary = await BuildVisitPatientSummaryAsync(
            visit,
            appointment,
            requiredService,
            today,
            nowLocal,
            ct);

        var candidateDoctors = await LoadHospitalDoctorsAsync(effectiveHospitalId, ct);
        var authorizedDoctorIds = await ResolveAuthorizedDoctorIdsAsync(requiredService?.Id, candidateDoctors, ct);
        var roomsInHospital = await _db.HospitalRooms
            .AsNoTracking()
            .Include(r => r.Department)
            .Where(r =>
                r.RoomType != null &&
                r.RoomType.ToUpper() == "EXAMINATION" &&
                (!r.HospitalId.HasValue || r.HospitalId == effectiveHospitalId))
            .OrderBy(r => r.RoomCode)
            .ToListAsync(ct);

        var servicePoints = await LoadHospitalServicePointsAsync(effectiveHospitalId, ct);
        var todaySlots = await LoadTodayDoctorSlotsAsync(
            candidateDoctors.Select(d => d.Id).ToList(),
            today,
            ct);

        var expectedDurationMinutes = await ResolveExpectedDurationMinutesAsync(effectiveHospitalId, nowUtc, ct);
        var workloadByDoctorId = await ComputeDoctorWorkloadsAsync(
            candidateDoctors,
            servicePoints,
            visit.Id,
            today,
            expectedDurationMinutes,
            ct);

        var items = new List<AssignableDoctorItemDto>(candidateDoctors.Count);
        foreach (var doctor in candidateDoctors)
        {
            var staff = doctor.Staff;
            var dept = staff?.Department;
            var isDoctorActive = doctor.IsActive &&
                                 (staff is null || string.Equals(staff.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase));
            var isDeptActive = dept?.IsActive != false;

            var isServiceAuthorized = authorizedDoctorIds is null || authorizedDoctorIds.Contains(doctor.Id);
            var isDepartmentCompatible = !requiredDepartmentId.HasValue ||
                                         staff?.DepartmentId is null ||
                                         staff.DepartmentId.Value == requiredDepartmentId.Value;
            var isRoomDepartmentCompatible = currentRoom?.DepartmentId is null ||
                                             staff?.DepartmentId is null ||
                                             staff.DepartmentId.Value == currentRoom.DepartmentId.Value;
            var isSpecialtyCompatible = requiredService?.SpecialtyId is null ||
                                        !doctor.SpecialtyId.HasValue ||
                                        doctor.SpecialtyId.Value == requiredService.SpecialtyId.Value;

            var isCompatible = isServiceAuthorized &&
                               isDepartmentCompatible &&
                               isRoomDepartmentCompatible &&
                               isSpecialtyCompatible;

            string? unavailableReason = null;
            if (!isDoctorActive)
            {
                unavailableReason = "Bác sĩ hiện đang tạm ngưng hoạt động.";
            }
            else if (!isDeptActive)
            {
                unavailableReason = $"Khoa phòng '{dept!.Name}' đang tạm ngưng hoạt động.";
            }
            else if (!isServiceAuthorized && requiredService is not null)
            {
                unavailableReason = $"Bác sĩ chưa được phân quyền thực hiện dịch vụ '{requiredService.Name}'.";
            }
            else if (!isRoomDepartmentCompatible && currentRoom is not null)
            {
                unavailableReason = $"Bác sĩ không thuộc cùng khoa phòng với buồng khám hiện tại '{currentRoom.RoomCode}'.";
            }
            else if (!isDepartmentCompatible || !isSpecialtyCompatible)
            {
                unavailableReason = "Bác sĩ không thuộc chuyên khoa / khoa phòng yêu cầu của lượt khám.";
            }

            var isAvailable = isCompatible && isDoctorActive && isDeptActive;

            var (resolvedRoom, resolvedServicePoint) = ResolveDoctorRoomAndServicePoint(
                doctor,
                currentRoom,
                roomsInHospital,
                servicePoints,
                todaySlots);

            var workload = workloadByDoctorId.GetValueOrDefault(
                doctor.Id,
                new DoctorWorkloadMetrics(0, 0, "---", 0, "Khám ngay", WorkloadLow));

            var fullName = FormatDoctorDisplayName(doctor);
            var departmentLabel = dept?.Name
                ?? doctor.Specialty?.Name
                ?? requiredDepartmentName
                ?? "Khoa Khám bệnh";
            var roomName = resolvedRoom?.Name ?? "Chưa gán phòng";
            var roomLocation = resolvedRoom?.Location ?? "Khu khám ngoại trú";

            items.Add(new AssignableDoctorItemDto(
                Id: !string.IsNullOrWhiteSpace(doctor.DoctorCode)
                    ? doctor.DoctorCode
                    : doctor.Id.ToString(CultureInfo.InvariantCulture),
                DoctorId: doctor.Id,
                DoctorCode: doctor.DoctorCode,
                FullName: fullName,
                ProfessionalTitle: doctor.ProfessionalTitle,
                SpecialtyId: doctor.SpecialtyId,
                SpecialtyCode: doctor.Specialty?.SpecialtyCode,
                SpecialtyName: doctor.Specialty?.Name,
                DepartmentId: staff?.DepartmentId ?? dept?.Id,
                DepartmentCode: dept?.DepartmentCode,
                DepartmentName: dept?.Name,
                Department: departmentLabel,
                RoomId: resolvedRoom?.Id,
                RoomCode: resolvedRoom?.RoomCode,
                RoomName: roomName,
                RoomLocation: roomLocation,
                ServicePointId: resolvedServicePoint?.Id,
                ServicePointName: resolvedServicePoint?.Name,
                WaitingCount: workload.WaitingCount,
                ActiveVisitCount: workload.ActiveVisitCount,
                CurrentTicket: workload.CurrentTicket,
                EstimatedWaitMinutes: workload.EstimatedWaitMinutes,
                EstimatedWait: workload.EstimatedWaitLabel,
                WorkloadLevel: workload.WorkloadLevel,
                IsActive: isDoctorActive && isDeptActive,
                IsServiceAuthorized: isServiceAuthorized,
                IsCompatible: isCompatible,
                IsAvailable: isAvailable,
                IsCurrentDoctor: visit.AssignedDoctorId.HasValue && visit.AssignedDoctorId.Value == doctor.Id,
                UnavailableReason: unavailableReason));
        }

        var sortedItems = items
            .OrderByDescending(i => i.IsAvailable)
            .ThenByDescending(i => i.IsCompatible)
            .ThenBy(i => i.WaitingCount)
            .ThenBy(i => i.DoctorCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(i => i.DoctorId)
            .ToList();

        var visitCode = !string.IsNullOrWhiteSpace(visit.VisitCode)
            ? visit.VisitCode
            : $"VIS-{visit.Id:D4}";

        return new AssignableDoctorListResponseDto(
            VisitId: visit.Id,
            VisitCode: visitCode,
            VisitStatus: visit.Status ?? "CHECKED_IN",
            HospitalId: effectiveHospitalId,
            AssignedDoctorId: currentDoctor?.Id ?? visit.AssignedDoctorId,
            AssignedDoctorCode: currentDoctor?.DoctorCode,
            AssignedDoctorName: currentDoctor is not null ? FormatDoctorDisplayName(currentDoctor) : null,
            CurrentExaminationRoomId: currentRoom?.Id ?? visit.ExaminationRoomId,
            CurrentExaminationRoomCode: currentRoom?.RoomCode,
            CurrentExaminationRoomName: currentRoom?.Name,
            RequiredServiceId: requiredService?.Id,
            RequiredServiceCode: requiredService?.ServiceCode,
            RequiredServiceName: requiredService?.Name,
            RequiredDepartmentId: requiredDepartmentId,
            RequiredDepartmentName: requiredDepartmentName,
            Patient: patientSummary,
            Doctors: sortedItems);
    }

    public async Task<AssignDoctorToVisitResponseDto> AssignDoctorAsync(
        long actorUserId,
        string visitIdOrCode,
        AssignDoctorToVisitRequest request,
        string? ipAddress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var parsedVisit = ParseVisitIdentifier(visitIdOrCode);

        var targetDoctorId = request.DoctorId;
        var rawDoctorCode = request.DoctorCode;

        if (targetDoctorId.HasValue && targetDoctorId.Value <= 0)
        {
            throw new ValidationException("ID bác sĩ phải lớn hơn 0.");
        }

        if (rawDoctorCode is not null && string.IsNullOrWhiteSpace(rawDoctorCode) && !targetDoctorId.HasValue)
        {
            throw new ValidationException("Mã bác sĩ không được để trống.");
        }

        if (!targetDoctorId.HasValue && string.IsNullOrWhiteSpace(rawDoctorCode))
        {
            throw new ValidationException("Vui lòng chọn bác sĩ phụ trách cần gán (DoctorId hoặc DoctorCode).");
        }

        string? targetDoctorCode = null;
        if (!string.IsNullOrWhiteSpace(rawDoctorCode))
        {
            targetDoctorCode = NormalizeCodeOrThrow(rawDoctorCode, "Mã bác sĩ không hợp lệ.");
        }

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

        string? targetRoomCode = null;
        if (!string.IsNullOrWhiteSpace(rawRoomCode))
        {
            targetRoomCode = NormalizeCodeOrThrow(rawRoomCode, "Mã buồng khám không hợp lệ.");
        }

        if (request.ServiceId.HasValue && request.ServiceId.Value <= 0)
        {
            throw new ValidationException("ID dịch vụ khám phải lớn hơn 0.");
        }

        string? targetServiceCode = null;
        if (request.ServiceCode is not null)
        {
            if (string.IsNullOrWhiteSpace(request.ServiceCode))
            {
                throw new ValidationException("Mã dịch vụ khám không được để trống.");
            }

            targetServiceCode = NormalizeCodeOrThrow(request.ServiceCode, "Mã dịch vụ khám không hợp lệ.");
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
            throw new ValidationException("Lý do / ghi chú phân công bác sĩ không được vượt quá 500 ký tự.");
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var nowLocal = AppointmentBookingRules.GetVietnamNow(_timeProvider);
        var today = DateOnly.FromDateTime(nowLocal);

        var actor = await EnsureRoutingActorAccessAsync(actorUserId, nowUtc, ct);

        var visit = await LoadVisitAsync(parsedVisit, asNoTracking: false, ct)
            ?? throw new NotFoundException("Visit", visitIdOrCode.Trim());

        var appointment = await LoadAppointmentWithSlotAsync(visit.AppointmentId, ct);
        var existingQueueEntry = await FindExistingExaminationQueueEntryAsync(visit, asNoTracking: false, ct);

        var normalizedVisitStatus = AppointmentBookingRules.NormalizeStatusKey(visit.Status);
        EnsureVisitCanBeAssignedDoctor(visit, normalizedVisitStatus, existingQueueEntry);

        Doctor? previousDoctor = null;
        if (visit.AssignedDoctorId.HasValue)
        {
            previousDoctor = await _db.Doctors
                .AsNoTracking()
                .Include(d => d.Staff)
                    .ThenInclude(s => s!.Department)
                .Include(d => d.Specialty)
                .FirstOrDefaultAsync(d => d.Id == visit.AssignedDoctorId.Value, ct);
        }

        HospitalRoom? currentRoom = null;
        if (visit.ExaminationRoomId.HasValue)
        {
            currentRoom = await _db.HospitalRooms
                .AsNoTracking()
                .Include(r => r.Department)
                .FirstOrDefaultAsync(r => r.Id == visit.ExaminationRoomId.Value, ct);
        }

        var visitHospitalId = await ResolveVisitHospitalIdAsync(
            visit,
            appointment,
            previousDoctor,
            currentRoom,
            ct);

        if (!actor.IsSystemAdmin &&
            visitHospitalId.HasValue &&
            visitHospitalId.Value != actor.ActorHospitalId!.Value)
        {
            throw new ForbiddenException("Bạn không có quyền thao tác trên lượt khám thuộc cơ sở y tế khác.");
        }

        var effectiveHospitalId = visitHospitalId ?? actor.ActorHospitalId ?? 1L;

        var targetDoctor = await ResolveTargetDoctorAsync(
            targetDoctorId,
            targetDoctorCode,
            effectiveHospitalId,
            ct)
            ?? throw new NotFoundException(
                "Doctor",
                targetDoctorId?.ToString(CultureInfo.InvariantCulture) ?? targetDoctorCode!);

        if (targetDoctor.Staff is not null &&
            targetDoctor.Staff.Department is null &&
            targetDoctor.Staff.DepartmentId.HasValue)
        {
            targetDoctor.Staff.Department = await _db.Departments
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == targetDoctor.Staff.DepartmentId.Value, ct);
        }

        var doctorHospitalId = targetDoctor.HospitalId ?? targetDoctor.Staff?.HospitalId;
        if (!actor.IsSystemAdmin &&
            doctorHospitalId.HasValue &&
            doctorHospitalId.Value != actor.ActorHospitalId!.Value)
        {
            throw new ForbiddenException("Bạn không có quyền phân công bác sĩ thuộc cơ sở y tế khác.");
        }

        if (visitHospitalId.HasValue &&
            doctorHospitalId.HasValue &&
            doctorHospitalId.Value != visitHospitalId.Value)
        {
            throw new ValidationException("Bác sĩ được chọn không thuộc cùng cơ sở y tế với lượt khám.");
        }

        if (!targetDoctor.IsActive ||
            (targetDoctor.Staff is not null &&
             !string.Equals(targetDoctor.Staff.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ConflictException($"Bác sĩ '{targetDoctor.DoctorCode}' hiện đang tạm ngưng hoạt động.");
        }

        if (targetDoctor.Staff?.Department is not null &&
            targetDoctor.Staff.Department.IsActive == false)
        {
            throw new ConflictException(
                $"Khoa phòng '{targetDoctor.Staff.Department.Name}' của bác sĩ '{targetDoctor.DoctorCode}' đang tạm ngưng hoạt động.");
        }

        var requiredService = await ResolveRequiredServiceAsync(
            request.ServiceId,
            targetServiceCode,
            appointment,
            existingQueueEntry,
            effectiveHospitalId,
            ct);

        if (requiredService is not null)
        {
            var isAuthorized = await IsDoctorAuthorizedForServiceAsync(
                targetDoctor.Id,
                requiredService.Id,
                ct);

            if (!isAuthorized)
            {
                throw new ValidationException(
                    $"Bác sĩ '{targetDoctor.DoctorCode}' chưa được phân quyền thực hiện dịch vụ '{requiredService.Name}'.");
            }

            if (requiredService.DepartmentId.HasValue &&
                targetDoctor.Staff?.DepartmentId.HasValue == true &&
                targetDoctor.Staff.DepartmentId.Value != requiredService.DepartmentId.Value)
            {
                throw new ValidationException(
                    $"Bác sĩ '{targetDoctor.DoctorCode}' không thuộc cùng khoa phòng với dịch vụ khám '{requiredService.Name}'.");
            }

            if (requiredService.SpecialtyId.HasValue &&
                targetDoctor.SpecialtyId.HasValue &&
                targetDoctor.SpecialtyId.Value != requiredService.SpecialtyId.Value)
            {
                throw new ValidationException(
                    $"Bác sĩ '{targetDoctor.DoctorCode}' không thuộc chuyên khoa yêu cầu của dịch vụ '{requiredService.Name}'.");
            }
        }

        HospitalRoom? effectiveRoom = currentRoom;
        if (targetRoomId.HasValue || !string.IsNullOrWhiteSpace(targetRoomCode))
        {
            var requestedRoom = await ResolveTargetRoomAsync(
                targetRoomId,
                targetRoomCode,
                effectiveHospitalId,
                ct)
                ?? throw new NotFoundException(
                    "HospitalRoom",
                    targetRoomId?.ToString(CultureInfo.InvariantCulture) ?? targetRoomCode!);

            if (requestedRoom.Department is null && requestedRoom.DepartmentId.HasValue)
            {
                requestedRoom.Department = await _db.Departments
                    .AsNoTracking()
                    .FirstOrDefaultAsync(d => d.Id == requestedRoom.DepartmentId.Value, ct);
            }

            if (!actor.IsSystemAdmin &&
                requestedRoom.HospitalId.HasValue &&
                requestedRoom.HospitalId.Value != actor.ActorHospitalId!.Value)
            {
                throw new ForbiddenException("Bạn không có quyền gán buồng khám thuộc cơ sở y tế khác.");
            }

            if (visitHospitalId.HasValue &&
                requestedRoom.HospitalId.HasValue &&
                requestedRoom.HospitalId.Value != visitHospitalId.Value)
            {
                throw new ValidationException("Buồng khám không thuộc cùng cơ sở y tế với lượt khám.");
            }

            if (!string.Equals(requestedRoom.RoomType, "EXAMINATION", StringComparison.OrdinalIgnoreCase))
            {
                throw new ValidationException(
                    $"Phòng '{requestedRoom.RoomCode}' (loại '{requestedRoom.RoomType}') không phải là buồng khám lâm sàng (EXAMINATION).");
            }

            if (!string.Equals(requestedRoom.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
            {
                throw new ConflictException(
                    $"Buồng khám '{requestedRoom.RoomCode}' hiện không khả dụng (trạng thái: {requestedRoom.Status}).");
            }

            if (requestedRoom.Department is not null && requestedRoom.Department.IsActive == false)
            {
                throw new ConflictException(
                    $"Khoa phòng '{requestedRoom.Department.Name}' của buồng khám '{requestedRoom.RoomCode}' đang tạm ngưng hoạt động.");
            }

            if (requiredService?.DepartmentId.HasValue == true &&
                requestedRoom.DepartmentId.HasValue &&
                requestedRoom.DepartmentId.Value != requiredService.DepartmentId.Value)
            {
                throw new ValidationException(
                    $"Buồng khám '{requestedRoom.RoomCode}' không thuộc cùng khoa phòng với dịch vụ khám của lượt khám.");
            }

            if (targetDoctor.Staff?.DepartmentId.HasValue == true &&
                requestedRoom.DepartmentId.HasValue &&
                requestedRoom.DepartmentId.Value != targetDoctor.Staff.DepartmentId.Value)
            {
                throw new ValidationException(
                    $"Buồng khám '{requestedRoom.RoomCode}' không tương thích với khoa phòng của bác sĩ '{targetDoctor.DoctorCode}'.");
            }

            var activeOccupancyExcludingCurrent = await CountActiveRoomOccupancyAsync(requestedRoom.Id, visit.Id, ct);
            if (requestedRoom.Capacity.HasValue &&
                requestedRoom.Capacity.Value > 0 &&
                activeOccupancyExcludingCurrent >= requestedRoom.Capacity.Value)
            {
                throw new ConflictException(
                    $"Buồng khám '{requestedRoom.RoomCode}' đã đầy ({activeOccupancyExcludingCurrent}/{requestedRoom.Capacity.Value} ca đang hoạt động). Vui lòng chọn buồng khám khác.");
            }

            effectiveRoom = requestedRoom;
        }
        else if (effectiveRoom is not null)
        {
            if (effectiveRoom.DepartmentId.HasValue &&
                targetDoctor.Staff?.DepartmentId.HasValue == true &&
                effectiveRoom.DepartmentId.Value != targetDoctor.Staff.DepartmentId.Value)
            {
                throw new ValidationException(
                    $"Bác sĩ '{targetDoctor.DoctorCode}' không tương thích với khoa phòng của buồng khám hiện tại '{effectiveRoom.RoomCode}'.");
            }
        }

        var patient = appointment?.Patient;
        if (patient is null && visit.PatientId.HasValue)
        {
            patient = await _db.Patients
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == visit.PatientId.Value, ct);
        }

        var servicePoint = await ResolveQueueServicePointAsync(
            effectiveRoom?.Id,
            targetDoctor.Id,
            effectiveHospitalId,
            ct);

        if (normalizedPriority is not null &&
            ClinicalQueueRules.IsElevated(normalizedPriority) &&
            normalizedPriority != ClinicalQueueRules.NormalizeStoredPriority(existingQueueEntry?.ManualPriority))
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
                throw new ValidationException(
                    "Chính sách bệnh viện yêu cầu xác minh giấy tờ trước khi áp dụng ưu tiên ELDERLY/PREGNANT (priorityVerificationConfirmed = true).");
            }
        }

        var previousDoctorId = visit.AssignedDoctorId;
        var previousRoomId = visit.ExaminationRoomId;
        var previousVisitStatus = visit.Status;
        var doctorChanged = previousDoctorId.HasValue && previousDoctorId.Value != targetDoctor.Id;

        visit.AssignedDoctorId = targetDoctor.Id;
        if (effectiveRoom is not null)
        {
            visit.ExaminationRoomId = effectiveRoom.Id;
        }

        visit.CheckedInAt ??= nowLocal;
        if (!visit.CheckedInByStaffId.HasValue && actor.ActorStaffId.HasValue)
        {
            visit.CheckedInByStaffId = actor.ActorStaffId.Value;
        }

        QueueEntry? queueEntry = existingQueueEntry;
        if (visit.ExaminationRoomId.HasValue)
        {
            if (string.Equals(normalizedVisitStatus, "CALLED", StringComparison.OrdinalIgnoreCase) && !doctorChanged)
            {
                visit.Status = "CALLED";
            }
            else
            {
                visit.Status = "WAITING";
            }

            var graceMinutes = await ResolveGraceMinutesAsync(effectiveHospitalId, servicePoint?.Id, nowUtc, ct);
            queueEntry = await UpsertExaminationQueueEntryAsync(
                visit,
                existingQueueEntry,
                appointment,
                servicePoint,
                normalizedPriority,
                doctorChanged,
                today,
                nowLocal,
                graceMinutes,
                ct);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(visit.Status))
            {
                visit.Status = "CHECKED_IN";
            }
            else if (string.Equals(normalizedVisitStatus, "CALLED", StringComparison.OrdinalIgnoreCase) && doctorChanged)
            {
                visit.Status = "WAITING";
            }

            if (queueEntry is not null)
            {
                queueEntry.VisitId = visit.Id;
                if (servicePoint is not null)
                {
                    queueEntry.ServicePointId ??= servicePoint.Id;
                    queueEntry.CurrentServicePointId = servicePoint.Id;
                }

                if (doctorChanged &&
                    string.Equals(queueEntry.Status, "CALLED", StringComparison.OrdinalIgnoreCase))
                {
                    queueEntry.Status = "WAITING";
                    queueEntry.CalledAt = null;
                }

                if (normalizedPriority is not null)
                {
                    if (normalizedPriority != ClinicalQueueRules.NormalizeStoredPriority(queueEntry.ManualPriority))
                    {
                        queueEntry.StateVersion = (queueEntry.StateVersion is > 0 ? queueEntry.StateVersion.Value : 1) + 1;
                    }

                    queueEntry.ManualPriority = normalizedPriority;
                }
            }
        }

        var expectedDurationMinutes = await ResolveExpectedDurationMinutesAsync(effectiveHospitalId, nowUtc, ct);
        var allServicePoints = await LoadHospitalServicePointsAsync(effectiveHospitalId, ct);
        var workloadByDoctor = await ComputeDoctorWorkloadsAsync(
            [targetDoctor],
            allServicePoints,
            visit.Id,
            today,
            expectedDurationMinutes,
            ct);
        var doctorWorkload = workloadByDoctor.GetValueOrDefault(
            targetDoctor.Id,
            new DoctorWorkloadMetrics(0, 0, "---", 0, "Khám ngay", WorkloadLow));

        var effectiveHospitalForAudit = visitHospitalId ?? doctorHospitalId ?? actor.ActorHospitalId ?? effectiveHospitalId;
        var changeData = JsonSerializer.Serialize(new
        {
            visitCode = visit.VisitCode,
            patientId = visit.PatientId,
            appointmentId = visit.AppointmentId,
            serviceId = requiredService?.Id,
            serviceCode = requiredService?.ServiceCode,
            previous = new
            {
                assignedDoctorId = previousDoctorId,
                assignedDoctorCode = previousDoctor?.DoctorCode,
                examinationRoomId = previousRoomId,
                status = previousVisitStatus,
            },
            updated = new
            {
                assignedDoctorId = targetDoctor.Id,
                assignedDoctorCode = targetDoctor.DoctorCode,
                assignedDoctorName = targetDoctor.Staff?.FullName,
                examinationRoomId = visit.ExaminationRoomId,
                roomCode = effectiveRoom?.RoomCode,
                status = visit.Status,
                servicePointId = queueEntry?.CurrentServicePointId ?? queueEntry?.ServicePointId ?? servicePoint?.Id,
                queueNumber = queueEntry?.QueueNumber,
                priority = queueEntry?.ManualPriority ?? normalizedPriority,
                waitingCount = doctorWorkload.WaitingCount,
                workloadLevel = doctorWorkload.WorkloadLevel,
            },
            reason = trimmedReason,
            priorityVerificationConfirmed = request.PriorityVerificationConfirmed == true,
        });

        _db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actorUserId,
            HospitalId = effectiveHospitalForAudit,
            Action = VisitDoctorAssignedAction,
            EntityType = "VISIT",
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

        var effectiveServicePointId = queueEntry?.CurrentServicePointId
            ?? queueEntry?.ServicePointId
            ?? servicePoint?.Id;
        var servicePointName = servicePoint?.Name;
        if (servicePointName is null && effectiveServicePointId.HasValue)
        {
            servicePointName = allServicePoints
                .FirstOrDefault(sp => sp.Id == effectiveServicePointId.Value)
                ?.Name;
        }

        var department = targetDoctor.Staff?.Department ?? effectiveRoom?.Department ?? requiredService?.Department;

        return new AssignDoctorToVisitResponseDto(
            VisitId: visit.Id,
            VisitCode: visitCode,
            VisitStatus: visit.Status ?? "CHECKED_IN",
            HospitalId: effectiveHospitalForAudit,
            PatientId: visit.PatientId ?? patient?.Id,
            PatientCode: patient?.PatientCode,
            PatientName: patient?.FullName,
            AppointmentId: visit.AppointmentId,
            AppointmentCode: appointment?.AppointmentCode,
            PreviousDoctorId: previousDoctorId,
            PreviousDoctorCode: previousDoctor?.DoctorCode,
            PreviousDoctorName: previousDoctor?.Staff?.FullName,
            AssignedDoctorId: targetDoctor.Id,
            AssignedDoctorCode: targetDoctor.DoctorCode,
            AssignedDoctorName: FormatDoctorDisplayName(targetDoctor),
            ProfessionalTitle: targetDoctor.ProfessionalTitle,
            SpecialtyId: targetDoctor.SpecialtyId,
            SpecialtyCode: targetDoctor.Specialty?.SpecialtyCode,
            SpecialtyName: targetDoctor.Specialty?.Name,
            DepartmentId: department?.Id ?? targetDoctor.Staff?.DepartmentId,
            DepartmentCode: department?.DepartmentCode,
            DepartmentName: department?.Name,
            ExaminationRoomId: visit.ExaminationRoomId,
            RoomCode: effectiveRoom?.RoomCode,
            RoomName: effectiveRoom?.Name,
            RoomLocation: effectiveRoom?.Location,
            ServiceId: requiredService?.Id,
            ServiceCode: requiredService?.ServiceCode,
            ServiceName: requiredService?.Name,
            QueueEntryId: queueEntry?.Id,
            QueueNumber: queueEntry?.QueueNumber,
            QueueCode: queueEntry?.QueueNumber.HasValue == true
                ? $"B{queueEntry.QueueNumber.Value:D3}"
                : null,
            QueueStatus: queueEntry?.Status,
            Priority: queueEntry?.ManualPriority ?? normalizedPriority,
            ServicePointId: effectiveServicePointId,
            ServicePointName: servicePointName,
            WaitingCount: doctorWorkload.WaitingCount,
            WorkloadLevel: doctorWorkload.WorkloadLevel,
            CheckedInByStaffId: visit.CheckedInByStaffId,
            CheckedInAt: visit.CheckedInAt,
            AssignedAtUtc: nowUtc);
    }

    public static string ClassifyWorkloadLevel(int waitingCount)
    {
        if (waitingCount <= LowWorkloadMaxWaiting)
        {
            return WorkloadLow;
        }

        if (waitingCount <= MediumWorkloadMaxWaiting)
        {
            return WorkloadMedium;
        }

        return WorkloadHigh;
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

    private static string NormalizeCodeOrThrow(string rawCode, string errorMessage)
    {
        var normalized = rawCode.Trim().ToUpperInvariant();
        if (normalized.Length == 0 ||
            normalized.Length > 40 ||
            !normalized.All(ch => char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' || ch == '.'))
        {
            throw new ValidationException(errorMessage);
        }

        return normalized;
    }

    private static void EnsureVisitCanBeAssignedDoctor(
        Visit visit,
        string normalizedVisitStatus,
        QueueEntry? existingQueueEntry)
    {
        if (visit.CompletedAt.HasValue ||
            string.Equals(normalizedVisitStatus, "COMPLETED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedVisitStatus, "DONE", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException("Lượt khám đã hoàn tất, không thể phân công bác sĩ.");
        }

        if (string.Equals(normalizedVisitStatus, "CANCELLED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedVisitStatus, "NO_SHOW", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException("Lượt khám đã hủy hoặc vắng mặt, không thể phân công bác sĩ.");
        }

        if (ActiveConsultationStatuses.Contains(normalizedVisitStatus))
        {
            throw new ConflictException("Bệnh nhân đang trong quá trình khám lâm sàng, không thể thay đổi bác sĩ phụ trách.");
        }

        if (existingQueueEntry is not null)
        {
            var normalizedQueueStatus = AppointmentBookingRules.NormalizeStatusKey(existingQueueEntry.Status);
            if ((existingQueueEntry.StartedAt.HasValue && !existingQueueEntry.CompletedAt.HasValue) ||
                ActiveConsultationStatuses.Contains(normalizedQueueStatus))
            {
                throw new ConflictException("Bệnh nhân đã bắt đầu khám lâm sàng, không thể thay đổi bác sĩ phụ trách.");
            }
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
            throw new ForbiddenException("Người dùng không có quyền phân công bác sĩ cho lượt khám.");
        }

        if (!isSystemAdmin && actorRoleIds.Count > 0)
        {
            var rolePermissionIds = await _db.RolePermissions
                .AsNoTracking()
                .Where(rp => actorRoleIds.Contains(rp.RoleId))
                .Select(rp => rp.PermissionId)
                .Distinct()
                .ToListAsync(ct);

            if (rolePermissionIds.Count > 0)
            {
                var permissionCodes = await _db.Permissions
                    .AsNoTracking()
                    .Where(p => rolePermissionIds.Contains(p.Id))
                    .Select(p => p.Code)
                    .ToListAsync(ct);

                var hasPermission = isHospitalAdmin
                    ? permissionCodes.Any(c =>
                        HospitalAdminAllowedPermissions.Any(allowed =>
                            string.Equals(c, allowed, StringComparison.OrdinalIgnoreCase)))
                    : permissionCodes.Any(c =>
                        string.Equals(c, VisitAssignPermissionCode, StringComparison.OrdinalIgnoreCase));

                if (!hasPermission)
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
                    .ThenInclude(sv => sv!.Department)
            .Include(a => a.Slot)
                .ThenInclude(s => s!.Service)
                    .ThenInclude(sv => sv!.Specialty)
            .Include(a => a.Slot)
                .ThenInclude(s => s!.Room)
                    .ThenInclude(r => r!.Department)
            .Include(a => a.Slot)
                .ThenInclude(s => s!.Doctor)
                    .ThenInclude(d => d!.Staff)
                        .ThenInclude(st => st!.Department)
            .FirstOrDefaultAsync(a => a.Id == appointmentId.Value, ct);
    }

    private async Task<long?> ResolveVisitHospitalIdAsync(
        Visit visit,
        Appointment? appointment,
        Doctor? assignedDoctor,
        HospitalRoom? currentRoom,
        CancellationToken ct)
    {
        var hospitalId = currentRoom?.HospitalId
            ?? currentRoom?.Department?.HospitalId
            ?? assignedDoctor?.HospitalId
            ?? assignedDoctor?.Staff?.HospitalId
            ?? appointment?.Slot?.Service?.HospitalId
            ?? appointment?.Slot?.Room?.HospitalId
            ?? appointment?.Slot?.Doctor?.HospitalId
            ?? appointment?.Slot?.Doctor?.Staff?.HospitalId;

        if (hospitalId.HasValue)
        {
            return hospitalId.Value;
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

    private async Task<Service?> ResolveRequiredServiceAsync(
        long? explicitServiceId,
        string? explicitServiceCode,
        Appointment? appointment,
        QueueEntry? existingQueueEntry,
        long effectiveHospitalId,
        CancellationToken ct)
    {
        if (explicitServiceId.HasValue || !string.IsNullOrWhiteSpace(explicitServiceCode))
        {
            Service? explicitService = null;
            if (explicitServiceId.HasValue)
            {
                explicitService = await _db.Services
                    .AsNoTracking()
                    .Include(s => s.Department)
                    .Include(s => s.Specialty)
                    .FirstOrDefaultAsync(s => s.Id == explicitServiceId.Value, ct);

                if (explicitService is null)
                {
                    throw new NotFoundException("Service", explicitServiceId.Value);
                }

                if (!string.IsNullOrWhiteSpace(explicitServiceCode) &&
                    !string.Equals(explicitService.ServiceCode, explicitServiceCode, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ValidationException("Mã dịch vụ khám không khớp với ID dịch vụ đã chọn.");
                }
            }
            else if (!string.IsNullOrWhiteSpace(explicitServiceCode))
            {
                explicitService = await _db.Services
                    .AsNoTracking()
                    .Include(s => s.Department)
                    .Include(s => s.Specialty)
                    .Where(s => s.ServiceCode.ToUpper() == explicitServiceCode)
                    .OrderByDescending(s => s.HospitalId == effectiveHospitalId)
                    .FirstOrDefaultAsync(ct);

                if (explicitService is null)
                {
                    throw new NotFoundException("Service", explicitServiceCode);
                }
            }

            if (explicitService!.HospitalId.HasValue &&
                explicitService.HospitalId.Value != effectiveHospitalId)
            {
                throw new ValidationException("Dịch vụ khám không thuộc cùng cơ sở y tế với lượt khám.");
            }

            if (explicitService.IsActive == false)
            {
                throw new ConflictException($"Dịch vụ khám '{explicitService.Name}' hiện đang tạm ngưng hoạt động.");
            }

            if (!string.IsNullOrWhiteSpace(explicitService.ServiceType) &&
                !string.Equals(explicitService.ServiceType, "EXAMINATION", StringComparison.OrdinalIgnoreCase))
            {
                throw new ValidationException(
                    $"Dịch vụ '{explicitService.ServiceCode}' không phải là dịch vụ khám lâm sàng (EXAMINATION).");
            }

            return explicitService;
        }

        if (appointment?.Slot?.Service is not null)
        {
            return appointment.Slot.Service;
        }

        if (appointment?.Slot?.ServiceId is long slotServiceId)
        {
            return await _db.Services
                .AsNoTracking()
                .Include(s => s.Department)
                .Include(s => s.Specialty)
                .FirstOrDefaultAsync(s => s.Id == slotServiceId, ct);
        }

        var spId = existingQueueEntry?.CurrentServicePointId ?? existingQueueEntry?.ServicePointId;
        if (spId.HasValue)
        {
            try
            {
                var spServiceId = await _db.QueueServicePoints
                    .AsNoTracking()
                    .Where(sp => sp.Id == spId.Value)
                    .Select(sp => sp.ServiceId)
                    .FirstOrDefaultAsync(ct);

                if (spServiceId.HasValue)
                {
                    return await _db.Services
                        .AsNoTracking()
                        .Include(s => s.Department)
                        .Include(s => s.Specialty)
                        .FirstOrDefaultAsync(s => s.Id == spServiceId.Value, ct);
                }
            }
            catch (NotSupportedException)
            {
                // Ignore when QueueServicePoints DbSet is not configured in unit test double.
            }
        }

        return null;
    }

    private async Task<VisitPatientSummaryDto> BuildVisitPatientSummaryAsync(
        Visit visit,
        Appointment? appointment,
        Service? requiredService,
        DateOnly today,
        DateTime nowLocal,
        CancellationToken ct)
    {
        var patient = appointment?.Patient;
        var patientId = visit.PatientId ?? appointment?.PatientId;
        if (patient is null && patientId.HasValue)
        {
            patient = await _db.Patients
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == patientId.Value, ct);
        }

        var visitCode = !string.IsNullOrWhiteSpace(visit.VisitCode)
            ? visit.VisitCode
            : $"VIS-{visit.Id:D4}";

        var genderLabel = NormalizeGenderLabel(patient?.Gender);
        var age = AppointmentSlotService.CalculateAge(patient?.DateOfBirth, today);
        var cccdVerified = !string.IsNullOrWhiteSpace(patient?.IdentificationHash);

        var price = 0m;
        if (requiredService is not null)
        {
            price = await TryGetActiveServicePriceAsync(requiredService.Id, nowLocal, ct);
        }

        return new VisitPatientSummaryDto(
            VisitId: visitCode,
            VisitNumericId: visit.Id,
            VisitCode: visitCode,
            PatientId: patient?.Id ?? patientId,
            PatientCode: patient?.PatientCode ?? string.Empty,
            FullName: patient?.FullName ?? string.Empty,
            Gender: genderLabel,
            Age: age,
            CccdVerified: cccdVerified,
            ServiceId: requiredService?.Id,
            ServiceCode: requiredService?.ServiceCode ?? string.Empty,
            ServiceName: requiredService?.Name ?? "Khám lâm sàng ngoại trú",
            Price: price);
    }

    private static string NormalizeGenderLabel(string? gender)
    {
        if (string.IsNullOrWhiteSpace(gender))
        {
            return "Khác";
        }

        return gender.Trim().ToUpperInvariant() switch
        {
            "MALE" or "NAM" => "Nam",
            "FEMALE" or "NU" or "NỮ" => "Nữ",
            _ => "Khác",
        };
    }

    private async Task<decimal> TryGetActiveServicePriceAsync(long serviceId, DateTime nowLocal, CancellationToken ct)
    {
        try
        {
            var prices = await _db.ServicePrices
                .AsNoTracking()
                .Where(sp =>
                    sp.ServiceId == serviceId &&
                    (!sp.EffectiveFrom.HasValue || sp.EffectiveFrom.Value <= nowLocal) &&
                    (!sp.EffectiveTo.HasValue || sp.EffectiveTo.Value >= nowLocal))
                .OrderByDescending(sp => sp.EffectiveFrom)
                .ThenByDescending(sp => sp.Id)
                .ToListAsync(ct);

            return prices.FirstOrDefault()?.PriceAmount ?? 0m;
        }
        catch (NotSupportedException)
        {
            return 0m;
        }
    }

    private async Task<List<Doctor>> LoadHospitalDoctorsAsync(long hospitalId, CancellationToken ct)
    {
        var allDoctors = await _db.Doctors
            .AsNoTracking()
            .Include(d => d.Staff)
                .ThenInclude(s => s!.Department)
            .Include(d => d.Specialty)
            .OrderBy(d => d.DoctorCode)
            .ThenBy(d => d.Id)
            .ToListAsync(ct);

        return allDoctors
            .Where(d =>
                (d.HospitalId ?? d.Staff?.HospitalId ?? 1L) == hospitalId)
            .ToList();
    }

    private async Task<HashSet<long>?> ResolveAuthorizedDoctorIdsAsync(
        long? requiredServiceId,
        IReadOnlyCollection<Doctor> candidateDoctors,
        CancellationToken ct)
    {
        if (!requiredServiceId.HasValue || candidateDoctors.Count == 0)
        {
            return null;
        }

        try
        {
            var doctorIds = candidateDoctors.Select(d => d.Id).ToList();
            var authorizations = await _db.DoctorServiceAuthorizations
                .AsNoTracking()
                .Where(a => doctorIds.Contains(a.DoctorId) || a.ServiceId == requiredServiceId.Value)
                .ToListAsync(ct);

            if (authorizations.Count == 0)
            {
                return null;
            }

            var result = new HashSet<long>();
            foreach (var doctor in candidateDoctors)
            {
                var doctorAuths = authorizations.Where(a => a.DoctorId == doctor.Id).ToList();
                if (doctorAuths.Count == 0)
                {
                    // If other doctors have explicit service authorizations configured in the catalog,
                    // require an active authorization row for this required service.
                    continue;
                }

                if (doctorAuths.Any(a => a.ServiceId == requiredServiceId.Value && a.IsActive == true))
                {
                    result.Add(doctor.Id);
                }
            }

            return result;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private async Task<bool> IsDoctorAuthorizedForServiceAsync(
        long doctorId,
        long serviceId,
        CancellationToken ct)
    {
        try
        {
            var anyConfigured = await _db.DoctorServiceAuthorizations
                .AsNoTracking()
                .AnyAsync(a => a.DoctorId == doctorId || a.ServiceId == serviceId, ct);

            if (!anyConfigured)
            {
                return true;
            }

            return await _db.DoctorServiceAuthorizations
                .AsNoTracking()
                .AnyAsync(
                    a => a.DoctorId == doctorId &&
                         a.ServiceId == serviceId &&
                         a.IsActive == true,
                    ct);
        }
        catch (NotSupportedException)
        {
            return true;
        }
    }

    private async Task<List<QueueServicePoint>> LoadHospitalServicePointsAsync(
        long hospitalId,
        CancellationToken ct)
    {
        try
        {
            return await _db.QueueServicePoints
                .AsNoTracking()
                .Where(sp =>
                    sp.IsActive != false &&
                    (!sp.HospitalId.HasValue || sp.HospitalId.Value == hospitalId))
                .OrderBy(sp => sp.Id)
                .ToListAsync(ct);
        }
        catch (NotSupportedException)
        {
            return [];
        }
    }

    private async Task<List<ExaminationSlot>> LoadTodayDoctorSlotsAsync(
        IReadOnlyCollection<long> doctorIds,
        DateOnly today,
        CancellationToken ct)
    {
        if (doctorIds.Count == 0)
        {
            return [];
        }

        var dayStart = today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var dayEnd = dayStart.AddDays(1);

        return await _db.ExaminationSlots
            .AsNoTracking()
            .Include(s => s.Room)
                .ThenInclude(r => r!.Department)
            .Where(s =>
                s.DoctorId.HasValue &&
                doctorIds.Contains(s.DoctorId.Value) &&
                s.StartsAt.HasValue &&
                s.StartsAt.Value >= dayStart &&
                s.StartsAt.Value < dayEnd)
            .OrderBy(s => s.StartsAt)
            .ToListAsync(ct);
    }

    private static (HospitalRoom? Room, QueueServicePoint? ServicePoint) ResolveDoctorRoomAndServicePoint(
        Doctor doctor,
        HospitalRoom? currentVisitRoom,
        IReadOnlyList<HospitalRoom> hospitalRooms,
        IReadOnlyList<QueueServicePoint> servicePoints,
        IReadOnlyList<ExaminationSlot> todaySlots)
    {
        var doctorSp = servicePoints.FirstOrDefault(sp =>
                sp.DoctorId == doctor.Id &&
                currentVisitRoom is not null &&
                sp.RoomId == currentVisitRoom.Id)
            ?? servicePoints.FirstOrDefault(sp => sp.DoctorId == doctor.Id);

        HospitalRoom? resolvedRoom = null;
        if (currentVisitRoom is not null &&
            (!currentVisitRoom.DepartmentId.HasValue ||
             doctor.Staff?.DepartmentId is null ||
             currentVisitRoom.DepartmentId.Value == doctor.Staff.DepartmentId.Value) &&
            doctorSp?.RoomId == currentVisitRoom.Id)
        {
            resolvedRoom = currentVisitRoom;
        }

        if (resolvedRoom is null && doctorSp?.RoomId is long spRoomId)
        {
            resolvedRoom = hospitalRooms.FirstOrDefault(r => r.Id == spRoomId);
        }

        if (resolvedRoom is null)
        {
            var slotRoomId = todaySlots
                .FirstOrDefault(s => s.DoctorId == doctor.Id && s.RoomId.HasValue)
                ?.RoomId;
            if (slotRoomId.HasValue)
            {
                resolvedRoom = hospitalRooms.FirstOrDefault(r => r.Id == slotRoomId.Value);
            }
        }

        if (resolvedRoom is null && doctor.Staff?.DepartmentId is long docDeptId)
        {
            resolvedRoom = hospitalRooms.FirstOrDefault(r =>
                r.DepartmentId == docDeptId &&
                string.Equals(r.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
                ?? hospitalRooms.FirstOrDefault(r => r.DepartmentId == docDeptId);
        }

        if (doctorSp is null && resolvedRoom is not null)
        {
            doctorSp = servicePoints.FirstOrDefault(sp => sp.RoomId == resolvedRoom.Id);
        }

        return (resolvedRoom, doctorSp);
    }

    private sealed record DoctorWorkloadMetrics(
        int WaitingCount,
        int ActiveVisitCount,
        string CurrentTicket,
        int? EstimatedWaitMinutes,
        string? EstimatedWaitLabel,
        string WorkloadLevel);

    private async Task<Dictionary<long, DoctorWorkloadMetrics>> ComputeDoctorWorkloadsAsync(
        IReadOnlyList<Doctor> doctors,
        IReadOnlyList<QueueServicePoint> servicePoints,
        long currentVisitId,
        DateOnly today,
        int expectedDurationMinutes,
        CancellationToken ct)
    {
        var result = new Dictionary<long, DoctorWorkloadMetrics>(doctors.Count);
        if (doctors.Count == 0)
        {
            return result;
        }

        var doctorIds = doctors.Select(d => d.Id).ToHashSet();
        var spIdsByDoctorId = doctors.ToDictionary(
            d => d.Id,
            d => servicePoints
                .Where(sp => sp.DoctorId == d.Id)
                .Select(sp => sp.Id)
                .ToHashSet());
        var allDoctorSpIds = spIdsByDoctorId.Values.SelectMany(x => x).ToHashSet();

        var openVisits = await _db.Visits
            .AsNoTracking()
            .Where(v =>
                v.Id != currentVisitId &&
                v.CompletedAt == null &&
                v.AssignedDoctorId.HasValue &&
                doctorIds.Contains(v.AssignedDoctorId.Value))
            .ToListAsync(ct);

        var openVisitIds = openVisits.Select(v => v.Id).ToHashSet();

        var todayQueueEntries = await _db.QueueEntries
            .AsNoTracking()
            .Where(q =>
                (q.QueueDate == null || q.QueueDate == today) &&
                (q.QueueType == null ||
                 q.QueueType.ToUpper() == "EXAMINATION" ||
                 q.QueueType.ToUpper() == "RETURN_CONSULTATION") &&
                (q.Status == null || q.Status.ToUpper() != "CANCELLED") &&
                q.VisitId != currentVisitId &&
                ((q.VisitId.HasValue && openVisitIds.Contains(q.VisitId.Value)) ||
                 (q.ServicePointId.HasValue && allDoctorSpIds.Contains(q.ServicePointId.Value)) ||
                 (q.CurrentServicePointId.HasValue && allDoctorSpIds.Contains(q.CurrentServicePointId.Value))))
            .OrderBy(q => q.QueueNumber)
            .ThenBy(q => q.Id)
            .ToListAsync(ct);

        var visitById = openVisits.ToDictionary(v => v.Id);

        foreach (var doctor in doctors)
        {
            var doctorSpSet = spIdsByDoctorId[doctor.Id];
            var doctorVisits = openVisits
                .Where(v => v.AssignedDoctorId == doctor.Id && IsActiveNonTerminalVisitStatus(v.Status))
                .ToList();
            var doctorVisitIds = doctorVisits.Select(v => v.Id).ToHashSet();

            var doctorQueueEntries = todayQueueEntries
                .Where(q =>
                    (q.VisitId.HasValue && doctorVisitIds.Contains(q.VisitId.Value)) ||
                    (!q.VisitId.HasValue &&
                     ((q.CurrentServicePointId.HasValue && doctorSpSet.Contains(q.CurrentServicePointId.Value)) ||
                      (q.ServicePointId.HasValue && doctorSpSet.Contains(q.ServicePointId.Value)))) ||
                    (q.VisitId.HasValue &&
                     !visitById.ContainsKey(q.VisitId.Value) &&
                     ((q.CurrentServicePointId.HasValue && doctorSpSet.Contains(q.CurrentServicePointId.Value)) ||
                      (q.ServicePointId.HasValue && doctorSpSet.Contains(q.ServicePointId.Value)))))
                .ToList();

            var waitingVisitIds = new HashSet<long>();
            var activeVisitIds = new HashSet<long>();
            var extraWaitingEntries = 0;
            var extraActiveEntries = 0;

            foreach (var v in doctorVisits)
            {
                var status = AppointmentBookingRules.NormalizeStatusKey(v.Status);
                if (IsWaitingVisitStatus(status))
                {
                    waitingVisitIds.Add(v.Id);
                }

                activeVisitIds.Add(v.Id);
            }

            foreach (var q in doctorQueueEntries)
            {
                var qStatus = AppointmentBookingRules.NormalizeStatusKey(q.Status);
                var isWaitingQueue = IsWaitingQueueEntryStatus(qStatus, q.PresenceStatus);
                var isActiveQueue = isWaitingQueue ||
                                    qStatus is "IN_PROGRESS" or "IN_CONSULTATION";

                if (q.VisitId.HasValue)
                {
                    if (isWaitingQueue)
                    {
                        waitingVisitIds.Add(q.VisitId.Value);
                    }

                    if (isActiveQueue)
                    {
                        activeVisitIds.Add(q.VisitId.Value);
                    }
                }
                else
                {
                    if (isWaitingQueue)
                    {
                        extraWaitingEntries++;
                    }

                    if (isActiveQueue)
                    {
                        extraActiveEntries++;
                    }
                }
            }

            var waitingCount = waitingVisitIds.Count + extraWaitingEntries;
            var activeVisitCount = Math.Max(waitingCount, activeVisitIds.Count + extraActiveEntries);

            var currentEntry = doctorQueueEntries
                .FirstOrDefault(q =>
                    string.Equals(q.Status, "IN_PROGRESS", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(q.Status, "IN_CONSULTATION", StringComparison.OrdinalIgnoreCase))
                ?? doctorQueueEntries.FirstOrDefault(q =>
                    string.Equals(q.Status, "CALLED", StringComparison.OrdinalIgnoreCase))
                ?? doctorQueueEntries.FirstOrDefault(q =>
                    q.QueueNumber.HasValue &&
                    IsWaitingQueueEntryStatus(
                        AppointmentBookingRules.NormalizeStatusKey(q.Status),
                        q.PresenceStatus));

            var currentTicket = currentEntry?.QueueNumber is > 0
                ? $"{currentEntry.QueueNumber.Value:D3}"
                : "---";

            var workloadLevel = ClassifyWorkloadLevel(waitingCount);
            var duration = expectedDurationMinutes > 0 ? expectedDurationMinutes : DefaultExpectedDurationMinutes;
            var estimatedWaitMinutes = waitingCount * duration;
            var estimatedWaitLabel = waitingCount == 0
                ? "Khám ngay"
                : $"~{Math.Max(5, estimatedWaitMinutes - 5)}-{estimatedWaitMinutes} phút";

            result[doctor.Id] = new DoctorWorkloadMetrics(
                WaitingCount: waitingCount,
                ActiveVisitCount: activeVisitCount,
                CurrentTicket: currentTicket,
                EstimatedWaitMinutes: estimatedWaitMinutes,
                EstimatedWaitLabel: estimatedWaitLabel,
                WorkloadLevel: workloadLevel);
        }

        return result;
    }

    private static bool IsActiveNonTerminalVisitStatus(string? status)
    {
        var normalized = AppointmentBookingRules.NormalizeStatusKey(status);
        return normalized is not ("COMPLETED" or "CANCELLED" or "NO_SHOW" or "DONE");
    }

    private static bool IsWaitingVisitStatus(string normalizedStatus)
        => string.IsNullOrEmpty(normalizedStatus) ||
           normalizedStatus is "CHECKED_IN" or "CONFIRMED" or "ROUTED" or "WAITING" or "READY" or "CALLED";

    private static bool IsWaitingQueueEntryStatus(string normalizedQueueStatus, string? presenceStatus)
    {
        if (normalizedQueueStatus is "WAITING" or "READY" or "CALLED")
        {
            return true;
        }

        if (normalizedQueueStatus == "RESERVED" &&
            string.Equals(presenceStatus, "PRESENT", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static string FormatDoctorDisplayName(Doctor doctor)
    {
        var rawName = doctor.Staff?.FullName?.Trim();
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return doctor.DoctorCode;
        }

        var title = doctor.ProfessionalTitle?.Trim();
        if (!string.IsNullOrWhiteSpace(title) &&
            !rawName.StartsWith("BS", StringComparison.OrdinalIgnoreCase) &&
            !rawName.StartsWith(title, StringComparison.OrdinalIgnoreCase))
        {
            return $"{title} {rawName}";
        }

        return rawName;
    }

    private async Task<Doctor?> ResolveTargetDoctorAsync(
        long? targetDoctorId,
        string? targetDoctorCode,
        long effectiveHospitalId,
        CancellationToken ct)
    {
        if (targetDoctorId.HasValue)
        {
            var doctor = await _db.Doctors
                .AsNoTracking()
                .Include(d => d.Staff)
                    .ThenInclude(s => s!.Department)
                .Include(d => d.Specialty)
                .FirstOrDefaultAsync(d => d.Id == targetDoctorId.Value, ct);

            if (doctor is not null &&
                !string.IsNullOrWhiteSpace(targetDoctorCode) &&
                !string.Equals(doctor.DoctorCode, targetDoctorCode, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(doctor.Id.ToString(CultureInfo.InvariantCulture), targetDoctorCode, StringComparison.OrdinalIgnoreCase))
            {
                throw new ValidationException("Mã bác sĩ không khớp với ID bác sĩ đã chọn.");
            }

            return doctor;
        }

        if (!string.IsNullOrWhiteSpace(targetDoctorCode))
        {
            var scopedDoctor = await _db.Doctors
                .AsNoTracking()
                .Include(d => d.Staff)
                    .ThenInclude(s => s!.Department)
                .Include(d => d.Specialty)
                .Where(d => d.DoctorCode.ToUpper() == targetDoctorCode)
                .OrderByDescending(d => (d.HospitalId ?? d.Staff!.HospitalId ?? 1L) == effectiveHospitalId)
                .ThenBy(d => d.Id)
                .FirstOrDefaultAsync(ct);

            if (scopedDoctor is not null)
            {
                return scopedDoctor;
            }

            if (long.TryParse(targetDoctorCode, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numericDoctorId) &&
                numericDoctorId > 0)
            {
                return await _db.Doctors
                    .AsNoTracking()
                    .Include(d => d.Staff)
                        .ThenInclude(s => s!.Department)
                    .Include(d => d.Specialty)
                    .FirstOrDefaultAsync(d => d.Id == numericDoctorId, ct);
            }
        }

        return null;
    }

    private async Task<HospitalRoom?> ResolveTargetRoomAsync(
        long? targetRoomId,
        string? targetRoomCode,
        long effectiveHospitalId,
        CancellationToken ct)
    {
        if (targetRoomId.HasValue)
        {
            var room = await _db.HospitalRooms
                .AsNoTracking()
                .Include(r => r.Department)
                .FirstOrDefaultAsync(r => r.Id == targetRoomId.Value, ct);

            if (room is not null &&
                !string.IsNullOrWhiteSpace(targetRoomCode) &&
                !string.Equals(room.RoomCode, targetRoomCode, StringComparison.OrdinalIgnoreCase))
            {
                throw new ValidationException("Mã buồng khám không khớp với ID buồng khám đã chọn.");
            }

            return room;
        }

        if (!string.IsNullOrWhiteSpace(targetRoomCode))
        {
            var scopedRoom = await _db.HospitalRooms
                .AsNoTracking()
                .Include(r => r.Department)
                .Where(r => r.RoomCode.ToUpper() == targetRoomCode)
                .OrderByDescending(r => (r.HospitalId ?? 1L) == effectiveHospitalId)
                .ThenBy(r => r.Id)
                .FirstOrDefaultAsync(ct);

            return scopedRoom;
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
        long? roomId,
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
                    ((roomId.HasValue && sp.RoomId == roomId.Value) || sp.DoctorId == doctorId))
                .ToListAsync(ct);

            if (roomId.HasValue)
            {
                return candidates.FirstOrDefault(sp => sp.RoomId == roomId.Value && sp.DoctorId == doctorId)
                    ?? candidates.FirstOrDefault(sp => sp.DoctorId == doctorId)
                    ?? candidates.FirstOrDefault(sp => sp.RoomId == roomId.Value && sp.DoctorId == null)
                    ?? candidates.FirstOrDefault(sp => sp.RoomId == roomId.Value);
            }

            return candidates.FirstOrDefault(sp => sp.DoctorId == doctorId);
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private async Task<QueueEntry?> FindExistingExaminationQueueEntryAsync(
        Visit visit,
        bool asNoTracking,
        CancellationToken ct)
    {
        var query = asNoTracking ? _db.QueueEntries.AsNoTracking() : _db.QueueEntries.AsQueryable();

        var existingEntries = await query
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

    private async Task<int> ResolveExpectedDurationMinutesAsync(
        long hospitalId,
        DateTime nowUtc,
        CancellationToken ct)
    {
        try
        {
            var policySet = await QueueOrderingPolicyLoader.LoadAsync(_db, hospitalId, nowUtc, ct);
            var duration = policySet.ForServicePoint(null).ExpectedDurationMinutes;
            return duration is > 0 ? duration.Value : DefaultExpectedDurationMinutes;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return DefaultExpectedDurationMinutes;
        }
    }

    private async Task<int> ResolveGraceMinutesAsync(
        long hospitalId,
        long? servicePointId,
        DateTime nowUtc,
        CancellationToken ct)
    {
        try
        {
            var policySet = await QueueOrderingPolicyLoader.LoadAsync(_db, hospitalId, nowUtc, ct);
            return policySet.ForServicePoint(servicePointId).GraceMinutes;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Queueing.QueuePolicyKeys.DefaultGraceMinutes;
        }
    }

    private async Task<QueueEntry> UpsertExaminationQueueEntryAsync(
        Visit visit,
        QueueEntry? existingEntry,
        Appointment? appointment,
        QueueServicePoint? servicePoint,
        string? normalizedPriority,
        bool doctorChanged,
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
                string.Equals(queueEntry.Status, "RESERVED", StringComparison.OrdinalIgnoreCase) ||
                (doctorChanged && string.Equals(queueEntry.Status, "CALLED", StringComparison.OrdinalIgnoreCase)))
            {
                queueEntry.Status = "WAITING";
                if (doctorChanged)
                {
                    queueEntry.CalledAt = null;
                }
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
