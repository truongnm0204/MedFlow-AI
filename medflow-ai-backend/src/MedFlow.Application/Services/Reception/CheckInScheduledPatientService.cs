using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using MedFlow.Application.DTOs.Reception;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Appointments;
using MedFlow.Application.Services.Policies;
using MedFlow.Application.Services.Visits;
using MedFlow.Application.Services.Visits.Queueing;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Reception;

public sealed class CheckInScheduledPatientService : ICheckInScheduledPatientService
{
    public const string PatientCheckedInAction = "PATIENT_CHECKED_IN";
    public const string PatientCheckInPermissionCode = "PATIENT_CHECKIN";

    private const string VisitEntityType = "VISIT";
    private const int MaxAppointmentIdentifierLength = 40;
    private const int MinReasonLength = 3;
    private const int MaxReasonLength = 300;
    private const int MaxNotesLength = 500;
    private const int DefaultWaitMinutesPerPatient = 15;

    private static readonly Regex BloodPressureRegex = new(
        @"^\d{2,3}/\d{2,3}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly string[] HospitalAdminAllowedPermissions =
    [
        PatientCheckInPermissionCode,
        "VISIT_ASSIGN",
        "QUEUE_MANAGE",
    ];

    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;

    public CheckInScheduledPatientService(
        IApplicationDbContext db,
        TimeProvider? timeProvider = null)
    {
        _db = db;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<CheckInScheduledPatientResponseDto> CheckInScheduledPatientAsync(
        long actorUserId,
        string appointmentIdOrCode,
        CheckInScheduledPatientRequest request,
        string? ipAddress = null,
        CancellationToken ct = default)
    {
        if (actorUserId <= 0)
        {
            throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");
        }

        if (request is null)
        {
            throw new ValidationException("Dữ liệu check-in lịch hẹn không hợp lệ.");
        }

        var (trimmedIdentifier, parsedId, parsedCode) = ParseAppointmentIdentifier(appointmentIdOrCode);

        var isIdentityVerified = request.IsIdentityVerified ?? request.IdentityVerified ?? false;
        if (!isIdentityVerified)
        {
            throw new ValidationException(
                "Bắt buộc xác nhận đã đối chiếu giấy tờ tùy thân của bệnh nhân (isIdentityVerified = true).");
        }

        string? normalizedBloodPressure = null;
        if (!string.IsNullOrWhiteSpace(request.BloodPressure))
        {
            normalizedBloodPressure = request.BloodPressure.Trim();
            if (!BloodPressureRegex.IsMatch(normalizedBloodPressure))
            {
                throw new ValidationException("Huyết áp phải có định dạng chuẩn (ví dụ: 120/80).");
            }
        }

        if (request.HeartRate.HasValue && (request.HeartRate.Value < 40 || request.HeartRate.Value > 220))
        {
            throw new ValidationException("Mạch không hợp lệ (phải từ 40 đến 220 bpm).");
        }

        if (request.Temperature.HasValue &&
            (double.IsNaN(request.Temperature.Value) ||
             double.IsInfinity(request.Temperature.Value) ||
             request.Temperature.Value < 34.0 ||
             request.Temperature.Value > 43.0))
        {
            throw new ValidationException("Thân nhiệt không hợp lệ (phải từ 34.0°C đến 43.0°C).");
        }

        var normalizedPriority = string.IsNullOrWhiteSpace(request.Priority)
            ? ClinicalQueueRules.PriorityNormal
            : ClinicalQueueRules.NormalizePriorityOrThrow(request.Priority)!;

        string? trimmedNotes = null;
        if (!string.IsNullOrWhiteSpace(request.Notes))
        {
            trimmedNotes = request.Notes.Trim();
            if (trimmedNotes.Length > MaxNotesLength)
            {
                throw new ValidationException("Ghi chú tiếp nhận không được vượt quá 500 ký tự.");
            }
        }

        string? explicitReason = null;
        if (!string.IsNullOrWhiteSpace(request.ReasonForVisit))
        {
            explicitReason = request.ReasonForVisit.Trim();
            if (explicitReason.Length < MinReasonLength)
            {
                throw new ValidationException("Lý do khám sơ bộ phải có ít nhất 3 ký tự.");
            }

            if (explicitReason.Length > MaxReasonLength)
            {
                throw new ValidationException("Lý do khám không được vượt quá 300 ký tự.");
            }
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var nowLocal = AppointmentBookingRules.GetVietnamNow(_timeProvider);
        var today = DateOnly.FromDateTime(nowLocal);

        var actor = await EnsureCheckInActorAccessAsync(actorUserId, nowUtc, ct);

        var appointment = await LoadAppointmentAsync(parsedId, parsedCode, ct)
            ?? throw new NotFoundException("Appointment", trimmedIdentifier);

        var slot = appointment.Slot ?? (appointment.SlotId.HasValue
            ? await _db.ExaminationSlots
                .Include(s => s.Service)
                    .ThenInclude(sv => sv!.Department)
                .Include(s => s.Service)
                    .ThenInclude(sv => sv!.Specialty)
                .Include(s => s.Room)
                    .ThenInclude(r => r!.Department)
                .Include(s => s.Doctor)
                    .ThenInclude(d => d!.Staff)
                .Include(s => s.Doctor)
                    .ThenInclude(d => d!.Specialty)
                .FirstOrDefaultAsync(s => s.Id == appointment.SlotId.Value, ct)
            : null);

        var doctor = slot?.Doctor ?? (slot?.DoctorId.HasValue == true
            ? await _db.Doctors
                .AsNoTracking()
                .Include(d => d.Staff)
                .Include(d => d.Specialty)
                .FirstOrDefaultAsync(d => d.Id == slot.DoctorId.Value, ct)
            : null);

        var room = slot?.Room ?? (slot?.RoomId.HasValue == true
            ? await _db.HospitalRooms
                .AsNoTracking()
                .Include(r => r.Department)
                .FirstOrDefaultAsync(r => r.Id == slot.RoomId.Value, ct)
            : null);

        var appointmentHospitalId = slot?.Service?.HospitalId
            ?? room?.HospitalId
            ?? room?.Department?.HospitalId
            ?? doctor?.HospitalId
            ?? doctor?.Staff?.HospitalId;

        if (!actor.IsSystemAdmin &&
            appointmentHospitalId.HasValue &&
            appointmentHospitalId.Value != actor.ActorHospitalId!.Value)
        {
            throw new ForbiddenException("Bạn không có quyền thực hiện check-in cho lịch hẹn thuộc cơ sở y tế khác.");
        }

        var effectiveHospitalId = appointmentHospitalId ?? actor.ActorHospitalId ?? 1L;

        var patient = appointment.Patient ?? (appointment.PatientId.HasValue
            ? await _db.Patients.FirstOrDefaultAsync(p => p.Id == appointment.PatientId.Value, ct)
            : null)
            ?? throw new NotFoundException("Patient", appointment.PatientId ?? 0);

        if (!string.Equals(patient.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException(
                "Hồ sơ bệnh nhân hiện không ở trạng thái hoạt động, không thể thực hiện check-in.");
        }

        var normalizedApptStatus = AppointmentBookingRules.NormalizeStatusKey(appointment.Status);
        if (string.IsNullOrEmpty(normalizedApptStatus))
        {
            normalizedApptStatus = AppointmentBookingRules.BookedStatus;
        }

        if (normalizedApptStatus == AppointmentBookingRules.CancelledStatus)
        {
            throw new ConflictException("Lịch hẹn đã bị hủy, không thể thực hiện check-in.");
        }

        if (normalizedApptStatus == AppointmentBookingRules.NoShowStatus)
        {
            throw new ConflictException("Lịch hẹn đã được ghi nhận vắng mặt (NO_SHOW), không thể thực hiện check-in.");
        }

        if (normalizedApptStatus == AppointmentBookingRules.CompletedStatus)
        {
            throw new ConflictException("Lịch hẹn đã hoàn tất khám, không thể thực hiện check-in lại.");
        }

        var existingVisit = await _db.Visits
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.AppointmentId == appointment.Id, ct);

        if (existingVisit is not null || normalizedApptStatus == AppointmentBookingRules.CheckedInStatus)
        {
            throw new ConflictException("Lịch hẹn đã được check-in trước đó, không thể tạo thêm lượt khám mới.");
        }

        if (normalizedApptStatus is not ("BOOKED" or "CONFIRMED" or "PENDING"))
        {
            throw new ConflictException($"Trạng thái lịch hẹn '{appointment.Status}' không hợp lệ để check-in.");
        }

        if (slot is null || !slot.StartsAt.HasValue)
        {
            throw new ConflictException("Lịch hẹn chưa gắn khung giờ khám hợp lệ.");
        }

        var slotDate = DateOnly.FromDateTime(slot.StartsAt.Value);
        if (slotDate < today)
        {
            throw new ConflictException("Lịch hẹn đã quá ngày khám, không thể thực hiện check-in.");
        }

        if (slotDate > today)
        {
            throw new ConflictException("Chưa đến ngày hẹn khám, chỉ có thể check-in trong ngày diễn ra lịch hẹn.");
        }

        if (doctor is not null &&
            (!doctor.IsActive ||
             (doctor.Staff is not null &&
              !string.Equals(doctor.Staff.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))))
        {
            throw new ConflictException("Bác sĩ phụ trách lịch hẹn hiện đang tạm ngưng hoạt động.");
        }

        if (room is not null && !string.Equals(room.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException(
                $"Buồng khám '{room.RoomCode}' của lịch hẹn hiện không khả dụng (trạng thái: {room.Status}).");
        }

        if (room?.Department is not null && room.Department.IsActive == false)
        {
            throw new ConflictException($"Khoa phòng '{room.Department.Name}' hiện đang tạm ngưng hoạt động.");
        }

        var dayStart = today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var dayEnd = dayStart.AddDays(1);
        var patientSameDayVisits = await _db.Visits
            .AsNoTracking()
            .Where(v => v.PatientId == patient.Id && v.CheckedInAt >= dayStart && v.CheckedInAt < dayEnd)
            .ToListAsync(ct);

        if (patientSameDayVisits.Any(v => AppointmentBookingRules.IsActiveVisitStatus(v.Status)))
        {
            throw new ConflictException("Bệnh nhân đang có một lượt khám khác chưa hoàn tất trong ngày hôm nay.");
        }

        var resolvedReason = explicitReason
            ?? (!string.IsNullOrWhiteSpace(appointment.ReasonForVisit)
                ? appointment.ReasonForVisit.Trim()
                : appointment.ReceptionNote?.Trim());

        if (string.IsNullOrWhiteSpace(resolvedReason) || resolvedReason.Length < MinReasonLength)
        {
            throw new ValidationException("Lý do khám sơ bộ phải có ít nhất 3 ký tự.");
        }

        if (resolvedReason.Length > MaxReasonLength)
        {
            resolvedReason = resolvedReason[..MaxReasonLength];
        }

        var servicePoint = await ResolveQueueServicePointAsync(
            room?.Id ?? slot.RoomId,
            doctor?.Id ?? slot.DoctorId,
            effectiveHospitalId,
            ct);

        if (ClinicalQueueRules.IsElevated(normalizedPriority))
        {
            ClinicalQueueRules.EnsurePatientEligible(normalizedPriority, patient, today);

            var requiresVerification = await QueuePolicyReader.IsManualPriorityVerificationRequiredAsync(
                _db,
                effectiveHospitalId,
                servicePoint?.Id,
                nowUtc,
                ct);

            if (requiresVerification && request.PriorityVerificationConfirmed == false)
            {
                throw new ValidationException(
                    "Chính sách bệnh viện yêu cầu xác minh giấy tờ trước khi áp dụng ưu tiên ELDERLY/PREGNANT.");
            }
        }

        var policySet = await QueueOrderingPolicyLoader.LoadAsync(_db, effectiveHospitalId, nowUtc, ct);
        var orderingPolicy = policySet.ForServicePoint(servicePoint?.Id);
        var graceMinutes = orderingPolicy.GraceMinutes;

        await using var tx = await _db.BeginTransactionAsync(ct);

        var previousAppointmentStatus = appointment.Status ?? AppointmentBookingRules.BookedStatus;
        appointment.Status = AppointmentBookingRules.CheckedInStatus;
        appointment.ReasonForVisit ??= resolvedReason;
        if (!string.IsNullOrWhiteSpace(trimmedNotes))
        {
            appointment.ReceptionNote = trimmedNotes;
        }

        var assignedDoctorId = doctor?.Id ?? slot.DoctorId;
        var assignedRoomId = room?.Id ?? slot.RoomId;
        var visitStatus = assignedRoomId.HasValue && assignedDoctorId.HasValue ? "WAITING" : "CHECKED_IN";

        var visit = new Visit
        {
            PatientId = patient.Id,
            AppointmentId = appointment.Id,
            AssignedDoctorId = assignedDoctorId,
            ExaminationRoomId = assignedRoomId,
            CheckedInByStaffId = actor.ActorStaffId,
            ReasonForVisit = resolvedReason,
            Status = visitStatus,
            CheckedInAt = nowLocal,
        };

        _db.Visits.Add(visit);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            throw new ConflictException("Lịch hẹn đã được check-in trước đó, không thể tạo thêm lượt khám mới.");
        }

        visit.VisitCode = $"VIS-{today:yyyyMM}-{visit.Id:D4}";

        var existingQueueEntry = await FindExistingAppointmentQueueEntryAsync(appointment.Id, visit.Id, ct);
        var queueEntry = await ActivateOrCreateExaminationQueueEntryAsync(
            visit,
            existingQueueEntry,
            appointment,
            slot,
            servicePoint,
            normalizedPriority,
            today,
            nowLocal,
            graceMinutes,
            ct);

        var queueNumber = queueEntry.QueueNumber ?? 1;
        var ticketNo = $"A{queueNumber:D3}";
        var effectivePriority = ClinicalQueueRules.ResolveEffectivePriority(
            queueEntry.ManualPriority,
            patient.DateOfBirth,
            today);

        var parsedIp = !string.IsNullOrWhiteSpace(ipAddress) && IPAddress.TryParse(ipAddress, out var ip)
            ? ip
            : null;

        var changeData = JsonSerializer.Serialize(new
        {
            visitId = visit.Id,
            visitCode = visit.VisitCode,
            appointmentId = appointment.Id,
            appointmentCode = appointment.AppointmentCode,
            previousAppointmentStatus,
            updatedAppointmentStatus = AppointmentBookingRules.CheckedInStatus,
            patientId = patient.Id,
            patientCode = patient.PatientCode,
            doctorId = assignedDoctorId,
            roomId = assignedRoomId,
            roomCode = room?.RoomCode,
            servicePointId = queueEntry.ServicePointId,
            queueEntryId = queueEntry.Id,
            queueNumber,
            ticketNo,
            queueStatus = queueEntry.Status,
            presenceStatus = queueEntry.PresenceStatus,
            arrivalClass = queueEntry.ArrivalClass,
            manualPriority = queueEntry.ManualPriority,
            effectivePriority,
            reasonForVisit = resolvedReason,
            vitals = new
            {
                bloodPressure = normalizedBloodPressure,
                heartRate = request.HeartRate,
                temperature = request.Temperature,
            },
            isIdentityVerified = true,
            isInsuranceVerified = request.IsInsuranceVerified == true,
            notes = trimmedNotes,
        });

        _db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actorUserId,
            HospitalId = effectiveHospitalId,
            Action = PatientCheckedInAction,
            EntityType = VisitEntityType,
            EntityId = visit.Id.ToString(CultureInfo.InvariantCulture),
            Reason = resolvedReason,
            ChangeData = changeData,
            IpAddress = parsedIp,
            OccurredAt = nowUtc,
        });

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        var effectiveSpId = queueEntry.ServicePointId ?? servicePoint?.Id;
        var waitingCountBefore = await CountWaitingBeforeAsync(
            queueEntry.Id,
            effectiveSpId,
            today,
            queueNumber,
            ct);

        var durationPerPatient = orderingPolicy.ExpectedDurationMinutes is > 0
            ? orderingPolicy.ExpectedDurationMinutes.Value
            : DefaultWaitMinutesPerPatient;
        var estimatedWaitMinutes = waitingCountBefore * durationPerPatient;

        var department = room?.Department ?? slot.Service?.Department;
        if (department is null)
        {
            var fallbackDeptId = room?.DepartmentId
                ?? slot.Service?.DepartmentId
                ?? doctor?.Staff?.DepartmentId;
            if (fallbackDeptId.HasValue)
            {
                department = await _db.Departments
                    .AsNoTracking()
                    .FirstOrDefaultAsync(d => d.Id == fallbackDeptId.Value, ct);
            }
        }

        var departmentName = !string.IsNullOrWhiteSpace(department?.Name)
            ? department.Name
            : "Khoa Khám bệnh";

        var roomName = !string.IsNullOrWhiteSpace(room?.Name)
            ? room.Name
            : "Phòng khám ngoại trú";

        var roomDisplay = !string.IsNullOrWhiteSpace(room?.RoomCode)
            ? $"{roomName} ({room.RoomCode})"
            : roomName;

        var locationSuffix = !string.IsNullOrWhiteSpace(room?.Location)
            ? $" - {room.Location}"
            : string.Empty;

        var doctorName = !string.IsNullOrWhiteSpace(doctor?.Staff?.FullName)
            ? doctor.Staff.FullName
            : (!string.IsNullOrWhiteSpace(doctor?.DoctorCode) ? $"Bác sĩ {doctor.DoctorCode}" : "Bác sĩ phụ trách");

        var servicePointName = servicePoint?.Name;
        if (string.IsNullOrWhiteSpace(servicePointName) && effectiveSpId.HasValue)
        {
            servicePointName = await TryGetServicePointNameAsync(effectiveSpId.Value, ct);
        }

        servicePointName ??= roomName;

        var instructions = new List<string>
        {
            $"Di chuyển đến trước {roomDisplay} ({departmentName}{locationSuffix}).",
            "Theo dõi màn hình điện tử và lắng nghe loa gọi số thứ tự tiếp nhận.",
            "Chuẩn bị sẵn thẻ CCCD/BHYT bản gốc trước khi vào phòng khám.",
        };

        var appointmentCode = !string.IsNullOrWhiteSpace(appointment.AppointmentCode)
            ? appointment.AppointmentCode
            : $"APT-{today:yyyyMM}-{appointment.Id:D4}";

        var patientCode = !string.IsNullOrWhiteSpace(patient.PatientCode)
            ? patient.PatientCode
            : $"PAT-{patient.Id:D4}";

        var ticketCode = $"TKT-{visit.VisitCode}";

        return new CheckInScheduledPatientResponseDto(
            VisitId: visit.Id,
            VisitCode: visit.VisitCode,
            TicketCode: ticketCode,
            TicketNo: ticketNo,
            QueueEntryId: queueEntry.Id,
            QueueNumber: queueNumber,
            QueueNumberFormatted: queueNumber.ToString("D3", CultureInfo.InvariantCulture),
            QueueStatus: queueEntry.Status ?? "WAITING",
            PresenceStatus: queueEntry.PresenceStatus ?? "PRESENT",
            ArrivalClass: queueEntry.ArrivalClass ?? "ON_TIME_OR_GRACE",
            Priority: effectivePriority,
            ManualPriority: queueEntry.ManualPriority ?? ClinicalQueueRules.PriorityNormal,
            AppointmentId: appointment.Id,
            AppointmentCode: appointmentCode,
            AppointmentStatus: appointment.Status ?? AppointmentBookingRules.CheckedInStatus,
            VisitStatus: visit.Status ?? "WAITING",
            VisitType: "SCHEDULED",
            PatientId: patient.Id,
            PatientCode: patientCode,
            PatientName: patient.FullName,
            HospitalId: effectiveHospitalId,
            DepartmentId: department?.Id ?? room?.DepartmentId,
            DepartmentName: departmentName,
            RoomId: assignedRoomId,
            RoomCode: room?.RoomCode,
            RoomName: roomName,
            RoomLocation: room?.Location,
            DoctorId: assignedDoctorId,
            DoctorCode: doctor?.DoctorCode,
            DoctorName: doctorName,
            ServicePointId: effectiveSpId,
            ServicePointName: servicePointName,
            ReasonForVisit: resolvedReason,
            BloodPressure: normalizedBloodPressure,
            HeartRate: request.HeartRate,
            Temperature: request.Temperature,
            IsIdentityVerified: true,
            IsInsuranceVerified: request.IsInsuranceVerified == true,
            WaitingCountBefore: waitingCountBefore,
            WaitingAheadCount: waitingCountBefore,
            EstimatedWaitMinutes: estimatedWaitMinutes,
            CheckedInAt: nowLocal.ToString("HH:mm", CultureInfo.InvariantCulture),
            CheckedInAtIso: nowLocal,
            CheckedInByStaffId: actor.ActorStaffId,
            CheckedInByStaffName: actor.ActorStaffName,
            NextStepInstructions: instructions);
    }

    public async Task<CheckInScheduledPatientResponseDto> GetCheckInResultByAppointmentAsync(
        long actorUserId,
        string appointmentIdOrCode,
        CancellationToken ct = default)
    {
        if (actorUserId <= 0)
        {
            throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");
        }

        var (trimmedIdentifier, parsedId, parsedCode) = ParseAppointmentIdentifier(appointmentIdOrCode);

        if (parsedCode is not null && parsedCode.StartsWith("VIS-", StringComparison.OrdinalIgnoreCase))
        {
            return await GetCheckInResultByVisitAsync(actorUserId, appointmentIdOrCode, ct);
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var nowLocal = AppointmentBookingRules.GetVietnamNow(_timeProvider);

        var actor = await EnsureCheckInActorAccessAsync(actorUserId, nowUtc, ct);

        var appointment = await LoadAppointmentAsync(parsedId, parsedCode, ct)
            ?? throw new NotFoundException("Appointment", trimmedIdentifier);

        await EnsureAppointmentHospitalScopeAsync(actor, appointment, ct);

        var normalizedApptStatus = AppointmentBookingRules.NormalizeStatusKey(appointment.Status);
        if (normalizedApptStatus == AppointmentBookingRules.CancelledStatus)
        {
            throw new ConflictException("Lịch hẹn đã bị hủy, không có kết quả check-in.");
        }

        if (normalizedApptStatus == AppointmentBookingRules.NoShowStatus)
        {
            throw new ConflictException("Lịch hẹn đã được ghi nhận vắng mặt (NO_SHOW), không có kết quả check-in.");
        }

        var visit = await _db.Visits
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.AppointmentId == appointment.Id, ct);

        if (visit is null)
        {
            throw new ConflictException("Lịch hẹn chưa được check-in, chưa có kết quả tiếp nhận.");
        }

        return await BuildCheckInResultAsync(actor, visit, appointment, nowUtc, nowLocal, ct);
    }

    public async Task<CheckInScheduledPatientResponseDto> GetCheckInResultByVisitAsync(
        long actorUserId,
        string visitIdOrCode,
        CancellationToken ct = default)
    {
        if (actorUserId <= 0)
        {
            throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");
        }

        var (trimmedIdentifier, parsedId, parsedCode) = ParseVisitIdentifier(visitIdOrCode);

        if (parsedCode is not null && parsedCode.StartsWith("APT-", StringComparison.OrdinalIgnoreCase))
        {
            return await GetCheckInResultByAppointmentAsync(actorUserId, visitIdOrCode, ct);
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var nowLocal = AppointmentBookingRules.GetVietnamNow(_timeProvider);

        var actor = await EnsureCheckInActorAccessAsync(actorUserId, nowUtc, ct);

        Visit? visit;
        if (parsedId.HasValue)
        {
            visit = await _db.Visits
                .AsNoTracking()
                .FirstOrDefaultAsync(v => v.Id == parsedId.Value, ct);

            if (visit is null)
            {
                var fallbackAppointment = await LoadAppointmentAsync(parsedId, null, ct);
                if (fallbackAppointment is not null)
                {
                    await EnsureAppointmentHospitalScopeAsync(actor, fallbackAppointment, ct);

                    var apptStatus = AppointmentBookingRules.NormalizeStatusKey(fallbackAppointment.Status);
                    if (apptStatus == AppointmentBookingRules.CancelledStatus)
                    {
                        throw new ConflictException("Lịch hẹn đã bị hủy, không có kết quả check-in.");
                    }

                    if (apptStatus == AppointmentBookingRules.NoShowStatus)
                    {
                        throw new ConflictException("Lịch hẹn đã được ghi nhận vắng mặt (NO_SHOW), không có kết quả check-in.");
                    }

                    visit = await _db.Visits
                        .AsNoTracking()
                        .FirstOrDefaultAsync(v => v.AppointmentId == fallbackAppointment.Id, ct)
                        ?? throw new ConflictException("Lịch hẹn chưa được check-in, chưa có kết quả tiếp nhận.");

                    return await BuildCheckInResultAsync(actor, visit, fallbackAppointment, nowUtc, nowLocal, ct);
                }

                throw new NotFoundException("Visit", trimmedIdentifier);
            }
        }
        else
        {
            visit = await _db.Visits
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    v => v.VisitCode != null && v.VisitCode.ToUpper() == parsedCode,
                    ct)
                ?? throw new NotFoundException("Visit", trimmedIdentifier);
        }

        var appointment = visit.AppointmentId.HasValue
            ? await LoadAppointmentAsync(visit.AppointmentId.Value, null, ct)
            : null;

        return await BuildCheckInResultAsync(actor, visit, appointment, nowUtc, nowLocal, ct);
    }

    private static (string Trimmed, long? Id, string? Code) ParseAppointmentIdentifier(string? idOrCode)
    {
        if (string.IsNullOrWhiteSpace(idOrCode))
        {
            throw new ValidationException("Mã lịch hẹn không hợp lệ.");
        }

        var trimmed = idOrCode.Trim();
        if (trimmed.StartsWith('#'))
        {
            trimmed = trimmed[1..].TrimStart();
        }

        if (string.IsNullOrEmpty(trimmed) ||
            trimmed.Length > MaxAppointmentIdentifierLength ||
            trimmed[0] is '-' or '_' ||
            !trimmed.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_'))
        {
            throw new ValidationException("Mã lịch hẹn không hợp lệ.");
        }

        if (trimmed.All(char.IsAsciiDigit))
        {
            if (!long.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
            {
                throw new ValidationException("Mã lịch hẹn không hợp lệ.");
            }

            return (trimmed, id, null);
        }

        return (trimmed, null, trimmed.ToUpperInvariant());
    }

    private static (string Trimmed, long? Id, string? Code) ParseVisitIdentifier(string? idOrCode)
    {
        if (string.IsNullOrWhiteSpace(idOrCode))
        {
            throw new ValidationException("Mã lượt khám không hợp lệ.");
        }

        var trimmed = idOrCode.Trim();
        if (trimmed.StartsWith('#'))
        {
            trimmed = trimmed[1..].TrimStart();
        }

        if (string.IsNullOrEmpty(trimmed) ||
            trimmed.Length > MaxAppointmentIdentifierLength ||
            trimmed[0] is '-' or '_' ||
            !trimmed.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_'))
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

    private sealed record CheckInActorContext(
        bool IsSystemAdmin,
        long? ActorHospitalId,
        long? ActorStaffId,
        string? ActorStaffName);

    private async Task<CheckInActorContext> EnsureCheckInActorAccessAsync(
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
            throw new ForbiddenException(
                "Người dùng không có quyền thực hiện check-in lịch hẹn tại quầy tiếp đón.");
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

                if (isReception && !isHospitalAdmin)
                {
                    var hasCheckInPermission = permissionCodes.Any(c =>
                        string.Equals(c, PatientCheckInPermissionCode, StringComparison.OrdinalIgnoreCase));
                    if (!hasCheckInPermission)
                    {
                        throw new ForbiddenException(
                            "Vai trò tiếp đón chưa được cấp quyền thực hiện check-in bệnh nhân (PATIENT_CHECKIN).");
                    }
                }
                else if (isHospitalAdmin)
                {
                    var hasAllowedPermission = permissionCodes.Any(c =>
                        HospitalAdminAllowedPermissions.Any(allowed =>
                            string.Equals(c, allowed, StringComparison.OrdinalIgnoreCase)));
                    if (!hasAllowedPermission)
                    {
                        throw new ForbiddenException(
                            "Vai trò hiện tại chưa được cấp quyền tiếp nhận bệnh nhân (PATIENT_CHECKIN).");
                    }
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

        return new CheckInActorContext(
            isSystemAdmin,
            actorStaff?.HospitalId,
            actorStaff?.Id,
            actorStaff?.FullName);
    }

    private async Task<Appointment?> LoadAppointmentAsync(
        long? parsedId,
        string? parsedCode,
        CancellationToken ct)
    {
        var query = _db.Appointments
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
            .Include(a => a.Slot)
                .ThenInclude(s => s!.Doctor)
                    .ThenInclude(d => d!.Specialty);

        if (parsedId.HasValue)
        {
            return await query.FirstOrDefaultAsync(a => a.Id == parsedId.Value, ct);
        }

        return await query.FirstOrDefaultAsync(
            a => a.AppointmentCode != null && a.AppointmentCode.ToUpper() == parsedCode,
            ct);
    }

    private async Task<QueueServicePoint?> ResolveQueueServicePointAsync(
        long? roomId,
        long? doctorId,
        long? hospitalId,
        CancellationToken ct)
    {
        if (!roomId.HasValue && !doctorId.HasValue)
        {
            return null;
        }

        try
        {
            var candidates = await _db.QueueServicePoints
                .AsNoTracking()
                .Where(sp =>
                    sp.IsActive != false &&
                    (!hospitalId.HasValue || sp.HospitalId == null || sp.HospitalId == hospitalId.Value) &&
                    ((roomId.HasValue && sp.RoomId == roomId.Value) ||
                     (doctorId.HasValue && sp.DoctorId == doctorId.Value)))
                .ToListAsync(ct);

            if (roomId.HasValue && doctorId.HasValue)
            {
                var exact = candidates.FirstOrDefault(
                    sp => sp.RoomId == roomId.Value && sp.DoctorId == doctorId.Value);
                if (exact is not null)
                {
                    return exact;
                }
            }

            return (roomId.HasValue ? candidates.FirstOrDefault(sp => sp.RoomId == roomId.Value) : null)
                ?? (doctorId.HasValue ? candidates.FirstOrDefault(sp => sp.DoctorId == doctorId.Value) : null);
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

    private async Task<QueueEntry?> FindExistingAppointmentQueueEntryAsync(
        long appointmentId,
        long visitId,
        CancellationToken ct)
    {
        var entries = await _db.QueueEntries
            .Where(q =>
                (q.AppointmentId == appointmentId || q.VisitId == visitId) &&
                (q.QueueType == null ||
                 q.QueueType.ToUpper() == "EXAMINATION" ||
                 q.QueueType.ToUpper() == "RETURN_CONSULTATION") &&
                (q.Status == null || q.Status.ToUpper() != "CANCELLED"))
            .OrderByDescending(q => q.Id)
            .ToListAsync(ct);

        return entries.FirstOrDefault(q => q.AppointmentId == appointmentId)
            ?? entries.FirstOrDefault();
    }

    private async Task<QueueEntry> ActivateOrCreateExaminationQueueEntryAsync(
        Visit visit,
        QueueEntry? existingEntry,
        Appointment appointment,
        ExaminationSlot slot,
        QueueServicePoint? servicePoint,
        string normalizedPriority,
        DateOnly today,
        DateTime nowLocal,
        int graceMinutes,
        CancellationToken ct)
    {
        var effectiveSpId = servicePoint?.Id ?? existingEntry?.ServicePointId;

        if (existingEntry is not null)
        {
            if (existingEntry.QueueNumber is null or <= 0)
            {
                existingEntry.QueueNumber = await AllocateNextQueueNumberAsync(effectiveSpId, today, ct);
            }

            existingEntry.VisitId = visit.Id;
            existingEntry.AppointmentId ??= appointment.Id;
            existingEntry.QueueType = ClinicalQueueRules.QueueTypeExamination;
            existingEntry.ServicePointId ??= servicePoint?.Id;
            existingEntry.CurrentServicePointId = servicePoint?.Id ?? existingEntry.ServicePointId;
            existingEntry.QueueDate ??= today;
            existingEntry.ScheduledAt ??= slot.StartsAt;
            existingEntry.Status = "WAITING";
            existingEntry.PresenceStatus = "PRESENT";
            existingEntry.ArrivedAt = nowLocal;
            existingEntry.ReadyAt = nowLocal;
            existingEntry.ArrivalClass = QueueArrivalClassifier.Classify(
                true,
                existingEntry.ScheduledAt ?? slot.StartsAt,
                nowLocal,
                graceMinutes,
                "ON_TIME_OR_GRACE");
            existingEntry.ManualPriority = normalizedPriority;
            existingEntry.StateVersion = (existingEntry.StateVersion is > 0 ? existingEntry.StateVersion.Value : 1) + 1;

            return existingEntry;
        }

        var nextQueueNumber = await AllocateNextQueueNumberAsync(effectiveSpId, today, ct);

        var createdEntry = new QueueEntry
        {
            VisitId = visit.Id,
            AppointmentId = appointment.Id,
            ServicePointId = servicePoint?.Id,
            CurrentServicePointId = servicePoint?.Id,
            QueueDate = today,
            QueueNumber = nextQueueNumber,
            QueueType = ClinicalQueueRules.QueueTypeExamination,
            Status = "WAITING",
            ScheduledAt = slot.StartsAt,
            PresenceStatus = "PRESENT",
            ArrivedAt = nowLocal,
            ArrivalClass = QueueArrivalClassifier.Classify(
                true,
                slot.StartsAt,
                nowLocal,
                graceMinutes,
                "ON_TIME_OR_GRACE"),
            ManualPriority = normalizedPriority,
            StateVersion = 1,
            ReadyAt = nowLocal,
        };

        _db.QueueEntries.Add(createdEntry);
        return createdEntry;
    }

    private async Task<int> AllocateNextQueueNumberAsync(
        long? effectiveSpId,
        DateOnly today,
        CancellationToken ct)
    {
        var existingMaxNumber = await _db.QueueEntries
            .AsNoTracking()
            .Where(q =>
                q.QueueDate == today &&
                (q.QueueType == null ||
                 q.QueueType.ToUpper() == "EXAMINATION" ||
                 q.QueueType.ToUpper() == "RETURN_CONSULTATION") &&
                (effectiveSpId == null ||
                 q.ServicePointId == effectiveSpId ||
                 q.CurrentServicePointId == effectiveSpId))
            .Select(q => q.QueueNumber)
            .MaxAsync(ct);

        return (existingMaxNumber ?? 0) + 1;
    }

    private async Task<int> CountWaitingBeforeAsync(
        long currentQueueEntryId,
        long? effectiveSpId,
        DateOnly today,
        int queueNumber,
        CancellationToken ct)
    {
        return await _db.QueueEntries
            .AsNoTracking()
            .CountAsync(q =>
                q.Id != currentQueueEntryId &&
                q.QueueDate == today &&
                (effectiveSpId == null ||
                 q.ServicePointId == effectiveSpId ||
                 q.CurrentServicePointId == effectiveSpId) &&
                (q.QueueType == null ||
                 q.QueueType.ToUpper() == "EXAMINATION" ||
                 q.QueueType.ToUpper() == "RETURN_CONSULTATION") &&
                q.QueueNumber != null &&
                q.QueueNumber < queueNumber &&
                q.Status != null &&
                (q.Status.ToUpper() == "WAITING" ||
                 q.Status.ToUpper() == "READY" ||
                 q.Status.ToUpper() == "RESERVED" ||
                 q.Status.ToUpper() == "CALLED" ||
                 q.Status.ToUpper() == "IN_PROGRESS"),
                ct);
    }

    private async Task EnsureAppointmentHospitalScopeAsync(
        CheckInActorContext actor,
        Appointment appointment,
        CancellationToken ct)
    {
        if (actor.IsSystemAdmin || !actor.ActorHospitalId.HasValue)
        {
            return;
        }

        var slot = appointment.Slot ?? (appointment.SlotId.HasValue
            ? await _db.ExaminationSlots
                .AsNoTracking()
                .Include(s => s.Service)
                .Include(s => s.Room)
                    .ThenInclude(r => r!.Department)
                .Include(s => s.Doctor)
                    .ThenInclude(d => d!.Staff)
                .FirstOrDefaultAsync(s => s.Id == appointment.SlotId.Value, ct)
            : null);

        var appointmentHospitalId = slot?.Service?.HospitalId
            ?? slot?.Room?.HospitalId
            ?? slot?.Room?.Department?.HospitalId
            ?? slot?.Doctor?.HospitalId
            ?? slot?.Doctor?.Staff?.HospitalId;

        if (appointmentHospitalId.HasValue && appointmentHospitalId.Value != actor.ActorHospitalId.Value)
        {
            throw new ForbiddenException("Bạn không có quyền truy cập kết quả check-in của cơ sở y tế khác.");
        }
    }

    private async Task<CheckInScheduledPatientResponseDto> BuildCheckInResultAsync(
        CheckInActorContext actor,
        Visit visit,
        Appointment? appointment,
        DateTime nowUtc,
        DateTime nowLocal,
        CancellationToken ct)
    {
        var slot = appointment?.Slot ?? (appointment?.SlotId.HasValue == true
            ? await _db.ExaminationSlots
                .AsNoTracking()
                .Include(s => s.Service)
                    .ThenInclude(sv => sv!.Department)
                .Include(s => s.Service)
                    .ThenInclude(sv => sv!.Specialty)
                .Include(s => s.Room)
                    .ThenInclude(r => r!.Department)
                .Include(s => s.Doctor)
                    .ThenInclude(d => d!.Staff)
                .Include(s => s.Doctor)
                    .ThenInclude(d => d!.Specialty)
                .FirstOrDefaultAsync(s => s.Id == appointment.SlotId.Value, ct)
            : null);

        var targetDoctorId = visit.AssignedDoctorId ?? slot?.DoctorId;
        Doctor? doctor = null;
        if (targetDoctorId.HasValue)
        {
            doctor = (slot?.Doctor is not null && slot.Doctor.Id == targetDoctorId.Value)
                ? slot.Doctor
                : await _db.Doctors
                    .AsNoTracking()
                    .Include(d => d.Staff)
                    .Include(d => d.Specialty)
                    .FirstOrDefaultAsync(d => d.Id == targetDoctorId.Value, ct);
        }

        var targetRoomId = visit.ExaminationRoomId ?? slot?.RoomId;
        HospitalRoom? room = null;
        if (targetRoomId.HasValue)
        {
            room = (slot?.Room is not null && slot.Room.Id == targetRoomId.Value)
                ? slot.Room
                : await _db.HospitalRooms
                    .AsNoTracking()
                    .Include(r => r.Department)
                    .FirstOrDefaultAsync(r => r.Id == targetRoomId.Value, ct);
        }

        Staff? checkedInStaff = null;
        if (visit.CheckedInByStaffId.HasValue)
        {
            checkedInStaff = await _db.Staff
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == visit.CheckedInByStaffId.Value, ct);
        }

        var walkInMetadata = visit.AppointmentId.HasValue
            ? null
            : await CreateWalkInVisitService.LoadWalkInAuditMetadataStaticAsync(_db, visit.Id, ct);

        var associatedHospitalIds = new long?[]
        {
            walkInMetadata?.HospitalId,
            room?.HospitalId,
            room?.Department?.HospitalId,
            doctor?.HospitalId,
            doctor?.Staff?.HospitalId,
            checkedInStaff?.HospitalId,
            slot?.Service?.HospitalId,
            slot?.Room?.HospitalId,
            slot?.Doctor?.HospitalId,
        };

        if (!actor.IsSystemAdmin &&
            actor.ActorHospitalId.HasValue &&
            associatedHospitalIds.Any(hId => hId.HasValue && hId.Value != actor.ActorHospitalId.Value))
        {
            throw new ForbiddenException("Bạn không có quyền truy cập kết quả check-in của cơ sở y tế khác.");
        }

        var effectiveHospitalId = associatedHospitalIds.FirstOrDefault(hId => hId.HasValue)
            ?? actor.ActorHospitalId
            ?? 1L;

        if (appointment is not null)
        {
            var normalizedApptStatus = AppointmentBookingRules.NormalizeStatusKey(appointment.Status);
            if (normalizedApptStatus == AppointmentBookingRules.CancelledStatus)
            {
                throw new ConflictException("Lịch hẹn đã bị hủy, không có kết quả check-in.");
            }

            if (normalizedApptStatus == AppointmentBookingRules.NoShowStatus)
            {
                throw new ConflictException("Lịch hẹn đã được ghi nhận vắng mặt (NO_SHOW), không có kết quả check-in.");
            }
        }

        var normalizedVisitStatus = AppointmentBookingRules.NormalizeStatusKey(visit.Status);
        if (string.IsNullOrEmpty(normalizedVisitStatus))
        {
            normalizedVisitStatus = targetRoomId.HasValue && targetDoctorId.HasValue ? "WAITING" : "CHECKED_IN";
        }

        if (normalizedVisitStatus is "CANCELLED" or "NO_SHOW")
        {
            throw new ConflictException("Lượt khám đã bị hủy, không thể xem kết quả check-in.");
        }

        var patientId = visit.PatientId ?? appointment?.PatientId;
        var patient = appointment?.Patient ?? (patientId.HasValue
            ? await _db.Patients
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == patientId.Value, ct)
            : null)
            ?? throw new NotFoundException("Patient", patientId ?? 0);

        var candidateQueues = await _db.QueueEntries
            .AsNoTracking()
            .Where(q =>
                (q.VisitId == visit.Id ||
                 (visit.AppointmentId.HasValue && q.AppointmentId == visit.AppointmentId.Value) ||
                 (appointment != null && q.AppointmentId == appointment.Id)) &&
                (q.QueueType == null ||
                 q.QueueType.ToUpper() == "EXAMINATION" ||
                 q.QueueType.ToUpper() == "RETURN_CONSULTATION"))
            .OrderByDescending(q => q.VisitId == visit.Id)
            .ThenByDescending(q => q.Id)
            .ToListAsync(ct);

        var queueEntry = candidateQueues.FirstOrDefault(q =>
            !string.Equals(q.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
            ?? candidateQueues.FirstOrDefault();

        if (queueEntry is not null &&
            string.Equals(queueEntry.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException("Hàng đợi khám của lượt khám đã bị hủy, không thể xem kết quả check-in.");
        }

        var visitDate = visit.CheckedInAt.HasValue
            ? DateOnly.FromDateTime(visit.CheckedInAt.Value)
            : (slot?.StartsAt.HasValue == true
                ? DateOnly.FromDateTime(slot.StartsAt.Value)
                : DateOnly.FromDateTime(nowLocal));

        var queueDate = queueEntry?.QueueDate ?? visitDate;

        var servicePoint = await ResolveQueueServicePointAsync(
            targetRoomId,
            targetDoctorId,
            effectiveHospitalId,
            ct);

        var effectiveSpId = servicePoint?.Id
            ?? queueEntry?.CurrentServicePointId
            ?? queueEntry?.ServicePointId;

        var queueNumber = queueEntry?.QueueNumber is > 0
            ? queueEntry.QueueNumber.Value
            : 1;

        var ticketNo = $"A{queueNumber:D3}";

        var queueStatus = !string.IsNullOrWhiteSpace(queueEntry?.Status)
            ? queueEntry.Status.Trim().ToUpperInvariant()
            : (normalizedVisitStatus == "COMPLETED" ? "COMPLETED" : "WAITING");

        var presenceStatus = !string.IsNullOrWhiteSpace(queueEntry?.PresenceStatus)
            ? queueEntry.PresenceStatus.Trim().ToUpperInvariant()
            : "PRESENT";

        var arrivalClass = !string.IsNullOrWhiteSpace(queueEntry?.ArrivalClass)
            ? queueEntry.ArrivalClass.Trim().ToUpperInvariant()
            : "ON_TIME_OR_GRACE";

        var manualPriority = !string.IsNullOrWhiteSpace(queueEntry?.ManualPriority)
            ? queueEntry.ManualPriority.Trim().ToUpperInvariant()
            : ClinicalQueueRules.PriorityNormal;

        var effectivePriority = ClinicalQueueRules.ResolveEffectivePriority(
            manualPriority,
            patient.DateOfBirth,
            queueDate);

        var waitingCountBefore = 0;
        if (normalizedVisitStatus is not ("COMPLETED" or "IN_PROGRESS" or "IN_CONSULTATION") &&
            queueStatus is not ("COMPLETED" or "IN_PROGRESS" or "IN_CONSULTATION" or "CALLED"))
        {
            waitingCountBefore = await CountWaitingBeforeAsync(
                queueEntry?.Id ?? 0,
                effectiveSpId,
                queueDate,
                queueNumber,
                ct);
        }

        var policySet = await QueueOrderingPolicyLoader.LoadAsync(_db, effectiveHospitalId, nowUtc, ct);
        var orderingPolicy = policySet.ForServicePoint(effectiveSpId);
        var durationPerPatient = orderingPolicy.ExpectedDurationMinutes is > 0
            ? orderingPolicy.ExpectedDurationMinutes.Value
            : DefaultWaitMinutesPerPatient;
        var estimatedWaitMinutes = waitingCountBefore * durationPerPatient;

        var department = room?.Department ?? slot?.Service?.Department;
        if (department is null)
        {
            var fallbackDeptId = room?.DepartmentId
                ?? slot?.Service?.DepartmentId
                ?? walkInMetadata?.DepartmentId
                ?? doctor?.Staff?.DepartmentId;
            if (fallbackDeptId.HasValue)
            {
                department = await _db.Departments
                    .AsNoTracking()
                    .FirstOrDefaultAsync(d => d.Id == fallbackDeptId.Value, ct);
            }
        }

        var departmentName = !string.IsNullOrWhiteSpace(department?.Name)
            ? department.Name
            : "Khoa Khám bệnh";

        var roomName = !string.IsNullOrWhiteSpace(room?.Name)
            ? room.Name
            : "Phòng khám ngoại trú";

        var roomDisplay = !string.IsNullOrWhiteSpace(room?.RoomCode)
            ? $"{roomName} ({room.RoomCode})"
            : roomName;

        var locationSuffix = !string.IsNullOrWhiteSpace(room?.Location)
            ? $" - {room.Location}"
            : string.Empty;

        var doctorName = !string.IsNullOrWhiteSpace(doctor?.Staff?.FullName)
            ? doctor.Staff.FullName
            : (!string.IsNullOrWhiteSpace(doctor?.DoctorCode) ? $"Bác sĩ {doctor.DoctorCode}" : "Bác sĩ phụ trách");

        var servicePointName = servicePoint?.Name;
        if (string.IsNullOrWhiteSpace(servicePointName) && effectiveSpId.HasValue)
        {
            servicePointName = await TryGetServicePointNameAsync(effectiveSpId.Value, ct);
        }

        servicePointName ??= roomName;

        var instructions = new List<string>
        {
            $"Di chuyển đến trước {roomDisplay} ({departmentName}{locationSuffix}).",
            "Theo dõi màn hình điện tử và lắng nghe loa gọi số thứ tự tiếp nhận.",
            "Chuẩn bị sẵn thẻ CCCD/BHYT bản gốc trước khi vào phòng khám.",
        };

        var visitCode = !string.IsNullOrWhiteSpace(visit.VisitCode)
            ? visit.VisitCode
            : $"VIS-{queueDate:yyyyMM}-{visit.Id:D4}";

        var ticketCode = $"TKT-{visitCode}";

        var appointmentId = visit.AppointmentId ?? appointment?.Id ?? 0L;
        var appointmentCode = !string.IsNullOrWhiteSpace(appointment?.AppointmentCode)
            ? appointment.AppointmentCode
            : (appointmentId > 0 ? $"APT-{queueDate:yyyyMM}-{appointmentId:D4}" : string.Empty);

        var appointmentStatus = !string.IsNullOrWhiteSpace(appointment?.Status)
            ? appointment.Status.Trim().ToUpperInvariant()
            : AppointmentBookingRules.CheckedInStatus;

        var visitType = (visit.AppointmentId.HasValue || appointment is not null)
            ? "SCHEDULED"
            : "WALK_IN";

        var patientCode = !string.IsNullOrWhiteSpace(patient.PatientCode)
            ? patient.PatientCode
            : $"PAT-{patient.Id:D4}";

        var resolvedReason = !string.IsNullOrWhiteSpace(visit.ReasonForVisit)
            ? visit.ReasonForVisit.Trim()
            : (!string.IsNullOrWhiteSpace(appointment?.ReasonForVisit)
                ? appointment.ReasonForVisit.Trim()
                : (!string.IsNullOrWhiteSpace(appointment?.ReceptionNote)
                    ? appointment.ReceptionNote.Trim()
                    : "Khám bệnh ngoại trú"));

        var (bloodPressure, heartRate, temperature, isIdentityVerified, isInsuranceVerified) =
            await LoadCheckInAuditMetadataAsync(visit.Id, ct);

        var checkedInAtValue = visit.CheckedInAt ?? nowLocal;
        var checkedInByStaffId = visit.CheckedInByStaffId ?? checkedInStaff?.Id ?? actor.ActorStaffId;
        var checkedInByStaffName = checkedInStaff?.FullName ?? actor.ActorStaffName;

        return new CheckInScheduledPatientResponseDto(
            VisitId: visit.Id,
            VisitCode: visitCode,
            TicketCode: ticketCode,
            TicketNo: ticketNo,
            QueueEntryId: queueEntry?.Id ?? 0L,
            QueueNumber: queueNumber,
            QueueNumberFormatted: queueNumber.ToString("D3", CultureInfo.InvariantCulture),
            QueueStatus: queueStatus,
            PresenceStatus: presenceStatus,
            ArrivalClass: arrivalClass,
            Priority: effectivePriority,
            ManualPriority: manualPriority,
            AppointmentId: appointmentId,
            AppointmentCode: appointmentCode,
            AppointmentStatus: appointmentStatus,
            VisitStatus: normalizedVisitStatus,
            VisitType: visitType,
            PatientId: patient.Id,
            PatientCode: patientCode,
            PatientName: patient.FullName,
            HospitalId: effectiveHospitalId,
            DepartmentId: department?.Id ?? room?.DepartmentId,
            DepartmentName: departmentName,
            RoomId: targetRoomId,
            RoomCode: room?.RoomCode,
            RoomName: roomName,
            RoomLocation: room?.Location,
            DoctorId: targetDoctorId,
            DoctorCode: doctor?.DoctorCode,
            DoctorName: doctorName,
            ServicePointId: effectiveSpId,
            ServicePointName: servicePointName,
            ReasonForVisit: resolvedReason,
            BloodPressure: bloodPressure,
            HeartRate: heartRate,
            Temperature: temperature,
            IsIdentityVerified: isIdentityVerified,
            IsInsuranceVerified: isInsuranceVerified,
            WaitingCountBefore: waitingCountBefore,
            WaitingAheadCount: waitingCountBefore,
            EstimatedWaitMinutes: estimatedWaitMinutes,
            CheckedInAt: checkedInAtValue.ToString("HH:mm", CultureInfo.InvariantCulture),
            CheckedInAtIso: checkedInAtValue,
            CheckedInByStaffId: checkedInByStaffId,
            CheckedInByStaffName: checkedInByStaffName,
            NextStepInstructions: instructions);
    }

    private async Task<(string? BloodPressure, int? HeartRate, double? Temperature, bool IsIdentityVerified, bool IsInsuranceVerified)> LoadCheckInAuditMetadataAsync(
        long visitId,
        CancellationToken ct)
    {
        var visitIdText = visitId.ToString(CultureInfo.InvariantCulture);
        var auditLog = await _db.AuditLogs
            .AsNoTracking()
            .Where(a =>
                a.EntityType != null &&
                a.EntityType.ToUpper() == VisitEntityType &&
                a.EntityId == visitIdText &&
                a.Action != null &&
                (a.Action.ToUpper() == PatientCheckedInAction ||
                 a.Action.ToUpper() == CreateWalkInVisitService.WalkInVisitCreatedAction))
            .OrderByDescending(a => a.OccurredAt)
            .ThenByDescending(a => a.Id)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrWhiteSpace(auditLog?.ChangeData))
        {
            return (null, null, null, true, false);
        }

        try
        {
            using var doc = JsonDocument.Parse(auditLog.ChangeData);
            var root = doc.RootElement;

            string? bloodPressure = null;
            int? heartRate = null;
            double? temperature = null;

            if (root.TryGetProperty("vitals", out var vitalsEl) && vitalsEl.ValueKind == JsonValueKind.Object)
            {
                if (vitalsEl.TryGetProperty("bloodPressure", out var bpEl) && bpEl.ValueKind == JsonValueKind.String)
                {
                    bloodPressure = bpEl.GetString();
                }

                if (vitalsEl.TryGetProperty("heartRate", out var hrEl) &&
                    hrEl.ValueKind == JsonValueKind.Number &&
                    hrEl.TryGetInt32(out var hrValue))
                {
                    heartRate = hrValue;
                }

                if (vitalsEl.TryGetProperty("temperature", out var tempEl) &&
                    tempEl.ValueKind == JsonValueKind.Number &&
                    tempEl.TryGetDouble(out var tempValue))
                {
                    temperature = tempValue;
                }
            }

            var isIdentityVerified = true;
            if (root.TryGetProperty("isIdentityVerified", out var idVerEl) &&
                idVerEl.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                isIdentityVerified = idVerEl.GetBoolean();
            }

            var isInsuranceVerified = false;
            if (root.TryGetProperty("isInsuranceVerified", out var insVerEl) &&
                insVerEl.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                isInsuranceVerified = insVerEl.GetBoolean();
            }

            return (bloodPressure, heartRate, temperature, isIdentityVerified, isInsuranceVerified);
        }
        catch (JsonException)
        {
            return (null, null, null, true, false);
        }
    }
}
