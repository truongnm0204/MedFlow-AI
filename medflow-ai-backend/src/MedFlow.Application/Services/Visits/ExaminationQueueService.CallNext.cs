using System.Globalization;
using System.Text.Json;
using MedFlow.Application.Common.Time;
using MedFlow.Application.DTOs.Visits;
using MedFlow.Application.Services.Appointments;
using MedFlow.Application.Services.Visits.Queueing;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Visits;

public sealed partial class ExaminationQueueService
{
    public const string CallNextAction = "QUEUE_CALL_NEXT";

    public async Task<CallNextPatientResponseDto> CallNextPatientAsync(
        long actorUserId,
        string servicePointId,
        CallNextPatientRequest? request = null,
        string? ipAddress = null,
        CancellationToken ct = default)
    {
        if (actorUserId <= 0)
        {
            throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");
        }

        var filter = NormalizeAndValidateServicePointFilter(servicePointId);
        if (filter is null)
        {
            throw new ValidationException("Điểm phục vụ / phòng khám là bắt buộc.");
        }

        var notes = ValidateCoordinationNotes(request?.Notes);
        var nowUtc = HospitalClock.GetUtcNow(_timeProvider);
        var today = HospitalClock.GetToday(_timeProvider);

        var (hospital, ownerDoctorId) = await ResolveCallNextActorAsync(actorUserId, nowUtc, ct);

        var preBoard = await BuildQueueBoardForHospitalAsync(hospital, today, ct);
        var preBucket = FindRawBoardBucketForCallNext(preBoard, filter);

        if (preBucket is null)
        {
            if (preBoard.Buckets.Any(b => MatchesServicePointFilter(b.Bucket, filter)))
            {
                throw new ConflictException(
                    "Phòng khám chưa được cấu hình điểm phục vụ hàng đợi; không thể gọi bệnh nhân tự động.");
            }

            throw new NotFoundException("Không tìm thấy điểm phục vụ / phòng khám.");
        }

        if (ownerDoctorId is long preDocId && preBucket.Bucket.DoctorId != preDocId)
        {
            throw new ForbiddenException("Bác sĩ chỉ được gọi bệnh nhân tại điểm phục vụ được phân công.");
        }

        if (!preBucket.Bucket.IsActive)
        {
            throw new ConflictException("Điểm phục vụ đang ngừng hoạt động.");
        }

        var lockRoomId = preBucket.Bucket.RoomId;
        var lockServicePointId = preBucket.Bucket.RawServicePointId!.Value;
        var lockVisitIds = preBucket.Entries
            .Where(e => e.QueueEntry?.VisitId.HasValue == true)
            .Select(e => e.QueueEntry!.VisitId!.Value)
            .Distinct()
            .OrderBy(id => id)
            .ToArray();
        var lockQueueEntryIds = preBucket.Entries
            .Where(e => e.QueueEntry is not null && e.Dto.Status is not "COMPLETED")
            .Select(e => e.QueueEntry!.Id)
            .Distinct()
            .OrderBy(id => id)
            .ToArray();

        await using var tx = await _db.BeginTransactionAsync(ct);

        // ADR-0059 strict lock order: hospital_rooms (06) -> visits (09) -> queue_service_points (10) -> queue_entries (11)
        if (lockRoomId.HasValue)
        {
            await _db.TryLockRoomForUpdateAsync(lockRoomId.Value, ct);
        }

        foreach (var vId in lockVisitIds)
        {
            await _db.TryLockVisitForUpdateAsync(vId, ct);
        }

        await _db.TryLockServicePointForUpdateAsync(lockServicePointId, ct);

        if (lockQueueEntryIds.Length > 0)
        {
            await _db.TryLockQueueEntriesForUpdateAsync(lockQueueEntryIds, ct);
        }

        if (_db is DbContext dbCtx && dbCtx.Database.ProviderName != "Microsoft.EntityFrameworkCore.InMemory")
        {
            dbCtx.ChangeTracker.Clear();
        }

        var board = await BuildQueueBoardForHospitalAsync(hospital, today, ct);
        var boardBucket = FindRawBoardBucketForCallNext(board, filter)
            ?? throw new NotFoundException("Không tìm thấy điểm phục vụ / phòng khám.");

        if (ownerDoctorId is long docId && boardBucket.Bucket.DoctorId != docId)
        {
            throw new ForbiddenException("Bác sĩ chỉ được gọi bệnh nhân tại điểm phục vụ được phân công.");
        }

        if (!boardBucket.Bucket.IsActive)
        {
            throw new ConflictException("Điểm phục vụ đang ngừng hoạt động.");
        }

        if (boardBucket.Entries.Any(e => e.Dto.Status is "CALLED" or "IN_PROGRESS"))
        {
            throw new ConflictException(
                "Phòng khám đang có bệnh nhân được gọi hoặc đang khám. Hoàn tất hoặc đánh dấu qua lượt trước khi gọi bệnh nhân tiếp theo.");
        }

        if (boardBucket.Bucket.RoomId is long physicalRoomId)
        {
            await EnsureRoomHasAvailableCallCapacityAsync(board, physicalRoomId, ct);
        }

        var selected = await SelectVerifiedCallNextCandidateAsync(boardBucket, ct);
        if (selected is null)
        {
            var firstEligible = boardBucket.Entries.FirstOrDefault(e => e.Evaluation.IsEligible);
            if (firstEligible is null && HasPresentUnpaidWaitingCandidate(boardBucket))
            {
                throw new ConflictException(
                    "Bệnh nhân đang chờ tại điểm phục vụ chưa hoàn tất thanh toán phí khám (UNPAID_EXAMINATION_FEE). Vui lòng thanh toán trước khi gọi khám.");
            }

            var outcome = firstEligible is not null ? "HOLD_FOR_SLOT" : "NO_ELIGIBLE_PATIENT";
            var message = firstEligible is not null
                ? firstEligible.Evaluation.HoldReason ?? "Đang giữ khoảng trống cho lịch hẹn được bảo vệ tiếp theo."
                : "Chưa có bệnh nhân đủ điều kiện để gọi tại điểm phục vụ này.";

            return new CallNextPatientResponseDto(
                ServicePointId: boardBucket.Bucket.ServicePointId,
                Outcome: outcome,
                Message: message,
                SelectionReason: null,
                CalledEntry: null,
                PreviousStateVersion: null,
                StateVersion: null,
                UpdatedAtUtc: board.NowUtc,
                Entries: boardBucket.Entries.Select(e => e.Dto).ToList());
        }

        var target = await _db.QueueEntries.FirstOrDefaultAsync(q => q.Id == selected.QueueEntry!.Id, ct)
            ?? throw new ConflictException(
                "Dữ liệu hàng đợi đã thay đổi bởi thao tác khác (state_version mismatch). Vui lòng tải lại hàng đợi.");

        var visit = target.VisitId.HasValue
            ? await _db.Visits.FirstOrDefaultAsync(v => v.Id == target.VisitId.Value, ct)
            : null;

        if (IsTerminalVisit(visit))
        {
            throw new ConflictException("Không thể thao tác trên lượt khám đã hoàn tất, đã hủy hoặc vắng mặt (NO_SHOW).");
        }

        if (visit is not null)
        {
            if (!visit.ExaminationRoomId.HasValue)
            {
                throw new ConflictException("Lượt khám chưa được gán phòng khám lâm sàng, không thể gọi bệnh nhân.");
            }

            if (boardBucket.Bucket.RoomId.HasValue && visit.ExaminationRoomId.Value != boardBucket.Bucket.RoomId.Value)
            {
                throw new ConflictException(
                    "Bệnh nhân vừa được điều chuyển sang phòng khám khác. Vui lòng tải lại hàng đợi.");
            }

            if (selected.Dto.QueueType == ClinicalQueueRules.QueueTypeExamination)
            {
                var gate = await TryCheckExaminationEligibilitySafeAsync(visit.Id, ct);
                if (gate is { IsUnlocked: false })
                {
                    throw new ConflictException(
                        gate.Message ?? "Bệnh nhân chưa hoàn tất thanh toán phí khám trước khi gọi vào phòng khám.");
                }
            }
        }

        var normalizedStatus = AppointmentBookingRules.NormalizeStatusKey(target.Status);
        var normalizedPresence = NormalizePresenceStatus(target, visit);

        if (target.QueueDate.HasValue && target.QueueDate.Value != today)
        {
            throw new ConflictException(
                "Không thể gọi bệnh nhân thuộc hàng đợi của ngày khác. Vui lòng tải lại hàng đợi hôm nay.");
        }

        if (visit is not null)
        {
            var postLockVisitQueues = await _db.QueueEntries
                .AsNoTracking()
                .Where(q =>
                    q.VisitId == visit.Id ||
                    (visit.AppointmentId.HasValue && q.AppointmentId == visit.AppointmentId.Value))
                .ToListAsync(ct);

            if (!VisitPresenceEvaluator.IsPresentToday(visit, postLockVisitQueues, today))
            {
                throw new ConflictException(
                    "[PATIENT_NOT_PRESENT] Bệnh nhân hiện không có mặt tại bệnh viện (đã rời viện hoặc tạm hoãn), không thể gọi vào phòng khám.");
            }

            if (postLockVisitQueues.Any(q =>
                    q.Id != target.Id &&
                    !q.CompletedAt.HasValue &&
                    AppointmentBookingRules.NormalizeStatusKey(q.Status) is "IN_PROGRESS" or "IN_CONSULTATION" or "SERVING"))
            {
                throw new ConflictException(
                    "[PATIENT_BUSY_AT_ANOTHER_POINT] Bệnh nhân đang trong quá trình khám hoặc thực hiện kỹ thuật cận lâm sàng tại một điểm phục vụ khác (IN_PROGRESS), không thể bắt đầu đồng thời tại hai nơi.");
            }
        }

        if (normalizedStatus is not ("WAITING" or "READY") ||
            normalizedPresence != "PRESENT" ||
            target.StateVersion != selected.QueueEntry!.StateVersion)
        {
            throw new ConflictException(
                "Dữ liệu hàng đợi đã thay đổi bởi thao tác khác (state_version mismatch). Vui lòng tải lại hàng đợi.");
        }

        var previousStatus = selected.Dto.Status;
        var previousServingOrder = selected.Dto.ServingOrder;
        var prev = target.StateVersion is > 0 ? target.StateVersion.Value : 1;
        var nextVersion = prev + 1;
        var nowLocal = board.NowLocal;

        target.Status = "CALLED";
        target.ArrivedAt ??= visit?.CheckedInAt ?? nowLocal;
        target.ReadyAt ??= nowLocal;
        target.CalledAt = nowLocal;
        target.StartedAt = null;
        target.BlockedReason = null;
        target.PresenceStatus = "PRESENT";
        target.ArrivalClass = selected.Evaluation.EffectiveArrivalClass;
        target.StateVersion = nextVersion;

        if (visit is not null && selected.Dto.QueueType == ClinicalQueueRules.QueueTypeExamination)
        {
            visit.Status = "CALLED";
        }

        var rawServicePointId = boardBucket.Bucket.RawServicePointId!.Value;
        var policy = boardBucket.Policy;
        var row = await _db.QueueFairnessStates
            .FirstOrDefaultAsync(s => s.ServicePointId == rawServicePointId && s.QueueDate == today, ct);

        var beforeCounter = QueueFairnessRotation.ResolveEffectiveCounter(
            row?.ConsecutivePriorityStarted,
            row?.PolicyVersion,
            policy);
        var isPriorityGroup = QueueFairnessRotation.IsPriorityGroup(
            selected.Dto.Priority,
            selected.Dto.QueueType);
        var afterCounter = selected.Evaluation.IsDueProtected
            ? beforeCounter
            : QueueFairnessRotation.Advance(
                beforeCounter,
                isPriorityGroup,
                policy.PriorityWeight,
                policy.NormalWeight);

        if (row is null)
        {
            _db.QueueFairnessStates.Add(new QueueFairnessState
            {
                ServicePointId = rawServicePointId,
                QueueDate = today,
                ConsecutivePriorityStarted = afterCounter,
                LastStartedQueueEntryId = target.Id,
                PolicyVersion = policy.WalkinPriorityRatioVersion,
            });
        }
        else
        {
            row.ConsecutivePriorityStarted = afterCounter;
            row.LastStartedQueueEntryId = target.Id;
            row.PolicyVersion = policy.WalkinPriorityRatioVersion;
        }

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex is not DbUpdateConcurrencyException && row is null)
        {
            throw new ConflictException("Điểm phục vụ vừa được gọi bởi thao tác khác. Vui lòng tải lại hàng đợi.");
        }

        var afterBoard = await BuildQueueBoardForHospitalAsync(hospital, today, ct);
        var after = FindBoardEntry(afterBoard, target.Id)
            ?? throw new ConflictException("Không thể tải lại hàng đợi sau khi điều phối.");

        var selectionReason = ResolveSelectionReason(selected);

        var payload = new
        {
            action = "CALL_NEXT",
            servicePointId = boardBucket.Bucket.ServicePointId,
            previousStatus,
            currentStatus = after.Entry.Dto.Status,
            previousStateVersion = prev,
            stateVersion = nextVersion,
            queueNumber = after.Entry.Dto.QueueNumber,
            previousServingOrder,
            servingOrder = after.Entry.Dto.ServingOrder,
            selectionReason,
            isPriorityGroup,
            fairness = new
            {
                previousCounter = beforeCounter,
                counter = afterCounter,
                priorityWeight = policy.PriorityWeight,
                normalWeight = policy.NormalWeight,
                policyVersion = policy.WalkinPriorityRatioVersion,
            },
            notes,
        };

        var auditLog = new AuditLog
        {
            ActorUserId = actorUserId,
            HospitalId = board.Hospital.Id,
            Action = CallNextAction,
            EntityType = QueueStatusService.QueueEntryEntityType,
            EntityId = target.Id.ToString(CultureInfo.InvariantCulture),
            Reason = $"Gọi bệnh nhân tiếp theo ({selectionReason})",
            ChangeData = JsonSerializer.Serialize(payload, JsonOptions),
            IpAddress = QueueEntryRequestParsing.ParseIpAddress(ipAddress),
            OccurredAt = board.NowUtc,
        };

        _db.AuditLogs.Add(auditLog);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        await PublishExaminationQueueChangedAsync(
            hospitalId: board.Hospital.Id,
            servicePointId: rawServicePointId,
            roomId: after.Bucket.Bucket.RoomId ?? visit?.ExaminationRoomId,
            appointmentId: target.AppointmentId ?? visit?.AppointmentId,
            ct: ct);

        return new CallNextPatientResponseDto(
            ServicePointId: after.Bucket.Bucket.ServicePointId,
            Outcome: "CALLED",
            Message: $"Đã gọi {after.Entry.Dto.TicketNo} - {after.Entry.Dto.PatientName} vào {after.Entry.Dto.RoomName}.",
            SelectionReason: selectionReason,
            CalledEntry: after.Entry.Dto,
            PreviousStateVersion: prev,
            StateVersion: nextVersion,
            UpdatedAtUtc: board.NowUtc,
            Entries: after.Bucket.Entries.Select(e => e.Dto).ToList());
    }

    private async Task<(Hospital Hospital, long? OwnerDoctorId)> ResolveCallNextActorAsync(
        long actorUserId,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var actorRoleIds = await _db.UserRoles
            .AsNoTracking()
            .Where(ur => ur.UserId == actorUserId)
            .Select(ur => ur.RoleId)
            .ToListAsync(ct);

        var actorRoleCodes = actorRoleIds.Count == 0
            ? []
            : await _db.Roles
                .AsNoTracking()
                .Where(r => actorRoleIds.Contains(r.Id))
                .Select(r => r.Code)
                .ToListAsync(ct);

        if (actorRoleCodes.Any(code => AllowedRoleCodes.Contains(code)))
        {
            var hospital = await EnsureQueueViewerAccessAndResolveHospitalAsync(actorUserId, nowUtc, ct);
            return (hospital, null);
        }

        var (doctorHospital, doctor) = await EnsureDoctorQueueAccessAsync(actorUserId, nowUtc, ct);
        return (doctorHospital, doctor.Id);
    }

    private static QueueBoardBucket? FindRawBoardBucketForCallNext(QueueBoard board, string filter)
        => board.Buckets.FirstOrDefault(b =>
            b.Bucket.RawServicePointId is long raw &&
            (filter == raw.ToString(CultureInfo.InvariantCulture) ||
             string.Equals(filter, $"sp-{raw}", StringComparison.OrdinalIgnoreCase)));

    private async Task EnsureRoomHasAvailableCallCapacityAsync(
        QueueBoard board,
        long roomId,
        CancellationToken ct)
    {
        var room = await _db.HospitalRooms
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == roomId, ct);

        if (room is not null && !string.Equals(room.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException("Phòng khám của điểm phục vụ hiện đang ngừng hoạt động hoặc bảo trì.");
        }

        var effectiveRoomCapacity = room?.Capacity is > 0 ? room.Capacity.Value : 1;
        var activeInRoom = board.Buckets
            .Where(b => b.Bucket.RoomId == roomId)
            .SelectMany(b => b.Entries)
            .Where(e => e.Dto.Status is "CALLED" or "IN_PROGRESS")
            .Select(e => e.Dto.Id)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        if (activeInRoom >= effectiveRoomCapacity)
        {
            throw new ConflictException(
                "Phòng khám đang có bệnh nhân được gọi hoặc đang khám đạt sức chứa tối đa. Hoàn tất hoặc đánh dấu qua lượt trước khi gọi bệnh nhân tiếp theo.");
        }
    }

    private async Task<QueueBoardEntry?> SelectVerifiedCallNextCandidateAsync(
        QueueBoardBucket bucket,
        CancellationToken ct)
    {
        foreach (var entry in bucket.Entries)
        {
            if (!entry.Evaluation.IsEligible ||
                entry.QueueEntry is null ||
                !(entry.Evaluation.IsDueProtected || entry.Evaluation.CanSafeBackfill))
            {
                continue;
            }

            if (entry.QueueEntry.VisitId is long visitId)
            {
                var v = await _db.Visits.AsNoTracking().FirstOrDefaultAsync(x => x.Id == visitId, ct);
                if (v is not null && !v.ExaminationRoomId.HasValue)
                {
                    continue;
                }

                if (v is not null &&
                    bucket.Bucket.RoomId.HasValue &&
                    v.ExaminationRoomId.HasValue &&
                    v.ExaminationRoomId.Value != bucket.Bucket.RoomId.Value)
                {
                    continue;
                }

                if (v is not null)
                {
                    var todayCandidateDate = entry.QueueEntry.QueueDate ?? DateOnly.FromDateTime(AppointmentBookingRules.GetVietnamNow(_timeProvider));
                    var vQueues = await _db.QueueEntries
                        .AsNoTracking()
                        .Where(q =>
                            q.VisitId == visitId ||
                            (v.AppointmentId.HasValue && q.AppointmentId == v.AppointmentId.Value))
                        .ToListAsync(ct);

                    if (!VisitPresenceEvaluator.IsPresentToday(v, vQueues, todayCandidateDate))
                    {
                        continue;
                    }

                    if (vQueues.Any(q =>
                            q.Id != entry.QueueEntry.Id &&
                            !q.CompletedAt.HasValue &&
                            AppointmentBookingRules.NormalizeStatusKey(q.Status) is "IN_PROGRESS" or "IN_CONSULTATION" or "SERVING"))
                    {
                        continue;
                    }
                }
            }

            return entry;
        }

        return null;
    }

    private static bool HasPresentUnpaidWaitingCandidate(QueueBoardBucket bucket)
        => bucket.Entries.Any(e =>
            e.QueueEntry is not null &&
            e.Dto.Status is "WAITING" or "READY" &&
            string.Equals(e.Dto.PresenceStatus, "PRESENT", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(e.Dto.BlockedReason, UnpaidExaminationFeeReason, StringComparison.OrdinalIgnoreCase));

    private static string ResolveSelectionReason(QueueBoardEntry selected)
    {
        if (selected.Evaluation.IsDueProtected)
        {
            return "APPOINTMENT_DUE";
        }

        if (selected.Dto.QueueType == ClinicalQueueRules.QueueTypeReturnConsultation)
        {
            return "RETURN_CONSULTATION";
        }

        if (ClinicalQueueRules.IsElevated(selected.Dto.Priority))
        {
            return "PRIORITY";
        }

        if (selected.Evaluation.ProtectedNextStartsAt.HasValue)
        {
            return "SAFE_BACKFILL";
        }

        return "FIFO";
    }
}
