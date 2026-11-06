using System.Globalization;
using MedFlow.Application.DTOs.Visits;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Appointments;
using MedFlow.Application.Services.Visits.Queueing;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Visits;

public sealed partial class ExaminationQueueService : IExaminationQueueService
{
    private static readonly HashSet<string> AllowedRoleCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "RECEPTION",
        "HOSPITAL_ADMIN",
        "SYSTEM_ADMIN",
    };

    private static readonly HashSet<string> AllowedPermissionCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "QUEUE_MANAGE",
        "VISIT_ASSIGN",
        "PATIENT_CHECKIN",
        "HOSPITAL_CONFIG",
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

    private static readonly HashSet<string> ValidPriorities = new(StringComparer.OrdinalIgnoreCase)
    {
        "NORMAL",
        "URGENT",
        "ELDERLY",
        "PREGNANT",
        "RETURN_RESULT",
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

    public ExaminationQueueService(
        IApplicationDbContext db,
        TimeProvider timeProvider)
    {
        _db = db;
        _timeProvider = timeProvider;
    }

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
            normalizedVisitTypeFilter is not null ||
            normalizedKeyword is not null;

        var servicePointDtos = new List<ServicePointQueueDto>(board.Buckets.Count);

        foreach (var boardBucket in board.Buckets)
        {
            var bucket = boardBucket.Bucket;
            if (normalizedServicePointFilter is not null &&
                !MatchesServicePointFilter(bucket, normalizedServicePointFilter))
            {
                continue;
            }

            var sortedFullBucket = boardBucket.Entries.Select(e => e.Dto).ToList();
            var filteredEntries = sortedFullBucket
                .Where(e => MatchesEntryFilters(
                    e,
                    normalizedStatusFilter,
                    normalizedPriorityFilter,
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

            // Update bucket doctor name if bucket had placeholder and an entry resolved a real doctor
            var effectiveDoctorName = bucket.DoctorName;
            if (string.Equals(effectiveDoctorName, "Chưa phân công bác sĩ", StringComparison.OrdinalIgnoreCase))
            {
                var firstEntryDoctor = sortedFullBucket
                    .Select(e => e.DoctorName)
                    .FirstOrDefault(d =>
                        !string.IsNullOrWhiteSpace(d) &&
                        !string.Equals(d, "Chưa phân công bác sĩ", StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(firstEntryDoctor))
                {
                    effectiveDoctorName = firstEntryDoctor;
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
        var nowLocal = AppointmentBookingRules.GetVietnamNow(_timeProvider);
        var today = DateOnly.FromDateTime(nowLocal);

        var hospital = await EnsureQueueViewerAccessAndResolveHospitalAsync(actorUserId, nowUtc, ct);

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

        var allSlots = await _db.ExaminationSlots
            .AsNoTracking()
            .ToListAsync(ct);

        var hospitalSlots = allSlots
            .Where(s =>
                (s.RoomId.HasValue && roomsById.ContainsKey(s.RoomId.Value)) ||
                (s.DoctorId.HasValue && doctorsById.ContainsKey(s.DoctorId.Value)) ||
                (s.ServiceId.HasValue && hospitalServiceIds.Contains(s.ServiceId.Value)))
            .ToList();
        var hospitalSlotsById = hospitalSlots.ToDictionary(s => s.Id);

        var servicePoints = await LoadHospitalExaminationServicePointsAsync(
            hospital.Id,
            roomsById,
            doctorsById,
            ct);

        var buckets = BuildServicePointBuckets(
            servicePoints,
            rooms,
            roomsById,
            departmentsById,
            doctorsById,
            hospitalSlots);

        if (buckets.Count == 0)
        {
            return new QueueBoard(hospital, nowUtc, nowLocal, explicitQueueDate ?? today, []);
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

        var allAppointments = await _db.Appointments
            .AsNoTracking()
            .Where(a => a.SlotId.HasValue)
            .ToListAsync(ct);

        var hospitalAppointmentsById = allAppointments
            .Where(a => a.SlotId.HasValue && hospitalSlotsById.ContainsKey(a.SlotId.Value))
            .ToDictionary(a => a.Id);

        var allVisits = await _db.Visits
            .AsNoTracking()
            .OrderBy(v => v.Id)
            .ToListAsync(ct);

        var hospitalVisits = allVisits
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

        var allQueueEntries = await _db.QueueEntries
            .AsNoTracking()
            .OrderBy(q => q.Id)
            .ToListAsync(ct);

        var hospitalQueueEntries = allQueueEntries
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

        var effectiveQueueDate = ResolveEffectiveQueueDate(explicitQueueDate, today, candidates);

        var dateCandidates = candidates
            .Where(c => c.CandidateDate == effectiveQueueDate)
            .ToList();

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

                var ticketNo = $"{bucket.TicketPrefix}{queueNumber:D3}";
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
                var priority = NormalizeEntryPriority(q, v, patient, effectiveQueueDate);
                var presenceStatus = NormalizePresenceStatus(q, v);
                var arrivalClass = NormalizeArrivalClass(q, visitType);

                var scheduledDateTime = q?.ScheduledAt ?? slot?.StartsAt;
                var checkedInDateTime = v?.CheckedInAt ?? q?.ArrivedAt ?? q?.ReadyAt ?? q?.ScheduledAt ?? slot?.StartsAt;

                var scheduledAt = scheduledDateTime?.ToString("HH:mm", CultureInfo.InvariantCulture);
                var checkedInAt = checkedInDateTime?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "08:00";
                var calledAt = q?.CalledAt?.ToString("HH:mm", CultureInfo.InvariantCulture);
                var startedAt = q?.StartedAt?.ToString("HH:mm", CultureInfo.InvariantCulture);
                var blockedReason = string.IsNullOrWhiteSpace(q?.BlockedReason)
                    ? null
                    : q.BlockedReason.Trim();

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
                    IsScheduled: visitType == "SCHEDULED",
                    StoredArrivalClass: arrivalClass,
                    ScheduledAt: scheduledDateTime,
                    ArrivedAt: v?.CheckedInAt ?? q?.ArrivedAt,
                    ReadySince: q?.ReadyAt ?? checkedInDateTime,
                    BackfilledAt: q?.BackfilledAt,
                    QueueNumber: queueNumber));
            }

            var bucketPolicy = policySet.ForServicePoint(bucket.RawServicePointId);
            var orderingResult = ExaminationQueueOrderingEngine.Evaluate(orderingItems, bucketPolicy, nowLocal);

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
                };

                boardEntries.Add(new QueueBoardEntry(evaluatedDto, rawQueueEntry, eval));
            }

            boardBuckets.Add(new QueueBoardBucket(bucket, bucketPolicy, boardEntries));
        }

        return new QueueBoard(hospital, nowUtc, nowLocal, effectiveQueueDate, boardBuckets);
    }

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

        if (!ValidPriorities.Contains(normalized))
        {
            throw new ValidationException("Mức độ ưu tiên lọc không hợp lệ.");
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
            string.Equals(code, "SYSTEM_ADMIN", StringComparison.OrdinalIgnoreCase));

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

                var hasQueuePermission = permissionCodes.Any(code => AllowedPermissionCodes.Contains(code));
                if (!hasQueuePermission)
                {
                    throw new ForbiddenException(
                        "Vai trò hiện tại chưa được cấp quyền truy cập hàng đợi khám bệnh.");
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
        IReadOnlyList<ExaminationSlot> hospitalSlots)
    {
        var buckets = new List<ServicePointBucket>();
        var coveredRoomIds = new HashSet<long>();

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
                var slotRoomId = hospitalSlots
                    .Where(s => s.DoctorId == doctor.Id && s.RoomId.HasValue && roomsById.ContainsKey(s.RoomId.Value))
                    .OrderByDescending(s => s.StartsAt)
                    .Select(s => s.RoomId)
                    .FirstOrDefault();

                if (slotRoomId.HasValue)
                {
                    roomsById.TryGetValue(slotRoomId.Value, out room);
                }
            }

            if (doctor is null && room is not null)
            {
                var slotDoctorId = hospitalSlots
                    .Where(s => s.RoomId == room.Id && s.DoctorId.HasValue && doctorsById.ContainsKey(s.DoctorId.Value))
                    .OrderByDescending(s => s.StartsAt)
                    .Select(s => s.DoctorId)
                    .FirstOrDefault();

                if (slotDoctorId.HasValue)
                {
                    doctorsById.TryGetValue(slotDoctorId.Value, out doctor);
                }
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

            Doctor? doctor = null;
            var slotDoctorId = hospitalSlots
                .Where(s => s.RoomId == room.Id && s.DoctorId.HasValue && doctorsById.ContainsKey(s.DoctorId.Value))
                .OrderByDescending(s => s.StartsAt)
                .Select(s => s.DoctorId)
                .FirstOrDefault();

            if (slotDoctorId.HasValue)
            {
                doctorsById.TryGetValue(slotDoctorId.Value, out doctor);
            }

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
        var normalizedType = AppointmentBookingRules.NormalizeStatusKey(queueEntry.QueueType);
        if (!string.IsNullOrEmpty(normalizedType) && normalizedType != "EXAMINATION")
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

        return buckets.FirstOrDefault();
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

    private static string NormalizeEntryPriority(
        QueueEntry? queueEntry,
        Visit? visit,
        Patient? patient,
        DateOnly effectiveQueueDate)
    {
        var rawPriority = AppointmentBookingRules.NormalizeStatusKey(queueEntry?.ManualPriority);
        var visitStatus = AppointmentBookingRules.NormalizeStatusKey(visit?.Status);

        if (rawPriority is "URGENT" or "EMERGENCY" or "HIGH" or "CRITICAL")
        {
            return "URGENT";
        }

        if (rawPriority == "RETURN_RESULT" || visitStatus == "RETURN_CONSULTATION")
        {
            return "RETURN_RESULT";
        }

        if (rawPriority == "PREGNANT")
        {
            return "PREGNANT";
        }

        if (rawPriority == "ELDERLY")
        {
            return "ELDERLY";
        }

        if (patient?.DateOfBirth is DateOnly dob)
        {
            var age = effectiveQueueDate.Year - dob.Year;
            if (dob > effectiveQueueDate.AddYears(-age))
            {
                age--;
            }

            if (age >= 65)
            {
                return "ELDERLY";
            }
        }

        return "NORMAL";
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

    private static bool MatchesServicePointFilter(ServicePointBucket bucket, string filter)
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
            if (string.Equals(roomIdStr, filter, StringComparison.OrdinalIgnoreCase) ||
                string.Equals($"sp-{roomIdStr}", filter, StringComparison.OrdinalIgnoreCase) ||
                string.Equals($"sp-room-{roomIdStr}", filter, StringComparison.OrdinalIgnoreCase))
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
        => nowLocal.TimeOfDay < AppointmentBookingRules.AfternoonShiftStart
            ? "Ca sáng (07:00 - 11:30)"
            : "Ca chiều (13:00 - 17:00)";

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
