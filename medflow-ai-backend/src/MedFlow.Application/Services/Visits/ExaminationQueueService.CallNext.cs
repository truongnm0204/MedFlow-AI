using System.Globalization;
using System.Text.Json;
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
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        var (hospital, ownerDoctorId) = await ResolveCallNextActorAsync(actorUserId, nowUtc, ct);

        await using var tx = await _db.BeginTransactionAsync(ct);

        var today = DateOnly.FromDateTime(AppointmentBookingRules.GetVietnamNow(_timeProvider));
        var board = await BuildQueueBoardForHospitalAsync(hospital, today, ct);

        var boardBucket = board.Buckets.FirstOrDefault(b =>
            b.Bucket.RawServicePointId is long raw &&
            (filter == raw.ToString(CultureInfo.InvariantCulture) ||
             string.Equals(filter, $"sp-{raw}", StringComparison.OrdinalIgnoreCase)));

        if (boardBucket is null)
        {
            if (board.Buckets.Any(b => MatchesServicePointFilter(b.Bucket, filter)))
            {
                throw new ConflictException(
                    "Phòng khám chưa được cấu hình điểm phục vụ hàng đợi; không thể gọi bệnh nhân tự động.");
            }

            throw new NotFoundException("Không tìm thấy điểm phục vụ / phòng khám.");
        }

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

        var selected = SelectCallNextCandidate(boardBucket);
        if (selected is null)
        {
            var firstEligible = boardBucket.Entries.FirstOrDefault(e => e.Evaluation.IsEligible);
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

        var normalizedStatus = AppointmentBookingRules.NormalizeStatusKey(target.Status);
        var normalizedPresence = NormalizePresenceStatus(target, visit);

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

    private static QueueBoardEntry? SelectCallNextCandidate(QueueBoardBucket bucket)
        => bucket.Entries.FirstOrDefault(e =>
            e.Evaluation.IsEligible &&
            e.QueueEntry is not null &&
            (e.Evaluation.IsDueProtected || e.Evaluation.CanSafeBackfill));

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
