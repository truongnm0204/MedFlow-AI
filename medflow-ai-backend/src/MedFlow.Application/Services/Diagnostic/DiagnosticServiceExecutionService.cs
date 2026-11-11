using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using MedFlow.Application.DTOs.Diagnostic;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Appointments;
using MedFlow.Application.Services.Doctors;
using MedFlow.Application.Services.Visits;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Enums;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MedFlow.Application.Services.Diagnostic;

public sealed class DiagnosticServiceExecutionService : IDiagnosticServiceExecutionService
{
    public const string DiagnosticOrderEntityType = "DIAGNOSTIC_ORDER";
    public const string QueueEntryEntityType = "QUEUE_ENTRY";

    public const string ExecutionStartedAction = "DIAGNOSTIC_SERVICE_EXECUTION_STARTED";
    public const string ExecutionCompletedAction = "DIAGNOSTIC_SERVICE_EXECUTION_COMPLETED";
    public const string ExecutionStatusUpdatedAction = "DIAGNOSTIC_SERVICE_STATUS_UPDATED";
    public const string ExecutionCancelledAction = "DIAGNOSTIC_SERVICE_EXECUTION_CANCELLED";

    private const string DiagnosticStaffRoleCode = "DIAGNOSTIC_STAFF";
    private const string DefaultDepartmentName = "Khoa Cận lâm sàng";

    private static readonly string[] AllowedReadPermissions =
    [
        "QUEUE_MANAGE",
        "DIAGNOSTIC_RESULT_WRITE",
        "DIAGNOSTIC_ORDER",
    ];

    private static readonly string[] AllowedMutationPermissions =
    [
        "QUEUE_MANAGE",
        "DIAGNOSTIC_RESULT_WRITE",
    ];

    private static readonly string[] FallbackDiagnosticPermissions =
    [
        "DIAGNOSTIC_RESULT_WRITE",
        "QUEUE_MANAGE",
    ];

    private static readonly HashSet<string> ValidListStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "ORDERED",
        "PENDING",
        "PENDING_CONFIRMATION",
        "CONFIRMED",
        "READY",
        "WAITING",
        "CALLED",
        "IN_PROGRESS",
        "EXECUTING",
        "PROCESSING",
        "COMPLETED",
        "DONE",
        "FINALIZED",
        "CANCELLED",
    };

    private static readonly HashSet<string> ValidQueueStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "WAITING",
        "RESERVED",
        "READY",
        "CALLED",
        "IN_PROGRESS",
        "SKIPPED",
        "COMPLETED",
        "CANCELLED",
    };

    private static readonly HashSet<string> ValidCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "LABORATORY",
        "LAB",
        "IMAGING",
        "IMG",
        "FUNCTIONAL",
        "FUNC",
    };

    private static readonly HashSet<string> ValidPriorities = new(StringComparer.OrdinalIgnoreCase)
    {
        "NORMAL",
        "STANDARD",
        "ROUTINE",
        "URGENT",
        "STAT",
        "EMERGENCY",
    };

    private static readonly JsonSerializerOptions AuditJsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;

    public DiagnosticServiceExecutionService(
        IApplicationDbContext dbContext,
        TimeProvider? timeProvider = null)
    {
        _db = dbContext;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    // ======================= PUBLIC API =======================

    public async Task<DiagnosticServiceExecutionListResponseDto> GetExecutionsAsync(
        long actorUserId,
        GetDiagnosticServiceExecutionsRequest? request = null,
        CancellationToken ct = default)
    {
        var effectiveRequest = request ?? new GetDiagnosticServiceExecutionsRequest();
        var filter = ValidateListFilter(effectiveRequest);

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var nowLocal = AppointmentBookingRules.GetVietnamNow(_timeProvider);
        var referenceDate = DateOnly.FromDateTime(nowLocal);

        var staffAccess = await EnsureDiagnosticStaffAccessAsync(actorUserId, nowUtc, forMutation: false, ct);
        var hospitalId = staffAccess.Hospital.Id;

        var allOrders = await TryLoadAllDiagnosticOrdersAsync(ct);
        var lookupBundle = await LoadOrderLookupBundleAsync(allOrders, hospitalId, nowLocal, ct);
        var executionMetaByOrderId = await TryLoadExecutionAuditMetadataAsync(
            allOrders.Select(o => o.Id.ToString(CultureInfo.InvariantCulture)).Distinct().ToList(), ct);
        var resultStateByOrderId = await TryLoadResultStatesAsync(allOrders, ct);

        var hospitalOrders = allOrders
            .Where(o => IsOrderInHospitalScope(o, hospitalId, lookupBundle))
            .ToList();

        var allItems = new List<DiagnosticServiceExecutionItemDto>(hospitalOrders.Count);
        foreach (var order in hospitalOrders)
        {
            executionMetaByOrderId.TryGetValue(order.Id, out var meta);
            resultStateByOrderId.TryGetValue(order.Id, out var resultState);
            allItems.Add(BuildExecutionItemDto(
                order, staffAccess, lookupBundle, meta, resultState, referenceDate, nowLocal, forMutation: false));
        }

        var summary = BuildSummaryStats(allItems);
        var filteredItems = ApplyListFilters(allItems, filter);
        var availableServicePoints = BuildServicePointOptions(
            lookupBundle.HospitalDiagnosticServicePoints, lookupBundle.RoomsById);

        return new DiagnosticServiceExecutionListResponseDto(
            HospitalId: staffAccess.Hospital.Id,
            HospitalCode: staffAccess.Hospital.HospitalCode,
            HospitalName: staffAccess.Hospital.Name,
            FacilityName: staffAccess.Hospital.Name,
            FacilityCode: staffAccess.Hospital.HospitalCode,
            DepartmentId: staffAccess.Staff.DepartmentId,
            DepartmentName: staffAccess.DepartmentName,
            GeneratedAtUtc: nowUtc,
            StaffProfile: staffAccess.Profile,
            Summary: summary,
            AvailableServicePoints: availableServicePoints,
            Items: filteredItems);
    }

    public async Task<DiagnosticServiceExecutionDetailResponseDto> GetExecutionByIdOrCodeAsync(
        long actorUserId,
        string idOrCode,
        CancellationToken ct = default)
    {
        var parsedId = ParseOrderIdentifier(idOrCode);

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var nowLocal = AppointmentBookingRules.GetVietnamNow(_timeProvider);
        var referenceDate = DateOnly.FromDateTime(nowLocal);

        var staffAccess = await EnsureDiagnosticStaffAccessAsync(actorUserId, nowUtc, forMutation: false, ct);
        var hospitalId = staffAccess.Hospital.Id;

        var order = await ResolveDiagnosticOrderAsync(parsedId, hospitalId, referenceDate, asTracking: false, ct);
        var visitOrders = order.VisitId.HasValue
            ? await TryLoadVisitDiagnosticOrdersAsync(order.VisitId.Value, ct)
            : [order];
        if (visitOrders.All(o => o.Id != order.Id))
        {
            visitOrders.Add(order);
        }

        var lookupBundle = await LoadOrderLookupBundleAsync(visitOrders, hospitalId, nowLocal, ct);
        EnsureOrderHospitalOwnership(order, hospitalId, lookupBundle);

        var executionMetaByOrderId = await TryLoadExecutionAuditMetadataAsync(
            visitOrders.Select(o => o.Id.ToString(CultureInfo.InvariantCulture)).Distinct().ToList(), ct);
        var resultStateByOrderId = await TryLoadResultStatesAsync(visitOrders, ct);

        executionMetaByOrderId.TryGetValue(order.Id, out var meta);
        resultStateByOrderId.TryGetValue(order.Id, out var resultState);

        var itemDto = BuildExecutionItemDto(
            order, staffAccess, lookupBundle, meta, resultState, referenceDate, nowLocal, forMutation: false);

        var visit = order.VisitId.HasValue && lookupBundle.VisitsById.TryGetValue(order.VisitId.Value, out var foundVisit)
            ? foundVisit
            : null;
        var clinicalContext = await BuildClinicalSummaryAsync(
            visit, itemDto.VisitId, itemDto.VisitCode, itemDto.VisitStatus, lookupBundle, ct);

        var service = order.ServiceId.HasValue && lookupBundle.ServicesById.TryGetValue(order.ServiceId.Value, out var foundService)
            ? foundService
            : null;
        var availableServicePoints = BuildServicePointOptions(
            lookupBundle.HospitalDiagnosticServicePoints
                .Where(sp => !sp.ServiceId.HasValue || sp.ServiceId.Value == (service?.Id ?? order.ServiceId))
                .ToList(),
            lookupBundle.RoomsById);

        var history = await BuildExecutionHistoryAsync(order, lookupBundle, referenceDate, ct);

        return new DiagnosticServiceExecutionDetailResponseDto(
            HospitalId: staffAccess.Hospital.Id,
            HospitalCode: staffAccess.Hospital.HospitalCode,
            HospitalName: staffAccess.Hospital.Name,
            GeneratedAtUtc: nowUtc,
            StaffProfile: staffAccess.Profile,
            Order: itemDto,
            ClinicalContext: clinicalContext,
            AvailableServicePoints: availableServicePoints,
            ExecutionHistory: history);
    }

    public Task<UpdateDiagnosticServiceStatusResponseDto> UpdateServiceExecutionStatusAsync(
        long actorUserId,
        string idOrCode,
        UpdateDiagnosticServiceStatusRequest? request = null,
        string? ipAddress = null,
        CancellationToken ct = default)
    {
        var effectiveRequest = request ?? new UpdateDiagnosticServiceStatusRequest();
        var rawTarget = effectiveRequest.TargetStatus ?? effectiveRequest.Status;
        if (string.IsNullOrWhiteSpace(rawTarget))
        {
            throw new ValidationException("Vui lòng chọn trạng thái thực hiện dịch vụ cận lâm sàng cần cập nhật (targetStatus).");
        }

        var (targetStatus, requestedQueueStatus) = NormalizeTargetStatus(rawTarget);
        return ExecuteUpdateCoreAsync(actorUserId, [ParseOrderIdentifier(idOrCode)], effectiveRequest, targetStatus, requestedQueueStatus, ipAddress, ct);
    }

    public Task<UpdateDiagnosticServiceStatusResponseDto> StartServiceExecutionAsync(
        long actorUserId,
        string idOrCode,
        UpdateDiagnosticServiceStatusRequest? request = null,
        string? ipAddress = null,
        CancellationToken ct = default)
    {
        var effectiveRequest = request ?? new UpdateDiagnosticServiceStatusRequest();
        var rawTarget = effectiveRequest.TargetStatus ?? effectiveRequest.Status;
        if (!string.IsNullOrWhiteSpace(rawTarget))
        {
            var (explicitTarget, _) = NormalizeTargetStatus(rawTarget);
            if (explicitTarget != "IN_PROGRESS")
            {
                throw new ValidationException("Endpoint /start chỉ hỗ trợ chuyển trạng thái thực hiện sang IN_PROGRESS.");
            }
        }

        return ExecuteUpdateCoreAsync(actorUserId, [ParseOrderIdentifier(idOrCode)], effectiveRequest, "IN_PROGRESS", null, ipAddress, ct);
    }

    public Task<UpdateDiagnosticServiceStatusResponseDto> CompleteServiceExecutionAsync(
        long actorUserId,
        string idOrCode,
        UpdateDiagnosticServiceStatusRequest? request = null,
        string? ipAddress = null,
        CancellationToken ct = default)
    {
        var effectiveRequest = request ?? new UpdateDiagnosticServiceStatusRequest();
        var rawTarget = effectiveRequest.TargetStatus ?? effectiveRequest.Status;
        if (!string.IsNullOrWhiteSpace(rawTarget))
        {
            var (explicitTarget, _) = NormalizeTargetStatus(rawTarget);
            if (explicitTarget != "COMPLETED")
            {
                throw new ValidationException("Endpoint /complete chỉ hỗ trợ chuyển trạng thái thực hiện sang COMPLETED.");
            }
        }

        return ExecuteUpdateCoreAsync(actorUserId, [ParseOrderIdentifier(idOrCode)], effectiveRequest, "COMPLETED", null, ipAddress, ct);
    }

    public async Task<UpdateDiagnosticServiceStatusResponseDto> UpdateServiceExecutionStatusBatchAsync(
        long actorUserId,
        UpdateDiagnosticServiceStatusBatchRequest? request = null,
        string? ipAddress = null,
        CancellationToken ct = default)
    {
        if (request is null)
        {
            throw new ValidationException("Dữ liệu cập nhật trạng thái thực hiện dịch vụ cận lâm sàng không được để trống.");
        }

        var rawTarget = request.TargetStatus ?? request.Status;
        if (string.IsNullOrWhiteSpace(rawTarget))
        {
            throw new ValidationException("Vui lòng chọn trạng thái thực hiện dịch vụ cận lâm sàng cần cập nhật (targetStatus).");
        }

        var (targetStatus, requestedQueueStatus) = NormalizeTargetStatus(rawTarget);

        var nowLocal0 = AppointmentBookingRules.GetVietnamNow(_timeProvider);
        var referenceDate0 = DateOnly.FromDateTime(nowLocal0);

        // Validate + resolve targets BEFORE authz? No: authz first for proper 401/403 ordering.
        var nowUtc0 = _timeProvider.GetUtcNow().UtcDateTime;
        var staffAccess0 = await EnsureDiagnosticStaffAccessAsync(actorUserId, nowUtc0, forMutation: true, ct);

        var targetIdentifiers = await ResolveBatchTargetIdentifiersAsync(
            request, staffAccess0.Hospital.Id, nowLocal0, referenceDate0, ct);

        var mapped = new UpdateDiagnosticServiceStatusRequest(
            TargetStatus: request.TargetStatus,
            Status: request.Status,
            PresenceStatus: request.PresenceStatus,
            ServicePointId: request.ServicePointId,
            RoomId: request.RoomId,
            SpecimenCode: request.SpecimenCode,
            SampleCode: request.SampleCode,
            SpecimenType: request.SpecimenType,
            Modality: request.Modality,
            EquipmentCode: request.EquipmentCode,
            DeviceName: request.DeviceName,
            ExecutionNote: request.ExecutionNote,
            TechnicalNote: request.TechnicalNote,
            Notes: request.Notes,
            Instruction: request.Instruction,
            Reason: request.Reason,
            BlockedReason: request.BlockedReason,
            ExpectedStateVersion: request.ExpectedStateVersion);

        return await ExecuteUpdateCoreAsync(actorUserId, targetIdentifiers, mapped, targetStatus, requestedQueueStatus, ipAddress, ct, preAuthorized: staffAccess0);
    }

    // ======================= MUTATION CORE =======================

    private async Task<UpdateDiagnosticServiceStatusResponseDto> ExecuteUpdateCoreAsync(
        long actorUserId,
        IReadOnlyList<ParsedOrderIdentifier> targetIdentifiers,
        UpdateDiagnosticServiceStatusRequest request,
        string targetStatus,
        string? requestedQueueStatus,
        string? ipAddress,
        CancellationToken ct,
        DiagnosticStaffAccessContext? preAuthorized = null)
    {
        var validated = ValidateMutationRequest(request, targetStatus);

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var nowLocal = AppointmentBookingRules.GetVietnamNow(_timeProvider);
        var referenceDate = DateOnly.FromDateTime(nowLocal);

        var staffAccess = preAuthorized
            ?? await EnsureDiagnosticStaffAccessAsync(actorUserId, nowUtc, forMutation: true, ct);
        var hospitalId = staffAccess.Hospital.Id;

        var targetOrders = new List<DiagnosticOrder>(targetIdentifiers.Count);
        var seenOrderIds = new HashSet<long>();
        foreach (var identifier in targetIdentifiers)
        {
            var resolvedOrder = await ResolveDiagnosticOrderAsync(identifier, hospitalId, referenceDate, asTracking: true, ct);
            if (!seenOrderIds.Add(resolvedOrder.Id))
            {
                throw new ValidationException("Danh sách phiếu chỉ định cần cập nhật có mã hoặc ID bị trùng lặp.");
            }

            targetOrders.Add(resolvedOrder);
        }

        var visitIds = targetOrders
            .Where(o => o.VisitId.HasValue)
            .Select(o => o.VisitId!.Value)
            .Distinct()
            .ToList();

        var allRelatedOrders = new List<DiagnosticOrder>(targetOrders);
        foreach (var vId in visitIds)
        {
            var vOrders = await TryLoadVisitDiagnosticOrdersAsync(vId, ct);
            foreach (var vo in vOrders)
            {
                if (allRelatedOrders.All(x => x.Id != vo.Id))
                {
                    allRelatedOrders.Add(vo);
                }
            }
        }

        var lookupBundle = await LoadOrderLookupBundleAsync(allRelatedOrders, hospitalId, nowLocal, ct);
        var executionMetaByOrderId = await TryLoadExecutionAuditMetadataAsync(
            allRelatedOrders.Select(o => o.Id.ToString(CultureInfo.InvariantCulture)).Distinct().ToList(), ct);
        var resultStateByOrderId = await TryLoadResultStatesAsync(allRelatedOrders, ct);

        // Validate every plan BEFORE mutating
        var plans = new List<ResolvedExecutionPlan>(targetOrders.Count);
        foreach (var order in targetOrders)
        {
            EnsureOrderHospitalOwnership(order, hospitalId, lookupBundle);
            resultStateByOrderId.TryGetValue(order.Id, out var resultState);
            var plan = await ValidateOrderForExecutionUpdateAsync(
                order, validated, targetStatus, staffAccess, lookupBundle, resultState, referenceDate, ct);
            plans.Add(plan);
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

        var updatedItems = new List<DiagnosticServiceExecutionItemDto>(plans.Count);
        string firstAction = string.Empty;
        long firstOrderId = 0;
        string firstOrderCode = string.Empty;
        long? firstQueueEntryId = null;
        string? firstTicketNo = null;
        string firstPrevStatus = string.Empty;
        string firstNewStatus = string.Empty;
        string? firstPrevQueueStatus = null;
        string? firstNewQueueStatus = null;
        int firstPrevStateVersion = 1;
        int firstStateVersion = 1;

        try
        {
            for (var index = 0; index < plans.Count; index++)
            {
                var plan = plans[index];
                var order = plan.Order;
                var orderCode = FormatOrderCode(order, referenceDate);
                var visit = plan.Visit;
                var visitCode = FormatVisitCode(visit, referenceDate);
                var linkedQueueEntry = plan.LinkedQueueEntry;

                var previousOrderStatus = plan.CurrentOrderStatus;
                var previousQueueStatus = linkedQueueEntry is not null ? NormalizeQueueStatus(linkedQueueEntry.Status) : null;
                var prevStateVersion = linkedQueueEntry?.StateVersion is > 0
                    ? linkedQueueEntry.StateVersion!.Value
                    : 1;
                var newStateVersion = linkedQueueEntry is null ? 1 : prevStateVersion + 1;

                // Create queue entry when missing on CONFIRMED order
                if (linkedQueueEntry is null)
                {
                    var diagSpId = plan.ServicePoint?.Id;
                    var maxQueueNumber = await _db.QueueEntries
                        .AsNoTracking()
                        .Where(q =>
                            q.QueueDate == referenceDate &&
                            (diagSpId.HasValue
                                ? (q.ServicePointId == diagSpId.Value || q.CurrentServicePointId == diagSpId.Value)
                                : (q.QueueType != null && q.QueueType.ToUpper() == "DIAGNOSTIC")))
                        .MaxAsync(q => (int?)q.QueueNumber, ct);

                    var nextQueueNumber = (maxQueueNumber ?? 0) + 1;
                    var newQueueStatus = ResolveQueueStatusForTarget(targetStatus, requestedQueueStatus);
                    var isStartOrComplete = targetStatus is "IN_PROGRESS" or "COMPLETED";

                    linkedQueueEntry = new QueueEntry
                    {
                        VisitId = visit.Id,
                        AppointmentId = null,
                        ServicePointId = diagSpId,
                        CurrentServicePointId = diagSpId,
                        QueueDate = referenceDate,
                        QueueNumber = nextQueueNumber,
                        QueueType = "DIAGNOSTIC",
                        Status = newQueueStatus,
                        ScheduledAt = null,
                        PresenceStatus = isStartOrComplete ? "PRESENT" : "NOT_ARRIVED",
                        ArrivedAt = isStartOrComplete ? nowLocal : null,
                        ArrivalClass = "WALK_IN",
                        ManualPriority = NormalizePriority(order.Priority),
                        StateVersion = newStateVersion,
                        ReadyAt = isStartOrComplete ? nowLocal : null,
                        CalledAt = isStartOrComplete ? nowLocal : null,
                        StartedAt = targetStatus == "IN_PROGRESS" ? nowLocal : null,
                        CompletedAt = targetStatus is "COMPLETED" or "CANCELLED" ? nowLocal : null,
                        BlockedReason = targetStatus is "CONFIRMED" or "CANCELLED" ? validated.Reason : null,
                    };

                    _db.QueueEntries.Add(linkedQueueEntry);
                    await SaveChangesWithConcurrencyGuardAsync(ct);
                    order.QueueEntryId = linkedQueueEntry.Id;
                    lookupBundle.QueueEntriesById[linkedQueueEntry.Id] = linkedQueueEntry;
                }
                else
                {
                    var isStartOrComplete = targetStatus is "IN_PROGRESS" or "COMPLETED";

                    if (plan.ServicePoint is not null)
                    {
                        linkedQueueEntry.ServicePointId ??= plan.ServicePoint.Id;
                        linkedQueueEntry.CurrentServicePointId = plan.ServicePoint.Id;
                    }

                    linkedQueueEntry.Status = ResolveQueueStatusForTarget(targetStatus, requestedQueueStatus);

                    if (isStartOrComplete)
                    {
                        linkedQueueEntry.PresenceStatus = "PRESENT";
                        linkedQueueEntry.ArrivedAt ??= nowLocal;
                        linkedQueueEntry.ReadyAt ??= nowLocal;
                        linkedQueueEntry.CalledAt ??= nowLocal;
                        if (targetStatus == "IN_PROGRESS")
                        {
                            linkedQueueEntry.StartedAt = nowLocal;
                            linkedQueueEntry.CompletedAt = null;
                        }
                        else
                        {
                            linkedQueueEntry.StartedAt ??= nowLocal;
                            linkedQueueEntry.CompletedAt = nowLocal;
                        }
                        linkedQueueEntry.BlockedReason = null;
                    }
                    else if (targetStatus == "CONFIRMED")
                    {
                        linkedQueueEntry.StartedAt = null;
                        linkedQueueEntry.CompletedAt = null;
                        linkedQueueEntry.BlockedReason = validated.Reason;
                    }
                    else if (targetStatus == "CANCELLED")
                    {
                        linkedQueueEntry.CompletedAt = nowLocal;
                        linkedQueueEntry.BlockedReason = validated.Reason;
                    }

                    linkedQueueEntry.StateVersion = newStateVersion;
                    lookupBundle.QueueEntriesById[linkedQueueEntry.Id] = linkedQueueEntry;
                }

                var newQueueStatusAfter = NormalizeQueueStatus(linkedQueueEntry.Status);
                var queueNumber = linkedQueueEntry.QueueNumber;
                var ticketNo = queueNumber is > 0 ? $"C{queueNumber.Value:D3}" : null;

                order.Status = targetStatus;
                if (validated.Instruction is not null)
                {
                    order.Instruction = validated.Instruction;
                }
                order.ConfirmedAt ??= nowLocal;
                order.ConfirmedByStaffId ??= staffAccess.Staff.Id;

                var auditAction = targetStatus switch
                {
                    "IN_PROGRESS" => ExecutionStartedAction,
                    "COMPLETED" => ExecutionCompletedAction,
                    "CONFIRMED" => ExecutionStatusUpdatedAction,
                    _ => ExecutionCancelledAction,
                };

                var changePayload = new
                {
                    orderId = order.Id,
                    orderCode,
                    visitId = visit.Id,
                    visitCode,
                    queueEntryId = linkedQueueEntry.Id,
                    ticketNo,
                    previousStatus = previousOrderStatus,
                    targetStatus,
                    previousQueueStatus,
                    targetQueueStatus = newQueueStatusAfter,
                    previousStateVersion = prevStateVersion,
                    stateVersion = newStateVersion,
                    servicePointId = linkedQueueEntry.CurrentServicePointId ?? linkedQueueEntry.ServicePointId,
                    roomId = plan.Room?.Id ?? plan.ServicePoint?.RoomId,
                    specimenCode = validated.SpecimenCode,
                    specimenType = validated.SpecimenType,
                    equipmentCode = validated.EquipmentCode,
                    executionNote = validated.ExecutionNote,
                    instruction = order.Instruction,
                    reason = validated.Reason,
                    performedByStaffId = staffAccess.Staff.Id,
                    performedByStaffCode = staffAccess.Staff.StaffCode,
                    performedByStaffName = staffAccess.Profile.FullName,
                    updatedAt = nowLocal,
                    ipAddress,
                };

                _db.AuditLogs.Add(new AuditLog
                {
                    ActorUserId = actorUserId,
                    HospitalId = hospitalId,
                    Action = auditAction,
                    EntityType = DiagnosticOrderEntityType,
                    EntityId = order.Id.ToString(CultureInfo.InvariantCulture),
                    Reason = validated.Reason,
                    ChangeData = JsonSerializer.Serialize(changePayload, AuditJsonOptions),
                    IpAddress = QueueEntryRequestParsing.ParseIpAddress(ipAddress),
                    OccurredAt = nowUtc,
                });

                await SaveChangesWithConcurrencyGuardAsync(ct);

                executionMetaByOrderId[order.Id] = MergeMetadataAfterUpdate(
                    executionMetaByOrderId.TryGetValue(order.Id, out var prevMeta) ? prevMeta : null,
                    validated, targetStatus, staffAccess, nowLocal);
                resultStateByOrderId.TryGetValue(order.Id, out var refreshedResultState);

                var item = BuildExecutionItemDto(
                    order, staffAccess, lookupBundle, executionMetaByOrderId[order.Id], refreshedResultState, referenceDate, nowLocal, forMutation: true);
                updatedItems.Add(item);

                if (index == 0)
                {
                    firstAction = auditAction;
                    firstOrderId = order.Id;
                    firstOrderCode = orderCode;
                    firstQueueEntryId = linkedQueueEntry.Id;
                    firstTicketNo = ticketNo;
                    firstPrevStatus = previousOrderStatus;
                    firstNewStatus = targetStatus;
                    firstPrevQueueStatus = previousQueueStatus;
                    firstNewQueueStatus = newQueueStatusAfter;
                    firstPrevStateVersion = prevStateVersion;
                    firstStateVersion = newStateVersion;
                }
            }

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

        var actionOutcome = firstAction;
        var message = targetStatus switch
        {
            "IN_PROGRESS" => plans.Count > 1
                ? $"Đã bắt đầu thực hiện {plans.Count} phiếu chỉ định cận lâm sàng."
                : $"Đã bắt đầu thực hiện phiếu chỉ định cận lâm sàng {firstOrderCode}.",
            "COMPLETED" => plans.Count > 1
                ? $"Đã hoàn tất kỹ thuật {plans.Count} phiếu chỉ định cận lâm sàng."
                : $"Đã hoàn tất kỹ thuật phiếu chỉ định cận lâm sàng {firstOrderCode}.",
            "CANCELLED" => plans.Count > 1
                ? $"Đã hủy thực hiện {plans.Count} phiếu chỉ định cận lâm sàng."
                : $"Đã hủy thực hiện phiếu chỉ định cận lâm sàng {firstOrderCode}.",
            _ => plans.Count > 1
                ? $"Đã cập nhật trạng thái thực hiện {plans.Count} phiếu chỉ định cận lâm sàng."
                : $"Đã cập nhật trạng thái thực hiện phiếu chỉ định cận lâm sàng {firstOrderCode}.",
        };

        return new UpdateDiagnosticServiceStatusResponseDto(
            ActionOutcome: actionOutcome,
            Action: firstAction,
            Message: message,
            UpdatedCount: updatedItems.Count,
            OrderId: firstOrderId,
            OrderCode: firstOrderCode,
            QueueEntryId: firstQueueEntryId,
            FormattedQueueEntryId: firstQueueEntryId.HasValue ? $"q-{firstQueueEntryId.Value:D3}" : null,
            TicketNo: firstTicketNo,
            PreviousStatus: firstPrevStatus,
            CurrentStatus: firstNewStatus,
            PreviousQueueStatus: firstPrevQueueStatus,
            CurrentQueueStatus: firstNewQueueStatus,
            PreviousStateVersion: firstPrevStateVersion,
            StateVersion: firstStateVersion,
            SpecimenCode: validated.SpecimenCode,
            SpecimenType: validated.SpecimenType,
            EquipmentCode: validated.EquipmentCode,
            ExecutionNote: validated.ExecutionNote,
            Reason: validated.Reason,
            UpdatedAtUtc: nowUtc,
            UpdatedByStaff: staffAccess.Profile,
            Order: updatedItems[0],
            UpdatedOrders: updatedItems);
    }

    // ======================= VALIDATION =======================

    private static (string TargetStatus, string? RequestedQueueStatus) NormalizeTargetStatus(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new ValidationException("Trạng thái thực hiện dịch vụ cận lâm sàng (targetStatus) không hợp lệ.");
        }

        var trimmed = raw.Trim().ToUpperInvariant();
        return trimmed switch
        {
            "IN_PROGRESS" or "START" or "STARTED" or "EXECUTING" or "PROCESSING" => ("IN_PROGRESS", null),
            "COMPLETED" or "COMPLETE" or "DONE" or "FINISHED" => ("COMPLETED", null),
            "CONFIRMED" or "READY" or "PAUSE" or "PAUSED" => ("CONFIRMED", "READY"),
            "WAITING" => ("CONFIRMED", "WAITING"),
            "CANCELLED" or "CANCELED" or "CANCEL" => ("CANCELLED", null),
            _ => throw new ValidationException($"Trạng thái thực hiện '{raw.Trim()}' không hợp lệ. Chỉ chấp nhận IN_PROGRESS, COMPLETED, CONFIRMED/READY/WAITING hoặc CANCELLED."),
        };
    }

    private static string ResolveQueueStatusForTarget(string targetStatus, string? requestedQueueStatus)
    {
        return targetStatus switch
        {
            "IN_PROGRESS" => "IN_PROGRESS",
            "COMPLETED" => "COMPLETED",
            "CANCELLED" => "CANCELLED",
            "CONFIRMED" => string.Equals(requestedQueueStatus, "WAITING", StringComparison.OrdinalIgnoreCase) ? "WAITING" : "READY",
            _ => "READY",
        };
    }

    private static ValidatedExecutionInput ValidateMutationRequest(
        UpdateDiagnosticServiceStatusRequest request,
        string targetStatus)
    {
        if (request.ExpectedStateVersion is < 1)
        {
            throw new ValidationException("Phiên bản trạng thái hàng đợi (ExpectedStateVersion) phải lớn hơn hoặc bằng 1.");
        }

        if (request.ServicePointId is <= 0)
        {
            throw new ValidationException("ID điểm phục vụ cận lâm sàng (ServicePointId) phải lớn hơn 0.");
        }

        if (request.RoomId is <= 0)
        {
            throw new ValidationException("ID phòng thực hiện cận lâm sàng (RoomId) phải lớn hơn 0.");
        }

        string? normalizedPresence = null;
        if (!string.IsNullOrWhiteSpace(request.PresenceStatus))
        {
            var trimmed = request.PresenceStatus.Trim().ToUpperInvariant();
            if (trimmed is not ("PRESENT" or "ABSENT" or "UNKNOWN"))
            {
                throw new ValidationException("Trạng thái hiện diện (PresenceStatus) không hợp lệ. Chỉ chấp nhận PRESENT, ABSENT hoặc UNKNOWN.");
            }

            if (targetStatus == "IN_PROGRESS" && trimmed == "ABSENT")
            {
                throw new ValidationException("Không thể bắt đầu thực hiện khi bệnh nhân vắng mặt (PresenceStatus = ABSENT).");
            }

            normalizedPresence = trimmed;
        }

        var specimenCode = TrimAndValidateLength(request.SpecimenCode ?? request.SampleCode, "Mã mẫu bệnh phẩm (SpecimenCode)", 60);
        var specimenType = TrimAndValidateLength(request.SpecimenType ?? request.Modality, "Loại mẫu bệnh phẩm (SpecimenType)", 100);
        var equipmentCode = TrimAndValidateLength(request.EquipmentCode ?? request.DeviceName, "Mã thiết bị (EquipmentCode)", 100);
        var executionNote = TrimAndValidateLength(
            request.ExecutionNote ?? request.TechnicalNote ?? request.Notes, "Ghi chú kỹ thuật (ExecutionNote)", 1000);
        var instruction = TrimAndValidateLength(request.Instruction, "Hướng dẫn thực hiện (Instruction)", 1000);
        var reason = TrimAndValidateLength(request.Reason ?? request.BlockedReason, "Lý do (Reason)", 500);

        var requiresReason = targetStatus == "CANCELLED" || targetStatus == "CONFIRMED";
        if (requiresReason)
        {
            if (string.IsNullOrWhiteSpace(reason) || reason.Length < 5)
            {
                throw new ValidationException(targetStatus == "CANCELLED"
                    ? "Vui lòng nhập lý do hủy thực hiện dịch vụ cận lâm sàng (tối thiểu 5 ký tự)."
                    : "Vui lòng nhập lý do tạm dừng thực hiện dịch vụ cận lâm sàng (tối thiểu 5 ký tự).");
            }
        }

        return new ValidatedExecutionInput(
            ServicePointId: request.ServicePointId,
            RoomId: request.RoomId,
            PresenceStatus: normalizedPresence,
            SpecimenCode: specimenCode,
            SpecimenType: specimenType,
            EquipmentCode: equipmentCode,
            ExecutionNote: executionNote,
            Instruction: instruction,
            Reason: reason,
            ExpectedStateVersion: request.ExpectedStateVersion);
    }

    private static string? TrimAndValidateLength(string? raw, string fieldLabel, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var trimmed = raw.Trim();
        if (trimmed.Contains('<') || trimmed.Contains('>'))
        {
            throw new ValidationException($"{fieldLabel} chứa ký tự không hợp lệ.");
        }

        if (trimmed.Length > maxLength)
        {
            throw new ValidationException($"{fieldLabel} không được vượt quá {maxLength} ký tự.");
        }

        return trimmed;
    }

    private async Task<ResolvedExecutionPlan> ValidateOrderForExecutionUpdateAsync(
        DiagnosticOrder order,
        ValidatedExecutionInput validated,
        string targetStatus,
        DiagnosticStaffAccessContext staffAccess,
        OrderLookupBundle lookupBundle,
        DiagnosticOrderResultState? resultState,
        DateOnly referenceDate,
        CancellationToken ct)
    {
        var hospitalId = staffAccess.Hospital.Id;
        var orderCode = FormatOrderCode(order, referenceDate);
        var currentOrderStatus = ResolveEffectiveOrderStatus(order, lookupBundle.FinalizedOrderIds);

        var visit = order.VisitId.HasValue && lookupBundle.VisitsById.TryGetValue(order.VisitId.Value, out var foundVisit)
            ? foundVisit
            : null;
        if (visit is null)
        {
            throw new ConflictException("Phiếu chỉ định cận lâm sàng không gắn với lượt khám hợp lệ.");
        }

        var visitStatus = AppointmentBookingRules.NormalizeStatusKey(visit.Status);
        if (visitStatus is "COMPLETED" or "DONE" or "CANCELLED" or "NO_SHOW")
        {
            throw new ConflictException($"Lượt khám đã kết thúc hoặc bị hủy ({visitStatus}), không thể cập nhật trạng thái thực hiện dịch vụ cận lâm sàng.");
        }

        var patient = visit.PatientId.HasValue && lookupBundle.PatientsById.TryGetValue(visit.PatientId.Value, out var foundPatient)
            ? foundPatient
            : null;
        if (patient is not null &&
            !string.IsNullOrWhiteSpace(patient.Status) &&
            !string.Equals(patient.Status.Trim(), "ACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException("Hồ sơ bệnh nhân hiện không ở trạng thái hoạt động.");
        }

        var service = order.ServiceId.HasValue && lookupBundle.ServicesById.TryGetValue(order.ServiceId.Value, out var foundService)
            ? foundService
            : null;
        if (service is null)
        {
            throw new NotFoundException("Service", order.ServiceId ?? 0);
        }

        if (!string.Equals(service.ServiceType?.Trim(), "DIAGNOSTIC", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException($"Dịch vụ '{service.Name}' ({service.ServiceCode}) không phải là dịch vụ cận lâm sàng (DIAGNOSTIC).");
        }

        if (service.IsActive == false)
        {
            throw new ConflictException($"Dịch vụ cận lâm sàng '{service.Name}' ({service.ServiceCode}) hiện đang ngừng hoạt động.");
        }

        if (service.DepartmentId.HasValue &&
            lookupBundle.DepartmentsById.TryGetValue(service.DepartmentId.Value, out var serviceDept) &&
            serviceDept.IsActive == false)
        {
            throw new ConflictException($"Khoa thực hiện dịch vụ '{service.Name}' hiện đang ngừng hoạt động.");
        }

        if (resultState?.IsResultFinalized == true)
        {
            throw new ConflictException($"Kết quả cận lâm sàng của phiếu chỉ định '{orderCode}' đã được khóa cuối (FINALIZED), không thể cập nhật trạng thái thực hiện.");
        }

        QueueEntry? linkedQueueEntry = null;
        if (order.QueueEntryId.HasValue)
        {
            linkedQueueEntry = await _db.QueueEntries
                .FirstOrDefaultAsync(q => q.Id == order.QueueEntryId.Value, ct)
                ?? (lookupBundle.QueueEntriesById.TryGetValue(order.QueueEntryId.Value, out var bundleQe) ? bundleQe : null);
        }

        if (linkedQueueEntry is not null &&
            !string.IsNullOrWhiteSpace(linkedQueueEntry.QueueType) &&
            !string.Equals(linkedQueueEntry.QueueType.Trim(), "DIAGNOSTIC", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException("Lượt xếp hàng liên kết với phiếu chỉ định không thuộc loại hàng đợi cận lâm sàng (DIAGNOSTIC).");
        }

        var currentQueueStatus = linkedQueueEntry is not null ? NormalizeQueueStatus(linkedQueueEntry.Status) : null;
        var currentStateVersion = linkedQueueEntry?.StateVersion is > 0 ? linkedQueueEntry.StateVersion!.Value : 1;
        if (validated.ExpectedStateVersion.HasValue && validated.ExpectedStateVersion.Value != currentStateVersion)
        {
            throw new ConflictException("Dữ liệu hàng đợi cận lâm sàng đã thay đổi bởi thao tác khác (state_version mismatch). Vui lòng tải lại.");
        }

        // State machine guards
        switch (currentOrderStatus)
        {
            case "ORDERED":
                throw new ConflictException("Phiếu chỉ định chưa được xác nhận (ORDERED). Vui lòng xác nhận phiếu chỉ định (UC39) trước khi cập nhật trạng thái thực hiện.");
            case "CONFIRMED":
                if (targetStatus == "COMPLETED")
                {
                    throw new ConflictException("Phiếu chỉ định chưa bắt đầu thực hiện (CONFIRMED). Vui lòng chuyển sang trạng thái Đang thực hiện (IN_PROGRESS) trước khi hoàn tất.");
                }
                if (targetStatus == "CONFIRMED")
                {
                    throw new ConflictException($"Phiếu chỉ định '{orderCode}' đã ở trạng thái Đã xác nhận (CONFIRMED), không cần cập nhật.");
                }
                if (targetStatus == "IN_PROGRESS" && currentQueueStatus is "SKIPPED" or "COMPLETED" or "CANCELLED")
                {
                    throw new ConflictException(currentQueueStatus == "SKIPPED"
                        ? "Lượt xếp hàng cận lâm sàng đang ở trạng thái SKIPPED. Vui lòng khôi phục lượt (UC41) trước khi bắt đầu thực hiện."
                        : $"Lượt xếp hàng cận lâm sàng đang ở trạng thái '{currentQueueStatus}', không thể bắt đầu thực hiện.");
                }
                break;
            case "IN_PROGRESS":
                if (targetStatus == "IN_PROGRESS")
                {
                    throw new ConflictException("Phiếu chỉ định đang ở trạng thái Đang thực hiện (IN_PROGRESS).");
                }
                break;
            case "COMPLETED":
            case "CANCELLED":
                throw new ConflictException($"Phiếu chỉ định '{orderCode}' đã ở trạng thái kết thúc ({currentOrderStatus}), không thể cập nhật trạng thái thực hiện.");
            default:
                throw new ConflictException($"Phiếu chỉ định '{orderCode}' đang ở trạng thái '{currentOrderStatus}', không hợp lệ để cập nhật trạng thái thực hiện.");
        }

        // Resolve requested service point / room
        QueueServicePoint? resolvedSp = null;
        HospitalRoom? resolvedRoom = null;

        if (validated.ServicePointId.HasValue)
        {
            try
            {
                resolvedSp = await _db.QueueServicePoints
                    .AsNoTracking()
                    .FirstOrDefaultAsync(sp => sp.Id == validated.ServicePointId.Value, ct);
            }
            catch (NotSupportedException)
            {
                resolvedSp = null;
            }

            if (resolvedSp is null)
            {
                throw new NotFoundException("QueueServicePoint", validated.ServicePointId.Value);
            }

            if (resolvedSp.HospitalId.HasValue && resolvedSp.HospitalId.Value != hospitalId)
            {
                throw new ForbiddenException("Điểm phục vụ cận lâm sàng thuộc cơ sở y tế khác.");
            }

            if (!string.Equals(resolvedSp.PointType?.Trim(), "DIAGNOSTIC", StringComparison.OrdinalIgnoreCase))
            {
                throw new ValidationException("Điểm phục vụ được chọn không phải là điểm phục vụ cận lâm sàng (DIAGNOSTIC).");
            }

            if (resolvedSp.ServiceId.HasValue && resolvedSp.ServiceId.Value != service.Id)
            {
                throw new ConflictException($"Điểm phục vụ '{resolvedSp.Name}' không tương thích với dịch vụ cận lâm sàng '{service.Name}'.");
            }

            if (resolvedSp.IsActive == false)
            {
                throw new ConflictException($"Điểm phục vụ cận lâm sàng '{resolvedSp.Name}' hiện đang ngừng hoạt động.");
            }
        }

        if (validated.RoomId.HasValue)
        {
            resolvedRoom = await _db.HospitalRooms
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == validated.RoomId.Value, ct)
                ?? throw new NotFoundException("HospitalRoom", validated.RoomId.Value);

            if (resolvedRoom.HospitalId.HasValue && resolvedRoom.HospitalId.Value != hospitalId)
            {
                throw new ForbiddenException("Phòng thực hiện cận lâm sàng thuộc cơ sở y tế khác.");
            }

            if (!string.Equals(resolvedRoom.RoomType?.Trim(), "DIAGNOSTIC", StringComparison.OrdinalIgnoreCase))
            {
                throw new ValidationException($"Phòng '{resolvedRoom.RoomCode}' không phải là phòng cận lâm sàng (DIAGNOSTIC).");
            }

            if (!string.Equals(resolvedRoom.Status?.Trim(), "ACTIVE", StringComparison.OrdinalIgnoreCase))
            {
                throw new ConflictException($"Phòng thực hiện cận lâm sàng '{resolvedRoom.RoomCode}' hiện đang bảo trì hoặc ngừng hoạt động.");
            }

            if (resolvedSp is not null && resolvedSp.RoomId.HasValue && resolvedSp.RoomId.Value != resolvedRoom.Id)
            {
                throw new ValidationException("Phòng thực hiện (RoomId) không khớp với điểm phục vụ cận lâm sàng (ServicePointId) đã chọn.");
            }

            if (resolvedSp is null)
            {
                resolvedSp = lookupBundle.HospitalDiagnosticServicePoints
                    .Where(sp => sp.RoomId == resolvedRoom.Id && (!sp.ServiceId.HasValue || sp.ServiceId.Value == service.Id))
                    .OrderBy(sp => sp.IsActive == false ? 1 : 0)
                    .ThenBy(sp => sp.Id)
                    .FirstOrDefault();

                if (resolvedSp is not null && resolvedSp.IsActive == false)
                {
                    throw new ConflictException($"Điểm phục vụ cận lâm sàng '{resolvedSp.Name}' hiện đang ngừng hoạt động.");
                }
            }
        }

        if (resolvedSp is null)
        {
            var existingSpId = linkedQueueEntry?.CurrentServicePointId ?? linkedQueueEntry?.ServicePointId;
            if (existingSpId.HasValue && lookupBundle.ServicePointsById.TryGetValue(existingSpId.Value, out var existingSp))
            {
                resolvedSp = existingSp;
            }
            else
            {
                resolvedSp = ResolveDiagnosticServicePointForCatalog(service.Id, lookupBundle.HospitalDiagnosticServicePoints);
            }
        }

        if (resolvedSp is not null && resolvedSp.IsActive == false)
        {
            throw new ConflictException($"Điểm phục vụ cận lâm sàng cho dịch vụ '{service.Name}' hiện đang ngừng hoạt động.");
        }

        if (resolvedRoom is null && resolvedSp?.RoomId.HasValue == true)
        {
            lookupBundle.RoomsById.TryGetValue(resolvedSp.RoomId.Value, out resolvedRoom);
        }

        if (resolvedRoom is not null &&
            !string.Equals(resolvedRoom.Status?.Trim(), "ACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException($"Phòng thực hiện cận lâm sàng '{resolvedRoom.RoomCode}' cho dịch vụ '{service.Name}' hiện đang bảo trì hoặc ngừng hoạt động.");
        }

        // Service-point busy guard for CONFIRMED -> IN_PROGRESS: another visit's active entry on same SP/date blocks.
        if (targetStatus == "IN_PROGRESS" && currentOrderStatus == "CONFIRMED" && resolvedSp is not null)
        {
            var spId = resolvedSp.Id;
            var queueDate = linkedQueueEntry?.QueueDate ?? referenceDate;
            var busyConflict = await _db.QueueEntries
                .AsNoTracking()
                .Where(q =>
                    q.Id != (linkedQueueEntry != null ? linkedQueueEntry.Id : 0) &&
                    q.QueueType != null && q.QueueType.ToUpper() == "DIAGNOSTIC" &&
                    q.QueueDate == queueDate &&
                    (q.ServicePointId == spId || q.CurrentServicePointId == spId) &&
                    q.Status != null && q.Status.ToUpper() == "IN_PROGRESS")
                .ToListAsync(ct);

            var sameVisitOnly = busyConflict.All(q => q.VisitId.HasValue && q.VisitId.Value == visit.Id);
            if (busyConflict.Count > 0 && !sameVisitOnly)
            {
                throw new ConflictException($"Điểm phục vụ '{resolvedSp.Name}' đang thực hiện lượt khám khác. Vui lòng hoàn tất hoặc tạm dừng lượt đang thực hiện trước khi bắt đầu phiếu chỉ định '{orderCode}'.");
            }
        }

        var category = ResolveDiagnosticCategory(service, lookupBundle.SpecialtiesById);

        return new ResolvedExecutionPlan(
            Order: order,
            Visit: visit,
            Patient: patient,
            Service: service,
            Category: category,
            ServicePoint: resolvedSp,
            Room: resolvedRoom,
            LinkedQueueEntry: linkedQueueEntry,
            CurrentOrderStatus: currentOrderStatus);
    }

    // ======================= DTO BUILDERS =======================

    private DiagnosticServiceExecutionItemDto BuildExecutionItemDto(
        DiagnosticOrder order,
        DiagnosticStaffAccessContext staffAccess,
        OrderLookupBundle lookupBundle,
        DiagnosticOrderExecutionMetadata? meta,
        DiagnosticOrderResultState? resultState,
        DateOnly referenceDate,
        DateTime nowLocal,
        bool forMutation)
    {
        var visit = order.VisitId.HasValue && lookupBundle.VisitsById.TryGetValue(order.VisitId.Value, out var foundVisit)
            ? foundVisit
            : null;
        var visitId = visit?.Id ?? order.VisitId ?? 0;
        var visitCode = visit is not null
            ? FormatVisitCode(visit, referenceDate)
            : (visitId > 0 ? $"VIS-{referenceDate:yyyyMM}-{visitId:D4}" : string.Empty);
        var visitStatus = visit is not null ? NormalizeVisitStatus(visit.Status) : "UNKNOWN";

        var patient = visit?.PatientId.HasValue == true &&
                      lookupBundle.PatientsById.TryGetValue(visit.PatientId.Value, out var foundPatient)
            ? foundPatient
            : null;

        lookupBundle.LatestRevisionByVisitId.TryGetValue(visitId, out var latestRevision);

        var patientSummary = new DiagnosticOrderPatientSummaryDto(
            PatientId: patient?.Id ?? visit?.PatientId ?? 0,
            PatientCode: patient?.PatientCode ?? string.Empty,
            FullName: patient?.FullName ?? string.Empty,
            DateOfBirth: patient?.DateOfBirth,
            Age: CalculatePatientAge(patient?.DateOfBirth, referenceDate),
            Gender: patient?.Gender,
            Phone: patient?.Phone,
            BloodType: patient?.BloodType,
            Allergies: latestRevision?.RelevantHistoryAllergies);

        var service = order.ServiceId.HasValue && lookupBundle.ServicesById.TryGetValue(order.ServiceId.Value, out var foundService)
            ? foundService
            : null;
        var dept = service?.DepartmentId.HasValue == true &&
                   lookupBundle.DepartmentsById.TryGetValue(service.DepartmentId.Value, out var foundDept)
            ? foundDept
            : null;
        var spec = service?.SpecialtyId.HasValue == true &&
                   lookupBundle.SpecialtiesById.TryGetValue(service.SpecialtyId.Value, out var foundSpec)
            ? foundSpec
            : null;

        var orderingDoctor = order.OrderingDoctorId.HasValue &&
                             lookupBundle.DoctorsById.TryGetValue(order.OrderingDoctorId.Value, out var foundDoc)
            ? foundDoc
            : (visit?.AssignedDoctorId.HasValue == true && lookupBundle.DoctorsById.TryGetValue(visit.AssignedDoctorId.Value, out var visitDoc)
                ? visitDoc
                : null);
        var formattedDocName = FormatDoctorName(orderingDoctor);

        var queueEntry = order.QueueEntryId.HasValue &&
                         lookupBundle.QueueEntriesById.TryGetValue(order.QueueEntryId.Value, out var foundQe)
            ? foundQe
            : null;

        var spId = queueEntry?.CurrentServicePointId ?? queueEntry?.ServicePointId;
        var sp = spId.HasValue && lookupBundle.ServicePointsById.TryGetValue(spId.Value, out var foundSp)
            ? foundSp
            : (service is not null
                ? ResolveDiagnosticServicePointForCatalog(service.Id, lookupBundle.HospitalDiagnosticServicePoints)
                : null);
        var room = sp?.RoomId.HasValue == true && lookupBundle.RoomsById.TryGetValue(sp.RoomId.Value, out var foundRoom)
            ? foundRoom
            : null;

        var unitPrice = service is not null && lookupBundle.ServicePricesByServiceId.TryGetValue(service.Id, out var price)
            ? price
            : 0m;
        var category = service is not null ? ResolveDiagnosticCategory(service, lookupBundle.SpecialtiesById) : "LABORATORY";
        var categoryLabel = FormatCategoryLabel(category);
        var orderCode = FormatOrderCode(order, referenceDate);
        var effectiveStatus = ResolveEffectiveOrderStatus(order, lookupBundle.FinalizedOrderIds);
        var priority = NormalizePriority(order.Priority);
        var priorityLabel = priority switch
        {
            "STAT" => "Cấp cứu STAT",
            "URGENT" => "Khẩn",
            _ => "Bình thường",
        };
        var statusLabel = effectiveStatus switch
        {
            "CONFIRMED" => "Đã xác nhận",
            "IN_PROGRESS" => "Đang thực hiện",
            "COMPLETED" => "Đã hoàn tất",
            "CANCELLED" => "Đã hủy",
            _ => "Chờ xác nhận",
        };

        var confirmedStaff = order.ConfirmedByStaffId.HasValue &&
                             lookupBundle.StaffById.TryGetValue(order.ConfirmedByStaffId.Value, out var foundConfirmedStaff)
            ? foundConfirmedStaff
            : null;

        var queueNumber = queueEntry?.QueueNumber;
        var ticketNo = queueNumber is > 0 ? $"C{queueNumber.Value:D3}" : null;
        var queueStatus = queueEntry is not null ? NormalizeQueueStatus(queueEntry.Status) : null;
        var queueStatusLabel = queueStatus switch
        {
            "WAITING" => "Chờ gọi",
            "RESERVED" => "Đã đặt chỗ",
            "READY" => "Sẵn sàng",
            "CALLED" => "Đã gọi",
            "IN_PROGRESS" => "Đang thực hiện",
            "SKIPPED" => "Bỏ lượt",
            "COMPLETED" => "Đã xong",
            "CANCELLED" => "Đã hủy",
            _ => queueStatus,
        };
        var presenceStatus = queueEntry is not null
            ? (string.IsNullOrWhiteSpace(queueEntry.PresenceStatus) ? "PRESENT" : queueEntry.PresenceStatus.Trim().ToUpperInvariant())
            : null;
        var manualPriority = queueEntry is not null
            ? ClinicalQueueRules.NormalizeStoredPriority(queueEntry.ManualPriority)
            : null;
        var effectiveClinicalPriority = queueEntry is not null
            ? ClinicalQueueRules.ResolveEffectivePriority(queueEntry.ManualPriority, patient?.DateOfBirth, referenceDate)
            : null;
        var stateVersion = queueEntry?.StateVersion is > 0 ? queueEntry.StateVersion.Value : 1;

        lookupBundle.AuditMetadataByOrderId.TryGetValue(order.Id, out var baseMeta);
        var sequenceNo = baseMeta?.OrderSequenceNo
            ?? (lookupBundle.SequenceByOrderId.TryGetValue(order.Id, out var computedSeq) ? computedSeq : 1);
        var isAdditionalOrder = baseMeta is not null ? baseMeta.IsAdditionalOrder : sequenceNo > 1;

        var startedAt = meta?.StartedAt ?? queueEntry?.StartedAt;
        var completedAt = meta?.CompletedAt ?? queueEntry?.CompletedAt;
        var startedByStaffId = meta?.StartedByStaffId;
        var completedByStaffId = meta?.CompletedByStaffId;
        var startedStaff = startedByStaffId.HasValue && lookupBundle.StaffById.TryGetValue(startedByStaffId.Value, out var s1) ? s1 : null;
        var completedStaff = completedByStaffId.HasValue && lookupBundle.StaffById.TryGetValue(completedByStaffId.Value, out var s2) ? s2 : null;

        var hasResult = resultState?.HasResult == true;
        var isResultFinalized = resultState?.IsResultFinalized == true;
        var resultStatus = resultState?.ResultStatus ?? "NONE";

        var eligibility = EvaluateExecutionEligibility(
            order, orderCode, effectiveStatus, visit, patient, service, dept, sp, room, queueEntry,
            isResultFinalized, staffAccess.HasExecutionPermission, currentQueueStatus: queueStatus);

        return new DiagnosticServiceExecutionItemDto(
            OrderId: order.Id,
            OrderCode: orderCode,
            VisitId: visitId,
            VisitCode: visitCode,
            VisitStatus: visitStatus,
            Patient: patientSummary,
            OrderingDoctorId: orderingDoctor?.Id ?? order.OrderingDoctorId,
            OrderingDoctorCode: orderingDoctor?.DoctorCode,
            OrderingDoctorName: formattedDocName,
            ServiceId: service?.Id ?? order.ServiceId ?? 0,
            ServiceCode: service?.ServiceCode ?? string.Empty,
            ServiceName: service?.Name ?? string.Empty,
            Category: category,
            CategoryLabel: categoryLabel,
            DepartmentId: dept?.Id ?? service?.DepartmentId,
            DepartmentCode: dept?.DepartmentCode,
            DepartmentName: dept?.Name ?? DefaultDepartmentName,
            SpecialtyId: spec?.Id ?? service?.SpecialtyId,
            SpecialtyCode: spec?.SpecialtyCode,
            SpecialtyName: spec?.Name ?? "Cận lâm sàng",
            UnitPrice: unitPrice,
            RequiresResultVerification: service?.RequiresResultVerification ?? true,
            Priority: priority,
            PriorityLabel: priorityLabel,
            Status: effectiveStatus,
            StatusLabel: statusLabel,
            RequiredForConclusion: order.RequiredForConclusion ?? true,
            Instruction: order.Instruction,
            OrderedAt: order.OrderedAt ?? nowLocal,
            ConfirmedAt: order.ConfirmedAt,
            ConfirmedByStaffId: order.ConfirmedByStaffId,
            ConfirmedByStaffCode: confirmedStaff?.StaffCode,
            ConfirmedByStaffName: confirmedStaff?.FullName,
            ConfirmationNote: baseMeta?.ConfirmationNote,
            ReceivedAt: baseMeta?.ReceivedAt,
            ReceivedByStaffId: baseMeta?.ReceivedByStaffId,
            ReceivedByStaffCode: baseMeta?.ReceivedByStaffCode,
            ReceivedByStaffName: baseMeta?.ReceivedByStaffName,
            ReceptionNote: baseMeta?.ReceptionNote,
            StartedAt: startedAt,
            StartedByStaffId: startedByStaffId,
            StartedByStaffCode: meta?.StartedByStaffCode ?? startedStaff?.StaffCode,
            StartedByStaffName: meta?.StartedByStaffName ?? startedStaff?.FullName,
            CompletedAt: completedAt,
            CompletedByStaffId: completedByStaffId,
            CompletedByStaffCode: meta?.CompletedByStaffCode ?? completedStaff?.StaffCode,
            CompletedByStaffName: meta?.CompletedByStaffName ?? completedStaff?.FullName,
            SpecimenCode: meta?.SpecimenCode,
            SpecimenType: meta?.SpecimenType,
            EquipmentCode: meta?.EquipmentCode,
            ExecutionNote: meta?.ExecutionNote,
            LastTransitionReason: meta?.LastTransitionReason,
            QueueEntryId: order.QueueEntryId,
            FormattedQueueEntryId: order.QueueEntryId.HasValue ? $"q-{order.QueueEntryId.Value:D3}" : null,
            TicketNo: ticketNo,
            QueueNumber: queueNumber,
            QueueDate: queueEntry?.QueueDate,
            QueueStatus: queueStatus,
            QueueStatusLabel: queueStatusLabel,
            PresenceStatus: presenceStatus,
            ManualPriority: manualPriority,
            EffectiveClinicalPriority: effectiveClinicalPriority,
            ArrivedAt: queueEntry?.ArrivedAt,
            ReadyAt: queueEntry?.ReadyAt,
            CalledAt: queueEntry?.CalledAt,
            StateVersion: stateVersion,
            ServicePointId: sp?.Id ?? spId,
            FormattedServicePointId: (sp?.Id ?? spId) is { } optSpId ? $"sp-{optSpId:D3}" : null,
            ServicePointName: sp?.Name,
            RoomId: room?.Id ?? sp?.RoomId,
            RoomCode: room?.RoomCode,
            RoomName: room?.Name,
            RoomLocation: room?.Location,
            HasResult: hasResult,
            IsResultFinalized: isResultFinalized,
            ResultStatus: resultStatus,
            IsAdditionalOrder: isAdditionalOrder,
            OrderSequenceNo: sequenceNo,
            FollowUpToOrderId: baseMeta?.FollowUpToOrderId,
            FollowUpToOrderCode: baseMeta?.FollowUpToOrderCode,
            AdditionalOrderReason: baseMeta?.AdditionalOrderReason,
            CanUpdateStatus: eligibility.CanUpdateStatus,
            CanStartExecution: eligibility.CanStartExecution,
            CanCompleteExecution: eligibility.CanCompleteExecution,
            CanPauseExecution: eligibility.CanPauseExecution,
            CanCancelExecution: eligibility.CanCancelExecution,
            CanEnterResult: effectiveStatus == "IN_PROGRESS" && !isResultFinalized && staffAccess.HasExecutionPermission,
            DisabledReason: eligibility.DisabledReason,
            AllowedTargetStatuses: eligibility.AllowedTargetStatuses,
            Eligibility: eligibility);
    }

    private static DiagnosticServiceExecutionEligibilityDto EvaluateExecutionEligibility(
        DiagnosticOrder order,
        string orderCode,
        string effectiveOrderStatus,
        Visit? visit,
        Patient? patient,
        Service? service,
        Department? serviceDept,
        QueueServicePoint? servicePoint,
        HospitalRoom? room,
        QueueEntry? queueEntry,
        bool isResultFinalized,
        bool hasExecutionPermission,
        string? currentQueueStatus)
    {
        var isOrderConfirmedOrInProgress = effectiveOrderStatus is "CONFIRMED" or "IN_PROGRESS";
        var normalizedVisitStatus = visit is not null ? AppointmentBookingRules.NormalizeStatusKey(visit.Status) : string.Empty;
        var isVisitActive = visit is not null &&
            normalizedVisitStatus is not ("COMPLETED" or "DONE" or "CANCELLED" or "NO_SHOW");
        var isPatientActive = patient is null ||
            string.IsNullOrWhiteSpace(patient.Status) ||
            string.Equals(patient.Status.Trim(), "ACTIVE", StringComparison.OrdinalIgnoreCase);
        var isServiceActive = service is not null &&
            string.Equals(service.ServiceType?.Trim(), "DIAGNOSTIC", StringComparison.OrdinalIgnoreCase) &&
            service.IsActive != false;
        var isDepartmentActive = serviceDept is null || serviceDept.IsActive != false;
        var isServicePointActive = servicePoint is null || servicePoint.IsActive != false;
        var isRoomActive = room is null ||
            string.Equals(room.Status?.Trim(), "ACTIVE", StringComparison.OrdinalIgnoreCase);
        var isQueueEligible = queueEntry is null ||
            string.IsNullOrWhiteSpace(queueEntry.QueueType) ||
            string.Equals(queueEntry.QueueType.Trim(), "DIAGNOSTIC", StringComparison.OrdinalIgnoreCase);
        var isResultNotFinalized = !isResultFinalized;

        var baseEligible = isOrderConfirmedOrInProgress && isVisitActive && isPatientActive && isServiceActive &&
            isDepartmentActive && isServicePointActive && isRoomActive && isQueueEligible &&
            isResultNotFinalized && hasExecutionPermission;

        var allowedTargets = new List<string>(3);
        if (effectiveOrderStatus == "CONFIRMED" && baseEligible)
        {
            if (currentQueueStatus is not ("SKIPPED" or "COMPLETED" or "CANCELLED"))
            {
                allowedTargets.Add("IN_PROGRESS");
            }
            allowedTargets.Add("CANCELLED");
        }
        else if (effectiveOrderStatus == "IN_PROGRESS" && baseEligible)
        {
            allowedTargets.Add("COMPLETED");
            allowedTargets.Add("CONFIRMED");
            allowedTargets.Add("CANCELLED");
        }

        var canStart = effectiveOrderStatus == "CONFIRMED" && baseEligible &&
            currentQueueStatus is not ("SKIPPED" or "COMPLETED" or "CANCELLED");
        var canComplete = effectiveOrderStatus == "IN_PROGRESS" && baseEligible;
        var canPause = effectiveOrderStatus == "IN_PROGRESS" && baseEligible;
        var canCancel = isOrderConfirmedOrInProgress && baseEligible;
        var canUpdate = allowedTargets.Count > 0;

        string? disabledReason = null;
        if (!hasExecutionPermission)
        {
            disabledReason = "Nhân viên cận lâm sàng không có quyền cập nhật trạng thái thực hiện dịch vụ (QUEUE_MANAGE hoặc DIAGNOSTIC_RESULT_WRITE).";
        }
        else if (!isOrderConfirmedOrInProgress)
        {
            disabledReason = effectiveOrderStatus switch
            {
                "ORDERED" => $"Phiếu chỉ định '{orderCode}' chưa được xác nhận (ORDERED). Vui lòng xác nhận phiếu chỉ định (UC39) trước.",
                "COMPLETED" => $"Phiếu chỉ định '{orderCode}' đã hoàn tất thực hiện (COMPLETED).",
                "CANCELLED" => $"Phiếu chỉ định '{orderCode}' đã bị hủy (CANCELLED).",
                _ => $"Phiếu chỉ định '{orderCode}' đang ở trạng thái '{effectiveOrderStatus}'.",
            };
        }
        else if (!isVisitActive)
        {
            disabledReason = "Lượt khám đã kết thúc hoặc bị hủy, không thể cập nhật trạng thái thực hiện.";
        }
        else if (!isPatientActive)
        {
            disabledReason = "Hồ sơ bệnh nhân hiện không ở trạng thái hoạt động.";
        }
        else if (!isServiceActive)
        {
            disabledReason = service is null
                ? "Không tìm thấy dịch vụ cận lâm sàng."
                : $"Dịch vụ '{service.Name}' hiện đang ngừng hoạt động hoặc không thuộc loại DIAGNOSTIC.";
        }
        else if (!isResultNotFinalized)
        {
            disabledReason = $"Kết quả cận lâm sàng của phiếu '{orderCode}' đã khóa cuối (FINALIZED).";
        }
        else if (!isServicePointActive)
        {
            disabledReason = "Điểm phục vụ cận lâm sàng hiện đang ngừng hoạt động.";
        }
        else if (!isRoomActive)
        {
            disabledReason = "Phòng thực hiện cận lâm sàng hiện đang bảo trì hoặc ngừng hoạt động.";
        }
        else if (effectiveOrderStatus == "CONFIRMED" && currentQueueStatus == "SKIPPED")
        {
            disabledReason = "Lượt xếp hàng đang SKIPPED. Vui lòng khôi phục lượt (UC41) trước khi bắt đầu thực hiện.";
        }

        return new DiagnosticServiceExecutionEligibilityDto(
            IsEligible: canUpdate,
            CanUpdateStatus: canUpdate,
            CanStartExecution: canStart,
            CanCompleteExecution: canComplete,
            CanPauseExecution: canPause,
            CanCancelExecution: canCancel,
            DisabledReason: disabledReason,
            AllowedTargetStatuses: allowedTargets,
            IsOrderConfirmedOrInProgress: isOrderConfirmedOrInProgress,
            IsQueueEligible: isQueueEligible,
            IsVisitActive: isVisitActive,
            IsPatientActive: isPatientActive,
            IsServiceActive: isServiceActive,
            IsDepartmentActive: isDepartmentActive,
            IsServicePointActive: isServicePointActive,
            IsRoomActive: isRoomActive,
            IsResultNotFinalized: isResultNotFinalized,
            HasExecutionPermission: hasExecutionPermission);
    }

    private async Task<IReadOnlyList<DiagnosticServiceExecutionHistoryItemDto>> BuildExecutionHistoryAsync(
        DiagnosticOrder order,
        OrderLookupBundle lookupBundle,
        DateOnly referenceDate,
        CancellationToken ct)
    {
        var orderIdStr = order.Id.ToString(CultureInfo.InvariantCulture);
        var queueIdStr = order.QueueEntryId?.ToString(CultureInfo.InvariantCulture);
        var orderCode = FormatOrderCode(order, referenceDate);

        var logs = new List<AuditLog>();
        try
        {
            logs = await _db.AuditLogs
                .AsNoTracking()
                .Where(a =>
                    (a.EntityType == DiagnosticOrderEntityType && a.EntityId == orderIdStr) ||
                    (queueIdStr != null && a.EntityType == QueueEntryEntityType && a.EntityId == queueIdStr))
                .OrderByDescending(a => a.OccurredAt)
                .ThenByDescending(a => a.Id)
                .ToListAsync(ct);
        }
        catch (NotSupportedException)
        {
            return [];
        }

        var userIds = logs.Where(l => l.ActorUserId.HasValue).Select(l => l.ActorUserId!.Value).Distinct().ToList();
        var staffByUserId = new Dictionary<long, Staff>();
        if (userIds.Count > 0)
        {
            try
            {
                var staffList = await _db.Staff.AsNoTracking().Where(s => s.UserId.HasValue && userIds.Contains(s.UserId.Value)).ToListAsync(ct);
                foreach (var s in staffList)
                {
                    if (s.UserId.HasValue)
                    {
                        staffByUserId[s.UserId.Value] = s;
                    }
                }
            }
            catch (NotSupportedException)
            {
                // ignore
            }
        }

        var items = new List<DiagnosticServiceExecutionHistoryItemDto>(logs.Count);
        foreach (var log in logs)
        {
            string? prevStatus = null, currStatus = null, prevQueue = null, currQueue = null;
            int? prevVersion = null;
            int version = 0;
            string? specimenCode = null, specimenType = null, equipmentCode = null, execNote = null, reason = null;
            long? performedByStaffId = null;
            string? performedByCode = null, performedByName = null;

            if (!string.IsNullOrWhiteSpace(log.ChangeData))
            {
                try
                {
                    using var doc = JsonDocument.Parse(log.ChangeData);
                    var root = doc.RootElement;
                    if (root.ValueKind == JsonValueKind.Object)
                    {
                        prevStatus = GetJsonString(root, "previousStatus") ?? GetJsonString(root, "fromStatus");
                        currStatus = GetJsonString(root, "targetStatus") ?? GetJsonString(root, "newStatus") ?? GetJsonString(root, "orderStatus");
                        prevQueue = GetJsonString(root, "previousQueueStatus");
                        currQueue = GetJsonString(root, "targetQueueStatus") ?? GetJsonString(root, "newQueueStatus");
                        prevVersion = GetJsonInt(root, "previousStateVersion");
                        version = GetJsonInt(root, "stateVersion") ?? 0;
                        specimenCode = GetJsonString(root, "specimenCode");
                        specimenType = GetJsonString(root, "specimenType");
                        equipmentCode = GetJsonString(root, "equipmentCode");
                        execNote = GetJsonString(root, "executionNote");
                        reason = GetJsonString(root, "reason") ?? log.Reason;
                        performedByStaffId = GetJsonLong(root, "performedByStaffId");
                        performedByCode = GetJsonString(root, "performedByStaffCode");
                        performedByName = GetJsonString(root, "performedByStaffName");
                    }
                }
                catch (JsonException)
                {
                    // ignore malformed JSON
                }
            }

            reason ??= log.Reason;
            if (performedByStaffId is null && log.ActorUserId.HasValue && staffByUserId.TryGetValue(log.ActorUserId.Value, out var actorStaff))
            {
                performedByStaffId = actorStaff.Id;
                performedByCode ??= actorStaff.StaffCode;
                performedByName ??= actorStaff.FullName;
            }

            items.Add(new DiagnosticServiceExecutionHistoryItemDto(
                AuditLogId: log.Id,
                OrderId: order.Id,
                OrderCode: orderCode,
                QueueEntryId: order.QueueEntryId,
                Action: log.Action ?? string.Empty,
                ActionLabel: FormatAuditActionLabel(log.Action),
                PreviousStatus: prevStatus ?? string.Empty,
                CurrentStatus: currStatus ?? string.Empty,
                PreviousQueueStatus: prevQueue,
                CurrentQueueStatus: currQueue,
                PreviousStateVersion: prevVersion,
                StateVersion: version,
                SpecimenCode: specimenCode,
                SpecimenType: specimenType,
                EquipmentCode: equipmentCode,
                ExecutionNote: execNote,
                Reason: reason,
                ActorUserId: log.ActorUserId,
                PerformedByStaffId: performedByStaffId,
                PerformedByStaffCode: performedByCode,
                PerformedByStaffName: performedByName,
                OccurredAtUtc: log.OccurredAt));
        }

        return items;
    }

    private async Task<DiagnosticOrderClinicalSummaryDto> BuildClinicalSummaryAsync(
        Visit? visit,
        long visitId,
        string visitCode,
        string visitStatus,
        OrderLookupBundle lookupBundle,
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

        lookupBundle.LatestRevisionByVisitId.TryGetValue(visit.Id, out var latestRevision);

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

    // ======================= AUTHZ =======================

    private async Task<DiagnosticStaffAccessContext> EnsureDiagnosticStaffAccessAsync(
        long actorUserId,
        DateTime nowUtc,
        bool forMutation,
        CancellationToken ct)
    {
        if (actorUserId <= 0)
        {
            throw new UnauthorizedAccessException("Thông tin xác thực nhân viên cận lâm sàng không hợp lệ.");
        }

        var actorUser = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == actorUserId, ct)
            ?? throw new NotFoundException("USER_NOT_FOUND", actorUserId);

        if (actorUser.AccountStatus != AccountStatus.ACTIVE)
        {
            throw new ForbiddenException("Tài khoản nhân viên cận lâm sàng không ở trạng thái hoạt động.");
        }

        if (actorUser.LockedUntil.HasValue && actorUser.LockedUntil.Value > nowUtc)
        {
            throw new ForbiddenException("Tài khoản nhân viên cận lâm sàng đang bị tạm khóa.");
        }

        var allRoles = await _db.Roles.AsNoTracking().OrderBy(r => r.Id).ToListAsync(ct);
        var actorRoleIds = (await _db.UserRoles.AsNoTracking().Where(ur => ur.UserId == actorUserId).Select(ur => ur.RoleId).ToListAsync(ct)).ToHashSet();
        var actorRoleCodes = allRoles.Where(r => actorRoleIds.Contains(r.Id)).Select(r => r.Code).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(c => c, StringComparer.Ordinal).ToList();

        if (!actorRoleCodes.Any(c => string.Equals(c, DiagnosticStaffRoleCode, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ForbiddenException("Tài khoản không có vai trò nhân viên cận lâm sàng (DIAGNOSTIC_STAFF).");
        }

        var actorStaff = await _db.Staff.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == actorUserId, ct);
        if (actorStaff is null || !actorStaff.HospitalId.HasValue)
        {
            throw new ForbiddenException("Tài khoản nhân viên cận lâm sàng chưa được gán cơ sở y tế.");
        }

        if (!string.Equals(actorStaff.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            throw new ForbiddenException("Hồ sơ nhân sự của nhân viên cận lâm sàng đã ngừng hoạt động.");
        }

        var hospitalId = actorStaff.HospitalId.Value;
        var hospital = await _db.Hospitals.AsNoTracking().FirstOrDefaultAsync(h => h.Id == hospitalId, ct)
            ?? throw new NotFoundException("Hospital", hospitalId);

        if (hospital.IsActive == false)
        {
            throw new ForbiddenException("Cơ sở y tế được gán cho nhân viên cận lâm sàng hiện đang ngừng hoạt động.");
        }

        var departments = await _db.Departments.AsNoTracking().Where(d => d.HospitalId == hospitalId).OrderBy(d => d.Id).ToListAsync(ct);
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
            : departments.FirstOrDefault(d => string.Equals(d.DepartmentCode, "DIA", StringComparison.OrdinalIgnoreCase))?.Name ?? DefaultDepartmentName;

        var allPermissions = await _db.Permissions.AsNoTracking().ToListAsync(ct);
        var allRolePermissions = await _db.RolePermissions.AsNoTracking().ToListAsync(ct);
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

        var hasAnyReadPermission = actorPermissionCodes.Any(code =>
            AllowedReadPermissions.Any(allowed => string.Equals(code, allowed, StringComparison.OrdinalIgnoreCase)));
        if (!hasAnyReadPermission)
        {
            throw new ForbiddenException("Nhân viên cận lâm sàng không có quyền truy cập danh sách thực hiện dịch vụ cận lâm sàng.");
        }

        var hasExecutionPermission = actorPermissionCodes.Any(code =>
            AllowedMutationPermissions.Any(allowed => string.Equals(code, allowed, StringComparison.OrdinalIgnoreCase)));

        if (forMutation && !hasExecutionPermission)
        {
            throw new ForbiddenException("Nhân viên cận lâm sàng không có quyền cập nhật trạng thái thực hiện dịch vụ (QUEUE_MANAGE hoặc DIAGNOSTIC_RESULT_WRITE).");
        }

        var fullName = !string.IsNullOrWhiteSpace(actorStaff.FullName) ? actorStaff.FullName : actorUser.Email;

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
            HasQueueManagePermission: actorPermissionCodes.Any(c => string.Equals(c, "QUEUE_MANAGE", StringComparison.OrdinalIgnoreCase)),
            HasExecutionPermission: hasExecutionPermission,
            Profile: staffProfile);
    }

    // ======================= IDENTIFIER RESOLUTION =======================

    private static ParsedOrderIdentifier ParseOrderIdentifier(string? rawIdOrCode)
    {
        if (string.IsNullOrWhiteSpace(rawIdOrCode))
        {
            throw new ValidationException("Mã định danh phiếu chỉ định cận lâm sàng không hợp lệ.");
        }

        var trimmed = rawIdOrCode.Trim();
        if (trimmed.StartsWith('#'))
        {
            trimmed = trimmed[1..].Trim();
        }

        if (string.IsNullOrWhiteSpace(trimmed) ||
            trimmed.Length > 60 ||
            trimmed.Contains('<') ||
            trimmed.Contains('>'))
        {
            throw new ValidationException("Mã định danh phiếu chỉ định cận lâm sàng không hợp lệ.");
        }

        if (trimmed.StartsWith("do-", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("do_", StringComparison.OrdinalIgnoreCase))
        {
            var numPart = trimmed[3..];
            if (long.TryParse(numPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var doId))
            {
                if (doId <= 0)
                {
                    throw new ValidationException("ID phiếu chỉ định cận lâm sàng phải lớn hơn 0.");
                }

                return new ParsedOrderIdentifier(doId, null, null, null, rawIdOrCode.Trim());
            }

            throw new ValidationException("Mã định danh phiếu chỉ định cận lâm sàng không hợp lệ.");
        }

        if (trimmed.StartsWith("q-", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("q_", StringComparison.OrdinalIgnoreCase))
        {
            var numPart = trimmed[2..];
            if (long.TryParse(numPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var qId))
            {
                if (qId <= 0)
                {
                    throw new ValidationException("ID hàng đợi cận lâm sàng phải lớn hơn 0.");
                }

                return new ParsedOrderIdentifier(null, qId, null, null, rawIdOrCode.Trim());
            }

            throw new ValidationException("Mã định danh phiếu chỉ định cận lâm sàng không hợp lệ.");
        }

        if ((trimmed.StartsWith("vis-", StringComparison.OrdinalIgnoreCase) ||
             trimmed.StartsWith("vis_", StringComparison.OrdinalIgnoreCase)) &&
            !trimmed[4..].Contains('-') &&
            !trimmed[4..].Contains('_'))
        {
            var numPart = trimmed[4..];
            if (long.TryParse(numPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var vId))
            {
                if (vId <= 0)
                {
                    throw new ValidationException("ID lượt khám phải lớn hơn 0.");
                }

                return new ParsedOrderIdentifier(null, null, vId, null, rawIdOrCode.Trim());
            }

            throw new ValidationException("Mã định danh phiếu chỉ định cận lâm sàng không hợp lệ.");
        }

        var isNumericCandidate = trimmed.All(char.IsDigit) ||
            (trimmed.StartsWith('-') && trimmed.Length > 1 && trimmed[1..].All(char.IsDigit));
        if (isNumericCandidate)
        {
            if (!long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedId) || parsedId <= 0)
            {
                throw new ValidationException("ID phiếu chỉ định cận lâm sàng phải lớn hơn 0.");
            }

            return new ParsedOrderIdentifier(parsedId, null, null, null, rawIdOrCode.Trim());
        }

        foreach (var ch in trimmed)
        {
            var isValidChar = (ch >= 'a' && ch <= 'z') ||
                              (ch >= 'A' && ch <= 'Z') ||
                              (ch >= '0' && ch <= '9') ||
                              ch is '-' or '_';
            if (!isValidChar)
            {
                throw new ValidationException("Mã định danh phiếu chỉ định cận lâm sàng không hợp lệ.");
            }
        }

        return new ParsedOrderIdentifier(null, null, null, trimmed.ToUpperInvariant(), rawIdOrCode.Trim());
    }

    private async Task<DiagnosticOrder> ResolveDiagnosticOrderAsync(
        ParsedOrderIdentifier identifier,
        long hospitalId,
        DateOnly referenceDate,
        bool asTracking,
        CancellationToken ct)
    {
        IQueryable<DiagnosticOrder> query;
        try
        {
            query = asTracking ? _db.DiagnosticOrders : _db.DiagnosticOrders.AsNoTracking();
        }
        catch (NotSupportedException)
        {
            throw new NotFoundException("DIAGNOSTIC_ORDER_NOT_FOUND", identifier.RawInput);
        }

        DiagnosticOrder? resolved = null;
        if (identifier.OrderId.HasValue)
        {
            resolved = await query.FirstOrDefaultAsync(o => o.Id == identifier.OrderId.Value, ct);
        }
        else if (identifier.QueueEntryId.HasValue)
        {
            resolved = await query.Where(o => o.QueueEntryId == identifier.QueueEntryId.Value).OrderByDescending(o => o.Id).FirstOrDefaultAsync(ct);
        }
        else if (identifier.VisitId.HasValue)
        {
            resolved = await ResolveBestOrderForVisitAsync(identifier.VisitId.Value, identifier.RawInput, query, ct);
        }
        else
        {
            var code = identifier.Code!;
            var byExactCode = await query.FirstOrDefaultAsync(o => o.OrderCode != null && o.OrderCode.ToUpper() == code, ct);
            if (byExactCode is not null)
            {
                resolved = byExactCode;
            }
            else if (code.StartsWith("DIA-", StringComparison.OrdinalIgnoreCase) ||
                     code.StartsWith("CLS-", StringComparison.OrdinalIgnoreCase))
            {
                var lastDash = code.LastIndexOf('-');
                if (lastDash > 0 && lastDash < code.Length - 1 &&
                    long.TryParse(code[(lastDash + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var candidateId) &&
                    candidateId > 0)
                {
                    var candidate = await query.FirstOrDefaultAsync(o => o.Id == candidateId, ct);
                    if (candidate is not null)
                    {
                        var orderDate = candidate.OrderedAt.HasValue ? DateOnly.FromDateTime(candidate.OrderedAt.Value) : referenceDate;
                        var formattedByRef = FormatOrderCode(candidate, referenceDate);
                        var formattedByOrderedAt = $"DIA-{orderDate:yyyyMM}-{candidate.Id:D4}";
                        var formattedClsByRef = $"CLS-{referenceDate:yyyy}-{candidate.Id:D4}";
                        var formattedClsByOrderedAt = $"CLS-{orderDate:yyyy}-{candidate.Id:D4}";

                        if (string.Equals(formattedByRef, code, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(formattedByOrderedAt, code, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(formattedClsByRef, code, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(formattedClsByOrderedAt, code, StringComparison.OrdinalIgnoreCase))
                        {
                            resolved = candidate;
                        }
                    }
                }
            }

            if (resolved is null && code.StartsWith("VIS-", StringComparison.OrdinalIgnoreCase))
            {
                var resolvedVisit = await ResolveVisitByCodeAsync(code, referenceDate, ct);
                if (resolvedVisit is not null)
                {
                    resolved = await ResolveBestOrderForVisitAsync(resolvedVisit.Id, identifier.RawInput, query, ct);
                }
            }

            if (resolved is null &&
                code.Length >= 2 &&
                code[0] == 'C' &&
                code[1..].All(char.IsDigit) &&
                int.TryParse(code[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticketNum) &&
                ticketNum > 0)
            {
                var hospitalDiagServicePointIds = (await TryLoadHospitalDiagnosticServicePointsAsync(hospitalId, ct))
                    .Select(sp => sp.Id).ToHashSet();

                var candidateQueueEntries = await _db.QueueEntries
                    .AsNoTracking()
                    .Where(q => q.QueueNumber == ticketNum && q.QueueType != null && q.QueueType.ToUpper() == "DIAGNOSTIC")
                    .OrderByDescending(q => q.QueueDate)
                    .ThenByDescending(q => q.Id)
                    .ToListAsync(ct);

                var matchingQueueIds = candidateQueueEntries
                    .Where(q =>
                        hospitalDiagServicePointIds.Count == 0 ||
                        (q.CurrentServicePointId.HasValue && hospitalDiagServicePointIds.Contains(q.CurrentServicePointId.Value)) ||
                        (q.ServicePointId.HasValue && hospitalDiagServicePointIds.Contains(q.ServicePointId.Value)))
                    .Select(q => q.Id)
                    .ToList();

                if (matchingQueueIds.Count > 0)
                {
                    var ticketCandidates = await query.Where(o => o.QueueEntryId.HasValue && matchingQueueIds.Contains(o.QueueEntryId.Value)).ToListAsync(ct);
                    resolved = ticketCandidates
                        .OrderBy(o => matchingQueueIds.IndexOf(o.QueueEntryId!.Value))
                        .ThenByDescending(o => o.OrderedAt ?? DateTime.MinValue)
                        .ThenByDescending(o => o.Id)
                        .FirstOrDefault();
                }
            }
        }

        if (resolved is null)
        {
            throw new NotFoundException("DIAGNOSTIC_ORDER_NOT_FOUND", identifier.RawInput);
        }

        return resolved;
    }

    private async Task<Visit?> ResolveVisitByCodeAsync(string visitCode, DateOnly referenceDate, CancellationToken ct)
    {
        var normalized = visitCode.Trim().ToUpperInvariant();
        var byExact = await _db.Visits.AsNoTracking().FirstOrDefaultAsync(v => v.VisitCode != null && v.VisitCode.ToUpper() == normalized, ct);
        if (byExact is not null)
        {
            return byExact;
        }

        var lastDash = normalized.LastIndexOf('-');
        if (lastDash > 0 && lastDash < normalized.Length - 1 &&
            long.TryParse(normalized[(lastDash + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var candidateVisitId) &&
            candidateVisitId > 0)
        {
            var candidateVisit = await _db.Visits.AsNoTracking().FirstOrDefaultAsync(v => v.Id == candidateVisitId, ct);
            if (candidateVisit is not null)
            {
                var formattedByRef = FormatVisitCode(candidateVisit, referenceDate);
                var visitDate = candidateVisit.CheckedInAt.HasValue ? DateOnly.FromDateTime(candidateVisit.CheckedInAt.Value) : referenceDate;
                var formattedByCheckedIn = $"VIS-{visitDate:yyyyMM}-{candidateVisit.Id:D4}";
                if (string.Equals(formattedByRef, normalized, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(formattedByCheckedIn, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return candidateVisit;
                }
            }
        }

        return null;
    }

    private async Task<DiagnosticOrder?> ResolveBestOrderForVisitAsync(
        long visitId,
        string rawInput,
        IQueryable<DiagnosticOrder> query,
        CancellationToken ct)
    {
        var visitOrders = await query.Where(o => o.VisitId == visitId).ToListAsync(ct);
        if (visitOrders.Count == 0)
        {
            return null;
        }

        var finalizedOrderIds = await TryLoadFinalizedOrderIdsAsync(visitOrders, ct);

        var active = visitOrders
            .Where(o => ResolveEffectiveOrderStatus(o, finalizedOrderIds) is "CONFIRMED" or "IN_PROGRESS")
            .OrderByDescending(o => ResolveEffectiveOrderStatus(o, finalizedOrderIds) == "IN_PROGRESS" ? 1 : 0)
            .ThenByDescending(o => GetPriorityRank(o.Priority))
            .ThenByDescending(o => o.OrderedAt ?? DateTime.MinValue)
            .ThenByDescending(o => o.Id)
            .FirstOrDefault();

        return active ?? visitOrders.OrderByDescending(o => o.OrderedAt ?? DateTime.MinValue).ThenByDescending(o => o.Id).First();
    }

    private async Task<IReadOnlyList<ParsedOrderIdentifier>> ResolveBatchTargetIdentifiersAsync(
        UpdateDiagnosticServiceStatusBatchRequest request,
        long hospitalId,
        DateTime nowLocal,
        DateOnly referenceDate,
        CancellationToken ct)
    {
        if (request.VisitId is <= 0)
        {
            throw new ValidationException("ID lượt khám (VisitId) phải lớn hơn 0.");
        }

        if (!string.IsNullOrWhiteSpace(request.VisitCode))
        {
            var trimmedVisitCode = request.VisitCode.Trim();
            if (trimmedVisitCode.Length > 40 || trimmedVisitCode.Contains('<') || trimmedVisitCode.Contains('>'))
            {
                throw new ValidationException("Mã lượt khám không hợp lệ.");
            }
        }

        var rawTargets = new List<ParsedOrderIdentifier>();
        var seenIds = new HashSet<long>();
        var seenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddParsed(ParsedOrderIdentifier parsed)
        {
            if (parsed.OrderId.HasValue && !seenIds.Add(parsed.OrderId.Value))
            {
                throw new ValidationException("Danh sách phiếu chỉ định cần cập nhật có mã hoặc ID bị trùng lặp.");
            }

            if (parsed.Code is not null && !seenCodes.Add(parsed.Code))
            {
                throw new ValidationException("Danh sách phiếu chỉ định cần cập nhật có mã hoặc ID bị trùng lặp.");
            }

            rawTargets.Add(parsed);
        }

        if (request.OrderId.HasValue)
        {
            if (request.OrderId.Value <= 0)
            {
                throw new ValidationException("ID phiếu chỉ định cận lâm sàng phải lớn hơn 0.");
            }

            AddParsed(new ParsedOrderIdentifier(request.OrderId.Value, null, null, null, request.OrderId.Value.ToString(CultureInfo.InvariantCulture)));
        }

        if (!string.IsNullOrWhiteSpace(request.OrderCode))
        {
            AddParsed(ParseOrderIdentifier(request.OrderCode));
        }

        if (request.OrderIds is not null)
        {
            foreach (var id in request.OrderIds)
            {
                if (id <= 0)
                {
                    throw new ValidationException("ID phiếu chỉ định cận lâm sàng phải lớn hơn 0.");
                }

                AddParsed(new ParsedOrderIdentifier(id, null, null, null, id.ToString(CultureInfo.InvariantCulture)));
            }
        }

        if (request.OrderCodes is not null)
        {
            foreach (var code in request.OrderCodes)
            {
                AddParsed(ParseOrderIdentifier(code));
            }
        }

        if (rawTargets.Count == 0 && (request.VisitId.HasValue || !string.IsNullOrWhiteSpace(request.VisitCode)))
        {
            long targetVisitId;
            if (request.VisitId.HasValue)
            {
                var foundVisit = await _db.Visits.AsNoTracking().FirstOrDefaultAsync(v => v.Id == request.VisitId.Value, ct)
                    ?? throw new NotFoundException("Visit", request.VisitId.Value);
                targetVisitId = foundVisit.Id;
            }
            else
            {
                var foundVisit = await ResolveVisitByCodeAsync(request.VisitCode!, referenceDate, ct)
                    ?? throw new NotFoundException("Visit", request.VisitCode!.Trim());
                targetVisitId = foundVisit.Id;
            }

            var visitOrders = await TryLoadVisitDiagnosticOrdersAsync(targetVisitId, ct);
            if (visitOrders.Count == 0)
            {
                throw new NotFoundException("DiagnosticOrder", request.VisitCode ?? targetVisitId.ToString(CultureInfo.InvariantCulture));
            }

            var lookupBundle = await LoadOrderLookupBundleAsync(visitOrders, hospitalId, nowLocal, ct);
            foreach (var vo in visitOrders)
            {
                EnsureOrderHospitalOwnership(vo, hospitalId, lookupBundle);
            }

            var eligible = visitOrders
                .Where(o => ResolveEffectiveOrderStatus(o, lookupBundle.FinalizedOrderIds) is "CONFIRMED" or "IN_PROGRESS")
                .OrderByDescending(o => GetPriorityRank(o.Priority))
                .ThenBy(o => o.OrderedAt ?? DateTime.MinValue)
                .ThenBy(o => o.Id)
                .ToList();

            var batchTargets = eligible.Count > 0
                ? eligible
                : [visitOrders.OrderByDescending(o => o.OrderedAt ?? DateTime.MinValue).ThenByDescending(o => o.Id).First()];

            foreach (var wo in batchTargets)
            {
                AddParsed(new ParsedOrderIdentifier(wo.Id, null, null, null, wo.Id.ToString(CultureInfo.InvariantCulture)));
            }
        }

        if (rawTargets.Count == 0)
        {
            throw new ValidationException("Vui lòng chọn ít nhất một phiếu chỉ định cận lâm sàng để cập nhật trạng thái thực hiện.");
        }

        if (rawTargets.Count > 20)
        {
            throw new ValidationException("Mỗi lần chỉ được cập nhật tối đa 20 phiếu chỉ định cận lâm sàng.");
        }

        return rawTargets;
    }

    // ======================= LOOKUP BUNDLE =======================

    private async Task<OrderLookupBundle> LoadOrderLookupBundleAsync(
        IReadOnlyList<DiagnosticOrder> orders,
        long hospitalId,
        DateTime nowLocal,
        CancellationToken ct)
    {
        var hospitalDiagPoints = await TryLoadHospitalDiagnosticServicePointsAsync(hospitalId, ct);

        var serviceIds = orders.Where(o => o.ServiceId.HasValue).Select(o => o.ServiceId!.Value)
            .Concat(hospitalDiagPoints.Where(sp => sp.ServiceId.HasValue).Select(sp => sp.ServiceId!.Value))
            .Distinct().ToList();

        var servicesById = serviceIds.Count > 0
            ? await _db.Services.AsNoTracking().Where(s => serviceIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct)
            : new Dictionary<long, Service>();

        var servicePricesByServiceId = new Dictionary<long, decimal>();
        if (serviceIds.Count > 0)
        {
            try
            {
                var prices = await _db.ServicePrices.AsNoTracking()
                    .Where(p => p.ServiceId.HasValue && serviceIds.Contains(p.ServiceId.Value) &&
                                (p.EffectiveFrom == null || p.EffectiveFrom <= nowLocal) &&
                                (p.EffectiveTo == null || p.EffectiveTo >= nowLocal))
                    .OrderByDescending(p => p.EffectiveFrom).ThenByDescending(p => p.Id)
                    .ToListAsync(ct);
                foreach (var price in prices)
                {
                    if (price.ServiceId.HasValue && !servicePricesByServiceId.ContainsKey(price.ServiceId.Value))
                    {
                        servicePricesByServiceId[price.ServiceId.Value] = price.PriceAmount ?? 0m;
                    }
                }
            }
            catch (NotSupportedException)
            {
            }
        }

        var departmentsById = await _db.Departments.AsNoTracking().ToDictionaryAsync(d => d.Id, ct);
        var specialtiesById = await _db.Specialties.AsNoTracking().ToDictionaryAsync(s => s.Id, ct);

        var visitIds = orders.Where(o => o.VisitId.HasValue).Select(o => o.VisitId!.Value).Distinct().ToList();
        var visitsById = visitIds.Count > 0
            ? await _db.Visits.AsNoTracking().Where(v => visitIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, ct)
            : new Dictionary<long, Visit>();

        var patientIds = visitsById.Values.Where(v => v.PatientId.HasValue).Select(v => v.PatientId!.Value).Distinct().ToList();
        var patientsById = patientIds.Count > 0
            ? await _db.Patients.AsNoTracking().Where(p => patientIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct)
            : new Dictionary<long, Patient>();

        var doctorIds = orders.Where(o => o.OrderingDoctorId.HasValue).Select(o => o.OrderingDoctorId!.Value)
            .Concat(visitsById.Values.Where(v => v.AssignedDoctorId.HasValue).Select(v => v.AssignedDoctorId!.Value))
            .Distinct().ToList();
        var doctorsById = doctorIds.Count > 0
            ? await _db.Doctors.AsNoTracking().Include(d => d.Staff).Where(d => doctorIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, ct)
            : new Dictionary<long, Doctor>();

        var queueEntryIds = orders.Where(o => o.QueueEntryId.HasValue).Select(o => o.QueueEntryId!.Value).Distinct().ToList();
        var queueEntriesById = queueEntryIds.Count > 0
            ? await _db.QueueEntries.AsNoTracking().Where(q => queueEntryIds.Contains(q.Id)).ToDictionaryAsync(q => q.Id, ct)
            : new Dictionary<long, QueueEntry>();

        var referencedSpIds = queueEntriesById.Values
            .SelectMany(q => new[] { q.CurrentServicePointId, q.ServicePointId })
            .Where(id => id.HasValue).Select(id => id!.Value)
            .Concat(hospitalDiagPoints.Select(sp => sp.Id)).Distinct().ToList();

        var servicePointsById = new Dictionary<long, QueueServicePoint>();
        foreach (var sp in hospitalDiagPoints)
        {
            servicePointsById[sp.Id] = sp;
        }

        if (referencedSpIds.Count > 0)
        {
            try
            {
                var loadedPoints = await _db.QueueServicePoints.AsNoTracking().Where(sp => referencedSpIds.Contains(sp.Id)).ToListAsync(ct);
                foreach (var sp in loadedPoints)
                {
                    servicePointsById[sp.Id] = sp;
                }
            }
            catch (NotSupportedException)
            {
            }
        }

        var roomIds = servicePointsById.Values.Where(sp => sp.RoomId.HasValue).Select(sp => sp.RoomId!.Value)
            .Concat(visitsById.Values.Where(v => v.ExaminationRoomId.HasValue).Select(v => v.ExaminationRoomId!.Value))
            .Distinct().ToList();
        var roomsById = await _db.HospitalRooms.AsNoTracking().Where(r => r.HospitalId == hospitalId || roomIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, ct);

        var orderIdStrings = orders.Select(o => o.Id.ToString(CultureInfo.InvariantCulture)).Distinct().ToList();
        var auditMetadataByOrderId = await TryLoadDiagnosticOrderAuditMetadataAsync(orderIdStrings, ct);

        var staffIds = orders.Where(o => o.ConfirmedByStaffId.HasValue).Select(o => o.ConfirmedByStaffId!.Value)
            .Concat(auditMetadataByOrderId.Values.Where(m => m.ReceivedByStaffId.HasValue).Select(m => m.ReceivedByStaffId!.Value))
            .Concat(auditMetadataByOrderId.Values.Where(m => m.PerformedByStaffId.HasValue).Select(m => m.PerformedByStaffId!.Value))
            .Distinct().ToList();
        var staffById = staffIds.Count > 0
            ? await _db.Staff.AsNoTracking().Where(s => staffIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct)
            : new Dictionary<long, Staff>();

        var finalizedOrderIds = await TryLoadFinalizedOrderIdsAsync(orders, ct);
        var latestRevisionByVisitId = await TryLoadLatestRevisionsByVisitIdAsync(visitIds, ct);

        var sequenceByOrderId = new Dictionary<long, int>();
        foreach (var group in orders.Where(o => o.VisitId.HasValue && AppointmentBookingRules.NormalizeStatusKey(o.Status) != "CANCELLED").GroupBy(o => o.VisitId!.Value))
        {
            var orderedVisitItems = group.OrderBy(o => o.OrderedAt ?? DateTime.MinValue).ThenBy(o => o.Id).ToList();
            for (var i = 0; i < orderedVisitItems.Count; i++)
            {
                sequenceByOrderId[orderedVisitItems[i].Id] = i + 1;
            }
        }

        return new OrderLookupBundle(
            ServicesById: servicesById,
            ServicePricesByServiceId: servicePricesByServiceId,
            DepartmentsById: departmentsById,
            SpecialtiesById: specialtiesById,
            VisitsById: visitsById,
            PatientsById: patientsById,
            DoctorsById: doctorsById,
            QueueEntriesById: queueEntriesById,
            ServicePointsById: servicePointsById,
            HospitalDiagnosticServicePoints: hospitalDiagPoints,
            RoomsById: roomsById,
            StaffById: staffById,
            FinalizedOrderIds: finalizedOrderIds,
            LatestRevisionByVisitId: latestRevisionByVisitId,
            SequenceByOrderId: sequenceByOrderId,
            AuditMetadataByOrderId: auditMetadataByOrderId);
    }

    private async Task<List<DiagnosticOrder>> TryLoadAllDiagnosticOrdersAsync(CancellationToken ct)
    {
        try
        {
            return await _db.DiagnosticOrders.AsNoTracking().OrderByDescending(o => o.OrderedAt).ThenByDescending(o => o.Id).ToListAsync(ct);
        }
        catch (NotSupportedException)
        {
            return [];
        }
    }

    private async Task<List<DiagnosticOrder>> TryLoadVisitDiagnosticOrdersAsync(long visitId, CancellationToken ct)
    {
        try
        {
            return await _db.DiagnosticOrders.AsNoTracking().Where(o => o.VisitId == visitId).OrderBy(o => o.OrderedAt).ThenBy(o => o.Id).ToListAsync(ct);
        }
        catch (NotSupportedException)
        {
            return [];
        }
    }

    private async Task<List<QueueServicePoint>> TryLoadHospitalDiagnosticServicePointsAsync(long hospitalId, CancellationToken ct)
    {
        try
        {
            return await _db.QueueServicePoints.AsNoTracking()
                .Where(sp => sp.HospitalId == hospitalId && sp.PointType != null && sp.PointType.ToUpper() == "DIAGNOSTIC")
                .OrderBy(sp => sp.Id).ToListAsync(ct);
        }
        catch (NotSupportedException)
        {
            return [];
        }
    }

    private async Task<HashSet<long>> TryLoadFinalizedOrderIdsAsync(IReadOnlyList<DiagnosticOrder> orders, CancellationToken ct)
    {
        var finalizedOrderIds = new HashSet<long>();
        if (orders.Count == 0)
        {
            return finalizedOrderIds;
        }

        var orderIds = orders.Select(o => o.Id).Distinct().ToList();
        try
        {
            var results = await _db.DiagnosticResults.AsNoTracking()
                .Where(r => r.DiagnosticOrderId.HasValue && orderIds.Contains(r.DiagnosticOrderId.Value))
                .ToListAsync(ct);
            foreach (var r in results)
            {
                if (!r.DiagnosticOrderId.HasValue)
                {
                    continue;
                }

                var normalized = AppointmentBookingRules.NormalizeStatusKey(r.Status);
                if (r.FinalizedAt.HasValue || normalized is "FINALIZED" or "VERIFIED" or "COMPLETED")
                {
                    finalizedOrderIds.Add(r.DiagnosticOrderId.Value);
                }
            }
        }
        catch (NotSupportedException)
        {
        }

        return finalizedOrderIds;
    }

    private async Task<Dictionary<long, DiagnosticOrderResultState>> TryLoadResultStatesAsync(
        IReadOnlyList<DiagnosticOrder> orders,
        CancellationToken ct)
    {
        var result = new Dictionary<long, DiagnosticOrderResultState>();
        if (orders.Count == 0)
        {
            return result;
        }

        var orderIds = orders.Select(o => o.Id).Distinct().ToList();
        try
        {
            var results = await _db.DiagnosticResults.AsNoTracking()
                .Where(r => r.DiagnosticOrderId.HasValue && orderIds.Contains(r.DiagnosticOrderId.Value))
                .ToListAsync(ct);
            foreach (var r in results)
            {
                if (!r.DiagnosticOrderId.HasValue)
                {
                    continue;
                }

                var normalized = AppointmentBookingRules.NormalizeStatusKey(r.Status);
                var isFinalized = r.FinalizedAt.HasValue || normalized is "FINALIZED" or "VERIFIED" or "COMPLETED";
                var status = string.IsNullOrWhiteSpace(normalized) ? "DRAFT" : normalized;
                var existing = result.TryGetValue(r.DiagnosticOrderId.Value, out var prev) ? prev : null;
                if (existing is null || isFinalized || r.Id > 0)
                {
                    result[r.DiagnosticOrderId.Value] = new DiagnosticOrderResultState(true, isFinalized, status);
                }
            }
        }
        catch (NotSupportedException)
        {
        }

        return result;
    }

    private async Task<Dictionary<long, MedicalRecordRevision>> TryLoadLatestRevisionsByVisitIdAsync(
        IReadOnlyList<long> visitIds,
        CancellationToken ct)
    {
        var result = new Dictionary<long, MedicalRecordRevision>();
        if (visitIds.Count == 0)
        {
            return result;
        }

        try
        {
            var records = await _db.MedicalRecords.AsNoTracking()
                .Where(m => m.VisitId.HasValue && visitIds.Contains(m.VisitId.Value))
                .OrderByDescending(m => m.Id).ToListAsync(ct);
            if (records.Count == 0)
            {
                return result;
            }

            var recordIds = records.Select(m => m.Id).Distinct().ToList();
            var revisions = await _db.MedicalRecordRevisions.AsNoTracking()
                .Where(r => r.MedicalRecordId.HasValue && recordIds.Contains(r.MedicalRecordId.Value))
                .OrderByDescending(r => r.RevisionNo).ThenByDescending(r => r.Id).ToListAsync(ct);

            var latestRevByRecordId = revisions.Where(r => r.MedicalRecordId.HasValue).GroupBy(r => r.MedicalRecordId!.Value).ToDictionary(g => g.Key, g => g.First());
            foreach (var record in records)
            {
                if (record.VisitId.HasValue && !result.ContainsKey(record.VisitId.Value) && latestRevByRecordId.TryGetValue(record.Id, out var rev))
                {
                    result[record.VisitId.Value] = rev;
                }
            }
        }
        catch (NotSupportedException)
        {
            return result;
        }

        return result;
    }

    private async Task<Dictionary<long, DiagnosticOrderAuditMetadata>> TryLoadDiagnosticOrderAuditMetadataAsync(
        IReadOnlyList<string> orderIdStrings,
        CancellationToken ct)
    {
        var result = new Dictionary<long, DiagnosticOrderAuditMetadata>();
        if (orderIdStrings.Count == 0)
        {
            return result;
        }

        try
        {
            var logs = await _db.AuditLogs.AsNoTracking()
                .Where(a =>
                    a.EntityType == DiagnosticOrderEntityType &&
                    a.EntityId != null &&
                    orderIdStrings.Contains(a.EntityId) &&
                    (a.Action == "ADDITIONAL_DIAGNOSTIC_ORDER_CREATED" ||
                     a.Action == "DIAGNOSTIC_ORDER_CREATED" ||
                     a.Action == "DIAGNOSTIC_ORDER_CONFIRMED" ||
                     a.Action == "DIAGNOSTIC_PATIENT_RECEIVED"))
                .OrderByDescending(a => a.OccurredAt).ThenByDescending(a => a.Id).ToListAsync(ct);

            foreach (var log in logs)
            {
                if (!long.TryParse(log.EntityId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var orderId))
                {
                    continue;
                }

                result.TryGetValue(orderId, out var existing);
                var isAdditional = existing?.IsAdditionalOrder ?? false;
                var seqNo = existing?.OrderSequenceNo;
                var followUpId = existing?.FollowUpToOrderId;
                var followUpCode = existing?.FollowUpToOrderCode;
                var additionalReason = existing?.AdditionalOrderReason;
                var confirmationNote = existing?.ConfirmationNote;
                var receivedAt = existing?.ReceivedAt;
                var receivedByStaffId = existing?.ReceivedByStaffId;
                var receivedByStaffCode = existing?.ReceivedByStaffCode;
                var receivedByStaffName = existing?.ReceivedByStaffName;
                var receptionNote = existing?.ReceptionNote;
                var performedByStaffId = existing?.PerformedByStaffId;
                var performedByStaffCode = existing?.PerformedByStaffCode;
                var performedByStaffName = existing?.PerformedByStaffName;

                if (string.Equals(log.Action, "ADDITIONAL_DIAGNOSTIC_ORDER_CREATED", StringComparison.OrdinalIgnoreCase))
                {
                    isAdditional = true;
                }

                if (!string.IsNullOrWhiteSpace(log.ChangeData))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(log.ChangeData);
                        if (doc.RootElement.ValueKind == JsonValueKind.Object)
                        {
                            var root = doc.RootElement;
                            if (root.TryGetProperty("isAdditionalOrder", out var addProp) && addProp.ValueKind is JsonValueKind.True or JsonValueKind.False)
                            {
                                isAdditional = isAdditional || addProp.GetBoolean();
                            }

                            if (!seqNo.HasValue && root.TryGetProperty("orderSequenceNo", out var seqProp) && seqProp.ValueKind == JsonValueKind.Number && seqProp.TryGetInt32(out var parsedSeq))
                            {
                                seqNo = parsedSeq;
                            }

                            if (!followUpId.HasValue && root.TryGetProperty("followUpToOrderId", out var fIdProp) && fIdProp.ValueKind == JsonValueKind.Number && fIdProp.TryGetInt64(out var parsedFup) && parsedFup > 0)
                            {
                                followUpId = parsedFup;
                            }

                            if (followUpCode is null && root.TryGetProperty("followUpToOrderCode", out var fCodeProp) && fCodeProp.ValueKind == JsonValueKind.String)
                            {
                                var rawCode = fCodeProp.GetString();
                                if (!string.IsNullOrWhiteSpace(rawCode)) followUpCode = rawCode.Trim();
                            }

                            if (additionalReason is null && root.TryGetProperty("additionalOrderReason", out var reasonProp) && reasonProp.ValueKind == JsonValueKind.String)
                            {
                                var rawReason = reasonProp.GetString();
                                if (!string.IsNullOrWhiteSpace(rawReason)) additionalReason = rawReason.Trim();
                            }

                            if (confirmationNote is null && root.TryGetProperty("confirmationNote", out var confNoteProp) && confNoteProp.ValueKind == JsonValueKind.String)
                            {
                                var rawNote = confNoteProp.GetString();
                                if (!string.IsNullOrWhiteSpace(rawNote)) confirmationNote = rawNote.Trim();
                            }

                            if (!receivedAt.HasValue && root.TryGetProperty("receivedAt", out var recAtProp) && recAtProp.ValueKind == JsonValueKind.String && recAtProp.TryGetDateTime(out var parsedRecAt))
                            {
                                receivedAt = parsedRecAt;
                            }

                            if (!receivedByStaffId.HasValue && root.TryGetProperty("receivedByStaffId", out var recStaffIdProp) && recStaffIdProp.ValueKind == JsonValueKind.Number && recStaffIdProp.TryGetInt64(out var parsedRecStaffId) && parsedRecStaffId > 0)
                            {
                                receivedByStaffId = parsedRecStaffId;
                            }

                            if (receivedByStaffCode is null && root.TryGetProperty("receivedByStaffCode", out var recStaffCodeProp) && recStaffCodeProp.ValueKind == JsonValueKind.String)
                            {
                                var rawStaffCode = recStaffCodeProp.GetString();
                                if (!string.IsNullOrWhiteSpace(rawStaffCode)) receivedByStaffCode = rawStaffCode.Trim();
                            }

                            if (receivedByStaffName is null && root.TryGetProperty("receivedByStaffName", out var recStaffNameProp) && recStaffNameProp.ValueKind == JsonValueKind.String)
                            {
                                var rawStaffName = recStaffNameProp.GetString();
                                if (!string.IsNullOrWhiteSpace(rawStaffName)) receivedByStaffName = rawStaffName.Trim();
                            }

                            if (receptionNote is null && root.TryGetProperty("receptionNote", out var recNoteProp) && recNoteProp.ValueKind == JsonValueKind.String)
                            {
                                var rawRecNote = recNoteProp.GetString();
                                if (!string.IsNullOrWhiteSpace(rawRecNote)) receptionNote = rawRecNote.Trim();
                            }
                        }
                    }
                    catch (JsonException)
                    {
                    }
                }

                if (string.Equals(log.Action, "DIAGNOSTIC_PATIENT_RECEIVED", StringComparison.OrdinalIgnoreCase) && !receivedAt.HasValue && log.OccurredAt != default)
                {
                    receivedAt = DateTime.SpecifyKind(log.OccurredAt.AddHours(7), DateTimeKind.Unspecified);
                }

                result[orderId] = new DiagnosticOrderAuditMetadata(
                    IsAdditionalOrder: isAdditional,
                    OrderSequenceNo: seqNo,
                    FollowUpToOrderId: followUpId,
                    FollowUpToOrderCode: followUpCode,
                    AdditionalOrderReason: additionalReason,
                    ConfirmationNote: confirmationNote,
                    ReceivedAt: receivedAt,
                    ReceivedByStaffId: receivedByStaffId,
                    ReceivedByStaffCode: receivedByStaffCode,
                    ReceivedByStaffName: receivedByStaffName,
                    ReceptionNote: receptionNote,
                    PerformedByStaffId: performedByStaffId,
                    PerformedByStaffCode: performedByStaffCode,
                    PerformedByStaffName: performedByStaffName);
            }
        }
        catch (NotSupportedException)
        {
            return result;
        }

        return result;
    }

    private async Task<Dictionary<long, DiagnosticOrderExecutionMetadata>> TryLoadExecutionAuditMetadataAsync(
        IReadOnlyList<string> orderIdStrings,
        CancellationToken ct)
    {
        var result = new Dictionary<long, DiagnosticOrderExecutionMetadata>();
        if (orderIdStrings.Count == 0)
        {
            return result;
        }

        try
        {
            var logs = await _db.AuditLogs.AsNoTracking()
                .Where(a =>
                    a.EntityType == DiagnosticOrderEntityType &&
                    a.EntityId != null &&
                    orderIdStrings.Contains(a.EntityId) &&
                    (a.Action == ExecutionStartedAction ||
                     a.Action == ExecutionCompletedAction ||
                     a.Action == ExecutionStatusUpdatedAction ||
                     a.Action == ExecutionCancelledAction))
                .OrderBy(a => a.OccurredAt).ThenBy(a => a.Id).ToListAsync(ct);

            foreach (var log in logs)
            {
                if (!long.TryParse(log.EntityId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var orderId))
                {
                    continue;
                }

                result.TryGetValue(orderId, out var existing);
                var startedAt = existing?.StartedAt;
                var startedByStaffId = existing?.StartedByStaffId;
                var startedByStaffCode = existing?.StartedByStaffCode;
                var startedByStaffName = existing?.StartedByStaffName;
                var completedAt = existing?.CompletedAt;
                var completedByStaffId = existing?.CompletedByStaffId;
                var completedByStaffCode = existing?.CompletedByStaffCode;
                var completedByStaffName = existing?.CompletedByStaffName;
                var specimenCode = existing?.SpecimenCode;
                var specimenType = existing?.SpecimenType;
                var equipmentCode = existing?.EquipmentCode;
                var executionNote = existing?.ExecutionNote;
                var lastReason = existing?.LastTransitionReason;

                string? logSpecimenCode = null, logSpecimenType = null, logEquipmentCode = null, logExecNote = null, logReason = null;
                long? logStaffId = null;
                string? logStaffCode = null, logStaffName = null;
                string? logTargetStatus = null;

                if (!string.IsNullOrWhiteSpace(log.ChangeData))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(log.ChangeData);
                        if (doc.RootElement.ValueKind == JsonValueKind.Object)
                        {
                            var root = doc.RootElement;
                            logSpecimenCode = GetJsonString(root, "specimenCode");
                            logSpecimenType = GetJsonString(root, "specimenType");
                            logEquipmentCode = GetJsonString(root, "equipmentCode");
                            logExecNote = GetJsonString(root, "executionNote");
                            logReason = GetJsonString(root, "reason");
                            logStaffId = GetJsonLong(root, "performedByStaffId");
                            logStaffCode = GetJsonString(root, "performedByStaffCode");
                            logStaffName = GetJsonString(root, "performedByStaffName");
                            logTargetStatus = GetJsonString(root, "targetStatus");
                        }
                    }
                    catch (JsonException)
                    {
                    }
                }

                var occurredLocal = DateTime.SpecifyKind(log.OccurredAt.AddHours(7), DateTimeKind.Unspecified);
                var actionKey = log.Action ?? string.Empty;
                var isStart = string.Equals(actionKey, ExecutionStartedAction, StringComparison.OrdinalIgnoreCase);
                var isComplete = string.Equals(actionKey, ExecutionCompletedAction, StringComparison.OrdinalIgnoreCase);
                var isPause = string.Equals(actionKey, ExecutionStatusUpdatedAction, StringComparison.OrdinalIgnoreCase);
                var isCancel = string.Equals(actionKey, ExecutionCancelledAction, StringComparison.OrdinalIgnoreCase);

                if (isStart)
                {
                    startedAt = occurredLocal;
                    startedByStaffId = logStaffId;
                    startedByStaffCode = logStaffCode;
                    startedByStaffName = logStaffName;
                    completedAt = null;
                    completedByStaffId = null;
                    completedByStaffCode = null;
                    completedByStaffName = null;
                }

                if (isComplete)
                {
                    completedAt = occurredLocal;
                    completedByStaffId = logStaffId;
                    completedByStaffCode = logStaffCode;
                    completedByStaffName = logStaffName;
                }

                specimenCode = logSpecimenCode ?? specimenCode;
                specimenType = logSpecimenType ?? specimenType;
                equipmentCode = logEquipmentCode ?? equipmentCode;
                executionNote = logExecNote ?? executionNote;
                if (isPause || isCancel)
                {
                    lastReason = logReason ?? log.Reason ?? lastReason;
                    if (isPause)
                    {
                        startedAt = null;
                        startedByStaffId = null;
                        startedByStaffCode = null;
                        startedByStaffName = null;
                    }
                    if (isCancel)
                    {
                        completedAt = occurredLocal;
                    }
                }

                result[orderId] = new DiagnosticOrderExecutionMetadata(
                    StartedAt: startedAt,
                    StartedByStaffId: startedByStaffId,
                    StartedByStaffCode: startedByStaffCode,
                    StartedByStaffName: startedByStaffName,
                    CompletedAt: completedAt,
                    CompletedByStaffId: completedByStaffId,
                    CompletedByStaffCode: completedByStaffCode,
                    CompletedByStaffName: completedByStaffName,
                    SpecimenCode: specimenCode,
                    SpecimenType: specimenType,
                    EquipmentCode: equipmentCode,
                    ExecutionNote: executionNote,
                    LastTransitionReason: lastReason);
            }
        }
        catch (NotSupportedException)
        {
            return result;
        }

        return result;
    }

    private static DiagnosticOrderExecutionMetadata MergeMetadataAfterUpdate(
        DiagnosticOrderExecutionMetadata? existing,
        ValidatedExecutionInput validated,
        string targetStatus,
        DiagnosticStaffAccessContext staffAccess,
        DateTime nowLocal)
    {
        var startedAt = targetStatus == "IN_PROGRESS" ? nowLocal : (targetStatus == "CONFIRMED" ? null : existing?.StartedAt);
        var startedByStaffId = targetStatus == "IN_PROGRESS" ? staffAccess.Staff.Id : (targetStatus == "CONFIRMED" ? null : existing?.StartedByStaffId);
        var startedByStaffCode = targetStatus == "IN_PROGRESS" ? staffAccess.Staff.StaffCode : (targetStatus == "CONFIRMED" ? null : existing?.StartedByStaffCode);
        var startedByStaffName = targetStatus == "IN_PROGRESS" ? staffAccess.Profile.FullName : (targetStatus == "CONFIRMED" ? null : existing?.StartedByStaffName);

        var completedAt = targetStatus is "COMPLETED" or "CANCELLED" ? nowLocal : existing?.CompletedAt;
        var completedByStaffId = targetStatus == "COMPLETED" ? staffAccess.Staff.Id : existing?.CompletedByStaffId;
        var completedByStaffCode = targetStatus == "COMPLETED" ? staffAccess.Staff.StaffCode : existing?.CompletedByStaffCode;
        var completedByStaffName = targetStatus == "COMPLETED" ? staffAccess.Profile.FullName : existing?.CompletedByStaffName;

        return new DiagnosticOrderExecutionMetadata(
            StartedAt: startedAt,
            StartedByStaffId: startedByStaffId,
            StartedByStaffCode: startedByStaffCode,
            StartedByStaffName: startedByStaffName,
            CompletedAt: completedAt,
            CompletedByStaffId: completedByStaffId,
            CompletedByStaffCode: completedByStaffCode,
            CompletedByStaffName: completedByStaffName,
            SpecimenCode: validated.SpecimenCode ?? existing?.SpecimenCode,
            SpecimenType: validated.SpecimenType ?? existing?.SpecimenType,
            EquipmentCode: validated.EquipmentCode ?? existing?.EquipmentCode,
            ExecutionNote: validated.ExecutionNote ?? existing?.ExecutionNote,
            LastTransitionReason: validated.Reason ?? existing?.LastTransitionReason);
    }

    // ======================= LIST FILTER / SORT =======================

    private static ValidatedExecutionListFilter ValidateListFilter(GetDiagnosticServiceExecutionsRequest request)
    {
        if (request.Limit is < 1 or > 100)
        {
            throw new ValidationException("Số lượng phiếu chỉ định cận lâm sàng hiển thị phải từ 1 đến 100.");
        }

        string? normalizedStatus = null;
        if (!string.IsNullOrWhiteSpace(request.Status) &&
            !string.Equals(request.Status.Trim(), "ALL", StringComparison.OrdinalIgnoreCase))
        {
            var trimmedStatus = request.Status.Trim();
            if (!ValidListStatuses.Contains(trimmedStatus))
            {
                throw new ValidationException("Trạng thái phiếu chỉ định cận lâm sàng lọc không hợp lệ.");
            }

            var upperStatus = trimmedStatus.ToUpperInvariant();
            normalizedStatus = upperStatus is "PENDING" or "PENDING_CONFIRMATION" or "READY" or "WAITING" or "CALLED"
                ? "ORDERED" == upperStatus ? "ORDERED" : (upperStatus is "READY" or "WAITING" or "CALLED" ? "CONFIRMED" : "ORDERED")
                : upperStatus is "EXECUTING" or "PROCESSING" ? "IN_PROGRESS"
                : upperStatus is "DONE" or "FINALIZED" ? "COMPLETED"
                : upperStatus;
        }

        string? normalizedQueueStatus = null;
        if (!string.IsNullOrWhiteSpace(request.QueueStatus) &&
            !string.Equals(request.QueueStatus.Trim(), "ALL", StringComparison.OrdinalIgnoreCase))
        {
            var trimmedQueueStatus = request.QueueStatus.Trim();
            if (!ValidQueueStatuses.Contains(trimmedQueueStatus))
            {
                throw new ValidationException("Trạng thái hàng đợi cận lâm sàng lọc không hợp lệ.");
            }

            normalizedQueueStatus = trimmedQueueStatus.ToUpperInvariant();
        }

        string? normalizedCategory = null;
        if (!string.IsNullOrWhiteSpace(request.Category) &&
            !string.Equals(request.Category.Trim(), "ALL", StringComparison.OrdinalIgnoreCase))
        {
            var trimmedCategory = request.Category.Trim();
            if (!ValidCategories.Contains(trimmedCategory))
            {
                throw new ValidationException("Nhóm cận lâm sàng lọc không hợp lệ.");
            }

            var upper = trimmedCategory.ToUpperInvariant();
            normalizedCategory = upper is "LAB" ? "LABORATORY" : upper is "IMG" ? "IMAGING" : upper is "FUNC" ? "FUNCTIONAL" : upper;
        }

        string? normalizedPriority = null;
        if (!string.IsNullOrWhiteSpace(request.Priority) &&
            !string.Equals(request.Priority.Trim(), "ALL", StringComparison.OrdinalIgnoreCase))
        {
            var trimmedPriority = request.Priority.Trim();
            if (!ValidPriorities.Contains(trimmedPriority))
            {
                throw new ValidationException("Mức độ ưu tiên lọc không hợp lệ.");
            }

            var upperPriority = trimmedPriority.ToUpperInvariant();
            normalizedPriority = upperPriority is "STANDARD" or "ROUTINE" ? "NORMAL" : upperPriority is "EMERGENCY" ? "STAT" : upperPriority;
        }

        if (request.VisitId is <= 0)
        {
            throw new ValidationException("ID lượt khám (VisitId) phải lớn hơn 0.");
        }

        if (request.ServiceId is <= 0)
        {
            throw new ValidationException("ID dịch vụ cận lâm sàng (ServiceId) phải lớn hơn 0.");
        }

        if (request.RoomId is <= 0)
        {
            throw new ValidationException("ID phòng thực hiện (RoomId) phải lớn hơn 0.");
        }

        if (request.ServicePointId is <= 0)
        {
            throw new ValidationException("ID điểm phục vụ (ServicePointId) phải lớn hơn 0.");
        }

        string? normalizedVisitCode = null;
        if (!string.IsNullOrWhiteSpace(request.VisitCode))
        {
            normalizedVisitCode = request.VisitCode.Trim();
            if (normalizedVisitCode.Length > 40 || normalizedVisitCode.Contains('<') || normalizedVisitCode.Contains('>'))
            {
                throw new ValidationException("Mã lượt khám lọc không hợp lệ.");
            }
        }

        string? normalizedKeyword = null;
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            normalizedKeyword = request.Keyword.Trim();
            if (normalizedKeyword.Length > 120 || normalizedKeyword.Contains('<') || normalizedKeyword.Contains('>'))
            {
                throw new ValidationException("Từ khóa tìm kiếm phiếu chỉ định cận lâm sàng không hợp lệ.");
            }
        }

        return new ValidatedExecutionListFilter(
            Status: normalizedStatus,
            QueueStatus: normalizedQueueStatus,
            Category: normalizedCategory,
            Priority: normalizedPriority,
            VisitId: request.VisitId,
            VisitCode: normalizedVisitCode,
            ServiceId: request.ServiceId,
            RoomId: request.RoomId,
            ServicePointId: request.ServicePointId,
            RequiredForConclusion: request.RequiredForConclusion,
            ActiveOnly: request.ActiveOnly,
            ReadyToExecuteOnly: request.ReadyToExecuteOnly,
            Keyword: normalizedKeyword,
            Limit: request.Limit);
    }

    private static IReadOnlyList<DiagnosticServiceExecutionItemDto> ApplyListFilters(
        IReadOnlyList<DiagnosticServiceExecutionItemDto> items,
        ValidatedExecutionListFilter filter)
    {
        IEnumerable<DiagnosticServiceExecutionItemDto> query = items;

        if (filter.ActiveOnly)
        {
            query = query.Where(i => i.Status is "CONFIRMED" or "IN_PROGRESS");
        }

        if (filter.ReadyToExecuteOnly)
        {
            query = query.Where(i => i.CanStartExecution || i.Status == "IN_PROGRESS");
        }

        if (filter.Status is not null)
        {
            query = query.Where(i => string.Equals(i.Status, filter.Status, StringComparison.OrdinalIgnoreCase));
        }

        if (filter.QueueStatus is not null)
        {
            query = query.Where(i => string.Equals(i.QueueStatus, filter.QueueStatus, StringComparison.OrdinalIgnoreCase));
        }

        if (filter.Category is not null)
        {
            query = query.Where(i => string.Equals(i.Category, filter.Category, StringComparison.OrdinalIgnoreCase));
        }

        if (filter.Priority is not null)
        {
            query = query.Where(i => string.Equals(i.Priority, filter.Priority, StringComparison.OrdinalIgnoreCase));
        }

        if (filter.VisitId.HasValue)
        {
            query = query.Where(i => i.VisitId == filter.VisitId.Value);
        }

        if (filter.VisitCode is not null)
        {
            query = query.Where(i => string.Equals(i.VisitCode, filter.VisitCode, StringComparison.OrdinalIgnoreCase));
        }

        if (filter.ServiceId.HasValue)
        {
            query = query.Where(i => i.ServiceId == filter.ServiceId.Value);
        }

        if (filter.RoomId.HasValue)
        {
            query = query.Where(i => i.RoomId == filter.RoomId.Value);
        }

        if (filter.ServicePointId.HasValue)
        {
            query = query.Where(i => i.ServicePointId == filter.ServicePointId.Value);
        }

        if (filter.RequiredForConclusion.HasValue)
        {
            query = query.Where(i => i.RequiredForConclusion == filter.RequiredForConclusion.Value);
        }

        if (filter.Keyword is not null)
        {
            var kw = filter.Keyword;
            query = query.Where(i =>
                i.OrderCode.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                i.VisitCode.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                i.Patient.FullName.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                i.Patient.PatientCode.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                (i.Patient.Phone is not null && i.Patient.Phone.Contains(kw, StringComparison.OrdinalIgnoreCase)) ||
                i.ServiceName.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                i.ServiceCode.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                i.OrderingDoctorName.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                (i.TicketNo is not null && i.TicketNo.Contains(kw, StringComparison.OrdinalIgnoreCase)) ||
                (i.SpecimenCode is not null && i.SpecimenCode.Contains(kw, StringComparison.OrdinalIgnoreCase)));
        }

        return query
            .OrderBy(i => GetExecutionSortTier(i))
            .ThenByDescending(i => GetPriorityRank(i.Priority))
            .ThenBy(i => i.OrderedAt)
            .ThenBy(i => i.OrderId)
            .Take(filter.Limit)
            .ToList();
    }

    private static int GetExecutionSortTier(DiagnosticServiceExecutionItemDto item)
    {
        if (item.Status == "IN_PROGRESS")
        {
            return 0;
        }

        if (item.Status == "CONFIRMED")
        {
            return 1;
        }

        if (item.Status == "ORDERED")
        {
            return 2;
        }

        return 3;
    }

    private static DiagnosticServiceExecutionSummaryStatsDto BuildSummaryStats(
        IReadOnlyList<DiagnosticServiceExecutionItemDto> items)
    {
        var pendingConfirmation = 0;
        var waitingExecution = 0;
        var readyToStart = 0;
        var inProgress = 0;
        var completed = 0;
        var cancelled = 0;
        var urgentOrStat = 0;
        var mandatory = 0;
        var lab = 0;
        var imaging = 0;
        var functional = 0;

        foreach (var item in items)
        {
            switch (item.Status)
            {
                case "ORDERED":
                    pendingConfirmation++;
                    break;
                case "CONFIRMED":
                    waitingExecution++;
                    if (item.CanStartExecution || item.QueueStatus is "READY" or "CALLED")
                    {
                        readyToStart++;
                    }
                    break;
                case "IN_PROGRESS":
                    inProgress++;
                    break;
                case "COMPLETED":
                    completed++;
                    break;
                case "CANCELLED":
                    cancelled++;
                    break;
            }

            if (item.Priority is "URGENT" or "STAT" && item.Status != "CANCELLED")
            {
                urgentOrStat++;
            }

            if (item.RequiredForConclusion && item.Status != "CANCELLED")
            {
                mandatory++;
            }

            switch (item.Category)
            {
                case "IMAGING": imaging++; break;
                case "FUNCTIONAL": functional++; break;
                default: lab++; break;
            }
        }

        return new DiagnosticServiceExecutionSummaryStatsDto(
            TotalOrders: items.Count,
            PendingConfirmationCount: pendingConfirmation,
            WaitingExecutionCount: waitingExecution,
            ReadyToStartCount: readyToStart,
            InProgressCount: inProgress,
            CompletedCount: completed,
            CancelledCount: cancelled,
            UrgentOrStatCount: urgentOrStat,
            MandatoryCount: mandatory,
            LaboratoryCount: lab,
            ImagingCount: imaging,
            FunctionalCount: functional);
    }

    // ======================= SHARED HELPERS =======================

    private async Task SaveChangesWithConcurrencyGuardAsync(CancellationToken ct)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("Dữ liệu phiếu chỉ định hoặc hàng đợi cận lâm sàng vừa được cập nhật bởi thao tác khác. Vui lòng tải lại và thử lại.");
        }
    }

    private static IReadOnlyList<DiagnosticServicePointOptionDto> BuildServicePointOptions(
        IReadOnlyList<QueueServicePoint> servicePoints,
        IReadOnlyDictionary<long, HospitalRoom> roomsById)
    {
        return servicePoints
            .OrderBy(sp => sp.IsActive == false ? 1 : 0)
            .ThenBy(sp => sp.Id)
            .Select(sp =>
            {
                var room = sp.RoomId.HasValue && roomsById.TryGetValue(sp.RoomId.Value, out var foundRoom) ? foundRoom : null;
                var isRoomActive = room is null || string.Equals(room.Status?.Trim(), "ACTIVE", StringComparison.OrdinalIgnoreCase);
                return new DiagnosticServicePointOptionDto(
                    ServicePointId: sp.Id,
                    Name: sp.Name,
                    ServiceId: sp.ServiceId,
                    RoomId: room?.Id ?? sp.RoomId,
                    RoomCode: room?.RoomCode,
                    RoomName: room?.Name,
                    RoomLocation: room?.Location,
                    IsActive: sp.IsActive != false,
                    IsRoomActive: isRoomActive);
            })
            .ToList();
    }

    private static long? ResolveOrderHospitalId(DiagnosticOrder order, OrderLookupBundle bundle)
    {
        if (order.ServiceId.HasValue && bundle.ServicesById.TryGetValue(order.ServiceId.Value, out var service) && service.HospitalId.HasValue)
        {
            return service.HospitalId.Value;
        }

        if (order.OrderingDoctorId.HasValue && bundle.DoctorsById.TryGetValue(order.OrderingDoctorId.Value, out var doctor))
        {
            if (doctor.HospitalId.HasValue)
            {
                return doctor.HospitalId.Value;
            }

            if (doctor.Staff?.HospitalId.HasValue == true)
            {
                return doctor.Staff.HospitalId.Value;
            }
        }

        if (order.QueueEntryId.HasValue && bundle.QueueEntriesById.TryGetValue(order.QueueEntryId.Value, out var qe))
        {
            var spId = qe.CurrentServicePointId ?? qe.ServicePointId;
            if (spId.HasValue && bundle.ServicePointsById.TryGetValue(spId.Value, out var sp) && sp.HospitalId.HasValue)
            {
                return sp.HospitalId.Value;
            }
        }

        if (order.VisitId.HasValue && bundle.VisitsById.TryGetValue(order.VisitId.Value, out var visit))
        {
            if (visit.ExaminationRoomId.HasValue && bundle.RoomsById.TryGetValue(visit.ExaminationRoomId.Value, out var room) && room.HospitalId.HasValue)
            {
                return room.HospitalId.Value;
            }

            if (visit.AssignedDoctorId.HasValue && bundle.DoctorsById.TryGetValue(visit.AssignedDoctorId.Value, out var visitDoc))
            {
                if (visitDoc.HospitalId.HasValue)
                {
                    return visitDoc.HospitalId.Value;
                }

                if (visitDoc.Staff?.HospitalId.HasValue == true)
                {
                    return visitDoc.Staff.HospitalId.Value;
                }
            }
        }

        return null;
    }

    private static bool IsOrderInHospitalScope(DiagnosticOrder order, long hospitalId, OrderLookupBundle bundle)
    {
        var orderHospitalId = ResolveOrderHospitalId(order, bundle);
        return !orderHospitalId.HasValue || orderHospitalId.Value == hospitalId;
    }

    private static void EnsureOrderHospitalOwnership(DiagnosticOrder order, long hospitalId, OrderLookupBundle bundle)
    {
        var orderHospitalId = ResolveOrderHospitalId(order, bundle);
        if (orderHospitalId.HasValue && orderHospitalId.Value != hospitalId)
        {
            throw new ForbiddenException("Phiếu chỉ định cận lâm sàng thuộc cơ sở y tế khác, nhân viên không có quyền truy cập hoặc cập nhật.");
        }
    }

    private static QueueServicePoint? ResolveDiagnosticServicePointForCatalog(long serviceId, IReadOnlyList<QueueServicePoint> hospitalDiagPoints)
    {
        var serviceSpecific = hospitalDiagPoints
            .Where(sp => sp.ServiceId == serviceId)
            .OrderBy(sp => sp.IsActive == false ? 1 : 0)
            .ThenBy(sp => sp.Id)
            .FirstOrDefault();
        if (serviceSpecific is not null)
        {
            return serviceSpecific;
        }

        return hospitalDiagPoints.Where(sp => sp.IsActive != false).OrderBy(sp => sp.ServiceId.HasValue ? 1 : 0).ThenBy(sp => sp.Id).FirstOrDefault();
    }

    private static string ResolveDiagnosticCategory(Service service, IReadOnlyDictionary<long, Specialty> specialtiesById)
    {
        var specCode = service.SpecialtyId.HasValue && specialtiesById.TryGetValue(service.SpecialtyId.Value, out var spec) ? spec.SpecialtyCode : null;
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

    private static string FormatCategoryLabel(string category) => category switch
    {
        "IMAGING" => "Chẩn đoán hình ảnh",
        "FUNCTIONAL" => "Thăm dò chức năng",
        _ => "Xét nghiệm",
    };

    private static string FormatAuditActionLabel(string? action) => action switch
    {
        ExecutionStartedAction => "Bắt đầu thực hiện",
        ExecutionCompletedAction => "Hoàn tất kỹ thuật",
        ExecutionStatusUpdatedAction => "Tạm dừng / trả về hàng đợi",
        ExecutionCancelledAction => "Hủy thực hiện",
        "DIAGNOSTIC_ORDER_CONFIRMED" => "Xác nhận phiếu chỉ định",
        "DIAGNOSTIC_PATIENT_RECEIVED" => "Tiếp nhận bệnh nhân",
        "DIAGNOSTIC_ORDER_CREATED" or "ADDITIONAL_DIAGNOSTIC_ORDER_CREATED" => "Tạo phiếu chỉ định",
        _ => action ?? string.Empty,
    };

    private static string FormatDoctorName(Doctor? doctor)
    {
        if (doctor is null)
        {
            return "BS. Chỉ định CLS";
        }

        var docTitle = string.IsNullOrWhiteSpace(doctor.ProfessionalTitle) ? "BS" : doctor.ProfessionalTitle.Trim();
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

        var date = order.OrderedAt.HasValue ? DateOnly.FromDateTime(order.OrderedAt.Value) : referenceDate;
        return $"DIA-{date:yyyyMM}-{order.Id:D4}";
    }

    private static string FormatVisitCode(Visit visit, DateOnly referenceDate)
    {
        if (!string.IsNullOrWhiteSpace(visit.VisitCode))
        {
            return visit.VisitCode.Trim();
        }

        var date = visit.CheckedInAt.HasValue ? DateOnly.FromDateTime(visit.CheckedInAt.Value) : referenceDate;
        return $"VIS-{date:yyyyMM}-{visit.Id:D4}";
    }

    private static string ResolveEffectiveOrderStatus(DiagnosticOrder order, IReadOnlySet<long> finalizedOrderIds)
    {
        var raw = NormalizeOrderStatus(order.Status);
        if (raw == "CANCELLED")
        {
            return "CANCELLED";
        }

        if (finalizedOrderIds.Contains(order.Id) || raw is "COMPLETED" or "DONE" or "FINALIZED" or "VERIFIED")
        {
            return "COMPLETED";
        }

        return raw;
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
            "STAT" or "EMERGENCY" => "STAT",
            _ => "NORMAL",
        };
    }

    private static int GetPriorityRank(string? priority) => NormalizePriority(priority) switch
    {
        "STAT" => 3,
        "URGENT" => 2,
        _ => 1,
    };

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

    private static string NormalizeVisitStatus(string? status)
    {
        var normalized = AppointmentBookingRules.NormalizeStatusKey(status);
        return string.IsNullOrWhiteSpace(normalized) ? "WAITING" : normalized;
    }

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

    private static string? GetJsonString(JsonElement root, string name)
        => root.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String ? prop.GetString() : null;

    private static int? GetJsonInt(JsonElement root, string name)
        => root.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var v) ? v : null;

    private static long? GetJsonLong(JsonElement root, string name)
        => root.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Number && prop.TryGetInt64(out var v) ? v : null;

    // ======================= PRIVATE RECORDS =======================

    private sealed record DiagnosticStaffAccessContext(
        Hospital Hospital,
        Staff Staff,
        string DepartmentName,
        bool HasQueueManagePermission,
        bool HasExecutionPermission,
        DiagnosticStaffProfileDto Profile);

    private sealed record ParsedOrderIdentifier(
        long? OrderId,
        long? QueueEntryId,
        long? VisitId,
        string? Code,
        string RawInput);

    private sealed record ValidatedExecutionInput(
        long? ServicePointId,
        long? RoomId,
        string? PresenceStatus,
        string? SpecimenCode,
        string? SpecimenType,
        string? EquipmentCode,
        string? ExecutionNote,
        string? Instruction,
        string? Reason,
        int? ExpectedStateVersion);

    private sealed record ValidatedExecutionListFilter(
        string? Status,
        string? QueueStatus,
        string? Category,
        string? Priority,
        long? VisitId,
        string? VisitCode,
        long? ServiceId,
        long? RoomId,
        long? ServicePointId,
        bool? RequiredForConclusion,
        bool ActiveOnly,
        bool ReadyToExecuteOnly,
        string? Keyword,
        int Limit);

    private sealed record ResolvedExecutionPlan(
        DiagnosticOrder Order,
        Visit Visit,
        Patient? Patient,
        Service Service,
        string Category,
        QueueServicePoint? ServicePoint,
        HospitalRoom? Room,
        QueueEntry? LinkedQueueEntry,
        string CurrentOrderStatus);

    private sealed record DiagnosticOrderResultState(
        bool HasResult,
        bool IsResultFinalized,
        string ResultStatus);

    private sealed record DiagnosticOrderAuditMetadata(
        bool IsAdditionalOrder,
        int? OrderSequenceNo,
        long? FollowUpToOrderId,
        string? FollowUpToOrderCode,
        string? AdditionalOrderReason,
        string? ConfirmationNote,
        DateTime? ReceivedAt,
        long? ReceivedByStaffId,
        string? ReceivedByStaffCode,
        string? ReceivedByStaffName,
        string? ReceptionNote,
        long? PerformedByStaffId,
        string? PerformedByStaffCode,
        string? PerformedByStaffName);

    private sealed record DiagnosticOrderExecutionMetadata(
        DateTime? StartedAt,
        long? StartedByStaffId,
        string? StartedByStaffCode,
        string? StartedByStaffName,
        DateTime? CompletedAt,
        long? CompletedByStaffId,
        string? CompletedByStaffCode,
        string? CompletedByStaffName,
        string? SpecimenCode,
        string? SpecimenType,
        string? EquipmentCode,
        string? ExecutionNote,
        string? LastTransitionReason);

    private sealed record OrderLookupBundle(
        Dictionary<long, Service> ServicesById,
        Dictionary<long, decimal> ServicePricesByServiceId,
        Dictionary<long, Department> DepartmentsById,
        Dictionary<long, Specialty> SpecialtiesById,
        Dictionary<long, Visit> VisitsById,
        Dictionary<long, Patient> PatientsById,
        Dictionary<long, Doctor> DoctorsById,
        Dictionary<long, QueueEntry> QueueEntriesById,
        Dictionary<long, QueueServicePoint> ServicePointsById,
        IReadOnlyList<QueueServicePoint> HospitalDiagnosticServicePoints,
        Dictionary<long, HospitalRoom> RoomsById,
        Dictionary<long, Staff> StaffById,
        HashSet<long> FinalizedOrderIds,
        Dictionary<long, MedicalRecordRevision> LatestRevisionByVisitId,
        Dictionary<long, int> SequenceByOrderId,
        Dictionary<long, DiagnosticOrderAuditMetadata> AuditMetadataByOrderId);
}
