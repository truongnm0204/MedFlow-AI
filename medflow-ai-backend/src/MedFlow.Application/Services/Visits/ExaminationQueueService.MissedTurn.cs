using System.Globalization;
using System.Text.Json;
using MedFlow.Application.DTOs.Visits;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Visits;

public sealed partial class ExaminationQueueService
{
    public const string MissedTurnAction = "QUEUE_MISSED_TURN";
    public const string RestoreTurnAction = "QUEUE_TURN_RESTORED";

    public async Task<QueueTurnActionResponseDto> MarkMissedTurnAsync(
        long actorUserId,
        string idOrCode,
        MarkMissedTurnRequest request,
        string? ipAddress = null,
        CancellationToken ct = default)
    {
        if (actorUserId <= 0)
        {
            throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");
        }

        if (request is null)
        {
            throw new ValidationException("Dữ liệu điều phối không hợp lệ.");
        }

        var (numericId, code) = QueueEntryRequestParsing.ParseIdentifier(idOrCode);

        var normalizedServicePointId = string.IsNullOrWhiteSpace(request.ServicePointId)
            ? null
            : NormalizeAndValidateServicePointFilter(request.ServicePointId);
        if (normalizedServicePointId is null)
        {
            throw new ValidationException("Điểm phục vụ / phòng khám là bắt buộc.");
        }

        var reason = ValidateCoordinationReason(request.Reason);
        var notes = ValidateCoordinationNotes(request.Notes);

        if (request.ExpectedStateVersion is < 1)
        {
            throw new ValidationException("Phiên bản trạng thái (ExpectedStateVersion) phải lớn hơn hoặc bằng 1.");
        }

        await using var tx = await _db.BeginTransactionAsync(ct);

        var target = await FindExaminationQueueEntryForActionAsync(numericId, code, ct);
        var board = await BuildQueueBoardAsync(actorUserId, target.QueueDate, ct);
        var before = FindBoardEntry(board, target.Id)
            ?? throw new NotFoundException("Không tìm thấy lượt xếp hàng khám.");
        var (bucket, entry) = before;

        if (board.EffectiveQueueDate != DateOnly.FromDateTime(board.NowLocal))
        {
            throw new ConflictException("Chỉ xử lý qua lượt / khôi phục lượt cho hàng đợi của ngày hiện tại.");
        }

        if (!MatchesServicePointFilter(bucket.Bucket, normalizedServicePointId))
        {
            throw new ConflictException("Lượt khám không thuộc điểm phục vụ / phòng khám đã chọn.");
        }

        var visit = target.VisitId.HasValue
            ? await _db.Visits.FirstOrDefaultAsync(v => v.Id == target.VisitId.Value, ct)
            : null;
        if (IsTerminalVisit(visit))
        {
            throw new ConflictException("Không thể thao tác trên lượt khám đã hoàn tất, đã hủy hoặc vắng mặt (NO_SHOW).");
        }

        var prevVersion = target.StateVersion is > 0 ? target.StateVersion.Value : 1;
        if (request.ExpectedStateVersion.HasValue && request.ExpectedStateVersion.Value != prevVersion)
        {
            throw new ConflictException("Dữ liệu hàng đợi đã thay đổi bởi thao tác khác (state_version mismatch). Vui lòng tải lại hàng đợi.");
        }

        var previousStatus = entry.Dto.Status;
        if (previousStatus != "CALLED")
        {
            throw new ConflictException("Chỉ đánh dấu Qua lượt cho bệnh nhân đang ở trạng thái Đã gọi (CALLED).");
        }

        var previousPresenceStatus = entry.Dto.PresenceStatus;
        var previousArrivedAt = target.ArrivedAt;
        var previousArrivalClass = entry.Dto.ArrivalClass;
        var previousServingOrder = entry.Dto.ServingOrder;

        var nextVersion = prevVersion + 1;
        target.Status = "SKIPPED";
        target.PresenceStatus = "LEFT";
        target.BlockedReason = reason;
        target.StartedAt = null;
        target.StateVersion = nextVersion;

        if (visit is not null && entry.Dto.QueueType == ClinicalQueueRules.QueueTypeExamination)
        {
            visit.Status = "WAITING";
        }

        await _db.SaveChangesAsync(ct);

        var afterBoard = await BuildQueueBoardAsync(actorUserId, target.QueueDate, ct);
        var after = FindBoardEntry(afterBoard, target.Id)
            ?? throw new ConflictException("Không thể tải lại hàng đợi sau khi điều phối.");

        var changePayload = new
        {
            action = "MISSED_TURN",
            previousStatus,
            currentStatus = after.Entry.Dto.Status,
            previousPresenceStatus,
            presenceStatus = after.Entry.Dto.PresenceStatus,
            previousArrivedAt,
            arrivedAt = target.ArrivedAt,
            previousArrivalClass,
            arrivalClass = after.Entry.Dto.ArrivalClass,
            previousStateVersion = prevVersion,
            stateVersion = nextVersion,
            queueNumber = after.Entry.Dto.QueueNumber,
            servicePointId = bucket.Bucket.ServicePointId,
            previousServingOrder,
            servingOrder = after.Entry.Dto.ServingOrder,
            isAppointmentProtected = after.Entry.Evaluation.IsAppointmentProtected,
            graceMinutes = bucket.Policy.GraceMinutes,
            blockedReason = target.BlockedReason,
            notes,
        };

        var auditLog = new AuditLog
        {
            ActorUserId = actorUserId,
            HospitalId = board.Hospital.Id,
            Action = MissedTurnAction,
            EntityType = QueueStatusService.QueueEntryEntityType,
            EntityId = target.Id.ToString(CultureInfo.InvariantCulture),
            Reason = reason,
            ChangeData = JsonSerializer.Serialize(changePayload, JsonOptions),
            IpAddress = QueueEntryRequestParsing.ParseIpAddress(ipAddress),
            OccurredAt = board.NowUtc,
        };

        _db.AuditLogs.Add(auditLog);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new QueueTurnActionResponseDto(
            QueueEntryId: after.Entry.Dto.Id,
            TicketNo: after.Entry.Dto.TicketNo,
            QueueNumber: after.Entry.Dto.QueueNumber,
            VisitCode: after.Entry.Dto.VisitCode,
            PatientCode: after.Entry.Dto.PatientCode,
            PatientName: after.Entry.Dto.PatientName,
            ServicePointId: after.Entry.Dto.ServicePointId,
            Action: "MISSED_TURN",
            PreviousStatus: previousStatus,
            CurrentStatus: after.Entry.Dto.Status,
            PresenceStatus: after.Entry.Dto.PresenceStatus,
            ArrivalClass: after.Entry.Dto.ArrivalClass,
            PreviousServingOrder: previousServingOrder,
            ServingOrder: after.Entry.Dto.ServingOrder,
            IsAppointmentProtected: after.Entry.Evaluation.IsAppointmentProtected,
            BlockedReason: target.BlockedReason,
            Reason: reason,
            Notes: notes,
            PreviousStateVersion: prevVersion,
            StateVersion: target.StateVersion!.Value,
            UpdatedAtUtc: board.NowUtc,
            UpdatedEntry: after.Entry.Dto);
    }

    public async Task<QueueTurnActionResponseDto> RestoreSkippedTurnAsync(
        long actorUserId,
        string idOrCode,
        RestoreQueueTurnRequest request,
        string? ipAddress = null,
        CancellationToken ct = default)
    {
        if (actorUserId <= 0)
        {
            throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");
        }

        if (request is null)
        {
            throw new ValidationException("Dữ liệu điều phối không hợp lệ.");
        }

        var (numericId, code) = QueueEntryRequestParsing.ParseIdentifier(idOrCode);

        var normalizedServicePointId = string.IsNullOrWhiteSpace(request.ServicePointId)
            ? null
            : NormalizeAndValidateServicePointFilter(request.ServicePointId);
        if (normalizedServicePointId is null)
        {
            throw new ValidationException("Điểm phục vụ / phòng khám là bắt buộc.");
        }

        var reason = ValidateCoordinationReason(request.Reason);
        var notes = ValidateCoordinationNotes(request.Notes);

        if (request.ExpectedStateVersion is < 1)
        {
            throw new ValidationException("Phiên bản trạng thái (ExpectedStateVersion) phải lớn hơn hoặc bằng 1.");
        }

        if (!request.ConfirmPresent)
        {
            throw new ValidationException("Cần xác nhận bệnh nhân đã có mặt (PRESENT) trước khi khôi phục lượt khám.");
        }

        await using var tx = await _db.BeginTransactionAsync(ct);

        var target = await FindExaminationQueueEntryForActionAsync(numericId, code, ct);
        var board = await BuildQueueBoardAsync(actorUserId, target.QueueDate, ct);
        var before = FindBoardEntry(board, target.Id)
            ?? throw new NotFoundException("Không tìm thấy lượt xếp hàng khám.");
        var (bucket, entry) = before;

        if (board.EffectiveQueueDate != DateOnly.FromDateTime(board.NowLocal))
        {
            throw new ConflictException("Chỉ xử lý qua lượt / khôi phục lượt cho hàng đợi của ngày hiện tại.");
        }

        if (!MatchesServicePointFilter(bucket.Bucket, normalizedServicePointId))
        {
            throw new ConflictException("Lượt khám không thuộc điểm phục vụ / phòng khám đã chọn.");
        }

        var visit = target.VisitId.HasValue
            ? await _db.Visits.FirstOrDefaultAsync(v => v.Id == target.VisitId.Value, ct)
            : null;
        if (IsTerminalVisit(visit))
        {
            throw new ConflictException("Không thể thao tác trên lượt khám đã hoàn tất, đã hủy hoặc vắng mặt (NO_SHOW).");
        }

        var prevVersion = target.StateVersion is > 0 ? target.StateVersion.Value : 1;
        if (request.ExpectedStateVersion.HasValue && request.ExpectedStateVersion.Value != prevVersion)
        {
            throw new ConflictException("Dữ liệu hàng đợi đã thay đổi bởi thao tác khác (state_version mismatch). Vui lòng tải lại hàng đợi.");
        }

        var previousStatus = entry.Dto.Status;
        if (previousStatus != "SKIPPED")
        {
            throw new ConflictException("Chỉ khôi phục được lượt khám đang ở trạng thái Qua lượt (SKIPPED).");
        }

        var previousPresenceStatus = entry.Dto.PresenceStatus;
        var previousArrivedAt = target.ArrivedAt;
        var previousArrivalClass = entry.Dto.ArrivalClass;
        var previousServingOrder = entry.Dto.ServingOrder;

        var nextVersion = prevVersion + 1;
        target.Status = "READY";
        target.PresenceStatus = "PRESENT";
        target.ArrivedAt = board.NowLocal;
        target.ReadyAt = board.NowLocal;
        target.CalledAt = null;
        target.StartedAt = null;
        target.BlockedReason = null;
        target.BackfilledAt = null;
        target.BackfillReason = null;
        target.StateVersion = nextVersion;

        if (visit is not null && entry.Dto.QueueType == ClinicalQueueRules.QueueTypeExamination)
        {
            visit.Status = "WAITING";
        }

        await _db.SaveChangesAsync(ct);

        var afterBoard = await BuildQueueBoardAsync(actorUserId, target.QueueDate, ct);
        var after = FindBoardEntry(afterBoard, target.Id)
            ?? throw new ConflictException("Không thể tải lại hàng đợi sau khi điều phối.");

        target.ArrivalClass = after.Entry.Evaluation.EffectiveArrivalClass;

        var changePayload = new
        {
            action = "RESTORE_TURN",
            previousStatus,
            currentStatus = after.Entry.Dto.Status,
            previousPresenceStatus,
            presenceStatus = after.Entry.Dto.PresenceStatus,
            previousArrivedAt,
            arrivedAt = target.ArrivedAt,
            previousArrivalClass,
            arrivalClass = after.Entry.Dto.ArrivalClass,
            previousStateVersion = prevVersion,
            stateVersion = nextVersion,
            queueNumber = after.Entry.Dto.QueueNumber,
            servicePointId = bucket.Bucket.ServicePointId,
            previousServingOrder,
            servingOrder = after.Entry.Dto.ServingOrder,
            isAppointmentProtected = after.Entry.Evaluation.IsAppointmentProtected,
            graceMinutes = bucket.Policy.GraceMinutes,
            blockedReason = target.BlockedReason,
            notes,
        };

        var auditLog = new AuditLog
        {
            ActorUserId = actorUserId,
            HospitalId = board.Hospital.Id,
            Action = RestoreTurnAction,
            EntityType = QueueStatusService.QueueEntryEntityType,
            EntityId = target.Id.ToString(CultureInfo.InvariantCulture),
            Reason = reason,
            ChangeData = JsonSerializer.Serialize(changePayload, JsonOptions),
            IpAddress = QueueEntryRequestParsing.ParseIpAddress(ipAddress),
            OccurredAt = board.NowUtc,
        };

        _db.AuditLogs.Add(auditLog);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new QueueTurnActionResponseDto(
            QueueEntryId: after.Entry.Dto.Id,
            TicketNo: after.Entry.Dto.TicketNo,
            QueueNumber: after.Entry.Dto.QueueNumber,
            VisitCode: after.Entry.Dto.VisitCode,
            PatientCode: after.Entry.Dto.PatientCode,
            PatientName: after.Entry.Dto.PatientName,
            ServicePointId: after.Entry.Dto.ServicePointId,
            Action: "RESTORE_TURN",
            PreviousStatus: previousStatus,
            CurrentStatus: after.Entry.Dto.Status,
            PresenceStatus: after.Entry.Dto.PresenceStatus,
            ArrivalClass: after.Entry.Dto.ArrivalClass,
            PreviousServingOrder: previousServingOrder,
            ServingOrder: after.Entry.Dto.ServingOrder,
            IsAppointmentProtected: after.Entry.Evaluation.IsAppointmentProtected,
            BlockedReason: target.BlockedReason,
            Reason: reason,
            Notes: notes,
            PreviousStateVersion: prevVersion,
            StateVersion: target.StateVersion!.Value,
            UpdatedAtUtc: board.NowUtc,
            UpdatedEntry: after.Entry.Dto);
    }
}
