using System.Globalization;
using System.Net;
using System.Text.Json;
using MedFlow.Application.DTOs.Visits;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Appointments;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Visits;

public sealed class VisitTicketService : IVisitTicketService
{
    private const string ReceptionRoleCode = "RECEPTION";
    private const string PrintTicketAction = "PRINT_TICKET";
    private const string VisitEntityType = "VISIT";
    private const int MaxVisitIdentifierLength = 40;
    private const int MinCopies = 1;
    private const int MaxCopies = 5;
    private const int MaxNoteLength = 250;

    private static readonly string[] AllowedReceptionPermissions =
    [
        "PATIENT_CHECKIN",
        "VISIT_ASSIGN",
        "QUEUE_MANAGE",
    ];

    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;

    public VisitTicketService(IApplicationDbContext db, TimeProvider? timeProvider = null)
    {
        _db = db;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<ExaminationTicketDto> GetExaminationTicketAsync(
        long actorUserId,
        string idOrVisitCode,
        CancellationToken ct = default)
        => BuildOrPrintExaminationTicketAsync(
            actorUserId,
            idOrVisitCode,
            request: null,
            ipAddress: null,
            persistPrintAudit: false,
            ct);

    public Task<ExaminationTicketDto> PrintExaminationTicketAsync(
        long actorUserId,
        string idOrVisitCode,
        PrintExaminationTicketRequest? request = null,
        string? ipAddress = null,
        CancellationToken ct = default)
        => BuildOrPrintExaminationTicketAsync(
            actorUserId,
            idOrVisitCode,
            request ?? new PrintExaminationTicketRequest(),
            ipAddress,
            persistPrintAudit: true,
            ct);

    private async Task<ExaminationTicketDto> BuildOrPrintExaminationTicketAsync(
        long actorUserId,
        string idOrVisitCode,
        PrintExaminationTicketRequest? request,
        string? ipAddress,
        bool persistPrintAudit,
        CancellationToken ct)
    {
        if (actorUserId <= 0)
        {
            throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");
        }

        var (trimmedIdentifier, visitId, visitCodeFilter) = ParseVisitIdentifier(idOrVisitCode);

        var copies = 1;
        string? trimmedNote = null;

        if (persistPrintAudit)
        {
            request ??= new PrintExaminationTicketRequest();
            copies = request.Copies ?? 1;
            if (copies < MinCopies || copies > MaxCopies)
            {
                throw new ValidationException("Số bản in phải từ 1 đến 5.");
            }

            if (request.Reason is not null && request.Reason.Trim().Length > MaxNoteLength)
            {
                throw new ValidationException("Ghi chú in phiếu khám không được vượt quá 250 ký tự.");
            }

            if (request.Note is not null && request.Note.Trim().Length > MaxNoteLength)
            {
                throw new ValidationException("Ghi chú in phiếu khám không được vượt quá 250 ký tự.");
            }

            var rawNote = !string.IsNullOrWhiteSpace(request.Reason)
                ? request.Reason
                : request.Note;

            trimmedNote = string.IsNullOrWhiteSpace(rawNote)
                ? null
                : rawNote.Trim();
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var nowLocal = AppointmentBookingRules.GetVietnamNow(_timeProvider);

        var (actorStaff, hospital) = await EnsureReceptionAccessAsync(actorUserId, nowUtc, ct);

        var visit = visitId.HasValue
            ? await _db.Visits
                .AsNoTracking()
                .FirstOrDefaultAsync(v => v.Id == visitId.Value, ct)
            : await _db.Visits
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    v => v.VisitCode != null && v.VisitCode.ToUpper() == visitCodeFilter,
                    ct);

        if (visit is null)
        {
            throw new NotFoundException("Visit", trimmedIdentifier);
        }

        Appointment? appointment = null;
        if (visit.AppointmentId.HasValue)
        {
            appointment = await _db.Appointments
                .AsNoTracking()
                .Include(a => a.Slot)
                    .ThenInclude(s => s!.Service)
                        .ThenInclude(sv => sv!.Department)
                .Include(a => a.Slot)
                    .ThenInclude(s => s!.Service)
                        .ThenInclude(sv => sv!.Specialty)
                .Include(a => a.Slot)
                    .ThenInclude(s => s!.Doctor)
                .Include(a => a.Slot)
                    .ThenInclude(s => s!.Room)
                .FirstOrDefaultAsync(a => a.Id == visit.AppointmentId.Value, ct);
        }

        Staff? checkedInStaff = null;
        if (visit.CheckedInByStaffId.HasValue)
        {
            checkedInStaff = await _db.Staff
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == visit.CheckedInByStaffId.Value, ct);
        }

        HospitalRoom? room = null;
        if (visit.ExaminationRoomId.HasValue)
        {
            room = await _db.HospitalRooms
                .AsNoTracking()
                .Include(r => r.Department)
                .FirstOrDefaultAsync(r => r.Id == visit.ExaminationRoomId.Value, ct);
        }

        Doctor? doctor = null;
        if (visit.AssignedDoctorId.HasValue)
        {
            doctor = await _db.Doctors
                .AsNoTracking()
                .Include(d => d.Staff)
                .Include(d => d.Specialty)
                .FirstOrDefaultAsync(d => d.Id == visit.AssignedDoctorId.Value, ct);
        }

        var associatedHospitalIds = new long?[]
        {
            room?.HospitalId,
            room?.Department?.HospitalId,
            doctor?.HospitalId,
            doctor?.Staff?.HospitalId,
            checkedInStaff?.HospitalId,
            appointment?.Slot?.Service?.HospitalId,
            appointment?.Slot?.Doctor?.HospitalId,
            appointment?.Slot?.Room?.HospitalId,
        };

        if (associatedHospitalIds.Any(hId => hId.HasValue && hId.Value != hospital.Id))
        {
            throw new ForbiddenException("Bạn không có quyền truy cập phiếu khám của cơ sở y tế khác.");
        }

        var visitStatus = AppointmentBookingRules.NormalizeStatusKey(visit.Status);
        if (string.IsNullOrEmpty(visitStatus))
        {
            visitStatus = "CHECKED_IN";
        }

        if (visitStatus is "CANCELLED" or "NO_SHOW")
        {
            throw new ConflictException("Lượt khám đã bị hủy, không thể in phiếu khám.");
        }

        var patientId = visit.PatientId ?? appointment?.PatientId;
        if (!patientId.HasValue)
        {
            throw new NotFoundException("Patient", 0);
        }

        var patient = await _db.Patients
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == patientId.Value, ct)
            ?? throw new NotFoundException("Patient", patientId.Value);

        // UC26 Prerequisite: Assigned Doctor
        if (!visit.AssignedDoctorId.HasValue)
        {
            throw new ConflictException("Lượt khám chưa được phân công bác sĩ phụ trách (UC26). Vui lòng gán bác sĩ trước khi in phiếu khám.");
        }

        if (doctor is null)
        {
            throw new NotFoundException("Doctor", visit.AssignedDoctorId.Value);
        }

        if (!doctor.IsActive)
        {
            throw new ConflictException("Bác sĩ được phân công hiện đang tạm ngừng hoạt động, không thể in phiếu khám.");
        }

        // UC27 Prerequisite: Assigned Examination Room
        if (!visit.ExaminationRoomId.HasValue)
        {
            throw new ConflictException("Lượt khám chưa được phân công buồng khám (UC27). Vui lòng gán buồng khám trước khi in phiếu khám.");
        }

        if (room is null)
        {
            throw new NotFoundException("HospitalRoom", visit.ExaminationRoomId.Value);
        }

        if (!string.Equals(room.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException("Buồng khám được phân công hiện không ở trạng thái hoạt động, không thể in phiếu khám.");
        }

        // Queue entry resolution
        var candidateQueues = await _db.QueueEntries
            .AsNoTracking()
            .Where(q =>
                (q.VisitId == visit.Id || (visit.AppointmentId.HasValue && q.AppointmentId == visit.AppointmentId.Value)) &&
                (q.QueueType == null || q.QueueType.ToUpper() == "EXAMINATION"))
            .OrderByDescending(q => q.VisitId == visit.Id)
            .ThenByDescending(q => q.Id)
            .ToListAsync(ct);

        var examQueue = candidateQueues.FirstOrDefault(q =>
            !string.Equals(q.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
            ?? candidateQueues.FirstOrDefault();

        if (examQueue is not null &&
            string.Equals(examQueue.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException("Hàng đợi khám của lượt khám này đã bị hủy, không thể in phiếu khám.");
        }

        var visitDate = visit.CheckedInAt.HasValue
            ? DateOnly.FromDateTime(visit.CheckedInAt.Value)
            : DateOnly.FromDateTime(nowLocal);

        var queueDate = examQueue?.QueueDate ?? visitDate;

        int queueNumber;
        if (examQueue?.QueueNumber is > 0)
        {
            queueNumber = examQueue.QueueNumber.Value;
        }
        else
        {
            var roomVisits = await _db.Visits
                .AsNoTracking()
                .Where(v =>
                    v.ExaminationRoomId == room.Id &&
                    (v.Status == null ||
                     (v.Status.ToUpper() != "CANCELLED" && v.Status.ToUpper() != "NO_SHOW")))
                .ToListAsync(ct);

            var sameDayVisits = roomVisits
                .Where(v =>
                    (v.CheckedInAt.HasValue
                        ? DateOnly.FromDateTime(v.CheckedInAt.Value)
                        : visitDate) == visitDate)
                .OrderBy(v => v.CheckedInAt ?? DateTime.MaxValue)
                .ThenBy(v => v.Id)
                .ToList();

            var index = sameDayVisits.FindIndex(v => v.Id == visit.Id);
            queueNumber = index >= 0
                ? index + 1
                : Math.Max(1, sameDayVisits.Count(v => v.Id <= visit.Id));
        }

        var queueNumberFormatted = queueNumber.ToString("D3", CultureInfo.InvariantCulture);
        var ticketNo = $"A{queueNumber:D3}";

        var queueStatus = AppointmentBookingRules.NormalizeStatusKey(examQueue?.Status);
        if (string.IsNullOrEmpty(queueStatus))
        {
            queueStatus = visitStatus == "COMPLETED" ? "COMPLETED" : "WAITING";
        }

        var waitingAheadCount = 0;
        if (visitStatus != "COMPLETED" &&
            queueStatus is not ("COMPLETED" or "IN_PROGRESS" or "IN_CONSULTATION" or "CALLED") &&
            examQueue?.ServicePointId != null)
        {
            var servicePointId = examQueue.ServicePointId.Value;
            waitingAheadCount = await _db.QueueEntries
                .AsNoTracking()
                .CountAsync(q =>
                    q.Id != examQueue.Id &&
                    q.ServicePointId == servicePointId &&
                    q.QueueDate == queueDate &&
                    (q.QueueType == null || q.QueueType.ToUpper() == "EXAMINATION") &&
                    q.QueueNumber != null &&
                    q.QueueNumber < queueNumber &&
                    q.Status != null &&
                    (q.Status.ToUpper() == "WAITING" ||
                     q.Status.ToUpper() == "RESERVED" ||
                     q.Status.ToUpper() == "READY"),
                    ct);
        }

        var estimatedWaitMinutes = waitingAheadCount * 15;

        // Resolve Department, Specialty, and Service metadata
        var department = room.Department ?? appointment?.Slot?.Service?.Department;
        if (department is null)
        {
            var fallbackDeptId = room.DepartmentId
                ?? appointment?.Slot?.Service?.DepartmentId
                ?? doctor.Staff?.DepartmentId;

            if (fallbackDeptId.HasValue)
            {
                department = await _db.Departments
                    .AsNoTracking()
                    .FirstOrDefaultAsync(d => d.Id == fallbackDeptId.Value, ct);
            }
        }

        var specialty = doctor.Specialty ?? appointment?.Slot?.Service?.Specialty;
        if (specialty is null)
        {
            var fallbackSpecialtyId = doctor.SpecialtyId ?? appointment?.Slot?.Service?.SpecialtyId;
            if (fallbackSpecialtyId.HasValue)
            {
                specialty = await _db.Specialties
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Id == fallbackSpecialtyId.Value, ct);
            }
        }

        var service = appointment?.Slot?.Service;
        if (service is null)
        {
            var targetSpecialtyId = specialty?.Id ?? doctor.SpecialtyId;
            var targetDepartmentId = department?.Id ?? room.DepartmentId;

            service = await _db.Services
                .AsNoTracking()
                .Where(s =>
                    s.HospitalId == hospital.Id &&
                    s.ServiceType.ToUpper() == "EXAMINATION" &&
                    s.IsActive != false &&
                    ((targetSpecialtyId.HasValue && s.SpecialtyId == targetSpecialtyId.Value) ||
                     (targetDepartmentId.HasValue && s.DepartmentId == targetDepartmentId.Value)))
                .OrderBy(s => s.Id)
                .FirstOrDefaultAsync(ct);
        }

        var resolvedVisitCode = !string.IsNullOrWhiteSpace(visit.VisitCode)
            ? visit.VisitCode
            : $"VIS-{queueDate:yyyyMM}-{visit.Id:D4}";

        var ticketCode = $"TKT-{resolvedVisitCode}";

        var visitIdText = visit.Id.ToString(CultureInfo.InvariantCulture);
        var existingPrintCount = await _db.AuditLogs
            .AsNoTracking()
            .CountAsync(a =>
                a.EntityType != null &&
                a.EntityType.ToUpper() == VisitEntityType &&
                a.EntityId == visitIdText &&
                a.Action != null &&
                a.Action.ToUpper() == PrintTicketAction,
                ct);

        int printCount;
        bool isReprint;

        if (persistPrintAudit)
        {
            printCount = existingPrintCount + 1;
            isReprint = existingPrintCount > 0 || request?.IsReprint == true;

            var changeData = JsonSerializer.Serialize(new
            {
                visitId = visit.Id,
                visitCode = resolvedVisitCode,
                ticketCode,
                ticketNo,
                queueNumber,
                patientId = patient.Id,
                patientCode = patient.PatientCode,
                doctorId = doctor.Id,
                doctorCode = doctor.DoctorCode,
                roomId = room.Id,
                roomCode = room.RoomCode,
                copies,
                printCount,
                isReprint,
                note = trimmedNote,
            });

            _db.AuditLogs.Add(new AuditLog
            {
                ActorUserId = actorUserId,
                HospitalId = hospital.Id,
                Action = PrintTicketAction,
                EntityType = VisitEntityType,
                EntityId = visitIdText,
                Reason = trimmedNote ?? (isReprint ? "In lại phiếu khám bệnh" : "In phiếu khám bệnh tại quầy tiếp đón"),
                ChangeData = changeData,
                IpAddress = !string.IsNullOrWhiteSpace(ipAddress) && IPAddress.TryParse(ipAddress, out var parsedIp)
                    ? parsedIp
                    : null,
                OccurredAt = nowUtc,
            });

            await _db.SaveChangesAsync(ct);
        }
        else
        {
            printCount = existingPrintCount;
            isReprint = existingPrintCount > 0;
        }

        var patientAge = CalculateAge(patient.DateOfBirth, queueDate);
        var (priority, priorityLabel) = ResolvePriority(examQueue?.ManualPriority, patientAge);
        var visitType = visit.AppointmentId.HasValue ? "SCHEDULED" : "WALK_IN";
        var visitTypeLabel = visit.AppointmentId.HasValue
            ? "Khám theo lịch hẹn"
            : "Khám vãng lai (Walk-in)";

        var scheduledTimeRange = FormatScheduledTimeRange(appointment?.Slot);

        var departmentName = !string.IsNullOrWhiteSpace(department?.Name)
            ? department.Name
            : "Khoa Khám bệnh";

        var specialtyName = !string.IsNullOrWhiteSpace(specialty?.Name)
            ? specialty.Name
            : "Nội tổng quát";

        var serviceName = !string.IsNullOrWhiteSpace(service?.Name)
            ? service.Name
            : $"Khám {specialtyName}";

        var doctorFullName = !string.IsNullOrWhiteSpace(doctor.Staff?.FullName)
            ? doctor.Staff.FullName
            : $"Bác sĩ {doctor.DoctorCode}";

        var reasonForVisit = FirstNonEmpty(
            visit.ReasonForVisit,
            appointment?.ReasonForVisit,
            appointment?.ReceptionNote,
            serviceName);

        var checkedInAtLabel = visit.CheckedInAt.HasValue
            ? visit.CheckedInAt.Value.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)
            : null;

        var printedAtLabel = nowLocal.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

        var locationSuffix = !string.IsNullOrWhiteSpace(room.Location)
            ? $" ({room.Location})"
            : string.Empty;

        var guidanceNote =
            $"Vui lòng di chuyển đến {room.Name} - {room.RoomCode}{locationSuffix} và theo dõi màn hình gọi số thứ tự {ticketNo}.";

        return new ExaminationTicketDto(
            VisitId: visit.Id,
            VisitCode: resolvedVisitCode,
            TicketCode: ticketCode,
            TicketNo: ticketNo,
            QueueNumber: queueNumber,
            QueueNumberFormatted: queueNumberFormatted,
            QueueEntryId: examQueue?.Id,
            QueueDate: queueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            QueueStatus: queueStatus,
            Priority: priority,
            PriorityLabel: priorityLabel,
            WaitingAheadCount: waitingAheadCount,
            EstimatedWaitMinutes: estimatedWaitMinutes,
            VisitType: visitType,
            VisitTypeLabel: visitTypeLabel,
            VisitStatus: visitStatus,
            VisitStatusLabel: FormatVisitStatusLabel(visitStatus),
            AppointmentId: visit.AppointmentId,
            AppointmentCode: appointment?.AppointmentCode,
            ScheduledTimeRange: scheduledTimeRange,
            PatientId: patient.Id,
            PatientCode: !string.IsNullOrWhiteSpace(patient.PatientCode)
                ? patient.PatientCode
                : $"PAT-{patient.Id:D4}",
            PatientName: patient.FullName,
            PatientDateOfBirth: patient.DateOfBirth?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            PatientBirthYear: patient.DateOfBirth?.Year,
            PatientAge: patientAge,
            PatientGender: patient.Gender,
            PatientGenderLabel: FormatGenderLabel(patient.Gender),
            PatientPhone: patient.Phone,
            HospitalId: hospital.Id,
            HospitalCode: hospital.HospitalCode,
            HospitalName: hospital.Name,
            HospitalAddress: hospital.Address,
            DepartmentId: department?.Id ?? room.DepartmentId,
            DepartmentCode: department?.DepartmentCode,
            DepartmentName: departmentName,
            SpecialtyId: specialty?.Id ?? doctor.SpecialtyId,
            SpecialtyCode: specialty?.SpecialtyCode,
            SpecialtyName: specialtyName,
            RoomId: room.Id,
            RoomCode: room.RoomCode,
            RoomName: room.Name,
            RoomLocation: room.Location,
            DoctorId: doctor.Id,
            DoctorCode: doctor.DoctorCode,
            DoctorTitle: doctor.ProfessionalTitle,
            DoctorName: doctorFullName,
            ServiceId: service?.Id,
            ServiceCode: service?.ServiceCode,
            ServiceName: serviceName,
            ReasonForVisit: reasonForVisit,
            CheckedInAt: visit.CheckedInAt,
            CheckedInAtLabel: checkedInAtLabel,
            CheckedInByStaffId: visit.CheckedInByStaffId ?? actorStaff.Id,
            CheckedInByStaffName: checkedInStaff?.FullName ?? actorStaff.FullName,
            PrintedAtUtc: nowUtc,
            PrintedAtLabel: printedAtLabel,
            PrintedByUserId: actorUserId,
            PrintedByStaffCode: actorStaff.StaffCode,
            PrintedByStaffName: actorStaff.FullName,
            Copies: copies,
            PrintCount: printCount,
            IsReprint: isReprint,
            GuidanceNote: guidanceNote);
    }

    private async Task<(Staff ActorStaff, Hospital Hospital)> EnsureReceptionAccessAsync(
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
            throw new ForbiddenException("Tài khoản nhân viên tiếp đón không ở trạng thái hoạt động.");
        }

        if (actorUser.LockedUntil.HasValue && actorUser.LockedUntil.Value > nowUtc)
        {
            throw new ForbiddenException("Tài khoản nhân viên tiếp đón đang bị tạm khóa.");
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

        if (!actorRoleCodes.Any(c => string.Equals(c, ReceptionRoleCode, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ForbiddenException("Người dùng không có quyền in phiếu khám bệnh.");
        }

        var rolePermissionIds = await _db.RolePermissions
            .AsNoTracking()
            .Where(rp => actorRoleIds.Contains(rp.RoleId))
            .Select(rp => rp.PermissionId)
            .ToListAsync(ct);

        if (rolePermissionIds.Count > 0)
        {
            var actorPermissions = await _db.Permissions
                .AsNoTracking()
                .Where(p => rolePermissionIds.Contains(p.Id))
                .Select(p => p.Code)
                .ToListAsync(ct);

            var hasTicketPermission = actorPermissions.Any(code =>
                AllowedReceptionPermissions.Contains(code, StringComparer.OrdinalIgnoreCase));

            if (!hasTicketPermission)
            {
                throw new ForbiddenException("Vai trò tiếp đón hiện không được cấp quyền thao tác phiếu khám.");
            }
        }

        var actorStaff = await _db.Staff
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == actorUserId, ct);

        if (actorStaff is null || !actorStaff.HospitalId.HasValue)
        {
            throw new ForbiddenException("Tài khoản nhân viên tiếp đón chưa được gán cơ sở y tế.");
        }

        if (!string.Equals(actorStaff.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            throw new ForbiddenException("Hồ sơ nhân sự của nhân viên tiếp đón đã ngừng hoạt động.");
        }

        var hospital = await _db.Hospitals
            .AsNoTracking()
            .FirstOrDefaultAsync(h => h.Id == actorStaff.HospitalId.Value, ct)
            ?? throw new NotFoundException("Hospital", actorStaff.HospitalId.Value);

        if (hospital.IsActive == false)
        {
            throw new ForbiddenException("Cơ sở y tế của nhân viên tiếp đón hiện đang ngừng hoạt động.");
        }

        return (actorStaff, hospital);
    }

    private static (string Trimmed, long? Id, string? Code) ParseVisitIdentifier(string? idOrVisitCode)
    {
        if (string.IsNullOrWhiteSpace(idOrVisitCode))
        {
            throw new ValidationException("Mã lượt khám không hợp lệ.");
        }

        var trimmed = idOrVisitCode.Trim();
        if (trimmed.StartsWith('#'))
        {
            trimmed = trimmed[1..].TrimStart();
        }

        if (string.IsNullOrEmpty(trimmed) ||
            trimmed.Length > MaxVisitIdentifierLength ||
            trimmed.StartsWith('-') ||
            trimmed.StartsWith('_') ||
            !trimmed.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '-' || ch == '_'))
        {
            throw new ValidationException("Mã lượt khám không hợp lệ.");
        }

        if (trimmed.All(char.IsAsciiDigit))
        {
            if (!long.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
            {
                throw new ValidationException("Mã lượt khám không hợp lệ.");
            }

            return (trimmed, id, null);
        }

        return (trimmed, null, trimmed.ToUpperInvariant());
    }

    private static int? CalculateAge(DateOnly? dateOfBirth, DateOnly referenceDate)
    {
        if (!dateOfBirth.HasValue)
        {
            return null;
        }

        var age = referenceDate.Year - dateOfBirth.Value.Year;
        if (referenceDate < dateOfBirth.Value.AddYears(age))
        {
            age--;
        }

        return Math.Max(0, age);
    }

    private static (string Priority, string PriorityLabel) ResolvePriority(string? manualPriority, int? patientAge)
    {
        var normalized = AppointmentBookingRules.NormalizeStatusKey(manualPriority);
        if (normalized is "URGENT" or "EMERGENCY")
        {
            return ("URGENT", "Ưu tiên khẩn");
        }

        if (normalized is "PRIORITY" or "ELDERLY" ||
            (patientAge.HasValue && (patientAge.Value >= 60 || patientAge.Value < 6)))
        {
            return ("PRIORITY", "Ưu tiên");
        }

        return ("NORMAL", "Thông thường");
    }

    private static string FormatVisitStatusLabel(string visitStatus) =>
        visitStatus switch
        {
            "CHECKED_IN" => "Đã tiếp nhận",
            "WAITING" => "Đang chờ khám",
            "IN_PROGRESS" or "IN_CONSULTATION" => "Đang khám",
            "COMPLETED" => "Đã hoàn tất",
            _ => "Đã tiếp nhận",
        };

    private static string FormatGenderLabel(string? gender) =>
        gender?.Trim().ToUpperInvariant() switch
        {
            "MALE" => "Nam",
            "FEMALE" => "Nữ",
            "OTHER" => "Khác",
            _ => "Chưa cập nhật",
        };

    private static string? FormatScheduledTimeRange(ExaminationSlot? slot)
    {
        if (slot?.StartsAt is null)
        {
            return null;
        }

        if (slot.EndsAt is not null)
        {
            return $"{slot.StartsAt.Value:HH:mm} - {slot.EndsAt.Value:HH:mm}";
        }

        return slot.StartsAt.Value.ToString("HH:mm", CultureInfo.InvariantCulture);
    }

    private static string? FirstNonEmpty(params string?[] candidates) =>
        candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c))?.Trim();
}
