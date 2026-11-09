using System.Globalization;
using MedFlow.Application.DTOs.Appointments;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Doctors;
using MedFlow.Application.Services.Visits;
using MedFlow.Application.Services.Visits.Queueing;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Appointments;

public sealed class AppointmentQueueTrackingService : IAppointmentQueueTrackingService
{
    private const string StepCheckIn = "CHECK_IN";
    private const string StepConsultation = "CONSULTATION";
    private const string StepComplete = "COMPLETE";

    private const string StateDone = "done";
    private const string StateCurrent = "current";
    private const string StateUpcoming = "upcoming";

    private const int DefaultDurationMinutes = 15;

    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;

    public AppointmentQueueTrackingService(
        IApplicationDbContext db,
        TimeProvider timeProvider)
    {
        _db = db;
        _timeProvider = timeProvider;
    }

    public async Task<AppointmentQueueStatusDto> GetMyAppointmentQueueStatusAsync(
        long userId,
        string? idOrCode,
        CancellationToken ct = default)
    {
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var nowLocal = AppointmentBookingRules.GetVietnamNow(_timeProvider);
        var today = DateOnly.FromDateTime(nowLocal);

        var view = await AppointmentReadModel.LoadOwnedAppointmentViewAsync(
            _db,
            userId,
            idOrCode,
            nowLocal,
            "Bạn không có quyền xem hàng đợi của lịch hẹn này.",
            ct);

        var item = AppointmentReadModel.MapItem(
            view.Appointment,
            view.ScopeMember,
            view.Visit,
            view.Queue,
            view.Paid,
            nowLocal,
            today);

        var appointmentId = view.Appointment.Id;
        var visitId = view.Visit?.Id;

        var candidateQueues = await _db.QueueEntries
            .AsNoTracking()
            .Where(q =>
                (q.AppointmentId == appointmentId || (visitId.HasValue && q.VisitId == visitId.Value)) &&
                (q.QueueType == null || q.QueueType.ToUpper() == "EXAMINATION" || q.QueueType.ToUpper() == "RETURN_CONSULTATION"))
            .ToListAsync(ct);

        var orderedCandidates = candidateQueues
            .OrderByDescending(q => visitId.HasValue && q.VisitId == visitId.Value)
            .ThenByDescending(q => q.Id)
            .ToList();

        var activeQueue = orderedCandidates.FirstOrDefault(
            q => AppointmentBookingRules.NormalizeStatusKey(q.Status) != "CANCELLED");
        var effectiveQueue = activeQueue ?? orderedCandidates.FirstOrDefault();

        var visitStatus = AppointmentBookingRules.NormalizeStatusKey(view.Visit?.Status);

        if (item.Status == "CANCELLED" || visitStatus == "CANCELLED")
            throw new ConflictException("Lịch hẹn đã hủy nên không còn số thứ tự trong hàng đợi.");

        if (item.Status == "NO_SHOW" || visitStatus == "NO_SHOW")
            throw new ConflictException("Lịch hẹn đã ghi nhận vắng mặt nên không còn số thứ tự trong hàng đợi.");

        if (effectiveQueue is not null && AppointmentBookingRules.NormalizeStatusKey(effectiveQueue.Status) == "CANCELLED")
            throw new ConflictException("Số thứ tự trong hàng đợi của lượt khám này đã bị hủy.");

        var roomName = item.RoomName;
        var doctorName = item.DoctorName;
        var departmentName = item.DepartmentName;

        var effectiveRoomId = view.Visit?.ExaminationRoomId ?? view.Appointment.Slot?.RoomId;
        var effectiveDoctorId = view.Visit?.AssignedDoctorId ?? view.Appointment.Slot?.DoctorId;

        if (view.Visit?.ExaminationRoomId is long visitRoomId && visitRoomId != view.Appointment.Slot?.RoomId)
        {
            var overrideRoom = await _db.HospitalRooms
                .AsNoTracking()
                .Include(r => r.Department)
                .FirstOrDefaultAsync(r => r.Id == visitRoomId, ct);

            if (overrideRoom is not null)
            {
                if (!string.IsNullOrWhiteSpace(overrideRoom.Name))
                    roomName = overrideRoom.Name;

                if (!string.IsNullOrWhiteSpace(overrideRoom.Department?.Name))
                    departmentName = overrideRoom.Department.Name;
            }
        }

        if (view.Visit?.AssignedDoctorId is long visitDoctorId && visitDoctorId != view.Appointment.Slot?.DoctorId)
        {
            var overrideDoctor = await _db.Doctors
                .AsNoTracking()
                .Include(d => d.Staff)
                .FirstOrDefaultAsync(d => d.Id == visitDoctorId, ct);

            if (overrideDoctor is not null)
            {
                var doctorTitle = string.IsNullOrWhiteSpace(overrideDoctor.ProfessionalTitle)
                    ? "BS"
                    : overrideDoctor.ProfessionalTitle.Trim();
                doctorName = DoctorCatalogService.FormatDisplayName(overrideDoctor.Staff?.FullName, doctorTitle);
            }
        }

        var queueDate = effectiveQueue?.QueueDate
            ?? (view.Visit?.CheckedInAt.HasValue == true
                ? DateOnly.FromDateTime(view.Visit.CheckedInAt.Value)
                : DateOnly.FromDateTime(view.Appointment.Slot!.StartsAt!.Value));

        string queueNumber;
        if (effectiveQueue?.QueueNumber is > 0)
        {
            queueNumber = effectiveQueue.QueueNumber.Value.ToString("D3", CultureInfo.InvariantCulture);
        }
        else
        {
            queueNumber = await AppointmentReadModel.ResolveDisplayQueueNumberAsync(_db, item, view.Appointment, ct)
                ?? await AppointmentBookingRules.ComputeQueueNumberAsync(_db, view.Appointment.Id, view.Appointment.Slot!, ct);
        }

        string visitCode;
        if (!string.IsNullOrWhiteSpace(view.Visit?.VisitCode))
        {
            visitCode = view.Visit.VisitCode.Trim();
        }
        else if (view.Visit is not null)
        {
            visitCode = $"VIS-{queueDate:yyyyMM}-{view.Visit.Id:D4}";
        }
        else
        {
            visitCode = $"VIS-{item.AppointmentCode.Replace("APT-", "", StringComparison.OrdinalIgnoreCase)}";
        }

        var hospitalId = view.Appointment.Slot?.Service?.HospitalId
            ?? view.Appointment.Slot?.Room?.HospitalId
            ?? view.Appointment.Slot?.Doctor?.HospitalId
            ?? 0L;

        var targetServicePointId = effectiveQueue?.CurrentServicePointId ?? effectiveQueue?.ServicePointId;
        if (!targetServicePointId.HasValue && (effectiveRoomId.HasValue || effectiveDoctorId.HasValue))
        {
            targetServicePointId = await TryResolveServicePointIdAsync(
                hospitalId,
                effectiveRoomId,
                effectiveDoctorId,
                ct);
        }

        var bucketEvaluation = await EvaluateServicePointBucketAsync(
            view,
            effectiveQueue,
            queueNumber,
            queueDate,
            hospitalId,
            targetServicePointId,
            effectiveRoomId,
            effectiveDoctorId,
            nowUtc,
            nowLocal,
            ct);

        var roomLabel = !string.IsNullOrWhiteSpace(roomName) ? roomName : "phòng khám";
        var (status, statusLabel, estimatedWaitLabel, visitSteps, guidanceNote) = BuildProgressPresentation(
            item,
            view,
            effectiveQueue,
            visitStatus,
            queueNumber,
            roomLabel,
            doctorName,
            bucketEvaluation);

        return new AppointmentQueueStatusDto(
            AppointmentId: item.Id,
            AppointmentCode: item.AppointmentCode,
            VisitCode: visitCode,
            QueueNumber: queueNumber,
            Status: status,
            StatusLabel: statusLabel,
            RoomName: roomName,
            DoctorName: doctorName,
            DepartmentName: departmentName,
            ServingQueueNumber: bucketEvaluation.ServingQueueNumber,
            NextQueueNumber: bucketEvaluation.NextQueueNumber,
            EstimatedWaitLabel: estimatedWaitLabel,
            VisitSteps: visitSteps,
            GuidanceNote: guidanceNote);
    }

    private async Task<long?> TryResolveServicePointIdAsync(
        long hospitalId,
        long? effectiveRoomId,
        long? effectiveDoctorId,
        CancellationToken ct)
    {
        try
        {
            var servicePoints = await _db.QueueServicePoints
                .AsNoTracking()
                .Where(sp =>
                    sp.IsActive != false &&
                    (hospitalId <= 0 || sp.HospitalId == null || sp.HospitalId == hospitalId))
                .OrderBy(sp => sp.Id)
                .ToListAsync(ct);

            var examPoints = servicePoints
                .Where(sp => AppointmentBookingRules.NormalizeStatusKey(sp.PointType) is not ("DIAGNOSTIC" or "RECEPTION" or "PHARMACY"))
                .ToList();

            if (effectiveRoomId.HasValue)
            {
                var byRoom = examPoints.FirstOrDefault(sp => sp.RoomId == effectiveRoomId.Value);
                if (byRoom is not null)
                    return byRoom.Id;
            }

            if (effectiveDoctorId.HasValue)
            {
                var byDoctor = examPoints.FirstOrDefault(sp => sp.DoctorId == effectiveDoctorId.Value);
                if (byDoctor is not null)
                    return byDoctor.Id;
            }
        }
        catch (NotSupportedException)
        {
            // Fallback when QueueServicePoints is not configured in unit-test DbContext
        }

        return null;
    }

    private async Task<BucketEvaluationSummary> EvaluateServicePointBucketAsync(
        AppointmentReadModel.OwnedAppointmentView view,
        QueueEntry? effectiveQueue,
        string ownQueueNumberText,
        DateOnly queueDate,
        long hospitalId,
        long? targetServicePointId,
        long? effectiveRoomId,
        long? effectiveDoctorId,
        DateTime nowUtc,
        DateTime nowLocal,
        CancellationToken ct)
    {
        var rawQueues = await _db.QueueEntries
            .AsNoTracking()
            .Where(q =>
                (q.QueueDate == null || q.QueueDate == queueDate) &&
                (q.QueueType == null || q.QueueType.ToUpper() == "EXAMINATION" || q.QueueType.ToUpper() == "RETURN_CONSULTATION") &&
                (q.Status == null || q.Status.ToUpper() != "CANCELLED"))
            .OrderBy(q => q.Id)
            .ToListAsync(ct);

        var queueVisitIds = rawQueues
            .Where(q => q.VisitId.HasValue)
            .Select(q => q.VisitId!.Value)
            .ToHashSet();

        if (view.Visit is not null)
            queueVisitIds.Add(view.Visit.Id);

        var queueAppointmentIds = rawQueues
            .Where(q => q.AppointmentId.HasValue)
            .Select(q => q.AppointmentId!.Value)
            .ToHashSet();

        queueAppointmentIds.Add(view.Appointment.Id);

        var visits = await _db.Visits
            .AsNoTracking()
            .Where(v =>
                queueVisitIds.Contains(v.Id) ||
                (v.AppointmentId.HasValue && queueAppointmentIds.Contains(v.AppointmentId.Value)))
            .OrderBy(v => v.Id)
            .ToListAsync(ct);

        var visitsById = visits.ToDictionary(v => v.Id);
        var visitsByAppointmentId = visits
            .Where(v => v.AppointmentId.HasValue)
            .GroupBy(v => v.AppointmentId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(v => v.Id).First());

        foreach (var v in visits)
        {
            if (v.AppointmentId.HasValue)
                queueAppointmentIds.Add(v.AppointmentId.Value);
        }

        var appointments = await _db.Appointments
            .AsNoTracking()
            .Include(a => a.Slot)
            .Where(a => queueAppointmentIds.Contains(a.Id))
            .ToListAsync(ct);

        var appointmentsById = appointments.ToDictionary(a => a.Id);

        var allVisitIds = visitsById.Keys.ToHashSet();
        var visitsWithOpenDiagnostics = new HashSet<long>();
        if (allVisitIds.Count > 0)
        {
            var openDiagnosticVisitIds = await _db.QueueEntries
                .AsNoTracking()
                .Where(q =>
                    q.VisitId.HasValue &&
                    allVisitIds.Contains(q.VisitId.Value) &&
                    q.QueueType != null &&
                    q.QueueType.ToUpper() == "DIAGNOSTIC" &&
                    !q.CompletedAt.HasValue &&
                    (q.Status == null || (q.Status.ToUpper() != "COMPLETED" && q.Status.ToUpper() != "CANCELLED")))
                .Select(q => q.VisitId!.Value)
                .ToListAsync(ct);

            visitsWithOpenDiagnostics = openDiagnosticVisitIds.ToHashSet();
        }

        var filteredCandidates = new List<BucketCandidate>();
        foreach (var q in rawQueues)
        {
            Visit? visit = null;
            if (q.VisitId.HasValue)
                visitsById.TryGetValue(q.VisitId.Value, out visit);
            else if (q.AppointmentId.HasValue)
                visitsByAppointmentId.TryGetValue(q.AppointmentId.Value, out visit);

            var apptId = q.AppointmentId ?? visit?.AppointmentId;
            Appointment? appointment = null;
            if (apptId.HasValue)
                appointmentsById.TryGetValue(apptId.Value, out appointment);

            var slot = appointment?.Slot;

            var candVisitStatus = AppointmentBookingRules.NormalizeStatusKey(visit?.Status);
            var candApptStatus = AppointmentBookingRules.NormalizeStatusKey(appointment?.Status);
            if (candVisitStatus == "CANCELLED" || (visit is null && candApptStatus is "CANCELLED" or "NO_SHOW"))
                continue;

            var candidateDate = q.QueueDate
                ?? (visit?.CheckedInAt.HasValue == true
                    ? DateOnly.FromDateTime(visit.CheckedInAt.Value)
                    : slot?.StartsAt.HasValue == true
                        ? DateOnly.FromDateTime(slot.StartsAt.Value)
                        : (DateOnly?)null);

            if (candidateDate != queueDate)
                continue;

            if (!MatchesBucket(q, visit, slot, view.Appointment.Id, targetServicePointId, effectiveRoomId, effectiveDoctorId))
                continue;

            var checkInSortTime = visit?.CheckedInAt
                ?? q.ArrivedAt
                ?? q.ReadyAt
                ?? q.ScheduledAt
                ?? slot?.StartsAt
                ?? DateTime.MaxValue;

            filteredCandidates.Add(new BucketCandidate(q, visit, appointment, slot, checkInSortTime));
        }

        var deduplicatedCandidates = filteredCandidates
            .GroupBy(c => c.Visit is not null
                ? $"V:{c.Visit.Id}"
                : c.Appointment is not null
                    ? $"A:{c.Appointment.Id}"
                    : $"Q:{c.Queue.Id}")
            .Select(g => g.OrderByDescending(c => c.Queue.Id).First())
            .OrderBy(c => c.CheckInSortTime)
            .ThenBy(c => c.Queue.Id)
            .ToList();

        var patientIds = deduplicatedCandidates
            .Select(c => c.Visit?.PatientId ?? c.Appointment?.PatientId)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToHashSet();

        var patientsById = patientIds.Count == 0
            ? new Dictionary<long, Patient>()
            : await _db.Patients
                .AsNoTracking()
                .Where(p => patientIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, ct);

        var nextFallbackNumber = deduplicatedCandidates
            .Select(c => c.Queue.QueueNumber ?? 0)
            .DefaultIfEmpty(0)
            .Max() + 1;

        int.TryParse(ownQueueNumberText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedOwnQueueNumber);

        var orderingItems = new List<QueueOrderingItem>(deduplicatedCandidates.Count);
        foreach (var cand in deduplicatedCandidates)
        {
            var q = cand.Queue;
            var visit = cand.Visit;
            var appt = cand.Appointment;
            var slot = cand.Slot;

            var patId = visit?.PatientId ?? appt?.PatientId;
            patientsById.TryGetValue(patId ?? 0, out var patient);

            int candQueueNumber;
            if (q.QueueNumber is > 0)
            {
                candQueueNumber = q.QueueNumber.Value;
            }
            else if (effectiveQueue is not null && q.Id == effectiveQueue.Id && parsedOwnQueueNumber > 0)
            {
                candQueueNumber = parsedOwnQueueNumber;
            }
            else
            {
                candQueueNumber = nextFallbackNumber++;
            }

            var isScheduled = (visit?.AppointmentId ?? q.AppointmentId).HasValue;
            var visitType = isScheduled ? "SCHEDULED" : "WALK_IN";

            var status = NormalizeEntryStatus(q, visit);
            var priority = ClinicalQueueRules.ResolveEffectivePriority(q.ManualPriority, patient?.DateOfBirth, queueDate);
            var queueType = ClinicalQueueRules.ResolveQueueType(q.QueueType, visit?.Status);
            var presenceStatus = NormalizePresenceStatus(q, visit);
            var arrivalClass = NormalizeArrivalClass(q, visitType);

            var scheduledDateTime = q.ScheduledAt ?? slot?.StartsAt;
            var checkedInDateTime = visit?.CheckedInAt ?? q.ArrivedAt ?? q.ReadyAt ?? q.ScheduledAt ?? slot?.StartsAt;
            var blockedReason = string.IsNullOrWhiteSpace(q.BlockedReason) ? null : q.BlockedReason.Trim();
            var pendingRequiredResults = visit is not null
                && (visitsWithOpenDiagnostics.Contains(visit.Id)
                    || AppointmentBookingRules.NormalizeStatusKey(visit.Status) is "AWAITING_DIAGNOSTIC" or "AWAITING_RESULTS");

            orderingItems.Add(new QueueOrderingItem(
                Status: status,
                Priority: priority,
                PresenceStatus: presenceStatus,
                IsScheduled: isScheduled,
                StoredArrivalClass: arrivalClass,
                ScheduledAt: scheduledDateTime,
                ArrivedAt: q.ArrivedAt ?? visit?.CheckedInAt,
                ReadySince: q.ReadyAt ?? checkedInDateTime,
                BackfilledAt: q.BackfilledAt,
                QueueNumber: candQueueNumber,
                QueueType: queueType,
                BlockedReason: blockedReason,
                PendingRequiredResults: pendingRequiredResults));
        }

        var policySet = await QueueOrderingPolicyLoader.LoadAsync(_db, hospitalId, nowUtc, ct);
        var policy = policySet.ForServicePoint(targetServicePointId);

        var fairnessCounter = 0;
        if (targetServicePointId.HasValue)
        {
            try
            {
                var fairnessRow = await _db.QueueFairnessStates
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        s => s.ServicePointId == targetServicePointId.Value && s.QueueDate == queueDate,
                        ct);

                if (fairnessRow is not null)
                {
                    fairnessCounter = QueueFairnessRotation.ResolveEffectiveCounter(
                        fairnessRow.ConsecutivePriorityStarted,
                        fairnessRow.PolicyVersion,
                        policy);
                }
            }
            catch (NotSupportedException)
            {
                fairnessCounter = 0;
            }
        }

        var orderingResult = ExaminationQueueOrderingEngine.Evaluate(
            orderingItems,
            policy,
            nowLocal,
            fairnessCounter);

        var ordered = orderingResult.OrderedIndices
            .Select(i => new OrderedBucketEntry(
                deduplicatedCandidates[i],
                orderingItems[i],
                orderingResult.Evaluations[i]))
            .ToList();

        var servingEntry = ordered.FirstOrDefault(e => e.Item.Status == "IN_PROGRESS")
            ?? ordered.FirstOrDefault(e => e.Item.Status == "CALLED");
        var servingQueueNumber = servingEntry is not null
            ? servingEntry.Item.QueueNumber.ToString("D3", CultureInfo.InvariantCulture)
            : null;

        var nextEntry = ordered.FirstOrDefault(e => e.Eval.IsEligible)
            ?? ordered.FirstOrDefault(e => e.Item.Status is "READY" or "WAITING" && e.Item.PresenceStatus != "LEFT");
        var nextQueueNumber = nextEntry is not null
            ? nextEntry.Item.QueueNumber.ToString("D3", CultureInfo.InvariantCulture)
            : null;

        var patientPendingRequiredResults = view.Visit is not null
            && (visitsWithOpenDiagnostics.Contains(view.Visit.Id)
                || AppointmentBookingRules.NormalizeStatusKey(view.Visit.Status) is "AWAITING_DIAGNOSTIC" or "AWAITING_RESULTS");

        return new BucketEvaluationSummary(
            Ordered: ordered,
            Policy: policy,
            ServingQueueNumber: servingQueueNumber,
            NextQueueNumber: nextQueueNumber,
            PatientPendingRequiredResults: patientPendingRequiredResults);
    }

    private static bool MatchesBucket(
        QueueEntry q,
        Visit? visit,
        ExaminationSlot? slot,
        long currentAppointmentId,
        long? targetServicePointId,
        long? effectiveRoomId,
        long? effectiveDoctorId)
    {
        var qSpId = q.CurrentServicePointId ?? q.ServicePointId;
        var candRoomId = visit?.ExaminationRoomId ?? slot?.RoomId;
        var candDoctorId = visit?.AssignedDoctorId ?? slot?.DoctorId;

        if (targetServicePointId.HasValue)
        {
            if (qSpId == targetServicePointId.Value)
                return true;

            if (!qSpId.HasValue && effectiveRoomId.HasValue && candRoomId == effectiveRoomId.Value)
                return true;

            if (!qSpId.HasValue && !effectiveRoomId.HasValue && effectiveDoctorId.HasValue && candDoctorId == effectiveDoctorId.Value)
                return true;

            return false;
        }

        if (effectiveRoomId.HasValue)
            return candRoomId == effectiveRoomId.Value;

        if (effectiveDoctorId.HasValue)
            return candDoctorId == effectiveDoctorId.Value;

        return q.AppointmentId == currentAppointmentId;
    }

    private static (
        string Status,
        string StatusLabel,
        string? EstimatedWaitLabel,
        IReadOnlyList<QueueVisitStepDto> VisitSteps,
        string GuidanceNote) BuildProgressPresentation(
        AppointmentListItemDto item,
        AppointmentReadModel.OwnedAppointmentView view,
        QueueEntry? effectiveQueue,
        string visitStatus,
        string queueNumber,
        string roomLabel,
        string doctorName,
        BucketEvaluationSummary bucketEvaluation)
    {
        var patientQueueStatus = effectiveQueue is not null || view.Visit is not null
            ? NormalizeEntryStatus(effectiveQueue, view.Visit)
            : "WAITING";
        var patientPresenceStatus = NormalizePresenceStatus(effectiveQueue, view.Visit);

        if (item.Status == "COMPLETED" || patientQueueStatus == "COMPLETED")
        {
            var completedSteps = new QueueVisitStepDto[]
            {
                new(StepCheckIn, "1. Tiếp đón & Check-in", StateDone, "Đã check-in", "Kiosk / Quầy Tiếp đón"),
                new(StepConsultation, "2. Khám Lâm sàng ban đầu", StateDone, "Đã hoàn tất", "Bác sĩ đã hoàn tất thăm khám."),
                new(StepComplete, "3. Hoàn thành lượt khám", StateDone, "Đã hoàn thành", "Nhận đơn thuốc chỉ khi bác sĩ kê đơn."),
            };
            var completedGuidance = $"Lượt khám tại {roomLabel} đã hoàn tất. Quý khách vui lòng kiểm tra hướng dẫn sau khám, nhận đơn thuốc (nếu bác sĩ có kê đơn) và theo dõi lịch tái khám.";
            return ("COMPLETED", "ĐÃ HOÀN THÀNH", null, completedSteps, completedGuidance);
        }

        var isAwaitingCheckIn = item.Status is "BOOKED" or "CONFIRMED" or "PENDING"
            && view.Visit is null
            && patientQueueStatus is not ("IN_PROGRESS" or "CALLED" or "READY" or "SKIPPED")
            && effectiveQueue?.ArrivedAt is null;

        if (isAwaitingCheckIn)
        {
            var checkInNote = $"Có mặt trước {item.CheckInDeadlineTime ?? "--:--"} tại {item.CheckInCounterLabel ?? "Quầy Tiếp đón"}.";
            var awaitingSteps = new QueueVisitStepDto[]
            {
                new(StepCheckIn, "1. Tiếp đón & Check-in", StateCurrent, "CHƯA CHECK-IN", checkInNote),
                new(StepConsultation, "2. Khám Lâm sàng ban đầu", StateUpcoming, null, "Chỉ bắt đầu khi bác sĩ khám thực tế."),
                new(StepComplete, "3. Hoàn thành lượt khám", StateUpcoming, null, "Nhận đơn thuốc chỉ khi bác sĩ kê đơn."),
            };
            var awaitingGuidance = $"Quý khách vui lòng ngồi tại sảnh chờ trước cửa {roomLabel}. Khi hệ thống gọi số thứ tự {queueNumber}, mời quý khách vào phòng khám. Nếu quý khách có việc rời sảnh chờ quá 15 phút sau khi được gọi, số thứ tự sẽ tự động chuyển sang hàng đợi điều phối trễ.";
            return ("AWAITING_CHECK_IN", "CHỜ CHECK-IN", null, awaitingSteps, awaitingGuidance);
        }

        var step1 = new QueueVisitStepDto(
            StepCheckIn,
            "1. Tiếp đón & Check-in",
            StateDone,
            "Đã check-in",
            "Kiosk / Quầy Tiếp đón");
        var step3 = new QueueVisitStepDto(
            StepComplete,
            "3. Hoàn thành lượt khám",
            StateUpcoming,
            null,
            "Nhận đơn thuốc chỉ khi bác sĩ kê đơn.");

        if (patientQueueStatus == "IN_PROGRESS")
        {
            var step2 = new QueueVisitStepDto(
                StepConsultation,
                "2. Khám Lâm sàng ban đầu",
                StateCurrent,
                "ĐANG KHÁM",
                $"Bác sĩ đang thăm khám tại {roomLabel}.");
            var guidance = $"Quý khách đang trong lượt khám tại {roomLabel}. Vui lòng làm theo hướng dẫn của bác sĩ {doctorName}.";
            return ("WAITING", "ĐANG KHÁM", "Đang trong phòng khám", [step1, step2, step3], guidance);
        }

        if (patientQueueStatus == "CALLED")
        {
            var step2 = new QueueVisitStepDto(
                StepConsultation,
                "2. Khám Lâm sàng ban đầu",
                StateCurrent,
                "ĐÃ GỌI SỐ",
                $"Đã gọi số thứ tự {queueNumber}. Mời quý khách vào {roomLabel}.");
            var guidance = $"Đã đến lượt số thứ tự {queueNumber}. Mời quý khách di chuyển vào {roomLabel} để bắt đầu khám.";
            return ("WAITING", "ĐÃ GỌI SỐ - MỜI VÀO PHÒNG", "Đã đến lượt vào khám", [step1, step2, step3], guidance);
        }

        if (patientQueueStatus == "SKIPPED" || patientPresenceStatus == "LEFT")
        {
            var step2 = new QueueVisitStepDto(
                StepConsultation,
                "2. Khám Lâm sàng ban đầu",
                StateCurrent,
                "TẠM QUA LƯỢT",
                "Quý khách vắng mặt khi gọi số. Vui lòng báo nhân viên để xếp lại lượt.");
            var guidance = $"Số thứ tự {queueNumber} đang ở trạng thái tạm qua lượt. Quý khách vui lòng liên hệ nhân viên tại {roomLabel} hoặc Quầy Tiếp đón để xác nhận có mặt.";
            return ("WAITING", "TẠM QUA LƯỢT", "Tạm qua lượt — vui lòng báo quầy tiếp đón", [step1, step2, step3], guidance);
        }

        if (bucketEvaluation.PatientPendingRequiredResults)
        {
            var step2 = new QueueVisitStepDto(
                StepConsultation,
                "2. Khám Lâm sàng ban đầu",
                StateCurrent,
                "CHỜ KẾT QUẢ CLS",
                "Đang chờ kết quả cận lâm sàng trước khi bác sĩ khám kết luận.");
            var guidance = $"Quý khách đang chờ kết quả cận lâm sàng. Sau khi có đầy đủ kết quả, hệ thống sẽ tự động đưa số thứ tự {queueNumber} vào danh sách đủ điều kiện gọi khám tại {roomLabel}.";
            return ("WAITING", "ĐANG CHỜ KHÁM", "Đang chờ kết quả cận lâm sàng", [step1, step2, step3], guidance);
        }

        var waitingStep2 = new QueueVisitStepDto(
            StepConsultation,
            "2. Khám Lâm sàng ban đầu",
            StateCurrent,
            "CHƯA BẮT ĐẦU",
            "Đã check-in, chưa được gọi. Chỉ bắt đầu khi bác sĩ khám thực tế.");
        var waitingGuidance = $"Quý khách vui lòng ngồi tại sảnh chờ trước cửa {roomLabel}. Khi hệ thống gọi số thứ tự {queueNumber}, mời quý khách vào phòng khám. Nếu quý khách có việc rời sảnh chờ quá 15 phút sau khi được gọi, số thứ tự sẽ tự động chuyển sang hàng đợi điều phối trễ.";

        var estimatedWait = ComputeEstimatedWaitLabel(view, effectiveQueue, bucketEvaluation);
        return ("WAITING", "ĐANG CHỜ KHÁM", estimatedWait, [step1, waitingStep2, step3], waitingGuidance);
    }

    private static string ComputeEstimatedWaitLabel(
        AppointmentReadModel.OwnedAppointmentView view,
        QueueEntry? effectiveQueue,
        BucketEvaluationSummary bucketEvaluation)
    {
        var ordered = bucketEvaluation.Ordered;
        var patientIndex = -1;
        for (var i = 0; i < ordered.Count; i++)
        {
            var e = ordered[i];
            if ((effectiveQueue is not null && e.Candidate.Queue.Id == effectiveQueue.Id) ||
                (view.Visit is not null && e.Candidate.Visit?.Id == view.Visit.Id) ||
                e.Candidate.Appointment?.Id == view.Appointment.Id)
            {
                patientIndex = i;
                break;
            }
        }

        var precedingEntries = patientIndex >= 0
            ? ordered.Take(patientIndex)
            : ordered;

        var aheadCount = precedingEntries.Count(e =>
            e.Item.Status is "IN_PROGRESS" or "CALLED" ||
            (e.Item.Status is "READY" or "WAITING" && e.Item.PresenceStatus != "LEFT"));

        if (aheadCount == 0 && bucketEvaluation.ServingQueueNumber is null)
            return "Chuẩn bị đến lượt khám";

        var durationMinutes = bucketEvaluation.Policy.ExpectedDurationMinutes ?? DefaultDurationMinutes;
        var totalWaitMinutes = Math.Max(1, aheadCount) * durationMinutes;
        return $"Ước tính chờ ~{totalWaitMinutes.ToString(CultureInfo.InvariantCulture)} phút";
    }

    private static string NormalizeEntryStatus(QueueEntry? queueEntry, Visit? visit)
    {
        var queueStatus = AppointmentBookingRules.NormalizeStatusKey(queueEntry?.Status);
        var visitStatus = AppointmentBookingRules.NormalizeStatusKey(visit?.Status);

        if (visit?.CompletedAt.HasValue == true ||
            queueEntry?.CompletedAt.HasValue == true ||
            visitStatus == "COMPLETED" ||
            queueStatus == "COMPLETED")
        {
            return "COMPLETED";
        }

        if (queueStatus is "SKIPPED" or "MISSED" or "NO_SHOW" or "PASSED" ||
            visitStatus == "NO_SHOW")
        {
            return "SKIPPED";
        }

        if (visitStatus is "IN_PROGRESS" or "IN_CONSULTATION" or "SERVING" ||
            queueStatus is "IN_PROGRESS" or "IN_CONSULTATION" or "SERVING" ||
            (queueEntry?.StartedAt.HasValue == true && queueStatus != "WAITING"))
        {
            return "IN_PROGRESS";
        }

        if (visitStatus == "CALLED" ||
            queueStatus == "CALLED" ||
            (queueEntry?.CalledAt.HasValue == true && queueStatus != "WAITING"))
        {
            return "CALLED";
        }

        if (queueStatus == "READY")
        {
            return "READY";
        }

        return "WAITING";
    }

    private static string NormalizePresenceStatus(QueueEntry? queueEntry, Visit? visit)
    {
        var rawPresence = AppointmentBookingRules.NormalizeStatusKey(queueEntry?.PresenceStatus);
        return rawPresence switch
        {
            "LEFT" or "ABSENT" => "LEFT",
            "NOT_ARRIVED" => "NOT_ARRIVED",
            "PRESENT" or "ARRIVED" => "PRESENT",
            _ => visit?.CheckedInAt.HasValue == true || queueEntry?.ArrivedAt.HasValue == true
                ? "PRESENT"
                : "NOT_ARRIVED",
        };
    }

    private static string NormalizeArrivalClass(QueueEntry? queueEntry, string visitType)
    {
        var rawArrival = AppointmentBookingRules.NormalizeStatusKey(queueEntry?.ArrivalClass);
        return rawArrival switch
        {
            "EARLY" => "EARLY",
            "LATE" => "LATE",
            "WALK_IN" => "WALK_IN",
            "ON_TIME_OR_GRACE" or "ON_TIME" => "ON_TIME_OR_GRACE",
            _ => string.Equals(visitType, "WALK_IN", StringComparison.OrdinalIgnoreCase)
                ? "WALK_IN"
                : "ON_TIME_OR_GRACE",
        };
    }

    private sealed record BucketCandidate(
        QueueEntry Queue,
        Visit? Visit,
        Appointment? Appointment,
        ExaminationSlot? Slot,
        DateTime CheckInSortTime);

    private sealed record OrderedBucketEntry(
        BucketCandidate Candidate,
        QueueOrderingItem Item,
        QueueEntryEvaluation Eval);

    private sealed record BucketEvaluationSummary(
        IReadOnlyList<OrderedBucketEntry> Ordered,
        QueueOrderingPolicy Policy,
        string? ServingQueueNumber,
        string? NextQueueNumber,
        bool PatientPendingRequiredResults);
}
