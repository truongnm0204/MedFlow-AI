using System.Globalization;
using System.Net;
using System.Text.Json;
using MedFlow.Application.Common.Time;
using MedFlow.Application.DTOs.Realtime;
using MedFlow.Application.DTOs.Visits;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Appointments;
using MedFlow.Application.Services.Payments;
using MedFlow.Application.Services.Policies;
using MedFlow.Application.Services.Realtime;
using MedFlow.Application.Services.Visits.Queueing;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Visits;

public sealed class QueueStatusService : IQueueStatusService
{
    public const string QueueStatusUpdateAction = "QUEUE_STATUS_UPDATED";
    public const string QueuePriorityUpdateAction = "QUEUE_PRIORITY_UPDATED";
    public const string QueueEntryCancelAction = "QUEUE_ENTRY_CANCELLED";
    public const string ExaminationChargeRefundPendingAction = "EXAMINATION_CHARGE_REFUND_PENDING";
    public const string QueueEntryEntityType = "QUEUE_ENTRY";

    private static readonly HashSet<string> ValidCancelDispositions = new(StringComparer.OrdinalIgnoreCase)
    {
        QueueCancelDispositions.PatientAbandoned,
        QueueCancelDispositions.ReassignRequeue,
        QueueCancelDispositions.TechnicalError,
    };

    private static readonly string[] QueueManagerRoles =
    [
        RoleNames.Reception,
        RoleNames.HospitalAdmin,
        RoleNames.SystemAdmin,
    ];

    private static readonly string[] AllowedOperatorRoles =
    [
        RoleNames.Reception,
        RoleNames.Doctor,
        RoleNames.DiagnosticStaff,
        RoleNames.HospitalAdmin,
        RoleNames.SystemAdmin,
    ];

    private static readonly string[] AllowedPermissionCodes =
    [
        PermissionCodes.QueueManage,
        PermissionCodes.VisitAssign,
        PermissionCodes.PatientCheckin,
        PermissionCodes.MedicalRecordWrite,
        PermissionCodes.DiagnosticResultWrite,
        PermissionCodes.HospitalConfig,
        PermissionCodes.UserManage,
    ];

    private static readonly HashSet<string> ValidTargetStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "WAITING",
        "READY",
        "CALLED",
        "IN_PROGRESS",
        "COMPLETED",
        "SKIPPED",
    };

    private static readonly HashSet<string> ValidPresenceStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "PRESENT",
        "NOT_ARRIVED",
        "LEFT",
    };

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> AllowedTransitions =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["RESERVED"] = ["WAITING", "READY"],
            ["WAITING"] = ["READY", "CALLED", "SKIPPED"],
            ["READY"] = ["WAITING", "CALLED", "SKIPPED"],
            ["CALLED"] = ["IN_PROGRESS", "WAITING", "READY", "SKIPPED"],
            ["IN_PROGRESS"] = ["COMPLETED", "WAITING", "SKIPPED"],
            ["SKIPPED"] = ["WAITING", "READY", "CALLED"],
            ["COMPLETED"] = [],
            ["CANCELLED"] = [],
        };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;
    private readonly IRealtimeNotificationPublisher _realtimePublisher;
    private readonly IRealtimeAccessRevoker _accessRevoker;
    private readonly IVisitChargeSettlementReader _settlementReader;

    public QueueStatusService(
        IApplicationDbContext db,
        TimeProvider? timeProvider = null,
        IRealtimeNotificationPublisher? realtimePublisher = null,
        IRealtimeAccessRevoker? accessRevoker = null,
        IVisitChargeSettlementReader? settlementReader = null)
    {
        _db = db;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _realtimePublisher = realtimePublisher ?? NullRealtimeNotificationPublisher.Instance;
        _accessRevoker = accessRevoker ?? NullRealtimeAccessRevoker.Instance;
        _settlementReader = settlementReader ?? new VisitChargeSettlementReader(_db, _timeProvider);
    }

    public async Task<QueueEntryStatusDetailDto> GetQueueEntryStatusAsync(
        long actorUserId,
        string idOrCode,
        CancellationToken ct = default)
    {
        if (actorUserId <= 0)
        {
            throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");
        }

        var (numericId, code) = QueueEntryRequestParsing.ParseIdentifier(idOrCode);
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var operatorAccess = await EnsureOperatorAccessAsync(actorUserId, nowUtc, ct);

        var context = await ResolveQueueEntryContextAsync(
            numericId,
            code,
            idOrCode,
            operatorAccess,
            trackEntities: false,
            ct);

        var queueEntry = context.QueueEntry;
        var currentStatus = NormalizeCurrentQueueStatus(queueEntry.Status);
        var allowedTargets = GetAllowedTargetStatuses(currentStatus, context.Visit);
        var history = await LoadStatusHistoryAsync(queueEntry.Id, ct);
        var queueNumber = queueEntry.QueueNumber is > 0 ? queueEntry.QueueNumber.Value : 1;
        var originRoomCode = await ResolveOriginTicketRoomCodeAsync(queueEntry, context.Room?.RoomCode, ct);
        var ticketNo = FormatTicketNo(queueNumber, originRoomCode);

        return new QueueEntryStatusDetailDto(
            QueueEntryId: queueEntry.Id,
            VisitId: context.Visit?.Id ?? queueEntry.VisitId,
            VisitCode: context.Visit is not null
                ? (string.IsNullOrWhiteSpace(context.Visit.VisitCode) ? $"VIS-{context.Visit.Id:D4}" : context.Visit.VisitCode)
                : null,
            AppointmentId: queueEntry.AppointmentId ?? context.Visit?.AppointmentId,
            PatientId: context.Patient?.Id ?? context.Visit?.PatientId,
            PatientCode: context.Patient?.PatientCode,
            PatientName: context.Patient?.FullName,
            HospitalId: context.HospitalId,
            DepartmentId: context.Department?.Id ?? context.Room?.DepartmentId,
            DepartmentCode: context.Department?.DepartmentCode,
            DepartmentName: context.Department?.Name,
            RoomId: context.Room?.Id ?? context.Visit?.ExaminationRoomId,
            RoomCode: context.Room?.RoomCode,
            RoomName: context.Room?.Name,
            AssignedDoctorId: context.AssignedDoctor?.Id ?? context.Visit?.AssignedDoctorId,
            AssignedDoctorCode: context.AssignedDoctor?.DoctorCode,
            AssignedDoctorName: context.AssignedDoctor?.Staff?.FullName,
            ServicePointId: context.ServicePoint?.Id ?? queueEntry.CurrentServicePointId ?? queueEntry.ServicePointId,
            ServicePointName: context.ServicePoint?.Name ?? context.Room?.Name,
            QueueDate: queueEntry.QueueDate,
            QueueNumber: queueNumber,
            TicketNo: ticketNo,
            QueueType: string.IsNullOrWhiteSpace(queueEntry.QueueType) ? "EXAMINATION" : queueEntry.QueueType.ToUpperInvariant(),
            CurrentStatus: currentStatus,
            VisitStatus: context.Visit?.Status,
            PresenceStatus: string.IsNullOrWhiteSpace(queueEntry.PresenceStatus) ? "PRESENT" : queueEntry.PresenceStatus.ToUpperInvariant(),
            Priority: ClinicalQueueRules.NormalizeStoredPriority(queueEntry.ManualPriority),
            ArrivalClass: string.IsNullOrWhiteSpace(queueEntry.ArrivalClass) ? "ON_TIME_OR_GRACE" : queueEntry.ArrivalClass.ToUpperInvariant(),
            StateVersion: queueEntry.StateVersion is > 0 ? queueEntry.StateVersion.Value : 1,
            ScheduledAt: queueEntry.ScheduledAt,
            ArrivedAt: queueEntry.ArrivedAt,
            ReadyAt: queueEntry.ReadyAt,
            CalledAt: queueEntry.CalledAt,
            StartedAt: queueEntry.StartedAt,
            CompletedAt: queueEntry.CompletedAt,
            BlockedReason: queueEntry.BlockedReason,
            AllowedTargetStatuses: allowedTargets,
            StatusHistory: history);
    }

    public async Task<UpdateQueueStatusResponseDto> UpdateQueueEntryStatusAsync(
        long actorUserId,
        string idOrCode,
        UpdateQueueStatusRequest request,
        string? ipAddress = null,
        CancellationToken ct = default)
    {
        if (actorUserId <= 0)
        {
            throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");
        }

        ArgumentNullException.ThrowIfNull(request);

        var (numericId, code) = QueueEntryRequestParsing.ParseIdentifier(idOrCode);
        var targetStatus = ResolveAndValidateTargetStatus(request);
        var requestedPresence = NormalizeAndValidatePresenceStatus(request.PresenceStatus);
        var trimmedReason = TrimAndValidateLength(request.Reason, "Lý do cập nhật trạng thái");
        var trimmedBlockedReason = TrimAndValidateLength(request.BlockedReason, "Lý do chặn/qua lượt");
        var trimmedNotes = TrimAndValidateLength(request.Notes, "Ghi chú");

        if (request.ExpectedStateVersion.HasValue && request.ExpectedStateVersion.Value < 1)
        {
            throw new ValidationException("Phiên bản trạng thái (ExpectedStateVersion) phải lớn hơn hoặc bằng 1.");
        }

        var effectiveReason = trimmedReason ?? trimmedBlockedReason;
        var effectiveBlockedReason = trimmedBlockedReason ?? trimmedReason;

        if (targetStatus == "SKIPPED" && string.IsNullOrWhiteSpace(effectiveBlockedReason))
        {
            throw new ValidationException("Vui lòng nhập lý do khi chuyển trạng thái sang Qua lượt (SKIPPED).");
        }

        if (targetStatus == "IN_PROGRESS" && requestedPresence is "NOT_ARRIVED" or "LEFT")
        {
            throw new ValidationException("Không thể bắt đầu khám khi trạng thái hiện diện của bệnh nhân là vắng mặt hoặc chưa đến.");
        }

        var nowUtc = HospitalClock.GetUtcNow(_timeProvider);
        var nowLocal = HospitalClock.GetNow(_timeProvider);
        var operatorAccess = await EnsureOperatorAccessAsync(actorUserId, nowUtc, ct);

        var preContext = await ResolveQueueEntryContextAsync(
            numericId,
            code,
            idOrCode,
            operatorAccess,
            trackEntities: false,
            ct);

        var lockRoomId = preContext.Room?.Id ?? preContext.Visit?.ExaminationRoomId;
        var lockVisitId = preContext.Visit?.Id ?? preContext.QueueEntry.VisitId;
        var lockSpId = preContext.QueueEntry.CurrentServicePointId
            ?? preContext.QueueEntry.ServicePointId
            ?? preContext.ServicePoint?.Id;
        var lockQueueEntryId = preContext.QueueEntry.Id;

        await using var tx = await _db.BeginTransactionAsync(ct);

        // ADR-0059 strict lock order: hospital_rooms (06) -> visits (09) -> queue_service_points (10) -> queue_entries (11)
        if (lockRoomId.HasValue)
        {
            await _db.TryLockRoomForUpdateAsync(lockRoomId.Value, ct);
        }

        if (lockVisitId.HasValue)
        {
            await _db.TryLockVisitForUpdateAsync(lockVisitId.Value, ct);
        }

        if (lockSpId.HasValue)
        {
            await _db.TryLockServicePointForUpdateAsync(lockSpId.Value, ct);
        }

        await _db.TryLockQueueEntryForUpdateAsync(lockQueueEntryId, ct);

        if (_db is DbContext dbCtx && dbCtx.Database.ProviderName != "Microsoft.EntityFrameworkCore.InMemory")
        {
            dbCtx.ChangeTracker.Clear();
        }

        var context = await ResolveQueueEntryContextAsync(
            lockQueueEntryId,
            null,
            idOrCode,
            operatorAccess,
            trackEntities: true,
            ct);

        var queueEntry = context.QueueEntry;
        var visit = context.Visit;
        var currentStatus = NormalizeCurrentQueueStatus(queueEntry.Status);
        var previousVisitStatus = visit?.Status;
        var previousPresenceStatus = string.IsNullOrWhiteSpace(queueEntry.PresenceStatus)
            ? "PRESENT"
            : queueEntry.PresenceStatus.ToUpperInvariant();
        var previousPriority = ClinicalQueueRules.NormalizeStoredPriority(queueEntry.ManualPriority);
        var previousStateVersion = queueEntry.StateVersion is > 0 ? queueEntry.StateVersion.Value : 1;

        if (currentStatus is "COMPLETED" or "CANCELLED" ||
            (visit is not null && IsTerminalVisitStatus(visit.Status)))
        {
            throw new ConflictException("Không thể cập nhật trạng thái cho lượt xếp hàng đã hoàn tất hoặc đã hủy.");
        }

        if (request.ExpectedStateVersion.HasValue &&
            request.ExpectedStateVersion.Value != previousStateVersion)
        {
            throw new ConflictException("Dữ liệu hàng đợi đã thay đổi bởi thao tác khác (state_version mismatch). Vui lòng tải lại hàng đợi.");
        }

        if (string.Equals(currentStatus, targetStatus, StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException($"Lượt xếp hàng hiện đã ở trạng thái {targetStatus}.");
        }

        var allowedTargets = GetAllowedTargetStatuses(currentStatus, visit);
        if (!allowedTargets.Contains(targetStatus, StringComparer.OrdinalIgnoreCase))
        {
            throw new ConflictException($"Chuyển tiếp trạng thái hàng đợi từ {currentStatus} sang {targetStatus} không hợp lệ.");
        }

        var resolvedQueueTypeForUpdate = ClinicalQueueRules.ResolveQueueType(queueEntry.QueueType, visit?.Status);
        if (resolvedQueueTypeForUpdate == ClinicalQueueRules.QueueTypeReturnConsultation &&
            targetStatus is "READY" or "CALLED" or "IN_PROGRESS" &&
            visit is not null)
        {
            var policySet = await QueueOrderingPolicyLoader.LoadAsync(_db, context.HospitalId, nowUtc, ct);
            var servicePointIdForPolicy = queueEntry.CurrentServicePointId ?? queueEntry.ServicePointId ?? context.ServicePoint?.Id;
            var policy = policySet.ForServicePoint(servicePointIdForPolicy);
            if (policy.FinalResultRequired)
            {
                var hasPendingDiagnostics = await HasPendingDiagnosticResultsAsync(visit.Id, ct);
                if (hasPendingDiagnostics)
                {
                    throw new ConflictException(
                        "Chưa thể chuyển trạng thái lượt khám lại vì kết quả cận lâm sàng bắt buộc chưa sẵn sàng.");
                }
            }
        }

        if (targetStatus is "CALLED" or "IN_PROGRESS")
        {
            var isExaminationQueue = ClinicalQueueRules.IsClinicalExaminationQueueType(queueEntry.QueueType);

            if (isExaminationQueue &&
                visit is not null &&
                !visit.ExaminationRoomId.HasValue &&
                !queueEntry.ServicePointId.HasValue &&
                !queueEntry.CurrentServicePointId.HasValue)
            {
                throw new ConflictException("Lượt khám chưa được gán phòng khám lâm sàng, không thể gọi số hoặc bắt đầu khám.");
            }

            if (context.Room is not null &&
                !string.Equals(context.Room.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
            {
                throw new ConflictException("Phòng khám của hàng đợi hiện không hoạt động hoặc đang bảo trì.");
            }

            if (isExaminationQueue &&
                resolvedQueueTypeForUpdate == ClinicalQueueRules.QueueTypeExamination &&
                visit is not null)
            {
                var gate = await TryCheckExaminationEligibilitySafeAsync(visit.Id, ct);
                if (gate is { IsUnlocked: false })
                {
                    throw new ConflictException(
                        gate.Message ?? "Bệnh nhân chưa hoàn tất thanh toán phí khám trước khi gọi số hoặc bắt đầu khám.");
                }
            }

            if (currentStatus is not ("CALLED" or "IN_PROGRESS") && context.Room is not null)
            {
                await EnsureRoomCallCapacityAsync(queueEntry, context.Room, ct);
            }
        }

        if (targetStatus == "IN_PROGRESS")
        {
            await EnsureNoOtherActiveInProgressEntryAsync(queueEntry, visit, context.Room, ct);
        }

        ApplyStatusTransition(
            queueEntry,
            visit,
            targetStatus,
            requestedPresence,
            effectiveBlockedReason,
            previousStateVersion,
            nowLocal);

        var updatedByName = !string.IsNullOrWhiteSpace(operatorAccess.ActorStaff?.FullName)
            ? operatorAccess.ActorStaff.FullName
            : operatorAccess.ActorUser.Email;

        var auditPayload = new QueueStatusTransitionAuditPayload(
            QueueEntryId: queueEntry.Id,
            VisitId: visit?.Id ?? queueEntry.VisitId,
            VisitCode: visit?.VisitCode,
            PreviousStatus: currentStatus,
            TargetStatus: targetStatus,
            PreviousVisitStatus: previousVisitStatus,
            UpdatedVisitStatus: visit?.Status,
            PreviousPresenceStatus: previousPresenceStatus,
            UpdatedPresenceStatus: queueEntry.PresenceStatus,
            PreviousPriority: previousPriority,
            UpdatedPriority: queueEntry.ManualPriority,
            PreviousStateVersion: previousStateVersion,
            UpdatedStateVersion: queueEntry.StateVersion ?? (previousStateVersion + 1),
            Reason: effectiveReason,
            BlockedReason: queueEntry.BlockedReason,
            Notes: trimmedNotes,
            UpdatedByUserId: actorUserId,
            UpdatedByStaffCode: operatorAccess.ActorStaff?.StaffCode,
            UpdatedByName: updatedByName,
            UpdatedAtUtc: nowUtc);

        var auditLog = new AuditLog
        {
            ActorUserId = actorUserId,
            HospitalId = context.HospitalId,
            Action = QueueStatusUpdateAction,
            EntityType = QueueEntryEntityType,
            EntityId = queueEntry.Id.ToString(CultureInfo.InvariantCulture),
            Reason = effectiveReason,
            ChangeData = JsonSerializer.Serialize(auditPayload, JsonOptions),
            IpAddress = QueueEntryRequestParsing.ParseIpAddress(ipAddress),
            OccurredAt = nowUtc,
        };

        _db.AuditLogs.Add(auditLog);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        await _realtimePublisher.TryPublishQueueChangedSafeAsync(
            new QueueChangedNotification(
                HospitalId: context.HospitalId,
                ServicePointId: context.ServicePoint?.Id ?? queueEntry.CurrentServicePointId ?? queueEntry.ServicePointId,
                RoomId: context.Room?.Id ?? visit?.ExaminationRoomId,
                AppointmentId: queueEntry.AppointmentId ?? visit?.AppointmentId),
            ct);

        if (targetStatus == "COMPLETED" && (queueEntry.AppointmentId ?? visit?.AppointmentId) is long completedApptId)
        {
            await _accessRevoker.TryMigrateAppointmentBucketSafeAsync(completedApptId, null, null, ct);
        }

        return await BuildStatusResponseAsync(
            context,
            currentStatus,
            previousVisitStatus,
            previousPresenceStatus,
            previousStateVersion,
            effectiveReason,
            trimmedNotes,
            operatorAccess,
            actorUserId,
            updatedByName,
            nowUtc,
            ct);
    }

    public async Task<UpdateQueuePriorityResponseDto> UpdateQueueEntryPriorityAsync(
        long actorUserId,
        string idOrCode,
        UpdateQueuePriorityRequest request,
        string? ipAddress = null,
        CancellationToken ct = default)
    {
        if (actorUserId <= 0)
        {
            throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");
        }

        ArgumentNullException.ThrowIfNull(request);

        var (numericId, code) = QueueEntryRequestParsing.ParseIdentifier(idOrCode);

        var target = ClinicalQueueRules.NormalizePriorityOrThrow(request.Priority)
            ?? throw new ValidationException("Vui lòng chọn mức ưu tiên (NORMAL, ELDERLY, PREGNANT).");

        var reason = TrimAndValidateLength(request.Reason, "Lý do áp dụng mức ưu tiên")
            ?? throw new ValidationException("Vui lòng nhập lý do khi áp dụng hoặc thay đổi mức ưu tiên.");

        if (!request.ExpectedStateVersion.HasValue || request.ExpectedStateVersion.Value < 1)
        {
            throw new ValidationException("Phiên bản trạng thái (ExpectedStateVersion) là bắt buộc và phải lớn hơn hoặc bằng 1.");
        }

        var nowUtc = HospitalClock.GetUtcNow(_timeProvider);
        var nowLocal = HospitalClock.GetNow(_timeProvider);
        var operatorAccess = await EnsureOperatorAccessAsync(actorUserId, nowUtc, ct);
        EnsureQueueManagerAccess(operatorAccess, "Chỉ nhân viên tiếp đón hoặc quản trị được phép áp dụng mức ưu tiên.");

        var preContext = await ResolveQueueEntryContextAsync(
            numericId,
            code,
            idOrCode,
            operatorAccess,
            trackEntities: false,
            ct);

        var lockVisitId = preContext.Visit?.Id ?? preContext.QueueEntry.VisitId;
        var lockSpId = preContext.QueueEntry.CurrentServicePointId ?? preContext.QueueEntry.ServicePointId;
        var lockQueueEntryId = preContext.QueueEntry.Id;

        await using var tx = await _db.BeginTransactionAsync(ct);

        if (lockVisitId.HasValue)
        {
            await _db.TryLockVisitForUpdateAsync(lockVisitId.Value, ct);
        }

        if (lockSpId.HasValue)
        {
            await _db.TryLockServicePointForUpdateAsync(lockSpId.Value, ct);
        }

        await _db.TryLockQueueEntryForUpdateAsync(lockQueueEntryId, ct);

        if (_db is DbContext dbCtx && dbCtx.Database.ProviderName != "Microsoft.EntityFrameworkCore.InMemory")
        {
            dbCtx.ChangeTracker.Clear();
        }

        var context = await ResolveQueueEntryContextAsync(
            lockQueueEntryId,
            null,
            idOrCode,
            operatorAccess,
            trackEntities: true,
            ct);

        var queueEntry = context.QueueEntry;
        var visit = context.Visit;

        if (!ClinicalQueueRules.IsClinicalExaminationQueueType(queueEntry.QueueType))
        {
            throw new ConflictException("Chỉ áp dụng mức ưu tiên cho lượt khám lâm sàng (không áp dụng cho hàng đợi cận lâm sàng).");
        }

        var currentStatus = NormalizeCurrentQueueStatus(queueEntry.Status);
        if (currentStatus is "COMPLETED" or "CANCELLED" or "IN_PROGRESS" ||
            (visit is not null && IsTerminalVisitStatus(visit.Status)))
        {
            throw new ConflictException("Không thể thay đổi mức ưu tiên cho lượt đang khám, đã hoàn tất hoặc đã hủy.");
        }

        var previousStateVersion = queueEntry.StateVersion is > 0 ? queueEntry.StateVersion.Value : 1;
        if (request.ExpectedStateVersion.Value != previousStateVersion)
        {
            throw new ConflictException("Dữ liệu hàng đợi đã thay đổi bởi thao tác khác (state_version mismatch). Vui lòng tải lại hàng đợi.");
        }

        var previousPriority = ClinicalQueueRules.NormalizeStoredPriority(queueEntry.ManualPriority);
        if (string.Equals(previousPriority, target, StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException($"Lượt xếp hàng hiện đã ở mức ưu tiên {target}.");
        }

        ClinicalQueueRules.EnsurePatientEligible(target, context.Patient, DateOnly.FromDateTime(nowLocal));

        var servicePointId = queueEntry.CurrentServicePointId ?? queueEntry.ServicePointId;
        var verificationRequired = ClinicalQueueRules.IsElevated(target) &&
            await QueuePolicyReader.IsManualPriorityVerificationRequiredAsync(
                _db,
                context.HospitalId,
                servicePointId,
                nowUtc,
                ct);

        if (verificationRequired && request.VerificationConfirmed != true)
        {
            throw new ValidationException("Chính sách bệnh viện yêu cầu xác minh giấy tờ trước khi áp dụng ưu tiên ELDERLY/PREGNANT (verificationConfirmed = true).");
        }

        var updatedStateVersion = previousStateVersion + 1;
        queueEntry.ManualPriority = target;
        queueEntry.StateVersion = updatedStateVersion;

        var updatedByName = !string.IsNullOrWhiteSpace(operatorAccess.ActorStaff?.FullName)
            ? operatorAccess.ActorStaff.FullName
            : operatorAccess.ActorUser.Email;

        var verificationConfirmed = request.VerificationConfirmed == true;
        var auditPayload = new QueuePriorityChangeAuditPayload(
            QueueEntryId: queueEntry.Id,
            VisitId: visit?.Id ?? queueEntry.VisitId,
            VisitCode: visit?.VisitCode,
            PreviousPriority: previousPriority,
            UpdatedPriority: target,
            VerificationRequired: verificationRequired,
            VerificationConfirmed: verificationConfirmed,
            PreviousStateVersion: previousStateVersion,
            UpdatedStateVersion: updatedStateVersion,
            Reason: reason,
            UpdatedByUserId: actorUserId,
            UpdatedByStaffCode: operatorAccess.ActorStaff?.StaffCode,
            UpdatedByName: updatedByName,
            UpdatedAtUtc: nowUtc);

        var auditLog = new AuditLog
        {
            ActorUserId = actorUserId,
            HospitalId = context.HospitalId,
            Action = QueuePriorityUpdateAction,
            EntityType = QueueEntryEntityType,
            EntityId = queueEntry.Id.ToString(CultureInfo.InvariantCulture),
            Reason = reason,
            ChangeData = JsonSerializer.Serialize(auditPayload, JsonOptions),
            IpAddress = QueueEntryRequestParsing.ParseIpAddress(ipAddress),
            OccurredAt = nowUtc,
        };

        _db.AuditLogs.Add(auditLog);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        await _realtimePublisher.TryPublishQueueChangedSafeAsync(
            new QueueChangedNotification(
                HospitalId: context.HospitalId,
                ServicePointId: context.ServicePoint?.Id ?? queueEntry.CurrentServicePointId ?? queueEntry.ServicePointId,
                RoomId: context.Room?.Id ?? visit?.ExaminationRoomId,
                AppointmentId: queueEntry.AppointmentId ?? visit?.AppointmentId),
            ct);

        var queueNumber = queueEntry.QueueNumber is > 0 ? queueEntry.QueueNumber.Value : 1;
        var originRoomCode = await ResolveOriginTicketRoomCodeAsync(queueEntry, context.Room?.RoomCode, ct);
        var ticketNo = FormatTicketNo(queueNumber, originRoomCode);
        var visitCode = visit is not null
            ? (string.IsNullOrWhiteSpace(visit.VisitCode) ? $"VIS-{visit.Id:D4}" : visit.VisitCode)
            : null;

        return new UpdateQueuePriorityResponseDto(
            QueueEntryId: queueEntry.Id,
            VisitId: visit?.Id ?? queueEntry.VisitId,
            VisitCode: visitCode,
            TicketNo: ticketNo,
            QueueNumber: queueNumber,
            QueueType: ClinicalQueueRules.ResolveQueueType(queueEntry.QueueType, visit?.Status),
            CurrentStatus: currentStatus,
            PreviousPriority: previousPriority,
            Priority: target,
            VerificationRequired: verificationRequired,
            VerificationConfirmed: verificationConfirmed,
            Reason: reason,
            PreviousStateVersion: previousStateVersion,
            StateVersion: updatedStateVersion,
            UpdatedByUserId: actorUserId,
            UpdatedByStaffCode: operatorAccess.ActorStaff?.StaffCode,
            UpdatedByName: updatedByName,
            UpdatedAtUtc: nowUtc);
    }

    public async Task<UpdateQueueStatusResponseDto> CancelQueueEntryAsync(
        long actorUserId,
        string idOrCode,
        CancelQueueEntryRequest request,
        string? ipAddress = null,
        CancellationToken ct = default)
    {
        if (actorUserId <= 0)
        {
            throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");
        }

        ArgumentNullException.ThrowIfNull(request);

        var (numericId, code) = QueueEntryRequestParsing.ParseIdentifier(idOrCode);

        var reason = TrimAndValidateLength(request.Reason, "Lý do hủy lượt")
            ?? throw new ValidationException("Vui lòng nhập lý do hủy lượt xếp hàng.");

        var notes = TrimAndValidateLength(request.Notes, "Ghi chú");

        if (!request.ExpectedStateVersion.HasValue || request.ExpectedStateVersion.Value < 1)
        {
            throw new ValidationException("Phiên bản trạng thái (ExpectedStateVersion) là bắt buộc và phải lớn hơn hoặc bằng 1.");
        }

        var disposition = NormalizeAndValidateCancelDisposition(request.Disposition);
        if (request.TargetRoomId.HasValue && request.TargetRoomId.Value <= 0)
        {
            throw new ValidationException("ID phòng khám đích phải lớn hơn 0.");
        }

        if (request.TargetDoctorId.HasValue && request.TargetDoctorId.Value <= 0)
        {
            throw new ValidationException("ID bác sĩ đích phải lớn hơn 0.");
        }

        var nowUtc = HospitalClock.GetUtcNow(_timeProvider);
        var nowLocal = HospitalClock.GetNow(_timeProvider);
        var operatorAccess = await EnsureOperatorAccessAsync(actorUserId, nowUtc, ct);
        EnsureQueueManagerAccess(operatorAccess, "Chỉ nhân viên tiếp đón hoặc quản trị được phép hủy lượt xếp hàng.");

        var preContext = await ResolveQueueEntryContextAsync(
            numericId,
            code,
            idOrCode,
            operatorAccess,
            trackEntities: false,
            ct);

        var preVisitId = preContext.Visit?.Id ?? preContext.QueueEntry.VisitId;
        var preApptId = preContext.QueueEntry.AppointmentId ?? preContext.Visit?.AppointmentId;
        var preSourceSpId = preContext.QueueEntry.CurrentServicePointId
            ?? preContext.QueueEntry.ServicePointId
            ?? preContext.ServicePoint?.Id;
        var preSourceRoomId = preContext.Room?.Id ?? preContext.Visit?.ExaminationRoomId;
        var preQueueEntryId = preContext.QueueEntry.Id;

        HospitalRoom? preReassignRoom = null;
        Doctor? preReassignDoctor = null;
        long? preReassignSpId = null;

        if (disposition == QueueCancelDispositions.ReassignRequeue && request.TargetRoomId.HasValue)
        {
            preReassignRoom = await _db.HospitalRooms
                .AsNoTracking()
                .Include(r => r.Department)
                .FirstOrDefaultAsync(r => r.Id == request.TargetRoomId.Value, ct)
                ?? throw new NotFoundException("HospitalRoom", request.TargetRoomId.Value);

            if (!preReassignRoom.HospitalId.HasValue || preReassignRoom.HospitalId.Value != preContext.HospitalId)
            {
                throw new ForbiddenException("Phòng khám điều phối lại không thuộc cùng cơ sở y tế.");
            }

            if (!string.Equals(preReassignRoom.RoomType, "EXAMINATION", StringComparison.OrdinalIgnoreCase))
            {
                throw new ValidationException("Phòng đích điều phối lại phải là buồng khám lâm sàng (EXAMINATION).");
            }

            if (preContext.Department?.Id is long reqDeptId &&
                preReassignRoom.DepartmentId.HasValue &&
                preReassignRoom.DepartmentId.Value != reqDeptId)
            {
                throw new ValidationException("Phòng đích điều phối lại không thuộc cùng khoa phòng với lượt khám.");
            }

            if (request.TargetDoctorId.HasValue)
            {
                preReassignDoctor = await _db.Doctors
                    .AsNoTracking()
                    .Include(d => d.Staff)
                    .FirstOrDefaultAsync(d => d.Id == request.TargetDoctorId.Value, ct)
                    ?? throw new NotFoundException("Doctor", request.TargetDoctorId.Value);

                var docHospId = preReassignDoctor.HospitalId ?? preReassignDoctor.Staff?.HospitalId;
                if (docHospId.HasValue && docHospId.Value != preContext.HospitalId)
                {
                    throw new ForbiddenException("Bác sĩ được chọn không thuộc cùng cơ sở y tế.");
                }

                if (!preReassignDoctor.IsActive ||
                    (preReassignDoctor.Staff is not null &&
                     !string.Equals(preReassignDoctor.Staff.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase)))
                {
                    throw new ConflictException("Bác sĩ được chọn hiện không ở trạng thái hoạt động.");
                }
            }

            preReassignSpId = await TryResolveRoomServicePointIdAsync(
                preReassignRoom.Id,
                preReassignDoctor?.Id,
                preContext.HospitalId,
                ct);
        }

        var chargeIdsToLock = preVisitId.HasValue
            ? await TryLoadVisitChargeIdsForLockAsync(preVisitId.Value, ct)
            : [];
        var paymentIdsToLock = preVisitId.HasValue
            ? await TryLoadPaymentIdsForLockAsync(preVisitId.Value, chargeIdsToLock, ct)
            : [];

        var doctorIdsToLock = new[] { preContext.Visit?.AssignedDoctorId, request.TargetDoctorId }
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .OrderBy(id => id)
            .ToArray();
        var roomIdsToLock = new[] { preSourceRoomId, request.TargetRoomId }
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .OrderBy(id => id)
            .ToArray();
        var spIdsToLock = new[] { preSourceSpId, preReassignSpId }
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .OrderBy(id => id)
            .ToArray();

        await using var tx = await _db.BeginTransactionAsync(ct);

        // ADR-0059 strict lock order: doctors (05) -> hospital_rooms (06) -> appointments (08) -> visits (09) -> queue_service_points (10) -> queue_entries (11) -> visit_charges (15) -> payments (16)
        if (doctorIdsToLock.Length > 0 && disposition == QueueCancelDispositions.ReassignRequeue)
        {
            await _db.TryLockDoctorsForUpdateAsync(doctorIdsToLock, ct);
        }

        if (roomIdsToLock.Length > 0 && disposition == QueueCancelDispositions.ReassignRequeue)
        {
            await _db.TryLockRoomsForUpdateAsync(roomIdsToLock, ct);
        }

        if (preApptId.HasValue && disposition != QueueCancelDispositions.ReassignRequeue)
        {
            await _db.TryLockAppointmentForUpdateAsync(preApptId.Value, ct);
        }

        if (preVisitId.HasValue)
        {
            await _db.TryLockVisitForUpdateAsync(preVisitId.Value, ct);
        }

        if (spIdsToLock.Length > 0)
        {
            await _db.TryLockServicePointsForUpdateAsync(spIdsToLock, ct);
        }

        await _db.TryLockQueueEntryForUpdateAsync(preQueueEntryId, ct);

        if (disposition != QueueCancelDispositions.ReassignRequeue)
        {
            if (chargeIdsToLock.Count > 0)
            {
                await _db.TryLockVisitChargesForUpdateAsync(chargeIdsToLock, ct);
            }

            if (paymentIdsToLock.Count > 0)
            {
                await _db.TryLockPaymentsForUpdateAsync(paymentIdsToLock, ct);
            }
        }

        if (_db is DbContext dbCtx && dbCtx.Database.ProviderName != "Microsoft.EntityFrameworkCore.InMemory")
        {
            dbCtx.ChangeTracker.Clear();
        }

        var context = await ResolveQueueEntryContextAsync(
            preQueueEntryId,
            null,
            idOrCode,
            operatorAccess,
            trackEntities: true,
            ct);

        var queueEntry = context.QueueEntry;
        var visit = context.Visit;

        if (!ClinicalQueueRules.IsClinicalExaminationQueueType(queueEntry.QueueType))
        {
            throw new ConflictException("Chỉ hủy được lượt khám lâm sàng (không áp dụng cho hàng đợi cận lâm sàng).");
        }

        var currentStatus = NormalizeCurrentQueueStatus(queueEntry.Status);
        if (currentStatus == "CANCELLED")
        {
            throw new ConflictException("Lượt xếp hàng đã được hủy trước đó.");
        }

        if (currentStatus is "IN_PROGRESS" or "COMPLETED" ||
            (visit is not null && IsTerminalVisitStatus(visit.Status)))
        {
            throw new ConflictException("Không thể hủy lượt đang khám (IN_PROGRESS) hoặc đã hoàn tất.");
        }

        var previousStateVersion = queueEntry.StateVersion is > 0 ? queueEntry.StateVersion.Value : 1;
        if (request.ExpectedStateVersion.Value != previousStateVersion)
        {
            throw new ConflictException("Dữ liệu hàng đợi đã thay đổi bởi thao tác khác (state_version mismatch). Vui lòng tải lại hàng đợi.");
        }

        var previousVisitStatus = visit?.Status;
        var previousPresenceStatus = string.IsNullOrWhiteSpace(queueEntry.PresenceStatus)
            ? "PRESENT"
            : queueEntry.PresenceStatus.ToUpperInvariant();
        var previousPriority = ClinicalQueueRules.NormalizeStoredPriority(queueEntry.ManualPriority);
        var resolvedQueueType = ClinicalQueueRules.ResolveQueueType(queueEntry.QueueType, visit?.Status);

        var visitCancelled = false;
        long? migratedTargetSpId = null;
        long? migratedTargetRoomId = null;

        if (disposition == QueueCancelDispositions.ReassignRequeue)
        {
            // Operational queue entry cancellation / re-routing: preserve Visit, Appointment, and VisitCharge!
            ApplyStatusTransition(
                queueEntry,
                visit: null,
                targetStatus: "CANCELLED",
                requestedPresence: previousPresenceStatus == "NOT_ARRIVED" ? "NOT_ARRIVED" : "PRESENT",
                effectiveBlockedReason: reason,
                previousStateVersion: previousStateVersion,
                nowLocal: nowLocal);

            if (visit is not null)
            {
                if (preReassignRoom is not null)
                {
                    var latestReassignRoom = await _db.HospitalRooms
                        .AsNoTracking()
                        .FirstOrDefaultAsync(r => r.Id == preReassignRoom.Id, ct)
                        ?? throw new NotFoundException("HospitalRoom", preReassignRoom.Id);

                    if (!string.Equals(latestReassignRoom.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new ConflictException("Phòng khám đích điều phối lại hiện không hoạt động.");
                    }

                    var targetCap = latestReassignRoom.Capacity is > 0 ? latestReassignRoom.Capacity.Value : 1;
                    var targetOcc = await CountActiveRoomVisitsExcludingAsync(latestReassignRoom.Id, visit.Id, ct);
                    if (targetOcc >= targetCap)
                    {
                        throw new ConflictException("Phòng khám đích điều phối lại đã đạt sức chứa tối đa.");
                    }

                    visit.ExaminationRoomId = latestReassignRoom.Id;
                    visit.AssignedDoctorId = preReassignDoctor?.Id;
                    visit.Status = resolvedQueueType == ClinicalQueueRules.QueueTypeReturnConsultation
                        ? "RETURN_CONSULTATION"
                        : "WAITING";

                    // Flush CANCELLED status on the previous QueueEntry inside the open transaction
                    // before inserting the replacement QueueEntry so active-entry partial unique indexes never collide.
                    await _db.SaveChangesAsync(ct);

                    var today = DateOnly.FromDateTime(nowLocal);
                    var nextQueueNo = await AllocateNextQueueNumberForServicePointAsync(preReassignSpId, today, ct);
                    _db.QueueEntries.Add(new QueueEntry
                    {
                        VisitId = visit.Id,
                        AppointmentId = queueEntry.AppointmentId ?? visit.AppointmentId,
                        ServicePointId = preReassignSpId,
                        CurrentServicePointId = preReassignSpId,
                        QueueDate = today,
                        QueueNumber = nextQueueNo,
                        QueueType = resolvedQueueType,
                        Status = "WAITING",
                        ScheduledAt = queueEntry.ScheduledAt,
                        PresenceStatus = "PRESENT",
                        ArrivedAt = queueEntry.ArrivedAt ?? visit.CheckedInAt ?? nowLocal,
                        ArrivalClass = queueEntry.ArrivalClass ?? "ON_TIME_OR_GRACE",
                        ManualPriority = previousPriority,
                        StateVersion = 1,
                        ReadyAt = nowLocal,
                    });

                    migratedTargetSpId = preReassignSpId;
                    migratedTargetRoomId = latestReassignRoom.Id;
                }
                else
                {
                    visit.ExaminationRoomId = null;
                    visit.AssignedDoctorId = null;
                    visit.Status = resolvedQueueType == ClinicalQueueRules.QueueTypeReturnConsultation
                        ? "RETURN_CONSULTATION"
                        : "CHECKED_IN";
                }
            }
        }
        else
        {
            var isPreConsultationVisit = resolvedQueueType == ClinicalQueueRules.QueueTypeExamination &&
                visit is not null &&
                AppointmentBookingRules.NormalizeStatusKey(visit.Status) is not (
                    "AWAITING_DIAGNOSTIC" or "AWAITING_RESULTS" or "RETURN_CONSULTATION" or "COMPLETED");

            var visitToCancel = isPreConsultationVisit ? visit : null;
            var cancelPresence = previousPresenceStatus == "NOT_ARRIVED" ? "NOT_ARRIVED" : "LEFT";

            ApplyStatusTransition(
                queueEntry,
                visitToCancel,
                "CANCELLED",
                cancelPresence,
                reason,
                previousStateVersion,
                nowLocal);

            if (visitToCancel is not null)
            {
                visitCancelled = true;

                if (disposition == QueueCancelDispositions.PatientAbandoned && preApptId.HasValue)
                {
                    var linkedAppt = await _db.Appointments
                        .FirstOrDefaultAsync(a => a.Id == preApptId.Value, ct);
                    if (linkedAppt is not null)
                    {
                        var apptStatus = AppointmentBookingRules.NormalizeStatusKey(linkedAppt.Status);
                        if (apptStatus is not ("COMPLETED" or "CANCELLED" or "NO_SHOW"))
                        {
                            linkedAppt.Status = AppointmentBookingRules.CancelledStatus;
                            linkedAppt.CancelledAt = nowLocal;
                            linkedAppt.CancellationReason = string.IsNullOrWhiteSpace(linkedAppt.CancellationReason)
                                ? reason
                                : linkedAppt.CancellationReason;
                        }
                    }
                }

                await ReconcileCancelledPreConsultationChargesAsync(
                    visitToCancel,
                    disposition,
                    actorUserId,
                    context.HospitalId,
                    reason,
                    ipAddress,
                    nowLocal,
                    nowUtc,
                    ct);
            }
        }

        var updatedByName = !string.IsNullOrWhiteSpace(operatorAccess.ActorStaff?.FullName)
            ? operatorAccess.ActorStaff.FullName
            : operatorAccess.ActorUser.Email;

        var auditPayload = new QueueStatusTransitionAuditPayload(
            QueueEntryId: queueEntry.Id,
            VisitId: visit?.Id ?? queueEntry.VisitId,
            VisitCode: visit?.VisitCode,
            PreviousStatus: currentStatus,
            TargetStatus: "CANCELLED",
            PreviousVisitStatus: previousVisitStatus,
            UpdatedVisitStatus: visit?.Status,
            PreviousPresenceStatus: previousPresenceStatus,
            UpdatedPresenceStatus: queueEntry.PresenceStatus,
            PreviousPriority: previousPriority,
            UpdatedPriority: queueEntry.ManualPriority,
            PreviousStateVersion: previousStateVersion,
            UpdatedStateVersion: queueEntry.StateVersion ?? (previousStateVersion + 1),
            Reason: reason,
            BlockedReason: queueEntry.BlockedReason,
            Notes: notes,
            UpdatedByUserId: actorUserId,
            UpdatedByStaffCode: operatorAccess.ActorStaff?.StaffCode,
            UpdatedByName: updatedByName,
            UpdatedAtUtc: nowUtc,
            Disposition: disposition);

        var auditLog = new AuditLog
        {
            ActorUserId = actorUserId,
            HospitalId = context.HospitalId,
            Action = QueueEntryCancelAction,
            EntityType = QueueEntryEntityType,
            EntityId = queueEntry.Id.ToString(CultureInfo.InvariantCulture),
            Reason = reason,
            ChangeData = JsonSerializer.Serialize(auditPayload, JsonOptions),
            IpAddress = QueueEntryRequestParsing.ParseIpAddress(ipAddress),
            OccurredAt = nowUtc,
        };

        _db.AuditLogs.Add(auditLog);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        var cancelledApptId = queueEntry.AppointmentId ?? visit?.AppointmentId;
        await _realtimePublisher.TryPublishQueueChangedSafeAsync(
            new QueueChangedNotification(
                HospitalId: context.HospitalId,
                ServicePointId: preSourceSpId,
                RoomId: preSourceRoomId,
                AppointmentId: cancelledApptId),
            ct);

        if (migratedTargetRoomId.HasValue || migratedTargetSpId.HasValue)
        {
            if (cancelledApptId.HasValue)
            {
                await _accessRevoker.TryMigrateAppointmentBucketSafeAsync(
                    cancelledApptId.Value,
                    migratedTargetSpId,
                    migratedTargetRoomId,
                    ct);
            }

            await _realtimePublisher.TryPublishQueueChangedSafeAsync(
                new QueueChangedNotification(
                    HospitalId: context.HospitalId,
                    ServicePointId: migratedTargetSpId,
                    RoomId: migratedTargetRoomId,
                    AppointmentId: cancelledApptId),
                ct);
        }
        else if (visitCancelled && cancelledApptId is long revokedApptId)
        {
            await _accessRevoker.TryRevokeAppointmentTrackingSafeAsync(revokedApptId, ct);
        }

        return await BuildStatusResponseAsync(
            context,
            currentStatus,
            previousVisitStatus,
            previousPresenceStatus,
            previousStateVersion,
            reason,
            notes,
            operatorAccess,
            actorUserId,
            updatedByName,
            nowUtc,
            ct);
    }

    private async Task<UpdateQueueStatusResponseDto> BuildStatusResponseAsync(
        QueueEntryContext context,
        string previousStatus,
        string? previousVisitStatus,
        string previousPresenceStatus,
        int previousStateVersion,
        string? reason,
        string? notes,
        OperatorAccessContext operatorAccess,
        long actorUserId,
        string updatedByName,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var queueEntry = context.QueueEntry;
        var visit = context.Visit;
        var currentStatus = NormalizeCurrentQueueStatus(queueEntry.Status);
        var nextAllowedTargets = GetAllowedTargetStatuses(currentStatus, visit);
        var statusHistory = await LoadStatusHistoryAsync(queueEntry.Id, ct);
        var queueNumber = queueEntry.QueueNumber is > 0 ? queueEntry.QueueNumber.Value : 1;
        var originRoomCode = await ResolveOriginTicketRoomCodeAsync(queueEntry, context.Room?.RoomCode, ct);
        var ticketNo = FormatTicketNo(queueNumber, originRoomCode);

        return new UpdateQueueStatusResponseDto(
            QueueEntryId: queueEntry.Id,
            VisitId: visit?.Id ?? queueEntry.VisitId,
            VisitCode: visit is not null
                ? (string.IsNullOrWhiteSpace(visit.VisitCode) ? $"VIS-{visit.Id:D4}" : visit.VisitCode)
                : null,
            AppointmentId: queueEntry.AppointmentId ?? visit?.AppointmentId,
            PatientId: context.Patient?.Id ?? visit?.PatientId,
            PatientCode: context.Patient?.PatientCode,
            PatientName: context.Patient?.FullName,
            HospitalId: context.HospitalId,
            DepartmentId: context.Department?.Id ?? context.Room?.DepartmentId,
            DepartmentCode: context.Department?.DepartmentCode,
            DepartmentName: context.Department?.Name,
            RoomId: context.Room?.Id ?? visit?.ExaminationRoomId,
            RoomCode: context.Room?.RoomCode,
            RoomName: context.Room?.Name,
            AssignedDoctorId: context.AssignedDoctor?.Id ?? visit?.AssignedDoctorId,
            AssignedDoctorCode: context.AssignedDoctor?.DoctorCode,
            AssignedDoctorName: context.AssignedDoctor?.Staff?.FullName,
            ServicePointId: context.ServicePoint?.Id ?? queueEntry.CurrentServicePointId ?? queueEntry.ServicePointId,
            ServicePointName: context.ServicePoint?.Name ?? context.Room?.Name,
            QueueDate: queueEntry.QueueDate,
            QueueNumber: queueNumber,
            TicketNo: ticketNo,
            QueueType: string.IsNullOrWhiteSpace(queueEntry.QueueType) ? "EXAMINATION" : queueEntry.QueueType.ToUpperInvariant(),
            PreviousStatus: previousStatus,
            CurrentStatus: currentStatus,
            PreviousVisitStatus: previousVisitStatus,
            VisitStatus: visit?.Status,
            PreviousPresenceStatus: previousPresenceStatus,
            PresenceStatus: queueEntry.PresenceStatus ?? "PRESENT",
            Priority: ClinicalQueueRules.NormalizeStoredPriority(queueEntry.ManualPriority),
            ArrivalClass: string.IsNullOrWhiteSpace(queueEntry.ArrivalClass) ? "ON_TIME_OR_GRACE" : queueEntry.ArrivalClass.ToUpperInvariant(),
            PreviousStateVersion: previousStateVersion,
            StateVersion: queueEntry.StateVersion ?? (previousStateVersion + 1),
            ScheduledAt: queueEntry.ScheduledAt,
            ArrivedAt: queueEntry.ArrivedAt,
            ReadyAt: queueEntry.ReadyAt,
            CalledAt: queueEntry.CalledAt,
            StartedAt: queueEntry.StartedAt,
            CompletedAt: queueEntry.CompletedAt,
            BlockedReason: queueEntry.BlockedReason,
            Reason: reason,
            Notes: notes,
            UpdatedByUserId: actorUserId,
            UpdatedByStaffCode: operatorAccess.ActorStaff?.StaffCode,
            UpdatedByName: updatedByName,
            UpdatedAtUtc: nowUtc,
            AllowedTargetStatuses: nextAllowedTargets,
            StatusHistory: statusHistory);
    }
    private static string ResolveAndValidateTargetStatus(UpdateQueueStatusRequest request)
    {
        var hasTarget = !string.IsNullOrWhiteSpace(request.TargetStatus);
        var hasStatus = !string.IsNullOrWhiteSpace(request.Status);

        if (!hasTarget && !hasStatus)
        {
            throw new ValidationException("Vui lòng chọn trạng thái hàng đợi đích.");
        }

        var normalizedTarget = hasTarget ? NormalizeTargetStatusAlias(request.TargetStatus!) : null;
        var normalizedStatus = hasStatus ? NormalizeTargetStatusAlias(request.Status!) : null;

        if (normalizedTarget is not null &&
            normalizedStatus is not null &&
            !string.Equals(normalizedTarget, normalizedStatus, StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException("Trạng thái đích trong yêu cầu không đồng nhất.");
        }

        var resolved = normalizedTarget ?? normalizedStatus!;
        if (resolved == "CANCELLED")
        {
            throw new ValidationException("Vui lòng sử dụng chức năng Hủy lượt (POST /api/v1/queue-entries/{idOrCode}/cancel) để hủy lượt xếp hàng.");
        }

        if (!ValidTargetStatuses.Contains(resolved))
        {
            throw new ValidationException("Trạng thái hàng đợi không hợp lệ. Chỉ chấp nhận: WAITING, READY, CALLED, IN_PROGRESS, COMPLETED, SKIPPED.");
        }

        return resolved;
    }

    private static string NormalizeTargetStatusAlias(string rawStatus)
    {
        var key = AppointmentBookingRules.NormalizeStatusKey(rawStatus);
        return key switch
        {
            "IN_CONSULTATION" or "SERVING" => "IN_PROGRESS",
            "DONE" => "COMPLETED",
            _ => key,
        };
    }

    private static string NormalizeCurrentQueueStatus(string? rawStatus)
    {
        if (string.IsNullOrWhiteSpace(rawStatus))
        {
            return "WAITING";
        }

        var key = NormalizeTargetStatusAlias(rawStatus);
        return AllowedTransitions.ContainsKey(key) ? key : "WAITING";
    }

    private static string? NormalizeAndValidatePresenceStatus(string? rawPresence)
    {
        if (string.IsNullOrWhiteSpace(rawPresence))
        {
            return null;
        }

        var normalized = AppointmentBookingRules.NormalizeStatusKey(rawPresence);
        if (!ValidPresenceStatuses.Contains(normalized))
        {
            throw new ValidationException("Trạng thái hiện diện không hợp lệ. Chỉ chấp nhận: PRESENT, NOT_ARRIVED, LEFT.");
        }

        return normalized;
    }

    private static string? TrimAndValidateLength(string? value, string fieldLabel)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > 500)
        {
            throw new ValidationException($"{fieldLabel} không được vượt quá 500 ký tự.");
        }

        return trimmed;
    }

    private static bool IsTerminalVisitStatus(string? status)
    {
        var normalized = AppointmentBookingRules.NormalizeStatusKey(status);
        return normalized is "COMPLETED" or "CANCELLED" or "NO_SHOW" or "DONE";
    }

    private static IReadOnlyList<string> GetAllowedTargetStatuses(string currentStatus, Visit? visit)
    {
        if (visit is not null && IsTerminalVisitStatus(visit.Status))
        {
            return [];
        }

        return AllowedTransitions.TryGetValue(currentStatus, out var targets)
            ? targets
            : [];
    }

    private static void ApplyStatusTransition(
        QueueEntry queueEntry,
        Visit? visit,
        string targetStatus,
        string? requestedPresence,
        string? effectiveBlockedReason,
        int previousStateVersion,
        DateTime nowLocal)
    {
        var previousStatus = NormalizeCurrentQueueStatus(queueEntry.Status);
        queueEntry.Status = targetStatus;
        queueEntry.StateVersion = previousStateVersion + 1;
        queueEntry.QueueDate ??= DateOnly.FromDateTime(nowLocal);
        queueEntry.QueueType = string.IsNullOrWhiteSpace(queueEntry.QueueType)
            ? "EXAMINATION"
            : queueEntry.QueueType.ToUpperInvariant();
        queueEntry.ArrivalClass ??= "ON_TIME_OR_GRACE";
        queueEntry.ManualPriority ??= ClinicalQueueRules.PriorityNormal;
        var isClinicalQueue = ClinicalQueueRules.IsClinicalExaminationQueueType(queueEntry.QueueType);
        var isReturnConsultation = isClinicalQueue &&
                                   ClinicalQueueRules.ResolveQueueType(queueEntry.QueueType, visit?.Status) ==
                                   ClinicalQueueRules.QueueTypeReturnConsultation;
        switch (targetStatus)
        {
            case "WAITING":
                queueEntry.ArrivedAt ??= visit?.CheckedInAt ?? nowLocal;
                queueEntry.PresenceStatus = requestedPresence ??
                    (string.IsNullOrWhiteSpace(queueEntry.PresenceStatus) ||
                     queueEntry.PresenceStatus.Equals("NOT_ARRIVED", StringComparison.OrdinalIgnoreCase) ||
                     queueEntry.PresenceStatus.Equals("LEFT", StringComparison.OrdinalIgnoreCase)
                        ? "PRESENT"
                        : queueEntry.PresenceStatus.ToUpperInvariant());
                if (previousStatus is "CALLED" or "IN_PROGRESS" or "SKIPPED")
                {
                    queueEntry.CalledAt = null;
                    queueEntry.StartedAt = null;
                    queueEntry.BlockedReason = null;
                }

                if (visit is not null && isClinicalQueue)
                {
                    visit.Status = isReturnConsultation ? "RETURN_CONSULTATION" : "WAITING";
                }

                break;

            case "READY":
                queueEntry.ArrivedAt ??= visit?.CheckedInAt ?? nowLocal;
                queueEntry.ReadyAt = nowLocal;
                queueEntry.PresenceStatus = requestedPresence ?? "PRESENT";
                queueEntry.CalledAt = null;
                queueEntry.StartedAt = null;
                queueEntry.BlockedReason = null;
                if (visit is not null && isClinicalQueue)
                {
                    visit.Status = isReturnConsultation ? "RETURN_CONSULTATION" : "WAITING";
                }

                break;

            case "CALLED":
                queueEntry.ArrivedAt ??= visit?.CheckedInAt ?? nowLocal;
                queueEntry.ReadyAt ??= nowLocal;
                queueEntry.CalledAt = nowLocal;
                queueEntry.StartedAt = null;
                queueEntry.BlockedReason = null;
                queueEntry.PresenceStatus = requestedPresence ??
                    (string.IsNullOrWhiteSpace(queueEntry.PresenceStatus) ||
                     queueEntry.PresenceStatus.Equals("NOT_ARRIVED", StringComparison.OrdinalIgnoreCase) ||
                     queueEntry.PresenceStatus.Equals("LEFT", StringComparison.OrdinalIgnoreCase)
                        ? "PRESENT"
                        : queueEntry.PresenceStatus.ToUpperInvariant());
                if (visit is not null && isClinicalQueue)
                {
                    visit.Status = isReturnConsultation ? "RETURN_CONSULTATION" : "CALLED";
                }

                break;

            case "IN_PROGRESS":
                queueEntry.ArrivedAt ??= visit?.CheckedInAt ?? nowLocal;
                queueEntry.ReadyAt ??= nowLocal;
                queueEntry.CalledAt ??= nowLocal;
                queueEntry.StartedAt = nowLocal;
                queueEntry.PresenceStatus = "PRESENT";
                queueEntry.BlockedReason = null;
                if (visit is not null && isClinicalQueue)
                {
                    visit.Status = "IN_PROGRESS";
                }

                break;

            case "COMPLETED":
                queueEntry.ArrivedAt ??= visit?.CheckedInAt ?? nowLocal;
                queueEntry.ReadyAt ??= nowLocal;
                queueEntry.CalledAt ??= nowLocal;
                queueEntry.StartedAt ??= nowLocal;
                queueEntry.CompletedAt = nowLocal;
                queueEntry.PresenceStatus = requestedPresence ?? "PRESENT";
                queueEntry.BlockedReason = null;
                if (visit is not null && isClinicalQueue)
                {
                    visit.Status = "COMPLETED";
                    visit.CompletedAt = nowLocal;
                    visit.CompletedByDoctorId ??= visit.AssignedDoctorId;
                }

                break;

            case "SKIPPED":
                queueEntry.BlockedReason = effectiveBlockedReason;
                queueEntry.StartedAt = null;
                queueEntry.PresenceStatus = requestedPresence ??
                    (string.Equals(queueEntry.PresenceStatus, "PRESENT", StringComparison.OrdinalIgnoreCase)
                        ? "LEFT"
                        : (string.IsNullOrWhiteSpace(queueEntry.PresenceStatus) ? "NOT_ARRIVED" : queueEntry.PresenceStatus.ToUpperInvariant()));
                if (visit is not null && isClinicalQueue)
                {
                    visit.Status = isReturnConsultation ? "RETURN_CONSULTATION" : "WAITING";
                }

                break;

            case "CANCELLED":
                queueEntry.BlockedReason = effectiveBlockedReason;
                queueEntry.CompletedAt = nowLocal;
                queueEntry.PresenceStatus = requestedPresence ?? "LEFT";
                if (visit is not null && isClinicalQueue)
                {
                    visit.Status = "CANCELLED";
                    visit.CompletedAt = null;
                }

                break;
        }
    }

    private async Task EnsureRoomCallCapacityAsync(
        QueueEntry currentEntry,
        HospitalRoom room,
        CancellationToken ct)
    {
        var effectiveRoomCapacity = room.Capacity is > 0 ? room.Capacity.Value : 1;

        var activeEntries = await _db.QueueEntries
            .AsNoTracking()
            .Where(q =>
                q.Id != currentEntry.Id &&
                q.CompletedAt == null &&
                (!currentEntry.QueueDate.HasValue || q.QueueDate == currentEntry.QueueDate) &&
                q.Status != null &&
                (q.Status.ToUpper() == "CALLED" ||
                 q.Status.ToUpper() == "IN_PROGRESS" ||
                 q.Status.ToUpper() == "IN_CONSULTATION" ||
                 q.Status.ToUpper() == "SERVING"))
            .ToListAsync(ct);

        if (activeEntries.Count == 0)
        {
            return;
        }

        var roomSpIds = new HashSet<long>();
        try
        {
            roomSpIds = (await _db.QueueServicePoints
                    .AsNoTracking()
                    .Where(sp => sp.RoomId == room.Id && sp.IsActive != false)
                    .Select(sp => sp.Id)
                    .ToListAsync(ct))
                .ToHashSet();
        }
        catch (NotSupportedException)
        {
            // Ignore when QueueServicePoints DbSet is not configured in lightweight test double.
        }

        var activeVisitIds = activeEntries
            .Where(q => q.VisitId.HasValue)
            .Select(q => q.VisitId!.Value)
            .Distinct()
            .ToList();

        var sameRoomVisitIds = activeVisitIds.Count == 0
            ? new HashSet<long>()
            : (await _db.Visits
                    .AsNoTracking()
                    .Where(v =>
                        activeVisitIds.Contains(v.Id) &&
                        v.ExaminationRoomId == room.Id &&
                        v.CompletedAt == null)
                    .Select(v => v.Id)
                    .ToListAsync(ct))
                .ToHashSet();

        var sameRoomActiveCount = activeEntries.Count(q =>
            (q.VisitId.HasValue && sameRoomVisitIds.Contains(q.VisitId.Value)) ||
            (!q.VisitId.HasValue &&
             ((q.CurrentServicePointId.HasValue && roomSpIds.Contains(q.CurrentServicePointId.Value)) ||
              (q.ServicePointId.HasValue && roomSpIds.Contains(q.ServicePointId.Value)))));

        if (sameRoomActiveCount >= effectiveRoomCapacity)
        {
            throw new ConflictException(
                "Phòng khám đang có bệnh nhân được gọi hoặc đang khám đạt sức chứa tối đa. Hoàn tất hoặc đánh dấu qua lượt trước khi gọi bệnh nhân tiếp theo.");
        }
    }

    private async Task EnsureNoOtherActiveInProgressEntryAsync(
        QueueEntry currentEntry,
        Visit? currentVisit,
        HospitalRoom? room,
        CancellationToken ct)
    {
        var effectiveServicePointId = currentEntry.CurrentServicePointId ?? currentEntry.ServicePointId;
        var effectiveRoomId = currentVisit?.ExaminationRoomId ?? room?.Id;

        if (!effectiveServicePointId.HasValue && !effectiveRoomId.HasValue)
        {
            return;
        }

        var candidateEntries = await _db.QueueEntries
            .AsNoTracking()
            .Where(q =>
                q.Id != currentEntry.Id &&
                q.CompletedAt == null &&
                (!currentEntry.QueueDate.HasValue || q.QueueDate == currentEntry.QueueDate) &&
                q.Status != null &&
                (q.Status.ToUpper() == "IN_PROGRESS" ||
                 q.Status.ToUpper() == "IN_CONSULTATION" ||
                 q.Status.ToUpper() == "SERVING"))
            .ToListAsync(ct);

        if (candidateEntries.Count == 0)
        {
            return;
        }

        if (effectiveServicePointId.HasValue &&
            candidateEntries.Any(q =>
                q.CurrentServicePointId == effectiveServicePointId.Value ||
                q.ServicePointId == effectiveServicePointId.Value))
        {
            throw new ConflictException("Điểm dịch vụ/phòng khám hiện đang có bệnh nhân khác trong trạng thái đang khám (IN_PROGRESS).");
        }

        if (effectiveRoomId.HasValue)
        {
            var effectiveRoomCapacity = room?.Capacity is > 0 ? room.Capacity.Value : 1;
            var candidateVisitIds = candidateEntries
                .Where(q => q.VisitId.HasValue)
                .Select(q => q.VisitId!.Value)
                .Distinct()
                .ToList();

            if (candidateVisitIds.Count > 0)
            {
                var sameRoomActiveCount = await _db.Visits
                    .AsNoTracking()
                    .CountAsync(v =>
                        candidateVisitIds.Contains(v.Id) &&
                        v.ExaminationRoomId == effectiveRoomId.Value &&
                        v.CompletedAt == null,
                        ct);

                if (sameRoomActiveCount >= effectiveRoomCapacity)
                {
                    throw new ConflictException("Điểm dịch vụ/phòng khám hiện đang có bệnh nhân khác trong trạng thái đang khám (IN_PROGRESS).");
                }
            }
        }
    }

    private async Task<VisitChargeGateResult?> TryCheckExaminationEligibilitySafeAsync(
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

    private async Task<bool> HasPendingDiagnosticResultsAsync(long visitId, CancellationToken ct)
    {
        var hasOpenDiagnostics = await _db.QueueEntries
            .AsNoTracking()
            .AnyAsync(q =>
                q.VisitId == visitId &&
                q.QueueType != null &&
                q.QueueType.ToUpper() == "DIAGNOSTIC" &&
                q.CompletedAt == null &&
                (q.Status == null ||
                 (q.Status.ToUpper() != "COMPLETED" && q.Status.ToUpper() != "CANCELLED")),
                ct);

        if (hasOpenDiagnostics)
        {
            return true;
        }

        try
        {
            var activeRequiredOrders = await _db.DiagnosticOrders
                .AsNoTracking()
                .Where(o =>
                    o.VisitId == visitId &&
                    o.RequiredForConclusion != false &&
                    (o.Status == null || o.Status.ToUpper() != "CANCELLED"))
                .Select(o => o.Id)
                .ToListAsync(ct);

            if (activeRequiredOrders.Count == 0)
            {
                return false;
            }

            var orderIdSet = activeRequiredOrders.ToHashSet();
            var results = await _db.DiagnosticResults
                .AsNoTracking()
                .Where(r => r.DiagnosticOrderId.HasValue && orderIdSet.Contains(r.DiagnosticOrderId.Value))
                .Select(r => new { OrderId = r.DiagnosticOrderId!.Value, r.Status, r.FinalizedAt })
                .ToListAsync(ct);

            var finalizedOrderIds = results
                .Where(r => ExaminationQueueService.IsDiagnosticResultFinalized(r.Status, r.FinalizedAt))
                .Select(r => r.OrderId)
                .ToHashSet();

            return activeRequiredOrders.Any(id => !finalizedOrderIds.Contains(id));
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    private static string NormalizeAndValidateCancelDisposition(string? rawDisposition)
    {
        if (string.IsNullOrWhiteSpace(rawDisposition))
        {
            return QueueCancelDispositions.PatientAbandoned;
        }

        var normalized = AppointmentBookingRules.NormalizeStatusKey(rawDisposition);
        if (!ValidCancelDispositions.Contains(normalized))
        {
            throw new ValidationException(
                "Phân loại hủy lượt (Disposition) không hợp lệ. Chỉ chấp nhận: PATIENT_ABANDONED, REASSIGN_REQUEUE, TECHNICAL_ERROR.");
        }

        return normalized;
    }

    private async Task<List<long>> TryLoadVisitChargeIdsForLockAsync(long visitId, CancellationToken ct)
    {
        try
        {
            return await _db.VisitCharges
                .AsNoTracking()
                .Where(c => c.VisitId == visitId)
                .OrderBy(c => c.Id)
                .Select(c => c.Id)
                .ToListAsync(ct);
        }
        catch (NotSupportedException)
        {
            return [];
        }
    }

    private async Task<List<long>> TryLoadPaymentIdsForLockAsync(
        long visitId,
        IReadOnlyCollection<long> chargeIds,
        CancellationToken ct)
    {
        try
        {
            return await _db.Payments
                .AsNoTracking()
                .Where(p =>
                    p.VisitId == visitId ||
                    (p.VisitChargeId.HasValue && chargeIds.Contains(p.VisitChargeId.Value)))
                .OrderBy(p => p.Id)
                .Select(p => p.Id)
                .Distinct()
                .ToListAsync(ct);
        }
        catch (NotSupportedException)
        {
            return [];
        }
    }

    private async Task ReconcileCancelledPreConsultationChargesAsync(
        Visit cancelledVisit,
        string disposition,
        long actorUserId,
        long hospitalId,
        string reason,
        string? ipAddress,
        DateTime nowLocal,
        DateTime nowUtc,
        CancellationToken ct)
    {
        List<VisitCharge> visitCharges;
        try
        {
            visitCharges = await _db.VisitCharges
                .Where(c => c.VisitId == cancelledVisit.Id)
                .OrderBy(c => c.Id)
                .ToListAsync(ct);
        }
        catch (NotSupportedException)
        {
            return;
        }

        List<Payment> visitPayments;
        try
        {
            var chargeIds = visitCharges.Select(c => c.Id).ToHashSet();
            visitPayments = await _db.Payments
                .Where(p =>
                    p.VisitId == cancelledVisit.Id ||
                    (p.VisitChargeId.HasValue && chargeIds.Contains(p.VisitChargeId.Value)))
                .OrderBy(p => p.Id)
                .ToListAsync(ct);
        }
        catch (NotSupportedException)
        {
            visitPayments = [];
        }

        foreach (var p in visitPayments)
        {
            if (string.Equals(p.Status?.Trim(), PaymentStatuses.Pending, StringComparison.OrdinalIgnoreCase))
            {
                p.Status = PaymentStatuses.Expired;
            }
        }

        var examCharges = visitCharges
            .Where(c =>
                string.IsNullOrWhiteSpace(c.ChargeType)
                    ? !c.DiagnosticOrderId.HasValue
                    : string.Equals(c.ChargeType.Trim(), VisitChargeTypes.Examination, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var legacyPaid = visitPayments.Any(p =>
            !p.VisitChargeId.HasValue &&
            string.Equals(p.Status?.Trim(), PaymentStatuses.Paid, StringComparison.OrdinalIgnoreCase));

        foreach (var examCharge in examCharges)
        {
            var chargePaidPayments = visitPayments
                .Where(p =>
                    p.VisitChargeId == examCharge.Id &&
                    string.Equals(p.Status?.Trim(), PaymentStatuses.Paid, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var isPaid = chargePaidPayments.Count > 0 || legacyPaid;
            if (isPaid)
            {
                examCharge.Status = VisitChargeStatuses.CancelledRefundPending;
                examCharge.CancelledAt ??= nowLocal;

                _db.AuditLogs.Add(new AuditLog
                {
                    ActorUserId = actorUserId,
                    HospitalId = hospitalId,
                    Action = ExaminationChargeRefundPendingAction,
                    EntityType = "VISIT_CHARGE",
                    EntityId = examCharge.Id.ToString(CultureInfo.InvariantCulture),
                    Reason = reason,
                    ChangeData = JsonSerializer.Serialize(new
                    {
                        visitId = cancelledVisit.Id,
                        visitCode = cancelledVisit.VisitCode,
                        visitChargeId = examCharge.Id,
                        unitPrice = examCharge.UnitPrice,
                        chargeStatus = examCharge.Status,
                        disposition,
                        paidPaymentIds = chargePaidPayments.Select(p => p.Id).ToArray(),
                        reason,
                    }, JsonOptions),
                    IpAddress = QueueEntryRequestParsing.ParseIpAddress(ipAddress),
                    OccurredAt = nowUtc,
                });
            }
            else
            {
                examCharge.Status = VisitChargeStatuses.Cancelled;
                examCharge.CancelledAt ??= nowLocal;
            }
        }
    }

    private async Task<long?> TryResolveRoomServicePointIdAsync(
        long roomId,
        long? doctorId,
        long hospitalId,
        CancellationToken ct)
    {
        try
        {
            var points = await _db.QueueServicePoints
                .AsNoTracking()
                .Where(sp =>
                    sp.RoomId == roomId &&
                    sp.IsActive != false &&
                    (!sp.HospitalId.HasValue || sp.HospitalId.Value == hospitalId))
                .OrderBy(sp => sp.Id)
                .ToListAsync(ct);

            if (points.Count == 0)
            {
                return null;
            }

            if (doctorId.HasValue)
            {
                var exact = points.FirstOrDefault(sp => sp.DoctorId == doctorId.Value);
                if (exact is not null)
                {
                    return exact.Id;
                }
            }

            return points.FirstOrDefault(sp => !sp.DoctorId.HasValue)?.Id
                ?? (doctorId.HasValue || points.Select(p => p.DoctorId).Distinct().Count() <= 1 ? points[0].Id : null);
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private async Task<int> CountActiveRoomVisitsExcludingAsync(
        long roomId,
        long currentVisitId,
        CancellationToken ct)
        => await _db.Visits
            .AsNoTracking()
            .CountAsync(v =>
                v.Id != currentVisitId &&
                v.ExaminationRoomId == roomId &&
                v.CompletedAt == null &&
                v.Status != null &&
                v.Status.ToUpper() != "COMPLETED" &&
                v.Status.ToUpper() != "CANCELLED" &&
                v.Status.ToUpper() != "NO_SHOW" &&
                v.Status.ToUpper() != "DONE",
                ct);

    private async Task<int> AllocateNextQueueNumberForServicePointAsync(
        long? servicePointId,
        DateOnly today,
        CancellationToken ct)
    {
        var max = await _db.QueueEntries
            .AsNoTracking()
            .Where(q =>
                q.QueueDate == today &&
                (q.QueueType == null ||
                 q.QueueType.ToUpper() == "EXAMINATION" ||
                 q.QueueType.ToUpper() == "RETURN_CONSULTATION") &&
                (servicePointId == null
                    ? (q.ServicePointId == null && q.CurrentServicePointId == null)
                    : (q.ServicePointId == servicePointId || q.CurrentServicePointId == servicePointId)))
            .Select(q => q.QueueNumber)
            .MaxAsync(ct);

        return (max ?? 0) + 1;
    }

    private async Task<string?> ResolveOriginTicketRoomCodeAsync(
        QueueEntry queueEntry,
        string? fallbackRoomCode,
        CancellationToken ct)
    {
        if (queueEntry.ServicePointId.HasValue &&
            queueEntry.CurrentServicePointId != queueEntry.ServicePointId)
        {
            var originSp = await TryGetServicePointAsync(queueEntry.ServicePointId.Value, ct);
            if (originSp?.RoomId is long originRoomId)
            {
                var originRoomCode = await _db.HospitalRooms
                    .AsNoTracking()
                    .Where(r => r.Id == originRoomId)
                    .Select(r => r.RoomCode)
                    .FirstOrDefaultAsync(ct);
                if (!string.IsNullOrWhiteSpace(originRoomCode))
                {
                    return originRoomCode;
                }
            }
        }

        return fallbackRoomCode;
    }

    private async Task<OperatorAccessContext> EnsureOperatorAccessAsync(
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

        var hasAllowedRole = actorRoleCodes.Any(code =>
            AllowedOperatorRoles.Any(allowed => string.Equals(code, allowed, StringComparison.OrdinalIgnoreCase)));

        if (!hasAllowedRole)
        {
            throw new ForbiddenException("Người dùng không có quyền cập nhật trạng thái hàng đợi khám.");
        }

        var isSystemAdmin = actorRoleCodes.Any(code =>
            string.Equals(code, RoleNames.SystemAdmin, StringComparison.OrdinalIgnoreCase));

        var hasReceptionOrAdminRole = isSystemAdmin || actorRoleCodes.Any(code =>
            string.Equals(code, RoleNames.Reception, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(code, RoleNames.HospitalAdmin, StringComparison.OrdinalIgnoreCase));

        var hasDoctorRole = actorRoleCodes.Any(code =>
            string.Equals(code, RoleNames.Doctor, StringComparison.OrdinalIgnoreCase));

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
            throw new ForbiddenException("Vai trò hiện tại chưa được cấp quyền quản lý hàng đợi (QUEUE_MANAGE).");
        }

        var permissionCodes = await _db.Permissions
            .AsNoTracking()
            .Where(p => rolePermissionIds.Contains(p.Id))
            .Select(p => p.Code)
            .ToListAsync(ct);

        var hasPermission = permissionCodes.Any(code =>
            AllowedPermissionCodes.Any(allowed =>
                string.Equals(code, allowed, StringComparison.OrdinalIgnoreCase)));

        if (!hasPermission)
        {
            throw new ForbiddenException("Vai trò hiện tại chưa được cấp quyền quản lý hàng đợi (QUEUE_MANAGE).");
        }

        IReadOnlyList<string> resolvedPermissionCodes = permissionCodes;

        var actorStaff = await _db.Staff
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == actorUserId, ct);

        if (actorStaff is not null &&
            !string.Equals(actorStaff.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            throw new ForbiddenException("Hồ sơ nhân sự không ở trạng thái hoạt động.");
        }

        if (!isSystemAdmin)
        {
            if (actorStaff is null || !actorStaff.HospitalId.HasValue)
            {
                throw new ForbiddenException("Tài khoản nhân sự chưa được gán cơ sở y tế.");
            }

            var hospital = await _db.Hospitals
                .AsNoTracking()
                .FirstOrDefaultAsync(h => h.Id == actorStaff.HospitalId.Value, ct);

            if (hospital is not null && hospital.IsActive == false)
            {
                throw new ForbiddenException("Cơ sở y tế của nhân sự đang ngừng hoạt động.");
            }
        }

        Doctor? actorDoctor = null;
        if (hasDoctorRole && actorStaff is not null)
        {
            actorDoctor = await _db.Doctors
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.StaffId == actorStaff.Id, ct);
        }

        return new OperatorAccessContext(
            IsSystemAdmin: isSystemAdmin,
            IsDoctorOnly: hasDoctorRole && !hasReceptionOrAdminRole,
            ActorHospitalId: actorStaff?.HospitalId,
            ActorStaff: actorStaff,
            ActorDoctor: actorDoctor,
            ActorUser: actorUser,
            RoleCodes: actorRoleCodes,
            PermissionCodes: resolvedPermissionCodes);
    }

    private static void EnsureQueueManagerAccess(OperatorAccessContext operatorAccess, string forbiddenRoleMessage)
    {
        var hasManagerRole = operatorAccess.RoleCodes.Any(code =>
            QueueManagerRoles.Any(allowed => string.Equals(code, allowed, StringComparison.OrdinalIgnoreCase)));

        if (!hasManagerRole)
        {
            throw new ForbiddenException(forbiddenRoleMessage);
        }

        var hasRequiredPermission = operatorAccess.IsSystemAdmin
            ? operatorAccess.PermissionCodes.Any(code =>
                string.Equals(code, PermissionCodes.QueueManage, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(code, PermissionCodes.HospitalConfig, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(code, PermissionCodes.UserManage, StringComparison.OrdinalIgnoreCase))
            : operatorAccess.PermissionCodes.Any(code =>
                string.Equals(code, PermissionCodes.QueueManage, StringComparison.OrdinalIgnoreCase));

        if (!hasRequiredPermission)
        {
            throw new ForbiddenException("Vai trò hiện tại chưa được cấp quyền quản lý hàng đợi (QUEUE_MANAGE).");
        }
    }

    private async Task<QueueEntryContext> ResolveQueueEntryContextAsync(
        long? numericId,
        string? code,
        string rawIdOrCode,
        OperatorAccessContext operatorAccess,
        bool trackEntities,
        CancellationToken ct)
    {
        var queueQuery = trackEntities ? _db.QueueEntries.AsQueryable() : _db.QueueEntries.AsNoTracking();
        var visitQuery = trackEntities ? _db.Visits.AsQueryable() : _db.Visits.AsNoTracking();

        QueueEntry? queueEntry = null;
        Visit? visit = null;

        if (numericId.HasValue)
        {
            queueEntry = await queueQuery.FirstOrDefaultAsync(q => q.Id == numericId.Value, ct);
            if (queueEntry is not null)
            {
                if (queueEntry.VisitId.HasValue)
                {
                    visit = await visitQuery.FirstOrDefaultAsync(v => v.Id == queueEntry.VisitId.Value, ct);
                }
                else if (queueEntry.AppointmentId.HasValue)
                {
                    visit = await visitQuery
                        .OrderByDescending(v => v.Id)
                        .FirstOrDefaultAsync(v => v.AppointmentId == queueEntry.AppointmentId.Value, ct);
                }
            }
            else
            {
                visit = await visitQuery.FirstOrDefaultAsync(v => v.Id == numericId.Value, ct);
                if (visit is null)
                {
                    throw new NotFoundException("QueueEntry", numericId.Value);
                }

                queueEntry = await FindQueueEntryForVisitAsync(visit, queueQuery, ct);
                if (queueEntry is null)
                {
                    throw new ConflictException("Lượt khám chưa có bản ghi hàng đợi. Vui lòng gán phòng khám trước.");
                }
            }
        }
        else
        {
            visit = await visitQuery.FirstOrDefaultAsync(
                v => v.VisitCode != null && v.VisitCode.ToUpper() == code,
                ct);

            if (visit is null)
            {
                throw new NotFoundException("Visit", rawIdOrCode.Trim());
            }

            queueEntry = await FindQueueEntryForVisitAsync(visit, queueQuery, ct);
            if (queueEntry is null)
            {
                throw new ConflictException("Lượt khám chưa có bản ghi hàng đợi. Vui lòng gán phòng khám trước.");
            }
        }

        var servicePointId = queueEntry.CurrentServicePointId ?? queueEntry.ServicePointId;
        var servicePoint = servicePointId.HasValue
            ? await TryGetServicePointAsync(servicePointId.Value, ct)
            : null;

        var roomId = visit?.ExaminationRoomId ?? servicePoint?.RoomId;
        var room = roomId.HasValue
            ? await _db.HospitalRooms
                .AsNoTracking()
                .Include(r => r.Department)
                .FirstOrDefaultAsync(r => r.Id == roomId.Value, ct)
            : null;

        var appointmentId = visit?.AppointmentId ?? queueEntry.AppointmentId;
        var appointment = appointmentId.HasValue
            ? await _db.Appointments
                .AsNoTracking()
                .Include(a => a.Slot)
                    .ThenInclude(s => s!.Service)
                .Include(a => a.Slot)
                    .ThenInclude(s => s!.Room)
                .Include(a => a.Slot)
                    .ThenInclude(s => s!.Doctor)
                .FirstOrDefaultAsync(a => a.Id == appointmentId.Value, ct)
            : null;

        var resolvedDoctorId = visit?.AssignedDoctorId ?? servicePoint?.DoctorId ?? appointment?.Slot?.DoctorId;
        var assignedDoctor = resolvedDoctorId.HasValue
            ? await _db.Doctors
                .AsNoTracking()
                .Include(d => d.Staff)
                    .ThenInclude(s => s!.Department)
                .FirstOrDefaultAsync(d => d.Id == resolvedDoctorId.Value, ct)
            : null;

        var checkedInStaff = visit?.CheckedInByStaffId.HasValue == true
            ? await _db.Staff
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == visit.CheckedInByStaffId.Value, ct)
            : null;

        var entryHospitalId = servicePoint?.HospitalId
            ?? room?.HospitalId
            ?? room?.Department?.HospitalId
            ?? assignedDoctor?.HospitalId
            ?? assignedDoctor?.Staff?.HospitalId
            ?? appointment?.Slot?.Service?.HospitalId
            ?? appointment?.Slot?.Room?.HospitalId
            ?? checkedInStaff?.HospitalId
            ?? operatorAccess.ActorHospitalId
            ?? throw new ValidationException("Không xác định được cơ sở y tế của lượt xếp hàng.");

        if (!operatorAccess.IsSystemAdmin && entryHospitalId != operatorAccess.ActorHospitalId!.Value)
        {
            throw new ForbiddenException("Bạn không có quyền thao tác trên hàng đợi thuộc cơ sở y tế khác.");
        }

        if (operatorAccess.IsDoctorOnly)
        {
            var entryDoctorId = visit?.AssignedDoctorId ?? servicePoint?.DoctorId ?? appointment?.Slot?.DoctorId;
            if (entryDoctorId.HasValue &&
                (operatorAccess.ActorDoctor is null ||
                 (entryDoctorId.Value != operatorAccess.ActorDoctor.Id &&
                  servicePoint?.DoctorId != operatorAccess.ActorDoctor.Id)))
            {
                throw new ForbiddenException("Bác sĩ chỉ được phép cập nhật trạng thái hàng đợi thuộc buồng khám hoặc lượt khám được phân công.");
            }
        }

        var patientId = visit?.PatientId ?? appointment?.PatientId;
        var patient = patientId.HasValue
            ? await _db.Patients
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == patientId.Value, ct)
            : null;

        var departmentId = room?.DepartmentId
            ?? assignedDoctor?.Staff?.DepartmentId
            ?? appointment?.Slot?.Service?.DepartmentId;

        var department = room?.Department
            ?? assignedDoctor?.Staff?.Department
            ?? (departmentId.HasValue
                ? await _db.Departments
                    .AsNoTracking()
                    .FirstOrDefaultAsync(d => d.Id == departmentId.Value, ct)
                : null);

        return new QueueEntryContext(
            HospitalId: entryHospitalId,
            QueueEntry: queueEntry,
            Visit: visit,
            ServicePoint: servicePoint,
            Room: room,
            Department: department,
            AssignedDoctor: assignedDoctor,
            Patient: patient);
    }

    private static async Task<QueueEntry?> FindQueueEntryForVisitAsync(
        Visit visit,
        IQueryable<QueueEntry> queueQuery,
        CancellationToken ct)
    {
        var entries = await queueQuery
            .Where(q => q.VisitId == visit.Id ||
                        (visit.AppointmentId.HasValue && q.AppointmentId == visit.AppointmentId.Value))
            .OrderByDescending(q => q.Id)
            .ToListAsync(ct);

        return entries.FirstOrDefault(q =>
                   q.VisitId == visit.Id &&
                   ClinicalQueueRules.IsClinicalExaminationQueueType(q.QueueType))
               ?? entries.FirstOrDefault(q =>
                   ClinicalQueueRules.IsClinicalExaminationQueueType(q.QueueType))
               ?? entries.FirstOrDefault();
    }

    private async Task<QueueServicePoint?> TryGetServicePointAsync(long servicePointId, CancellationToken ct)
    {
        try
        {
            return await _db.QueueServicePoints
                .AsNoTracking()
                .FirstOrDefaultAsync(sp => sp.Id == servicePointId, ct);
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private async Task<IReadOnlyList<QueueStatusTransitionHistoryItemDto>> LoadStatusHistoryAsync(
        long queueEntryId,
        CancellationToken ct)
    {
        var queueEntryIdStr = queueEntryId.ToString(CultureInfo.InvariantCulture);
        var auditLogs = await _db.AuditLogs
            .AsNoTracking()
            .Where(a => (a.Action == QueueStatusUpdateAction || a.Action == QueueEntryCancelAction) && a.EntityId == queueEntryIdStr)
            .OrderByDescending(a => a.OccurredAt)
            .ThenByDescending(a => a.Id)
            .ToListAsync(ct);

        var result = new List<QueueStatusTransitionHistoryItemDto>(auditLogs.Count);
        foreach (var log in auditLogs)
        {
            QueueStatusTransitionAuditPayload? payload = null;
            if (!string.IsNullOrWhiteSpace(log.ChangeData))
            {
                try
                {
                    payload = JsonSerializer.Deserialize<QueueStatusTransitionAuditPayload>(log.ChangeData, JsonOptions);
                }
                catch (JsonException)
                {
                    payload = null;
                }
            }

            result.Add(new QueueStatusTransitionHistoryItemDto(
                AuditLogId: log.Id,
                QueueEntryId: payload?.QueueEntryId ?? queueEntryId,
                VisitId: payload?.VisitId,
                VisitCode: payload?.VisitCode,
                PreviousStatus: payload?.PreviousStatus ?? string.Empty,
                TargetStatus: payload?.TargetStatus ?? string.Empty,
                PreviousPresenceStatus: payload?.PreviousPresenceStatus,
                UpdatedPresenceStatus: payload?.UpdatedPresenceStatus,
                PreviousPriority: payload?.PreviousPriority,
                UpdatedPriority: payload?.UpdatedPriority,
                PreviousStateVersion: payload?.PreviousStateVersion,
                UpdatedStateVersion: payload?.UpdatedStateVersion ?? 1,
                Reason: payload?.Reason ?? log.Reason,
                BlockedReason: payload?.BlockedReason,
                Notes: payload?.Notes,
                UpdatedByUserId: payload?.UpdatedByUserId ?? log.ActorUserId,
                UpdatedByStaffCode: payload?.UpdatedByStaffCode,
                UpdatedByName: payload?.UpdatedByName,
                UpdatedAtUtc: payload?.UpdatedAtUtc ?? log.OccurredAt));
        }

        return result;
    }

    private static string FormatTicketNo(int queueNumber, string? roomCode)
    {
        var prefix = "A";
        if (!string.IsNullOrWhiteSpace(roomCode))
        {
            var firstLetter = roomCode.Trim().FirstOrDefault(char.IsAsciiLetter);
            if (firstLetter != default)
            {
                prefix = char.ToUpperInvariant(firstLetter).ToString();
            }
        }

        return $"{prefix}{queueNumber:D3}";
    }

    private sealed record OperatorAccessContext(
        bool IsSystemAdmin,
        bool IsDoctorOnly,
        long? ActorHospitalId,
        Staff? ActorStaff,
        Doctor? ActorDoctor,
        User ActorUser,
        IReadOnlyList<string> RoleCodes,
        IReadOnlyList<string> PermissionCodes);

    private sealed record QueueEntryContext(
        long HospitalId,
        QueueEntry QueueEntry,
        Visit? Visit,
        QueueServicePoint? ServicePoint,
        HospitalRoom? Room,
        Department? Department,
        Doctor? AssignedDoctor,
        Patient? Patient);

    private sealed record QueueStatusTransitionAuditPayload(
        long QueueEntryId,
        long? VisitId,
        string? VisitCode,
        string PreviousStatus,
        string TargetStatus,
        string? PreviousVisitStatus,
        string? UpdatedVisitStatus,
        string? PreviousPresenceStatus,
        string? UpdatedPresenceStatus,
        string? PreviousPriority,
        string? UpdatedPriority,
        int? PreviousStateVersion,
        int UpdatedStateVersion,
        string? Reason,
        string? BlockedReason,
        string? Notes,
        long? UpdatedByUserId,
        string? UpdatedByStaffCode,
        string? UpdatedByName,
        DateTime UpdatedAtUtc,
        string? Disposition = null);

    private sealed record QueuePriorityChangeAuditPayload(
        long QueueEntryId,
        long? VisitId,
        string? VisitCode,
        string PreviousPriority,
        string UpdatedPriority,
        bool VerificationRequired,
        bool VerificationConfirmed,
        int PreviousStateVersion,
        int UpdatedStateVersion,
        string Reason,
        long UpdatedByUserId,
        string? UpdatedByStaffCode,
        string UpdatedByName,
        DateTime UpdatedAtUtc);
}
