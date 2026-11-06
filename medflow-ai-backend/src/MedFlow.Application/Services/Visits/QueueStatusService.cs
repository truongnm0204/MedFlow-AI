using System.Globalization;
using System.Net;
using System.Text.Json;
using MedFlow.Application.DTOs.Visits;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Appointments;
using MedFlow.Application.Services.Policies;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Visits;

public sealed class QueueStatusService : IQueueStatusService
{
    public const string QueueStatusUpdateAction = "QUEUE_STATUS_UPDATED";
    public const string QueuePriorityUpdateAction = "QUEUE_PRIORITY_UPDATED";
    public const string QueueEntryEntityType = "QUEUE_ENTRY";

    private static readonly string[] PriorityManagerRoles =
    [
        "RECEPTION",
        "HOSPITAL_ADMIN",
        "SYSTEM_ADMIN",
    ];

    private static readonly string[] AllowedOperatorRoles =
    [
        "RECEPTION",
        "DOCTOR",
        "DIAGNOSTIC_STAFF",
        "HOSPITAL_ADMIN",
        "SYSTEM_ADMIN",
    ];

    private static readonly string[] AllowedPermissionCodes =
    [
        "QUEUE_MANAGE",
        "VISIT_ASSIGN",
        "PATIENT_CHECKIN",
        "MEDICAL_RECORD_WRITE",
        "DIAGNOSTIC_RESULT_WRITE",
        "HOSPITAL_CONFIG",
    ];

    private static readonly HashSet<string> ValidTargetStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "WAITING",
        "READY",
        "CALLED",
        "IN_PROGRESS",
        "COMPLETED",
        "SKIPPED",
        "CANCELLED",
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
            ["RESERVED"] = ["WAITING", "READY", "CANCELLED"],
            ["WAITING"] = ["READY", "CALLED", "SKIPPED", "CANCELLED"],
            ["READY"] = ["WAITING", "CALLED", "SKIPPED", "CANCELLED"],
            ["CALLED"] = ["IN_PROGRESS", "WAITING", "READY", "SKIPPED", "CANCELLED"],
            ["IN_PROGRESS"] = ["COMPLETED", "WAITING", "SKIPPED"],
            ["SKIPPED"] = ["WAITING", "READY", "CALLED", "CANCELLED"],
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

    public QueueStatusService(
        IApplicationDbContext db,
        TimeProvider? timeProvider = null)
    {
        _db = db;
        _timeProvider = timeProvider ?? TimeProvider.System;
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

        var (numericId, code) = ParseIdentifier(idOrCode);
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
        var ticketNo = FormatTicketNo(queueNumber, context.Room?.RoomCode);

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

        var (numericId, code) = ParseIdentifier(idOrCode);
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

        if (targetStatus is "SKIPPED" or "CANCELLED" && string.IsNullOrWhiteSpace(effectiveBlockedReason))
        {
            throw new ValidationException("Vui lòng nhập lý do khi chuyển trạng thái sang Qua lượt (SKIPPED) hoặc Hủy (CANCELLED).");
        }

        if (targetStatus == "IN_PROGRESS" && requestedPresence is "NOT_ARRIVED" or "LEFT")
        {
            throw new ValidationException("Không thể bắt đầu khám khi trạng thái hiện diện của bệnh nhân là vắng mặt hoặc chưa đến.");
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var nowLocal = AppointmentBookingRules.GetVietnamNow(_timeProvider);
        var operatorAccess = await EnsureOperatorAccessAsync(actorUserId, nowUtc, ct);

        var context = await ResolveQueueEntryContextAsync(
            numericId,
            code,
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
        }

        if (targetStatus == "IN_PROGRESS")
        {
            await EnsureNoOtherActiveInProgressEntryAsync(queueEntry, visit, ct);
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
            IpAddress = ParseIpAddress(ipAddress),
            OccurredAt = nowUtc,
        };

        _db.AuditLogs.Add(auditLog);
        await _db.SaveChangesAsync(ct);

        var nextAllowedTargets = GetAllowedTargetStatuses(targetStatus, visit);
        var statusHistory = await LoadStatusHistoryAsync(queueEntry.Id, ct);
        var queueNumber = queueEntry.QueueNumber is > 0 ? queueEntry.QueueNumber.Value : 1;
        var ticketNo = FormatTicketNo(queueNumber, context.Room?.RoomCode);

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
            PreviousStatus: currentStatus,
            CurrentStatus: targetStatus,
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
            Reason: effectiveReason,
            Notes: trimmedNotes,
            UpdatedByUserId: actorUserId,
            UpdatedByStaffCode: operatorAccess.ActorStaff?.StaffCode,
            UpdatedByName: updatedByName,
            UpdatedAtUtc: nowUtc,
            AllowedTargetStatuses: nextAllowedTargets,
            StatusHistory: statusHistory);
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

        var (numericId, code) = ParseIdentifier(idOrCode);

        var target = ClinicalQueueRules.NormalizePriorityOrThrow(request.Priority)
            ?? throw new ValidationException("Vui lòng chọn mức ưu tiên (NORMAL, ELDERLY, PREGNANT).");

        var reason = TrimAndValidateLength(request.Reason, "Lý do áp dụng mức ưu tiên")
            ?? throw new ValidationException("Vui lòng nhập lý do khi áp dụng hoặc thay đổi mức ưu tiên.");

        if (!request.ExpectedStateVersion.HasValue || request.ExpectedStateVersion.Value < 1)
        {
            throw new ValidationException("Phiên bản trạng thái (ExpectedStateVersion) là bắt buộc và phải lớn hơn hoặc bằng 1.");
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var nowLocal = AppointmentBookingRules.GetVietnamNow(_timeProvider);
        var operatorAccess = await EnsureOperatorAccessAsync(actorUserId, nowUtc, ct);
        EnsurePriorityManagerAccess(operatorAccess);

        var context = await ResolveQueueEntryContextAsync(
            numericId,
            code,
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
            IpAddress = ParseIpAddress(ipAddress),
            OccurredAt = nowUtc,
        };

        _db.AuditLogs.Add(auditLog);
        await _db.SaveChangesAsync(ct);

        var queueNumber = queueEntry.QueueNumber is > 0 ? queueEntry.QueueNumber.Value : 1;
        var ticketNo = FormatTicketNo(queueNumber, context.Room?.RoomCode);
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

    private static (long? Id, string? Code) ParseIdentifier(string? idOrCode)
    {
        if (string.IsNullOrWhiteSpace(idOrCode))
        {
            throw new ValidationException("Mã định danh lượt xếp hàng không hợp lệ.");
        }

        var trimmed = idOrCode.Trim();
        if (trimmed.StartsWith('#'))
        {
            trimmed = trimmed[1..].TrimStart();
        }

        if (trimmed.Length >= 3 &&
            (trimmed.StartsWith("q-", StringComparison.OrdinalIgnoreCase) ||
             trimmed.StartsWith("q_", StringComparison.OrdinalIgnoreCase)))
        {
            var suffix = trimmed[2..];
            if (suffix.All(char.IsAsciiDigit) ||
                (suffix.StartsWith('-') && suffix.Length > 1 && suffix[1..].All(char.IsAsciiDigit)))
            {
                trimmed = suffix;
            }
        }

        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > 40)
        {
            throw new ValidationException("Mã định danh lượt xếp hàng không hợp lệ.");
        }

        if (trimmed.All(char.IsAsciiDigit) ||
            (trimmed.StartsWith('-') && trimmed.Length > 1 && trimmed[1..].All(char.IsAsciiDigit)))
        {
            if (!long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numericId) ||
                numericId <= 0)
            {
                throw new ValidationException("ID lượt xếp hàng phải lớn hơn 0.");
            }

            return (numericId, null);
        }

        if (trimmed.StartsWith('-') ||
            !trimmed.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '-' || ch == '_'))
        {
            throw new ValidationException("Mã định danh lượt xếp hàng không hợp lệ.");
        }

        return (null, trimmed.ToUpperInvariant());
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
        if (!ValidTargetStatuses.Contains(resolved))
        {
            throw new ValidationException("Trạng thái hàng đợi không hợp lệ. Chỉ chấp nhận: WAITING, READY, CALLED, IN_PROGRESS, COMPLETED, SKIPPED, CANCELLED.");
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

                if (visit is not null)
                {
                    visit.Status = "WAITING";
                }

                break;

            case "READY":
                queueEntry.ArrivedAt ??= visit?.CheckedInAt ?? nowLocal;
                queueEntry.ReadyAt = nowLocal;
                queueEntry.PresenceStatus = requestedPresence ?? "PRESENT";
                queueEntry.CalledAt = null;
                queueEntry.StartedAt = null;
                queueEntry.BlockedReason = null;
                if (visit is not null)
                {
                    visit.Status = "WAITING";
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
                if (visit is not null)
                {
                    visit.Status = "CALLED";
                }

                break;

            case "IN_PROGRESS":
                queueEntry.ArrivedAt ??= visit?.CheckedInAt ?? nowLocal;
                queueEntry.ReadyAt ??= nowLocal;
                queueEntry.CalledAt ??= nowLocal;
                queueEntry.StartedAt = nowLocal;
                queueEntry.PresenceStatus = "PRESENT";
                queueEntry.BlockedReason = null;
                if (visit is not null)
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
                if (visit is not null)
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
                if (visit is not null)
                {
                    visit.Status = "WAITING";
                }

                break;

            case "CANCELLED":
                queueEntry.BlockedReason = effectiveBlockedReason;
                queueEntry.CompletedAt = nowLocal;
                queueEntry.PresenceStatus = requestedPresence ?? "LEFT";
                if (visit is not null)
                {
                    visit.Status = "CANCELLED";
                    visit.CompletedAt = nowLocal;
                }

                break;
        }
    }

    private async Task EnsureNoOtherActiveInProgressEntryAsync(
        QueueEntry currentEntry,
        Visit? currentVisit,
        CancellationToken ct)
    {
        var effectiveServicePointId = currentEntry.CurrentServicePointId ?? currentEntry.ServicePointId;
        var effectiveRoomId = currentVisit?.ExaminationRoomId;

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
            var candidateVisitIds = candidateEntries
                .Where(q => q.VisitId.HasValue)
                .Select(q => q.VisitId!.Value)
                .Distinct()
                .ToList();

            if (candidateVisitIds.Count > 0)
            {
                var hasSameRoomActiveVisit = await _db.Visits
                    .AsNoTracking()
                    .AnyAsync(v =>
                        candidateVisitIds.Contains(v.Id) &&
                        v.ExaminationRoomId == effectiveRoomId.Value &&
                        v.CompletedAt == null,
                        ct);

                if (hasSameRoomActiveVisit)
                {
                    throw new ConflictException("Điểm dịch vụ/phòng khám hiện đang có bệnh nhân khác trong trạng thái đang khám (IN_PROGRESS).");
                }
            }
        }
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
            string.Equals(code, "SYSTEM_ADMIN", StringComparison.OrdinalIgnoreCase));

        var hasReceptionOrAdminRole = isSystemAdmin || actorRoleCodes.Any(code =>
            string.Equals(code, "RECEPTION", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(code, "HOSPITAL_ADMIN", StringComparison.OrdinalIgnoreCase));

        var hasDoctorRole = actorRoleCodes.Any(code =>
            string.Equals(code, "DOCTOR", StringComparison.OrdinalIgnoreCase));

        IReadOnlyList<string> resolvedPermissionCodes = [];
        if (!isSystemAdmin && actorRoleIds.Count > 0)
        {
            var rolePermissionIds = await _db.RolePermissions
                .AsNoTracking()
                .Where(rp => actorRoleIds.Contains(rp.RoleId))
                .Select(rp => rp.PermissionId)
                .Distinct()
                .ToListAsync(ct);

            if (rolePermissionIds.Count > 0)
            {
                var permissionCodes = await _db.Permissions
                    .AsNoTracking()
                    .Where(p => rolePermissionIds.Contains(p.Id))
                    .Select(p => p.Code)
                    .ToListAsync(ct);

                resolvedPermissionCodes = permissionCodes;
                if (permissionCodes.Count > 0)
                {
                    var hasPermission = permissionCodes.Any(code =>
                        AllowedPermissionCodes.Any(allowed =>
                            string.Equals(code, allowed, StringComparison.OrdinalIgnoreCase)));

                    if (!hasPermission)
                    {
                        throw new ForbiddenException("Vai trò hiện tại chưa được cấp quyền quản lý hàng đợi (QUEUE_MANAGE).");
                    }
                }
            }
        }

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

    private static void EnsurePriorityManagerAccess(OperatorAccessContext operatorAccess)
    {
        var hasManagerRole = operatorAccess.RoleCodes.Any(code =>
            PriorityManagerRoles.Any(allowed => string.Equals(code, allowed, StringComparison.OrdinalIgnoreCase)));

        if (!hasManagerRole)
        {
            throw new ForbiddenException("Chỉ nhân viên tiếp đón hoặc quản trị được phép áp dụng mức ưu tiên.");
        }

        if (!operatorAccess.IsSystemAdmin &&
            !operatorAccess.PermissionCodes.Any(code =>
                string.Equals(code, "QUEUE_MANAGE", StringComparison.OrdinalIgnoreCase)))
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
            ?? assignedDoctor?.HospitalId
            ?? assignedDoctor?.Staff?.HospitalId
            ?? appointment?.Slot?.Service?.HospitalId
            ?? appointment?.Slot?.Room?.HospitalId
            ?? checkedInStaff?.HospitalId
            ?? 1L;

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
            .Where(a => a.Action == QueueStatusUpdateAction && a.EntityId == queueEntryIdStr)
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

    private static IPAddress? ParseIpAddress(string? ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            return null;
        }

        return IPAddress.TryParse(ipAddress.Trim(), out var parsed) ? parsed : null;
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
        DateTime UpdatedAtUtc);

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
