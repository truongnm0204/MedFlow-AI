using System.Globalization;
using System.Text.Json;
using MedFlow.Application.DTOs.Visits;
using MedFlow.Application.Services.Appointments;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Visits;

public sealed partial class ExaminationQueueService
{
    public const string ReturnToDoctorAction = "QUEUE_RETURN_TO_DOCTOR";

    public async Task<CreateReturnToDoctorQueueEntryResponseDto> CreateReturnToDoctorQueueEntryAsync(
        long actorUserId,
        string idOrCode,
        CreateReturnToDoctorQueueEntryRequest request,
        string? ipAddress = null,
        CancellationToken ct = default)
    {
        if (actorUserId <= 0)
        {
            throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");
        }

        if (request is null)
        {
            throw new ValidationException("Dữ liệu điều phối lượt khám lại không hợp lệ.");
        }

        var (numericId, code) = QueueEntryRequestParsing.ParseIdentifier(idOrCode);

        var normalizedServicePointId = string.IsNullOrWhiteSpace(request.ServicePointId)
            ? null
            : NormalizeAndValidateServicePointFilter(request.ServicePointId);

        var reason = ValidateCoordinationReason(request.Reason);
        var notes = ValidateCoordinationNotes(request.Notes);

        if (request.ExpectedStateVersion is < 1)
        {
            throw new ValidationException("Phiên bản trạng thái (ExpectedStateVersion) phải lớn hơn hoặc bằng 1.");
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var (hospital, ownerDoctorId) = await ResolveCallNextActorAsync(actorUserId, nowUtc, ct);

        await using var tx = await _db.BeginTransactionAsync(ct);

        var (visit, existingReturnEntry, existingExamEntry) = await ResolveVisitAndQueueEntriesForReturnAsync(
            numericId,
            code,
            idOrCode,
            ct);

        if (IsTerminalVisit(visit))
        {
            throw new ConflictException("Không thể thao tác trên lượt khám đã hoàn tất, đã hủy hoặc vắng mặt (NO_SHOW).");
        }

        var referenceEntry = existingReturnEntry ?? existingExamEntry;
        if (await DoesVisitOrEntryBelongToOtherHospitalAsync(visit, referenceEntry, hospital.Id, ct))
        {
            throw new ForbiddenException("Bạn không có quyền thao tác trên lượt khám của cơ sở y tế khác.");
        }

        var today = DateOnly.FromDateTime(AppointmentBookingRules.GetVietnamNow(_timeProvider));
        if (referenceEntry?.QueueDate is DateOnly entryDate && entryDate != today)
        {
            throw new ConflictException("Chỉ tạo hoặc kích hoạt lượt khám lại cho hàng đợi của ngày hiện tại.");
        }

        if (referenceEntry is null &&
            visit.CheckedInAt.HasValue &&
            DateOnly.FromDateTime(visit.CheckedInAt.Value) != today)
        {
            throw new ConflictException("Chỉ tạo hoặc kích hoạt lượt khám lại cho hàng đợi của ngày hiện tại.");
        }

        var board = await BuildQueueBoardForHospitalAsync(hospital, today, ct);

        var before = LocateBoardEntryForVisit(board, visit, referenceEntry);
        if (before is null)
        {
            if (referenceEntry?.QueueDate is DateOnly refDate)
            {
                var refBoard = await BuildQueueBoardForHospitalAsync(hospital, refDate, ct);
                if (LocateBoardEntryForVisit(refBoard, visit, referenceEntry) is not null)
                {
                    throw new ConflictException("Chỉ tạo hoặc kích hoạt lượt khám lại cho hàng đợi của ngày hiện tại.");
                }
            }

            throw new NotFoundException("Không tìm thấy lượt xếp hàng khám trên bảng điều phối.");
        }

        var (targetBucket, beforeEntry) = before.Value;

        if (normalizedServicePointId is not null &&
            !MatchesServicePointFilter(targetBucket.Bucket, normalizedServicePointId))
        {
            if (board.Buckets.Any(b => MatchesServicePointFilter(b.Bucket, normalizedServicePointId)))
            {
                throw new ConflictException("Lượt khám không thuộc điểm phục vụ / phòng khám đã chọn.");
            }

            throw new NotFoundException("Không tìm thấy điểm phục vụ / phòng khám.");
        }

        if (ownerDoctorId is long docId && targetBucket.Bucket.DoctorId != docId)
        {
            throw new ForbiddenException("Bác sĩ chỉ được điều phối lượt khám lại tại điểm phục vụ được phân công.");
        }

        if (!targetBucket.Bucket.IsActive)
        {
            throw new ConflictException("Điểm phục vụ đang ngừng hoạt động.");
        }

        int? prevVersion = referenceEntry is not null
            ? (referenceEntry.StateVersion is > 0 ? referenceEntry.StateVersion.Value : 1)
            : null;

        if (request.ExpectedStateVersion.HasValue &&
            prevVersion.HasValue &&
            request.ExpectedStateVersion.Value != prevVersion.Value)
        {
            throw new ConflictException(
                "Dữ liệu hàng đợi đã thay đổi bởi thao tác khác (state_version mismatch). Vui lòng tải lại hàng đợi.");
        }

        var openDiagnosticCount = await _db.QueueEntries
            .AsNoTracking()
            .CountAsync(q =>
                q.VisitId == visit.Id &&
                q.QueueType != null &&
                q.QueueType.ToUpper() == "DIAGNOSTIC" &&
                q.CompletedAt == null &&
                (q.Status == null ||
                 (q.Status.ToUpper() != "COMPLETED" && q.Status.ToUpper() != "CANCELLED")),
                ct);

        var allDiagnosticResultsReady = openDiagnosticCount == 0 && request.DiagnosticResultsReady != false;
        var finalResultRequired = targetBucket.Policy.FinalResultRequired;
        var isAwaitingMandatoryResults = finalResultRequired && !allDiagnosticResultsReady;
        var presenceStatus = request.ConfirmPresent ? "PRESENT" : "NOT_ARRIVED";
        var targetStatus = request.ConfirmPresent && !isAwaitingMandatoryResults ? "READY" : "WAITING";

        var previousQueueEntryId = referenceEntry is not null ? $"q-{referenceEntry.Id:D3}" : null;
        var previousStatus = referenceEntry is not null ? beforeEntry.Dto.Status : null;
        int? previousServingOrder = referenceEntry is not null ? beforeEntry.Dto.ServingOrder : null;

        QueueEntry targetEntry;
        string actionOutcome;
        int nextStateVersion;

        if (existingReturnEntry is not null)
        {
            var existingStatus = NormalizeEntryStatus(existingReturnEntry, visit);
            var existingPresence = NormalizePresenceStatus(existingReturnEntry, visit);

            if (existingStatus is "CALLED" or "IN_PROGRESS")
            {
                throw new ConflictException(
                    "Lượt khám lại đang được gọi hoặc đang khám, không thể tạo hoặc kích hoạt lại.");
            }

            if (existingStatus == "COMPLETED")
            {
                throw new ConflictException("Lượt khám lại đã hoàn tất, không thể kích hoạt lại.");
            }

            if (existingStatus == "SKIPPED")
            {
                throw new ConflictException(
                    "Lượt khám lại đang ở trạng thái Qua lượt (SKIPPED). Vui lòng sử dụng chức năng Khôi phục lượt khám (restore-turn).");
            }

            if (existingStatus == "READY" && existingPresence == "PRESENT" && !isAwaitingMandatoryResults)
            {
                throw new ConflictException(
                    "Lượt khám lại đọc kết quả cận lâm sàng đã ở trạng thái Sẵn sàng (READY) trong hàng đợi.");
            }

            if (isAwaitingMandatoryResults && existingStatus == "WAITING" && existingPresence == presenceStatus)
            {
                throw new ConflictException(
                    "Chưa thể kích hoạt lượt khám lại sang trạng thái Sẵn sàng (READY) vì kết quả cận lâm sàng bắt buộc chưa hoàn tất.");
            }

            if (!request.ConfirmPresent && existingStatus == "WAITING" && existingPresence == "NOT_ARRIVED")
            {
                throw new ConflictException(
                    "Cần xác nhận bệnh nhân đã có mặt (PRESENT) để kích hoạt lượt khám lại.");
            }

            actionOutcome = targetStatus == "READY" ? "ACTIVATED" : "UPDATED";
            nextStateVersion = (prevVersion ?? 1) + 1;

            existingReturnEntry.VisitId ??= visit.Id;
            existingReturnEntry.QueueType = ClinicalQueueRules.QueueTypeReturnConsultation;
            existingReturnEntry.Status = targetStatus;
            existingReturnEntry.PresenceStatus = presenceStatus;
            existingReturnEntry.ArrivedAt = request.ConfirmPresent
                ? (existingReturnEntry.ArrivedAt ?? board.NowLocal)
                : existingReturnEntry.ArrivedAt;
            existingReturnEntry.ReadyAt = targetStatus == "READY"
                ? board.NowLocal
                : existingReturnEntry.ReadyAt;
            existingReturnEntry.CalledAt = null;
            existingReturnEntry.StartedAt = null;
            existingReturnEntry.BlockedReason = null;
            existingReturnEntry.ArrivalClass = "WALK_IN";
            existingReturnEntry.ManualPriority = ClinicalQueueRules.NormalizeStoredPriority(existingReturnEntry.ManualPriority);
            existingReturnEntry.StateVersion = nextStateVersion;

            visit.Status = isAwaitingMandatoryResults ? "AWAITING_RESULTS" : "RETURN_CONSULTATION";
            targetEntry = existingReturnEntry;
        }
        else
        {
            actionOutcome = "CREATED";
            nextStateVersion = 1;

            if (existingExamEntry is not null)
            {
                existingExamEntry.VisitId ??= visit.Id;
                var examStatus = AppointmentBookingRules.NormalizeStatusKey(existingExamEntry.Status);
                if (examStatus is not ("COMPLETED" or "CANCELLED"))
                {
                    existingExamEntry.Status = "COMPLETED";
                    existingExamEntry.CompletedAt ??= board.NowLocal;
                    existingExamEntry.StateVersion = (existingExamEntry.StateVersion is > 0
                        ? existingExamEntry.StateVersion.Value
                        : 1) + 1;
                }
            }

            var rawServicePointId = targetBucket.Bucket.RawServicePointId
                ?? existingExamEntry?.CurrentServicePointId
                ?? existingExamEntry?.ServicePointId;

            var existingMaxNumber = await _db.QueueEntries
                .AsNoTracking()
                .Where(q =>
                    q.QueueDate == today &&
                    (rawServicePointId == null
                        ? (q.ServicePointId == null && q.CurrentServicePointId == null)
                        : (q.ServicePointId == rawServicePointId || q.CurrentServicePointId == rawServicePointId)))
                .Select(q => q.QueueNumber)
                .MaxAsync(ct);

            var nextQueueNumber = (existingMaxNumber is > 0 ? existingMaxNumber.Value : 0) + 1;
            var inheritedPriority = ClinicalQueueRules.NormalizeStoredPriority(existingExamEntry?.ManualPriority);

            var createdEntry = new QueueEntry
            {
                VisitId = visit.Id,
                AppointmentId = null,
                ServicePointId = rawServicePointId,
                CurrentServicePointId = rawServicePointId,
                QueueDate = today,
                QueueNumber = nextQueueNumber,
                QueueType = ClinicalQueueRules.QueueTypeReturnConsultation,
                Status = targetStatus,
                ScheduledAt = null,
                PresenceStatus = presenceStatus,
                ArrivedAt = request.ConfirmPresent ? board.NowLocal : null,
                ArrivalClass = "WALK_IN",
                ManualPriority = inheritedPriority,
                StateVersion = nextStateVersion,
                ReadyAt = targetStatus == "READY" ? board.NowLocal : null,
            };

            _db.QueueEntries.Add(createdEntry);
            visit.Status = isAwaitingMandatoryResults ? "AWAITING_RESULTS" : "RETURN_CONSULTATION";
            targetEntry = createdEntry;
        }

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex is not DbUpdateConcurrencyException)
        {
            throw new ConflictException("Hàng đợi tại điểm phục vụ vừa thay đổi đồng thời. Vui lòng thử lại.");
        }

        var afterBoard = await BuildQueueBoardForHospitalAsync(hospital, today, ct);
        var after = FindBoardEntry(afterBoard, targetEntry.Id)
            ?? throw new ConflictException("Không thể tải lại hàng đợi sau khi tạo hoặc kích hoạt lượt khám lại.");

        targetEntry.ArrivalClass = after.Entry.Evaluation.EffectiveArrivalClass;

        var resolvedQueueGroup = after.Entry.Dto.QueueGroup
            ?? ResolveQueueGroup(after.Entry.Dto.Status, after.Entry.Evaluation.IsEligible);

        var auditPayload = new
        {
            action = "RETURN_TO_DOCTOR",
            actionOutcome,
            visitId = visit.Id,
            visitCode = after.Entry.Dto.VisitCode,
            servicePointId = after.Bucket.Bucket.ServicePointId,
            previousQueueEntryId,
            queueEntryId = after.Entry.Dto.Id,
            queueNumber = after.Entry.Dto.QueueNumber,
            previousStatus,
            currentStatus = after.Entry.Dto.Status,
            queueType = after.Entry.Dto.QueueType,
            priority = after.Entry.Dto.Priority,
            presenceStatus = after.Entry.Dto.PresenceStatus,
            arrivalClass = after.Entry.Dto.ArrivalClass,
            finalResultRequired,
            allDiagnosticResultsReady,
            openDiagnosticCount,
            isEligible = after.Entry.Evaluation.IsEligible,
            queueGroup = resolvedQueueGroup,
            ineligibleReason = after.Entry.Evaluation.IneligibleReason,
            canSafeBackfill = after.Entry.Evaluation.CanSafeBackfill,
            isAppointmentProtected = after.Entry.Evaluation.IsAppointmentProtected,
            holdReason = after.Entry.Evaluation.HoldReason,
            previousServingOrder,
            servingOrder = after.Entry.Dto.ServingOrder,
            previousStateVersion = prevVersion,
            stateVersion = nextStateVersion,
            reason,
            notes,
        };

        var auditLog = new AuditLog
        {
            ActorUserId = actorUserId,
            HospitalId = afterBoard.Hospital.Id,
            Action = ReturnToDoctorAction,
            EntityType = QueueStatusService.QueueEntryEntityType,
            EntityId = targetEntry.Id.ToString(CultureInfo.InvariantCulture),
            Reason = reason,
            ChangeData = JsonSerializer.Serialize(auditPayload, JsonOptions),
            IpAddress = QueueEntryRequestParsing.ParseIpAddress(ipAddress),
            OccurredAt = afterBoard.NowUtc,
        };

        _db.AuditLogs.Add(auditLog);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new CreateReturnToDoctorQueueEntryResponseDto(
            QueueEntryId: after.Entry.Dto.Id,
            TicketNo: after.Entry.Dto.TicketNo,
            QueueNumber: after.Entry.Dto.QueueNumber,
            VisitId: visit.Id,
            VisitCode: after.Entry.Dto.VisitCode,
            PatientCode: after.Entry.Dto.PatientCode,
            PatientName: after.Entry.Dto.PatientName,
            ServicePointId: after.Bucket.Bucket.ServicePointId,
            RoomCode: after.Entry.Dto.RoomCode,
            RoomName: after.Entry.Dto.RoomName,
            ActionOutcome: actionOutcome,
            PreviousQueueEntryId: previousQueueEntryId,
            PreviousStatus: previousStatus,
            CurrentStatus: after.Entry.Dto.Status,
            QueueType: after.Entry.Dto.QueueType,
            Priority: after.Entry.Dto.Priority,
            PresenceStatus: after.Entry.Dto.PresenceStatus,
            ArrivalClass: after.Entry.Dto.ArrivalClass,
            FinalResultRequired: finalResultRequired,
            AllDiagnosticResultsReady: allDiagnosticResultsReady,
            OpenDiagnosticCount: openDiagnosticCount,
            IsEligible: after.Entry.Evaluation.IsEligible,
            QueueGroup: resolvedQueueGroup,
            IneligibleReason: after.Entry.Evaluation.IneligibleReason,
            CanSafeBackfill: after.Entry.Evaluation.CanSafeBackfill,
            IsAppointmentProtected: after.Entry.Evaluation.IsAppointmentProtected,
            HoldReason: after.Entry.Evaluation.HoldReason,
            PreviousServingOrder: previousServingOrder,
            ServingOrder: after.Entry.Dto.ServingOrder,
            Reason: reason,
            Notes: notes,
            PreviousStateVersion: prevVersion,
            StateVersion: nextStateVersion,
            UpdatedAtUtc: afterBoard.NowUtc,
            UpdatedEntry: after.Entry.Dto);
    }

    private static (QueueBoardBucket Bucket, QueueBoardEntry Entry)? LocateBoardEntryForVisit(
        QueueBoard board,
        Visit visit,
        QueueEntry? referenceEntry)
    {
        if (referenceEntry is not null)
        {
            var byId = FindBoardEntry(board, referenceEntry.Id);
            if (byId is not null)
            {
                return byId;
            }
        }

        var fallbackEntryId = $"vis-{visit.Id:D3}";
        var visitCode = visit.VisitCode?.Trim();

        foreach (var bucket in board.Buckets)
        {
            var found = bucket.Entries.FirstOrDefault(e =>
                string.Equals(e.Dto.Id, fallbackEntryId, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrEmpty(visitCode) &&
                 string.Equals(e.Dto.VisitCode, visitCode, StringComparison.OrdinalIgnoreCase)));
            if (found is not null)
            {
                return (bucket, found);
            }
        }

        return null;
    }

    private async Task<bool> DoesVisitOrEntryBelongToOtherHospitalAsync(
        Visit visit,
        QueueEntry? referenceEntry,
        long actorHospitalId,
        CancellationToken ct)
    {
        if (visit.ExaminationRoomId is long roomId)
        {
            var room = await _db.HospitalRooms
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == roomId, ct);
            if (room?.HospitalId is long roomHospId && roomHospId != actorHospitalId)
            {
                return true;
            }
        }

        if (visit.AssignedDoctorId is long docId)
        {
            var doctor = await _db.Doctors
                .AsNoTracking()
                .Include(d => d.Staff)
                .FirstOrDefaultAsync(d => d.Id == docId, ct);
            var docHospId = doctor?.HospitalId ?? doctor?.Staff?.HospitalId;
            if (docHospId is long resolvedDocHospId && resolvedDocHospId != actorHospitalId)
            {
                return true;
            }
        }

        if (visit.CheckedInByStaffId is long staffId)
        {
            var staff = await _db.Staff
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == staffId, ct);
            if (staff?.HospitalId is long staffHospId && staffHospId != actorHospitalId)
            {
                return true;
            }
        }

        var spId = referenceEntry?.CurrentServicePointId ?? referenceEntry?.ServicePointId;
        if (spId is long rawSpId)
        {
            var sp = await _db.QueueServicePoints
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == rawSpId, ct);
            if (sp?.HospitalId is long resolvedSpHospId && resolvedSpHospId != actorHospitalId)
            {
                return true;
            }

            if (sp?.RoomId is long spRoomId)
            {
                var spRoom = await _db.HospitalRooms
                    .AsNoTracking()
                    .FirstOrDefaultAsync(r => r.Id == spRoomId, ct);
                if (spRoom?.HospitalId is long spRoomHospId && spRoomHospId != actorHospitalId)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
