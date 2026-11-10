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

public sealed class CreateWalkInVisitService : ICreateWalkInVisitService
{
    public const string WalkInVisitCreatedAction = "WALK_IN_VISIT_CREATED";
    public const string PatientCheckInPermissionCode = "PATIENT_CHECKIN";

    private const string VisitEntityType = "VISIT";
    private const int MaxIdentifierLength = 40;
    private const int MinReasonLength = 3;
    private const int MaxReasonLength = 500;
    private const int MaxNotesLength = 500;
    private const int MaxSymptomCount = 20;
    private const int MaxSymptomLength = 100;
    private const decimal DefaultServicePrice = 200_000m;
    private const int DefaultWaitMinutesPerPatient = 15;

    public static readonly IReadOnlyList<string> DefaultSymptomOptions =
    [
        "Đau thượng vị",
        "Ợ hơi/ợ chua",
        "Đầy bụng khó tiêu",
        "Buồn nôn",
        "Đi ngoài phân đen",
        "Đau tức ngực",
        "Chóng mặt",
        "Sốt nhẹ",
    ];

    private static readonly Regex BloodPressureRegex = new(
        @"^\d{2,3}/\d{2,3}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly string[] ReceptionAllowedPermissions =
    [
        PatientCheckInPermissionCode,
        "VISIT_ASSIGN",
    ];

    private static readonly string[] HospitalAdminAllowedPermissions =
    [
        PatientCheckInPermissionCode,
        "VISIT_ASSIGN",
        "QUEUE_MANAGE",
        "HOSPITAL_CONFIG",
    ];

    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;

    public CreateWalkInVisitService(
        IApplicationDbContext db,
        TimeProvider? timeProvider = null)
    {
        _db = db;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<CreateWalkInVisitResponseDto> CreateWalkInVisitAsync(
        long actorUserId,
        CreateWalkInVisitRequest request,
        string? routePatientIdOrCode = null,
        string? ipAddress = null,
        CancellationToken ct = default)
    {
        if (actorUserId <= 0)
        {
            throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");
        }

        if (request is null)
        {
            throw new ValidationException("Dữ liệu tạo lượt khám vãng lai không hợp lệ.");
        }

        var rawPatientIdentifier = FirstNonEmpty(routePatientIdOrCode, request.PatientId, request.PatientCode);
        if (string.IsNullOrWhiteSpace(rawPatientIdentifier))
        {
            throw new ValidationException("Bắt buộc chọn bệnh nhân để tạo lượt khám vãng lai.");
        }

        var parsedPatient = ParseIdentifier(rawPatientIdentifier, "Mã định danh bệnh nhân không hợp lệ.");

        var isIdentityVerified = request.IsIdentityVerified ?? request.IdentityVerified ?? true;
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

        var normalizedPriority = NormalizeWalkInPriorityOrThrow(
            FirstNonEmpty(request.Priority, request.ClinicalPriority));

        var normalizedPaymentMethod = NormalizePaymentMethodOrThrow(request.PaymentMethod);

        if (string.IsNullOrWhiteSpace(request.ReasonForVisit))
        {
            throw new ValidationException("Vui lòng nhập lý do đến khám / triệu chứng lâm sàng sơ bộ.");
        }

        var resolvedReason = request.ReasonForVisit.Trim();
        if (resolvedReason.Length < MinReasonLength)
        {
            throw new ValidationException($"Lý do đến khám phải có ít nhất {MinReasonLength} ký tự.");
        }

        if (resolvedReason.Length > MaxReasonLength)
        {
            throw new ValidationException($"Lý do đến khám không được vượt quá {MaxReasonLength} ký tự.");
        }

        var normalizedSymptoms = NormalizeSymptomsOrThrow(request.Symptoms);

        string? trimmedNotes = null;
        if (!string.IsNullOrWhiteSpace(request.Notes))
        {
            trimmedNotes = request.Notes.Trim();
            if (trimmedNotes.Length > MaxNotesLength)
            {
                throw new ValidationException("Ghi chú tiếp nhận không được vượt quá 500 ký tự.");
            }
        }

        var rawDepartmentIdentifier = FirstNonEmpty(request.DepartmentId, request.DepartmentCode);
        var rawServiceIdentifier = FirstNonEmpty(request.ServiceId, request.ServiceCode);

        if (string.IsNullOrWhiteSpace(rawDepartmentIdentifier) && string.IsNullOrWhiteSpace(rawServiceIdentifier))
        {
            throw new ValidationException("Vui lòng chọn khoa tiếp nhận và dịch vụ khám yêu cầu.");
        }

        var parsedDepartment = !string.IsNullOrWhiteSpace(rawDepartmentIdentifier)
            ? ParseIdentifier(rawDepartmentIdentifier, "Mã định danh khoa tiếp nhận không hợp lệ.")
            : ((string Trimmed, long? Id, string? Code)?)null;

        var parsedService = !string.IsNullOrWhiteSpace(rawServiceIdentifier)
            ? ParseIdentifier(rawServiceIdentifier, "Mã định danh dịch vụ khám không hợp lệ.")
            : ((string Trimmed, long? Id, string? Code)?)null;

        var rawDoctorIdentifier = FirstNonEmpty(request.DoctorId, request.DoctorCode);
        var parsedDoctor = !string.IsNullOrWhiteSpace(rawDoctorIdentifier)
            ? ParseIdentifier(rawDoctorIdentifier, "Mã định danh bác sĩ phụ trách không hợp lệ.")
            : ((string Trimmed, long? Id, string? Code)?)null;

        var rawRoomIdentifier = FirstNonEmpty(request.ExaminationRoomId, request.RoomId, request.RoomCode);
        var parsedRoom = !string.IsNullOrWhiteSpace(rawRoomIdentifier)
            ? ParseIdentifier(rawRoomIdentifier, "Mã định danh buồng khám không hợp lệ.")
            : ((string Trimmed, long? Id, string? Code)?)null;

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var nowLocal = AppointmentBookingRules.GetVietnamNow(_timeProvider);
        var today = DateOnly.FromDateTime(nowLocal);

        var actor = await EnsureWalkInActorAccessAsync(actorUserId, nowUtc, ct);

        var patient = await LoadPatientAsync(parsedPatient, ct)
            ?? throw new NotFoundException("Patient", parsedPatient.Trimmed);

        if (!string.Equals(patient.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException(
                "Hồ sơ bệnh nhân hiện không ở trạng thái hoạt động, không thể tạo lượt khám vãng lai.");
        }

        var (department, service) = await ResolveDepartmentAndServiceAsync(
            actor,
            parsedDepartment,
            parsedService,
            ct);

        var effectiveHospitalId = service.HospitalId
            ?? department.HospitalId
            ?? actor.ActorHospitalId
            ?? 1L;

        var doctor = parsedDoctor.HasValue
            ? await ResolveAndValidateDoctorAsync(actor, parsedDoctor.Value, department, effectiveHospitalId, ct)
            : null;

        var room = parsedRoom.HasValue
            ? await ResolveAndValidateRoomAsync(actor, parsedRoom.Value, department, effectiveHospitalId, ct)
            : null;

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

        var sameDayAppointments = await _db.Appointments
            .AsNoTracking()
            .Include(a => a.Slot)
                .ThenInclude(s => s!.Service)
            .Include(a => a.Slot)
                .ThenInclude(s => s!.Room)
            .Include(a => a.Slot)
                .ThenInclude(s => s!.Doctor)
            .Where(a =>
                a.PatientId == patient.Id &&
                a.Slot != null &&
                a.Slot.StartsAt != null &&
                a.Slot.StartsAt >= dayStart &&
                a.Slot.StartsAt < dayEnd)
            .ToListAsync(ct);

        var hasActiveSameDayAppointmentInHospital = sameDayAppointments.Any(a =>
        {
            var normalizedApptStatus = AppointmentBookingRules.NormalizeStatusKey(a.Status);
            if (normalizedApptStatus is "CANCELLED" or "NO_SHOW" or "COMPLETED")
            {
                return false;
            }

            var apptHospitalId = a.Slot?.Service?.HospitalId
                ?? a.Slot?.Room?.HospitalId
                ?? a.Slot?.Doctor?.HospitalId;

            return !apptHospitalId.HasValue || apptHospitalId.Value == effectiveHospitalId;
        });

        if (hasActiveSameDayAppointmentInHospital)
        {
            throw new ConflictException(
                "Bệnh nhân đã có lịch hẹn khám trong ngày hôm nay tại cơ sở y tế này. Vui lòng thực hiện check-in theo lịch hẹn (UC24).");
        }

        var servicePoint = await ResolveQueueServicePointAsync(
            room?.Id,
            doctor?.Id,
            service.Id,
            department.Id,
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

        var servicePrice = await ResolveServicePriceAsync(service.Id, nowLocal, ct);
        var priceFormatted = FormatVndPrice(servicePrice);
        var paymentStatus = "PENDING";

        var policySet = await QueueOrderingPolicyLoader.LoadAsync(_db, effectiveHospitalId, nowUtc, ct);
        var orderingPolicy = policySet.ForServicePoint(servicePoint?.Id);

        await using var tx = await _db.BeginTransactionAsync(ct);

        var assignedDoctorId = doctor?.Id;
        var assignedRoomId = room?.Id;
        var visitStatus = assignedDoctorId.HasValue && assignedRoomId.HasValue ? "WAITING" : "CHECKED_IN";

        var visit = new Visit
        {
            PatientId = patient.Id,
            AppointmentId = null,
            AssignedDoctorId = assignedDoctorId,
            ExaminationRoomId = assignedRoomId,
            CheckedInByStaffId = actor.ActorStaffId,
            ReasonForVisit = resolvedReason,
            Status = visitStatus,
            CheckedInAt = nowLocal,
        };

        _db.Visits.Add(visit);
        await _db.SaveChangesAsync(ct);

        visit.VisitCode = $"VIS-{today:yyyyMM}-{visit.Id:D4}";

        var effectiveSpId = servicePoint?.Id;
        var queueNumber = await AllocateNextQueueNumberAsync(effectiveSpId, today, ct);

        var queueEntry = new QueueEntry
        {
            VisitId = visit.Id,
            AppointmentId = null,
            ServicePointId = effectiveSpId,
            CurrentServicePointId = effectiveSpId,
            QueueDate = today,
            QueueNumber = queueNumber,
            QueueType = ClinicalQueueRules.QueueTypeExamination,
            Status = "WAITING",
            ScheduledAt = null,
            PresenceStatus = "PRESENT",
            ArrivedAt = nowLocal,
            ArrivalClass = QueueArrivalClassifier.Classify(
                isScheduled: false,
                scheduledAt: null,
                arrivedAt: nowLocal,
                graceMinutes: orderingPolicy.GraceMinutes,
                fallback: "WALK_IN"),
            ManualPriority = normalizedPriority,
            StateVersion = 1,
            ReadyAt = nowLocal,
        };

        _db.QueueEntries.Add(queueEntry);

        TryAddWalkInPayment(visit.Id, servicePrice, paymentStatus);

        await _db.SaveChangesAsync(ct);

        var ticketNo = $"A{queueNumber:D3}";
        var effectivePriority = ClinicalQueueRules.ResolveEffectivePriority(
            queueEntry.ManualPriority,
            patient.DateOfBirth,
            today);
        var clinicalPriorityLabel = ClinicalQueueRules.IsElevated(effectivePriority) ? "PRIORITY" : "STANDARD";

        var parsedIp = !string.IsNullOrWhiteSpace(ipAddress) && IPAddress.TryParse(ipAddress, out var ip)
            ? ip
            : null;

        var changeData = JsonSerializer.Serialize(new
        {
            visitId = visit.Id,
            visitCode = visit.VisitCode,
            visitType = "WALK_IN",
            visitStatus = visit.Status,
            patientId = patient.Id,
            patientCode = patient.PatientCode,
            departmentId = department.Id,
            departmentCode = department.DepartmentCode,
            departmentName = department.Name,
            serviceId = service.Id,
            serviceCode = service.ServiceCode,
            serviceName = service.Name,
            servicePrice,
            priceFormatted,
            paymentMethod = normalizedPaymentMethod,
            paymentStatus,
            doctorId = assignedDoctorId,
            doctorCode = doctor?.DoctorCode,
            roomId = assignedRoomId,
            roomCode = room?.RoomCode,
            servicePointId = effectiveSpId,
            queueEntryId = queueEntry.Id,
            queueNumber,
            ticketNo,
            queueStatus = queueEntry.Status,
            presenceStatus = queueEntry.PresenceStatus,
            arrivalClass = queueEntry.ArrivalClass,
            manualPriority = queueEntry.ManualPriority,
            effectivePriority,
            clinicalPriority = clinicalPriorityLabel,
            reasonForVisit = resolvedReason,
            symptoms = normalizedSymptoms,
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
            Action = WalkInVisitCreatedAction,
            EntityType = VisitEntityType,
            EntityId = visit.Id.ToString(CultureInfo.InvariantCulture),
            Reason = resolvedReason,
            ChangeData = changeData,
            IpAddress = parsedIp,
            OccurredAt = nowUtc,
        });

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

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

        return await BuildWalkInResponseDtoAsync(
            visit,
            queueEntry,
            patient,
            department,
            service,
            doctor,
            room,
            servicePoint,
            effectiveHospitalId,
            servicePrice,
            priceFormatted,
            normalizedPaymentMethod,
            paymentStatus,
            resolvedReason,
            normalizedSymptoms,
            normalizedBloodPressure,
            request.HeartRate,
            request.Temperature,
            isIdentityVerified: true,
            isInsuranceVerified: request.IsInsuranceVerified == true,
            trimmedNotes,
            waitingCountBefore,
            estimatedWaitMinutes,
            nowLocal,
            actor.ActorStaffId,
            actor.ActorStaffName,
            ct);
    }

    public async Task<CreateWalkInVisitResponseDto> GetWalkInVisitByIdOrCodeAsync(
        long actorUserId,
        string visitIdOrCode,
        CancellationToken ct = default)
    {
        if (actorUserId <= 0)
        {
            throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");
        }

        var parsedVisit = ParseIdentifier(visitIdOrCode, "Mã định danh lượt khám không hợp lệ.");
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var nowLocal = AppointmentBookingRules.GetVietnamNow(_timeProvider);

        var actor = await EnsureWalkInActorAccessAsync(actorUserId, nowUtc, ct);

        var visit = parsedVisit.Id.HasValue
            ? await _db.Visits
                .AsNoTracking()
                .FirstOrDefaultAsync(v => v.Id == parsedVisit.Id.Value, ct)
            : await _db.Visits
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    v => v.VisitCode != null && v.VisitCode.ToUpper() == parsedVisit.Code,
                    ct);

        if (visit is null)
        {
            throw new NotFoundException("Visit", parsedVisit.Trimmed);
        }

        if (visit.AppointmentId.HasValue)
        {
            throw new ConflictException(
                "Lượt khám này được tạo từ lịch hẹn đặt trước (SCHEDULED), không phải lượt khám vãng lai (WALK_IN).");
        }

        var normalizedVisitStatus = AppointmentBookingRules.NormalizeStatusKey(visit.Status);
        if (normalizedVisitStatus is "CANCELLED" or "NO_SHOW")
        {
            throw new ConflictException("Lượt khám vãng lai đã bị hủy, không thể xem thông tin tiếp nhận.");
        }

        var auditMetadata = await LoadWalkInAuditMetadataAsync(visit.Id, ct);

        Doctor? doctor = null;
        var effectiveDoctorId = visit.AssignedDoctorId ?? auditMetadata.DoctorId;
        if (effectiveDoctorId.HasValue)
        {
            doctor = await _db.Doctors
                .AsNoTracking()
                .Include(d => d.Staff)
                .Include(d => d.Specialty)
                .FirstOrDefaultAsync(d => d.Id == effectiveDoctorId.Value, ct);
        }

        HospitalRoom? room = null;
        var effectiveRoomId = visit.ExaminationRoomId ?? auditMetadata.RoomId;
        if (effectiveRoomId.HasValue)
        {
            room = await _db.HospitalRooms
                .AsNoTracking()
                .Include(r => r.Department)
                .FirstOrDefaultAsync(r => r.Id == effectiveRoomId.Value, ct);
        }

        Staff? checkedInStaff = null;
        if (visit.CheckedInByStaffId.HasValue)
        {
            checkedInStaff = await _db.Staff
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == visit.CheckedInByStaffId.Value, ct);
        }

        Department? department = room?.Department;
        if (department is null)
        {
            var deptId = auditMetadata.DepartmentId ?? room?.DepartmentId ?? doctor?.Staff?.DepartmentId;
            if (deptId.HasValue)
            {
                department = await _db.Departments
                    .AsNoTracking()
                    .FirstOrDefaultAsync(d => d.Id == deptId.Value, ct);
            }
        }

        Service? service = null;
        if (auditMetadata.ServiceId.HasValue)
        {
            service = await _db.Services
                .AsNoTracking()
                .Include(s => s.Department)
                .FirstOrDefaultAsync(s => s.Id == auditMetadata.ServiceId.Value, ct);
            department ??= service?.Department;
        }

        var associatedHospitalIds = new long?[]
        {
            auditMetadata.HospitalId,
            service?.HospitalId,
            department?.HospitalId,
            room?.HospitalId,
            room?.Department?.HospitalId,
            doctor?.HospitalId,
            doctor?.Staff?.HospitalId,
            checkedInStaff?.HospitalId,
        };

        if (!actor.IsSystemAdmin &&
            actor.ActorHospitalId.HasValue &&
            associatedHospitalIds.Any(hId => hId.HasValue && hId.Value != actor.ActorHospitalId.Value))
        {
            throw new ForbiddenException("Bạn không có quyền truy cập lượt khám vãng lai thuộc cơ sở y tế khác.");
        }

        var effectiveHospitalId = associatedHospitalIds.FirstOrDefault(hId => hId.HasValue)
            ?? actor.ActorHospitalId
            ?? 1L;

        department ??= await _db.Departments
            .AsNoTracking()
            .Where(d => d.HospitalId == effectiveHospitalId && d.IsActive != false)
            .OrderBy(d => d.Id)
            .FirstOrDefaultAsync(ct)
            ?? new Department
            {
                Id = auditMetadata.DepartmentId ?? 1L,
                HospitalId = effectiveHospitalId,
                DepartmentCode = auditMetadata.DepartmentCode ?? "OPD",
                Name = auditMetadata.DepartmentName ?? "Khoa Khám bệnh",
                IsActive = true,
            };

        service ??= await _db.Services
            .AsNoTracking()
            .Where(s =>
                s.HospitalId == effectiveHospitalId &&
                s.DepartmentId == department.Id &&
                s.ServiceType.ToUpper() == "EXAMINATION" &&
                s.IsActive != false)
            .OrderBy(s => s.Id)
            .FirstOrDefaultAsync(ct)
            ?? new Service
            {
                Id = auditMetadata.ServiceId ?? 1L,
                HospitalId = effectiveHospitalId,
                DepartmentId = department.Id,
                ServiceCode = auditMetadata.ServiceCode ?? "EX-WALKIN",
                Name = auditMetadata.ServiceName ?? $"Khám {department.Name}",
                ServiceType = "EXAMINATION",
                IsActive = true,
            };

        if (!visit.PatientId.HasValue)
        {
            throw new NotFoundException("Patient", 0);
        }

        var patient = await _db.Patients
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == visit.PatientId.Value, ct)
            ?? throw new NotFoundException("Patient", visit.PatientId.Value);

        var candidateQueues = await _db.QueueEntries
            .AsNoTracking()
            .Where(q =>
                q.VisitId == visit.Id &&
                (q.QueueType == null ||
                 q.QueueType.ToUpper() == "EXAMINATION" ||
                 q.QueueType.ToUpper() == "RETURN_CONSULTATION"))
            .OrderByDescending(q => q.Id)
            .ToListAsync(ct);

        var queueEntry = candidateQueues.FirstOrDefault(q =>
            !string.Equals(q.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
            ?? candidateQueues.FirstOrDefault();

        if (queueEntry is not null &&
            string.Equals(queueEntry.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException("Hàng đợi khám của lượt khám vãng lai đã bị hủy.");
        }

        var visitDate = visit.CheckedInAt.HasValue
            ? DateOnly.FromDateTime(visit.CheckedInAt.Value)
            : DateOnly.FromDateTime(nowLocal);
        var queueDate = queueEntry?.QueueDate ?? visitDate;

        var servicePoint = await ResolveQueueServicePointAsync(
            effectiveRoomId,
            effectiveDoctorId,
            service.Id,
            department.Id,
            effectiveHospitalId,
            ct);

        var effectiveSpId = servicePoint?.Id
            ?? queueEntry?.CurrentServicePointId
            ?? queueEntry?.ServicePointId;

        var queueNumber = queueEntry?.QueueNumber is > 0 ? queueEntry.QueueNumber.Value : 1;
        var queueStatus = !string.IsNullOrWhiteSpace(queueEntry?.Status)
            ? queueEntry.Status.Trim().ToUpperInvariant()
            : (normalizedVisitStatus == "COMPLETED" ? "COMPLETED" : "WAITING");

        var waitingCountBefore = 0;
        if (normalizedVisitStatus is not ("COMPLETED" or "IN_PROGRESS" or "IN_CONSULTATION") &&
            queueStatus is not ("COMPLETED" or "IN_PROGRESS" or "IN_CONSULTATION" or "CALLED"))
        {
            waitingCountBefore = await CountWaitingBeforeAsync(
                queueEntry?.Id ?? 0L,
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

        var payment = await TryLoadLatestPaymentAsync(visit.Id, ct);
        var servicePrice = payment?.Amount
            ?? auditMetadata.ServicePrice
            ?? await ResolveServicePriceAsync(service.Id, visit.CheckedInAt ?? nowLocal, ct);
        var priceFormatted = auditMetadata.PriceFormatted ?? FormatVndPrice(servicePrice);
        var paymentMethod = auditMetadata.PaymentMethod ?? "CASHIER";
        var paymentStatus = !string.IsNullOrWhiteSpace(payment?.Status)
            ? payment.Status.Trim().ToUpperInvariant()
            : (auditMetadata.PaymentStatus ?? "PENDING");

        var effectiveQueueEntry = queueEntry ?? new QueueEntry
        {
            Id = 0,
            VisitId = visit.Id,
            ServicePointId = effectiveSpId,
            CurrentServicePointId = effectiveSpId,
            QueueDate = queueDate,
            QueueNumber = queueNumber,
            QueueType = ClinicalQueueRules.QueueTypeExamination,
            Status = queueStatus,
            PresenceStatus = "PRESENT",
            ArrivedAt = visit.CheckedInAt ?? nowLocal,
            ArrivalClass = "WALK_IN",
            ManualPriority = auditMetadata.ManualPriority ?? ClinicalQueueRules.PriorityNormal,
            StateVersion = 1,
            ReadyAt = visit.CheckedInAt ?? nowLocal,
        };

        var checkedInAtValue = visit.CheckedInAt ?? nowLocal;
        var resolvedReason = !string.IsNullOrWhiteSpace(visit.ReasonForVisit)
            ? visit.ReasonForVisit.Trim()
            : (auditMetadata.ReasonForVisit ?? service.Name);

        return await BuildWalkInResponseDtoAsync(
            visit,
            effectiveQueueEntry,
            patient,
            department,
            service,
            doctor,
            room,
            servicePoint,
            effectiveHospitalId,
            servicePrice,
            priceFormatted,
            paymentMethod,
            paymentStatus,
            resolvedReason,
            auditMetadata.Symptoms,
            auditMetadata.BloodPressure,
            auditMetadata.HeartRate,
            auditMetadata.Temperature,
            auditMetadata.IsIdentityVerified,
            auditMetadata.IsInsuranceVerified,
            auditMetadata.Notes,
            waitingCountBefore,
            estimatedWaitMinutes,
            checkedInAtValue,
            visit.CheckedInByStaffId ?? checkedInStaff?.Id ?? actor.ActorStaffId,
            checkedInStaff?.FullName ?? actor.ActorStaffName,
            ct);
    }

    public async Task<WalkInCatalogResponseDto> GetWalkInCatalogAsync(
        long actorUserId,
        CancellationToken ct = default)
    {
        if (actorUserId <= 0)
        {
            throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var nowLocal = AppointmentBookingRules.GetVietnamNow(_timeProvider);
        var actor = await EnsureWalkInActorAccessAsync(actorUserId, nowUtc, ct);

        var targetHospitalId = actor.ActorHospitalId ?? 1L;
        var hospital = await _db.Hospitals
            .AsNoTracking()
            .FirstOrDefaultAsync(h => h.Id == targetHospitalId, ct);

        var hospitalName = !string.IsNullOrWhiteSpace(hospital?.Name)
            ? hospital.Name
            : "Bệnh viện Đa khoa MedFlow";

        var departments = await _db.Departments
            .AsNoTracking()
            .Where(d => d.HospitalId == targetHospitalId && d.IsActive != false)
            .OrderBy(d => d.Id)
            .ToListAsync(ct);

        var services = await _db.Services
            .AsNoTracking()
            .Where(s =>
                s.HospitalId == targetHospitalId &&
                s.IsActive != false &&
                s.ServiceType != null &&
                s.ServiceType.ToUpper() == "EXAMINATION")
            .OrderBy(s => s.Id)
            .ToListAsync(ct);

        var rooms = await _db.HospitalRooms
            .AsNoTracking()
            .Where(r =>
                r.HospitalId == targetHospitalId &&
                r.Status != null &&
                r.Status.ToUpper() == "ACTIVE" &&
                r.RoomType != null &&
                r.RoomType.ToUpper() == "EXAMINATION")
            .OrderBy(r => r.Id)
            .ToListAsync(ct);

        var serviceIds = services.Select(s => s.Id).ToHashSet();
        var priceMap = await LoadEffectiveServicePriceMapAsync(serviceIds, nowLocal, ct);

        var departmentDtos = new List<WalkInDepartmentCatalogItemDto>();
        foreach (var dept in departments)
        {
            var deptServices = services
                .Where(s => s.DepartmentId == dept.Id)
                .Select(s =>
                {
                    var price = priceMap.TryGetValue(s.Id, out var resolved)
                        ? resolved
                        : DefaultServicePrice;
                    return new WalkInServiceCatalogItemDto(
                        Id: s.Id,
                        Code: s.ServiceCode,
                        Name: s.Name,
                        Price: price,
                        PriceFormatted: FormatVndPrice(price));
                })
                .ToList();

            if (deptServices.Count == 0)
            {
                continue;
            }

            var deptLocation = rooms
                .Where(r => r.DepartmentId == dept.Id && !string.IsNullOrWhiteSpace(r.Location))
                .Select(r => r.Location!.Trim())
                .FirstOrDefault()
                ?? "Khu Khám bệnh Ngoại trú";

            departmentDtos.Add(new WalkInDepartmentCatalogItemDto(
                Id: dept.Id,
                Code: dept.DepartmentCode,
                Name: dept.Name,
                Location: deptLocation,
                Services: deptServices));
        }

        return new WalkInCatalogResponseDto(
            HospitalId: targetHospitalId,
            HospitalName: hospitalName,
            Departments: departmentDtos,
            SymptomOptions: DefaultSymptomOptions);
    }

    private sealed record WalkInActorContext(
        bool IsSystemAdmin,
        long? ActorHospitalId,
        long? ActorStaffId,
        string? ActorStaffName);

    private async Task<WalkInActorContext> EnsureWalkInActorAccessAsync(
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
            throw new ForbiddenException("Tài khoản người dùng hiện không ở trạng thái hoạt động.");
        }

        if (actorUser.LockedUntil.HasValue && actorUser.LockedUntil.Value > nowUtc)
        {
            throw new ForbiddenException("Tài khoản người dùng hiện đang bị tạm khóa.");
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
            throw new ForbiddenException("Người dùng không có quyền tiếp nhận lượt khám vãng lai tại quầy.");
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

                var hasPermission = isReception
                    ? permissionCodes.Any(c => ReceptionAllowedPermissions.Any(allowed =>
                        string.Equals(c, allowed, StringComparison.OrdinalIgnoreCase)))
                    : permissionCodes.Any(c => HospitalAdminAllowedPermissions.Any(allowed =>
                        string.Equals(c, allowed, StringComparison.OrdinalIgnoreCase)));

                if (!hasPermission)
                {
                    throw new ForbiddenException(
                        "Vai trò hiện tại chưa được cấp quyền tiếp nhận bệnh nhân (PATIENT_CHECKIN).");
                }
            }
        }

        var actorStaff = await _db.Staff
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == actorUserId, ct);

        if (actorStaff is not null &&
            !string.Equals(actorStaff.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            throw new ForbiddenException("Hồ sơ nhân sự của người dùng hiện không ở trạng thái hoạt động.");
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

        return new WalkInActorContext(
            isSystemAdmin,
            actorStaff?.HospitalId,
            actorStaff?.Id,
            actorStaff?.FullName);
    }

    private async Task<Patient?> LoadPatientAsync(
        (string Trimmed, long? Id, string? Code) parsedPatient,
        CancellationToken ct)
    {
        if (parsedPatient.Id.HasValue)
        {
            return await _db.Patients
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == parsedPatient.Id.Value, ct);
        }

        return await _db.Patients
            .AsNoTracking()
            .FirstOrDefaultAsync(
                p => p.PatientCode != null && p.PatientCode.ToUpper() == parsedPatient.Code,
                ct);
    }

    private async Task<(Department Department, Service Service)> ResolveDepartmentAndServiceAsync(
        WalkInActorContext actor,
        (string Trimmed, long? Id, string? Code)? parsedDepartment,
        (string Trimmed, long? Id, string? Code)? parsedService,
        CancellationToken ct)
    {
        Department? department = null;
        if (parsedDepartment.HasValue)
        {
            var deptSpec = parsedDepartment.Value;
            if (deptSpec.Id.HasValue)
            {
                department = await _db.Departments
                    .AsNoTracking()
                    .FirstOrDefaultAsync(d => d.Id == deptSpec.Id.Value, ct);
            }
            else
            {
                if (actor.ActorHospitalId.HasValue)
                {
                    department = await _db.Departments
                        .AsNoTracking()
                        .FirstOrDefaultAsync(
                            d => d.HospitalId == actor.ActorHospitalId.Value &&
                                 d.DepartmentCode != null &&
                                 d.DepartmentCode.ToUpper() == deptSpec.Code,
                            ct);
                }

                department ??= await _db.Departments
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        d => d.DepartmentCode != null && d.DepartmentCode.ToUpper() == deptSpec.Code,
                        ct);
            }

            if (department is null)
            {
                throw new NotFoundException("Department", deptSpec.Trimmed);
            }

            if (!actor.IsSystemAdmin &&
                department.HospitalId.HasValue &&
                actor.ActorHospitalId.HasValue &&
                department.HospitalId.Value != actor.ActorHospitalId.Value)
            {
                throw new ForbiddenException("Bạn không có quyền tiếp nhận lượt khám cho khoa phòng thuộc cơ sở y tế khác.");
            }

            if (department.IsActive == false)
            {
                throw new ConflictException($"Khoa phòng '{department.Name}' hiện đang tạm ngưng hoạt động.");
            }
        }

        Service? service = null;
        if (parsedService.HasValue)
        {
            var srvSpec = parsedService.Value;
            if (srvSpec.Id.HasValue)
            {
                service = await _db.Services
                    .AsNoTracking()
                    .Include(s => s.Department)
                    .Include(s => s.Specialty)
                    .FirstOrDefaultAsync(s => s.Id == srvSpec.Id.Value, ct);
            }
            else
            {
                var targetHospitalId = department?.HospitalId ?? actor.ActorHospitalId;
                if (targetHospitalId.HasValue)
                {
                    service = await _db.Services
                        .AsNoTracking()
                        .Include(s => s.Department)
                        .Include(s => s.Specialty)
                        .FirstOrDefaultAsync(
                            s => s.HospitalId == targetHospitalId.Value &&
                                 s.ServiceCode != null &&
                                 s.ServiceCode.ToUpper() == srvSpec.Code,
                            ct);
                }

                service ??= await _db.Services
                    .AsNoTracking()
                    .Include(s => s.Department)
                    .Include(s => s.Specialty)
                    .FirstOrDefaultAsync(
                        s => s.ServiceCode != null && s.ServiceCode.ToUpper() == srvSpec.Code,
                        ct);
            }

            if (service is null)
            {
                throw new NotFoundException("Service", srvSpec.Trimmed);
            }

            var serviceHospitalId = service.HospitalId ?? service.Department?.HospitalId;
            if (!actor.IsSystemAdmin &&
                serviceHospitalId.HasValue &&
                actor.ActorHospitalId.HasValue &&
                serviceHospitalId.Value != actor.ActorHospitalId.Value)
            {
                throw new ForbiddenException("Bạn không có quyền chọn dịch vụ khám thuộc cơ sở y tế khác.");
            }

            if (!string.Equals(service.ServiceType, "EXAMINATION", StringComparison.OrdinalIgnoreCase))
            {
                throw new ValidationException(
                    $"Dịch vụ '{service.ServiceCode}' (loại '{service.ServiceType}') không phải là dịch vụ khám lâm sàng ngoại trú (EXAMINATION).");
            }

            if (service.IsActive == false)
            {
                throw new ConflictException($"Dịch vụ khám '{service.Name}' hiện đang tạm ngưng hoạt động.");
            }
        }

        if (department is not null && service is not null)
        {
            if (service.DepartmentId.HasValue && service.DepartmentId.Value != department.Id)
            {
                throw new ValidationException(
                    $"Dịch vụ '{service.ServiceCode}' không thuộc khoa '{department.Name}'.");
            }

            if (department.HospitalId.HasValue &&
                service.HospitalId.HasValue &&
                department.HospitalId.Value != service.HospitalId.Value)
            {
                throw new ValidationException("Khoa tiếp nhận và dịch vụ khám không thuộc cùng cơ sở y tế.");
            }

            return (department, service);
        }

        if (service is not null)
        {
            department = service.Department;
            if (department is null && service.DepartmentId.HasValue)
            {
                department = await _db.Departments
                    .AsNoTracking()
                    .FirstOrDefaultAsync(d => d.Id == service.DepartmentId.Value, ct);
            }

            if (department is null)
            {
                throw new NotFoundException("Department", service.DepartmentId ?? 0);
            }

            if (!actor.IsSystemAdmin &&
                department.HospitalId.HasValue &&
                actor.ActorHospitalId.HasValue &&
                department.HospitalId.Value != actor.ActorHospitalId.Value)
            {
                throw new ForbiddenException("Bạn không có quyền tiếp nhận lượt khám cho khoa phòng thuộc cơ sở y tế khác.");
            }

            if (department.IsActive == false)
            {
                throw new ConflictException($"Khoa phòng '{department.Name}' hiện đang tạm ngưng hoạt động.");
            }

            return (department, service);
        }

        var departmentServices = await _db.Services
            .AsNoTracking()
            .Where(s =>
                s.DepartmentId == department!.Id &&
                s.ServiceType != null &&
                s.ServiceType.ToUpper() == "EXAMINATION")
            .OrderBy(s => s.Id)
            .ToListAsync(ct);

        if (departmentServices.Count == 0)
        {
            throw new NotFoundException("Service", $"Department:{department!.DepartmentCode}");
        }

        service = departmentServices.FirstOrDefault(s => s.IsActive != false)
            ?? throw new ConflictException(
                $"Khoa phòng '{department!.Name}' hiện không có dịch vụ khám ngoại trú nào đang hoạt động.");

        return (department!, service);
    }

    private async Task<Doctor> ResolveAndValidateDoctorAsync(
        WalkInActorContext actor,
        (string Trimmed, long? Id, string? Code) parsedDoctor,
        Department department,
        long effectiveHospitalId,
        CancellationToken ct)
    {
        Doctor? doctor = null;
        if (parsedDoctor.Id.HasValue)
        {
            doctor = await _db.Doctors
                .AsNoTracking()
                .Include(d => d.Staff)
                .Include(d => d.Specialty)
                .FirstOrDefaultAsync(d => d.Id == parsedDoctor.Id.Value, ct);
        }
        else
        {
            doctor = await _db.Doctors
                .AsNoTracking()
                .Include(d => d.Staff)
                .Include(d => d.Specialty)
                .FirstOrDefaultAsync(
                    d => d.HospitalId == effectiveHospitalId &&
                         d.DoctorCode != null &&
                         d.DoctorCode.ToUpper() == parsedDoctor.Code,
                    ct)
                ?? await _db.Doctors
                    .AsNoTracking()
                    .Include(d => d.Staff)
                    .Include(d => d.Specialty)
                    .FirstOrDefaultAsync(
                        d => d.DoctorCode != null && d.DoctorCode.ToUpper() == parsedDoctor.Code,
                        ct);
        }

        if (doctor is null)
        {
            throw new NotFoundException("Doctor", parsedDoctor.Trimmed);
        }

        var doctorHospitalId = doctor.HospitalId ?? doctor.Staff?.HospitalId;
        if (!actor.IsSystemAdmin &&
            doctorHospitalId.HasValue &&
            actor.ActorHospitalId.HasValue &&
            doctorHospitalId.Value != actor.ActorHospitalId.Value)
        {
            throw new ForbiddenException("Bạn không có quyền phân công bác sĩ thuộc cơ sở y tế khác.");
        }

        if (doctorHospitalId.HasValue && doctorHospitalId.Value != effectiveHospitalId)
        {
            throw new ValidationException("Bác sĩ được chọn không thuộc cùng cơ sở y tế với khoa tiếp nhận.");
        }

        if (!doctor.IsActive ||
            (doctor.Staff is not null &&
             !string.Equals(doctor.Staff.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ConflictException("Bác sĩ được chọn hiện đang tạm ngưng hoạt động.");
        }

        if (doctor.Staff?.DepartmentId.HasValue == true &&
            doctor.Staff.DepartmentId.Value != department.Id)
        {
            throw new ValidationException(
                $"Bác sĩ '{doctor.DoctorCode}' không thuộc khoa '{department.Name}'.");
        }

        return doctor;
    }

    private async Task<HospitalRoom> ResolveAndValidateRoomAsync(
        WalkInActorContext actor,
        (string Trimmed, long? Id, string? Code) parsedRoom,
        Department department,
        long effectiveHospitalId,
        CancellationToken ct)
    {
        HospitalRoom? room = null;
        if (parsedRoom.Id.HasValue)
        {
            room = await _db.HospitalRooms
                .AsNoTracking()
                .Include(r => r.Department)
                .FirstOrDefaultAsync(r => r.Id == parsedRoom.Id.Value, ct);
        }
        else
        {
            room = await _db.HospitalRooms
                .AsNoTracking()
                .Include(r => r.Department)
                .FirstOrDefaultAsync(
                    r => r.HospitalId == effectiveHospitalId &&
                         r.RoomCode != null &&
                         r.RoomCode.ToUpper() == parsedRoom.Code,
                    ct)
                ?? await _db.HospitalRooms
                    .AsNoTracking()
                    .Include(r => r.Department)
                    .FirstOrDefaultAsync(
                        r => r.RoomCode != null && r.RoomCode.ToUpper() == parsedRoom.Code,
                        ct);
        }

        if (room is null)
        {
            throw new NotFoundException("HospitalRoom", parsedRoom.Trimmed);
        }

        if (!actor.IsSystemAdmin &&
            room.HospitalId.HasValue &&
            actor.ActorHospitalId.HasValue &&
            room.HospitalId.Value != actor.ActorHospitalId.Value)
        {
            throw new ForbiddenException("Bạn không có quyền chọn buồng khám thuộc cơ sở y tế khác.");
        }

        if (room.HospitalId.HasValue && room.HospitalId.Value != effectiveHospitalId)
        {
            throw new ValidationException("Buồng khám không thuộc cùng cơ sở y tế với khoa tiếp nhận.");
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

        if (room.DepartmentId.HasValue && room.DepartmentId.Value != department.Id)
        {
            throw new ValidationException(
                $"Buồng khám '{room.RoomCode}' không thuộc khoa '{department.Name}'.");
        }

        var activeOccupancy = await _db.Visits
            .AsNoTracking()
            .CountAsync(
                v => v.ExaminationRoomId == room.Id &&
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

        if (room.Capacity.HasValue &&
            room.Capacity.Value > 0 &&
            activeOccupancy >= room.Capacity.Value)
        {
            throw new ConflictException(
                $"Buồng khám '{room.RoomCode}' đã đầy ({activeOccupancy}/{room.Capacity.Value} ca đang hoạt động). Vui lòng chọn buồng khám khác.");
        }

        return room;
    }

    private async Task<QueueServicePoint?> ResolveQueueServicePointAsync(
        long? roomId,
        long? doctorId,
        long? serviceId,
        long? departmentId,
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
                    (sp.PointType == null ||
                     (sp.PointType.ToUpper() != "DIAGNOSTIC" &&
                      sp.PointType.ToUpper() != "RECEPTION" &&
                      sp.PointType.ToUpper() != "PHARMACY")))
                .OrderBy(sp => sp.Id)
                .ToListAsync(ct);

            if (candidates.Count == 0)
            {
                return null;
            }

            if (roomId.HasValue && doctorId.HasValue)
            {
                var exactRoomAndDoctor = candidates.FirstOrDefault(
                    sp => sp.RoomId == roomId.Value && sp.DoctorId == doctorId.Value);
                if (exactRoomAndDoctor is not null)
                {
                    return exactRoomAndDoctor;
                }
            }

            if (roomId.HasValue)
            {
                var byRoom = candidates.FirstOrDefault(sp => sp.RoomId == roomId.Value);
                if (byRoom is not null)
                {
                    return byRoom;
                }
            }

            if (doctorId.HasValue)
            {
                var byDoctor = candidates.FirstOrDefault(sp => sp.DoctorId == doctorId.Value);
                if (byDoctor is not null)
                {
                    return byDoctor;
                }
            }

            if (serviceId.HasValue)
            {
                var byService = candidates.FirstOrDefault(sp => sp.ServiceId == serviceId.Value);
                if (byService is not null)
                {
                    return byService;
                }
            }

            if (departmentId.HasValue)
            {
                var deptRoomIds = (await _db.HospitalRooms
                        .AsNoTracking()
                        .Where(r =>
                            r.DepartmentId == departmentId.Value &&
                            r.Status != null &&
                            r.Status.ToUpper() == "ACTIVE" &&
                            r.RoomType != null &&
                            r.RoomType.ToUpper() == "EXAMINATION")
                        .Select(r => r.Id)
                        .ToListAsync(ct))
                    .ToHashSet();

                var byDeptRoom = candidates.FirstOrDefault(
                    sp => sp.RoomId.HasValue && deptRoomIds.Contains(sp.RoomId.Value));
                if (byDeptRoom is not null)
                {
                    return byDeptRoom;
                }
            }

            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
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
                (q.QueueType == null ||
                 q.QueueType.ToUpper() == "EXAMINATION" ||
                 q.QueueType.ToUpper() == "RETURN_CONSULTATION") &&
                (effectiveSpId == null ||
                 q.ServicePointId == effectiveSpId ||
                 q.CurrentServicePointId == effectiveSpId) &&
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

    private async Task<decimal> ResolveServicePriceAsync(
        long serviceId,
        DateTime referenceLocalTime,
        CancellationToken ct)
    {
        try
        {
            var prices = await _db.ServicePrices
                .AsNoTracking()
                .Where(p =>
                    p.ServiceId == serviceId &&
                    p.PriceAmount != null &&
                    (p.EffectiveFrom == null || p.EffectiveFrom <= referenceLocalTime) &&
                    (p.EffectiveTo == null || p.EffectiveTo >= referenceLocalTime))
                .OrderByDescending(p => p.EffectiveFrom ?? DateTime.MinValue)
                .ThenByDescending(p => p.Id)
                .Select(p => p.PriceAmount!.Value)
                .ToListAsync(ct);

            if (prices.Count > 0 && prices[0] > 0)
            {
                return prices[0];
            }
        }
        catch (NotSupportedException)
        {
            // ServicePrices DbSet not configured in lightweight unit test double
        }

        return DefaultServicePrice;
    }

    private async Task<Dictionary<long, decimal>> LoadEffectiveServicePriceMapAsync(
        IReadOnlySet<long> serviceIds,
        DateTime referenceLocalTime,
        CancellationToken ct)
    {
        var result = new Dictionary<long, decimal>();
        if (serviceIds.Count == 0)
        {
            return result;
        }

        try
        {
            var rows = await _db.ServicePrices
                .AsNoTracking()
                .Where(p =>
                    p.ServiceId.HasValue &&
                    serviceIds.Contains(p.ServiceId.Value) &&
                    p.PriceAmount != null &&
                    (p.EffectiveFrom == null || p.EffectiveFrom <= referenceLocalTime) &&
                    (p.EffectiveTo == null || p.EffectiveTo >= referenceLocalTime))
                .OrderByDescending(p => p.EffectiveFrom ?? DateTime.MinValue)
                .ThenByDescending(p => p.Id)
                .ToListAsync(ct);

            foreach (var row in rows)
            {
                var sid = row.ServiceId!.Value;
                if (!result.ContainsKey(sid) && row.PriceAmount is > 0)
                {
                    result[sid] = row.PriceAmount.Value;
                }
            }
        }
        catch (NotSupportedException)
        {
            // ServicePrices DbSet not configured in lightweight unit test double
        }

        return result;
    }

    private void TryAddWalkInPayment(long visitId, decimal amount, string status)
    {
        try
        {
            _db.Payments.Add(new Payment
            {
                VisitId = visitId,
                Amount = amount,
                Status = status,
                PaidAt = null,
            });
        }
        catch (NotSupportedException)
        {
            // Payments DbSet not configured in lightweight unit test double
        }
    }

    private async Task<Payment?> TryLoadLatestPaymentAsync(long visitId, CancellationToken ct)
    {
        try
        {
            return await _db.Payments
                .AsNoTracking()
                .Where(p => p.VisitId == visitId)
                .OrderByDescending(p => p.Id)
                .FirstOrDefaultAsync(ct);
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

    private async Task<CreateWalkInVisitResponseDto> BuildWalkInResponseDtoAsync(
        Visit visit,
        QueueEntry queueEntry,
        Patient patient,
        Department department,
        Service service,
        Doctor? doctor,
        HospitalRoom? room,
        QueueServicePoint? servicePoint,
        long effectiveHospitalId,
        decimal servicePrice,
        string priceFormatted,
        string paymentMethod,
        string paymentStatus,
        string reasonForVisit,
        IReadOnlyList<string> symptoms,
        string? bloodPressure,
        int? heartRate,
        double? temperature,
        bool isIdentityVerified,
        bool isInsuranceVerified,
        string? notes,
        int waitingCountBefore,
        int estimatedWaitMinutes,
        DateTime checkedInAtLocal,
        long? checkedInByStaffId,
        string? checkedInByStaffName,
        CancellationToken ct)
    {
        var today = queueEntry.QueueDate ?? DateOnly.FromDateTime(checkedInAtLocal);
        var visitCode = !string.IsNullOrWhiteSpace(visit.VisitCode)
            ? visit.VisitCode
            : $"VIS-{today:yyyyMM}-{visit.Id:D4}";
        var ticketCode = $"TKT-{visitCode}";
        var queueNumber = queueEntry.QueueNumber is > 0 ? queueEntry.QueueNumber.Value : 1;
        var ticketNo = $"A{queueNumber:D3}";

        var manualPriority = ClinicalQueueRules.NormalizeStoredPriority(queueEntry.ManualPriority);
        var effectivePriority = ClinicalQueueRules.ResolveEffectivePriority(
            manualPriority,
            patient.DateOfBirth,
            today);
        var clinicalPriorityLabel = ClinicalQueueRules.IsElevated(effectivePriority) ? "PRIORITY" : "STANDARD";

        var departmentCode = !string.IsNullOrWhiteSpace(department.DepartmentCode)
            ? department.DepartmentCode
            : $"DEPT-{department.Id}";
        var departmentName = !string.IsNullOrWhiteSpace(department.Name)
            ? department.Name
            : "Khoa Khám bệnh";

        var serviceCode = !string.IsNullOrWhiteSpace(service.ServiceCode)
            ? service.ServiceCode
            : $"SRV-{service.Id}";
        var serviceName = !string.IsNullOrWhiteSpace(service.Name)
            ? service.Name
            : $"Khám {departmentName}";

        var roomName = room is not null && !string.IsNullOrWhiteSpace(room.Name)
            ? room.Name
            : null;
        var doctorName = doctor is not null
            ? (!string.IsNullOrWhiteSpace(doctor.Staff?.FullName)
                ? doctor.Staff.FullName
                : (!string.IsNullOrWhiteSpace(doctor.DoctorCode) ? $"Bác sĩ {doctor.DoctorCode}" : "Bác sĩ phụ trách"))
            : null;

        var effectiveSpId = queueEntry.CurrentServicePointId
            ?? queueEntry.ServicePointId
            ?? servicePoint?.Id;
        var servicePointName = servicePoint?.Name;
        if (string.IsNullOrWhiteSpace(servicePointName) && effectiveSpId.HasValue)
        {
            servicePointName = await TryGetServicePointNameAsync(effectiveSpId.Value, ct);
        }

        servicePointName ??= roomName ?? $"Hàng đợi {departmentName}";

        var instructions = new List<string>();
        if (room is not null)
        {
            var roomDisplay = !string.IsNullOrWhiteSpace(room.RoomCode)
                ? $"{room.Name} ({room.RoomCode})"
                : room.Name;
            var locationSuffix = !string.IsNullOrWhiteSpace(room.Location)
                ? $" - {room.Location}"
                : string.Empty;
            instructions.Add($"Di chuyển đến trước {roomDisplay} ({departmentName}{locationSuffix}).");
        }
        else
        {
            instructions.Add($"Thực hiện điều phối bác sĩ và phân buồng khám tại {departmentName} (UC26 / UC27).");
        }

        instructions.Add("Hoàn tất thanh toán phí khám ban đầu tại quầy thu ngân hoặc qua mã VietQR.");
        instructions.Add("Theo dõi màn hình điện tử và lắng nghe loa gọi số thứ tự tiếp nhận.");

        var patientCode = !string.IsNullOrWhiteSpace(patient.PatientCode)
            ? patient.PatientCode
            : $"PAT-{patient.Id:D4}";

        var timeLabel = checkedInAtLocal.ToString("HH:mm", CultureInfo.InvariantCulture);

        return new CreateWalkInVisitResponseDto(
            VisitId: visit.Id,
            VisitCode: visitCode,
            TicketCode: ticketCode,
            TicketNo: ticketNo,
            QueueEntryId: queueEntry.Id,
            QueueNumber: queueNumber,
            QueueNumberFormatted: queueNumber.ToString("D3", CultureInfo.InvariantCulture),
            QueueStatus: queueEntry.Status ?? "WAITING",
            PresenceStatus: queueEntry.PresenceStatus ?? "PRESENT",
            ArrivalClass: queueEntry.ArrivalClass ?? "WALK_IN",
            Priority: effectivePriority,
            ManualPriority: manualPriority,
            ClinicalPriority: clinicalPriorityLabel,
            VisitStatus: visit.Status ?? "CHECKED_IN",
            VisitType: "WALK_IN",
            PatientId: patient.Id,
            PatientCode: patientCode,
            PatientName: patient.FullName,
            HospitalId: effectiveHospitalId,
            DepartmentId: department.Id,
            DepartmentCode: departmentCode,
            DepartmentName: departmentName,
            ServiceId: service.Id,
            ServiceCode: serviceCode,
            ServiceName: serviceName,
            ServicePrice: servicePrice,
            PriceFormatted: priceFormatted,
            PaymentMethod: paymentMethod,
            PaymentStatus: paymentStatus,
            RoomId: room?.Id ?? visit.ExaminationRoomId,
            RoomCode: room?.RoomCode,
            RoomName: roomName,
            RoomLocation: room?.Location,
            DoctorId: doctor?.Id ?? visit.AssignedDoctorId,
            DoctorCode: doctor?.DoctorCode,
            DoctorName: doctorName,
            ServicePointId: effectiveSpId,
            ServicePointName: servicePointName,
            ReasonForVisit: reasonForVisit,
            Symptoms: symptoms,
            BloodPressure: bloodPressure,
            HeartRate: heartRate,
            Temperature: temperature,
            IsIdentityVerified: isIdentityVerified,
            IsInsuranceVerified: isInsuranceVerified,
            Notes: notes,
            WaitingCountBefore: waitingCountBefore,
            WaitingAheadCount: waitingCountBefore,
            EstimatedWaitMinutes: estimatedWaitMinutes,
            ReceptionTime: timeLabel,
            CheckedInAt: timeLabel,
            CheckedInAtIso: checkedInAtLocal,
            CheckedInByStaffId: checkedInByStaffId,
            CheckedInByStaffName: checkedInByStaffName,
            NextStepInstructions: instructions);
    }

    internal sealed record WalkInAuditMetadata(
        long? HospitalId,
        long? DepartmentId,
        string? DepartmentCode,
        string? DepartmentName,
        long? ServiceId,
        string? ServiceCode,
        string? ServiceName,
        decimal? ServicePrice,
        string? PriceFormatted,
        string? PaymentMethod,
        string? PaymentStatus,
        long? DoctorId,
        long? RoomId,
        string? ManualPriority,
        string? ReasonForVisit,
        IReadOnlyList<string> Symptoms,
        string? BloodPressure,
        int? HeartRate,
        double? Temperature,
        bool IsIdentityVerified,
        bool IsInsuranceVerified,
        string? Notes);

    internal async Task<WalkInAuditMetadata> LoadWalkInAuditMetadataAsync(
        long visitId,
        CancellationToken ct)
    {
        return await LoadWalkInAuditMetadataStaticAsync(_db, visitId, ct);
    }

    internal static async Task<WalkInAuditMetadata> LoadWalkInAuditMetadataStaticAsync(
        IApplicationDbContext db,
        long visitId,
        CancellationToken ct)
    {
        var visitIdText = visitId.ToString(CultureInfo.InvariantCulture);
        var auditLog = await db.AuditLogs
            .AsNoTracking()
            .Where(a =>
                a.EntityType != null &&
                a.EntityType.ToUpper() == VisitEntityType &&
                a.EntityId == visitIdText &&
                a.Action != null &&
                (a.Action.ToUpper() == WalkInVisitCreatedAction ||
                 a.Action.ToUpper() == CheckInScheduledPatientService.PatientCheckedInAction))
            .OrderByDescending(a => a.OccurredAt)
            .ThenByDescending(a => a.Id)
            .FirstOrDefaultAsync(ct);

        if (auditLog is null || string.IsNullOrWhiteSpace(auditLog.ChangeData))
        {
            return new WalkInAuditMetadata(
                HospitalId: auditLog?.HospitalId,
                DepartmentId: null,
                DepartmentCode: null,
                DepartmentName: null,
                ServiceId: null,
                ServiceCode: null,
                ServiceName: null,
                ServicePrice: null,
                PriceFormatted: null,
                PaymentMethod: null,
                PaymentStatus: null,
                DoctorId: null,
                RoomId: null,
                ManualPriority: null,
                ReasonForVisit: auditLog?.Reason,
                Symptoms: [],
                BloodPressure: null,
                HeartRate: null,
                Temperature: null,
                IsIdentityVerified: true,
                IsInsuranceVerified: false,
                Notes: null);
        }

        try
        {
            using var doc = JsonDocument.Parse(auditLog.ChangeData);
            var root = doc.RootElement;

            var departmentId = TryGetInt64(root, "departmentId");
            var departmentCode = TryGetString(root, "departmentCode");
            var departmentName = TryGetString(root, "departmentName");
            var serviceId = TryGetInt64(root, "serviceId");
            var serviceCode = TryGetString(root, "serviceCode");
            var serviceName = TryGetString(root, "serviceName");
            var servicePrice = TryGetDecimal(root, "servicePrice");
            var priceFormatted = TryGetString(root, "priceFormatted");
            var paymentMethod = TryGetString(root, "paymentMethod");
            var paymentStatus = TryGetString(root, "paymentStatus");
            var doctorId = TryGetInt64(root, "doctorId");
            var roomId = TryGetInt64(root, "roomId");
            var manualPriority = TryGetString(root, "manualPriority");
            var reasonForVisit = TryGetString(root, "reasonForVisit") ?? auditLog.Reason;
            var notes = TryGetString(root, "notes");

            var symptoms = new List<string>();
            if (root.TryGetProperty("symptoms", out var symEl) && symEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in symEl.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        var val = item.GetString();
                        if (!string.IsNullOrWhiteSpace(val))
                        {
                            symptoms.Add(val.Trim());
                        }
                    }
                }
            }

            string? bloodPressure = null;
            int? heartRate = null;
            double? temperature = null;

            if (root.TryGetProperty("vitals", out var vitalsEl) && vitalsEl.ValueKind == JsonValueKind.Object)
            {
                bloodPressure = TryGetString(vitalsEl, "bloodPressure");
                heartRate = TryGetInt32(vitalsEl, "heartRate");
                temperature = TryGetDouble(vitalsEl, "temperature");
            }

            var isIdentityVerified = TryGetBoolean(root, "isIdentityVerified") ?? true;
            var isInsuranceVerified = TryGetBoolean(root, "isInsuranceVerified") ?? false;

            return new WalkInAuditMetadata(
                HospitalId: auditLog.HospitalId,
                DepartmentId: departmentId,
                DepartmentCode: departmentCode,
                DepartmentName: departmentName,
                ServiceId: serviceId,
                ServiceCode: serviceCode,
                ServiceName: serviceName,
                ServicePrice: servicePrice,
                PriceFormatted: priceFormatted,
                PaymentMethod: paymentMethod,
                PaymentStatus: paymentStatus,
                DoctorId: doctorId,
                RoomId: roomId,
                ManualPriority: manualPriority,
                ReasonForVisit: reasonForVisit,
                Symptoms: symptoms,
                BloodPressure: bloodPressure,
                HeartRate: heartRate,
                Temperature: temperature,
                IsIdentityVerified: isIdentityVerified,
                IsInsuranceVerified: isInsuranceVerified,
                Notes: notes);
        }
        catch (JsonException)
        {
            return new WalkInAuditMetadata(
                HospitalId: auditLog.HospitalId,
                DepartmentId: null,
                DepartmentCode: null,
                DepartmentName: null,
                ServiceId: null,
                ServiceCode: null,
                ServiceName: null,
                ServicePrice: null,
                PriceFormatted: null,
                PaymentMethod: null,
                PaymentStatus: null,
                DoctorId: null,
                RoomId: null,
                ManualPriority: null,
                ReasonForVisit: auditLog.Reason,
                Symptoms: [],
                BloodPressure: null,
                HeartRate: null,
                Temperature: null,
                IsIdentityVerified: true,
                IsInsuranceVerified: false,
                Notes: null);
        }
    }

    private static string NormalizeWalkInPriorityOrThrow(string? rawPriority)
    {
        if (string.IsNullOrWhiteSpace(rawPriority))
        {
            return ClinicalQueueRules.PriorityNormal;
        }

        var normalizedKey = AppointmentBookingRules.NormalizeStatusKey(rawPriority);
        if (normalizedKey == "STANDARD")
        {
            return ClinicalQueueRules.PriorityNormal;
        }

        return ClinicalQueueRules.NormalizePriorityOrThrow(rawPriority)!;
    }

    private static string NormalizePaymentMethodOrThrow(string? rawPaymentMethod)
    {
        if (string.IsNullOrWhiteSpace(rawPaymentMethod))
        {
            return "CASHIER";
        }

        var normalizedKey = AppointmentBookingRules.NormalizeStatusKey(rawPaymentMethod);
        return normalizedKey switch
        {
            "VIETQR" or "QR" or "BANK_TRANSFER" => "VIETQR",
            "CASHIER" or "CASH" or "COUNTER" => "CASHIER",
            _ => throw new ValidationException(
                "Phương thức thanh toán không hợp lệ. Chỉ chấp nhận: VIETQR, CASHIER."),
        };
    }

    private static IReadOnlyList<string> NormalizeSymptomsOrThrow(IReadOnlyList<string>? rawSymptoms)
    {
        if (rawSymptoms is null || rawSymptoms.Count == 0)
        {
            return [];
        }

        if (rawSymptoms.Count > MaxSymptomCount)
        {
            throw new ValidationException($"Danh sách triệu chứng đi kèm không được vượt quá {MaxSymptomCount} mục.");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalized = new List<string>(rawSymptoms.Count);

        foreach (var raw in rawSymptoms)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var trimmed = raw.Trim();
            if (trimmed.Length > MaxSymptomLength)
            {
                throw new ValidationException(
                    $"Mỗi triệu chứng ghi nhận không được vượt quá {MaxSymptomLength} ký tự.");
            }

            if (seen.Add(trimmed))
            {
                normalized.Add(trimmed);
            }
        }

        return normalized;
    }

    private static (string Trimmed, long? Id, string? Code) ParseIdentifier(
        string? idOrCode,
        string invalidMessage)
    {
        if (string.IsNullOrWhiteSpace(idOrCode))
        {
            throw new ValidationException(invalidMessage);
        }

        var trimmed = idOrCode.Trim();
        if (trimmed.StartsWith('#'))
        {
            trimmed = trimmed[1..].TrimStart();
        }

        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxIdentifierLength)
        {
            throw new ValidationException(invalidMessage);
        }

        if (trimmed.All(ch => char.IsAsciiDigit(ch) || ch == '-') &&
            long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedId))
        {
            if (parsedId <= 0)
            {
                throw new ValidationException(invalidMessage);
            }

            return (trimmed, parsedId, null);
        }

        if (!trimmed.All(ch => char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' || ch == '.'))
        {
            throw new ValidationException(invalidMessage);
        }

        return (trimmed, null, trimmed.ToUpperInvariant());
    }

    private static string FormatVndPrice(decimal amount)
    {
        var rounded = Math.Round(amount, 0, MidpointRounding.AwayFromZero);
        return $"{rounded.ToString("#,##0", CultureInfo.InvariantCulture).Replace(',', '.')} VNĐ";
    }

    private static string? FirstNonEmpty(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate.Trim();
            }
        }

        return null;
    }

    private static string? TryGetString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;

    private static long? TryGetInt64(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var prop) &&
           prop.ValueKind == JsonValueKind.Number &&
           prop.TryGetInt64(out var val)
            ? val
            : null;

    private static int? TryGetInt32(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var prop) &&
           prop.ValueKind == JsonValueKind.Number &&
           prop.TryGetInt32(out var val)
            ? val
            : null;

    private static double? TryGetDouble(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var prop) &&
           prop.ValueKind == JsonValueKind.Number &&
           prop.TryGetDouble(out var val)
            ? val
            : null;

    private static decimal? TryGetDecimal(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var prop) &&
           prop.ValueKind == JsonValueKind.Number &&
           prop.TryGetDecimal(out var val)
            ? val
            : null;

    private static bool? TryGetBoolean(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var prop) &&
           prop.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? prop.GetBoolean()
            : null;
}
