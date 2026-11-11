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

public sealed class DiagnosticOrderReviewService : IDiagnosticOrderReviewService
{
    public const string DiagnosticOrderConfirmedAction = "DIAGNOSTIC_ORDER_CONFIRMED";
    public const string DiagnosticOrderEntityType = "DIAGNOSTIC_ORDER";

    private const string DiagnosticStaffRoleCode = "DIAGNOSTIC_STAFF";
    private const string DefaultDepartmentName = "Khoa Cận lâm sàng";

    private static readonly string[] AllowedConfirmPermissions =
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

    public DiagnosticOrderReviewService(
        IApplicationDbContext dbContext,
        TimeProvider? timeProvider = null)
    {
        _db = dbContext;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<DiagnosticOrderReviewListResponseDto> GetDiagnosticOrdersAsync(
        long actorUserId,
        GetDiagnosticOrdersForReviewRequest? request = null,
        CancellationToken ct = default)
    {
        var effectiveRequest = request ?? new GetDiagnosticOrdersForReviewRequest();
        var filter = ValidateListFilter(effectiveRequest);

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var nowLocal = AppointmentBookingRules.GetVietnamNow(_timeProvider);
        var referenceDate = DateOnly.FromDateTime(nowLocal);

        var staffAccess = await EnsureDiagnosticStaffAccessAsync(actorUserId, nowUtc, ct);
        var hospitalId = staffAccess.Hospital.Id;

        var allOrders = await TryLoadAllDiagnosticOrdersAsync(ct);
        var lookupBundle = await LoadOrderLookupBundleAsync(allOrders, hospitalId, nowLocal, ct);

        var hospitalOrders = allOrders
            .Where(o => IsOrderInHospitalScope(o, hospitalId, lookupBundle))
            .OrderByDescending(o => o.OrderedAt ?? DateTime.MinValue)
            .ThenByDescending(o => o.Id)
            .ToList();

        var allHospitalItems = new List<DiagnosticOrderReviewItemDto>(hospitalOrders.Count);
        foreach (var order in hospitalOrders)
        {
            var item = await BuildOrderReviewItemDtoAsync(
                order,
                lookupBundle,
                referenceDate,
                nowLocal,
                ct);
            allHospitalItems.Add(item);
        }

        var summary = BuildSummaryStats(allHospitalItems);
        var filteredItems = ApplyListFilters(allHospitalItems, filter);
        var availableServicePoints = BuildServicePointOptions(lookupBundle.HospitalDiagnosticServicePoints, lookupBundle.RoomsById);

        return new DiagnosticOrderReviewListResponseDto(
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

    public async Task<DiagnosticOrderReviewDetailResponseDto> GetDiagnosticOrderByIdOrCodeAsync(
        long actorUserId,
        string orderIdOrCode,
        CancellationToken ct = default)
    {
        var parsedId = ParseOrderIdentifier(orderIdOrCode);

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var nowLocal = AppointmentBookingRules.GetVietnamNow(_timeProvider);
        var referenceDate = DateOnly.FromDateTime(nowLocal);

        var staffAccess = await EnsureDiagnosticStaffAccessAsync(actorUserId, nowUtc, ct);
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

        var itemDto = await BuildOrderReviewItemDtoAsync(
            order,
            lookupBundle,
            referenceDate,
            nowLocal,
            ct);

        var visit = order.VisitId.HasValue && lookupBundle.VisitsById.TryGetValue(order.VisitId.Value, out var foundVisit)
            ? foundVisit
            : null;
        var clinicalContext = await BuildClinicalSummaryAsync(visit, itemDto.VisitId, itemDto.VisitCode, itemDto.VisitStatus, lookupBundle, ct);
        var availableServicePoints = BuildServicePointOptions(
            lookupBundle.HospitalDiagnosticServicePoints
                .Where(sp => !sp.ServiceId.HasValue || sp.ServiceId.Value == itemDto.ServiceId)
                .ToList(),
            lookupBundle.RoomsById);

        return new DiagnosticOrderReviewDetailResponseDto(
            HospitalId: staffAccess.Hospital.Id,
            HospitalCode: staffAccess.Hospital.HospitalCode,
            HospitalName: staffAccess.Hospital.Name,
            GeneratedAtUtc: nowUtc,
            StaffProfile: staffAccess.Profile,
            Order: itemDto,
            ClinicalContext: clinicalContext,
            AvailableServicePoints: availableServicePoints);
    }

    public async Task<ConfirmDiagnosticOrderResponseDto> ConfirmDiagnosticOrderAsync(
        long actorUserId,
        string orderIdOrCode,
        ConfirmDiagnosticOrderRequest? request = null,
        string? ipAddress = null,
        CancellationToken ct = default)
    {
        var parsedId = ParseOrderIdentifier(orderIdOrCode);
        var validated = ValidateConfirmRequest(request ?? new ConfirmDiagnosticOrderRequest());

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var nowLocal = AppointmentBookingRules.GetVietnamNow(_timeProvider);
        var referenceDate = DateOnly.FromDateTime(nowLocal);

        var staffAccess = await EnsureDiagnosticStaffAccessAsync(actorUserId, nowUtc, ct);

        return await ConfirmOrdersCoreAsync(
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

    public async Task<ConfirmDiagnosticOrdersBatchResponseInternal> ConfirmDiagnosticOrdersBatchInternalAsync(
        long actorUserId,
        ConfirmDiagnosticOrdersBatchRequest? request,
        string? ipAddress,
        CancellationToken ct)
    {
        if (request is null)
        {
            throw new ValidationException("Dữ liệu xác nhận phiếu chỉ định cận lâm sàng không được để trống.");
        }

        var targets = ValidateBatchTargets(request);
        var validated = ValidateConfirmRequest(new ConfirmDiagnosticOrderRequest(
            IsEligibilityVerified: request.IsEligibilityVerified,
            EligibilityConfirmed: request.EligibilityConfirmed,
            ServicePointId: request.ServicePointId,
            RoomId: request.RoomId,
            Priority: request.Priority,
            Instruction: request.Instruction,
            ConfirmationNote: request.ConfirmationNote,
            ReviewNote: request.ReviewNote,
            Notes: request.Notes,
            PatientPresent: request.PatientPresent,
            ExpectedStateVersion: request.ExpectedStateVersion));

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var nowLocal = AppointmentBookingRules.GetVietnamNow(_timeProvider);
        var referenceDate = DateOnly.FromDateTime(nowLocal);

        var staffAccess = await EnsureDiagnosticStaffAccessAsync(actorUserId, nowUtc, ct);

        var response = await ConfirmOrdersCoreAsync(
            actorUserId,
            staffAccess,
            targets,
            validated,
            ipAddress,
            nowUtc,
            nowLocal,
            referenceDate,
            ct);

        return new ConfirmDiagnosticOrdersBatchResponseInternal(response);
    }

    public async Task<ConfirmDiagnosticOrderResponseDto> ConfirmDiagnosticOrdersBatchAsync(
        long actorUserId,
        ConfirmDiagnosticOrdersBatchRequest? request = null,
        string? ipAddress = null,
        CancellationToken ct = default)
    {
        var wrapped = await ConfirmDiagnosticOrdersBatchInternalAsync(actorUserId, request, ipAddress, ct);
        return wrapped.Response;
    }

    private async Task<ConfirmDiagnosticOrderResponseDto> ConfirmOrdersCoreAsync(
        long actorUserId,
        DiagnosticStaffAccessContext staffAccess,
        IReadOnlyList<ParsedOrderIdentifier> targetIdentifiers,
        ValidatedConfirmDiagnosticOrderInput validated,
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
                throw new ValidationException("Danh sách phiếu chỉ định cần xác nhận có mã hoặc ID bị trùng lặp.");
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

        var executionPlans = new List<ResolvedOrderConfirmationPlan>(targetOrders.Count);
        foreach (var order in targetOrders)
        {
            EnsureOrderHospitalOwnership(order, hospitalId, lookupBundle);
            var plan = await ValidateOrderForConfirmationAsync(
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
                var service = plan.Service;
                var resolvedSp = plan.ServicePoint;
                var resolvedRoom = plan.Room;
                var linkedQueueEntry = plan.LinkedQueueEntry;

                int finalStateVersion;
                int? queueNumber;
                string? ticketNo;
                string queueStatus;
                string presenceStatus;

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
                    presenceStatus = validated.PatientPresent == false ? "NOT_ARRIVED" : "PRESENT";
                    queueStatus = "WAITING";
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
                        Status = queueStatus,
                        ScheduledAt = null,
                        PresenceStatus = presenceStatus,
                        ArrivedAt = nowLocal,
                        ReadyAt = nowLocal,
                        ArrivalClass = "WALK_IN",
                        ManualPriority = "NORMAL",
                        StateVersion = finalStateVersion,
                    };

                    _db.QueueEntries.Add(linkedQueueEntry);
                    await SaveChangesWithConcurrencyGuardAsync(ct);

                    order.QueueEntryId = linkedQueueEntry.Id;
                    lookupBundle.QueueEntriesById[linkedQueueEntry.Id] = linkedQueueEntry;
                    queueNumber = nextQueueNumber;
                    ticketNo = $"C{nextQueueNumber:D3}";
                }
                else
                {
                    if (resolvedSp is not null)
                    {
                        linkedQueueEntry.ServicePointId ??= resolvedSp.Id;
                        linkedQueueEntry.CurrentServicePointId = resolvedSp.Id;
                    }

                    var normalizedCurrentQStatus = NormalizeQueueStatus(linkedQueueEntry.Status);
                    if (normalizedCurrentQStatus == "RESERVED" || string.IsNullOrWhiteSpace(linkedQueueEntry.Status))
                    {
                        linkedQueueEntry.Status = "WAITING";
                    }

                    if (validated.PatientPresent == true)
                    {
                        linkedQueueEntry.PresenceStatus = "PRESENT";
                        linkedQueueEntry.ArrivedAt ??= nowLocal;
                        linkedQueueEntry.ReadyAt ??= nowLocal;
                    }
                    else if (validated.PatientPresent == false && string.IsNullOrWhiteSpace(linkedQueueEntry.PresenceStatus))
                    {
                        linkedQueueEntry.PresenceStatus = "NOT_ARRIVED";
                    }

                    var prevStateVersion = linkedQueueEntry.StateVersion is > 0 ? linkedQueueEntry.StateVersion.Value : 1;
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
                    queueStatus = NormalizeQueueStatus(linkedQueueEntry.Status);
                    presenceStatus = string.IsNullOrWhiteSpace(linkedQueueEntry.PresenceStatus)
                        ? "PRESENT"
                        : linkedQueueEntry.PresenceStatus.Trim().ToUpperInvariant();
                }

                var previousStatus = NormalizeOrderStatus(order.Status);
                var previousPriority = NormalizePriority(order.Priority);
                var effectivePriority = validated.Priority ?? previousPriority;
                var effectiveInstruction = validated.Instruction ?? order.Instruction;
                var orderCode = FormatOrderCode(order, referenceDate);

                order.OrderCode = orderCode;
                order.Status = "CONFIRMED";
                order.ConfirmedByStaffId = staffAccess.Staff.Id;
                order.ConfirmedAt = nowLocal;
                order.Priority = effectivePriority;
                if (validated.Instruction is not null)
                {
                    order.Instruction = validated.Instruction;
                }

                var visitCode = FormatVisitCode(visit, referenceDate);
                var effectiveReason = validated.ConfirmationNote
                    ?? $"Nhân viên cận lâm sàng xác nhận phiếu chỉ định {orderCode} ({service.Name}).";

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
                    previousStatus,
                    newStatus = "CONFIRMED",
                    previousPriority,
                    priority = effectivePriority,
                    instruction = effectiveInstruction,
                    confirmationNote = validated.ConfirmationNote,
                    isEligibilityVerified = true,
                    confirmedByStaffId = staffAccess.Staff.Id,
                    confirmedByStaffCode = staffAccess.Staff.StaffCode,
                    confirmedByStaffName = staffAccess.Profile.FullName,
                    confirmedAt = nowLocal,
                    queueEntryId = linkedQueueEntry.Id,
                    ticketNo,
                    queueNumber,
                    queueStatus,
                    presenceStatus,
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
                    Action = DiagnosticOrderConfirmedAction,
                    EntityType = DiagnosticOrderEntityType,
                    EntityId = order.Id.ToString(CultureInfo.InvariantCulture),
                    Reason = effectiveReason,
                    ChangeData = JsonSerializer.Serialize(changePayload, AuditJsonOptions),
                    IpAddress = QueueEntryRequestParsing.ParseIpAddress(ipAddress),
                    OccurredAt = nowUtc,
                });

                lookupBundle.StaffById[staffAccess.Staff.Id] = staffAccess.Staff;
                lookupBundle.AuditMetadataByOrderId[order.Id] = lookupBundle.AuditMetadataByOrderId.TryGetValue(order.Id, out var existingMeta)
                    ? existingMeta with { ConfirmationNote = validated.ConfirmationNote ?? existingMeta.ConfirmationNote }
                    : new DiagnosticOrderAuditMetadata(
                        IsAdditionalOrder: false,
                        OrderSequenceNo: null,
                        FollowUpToOrderId: null,
                        FollowUpToOrderCode: null,
                        AdditionalOrderReason: null,
                        ConfirmationNote: validated.ConfirmationNote);
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

        var confirmedDtos = new List<DiagnosticOrderReviewItemDto>(executionPlans.Count);
        foreach (var plan in executionPlans)
        {
            var dto = await BuildOrderReviewItemDtoAsync(
                plan.Order,
                lookupBundle,
                referenceDate,
                nowLocal,
                ct);
            confirmedDtos.Add(dto);
        }

        return new ConfirmDiagnosticOrderResponseDto(
            ActionOutcome: DiagnosticOrderConfirmedAction,
            ConfirmedCount: confirmedDtos.Count,
            ConfirmedAt: nowLocal,
            ConfirmedByStaff: staffAccess.Profile,
            Order: confirmedDtos[0],
            ConfirmedOrders: confirmedDtos);
    }

    private async Task<ResolvedOrderConfirmationPlan> ValidateOrderForConfirmationAsync(
        DiagnosticOrder order,
        ValidatedConfirmDiagnosticOrderInput validated,
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
            throw new ConflictException($"Phiếu chỉ định cận lâm sàng '{orderCode}' đã bị hủy, không thể xác nhận.");
        }

        if (orderStatus is "COMPLETED" or "DONE" or "FINALIZED" or "VERIFIED")
        {
            throw new ConflictException($"Phiếu chỉ định cận lâm sàng '{orderCode}' đã hoàn tất kết quả (COMPLETED), không thể xác nhận lại.");
        }

        if (orderStatus == "IN_PROGRESS")
        {
            throw new ConflictException($"Phiếu chỉ định cận lâm sàng '{orderCode}' đang trong quá trình thực hiện kỹ thuật (IN_PROGRESS), không thể xác nhận lại.");
        }

        if (orderStatus == "CONFIRMED")
        {
            throw new ConflictException($"Phiếu chỉ định cận lâm sàng '{orderCode}' đã được xác nhận trước đó.");
        }

        if (orderStatus != "ORDERED")
        {
            throw new ConflictException($"Phiếu chỉ định cận lâm sàng '{orderCode}' đang ở trạng thái '{orderStatus}', không hợp lệ để xác nhận.");
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
            throw new ConflictException("Không thể xác nhận phiếu chỉ định cận lâm sàng của lượt khám đã hủy hoặc vắng mặt (NO_SHOW).");
        }

        if (visitStatus is "COMPLETED" or "DONE")
        {
            throw new ConflictException("Lượt khám đã hoàn tất (COMPLETED), không thể xác nhận phiếu chỉ định cận lâm sàng.");
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

        var orderingDoctor = order.OrderingDoctorId.HasValue &&
                             lookupBundle.DoctorsById.TryGetValue(order.OrderingDoctorId.Value, out var foundDoc)
            ? foundDoc
            : (visit.AssignedDoctorId.HasValue && lookupBundle.DoctorsById.TryGetValue(visit.AssignedDoctorId.Value, out var visitDoc)
                ? visitDoc
                : null);

        if (orderingDoctor is not null && orderingDoctor.IsActive == false)
        {
            throw new ConflictException("Bác sĩ chỉ định hiện đang tạm ngưng hoạt động.");
        }

        if (orderingDoctor is not null)
        {
            var isDoctorAuthorized = await IsDoctorAuthorizedForServiceAsync(orderingDoctor.Id, service.Id, ct);
            if (!isDoctorAuthorized)
            {
                throw new ConflictException($"Bác sĩ chỉ định chưa được phân quyền cho dịch vụ cận lâm sàng '{service.Name}' ({service.ServiceCode}).");
            }
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
            if (queueStatus is "CANCELLED" or "COMPLETED" or "IN_PROGRESS")
            {
                throw new ConflictException($"Lượt xếp hàng cận lâm sàng của phiếu chỉ định đang ở trạng thái '{queueStatus}', không thể xác nhận.");
            }

            var currentStateVersion = linkedQueueEntry.StateVersion is > 0 ? linkedQueueEntry.StateVersion.Value : 1;
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

        return new ResolvedOrderConfirmationPlan(
            Order: order,
            Visit: visit,
            Service: service,
            Category: category,
            ServicePoint: resolvedSp,
            Room: resolvedRoom,
            LinkedQueueEntry: linkedQueueEntry);
    }

    private async Task<DiagnosticOrderReviewItemDto> BuildOrderReviewItemDtoAsync(
        DiagnosticOrder order,
        OrderLookupBundle lookupBundle,
        DateOnly referenceDate,
        DateTime nowLocal,
        CancellationToken ct)
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
                             lookupBundle.StaffById.TryGetValue(order.ConfirmedByStaffId.Value, out var foundStaff)
            ? foundStaff
            : null;

        var queueNumber = queueEntry?.QueueNumber;
        var ticketNo = queueNumber is > 0 ? $"C{queueNumber.Value:D3}" : null;
        var queueStatus = queueEntry is not null ? NormalizeQueueStatus(queueEntry.Status) : null;
        var presenceStatus = queueEntry is not null
            ? (string.IsNullOrWhiteSpace(queueEntry.PresenceStatus) ? "PRESENT" : queueEntry.PresenceStatus.Trim().ToUpperInvariant())
            : null;
        var stateVersion = queueEntry?.StateVersion is > 0 ? queueEntry.StateVersion.Value : 1;

        lookupBundle.AuditMetadataByOrderId.TryGetValue(order.Id, out var auditMeta);
        var sequenceNo = auditMeta?.OrderSequenceNo
            ?? (lookupBundle.SequenceByOrderId.TryGetValue(order.Id, out var computedSeq) ? computedSeq : 1);
        var isAdditionalOrder = auditMeta is not null
            ? auditMeta.IsAdditionalOrder
            : sequenceNo > 1;

        var eligibility = await EvaluateOrderEligibilityAsync(
            order,
            orderCode,
            effectiveStatus,
            visit,
            patient,
            service,
            dept,
            orderingDoctor,
            sp,
            room,
            queueEntry,
            ct);

        return new DiagnosticOrderReviewItemDto(
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
            QueueEntryId: order.QueueEntryId,
            FormattedQueueEntryId: order.QueueEntryId.HasValue ? $"q-{order.QueueEntryId.Value:D3}" : null,
            TicketNo: ticketNo,
            QueueNumber: queueNumber,
            QueueDate: queueEntry?.QueueDate,
            QueueStatus: queueStatus,
            PresenceStatus: presenceStatus,
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
            CanConfirm: eligibility.CanConfirm,
            DisabledReason: eligibility.DisabledReason,
            Eligibility: eligibility);
    }

    private async Task<DiagnosticOrderEligibilityCheckDto> EvaluateOrderEligibilityAsync(
        DiagnosticOrder order,
        string orderCode,
        string effectiveOrderStatus,
        Visit? visit,
        Patient? patient,
        Service? service,
        Department? serviceDept,
        Doctor? orderingDoctor,
        QueueServicePoint? servicePoint,
        HospitalRoom? room,
        QueueEntry? queueEntry,
        CancellationToken ct)
    {
        var isOrderPendingConfirmation = effectiveOrderStatus == "ORDERED";

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

        var isDoctorAuthorized = true;
        if (orderingDoctor is not null)
        {
            if (orderingDoctor.IsActive == false)
            {
                isDoctorAuthorized = false;
            }
            else if (service is not null)
            {
                isDoctorAuthorized = await IsDoctorAuthorizedForServiceAsync(orderingDoctor.Id, service.Id, ct);
            }
        }

        var isServicePointActive = servicePoint is null || servicePoint.IsActive != false;
        var isRoomActive = room is null ||
            string.Equals(room.Status?.Trim(), "ACTIVE", StringComparison.OrdinalIgnoreCase);

        var isQueueEntryEligible = true;
        if (queueEntry is not null)
        {
            var isDiagQueue = string.IsNullOrWhiteSpace(queueEntry.QueueType) ||
                string.Equals(queueEntry.QueueType.Trim(), "DIAGNOSTIC", StringComparison.OrdinalIgnoreCase);
            var qStatus = NormalizeQueueStatus(queueEntry.Status);
            isQueueEntryEligible = isDiagQueue && qStatus is not ("CANCELLED" or "COMPLETED" or "IN_PROGRESS");
        }

        string? disabledReason = null;
        if (!isOrderPendingConfirmation)
        {
            disabledReason = effectiveOrderStatus switch
            {
                "CONFIRMED" => $"Phiếu chỉ định cận lâm sàng '{orderCode}' đã được xác nhận trước đó.",
                "IN_PROGRESS" => $"Phiếu chỉ định cận lâm sàng '{orderCode}' đang trong quá trình thực hiện kỹ thuật (IN_PROGRESS), không thể xác nhận lại.",
                "COMPLETED" => $"Phiếu chỉ định cận lâm sàng '{orderCode}' đã hoàn tất kết quả (COMPLETED), không thể xác nhận lại.",
                "CANCELLED" => $"Phiếu chỉ định cận lâm sàng '{orderCode}' đã bị hủy, không thể xác nhận.",
                _ => $"Phiếu chỉ định cận lâm sàng '{orderCode}' đang ở trạng thái '{effectiveOrderStatus}', không hợp lệ để xác nhận.",
            };
        }
        else if (visit is null)
        {
            disabledReason = "Phiếu chỉ định cận lâm sàng không gắn với lượt khám hợp lệ.";
        }
        else if (visitStatus is "CANCELLED" or "NO_SHOW")
        {
            disabledReason = "Không thể xác nhận phiếu chỉ định cận lâm sàng của lượt khám đã hủy hoặc vắng mặt (NO_SHOW).";
        }
        else if (visitStatus is "COMPLETED" or "DONE")
        {
            disabledReason = "Lượt khám đã hoàn tất (COMPLETED), không thể xác nhận phiếu chỉ định cận lâm sàng.";
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
        else if (orderingDoctor?.IsActive == false)
        {
            disabledReason = "Bác sĩ chỉ định hiện đang tạm ngưng hoạt động.";
        }
        else if (!isDoctorAuthorized)
        {
            disabledReason = $"Bác sĩ chỉ định chưa được phân quyền cho dịch vụ cận lâm sàng '{service.Name}' ({service.ServiceCode}).";
        }
        else if (!isServicePointActive)
        {
            disabledReason = $"Điểm phục vụ cận lâm sàng cho dịch vụ '{service.Name}' hiện đang ngừng hoạt động.";
        }
        else if (!isRoomActive)
        {
            disabledReason = $"Phòng thực hiện cận lâm sàng '{room?.RoomCode}' cho dịch vụ '{service.Name}' hiện đang bảo trì hoặc ngừng hoạt động.";
        }
        else if (!isQueueEntryEligible)
        {
            var qStatus = NormalizeQueueStatus(queueEntry?.Status);
            disabledReason = $"Lượt xếp hàng cận lâm sàng của phiếu chỉ định đang ở trạng thái '{qStatus}', không thể xác nhận.";
        }

        var canConfirm = disabledReason is null;

        return new DiagnosticOrderEligibilityCheckDto(
            IsEligible: canConfirm,
            CanConfirm: canConfirm,
            DisabledReason: disabledReason,
            IsOrderPendingConfirmation: isOrderPendingConfirmation,
            IsVisitEligible: isVisitEligible,
            IsPatientActive: isPatientActive,
            IsServiceActive: isServiceActive,
            IsDepartmentActive: isDepartmentActive,
            IsDoctorAuthorizedForService: isDoctorAuthorized,
            IsServicePointActive: isServicePointActive,
            IsRoomActive: isRoomActive,
            IsQueueEntryEligible: isQueueEntryEligible);
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

        var hasRequiredPermission = actorPermissionCodes.Any(code =>
            AllowedConfirmPermissions.Any(allowed => string.Equals(code, allowed, StringComparison.OrdinalIgnoreCase)));

        if (!hasRequiredPermission)
        {
            throw new ForbiddenException("Nhân viên cận lâm sàng không có quyền xác nhận phiếu chỉ định cận lâm sàng.");
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

                return new ParsedOrderIdentifier(OrderId: doId, QueueEntryId: null, Code: null, RawInput: rawIdOrCode.Trim());
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

                return new ParsedOrderIdentifier(OrderId: null, QueueEntryId: qId, Code: null, RawInput: rawIdOrCode.Trim());
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

            return new ParsedOrderIdentifier(OrderId: parsedId, QueueEntryId: null, Code: null, RawInput: rawIdOrCode.Trim());
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

    private static ValidatedListFilter ValidateListFilter(GetDiagnosticOrdersForReviewRequest request)
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
            Category: normalizedCategory,
            Priority: normalizedPriority,
            VisitId: request.VisitId,
            VisitCode: normalizedVisitCode,
            ServiceId: request.ServiceId,
            RoomId: request.RoomId,
            ServicePointId: request.ServicePointId,
            PendingOnly: request.PendingOnly,
            Keyword: normalizedKeyword,
            Limit: request.Limit);
    }

    private static ValidatedConfirmDiagnosticOrderInput ValidateConfirmRequest(ConfirmDiagnosticOrderRequest request)
    {
        if (request.IsEligibilityVerified == false || request.EligibilityConfirmed == false)
        {
            throw new ValidationException("Nhân viên cận lâm sàng cần xác nhận phiếu chỉ định đủ điều kiện xử lý (isEligibilityVerified = true).");
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

        var rawNote = request.ConfirmationNote ?? request.ReviewNote ?? request.Notes;
        string? normalizedNote = null;
        if (!string.IsNullOrWhiteSpace(rawNote))
        {
            normalizedNote = rawNote.Trim();
            if (normalizedNote.Contains('<') || normalizedNote.Contains('>'))
            {
                throw new ValidationException("Ghi chú xác nhận chỉ định chứa ký tự không hợp lệ.");
            }

            if (normalizedNote.Length > 500)
            {
                throw new ValidationException("Ghi chú xác nhận chỉ định không được vượt quá 500 ký tự.");
            }
        }

        return new ValidatedConfirmDiagnosticOrderInput(
            ServicePointId: request.ServicePointId,
            RoomId: request.RoomId,
            Priority: normalizedPriority,
            Instruction: normalizedInstruction,
            ConfirmationNote: normalizedNote,
            PatientPresent: request.PatientPresent,
            ExpectedStateVersion: request.ExpectedStateVersion);
    }

    private static IReadOnlyList<ParsedOrderIdentifier> ValidateBatchTargets(ConfirmDiagnosticOrdersBatchRequest request)
    {
        var rawTargets = new List<ParsedOrderIdentifier>();
        var seenIds = new HashSet<long>();
        var seenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddParsed(ParsedOrderIdentifier parsed)
        {
            if (parsed.OrderId.HasValue && !seenIds.Add(parsed.OrderId.Value))
            {
                throw new ValidationException("Danh sách phiếu chỉ định cần xác nhận có mã hoặc ID bị trùng lặp.");
            }

            if (parsed.Code is not null && !seenCodes.Add(parsed.Code))
            {
                throw new ValidationException("Danh sách phiếu chỉ định cần xác nhận có mã hoặc ID bị trùng lặp.");
            }

            rawTargets.Add(parsed);
        }

        if (request.OrderId.HasValue)
        {
            if (request.OrderId.Value <= 0)
            {
                throw new ValidationException("ID phiếu chỉ định cận lâm sàng phải lớn hơn 0.");
            }

            AddParsed(new ParsedOrderIdentifier(request.OrderId.Value, null, null, request.OrderId.Value.ToString(CultureInfo.InvariantCulture)));
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

                AddParsed(new ParsedOrderIdentifier(id, null, null, id.ToString(CultureInfo.InvariantCulture)));
            }
        }

        if (request.OrderCodes is not null)
        {
            foreach (var code in request.OrderCodes)
            {
                AddParsed(ParseOrderIdentifier(code));
            }
        }

        if (rawTargets.Count == 0)
        {
            throw new ValidationException("Vui lòng chọn ít nhất một phiếu chỉ định cận lâm sàng để xác nhận.");
        }

        if (rawTargets.Count > 20)
        {
            throw new ValidationException("Mỗi lần chỉ được xác nhận tối đa 20 phiếu chỉ định cận lâm sàng.");
        }

        return rawTargets;
    }

    private static IReadOnlyList<DiagnosticOrderReviewItemDto> ApplyListFilters(
        IReadOnlyList<DiagnosticOrderReviewItemDto> items,
        ValidatedListFilter filter)
    {
        IEnumerable<DiagnosticOrderReviewItemDto> query = items;

        if (filter.PendingOnly)
        {
            query = query.Where(i => string.Equals(i.Status, "ORDERED", StringComparison.OrdinalIgnoreCase));
        }

        if (filter.Status is not null)
        {
            query = query.Where(i => string.Equals(i.Status, filter.Status, StringComparison.OrdinalIgnoreCase));
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
            .OrderByDescending(i => string.Equals(i.Status, "ORDERED", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenByDescending(i => GetPriorityRank(i.Priority))
            .ThenBy(i => i.OrderedAt)
            .ThenByDescending(i => i.OrderId)
            .Take(filter.Limit)
            .ToList();
    }

    private static DiagnosticOrderReviewSummaryStatsDto BuildSummaryStats(IReadOnlyList<DiagnosticOrderReviewItemDto> items)
    {
        var total = items.Count;
        var pending = 0;
        var confirmed = 0;
        var inProgress = 0;
        var completed = 0;
        var cancelled = 0;
        var urgentOrStatPending = 0;

        foreach (var item in items)
        {
            switch (item.Status)
            {
                case "ORDERED":
                    pending++;
                    if (item.Priority is "URGENT" or "STAT")
                    {
                        urgentOrStatPending++;
                    }
                    break;
                case "CONFIRMED":
                    confirmed++;
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

        return new DiagnosticOrderReviewSummaryStatsDto(
            TotalOrders: total,
            PendingConfirmationCount: pending,
            ConfirmedCount: confirmed,
            InProgressCount: inProgress,
            CompletedCount: completed,
            CancelledCount: cancelled,
            UrgentOrStatPendingCount: urgentOrStatPending);
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
            throw new ForbiddenException("Phiếu chỉ định cận lâm sàng thuộc cơ sở y tế khác, nhân viên không có quyền truy cập hoặc xác nhận.");
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

        var staffIds = orders
            .Where(o => o.ConfirmedByStaffId.HasValue)
            .Select(o => o.ConfirmedByStaffId!.Value)
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

        var orderIdStrings = orders
            .Select(o => o.Id.ToString(CultureInfo.InvariantCulture))
            .Distinct()
            .ToList();
        var auditMetadataByOrderId = await TryLoadDiagnosticOrderAuditMetadataAsync(orderIdStrings, ct);

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
                     a.Action == DiagnosticOrderConfirmedAction))
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
                        }
                    }
                    catch (JsonException)
                    {
                        // Ignore malformed JSON in historical audit logs
                    }
                }

                result[orderId] = new DiagnosticOrderAuditMetadata(
                    IsAdditionalOrder: isAdditional,
                    OrderSequenceNo: seqNo,
                    FollowUpToOrderId: followUpId,
                    FollowUpToOrderCode: followUpCode,
                    AdditionalOrderReason: additionalReason,
                    ConfirmationNote: confirmationNote);
            }
        }
        catch (NotSupportedException)
        {
            return result;
        }

        return result;
    }

    private async Task<bool> IsDoctorAuthorizedForServiceAsync(
        long doctorId,
        long serviceId,
        CancellationToken ct)
    {
        try
        {
            var anyConfigured = await _db.DoctorServiceAuthorizations
                .AsNoTracking()
                .AnyAsync(a => a.DoctorId == doctorId || a.ServiceId == serviceId, ct);

            if (!anyConfigured)
            {
                return true;
            }

            return await _db.DoctorServiceAuthorizations
                .AsNoTracking()
                .AnyAsync(
                    a => a.DoctorId == doctorId &&
                         a.ServiceId == serviceId &&
                         a.IsActive == true,
                    ct);
        }
        catch (NotSupportedException)
        {
            return true;
        }
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

    public sealed record ConfirmDiagnosticOrdersBatchResponseInternal(
        ConfirmDiagnosticOrderResponseDto Response);

    private sealed record DiagnosticStaffAccessContext(
        Hospital Hospital,
        Staff Staff,
        string DepartmentName,
        DiagnosticStaffProfileDto Profile);

    private sealed record ParsedOrderIdentifier(
        long? OrderId,
        long? QueueEntryId,
        string? Code,
        string RawInput);

    private sealed record ValidatedListFilter(
        string? Status,
        string? Category,
        string? Priority,
        long? VisitId,
        string? VisitCode,
        long? ServiceId,
        long? RoomId,
        long? ServicePointId,
        bool PendingOnly,
        string? Keyword,
        int Limit);

    private sealed record ValidatedConfirmDiagnosticOrderInput(
        long? ServicePointId,
        long? RoomId,
        string? Priority,
        string? Instruction,
        string? ConfirmationNote,
        bool? PatientPresent,
        int? ExpectedStateVersion);

    private sealed record ResolvedOrderConfirmationPlan(
        DiagnosticOrder Order,
        Visit Visit,
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
        string? ConfirmationNote);

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
