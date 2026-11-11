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

public sealed class DiagnosticResultEntryService : IDiagnosticResultEntryService
{
    public const string DiagnosticResultEnteredAction = "DIAGNOSTIC_RESULT_ENTERED";
    public const string DiagnosticResultFinalizedAction = "DIAGNOSTIC_RESULT_FINALIZED";
    public const string DiagnosticResultEntityType = "DIAGNOSTIC_RESULT";
    public const string DiagnosticOrderEntityType = "DIAGNOSTIC_ORDER";
    public const string DiagnosticResultWritePermissionCode = "DIAGNOSTIC_RESULT_WRITE";

    private const string DiagnosticStaffRoleCode = "DIAGNOSTIC_STAFF";
    private const string DefaultDepartmentName = "Khoa Cận lâm sàng";

    private static readonly string[] AllowedReadPermissions =
    [
        "DIAGNOSTIC_RESULT_WRITE",
        "QUEUE_MANAGE",
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

    private static readonly HashSet<string> ValidResultStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "PENDING",
        "DRAFT",
        "PRELIMINARY",
        "PENDING_VERIFY",
        "FINALIZED",
        "VERIFIED",
        "COMPLETED",
        "CANCELLED",
    };

    private static readonly HashSet<string> ValidWriteResultStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "DRAFT",
        "PRELIMINARY",
        "PENDING_VERIFY",
        "FINALIZED",
        "VERIFIED",
        "COMPLETED",
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

    public DiagnosticResultEntryService(
        IApplicationDbContext dbContext,
        TimeProvider? timeProvider = null)
    {
        _db = dbContext;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<DiagnosticResultEntryListResponseDto> GetOrdersForResultEntryAsync(
        long actorUserId,
        GetDiagnosticOrdersForResultEntryRequest? request = null,
        CancellationToken ct = default)
    {
        var effectiveRequest = request ?? new GetDiagnosticOrdersForResultEntryRequest();
        var filter = ValidateListFilter(effectiveRequest);

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var nowLocal = AppointmentBookingRules.GetVietnamNow(_timeProvider);
        var referenceDate = DateOnly.FromDateTime(nowLocal);

        var staffAccess = await EnsureDiagnosticStaffAccessAsync(
            actorUserId,
            nowUtc,
            requireResultWritePermission: false,
            ct);
        var hospitalId = staffAccess.Hospital.Id;

        var allOrders = await TryLoadAllDiagnosticOrdersAsync(ct);
        var lookupBundle = await LoadOrderLookupBundleAsync(allOrders, hospitalId, nowLocal, ct);

        var hospitalOrders = allOrders
            .Where(o => IsOrderInHospitalScope(o, hospitalId, lookupBundle))
            .OrderByDescending(o => o.OrderedAt ?? DateTime.MinValue)
            .ThenByDescending(o => o.Id)
            .ToList();

        var allHospitalItems = new List<DiagnosticResultEntryItemDto>(hospitalOrders.Count);
        foreach (var order in hospitalOrders)
        {
            var item = BuildResultEntryItemDto(
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

        return new DiagnosticResultEntryListResponseDto(
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

    public async Task<DiagnosticResultEntryDetailResponseDto> GetOrderResultByIdOrCodeAsync(
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
            requireResultWritePermission: false,
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

        var itemDto = BuildResultEntryItemDto(
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

        var statusHistory = await BuildResultHistoryAsync(order, itemDto, lookupBundle, ct);
        var availableServicePoints = BuildServicePointOptions(
            lookupBundle.HospitalDiagnosticServicePoints
                .Where(sp => !sp.ServiceId.HasValue || sp.ServiceId.Value == itemDto.ServiceId)
                .ToList(),
            lookupBundle.RoomsById);

        return new DiagnosticResultEntryDetailResponseDto(
            HospitalId: staffAccess.Hospital.Id,
            HospitalCode: staffAccess.Hospital.HospitalCode,
            HospitalName: staffAccess.Hospital.Name,
            GeneratedAtUtc: nowUtc,
            StaffProfile: staffAccess.Profile,
            Order: itemDto,
            ClinicalContext: clinicalContext,
            Versions: itemDto.Versions,
            StatusHistory: statusHistory,
            AvailableServicePoints: availableServicePoints);
    }

    public async Task<EnterDiagnosticResultResponseDto> EnterDiagnosticResultAsync(
        long actorUserId,
        string? orderIdOrCode,
        EnterDiagnosticResultRequest? request = null,
        string? ipAddress = null,
        CancellationToken ct = default)
    {
        if (request is null)
        {
            throw new ValidationException("Dữ liệu nhập kết quả cận lâm sàng không được để trống.");
        }

        var parsedId = ResolveTargetIdentifier(orderIdOrCode, request);
        var validated = ValidateEnterResultRequest(request);

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var nowLocal = AppointmentBookingRules.GetVietnamNow(_timeProvider);
        var referenceDate = DateOnly.FromDateTime(nowLocal);

        var staffAccess = await EnsureDiagnosticStaffAccessAsync(
            actorUserId,
            nowUtc,
            requireResultWritePermission: true,
            ct);
        var hospitalId = staffAccess.Hospital.Id;

        var trackedOrder = await ResolveDiagnosticOrderAsync(
            parsedId,
            hospitalId,
            referenceDate,
            asTracking: true,
            ct);

        var visitOrders = trackedOrder.VisitId.HasValue
            ? await TryLoadVisitDiagnosticOrdersAsync(trackedOrder.VisitId.Value, ct)
            : [trackedOrder];
        if (visitOrders.All(o => o.Id != trackedOrder.Id))
        {
            visitOrders.Add(trackedOrder);
        }

        var lookupBundle = await LoadOrderLookupBundleAsync(visitOrders, hospitalId, nowLocal, ct);
        EnsureOrderHospitalOwnership(trackedOrder, hospitalId, lookupBundle);

        var orderCode = FormatOrderCode(trackedOrder, referenceDate);
        var rawOrderStatus = NormalizeOrderStatus(trackedOrder.Status);

        QueueEntry? trackedQueueEntry = null;
        if (trackedOrder.QueueEntryId.HasValue)
        {
            try
            {
                trackedQueueEntry = await _db.QueueEntries
                    .FirstOrDefaultAsync(q => q.Id == trackedOrder.QueueEntryId.Value, ct);
            }
            catch (NotSupportedException)
            {
                lookupBundle.QueueEntriesById.TryGetValue(trackedOrder.QueueEntryId.Value, out trackedQueueEntry);
            }
        }

        var normalizedQueueStatus = trackedQueueEntry is not null
            ? NormalizeQueueStatus(trackedQueueEntry.Status)
            : null;
        var previousStateVersion = trackedQueueEntry?.StateVersion is > 0
            ? trackedQueueEntry.StateVersion.Value
            : 1;

        if (validated.ExpectedStateVersion.HasValue &&
            validated.ExpectedStateVersion.Value != previousStateVersion)
        {
            throw new ConflictException(
                "Dữ liệu phiếu chỉ định hoặc hàng đợi cận lâm sàng đã thay đổi bởi thao tác khác (state_version mismatch). Vui lòng tải lại dữ liệu.");
        }

        lookupBundle.ResultsByOrderId.TryGetValue(trackedOrder.Id, out var existingResultSnapshot);
        var existingVersions = existingResultSnapshot is not null &&
            lookupBundle.VersionsByResultId.TryGetValue(existingResultSnapshot.Id, out var loadedVersions)
            ? loadedVersions
            : [];
        var latestExistingVersion = existingVersions.Count > 0 ? existingVersions[0] : null;

        var isResultAlreadyFinalized = IsDiagnosticResultRowFinalized(existingResultSnapshot, latestExistingVersion);
        if (isResultAlreadyFinalized)
        {
            throw new ConflictException(
                "Phiếu chỉ định cận lâm sàng đã có kết quả được xác nhận/khóa cuối, không thể nhập mới kết quả.");
        }

        if (rawOrderStatus == "CANCELLED" || normalizedQueueStatus == "CANCELLED")
        {
            throw new ConflictException(
                "Phiếu chỉ định cận lâm sàng đã bị hủy, không thể nhập kết quả.");
        }

        var visit = trackedOrder.VisitId.HasValue &&
                    lookupBundle.VisitsById.TryGetValue(trackedOrder.VisitId.Value, out var foundVisit)
            ? foundVisit
            : null;
        if (visit is null)
        {
            throw new ConflictException("Phiếu chỉ định cận lâm sàng không gắn với lượt khám hợp lệ.");
        }

        var normalizedVisitStatus = AppointmentBookingRules.NormalizeStatusKey(visit.Status);
        if (normalizedVisitStatus is "CANCELLED" or "NO_SHOW")
        {
            throw new ConflictException(
                "Không thể nhập kết quả cận lâm sàng cho lượt khám đã hủy hoặc vắng mặt (NO_SHOW).");
        }

        if (normalizedVisitStatus is "COMPLETED" or "DONE")
        {
            throw new ConflictException(
                "Lượt khám đã hoàn tất (COMPLETED), không thể nhập kết quả cận lâm sàng mới.");
        }

        lookupBundle.MedicalRecordsByVisitId.TryGetValue(visit.Id, out var medicalRecord);
        var medicalRecordStatus = medicalRecord is not null
            ? AppointmentBookingRules.NormalizeStatusKey(medicalRecord.Status)
            : string.Empty;
        if (medicalRecordStatus is "FINALIZED" or "LOCKED" or "CLOSED")
        {
            throw new ConflictException(
                "Hồ sơ bệnh án của lượt khám đã được khóa (FINALIZED), không thể nhập kết quả cận lâm sàng.");
        }

        var patient = visit.PatientId.HasValue &&
                      lookupBundle.PatientsById.TryGetValue(visit.PatientId.Value, out var foundPatient)
            ? foundPatient
            : null;
        if (patient is not null &&
            !string.IsNullOrWhiteSpace(patient.Status) &&
            !string.Equals(patient.Status.Trim(), "ACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException("Hồ sơ bệnh nhân hiện không ở trạng thái hoạt động.");
        }

        var service = trackedOrder.ServiceId.HasValue &&
                      lookupBundle.ServicesById.TryGetValue(trackedOrder.ServiceId.Value, out var foundService)
            ? foundService
            : null;
        if (service is null)
        {
            throw new NotFoundException("Service", trackedOrder.ServiceId ?? 0);
        }

        if (!string.Equals(service.ServiceType?.Trim(), "DIAGNOSTIC", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException(
                $"Dịch vụ '{service.Name}' ({service.ServiceCode}) không phải là dịch vụ cận lâm sàng (DIAGNOSTIC).");
        }

        if (service.IsActive == false)
        {
            throw new ConflictException(
                $"Dịch vụ cận lâm sàng '{service.Name}' ({service.ServiceCode}) hiện đang ngừng hoạt động.");
        }

        var serviceDept = service.DepartmentId.HasValue &&
                          lookupBundle.DepartmentsById.TryGetValue(service.DepartmentId.Value, out var foundDept)
            ? foundDept
            : null;
        if (serviceDept is not null && serviceDept.IsActive == false)
        {
            throw new ConflictException(
                $"Khoa thực hiện dịch vụ '{service.Name}' hiện đang ngừng hoạt động.");
        }

        var spId = trackedQueueEntry?.CurrentServicePointId ?? trackedQueueEntry?.ServicePointId;
        var resolvedSp = spId.HasValue && lookupBundle.ServicePointsById.TryGetValue(spId.Value, out var foundSp)
            ? foundSp
            : ResolveDiagnosticServicePointForCatalog(service.Id, lookupBundle.HospitalDiagnosticServicePoints);
        if (resolvedSp is not null && resolvedSp.IsActive == false)
        {
            throw new ConflictException(
                $"Điểm phục vụ cận lâm sàng '{resolvedSp.Name}' hiện đang ngừng hoạt động.");
        }

        var resolvedRoom = resolvedSp?.RoomId.HasValue == true &&
                           lookupBundle.RoomsById.TryGetValue(resolvedSp.RoomId.Value, out var foundRoom)
            ? foundRoom
            : null;
        if (resolvedRoom is not null &&
            !string.Equals(resolvedRoom.Status?.Trim(), "ACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            throw new ConflictException(
                $"Phòng thực hiện cận lâm sàng '{resolvedRoom.RoomCode}' hiện đang bảo trì hoặc ngừng hoạt động.");
        }

        var isExecutionStartedOrCompleted = IsOrderExecutionStartedOrCompleted(
            rawOrderStatus,
            normalizedQueueStatus,
            trackedQueueEntry,
            existingResultSnapshot);

        if (!isExecutionStartedOrCompleted)
        {
            if (rawOrderStatus == "ORDERED")
            {
                throw new ConflictException(
                    "Phiếu chỉ định cận lâm sàng chưa được xác nhận (ORDERED). Vui lòng xác nhận chỉ định và thực hiện kỹ thuật trước khi nhập kết quả.");
            }

            throw new ConflictException(
                "Dịch vụ cận lâm sàng chưa được bắt đầu hoặc hoàn tất thực hiện kỹ thuật (UC42). Vui lòng chuyển trạng thái thực hiện sang IN_PROGRESS hoặc COMPLETED trước khi nhập kết quả.");
        }

        var requiresResultVerification = service.RequiresResultVerification ?? true;
        var (targetResultStatus, isFinalized, _) = ResolveTargetResultStatus(validated, requiresResultVerification);

        var previousOrderStatus = ResolveEffectiveOrderStatus(
            trackedOrder,
            rawOrderStatus,
            normalizedQueueStatus,
            normalizedVisitStatus,
            isResultAlreadyFinalized,
            existingResultSnapshot is not null);
        var previousQueueStatus = normalizedQueueStatus;

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

        DiagnosticResult trackedResult;
        DiagnosticResultVersion newVersion;
        int nextVersionNo;
        int finalStateVersion = previousStateVersion;

        try
        {
            if (existingResultSnapshot is null)
            {
                trackedResult = new DiagnosticResult
                {
                    DiagnosticOrderId = trackedOrder.Id,
                    Status = targetResultStatus,
                    FinalizedAt = isFinalized ? nowLocal : null,
                    FinalizedByStaffId = isFinalized ? staffAccess.Staff.Id : null,
                };
                _db.DiagnosticResults.Add(trackedResult);
                await SaveChangesWithConcurrencyGuardAsync(ct);
            }
            else
            {
                trackedResult = await _db.DiagnosticResults
                    .FirstOrDefaultAsync(r => r.Id == existingResultSnapshot.Id, ct)
                    ?? existingResultSnapshot;
                trackedResult.Status = targetResultStatus;
                if (isFinalized)
                {
                    trackedResult.FinalizedAt = nowLocal;
                    trackedResult.FinalizedByStaffId = staffAccess.Staff.Id;
                }
            }

            nextVersionNo = (existingVersions.Count > 0 ? existingVersions.Max(v => v.VersionNo ?? 0) : 0) + 1;

            newVersion = new DiagnosticResultVersion
            {
                DiagnosticResultId = trackedResult.Id,
                VersionNo = nextVersionNo,
                ResultText = validated.ResultText,
                Conclusion = validated.Conclusion,
                ReportFileUrl = validated.ReportFileUrl,
                EnteredByStaffId = staffAccess.Staff.Id,
                EnteredAt = nowLocal,
                IsFinalized = isFinalized,
                VerifiedByStaffId = isFinalized ? staffAccess.Staff.Id : null,
                VerifiedAt = isFinalized ? nowLocal : null,
                CorrectionReason = validated.CorrectionReason,
            };
            _db.DiagnosticResultVersions.Add(newVersion);

            trackedOrder.OrderCode = orderCode;
            trackedOrder.ConfirmedByStaffId ??= staffAccess.Staff.Id;
            trackedOrder.ConfirmedAt ??= nowLocal;

            if (isFinalized)
            {
                trackedOrder.Status = "COMPLETED";

                if (trackedQueueEntry is not null)
                {
                    trackedQueueEntry.ArrivedAt ??= nowLocal;
                    trackedQueueEntry.ReadyAt ??= nowLocal;
                    trackedQueueEntry.CalledAt ??= nowLocal;
                    trackedQueueEntry.StartedAt ??= nowLocal;
                    trackedQueueEntry.CompletedAt ??= nowLocal;
                    trackedQueueEntry.Status = "COMPLETED";
                    trackedQueueEntry.PresenceStatus = string.IsNullOrWhiteSpace(trackedQueueEntry.PresenceStatus)
                        ? "PRESENT"
                        : trackedQueueEntry.PresenceStatus;
                    trackedQueueEntry.BlockedReason = null;
                    finalStateVersion = previousStateVersion + 1;
                    trackedQueueEntry.StateVersion = finalStateVersion;
                }
            }
            else
            {
                if (NormalizeOrderStatus(trackedOrder.Status) != "COMPLETED")
                {
                    trackedOrder.Status = "IN_PROGRESS";
                }

                if (trackedQueueEntry is not null)
                {
                    trackedQueueEntry.ArrivedAt ??= nowLocal;
                    trackedQueueEntry.ReadyAt ??= nowLocal;
                    trackedQueueEntry.CalledAt ??= nowLocal;
                    trackedQueueEntry.StartedAt ??= nowLocal;
                    if (NormalizeQueueStatus(trackedQueueEntry.Status) != "COMPLETED")
                    {
                        trackedQueueEntry.Status = "IN_PROGRESS";
                    }
                    trackedQueueEntry.BlockedReason = null;
                    finalStateVersion = previousStateVersion + 1;
                    trackedQueueEntry.StateVersion = finalStateVersion;
                }
            }

            var actionCode = isFinalized
                ? DiagnosticResultFinalizedAction
                : DiagnosticResultEnteredAction;
            var effectiveReason = validated.CorrectionReason
                ?? (isFinalized
                    ? $"Nhập và xác nhận hoàn tất kết quả cận lâm sàng ({targetResultStatus}) cho phiếu {orderCode}."
                    : $"Lưu bản kết quả cận lâm sàng tạm thời ({targetResultStatus}) cho phiếu {orderCode}.");

            var changePayload = new
            {
                orderId = trackedOrder.Id,
                orderCode,
                visitId = visit.Id,
                visitCode = FormatVisitCode(visit, referenceDate),
                resultId = trackedResult.Id,
                versionNo = nextVersionNo,
                resultStatus = targetResultStatus,
                isFinalized,
                resultText = validated.ResultText,
                conclusion = validated.Conclusion,
                reportFileUrl = validated.ReportFileUrl,
                correctionReason = validated.CorrectionReason,
                previousOrderStatus,
                orderStatus = trackedOrder.Status,
                previousQueueStatus,
                queueStatus = trackedQueueEntry?.Status,
                previousStateVersion,
                stateVersion = finalStateVersion,
                enteredByStaffId = staffAccess.Staff.Id,
                enteredByStaffCode = staffAccess.Staff.StaffCode,
                enteredByStaffName = staffAccess.Profile.FullName,
                enteredAt = nowLocal,
                finalizedAt = trackedResult.FinalizedAt,
            };

            _db.AuditLogs.Add(new AuditLog
            {
                ActorUserId = actorUserId,
                HospitalId = staffAccess.Hospital.Id,
                Action = actionCode,
                EntityType = DiagnosticResultEntityType,
                EntityId = trackedResult.Id.ToString(CultureInfo.InvariantCulture),
                Reason = effectiveReason,
                ChangeData = JsonSerializer.Serialize(changePayload, AuditJsonOptions),
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

        lookupBundle.StaffById[staffAccess.Staff.Id] = staffAccess.Staff;
        lookupBundle.ResultsByOrderId[trackedOrder.Id] = trackedResult;
        var updatedVersions = new List<DiagnosticResultVersion>(existingVersions.Count + 1)
        {
            newVersion,
        };
        updatedVersions.AddRange(existingVersions);
        lookupBundle.VersionsByResultId[trackedResult.Id] = updatedVersions;
        if (trackedQueueEntry is not null)
        {
            lookupBundle.QueueEntriesById[trackedQueueEntry.Id] = trackedQueueEntry;
        }

        var updatedItemDto = BuildResultEntryItemDto(
            trackedOrder,
            staffAccess,
            lookupBundle,
            referenceDate,
            nowLocal);

        var latestVersionDto = updatedItemDto.Versions.Count > 0
            ? updatedItemDto.Versions[0]
            : MapVersionItemDto(newVersion, trackedResult.Id, lookupBundle.StaffById);

        var actionOutcome = isFinalized
            ? DiagnosticResultFinalizedAction
            : DiagnosticResultEnteredAction;
        var message = isFinalized
            ? $"Đã nhập và xác nhận hoàn tất kết quả cận lâm sàng cho phiếu '{orderCode}' (phiên bản v{nextVersionNo})."
            : $"Đã lưu kết quả cận lâm sàng ({targetResultStatus}) cho phiếu '{orderCode}' (phiên bản v{nextVersionNo}).";

        return new EnterDiagnosticResultResponseDto(
            ActionOutcome: actionOutcome,
            Message: message,
            OrderId: trackedOrder.Id,
            OrderCode: orderCode,
            ResultId: trackedResult.Id,
            VersionNo: nextVersionNo,
            ResultStatus: updatedItemDto.ResultStatus,
            IsFinalized: isFinalized,
            PreviousOrderStatus: previousOrderStatus,
            OrderStatus: updatedItemDto.Status,
            PreviousQueueStatus: previousQueueStatus,
            QueueStatus: updatedItemDto.QueueStatus,
            PreviousStateVersion: previousStateVersion,
            StateVersion: updatedItemDto.StateVersion,
            EnteredAt: nowLocal,
            FinalizedAt: trackedResult.FinalizedAt,
            EnteredByStaff: staffAccess.Profile,
            Order: updatedItemDto,
            LatestVersion: latestVersionDto,
            Versions: updatedItemDto.Versions);
    }

    private DiagnosticResultEntryItemDto BuildResultEntryItemDto(
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
        lookupBundle.MedicalRecordsByVisitId.TryGetValue(visitId, out var medicalRecord);

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

        var requiresResultVerification = service?.RequiresResultVerification ?? true;
        var orderCode = FormatOrderCode(order, referenceDate);
        var rawOrderStatus = NormalizeOrderStatus(order.Status);
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
        var stateVersion = queueEntry?.StateVersion is > 0 ? queueEntry.StateVersion.Value : 1;

        lookupBundle.ResultsByOrderId.TryGetValue(order.Id, out var diagnosticResult);
        var rawVersions = diagnosticResult is not null &&
                          lookupBundle.VersionsByResultId.TryGetValue(diagnosticResult.Id, out var loadedVersions)
            ? loadedVersions
            : [];
        var latestVersion = rawVersions.Count > 0 ? rawVersions[0] : null;

        var resultState = ResolveResultState(
            rawOrderStatus,
            queueStatus,
            queueEntry,
            diagnosticResult,
            latestVersion,
            requiresResultVerification);

        var effectiveStatus = ResolveEffectiveOrderStatus(
            order,
            rawOrderStatus,
            queueStatus,
            visitStatus,
            resultState.IsResultFinalized,
            resultState.HasResult);

        var finalizedStaffId = diagnosticResult?.FinalizedByStaffId ?? latestVersion?.VerifiedByStaffId;
        var finalizedStaff = finalizedStaffId.HasValue &&
                             lookupBundle.StaffById.TryGetValue(finalizedStaffId.Value, out var foundFinalizedStaff)
            ? foundFinalizedStaff
            : null;

        var enteredStaffId = latestVersion?.EnteredByStaffId;
        var enteredStaff = enteredStaffId.HasValue &&
                           lookupBundle.StaffById.TryGetValue(enteredStaffId.Value, out var foundEnteredStaff)
            ? foundEnteredStaff
            : null;

        var verifiedStaffId = latestVersion?.VerifiedByStaffId ?? (resultState.IsResultFinalized ? diagnosticResult?.FinalizedByStaffId : null);
        var verifiedStaff = verifiedStaffId.HasValue &&
                            lookupBundle.StaffById.TryGetValue(verifiedStaffId.Value, out var foundVerifiedStaff)
            ? foundVerifiedStaff
            : null;

        var versionDtos = rawVersions
            .Select(v => MapVersionItemDto(v, diagnosticResult?.Id ?? 0, lookupBundle.StaffById))
            .ToList();

        lookupBundle.AuditMetadataByOrderId.TryGetValue(order.Id, out var auditMeta);
        var sequenceNo = auditMeta?.OrderSequenceNo
            ?? (lookupBundle.SequenceByOrderId.TryGetValue(order.Id, out var computedSeq) ? computedSeq : 1);
        var isAdditionalOrder = auditMeta is not null
            ? auditMeta.IsAdditionalOrder
            : sequenceNo > 1;

        var eligibility = EvaluateResultEntryEligibility(
            order,
            orderCode,
            rawOrderStatus,
            queueStatus,
            queueEntry,
            diagnosticResult,
            resultState,
            visit,
            medicalRecord,
            patient,
            service,
            dept,
            sp,
            room,
            staffAccess.HasResultWritePermission);

        return new DiagnosticResultEntryItemDto(
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
            RequiresResultVerification: requiresResultVerification,
            Priority: priority,
            Status: effectiveStatus,
            OrderStatus: rawOrderStatus,
            RequiredForConclusion: order.RequiredForConclusion ?? true,
            Instruction: order.Instruction,
            OrderedAt: order.OrderedAt ?? nowLocal,
            ConfirmedAt: order.ConfirmedAt,
            ConfirmedByStaffId: order.ConfirmedByStaffId,
            ConfirmedByStaffCode: confirmedStaff?.StaffCode,
            ConfirmedByStaffName: confirmedStaff?.FullName,
            StartedAt: queueEntry?.StartedAt,
            CompletedAt: queueEntry?.CompletedAt ?? resultState.ResultFinalizedAt,
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
            HasResult: resultState.HasResult,
            IsResultReady: resultState.IsResultReady,
            IsResultFinalized: resultState.IsResultFinalized,
            ResultId: diagnosticResult?.Id,
            ResultStatus: resultState.ResultStatus,
            ResultFinalizedAt: resultState.ResultFinalizedAt,
            FinalizedByStaffId: finalizedStaffId,
            FinalizedByStaffCode: finalizedStaff?.StaffCode,
            FinalizedByStaffName: finalizedStaff?.FullName,
            LatestVersionNo: latestVersion?.VersionNo,
            ResultText: latestVersion?.ResultText,
            Conclusion: latestVersion?.Conclusion,
            ReportFileUrl: latestVersion?.ReportFileUrl,
            EnteredByStaffId: enteredStaffId,
            EnteredByStaffCode: enteredStaff?.StaffCode,
            EnteredByStaffName: enteredStaff?.FullName,
            EnteredAt: latestVersion?.EnteredAt,
            VerifiedByStaffId: verifiedStaffId,
            VerifiedByStaffCode: verifiedStaff?.StaffCode,
            VerifiedByStaffName: verifiedStaff?.FullName,
            VerifiedAt: latestVersion?.VerifiedAt ?? diagnosticResult?.FinalizedAt,
            CorrectionReason: latestVersion?.CorrectionReason,
            IsAdditionalOrder: isAdditionalOrder,
            OrderSequenceNo: sequenceNo,
            FollowUpToOrderId: auditMeta?.FollowUpToOrderId,
            FollowUpToOrderCode: auditMeta?.FollowUpToOrderCode,
            AdditionalOrderReason: auditMeta?.AdditionalOrderReason,
            CanEnterResult: eligibility.CanEnterResult,
            CanFinalizeResult: eligibility.CanFinalizeResult,
            DisabledReason: eligibility.DisabledReason,
            Eligibility: eligibility,
            Versions: versionDtos);
    }

    private static DiagnosticResultVersionItemDto MapVersionItemDto(
        DiagnosticResultVersion version,
        long fallbackResultId,
        IReadOnlyDictionary<long, Staff> staffById)
    {
        var enteredStaff = version.EnteredByStaffId.HasValue &&
                           staffById.TryGetValue(version.EnteredByStaffId.Value, out var foundEntered)
            ? foundEntered
            : null;
        var verifiedStaff = version.VerifiedByStaffId.HasValue &&
                            staffById.TryGetValue(version.VerifiedByStaffId.Value, out var foundVerified)
            ? foundVerified
            : null;

        return new DiagnosticResultVersionItemDto(
            VersionId: version.Id,
            ResultId: version.DiagnosticResultId ?? fallbackResultId,
            VersionNo: version.VersionNo ?? 1,
            ResultText: version.ResultText,
            Conclusion: version.Conclusion,
            ReportFileUrl: version.ReportFileUrl,
            IsFinalized: version.IsFinalized == true || version.VerifiedAt.HasValue,
            EnteredByStaffId: version.EnteredByStaffId,
            EnteredByStaffCode: enteredStaff?.StaffCode,
            EnteredByStaffName: enteredStaff?.FullName,
            EnteredAt: version.EnteredAt,
            VerifiedByStaffId: version.VerifiedByStaffId,
            VerifiedByStaffCode: verifiedStaff?.StaffCode,
            VerifiedByStaffName: verifiedStaff?.FullName,
            VerifiedAt: version.VerifiedAt,
            CorrectionReason: version.CorrectionReason);
    }

    private static DiagnosticResultEntryEligibilityDto EvaluateResultEntryEligibility(
        DiagnosticOrder order,
        string orderCode,
        string rawOrderStatus,
        string? normalizedQueueStatus,
        QueueEntry? queueEntry,
        DiagnosticResult? existingResult,
        ResolvedResultState resultState,
        Visit? visit,
        MedicalRecord? medicalRecord,
        Patient? patient,
        Service? service,
        Department? serviceDept,
        QueueServicePoint? servicePoint,
        HospitalRoom? room,
        bool hasResultWritePermission)
    {
        var isResultAlreadyFinalized = resultState.IsResultFinalized;
        var isExecutionStartedOrCompleted = IsOrderExecutionStartedOrCompleted(
            rawOrderStatus,
            normalizedQueueStatus,
            queueEntry,
            existingResult);

        var visitStatus = visit is not null
            ? AppointmentBookingRules.NormalizeStatusKey(visit.Status)
            : string.Empty;
        var isVisitEligible = visit is not null &&
            visitStatus is not ("CANCELLED" or "NO_SHOW" or "COMPLETED" or "DONE");

        var medicalRecordStatus = medicalRecord is not null
            ? AppointmentBookingRules.NormalizeStatusKey(medicalRecord.Status)
            : string.Empty;
        var isMedicalRecordOpen = medicalRecordStatus is not ("FINALIZED" or "LOCKED" or "CLOSED");

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
        if (isResultAlreadyFinalized)
        {
            disabledReason = $"Phiếu chỉ định cận lâm sàng '{orderCode}' đã có kết quả được xác nhận/khóa cuối, không thể nhập mới kết quả.";
        }
        else if (rawOrderStatus == "CANCELLED" || normalizedQueueStatus == "CANCELLED")
        {
            disabledReason = $"Phiếu chỉ định cận lâm sàng '{orderCode}' đã bị hủy, không thể nhập kết quả.";
        }
        else if (!isExecutionStartedOrCompleted)
        {
            disabledReason = rawOrderStatus == "ORDERED"
                ? $"Phiếu chỉ định cận lâm sàng '{orderCode}' chưa được xác nhận (ORDERED). Vui lòng xác nhận chỉ định và thực hiện kỹ thuật trước khi nhập kết quả."
                : $"Dịch vụ cận lâm sàng '{orderCode}' chưa được bắt đầu hoặc hoàn tất thực hiện kỹ thuật (UC42). Vui lòng chuyển trạng thái thực hiện sang IN_PROGRESS hoặc COMPLETED trước khi nhập kết quả.";
        }
        else if (visit is null)
        {
            disabledReason = "Phiếu chỉ định cận lâm sàng không gắn với lượt khám hợp lệ.";
        }
        else if (visitStatus is "CANCELLED" or "NO_SHOW")
        {
            disabledReason = "Không thể nhập kết quả cận lâm sàng cho lượt khám đã hủy hoặc vắng mặt (NO_SHOW).";
        }
        else if (visitStatus is "COMPLETED" or "DONE")
        {
            disabledReason = "Lượt khám đã hoàn tất (COMPLETED), không thể nhập kết quả cận lâm sàng mới.";
        }
        else if (!isMedicalRecordOpen)
        {
            disabledReason = "Hồ sơ bệnh án của lượt khám đã được khóa (FINALIZED), không thể nhập kết quả cận lâm sàng.";
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
        else if (!hasResultWritePermission)
        {
            disabledReason = "Nhân viên cận lâm sàng không có quyền nhập và trả kết quả cận lâm sàng (yêu cầu quyền DIAGNOSTIC_RESULT_WRITE).";
        }

        var canEnter = disabledReason is null;

        return new DiagnosticResultEntryEligibilityDto(
            IsEligible: canEnter,
            CanEnterResult: canEnter,
            CanFinalizeResult: canEnter,
            DisabledReason: disabledReason,
            IsOrderExecutionStartedOrCompleted: isExecutionStartedOrCompleted,
            IsResultAlreadyFinalized: isResultAlreadyFinalized,
            IsVisitEligible: isVisitEligible,
            IsMedicalRecordOpen: isMedicalRecordOpen,
            IsPatientActive: isPatientActive,
            IsServiceActive: isServiceActive,
            IsDepartmentActive: isDepartmentActive,
            IsServicePointActive: isServicePointActive,
            IsRoomActive: isRoomActive,
            HasResultWritePermission: hasResultWritePermission);
    }

    private static bool IsOrderExecutionStartedOrCompleted(
        string rawOrderStatus,
        string? normalizedQueueStatus,
        QueueEntry? queueEntry,
        DiagnosticResult? existingResult)
    {
        return rawOrderStatus is "IN_PROGRESS" or "COMPLETED" or "DONE" ||
               normalizedQueueStatus is "IN_PROGRESS" or "COMPLETED" ||
               queueEntry?.StartedAt.HasValue == true ||
               queueEntry?.CompletedAt.HasValue == true ||
               existingResult is not null;
    }

    private static bool IsDiagnosticResultRowFinalized(
        DiagnosticResult? result,
        DiagnosticResultVersion? latestVersion)
    {
        if (result is null)
        {
            return false;
        }

        var rawResultStatus = AppointmentBookingRules.NormalizeStatusKey(result.Status);
        return result.FinalizedAt.HasValue ||
               rawResultStatus is "FINALIZED" or "VERIFIED" or "COMPLETED" ||
               latestVersion?.IsFinalized == true ||
               latestVersion?.VerifiedAt.HasValue == true;
    }

    private static ResolvedResultState ResolveResultState(
        string rawOrderStatus,
        string? normalizedQueueStatus,
        QueueEntry? queueEntry,
        DiagnosticResult? result,
        DiagnosticResultVersion? latestVersion,
        bool requiresResultVerification)
    {
        if (result is not null)
        {
            var rawResultStatus = AppointmentBookingRules.NormalizeStatusKey(result.Status);
            var isResultFinalized = IsDiagnosticResultRowFinalized(result, latestVersion);
            var isCancelled = rawOrderStatus == "CANCELLED" ||
                              normalizedQueueStatus == "CANCELLED" ||
                              rawResultStatus == "CANCELLED";
            var isResultReady = isResultFinalized && !isCancelled;

            string resultStatus;
            if (isCancelled)
            {
                resultStatus = "CANCELLED";
            }
            else if (isResultFinalized)
            {
                resultStatus = rawResultStatus == "VERIFIED" ? "VERIFIED" : "FINALIZED";
            }
            else if (!string.IsNullOrEmpty(rawResultStatus))
            {
                resultStatus = rawResultStatus;
            }
            else
            {
                resultStatus = requiresResultVerification ? "PENDING_VERIFY" : "DRAFT";
            }

            var finalizedAt = result.FinalizedAt
                ?? latestVersion?.VerifiedAt
                ?? (isResultFinalized ? (queueEntry?.CompletedAt ?? latestVersion?.EnteredAt) : null);

            return new ResolvedResultState(
                HasResult: true,
                IsResultReady: isResultReady,
                IsResultFinalized: isResultFinalized,
                ResultStatus: resultStatus,
                ResultFinalizedAt: finalizedAt);
        }

        var isOrderOrQueueCancelled = rawOrderStatus == "CANCELLED" || normalizedQueueStatus == "CANCELLED";
        return new ResolvedResultState(
            HasResult: false,
            IsResultReady: false,
            IsResultFinalized: false,
            ResultStatus: isOrderOrQueueCancelled ? "CANCELLED" : "PENDING",
            ResultFinalizedAt: null);
    }

    private static string ResolveEffectiveOrderStatus(
        DiagnosticOrder order,
        string rawOrderStatus,
        string? normalizedQueueStatus,
        string normalizedVisitStatus,
        bool isResultFinalized,
        bool hasResult)
    {
        if (rawOrderStatus == "CANCELLED" ||
            normalizedQueueStatus == "CANCELLED" ||
            normalizedVisitStatus == "CANCELLED")
        {
            return "CANCELLED";
        }

        if (isResultFinalized ||
            rawOrderStatus is "COMPLETED" or "DONE" or "FINALIZED" or "VERIFIED" ||
            normalizedQueueStatus == "COMPLETED")
        {
            return "COMPLETED";
        }

        if (rawOrderStatus == "IN_PROGRESS" ||
            normalizedQueueStatus == "IN_PROGRESS" ||
            hasResult)
        {
            return "IN_PROGRESS";
        }

        if (rawOrderStatus == "CONFIRMED" ||
            order.ConfirmedAt.HasValue ||
            order.ConfirmedByStaffId.HasValue ||
            normalizedQueueStatus is "READY" or "CALLED")
        {
            return "CONFIRMED";
        }

        return "ORDERED";
    }

    private async Task<IReadOnlyList<DiagnosticResultHistoryItemDto>> BuildResultHistoryAsync(
        DiagnosticOrder order,
        DiagnosticResultEntryItemDto itemDto,
        OrderLookupBundle lookupBundle,
        CancellationToken ct)
    {
        var historyItems = new List<DiagnosticResultHistoryItemDto>();
        var orderIdString = order.Id.ToString(CultureInfo.InvariantCulture);
        var resultIdString = itemDto.ResultId?.ToString(CultureInfo.InvariantCulture);

        try
        {
            var logs = await _db.AuditLogs
                .AsNoTracking()
                .Where(a =>
                    (a.EntityType == DiagnosticResultEntityType &&
                     resultIdString != null &&
                     a.EntityId == resultIdString) ||
                    (a.EntityType == DiagnosticOrderEntityType &&
                     a.EntityId == orderIdString &&
                     (a.Action == DiagnosticResultEnteredAction ||
                      a.Action == DiagnosticResultFinalizedAction)))
                .OrderByDescending(a => a.OccurredAt)
                .ThenByDescending(a => a.Id)
                .ToListAsync(ct);

            foreach (var log in logs)
            {
                int? versionNo = null;
                string? resultStatus = null;
                bool? isFinalized = null;
                string? resultText = null;
                string? conclusion = null;
                string? reportFileUrl = null;
                string? actorStaffCode = null;
                string? actorStaffName = null;
                long? parsedResultId = itemDto.ResultId;

                if (!string.IsNullOrWhiteSpace(log.ChangeData))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(log.ChangeData);
                        if (doc.RootElement.ValueKind == JsonValueKind.Object)
                        {
                            var root = doc.RootElement;
                            if (root.TryGetProperty("resultId", out var rIdProp) &&
                                rIdProp.ValueKind == JsonValueKind.Number &&
                                rIdProp.TryGetInt64(out var rIdVal))
                            {
                                parsedResultId = rIdVal;
                            }

                            if (root.TryGetProperty("versionNo", out var vProp) &&
                                vProp.ValueKind == JsonValueKind.Number &&
                                vProp.TryGetInt32(out var vVal))
                            {
                                versionNo = vVal;
                            }

                            if (root.TryGetProperty("resultStatus", out var rsProp) &&
                                rsProp.ValueKind == JsonValueKind.String)
                            {
                                resultStatus = rsProp.GetString();
                            }

                            if (root.TryGetProperty("isFinalized", out var finProp) &&
                                finProp.ValueKind is JsonValueKind.True or JsonValueKind.False)
                            {
                                isFinalized = finProp.GetBoolean();
                            }

                            if (root.TryGetProperty("resultText", out var rtProp) &&
                                rtProp.ValueKind == JsonValueKind.String)
                            {
                                resultText = rtProp.GetString();
                            }

                            if (root.TryGetProperty("conclusion", out var concProp) &&
                                concProp.ValueKind == JsonValueKind.String)
                            {
                                conclusion = concProp.GetString();
                            }

                            if (root.TryGetProperty("reportFileUrl", out var urlProp) &&
                                urlProp.ValueKind == JsonValueKind.String)
                            {
                                reportFileUrl = urlProp.GetString();
                            }

                            if (root.TryGetProperty("enteredByStaffCode", out var scProp) &&
                                scProp.ValueKind == JsonValueKind.String)
                            {
                                actorStaffCode = scProp.GetString();
                            }

                            if (root.TryGetProperty("enteredByStaffName", out var snProp) &&
                                snProp.ValueKind == JsonValueKind.String)
                            {
                                actorStaffName = snProp.GetString();
                            }
                        }
                    }
                    catch (JsonException)
                    {
                        // Ignore malformed audit JSON
                    }
                }

                var effectiveIsFinalized = isFinalized
                    ?? string.Equals(log.Action, DiagnosticResultFinalizedAction, StringComparison.OrdinalIgnoreCase);
                var effectiveResultStatus = !string.IsNullOrWhiteSpace(resultStatus)
                    ? resultStatus!
                    : (effectiveIsFinalized ? "FINALIZED" : "DRAFT");

                historyItems.Add(new DiagnosticResultHistoryItemDto(
                    AuditLogId: log.Id,
                    OrderId: order.Id,
                    OrderCode: itemDto.OrderCode,
                    ResultId: parsedResultId,
                    VersionNo: versionNo,
                    Action: log.Action,
                    ResultStatus: effectiveResultStatus,
                    IsFinalized: effectiveIsFinalized,
                    ResultText: resultText,
                    Conclusion: conclusion,
                    ReportFileUrl: reportFileUrl,
                    Reason: log.Reason,
                    ActorUserId: log.ActorUserId,
                    ActorStaffCode: actorStaffCode,
                    ActorStaffName: actorStaffName,
                    OccurredAtUtc: log.OccurredAt));
            }
        }
        catch (NotSupportedException)
        {
            // Ignore when AuditLogs DbSet is not configured
        }

        var loggedVersions = historyItems
            .Where(h => h.VersionNo.HasValue)
            .Select(h => h.VersionNo!.Value)
            .ToHashSet();

        foreach (var v in itemDto.Versions)
        {
            if (loggedVersions.Contains(v.VersionNo))
            {
                continue;
            }

            var isFin = v.IsFinalized;
            var status = isFin ? itemDto.ResultStatus : (itemDto.RequiresResultVerification ? "PENDING_VERIFY" : "DRAFT");
            var occurredLocal = v.VerifiedAt ?? v.EnteredAt ?? itemDto.OrderedAt;
            var occurredUtc = DateTime.SpecifyKind(occurredLocal.AddHours(-7), DateTimeKind.Utc);

            historyItems.Add(new DiagnosticResultHistoryItemDto(
                AuditLogId: v.VersionId,
                OrderId: order.Id,
                OrderCode: itemDto.OrderCode,
                ResultId: v.ResultId,
                VersionNo: v.VersionNo,
                Action: isFin ? DiagnosticResultFinalizedAction : DiagnosticResultEnteredAction,
                ResultStatus: status,
                IsFinalized: isFin,
                ResultText: v.ResultText,
                Conclusion: v.Conclusion,
                ReportFileUrl: v.ReportFileUrl,
                Reason: v.CorrectionReason,
                ActorUserId: null,
                ActorStaffCode: v.VerifiedByStaffCode ?? v.EnteredByStaffCode,
                ActorStaffName: v.VerifiedByStaffName ?? v.EnteredByStaffName,
                OccurredAtUtc: occurredUtc));
        }

        return historyItems
            .OrderByDescending(h => h.VersionNo ?? 0)
            .ThenByDescending(h => h.OccurredAtUtc)
            .ThenByDescending(h => h.AuditLogId)
            .ToList();
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
        bool requireResultWritePermission,
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
            throw new ForbiddenException("Nhân viên cận lâm sàng không có quyền truy cập danh sách kết quả cận lâm sàng.");
        }

        var hasResultWritePermission = actorPermissionCodes.Any(code =>
            string.Equals(code, DiagnosticResultWritePermissionCode, StringComparison.OrdinalIgnoreCase));

        if (requireResultWritePermission && !hasResultWritePermission)
        {
            throw new ForbiddenException(
                "Nhân viên cận lâm sàng không có quyền nhập và trả kết quả cận lâm sàng (yêu cầu quyền DIAGNOSTIC_RESULT_WRITE).");
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
            HasResultWritePermission: hasResultWritePermission,
            Profile: staffProfile);
    }

    private static ParsedOrderIdentifier ResolveTargetIdentifier(
        string? orderIdOrCode,
        EnterDiagnosticResultRequest request)
    {
        if (request.OrderId.HasValue && request.OrderId.Value <= 0)
        {
            throw new ValidationException("ID phiếu chỉ định cận lâm sàng phải lớn hơn 0.");
        }

        if (!string.IsNullOrWhiteSpace(orderIdOrCode))
        {
            return ParseOrderIdentifier(orderIdOrCode);
        }

        if (!string.IsNullOrWhiteSpace(request.OrderCode))
        {
            return ParseOrderIdentifier(request.OrderCode);
        }

        if (request.OrderId.HasValue)
        {
            return new ParsedOrderIdentifier(
                OrderId: request.OrderId.Value,
                ResultId: null,
                QueueEntryId: null,
                VisitId: null,
                Code: null,
                RawInput: request.OrderId.Value.ToString(CultureInfo.InvariantCulture));
        }

        throw new ValidationException("Vui lòng cung cấp mã hoặc ID phiếu chỉ định cận lâm sàng cần nhập kết quả.");
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
            trimmed.StartsWith("do_", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("pr-", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("pr_", StringComparison.OrdinalIgnoreCase))
        {
            var numPart = trimmed[3..];
            if (long.TryParse(numPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var doId))
            {
                if (doId <= 0)
                {
                    throw new ValidationException("ID phiếu chỉ định cận lâm sàng phải lớn hơn 0.");
                }

                return new ParsedOrderIdentifier(
                    OrderId: doId,
                    ResultId: null,
                    QueueEntryId: null,
                    VisitId: null,
                    Code: null,
                    RawInput: rawIdOrCode.Trim());
            }

            throw new ValidationException("Mã định danh phiếu chỉ định cận lâm sàng không hợp lệ.");
        }

        if (trimmed.StartsWith("dr-", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("dr_", StringComparison.OrdinalIgnoreCase))
        {
            var numPart = trimmed[3..];
            if (long.TryParse(numPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var drId))
            {
                if (drId <= 0)
                {
                    throw new ValidationException("ID kết quả cận lâm sàng phải lớn hơn 0.");
                }

                return new ParsedOrderIdentifier(
                    OrderId: null,
                    ResultId: drId,
                    QueueEntryId: null,
                    VisitId: null,
                    Code: null,
                    RawInput: rawIdOrCode.Trim());
            }

            throw new ValidationException("Mã định danh kết quả cận lâm sàng không hợp lệ.");
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

                return new ParsedOrderIdentifier(
                    OrderId: null,
                    ResultId: null,
                    QueueEntryId: qId,
                    VisitId: null,
                    Code: null,
                    RawInput: rawIdOrCode.Trim());
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

                return new ParsedOrderIdentifier(
                    OrderId: null,
                    ResultId: null,
                    QueueEntryId: null,
                    VisitId: vId,
                    Code: null,
                    RawInput: rawIdOrCode.Trim());
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

            return new ParsedOrderIdentifier(
                OrderId: parsedId,
                ResultId: null,
                QueueEntryId: null,
                VisitId: null,
                Code: null,
                RawInput: rawIdOrCode.Trim());
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
            ResultId: null,
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

        if (identifier.ResultId.HasValue)
        {
            long? orderIdFromResult = null;
            try
            {
                var foundResult = await _db.DiagnosticResults
                    .AsNoTracking()
                    .FirstOrDefaultAsync(r => r.Id == identifier.ResultId.Value, ct);
                orderIdFromResult = foundResult?.DiagnosticOrderId;
            }
            catch (NotSupportedException)
            {
                orderIdFromResult = null;
            }

            var targetOrderId = orderIdFromResult ?? identifier.ResultId.Value;
            var byResult = await query.FirstOrDefaultAsync(o => o.Id == targetOrderId, ct);
            return byResult ?? throw new NotFoundException("DiagnosticOrder", identifier.RawInput);
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

        var (resultsByOrderId, versionsByResultId) = await TryLoadResultsAndVersionsAsync(visitOrders, ct);

        var eligibleForEntry = visitOrders
            .Where(o =>
            {
                var rawStatus = NormalizeOrderStatus(o.Status);
                if (rawStatus == "CANCELLED")
                {
                    return false;
                }

                var qe = o.QueueEntryId.HasValue && queueEntriesById.TryGetValue(o.QueueEntryId.Value, out var foundQe)
                    ? foundQe
                    : null;
                var qStatus = qe is not null ? NormalizeQueueStatus(qe.Status) : null;
                if (qStatus == "CANCELLED")
                {
                    return false;
                }

                resultsByOrderId.TryGetValue(o.Id, out var res);
                var latestVer = res is not null &&
                                versionsByResultId.TryGetValue(res.Id, out var vers) &&
                                vers.Count > 0
                    ? vers[0]
                    : null;

                if (IsDiagnosticResultRowFinalized(res, latestVer))
                {
                    return false;
                }

                return IsOrderExecutionStartedOrCompleted(rawStatus, qStatus, qe, res);
            })
            .OrderByDescending(o => GetPriorityRank(o.Priority))
            .ThenBy(o => o.OrderedAt ?? DateTime.MinValue)
            .ThenBy(o => o.Id)
            .FirstOrDefault();

        if (eligibleForEntry is not null)
        {
            return eligibleForEntry;
        }

        return visitOrders
            .OrderByDescending(o => GetPriorityRank(o.Priority))
            .ThenByDescending(o => o.OrderedAt ?? DateTime.MinValue)
            .ThenByDescending(o => o.Id)
            .First();
    }

    private static ValidatedListFilter ValidateListFilter(GetDiagnosticOrdersForResultEntryRequest request)
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

        string? normalizedResultStatus = null;
        if (!string.IsNullOrWhiteSpace(request.ResultStatus) &&
            !string.Equals(request.ResultStatus.Trim(), "ALL", StringComparison.OrdinalIgnoreCase))
        {
            var trimmedResultStatus = request.ResultStatus.Trim();
            if (!ValidResultStatuses.Contains(trimmedResultStatus))
            {
                throw new ValidationException("Trạng thái kết quả cận lâm sàng lọc không hợp lệ.");
            }

            normalizedResultStatus = trimmedResultStatus.ToUpperInvariant();
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
            ResultStatus: normalizedResultStatus,
            QueueStatus: normalizedQueueStatus,
            Category: normalizedCategory,
            Priority: normalizedPriority,
            VisitId: request.VisitId,
            VisitCode: normalizedVisitCode,
            ServiceId: request.ServiceId,
            RoomId: request.RoomId,
            ServicePointId: request.ServicePointId,
            ReadyForResultEntryOnly: request.ReadyForResultEntryOnly,
            PendingFinalizationOnly: request.PendingFinalizationOnly,
            Keyword: normalizedKeyword,
            Limit: request.Limit);
    }

    private static ValidatedEnterDiagnosticResultInput ValidateEnterResultRequest(EnterDiagnosticResultRequest request)
    {
        if (request is null)
        {
            throw new ValidationException("Dữ liệu nhập kết quả cận lâm sàng không được để trống.");
        }

        if (request.ExpectedStateVersion is <= 0)
        {
            throw new ValidationException("ExpectedStateVersion phải lớn hơn hoặc bằng 1.");
        }

        var rawResultText = FirstNonEmpty(
            request.ResultText,
            request.Findings,
            request.Description,
            request.ResultSummary);
        if (string.IsNullOrWhiteSpace(rawResultText))
        {
            throw new ValidationException("Nội dung kết quả cận lâm sàng (ResultText) không được để trống.");
        }

        var trimmedResultText = rawResultText.Trim();
        if (trimmedResultText.Length < 3)
        {
            throw new ValidationException("Nội dung kết quả cận lâm sàng (ResultText) phải có ít nhất 3 ký tự.");
        }

        if (trimmedResultText.Length > 5000)
        {
            throw new ValidationException("Nội dung kết quả cận lâm sàng (ResultText) không được vượt quá 5000 ký tự.");
        }

        if (trimmedResultText.Contains('<') || trimmedResultText.Contains('>'))
        {
            throw new ValidationException("Nội dung kết quả cận lâm sàng chứa ký tự không hợp lệ.");
        }

        var rawConclusion = FirstNonEmpty(
            request.Conclusion,
            request.Impression,
            request.DiagnosticConclusion);
        if (string.IsNullOrWhiteSpace(rawConclusion))
        {
            throw new ValidationException("Kết luận cận lâm sàng (Conclusion) không được để trống.");
        }

        var trimmedConclusion = rawConclusion.Trim();
        if (trimmedConclusion.Length < 3)
        {
            throw new ValidationException("Kết luận cận lâm sàng (Conclusion) phải có ít nhất 3 ký tự.");
        }

        if (trimmedConclusion.Length > 2000)
        {
            throw new ValidationException("Kết luận cận lâm sàng (Conclusion) không được vượt quá 2000 ký tự.");
        }

        if (trimmedConclusion.Contains('<') || trimmedConclusion.Contains('>'))
        {
            throw new ValidationException("Kết luận cận lâm sàng chứa ký tự không hợp lệ.");
        }

        var rawReportFileUrl = FirstNonEmpty(
            request.ReportFileUrl,
            request.AttachmentUrl,
            request.FileUrl);
        string? normalizedReportFileUrl = null;
        if (!string.IsNullOrWhiteSpace(rawReportFileUrl))
        {
            normalizedReportFileUrl = rawReportFileUrl.Trim();
            if (normalizedReportFileUrl.Length > 500)
            {
                throw new ValidationException("Đường dẫn tệp báo cáo cận lâm sàng (ReportFileUrl) không được vượt quá 500 ký tự.");
            }

            if (normalizedReportFileUrl.Contains('<') ||
                normalizedReportFileUrl.Contains('>') ||
                normalizedReportFileUrl.Any(char.IsWhiteSpace))
            {
                throw new ValidationException("Đường dẫn tệp báo cáo cận lâm sàng (ReportFileUrl) chứa ký tự không hợp lệ.");
            }

            if (!normalizedReportFileUrl.StartsWith('/') &&
                !normalizedReportFileUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !normalizedReportFileUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                throw new ValidationException(
                    "Đường dẫn tệp báo cáo cận lâm sàng (ReportFileUrl) phải bắt đầu bằng '/', 'http://' hoặc 'https://'.");
            }
        }

        var rawCorrectionReason = FirstNonEmpty(
            request.CorrectionReason,
            request.AmendmentReason,
            request.Notes);
        string? normalizedCorrectionReason = null;
        if (!string.IsNullOrWhiteSpace(rawCorrectionReason))
        {
            normalizedCorrectionReason = rawCorrectionReason.Trim();
            if (normalizedCorrectionReason.Length > 500)
            {
                throw new ValidationException("Ghi chú hoặc lý do hiệu chỉnh kết quả không được vượt quá 500 ký tự.");
            }

            if (normalizedCorrectionReason.Contains('<') || normalizedCorrectionReason.Contains('>'))
            {
                throw new ValidationException("Ghi chú hoặc lý do hiệu chỉnh kết quả chứa ký tự không hợp lệ.");
            }
        }

        var rawStatus = FirstNonEmpty(request.ResultStatus, request.Status);
        var normalizedStatusKey = string.IsNullOrWhiteSpace(rawStatus)
            ? string.Empty
            : AppointmentBookingRules.NormalizeStatusKey(rawStatus);

        if (!string.IsNullOrEmpty(normalizedStatusKey))
        {
            if (!ValidWriteResultStatuses.Contains(normalizedStatusKey))
            {
                throw new ValidationException(
                    "Trạng thái kết quả cận lâm sàng không hợp lệ. Chỉ chấp nhận DRAFT, PRELIMINARY, PENDING_VERIFY, FINALIZED hoặc VERIFIED.");
            }

            if (normalizedStatusKey == "COMPLETED")
            {
                normalizedStatusKey = "FINALIZED";
            }
        }

        var explicitFinalize = request.IsFinalized ?? request.Finalize;
        var explicitVerify = request.IsVerified ?? request.Verify;

        if (explicitVerify == true && explicitFinalize == false)
        {
            throw new ValidationException(
                "Không thể đánh dấu đã thẩm định (IsVerified = true) khi kết quả chưa được hoàn tất (IsFinalized = false).");
        }

        if (normalizedStatusKey is "DRAFT" or "PRELIMINARY" or "PENDING_VERIFY" &&
            (explicitFinalize == true || explicitVerify == true))
        {
            throw new ValidationException(
                "Trạng thái kết quả nháp/sơ bộ không tương thích với cờ khóa kết quả cuối (IsFinalized = true).");
        }

        if (normalizedStatusKey is "FINALIZED" or "VERIFIED" && explicitFinalize == false)
        {
            throw new ValidationException(
                "Trạng thái kết quả hoàn tất/đã duyệt không tương thích với cờ IsFinalized = false.");
        }

        return new ValidatedEnterDiagnosticResultInput(
            ResultText: trimmedResultText,
            Conclusion: trimmedConclusion,
            ReportFileUrl: normalizedReportFileUrl,
            CorrectionReason: normalizedCorrectionReason,
            StatusKey: normalizedStatusKey,
            ExplicitFinalize: explicitFinalize,
            ExplicitVerify: explicitVerify,
            ExpectedStateVersion: request.ExpectedStateVersion);
    }

    private static (string TargetResultStatus, bool IsFinalized, bool IsVerified) ResolveTargetResultStatus(
        ValidatedEnterDiagnosticResultInput validated,
        bool requiresResultVerification)
    {
        var status = validated.StatusKey;
        var explicitFinalize = validated.ExplicitFinalize;
        var explicitVerify = validated.ExplicitVerify;

        if (status is "DRAFT" or "PRELIMINARY" or "PENDING_VERIFY")
        {
            return (status, false, false);
        }

        if (status == "VERIFIED" || (status is "" or "FINALIZED" && explicitVerify == true))
        {
            return ("VERIFIED", true, true);
        }

        if (status == "FINALIZED" || (status == "" && explicitFinalize != false))
        {
            return ("FINALIZED", true, true);
        }

        var unfinalizedStatus = requiresResultVerification ? "PENDING_VERIFY" : "DRAFT";
        return (unfinalizedStatus, false, false);
    }

    private static IReadOnlyList<DiagnosticResultEntryItemDto> ApplyListFilters(
        IReadOnlyList<DiagnosticResultEntryItemDto> items,
        ValidatedListFilter filter)
    {
        IEnumerable<DiagnosticResultEntryItemDto> query = items;

        if (filter.ReadyForResultEntryOnly)
        {
            query = query.Where(i => i.CanEnterResult);
        }

        if (filter.PendingFinalizationOnly)
        {
            query = query.Where(i => i.HasResult && !i.IsResultFinalized && i.Status != "CANCELLED");
        }

        if (filter.Status is not null)
        {
            query = query.Where(i =>
                string.Equals(i.Status, filter.Status, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(i.OrderStatus, filter.Status, StringComparison.OrdinalIgnoreCase));
        }

        if (filter.ResultStatus is not null)
        {
            if (string.Equals(filter.ResultStatus, "COMPLETED", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(i => i.IsResultFinalized);
            }
            else
            {
                query = query.Where(i =>
                    string.Equals(i.ResultStatus, filter.ResultStatus, StringComparison.OrdinalIgnoreCase));
            }
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
                (i.TicketNo is not null && i.TicketNo.Contains(kw, StringComparison.OrdinalIgnoreCase)) ||
                (i.ResultText is not null && i.ResultText.Contains(kw, StringComparison.OrdinalIgnoreCase)) ||
                (i.Conclusion is not null && i.Conclusion.Contains(kw, StringComparison.OrdinalIgnoreCase)));
        }

        return query
            .OrderByDescending(i => i.CanEnterResult ? 2 : (!i.IsResultFinalized && i.Status != "CANCELLED" ? 1 : 0))
            .ThenByDescending(i => GetPriorityRank(i.Priority))
            .ThenBy(i => i.OrderedAt)
            .ThenByDescending(i => i.OrderId)
            .Take(filter.Limit)
            .ToList();
    }

    private static DiagnosticResultEntrySummaryStatsDto BuildSummaryStats(
        IReadOnlyList<DiagnosticResultEntryItemDto> items)
    {
        var total = items.Count;
        var readyForResultEntry = 0;
        var inProgressExecution = 0;
        var draftOrPreliminary = 0;
        var pendingVerification = 0;
        var finalized = 0;
        var cancelled = 0;
        var urgentOrStatPendingResult = 0;

        foreach (var item in items)
        {
            if (item.Status == "CANCELLED" || item.ResultStatus == "CANCELLED")
            {
                cancelled++;
                continue;
            }

            if (item.CanEnterResult)
            {
                readyForResultEntry++;
            }

            if (item.IsResultFinalized)
            {
                finalized++;
                continue;
            }

            if (item.Eligibility.IsOrderExecutionStartedOrCompleted)
            {
                inProgressExecution++;
            }

            if (item.ResultStatus is "DRAFT" or "PRELIMINARY")
            {
                draftOrPreliminary++;
            }
            else if (item.ResultStatus == "PENDING_VERIFY")
            {
                pendingVerification++;
            }

            if (item.Priority is "URGENT" or "STAT")
            {
                urgentOrStatPendingResult++;
            }
        }

        return new DiagnosticResultEntrySummaryStatsDto(
            TotalOrders: total,
            ReadyForResultEntryCount: readyForResultEntry,
            InProgressExecutionCount: inProgressExecution,
            DraftOrPreliminaryCount: draftOrPreliminary,
            PendingVerificationCount: pendingVerification,
            FinalizedCount: finalized,
            CancelledCount: cancelled,
            UrgentOrStatPendingResultCount: urgentOrStatPendingResult);
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
            bundle.ServicesById.TryGetValue(order.ServiceId.Value, out var service))
        {
            if (service.HospitalId.HasValue)
            {
                return service.HospitalId.Value;
            }

            if (service.DepartmentId.HasValue &&
                bundle.DepartmentsById.TryGetValue(service.DepartmentId.Value, out var dept) &&
                dept.HospitalId.HasValue)
            {
                return dept.HospitalId.Value;
            }
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
                bundle.ServicePointsById.TryGetValue(spId.Value, out var sp))
            {
                if (sp.HospitalId.HasValue)
                {
                    return sp.HospitalId.Value;
                }

                if (sp.RoomId.HasValue &&
                    bundle.RoomsById.TryGetValue(sp.RoomId.Value, out var spRoom) &&
                    spRoom.HospitalId.HasValue)
                {
                    return spRoom.HospitalId.Value;
                }
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
            throw new ForbiddenException(
                "Phiếu chỉ định cận lâm sàng không thuộc cơ sở y tế của nhân viên cận lâm sàng.");
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

        var (resultsByOrderId, versionsByResultId) = await TryLoadResultsAndVersionsAsync(orders, ct);

        var orderIdStrings = orders
            .Select(o => o.Id.ToString(CultureInfo.InvariantCulture))
            .Distinct()
            .ToList();
        var auditMetadataByOrderId = await TryLoadDiagnosticOrderAuditMetadataAsync(orderIdStrings, ct);

        var staffIds = orders
            .Where(o => o.ConfirmedByStaffId.HasValue)
            .Select(o => o.ConfirmedByStaffId!.Value)
            .Concat(resultsByOrderId.Values
                .Where(r => r.FinalizedByStaffId.HasValue)
                .Select(r => r.FinalizedByStaffId!.Value))
            .Concat(versionsByResultId.Values
                .SelectMany(list => list)
                .SelectMany(v => new[] { v.EnteredByStaffId, v.VerifiedByStaffId })
                .Where(id => id.HasValue)
                .Select(id => id!.Value))
            .Distinct()
            .ToList();

        var staffById = staffIds.Count > 0
            ? await _db.Staff
                .AsNoTracking()
                .Where(s => staffIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, ct)
            : new Dictionary<long, Staff>();

        var (medicalRecordsByVisitId, latestRevisionByVisitId) = await TryLoadMedicalRecordsAndRevisionsByVisitIdAsync(visitIds, ct);

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
            ResultsByOrderId: resultsByOrderId,
            VersionsByResultId: versionsByResultId,
            MedicalRecordsByVisitId: medicalRecordsByVisitId,
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

    private async Task<(Dictionary<long, DiagnosticResult> ResultsByOrderId, Dictionary<long, IReadOnlyList<DiagnosticResultVersion>> VersionsByResultId)> TryLoadResultsAndVersionsAsync(
        IReadOnlyList<DiagnosticOrder> orders,
        CancellationToken ct)
    {
        var resultsByOrderId = new Dictionary<long, DiagnosticResult>();
        var versionsByResultId = new Dictionary<long, IReadOnlyList<DiagnosticResultVersion>>();

        if (orders.Count == 0)
        {
            return (resultsByOrderId, versionsByResultId);
        }

        var orderIds = orders.Select(o => o.Id).Distinct().ToList();
        List<DiagnosticResult> rawResults;
        try
        {
            rawResults = await _db.DiagnosticResults
                .AsNoTracking()
                .Where(r => r.DiagnosticOrderId.HasValue && orderIds.Contains(r.DiagnosticOrderId.Value))
                .ToListAsync(ct);
        }
        catch (NotSupportedException)
        {
            rawResults = [];
        }

        resultsByOrderId = rawResults
            .Where(r => r.DiagnosticOrderId.HasValue)
            .GroupBy(r => r.DiagnosticOrderId!.Value)
            .ToDictionary(
                g => g.Key,
                g => g
                    .OrderByDescending(r =>
                        r.FinalizedAt.HasValue ||
                        AppointmentBookingRules.NormalizeStatusKey(r.Status) is "FINALIZED" or "VERIFIED" or "COMPLETED"
                            ? 1
                            : 0)
                    .ThenByDescending(r => r.FinalizedAt ?? DateTime.MinValue)
                    .ThenByDescending(r => r.Id)
                    .First());

        var resultIds = resultsByOrderId.Values.Select(r => r.Id).Distinct().ToList();
        if (resultIds.Count > 0)
        {
            try
            {
                var rawVersions = await _db.DiagnosticResultVersions
                    .AsNoTracking()
                    .Where(v => v.DiagnosticResultId.HasValue && resultIds.Contains(v.DiagnosticResultId.Value))
                    .ToListAsync(ct);

                versionsByResultId = rawVersions
                    .Where(v => v.DiagnosticResultId.HasValue)
                    .GroupBy(v => v.DiagnosticResultId!.Value)
                    .ToDictionary(
                        g => g.Key,
                        g => (IReadOnlyList<DiagnosticResultVersion>)g
                            .OrderByDescending(v => v.VersionNo ?? 0)
                            .ThenByDescending(v => v.IsFinalized == true ? 1 : 0)
                            .ThenByDescending(v => v.VerifiedAt ?? v.EnteredAt ?? DateTime.MinValue)
                            .ThenByDescending(v => v.Id)
                            .ToList());
            }
            catch (NotSupportedException)
            {
                // Ignore when DiagnosticResultVersions DbSet not configured
            }
        }

        return (resultsByOrderId, versionsByResultId);
    }

    private async Task<(Dictionary<long, MedicalRecord> RecordsByVisitId, Dictionary<long, MedicalRecordRevision> LatestRevisionByVisitId)> TryLoadMedicalRecordsAndRevisionsByVisitIdAsync(
        IReadOnlyList<long> visitIds,
        CancellationToken ct)
    {
        var recordsByVisitId = new Dictionary<long, MedicalRecord>();
        var latestRevisionByVisitId = new Dictionary<long, MedicalRecordRevision>();

        if (visitIds.Count == 0)
        {
            return (recordsByVisitId, latestRevisionByVisitId);
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
                return (recordsByVisitId, latestRevisionByVisitId);
            }

            foreach (var rec in records)
            {
                if (rec.VisitId.HasValue && !recordsByVisitId.ContainsKey(rec.VisitId.Value))
                {
                    recordsByVisitId[rec.VisitId.Value] = rec;
                }
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
                    !latestRevisionByVisitId.ContainsKey(record.VisitId.Value) &&
                    latestRevByRecordId.TryGetValue(record.Id, out var rev))
                {
                    latestRevisionByVisitId[record.VisitId.Value] = rev;
                }
            }
        }
        catch (NotSupportedException)
        {
            return (recordsByVisitId, latestRevisionByVisitId);
        }

        return (recordsByVisitId, latestRevisionByVisitId);
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
                     a.Action == "DIAGNOSTIC_ORDER_CREATED"))
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
                    AdditionalOrderReason: additionalReason);
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
            throw new ConflictException(
                "Dữ liệu phiếu chỉ định hoặc hàng đợi cận lâm sàng vừa được cập nhật bởi thao tác khác. Vui lòng tải lại và thử lại.");
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

    private static string? FirstNonEmpty(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private sealed record DiagnosticStaffAccessContext(
        Hospital Hospital,
        Staff Staff,
        string DepartmentName,
        bool HasResultWritePermission,
        DiagnosticStaffProfileDto Profile);

    private sealed record ParsedOrderIdentifier(
        long? OrderId,
        long? ResultId,
        long? QueueEntryId,
        long? VisitId,
        string? Code,
        string RawInput);

    private sealed record ValidatedListFilter(
        string? Status,
        string? ResultStatus,
        string? QueueStatus,
        string? Category,
        string? Priority,
        long? VisitId,
        string? VisitCode,
        long? ServiceId,
        long? RoomId,
        long? ServicePointId,
        bool ReadyForResultEntryOnly,
        bool PendingFinalizationOnly,
        string? Keyword,
        int Limit);

    private sealed record ValidatedEnterDiagnosticResultInput(
        string ResultText,
        string Conclusion,
        string? ReportFileUrl,
        string? CorrectionReason,
        string StatusKey,
        bool? ExplicitFinalize,
        bool? ExplicitVerify,
        int? ExpectedStateVersion);

    private sealed record ResolvedResultState(
        bool HasResult,
        bool IsResultReady,
        bool IsResultFinalized,
        string ResultStatus,
        DateTime? ResultFinalizedAt);

    private sealed record DiagnosticOrderAuditMetadata(
        bool IsAdditionalOrder,
        int? OrderSequenceNo,
        long? FollowUpToOrderId,
        string? FollowUpToOrderCode,
        string? AdditionalOrderReason);

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
        Dictionary<long, DiagnosticResult> ResultsByOrderId,
        Dictionary<long, IReadOnlyList<DiagnosticResultVersion>> VersionsByResultId,
        Dictionary<long, MedicalRecord> MedicalRecordsByVisitId,
        Dictionary<long, MedicalRecordRevision> LatestRevisionByVisitId,
        Dictionary<long, int> SequenceByOrderId,
        Dictionary<long, DiagnosticOrderAuditMetadata> AuditMetadataByOrderId);
}
