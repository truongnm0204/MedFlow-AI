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

public sealed class DiagnosticPatientReceiveService : IDiagnosticPatientReceiveService
{
    public const string DiagnosticPatientReceivedAction = "DIAGNOSTIC_PATIENT_RECEIVED";
    public const string DiagnosticOrderConfirmedAction = "DIAGNOSTIC_ORDER_CONFIRMED";
    public const string DiagnosticOrderEntityType = "DIAGNOSTIC_ORDER";
    public const string QueueManagePermissionCode = "QUEUE_MANAGE";

    private const string DiagnosticStaffRoleCode = "DIAGNOSTIC_STAFF";
    private const string DefaultDepartmentName = "Khoa Cận lâm sàng";

    private static readonly string[] AllowedReadPermissions =
    [
        "QUEUE_MANAGE",
        "DIAGNOSTIC_RESULT_WRITE",
        "DIAGNOSTIC_ORDER",
    ];

    private static readonly string[] FallbackDiagnosticPermissions =
    [
        "DIAGNOSTIC_RESULT_WRITE",
        "QUEUE_MANAGE",
    ];

    private static readonly HashSet<string> ValidCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "LABORATORY",
        "IMAGING",
        "FUNCTIONAL",
    };

    private static readonly HashSet<string> ValidListStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "ORDERED",
        "PENDING",
        "PENDING_CONFIRMATION",
        "CONFIRMED",
        "IN_PROGRESS",
        "COMPLETED",
        "CANCELLED",
    };

    private static readonly HashSet<string> ValidQueueStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "WAITING",
        "RESERVED",
        "READY",
        "CALLED",
        "IN_PROGRESS",
        "COMPLETED",
        "SKIPPED",
        "CANCELLED",
    };

    private static readonly HashSet<string> ValidPriorities = new(StringComparer.OrdinalIgnoreCase)
    {
        "NORMAL",
        "STANDARD",
        "ROUTINE",
        "URGENT",
        "STAT",
    };

    private static readonly JsonSerializerOptions AuditJsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;

    public DiagnosticPatientReceiveService(
        IApplicationDbContext dbContext,
        TimeProvider? timeProvider = null)
    {
        _db = dbContext;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<DiagnosticPatientReceiveListResponseDto> GetOrdersForReceiveAsync(
        long actorUserId,
        GetDiagnosticOrdersForReceiveRequest? request = null,
        CancellationToken ct = default)
    {
        var effectiveRequest = request ?? new GetDiagnosticOrdersForReceiveRequest();
        var filter = ValidateListFilter(effectiveRequest);

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var nowLocal = AppointmentBookingRules.GetVietnamNow(_timeProvider);
        var referenceDate = DateOnly.FromDateTime(nowLocal);

        var staffAccess = await EnsureDiagnosticStaffAccessAsync(
            actorUserId,
            nowUtc,
            requireQueueManagePermission: false,
            ct);
        var hospitalId = staffAccess.Hospital.Id;

        var allOrders = await TryLoadAllDiagnosticOrdersAsync(ct);
        var lookupBundle = await LoadOrderLookupBundleAsync(allOrders, hospitalId, nowLocal, ct);

        var hospitalOrders = allOrders
            .Where(o => IsOrderInHospitalScope(o, hospitalId, lookupBundle))
            .OrderByDescending(o => o.OrderedAt ?? DateTime.MinValue)
            .ThenByDescending(o => o.Id)
            .ToList();

        var allHospitalItems = new List<DiagnosticPatientReceiveItemDto>(hospitalOrders.Count);
        foreach (var order in hospitalOrders)
        {
            var item = BuildOrderReceiveItemDto(
                order,
                staffAccess,
                lookupBundle,
                referenceDate,
                nowLocal);
            allHospitalItems.Add(item);
        }

        var summary = BuildSummaryStats(allHospitalItems);
        var filteredItems = ApplyListFilters(allHospitalItems, filter);
        var availableServicePoints = BuildServicePointOptions(
            lookupBundle.HospitalDiagnosticServicePoints,
            lookupBundle.RoomsById);

        return new DiagnosticPatientReceiveListResponseDto(
            HospitalId: staffAccess.Hospital.Id,
            HospitalCode: staffAccess.Hospital.HospitalCode,
            HospitalName: staffAccess.Hospital.Name,
            DepartmentId: staffAccess.Staff.DepartmentId,
            DepartmentName: staffAccess.DepartmentName,
            GeneratedAtUtc: nowUtc,
            StaffProfile: staffAccess.Profile,
            Summary: summary,
            AvailableServicePoints: availableServicePoints,
            Items: filteredItems);
    }

    public async Task<DiagnosticPatientReceiveDetailResponseDto> GetOrderForReceiveByIdOrCodeAsync(
        long actorUserId,
        string orderIdOrCode,
        CancellationToken ct = default)
    {
        var parsedId = ParseOrderIdentifier(orderIdOrCode);

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var nowLocal = AppointmentBookingRules.GetVietnamNow(_timeProvider);
        var referenceDate = DateOnly.FromDateTime(nowLocal);

        var staffAccess = await EnsureDiagnosticStaffAccessAsync(
            actorUserId,
            nowUtc,
            requireQueueManagePermission: false,
            ct);
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

        var itemDto = BuildOrderReceiveItemDto(
            order,
            staffAccess,
            lookupBundle,
            referenceDate,
            nowLocal);

        var visit = order.VisitId.HasValue && lookupBundle.VisitsById.TryGetValue(order.VisitId.Value, out var foundVisit)
            ? foundVisit
            : null;
        var clinicalContext = await BuildClinicalSummaryAsync(
            visit,
            itemDto.VisitId,
            itemDto.VisitCode,
            itemDto.VisitStatus,
            lookupBundle,
            ct);
        var availableServicePoints = BuildServicePointOptions(
            lookupBundle.HospitalDiagnosticServicePoints
                .Where(sp => !sp.ServiceId.HasValue || sp.ServiceId.Value == itemDto.ServiceId)
                .ToList(),
            lookupBundle.RoomsById);

        return new DiagnosticPatientReceiveDetailResponseDto(
            HospitalId: staffAccess.Hospital.Id,
            HospitalCode: staffAccess.Hospital.HospitalCode,
            HospitalName: staffAccess.Hospital.Name,
            GeneratedAtUtc: nowUtc,
            StaffProfile: staffAccess.Profile,
            Order: itemDto,
            ClinicalContext: clinicalContext,
            AvailableServicePoints: availableServicePoints);
    }

    public async Task<ReceiveDiagnosticPatientResponseDto> ReceiveDiagnosticPatientAsync(
        long actorUserId,
        string orderIdOrCode,
        ReceiveDiagnosticPatientRequest? request = null,
        string? ipAddress = null,
        CancellationToken ct = default)
    {
        var parsedId = ParseOrderIdentifier(orderIdOrCode);
        var validated = ValidateReceiveRequest(request ?? new ReceiveDiagnosticPatientRequest());

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var nowLocal = AppointmentBookingRules.GetVietnamNow(_timeProvider);
        var referenceDate = DateOnly.FromDateTime(nowLocal);

        var staffAccess = await EnsureDiagnosticStaffAccessAsync(
            actorUserId,
            nowUtc,
            requireQueueManagePermission: true,
            ct);

        return await ReceivePatientsCoreAsync(
            actorUserId,
            staffAccess,
            [parsedId],
            validated,
            ipAddress,
            nowUtc,
            nowLocal,
            referenceDate,
            ct);
    }

    public async Task<ReceiveDiagnosticPatientResponseDto> ReceiveDiagnosticPatientsBatchAsync(
        long actorUserId,
        ReceiveDiagnosticPatientsBatchRequest? request = null,
        string? ipAddress = null,
        CancellationToken ct = default)
    {
        if (request is null)
        {
            throw new ValidationException("Dữ liệu tiếp nhận bệnh nhân cận lâm sàng không được để trống.");
        }

        var validated = ValidateReceiveRequest(new ReceiveDiagnosticPatientRequest(
            IsPatientPresent: request.IsPatientPresent,
            PatientPresent: request.PatientPresent,
            IsIdentityVerified: request.IsIdentityVerified,
            IdentityVerified: request.IdentityVerified,
            IsPreparationVerified: request.IsPreparationVerified,
            ServicePointId: request.ServicePointId,
            RoomId: request.RoomId,
            Priority: request.Priority,
            ManualPriority: request.ManualPriority,
            ClinicalPriority: request.ClinicalPriority,
            Instruction: request.Instruction,
            ReceptionNote: request.ReceptionNote,
            ReceiveNote: request.ReceiveNote,
            Notes: request.Notes,
            ExpectedStateVersion: request.ExpectedStateVersion));

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var nowLocal = AppointmentBookingRules.GetVietnamNow(_timeProvider);
        var referenceDate = DateOnly.FromDateTime(nowLocal);

        var staffAccess = await EnsureDiagnosticStaffAccessAsync(
            actorUserId,
            nowUtc,
            requireQueueManagePermission: true,
            ct);

        var targets = await ResolveBatchTargetIdentifiersAsync(
            request,
            staffAccess.Hospital.Id,
            nowLocal,
            referenceDate,
            ct);

        return await ReceivePatientsCoreAsync(
            actorUserId,
            staffAccess,
            targets,
            validated,
            ipAddress,
            nowUtc,
            nowLocal,
            referenceDate,
            ct);
    }

    private async Task<ReceiveDiagnosticPatientResponseDto> ReceivePatientsCoreAsync(
        long actorUserId,
        DiagnosticStaffAccessContext staffAccess,
        IReadOnlyList<ParsedOrderIdentifier> targetIdentifiers,
        ValidatedReceiveDiagnosticPatientInput validated,
        string? ipAddress,
        DateTime nowUtc,
        DateTime nowLocal,
        DateOnly referenceDate,
        CancellationToken ct)
    {
        var hospitalId = staffAccess.Hospital.Id;
        var targetOrders = new List<DiagnosticOrder>(targetIdentifiers.Count);
        var seenOrderIds = new HashSet<long>();

        foreach (var identifier in targetIdentifiers)
        {
            var resolvedOrder = await ResolveDiagnosticOrderAsync(
                identifier,
                hospitalId,
                referenceDate,
                asTracking: true,
                ct);

            if (!seenOrderIds.Add(resolvedOrder.Id))
            {
                throw new ValidationException("Danh sách phiếu chỉ định cần tiếp nhận có mã hoặc ID bị trùng lặp.");
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

        var executionPlans = new List<ResolvedPatientReceptionPlan>(targetOrders.Count);
        foreach (var order in targetOrders)
        {
            EnsureOrderHospitalOwnership(order, hospitalId, lookupBundle);
            var plan = await ValidateOrderForReceptionAsync(
                order,
                validated,
                staffAccess,
                lookupBundle,
                referenceDate,
                ct);
            executionPlans.Add(plan);
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

        try
        {
            foreach (var plan in executionPlans)
            {
                var order = plan.Order;
                var visit = plan.Visit;
                var patient = plan.Patient;
                var service = plan.Service;
                var resolvedSp = plan.ServicePoint;
                var resolvedRoom = plan.Room;
                var linkedQueueEntry = plan.LinkedQueueEntry;

                int finalStateVersion;
                int? queueNumber;
                string? ticketNo;
                string previousQueueStatus;
                string previousPresenceStatus;
                string effectiveManualPriority;

                if (linkedQueueEntry is null)
                {
                    var diagSpId = resolvedSp?.Id;
                    var maxQueueNumber = await _db.QueueEntries
                        .AsNoTracking()
                        .Where(q =>
                            q.QueueDate == referenceDate &&
                            (diagSpId.HasValue
                                ? (q.ServicePointId == diagSpId.Value || q.CurrentServicePointId == diagSpId.Value)
                                : (q.QueueType != null && q.QueueType.ToUpper() == "DIAGNOSTIC")))
                        .MaxAsync(q => (int?)q.QueueNumber, ct);

                    var nextQueueNumber = (maxQueueNumber ?? 0) + 1;
                    previousQueueStatus = "WAITING";
                    previousPresenceStatus = "NOT_ARRIVED";
                    effectiveManualPriority = validated.ManualPriority ?? ClinicalQueueRules.PriorityNormal;
                    finalStateVersion = 1;

                    linkedQueueEntry = new QueueEntry
                    {
                        VisitId = visit.Id,
                        AppointmentId = null,
                        ServicePointId = diagSpId,
                        CurrentServicePointId = diagSpId,
                        QueueDate = referenceDate,
                        QueueNumber = nextQueueNumber,
                        QueueType = "DIAGNOSTIC",
                        Status = "READY",
                        ScheduledAt = null,
                        PresenceStatus = "PRESENT",
                        ArrivedAt = nowLocal,
                        ReadyAt = nowLocal,
                        ArrivalClass = "WALK_IN",
                        ManualPriority = effectiveManualPriority,
                        StateVersion = finalStateVersion,
                        BlockedReason = null,
                    };

                    _db.QueueEntries.Add(linkedQueueEntry);
                    await SaveChangesWithConcurrencyGuardAsync(ct);

                    order.QueueEntryId = linkedQueueEntry.Id;
                    lookupBundle.QueueEntriesById[linkedQueueEntry.Id] = linkedQueueEntry;
                    if (resolvedSp is not null)
                    {
                        lookupBundle.ServicePointsById[resolvedSp.Id] = resolvedSp;
                    }
                    if (resolvedRoom is not null)
                    {
                        lookupBundle.RoomsById[resolvedRoom.Id] = resolvedRoom;
                    }

                    queueNumber = nextQueueNumber;
                    ticketNo = $"C{nextQueueNumber:D3}";
                }
                else
                {
                    previousQueueStatus = NormalizeQueueStatus(linkedQueueEntry.Status);
                    previousPresenceStatus = string.IsNullOrWhiteSpace(linkedQueueEntry.PresenceStatus)
                        ? "PRESENT"
                        : linkedQueueEntry.PresenceStatus.Trim().ToUpperInvariant();

                    if (resolvedSp is not null)
                    {
                        linkedQueueEntry.ServicePointId ??= resolvedSp.Id;
                        linkedQueueEntry.CurrentServicePointId = resolvedSp.Id;
                    }

                    effectiveManualPriority = validated.ManualPriority
                        ?? ClinicalQueueRules.NormalizeStoredPriority(linkedQueueEntry.ManualPriority);

                    linkedQueueEntry.Status = "READY";
                    linkedQueueEntry.PresenceStatus = "PRESENT";
                    if (!linkedQueueEntry.ArrivedAt.HasValue ||
                        previousPresenceStatus is "NOT_ARRIVED" or "LEFT")
                    {
                        linkedQueueEntry.ArrivedAt = nowLocal;
                    }
                    linkedQueueEntry.ReadyAt = nowLocal;
                    linkedQueueEntry.BlockedReason = null;
                    linkedQueueEntry.ManualPriority = effectiveManualPriority;

                    var prevStateVersion = linkedQueueEntry.StateVersion is > 0
                        ? linkedQueueEntry.StateVersion.Value
                        : 1;
                    finalStateVersion = prevStateVersion + 1;
                    linkedQueueEntry.StateVersion = finalStateVersion;

                    lookupBundle.QueueEntriesById[linkedQueueEntry.Id] = linkedQueueEntry;
                    if (resolvedSp is not null)
                    {
                        lookupBundle.ServicePointsById[resolvedSp.Id] = resolvedSp;
                    }
                    if (resolvedRoom is not null)
                    {
                        lookupBundle.RoomsById[resolvedRoom.Id] = resolvedRoom;
                    }

                    queueNumber = linkedQueueEntry.QueueNumber;
                    ticketNo = queueNumber is > 0 ? $"C{queueNumber.Value:D3}" : null;
                }

                var previousPriority = NormalizePriority(order.Priority);
                var effectivePriority = validated.Priority ?? previousPriority;
                var effectiveInstruction = validated.Instruction ?? order.Instruction;
                var orderCode = FormatOrderCode(order, referenceDate);

                order.OrderCode = orderCode;
                order.Priority = effectivePriority;
                if (validated.Instruction is not null)
                {
                    order.Instruction = validated.Instruction;
                }

                var visitCode = FormatVisitCode(visit, referenceDate);
                var effectiveClinicalPriority = ClinicalQueueRules.ResolveEffectivePriority(
                    effectiveManualPriority,
                    patient?.DateOfBirth,
                    referenceDate);

                var effectiveReason = validated.ReceptionNote
                    ?? $"Nhân viên cận lâm sàng tiếp nhận bệnh nhân cho phiếu chỉ định {orderCode} ({service.Name}) vào hàng đợi phòng kỹ thuật.";

                var changePayload = new
                {
                    diagnosticOrderId = order.Id,
                    orderCode,
                    visitId = visit.Id,
                    visitCode,
                    patientId = visit.PatientId,
                    serviceId = service.Id,
                    serviceCode = service.ServiceCode,
                    serviceName = service.Name,
                    category = plan.Category,
                    orderStatus = "CONFIRMED",
                    previousPriority,
                    priority = effectivePriority,
                    instruction = effectiveInstruction,
                    receptionNote = validated.ReceptionNote,
                    isPatientPresent = true,
                    isIdentityVerified = true,
                    isPreparationVerified = true,
                    receivedByStaffId = staffAccess.Staff.Id,
                    receivedByStaffCode = staffAccess.Staff.StaffCode,
                    receivedByStaffName = staffAccess.Profile.FullName,
                    receivedAt = nowLocal,
                    queueEntryId = linkedQueueEntry.Id,
                    ticketNo,
                    queueNumber,
                    previousQueueStatus,
                    newQueueStatus = "READY",
                    previousPresenceStatus,
                    newPresenceStatus = "PRESENT",
                    manualPriority = effectiveManualPriority,
                    effectiveClinicalPriority,
                    arrivedAt = linkedQueueEntry.ArrivedAt,
                    readyAt = linkedQueueEntry.ReadyAt,
                    servicePointId = resolvedSp?.Id,
                    servicePointName = resolvedSp?.Name,
                    roomId = resolvedRoom?.Id ?? resolvedSp?.RoomId,
                    roomCode = resolvedRoom?.RoomCode,
                    roomName = resolvedRoom?.Name,
                    stateVersion = finalStateVersion,
                    ipAddress,
                };

                _db.AuditLogs.Add(new AuditLog
                {
                    ActorUserId = actorUserId,
                    HospitalId = hospitalId,
                    Action = DiagnosticPatientReceivedAction,
                    EntityType = DiagnosticOrderEntityType,
                    EntityId = order.Id.ToString(CultureInfo.InvariantCulture),
                    Reason = effectiveReason,
                    ChangeData = JsonSerializer.Serialize(changePayload, AuditJsonOptions),
                    IpAddress = QueueEntryRequestParsing.ParseIpAddress(ipAddress),
                    OccurredAt = nowUtc,
                });

                lookupBundle.StaffById[staffAccess.Staff.Id] = staffAccess.Staff;
                lookupBundle.AuditMetadataByOrderId[order.Id] = lookupBundle.AuditMetadataByOrderId.TryGetValue(order.Id, out var existingMeta)
                    ? existingMeta with
                    {
                        ReceivedAt = nowLocal,
                        ReceivedByStaffId = staffAccess.Staff.Id,
                        ReceivedByStaffCode = staffAccess.Staff.StaffCode,
                        ReceivedByStaffName = staffAccess.Profile.FullName,
                        ReceptionNote = validated.ReceptionNote ?? existingMeta.ReceptionNote,
                    }
                    : new DiagnosticOrderAuditMetadata(
                        IsAdditionalOrder: false,
                        OrderSequenceNo: null,
                        FollowUpToOrderId: null,
                        FollowUpToOrderCode: null,
                        AdditionalOrderReason: null,
                        ConfirmationNote: null,
                        ReceivedAt: nowLocal,
                        ReceivedByStaffId: staffAccess.Staff.Id,
                        ReceivedByStaffCode: staffAccess.Staff.StaffCode,
                        ReceivedByStaffName: staffAccess.Profile.FullName,
                        ReceptionNote: validated.ReceptionNote);
            }

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

        var receivedDtos = new List<DiagnosticPatientReceiveItemDto>(executionPlans.Count);
        foreach (var plan in executionPlans)
        {
            var dto = BuildOrderReceiveItemDto(
                plan.Order,
                staffAccess,
                lookupBundle,
                referenceDate,
                nowLocal);
            receivedDtos.Add(dto);
        }

        return new ReceiveDiagnosticPatientResponseDto(
            ActionOutcome: DiagnosticPatientReceivedAction,
            ReceivedCount: receivedDtos.Count,
            ReceivedAt: nowLocal,
            ReceivedByStaff: staffAccess.Profile,
            Order: receivedDtos[0],
            ReceivedOrders: receivedDtos);
    }

    private async Task<ResolvedPatientReceptionPlan> ValidateOrderForReceptionAsync(
        DiagnosticOrder order,
        ValidatedReceiveDiagnosticPatientInput validated,
        DiagnosticStaffAccessContext staffAccess,
        OrderLookupBundle lookupBundle,
        DateOnly referenceDate,
        CancellationToken ct)
    {
        var hospitalId = staffAccess.Hospital.Id;
        var orderCode = FormatOrderCode(order, referenceDate);
        var orderStatus = ResolveEffectiveOrderStatus(order, lookupBundle.FinalizedOrderIds);

        if (orderStatus == "CANCELLED")
        {
            throw new ConflictException($"Phiếu chỉ định cận lâm sàng '{orderCode}' đã bị hủy, không thể tiếp nhận bệnh nhân.");
        }

        if (orderStatus is "COMPLETED" or "DONE" or "FINALIZED" or "VERIFIED")
        {
            throw new ConflictException($"Phiếu chỉ định cận lâm sàng '{orderCode}' đã hoàn tất kết quả (COMPLETED), không thể tiếp nhận lại.");
        }

        if (orderStatus == "IN_PROGRESS")
        {
            throw new ConflictException($"Phiếu chỉ định cận lâm sàng '{orderCode}' đang trong quá trình thực hiện kỹ thuật (IN_PROGRESS), không thể tiếp nhận lại.");
        }

        if (orderStatus == "ORDERED")
        {
            throw new ConflictException($"Phiếu chỉ định cận lâm sàng '{orderCode}' chưa được xác nhận điều kiện xử lý (ORDERED). Vui lòng xác nhận phiếu chỉ định (UC39) trước khi tiếp nhận bệnh nhân.");
        }

        if (orderStatus != "CONFIRMED")
        {
            throw new ConflictException($"Phiếu chỉ định cận lâm sàng '{orderCode}' đang ở trạng thái '{orderStatus}', không hợp lệ để tiếp nhận bệnh nhân.");
        }

        var visit = order.VisitId.HasValue && lookupBundle.VisitsById.TryGetValue(order.VisitId.Value, out var foundVisit)
            ? foundVisit
            : null;
        if (visit is null)
        {
            throw new ConflictException("Phiếu chỉ định cận lâm sàng không gắn với lượt khám hợp lệ.");
        }

        var visitStatus = AppointmentBookingRules.NormalizeStatusKey(visit.Status);
        if (visitStatus is "CANCELLED" or "NO_SHOW")
        {
            throw new ConflictException("Không thể tiếp nhận bệnh nhân cho phiếu chỉ định cận lâm sàng của lượt khám đã hủy hoặc vắng mặt (NO_SHOW).");
        }

        if (visitStatus is "COMPLETED" or "DONE")
        {
            throw new ConflictException("Lượt khám đã hoàn tất (COMPLETED), không thể tiếp nhận bệnh nhân cận lâm sàng.");
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

        if (validated.ManualPriority is not null)
        {
            ClinicalQueueRules.EnsurePatientEligible(validated.ManualPriority, patient, referenceDate);
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

        QueueEntry? linkedQueueEntry = null;
        if (order.QueueEntryId.HasValue)
        {
            linkedQueueEntry = await _db.QueueEntries
                .FirstOrDefaultAsync(q => q.Id == order.QueueEntryId.Value, ct);
        }

        if (linkedQueueEntry is not null)
        {
            if (!string.IsNullOrWhiteSpace(linkedQueueEntry.QueueType) &&
                !string.Equals(linkedQueueEntry.QueueType.Trim(), "DIAGNOSTIC", StringComparison.OrdinalIgnoreCase))
            {
                throw new ConflictException("Lượt xếp hàng liên kết với phiếu chỉ định không thuộc loại hàng đợi cận lâm sàng (DIAGNOSTIC).");
            }

            var queueStatus = NormalizeQueueStatus(linkedQueueEntry.Status);
            if (queueStatus is "READY" or "CALLED")
            {
                throw new ConflictException($"Bệnh nhân của phiếu chỉ định '{orderCode}' đã được tiếp nhận và đang sẵn sàng trong hàng đợi phòng kỹ thuật ({queueStatus}).");
            }

            if (queueStatus is "IN_PROGRESS" or "COMPLETED" or "CANCELLED")
            {
                throw new ConflictException($"Lượt xếp hàng cận lâm sàng của phiếu chỉ định '{orderCode}' đang ở trạng thái '{queueStatus}', không thể tiếp nhận bệnh nhân.");
            }

            if (queueStatus is not ("WAITING" or "RESERVED" or "SKIPPED"))
            {
                throw new ConflictException($"Lượt xếp hàng cận lâm sàng của phiếu chỉ định '{orderCode}' đang ở trạng thái '{queueStatus}', không hợp lệ để tiếp nhận bệnh nhân.");
            }

            var currentStateVersion = linkedQueueEntry.StateVersion is > 0
                ? linkedQueueEntry.StateVersion.Value
                : 1;
            if (validated.ExpectedStateVersion.HasValue && validated.ExpectedStateVersion.Value != currentStateVersion)
            {
                throw new ConflictException("Dữ liệu hàng đợi cận lâm sàng đã thay đổi bởi thao tác khác (state_version mismatch). Vui lòng tải lại.");
            }
        }

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

        var category = ResolveDiagnosticCategory(service, lookupBundle.SpecialtiesById);

        return new ResolvedPatientReceptionPlan(
            Order: order,
            Visit: visit,
            Patient: patient,
            Service: service,
            Category: category,
            ServicePoint: resolvedSp,
            Room: resolvedRoom,
            LinkedQueueEntry: linkedQueueEntry);
    }

    private DiagnosticPatientReceiveItemDto BuildOrderReceiveItemDto(
        DiagnosticOrder order,
        DiagnosticStaffAccessContext staffAccess,
        OrderLookupBundle lookupBundle,
        DateOnly referenceDate,
        DateTime nowLocal)
    {
        var visit = order.VisitId.HasValue && lookupBundle.VisitsById.TryGetValue(order.VisitId.Value, out var foundVisit)
            ? foundVisit
            : null;
        var visitId = visit?.Id ?? order.VisitId ?? 0;
        var visitCode = visit is not null
            ? FormatVisitCode(visit, referenceDate)
            : (visitId > 0 ? $"VIS-{referenceDate:yyyyMM}-{visitId:D4}" : string.Empty);
        var visitStatus = visit is not null
            ? NormalizeVisitStatus(visit.Status)
            : "UNKNOWN";

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

        var category = service is not null
            ? ResolveDiagnosticCategory(service, lookupBundle.SpecialtiesById)
            : "LABORATORY";

        var orderCode = FormatOrderCode(order, referenceDate);
        var effectiveStatus = ResolveEffectiveOrderStatus(order, lookupBundle.FinalizedOrderIds);
        var priority = NormalizePriority(order.Priority);

        var confirmedStaff = order.ConfirmedByStaffId.HasValue &&
                             lookupBundle.StaffById.TryGetValue(order.ConfirmedByStaffId.Value, out var foundConfirmedStaff)
            ? foundConfirmedStaff
            : null;

        var queueNumber = queueEntry?.QueueNumber;
        var ticketNo = queueNumber is > 0 ? $"C{queueNumber.Value:D3}" : null;
        var queueStatus = queueEntry is not null ? NormalizeQueueStatus(queueEntry.Status) : null;
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

        lookupBundle.AuditMetadataByOrderId.TryGetValue(order.Id, out var auditMeta);
        var sequenceNo = auditMeta?.OrderSequenceNo
            ?? (lookupBundle.SequenceByOrderId.TryGetValue(order.Id, out var computedSeq) ? computedSeq : 1);
        var isAdditionalOrder = auditMeta is not null
            ? auditMeta.IsAdditionalOrder
            : sequenceNo > 1;

        var receivedByStaffId = auditMeta?.ReceivedByStaffId;
        var receivedStaff = receivedByStaffId.HasValue &&
                            lookupBundle.StaffById.TryGetValue(receivedByStaffId.Value, out var foundReceivedStaff)
            ? foundReceivedStaff
            : null;
        var receivedAt = auditMeta?.ReceivedAt
            ?? (queueStatus is "READY" or "CALLED" or "IN_PROGRESS" or "COMPLETED"
                ? (queueEntry?.ReadyAt ?? queueEntry?.ArrivedAt)
                : null);

        var eligibility = EvaluateReceiveEligibility(
            order,
            orderCode,
            effectiveStatus,
            visit,
            patient,
            service,
            dept,
            sp,
            room,
            queueEntry,
            staffAccess.HasQueueManagePermission);

        return new DiagnosticPatientReceiveItemDto(
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
            DepartmentId: dept?.Id ?? service?.DepartmentId,
            DepartmentCode: dept?.DepartmentCode,
            DepartmentName: dept?.Name ?? DefaultDepartmentName,
            SpecialtyId: spec?.Id ?? service?.SpecialtyId,
            SpecialtyCode: spec?.SpecialtyCode,
            SpecialtyName: spec?.Name ?? "Cận lâm sàng",
            UnitPrice: unitPrice,
            RequiresResultVerification: service?.RequiresResultVerification ?? true,
            Priority: priority,
            Status: effectiveStatus,
            RequiredForConclusion: order.RequiredForConclusion ?? true,
            Instruction: order.Instruction,
            OrderedAt: order.OrderedAt ?? nowLocal,
            ConfirmedAt: order.ConfirmedAt,
            ConfirmedByStaffId: order.ConfirmedByStaffId,
            ConfirmedByStaffCode: confirmedStaff?.StaffCode,
            ConfirmedByStaffName: confirmedStaff?.FullName,
            ConfirmationNote: auditMeta?.ConfirmationNote,
            ReceivedAt: receivedAt,
            ReceivedByStaffId: receivedByStaffId,
            ReceivedByStaffCode: auditMeta?.ReceivedByStaffCode ?? receivedStaff?.StaffCode,
            ReceivedByStaffName: auditMeta?.ReceivedByStaffName ?? receivedStaff?.FullName,
            ReceptionNote: auditMeta?.ReceptionNote,
            QueueEntryId: order.QueueEntryId,
            FormattedQueueEntryId: order.QueueEntryId.HasValue ? $"q-{order.QueueEntryId.Value:D3}" : null,
            TicketNo: ticketNo,
            QueueNumber: queueNumber,
            QueueDate: queueEntry?.QueueDate,
            QueueStatus: queueStatus,
            PresenceStatus: presenceStatus,
            ManualPriority: manualPriority,
            EffectiveClinicalPriority: effectiveClinicalPriority,
            ArrivedAt: queueEntry?.ArrivedAt,
            ReadyAt: queueEntry?.ReadyAt,
            StateVersion: stateVersion,
            ServicePointId: sp?.Id ?? spId,
            ServicePointName: sp?.Name,
            RoomId: room?.Id ?? sp?.RoomId,
            RoomCode: room?.RoomCode,
            RoomName: room?.Name,
            RoomLocation: room?.Location,
            IsAdditionalOrder: isAdditionalOrder,
            OrderSequenceNo: sequenceNo,
            FollowUpToOrderId: auditMeta?.FollowUpToOrderId,
            FollowUpToOrderCode: auditMeta?.FollowUpToOrderCode,
            AdditionalOrderReason: auditMeta?.AdditionalOrderReason,
            CanReceive: eligibility.CanReceive,
            DisabledReason: eligibility.DisabledReason,
            Eligibility: eligibility);
    }

    private static DiagnosticPatientReceiveEligibilityDto EvaluateReceiveEligibility(
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
        bool hasQueueManagePermission)
    {
        var isOrderConfirmed = effectiveOrderStatus == "CONFIRMED";

        var normalizedQueueStatus = queueEntry is not null
            ? NormalizeQueueStatus(queueEntry.Status)
            : null;
        var isDiagQueue = queueEntry is null ||
            string.IsNullOrWhiteSpace(queueEntry.QueueType) ||
            string.Equals(queueEntry.QueueType.Trim(), "DIAGNOSTIC", StringComparison.OrdinalIgnoreCase);
        var isQueueWaitingForReception = isDiagQueue &&
            (normalizedQueueStatus is null or "WAITING" or "RESERVED" or "SKIPPED");

        var visitStatus = visit is not null ? AppointmentBookingRules.NormalizeStatusKey(visit.Status) : string.Empty;
        var isVisitEligible = visit is not null &&
            visitStatus is not ("CANCELLED" or "NO_SHOW" or "COMPLETED" or "DONE");

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

        string? disabledReason = null;
        if (!isOrderConfirmed)
        {
            disabledReason = effectiveOrderStatus switch
            {
                "ORDERED" => $"Phiếu chỉ định cận lâm sàng '{orderCode}' chưa được xác nhận điều kiện xử lý (ORDERED). Vui lòng xác nhận phiếu chỉ định (UC39) trước khi tiếp nhận bệnh nhân.",
                "IN_PROGRESS" => $"Phiếu chỉ định cận lâm sàng '{orderCode}' đang trong quá trình thực hiện kỹ thuật (IN_PROGRESS), không thể tiếp nhận lại.",
                "COMPLETED" => $"Phiếu chỉ định cận lâm sàng '{orderCode}' đã hoàn tất kết quả (COMPLETED), không thể tiếp nhận lại.",
                "CANCELLED" => $"Phiếu chỉ định cận lâm sàng '{orderCode}' đã bị hủy, không thể tiếp nhận bệnh nhân.",
                _ => $"Phiếu chỉ định cận lâm sàng '{orderCode}' đang ở trạng thái '{effectiveOrderStatus}', không hợp lệ để tiếp nhận bệnh nhân.",
            };
        }
        else if (!isDiagQueue)
        {
            disabledReason = "Lượt xếp hàng liên kết với phiếu chỉ định không thuộc loại hàng đợi cận lâm sàng (DIAGNOSTIC).";
        }
        else if (!isQueueWaitingForReception)
        {
            disabledReason = normalizedQueueStatus is "READY" or "CALLED"
                ? $"Bệnh nhân của phiếu chỉ định '{orderCode}' đã được tiếp nhận và đang sẵn sàng trong hàng đợi phòng kỹ thuật ({normalizedQueueStatus})."
                : $"Lượt xếp hàng cận lâm sàng của phiếu chỉ định '{orderCode}' đang ở trạng thái '{normalizedQueueStatus}', không thể tiếp nhận bệnh nhân.";
        }
        else if (visit is null)
        {
            disabledReason = "Phiếu chỉ định cận lâm sàng không gắn với lượt khám hợp lệ.";
        }
        else if (visitStatus is "CANCELLED" or "NO_SHOW")
        {
            disabledReason = "Không thể tiếp nhận bệnh nhân cho phiếu chỉ định cận lâm sàng của lượt khám đã hủy hoặc vắng mặt (NO_SHOW).";
        }
        else if (visitStatus is "COMPLETED" or "DONE")
        {
            disabledReason = "Lượt khám đã hoàn tất (COMPLETED), không thể tiếp nhận bệnh nhân cận lâm sàng.";
        }
        else if (!isPatientActive)
        {
            disabledReason = "Hồ sơ bệnh nhân hiện không ở trạng thái hoạt động.";
        }
        else if (service is null)
        {
            disabledReason = $"Không tìm thấy dịch vụ cận lâm sàng (ID: {order.ServiceId ?? 0}).";
        }
        else if (!string.Equals(service.ServiceType?.Trim(), "DIAGNOSTIC", StringComparison.OrdinalIgnoreCase))
        {
            disabledReason = $"Dịch vụ '{service.Name}' ({service.ServiceCode}) không phải là dịch vụ cận lâm sàng (DIAGNOSTIC).";
        }
        else if (service.IsActive == false)
        {
            disabledReason = $"Dịch vụ cận lâm sàng '{service.Name}' ({service.ServiceCode}) hiện đang ngừng hoạt động.";
        }
        else if (!isDepartmentActive)
        {
            disabledReason = $"Khoa thực hiện dịch vụ '{service.Name}' hiện đang ngừng hoạt động.";
        }
        else if (!isServicePointActive)
        {
            disabledReason = $"Điểm phục vụ cận lâm sàng cho dịch vụ '{service.Name}' hiện đang ngừng hoạt động.";
        }
        else if (!isRoomActive)
        {
            disabledReason = $"Phòng thực hiện cận lâm sàng '{room?.RoomCode}' cho dịch vụ '{service.Name}' hiện đang bảo trì hoặc ngừng hoạt động.";
        }
        else if (!hasQueueManagePermission)
        {
            disabledReason = "Nhân viên cận lâm sàng không có quyền tiếp nhận bệnh nhân vào hàng đợi phòng kỹ thuật (yêu cầu quyền QUEUE_MANAGE).";
        }

        var canReceive = disabledReason is null;

        return new DiagnosticPatientReceiveEligibilityDto(
            IsEligible: canReceive,
            CanReceive: canReceive,
            DisabledReason: disabledReason,
            IsOrderConfirmed: isOrderConfirmed,
            IsQueueWaitingForReception: isQueueWaitingForReception,
            IsVisitEligible: isVisitEligible,
            IsPatientActive: isPatientActive,
            IsServiceActive: isServiceActive,
            IsDepartmentActive: isDepartmentActive,
            IsServicePointActive: isServicePointActive,
            IsRoomActive: isRoomActive,
            HasQueueManagePermission: hasQueueManagePermission);
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

    private async Task<DiagnosticStaffAccessContext> EnsureDiagnosticStaffAccessAsync(
        long actorUserId,
        DateTime nowUtc,
        bool requireQueueManagePermission,
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

        var hasAnyDiagnosticPermission = actorPermissionCodes.Any(code =>
            AllowedReadPermissions.Any(allowed => string.Equals(code, allowed, StringComparison.OrdinalIgnoreCase)));

        if (!hasAnyDiagnosticPermission)
        {
            throw new ForbiddenException("Nhân viên cận lâm sàng không có quyền truy cập danh sách tiếp nhận bệnh nhân cận lâm sàng.");
        }

        var hasQueueManagePermission = actorPermissionCodes.Any(code =>
            string.Equals(code, QueueManagePermissionCode, StringComparison.OrdinalIgnoreCase));

        if (requireQueueManagePermission && !hasQueueManagePermission)
        {
            throw new ForbiddenException("Nhân viên cận lâm sàng không có quyền tiếp nhận bệnh nhân vào hàng đợi phòng kỹ thuật (yêu cầu quyền QUEUE_MANAGE).");
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
            HasQueueManagePermission: hasQueueManagePermission,
            Profile: staffProfile);
    }

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
            trimmed.Length > 40 ||
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

                return new ParsedOrderIdentifier(OrderId: doId, QueueEntryId: null, VisitId: null, Code: null, RawInput: rawIdOrCode.Trim());
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

                return new ParsedOrderIdentifier(OrderId: null, QueueEntryId: qId, VisitId: null, Code: null, RawInput: rawIdOrCode.Trim());
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

                return new ParsedOrderIdentifier(OrderId: null, QueueEntryId: null, VisitId: vId, Code: null, RawInput: rawIdOrCode.Trim());
            }

            throw new ValidationException("Mã định danh phiếu chỉ định cận lâm sàng không hợp lệ.");
        }

        var isNumericCandidate = trimmed.All(char.IsDigit) ||
            (trimmed.StartsWith('-') && trimmed.Length > 1 && trimmed[1..].All(char.IsDigit));

        if (isNumericCandidate)
        {
            if (!long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedId) ||
                parsedId <= 0)
            {
                throw new ValidationException("ID phiếu chỉ định cận lâm sàng phải lớn hơn 0.");
            }

            return new ParsedOrderIdentifier(OrderId: parsedId, QueueEntryId: null, VisitId: null, Code: null, RawInput: rawIdOrCode.Trim());
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

        return new ParsedOrderIdentifier(
            OrderId: null,
            QueueEntryId: null,
            VisitId: null,
            Code: trimmed.ToUpperInvariant(),
            RawInput: rawIdOrCode.Trim());
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
            query = asTracking
                ? _db.DiagnosticOrders
                : _db.DiagnosticOrders.AsNoTracking();
        }
        catch (NotSupportedException)
        {
            throw new NotFoundException("DiagnosticOrder", identifier.RawInput);
        }

        if (identifier.OrderId.HasValue)
        {
            var byId = await query.FirstOrDefaultAsync(o => o.Id == identifier.OrderId.Value, ct);
            return byId ?? throw new NotFoundException("DiagnosticOrder", identifier.RawInput);
        }

        if (identifier.QueueEntryId.HasValue)
        {
            var byQueueId = await query
                .Where(o => o.QueueEntryId == identifier.QueueEntryId.Value)
                .OrderByDescending(o => o.Id)
                .FirstOrDefaultAsync(ct);

            return byQueueId ?? throw new NotFoundException("DiagnosticOrder", identifier.RawInput);
        }

        if (identifier.VisitId.HasValue)
        {
            return await ResolveBestOrderForVisitAsync(identifier.VisitId.Value, identifier.RawInput, query, ct);
        }

        var code = identifier.Code!;
        var byExactCode = await query
            .FirstOrDefaultAsync(o => o.OrderCode != null && o.OrderCode.ToUpper() == code, ct);
        if (byExactCode is not null)
        {
            return byExactCode;
        }

        if (code.StartsWith("DIA-", StringComparison.OrdinalIgnoreCase) ||
            code.StartsWith("CLS-", StringComparison.OrdinalIgnoreCase))
        {
            var lastDash = code.LastIndexOf('-');
            if (lastDash > 0 &&
                lastDash < code.Length - 1 &&
                long.TryParse(code[(lastDash + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var candidateId) &&
                candidateId > 0)
            {
                var candidate = await query.FirstOrDefaultAsync(o => o.Id == candidateId, ct);
                if (candidate is not null)
                {
                    var orderDate = candidate.OrderedAt.HasValue
                        ? DateOnly.FromDateTime(candidate.OrderedAt.Value)
                        : referenceDate;
                    var formattedByRef = FormatOrderCode(candidate, referenceDate);
                    var formattedByOrderedAt = $"DIA-{orderDate:yyyyMM}-{candidate.Id:D4}";
                    var formattedClsByRef = $"CLS-{referenceDate:yyyy}-{candidate.Id:D4}";
                    var formattedClsByOrderedAt = $"CLS-{orderDate:yyyy}-{candidate.Id:D4}";

                    if (string.Equals(formattedByRef, code, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(formattedByOrderedAt, code, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(formattedClsByRef, code, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(formattedClsByOrderedAt, code, StringComparison.OrdinalIgnoreCase))
                    {
                        return candidate;
                    }
                }
            }
        }

        if (code.StartsWith("VIS-", StringComparison.OrdinalIgnoreCase))
        {
            var resolvedVisit = await ResolveVisitByCodeAsync(code, referenceDate, ct);
            if (resolvedVisit is not null)
            {
                return await ResolveBestOrderForVisitAsync(resolvedVisit.Id, identifier.RawInput, query, ct);
            }
        }

        if (code.Length >= 2 &&
            code[0] == 'C' &&
            code[1..].All(char.IsDigit) &&
            int.TryParse(code[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticketNum) &&
            ticketNum > 0)
        {
            var hospitalDiagServicePointIds = (await TryLoadHospitalDiagnosticServicePointsAsync(hospitalId, ct))
                .Select(sp => sp.Id)
                .ToHashSet();

            var candidateQueueEntries = await _db.QueueEntries
                .AsNoTracking()
                .Where(q =>
                    q.QueueNumber == ticketNum &&
                    q.QueueType != null &&
                    q.QueueType.ToUpper() == "DIAGNOSTIC")
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
                var ticketCandidates = await query
                    .Where(o => o.QueueEntryId.HasValue && matchingQueueIds.Contains(o.QueueEntryId.Value))
                    .ToListAsync(ct);

                var byTicket = ticketCandidates
                    .OrderBy(o => matchingQueueIds.IndexOf(o.QueueEntryId!.Value))
                    .ThenByDescending(o => o.OrderedAt ?? DateTime.MinValue)
                    .ThenByDescending(o => o.Id)
                    .FirstOrDefault();

                if (byTicket is not null)
                {
                    return byTicket;
                }
            }
        }

        throw new NotFoundException("DiagnosticOrder", identifier.RawInput);
    }

    private async Task<Visit?> ResolveVisitByCodeAsync(
        string visitCode,
        DateOnly referenceDate,
        CancellationToken ct)
    {
        var normalized = visitCode.Trim().ToUpperInvariant();
        var byExact = await _db.Visits
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.VisitCode != null && v.VisitCode.ToUpper() == normalized, ct);
        if (byExact is not null)
        {
            return byExact;
        }

        var lastDash = normalized.LastIndexOf('-');
        if (lastDash > 0 &&
            lastDash < normalized.Length - 1 &&
            long.TryParse(normalized[(lastDash + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var candidateVisitId) &&
            candidateVisitId > 0)
        {
            var candidateVisit = await _db.Visits
                .AsNoTracking()
                .FirstOrDefaultAsync(v => v.Id == candidateVisitId, ct);
            if (candidateVisit is not null)
            {
                var formattedByRef = FormatVisitCode(candidateVisit, referenceDate);
                var visitDate = candidateVisit.CheckedInAt.HasValue
                    ? DateOnly.FromDateTime(candidateVisit.CheckedInAt.Value)
                    : referenceDate;
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

    private async Task<DiagnosticOrder> ResolveBestOrderForVisitAsync(
        long visitId,
        string rawInput,
        IQueryable<DiagnosticOrder> query,
        CancellationToken ct)
    {
        var visitOrders = await query
            .Where(o => o.VisitId == visitId)
            .ToListAsync(ct);

        if (visitOrders.Count == 0)
        {
            throw new NotFoundException("DiagnosticOrder", rawInput);
        }

        var queueEntryIds = visitOrders
            .Where(o => o.QueueEntryId.HasValue)
            .Select(o => o.QueueEntryId!.Value)
            .Distinct()
            .ToList();

        var queueEntriesById = queueEntryIds.Count > 0
            ? await _db.QueueEntries
                .AsNoTracking()
                .Where(q => queueEntryIds.Contains(q.Id))
                .ToDictionaryAsync(q => q.Id, ct)
            : new Dictionary<long, QueueEntry>();

        var finalizedOrderIds = await TryLoadFinalizedOrderIdsAsync(visitOrders, ct);

        var waitingConfirmed = visitOrders
            .Where(o =>
            {
                var effStatus = ResolveEffectiveOrderStatus(o, finalizedOrderIds);
                if (effStatus != "CONFIRMED")
                {
                    return false;
                }

                if (!o.QueueEntryId.HasValue || !queueEntriesById.TryGetValue(o.QueueEntryId.Value, out var qe))
                {
                    return true;
                }

                var qStatus = NormalizeQueueStatus(qe.Status);
                return qStatus is "WAITING" or "RESERVED" or "SKIPPED";
            })
            .OrderByDescending(o => GetPriorityRank(o.Priority))
            .ThenBy(o => o.OrderedAt ?? DateTime.MinValue)
            .ThenBy(o => o.Id)
            .FirstOrDefault();

        if (waitingConfirmed is not null)
        {
            return waitingConfirmed;
        }

        return visitOrders
            .OrderByDescending(o => ResolveEffectiveOrderStatus(o, finalizedOrderIds) == "CONFIRMED" ? 1 : 0)
            .ThenByDescending(o => o.OrderedAt ?? DateTime.MinValue)
            .ThenByDescending(o => o.Id)
            .First();
    }

    private static ValidatedListFilter ValidateListFilter(GetDiagnosticOrdersForReceiveRequest request)
    {
        if (request.Limit is < 1 or > 100)
        {
            throw new ValidationException("Số lượng phiếu chỉ định cận lâm sàng hiển thị phải từ 1 đến 100.");
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

            normalizedCategory = trimmedCategory.ToUpperInvariant();
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
            normalizedStatus = upperStatus is "PENDING" or "PENDING_CONFIRMATION"
                ? "ORDERED"
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
            normalizedPriority = upperPriority is "STANDARD" or "ROUTINE"
                ? "NORMAL"
                : upperPriority;
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
            if (normalizedVisitCode.Length > 40 ||
                normalizedVisitCode.Contains('<') ||
                normalizedVisitCode.Contains('>'))
            {
                throw new ValidationException("Mã lượt khám lọc không hợp lệ.");
            }
        }

        string? normalizedKeyword = null;
        if (!string.IsNullOrWhiteSpace(request.Keyword))
        {
            normalizedKeyword = request.Keyword.Trim();
            if (normalizedKeyword.Length > 100 ||
                normalizedKeyword.Contains('<') ||
                normalizedKeyword.Contains('>'))
            {
                throw new ValidationException("Từ khóa tìm kiếm phiếu chỉ định cận lâm sàng không hợp lệ.");
            }
        }

        return new ValidatedListFilter(
            Status: normalizedStatus,
            QueueStatus: normalizedQueueStatus,
            Category: normalizedCategory,
            Priority: normalizedPriority,
            VisitId: request.VisitId,
            VisitCode: normalizedVisitCode,
            ServiceId: request.ServiceId,
            RoomId: request.RoomId,
            ServicePointId: request.ServicePointId,
            ReadyToReceiveOnly: request.ReadyToReceiveOnly,
            Keyword: normalizedKeyword,
            Limit: request.Limit);
    }

    private static ValidatedReceiveDiagnosticPatientInput ValidateReceiveRequest(ReceiveDiagnosticPatientRequest request)
    {
        if (request.IsPatientPresent == false || request.PatientPresent == false)
        {
            throw new ValidationException("Nhân viên cận lâm sàng cần xác nhận bệnh nhân đã có mặt tại điểm dịch vụ (isPatientPresent = true).");
        }

        if (request.IsIdentityVerified == false || request.IdentityVerified == false)
        {
            throw new ValidationException("Nhân viên cận lâm sàng cần xác nhận đã đối chiếu thông tin bệnh nhân (isIdentityVerified = true).");
        }

        if (request.IsPreparationVerified == false)
        {
            throw new ValidationException("Nhân viên cận lâm sàng cần xác nhận bệnh nhân đủ điều kiện chuẩn bị thực hiện kỹ thuật (isPreparationVerified = true).");
        }

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

        string? normalizedPriority = null;
        if (!string.IsNullOrWhiteSpace(request.Priority))
        {
            var trimmedPriority = request.Priority.Trim();
            if (!ValidPriorities.Contains(trimmedPriority))
            {
                throw new ValidationException("Mức độ ưu tiên chỉ định cận lâm sàng không hợp lệ. Chỉ chấp nhận NORMAL, URGENT hoặc STAT.");
            }

            var upperPriority = trimmedPriority.ToUpperInvariant();
            normalizedPriority = upperPriority is "STANDARD" or "ROUTINE"
                ? "NORMAL"
                : upperPriority;
        }

        var normalizedManualPriority = ClinicalQueueRules.NormalizePriorityOrThrow(request.ManualPriority);
        var normalizedClinicalPriority = ClinicalQueueRules.NormalizePriorityOrThrow(request.ClinicalPriority);
        var effectiveManualPriority = normalizedManualPriority ?? normalizedClinicalPriority;

        string? normalizedInstruction = null;
        if (!string.IsNullOrWhiteSpace(request.Instruction))
        {
            normalizedInstruction = request.Instruction.Trim();
            if (normalizedInstruction.Contains('<') || normalizedInstruction.Contains('>'))
            {
                throw new ValidationException("Hướng dẫn thực hiện cận lâm sàng chứa ký tự không hợp lệ.");
            }

            if (normalizedInstruction.Length > 1000)
            {
                throw new ValidationException("Hướng dẫn thực hiện cận lâm sàng không được vượt quá 1000 ký tự.");
            }
        }

        var rawNote = request.ReceptionNote ?? request.ReceiveNote ?? request.Notes;
        string? normalizedNote = null;
        if (!string.IsNullOrWhiteSpace(rawNote))
        {
            normalizedNote = rawNote.Trim();
            if (normalizedNote.Contains('<') || normalizedNote.Contains('>'))
            {
                throw new ValidationException("Ghi chú tiếp nhận bệnh nhân chứa ký tự không hợp lệ.");
            }

            if (normalizedNote.Length > 500)
            {
                throw new ValidationException("Ghi chú tiếp nhận bệnh nhân không được vượt quá 500 ký tự.");
            }
        }

        return new ValidatedReceiveDiagnosticPatientInput(
            ServicePointId: request.ServicePointId,
            RoomId: request.RoomId,
            Priority: normalizedPriority,
            ManualPriority: effectiveManualPriority,
            Instruction: normalizedInstruction,
            ReceptionNote: normalizedNote,
            ExpectedStateVersion: request.ExpectedStateVersion);
    }

    private async Task<IReadOnlyList<ParsedOrderIdentifier>> ResolveBatchTargetIdentifiersAsync(
        ReceiveDiagnosticPatientsBatchRequest request,
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
            if (trimmedVisitCode.Length > 40 ||
                trimmedVisitCode.Contains('<') ||
                trimmedVisitCode.Contains('>'))
            {
                throw new ValidationException("Mã lượt khám không hợp lệ.");
            }
        }

        var rawTargets = new List<ParsedOrderIdentifier>();
        var seenIds = new HashSet<long>();
        var seenQueueIds = new HashSet<long>();
        var seenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddParsed(ParsedOrderIdentifier parsed)
        {
            if (parsed.OrderId.HasValue && !seenIds.Add(parsed.OrderId.Value))
            {
                throw new ValidationException("Danh sách phiếu chỉ định cần tiếp nhận có mã hoặc ID bị trùng lặp.");
            }

            if (parsed.QueueEntryId.HasValue && !seenQueueIds.Add(parsed.QueueEntryId.Value))
            {
                throw new ValidationException("Danh sách phiếu chỉ định cần tiếp nhận có mã hoặc ID bị trùng lặp.");
            }

            if (parsed.Code is not null && !seenCodes.Add(parsed.Code))
            {
                throw new ValidationException("Danh sách phiếu chỉ định cần tiếp nhận có mã hoặc ID bị trùng lặp.");
            }

            rawTargets.Add(parsed);
        }

        if (request.OrderId.HasValue)
        {
            if (request.OrderId.Value <= 0)
            {
                throw new ValidationException("ID phiếu chỉ định cận lâm sàng phải lớn hơn 0.");
            }

            AddParsed(new ParsedOrderIdentifier(
                request.OrderId.Value,
                null,
                null,
                null,
                request.OrderId.Value.ToString(CultureInfo.InvariantCulture)));
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

                AddParsed(new ParsedOrderIdentifier(
                    id,
                    null,
                    null,
                    null,
                    id.ToString(CultureInfo.InvariantCulture)));
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
                var foundVisit = await _db.Visits
                    .AsNoTracking()
                    .FirstOrDefaultAsync(v => v.Id == request.VisitId.Value, ct)
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

            var waitingConfirmedOrders = visitOrders
                .Where(o =>
                {
                    var effStatus = ResolveEffectiveOrderStatus(o, lookupBundle.FinalizedOrderIds);
                    if (effStatus != "CONFIRMED")
                    {
                        return false;
                    }

                    if (!o.QueueEntryId.HasValue || !lookupBundle.QueueEntriesById.TryGetValue(o.QueueEntryId.Value, out var qe))
                    {
                        return true;
                    }

                    var qStatus = NormalizeQueueStatus(qe.Status);
                    return qStatus is "WAITING" or "RESERVED" or "SKIPPED";
                })
                .OrderByDescending(o => GetPriorityRank(o.Priority))
                .ThenBy(o => o.OrderedAt ?? DateTime.MinValue)
                .ThenBy(o => o.Id)
                .ToList();

            if (waitingConfirmedOrders.Count > 0)
            {
                foreach (var wo in waitingConfirmedOrders)
                {
                    AddParsed(new ParsedOrderIdentifier(
                        wo.Id,
                        null,
                        null,
                        null,
                        wo.Id.ToString(CultureInfo.InvariantCulture)));
                }
            }
            else
            {
                var fallbackOrder = visitOrders
                    .OrderByDescending(o => o.OrderedAt ?? DateTime.MinValue)
                    .ThenByDescending(o => o.Id)
                    .First();
                AddParsed(new ParsedOrderIdentifier(
                    fallbackOrder.Id,
                    null,
                    null,
                    null,
                    fallbackOrder.Id.ToString(CultureInfo.InvariantCulture)));
            }
        }

        if (rawTargets.Count == 0)
        {
            throw new ValidationException("Vui lòng chọn ít nhất một phiếu chỉ định cận lâm sàng để tiếp nhận bệnh nhân.");
        }

        if (rawTargets.Count > 20)
        {
            throw new ValidationException("Mỗi lần chỉ được tiếp nhận tối đa 20 phiếu chỉ định cận lâm sàng.");
        }

        return rawTargets;
    }

    private static IReadOnlyList<DiagnosticPatientReceiveItemDto> ApplyListFilters(
        IReadOnlyList<DiagnosticPatientReceiveItemDto> items,
        ValidatedListFilter filter)
    {
        IEnumerable<DiagnosticPatientReceiveItemDto> query = items;

        if (filter.ReadyToReceiveOnly)
        {
            query = query.Where(i => i.CanReceive);
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
                (i.TicketNo is not null && i.TicketNo.Contains(kw, StringComparison.OrdinalIgnoreCase)));
        }

        return query
            .OrderByDescending(i => i.CanReceive ? 2 : (i.QueueStatus is "READY" or "CALLED" ? 1 : 0))
            .ThenByDescending(i => GetPriorityRank(i.Priority))
            .ThenBy(i => i.OrderedAt)
            .ThenByDescending(i => i.OrderId)
            .Take(filter.Limit)
            .ToList();
    }

    private static DiagnosticPatientReceiveSummaryStatsDto BuildSummaryStats(
        IReadOnlyList<DiagnosticPatientReceiveItemDto> items)
    {
        var total = items.Count;
        var pendingConfirmation = 0;
        var waitingReception = 0;
        var readyInQueue = 0;
        var inProgress = 0;
        var completed = 0;
        var cancelled = 0;
        var urgentOrStatWaiting = 0;

        foreach (var item in items)
        {
            switch (item.Status)
            {
                case "ORDERED":
                    pendingConfirmation++;
                    break;
                case "CONFIRMED":
                    if (item.QueueStatus is "READY" or "CALLED")
                    {
                        readyInQueue++;
                    }
                    else if (item.QueueStatus == "IN_PROGRESS")
                    {
                        inProgress++;
                    }
                    else
                    {
                        waitingReception++;
                        if (item.Priority is "URGENT" or "STAT")
                        {
                            urgentOrStatWaiting++;
                        }
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
        }

        return new DiagnosticPatientReceiveSummaryStatsDto(
            TotalOrders: total,
            PendingConfirmationCount: pendingConfirmation,
            WaitingReceptionCount: waitingReception,
            ReadyInQueueCount: readyInQueue,
            InProgressCount: inProgress,
            CompletedCount: completed,
            CancelledCount: cancelled,
            UrgentOrStatWaitingCount: urgentOrStatWaiting);
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
                var room = sp.RoomId.HasValue && roomsById.TryGetValue(sp.RoomId.Value, out var foundRoom)
                    ? foundRoom
                    : null;
                var isRoomActive = room is null ||
                    string.Equals(room.Status?.Trim(), "ACTIVE", StringComparison.OrdinalIgnoreCase);

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
        if (order.ServiceId.HasValue &&
            bundle.ServicesById.TryGetValue(order.ServiceId.Value, out var service) &&
            service.HospitalId.HasValue)
        {
            return service.HospitalId.Value;
        }

        if (order.OrderingDoctorId.HasValue &&
            bundle.DoctorsById.TryGetValue(order.OrderingDoctorId.Value, out var doctor))
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

        if (order.QueueEntryId.HasValue &&
            bundle.QueueEntriesById.TryGetValue(order.QueueEntryId.Value, out var qe))
        {
            var spId = qe.CurrentServicePointId ?? qe.ServicePointId;
            if (spId.HasValue &&
                bundle.ServicePointsById.TryGetValue(spId.Value, out var sp) &&
                sp.HospitalId.HasValue)
            {
                return sp.HospitalId.Value;
            }
        }

        if (order.VisitId.HasValue &&
            bundle.VisitsById.TryGetValue(order.VisitId.Value, out var visit))
        {
            if (visit.ExaminationRoomId.HasValue &&
                bundle.RoomsById.TryGetValue(visit.ExaminationRoomId.Value, out var room) &&
                room.HospitalId.HasValue)
            {
                return room.HospitalId.Value;
            }

            if (visit.AssignedDoctorId.HasValue &&
                bundle.DoctorsById.TryGetValue(visit.AssignedDoctorId.Value, out var visitDoc))
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
            throw new ForbiddenException("Phiếu chỉ định cận lâm sàng thuộc cơ sở y tế khác, nhân viên không có quyền truy cập hoặc tiếp nhận bệnh nhân.");
        }
    }

    private async Task<OrderLookupBundle> LoadOrderLookupBundleAsync(
        IReadOnlyList<DiagnosticOrder> orders,
        long hospitalId,
        DateTime nowLocal,
        CancellationToken ct)
    {
        var hospitalDiagPoints = await TryLoadHospitalDiagnosticServicePointsAsync(hospitalId, ct);

        var serviceIds = orders
            .Where(o => o.ServiceId.HasValue)
            .Select(o => o.ServiceId!.Value)
            .Concat(hospitalDiagPoints.Where(sp => sp.ServiceId.HasValue).Select(sp => sp.ServiceId!.Value))
            .Distinct()
            .ToList();

        var servicesById = serviceIds.Count > 0
            ? await _db.Services
                .AsNoTracking()
                .Where(s => serviceIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, ct)
            : new Dictionary<long, Service>();

        var servicePricesByServiceId = new Dictionary<long, decimal>();
        if (serviceIds.Count > 0)
        {
            try
            {
                var prices = await _db.ServicePrices
                    .AsNoTracking()
                    .Where(p =>
                        p.ServiceId.HasValue &&
                        serviceIds.Contains(p.ServiceId.Value) &&
                        (p.EffectiveFrom == null || p.EffectiveFrom <= nowLocal) &&
                        (p.EffectiveTo == null || p.EffectiveTo >= nowLocal))
                    .OrderByDescending(p => p.EffectiveFrom)
                    .ThenByDescending(p => p.Id)
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
                // Ignore if ServicePrices not configured in minimal unit test
            }
        }

        var departmentsById = await _db.Departments
            .AsNoTracking()
            .ToDictionaryAsync(d => d.Id, ct);

        var specialtiesById = await _db.Specialties
            .AsNoTracking()
            .ToDictionaryAsync(s => s.Id, ct);

        var visitIds = orders
            .Where(o => o.VisitId.HasValue)
            .Select(o => o.VisitId!.Value)
            .Distinct()
            .ToList();

        var visitsById = visitIds.Count > 0
            ? await _db.Visits
                .AsNoTracking()
                .Where(v => visitIds.Contains(v.Id))
                .ToDictionaryAsync(v => v.Id, ct)
            : new Dictionary<long, Visit>();

        var patientIds = visitsById.Values
            .Where(v => v.PatientId.HasValue)
            .Select(v => v.PatientId!.Value)
            .Distinct()
            .ToList();

        var patientsById = patientIds.Count > 0
            ? await _db.Patients
                .AsNoTracking()
                .Where(p => patientIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, ct)
            : new Dictionary<long, Patient>();

        var doctorIds = orders
            .Where(o => o.OrderingDoctorId.HasValue)
            .Select(o => o.OrderingDoctorId!.Value)
            .Concat(visitsById.Values.Where(v => v.AssignedDoctorId.HasValue).Select(v => v.AssignedDoctorId!.Value))
            .Distinct()
            .ToList();

        var doctorsById = doctorIds.Count > 0
            ? await _db.Doctors
                .AsNoTracking()
                .Include(d => d.Staff)
                .Where(d => doctorIds.Contains(d.Id))
                .ToDictionaryAsync(d => d.Id, ct)
            : new Dictionary<long, Doctor>();

        var queueEntryIds = orders
            .Where(o => o.QueueEntryId.HasValue)
            .Select(o => o.QueueEntryId!.Value)
            .Distinct()
            .ToList();

        var queueEntriesById = queueEntryIds.Count > 0
            ? await _db.QueueEntries
                .AsNoTracking()
                .Where(q => queueEntryIds.Contains(q.Id))
                .ToDictionaryAsync(q => q.Id, ct)
            : new Dictionary<long, QueueEntry>();

        var referencedSpIds = queueEntriesById.Values
            .SelectMany(q => new[] { q.CurrentServicePointId, q.ServicePointId })
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Concat(hospitalDiagPoints.Select(sp => sp.Id))
            .Distinct()
            .ToList();

        var servicePointsById = new Dictionary<long, QueueServicePoint>();
        foreach (var sp in hospitalDiagPoints)
        {
            servicePointsById[sp.Id] = sp;
        }

        if (referencedSpIds.Count > 0)
        {
            try
            {
                var loadedPoints = await _db.QueueServicePoints
                    .AsNoTracking()
                    .Where(sp => referencedSpIds.Contains(sp.Id))
                    .ToListAsync(ct);

                foreach (var sp in loadedPoints)
                {
                    servicePointsById[sp.Id] = sp;
                }
            }
            catch (NotSupportedException)
            {
                // Ignore if QueueServicePoints not configured
            }
        }

        var roomIds = servicePointsById.Values
            .Where(sp => sp.RoomId.HasValue)
            .Select(sp => sp.RoomId!.Value)
            .Concat(visitsById.Values.Where(v => v.ExaminationRoomId.HasValue).Select(v => v.ExaminationRoomId!.Value))
            .Distinct()
            .ToList();

        var roomsById = await _db.HospitalRooms
            .AsNoTracking()
            .Where(r => r.HospitalId == hospitalId || roomIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, ct);

        var orderIdStrings = orders
            .Select(o => o.Id.ToString(CultureInfo.InvariantCulture))
            .Distinct()
            .ToList();
        var auditMetadataByOrderId = await TryLoadDiagnosticOrderAuditMetadataAsync(orderIdStrings, ct);

        var staffIds = orders
            .Where(o => o.ConfirmedByStaffId.HasValue)
            .Select(o => o.ConfirmedByStaffId!.Value)
            .Concat(auditMetadataByOrderId.Values
                .Where(m => m.ReceivedByStaffId.HasValue)
                .Select(m => m.ReceivedByStaffId!.Value))
            .Distinct()
            .ToList();

        var staffById = staffIds.Count > 0
            ? await _db.Staff
                .AsNoTracking()
                .Where(s => staffIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, ct)
            : new Dictionary<long, Staff>();

        var finalizedOrderIds = await TryLoadFinalizedOrderIdsAsync(orders, ct);
        var latestRevisionByVisitId = await TryLoadLatestRevisionsByVisitIdAsync(visitIds, ct);

        var sequenceByOrderId = new Dictionary<long, int>();
        foreach (var group in orders
                     .Where(o => o.VisitId.HasValue && AppointmentBookingRules.NormalizeStatusKey(o.Status) != "CANCELLED")
                     .GroupBy(o => o.VisitId!.Value))
        {
            var orderedVisitItems = group
                .OrderBy(o => o.OrderedAt ?? DateTime.MinValue)
                .ThenBy(o => o.Id)
                .ToList();

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
            return await _db.DiagnosticOrders
                .AsNoTracking()
                .OrderByDescending(o => o.OrderedAt)
                .ThenByDescending(o => o.Id)
                .ToListAsync(ct);
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
            return await _db.DiagnosticOrders
                .AsNoTracking()
                .Where(o => o.VisitId == visitId)
                .OrderBy(o => o.OrderedAt)
                .ThenBy(o => o.Id)
                .ToListAsync(ct);
        }
        catch (NotSupportedException)
        {
            return [];
        }
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

    private async Task<HashSet<long>> TryLoadFinalizedOrderIdsAsync(
        IReadOnlyList<DiagnosticOrder> orders,
        CancellationToken ct)
    {
        var finalizedOrderIds = new HashSet<long>();
        if (orders.Count == 0)
        {
            return finalizedOrderIds;
        }

        var orderIds = orders.Select(o => o.Id).Distinct().ToList();
        try
        {
            var results = await _db.DiagnosticResults
                .AsNoTracking()
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
            // Ignore when DiagnosticResults DbSet not configured
        }

        return finalizedOrderIds;
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
            var records = await _db.MedicalRecords
                .AsNoTracking()
                .Where(m => m.VisitId.HasValue && visitIds.Contains(m.VisitId.Value))
                .OrderByDescending(m => m.Id)
                .ToListAsync(ct);

            if (records.Count == 0)
            {
                return result;
            }

            var recordIds = records.Select(m => m.Id).Distinct().ToList();
            var revisions = await _db.MedicalRecordRevisions
                .AsNoTracking()
                .Where(r => r.MedicalRecordId.HasValue && recordIds.Contains(r.MedicalRecordId.Value))
                .OrderByDescending(r => r.RevisionNo)
                .ThenByDescending(r => r.Id)
                .ToListAsync(ct);

            var latestRevByRecordId = revisions
                .Where(r => r.MedicalRecordId.HasValue)
                .GroupBy(r => r.MedicalRecordId!.Value)
                .ToDictionary(g => g.Key, g => g.First());

            foreach (var record in records)
            {
                if (record.VisitId.HasValue &&
                    !result.ContainsKey(record.VisitId.Value) &&
                    latestRevByRecordId.TryGetValue(record.Id, out var rev))
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
            var logs = await _db.AuditLogs
                .AsNoTracking()
                .Where(a =>
                    a.EntityType == DiagnosticOrderEntityType &&
                    a.EntityId != null &&
                    orderIdStrings.Contains(a.EntityId) &&
                    (a.Action == "ADDITIONAL_DIAGNOSTIC_ORDER_CREATED" ||
                     a.Action == "DIAGNOSTIC_ORDER_CREATED" ||
                     a.Action == DiagnosticOrderConfirmedAction ||
                     a.Action == DiagnosticPatientReceivedAction))
                .OrderByDescending(a => a.OccurredAt)
                .ThenByDescending(a => a.Id)
                .ToListAsync(ct);

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
                            if (root.TryGetProperty("isAdditionalOrder", out var addProp) &&
                                addProp.ValueKind is JsonValueKind.True or JsonValueKind.False)
                            {
                                isAdditional = isAdditional || addProp.GetBoolean();
                            }

                            if (!seqNo.HasValue &&
                                root.TryGetProperty("orderSequenceNo", out var seqProp) &&
                                seqProp.ValueKind == JsonValueKind.Number &&
                                seqProp.TryGetInt32(out var parsedSeq))
                            {
                                seqNo = parsedSeq;
                            }

                            if (!followUpId.HasValue &&
                                root.TryGetProperty("followUpToOrderId", out var fIdProp) &&
                                fIdProp.ValueKind == JsonValueKind.Number &&
                                fIdProp.TryGetInt64(out var parsedFollowUpId) &&
                                parsedFollowUpId > 0)
                            {
                                followUpId = parsedFollowUpId;
                            }

                            if (followUpCode is null &&
                                root.TryGetProperty("followUpToOrderCode", out var fCodeProp) &&
                                fCodeProp.ValueKind == JsonValueKind.String)
                            {
                                var rawCode = fCodeProp.GetString();
                                if (!string.IsNullOrWhiteSpace(rawCode))
                                {
                                    followUpCode = rawCode.Trim();
                                }
                            }

                            if (additionalReason is null &&
                                root.TryGetProperty("additionalOrderReason", out var reasonProp) &&
                                reasonProp.ValueKind == JsonValueKind.String)
                            {
                                var rawReason = reasonProp.GetString();
                                if (!string.IsNullOrWhiteSpace(rawReason))
                                {
                                    additionalReason = rawReason.Trim();
                                }
                            }

                            if (confirmationNote is null &&
                                root.TryGetProperty("confirmationNote", out var confNoteProp) &&
                                confNoteProp.ValueKind == JsonValueKind.String)
                            {
                                var rawNote = confNoteProp.GetString();
                                if (!string.IsNullOrWhiteSpace(rawNote))
                                {
                                    confirmationNote = rawNote.Trim();
                                }
                            }

                            if (!receivedAt.HasValue &&
                                root.TryGetProperty("receivedAt", out var recAtProp) &&
                                recAtProp.ValueKind == JsonValueKind.String &&
                                recAtProp.TryGetDateTime(out var parsedRecAt))
                            {
                                receivedAt = parsedRecAt;
                            }

                            if (!receivedByStaffId.HasValue &&
                                root.TryGetProperty("receivedByStaffId", out var recStaffIdProp) &&
                                recStaffIdProp.ValueKind == JsonValueKind.Number &&
                                recStaffIdProp.TryGetInt64(out var parsedRecStaffId) &&
                                parsedRecStaffId > 0)
                            {
                                receivedByStaffId = parsedRecStaffId;
                            }

                            if (receivedByStaffCode is null &&
                                root.TryGetProperty("receivedByStaffCode", out var recStaffCodeProp) &&
                                recStaffCodeProp.ValueKind == JsonValueKind.String)
                            {
                                var rawStaffCode = recStaffCodeProp.GetString();
                                if (!string.IsNullOrWhiteSpace(rawStaffCode))
                                {
                                    receivedByStaffCode = rawStaffCode.Trim();
                                }
                            }

                            if (receivedByStaffName is null &&
                                root.TryGetProperty("receivedByStaffName", out var recStaffNameProp) &&
                                recStaffNameProp.ValueKind == JsonValueKind.String)
                            {
                                var rawStaffName = recStaffNameProp.GetString();
                                if (!string.IsNullOrWhiteSpace(rawStaffName))
                                {
                                    receivedByStaffName = rawStaffName.Trim();
                                }
                            }

                            if (receptionNote is null &&
                                root.TryGetProperty("receptionNote", out var recNoteProp) &&
                                recNoteProp.ValueKind == JsonValueKind.String)
                            {
                                var rawRecNote = recNoteProp.GetString();
                                if (!string.IsNullOrWhiteSpace(rawRecNote))
                                {
                                    receptionNote = rawRecNote.Trim();
                                }
                            }
                        }
                    }
                    catch (JsonException)
                    {
                        // Ignore malformed JSON in historical audit logs
                    }
                }

                if (string.Equals(log.Action, DiagnosticPatientReceivedAction, StringComparison.OrdinalIgnoreCase) &&
                    !receivedAt.HasValue &&
                    log.OccurredAt != default)
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
                    ReceptionNote: receptionNote);
            }
        }
        catch (NotSupportedException)
        {
            return result;
        }

        return result;
    }

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

    private static QueueServicePoint? ResolveDiagnosticServicePointForCatalog(
        long serviceId,
        IReadOnlyList<QueueServicePoint> hospitalDiagPoints)
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

    private static string ResolveEffectiveOrderStatus(
        DiagnosticOrder order,
        IReadOnlySet<long> finalizedOrderIds)
    {
        var raw = NormalizeOrderStatus(order.Status);
        if (raw == "CANCELLED")
        {
            return "CANCELLED";
        }

        if (finalizedOrderIds.Contains(order.Id) ||
            raw is "COMPLETED" or "DONE" or "FINALIZED" or "VERIFIED")
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
            "STAT" => "STAT",
            _ => "NORMAL",
        };
    }

    private static int GetPriorityRank(string? priority)
        => NormalizePriority(priority) switch
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

    private sealed record DiagnosticStaffAccessContext(
        Hospital Hospital,
        Staff Staff,
        string DepartmentName,
        bool HasQueueManagePermission,
        DiagnosticStaffProfileDto Profile);

    private sealed record ParsedOrderIdentifier(
        long? OrderId,
        long? QueueEntryId,
        long? VisitId,
        string? Code,
        string RawInput);

    private sealed record ValidatedListFilter(
        string? Status,
        string? QueueStatus,
        string? Category,
        string? Priority,
        long? VisitId,
        string? VisitCode,
        long? ServiceId,
        long? RoomId,
        long? ServicePointId,
        bool ReadyToReceiveOnly,
        string? Keyword,
        int Limit);

    private sealed record ValidatedReceiveDiagnosticPatientInput(
        long? ServicePointId,
        long? RoomId,
        string? Priority,
        string? ManualPriority,
        string? Instruction,
        string? ReceptionNote,
        int? ExpectedStateVersion);

    private sealed record ResolvedPatientReceptionPlan(
        DiagnosticOrder Order,
        Visit Visit,
        Patient? Patient,
        Service Service,
        string Category,
        QueueServicePoint? ServicePoint,
        HospitalRoom? Room,
        QueueEntry? LinkedQueueEntry);

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
        string? ReceptionNote);

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
