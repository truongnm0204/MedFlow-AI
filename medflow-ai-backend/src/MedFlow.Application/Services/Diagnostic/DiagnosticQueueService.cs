using System.Globalization;
using System.Text.Json;
using MedFlow.Domain.Exceptions;
using MedFlow.Application.DTOs.Diagnostic;
using MedFlow.Application.DTOs.Visits;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Appointments;
using MedFlow.Application.Services.Doctors;
using MedFlow.Application.Services.Visits;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.Application.Services.Diagnostic;

public sealed class DiagnosticQueueService : IDiagnosticQueueService
{
    public const string DiagnosticQueueCallNextAction = "DIAGNOSTIC_QUEUE_CALL_NEXT";
    public const string DiagnosticQueueStatusUpdatedAction = "DIAGNOSTIC_QUEUE_STATUS_UPDATED";
    public const string DiagnosticQueueMissedTurnAction = "DIAGNOSTIC_QUEUE_MISSED_TURN";
    public const string DiagnosticQueueTurnRestoredAction = "DIAGNOSTIC_QUEUE_TURN_RESTORED";
    public const string DiagnosticQueueEntryCancelledAction = "DIAGNOSTIC_QUEUE_ENTRY_CANCELLED";
    public const string QueueEntryEntityType = "QUEUE_ENTRY";
    public const string DiagnosticOrderEntityType = "DIAGNOSTIC_ORDER";

    private const string DiagnosticStaffRoleCode = "DIAGNOSTIC_STAFF";
    private const string DefaultDepartmentName = "Khoa Cận lâm sàng & Chẩn đoán hình ảnh";

    private static readonly string[] FallbackDiagnosticPermissions =
    [
        "DIAGNOSTIC_RESULT_WRITE",
        "QUEUE_MANAGE",
        "DIAGNOSTIC_ORDER",
    ];

    private static readonly string[] AllowedReadPermissions =
    [
        "QUEUE_MANAGE",
        "DIAGNOSTIC_RESULT_WRITE",
        "DIAGNOSTIC_ORDER",
    ];

    private static readonly string[] AllowedManagePermissions =
    [
        "QUEUE_MANAGE",
        "DIAGNOSTIC_RESULT_WRITE",
    ];

    private static readonly HashSet<string> AllowedCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "LABORATORY",
        "IMAGING",
        "FUNCTIONAL",
    };

    private static readonly HashSet<string> AllowedQueueStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "RESERVED",
        "WAITING",
        "READY",
        "CALLED",
        "IN_PROGRESS",
        "COMPLETED",
        "SKIPPED",
        "CANCELLED",
    };

    private static readonly HashSet<string> AllowedTargetQueueStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "WAITING",
        "READY",
        "CALLED",
        "IN_PROGRESS",
        "COMPLETED",
        "SKIPPED",
        "CANCELLED",
    };

    private static readonly HashSet<string> AllowedOrderStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "ORDERED",
        "CONFIRMED",
        "IN_PROGRESS",
        "COMPLETED",
        "CANCELLED",
    };

    private static readonly HashSet<string> AllowedPriorities = new(StringComparer.OrdinalIgnoreCase)
    {
        "NORMAL",
        "URGENT",
        "STAT",
        "ELDERLY",
        "PREGNANT",
    };

    private static readonly HashSet<string> AllowedPresenceStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "PRESENT",
        "NOT_ARRIVED",
        "LEFT",
    };

    private static readonly IReadOnlyDictionary<string, string[]> AllowedTransitions =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["RESERVED"] = ["WAITING", "READY", "CANCELLED"],
            ["WAITING"] = ["READY", "CALLED", "IN_PROGRESS", "SKIPPED", "CANCELLED"],
            ["READY"] = ["WAITING", "CALLED", "IN_PROGRESS", "SKIPPED", "CANCELLED"],
            ["CALLED"] = ["IN_PROGRESS", "READY", "WAITING", "SKIPPED", "CANCELLED"],
            ["IN_PROGRESS"] = ["COMPLETED", "WAITING", "READY", "SKIPPED"],
            ["SKIPPED"] = ["READY", "WAITING", "CALLED", "CANCELLED"],
            ["COMPLETED"] = [],
            ["CANCELLED"] = [],
        };

    private static readonly JsonSerializerOptions AuditJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;

    public DiagnosticQueueService(IApplicationDbContext db, TimeProvider timeProvider)
    {
        _db = db;
        _timeProvider = timeProvider;
    }

    public async Task<DiagnosticQueueBoardResponseDto> GetDiagnosticQueueAsync(
        long actorUserId,
        GetDiagnosticQueueRequest? request = null,
        CancellationToken ct = default)
    {
        var filter = ValidateGetQueueRequest(request);
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var nowLocal = AppointmentBookingRules.GetVietnamNow(_timeProvider);
        var todayLocal = DateOnly.FromDateTime(nowLocal);

        var staffAccess = await EnsureDiagnosticStaffAccessAsync(
            actorUserId,
            nowUtc,
            requireManagePermission: false,
            ct);

        var dataset = await LoadHospitalDiagnosticQueueDatasetAsync(
            staffAccess,
            nowLocal,
            todayLocal,
            filter.FilterDate,
            ct);

        var boardDate = filter.FilterDate ?? dataset.EffectiveQueueDate;
        var filteredServicePoints = FilterServicePointBuckets(dataset.ServicePointBuckets, filter);
        var filteredEntries = ApplyEntryFilters(dataset.AllSortedEntries, filter)
            .Take(filter.Limit)
            .ToList();

        var stats = BuildSummaryStats(dataset.AllSortedEntries, dataset.ServicePointBuckets);
        var availableServicePoints = BuildAvailableServicePoints(
            dataset.HospitalDiagnosticServicePoints,
            dataset.RoomsById);
        var quickActions = BuildQuickActions(staffAccess.Profile.Permissions);

        return new DiagnosticQueueBoardResponseDto(
            HospitalId: staffAccess.Hospital.Id,
            HospitalCode: staffAccess.Hospital.HospitalCode,
            HospitalName: staffAccess.Hospital.Name,
            FacilityName: staffAccess.Hospital.Name,
            FacilityCode: staffAccess.Hospital.HospitalCode,
            DepartmentId: staffAccess.Staff.DepartmentId,
            DepartmentName: staffAccess.DepartmentName,
            QueueDate: boardDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ShiftLabel: ResolveShiftLabel(nowLocal),
            GeneratedAtUtc: nowUtc,
            StaffProfile: staffAccess.Profile,
            Stats: stats,
            AvailableServicePoints: availableServicePoints,
            ServicePoints: filteredServicePoints,
            Entries: filteredEntries,
            Items: filteredEntries,
            QuickActions: quickActions);
    }

    public async Task<DiagnosticQueueEntryDetailResponseDto> GetDiagnosticQueueEntryByIdOrCodeAsync(
        long actorUserId,
        string idOrCode,
        CancellationToken ct = default)
    {
        ValidateEntryIdentifier(idOrCode);
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var nowLocal = AppointmentBookingRules.GetVietnamNow(_timeProvider);
        var todayLocal = DateOnly.FromDateTime(nowLocal);

        var staffAccess = await EnsureDiagnosticStaffAccessAsync(
            actorUserId,
            nowUtc,
            requireManagePermission: false,
            ct);

        var dataset = await LoadHospitalDiagnosticQueueDatasetAsync(
            staffAccess,
            nowLocal,
            todayLocal,
            explicitDate: null,
            ct);

        var entryDto = ResolveEntryDtoFromDataset(idOrCode, staffAccess.Hospital.Id, dataset);
        dataset.VisitsById.TryGetValue(entryDto.VisitId, out var visit);

        var clinicalContext = await BuildClinicalSummaryAsync(
            visit,
            entryDto.VisitId,
            entryDto.VisitCode,
            entryDto.VisitStatus,
            ct);

        var availableServicePoints = BuildAvailableServicePoints(
            dataset.HospitalDiagnosticServicePoints,
            dataset.RoomsById);

        var statusHistory = entryDto.QueueEntryId > 0
            ? await LoadStatusHistoryAsync(entryDto.QueueEntryId, ct)
            : [];

        return new DiagnosticQueueEntryDetailResponseDto(
            HospitalId: staffAccess.Hospital.Id,
            HospitalCode: staffAccess.Hospital.HospitalCode,
            HospitalName: staffAccess.Hospital.Name,
            GeneratedAtUtc: nowUtc,
            StaffProfile: staffAccess.Profile,
            Entry: entryDto,
            ClinicalContext: clinicalContext,
            AvailableServicePoints: availableServicePoints,
            StatusHistory: statusHistory);
    }

    public async Task<CallNextDiagnosticPatientResponseDto> CallNextDiagnosticPatientAsync(
        long actorUserId,
        string servicePointId,
        CallNextDiagnosticPatientRequest? request = null,
        string? ipAddress = null,
        CancellationToken ct = default)
    {
        var effectiveSpInput = !string.IsNullOrWhiteSpace(servicePointId)
            ? servicePointId.Trim()
            : request?.ServicePointId?.Trim();

        if (string.IsNullOrWhiteSpace(effectiveSpInput) ||
            effectiveSpInput.Length > 64 ||
            effectiveSpInput.Contains('<') ||
            effectiveSpInput.Contains('>'))
        {
            throw new ValidationException("Mã điểm phục vụ cận lâm sàng (ServicePointId) không hợp lệ.");
        }

        string? normalizedCategory = null;
        if (!string.IsNullOrWhiteSpace(request?.Category) &&
            !string.Equals(request.Category.Trim(), "ALL", StringComparison.OrdinalIgnoreCase))
        {
            normalizedCategory = request.Category.Trim().ToUpperInvariant();
            if (!AllowedCategories.Contains(normalizedCategory))
            {
                throw new ValidationException("Nhóm cận lâm sàng lọc không hợp lệ.");
            }
        }

        if (request?.ExpectedStateVersion is < 1)
        {
            throw new ValidationException("Phiên bản trạng thái (ExpectedStateVersion) phải lớn hơn hoặc bằng 1.");
        }

        var sanitizedNotes = ValidateOptionalNotes(request?.Notes);

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var nowLocal = AppointmentBookingRules.GetVietnamNow(_timeProvider);
        var todayLocal = DateOnly.FromDateTime(nowLocal);

        var staffAccess = await EnsureDiagnosticStaffAccessAsync(
            actorUserId,
            nowUtc,
            requireManagePermission: true,
            ct);

        var dataset = await LoadHospitalDiagnosticQueueDatasetAsync(
            staffAccess,
            nowLocal,
            todayLocal,
            explicitDate: null,
            ct);

        var resolvedServicePoint = ResolveTargetDiagnosticServicePoint(
            effectiveSpInput,
            staffAccess.Hospital.Id,
            dataset);

        if (resolvedServicePoint.IsActive == false)
        {
            throw new ConflictException("Điểm phục vụ cận lâm sàng hiện đang ngừng hoạt động.");
        }

        if (resolvedServicePoint.RoomId.HasValue &&
            dataset.RoomsById.TryGetValue(resolvedServicePoint.RoomId.Value, out var spRoom) &&
            !string.Equals(spRoom.Status?.Trim(), "ACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException("Phòng thực hiện cận lâm sàng hiện đang ngừng hoạt động.");
        }

        var bucket = dataset.ServicePointBuckets
            .FirstOrDefault(b => b.ServicePointId == resolvedServicePoint.Id)
            ?? BuildEmptyServicePointBucket(
                resolvedServicePoint,
                dataset.RoomsById,
                dataset.DepartmentsById,
                dataset.ServicesById,
                dataset.SpecialtiesById);

        var busyEntry = bucket.Entries.FirstOrDefault(e => e.Status is "CALLED" or "IN_PROGRESS");
        if (busyEntry is not null)
        {
            throw new ConflictException(
                "Điểm phục vụ cận lâm sàng đang có bệnh nhân được gọi hoặc đang thực hiện kỹ thuật. Hoàn tất hoặc đánh dấu qua lượt trước khi gọi bệnh nhân tiếp theo.");
        }

        var candidateDto = bucket.Entries
            .Where(e => e.IsEligible && e.Status is "READY" or "WAITING")
            .Where(e => normalizedCategory is null || string.Equals(e.Category, normalizedCategory, StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();

        if (candidateDto is null)
        {
            return new CallNextDiagnosticPatientResponseDto(
                ServicePointId: resolvedServicePoint.Id,
                FormattedServicePointId: bucket.FormattedServicePointId,
                ServicePointName: bucket.ServicePointName,
                Outcome: "NO_ELIGIBLE_PATIENT",
                Message: $"Không có bệnh nhân đủ điều kiện chờ gọi tại {bucket.ServicePointName}.",
                SelectionReason: null,
                CalledEntry: null,
                PreviousStateVersion: null,
                StateVersion: null,
                UpdatedAtUtc: nowUtc,
                CalledByStaff: staffAccess.Profile,
                ServicePointBucket: bucket,
                Entries: bucket.Entries);
        }

        var selectionReason = candidateDto.Priority is "STAT" or "URGENT"
            ? $"PRIORITY_{candidateDto.Priority}"
            : (!string.Equals(candidateDto.ClinicalPriority, "NORMAL", StringComparison.OrdinalIgnoreCase)
                ? $"CLINICAL_{candidateDto.ClinicalPriority}"
                : "FIFO_READY");

        var transitionResult = await ExecuteDiagnosticQueueMutationAsync(
            actorUserId: actorUserId,
            staffAccess: staffAccess,
            targetCandidate: candidateDto,
            targetStatus: "CALLED",
            requestedPresence: "PRESENT",
            overrideServicePointId: resolvedServicePoint.Id,
            overrideRoomId: resolvedServicePoint.RoomId,
            reason: sanitizedNotes ?? $"Gọi bệnh nhân tiếp theo ({candidateDto.TicketNo}) vào {bucket.ServicePointName}.",
            blockedReason: null,
            notes: sanitizedNotes,
            cancelLinkedOrder: false,
            expectedStateVersion: request?.ExpectedStateVersion,
            auditAction: DiagnosticQueueCallNextAction,
            actionOutcomeCode: "CALLED",
            ipAddress: ipAddress,
            nowUtc: nowUtc,
            nowLocal: nowLocal,
            todayLocal: todayLocal,
            ct: ct);

        var updatedBucket = transitionResult.ServicePointBucket ?? bucket;

        return new CallNextDiagnosticPatientResponseDto(
            ServicePointId: resolvedServicePoint.Id,
            FormattedServicePointId: updatedBucket.FormattedServicePointId,
            ServicePointName: updatedBucket.ServicePointName,
            Outcome: "CALLED",
            Message: $"Đã gọi bệnh nhân {transitionResult.Entry.PatientName} ({transitionResult.Entry.TicketNo}) vào {updatedBucket.ServicePointName}.",
            SelectionReason: selectionReason,
            CalledEntry: transitionResult.Entry,
            PreviousStateVersion: transitionResult.PreviousStateVersion,
            StateVersion: transitionResult.StateVersion,
            UpdatedAtUtc: nowUtc,
            CalledByStaff: staffAccess.Profile,
            ServicePointBucket: updatedBucket,
            Entries: updatedBucket.Entries);
    }

    public async Task<ManageDiagnosticQueueActionResponseDto> UpdateDiagnosticQueueEntryStatusAsync(
        long actorUserId,
        string idOrCode,
        UpdateDiagnosticQueueStatusRequest request,
        string? ipAddress = null,
        CancellationToken ct = default)
    {
        ValidateEntryIdentifier(idOrCode);
        if (request is null)
        {
            throw new ValidationException("Dữ liệu cập nhật trạng thái hàng đợi cận lâm sàng không được để trống.");
        }

        var rawTarget = !string.IsNullOrWhiteSpace(request.TargetStatus)
            ? request.TargetStatus.Trim()
            : request.Status?.Trim();

        if (string.IsNullOrWhiteSpace(rawTarget))
        {
            throw new ValidationException("Trạng thái hàng đợi cận lâm sàng đích (TargetStatus) không được để trống.");
        }

        var targetStatus = NormalizeQueueStatus(rawTarget);
        if (!AllowedTargetQueueStatuses.Contains(targetStatus))
        {
            throw new ValidationException("Trạng thái hàng đợi cận lâm sàng đích không hợp lệ.");
        }

        var requestedPresence = ValidateOptionalPresenceStatus(request.PresenceStatus);
        if (request.ExpectedStateVersion is < 1)
        {
            throw new ValidationException("Phiên bản trạng thái (ExpectedStateVersion) phải lớn hơn hoặc bằng 1.");
        }

        var sanitizedNotes = ValidateOptionalNotes(request.Notes);
        var rawReason = !string.IsNullOrWhiteSpace(request.BlockedReason)
            ? request.BlockedReason.Trim()
            : request.Reason?.Trim();

        string? validatedReason = null;
        string? validatedBlockedReason = null;

        if (targetStatus == "CANCELLED")
        {
            if (string.IsNullOrWhiteSpace(rawReason) ||
                rawReason.Length < 5 ||
                rawReason.Length > 500 ||
                rawReason.Contains('<') ||
                rawReason.Contains('>'))
            {
                throw new ValidationException("Lý do hủy lượt xếp hàng cận lâm sàng phải từ 5 đến 500 ký tự.");
            }

            validatedReason = rawReason;
            validatedBlockedReason = rawReason;
        }
        else if (targetStatus == "SKIPPED")
        {
            if (!string.IsNullOrWhiteSpace(rawReason))
            {
                if (rawReason.Length < 5 ||
                    rawReason.Length > 500 ||
                    rawReason.Contains('<') ||
                    rawReason.Contains('>'))
                {
                    throw new ValidationException("Lý do đánh dấu qua lượt phải từ 5 đến 500 ký tự.");
                }

                validatedReason = rawReason;
                validatedBlockedReason = rawReason;
            }
            else
            {
                validatedBlockedReason = "Bệnh nhân vắng mặt khi gọi tên tại phòng cận lâm sàng.";
                validatedReason = validatedBlockedReason;
            }
        }
        else if (!string.IsNullOrWhiteSpace(rawReason))
        {
            if (rawReason.Length > 500 || rawReason.Contains('<') || rawReason.Contains('>'))
            {
                throw new ValidationException("Lý do cập nhật trạng thái không được vượt quá 500 ký tự.");
            }

            validatedReason = rawReason;
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var nowLocal = AppointmentBookingRules.GetVietnamNow(_timeProvider);
        var todayLocal = DateOnly.FromDateTime(nowLocal);

        var staffAccess = await EnsureDiagnosticStaffAccessAsync(
            actorUserId,
            nowUtc,
            requireManagePermission: true,
            ct);

        var dataset = await LoadHospitalDiagnosticQueueDatasetAsync(
            staffAccess,
            nowLocal,
            todayLocal,
            explicitDate: null,
            ct);

        var targetCandidate = ResolveEntryDtoFromDataset(idOrCode, staffAccess.Hospital.Id, dataset);

        var auditAction = targetStatus switch
        {
            "SKIPPED" => DiagnosticQueueMissedTurnAction,
            "CANCELLED" => DiagnosticQueueEntryCancelledAction,
            _ => DiagnosticQueueStatusUpdatedAction,
        };

        return await ExecuteDiagnosticQueueMutationAsync(
            actorUserId: actorUserId,
            staffAccess: staffAccess,
            targetCandidate: targetCandidate,
            targetStatus: targetStatus,
            requestedPresence: requestedPresence,
            overrideServicePointId: request.ServicePointId,
            overrideRoomId: request.RoomId,
            reason: validatedReason,
            blockedReason: validatedBlockedReason,
            notes: sanitizedNotes,
            cancelLinkedOrder: request.CancelLinkedOrder == true,
            expectedStateVersion: request.ExpectedStateVersion,
            auditAction: auditAction,
            actionOutcomeCode: $"STATUS_{targetStatus}",
            ipAddress: ipAddress,
            nowUtc: nowUtc,
            nowLocal: nowLocal,
            todayLocal: todayLocal,
            ct: ct);
    }

    public async Task<ManageDiagnosticQueueActionResponseDto> MarkDiagnosticMissedTurnAsync(
        long actorUserId,
        string idOrCode,
        DiagnosticQueueTurnActionRequest? request = null,
        string? ipAddress = null,
        CancellationToken ct = default)
    {
        ValidateEntryIdentifier(idOrCode);
        if (request?.ExpectedStateVersion is < 1)
        {
            throw new ValidationException("Phiên bản trạng thái (ExpectedStateVersion) phải lớn hơn hoặc bằng 1.");
        }

        var requestedPresence = ValidateOptionalPresenceStatus(request?.PresenceStatus);
        var sanitizedNotes = ValidateOptionalNotes(request?.Notes);

        var rawReason = !string.IsNullOrWhiteSpace(request?.BlockedReason)
            ? request!.BlockedReason!.Trim()
            : request?.Reason?.Trim();

        if (string.IsNullOrWhiteSpace(rawReason) ||
            rawReason.Length < 5 ||
            rawReason.Length > 500 ||
            rawReason.Contains('<') ||
            rawReason.Contains('>'))
        {
            throw new ValidationException("Lý do đánh dấu bệnh nhân qua lượt cận lâm sàng phải từ 5 đến 500 ký tự.");
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var nowLocal = AppointmentBookingRules.GetVietnamNow(_timeProvider);
        var todayLocal = DateOnly.FromDateTime(nowLocal);

        var staffAccess = await EnsureDiagnosticStaffAccessAsync(
            actorUserId,
            nowUtc,
            requireManagePermission: true,
            ct);

        var dataset = await LoadHospitalDiagnosticQueueDatasetAsync(
            staffAccess,
            nowLocal,
            todayLocal,
            explicitDate: null,
            ct);

        var targetCandidate = ResolveEntryDtoFromDataset(idOrCode, staffAccess.Hospital.Id, dataset);

        if (!string.IsNullOrWhiteSpace(request?.ServicePointId))
        {
            var requestedSp = ResolveTargetDiagnosticServicePoint(
                request.ServicePointId.Trim(),
                staffAccess.Hospital.Id,
                dataset);
            if (targetCandidate.ServicePointId.HasValue && targetCandidate.ServicePointId.Value != requestedSp.Id)
            {
                throw new ConflictException("Lượt xếp hàng cận lâm sàng không thuộc điểm phục vụ đã chọn.");
            }
        }

        if (targetCandidate.Status is not ("CALLED" or "READY" or "WAITING"))
        {
            throw new ConflictException(
                $"Không thể đánh dấu qua lượt khi lượt xếp hàng cận lâm sàng đang ở trạng thái '{targetCandidate.Status}'.");
        }

        return await ExecuteDiagnosticQueueMutationAsync(
            actorUserId: actorUserId,
            staffAccess: staffAccess,
            targetCandidate: targetCandidate,
            targetStatus: "SKIPPED",
            requestedPresence: requestedPresence ?? "LEFT",
            overrideServicePointId: null,
            overrideRoomId: null,
            reason: rawReason,
            blockedReason: rawReason,
            notes: sanitizedNotes,
            cancelLinkedOrder: false,
            expectedStateVersion: request?.ExpectedStateVersion,
            auditAction: DiagnosticQueueMissedTurnAction,
            actionOutcomeCode: "MISSED_TURN",
            ipAddress: ipAddress,
            nowUtc: nowUtc,
            nowLocal: nowLocal,
            todayLocal: todayLocal,
            ct: ct);
    }

    public async Task<ManageDiagnosticQueueActionResponseDto> RestoreDiagnosticSkippedTurnAsync(
        long actorUserId,
        string idOrCode,
        DiagnosticQueueTurnActionRequest? request = null,
        string? ipAddress = null,
        CancellationToken ct = default)
    {
        ValidateEntryIdentifier(idOrCode);
        if (request?.ExpectedStateVersion is < 1)
        {
            throw new ValidationException("Phiên bản trạng thái (ExpectedStateVersion) phải lớn hơn hoặc bằng 1.");
        }

        var requestedPresence = ValidateOptionalPresenceStatus(request?.PresenceStatus);
        var sanitizedNotes = ValidateOptionalNotes(request?.Notes);
        var rawReason = !string.IsNullOrWhiteSpace(request?.Reason)
            ? request!.Reason!.Trim()
            : request?.BlockedReason?.Trim();

        if (!string.IsNullOrWhiteSpace(rawReason) &&
            (rawReason.Length > 500 || rawReason.Contains('<') || rawReason.Contains('>')))
        {
            throw new ValidationException("Lý do khôi phục lượt không được vượt quá 500 ký tự.");
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var nowLocal = AppointmentBookingRules.GetVietnamNow(_timeProvider);
        var todayLocal = DateOnly.FromDateTime(nowLocal);

        var staffAccess = await EnsureDiagnosticStaffAccessAsync(
            actorUserId,
            nowUtc,
            requireManagePermission: true,
            ct);

        var dataset = await LoadHospitalDiagnosticQueueDatasetAsync(
            staffAccess,
            nowLocal,
            todayLocal,
            explicitDate: null,
            ct);

        var targetCandidate = ResolveEntryDtoFromDataset(idOrCode, staffAccess.Hospital.Id, dataset);

        if (!string.IsNullOrWhiteSpace(request?.ServicePointId))
        {
            var requestedSp = ResolveTargetDiagnosticServicePoint(
                request.ServicePointId.Trim(),
                staffAccess.Hospital.Id,
                dataset);
            if (targetCandidate.ServicePointId.HasValue && targetCandidate.ServicePointId.Value != requestedSp.Id)
            {
                throw new ConflictException("Lượt xếp hàng cận lâm sàng không thuộc điểm phục vụ đã chọn.");
            }
        }

        if (targetCandidate.Status != "SKIPPED")
        {
            throw new ConflictException("Chỉ khôi phục lượt cho bệnh nhân đang ở trạng thái Qua lượt (SKIPPED).");
        }

        var effectiveReason = !string.IsNullOrWhiteSpace(rawReason)
            ? rawReason
            : "Bệnh nhân đã quay lại phòng cận lâm sàng và được khôi phục lượt chờ.";

        return await ExecuteDiagnosticQueueMutationAsync(
            actorUserId: actorUserId,
            staffAccess: staffAccess,
            targetCandidate: targetCandidate,
            targetStatus: "READY",
            requestedPresence: requestedPresence ?? "PRESENT",
            overrideServicePointId: null,
            overrideRoomId: null,
            reason: effectiveReason,
            blockedReason: null,
            notes: sanitizedNotes,
            cancelLinkedOrder: false,
            expectedStateVersion: request?.ExpectedStateVersion,
            auditAction: DiagnosticQueueTurnRestoredAction,
            actionOutcomeCode: "RESTORED_TURN",
            ipAddress: ipAddress,
            nowUtc: nowUtc,
            nowLocal: nowLocal,
            todayLocal: todayLocal,
            ct: ct);
    }

    public async Task<ManageDiagnosticQueueActionResponseDto> CancelDiagnosticQueueEntryAsync(
        long actorUserId,
        string idOrCode,
        CancelDiagnosticQueueEntryRequest request,
        string? ipAddress = null,
        CancellationToken ct = default)
    {
        ValidateEntryIdentifier(idOrCode);
        if (request is null)
        {
            throw new ValidationException("Dữ liệu hủy lượt xếp hàng cận lâm sàng không được để trống.");
        }

        if (request.ExpectedStateVersion is < 1)
        {
            throw new ValidationException("Phiên bản trạng thái (ExpectedStateVersion) phải lớn hơn hoặc bằng 1.");
        }

        var rawReason = request.Reason?.Trim();
        if (string.IsNullOrWhiteSpace(rawReason) ||
            rawReason.Length < 5 ||
            rawReason.Length > 500 ||
            rawReason.Contains('<') ||
            rawReason.Contains('>'))
        {
            throw new ValidationException("Lý do hủy lượt xếp hàng cận lâm sàng phải từ 5 đến 500 ký tự.");
        }

        var sanitizedNotes = ValidateOptionalNotes(request.Notes);

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var nowLocal = AppointmentBookingRules.GetVietnamNow(_timeProvider);
        var todayLocal = DateOnly.FromDateTime(nowLocal);

        var staffAccess = await EnsureDiagnosticStaffAccessAsync(
            actorUserId,
            nowUtc,
            requireManagePermission: true,
            ct);

        var dataset = await LoadHospitalDiagnosticQueueDatasetAsync(
            staffAccess,
            nowLocal,
            todayLocal,
            explicitDate: null,
            ct);

        var targetCandidate = ResolveEntryDtoFromDataset(idOrCode, staffAccess.Hospital.Id, dataset);

        if (targetCandidate.Status is "IN_PROGRESS" or "COMPLETED" or "CANCELLED")
        {
            throw new ConflictException(
                $"Không thể hủy lượt xếp hàng cận lâm sàng đang ở trạng thái '{targetCandidate.Status}'.");
        }

        return await ExecuteDiagnosticQueueMutationAsync(
            actorUserId: actorUserId,
            staffAccess: staffAccess,
            targetCandidate: targetCandidate,
            targetStatus: "CANCELLED",
            requestedPresence: "LEFT",
            overrideServicePointId: null,
            overrideRoomId: null,
            reason: rawReason,
            blockedReason: rawReason,
            notes: sanitizedNotes,
            cancelLinkedOrder: request.CancelLinkedOrder,
            expectedStateVersion: request.ExpectedStateVersion,
            auditAction: DiagnosticQueueEntryCancelledAction,
            actionOutcomeCode: "CANCELLED",
            ipAddress: ipAddress,
            nowUtc: nowUtc,
            nowLocal: nowLocal,
            todayLocal: todayLocal,
            ct: ct);
    }

    private async Task<ManageDiagnosticQueueActionResponseDto> ExecuteDiagnosticQueueMutationAsync(
        long actorUserId,
        DiagnosticStaffAccessContext staffAccess,
        DiagnosticQueueEntryItemDto targetCandidate,
        string targetStatus,
        string? requestedPresence,
        long? overrideServicePointId,
        long? overrideRoomId,
        string? reason,
        string? blockedReason,
        string? notes,
        bool cancelLinkedOrder,
        int? expectedStateVersion,
        string auditAction,
        string actionOutcomeCode,
        string? ipAddress,
        DateTime nowUtc,
        DateTime nowLocal,
        DateOnly todayLocal,
        CancellationToken ct)
    {
        var previousStatus = targetCandidate.Status;
        var previousPresence = targetCandidate.PresenceStatus;
        var previousServingOrder = targetCandidate.ServingOrder;
        var previousStateVersion = targetCandidate.StateVersion;
        var previousOrderStatus = targetCandidate.OrderId.HasValue ? targetCandidate.OrderStatus : null;

        if (expectedStateVersion.HasValue && expectedStateVersion.Value != previousStateVersion)
        {
            throw new ConflictException(
                "Dữ liệu hàng đợi cận lâm sàng đã thay đổi bởi thao tác khác (state_version mismatch). Vui lòng tải lại hàng đợi.");
        }

        if (IsTerminalVisitStatus(targetCandidate.VisitStatus))
        {
            throw new ConflictException(
                "Không thể thao tác trên lượt khám đã hoàn tất, đã hủy hoặc vắng mặt (NO_SHOW).");
        }

        if (previousStatus is "COMPLETED" or "CANCELLED")
        {
            throw new ConflictException(
                $"Lượt xếp hàng cận lâm sàng đã ở trạng thái kết thúc ({previousStatus}), không thể chuyển sang {targetStatus}.");
        }

        if (!AllowedTransitions.TryGetValue(previousStatus, out var allowedTargets) ||
            !allowedTargets.Contains(targetStatus, StringComparer.OrdinalIgnoreCase))
        {
            throw new ConflictException(
                $"Không thể chuyển trạng thái hàng đợi cận lâm sàng từ '{previousStatus}' sang '{targetStatus}'.");
        }

        if (targetCandidate.OrderId.HasValue)
        {
            if (targetCandidate.OrderStatus == "CANCELLED" && targetStatus != "CANCELLED")
            {
                throw new ConflictException(
                    "Phiếu chỉ định cận lâm sàng đã bị hủy; không thể tiếp tục điều phối hàng đợi.");
            }

            if (targetCandidate.OrderStatus == "ORDERED" &&
                targetStatus is "CALLED" or "IN_PROGRESS" or "COMPLETED")
            {
                throw new ConflictException(
                    "Phiếu chỉ định cận lâm sàng chưa được xác nhận (ORDERED). Vui lòng xác nhận chỉ định trước khi gọi hoặc thực hiện.");
            }
        }

        var hospitalServicePoints = await TryLoadHospitalDiagnosticServicePointsAsync(staffAccess.Hospital.Id, ct);
        var roomsById = await _db.HospitalRooms
            .AsNoTracking()
            .Where(r => r.HospitalId == staffAccess.Hospital.Id)
            .ToDictionaryAsync(r => r.Id, ct);

        var effectiveSpId = overrideServicePointId ?? targetCandidate.ServicePointId;
        QueueServicePoint? resolvedSp = null;
        if (effectiveSpId.HasValue)
        {
            resolvedSp = hospitalServicePoints.FirstOrDefault(sp => sp.Id == effectiveSpId.Value)
                ?? await _db.QueueServicePoints.AsNoTracking().FirstOrDefaultAsync(sp => sp.Id == effectiveSpId.Value, ct);

            if (resolvedSp is null)
            {
                throw new NotFoundException("DiagnosticServicePoint", effectiveSpId.Value);
            }

            if (resolvedSp.HospitalId.HasValue && resolvedSp.HospitalId.Value != staffAccess.Hospital.Id)
            {
                throw new ForbiddenException("Điểm phục vụ cận lâm sàng không thuộc cơ sở y tế của nhân viên cận lâm sàng.");
            }

            if (!string.Equals(resolvedSp.PointType?.Trim(), "DIAGNOSTIC", StringComparison.OrdinalIgnoreCase))
            {
                throw new ConflictException("Điểm phục vụ này không phải là điểm phục vụ cận lâm sàng (DIAGNOSTIC).");
            }
        }

        var effectiveRoomId = overrideRoomId ?? resolvedSp?.RoomId ?? targetCandidate.RoomId;
        HospitalRoom? resolvedRoom = null;
        if (effectiveRoomId.HasValue)
        {
            if (!roomsById.TryGetValue(effectiveRoomId.Value, out resolvedRoom))
            {
                resolvedRoom = await _db.HospitalRooms.AsNoTracking().FirstOrDefaultAsync(r => r.Id == effectiveRoomId.Value, ct)
                    ?? throw new NotFoundException("HospitalRoom", effectiveRoomId.Value);
            }

            if (resolvedRoom.HospitalId.HasValue && resolvedRoom.HospitalId.Value != staffAccess.Hospital.Id)
            {
                throw new ForbiddenException("Phòng thực hiện cận lâm sàng không thuộc cơ sở y tế của nhân viên cận lâm sàng.");
            }
        }

        if (targetStatus is "READY" or "CALLED" or "IN_PROGRESS")
        {
            if (resolvedSp?.IsActive == false)
            {
                throw new ConflictException("Điểm phục vụ cận lâm sàng hiện đang ngừng hoạt động.");
            }

            if (resolvedRoom is not null &&
                !string.Equals(resolvedRoom.Status?.Trim(), "ACTIVE", StringComparison.OrdinalIgnoreCase))
            {
                throw new ConflictException("Phòng thực hiện cận lâm sàng hiện đang ngừng hoạt động.");
            }
        }

        var entryQueueDate = DateOnly.TryParseExact(
            targetCandidate.QueueDate,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsedQDate)
            ? parsedQDate
            : todayLocal;

        if (targetStatus is "CALLED" or "IN_PROGRESS" && effectiveSpId.HasValue)
        {
            var busyOtherExists = await _db.QueueEntries
                .AsNoTracking()
                .AnyAsync(
                    q => q.Id != targetCandidate.QueueEntryId &&
                         q.QueueDate == entryQueueDate &&
                         (q.CurrentServicePointId == effectiveSpId.Value || q.ServicePointId == effectiveSpId.Value) &&
                         q.VisitId != targetCandidate.VisitId &&
                         q.Status != null &&
                         (q.Status.ToUpper() == "CALLED" || q.Status.ToUpper() == "IN_PROGRESS"),
                    ct);

            if (busyOtherExists)
            {
                throw new ConflictException(
                    "Điểm phục vụ cận lâm sàng đang có bệnh nhân được gọi hoặc đang thực hiện kỹ thuật. Hoàn tất hoặc đánh dấu qua lượt trước khi gọi bệnh nhân tiếp theo.");
            }
        }

        IDbContextTransaction? tx = null;
        try
        {
            tx = await _db.BeginTransactionAsync(ct);
        }
        catch (NotSupportedException)
        {
            tx = null;
        }
        catch (InvalidOperationException)
        {
            tx = null;
        }

        long persistedQueueEntryId;
        int newStateVersion;
        string? updatedOrderStatus = previousOrderStatus;

        try
        {
            DiagnosticOrder? trackedOrder = null;
            if (targetCandidate.OrderId.HasValue)
            {
                trackedOrder = await _db.DiagnosticOrders
                    .FirstOrDefaultAsync(o => o.Id == targetCandidate.OrderId.Value, ct);
            }

            QueueEntry? trackedQueueEntry = null;
            if (targetCandidate.QueueEntryId > 0)
            {
                trackedQueueEntry = await _db.QueueEntries
                    .FirstOrDefaultAsync(q => q.Id == targetCandidate.QueueEntryId, ct);
            }

            if (trackedQueueEntry is null)
            {
                if (trackedOrder is null)
                {
                    throw new NotFoundException("DiagnosticQueueEntry", targetCandidate.Id);
                }

                var maxQueueNumber = await _db.QueueEntries
                    .AsNoTracking()
                    .Where(q =>
                        q.QueueDate == entryQueueDate &&
                        (effectiveSpId.HasValue
                            ? (q.ServicePointId == effectiveSpId.Value || q.CurrentServicePointId == effectiveSpId.Value)
                            : (q.QueueType != null && q.QueueType.ToUpper() == "DIAGNOSTIC")))
                    .MaxAsync(q => (int?)q.QueueNumber, ct);

                var nextQueueNumber = targetCandidate.QueueNumber > 0
                    ? targetCandidate.QueueNumber
                    : (maxQueueNumber ?? 0) + 1;

                trackedQueueEntry = new QueueEntry
                {
                    VisitId = targetCandidate.VisitId,
                    AppointmentId = null,
                    ServicePointId = effectiveSpId,
                    CurrentServicePointId = effectiveSpId,
                    QueueDate = entryQueueDate,
                    QueueNumber = nextQueueNumber,
                    QueueType = "DIAGNOSTIC",
                    Status = previousStatus,
                    PresenceStatus = previousPresence,
                    ArrivedAt = targetCandidate.ArrivedAt ?? nowLocal,
                    ReadyAt = targetCandidate.ReadyAt ?? nowLocal,
                    ArrivalClass = targetCandidate.ArrivalClass,
                    ManualPriority = targetCandidate.ClinicalPriority,
                    StateVersion = previousStateVersion,
                };

                _db.QueueEntries.Add(trackedQueueEntry);
                await SaveChangesWithConcurrencyGuardAsync(ct);

                trackedOrder.QueueEntryId = trackedQueueEntry.Id;
            }

            var currentDbStateVersion = trackedQueueEntry.StateVersion is > 0 ? trackedQueueEntry.StateVersion.Value : 1;
            if (expectedStateVersion.HasValue && expectedStateVersion.Value != currentDbStateVersion)
            {
                throw new ConflictException(
                    "Dữ liệu hàng đợi cận lâm sàng đã thay đổi bởi thao tác khác (state_version mismatch). Vui lòng tải lại hàng đợi.");
            }

            newStateVersion = currentDbStateVersion + 1;
            trackedQueueEntry.QueueType = "DIAGNOSTIC";
            trackedQueueEntry.QueueDate ??= entryQueueDate;
            trackedQueueEntry.StateVersion = newStateVersion;
            trackedQueueEntry.Status = targetStatus;

            if (effectiveSpId.HasValue)
            {
                trackedQueueEntry.ServicePointId ??= effectiveSpId.Value;
                trackedQueueEntry.CurrentServicePointId = effectiveSpId.Value;
            }

            switch (targetStatus)
            {
                case "READY":
                    trackedQueueEntry.ArrivedAt ??= nowLocal;
                    trackedQueueEntry.ReadyAt = nowLocal;
                    trackedQueueEntry.CalledAt = null;
                    trackedQueueEntry.StartedAt = null;
                    trackedQueueEntry.BlockedReason = null;
                    trackedQueueEntry.PresenceStatus = requestedPresence ?? "PRESENT";
                    if (trackedOrder is not null && NormalizeOrderStatus(trackedOrder.Status) == "IN_PROGRESS")
                    {
                        trackedOrder.Status = "CONFIRMED";
                    }
                    break;

                case "WAITING":
                    trackedQueueEntry.ArrivedAt ??= nowLocal;
                    trackedQueueEntry.CalledAt = null;
                    trackedQueueEntry.StartedAt = null;
                    trackedQueueEntry.BlockedReason = null;
                    trackedQueueEntry.PresenceStatus = requestedPresence ?? "PRESENT";
                    if (trackedOrder is not null && NormalizeOrderStatus(trackedOrder.Status) == "IN_PROGRESS")
                    {
                        trackedOrder.Status = "CONFIRMED";
                    }
                    break;

                case "CALLED":
                    trackedQueueEntry.ArrivedAt ??= nowLocal;
                    trackedQueueEntry.ReadyAt ??= nowLocal;
                    trackedQueueEntry.CalledAt = nowLocal;
                    trackedQueueEntry.StartedAt = null;
                    trackedQueueEntry.BlockedReason = null;
                    trackedQueueEntry.PresenceStatus = requestedPresence ?? "PRESENT";
                    if (trackedOrder is not null && NormalizeOrderStatus(trackedOrder.Status) == "IN_PROGRESS")
                    {
                        trackedOrder.Status = "CONFIRMED";
                    }
                    break;

                case "IN_PROGRESS":
                    trackedQueueEntry.ArrivedAt ??= nowLocal;
                    trackedQueueEntry.ReadyAt ??= nowLocal;
                    trackedQueueEntry.CalledAt ??= nowLocal;
                    trackedQueueEntry.StartedAt = nowLocal;
                    trackedQueueEntry.BlockedReason = null;
                    trackedQueueEntry.PresenceStatus = "PRESENT";
                    if (trackedOrder is not null && NormalizeOrderStatus(trackedOrder.Status) is "CONFIRMED" or "ORDERED")
                    {
                        trackedOrder.Status = "IN_PROGRESS";
                    }
                    break;

                case "COMPLETED":
                    trackedQueueEntry.ArrivedAt ??= nowLocal;
                    trackedQueueEntry.ReadyAt ??= nowLocal;
                    trackedQueueEntry.CalledAt ??= nowLocal;
                    trackedQueueEntry.StartedAt ??= nowLocal;
                    trackedQueueEntry.CompletedAt = nowLocal;
                    trackedQueueEntry.BlockedReason = null;
                    trackedQueueEntry.PresenceStatus = requestedPresence ?? "PRESENT";
                    if (trackedOrder is not null && NormalizeOrderStatus(trackedOrder.Status) != "CANCELLED")
                    {
                        trackedOrder.Status = "COMPLETED";
                    }
                    break;

                case "SKIPPED":
                    trackedQueueEntry.StartedAt = null;
                    trackedQueueEntry.BlockedReason = blockedReason ?? reason;
                    trackedQueueEntry.PresenceStatus = requestedPresence ?? "LEFT";
                    if (trackedOrder is not null && NormalizeOrderStatus(trackedOrder.Status) == "IN_PROGRESS")
                    {
                        trackedOrder.Status = "CONFIRMED";
                    }
                    break;

                case "CANCELLED":
                    trackedQueueEntry.StartedAt = null;
                    trackedQueueEntry.CompletedAt = nowLocal;
                    trackedQueueEntry.BlockedReason = blockedReason ?? reason;
                    trackedQueueEntry.PresenceStatus = requestedPresence ?? "LEFT";
                    if (cancelLinkedOrder &&
                        trackedOrder is not null &&
                        NormalizeOrderStatus(trackedOrder.Status) != "COMPLETED")
                    {
                        trackedOrder.Status = "CANCELLED";
                    }
                    break;
            }

            if (trackedOrder is not null)
            {
                updatedOrderStatus = NormalizeOrderStatus(trackedOrder.Status);
            }

            persistedQueueEntryId = trackedQueueEntry.Id;
            var effectivePresence = trackedQueueEntry.PresenceStatus ?? "PRESENT";
            var effectiveAuditReason = reason
                ?? blockedReason
                ?? notes
                ?? $"Cập nhật trạng thái hàng đợi cận lâm sàng {targetCandidate.TicketNo}: {previousStatus} -> {targetStatus}.";

            var auditPayload = new DiagnosticQueueAuditPayload(
                QueueEntryId: persistedQueueEntryId,
                VisitId: targetCandidate.VisitId,
                VisitCode: targetCandidate.VisitCode,
                OrderId: targetCandidate.OrderId,
                OrderCode: targetCandidate.OrderCode,
                PreviousStatus: previousStatus,
                TargetStatus: targetStatus,
                PreviousOrderStatus: previousOrderStatus,
                UpdatedOrderStatus: updatedOrderStatus,
                PreviousPresenceStatus: previousPresence,
                UpdatedPresenceStatus: effectivePresence,
                PreviousPriority: targetCandidate.Priority,
                UpdatedPriority: targetCandidate.Priority,
                PreviousStateVersion: previousStateVersion,
                UpdatedStateVersion: newStateVersion,
                ServicePointId: effectiveSpId,
                ServicePointName: resolvedSp?.Name ?? targetCandidate.ServicePointName,
                RoomId: effectiveRoomId,
                RoomCode: resolvedRoom?.RoomCode ?? targetCandidate.RoomCode,
                Reason: reason,
                BlockedReason: trackedQueueEntry.BlockedReason,
                Notes: notes,
                UpdatedByUserId: actorUserId,
                UpdatedByStaffCode: staffAccess.Staff.StaffCode,
                UpdatedByName: staffAccess.Profile.FullName,
                UpdatedAtUtc: nowUtc);

            _db.AuditLogs.Add(new AuditLog
            {
                ActorUserId = actorUserId,
                HospitalId = staffAccess.Hospital.Id,
                Action = auditAction,
                EntityType = QueueEntryEntityType,
                EntityId = persistedQueueEntryId.ToString(CultureInfo.InvariantCulture),
                Reason = effectiveAuditReason,
                ChangeData = JsonSerializer.Serialize(auditPayload, AuditJsonOptions),
                IpAddress = QueueEntryRequestParsing.ParseIpAddress(ipAddress),
                OccurredAt = nowUtc,
            });

            await SaveChangesWithConcurrencyGuardAsync(ct);

            if (tx is not null)
            {
                await tx.CommitAsync(ct);
            }
        }
        finally
        {
            if (tx is not null)
            {
                await tx.DisposeAsync();
            }
        }

        var refreshedDataset = await LoadHospitalDiagnosticQueueDatasetAsync(
            staffAccess,
            nowLocal,
            todayLocal,
            explicitDate: entryQueueDate,
            ct);

        var updatedEntry = refreshedDataset.AllSortedEntries
            .FirstOrDefault(e => e.QueueEntryId == persistedQueueEntryId)
            ?? refreshedDataset.AllSortedEntries.First(e =>
                (targetCandidate.OrderId.HasValue && e.OrderId == targetCandidate.OrderId.Value) ||
                e.QueueEntryId == persistedQueueEntryId);

        var updatedBucket = refreshedDataset.ServicePointBuckets
            .FirstOrDefault(b => b.ServicePointId == updatedEntry.ServicePointId);

        var message = BuildMutationMessage(auditAction, updatedEntry, previousStatus, targetStatus);

        return new ManageDiagnosticQueueActionResponseDto(
            ActionOutcome: actionOutcomeCode,
            Action: auditAction,
            Message: message,
            QueueEntryId: updatedEntry.QueueEntryId,
            FormattedQueueEntryId: updatedEntry.FormattedQueueEntryId,
            TicketNo: updatedEntry.TicketNo,
            QueueNumber: updatedEntry.QueueNumber,
            OrderId: updatedEntry.OrderId,
            OrderCode: updatedEntry.OrderCode,
            PreviousStatus: previousStatus,
            CurrentStatus: updatedEntry.Status,
            PreviousOrderStatus: previousOrderStatus,
            OrderStatus: updatedEntry.OrderId.HasValue ? updatedEntry.OrderStatus : null,
            PreviousPresenceStatus: previousPresence,
            PresenceStatus: updatedEntry.PresenceStatus,
            PreviousServingOrder: previousServingOrder,
            ServingOrder: updatedEntry.ServingOrder,
            PreviousStateVersion: previousStateVersion,
            StateVersion: updatedEntry.StateVersion,
            BlockedReason: updatedEntry.BlockedReason,
            Reason: reason,
            Notes: notes,
            UpdatedAtUtc: nowUtc,
            UpdatedByStaff: staffAccess.Profile,
            Entry: updatedEntry,
            ServicePointBucket: updatedBucket);
    }

    private async Task<HospitalDiagnosticQueueDataset> LoadHospitalDiagnosticQueueDatasetAsync(
        DiagnosticStaffAccessContext staffAccess,
        DateTime nowLocal,
        DateOnly todayLocal,
        DateOnly? explicitDate,
        CancellationToken ct)
    {
        var hospitalId = staffAccess.Hospital.Id;

        List<DiagnosticOrder> allOrders;
        try
        {
            allOrders = await _db.DiagnosticOrders
                .AsNoTracking()
                .OrderBy(o => o.Id)
                .ToListAsync(ct);
        }
        catch (NotSupportedException)
        {
            allOrders = [];
        }

        var allDepartments = await _db.Departments
            .AsNoTracking()
            .OrderBy(d => d.Id)
            .ToListAsync(ct);
        var departmentsById = allDepartments.ToDictionary(d => d.Id);

        var allServices = await _db.Services
            .AsNoTracking()
            .OrderBy(s => s.Id)
            .ToListAsync(ct);
        var servicesById = allServices.ToDictionary(s => s.Id);

        var allSpecialties = await _db.Specialties
            .AsNoTracking()
            .OrderBy(s => s.Id)
            .ToListAsync(ct);
        var specialtiesById = allSpecialties.ToDictionary(s => s.Id);

        var allRooms = await _db.HospitalRooms
            .AsNoTracking()
            .OrderBy(r => r.Id)
            .ToListAsync(ct);
        var roomsById = allRooms.ToDictionary(r => r.Id);

        var allDoctors = await _db.Doctors
            .AsNoTracking()
            .Include(d => d.Staff)
            .OrderBy(d => d.Id)
            .ToListAsync(ct);
        var doctorsById = allDoctors.ToDictionary(d => d.Id);

        var allStaff = await _db.Staff
            .AsNoTracking()
            .OrderBy(s => s.Id)
            .ToListAsync(ct);
        var staffById = allStaff.ToDictionary(s => s.Id);

        List<QueueServicePoint> allServicePoints;
        try
        {
            allServicePoints = await _db.QueueServicePoints
                .AsNoTracking()
                .OrderBy(sp => sp.Id)
                .ToListAsync(ct);
        }
        catch (NotSupportedException)
        {
            allServicePoints = [];
        }
        var servicePointsById = allServicePoints.ToDictionary(sp => sp.Id);

        List<ExaminationSlot> allSlots;
        try
        {
            allSlots = await _db.ExaminationSlots
                .AsNoTracking()
                .OrderBy(s => s.Id)
                .ToListAsync(ct);
        }
        catch (NotSupportedException)
        {
            allSlots = [];
        }
        var slotsById = allSlots.ToDictionary(s => s.Id);

        List<Appointment> allAppointments;
        try
        {
            allAppointments = await _db.Appointments
                .AsNoTracking()
                .OrderBy(a => a.Id)
                .ToListAsync(ct);
        }
        catch (NotSupportedException)
        {
            allAppointments = [];
        }
        var appointmentsById = allAppointments.ToDictionary(a => a.Id);

        var allVisits = await _db.Visits
            .AsNoTracking()
            .OrderBy(v => v.Id)
            .ToListAsync(ct);
        var visitsById = allVisits.ToDictionary(v => v.Id);

        var allPatients = await _db.Patients
            .AsNoTracking()
            .OrderBy(p => p.Id)
            .ToListAsync(ct);
        var patientsById = allPatients.ToDictionary(p => p.Id);

        var allQueueEntries = await _db.QueueEntries
            .AsNoTracking()
            .OrderBy(q => q.Id)
            .ToListAsync(ct);
        var queueEntriesById = allQueueEntries.ToDictionary(q => q.Id);

        var resultsByOrderId = await TryLoadDiagnosticResultsByOrderIdAsync(allOrders, ct);

        long? ResolveServiceHospitalId(Service? svc)
        {
            if (svc is null) return null;
            if (svc.HospitalId.HasValue) return svc.HospitalId.Value;
            if (svc.DepartmentId.HasValue && departmentsById.TryGetValue(svc.DepartmentId.Value, out var dept))
            {
                return dept.HospitalId;
            }
            return null;
        }

        long? ResolveRoomHospitalId(HospitalRoom? rm)
        {
            if (rm is null) return null;
            if (rm.HospitalId.HasValue) return rm.HospitalId.Value;
            if (rm.DepartmentId.HasValue && departmentsById.TryGetValue(rm.DepartmentId.Value, out var dept))
            {
                return dept.HospitalId;
            }
            return null;
        }

        long? ResolveDoctorHospitalId(Doctor? doc)
        {
            if (doc is null) return null;
            if (doc.HospitalId.HasValue) return doc.HospitalId.Value;
            if (doc.Staff?.HospitalId != null) return doc.Staff.HospitalId;
            if (doc.StaffId.HasValue && staffById.TryGetValue(doc.StaffId.Value, out var docStaff) && docStaff.HospitalId.HasValue)
            {
                return docStaff.HospitalId.Value;
            }
            return null;
        }

        long? ResolveServicePointHospitalId(QueueServicePoint? sp)
        {
            if (sp is null) return null;
            if (sp.HospitalId.HasValue) return sp.HospitalId.Value;
            if (sp.RoomId.HasValue && roomsById.TryGetValue(sp.RoomId.Value, out var rm))
            {
                var rmHosp = ResolveRoomHospitalId(rm);
                if (rmHosp.HasValue) return rmHosp.Value;
            }
            if (sp.ServiceId.HasValue && servicesById.TryGetValue(sp.ServiceId.Value, out var svc))
            {
                var svcHosp = ResolveServiceHospitalId(svc);
                if (svcHosp.HasValue) return svcHosp.Value;
            }
            if (sp.DoctorId.HasValue && doctorsById.TryGetValue(sp.DoctorId.Value, out var doc))
            {
                var docHosp = ResolveDoctorHospitalId(doc);
                if (docHosp.HasValue) return docHosp.Value;
            }
            return null;
        }

        long? ResolveSlotHospitalId(ExaminationSlot? slot)
        {
            if (slot is null) return null;
            if (slot.RoomId.HasValue && roomsById.TryGetValue(slot.RoomId.Value, out var rm))
            {
                var rmHosp = ResolveRoomHospitalId(rm);
                if (rmHosp.HasValue) return rmHosp.Value;
            }
            if (slot.ServiceId.HasValue && servicesById.TryGetValue(slot.ServiceId.Value, out var svc))
            {
                var svcHosp = ResolveServiceHospitalId(svc);
                if (svcHosp.HasValue) return svcHosp.Value;
            }
            if (slot.DoctorId.HasValue && doctorsById.TryGetValue(slot.DoctorId.Value, out var doc))
            {
                var docHosp = ResolveDoctorHospitalId(doc);
                if (docHosp.HasValue) return docHosp.Value;
            }
            return null;
        }

        long? ResolveAppointmentHospitalId(Appointment? appt)
        {
            if (appt is null) return null;
            if (appt.SlotId.HasValue && slotsById.TryGetValue(appt.SlotId.Value, out var slot))
            {
                return ResolveSlotHospitalId(slot);
            }
            return null;
        }

        long? ResolveVisitHospitalId(Visit? visit)
        {
            if (visit is null) return null;
            if (visit.ExaminationRoomId.HasValue && roomsById.TryGetValue(visit.ExaminationRoomId.Value, out var rm))
            {
                var rmHosp = ResolveRoomHospitalId(rm);
                if (rmHosp.HasValue) return rmHosp.Value;
            }
            if (visit.AssignedDoctorId.HasValue && doctorsById.TryGetValue(visit.AssignedDoctorId.Value, out var doc))
            {
                var docHosp = ResolveDoctorHospitalId(doc);
                if (docHosp.HasValue) return docHosp.Value;
            }
            if (visit.CheckedInByStaffId.HasValue && staffById.TryGetValue(visit.CheckedInByStaffId.Value, out var stf) && stf.HospitalId.HasValue)
            {
                return stf.HospitalId.Value;
            }
            if (visit.AppointmentId.HasValue && appointmentsById.TryGetValue(visit.AppointmentId.Value, out var appt))
            {
                var apptHosp = ResolveAppointmentHospitalId(appt);
                if (apptHosp.HasValue) return apptHosp.Value;
            }
            if (visit.CompletedByDoctorId.HasValue && doctorsById.TryGetValue(visit.CompletedByDoctorId.Value, out var compDoc))
            {
                var compDocHosp = ResolveDoctorHospitalId(compDoc);
                if (compDocHosp.HasValue) return compDocHosp.Value;
            }
            return null;
        }

        long? ResolveQueueEntryHospitalId(QueueEntry? qe)
        {
            if (qe is null) return null;
            var spId = qe.CurrentServicePointId ?? qe.ServicePointId;
            if (spId.HasValue && servicePointsById.TryGetValue(spId.Value, out var sp))
            {
                var spHosp = ResolveServicePointHospitalId(sp);
                if (spHosp.HasValue) return spHosp.Value;
            }
            if (qe.VisitId.HasValue && visitsById.TryGetValue(qe.VisitId.Value, out var vis))
            {
                var visHosp = ResolveVisitHospitalId(vis);
                if (visHosp.HasValue) return visHosp.Value;
            }
            if (qe.AppointmentId.HasValue && appointmentsById.TryGetValue(qe.AppointmentId.Value, out var appt))
            {
                var apptHosp = ResolveAppointmentHospitalId(appt);
                if (apptHosp.HasValue) return apptHosp.Value;
            }
            return null;
        }

        long? ResolveOrderHospitalId(DiagnosticOrder? order)
        {
            if (order is null) return null;
            if (order.QueueEntryId.HasValue && queueEntriesById.TryGetValue(order.QueueEntryId.Value, out var qe))
            {
                var qeHosp = ResolveQueueEntryHospitalId(qe);
                if (qeHosp.HasValue) return qeHosp.Value;
            }
            if (order.VisitId.HasValue && visitsById.TryGetValue(order.VisitId.Value, out var vis))
            {
                var visHosp = ResolveVisitHospitalId(vis);
                if (visHosp.HasValue) return visHosp.Value;
            }
            if (order.OrderingDoctorId.HasValue && doctorsById.TryGetValue(order.OrderingDoctorId.Value, out var doc))
            {
                var docHosp = ResolveDoctorHospitalId(doc);
                if (docHosp.HasValue) return docHosp.Value;
            }
            if (order.ConfirmedByStaffId.HasValue && staffById.TryGetValue(order.ConfirmedByStaffId.Value, out var confStaff) && confStaff.HospitalId.HasValue)
            {
                return confStaff.HospitalId.Value;
            }
            if (order.ServiceId.HasValue && servicesById.TryGetValue(order.ServiceId.Value, out var svc))
            {
                var svcHosp = ResolveServiceHospitalId(svc);
                if (svcHosp.HasValue) return svcHosp.Value;
            }
            return null;
        }

        var hospitalDiagServicePoints = allServicePoints
            .Where(sp =>
                string.Equals(sp.PointType?.Trim(), "DIAGNOSTIC", StringComparison.OrdinalIgnoreCase) &&
                ResolveServicePointHospitalId(sp) == hospitalId)
            .OrderBy(sp => sp.Id)
            .ToList();

        var hospitalDiagSpIds = hospitalDiagServicePoints.Select(sp => sp.Id).ToHashSet();

        var orderByQueueEntryId = new Dictionary<long, DiagnosticOrder>();
        var hospitalOrders = new List<DiagnosticOrder>();
        var foreignOrders = new List<DiagnosticOrder>();

        foreach (var order in allOrders)
        {
            var orderHospId = ResolveOrderHospitalId(order);
            if (orderHospId == hospitalId)
            {
                hospitalOrders.Add(order);
                if (order.QueueEntryId.HasValue && !orderByQueueEntryId.ContainsKey(order.QueueEntryId.Value))
                {
                    orderByQueueEntryId[order.QueueEntryId.Value] = order;
                }
            }
            else if (orderHospId.HasValue)
            {
                foreignOrders.Add(order);
            }
        }

        var hospitalDiagQueueEntries = new List<QueueEntry>();
        var foreignQueueEntries = new List<QueueEntry>();
        var nonDiagnosticHospitalQueueEntries = new List<QueueEntry>();

        foreach (var qe in allQueueEntries)
        {
            var spId = qe.CurrentServicePointId ?? qe.ServicePointId;
            var isDiagType = string.Equals(qe.QueueType?.Trim(), "DIAGNOSTIC", StringComparison.OrdinalIgnoreCase) ||
                             orderByQueueEntryId.ContainsKey(qe.Id) ||
                             (spId.HasValue && hospitalDiagSpIds.Contains(spId.Value));

            var qeHospId = ResolveQueueEntryHospitalId(qe);
            if (!qeHospId.HasValue && orderByQueueEntryId.TryGetValue(qe.Id, out var linkedOrd))
            {
                qeHospId = ResolveOrderHospitalId(linkedOrd);
            }

            if (qeHospId == hospitalId)
            {
                if (isDiagType)
                {
                    hospitalDiagQueueEntries.Add(qe);
                }
                else
                {
                    nonDiagnosticHospitalQueueEntries.Add(qe);
                }
            }
            else if (qeHospId.HasValue)
            {
                foreignQueueEntries.Add(qe);
            }
        }

        // Determine effective queue date when explicitDate is not provided
        var effectiveQueueDate = explicitDate ?? todayLocal;
        if (!explicitDate.HasValue &&
            hospitalDiagQueueEntries.Count > 0 &&
            !hospitalDiagQueueEntries.Any(q => q.QueueDate == todayLocal))
        {
            var latestDate = hospitalDiagQueueEntries
                .Where(q => q.QueueDate.HasValue)
                .Select(q => q.QueueDate!.Value)
                .OrderByDescending(d => d)
                .FirstOrDefault();

            if (latestDate != default)
            {
                effectiveQueueDate = latestDate;
            }
        }

        var rawCandidates = new List<RawDiagnosticQueueCandidate>();
        var handledQueueEntryIds = new HashSet<long>();

        foreach (var qe in hospitalDiagQueueEntries)
        {
            handledQueueEntryIds.Add(qe.Id);
            orderByQueueEntryId.TryGetValue(qe.Id, out var linkedOrder);

            if (linkedOrder is null && qe.VisitId.HasValue)
            {
                var spId = qe.CurrentServicePointId ?? qe.ServicePointId;
                servicePointsById.TryGetValue(spId ?? 0, out var sp);
                linkedOrder = hospitalOrders
                    .Where(o => o.VisitId == qe.VisitId && (!o.QueueEntryId.HasValue || o.QueueEntryId == qe.Id))
                    .OrderByDescending(o => sp?.ServiceId != null && o.ServiceId == sp.ServiceId)
                    .ThenBy(o => o.Id)
                    .FirstOrDefault();
            }

            rawCandidates.Add(new RawDiagnosticQueueCandidate(qe, linkedOrder));
        }

        // Include confirmed/in-progress orders in this hospital that do not yet have a persisted QueueEntry
        foreach (var order in hospitalOrders)
        {
            if (order.QueueEntryId.HasValue && handledQueueEntryIds.Contains(order.QueueEntryId.Value))
            {
                continue;
            }

            var normOrderStatus = NormalizeOrderStatus(order.Status);
            if (normOrderStatus is "CONFIRMED" or "IN_PROGRESS")
            {
                rawCandidates.Add(new RawDiagnosticQueueCandidate(null, order));
            }
        }

        // Build unsorted item DTOs
        var preliminaryItems = new List<DiagnosticQueueEntryItemDto>(rawCandidates.Count);
        foreach (var candidate in rawCandidates)
        {
            var item = BuildPreliminaryEntryDto(
                candidate.QueueEntry,
                candidate.Order,
                effectiveQueueDate,
                todayLocal,
                hospitalDiagServicePoints,
                servicePointsById,
                roomsById,
                departmentsById,
                servicesById,
                specialtiesById,
                visitsById,
                patientsById,
                doctorsById,
                staffById,
                resultsByOrderId);

            if (explicitDate.HasValue)
            {
                var dateStr = explicitDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                if (!string.Equals(item.QueueDate, dateStr, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }

            preliminaryItems.Add(item);
        }

        // Group by service point, sort deterministically, assign ServingOrder & EstimatedWaitMinutes
        var buckets = BuildServicePointQueueBuckets(
            hospitalDiagServicePoints,
            roomsById,
            departmentsById,
            servicesById,
            specialtiesById,
            preliminaryItems,
            out var allSortedEntries);

        return new HospitalDiagnosticQueueDataset(
            EffectiveQueueDate: effectiveQueueDate,
            HospitalDiagnosticServicePoints: hospitalDiagServicePoints,
            AllServicePointsById: servicePointsById,
            RoomsById: roomsById,
            DepartmentsById: departmentsById,
            ServicesById: servicesById,
            SpecialtiesById: specialtiesById,
            VisitsById: visitsById,
            QueueEntriesById: queueEntriesById,
            AllOrders: allOrders,
            ForeignOrders: foreignOrders,
            ForeignQueueEntries: foreignQueueEntries,
            NonDiagnosticHospitalQueueEntries: nonDiagnosticHospitalQueueEntries,
            ServicePointBuckets: buckets,
            AllSortedEntries: allSortedEntries,
            ResolveServicePointHospitalId: ResolveServicePointHospitalId,
            ResolveRoomHospitalId: ResolveRoomHospitalId);
    }

    private static DiagnosticQueueEntryItemDto BuildPreliminaryEntryDto(
        QueueEntry? qe,
        DiagnosticOrder? order,
        DateOnly defaultQueueDate,
        DateOnly todayLocal,
        IReadOnlyList<QueueServicePoint> hospitalDiagServicePoints,
        IReadOnlyDictionary<long, QueueServicePoint> servicePointsById,
        IReadOnlyDictionary<long, HospitalRoom> roomsById,
        IReadOnlyDictionary<long, Department> departmentsById,
        IReadOnlyDictionary<long, Service> servicesById,
        IReadOnlyDictionary<long, Specialty> specialtiesById,
        IReadOnlyDictionary<long, Visit> visitsById,
        IReadOnlyDictionary<long, Patient> patientsById,
        IReadOnlyDictionary<long, Doctor> doctorsById,
        IReadOnlyDictionary<long, Staff> staffById,
        IReadOnlyDictionary<long, DiagnosticResult> resultsByOrderId)
    {
        var visitId = qe?.VisitId ?? order?.VisitId ?? 0;
        visitsById.TryGetValue(visitId, out var visit);

        var patientId = visit?.PatientId ?? 0;
        patientsById.TryGetValue(patientId, out var patient);

        var spId = qe?.CurrentServicePointId ?? qe?.ServicePointId;
        QueueServicePoint? servicePoint = null;
        if (spId.HasValue)
        {
            servicePointsById.TryGetValue(spId.Value, out servicePoint);
        }

        Service? service = null;
        if (order?.ServiceId != null)
        {
            servicesById.TryGetValue(order.ServiceId.Value, out service);
        }
        else if (servicePoint?.ServiceId != null)
        {
            servicesById.TryGetValue(servicePoint.ServiceId.Value, out service);
        }

        if (servicePoint is null && service is not null)
        {
            servicePoint = ResolveDiagnosticServicePointForService(service.Id, hospitalDiagServicePoints);
            spId = servicePoint?.Id;
        }
        else if (servicePoint is null && hospitalDiagServicePoints.Count > 0)
        {
            servicePoint = hospitalDiagServicePoints[0];
            spId = servicePoint.Id;
        }

        var roomId = servicePoint?.RoomId;
        HospitalRoom? room = null;
        if (roomId.HasValue)
        {
            roomsById.TryGetValue(roomId.Value, out room);
        }

        var departmentId = service?.DepartmentId ?? room?.DepartmentId;
        Department? department = null;
        if (departmentId.HasValue)
        {
            departmentsById.TryGetValue(departmentId.Value, out department);
        }

        var specialtyId = service?.SpecialtyId;
        Specialty? specialty = null;
        if (specialtyId.HasValue)
        {
            specialtiesById.TryGetValue(specialtyId.Value, out specialty);
        }

        var orderingDoctorId = order?.OrderingDoctorId ?? visit?.AssignedDoctorId;
        Doctor? orderingDoctor = null;
        if (orderingDoctorId.HasValue)
        {
            doctorsById.TryGetValue(orderingDoctorId.Value, out orderingDoctor);
        }

        Staff? confirmedByStaff = null;
        if (order?.ConfirmedByStaffId != null)
        {
            staffById.TryGetValue(order.ConfirmedByStaffId.Value, out confirmedByStaff);
        }

        DiagnosticResult? result = null;
        if (order is not null)
        {
            resultsByOrderId.TryGetValue(order.Id, out result);
        }

        var hasResult = result is not null;
        var resultStatus = result is not null
            ? AppointmentBookingRules.NormalizeStatusKey(result.Status)
            : "NONE";
        var isResultFinalized = result is not null &&
            (result.FinalizedAt.HasValue || resultStatus is "FINALIZED" or "VERIFIED" or "COMPLETED");

        var queueEntryId = qe?.Id ?? 0;
        var queueNumber = qe?.QueueNumber ?? (int)(order?.Id ?? 1);
        var ticketNo = $"C{queueNumber:D3}";
        var queueDate = qe?.QueueDate
            ?? (order?.ConfirmedAt.HasValue == true ? DateOnly.FromDateTime(order.ConfirmedAt.Value) : (DateOnly?)null)
            ?? (order?.OrderedAt.HasValue == true ? DateOnly.FromDateTime(order.OrderedAt.Value) : (DateOnly?)null)
            ?? defaultQueueDate;

        var status = qe is not null
            ? NormalizeQueueStatus(qe.Status)
            : (NormalizeOrderStatus(order?.Status) == "IN_PROGRESS" ? "IN_PROGRESS" : "WAITING");

        var presenceStatus = !string.IsNullOrWhiteSpace(qe?.PresenceStatus)
            ? qe!.PresenceStatus!.Trim().ToUpperInvariant()
            : "PRESENT";

        var arrivalClass = !string.IsNullOrWhiteSpace(qe?.ArrivalClass)
            ? qe!.ArrivalClass!.Trim().ToUpperInvariant()
            : "WALK_IN";

        var clinicalPriority = !string.IsNullOrWhiteSpace(qe?.ManualPriority)
            ? qe!.ManualPriority!.Trim().ToUpperInvariant()
            : "NORMAL";

        var orderPriority = NormalizePriority(order?.Priority);
        var stateVersion = qe?.StateVersion is > 0 ? qe.StateVersion.Value : 1;

        var orderStatus = order is not null
            ? (isResultFinalized && NormalizeOrderStatus(order.Status) != "CANCELLED"
                ? "COMPLETED"
                : NormalizeOrderStatus(order.Status))
            : (status switch
            {
                "COMPLETED" => "COMPLETED",
                "IN_PROGRESS" => "IN_PROGRESS",
                "CANCELLED" => "CANCELLED",
                _ => "CONFIRMED",
            });

        var visitStatus = visit is not null ? NormalizeVisitStatus(visit.Status) : "IN_PROGRESS";
        var visitCode = visit is not null
            ? FormatVisitCode(visit, queueDate)
            : (visitId > 0 ? $"VIS-{queueDate:yyyyMM}-{visitId:D4}" : "VIS-UNKNOWN");

        var orderCode = order is not null
            ? FormatOrderCode(order, queueDate)
            : null;

        var category = service is not null
            ? ResolveDiagnosticCategory(service, specialtiesById)
            : "LABORATORY";

        var isSpActive = servicePoint?.IsActive != false;
        var isRoomActive = room is null || string.Equals(room.Status?.Trim(), "ACTIVE", StringComparison.OrdinalIgnoreCase);

        var (isEligible, ineligibleReason) = EvaluateQueueEntryEligibility(
            status,
            presenceStatus,
            orderStatus,
            visitStatus,
            isSpActive,
            isRoomActive);

        var allowedTargetStatuses = GetAllowedTargetStatusesForEntry(
            status,
            orderStatus,
            visitStatus);

        var canReceive = status is "RESERVED" or "WAITING" &&
                         (status == "RESERVED" || presenceStatus != "PRESENT") &&
                         allowedTargetStatuses.Contains("READY", StringComparer.OrdinalIgnoreCase);
        var canCall = status is "WAITING" or "READY" &&
                      isEligible &&
                      allowedTargetStatuses.Contains("CALLED", StringComparer.OrdinalIgnoreCase);
        var canStart = status is "CALLED" or "READY" or "WAITING" &&
                       orderStatus is "CONFIRMED" or "IN_PROGRESS" &&
                       isSpActive &&
                       isRoomActive &&
                       !IsTerminalVisitStatus(visitStatus) &&
                       allowedTargetStatuses.Contains("IN_PROGRESS", StringComparer.OrdinalIgnoreCase);
        var canComplete = status == "IN_PROGRESS" &&
                          !IsTerminalVisitStatus(visitStatus) &&
                          allowedTargetStatuses.Contains("COMPLETED", StringComparer.OrdinalIgnoreCase);
        var canSkip = status is "CALLED" or "READY" or "WAITING" &&
                      !IsTerminalVisitStatus(visitStatus) &&
                      allowedTargetStatuses.Contains("SKIPPED", StringComparer.OrdinalIgnoreCase);
        var canRestore = status == "SKIPPED" &&
                         !IsTerminalVisitStatus(visitStatus) &&
                         isSpActive &&
                         isRoomActive &&
                         allowedTargetStatuses.Contains("READY", StringComparer.OrdinalIgnoreCase);
        var canCancel = status is "RESERVED" or "WAITING" or "READY" or "CALLED" or "SKIPPED" &&
                        !IsTerminalVisitStatus(visitStatus) &&
                        allowedTargetStatuses.Contains("CANCELLED", StringComparer.OrdinalIgnoreCase);

        var allowedActions = BuildEntryActions(
            canReceive,
            canCall,
            canStart,
            canComplete,
            canSkip,
            canRestore,
            canCancel,
            ineligibleReason);

        var formattedQeId = queueEntryId > 0
            ? $"q-{queueEntryId:D3}"
            : $"do-{order?.Id ?? 0:D4}";

        var formattedSpId = spId.HasValue ? $"sp-{spId.Value:D3}" : "sp-unassigned";
        var patientGender = string.IsNullOrWhiteSpace(patient?.Gender)
            ? "OTHER"
            : patient!.Gender!.Trim().ToUpperInvariant();

        return new DiagnosticQueueEntryItemDto(
            Id: formattedQeId,
            QueueEntryId: queueEntryId,
            FormattedQueueEntryId: formattedQeId,
            QueueNumber: queueNumber,
            TicketNo: ticketNo,
            ServingOrder: 0,
            QueueDate: queueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            QueueType: "DIAGNOSTIC",
            Status: status,
            StatusLabel: FormatQueueStatusLabel(status),
            PresenceStatus: presenceStatus,
            PresenceStatusLabel: FormatPresenceStatusLabel(presenceStatus),
            ArrivalClass: arrivalClass,
            ClinicalPriority: clinicalPriority,
            Priority: orderPriority,
            PriorityLabel: FormatPriorityLabel(orderPriority),
            StateVersion: stateVersion,
            IsEligible: isEligible,
            IneligibleReason: ineligibleReason,
            ScheduledAt: qe?.ScheduledAt,
            ArrivedAt: qe?.ArrivedAt ?? order?.ConfirmedAt ?? order?.OrderedAt,
            ReadyAt: qe?.ReadyAt ?? order?.ConfirmedAt ?? order?.OrderedAt,
            CalledAt: qe?.CalledAt,
            StartedAt: qe?.StartedAt,
            CompletedAt: qe?.CompletedAt,
            BlockedReason: qe?.BlockedReason,
            EstimatedWaitMinutes: 0,
            EstimatedWaitLabel: "0 phút",
            OrderId: order?.Id,
            OrderCode: orderCode,
            OrderStatus: orderStatus,
            OrderStatusLabel: FormatOrderStatusLabel(orderStatus),
            RequiredForConclusion: order?.RequiredForConclusion ?? false,
            Instruction: order?.Instruction,
            OrderedAt: order?.OrderedAt,
            ConfirmedAt: order?.ConfirmedAt,
            ConfirmedByStaffId: order?.ConfirmedByStaffId,
            ConfirmedByStaffCode: confirmedByStaff?.StaffCode,
            ConfirmedByStaffName: confirmedByStaff?.FullName,
            VisitId: visitId,
            VisitCode: visitCode,
            VisitStatus: visitStatus,
            PatientId: patientId,
            PatientCode: !string.IsNullOrWhiteSpace(patient?.PatientCode) ? patient!.PatientCode! : $"PAT-{patientId:D4}",
            PatientName: !string.IsNullOrWhiteSpace(patient?.FullName) ? patient!.FullName! : $"Bệnh nhân #{patientId}",
            PatientGender: patientGender,
            PatientGenderLabel: FormatGenderLabel(patientGender),
            PatientDateOfBirth: patient?.DateOfBirth,
            PatientAge: CalculatePatientAge(patient?.DateOfBirth, todayLocal),
            BloodType: patient?.BloodType,
            AllergiesSummary: null,
            ServiceId: service?.Id ?? order?.ServiceId ?? 0,
            ServiceCode: service?.ServiceCode ?? "CLS-GEN",
            ServiceName: service?.Name ?? "Dịch vụ Cận lâm sàng",
            Category: category,
            CategoryLabel: FormatCategoryLabel(category),
            DepartmentId: department?.Id ?? departmentId,
            DepartmentCode: department?.DepartmentCode,
            DepartmentName: department?.Name ?? DefaultDepartmentName,
            SpecialtyId: specialty?.Id ?? specialtyId,
            SpecialtyCode: specialty?.SpecialtyCode,
            SpecialtyName: specialty?.Name ?? "Chẩn đoán Cận lâm sàng",
            OrderingDoctorId: orderingDoctor?.Id ?? orderingDoctorId,
            OrderingDoctorCode: orderingDoctor?.DoctorCode,
            OrderingDoctorName: FormatDoctorName(orderingDoctor),
            ServicePointId: spId,
            FormattedServicePointId: formattedSpId,
            ServicePointName: !string.IsNullOrWhiteSpace(servicePoint?.Name)
                ? servicePoint!.Name!
                : (room?.Name ?? "Điểm phục vụ Cận lâm sàng"),
            RoomId: room?.Id ?? roomId,
            RoomCode: room?.RoomCode ?? (spId.HasValue ? $"CLS-{spId.Value:D2}" : "CLS-00"),
            RoomName: room?.Name ?? servicePoint?.Name ?? "Phòng Cận lâm sàng",
            RoomLocation: room?.Location,
            HasResult: hasResult,
            IsResultFinalized: isResultFinalized,
            ResultStatus: resultStatus,
            AllowedTargetStatuses: allowedTargetStatuses,
            CanReceive: canReceive,
            CanCall: canCall,
            CanStart: canStart,
            CanComplete: canComplete,
            CanSkip: canSkip,
            CanRestore: canRestore,
            CanCancel: canCancel,
            AllowedActions: allowedActions);
    }

    private static IReadOnlyList<DiagnosticServicePointQueueBucketDto> BuildServicePointQueueBuckets(
        IReadOnlyList<QueueServicePoint> hospitalServicePoints,
        IReadOnlyDictionary<long, HospitalRoom> roomsById,
        IReadOnlyDictionary<long, Department> departmentsById,
        IReadOnlyDictionary<long, Service> servicesById,
        IReadOnlyDictionary<long, Specialty> specialtiesById,
        IReadOnlyList<DiagnosticQueueEntryItemDto> preliminaryItems,
        out List<DiagnosticQueueEntryItemDto> allSortedEntries)
    {
        var buckets = new List<DiagnosticServicePointQueueBucketDto>();
        allSortedEntries = new List<DiagnosticQueueEntryItemDto>(preliminaryItems.Count);
        var handledSpIds = new HashSet<long>();

        foreach (var sp in hospitalServicePoints)
        {
            handledSpIds.Add(sp.Id);
            var rawSpEntries = preliminaryItems
                .Where(e => e.ServicePointId == sp.Id)
                .ToList();

            var sortedSpEntries = SortAndRankBucketEntries(rawSpEntries);
            allSortedEntries.AddRange(sortedSpEntries);

            buckets.Add(CreateServicePointBucketDto(
                sp,
                roomsById,
                departmentsById,
                servicesById,
                specialtiesById,
                sortedSpEntries));
        }

        var unassignedGroups = preliminaryItems
            .Where(e => !e.ServicePointId.HasValue || !handledSpIds.Contains(e.ServicePointId.Value))
            .GroupBy(e => e.ServicePointId ?? -(e.RoomId ?? e.ServiceId));

        foreach (var group in unassignedGroups)
        {
            var sortedGroupEntries = SortAndRankBucketEntries(group.ToList());
            allSortedEntries.AddRange(sortedGroupEntries);
            var sample = sortedGroupEntries[0];

            var currentServing = sortedGroupEntries
                .FirstOrDefault(e => e.Status is "IN_PROGRESS" or "CALLED");
            var nextWaiting = sortedGroupEntries
                .FirstOrDefault(e => e.Status is "READY" or "WAITING" && e.IsEligible);

            var activeLoad = sortedGroupEntries.Count(e =>
                e.Status is "IN_PROGRESS" or "CALLED" ||
                (e.Status is "READY" or "WAITING" && e.PresenceStatus == "PRESENT"));

            buckets.Add(new DiagnosticServicePointQueueBucketDto(
                ServicePointId: sample.ServicePointId,
                FormattedServicePointId: sample.FormattedServicePointId,
                ServicePointName: sample.ServicePointName,
                RoomId: sample.RoomId,
                RoomCode: sample.RoomCode,
                RoomName: sample.RoomName,
                RoomLocation: sample.RoomLocation,
                DepartmentId: sample.DepartmentId,
                DepartmentCode: sample.DepartmentCode,
                DepartmentName: sample.DepartmentName,
                ServiceId: sample.ServiceId,
                ServiceCode: sample.ServiceCode,
                ServiceName: sample.ServiceName,
                Category: sample.Category,
                CategoryLabel: sample.CategoryLabel,
                IsActive: true,
                IsRoomActive: true,
                Capacity: 1,
                CurrentServingTicketNo: currentServing?.TicketNo,
                CurrentServingEntry: currentServing,
                NextWaitingTicketNo: nextWaiting?.TicketNo,
                NextWaitingEntry: nextWaiting,
                WaitingCount: sortedGroupEntries.Count(e => e.Status is "WAITING" or "RESERVED"),
                ReadyCount: sortedGroupEntries.Count(e => e.Status == "READY"),
                CalledCount: sortedGroupEntries.Count(e => e.Status == "CALLED"),
                InProgressCount: sortedGroupEntries.Count(e => e.Status == "IN_PROGRESS"),
                CompletedCount: sortedGroupEntries.Count(e => e.Status == "COMPLETED"),
                SkippedCount: sortedGroupEntries.Count(e => e.Status == "SKIPPED"),
                CancelledCount: sortedGroupEntries.Count(e => e.Status == "CANCELLED"),
                TotalEntriesCount: sortedGroupEntries.Count,
                EstimatedWaitMinutes: activeLoad * 10,
                Entries: sortedGroupEntries));
        }

        allSortedEntries = SortEntriesGlobal(allSortedEntries);
        return buckets;
    }

    private static DiagnosticServicePointQueueBucketDto CreateServicePointBucketDto(
        QueueServicePoint sp,
        IReadOnlyDictionary<long, HospitalRoom> roomsById,
        IReadOnlyDictionary<long, Department> departmentsById,
        IReadOnlyDictionary<long, Service> servicesById,
        IReadOnlyDictionary<long, Specialty> specialtiesById,
        IReadOnlyList<DiagnosticQueueEntryItemDto> sortedSpEntries)
    {
        roomsById.TryGetValue(sp.RoomId ?? 0, out var room);
        servicesById.TryGetValue(sp.ServiceId ?? 0, out var service);
        var deptId = service?.DepartmentId ?? room?.DepartmentId;
        Department? department = null;
        if (deptId.HasValue)
        {
            departmentsById.TryGetValue(deptId.Value, out department);
        }

        var category = service is not null
            ? ResolveDiagnosticCategory(service, specialtiesById)
            : (sortedSpEntries.FirstOrDefault()?.Category ?? "LABORATORY");

        var isSpActive = sp.IsActive != false;
        var isRoomActive = room is null || string.Equals(room.Status?.Trim(), "ACTIVE", StringComparison.OrdinalIgnoreCase);

        var currentServing = sortedSpEntries
            .FirstOrDefault(e => e.Status is "IN_PROGRESS" or "CALLED");
        var nextWaiting = sortedSpEntries
            .FirstOrDefault(e => e.Status is "READY" or "WAITING" && e.IsEligible);

        var activeLoad = sortedSpEntries.Count(e =>
            e.Status is "IN_PROGRESS" or "CALLED" ||
            (e.Status is "READY" or "WAITING" && e.PresenceStatus == "PRESENT"));

        return new DiagnosticServicePointQueueBucketDto(
            ServicePointId: sp.Id,
            FormattedServicePointId: $"sp-{sp.Id:D3}",
            ServicePointName: !string.IsNullOrWhiteSpace(sp.Name) ? sp.Name : $"Điểm CLS #{sp.Id}",
            RoomId: room?.Id ?? sp.RoomId,
            RoomCode: room?.RoomCode ?? $"CLS-{sp.Id:D2}",
            RoomName: room?.Name ?? sp.Name ?? $"Phòng CLS #{sp.Id}",
            RoomLocation: room?.Location,
            DepartmentId: department?.Id ?? deptId,
            DepartmentCode: department?.DepartmentCode,
            DepartmentName: department?.Name ?? DefaultDepartmentName,
            ServiceId: service?.Id ?? sp.ServiceId,
            ServiceCode: service?.ServiceCode,
            ServiceName: service?.Name,
            Category: category,
            CategoryLabel: FormatCategoryLabel(category),
            IsActive: isSpActive && isRoomActive,
            IsRoomActive: isRoomActive,
            Capacity: room?.Capacity is > 0 ? room.Capacity.Value : 1,
            CurrentServingTicketNo: currentServing?.TicketNo,
            CurrentServingEntry: currentServing,
            NextWaitingTicketNo: nextWaiting?.TicketNo,
            NextWaitingEntry: nextWaiting,
            WaitingCount: sortedSpEntries.Count(e => e.Status is "WAITING" or "RESERVED"),
            ReadyCount: sortedSpEntries.Count(e => e.Status == "READY"),
            CalledCount: sortedSpEntries.Count(e => e.Status == "CALLED"),
            InProgressCount: sortedSpEntries.Count(e => e.Status == "IN_PROGRESS"),
            CompletedCount: sortedSpEntries.Count(e => e.Status == "COMPLETED"),
            SkippedCount: sortedSpEntries.Count(e => e.Status == "SKIPPED"),
            CancelledCount: sortedSpEntries.Count(e => e.Status == "CANCELLED"),
            TotalEntriesCount: sortedSpEntries.Count,
            EstimatedWaitMinutes: activeLoad * 10,
            Entries: sortedSpEntries);
    }

    private static DiagnosticServicePointQueueBucketDto BuildEmptyServicePointBucket(
        QueueServicePoint sp,
        IReadOnlyDictionary<long, HospitalRoom> roomsById,
        IReadOnlyDictionary<long, Department> departmentsById,
        IReadOnlyDictionary<long, Service> servicesById,
        IReadOnlyDictionary<long, Specialty> specialtiesById)
    {
        return CreateServicePointBucketDto(
            sp,
            roomsById,
            departmentsById,
            servicesById,
            specialtiesById,
            []);
    }

    private static List<DiagnosticQueueEntryItemDto> SortAndRankBucketEntries(
        IReadOnlyList<DiagnosticQueueEntryItemDto> entries)
    {
        var sorted = entries
            .OrderBy(e => GetQueueStatusRank(e.Status))
            .ThenBy(e => IsWaitingOrReadyStatus(e.Status) ? (e.IsEligible ? 0 : 1) : 0)
            .ThenBy(e => IsWaitingOrReadyStatus(e.Status) ? (e.PresenceStatus == "PRESENT" ? 0 : 1) : 0)
            .ThenBy(e => IsActiveSortableStatus(e.Status) ? GetOrderPrioritySortRank(e.Priority) : 0)
            .ThenBy(e => IsActiveSortableStatus(e.Status) ? GetClinicalPrioritySortRank(e.ClinicalPriority) : 0)
            .ThenBy(e => IsActiveSortableStatus(e.Status) ? (e.RequiredForConclusion ? 0 : 1) : 0)
            .ThenBy(e => (e.ReadyAt ?? e.ArrivedAt ?? e.OrderedAt ?? DateTime.MaxValue).Ticks)
            .ThenBy(e => e.QueueNumber)
            .ThenBy(e => e.QueueEntryId > 0 ? e.QueueEntryId : (e.OrderId ?? long.MaxValue))
            .ToList();

        var ranked = new List<DiagnosticQueueEntryItemDto>(sorted.Count);
        var nextServingOrder = 1;
        var activeAheadCount = 0;

        foreach (var item in sorted)
        {
            var isActiveQueueCandidate =
                item.Status is "IN_PROGRESS" or "CALLED" ||
                (item.Status is "READY" or "WAITING" or "RESERVED" && item.PresenceStatus != "LEFT");

            var servingOrder = isActiveQueueCandidate ? nextServingOrder++ : 0;
            var waitMinutes = item.Status is "READY" or "WAITING" or "RESERVED" && item.PresenceStatus != "LEFT"
                ? activeAheadCount * 10
                : 0;

            if (item.Status is "IN_PROGRESS" or "CALLED" ||
                (item.Status is "READY" or "WAITING" && item.PresenceStatus == "PRESENT"))
            {
                activeAheadCount++;
            }

            ranked.Add(item with
            {
                ServingOrder = servingOrder,
                EstimatedWaitMinutes = waitMinutes,
                EstimatedWaitLabel = waitMinutes <= 0 ? "Đến lượt / 0 phút" : $"~{waitMinutes} phút",
            });
        }

        return ranked;
    }

    private static List<DiagnosticQueueEntryItemDto> SortEntriesGlobal(
        IReadOnlyList<DiagnosticQueueEntryItemDto> entries)
    {
        return entries
            .OrderBy(e => GetQueueStatusRank(e.Status))
            .ThenBy(e => e.ServingOrder > 0 ? e.ServingOrder : int.MaxValue)
            .ThenBy(e => GetOrderPrioritySortRank(e.Priority))
            .ThenBy(e => GetClinicalPrioritySortRank(e.ClinicalPriority))
            .ThenBy(e => e.RequiredForConclusion ? 0 : 1)
            .ThenBy(e => (e.ReadyAt ?? e.ArrivedAt ?? e.OrderedAt ?? DateTime.MaxValue).Ticks)
            .ThenBy(e => e.QueueNumber)
            .ThenBy(e => e.QueueEntryId > 0 ? e.QueueEntryId : (e.OrderId ?? long.MaxValue))
            .ToList();
    }

    private static bool IsWaitingOrReadyStatus(string status)
        => status is "READY" or "WAITING" or "RESERVED";

    private static bool IsActiveSortableStatus(string status)
        => status is "IN_PROGRESS" or "CALLED" or "READY" or "WAITING" or "RESERVED";

    private static int GetQueueStatusRank(string status) => status switch
    {
        "IN_PROGRESS" => 0,
        "CALLED" => 1,
        "READY" => 2,
        "WAITING" => 3,
        "RESERVED" => 4,
        "SKIPPED" => 5,
        "COMPLETED" => 6,
        "CANCELLED" => 7,
        _ => 8,
    };

    private static int GetOrderPrioritySortRank(string priority) => priority switch
    {
        "STAT" => 0,
        "URGENT" => 1,
        _ => 2,
    };

    private static int GetClinicalPrioritySortRank(string clinicalPriority) => clinicalPriority switch
    {
        "PREGNANT" => 0,
        "ELDERLY" => 1,
        "EMERGENCY" => 0,
        _ => 2,
    };

    private static (bool IsEligible, string? IneligibleReason) EvaluateQueueEntryEligibility(
        string queueStatus,
        string presenceStatus,
        string orderStatus,
        string visitStatus,
        bool isServicePointActive,
        bool isRoomActive)
    {
        if (IsTerminalVisitStatus(visitStatus))
        {
            return (false, "Lượt khám đã hoàn tất, đã hủy hoặc vắng mặt (NO_SHOW).");
        }

        if (orderStatus == "CANCELLED")
        {
            return (false, "Phiếu chỉ định cận lâm sàng đã bị hủy.");
        }

        if (orderStatus == "ORDERED")
        {
            return (false, "Phiếu chỉ định cận lâm sàng chưa được xác nhận (ORDERED).");
        }

        if (orderStatus == "COMPLETED" || queueStatus == "COMPLETED")
        {
            return (false, "Lượt cận lâm sàng đã hoàn tất (COMPLETED).");
        }

        if (queueStatus == "CANCELLED")
        {
            return (false, "Lượt xếp hàng cận lâm sàng đã bị hủy.");
        }

        if (!isServicePointActive)
        {
            return (false, "Điểm phục vụ cận lâm sàng hiện đang ngừng hoạt động.");
        }

        if (!isRoomActive)
        {
            return (false, "Phòng thực hiện cận lâm sàng hiện đang ngừng hoạt động.");
        }

        if (queueStatus == "SKIPPED")
        {
            return (false, "Bệnh nhân đang ở trạng thái Qua lượt (SKIPPED). Cần khôi phục lượt trước khi gọi.");
        }

        if (presenceStatus != "PRESENT")
        {
            return (false, "Bệnh nhân chưa có mặt tại điểm phục vụ cận lâm sàng.");
        }

        if (queueStatus is not ("WAITING" or "READY"))
        {
            return (false, $"Lượt xếp hàng đang ở trạng thái {queueStatus}.");
        }

        return (true, null);
    }

    private static IReadOnlyList<string> GetAllowedTargetStatusesForEntry(
        string currentQueueStatus,
        string orderStatus,
        string visitStatus)
    {
        if (IsTerminalVisitStatus(visitStatus))
        {
            return [];
        }

        if (!AllowedTransitions.TryGetValue(currentQueueStatus, out var targets) || targets.Length == 0)
        {
            return [];
        }

        if (orderStatus == "CANCELLED")
        {
            return targets.Where(t => t == "CANCELLED").ToList();
        }

        if (orderStatus == "ORDERED")
        {
            return targets
                .Where(t => t is "WAITING" or "READY" or "SKIPPED" or "CANCELLED")
                .ToList();
        }

        return targets;
    }

    private static IReadOnlyList<DiagnosticQueueItemActionDto> BuildEntryActions(
        bool canReceive,
        bool canCall,
        bool canStart,
        bool canComplete,
        bool canSkip,
        bool canRestore,
        bool canCancel,
        string? ineligibleReason)
    {
        return
        [
            new DiagnosticQueueItemActionDto(
                Id: "receive",
                Label: "Tiếp nhận vào hàng đợi",
                UcCode: "UC40",
                Enabled: canReceive,
                DisabledReason: canReceive ? null : "Bệnh nhân đã có mặt trong hàng đợi."),
            new DiagnosticQueueItemActionDto(
                Id: "call",
                Label: "Gọi bệnh nhân",
                UcCode: "UC41",
                Enabled: canCall,
                DisabledReason: canCall ? null : ineligibleReason),
            new DiagnosticQueueItemActionDto(
                Id: "start",
                Label: "Bắt đầu thực hiện",
                UcCode: "UC41",
                Enabled: canStart,
                DisabledReason: canStart ? null : ineligibleReason),
            new DiagnosticQueueItemActionDto(
                Id: "complete",
                Label: "Hoàn tất kỹ thuật",
                UcCode: "UC41",
                Enabled: canComplete,
                DisabledReason: canComplete ? null : "Chỉ hoàn tất khi đang thực hiện kỹ thuật (IN_PROGRESS)."),
            new DiagnosticQueueItemActionDto(
                Id: "missed-turn",
                Label: "Đánh dấu qua lượt",
                UcCode: "UC41",
                Enabled: canSkip,
                DisabledReason: canSkip ? null : "Không thể đánh dấu qua lượt ở trạng thái hiện tại."),
            new DiagnosticQueueItemActionDto(
                Id: "restore-turn",
                Label: "Khôi phục lượt chờ",
                UcCode: "UC41",
                Enabled: canRestore,
                DisabledReason: canRestore ? null : "Chỉ khôi phục cho lượt đang ở trạng thái SKIPPED."),
            new DiagnosticQueueItemActionDto(
                Id: "cancel",
                Label: "Hủy lượt xếp hàng",
                UcCode: "UC41",
                Enabled: canCancel,
                DisabledReason: canCancel ? null : "Không thể hủy lượt đang thực hiện hoặc đã hoàn tất."),
        ];
    }

    private static DiagnosticQueueEntryItemDto ResolveEntryDtoFromDataset(
        string idOrCode,
        long hospitalId,
        HospitalDiagnosticQueueDataset dataset)
    {
        var trimmed = idOrCode.Trim();
        if (trimmed.StartsWith('#'))
        {
            trimmed = trimmed[1..].Trim();
        }

        // 1. Direct match in hospital diagnostic queue entries
        var directMatch = dataset.AllSortedEntries.FirstOrDefault(e => MatchesQueueEntryItem(e, trimmed));
        if (directMatch is not null)
        {
            return directMatch;
        }

        // 2. Check if it matches an unconfirmed ORDERED order in the same hospital
        var hospitalOrder = dataset.AllOrders.FirstOrDefault(o =>
            !dataset.ForeignOrders.Any(f => f.Id == o.Id) &&
            MatchesRawOrderIdentifier(o, trimmed));
        if (hospitalOrder is not null)
        {
            var matchedByOrder = dataset.AllSortedEntries.FirstOrDefault(e => e.OrderId == hospitalOrder.Id);
            if (matchedByOrder is not null)
            {
                return matchedByOrder;
            }

            if (NormalizeOrderStatus(hospitalOrder.Status) == "ORDERED")
            {
                throw new ConflictException(
                    "Phiếu chỉ định cận lâm sàng chưa được xác nhận (ORDERED). Vui lòng xác nhận chỉ định trước khi gọi hoặc thực hiện.");
            }
        }

        // 3. Check if it matches a foreign hospital QueueEntry or DiagnosticOrder -> 403 Forbidden
        var foreignQeMatch = dataset.ForeignQueueEntries.FirstOrDefault(q => MatchesRawQueueEntryIdentifier(q, trimmed));
        if (foreignQeMatch is not null)
        {
            throw new ForbiddenException("Lượt xếp hàng cận lâm sàng không thuộc cơ sở y tế của nhân viên cận lâm sàng.");
        }

        var foreignOrderMatch = dataset.ForeignOrders.FirstOrDefault(o => MatchesRawOrderIdentifier(o, trimmed));
        if (foreignOrderMatch is not null)
        {
            throw new ForbiddenException("Lượt xếp hàng cận lâm sàng không thuộc cơ sở y tế của nhân viên cận lâm sàng.");
        }

        // 4. Check if it matches a non-diagnostic QueueEntry in the same hospital -> 409 Conflict
        var nonDiagMatch = dataset.NonDiagnosticHospitalQueueEntries.FirstOrDefault(q => MatchesRawQueueEntryIdentifier(q, trimmed));
        if (nonDiagMatch is not null)
        {
            throw new ConflictException("Lượt xếp hàng này không thuộc hàng đợi cận lâm sàng (DIAGNOSTIC).");
        }

        throw new NotFoundException("DiagnosticQueueEntry", idOrCode);
    }

    private static QueueServicePoint ResolveTargetDiagnosticServicePoint(
        string servicePointInput,
        long hospitalId,
        HospitalDiagnosticQueueDataset dataset)
    {
        var trimmed = servicePointInput.Trim();

        long? parsedSpId = null;
        if (long.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var numericId))
        {
            parsedSpId = numericId;
        }
        else if (trimmed.StartsWith("sp-", StringComparison.OrdinalIgnoreCase) &&
                 long.TryParse(trimmed[3..], NumberStyles.None, CultureInfo.InvariantCulture, out var spNum))
        {
            parsedSpId = spNum;
        }

        if (parsedSpId.HasValue)
        {
            if (dataset.AllServicePointsById.TryGetValue(parsedSpId.Value, out var sp))
            {
                var spHospId = dataset.ResolveServicePointHospitalId(sp);
                if (spHospId.HasValue && spHospId.Value != hospitalId)
                {
                    throw new ForbiddenException("Điểm phục vụ cận lâm sàng không thuộc cơ sở y tế của nhân viên cận lâm sàng.");
                }

                if (!string.Equals(sp.PointType?.Trim(), "DIAGNOSTIC", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ConflictException("Điểm phục vụ này không phải là điểm phục vụ cận lâm sàng (DIAGNOSTIC).");
                }

                return sp;
            }

            throw new NotFoundException("DiagnosticServicePoint", servicePointInput);
        }

        // Try matching by RoomCode / RoomId / ServicePoint Name
        long? parsedRoomId = null;
        if (trimmed.StartsWith("rm-", StringComparison.OrdinalIgnoreCase) &&
            long.TryParse(trimmed[3..], NumberStyles.None, CultureInfo.InvariantCulture, out var rmNum))
        {
            parsedRoomId = rmNum;
        }

        var hospitalMatch = dataset.HospitalDiagnosticServicePoints.FirstOrDefault(sp =>
            string.Equals(sp.Name?.Trim(), trimmed, StringComparison.OrdinalIgnoreCase) ||
            (parsedRoomId.HasValue && sp.RoomId == parsedRoomId.Value) ||
            (sp.RoomId.HasValue &&
             dataset.RoomsById.TryGetValue(sp.RoomId.Value, out var rm) &&
             string.Equals(rm.RoomCode?.Trim(), trimmed, StringComparison.OrdinalIgnoreCase)));

        if (hospitalMatch is not null)
        {
            return hospitalMatch;
        }

        // Check if matched a foreign or non-diagnostic service point by name/room
        foreach (var sp in dataset.AllServicePointsById.Values)
        {
            dataset.RoomsById.TryGetValue(sp.RoomId ?? 0, out var rm);
            var isNameOrRoomMatch =
                string.Equals(sp.Name?.Trim(), trimmed, StringComparison.OrdinalIgnoreCase) ||
                (parsedRoomId.HasValue && sp.RoomId == parsedRoomId.Value) ||
                (rm is not null && string.Equals(rm.RoomCode?.Trim(), trimmed, StringComparison.OrdinalIgnoreCase));

            if (!isNameOrRoomMatch)
            {
                continue;
            }

            var spHospId = dataset.ResolveServicePointHospitalId(sp);
            if (spHospId.HasValue && spHospId.Value != hospitalId)
            {
                throw new ForbiddenException("Điểm phục vụ cận lâm sàng không thuộc cơ sở y tế của nhân viên cận lâm sàng.");
            }

            if (!string.Equals(sp.PointType?.Trim(), "DIAGNOSTIC", StringComparison.OrdinalIgnoreCase))
            {
                throw new ConflictException("Điểm phục vụ này không phải là điểm phục vụ cận lâm sàng (DIAGNOSTIC).");
            }
        }

        throw new NotFoundException("DiagnosticServicePoint", servicePointInput);
    }

    private static bool MatchesQueueEntryItem(DiagnosticQueueEntryItemDto item, string identifier)
    {
        if (long.TryParse(identifier, NumberStyles.None, CultureInfo.InvariantCulture, out var numericId))
        {
            return item.QueueEntryId == numericId || (item.QueueEntryId == 0 && item.OrderId == numericId);
        }

        if (identifier.StartsWith("q-", StringComparison.OrdinalIgnoreCase) &&
            long.TryParse(identifier[2..], NumberStyles.None, CultureInfo.InvariantCulture, out var qId))
        {
            return item.QueueEntryId == qId;
        }

        if (identifier.StartsWith("qe-", StringComparison.OrdinalIgnoreCase) &&
            long.TryParse(identifier[3..], NumberStyles.None, CultureInfo.InvariantCulture, out var qeId))
        {
            return item.QueueEntryId == qeId;
        }

        if (identifier.StartsWith("do-", StringComparison.OrdinalIgnoreCase) &&
            long.TryParse(identifier[3..], NumberStyles.None, CultureInfo.InvariantCulture, out var doId))
        {
            return item.OrderId == doId;
        }

        if (identifier.StartsWith("C", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(identifier[1..], NumberStyles.None, CultureInfo.InvariantCulture, out var cTicketNum))
        {
            return item.QueueNumber == cTicketNum;
        }

        return string.Equals(item.Id, identifier, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(item.FormattedQueueEntryId, identifier, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(item.TicketNo, identifier, StringComparison.OrdinalIgnoreCase) ||
               (item.OrderCode is not null && string.Equals(item.OrderCode, identifier, StringComparison.OrdinalIgnoreCase)) ||
               (item.OrderId.HasValue &&
                item.OrderedAt.HasValue &&
                string.Equals($"CLS-{item.OrderedAt.Value:yyyy}-{item.OrderId.Value:D4}", identifier, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesRawQueueEntryIdentifier(QueueEntry qe, string identifier)
    {
        if (long.TryParse(identifier, NumberStyles.None, CultureInfo.InvariantCulture, out var numericId))
        {
            return qe.Id == numericId;
        }

        if (identifier.StartsWith("q-", StringComparison.OrdinalIgnoreCase) &&
            long.TryParse(identifier[2..], NumberStyles.None, CultureInfo.InvariantCulture, out var qId))
        {
            return qe.Id == qId;
        }

        if (identifier.StartsWith("qe-", StringComparison.OrdinalIgnoreCase) &&
            long.TryParse(identifier[3..], NumberStyles.None, CultureInfo.InvariantCulture, out var qeId))
        {
            return qe.Id == qeId;
        }

        return false;
    }

    private static bool MatchesRawOrderIdentifier(DiagnosticOrder order, string identifier)
    {
        if (long.TryParse(identifier, NumberStyles.None, CultureInfo.InvariantCulture, out var numericId))
        {
            return order.Id == numericId;
        }

        if (identifier.StartsWith("do-", StringComparison.OrdinalIgnoreCase) &&
            long.TryParse(identifier[3..], NumberStyles.None, CultureInfo.InvariantCulture, out var doId))
        {
            return order.Id == doId;
        }

        var orderDate = order.OrderedAt.HasValue ? DateOnly.FromDateTime(order.OrderedAt.Value) : DateOnly.FromDateTime(DateTime.UtcNow);
        var formatted = FormatOrderCode(order, orderDate);
        var clsCode = $"CLS-{orderDate:yyyy}-{order.Id:D4}";

        return string.Equals(order.OrderCode?.Trim(), identifier, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(formatted, identifier, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(clsCode, identifier, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<DiagnosticServicePointQueueBucketDto> FilterServicePointBuckets(
        IReadOnlyList<DiagnosticServicePointQueueBucketDto> buckets,
        ValidatedGetQueueFilter filter)
    {
        IEnumerable<DiagnosticServicePointQueueBucketDto> query = buckets;

        if (filter.ServicePointId is not null)
        {
            query = query.Where(b => MatchesBucketServicePointFilter(b, filter.ServicePointId));
        }

        if (filter.RoomId is not null)
        {
            query = query.Where(b => MatchesBucketRoomFilter(b, filter.RoomId));
        }

        if (filter.Category is not null)
        {
            query = query.Where(b =>
                string.Equals(b.Category, filter.Category, StringComparison.OrdinalIgnoreCase) ||
                b.Entries.Any(e => string.Equals(e.Category, filter.Category, StringComparison.OrdinalIgnoreCase)));
        }

        return query.ToList();
    }

    private static List<DiagnosticQueueEntryItemDto> ApplyEntryFilters(
        IReadOnlyList<DiagnosticQueueEntryItemDto> entries,
        ValidatedGetQueueFilter filter)
    {
        IEnumerable<DiagnosticQueueEntryItemDto> query = entries;

        if (filter.ServicePointId is not null)
        {
            query = query.Where(e => MatchesEntryServicePointFilter(e, filter.ServicePointId));
        }

        if (filter.RoomId is not null)
        {
            query = query.Where(e => MatchesEntryRoomFilter(e, filter.RoomId));
        }

        if (filter.ServiceId is not null)
        {
            query = query.Where(e => MatchesEntryServiceFilter(e, filter.ServiceId));
        }

        if (filter.Category is not null)
        {
            query = query.Where(e => string.Equals(e.Category, filter.Category, StringComparison.OrdinalIgnoreCase));
        }

        if (filter.QueueStatus is not null)
        {
            query = query.Where(e => string.Equals(e.Status, filter.QueueStatus, StringComparison.OrdinalIgnoreCase));
        }

        if (filter.OrderStatus is not null)
        {
            query = query.Where(e => string.Equals(e.OrderStatus, filter.OrderStatus, StringComparison.OrdinalIgnoreCase));
        }

        if (filter.Priority is not null)
        {
            query = query.Where(e =>
                string.Equals(e.Priority, filter.Priority, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(e.ClinicalPriority, filter.Priority, StringComparison.OrdinalIgnoreCase));
        }

        if (filter.PresenceStatus is not null)
        {
            query = query.Where(e => string.Equals(e.PresenceStatus, filter.PresenceStatus, StringComparison.OrdinalIgnoreCase));
        }

        if (filter.VisitId is not null)
        {
            query = query.Where(e => MatchesEntryVisitFilter(e, filter.VisitId));
        }

        if (filter.PatientId is not null)
        {
            query = query.Where(e => MatchesEntryPatientFilter(e, filter.PatientId));
        }

        if (filter.RequiredForConclusion.HasValue)
        {
            query = query.Where(e => e.RequiredForConclusion == filter.RequiredForConclusion.Value);
        }

        if (filter.ActiveOnly)
        {
            query = query.Where(e => e.Status is "RESERVED" or "WAITING" or "READY" or "CALLED" or "IN_PROGRESS");
        }

        if (filter.Keyword is not null)
        {
            var kw = filter.Keyword;
            query = query.Where(e =>
                e.TicketNo.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                e.FormattedQueueEntryId.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                (e.OrderCode is not null && e.OrderCode.Contains(kw, StringComparison.OrdinalIgnoreCase)) ||
                e.PatientName.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                e.PatientCode.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                e.VisitCode.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                e.ServiceName.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                e.ServiceCode.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                e.RoomCode.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                e.RoomName.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                e.ServicePointName.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                e.OrderingDoctorName.Contains(kw, StringComparison.OrdinalIgnoreCase));
        }

        return query.ToList();
    }

    private static bool MatchesBucketServicePointFilter(DiagnosticServicePointQueueBucketDto bucket, string filter)
    {
        if (long.TryParse(filter, NumberStyles.None, CultureInfo.InvariantCulture, out var numericId))
        {
            return bucket.ServicePointId == numericId;
        }

        if (filter.StartsWith("sp-", StringComparison.OrdinalIgnoreCase) &&
            long.TryParse(filter[3..], NumberStyles.None, CultureInfo.InvariantCulture, out var spNumericId))
        {
            return bucket.ServicePointId == spNumericId;
        }

        return string.Equals(bucket.FormattedServicePointId, filter, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(bucket.ServicePointName, filter, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesBucketRoomFilter(DiagnosticServicePointQueueBucketDto bucket, string filter)
    {
        if (long.TryParse(filter, NumberStyles.None, CultureInfo.InvariantCulture, out var numericId))
        {
            return bucket.RoomId == numericId;
        }

        if (filter.StartsWith("rm-", StringComparison.OrdinalIgnoreCase) &&
            long.TryParse(filter[3..], NumberStyles.None, CultureInfo.InvariantCulture, out var rmNumericId))
        {
            return bucket.RoomId == rmNumericId;
        }

        return string.Equals(bucket.RoomCode, filter, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(bucket.RoomName, filter, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesEntryServicePointFilter(DiagnosticQueueEntryItemDto item, string filter)
    {
        if (long.TryParse(filter, NumberStyles.None, CultureInfo.InvariantCulture, out var numericId))
        {
            return item.ServicePointId == numericId;
        }

        if (filter.StartsWith("sp-", StringComparison.OrdinalIgnoreCase) &&
            long.TryParse(filter[3..], NumberStyles.None, CultureInfo.InvariantCulture, out var spNumericId))
        {
            return item.ServicePointId == spNumericId;
        }

        return string.Equals(item.FormattedServicePointId, filter, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(item.ServicePointName, filter, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesEntryRoomFilter(DiagnosticQueueEntryItemDto item, string filter)
    {
        if (long.TryParse(filter, NumberStyles.None, CultureInfo.InvariantCulture, out var numericId))
        {
            return item.RoomId == numericId;
        }

        if (filter.StartsWith("rm-", StringComparison.OrdinalIgnoreCase) &&
            long.TryParse(filter[3..], NumberStyles.None, CultureInfo.InvariantCulture, out var rmNumericId))
        {
            return item.RoomId == rmNumericId;
        }

        return string.Equals(item.RoomCode, filter, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(item.RoomName, filter, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesEntryServiceFilter(DiagnosticQueueEntryItemDto item, string filter)
    {
        if (long.TryParse(filter, NumberStyles.None, CultureInfo.InvariantCulture, out var numericId))
        {
            return item.ServiceId == numericId;
        }

        if (filter.StartsWith("srv-", StringComparison.OrdinalIgnoreCase) &&
            long.TryParse(filter[4..], NumberStyles.None, CultureInfo.InvariantCulture, out var srvNumericId))
        {
            return item.ServiceId == srvNumericId;
        }

        return string.Equals(item.ServiceCode, filter, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesEntryVisitFilter(DiagnosticQueueEntryItemDto item, string filter)
    {
        if (long.TryParse(filter, NumberStyles.None, CultureInfo.InvariantCulture, out var numericId))
        {
            return item.VisitId == numericId;
        }

        if (filter.StartsWith("vis-", StringComparison.OrdinalIgnoreCase) &&
            long.TryParse(filter[4..], NumberStyles.None, CultureInfo.InvariantCulture, out var visNumericId))
        {
            return item.VisitId == visNumericId;
        }

        return string.Equals(item.VisitCode, filter, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesEntryPatientFilter(DiagnosticQueueEntryItemDto item, string filter)
    {
        if (long.TryParse(filter, NumberStyles.None, CultureInfo.InvariantCulture, out var numericId))
        {
            return item.PatientId == numericId;
        }

        if (filter.StartsWith("pat-", StringComparison.OrdinalIgnoreCase) &&
            long.TryParse(filter[4..], NumberStyles.None, CultureInfo.InvariantCulture, out var patNumericId))
        {
            return item.PatientId == patNumericId;
        }

        return string.Equals(item.PatientCode, filter, StringComparison.OrdinalIgnoreCase);
    }

    private static DiagnosticQueueSummaryStatsDto BuildSummaryStats(
        IReadOnlyList<DiagnosticQueueEntryItemDto> entries,
        IReadOnlyList<DiagnosticServicePointQueueBucketDto> buckets)
    {
        return new DiagnosticQueueSummaryStatsDto(
            TotalEntries: entries.Count,
            WaitingCount: entries.Count(e => e.Status is "WAITING" or "RESERVED"),
            ReadyCount: entries.Count(e => e.Status == "READY"),
            CalledCount: entries.Count(e => e.Status == "CALLED"),
            InProgressCount: entries.Count(e => e.Status == "IN_PROGRESS"),
            CompletedCount: entries.Count(e => e.Status == "COMPLETED"),
            SkippedCount: entries.Count(e => e.Status == "SKIPPED"),
            CancelledCount: entries.Count(e => e.Status == "CANCELLED"),
            UrgentOrStatCount: entries.Count(e => e.Priority is "URGENT" or "STAT" && e.Status != "CANCELLED"),
            MandatoryCount: entries.Count(e => e.RequiredForConclusion && e.Status != "CANCELLED"),
            ActiveServicePointsCount: buckets.Count(b => b.IsActive),
            BusyServicePointsCount: buckets.Count(b => b.CalledCount > 0 || b.InProgressCount > 0),
            LaboratoryCount: entries.Count(e => e.Category == "LABORATORY"),
            ImagingCount: entries.Count(e => e.Category == "IMAGING"),
            FunctionalCount: entries.Count(e => e.Category == "FUNCTIONAL"));
    }

    private static IReadOnlyList<DiagnosticServicePointOptionDto> BuildAvailableServicePoints(
        IReadOnlyList<QueueServicePoint> hospitalDiagPoints,
        IReadOnlyDictionary<long, HospitalRoom> roomsById)
    {
        return hospitalDiagPoints
            .Select(sp =>
            {
                roomsById.TryGetValue(sp.RoomId ?? 0, out var rm);
                var isRoomActive = rm is null || string.Equals(rm.Status?.Trim(), "ACTIVE", StringComparison.OrdinalIgnoreCase);
                return new DiagnosticServicePointOptionDto(
                    ServicePointId: sp.Id,
                    Name: sp.Name,
                    ServiceId: sp.ServiceId,
                    RoomId: rm?.Id ?? sp.RoomId,
                    RoomCode: rm?.RoomCode,
                    RoomName: rm?.Name,
                    RoomLocation: rm?.Location,
                    IsActive: sp.IsActive != false,
                    IsRoomActive: isRoomActive);
            })
            .ToList();
    }

    private static IReadOnlyList<DiagnosticQuickActionDto> BuildQuickActions(
        IReadOnlyCollection<string> permissionCodes)
    {
        var permissionSet = new HashSet<string>(permissionCodes, StringComparer.OrdinalIgnoreCase);
        var hasQueueManage = permissionSet.Contains("QUEUE_MANAGE");
        var hasResultWrite = permissionSet.Contains("DIAGNOSTIC_RESULT_WRITE");
        var hasDiagnosticOrder = permissionSet.Contains("DIAGNOSTIC_ORDER");

        var actions = new List<DiagnosticQuickActionDto>();

        if (hasQueueManage || hasResultWrite || hasDiagnosticOrder)
        {
            actions.Add(new DiagnosticQuickActionDto(
                Id: "confirm-order",
                Label: "Xác nhận chỉ định CLS",
                Description: "Kiểm tra và xác nhận phiếu chỉ định mới từ bác sĩ",
                Icon: "M9 5H7a2 2 0 00-2 2v12a2 2 0 002 2h10a2 2 0 002-2V7a2 2 0 00-2-2h-2M9 5a2 2 0 002 2h2a2 2 0 002-2M9 5a2 2 0 012-2h2a2 2 0 012 2m-6 9l2 2 4-4",
                Accent: "sky",
                UcCode: "UC39"));
        }

        if (hasQueueManage || hasResultWrite)
        {
            actions.Add(new DiagnosticQuickActionDto(
                Id: "receive-patient",
                Label: "Tiếp nhận bệnh nhân CLS",
                Description: "Quét mã lượt khám và xếp vào hàng đợi phòng kỹ thuật",
                Icon: "M13 5l7 7-7 7M5 5l7 7-7 7",
                Accent: "emerald",
                UcCode: "UC40"));

            actions.Add(new DiagnosticQuickActionDto(
                Id: "manage-queue",
                Label: "Điều phối hàng đợi CLS",
                Description: "Gọi số tiếp theo, đánh dấu qua lượt hoặc khôi phục lượt chờ",
                Icon: "M4 6h16M4 10h16M4 14h16M4 18h16",
                Accent: "indigo",
                UcCode: "UC41"));

            actions.Add(new DiagnosticQuickActionDto(
                Id: "update-execution",
                Label: "Cập nhật thực hiện kỹ thuật",
                Description: "Ghi nhận trạng thái lấy mẫu, chụp chiếu hoặc thăm dò",
                Icon: "M19.428 15.428a2 2 0 00-1.022-.547l-2.387-.477a6 6 0 00-3.86.517l-.318.158a6 6 0 01-3.86.517L6.05 15.21a2 2 0 00-1.806.547M8 4h8l-1 1v5.172a2 2 0 00.586 1.414l5 5c1.26 1.26.367 3.414-1.415 3.414H4.828c-1.782 0-2.674-2.154-1.414-3.414l5-5A2 2 0 009 10.172V5L8 4z",
                Accent: "amber",
                UcCode: "UC42"));
        }

        if (hasResultWrite)
        {
            actions.Add(new DiagnosticQuickActionDto(
                Id: "enter-result",
                Label: "Nhập & trả kết quả CLS",
                Description: "Nhập chỉ số, kết luận và chuyển kết quả về phòng khám",
                Icon: "M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z",
                Accent: "rose",
                UcCode: "UC43"));
        }

        return actions;
    }

    private async Task<DiagnosticOrderClinicalSummaryDto> BuildClinicalSummaryAsync(
        Visit? visit,
        long visitId,
        string visitCode,
        string visitStatus,
        CancellationToken ct)
    {
        if (visit is null || visitId <= 0)
        {
            return new DiagnosticOrderClinicalSummaryDto(
                VisitId: visitId,
                VisitCode: visitCode,
                VisitStatus: visitStatus,
                ReasonForVisit: null,
                PresentingComplaint: null,
                ClinicalFindings: null,
                PrimaryDiagnosisCode: null,
                PrimaryDiagnosisName: null,
                ClinicalDiagnosis: null);
        }

        MedicalRecordRevision? latestRevision = null;
        try
        {
            var record = await _db.MedicalRecords
                .AsNoTracking()
                .Where(m => m.VisitId == visit.Id)
                .OrderByDescending(m => m.Id)
                .FirstOrDefaultAsync(ct);

            if (record is not null)
            {
                latestRevision = await _db.MedicalRecordRevisions
                    .AsNoTracking()
                    .Where(r => r.MedicalRecordId == record.Id)
                    .OrderByDescending(r => r.RevisionNo)
                    .ThenByDescending(r => r.Id)
                    .FirstOrDefaultAsync(ct);
            }
        }
        catch (NotSupportedException)
        {
            latestRevision = null;
        }

        VisitDiagnosis? primaryDx = null;
        try
        {
            primaryDx = await _db.VisitDiagnoses
                .AsNoTracking()
                .Where(d => d.VisitId == visit.Id)
                .OrderByDescending(d => d.IsPrimary == true)
                .ThenByDescending(d => d.RecordedAt)
                .ThenByDescending(d => d.Id)
                .FirstOrDefaultAsync(ct);
        }
        catch (NotSupportedException)
        {
            primaryDx = null;
        }

        return new DiagnosticOrderClinicalSummaryDto(
            VisitId: visit.Id,
            VisitCode: visitCode,
            VisitStatus: visitStatus,
            ReasonForVisit: visit.ReasonForVisit,
            PresentingComplaint: latestRevision?.PresentingComplaint ?? visit.ReasonForVisit,
            ClinicalFindings: latestRevision?.ClinicalFindings,
            PrimaryDiagnosisCode: primaryDx?.CodeSnapshot,
            PrimaryDiagnosisName: primaryDx?.NameSnapshot,
            ClinicalDiagnosis: primaryDx?.ClinicalDiagnosis ?? latestRevision?.ClinicalConclusion);
    }

    private async Task<IReadOnlyList<QueueStatusTransitionHistoryItemDto>> LoadStatusHistoryAsync(
        long queueEntryId,
        CancellationToken ct)
    {
        var queueEntryIdStr = queueEntryId.ToString(CultureInfo.InvariantCulture);
        List<AuditLog> logs;
        try
        {
            logs = await _db.AuditLogs
                .AsNoTracking()
                .Where(a =>
                    a.EntityId == queueEntryIdStr &&
                    (a.Action == DiagnosticQueueCallNextAction ||
                     a.Action == DiagnosticQueueStatusUpdatedAction ||
                     a.Action == DiagnosticQueueMissedTurnAction ||
                     a.Action == DiagnosticQueueTurnRestoredAction ||
                     a.Action == DiagnosticQueueEntryCancelledAction ||
                     a.Action == QueueStatusService.QueueStatusUpdateAction ||
                     a.Action == QueueStatusService.QueueEntryCancelAction))
                .OrderByDescending(a => a.OccurredAt)
                .ThenByDescending(a => a.Id)
                .ToListAsync(ct);
        }
        catch (NotSupportedException)
        {
            return [];
        }

        var history = new List<QueueStatusTransitionHistoryItemDto>(logs.Count);
        foreach (var log in logs)
        {
            DiagnosticQueueAuditPayload? payload = null;
            if (!string.IsNullOrWhiteSpace(log.ChangeData))
            {
                try
                {
                    payload = JsonSerializer.Deserialize<DiagnosticQueueAuditPayload>(log.ChangeData, AuditJsonOptions);
                }
                catch (JsonException)
                {
                    payload = null;
                }
            }

            history.Add(new QueueStatusTransitionHistoryItemDto(
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

        return history;
    }

    private async Task<DiagnosticStaffAccessContext> EnsureDiagnosticStaffAccessAsync(
        long actorUserId,
        DateTime nowUtc,
        bool requireManagePermission,
        CancellationToken ct)
    {
        if (actorUserId <= 0)
        {
            throw new UnauthorizedAccessException("Thông tin định danh người dùng không hợp lệ.");
        }

        var actorUser = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == actorUserId, ct)
            ?? throw new NotFoundException("User", actorUserId);

        if (actorUser.AccountStatus != AccountStatus.ACTIVE)
        {
            throw new ForbiddenException("Tài khoản nhân viên cận lâm sàng không ở trạng thái hoạt động.");
        }

        if (actorUser.LockedUntil.HasValue && actorUser.LockedUntil.Value > nowUtc)
        {
            throw new ForbiddenException("Tài khoản nhân viên cận lâm sàng đang bị tạm khóa.");
        }

        var allRoles = await _db.Roles
            .AsNoTracking()
            .OrderBy(r => r.Id)
            .ToListAsync(ct);

        var actorRoleIds = (await _db.UserRoles
            .AsNoTracking()
            .Where(ur => ur.UserId == actorUserId)
            .Select(ur => ur.RoleId)
            .ToListAsync(ct))
            .ToHashSet();

        var actorRoleCodes = allRoles
            .Where(r => actorRoleIds.Contains(r.Id))
            .Select(r => r.Code)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();

        if (!actorRoleCodes.Any(c => string.Equals(c, DiagnosticStaffRoleCode, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ForbiddenException("Người dùng không có quyền thực hiện nghiệp vụ Cận lâm sàng.");
        }

        var actorStaff = await _db.Staff
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == actorUserId, ct);

        if (actorStaff is null || !actorStaff.HospitalId.HasValue)
        {
            throw new ForbiddenException("Tài khoản nhân viên cận lâm sàng chưa được gán cơ sở y tế.");
        }

        if (!string.Equals(actorStaff.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            throw new ForbiddenException("Hồ sơ nhân sự của nhân viên cận lâm sàng đã ngừng hoạt động.");
        }

        var hospitalId = actorStaff.HospitalId.Value;
        var hospital = await _db.Hospitals
            .AsNoTracking()
            .FirstOrDefaultAsync(h => h.Id == hospitalId, ct)
            ?? throw new NotFoundException("Hospital", hospitalId);

        if (hospital.IsActive == false)
        {
            throw new ForbiddenException("Cơ sở y tế được gán cho nhân viên cận lâm sàng hiện đang ngừng hoạt động.");
        }

        var departments = await _db.Departments
            .AsNoTracking()
            .Where(d => d.HospitalId == hospitalId)
            .OrderBy(d => d.Id)
            .ToListAsync(ct);

        var departmentsById = departments.ToDictionary(d => d.Id);

        Department? staffDept = null;
        if (actorStaff.DepartmentId.HasValue &&
            departmentsById.TryGetValue(actorStaff.DepartmentId.Value, out staffDept) &&
            staffDept.IsActive == false)
        {
            throw new ForbiddenException("Khoa cận lâm sàng được phân công hiện đang ngừng hoạt động.");
        }

        var departmentName = !string.IsNullOrWhiteSpace(staffDept?.Name)
            ? staffDept.Name
            : departments
                .FirstOrDefault(d => string.Equals(d.DepartmentCode, "DIA", StringComparison.OrdinalIgnoreCase))
                ?.Name ?? DefaultDepartmentName;

        var allPermissions = await _db.Permissions
            .AsNoTracking()
            .ToListAsync(ct);

        var allRolePermissions = await _db.RolePermissions
            .AsNoTracking()
            .ToListAsync(ct);

        var permissionsById = allPermissions.ToDictionary(p => p.Id, p => p.Code);

        var actorPermissionCodes = allRolePermissions
            .Where(rp => actorRoleIds.Contains(rp.RoleId) && permissionsById.ContainsKey(rp.PermissionId))
            .Select(rp => permissionsById[rp.PermissionId])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToList();

        if (actorPermissionCodes.Count == 0 && allRolePermissions.Count == 0)
        {
            actorPermissionCodes = [.. FallbackDiagnosticPermissions];
        }

        var hasReadPermission = actorPermissionCodes.Any(code =>
            AllowedReadPermissions.Any(allowed => string.Equals(code, allowed, StringComparison.OrdinalIgnoreCase)));

        if (!hasReadPermission)
        {
            throw new ForbiddenException("Nhân viên cận lâm sàng không có quyền truy cập hàng đợi cận lâm sàng.");
        }

        if (requireManagePermission)
        {
            var hasManagePermission = actorPermissionCodes.Any(code =>
                AllowedManagePermissions.Any(allowed => string.Equals(code, allowed, StringComparison.OrdinalIgnoreCase)));

            if (!hasManagePermission)
            {
                throw new ForbiddenException("Nhân viên cận lâm sàng không có quyền điều phối hàng đợi cận lâm sàng.");
            }
        }

        var fullName = !string.IsNullOrWhiteSpace(actorStaff.FullName)
            ? actorStaff.FullName
            : actorUser.Email;

        var staffProfile = new DiagnosticStaffProfileDto(
            UserId: actorUser.Id,
            Email: actorUser.Email,
            FullName: fullName,
            StaffCode: actorStaff.StaffCode,
            HospitalId: hospital.Id,
            HospitalCode: hospital.HospitalCode,
            HospitalName: hospital.Name,
            DepartmentId: actorStaff.DepartmentId,
            DepartmentName: departmentName,
            Roles: actorRoleCodes,
            Permissions: actorPermissionCodes);

        return new DiagnosticStaffAccessContext(
            Hospital: hospital,
            Staff: actorStaff,
            DepartmentName: departmentName,
            Profile: staffProfile);
    }

    private static ValidatedGetQueueFilter ValidateGetQueueRequest(GetDiagnosticQueueRequest? request)
    {
        var req = request ?? new GetDiagnosticQueueRequest();

        if (req.Limit is < 1 or > 100)
        {
            throw new ValidationException("Số lượng lượt xếp hàng cận lâm sàng hiển thị phải từ 1 đến 100.");
        }

        DateOnly? filterDate = null;
        if (!string.IsNullOrWhiteSpace(req.QueueDate) &&
            !string.Equals(req.QueueDate.Trim(), "ALL", StringComparison.OrdinalIgnoreCase))
        {
            if (!DateOnly.TryParseExact(
                    req.QueueDate.Trim(),
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var parsedDate))
            {
                throw new ValidationException("Ngày hàng đợi cận lâm sàng không hợp lệ (định dạng YYYY-MM-DD).");
            }

            filterDate = parsedDate;
        }

        string? category = null;
        if (!string.IsNullOrWhiteSpace(req.Category) &&
            !string.Equals(req.Category.Trim(), "ALL", StringComparison.OrdinalIgnoreCase))
        {
            category = req.Category.Trim().ToUpperInvariant();
            if (!AllowedCategories.Contains(category))
            {
                throw new ValidationException("Nhóm cận lâm sàng lọc không hợp lệ.");
            }
        }

        var rawQueueStatus = !string.IsNullOrWhiteSpace(req.QueueStatus)
            ? req.QueueStatus.Trim()
            : req.Status?.Trim();

        string? queueStatus = null;
        if (!string.IsNullOrWhiteSpace(rawQueueStatus) &&
            !string.Equals(rawQueueStatus, "ALL", StringComparison.OrdinalIgnoreCase))
        {
            queueStatus = NormalizeQueueStatus(rawQueueStatus);
            if (!AllowedQueueStatuses.Contains(queueStatus))
            {
                throw new ValidationException("Trạng thái hàng đợi cận lâm sàng lọc không hợp lệ.");
            }
        }

        string? orderStatus = null;
        if (!string.IsNullOrWhiteSpace(req.OrderStatus) &&
            !string.Equals(req.OrderStatus.Trim(), "ALL", StringComparison.OrdinalIgnoreCase))
        {
            orderStatus = NormalizeOrderStatus(req.OrderStatus.Trim());
            if (!AllowedOrderStatuses.Contains(orderStatus))
            {
                throw new ValidationException("Trạng thái chỉ định cận lâm sàng lọc không hợp lệ.");
            }
        }

        string? priority = null;
        if (!string.IsNullOrWhiteSpace(req.Priority) &&
            !string.Equals(req.Priority.Trim(), "ALL", StringComparison.OrdinalIgnoreCase))
        {
            priority = req.Priority.Trim().ToUpperInvariant();
            if (!AllowedPriorities.Contains(priority))
            {
                throw new ValidationException("Mức độ ưu tiên lọc không hợp lệ.");
            }
        }

        string? presenceStatus = null;
        if (!string.IsNullOrWhiteSpace(req.PresenceStatus) &&
            !string.Equals(req.PresenceStatus.Trim(), "ALL", StringComparison.OrdinalIgnoreCase))
        {
            presenceStatus = req.PresenceStatus.Trim().ToUpperInvariant();
            if (!AllowedPresenceStatuses.Contains(presenceStatus))
            {
                throw new ValidationException("Trạng thái hiện diện lọc không hợp lệ.");
            }
        }

        var spId = ValidateOptionalFilterToken(req.ServicePointId, "ServicePointId");
        var roomId = ValidateOptionalFilterToken(req.RoomId, "RoomId");
        var serviceId = ValidateOptionalFilterToken(req.ServiceId, "ServiceId");
        var visitId = ValidateOptionalFilterToken(req.VisitId, "VisitId");
        var patientId = ValidateOptionalFilterToken(req.PatientId, "PatientId");
        var keyword = ValidateOptionalFilterToken(req.Keyword, "Keyword");

        return new ValidatedGetQueueFilter(
            FilterDate: filterDate,
            ServicePointId: spId,
            RoomId: roomId,
            ServiceId: serviceId,
            Category: category,
            QueueStatus: queueStatus,
            OrderStatus: orderStatus,
            Priority: priority,
            PresenceStatus: presenceStatus,
            VisitId: visitId,
            PatientId: patientId,
            RequiredForConclusion: req.RequiredForConclusion,
            ActiveOnly: req.ActiveOnly,
            Keyword: keyword,
            Limit: req.Limit);
    }

    private static string? ValidateOptionalFilterToken(string? raw, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(raw) ||
            string.Equals(raw.Trim(), "ALL", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var trimmed = raw.Trim();
        if (trimmed.Length > 100 || trimmed.Contains('<') || trimmed.Contains('>'))
        {
            throw new ValidationException($"Giá trị bộ lọc '{fieldName}' không hợp lệ.");
        }

        return trimmed;
    }

    private static void ValidateEntryIdentifier(string? idOrCode)
    {
        if (string.IsNullOrWhiteSpace(idOrCode))
        {
            throw new ValidationException("Mã định danh lượt xếp hàng cận lâm sàng không hợp lệ.");
        }

        var trimmed = idOrCode.Trim();
        if (trimmed.Length > 64 || trimmed.Contains('<') || trimmed.Contains('>'))
        {
            throw new ValidationException("Mã định danh lượt xếp hàng cận lâm sàng không hợp lệ.");
        }
    }

    private static string? ValidateOptionalPresenceStatus(string? rawPresence)
    {
        if (string.IsNullOrWhiteSpace(rawPresence))
        {
            return null;
        }

        var normalized = rawPresence.Trim().ToUpperInvariant();
        if (!AllowedPresenceStatuses.Contains(normalized))
        {
            throw new ValidationException("Trạng thái hiện diện không hợp lệ.");
        }

        return normalized;
    }

    private static string? ValidateOptionalNotes(string? rawNotes)
    {
        if (string.IsNullOrWhiteSpace(rawNotes))
        {
            return null;
        }

        var trimmed = rawNotes.Trim();
        if (trimmed.Length > 500 || trimmed.Contains('<') || trimmed.Contains('>'))
        {
            throw new ValidationException("Ghi chú điều phối hàng đợi không hợp lệ (tối đa 500 ký tự).");
        }

        return trimmed;
    }

    private async Task<List<QueueServicePoint>> TryLoadHospitalDiagnosticServicePointsAsync(
        long hospitalId,
        CancellationToken ct)
    {
        try
        {
            return await _db.QueueServicePoints
                .AsNoTracking()
                .Where(sp =>
                    sp.HospitalId == hospitalId &&
                    sp.PointType != null &&
                    sp.PointType.ToUpper() == "DIAGNOSTIC")
                .OrderBy(sp => sp.Id)
                .ToListAsync(ct);
        }
        catch (NotSupportedException)
        {
            return [];
        }
    }

    private async Task<Dictionary<long, DiagnosticResult>> TryLoadDiagnosticResultsByOrderIdAsync(
        IReadOnlyList<DiagnosticOrder> orders,
        CancellationToken ct)
    {
        var dict = new Dictionary<long, DiagnosticResult>();
        if (orders.Count == 0)
        {
            return dict;
        }

        var orderIds = orders.Select(o => o.Id).Distinct().ToList();
        try
        {
            var results = await _db.DiagnosticResults
                .AsNoTracking()
                .Where(r => r.DiagnosticOrderId.HasValue && orderIds.Contains(r.DiagnosticOrderId.Value))
                .OrderByDescending(r => r.FinalizedAt)
                .ThenByDescending(r => r.Id)
                .ToListAsync(ct);

            foreach (var r in results)
            {
                if (r.DiagnosticOrderId.HasValue && !dict.ContainsKey(r.DiagnosticOrderId.Value))
                {
                    dict[r.DiagnosticOrderId.Value] = r;
                }
            }
        }
        catch (NotSupportedException)
        {
            return dict;
        }

        return dict;
    }

    private async Task SaveChangesWithConcurrencyGuardAsync(CancellationToken ct)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException(
                "Dữ liệu hàng đợi cận lâm sàng đã thay đổi bởi thao tác khác (state_version mismatch). Vui lòng tải lại hàng đợi.");
        }
    }

    private static QueueServicePoint? ResolveDiagnosticServicePointForService(
        long serviceId,
        IReadOnlyList<QueueServicePoint> hospitalDiagPoints)
    {
        var specific = hospitalDiagPoints
            .Where(sp => sp.ServiceId == serviceId)
            .OrderBy(sp => sp.IsActive == false ? 1 : 0)
            .ThenBy(sp => sp.Id)
            .FirstOrDefault();

        if (specific is not null)
        {
            return specific;
        }

        return hospitalDiagPoints
            .Where(sp => sp.IsActive != false)
            .OrderBy(sp => sp.ServiceId.HasValue ? 1 : 0)
            .ThenBy(sp => sp.Id)
            .FirstOrDefault();
    }

    private static string ResolveDiagnosticCategory(
        Service service,
        IReadOnlyDictionary<long, Specialty> specialtiesById)
    {
        var specCode = service.SpecialtyId.HasValue &&
                       specialtiesById.TryGetValue(service.SpecialtyId.Value, out var spec)
            ? spec.SpecialtyCode
            : null;

        var code = service.ServiceCode ?? string.Empty;
        var name = service.Name ?? string.Empty;

        if (string.Equals(specCode, "IMG", StringComparison.OrdinalIgnoreCase) ||
            code.Contains("XRAY", StringComparison.OrdinalIgnoreCase) ||
            code.Contains("XQ", StringComparison.OrdinalIgnoreCase) ||
            code.Contains("US", StringComparison.OrdinalIgnoreCase) ||
            code.Contains("SA", StringComparison.OrdinalIgnoreCase) ||
            code.Contains("CT", StringComparison.OrdinalIgnoreCase) ||
            code.Contains("MRI", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("X-quang", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Siêu âm", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Chụp", StringComparison.OrdinalIgnoreCase))
        {
            return "IMAGING";
        }

        if (string.Equals(specCode, "CARD", StringComparison.OrdinalIgnoreCase) ||
            code.Contains("ECG", StringComparison.OrdinalIgnoreCase) ||
            code.Contains("EEG", StringComparison.OrdinalIgnoreCase) ||
            code.Contains("TDCN", StringComparison.OrdinalIgnoreCase) ||
            code.Contains("ENDO", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Điện tâm", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Nội soi", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Thăm dò", StringComparison.OrdinalIgnoreCase))
        {
            return "FUNCTIONAL";
        }

        return "LABORATORY";
    }

    private static string BuildMutationMessage(
        string auditAction,
        DiagnosticQueueEntryItemDto entry,
        string previousStatus,
        string targetStatus)
    {
        return auditAction switch
        {
            DiagnosticQueueCallNextAction =>
                $"Đã gọi bệnh nhân {entry.PatientName} ({entry.TicketNo}) vào {entry.ServicePointName}.",
            DiagnosticQueueMissedTurnAction =>
                $"Đã đánh dấu qua lượt (SKIPPED) cho bệnh nhân {entry.PatientName} ({entry.TicketNo}).",
            DiagnosticQueueTurnRestoredAction =>
                $"Đã khôi phục lượt chờ (READY) cho bệnh nhân {entry.PatientName} ({entry.TicketNo}).",
            DiagnosticQueueEntryCancelledAction =>
                $"Đã hủy lượt xếp hàng cận lâm sàng ({entry.TicketNo}) của bệnh nhân {entry.PatientName}.",
            _ =>
                $"Đã cập nhật trạng thái lượt xếp hàng cận lâm sàng ({entry.TicketNo}) từ {previousStatus} sang {targetStatus}.",
        };
    }

    private static string FormatDoctorName(Doctor? doctor)
    {
        if (doctor is null)
        {
            return "BS. Chỉ định CLS";
        }

        var docTitle = string.IsNullOrWhiteSpace(doctor.ProfessionalTitle)
            ? "BS"
            : doctor.ProfessionalTitle.Trim();
        var docFullName = doctor.Staff?.FullName ?? doctor.DoctorCode;
        if (!string.IsNullOrWhiteSpace(docFullName))
        {
            return DoctorCatalogService.FormatDisplayName(docFullName, docTitle);
        }

        return $"BS #{doctor.Id}";
    }

    private static string FormatOrderCode(DiagnosticOrder order, DateOnly referenceDate)
    {
        if (!string.IsNullOrWhiteSpace(order.OrderCode))
        {
            return order.OrderCode.Trim();
        }

        var date = order.OrderedAt.HasValue
            ? DateOnly.FromDateTime(order.OrderedAt.Value)
            : referenceDate;
        return $"DIA-{date:yyyyMM}-{order.Id:D4}";
    }

    private static string FormatVisitCode(Visit visit, DateOnly referenceDate)
    {
        if (!string.IsNullOrWhiteSpace(visit.VisitCode))
        {
            return visit.VisitCode.Trim();
        }

        var date = visit.CheckedInAt.HasValue
            ? DateOnly.FromDateTime(visit.CheckedInAt.Value)
            : referenceDate;
        return $"VIS-{date:yyyyMM}-{visit.Id:D4}";
    }

    private static string NormalizeQueueStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return "WAITING";
        }

        var normalized = status.Trim().ToUpperInvariant();
        return normalized switch
        {
            "IN_CONSULTATION" or "SERVING" => "IN_PROGRESS",
            "DONE" => "COMPLETED",
            _ => normalized,
        };
    }

    private static string NormalizeOrderStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return "ORDERED";
        }

        var normalized = status.Trim().ToUpperInvariant();
        return normalized switch
        {
            "PENDING" or "WAITING" or "PENDING_CONFIRMATION" => "ORDERED",
            "SERVING" => "IN_PROGRESS",
            "DONE" or "FINALIZED" or "VERIFIED" => "COMPLETED",
            _ => normalized,
        };
    }

    private static string NormalizePriority(string? priority)
    {
        if (string.IsNullOrWhiteSpace(priority))
        {
            return "NORMAL";
        }

        var normalized = priority.Trim().ToUpperInvariant();
        return normalized switch
        {
            "URGENT" => "URGENT",
            "STAT" => "STAT",
            _ => "NORMAL",
        };
    }

    private static string NormalizeVisitStatus(string? status)
    {
        var normalized = AppointmentBookingRules.NormalizeStatusKey(status);
        return string.IsNullOrWhiteSpace(normalized) ? "IN_PROGRESS" : normalized;
    }

    private static bool IsTerminalVisitStatus(string? visitStatus)
    {
        var normalized = NormalizeVisitStatus(visitStatus);
        return normalized is "COMPLETED" or "CANCELLED" or "NO_SHOW" or "DONE";
    }

    private static string ResolveShiftLabel(DateTime nowLocal)
    {
        var hour = nowLocal.Hour;
        if (hour < 12) return "Ca sáng (07:00 - 12:00)";
        if (hour < 17) return "Ca chiều (13:00 - 17:00)";
        return "Ca trực tối (17:00 - 21:00)";
    }

    private static string FormatQueueStatusLabel(string status) => status switch
    {
        "RESERVED" => "Đã giữ chỗ",
        "WAITING" => "Đang chờ tiếp nhận",
        "READY" => "Sẵn sàng gọi",
        "CALLED" => "Đã gọi tên",
        "IN_PROGRESS" => "Đang thực hiện kỹ thuật",
        "COMPLETED" => "Đã hoàn tất",
        "SKIPPED" => "Qua lượt (Vắng mặt)",
        "CANCELLED" => "Đã hủy",
        _ => status,
    };

    private static string FormatPresenceStatusLabel(string presenceStatus) => presenceStatus switch
    {
        "PRESENT" => "Có mặt",
        "NOT_ARRIVED" => "Chưa đến",
        "LEFT" => "Tạm vắng / Rời vị trí",
        _ => presenceStatus,
    };

    private static string FormatPriorityLabel(string priority) => priority switch
    {
        "STAT" => "Tối khẩn (STAT)",
        "URGENT" => "Khẩn (URGENT)",
        _ => "Thường quy",
    };

    private static string FormatOrderStatusLabel(string orderStatus) => orderStatus switch
    {
        "ORDERED" => "Chờ xác nhận",
        "CONFIRMED" => "Đã xác nhận",
        "IN_PROGRESS" => "Đang thực hiện",
        "COMPLETED" => "Đã hoàn tất",
        "CANCELLED" => "Đã hủy",
        _ => orderStatus,
    };

    private static string FormatCategoryLabel(string category) => category switch
    {
        "LABORATORY" => "Xét nghiệm",
        "IMAGING" => "Chẩn đoán hình ảnh",
        "FUNCTIONAL" => "Thăm dò chức năng",
        _ => "Cận lâm sàng",
    };

    private static string FormatGenderLabel(string gender) => gender switch
    {
        "MALE" => "Nam",
        "FEMALE" => "Nữ",
        _ => "Khác",
    };

    private static int? CalculatePatientAge(DateOnly? dateOfBirth, DateOnly referenceDate)
    {
        if (!dateOfBirth.HasValue)
        {
            return null;
        }

        var age = referenceDate.Year - dateOfBirth.Value.Year;
        if (referenceDate < dateOfBirth.Value.AddYears(age))
        {
            age--;
        }

        return Math.Max(0, age);
    }

    private sealed record DiagnosticStaffAccessContext(
        Hospital Hospital,
        Staff Staff,
        string DepartmentName,
        DiagnosticStaffProfileDto Profile);

    private sealed record ValidatedGetQueueFilter(
        DateOnly? FilterDate,
        string? ServicePointId,
        string? RoomId,
        string? ServiceId,
        string? Category,
        string? QueueStatus,
        string? OrderStatus,
        string? Priority,
        string? PresenceStatus,
        string? VisitId,
        string? PatientId,
        bool? RequiredForConclusion,
        bool ActiveOnly,
        string? Keyword,
        int Limit);

    private sealed record RawDiagnosticQueueCandidate(
        QueueEntry? QueueEntry,
        DiagnosticOrder? Order);

    private sealed record HospitalDiagnosticQueueDataset(
        DateOnly EffectiveQueueDate,
        IReadOnlyList<QueueServicePoint> HospitalDiagnosticServicePoints,
        IReadOnlyDictionary<long, QueueServicePoint> AllServicePointsById,
        IReadOnlyDictionary<long, HospitalRoom> RoomsById,
        IReadOnlyDictionary<long, Department> DepartmentsById,
        IReadOnlyDictionary<long, Service> ServicesById,
        IReadOnlyDictionary<long, Specialty> SpecialtiesById,
        IReadOnlyDictionary<long, Visit> VisitsById,
        IReadOnlyDictionary<long, QueueEntry> QueueEntriesById,
        IReadOnlyList<DiagnosticOrder> AllOrders,
        IReadOnlyList<DiagnosticOrder> ForeignOrders,
        IReadOnlyList<QueueEntry> ForeignQueueEntries,
        IReadOnlyList<QueueEntry> NonDiagnosticHospitalQueueEntries,
        IReadOnlyList<DiagnosticServicePointQueueBucketDto> ServicePointBuckets,
        IReadOnlyList<DiagnosticQueueEntryItemDto> AllSortedEntries,
        Func<QueueServicePoint?, long?> ResolveServicePointHospitalId,
        Func<HospitalRoom?, long?> ResolveRoomHospitalId);

    private sealed record DiagnosticQueueAuditPayload(
        long QueueEntryId,
        long? VisitId,
        string? VisitCode,
        long? OrderId,
        string? OrderCode,
        string PreviousStatus,
        string TargetStatus,
        string? PreviousOrderStatus,
        string? UpdatedOrderStatus,
        string? PreviousPresenceStatus,
        string? UpdatedPresenceStatus,
        string? PreviousPriority,
        string? UpdatedPriority,
        int? PreviousStateVersion,
        int UpdatedStateVersion,
        long? ServicePointId,
        string? ServicePointName,
        long? RoomId,
        string? RoomCode,
        string? Reason,
        string? BlockedReason,
        string? Notes,
        long? UpdatedByUserId,
        string? UpdatedByStaffCode,
        string? UpdatedByName,
        DateTime UpdatedAtUtc);
}
