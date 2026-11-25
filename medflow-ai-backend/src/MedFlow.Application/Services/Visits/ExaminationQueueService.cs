using System.Globalization;
using MedFlow.Application.Common.Time;
using MedFlow.Application.DTOs.Realtime;
using MedFlow.Application.DTOs.Visits;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Appointments;
using MedFlow.Application.Services.Realtime;
using MedFlow.Application.Services.Visits.Queueing;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Visits;

public sealed partial class ExaminationQueueService : IExaminationQueueService
{
    private static readonly HashSet<string> AllowedRoleCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        RoleNames.Reception,
        RoleNames.HospitalAdmin,
        RoleNames.SystemAdmin,
    };

    private static readonly HashSet<string> AllowedPermissionCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        PermissionCodes.QueueManage,
        PermissionCodes.VisitAssign,
        PermissionCodes.PatientCheckin,
        PermissionCodes.HospitalConfig,
        PermissionCodes.UserManage,
    };

    private static readonly HashSet<string> ValidStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "WAITING",
        "READY",
        "CALLED",
        "IN_PROGRESS",
        "COMPLETED",
        "SKIPPED",
    };

    private static readonly HashSet<string> ValidVisitTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "SCHEDULED",
        "WALK_IN",
    };

    private static readonly HashSet<string> ExcludedPointTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "DIAGNOSTIC",
        "RECEPTION",
        "PHARMACY",
    };

    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;
    private readonly IRealtimeNotificationPublisher _realtimePublisher;
    private readonly IVisitChargeSettlementReader _settlementReader;

    public const string UnpaidExaminationFeeReason = "UNPAID_EXAMINATION_FEE";

    public ExaminationQueueService(
        IApplicationDbContext db,
        TimeProvider timeProvider,
        IRealtimeNotificationPublisher? realtimePublisher = null,
        IVisitChargeSettlementReader? settlementReader = null)
    {
        _db = db;
        _timeProvider = timeProvider;
        _realtimePublisher = realtimePublisher ?? NullRealtimeNotificationPublisher.Instance;
        _settlementReader = settlementReader ?? new MedFlow.Application.Services.Payments.VisitChargeSettlementReader(_db, _timeProvider);
    }

    private Task PublishExaminationQueueChangedAsync(
        long hospitalId,
        long? servicePointId,
        long? roomId,
        long? appointmentId,
        CancellationToken ct) =>
        _realtimePublisher.TryPublishQueueChangedSafeAsync(
            new QueueChangedNotification(
                HospitalId: hospitalId,
                ServicePointId: servicePointId,
                RoomId: roomId,
                AppointmentId: appointmentId),
            ct);

    public async Task<ExaminationQueueResponseDto> GetExaminationQueueAsync(
        long actorUserId,
        GetExaminationQueueRequest? request = null,
        CancellationToken ct = default)
    {
        if (actorUserId <= 0)
        {
            throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");
        }

        request ??= new GetExaminationQueueRequest();

        var explicitQueueDate = ParseAndValidateQueueDate(request.QueueDate);
        var normalizedServicePointFilter = NormalizeAndValidateServicePointFilter(request.ServicePointId);
        var normalizedStatusFilter = NormalizeAndValidateStatusFilter(request.Status);
        var normalizedPriorityFilter = NormalizeAndValidatePriorityFilter(request.Priority);
        var normalizedQueueTypeFilter = NormalizeAndValidateQueueTypeFilter(request.QueueType);
        var normalizedVisitTypeFilter = NormalizeAndValidateVisitTypeFilter(request.VisitType);
        var normalizedKeyword = NormalizeAndValidateKeyword(request.Keyword);

        var board = await BuildQueueBoardAsync(actorUserId, explicitQueueDate, ct);

        if (board.Buckets.Count == 0)
        {
            return new ExaminationQueueResponseDto(
                QueueDate: board.EffectiveQueueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                FacilityName: board.Hospital.Name,
                ShiftLabel: ResolveShiftLabel(board.NowLocal),
                GeneratedAtUtc: board.NowUtc,
                Stats: new ExaminationQueueStatsDto(0, 0, 0, 0, 0, 0),
                ServicePoints: []);
        }

        var hasEntryLevelFilters =
            normalizedStatusFilter is not null ||
            normalizedPriorityFilter is not null ||
            normalizedQueueTypeFilter is not null ||
            normalizedVisitTypeFilter is not null ||
            normalizedKeyword is not null;

        var matchedBuckets = FilterBoardBuckets(board.Buckets, normalizedServicePointFilter);
        var servicePointDtos = new List<ServicePointQueueDto>(matchedBuckets.Count);

        foreach (var boardBucket in matchedBuckets)
        {
            var bucket = boardBucket.Bucket;

            var sortedFullBucket = boardBucket.Entries.Select(e => e.Dto).ToList();
            var filteredEntries = sortedFullBucket
                .Where(e => MatchesEntryFilters(
                    e,
                    normalizedStatusFilter,
                    normalizedPriorityFilter,
                    normalizedQueueTypeFilter,
                    normalizedVisitTypeFilter,
                    normalizedKeyword))
                .Select((e, index) => e with { ServingOrder = index + 1 })
                .ToList();

            if (hasEntryLevelFilters && filteredEntries.Count == 0)
            {
                continue;
            }

            if (!hasEntryLevelFilters && !bucket.IsActive && sortedFullBucket.Count == 0 && normalizedServicePointFilter is null)
            {
                continue;
            }

            var entriesForMetrics = hasEntryLevelFilters ? filteredEntries : sortedFullBucket;

            var currentServingTicketNo = sortedFullBucket
                .FirstOrDefault(e => e.Status == "IN_PROGRESS")?.TicketNo
                ?? sortedFullBucket.FirstOrDefault(e => e.Status == "CALLED")?.TicketNo;

            var nextTicketNo = sortedFullBucket
                .FirstOrDefault(e =>
                    e.Status is "READY" or "WAITING" &&
                    !string.Equals(e.PresenceStatus, "LEFT", StringComparison.OrdinalIgnoreCase))
                ?.TicketNo;

            var waitingCount = entriesForMetrics.Count(e => e.Status is "WAITING" or "READY");
            var inProgressCount = entriesForMetrics.Count(e => e.Status is "CALLED" or "IN_PROGRESS");
            var completedCount = entriesForMetrics.Count(e => e.Status == "COMPLETED");
            var skippedCount = entriesForMetrics.Count(e => e.Status == "SKIPPED");

            // Update bucket doctor name only when all doctor-assigned entries in the bucket share a single unambiguous doctor
            var effectiveDoctorName = bucket.DoctorName;
            if (string.Equals(effectiveDoctorName, "Chưa phân công bác sĩ", StringComparison.OrdinalIgnoreCase))
            {
                var distinctEntryDoctors = sortedFullBucket
                    .Select(e => e.DoctorName)
                    .Where(d =>
                        !string.IsNullOrWhiteSpace(d) &&
                        !string.Equals(d, "Chưa phân công bác sĩ", StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (distinctEntryDoctors.Count == 1)
                {
                    effectiveDoctorName = distinctEntryDoctors[0];
                }
            }

            servicePointDtos.Add(new ServicePointQueueDto(
                ServicePointId: bucket.ServicePointId,
                ServicePointName: bucket.ServicePointName,
                RoomCode: bucket.RoomCode,
                RoomName: bucket.RoomName,
                DepartmentName: bucket.DepartmentName,
                DoctorName: effectiveDoctorName,
                IsActive: bucket.IsActive,
                CurrentServingTicketNo: currentServingTicketNo,
                NextTicketNo: nextTicketNo,
                WaitingCount: waitingCount,
                InProgressCount: inProgressCount,
                CompletedCount: completedCount,
                SkippedCount: skippedCount,
                Entries: filteredEntries));
        }


        var allReturnedEntries = servicePointDtos
            .SelectMany(sp => sp.Entries)
            .ToList();

        var stats = new ExaminationQueueStatsDto(
            TotalEntries: allReturnedEntries.Count,
            WaitingCount: allReturnedEntries.Count(e => e.Status is "WAITING" or "READY"),
            CalledOrServingCount: allReturnedEntries.Count(e => e.Status is "CALLED" or "IN_PROGRESS"),
            CompletedCount: allReturnedEntries.Count(e => e.Status == "COMPLETED"),
            SkippedCount: allReturnedEntries.Count(e => e.Status == "SKIPPED"),
            ActiveServicePoints: servicePointDtos.Count(sp => sp.IsActive));

        return new ExaminationQueueResponseDto(
            QueueDate: board.EffectiveQueueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            FacilityName: board.Hospital.Name,
            ShiftLabel: ResolveShiftLabel(board.NowLocal),
            GeneratedAtUtc: board.NowUtc,
            Stats: stats,
            ServicePoints: servicePointDtos);
    }

    private async Task<QueueBoard> BuildQueueBoardAsync(
        long actorUserId,
        DateOnly? explicitQueueDate,
        CancellationToken ct)
    {
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var hospital = await EnsureQueueViewerAccessAndResolveHospitalAsync(actorUserId, nowUtc, ct);

        return await BuildQueueBoardForHospitalAsync(hospital, explicitQueueDate, ct);
    }

    private async Task<QueueBoard> BuildQueueBoardForHospitalAsync(
        Hospital hospital,
        DateOnly? explicitQueueDate,
        CancellationToken ct)
    {
        var nowUtc = HospitalClock.GetUtcNow(_timeProvider);
        var nowLocal = HospitalClock.GetNow(_timeProvider);
        var today = DateOnly.FromDateTime(nowLocal);

        var departments = await _db.Departments
            .AsNoTracking()
            .Where(d => d.HospitalId == hospital.Id)
            .OrderBy(d => d.Id)
            .ToListAsync(ct);
        var departmentsById = departments.ToDictionary(d => d.Id);

        var rooms = await _db.HospitalRooms
            .AsNoTracking()
            .Where(r => r.HospitalId == hospital.Id)
            .OrderBy(r => r.Id)
            .ToListAsync(ct);
        var roomsById = rooms.ToDictionary(r => r.Id);
        var hospitalRoomIds = roomsById.Keys.ToHashSet();

        var doctors = await _db.Doctors
            .AsNoTracking()
            .Include(d => d.Staff)
            .Include(d => d.Specialty)
            .Where(d =>
                d.HospitalId == hospital.Id ||
                (d.HospitalId == null && d.Staff != null && d.Staff.HospitalId == hospital.Id))
            .OrderBy(d => d.Id)
            .ToListAsync(ct);
        var doctorsById = doctors.ToDictionary(d => d.Id);
        var hospitalDoctorIds = doctorsById.Keys.ToHashSet();

        var hospitalServiceIds = (await _db.Services
                .AsNoTracking()
                .Where(s => s.HospitalId == hospital.Id)
                .Select(s => s.Id)
                .ToListAsync(ct))
            .ToHashSet();

        var hospitalStaffIds = (await _db.Staff
                .AsNoTracking()
                .Where(s => s.HospitalId == hospital.Id)
                .Select(s => s.Id)
                .ToListAsync(ct))
            .ToHashSet();

        var servicePoints = await LoadHospitalExaminationServicePointsAsync(
            hospital.Id,
            roomsById,
            doctorsById,
            ct);

        var targetDate = explicitQueueDate ?? await ResolveInitialTargetQueueDateAsync(
            today,
            hospitalRoomIds,
            hospitalDoctorIds,
            hospitalServiceIds,
            hospitalStaffIds,
            servicePoints.Select(sp => sp.Id).ToHashSet(),
            ct);

        var dayStart = targetDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var dayEnd = dayStart.AddDays(1);

        var hospitalSlots = await _db.ExaminationSlots
            .AsNoTracking()
            .Where(s =>
                ((s.RoomId.HasValue && hospitalRoomIds.Contains(s.RoomId.Value)) ||
                 (s.DoctorId.HasValue && hospitalDoctorIds.Contains(s.DoctorId.Value)) ||
                 (s.ServiceId.HasValue && hospitalServiceIds.Contains(s.ServiceId.Value))) &&
                (!s.StartsAt.HasValue || (s.StartsAt.Value >= dayStart && s.StartsAt.Value < dayEnd)))
            .ToListAsync(ct);
        var hospitalSlotsById = hospitalSlots.ToDictionary(s => s.Id);
        var hospitalSlotIds = hospitalSlotsById.Keys.ToHashSet();

        var buckets = BuildServicePointBuckets(
            servicePoints,
            rooms,
            roomsById,
            departmentsById,
            doctorsById,
            hospitalSlots,
            targetDate,
            nowLocal);

        if (buckets.Count == 0)
        {
            return new QueueBoard(hospital, nowUtc, nowLocal, targetDate, []);
        }

        var allRoomHospitalMap = await _db.HospitalRooms
            .AsNoTracking()
            .Where(r => r.HospitalId.HasValue)
            .Select(r => new { r.Id, HospitalId = r.HospitalId!.Value })
            .ToDictionaryAsync(x => x.Id, x => x.HospitalId, ct);

        var allDoctorHospitalMap = await _db.Doctors
            .AsNoTracking()
            .Include(d => d.Staff)
            .Select(d => new
            {
                d.Id,
                HospitalId = d.HospitalId ?? (d.Staff != null ? d.Staff.HospitalId : null),
            })
            .Where(x => x.HospitalId.HasValue)
            .ToDictionaryAsync(x => x.Id, x => x.HospitalId!.Value, ct);

        var allStaffHospitalMap = await _db.Staff
            .AsNoTracking()
            .Where(s => s.HospitalId.HasValue)
            .Select(s => new { s.Id, HospitalId = s.HospitalId!.Value })
            .ToDictionaryAsync(x => x.Id, x => x.HospitalId, ct);

        var dateQueueEntries = await _db.QueueEntries
            .AsNoTracking()
            .Where(q => q.QueueDate == null || q.QueueDate == targetDate)
            .OrderBy(q => q.Id)
            .ToListAsync(ct);

        var referencedVisitIds = dateQueueEntries
            .Where(q => q.VisitId.HasValue)
            .Select(q => q.VisitId!.Value)
            .ToHashSet();

        var candidateVisits = await _db.Visits
            .AsNoTracking()
            .Where(v =>
                (v.CheckedInAt.HasValue && v.CheckedInAt.Value >= dayStart && v.CheckedInAt.Value < dayEnd) ||
                referencedVisitIds.Contains(v.Id) ||
                !v.CheckedInAt.HasValue)
            .OrderBy(v => v.Id)
            .ToListAsync(ct);

        var referencedApptIds = dateQueueEntries
            .Where(q => q.AppointmentId.HasValue)
            .Select(q => q.AppointmentId!.Value)
            .Concat(candidateVisits.Where(v => v.AppointmentId.HasValue).Select(v => v.AppointmentId!.Value))
            .ToHashSet();

        var candidateAppointments = (hospitalSlotIds.Count == 0 && referencedApptIds.Count == 0)
            ? []
            : await _db.Appointments
                .AsNoTracking()
                .Where(a =>
                    (a.SlotId.HasValue && hospitalSlotIds.Contains(a.SlotId.Value)) ||
                    referencedApptIds.Contains(a.Id))
                .ToListAsync(ct);

        var missingSlotIds = candidateAppointments
            .Where(a => a.SlotId.HasValue && !hospitalSlotsById.ContainsKey(a.SlotId.Value))
            .Select(a => a.SlotId!.Value)
            .Distinct()
            .ToList();

        if (missingSlotIds.Count > 0)
        {
            var extraSlots = await _db.ExaminationSlots
                .AsNoTracking()
                .Where(s =>
                    missingSlotIds.Contains(s.Id) &&
                    ((s.RoomId.HasValue && hospitalRoomIds.Contains(s.RoomId.Value)) ||
                     (s.DoctorId.HasValue && hospitalDoctorIds.Contains(s.DoctorId.Value)) ||
                     (s.ServiceId.HasValue && hospitalServiceIds.Contains(s.ServiceId.Value))))
                .ToListAsync(ct);

            foreach (var extraSlot in extraSlots)
            {
                hospitalSlotsById[extraSlot.Id] = extraSlot;
            }
        }

        var hospitalAppointmentsById = candidateAppointments
            .Where(a => a.SlotId.HasValue && hospitalSlotsById.ContainsKey(a.SlotId.Value))
            .ToDictionary(a => a.Id);

        var hospitalVisits = candidateVisits
            .Where(v => IsVisitInHospital(
                v,
                hospital.Id,
                roomsById,
                doctorsById,
                hospitalStaffIds,
                hospitalAppointmentsById,
                allRoomHospitalMap,
                allDoctorHospitalMap,
                allStaffHospitalMap))
            .ToList();
        var hospitalVisitsById = hospitalVisits.ToDictionary(v => v.Id);

        var bucketRawSpIds = buckets
            .Where(b => b.RawServicePointId.HasValue)
            .Select(b => b.RawServicePointId!.Value)
            .ToHashSet();

        var hospitalQueueEntries = dateQueueEntries
            .Where(q => IsExaminationQueueEntryInHospital(
                q,
                hospitalVisitsById,
                hospitalAppointmentsById,
                bucketRawSpIds))
            .ToList();

        var candidates = BuildQueueCandidates(
            hospitalQueueEntries,
            hospitalVisits,
            hospitalVisitsById,
            hospitalAppointmentsById,
            hospitalSlotsById,
            today);

        var effectiveQueueDate = explicitQueueDate ?? targetDate;

        var dateCandidates = candidates
            .Where(c => c.CandidateDate == effectiveQueueDate)
            .ToList();

        var candidateVisitIds = dateCandidates
            .Where(c => c.Visit is not null)
            .Select(c => c.Visit!.Id)
            .Distinct()
            .ToHashSet();

        var visitsWithOpenDiagnostics = await LoadVisitsWithPendingDiagnosticsAsync(
            candidateVisitIds,
            ct);

        var unpaidExaminationVisitIds = await LoadUnpaidExaminationVisitIdsAsync(
            dateCandidates,
            ct);

        var firstTransferFromRoomByVisitId = await LoadFirstTransferOriginRoomByVisitIdAsync(
            candidateVisitIds,
            ct);

        var bucketsByRawSpId = buckets
            .Where(b => b.RawServicePointId.HasValue)
            .GroupBy(b => b.RawServicePointId!.Value)
            .ToDictionary(g => g.Key, g => g.First());

        var bucketsByRoomId = buckets
            .Where(b => b.RoomId.HasValue)
            .GroupBy(b => b.RoomId!.Value)
            .ToDictionary(g => g.Key, g => g.First());

        var patientIds = dateCandidates
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

        var bucketEntriesMap = buckets.ToDictionary(b => b.ServicePointId, _ => new List<ResolvedCandidateEntry>());

        foreach (var candidate in dateCandidates)
        {
            var targetBucket = ResolveCandidateBucket(candidate, buckets);
            if (targetBucket is null)
            {
                continue;
            }

            bucketEntriesMap[targetBucket.ServicePointId].Add(
                new ResolvedCandidateEntry(candidate, targetBucket));
        }

        var policySet = await QueueOrderingPolicyLoader.LoadAsync(_db, hospital.Id, nowUtc, ct);
        Dictionary<long, QueueFairnessState> fairnessByServicePoint;
        try
        {
            fairnessByServicePoint = bucketRawSpIds.Count == 0
                ? new Dictionary<long, QueueFairnessState>()
                : await _db.QueueFairnessStates
                    .AsNoTracking()
                    .Where(s => s.QueueDate == effectiveQueueDate && bucketRawSpIds.Contains(s.ServicePointId))
                    .ToDictionaryAsync(s => s.ServicePointId, ct);
        }
        catch (NotSupportedException)
        {
            fairnessByServicePoint = new Dictionary<long, QueueFairnessState>();
        }

        var boardBuckets = new List<QueueBoardBucket>(buckets.Count);

        foreach (var bucket in buckets)
        {
            var rawBucketItems = bucketEntriesMap[bucket.ServicePointId];

            // Assign fallback queue numbers for visit-only candidates without an explicit QueueNumber
            var nextFallbackNumber = rawBucketItems
                .Select(x => x.Candidate.QueueEntry?.QueueNumber ?? 0)
                .DefaultIfEmpty(0)
                .Max() + 1;

            var rawEntries = new List<(ExaminationQueueEntryDto Dto, QueueEntry? QueueEntry)>(rawBucketItems.Count);
            var orderingItems = new List<QueueOrderingItem>(rawBucketItems.Count);

            foreach (var item in rawBucketItems
                         .OrderBy(x => x.Candidate.CheckInSortTime)
                         .ThenBy(x => x.Candidate.QueueEntry?.Id ?? x.Candidate.Visit?.Id ?? 0))
            {
                var q = item.Candidate.QueueEntry;
                var v = item.Candidate.Visit;
                var appt = item.Candidate.Appointment;
                var slot = item.Candidate.Slot;

                var patientId = v?.PatientId ?? appt?.PatientId;
                patientsById.TryGetValue(patientId ?? 0, out var patient);

                var queueNumber = q?.QueueNumber is > 0
                    ? q.QueueNumber.Value
                    : nextFallbackNumber++;

                var ticketPrefix = ResolveTicketPrefixForEntry(
                    q,
                    v,
                    bucket,
                    bucketsByRawSpId,
                    bucketsByRoomId,
                    firstTransferFromRoomByVisitId);
                var ticketNo = $"{ticketPrefix}{queueNumber:D3}";
                var visitCode = ResolveVisitCode(v, appt, q, effectiveQueueDate);
                var patientName = !string.IsNullOrWhiteSpace(patient?.FullName)
                    ? patient.FullName.Trim()
                    : $"Bệnh nhân #{patientId ?? 0}";
                var patientCode = !string.IsNullOrWhiteSpace(patient?.PatientCode)
                    ? patient.PatientCode.Trim()
                    : $"PAT-{(patientId ?? 0):D4}";
                var visitType = (v?.AppointmentId ?? q?.AppointmentId).HasValue
                    ? "SCHEDULED"
                    : "WALK_IN";

                var entryDoctor = ResolveEntryDoctor(v, slot, bucket, doctorsById);
                var entryRoom = ResolveEntryRoom(v, slot, bucket, roomsById);
                var entryDepartment = ResolveEntryDepartment(entryRoom, entryDoctor, bucket, departmentsById);

                var roomCode = entryRoom?.RoomCode ?? bucket.RoomCode;
                var roomName = !string.IsNullOrWhiteSpace(entryRoom?.Name)
                    ? entryRoom.Name
                    : bucket.RoomName;
                var departmentName = entryDepartment?.Name ?? bucket.DepartmentName;
                var doctorName = entryDoctor is not null
                    ? FormatDoctorName(entryDoctor)
                    : bucket.DoctorName;

                var status = NormalizeEntryStatus(q, v);
                var priority = ClinicalQueueRules.ResolveEffectivePriority(q?.ManualPriority, patient?.DateOfBirth, effectiveQueueDate);
                var queueType = ClinicalQueueRules.ResolveQueueType(q?.QueueType, v?.Status);
                var presenceStatus = NormalizePresenceStatus(q, v);
                var arrivalClass = NormalizeArrivalClass(q, visitType);

                var scheduledDateTime = queueType == ClinicalQueueRules.QueueTypeReturnConsultation && q is not null && !q.ScheduledAt.HasValue
                    ? null
                    : (q?.ScheduledAt ?? slot?.StartsAt);
                var checkedInDateTime = v?.CheckedInAt ?? q?.ArrivedAt ?? q?.ReadyAt ?? q?.ScheduledAt ?? slot?.StartsAt;

                var scheduledAt = scheduledDateTime?.ToString("HH:mm", CultureInfo.InvariantCulture);
                var checkedInAt = checkedInDateTime?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "08:00";
                var calledAt = q?.CalledAt?.ToString("HH:mm", CultureInfo.InvariantCulture);
                var startedAt = q?.StartedAt?.ToString("HH:mm", CultureInfo.InvariantCulture);
                var pendingPaymentSettlement = v is not null
                    && queueType == ClinicalQueueRules.QueueTypeExamination
                    && status is "WAITING" or "READY"
                    && unpaidExaminationVisitIds.Contains(v.Id);
                var blockedReason = !string.IsNullOrWhiteSpace(q?.BlockedReason)
                    ? q.BlockedReason.Trim()
                    : (pendingPaymentSettlement ? UnpaidExaminationFeeReason : null);
                var pendingRequiredResults = v is not null
                    && (visitsWithOpenDiagnostics.Contains(v.Id)
                        || AppointmentBookingRules.NormalizeStatusKey(v.Status) is "AWAITING_DIAGNOSTIC" or "AWAITING_RESULTS");

                var entryId = q is not null
                    ? $"q-{q.Id:D3}"
                    : $"vis-{v!.Id:D3}";

                var dto = new ExaminationQueueEntryDto(
                    Id: entryId,
                    QueueNumber: queueNumber,
                    TicketNo: ticketNo,
                    ServingOrder: 0,
                    VisitCode: visitCode,
                    PatientName: patientName,
                    PatientCode: patientCode,
                    VisitType: visitType,
                    ServicePointId: bucket.ServicePointId,
                    RoomCode: roomCode,
                    RoomName: roomName,
                    DepartmentName: departmentName,
                    DoctorName: doctorName,
                    Status: status,
                    Priority: priority,
                    QueueType: queueType,
                    PresenceStatus: presenceStatus,
                    ArrivalClass: arrivalClass,
                    ScheduledAt: scheduledAt,
                    CheckedInAt: checkedInAt,
                    CalledAt: calledAt,
                    StartedAt: startedAt,
                    BlockedReason: blockedReason,
                    IsBackfilled: q?.BackfilledAt != null,
                    BackfillReason: q?.BackfillReason,
                    BackfillUpdatedAt: q?.BackfilledAt?.ToString("HH:mm", CultureInfo.InvariantCulture));

                rawEntries.Add((dto, q));
                orderingItems.Add(new QueueOrderingItem(
                    Status: status,
                    Priority: priority,
                    PresenceStatus: presenceStatus,
                    IsScheduled: visitType == "SCHEDULED" && scheduledDateTime.HasValue,
                    StoredArrivalClass: arrivalClass,
                    ScheduledAt: scheduledDateTime,
                    ArrivedAt: q?.ArrivedAt ?? v?.CheckedInAt,
                    ReadySince: q?.ReadyAt ?? checkedInDateTime,
                    BackfilledAt: q?.BackfilledAt,
                    QueueNumber: queueNumber,
                    QueueType: queueType,
                    BlockedReason: blockedReason,
                    PendingRequiredResults: pendingRequiredResults,
                    PendingPaymentSettlement: pendingPaymentSettlement));
            }

            var bucketPolicy = policySet.ForServicePoint(bucket.RawServicePointId);
            var counter = bucket.RawServicePointId is long spId
                && fairnessByServicePoint.TryGetValue(spId, out var fairnessRow)
                    ? QueueFairnessRotation.ResolveEffectiveCounter(
                        fairnessRow.ConsecutivePriorityStarted,
                        fairnessRow.PolicyVersion,
                        bucketPolicy)
                    : 0;
            var orderingResult = ExaminationQueueOrderingEngine.Evaluate(orderingItems, bucketPolicy, nowLocal, counter);

            var boardEntries = new List<QueueBoardEntry>(orderingResult.OrderedIndices.Count);
            foreach (var idx in orderingResult.OrderedIndices)
            {
                var eval = orderingResult.Evaluations[idx];
                var (rawDto, rawQueueEntry) = rawEntries[idx];
                var evaluatedDto = rawDto with
                {
                    ServingOrder = eval.ServingOrder,
                    ArrivalClass = eval.EffectiveArrivalClass,
                    IsEligible = eval.IsEligible,
                    CanSafeBackfill = eval.CanSafeBackfill,
                    IsAppointmentProtected = eval.IsAppointmentProtected,
                    HoldReason = eval.HoldReason,
                    QueueGroup = ResolveQueueGroup(rawDto.Status, eval.IsEligible),
                    IneligibleReason = eval.IneligibleReason,
                };

                boardEntries.Add(new QueueBoardEntry(evaluatedDto, rawQueueEntry, eval));
            }

            boardBuckets.Add(new QueueBoardBucket(bucket, bucketPolicy, boardEntries));
        }

        return new QueueBoard(hospital, nowUtc, nowLocal, effectiveQueueDate, boardBuckets);
    }

    private static string ResolveQueueGroup(string status, bool isEligible) => status switch
    {
        "IN_PROGRESS" => QueueGroups.InProgress,
        "CALLED" => QueueGroups.Called,
        "SKIPPED" => QueueGroups.Skipped,
        "COMPLETED" => QueueGroups.Completed,
        _ => isEligible ? QueueGroups.Eligible : QueueGroups.NotEligible,
    };

    private static DateOnly? ParseAndValidateQueueDate(string? queueDate)
    {
        if (string.IsNullOrWhiteSpace(queueDate))
        {
            return null;
        }

        var trimmed = queueDate.Trim();
        if (!DateOnly.TryParseExact(
                trimmed,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsedDate))
        {
            throw new ValidationException("Ngày hàng đợi không hợp lệ (định dạng YYYY-MM-DD).");
        }

        return parsedDate;
    }

    private static string? NormalizeAndValidateServicePointFilter(string? servicePointId)
    {
        if (string.IsNullOrWhiteSpace(servicePointId))
        {
            return null;
        }

        var trimmed = servicePointId.Trim();
        if (string.Equals(trimmed, "ALL", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (trimmed.Length > 60 ||
            trimmed.Contains('<') ||
            trimmed.Contains('>') ||
            trimmed.StartsWith('-'))
        {
            throw new ValidationException("Điểm dịch vụ / phòng khám lọc không hợp lệ.");
        }

        if (trimmed.All(char.IsAsciiDigit))
        {
            if (!long.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var numericId) ||
                numericId <= 0)
            {
                throw new ValidationException("Điểm dịch vụ / phòng khám lọc không hợp lệ.");
            }

            return trimmed;
        }

        foreach (var ch in trimmed)
        {
            if (!(char.IsAsciiLetterOrDigit(ch) || ch is '.' or '_' or '-'))
            {
                throw new ValidationException("Điểm dịch vụ / phòng khám lọc không hợp lệ.");
            }
        }

        return trimmed;
    }

    private static string? NormalizeAndValidateStatusFilter(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return null;
        }

        var normalized = AppointmentBookingRules.NormalizeStatusKey(status);
        if (string.Equals(normalized, "ALL", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        normalized = normalized switch
        {
            "IN_CONSULTATION" => "IN_PROGRESS",
            "MISSED" or "NO_SHOW" => "SKIPPED",
            _ => normalized,
        };

        if (!ValidStatuses.Contains(normalized))
        {
            throw new ValidationException("Trạng thái hàng đợi lọc không hợp lệ.");
        }

        return normalized;
    }

    private static string? NormalizeAndValidatePriorityFilter(string? priority)
    {
        if (string.IsNullOrWhiteSpace(priority))
        {
            return null;
        }

        var normalized = AppointmentBookingRules.NormalizeStatusKey(priority);
        if (string.Equals(normalized, "ALL", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!ClinicalQueueRules.ValidPriorities.Contains(normalized))
        {
            throw new ValidationException("Mức độ ưu tiên lọc không hợp lệ.");
        }

        return normalized;
    }

    private static string? NormalizeAndValidateQueueTypeFilter(string? queueType)
    {
        if (string.IsNullOrWhiteSpace(queueType))
        {
            return null;
        }

        var normalized = AppointmentBookingRules.NormalizeStatusKey(queueType);
        if (string.Equals(normalized, "ALL", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!ClinicalQueueRules.ValidQueueTypes.Contains(normalized))
        {
            throw new ValidationException("Loại lượt khám lọc không hợp lệ.");
        }

        return normalized;
    }

    private static string? NormalizeAndValidateVisitTypeFilter(string? visitType)
    {
        if (string.IsNullOrWhiteSpace(visitType))
        {
            return null;
        }

        var normalized = AppointmentBookingRules.NormalizeStatusKey(visitType);
        if (string.Equals(normalized, "ALL", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!ValidVisitTypes.Contains(normalized))
        {
            throw new ValidationException("Phân loại lượt khám lọc không hợp lệ.");
        }

        return normalized;
    }

    private static string? NormalizeAndValidateKeyword(string? keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return null;
        }

        if (keyword.Contains('<') || keyword.Contains('>'))
        {
            throw new ValidationException("Từ khóa tìm kiếm chứa ký tự không hợp lệ.");
        }

        var trimmed = keyword.Trim();
        if (trimmed.Length > 100)
        {
            throw new ValidationException("Từ khóa tìm kiếm không được vượt quá 100 ký tự.");
        }

        return trimmed;
    }

    private async Task<Hospital> EnsureQueueViewerAccessAndResolveHospitalAsync(
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

        var hasAllowedRole = actorRoleCodes.Any(code => AllowedRoleCodes.Contains(code));
        if (!hasAllowedRole)
        {
            throw new ForbiddenException("Người dùng không có quyền xem hàng đợi khám bệnh.");
        }

        var isSystemAdmin = actorRoleCodes.Any(code =>
            string.Equals(code, RoleNames.SystemAdmin, StringComparison.OrdinalIgnoreCase));

        var rolePermissionIds = actorRoleIds.Count > 0
            ? await _db.RolePermissions
                .AsNoTracking()
                .Where(rp => actorRoleIds.Contains(rp.RoleId))
                .Select(rp => rp.PermissionId)
                .Distinct()
                .ToListAsync(ct)
            : [];

        if (rolePermissionIds.Count == 0)
        {
            throw new ForbiddenException(
                "Vai trò hiện tại chưa được cấp quyền truy cập hàng đợi khám bệnh.");
        }

        var permissionCodes = await _db.Permissions
            .AsNoTracking()
            .Where(p => rolePermissionIds.Contains(p.Id))
            .Select(p => p.Code)
            .ToListAsync(ct);

        var hasQueuePermission = permissionCodes.Any(code => AllowedPermissionCodes.Contains(code));
        if (!hasQueuePermission)
        {
            throw new ForbiddenException(
                "Vai trò hiện tại chưa được cấp quyền truy cập hàng đợi khám bệnh.");
        }

        var actorStaff = await _db.Staff
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == actorUserId, ct);

        if (actorStaff is not null &&
            !string.Equals(actorStaff.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            throw new ForbiddenException("Hồ sơ nhân sự của người dùng không ở trạng thái hoạt động.");
        }

        if (!isSystemAdmin || actorStaff?.HospitalId.HasValue == true)
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

            return hospital;
        }

        return await _db.Hospitals
            .AsNoTracking()
            .Where(h => h.IsActive != false)
            .OrderBy(h => h.Id)
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Hospital", 0);
    }

    private async Task<List<QueueServicePoint>> LoadHospitalExaminationServicePointsAsync(
        long hospitalId,
        IReadOnlyDictionary<long, HospitalRoom> roomsById,
        IReadOnlyDictionary<long, Doctor> doctorsById,
        CancellationToken ct)
    {
        try
        {
            var allPoints = await _db.QueueServicePoints
                .AsNoTracking()
                .OrderBy(sp => sp.Id)
                .ToListAsync(ct);

            return allPoints
                .Where(sp =>
                {
                    var belongsToHospital =
                        sp.HospitalId == hospitalId ||
                        (sp.HospitalId == null &&
                         ((sp.RoomId.HasValue && roomsById.ContainsKey(sp.RoomId.Value)) ||
                          (sp.DoctorId.HasValue && doctorsById.ContainsKey(sp.DoctorId.Value))));

                    if (!belongsToHospital)
                    {
                        return false;
                    }

                    var normalizedPointType = AppointmentBookingRules.NormalizeStatusKey(sp.PointType);
                    if (!string.IsNullOrEmpty(normalizedPointType) &&
                        ExcludedPointTypes.Contains(normalizedPointType))
                    {
                        return false;
                    }

                    if (sp.RoomId.HasValue && roomsById.TryGetValue(sp.RoomId.Value, out var room))
                    {
                        var normalizedRoomType = AppointmentBookingRules.NormalizeStatusKey(room.RoomType);
                        if (!string.IsNullOrEmpty(normalizedRoomType) &&
                            normalizedRoomType != "EXAMINATION" &&
                            ExcludedPointTypes.Contains(normalizedRoomType))
                        {
                            return false;
                        }
                    }

                    return true;
                })
                .ToList();
        }
        catch (NotSupportedException)
        {
            return [];
        }
    }

    private static List<ServicePointBucket> BuildServicePointBuckets(
        IReadOnlyList<QueueServicePoint> servicePoints,
        IReadOnlyList<HospitalRoom> rooms,
        IReadOnlyDictionary<long, HospitalRoom> roomsById,
        IReadOnlyDictionary<long, Department> departmentsById,
        IReadOnlyDictionary<long, Doctor> doctorsById,
        IReadOnlyList<ExaminationSlot> hospitalSlots,
        DateOnly effectiveQueueDate,
        DateTime nowLocal)
    {
        var buckets = new List<ServicePointBucket>();
        var coveredRoomIds = new HashSet<long>();
        var currentShift = HospitalShiftRules.ResolveShift(nowLocal);

        foreach (var sp in servicePoints)
        {
            HospitalRoom? room = null;
            if (sp.RoomId.HasValue)
            {
                roomsById.TryGetValue(sp.RoomId.Value, out room);
            }

            Doctor? doctor = null;
            if (sp.DoctorId.HasValue)
            {
                doctorsById.TryGetValue(sp.DoctorId.Value, out doctor);
            }

            if (room is null && doctor is not null)
            {
                var doctorDateSlots = hospitalSlots
                    .Where(s =>
                        s.DoctorId == doctor.Id &&
                        s.RoomId.HasValue &&
                        roomsById.ContainsKey(s.RoomId.Value) &&
                        s.StartsAt.HasValue &&
                        DateOnly.FromDateTime(s.StartsAt.Value) == effectiveQueueDate &&
                        !string.Equals(s.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var shiftSlots = doctorDateSlots
                    .Where(s => HospitalShiftRules.ResolveShift(s.StartsAt!.Value) == currentShift)
                    .ToList();
                var candidateSlots = shiftSlots.Count > 0 ? shiftSlots : doctorDateSlots;
                var distinctRoomIds = candidateSlots.Select(s => s.RoomId!.Value).Distinct().ToList();

                if (distinctRoomIds.Count == 1)
                {
                    roomsById.TryGetValue(distinctRoomIds[0], out room);
                }
            }

            if (doctor is null && room is not null)
            {
                doctor = ResolveUnambiguousSlotDoctorForRoom(
                    room.Id,
                    hospitalSlots,
                    doctorsById,
                    effectiveQueueDate,
                    currentShift);
            }

            if (room is not null)
            {
                coveredRoomIds.Add(room.Id);
            }

            Department? department = null;
            if (room?.DepartmentId is long roomDeptId)
            {
                departmentsById.TryGetValue(roomDeptId, out department);
            }
            else if (doctor?.Staff?.DepartmentId is long docDeptId)
            {
                departmentsById.TryGetValue(docDeptId, out department);
            }

            var prefix = (char)('A' + (buckets.Count % 26));
            var roomCode = room?.RoomCode ?? $"P-{buckets.Count + 1:D3}";
            var roomName = !string.IsNullOrWhiteSpace(room?.Name)
                ? room.Name
                : $"Buồng khám {roomCode}";
            var servicePointName = !string.IsNullOrWhiteSpace(sp.Name)
                ? sp.Name.Trim()
                : $"Điểm khám {roomName}";
            var departmentName = department?.Name ?? doctor?.Specialty?.Name ?? "Khoa Khám bệnh";
            var doctorName = doctor is not null
                ? FormatDoctorName(doctor)
                : "Chưa phân công bác sĩ";
            var isActive = (sp.IsActive ?? true) &&
                (room is null ||
                 string.IsNullOrWhiteSpace(room.Status) ||
                 string.Equals(room.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase));

            buckets.Add(new ServicePointBucket(
                ServicePointId: $"sp-{sp.Id}",
                RawServicePointId: sp.Id,
                RoomId: room?.Id ?? sp.RoomId,
                DoctorId: doctor?.Id ?? sp.DoctorId,
                ServicePointName: servicePointName,
                RoomCode: roomCode,
                RoomName: roomName,
                DepartmentName: departmentName,
                DoctorName: doctorName,
                IsActive: isActive,
                TicketPrefix: prefix));
        }

        var examinationRooms = rooms
            .Where(r =>
                string.IsNullOrWhiteSpace(r.RoomType) ||
                string.Equals(AppointmentBookingRules.NormalizeStatusKey(r.RoomType), "EXAMINATION", StringComparison.OrdinalIgnoreCase))
            .OrderBy(r => r.Id)
            .ToList();

        foreach (var room in examinationRooms)
        {
            if (coveredRoomIds.Contains(room.Id))
            {
                continue;
            }

            Department? department = null;
            if (room.DepartmentId.HasValue)
            {
                departmentsById.TryGetValue(room.DepartmentId.Value, out department);
            }

            var doctor = ResolveUnambiguousSlotDoctorForRoom(
                room.Id,
                hospitalSlots,
                doctorsById,
                effectiveQueueDate,
                currentShift);

            var prefix = (char)('A' + (buckets.Count % 26));
            var servicePointId = servicePoints.Count == 0
                ? $"sp-{room.Id}"
                : $"sp-room-{room.Id}";
            var roomCode = !string.IsNullOrWhiteSpace(room.RoomCode)
                ? room.RoomCode
                : $"P-{buckets.Count + 1:D3}";
            var roomName = !string.IsNullOrWhiteSpace(room.Name)
                ? room.Name
                : $"Buồng khám {roomCode}";
            var departmentName = department?.Name ?? doctor?.Specialty?.Name ?? "Khoa Khám bệnh";
            var doctorName = doctor is not null
                ? FormatDoctorName(doctor)
                : "Chưa phân công bác sĩ";
            var isActive = string.IsNullOrWhiteSpace(room.Status) ||
                string.Equals(room.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase);

            buckets.Add(new ServicePointBucket(
                ServicePointId: servicePointId,
                RawServicePointId: null,
                RoomId: room.Id,
                DoctorId: doctor?.Id,
                ServicePointName: $"Điểm khám {roomName}",
                RoomCode: roomCode,
                RoomName: roomName,
                DepartmentName: departmentName,
                DoctorName: doctorName,
                IsActive: isActive,
                TicketPrefix: prefix));
        }

        return buckets;
    }

    private static Doctor? ResolveUnambiguousSlotDoctorForRoom(
        long roomId,
        IReadOnlyList<ExaminationSlot> hospitalSlots,
        IReadOnlyDictionary<long, Doctor> doctorsById,
        DateOnly effectiveQueueDate,
        HospitalShift currentShift)
    {
        var roomDateSlots = hospitalSlots
            .Where(s =>
                s.RoomId == roomId &&
                s.DoctorId.HasValue &&
                doctorsById.ContainsKey(s.DoctorId.Value) &&
                s.StartsAt.HasValue &&
                DateOnly.FromDateTime(s.StartsAt.Value) == effectiveQueueDate &&
                !string.Equals(s.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (roomDateSlots.Count == 0)
        {
            return null;
        }

        var shiftSlots = roomDateSlots
            .Where(s => HospitalShiftRules.ResolveShift(s.StartsAt!.Value) == currentShift)
            .ToList();

        var candidateSlots = shiftSlots.Count > 0 ? shiftSlots : roomDateSlots;
        var distinctDoctorIds = candidateSlots
            .Select(s => s.DoctorId!.Value)
            .Distinct()
            .ToList();

        if (distinctDoctorIds.Count != 1)
        {
            return null;
        }

        doctorsById.TryGetValue(distinctDoctorIds[0], out var resolvedDoctor);
        return resolvedDoctor;
    }

    private async Task<DateOnly> ResolveInitialTargetQueueDateAsync(
        DateOnly today,
        IReadOnlySet<long> hospitalRoomIds,
        IReadOnlySet<long> hospitalDoctorIds,
        IReadOnlySet<long> hospitalServiceIds,
        IReadOnlySet<long> hospitalStaffIds,
        IReadOnlySet<long> hospitalSpIds,
        CancellationToken ct)
    {
        var todayStart = today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var todayEnd = todayStart.AddDays(1);

        var hasTodayVisit = await _db.Visits
            .AsNoTracking()
            .AnyAsync(v =>
                v.CheckedInAt.HasValue &&
                v.CheckedInAt.Value >= todayStart &&
                v.CheckedInAt.Value < todayEnd &&
                v.Status != "CANCELLED" &&
                ((v.ExaminationRoomId.HasValue && hospitalRoomIds.Contains(v.ExaminationRoomId.Value)) ||
                 (v.AssignedDoctorId.HasValue && hospitalDoctorIds.Contains(v.AssignedDoctorId.Value)) ||
                 (v.CheckedInByStaffId.HasValue && hospitalStaffIds.Contains(v.CheckedInByStaffId.Value))),
                ct);

        if (hasTodayVisit)
        {
            return today;
        }

        var hasTodayQueue = await _db.QueueEntries
            .AsNoTracking()
            .AnyAsync(q =>
                q.QueueDate == today &&
                q.Status != "CANCELLED" &&
                (q.QueueType == null ||
                 q.QueueType.ToUpper() == "EXAMINATION" ||
                 q.QueueType.ToUpper() == "RETURN_CONSULTATION") &&
                ((q.ServicePointId.HasValue && hospitalSpIds.Contains(q.ServicePointId.Value)) ||
                 (q.CurrentServicePointId.HasValue && hospitalSpIds.Contains(q.CurrentServicePointId.Value)) ||
                 q.VisitId.HasValue ||
                 q.AppointmentId.HasValue),
                ct);

        if (hasTodayQueue)
        {
            return today;
        }

        var latestSpQueueDate = hospitalSpIds.Count == 0
            ? null
            : await _db.QueueEntries
                .AsNoTracking()
                .Where(q =>
                    q.QueueDate.HasValue &&
                    q.Status != "CANCELLED" &&
                    (q.QueueType == null ||
                     q.QueueType.ToUpper() == "EXAMINATION" ||
                     q.QueueType.ToUpper() == "RETURN_CONSULTATION") &&
                    ((q.ServicePointId.HasValue && hospitalSpIds.Contains(q.ServicePointId.Value)) ||
                     (q.CurrentServicePointId.HasValue && hospitalSpIds.Contains(q.CurrentServicePointId.Value))))
                .Select(q => q.QueueDate)
                .MaxAsync(ct);

        if (latestSpQueueDate.HasValue)
        {
            return latestSpQueueDate.Value;
        }

        var latestVisitCheckIn = await _db.Visits
            .AsNoTracking()
            .Where(v =>
                v.CheckedInAt.HasValue &&
                v.Status != "CANCELLED" &&
                ((v.ExaminationRoomId.HasValue && hospitalRoomIds.Contains(v.ExaminationRoomId.Value)) ||
                 (v.AssignedDoctorId.HasValue && hospitalDoctorIds.Contains(v.AssignedDoctorId.Value)) ||
                 (v.CheckedInByStaffId.HasValue && hospitalStaffIds.Contains(v.CheckedInByStaffId.Value))))
            .Select(v => v.CheckedInAt)
            .MaxAsync(ct);

        if (latestVisitCheckIn.HasValue)
        {
            return DateOnly.FromDateTime(latestVisitCheckIn.Value);
        }

        return today;
    }

    private async Task<HashSet<long>> LoadVisitsWithPendingDiagnosticsAsync(
        IReadOnlySet<long> visitIds,
        CancellationToken ct)
    {
        var pending = new HashSet<long>();
        if (visitIds.Count == 0)
        {
            return pending;
        }

        var openDiagQueues = await _db.QueueEntries
            .AsNoTracking()
            .Where(q =>
                q.VisitId.HasValue &&
                visitIds.Contains(q.VisitId.Value) &&
                q.QueueType != null &&
                q.QueueType.ToUpper() == "DIAGNOSTIC" &&
                q.CompletedAt == null &&
                (q.Status == null ||
                 (q.Status.ToUpper() != "COMPLETED" && q.Status.ToUpper() != "CANCELLED")))
            .Select(q => q.VisitId!.Value)
            .ToListAsync(ct);

        foreach (var vid in openDiagQueues)
        {
            pending.Add(vid);
        }

        try
        {
            var activeRequiredOrders = await _db.DiagnosticOrders
                .AsNoTracking()
                .Where(o =>
                    o.VisitId.HasValue &&
                    visitIds.Contains(o.VisitId.Value) &&
                    o.RequiredForConclusion != false &&
                    (o.Status == null || o.Status.ToUpper() != "CANCELLED"))
                .Select(o => new { o.Id, VisitId = o.VisitId!.Value })
                .ToListAsync(ct);

            if (activeRequiredOrders.Count > 0)
            {
                var orderIds = activeRequiredOrders.Select(o => o.Id).ToHashSet();
                var results = await _db.DiagnosticResults
                    .AsNoTracking()
                    .Where(r => r.DiagnosticOrderId.HasValue && orderIds.Contains(r.DiagnosticOrderId.Value))
                    .Select(r => new { OrderId = r.DiagnosticOrderId!.Value, r.Status, r.FinalizedAt })
                    .ToListAsync(ct);

                var finalizedOrderIds = results
                    .Where(r => IsDiagnosticResultFinalized(r.Status, r.FinalizedAt))
                    .Select(r => r.OrderId)
                    .ToHashSet();

                foreach (var order in activeRequiredOrders)
                {
                    if (!finalizedOrderIds.Contains(order.Id))
                    {
                        pending.Add(order.VisitId);
                    }
                }
            }
        }
        catch (NotSupportedException)
        {
            // DiagnosticOrders / DiagnosticResults DbSet is not configured in lightweight test double.
        }

        return pending;
    }

    internal static bool IsDiagnosticResultFinalized(string? status, DateTime? finalizedAt)
    {
        if (finalizedAt.HasValue)
        {
            return true;
        }

        var normalized = AppointmentBookingRules.NormalizeStatusKey(status);
        return normalized is "FINAL" or "FINALIZED" or "COMPLETED" or "VERIFIED";
    }

    private async Task<HashSet<long>> LoadUnpaidExaminationVisitIdsAsync(
        IReadOnlyList<QueueCandidate> dateCandidates,
        CancellationToken ct)
    {
        var unpaid = new HashSet<long>();
        var candidateVisitIds = dateCandidates
            .Where(c => c.Visit is not null)
            .Where(c =>
            {
                var status = NormalizeEntryStatus(c.QueueEntry, c.Visit);
                var queueType = ClinicalQueueRules.ResolveQueueType(c.QueueEntry?.QueueType, c.Visit?.Status);
                return queueType == ClinicalQueueRules.QueueTypeExamination && status is "WAITING" or "READY";
            })
            .Select(c => c.Visit!.Id)
            .Distinct()
            .ToList();

        foreach (var visitId in candidateVisitIds)
        {
            var gate = await TryCheckExaminationEligibilitySafeAsync(visitId, ct);
            if (gate is { IsUnlocked: false })
            {
                unpaid.Add(visitId);
            }
        }

        return unpaid;
    }

    private async Task<MedFlow.Application.Interfaces.Services.VisitChargeGateResult?> TryCheckExaminationEligibilitySafeAsync(
        long visitId,
        CancellationToken ct)
    {
        try
        {
            return await _settlementReader.CheckExaminationEligibilityAsync(visitId, ct);
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private async Task<Dictionary<long, long>> LoadFirstTransferOriginRoomByVisitIdAsync(
        IReadOnlySet<long> visitIds,
        CancellationToken ct)
    {
        var result = new Dictionary<long, long>();
        if (visitIds.Count == 0)
        {
            return result;
        }

        var visitIdStrings = visitIds.Select(id => id.ToString(CultureInfo.InvariantCulture)).ToHashSet();
        var logs = await _db.AuditLogs
            .AsNoTracking()
            .Where(a =>
                a.Action == VisitRoomTransferService.VisitRoomTransferAction &&
                a.EntityId != null &&
                visitIdStrings.Contains(a.EntityId))
            .OrderBy(a => a.OccurredAt)
            .ThenBy(a => a.Id)
            .ToListAsync(ct);

        foreach (var log in logs)
        {
            if (!long.TryParse(log.EntityId, NumberStyles.None, CultureInfo.InvariantCulture, out var visitId) ||
                result.ContainsKey(visitId) ||
                string.IsNullOrWhiteSpace(log.ChangeData))
            {
                continue;
            }

            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(log.ChangeData);
                if (doc.RootElement.TryGetProperty("fromRoomId", out var fromEl) &&
                    fromEl.ValueKind == System.Text.Json.JsonValueKind.Number &&
                    fromEl.TryGetInt64(out var fromRoomId) &&
                    fromRoomId > 0)
                {
                    result[visitId] = fromRoomId;
                }
            }
            catch (System.Text.Json.JsonException)
            {
                // Ignore malformed audit JSON.
            }
        }

        return result;
    }

    private static char ResolveTicketPrefixForEntry(
        QueueEntry? queueEntry,
        Visit? visit,
        ServicePointBucket currentBucket,
        IReadOnlyDictionary<long, ServicePointBucket> bucketsByRawSpId,
        IReadOnlyDictionary<long, ServicePointBucket> bucketsByRoomId,
        IReadOnlyDictionary<long, long> firstTransferFromRoomByVisitId)
    {
        if (queueEntry?.ServicePointId is long originSpId &&
            bucketsByRawSpId.TryGetValue(originSpId, out var originSpBucket))
        {
            return originSpBucket.TicketPrefix;
        }

        if (visit is not null &&
            firstTransferFromRoomByVisitId.TryGetValue(visit.Id, out var originRoomId) &&
            bucketsByRoomId.TryGetValue(originRoomId, out var originRoomBucket))
        {
            return originRoomBucket.TicketPrefix;
        }

        return currentBucket.TicketPrefix;
    }

    private static bool IsVisitInHospital(
        Visit visit,
        long hospitalId,
        IReadOnlyDictionary<long, HospitalRoom> hospitalRoomsById,
        IReadOnlyDictionary<long, Doctor> hospitalDoctorsById,
        IReadOnlySet<long> hospitalStaffIds,
        IReadOnlyDictionary<long, Appointment> hospitalAppointmentsById,
        IReadOnlyDictionary<long, long> allRoomHospitalMap,
        IReadOnlyDictionary<long, long> allDoctorHospitalMap,
        IReadOnlyDictionary<long, long> allStaffHospitalMap)
    {
        if (visit.ExaminationRoomId.HasValue &&
            allRoomHospitalMap.TryGetValue(visit.ExaminationRoomId.Value, out var roomHospId) &&
            roomHospId != hospitalId)
        {
            return false;
        }

        if (visit.AssignedDoctorId.HasValue &&
            allDoctorHospitalMap.TryGetValue(visit.AssignedDoctorId.Value, out var docHospId) &&
            docHospId != hospitalId)
        {
            return false;
        }

        if (visit.CheckedInByStaffId.HasValue &&
            allStaffHospitalMap.TryGetValue(visit.CheckedInByStaffId.Value, out var staffHospId) &&
            staffHospId != hospitalId)
        {
            return false;
        }

        return (visit.ExaminationRoomId.HasValue && hospitalRoomsById.ContainsKey(visit.ExaminationRoomId.Value)) ||
               (visit.AssignedDoctorId.HasValue && hospitalDoctorsById.ContainsKey(visit.AssignedDoctorId.Value)) ||
               (visit.CompletedByDoctorId.HasValue && hospitalDoctorsById.ContainsKey(visit.CompletedByDoctorId.Value)) ||
               (visit.CheckedInByStaffId.HasValue && hospitalStaffIds.Contains(visit.CheckedInByStaffId.Value)) ||
               (visit.AppointmentId.HasValue && hospitalAppointmentsById.ContainsKey(visit.AppointmentId.Value));
    }

    private static bool IsExaminationQueueEntryInHospital(
        QueueEntry queueEntry,
        IReadOnlyDictionary<long, Visit> hospitalVisitsById,
        IReadOnlyDictionary<long, Appointment> hospitalAppointmentsById,
        IReadOnlySet<long> hospitalServicePointIds)
    {
        if (!ClinicalQueueRules.IsClinicalExaminationQueueType(queueEntry.QueueType))
        {
            return false;
        }

        var normalizedQueueStatus = AppointmentBookingRules.NormalizeStatusKey(queueEntry.Status);
        if (normalizedQueueStatus == "CANCELLED")
        {
            return false;
        }

        if (queueEntry.VisitId.HasValue)
        {
            if (!hospitalVisitsById.TryGetValue(queueEntry.VisitId.Value, out var visit))
            {
                return false;
            }

            if (AppointmentBookingRules.NormalizeStatusKey(visit.Status) == "CANCELLED")
            {
                return false;
            }

            return true;
        }

        if (queueEntry.AppointmentId.HasValue &&
            hospitalAppointmentsById.TryGetValue(queueEntry.AppointmentId.Value, out var appt))
        {
            if (AppointmentBookingRules.NormalizeStatusKey(appt.Status) == "CANCELLED")
            {
                return false;
            }

            return true;
        }

        return (queueEntry.CurrentServicePointId.HasValue &&
                hospitalServicePointIds.Contains(queueEntry.CurrentServicePointId.Value)) ||
               (queueEntry.ServicePointId.HasValue &&
                hospitalServicePointIds.Contains(queueEntry.ServicePointId.Value));
    }

    private static List<QueueCandidate> BuildQueueCandidates(
        IReadOnlyList<QueueEntry> hospitalQueueEntries,
        IReadOnlyList<Visit> hospitalVisits,
        IReadOnlyDictionary<long, Visit> hospitalVisitsById,
        IReadOnlyDictionary<long, Appointment> hospitalAppointmentsById,
        IReadOnlyDictionary<long, ExaminationSlot> hospitalSlotsById,
        DateOnly today)
    {
        var candidates = new List<QueueCandidate>();
        var coveredVisitIds = new HashSet<long>();

        // Deduplicate QueueEntries per VisitId (taking the latest QueueEntry if multiple exist)
        var deduplicatedEntries = new List<QueueEntry>();
        foreach (var entry in hospitalQueueEntries.OrderByDescending(q => q.Id))
        {
            if (entry.VisitId.HasValue && !coveredVisitIds.Add(entry.VisitId.Value))
            {
                continue;
            }

            deduplicatedEntries.Add(entry);
        }

        deduplicatedEntries.Reverse();

        foreach (var q in deduplicatedEntries)
        {
            Visit? visit = null;
            if (q.VisitId.HasValue)
            {
                hospitalVisitsById.TryGetValue(q.VisitId.Value, out visit);
            }

            Appointment? appt = null;
            var appointmentId = visit?.AppointmentId ?? q.AppointmentId;
            if (appointmentId.HasValue)
            {
                hospitalAppointmentsById.TryGetValue(appointmentId.Value, out appt);
            }

            ExaminationSlot? slot = null;
            if (appt?.SlotId is long slotId)
            {
                hospitalSlotsById.TryGetValue(slotId, out slot);
            }

            var candidateDate = q.QueueDate
                ?? (visit?.CheckedInAt.HasValue == true
                    ? DateOnly.FromDateTime(visit.CheckedInAt.Value)
                    : slot?.StartsAt.HasValue == true
                        ? DateOnly.FromDateTime(slot.StartsAt.Value)
                        : q.ArrivedAt.HasValue
                            ? DateOnly.FromDateTime(q.ArrivedAt.Value)
                            : q.ScheduledAt.HasValue
                                ? DateOnly.FromDateTime(q.ScheduledAt.Value)
                                : today);

            var checkInSortTime = visit?.CheckedInAt
                ?? q.ArrivedAt
                ?? q.ReadyAt
                ?? q.ScheduledAt
                ?? slot?.StartsAt
                ?? DateTime.MaxValue;

            candidates.Add(new QueueCandidate(
                QueueEntry: q,
                Visit: visit,
                Appointment: appt,
                Slot: slot,
                CandidateDate: candidateDate,
                CheckInSortTime: checkInSortTime));
        }

        foreach (var visit in hospitalVisits)
        {
            if (coveredVisitIds.Contains(visit.Id))
            {
                continue;
            }

            var normalizedVisitStatus = AppointmentBookingRules.NormalizeStatusKey(visit.Status);
            if (normalizedVisitStatus == "CANCELLED")
            {
                continue;
            }

            Appointment? appt = null;
            if (visit.AppointmentId.HasValue)
            {
                hospitalAppointmentsById.TryGetValue(visit.AppointmentId.Value, out appt);
            }

            ExaminationSlot? slot = null;
            if (appt?.SlotId is long slotId)
            {
                hospitalSlotsById.TryGetValue(slotId, out slot);
            }

            var hasRoutingTarget =
                visit.ExaminationRoomId.HasValue ||
                visit.AssignedDoctorId.HasValue ||
                slot?.RoomId.HasValue == true ||
                slot?.DoctorId.HasValue == true;

            if (!hasRoutingTarget)
            {
                continue;
            }

            var candidateDate = visit.CheckedInAt.HasValue
                ? DateOnly.FromDateTime(visit.CheckedInAt.Value)
                : slot?.StartsAt.HasValue == true
                    ? DateOnly.FromDateTime(slot.StartsAt.Value)
                    : today;

            var checkInSortTime = visit.CheckedInAt ?? slot?.StartsAt ?? DateTime.MaxValue;

            candidates.Add(new QueueCandidate(
                QueueEntry: null,
                Visit: visit,
                Appointment: appt,
                Slot: slot,
                CandidateDate: candidateDate,
                CheckInSortTime: checkInSortTime));
        }

        return candidates;
    }

    private static DateOnly ResolveEffectiveQueueDate(
        DateOnly? explicitQueueDate,
        DateOnly today,
        IReadOnlyList<QueueCandidate> candidates)
    {
        if (explicitQueueDate.HasValue)
        {
            return explicitQueueDate.Value;
        }

        if (candidates.Any(c => c.CandidateDate == today))
        {
            return today;
        }

        if (candidates.Count > 0)
        {
            return candidates.Max(c => c.CandidateDate);
        }

        return today;
    }

    private static ServicePointBucket? ResolveCandidateBucket(
        QueueCandidate candidate,
        IReadOnlyList<ServicePointBucket> buckets)
    {
        var q = candidate.QueueEntry;
        var v = candidate.Visit;
        var slot = candidate.Slot;

        // 1. If Visit.ExaminationRoomId is explicitly set, check if it overrides or matches a bucket
        if (v?.ExaminationRoomId is long visitRoomId)
        {
            var currentSpId = q?.CurrentServicePointId ?? q?.ServicePointId;
            var currentSpBucket = currentSpId.HasValue
                ? buckets.FirstOrDefault(b => b.RawServicePointId == currentSpId.Value)
                : null;

            if (currentSpBucket is null || currentSpBucket.RoomId != visitRoomId)
            {
                var roomBucket = buckets.FirstOrDefault(b =>
                                     b.RoomId == visitRoomId &&
                                     v.AssignedDoctorId.HasValue &&
                                     b.DoctorId == v.AssignedDoctorId.Value)
                                 ?? buckets.FirstOrDefault(b => b.RoomId == visitRoomId);
                if (roomBucket is not null)
                {
                    return roomBucket;
                }
            }
            else
            {
                return currentSpBucket;
            }
        }

        // 2. Match by QueueEntry.CurrentServicePointId
        if (q?.CurrentServicePointId is long currentServicePointId)
        {
            var matched = buckets.FirstOrDefault(b => b.RawServicePointId == currentServicePointId);
            if (matched is not null)
            {
                return matched;
            }
        }

        // 3. Match by QueueEntry.ServicePointId
        if (q?.ServicePointId is long servicePointId)
        {
            var matched = buckets.FirstOrDefault(b => b.RawServicePointId == servicePointId);
            if (matched is not null)
            {
                return matched;
            }
        }

        // 4. Match by RoomId (from Visit or ExaminationSlot)
        var effectiveRoomId = v?.ExaminationRoomId ?? slot?.RoomId;
        if (effectiveRoomId.HasValue)
        {
            var matched = buckets.FirstOrDefault(b => b.RoomId == effectiveRoomId.Value);
            if (matched is not null)
            {
                return matched;
            }
        }

        // 5. Match by DoctorId (from Visit or ExaminationSlot)
        var effectiveDoctorId = v?.AssignedDoctorId ?? slot?.DoctorId;
        if (effectiveDoctorId.HasValue)
        {
            var matched = buckets.FirstOrDefault(b => b.DoctorId == effectiveDoctorId.Value);
            if (matched is not null)
            {
                return matched;
            }
        }

        // Unrouted candidates (no ServicePoint, no Room, no Doctor, no Slot) MUST NOT fall into Room 1's bucket.
        return null;
    }

    private static Doctor? ResolveEntryDoctor(
        Visit? visit,
        ExaminationSlot? slot,
        ServicePointBucket bucket,
        IReadOnlyDictionary<long, Doctor> doctorsById)
    {
        if (visit?.AssignedDoctorId is long visitDocId &&
            doctorsById.TryGetValue(visitDocId, out var visitDoc))
        {
            return visitDoc;
        }

        if (bucket.DoctorId is long bucketDocId &&
            doctorsById.TryGetValue(bucketDocId, out var bucketDoc))
        {
            return bucketDoc;
        }

        if (slot?.DoctorId is long slotDocId &&
            doctorsById.TryGetValue(slotDocId, out var slotDoc))
        {
            return slotDoc;
        }

        return null;
    }

    private static HospitalRoom? ResolveEntryRoom(
        Visit? visit,
        ExaminationSlot? slot,
        ServicePointBucket bucket,
        IReadOnlyDictionary<long, HospitalRoom> roomsById)
    {
        if (visit?.ExaminationRoomId is long visitRoomId &&
            roomsById.TryGetValue(visitRoomId, out var visitRoom))
        {
            return visitRoom;
        }

        if (bucket.RoomId is long bucketRoomId &&
            roomsById.TryGetValue(bucketRoomId, out var bucketRoom))
        {
            return bucketRoom;
        }

        if (slot?.RoomId is long slotRoomId &&
            roomsById.TryGetValue(slotRoomId, out var slotRoom))
        {
            return slotRoom;
        }

        return null;
    }

    private static Department? ResolveEntryDepartment(
        HospitalRoom? room,
        Doctor? doctor,
        ServicePointBucket bucket,
        IReadOnlyDictionary<long, Department> departmentsById)
    {
        if (room?.DepartmentId is long roomDeptId &&
            departmentsById.TryGetValue(roomDeptId, out var roomDept))
        {
            return roomDept;
        }

        if (doctor?.Staff?.DepartmentId is long docDeptId &&
            departmentsById.TryGetValue(docDeptId, out var docDept))
        {
            return docDept;
        }

        return null;
    }

    private static string ResolveVisitCode(
        Visit? visit,
        Appointment? appointment,
        QueueEntry? queueEntry,
        DateOnly effectiveQueueDate)
    {
        if (!string.IsNullOrWhiteSpace(visit?.VisitCode))
        {
            return visit.VisitCode.Trim();
        }

        if (visit is not null)
        {
            return $"VIS-{effectiveQueueDate:yyyyMM}-{visit.Id:D4}";
        }

        if (!string.IsNullOrWhiteSpace(appointment?.AppointmentCode))
        {
            return appointment.AppointmentCode.Trim();
        }

        return $"APT-{effectiveQueueDate:yyyyMM}-{appointment?.Id ?? queueEntry?.Id ?? 0:D4}";
    }

    private static string NormalizeEntryStatus(QueueEntry? queueEntry, Visit? visit)
    {
        var queueStatus = AppointmentBookingRules.NormalizeStatusKey(queueEntry?.Status);
        var visitStatus = AppointmentBookingRules.NormalizeStatusKey(visit?.Status);

        if (queueStatus is "SKIPPED" or "MISSED" or "NO_SHOW" or "PASSED" ||
            visitStatus == "NO_SHOW")
        {
            return "SKIPPED";
        }

        if (visitStatus == "CANCELLED" || queueStatus == "CANCELLED")
        {
            return "SKIPPED";
        }

        var visitAwaitingOrReturning = visitStatus is "AWAITING_DIAGNOSTIC" or "AWAITING_RESULTS" or "RETURN_CONSULTATION";

        if (!visitAwaitingOrReturning &&
            (visitStatus == "COMPLETED" ||
             visit?.CompletedAt.HasValue == true ||
             queueStatus == "COMPLETED" ||
             queueEntry?.CompletedAt.HasValue == true))
        {
            return "COMPLETED";
        }

        if (visitAwaitingOrReturning &&
            (queueStatus == "COMPLETED" || queueEntry?.CompletedAt.HasValue == true))
        {
            return "COMPLETED";
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


    private static IReadOnlyList<QueueBoardBucket> FilterBoardBuckets(
        IReadOnlyList<QueueBoardBucket> buckets,
        string? normalizedFilter)
    {
        if (normalizedFilter is null)
        {
            return buckets;
        }

        var primaryMatches = buckets
            .Where(b => MatchesPrimaryServicePointFilter(b.Bucket, normalizedFilter))
            .ToList();
        if (primaryMatches.Count > 0)
        {
            return primaryMatches;
        }

        return buckets
            .Where(b => MatchesRoomIdFallbackFilter(b.Bucket, normalizedFilter))
            .ToList();
    }

    private static bool MatchesServicePointFilter(ServicePointBucket bucket, string filter) =>
        MatchesPrimaryServicePointFilter(bucket, filter) ||
        MatchesRoomIdFallbackFilter(bucket, filter);

    private static bool MatchesPrimaryServicePointFilter(ServicePointBucket bucket, string filter)
    {
        if (string.Equals(bucket.ServicePointId, filter, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(bucket.RoomCode, filter, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (bucket.RawServicePointId.HasValue)
        {
            var rawIdStr = bucket.RawServicePointId.Value.ToString(CultureInfo.InvariantCulture);
            if (string.Equals(rawIdStr, filter, StringComparison.OrdinalIgnoreCase) ||
                string.Equals($"sp-{rawIdStr}", filter, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        if (bucket.RoomId.HasValue)
        {
            var roomIdStr = bucket.RoomId.Value.ToString(CultureInfo.InvariantCulture);
            if (string.Equals($"sp-room-{roomIdStr}", filter, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesRoomIdFallbackFilter(ServicePointBucket bucket, string filter)
    {
        if (bucket.RoomId.HasValue)
        {
            var roomIdStr = bucket.RoomId.Value.ToString(CultureInfo.InvariantCulture);
            if (string.Equals(roomIdStr, filter, StringComparison.OrdinalIgnoreCase) ||
                string.Equals($"sp-{roomIdStr}", filter, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesEntryFilters(
        ExaminationQueueEntryDto entry,
        string? normalizedStatusFilter,
        string? normalizedPriorityFilter,
        string? normalizedQueueTypeFilter,
        string? normalizedVisitTypeFilter,
        string? normalizedKeyword)
    {
        if (normalizedStatusFilter is not null &&
            !string.Equals(entry.Status, normalizedStatusFilter, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (normalizedPriorityFilter is not null &&
            !string.Equals(entry.Priority, normalizedPriorityFilter, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (normalizedQueueTypeFilter is not null &&
            !string.Equals(entry.QueueType, normalizedQueueTypeFilter, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (normalizedVisitTypeFilter is not null &&
            !string.Equals(entry.VisitType, normalizedVisitTypeFilter, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (normalizedKeyword is not null)
        {
            var matched =
                entry.TicketNo.Contains(normalizedKeyword, StringComparison.OrdinalIgnoreCase) ||
                entry.PatientName.Contains(normalizedKeyword, StringComparison.OrdinalIgnoreCase) ||
                entry.PatientCode.Contains(normalizedKeyword, StringComparison.OrdinalIgnoreCase) ||
                entry.VisitCode.Contains(normalizedKeyword, StringComparison.OrdinalIgnoreCase) ||
                entry.RoomCode.Contains(normalizedKeyword, StringComparison.OrdinalIgnoreCase) ||
                entry.DoctorName.Contains(normalizedKeyword, StringComparison.OrdinalIgnoreCase);

            if (!matched)
            {
                return false;
            }
        }

        return true;
    }

    private static string FormatDoctorName(Doctor doctor)
    {
        var fullName = doctor.Staff?.FullName?.Trim();
        var title = doctor.ProfessionalTitle?.Trim();

        if (!string.IsNullOrWhiteSpace(fullName))
        {
            if (!string.IsNullOrWhiteSpace(title) &&
                !fullName.StartsWith(title, StringComparison.OrdinalIgnoreCase))
            {
                return $"{title} {fullName}";
            }

            return fullName;
        }

        if (!string.IsNullOrWhiteSpace(doctor.DoctorCode))
        {
            return $"BS. {doctor.DoctorCode}";
        }

        return $"Bác sĩ #{doctor.Id}";
    }

    private static string ResolveShiftLabel(DateTime nowLocal)
        => HospitalShiftRules.ResolveQueueShiftLabel(nowLocal);

    private sealed record ServicePointBucket(
        string ServicePointId,
        long? RawServicePointId,
        long? RoomId,
        long? DoctorId,
        string ServicePointName,
        string RoomCode,
        string RoomName,
        string DepartmentName,
        string DoctorName,
        bool IsActive,
        char TicketPrefix);

    private sealed record QueueCandidate(
        QueueEntry? QueueEntry,
        Visit? Visit,
        Appointment? Appointment,
        ExaminationSlot? Slot,
        DateOnly CandidateDate,
        DateTime CheckInSortTime);

    private sealed record ResolvedCandidateEntry(
        QueueCandidate Candidate,
        ServicePointBucket Bucket);

    private sealed record QueueBoard(
        Hospital Hospital,
        DateTime NowUtc,
        DateTime NowLocal,
        DateOnly EffectiveQueueDate,
        IReadOnlyList<QueueBoardBucket> Buckets);

    private sealed record QueueBoardBucket(
        ServicePointBucket Bucket,
        QueueOrderingPolicy Policy,
        IReadOnlyList<QueueBoardEntry> Entries);

    private sealed record QueueBoardEntry(
        ExaminationQueueEntryDto Dto,
        QueueEntry? QueueEntry,
        QueueEntryEvaluation Evaluation);
}
