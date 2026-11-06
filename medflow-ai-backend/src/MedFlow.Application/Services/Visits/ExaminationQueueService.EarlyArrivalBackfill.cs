using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using MedFlow.Application.DTOs.Visits;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Visits;

public sealed partial class ExaminationQueueService
{
    public const string EarlyArrivalBackfillAction = "QUEUE_EARLY_ARRIVAL_BACKFILL";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<EarlyArrivalBackfillResponseDto> ApplyEarlyArrivalBackfillAsync(
        long actorUserId,
        string idOrCode,
        EarlyArrivalBackfillRequest request,
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

        var actionMode = request.ActionMode?.Trim().ToUpperInvariant();
        if (actionMode is not ("SAFE_BACKFILL" or "HOLD_FOR_SLOT"))
        {
            throw new ValidationException("Hình thức điều phối không hợp lệ (SAFE_BACKFILL hoặc HOLD_FOR_SLOT).");
        }

        var requestedArrivalClass = request.ArrivalClass?.Trim().ToUpperInvariant();
        if (requestedArrivalClass is not ("ON_TIME_OR_GRACE" or "EARLY" or "LATE" or "WALK_IN"))
        {
            throw new ValidationException("Phân loại đến khám không hợp lệ.");
        }

        var normalizedServicePointId = string.IsNullOrWhiteSpace(request.ServicePointId)
            ? null
            : NormalizeAndValidateServicePointFilter(request.ServicePointId);
        if (normalizedServicePointId is null)
        {
            throw new ValidationException("Điểm phục vụ / phòng khám là bắt buộc.");
        }

        var reason = request.Reason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length < 5 || reason.Length > 300)
        {
            throw new ValidationException("Lý do điều phối phải từ 5 đến 300 ký tự.");
        }

        if (reason.Contains('<') || reason.Contains('>'))
        {
            throw new ValidationException("Lý do điều phối không được chứa ký tự < hoặc >.");
        }

        var notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        if (notes is not null)
        {
            if (notes.Length > 300)
            {
                throw new ValidationException("Ghi chú tối đa 300 ký tự.");
            }

            if (notes.Contains('<') || notes.Contains('>'))
            {
                throw new ValidationException("Ghi chú không được chứa ký tự < hoặc >.");
            }
        }

        if (actionMode == "SAFE_BACKFILL" && !request.AcknowledgeAppointmentProtection)
        {
            throw new ValidationException("Cần xác nhận đã kiểm tra bảo vệ lịch hẹn trước khi Safe Backfill.");
        }

        await using var tx = await _db.BeginTransactionAsync(ct);

        QueueEntry? target = null;
        if (numericId.HasValue)
        {
            target = await _db.QueueEntries.FirstOrDefaultAsync(q => q.Id == numericId.Value, ct);
        }
        else if (!string.IsNullOrEmpty(code))
        {
            var visit = await _db.Visits
                .AsNoTracking()
                .FirstOrDefaultAsync(v => v.VisitCode == code, ct);
            if (visit is not null)
            {
                target = await _db.QueueEntries
                    .Where(q => q.VisitId == visit.Id && (q.QueueType == null || q.QueueType == "EXAMINATION"))
                    .OrderByDescending(q => q.Id)
                    .FirstOrDefaultAsync(ct);
            }
        }

        if (target is null)
        {
            throw new NotFoundException("Không tìm thấy lượt xếp hàng khám.");
        }

        var board = await BuildQueueBoardAsync(actorUserId, target.QueueDate, ct);

        QueueBoardBucket? bucket = null;
        QueueBoardEntry? entry = null;
        foreach (var b in board.Buckets)
        {
            var found = b.Entries.FirstOrDefault(e => e.QueueEntry?.Id == target.Id);
            if (found is not null)
            {
                bucket = b;
                entry = found;
                break;
            }
        }

        if (bucket is null || entry is null)
        {
            throw new NotFoundException("Không tìm thấy lượt xếp hàng khám.");
        }

        if (board.EffectiveQueueDate != DateOnly.FromDateTime(board.NowLocal))
        {
            throw new ConflictException("Chỉ điều phối Safe Backfill cho hàng đợi của ngày hiện tại.");
        }

        if (!MatchesServicePointFilter(bucket.Bucket, normalizedServicePointId))
        {
            throw new ConflictException("Lượt khám không thuộc điểm phục vụ / phòng khám đã chọn.");
        }

        var previousStatus = entry.Dto.Status;
        if (previousStatus is not ("WAITING" or "READY"))
        {
            throw new ConflictException("Chỉ hỗ trợ điều phối đến sớm / Safe Backfill cho lượt khám đang ở trạng thái Đang chờ hoặc Sẵn sàng.");
        }

        var eval = entry.Evaluation;
        var nowLocal = board.NowLocal;
        var prevVersion = target.StateVersion is > 0 ? target.StateVersion.Value : 1;

        if (actionMode == "SAFE_BACKFILL")
        {
            if (target.BackfilledAt is not null && previousStatus == "READY")
            {
                throw new ConflictException("Lượt khám đã được Safe Backfill trước đó.");
            }

            if (!eval.CanSafeBackfill)
            {
                throw new ConflictException($"Không thể Safe Backfill: {eval.HoldReason}");
            }

            if (previousStatus != "READY")
            {
                target.ReadyAt = nowLocal;
            }

            target.Status = "READY";
            target.ArrivalClass = eval.EffectiveArrivalClass;
            target.BlockedReason = null;
            target.BackfilledAt = nowLocal;
            target.BackfillReason = reason;
        }
        else
        {
            if (entry.Dto.VisitType == "WALK_IN")
            {
                throw new ValidationException("Không áp dụng giữ chờ khung giờ hẹn cho bệnh nhân khám trực tiếp.");
            }

            target.Status = "WAITING";
            target.ArrivalClass = eval.EffectiveArrivalClass;
            target.BlockedReason = !string.IsNullOrWhiteSpace(entry.Dto.ScheduledAt)
                ? $"Giữ chờ tới khung giờ hẹn {entry.Dto.ScheduledAt} (Bảo vệ lịch hẹn đúng giờ)"
                : "Giữ chờ khoảng trống an toàn (Bảo vệ lịch hẹn đúng giờ)";
            target.BackfilledAt = null;
            target.BackfillReason = null;
        }

        var nextVersion = prevVersion + 1;
        target.StateVersion = nextVersion;

        var changePayload = new
        {
            actionMode,
            previousStatus,
            currentStatus = target.Status,
            previousStateVersion = prevVersion,
            stateVersion = nextVersion,
            queueNumber = entry.Dto.QueueNumber,
            requestedArrivalClass,
            arrivalClass = target.ArrivalClass,
            servicePointId = bucket.Bucket.ServicePointId,
            previousServingOrder = entry.Dto.ServingOrder,
            canSafeBackfill = eval.CanSafeBackfill,
            holdReason = eval.HoldReason,
            protectedNextStartsAt = eval.ProtectedNextStartsAt,
            graceMinutes = bucket.Policy.GraceMinutes,
            expectedDurationMinutes = bucket.Policy.ExpectedDurationMinutes,
            safetyBufferMinutes = bucket.Policy.SafetyBufferMinutes,
            acknowledgeAppointmentProtection = request.AcknowledgeAppointmentProtection,
            notes,
        };

        var auditLog = new AuditLog
        {
            ActorUserId = actorUserId,
            HospitalId = board.Hospital.Id,
            Action = EarlyArrivalBackfillAction,
            EntityType = QueueStatusService.QueueEntryEntityType,
            EntityId = target.Id.ToString(CultureInfo.InvariantCulture),
            Reason = reason,
            ChangeData = JsonSerializer.Serialize(changePayload, JsonOptions),
            IpAddress = QueueEntryRequestParsing.ParseIpAddress(ipAddress),
            OccurredAt = board.NowUtc,
        };

        _db.AuditLogs.Add(auditLog);
        await _db.SaveChangesAsync(ct);

        var afterBoard = await BuildQueueBoardAsync(actorUserId, target.QueueDate, ct);
        QueueBoardEntry? afterEntry = null;
        foreach (var b in afterBoard.Buckets)
        {
            var found = b.Entries.FirstOrDefault(e => e.QueueEntry?.Id == target.Id);
            if (found is not null)
            {
                afterEntry = found;
                break;
            }
        }

        if (afterEntry is null)
        {
            throw new ConflictException("Không thể tải lại hàng đợi sau khi điều phối.");
        }

        await tx.CommitAsync(ct);

        return new EarlyArrivalBackfillResponseDto(
            QueueEntryId: afterEntry.Dto.Id,
            TicketNo: afterEntry.Dto.TicketNo,
            QueueNumber: afterEntry.Dto.QueueNumber,
            VisitCode: afterEntry.Dto.VisitCode,
            PatientCode: afterEntry.Dto.PatientCode,
            PatientName: afterEntry.Dto.PatientName,
            ServicePointId: afterEntry.Dto.ServicePointId,
            RoomCode: afterEntry.Dto.RoomCode,
            RoomName: afterEntry.Dto.RoomName,
            PreviousStatus: previousStatus,
            CurrentStatus: afterEntry.Dto.Status,
            ArrivalClass: afterEntry.Dto.ArrivalClass,
            ServingOrder: afterEntry.Dto.ServingOrder,
            IsBackfilled: target.BackfilledAt is not null,
            AppointmentProtected: eval.HasDueProtectedAppointment || eval.ProtectedNextStartsAt is not null,
            BlockedReason: target.BlockedReason,
            Reason: reason,
            Notes: notes,
            StateVersion: target.StateVersion!.Value,
            UpdatedAtUtc: board.NowUtc,
            UpdatedEntry: afterEntry.Dto);
    }
}
