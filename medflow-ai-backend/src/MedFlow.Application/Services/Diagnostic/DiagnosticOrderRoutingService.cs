using System.Globalization;
using System.Net;
using System.Text.Json;
using MedFlow.Application.Common.Time;
using MedFlow.Application.DTOs.Diagnostic;
using MedFlow.Application.DTOs.Realtime;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Services.Appointments;
using MedFlow.Application.Services.Payments;
using MedFlow.Application.Services.Realtime;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Diagnostic;

/// <summary>
/// Điều phối lại điểm CLS (#870) và cung cấp dữ liệu phiếu/màn hình bệnh nhân (#871).
/// </summary>
public sealed class DiagnosticOrderRoutingService : IDiagnosticOrderRoutingService
{
    private const string ForbiddenMessage = "Bạn không có quyền điều phối chỉ định cận lâm sàng của cơ sở y tế này.";
    private const string ReassignPermissionMessage = "Tài khoản không có quyền điều phối lại điểm cận lâm sàng (DIAGNOSTIC_REROUTE).";
    private const string ReasonRequiredMessage = "Điều phối lại điểm cận lâm sàng bắt buộc phải có lý do (tối thiểu 5 ký tự).";
    private const string InProgressMessage = "Không thể điều phối lại khi bệnh nhân đang thực hiện dịch vụ cận lâm sàng tại điểm hiện tại.";
    private const string SlipForbiddenMessage = "Người dùng không có quyền xem phiếu chỉ định cận lâm sàng của lượt khám này.";

    private static readonly HashSet<string> AllowedReassignRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        RoleNames.DiagnosticStaff,
        RoleNames.Reception,
    };

    private static readonly HashSet<string> AllowedSlipRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        RoleNames.Patient,
        RoleNames.Doctor,
        RoleNames.Reception,
        RoleNames.DiagnosticStaff,
        RoleNames.HospitalAdmin,
        RoleNames.HeadDoctor,
        RoleNames.ClinicalNurse,
    };

    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _timeProvider;
    private readonly IRealtimeNotificationPublisher _realtimePublisher;

    public DiagnosticOrderRoutingService(
        IApplicationDbContext db,
        TimeProvider? timeProvider = null,
        IRealtimeNotificationPublisher? realtimePublisher = null)
    {
        _db = db;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _realtimePublisher = realtimePublisher ?? NullRealtimeNotificationPublisher.Instance;
    }

    public async Task<ReassignDiagnosticOrderResponseDto> ReassignAsync(
        long actorUserId,
        string orderIdOrCode,
        ReassignDiagnosticOrderRequest? request,
        string? ipAddress = null,
        CancellationToken ct = default)
    {
        var reason = ValidateReason(request?.Reason);
        if (request?.TargetServicePointId is null && request?.TargetRoomId is null)
        {
            throw new ValidationException("Vui lòng chọn điểm phục vụ cận lâm sàng đích để điều phối lại.");
        }

        await VisitPaymentAccess.RequireActiveUserAsync(_db, actorUserId, _timeProvider, ct);
        var (roleIds, roleCodes) = await VisitPaymentAccess.LoadActorRolesAsync(_db, actorUserId, ct);
        var actingRole = AllowedReassignRoles.FirstOrDefault(role => roleCodes.Contains(role));
        if (actingRole is null)
        {
            throw new ForbiddenException(ForbiddenMessage);
        }

        var permissionCodes = await VisitPaymentAccess.LoadRolePermissionCodesAsync(_db, roleIds, ct);
        if (!permissionCodes.Contains(PermissionCodes.DiagnosticReroute))
        {
            throw new ForbiddenException(ReassignPermissionMessage);
        }

        var staff = await _db.Staff.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == actorUserId, ct)
            ?? throw new ForbiddenException(ForbiddenMessage);
        if (!string.Equals(staff.Status?.Trim(), "ACTIVE", StringComparison.OrdinalIgnoreCase) ||
            !staff.HospitalId.HasValue)
        {
            throw new ForbiddenException(ForbiddenMessage);
        }

        var orderId = await ResolveOrderIdAsync(orderIdOrCode, staff.HospitalId.Value, ct);
        var preOrder = await _db.DiagnosticOrders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId, ct)
            ?? throw new NotFoundException("DiagnosticOrder", orderId);
        var preVisit = preOrder.VisitId.HasValue
            ? await _db.Visits.AsNoTracking().FirstOrDefaultAsync(v => v.Id == preOrder.VisitId.Value, ct)
            : null;
        if (preVisit is null)
        {
            throw new NotFoundException("DiagnosticOrder", orderId);
        }

        var hospitalIds = await VisitHospitalResolver.CollectAsync(_db, preVisit, ct);
        if (hospitalIds.Count != 1 || hospitalIds.First() != staff.HospitalId.Value)
        {
            throw new ForbiddenException(ForbiddenMessage);
        }

        var hospitalId = staff.HospitalId.Value;
        var nowLocal = HospitalClock.GetNow(_timeProvider);
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        long? queueEntryId = preOrder.QueueEntryId;
        long? previousPointId = preOrder.AssignedServicePointId;
        QueueServicePoint targetPoint;
        QueueServicePoint? previousPoint = null;
        Service service;
        HospitalRoom? targetRoom;
        DiagnosticOrder order;
        Visit visit;
        QueueEntry? queueEntry = null;
        long historyId;

        await using (var tx = await _db.BeginTransactionAsync(ct))
        {
            try
            {
                await DiagnosticOrderLifecycleHelper.AcquireVisitAndDiagnosticLocksAsync(
                    _db,
                    visitIds: [preVisit.Id],
                    extraServicePointIds: new[] { request!.TargetServicePointId ?? 0, previousPointId ?? 0 }
                        .Where(id => id > 0)
                        .Distinct()
                        .ToList(),
                    extraQueueEntryIds: queueEntryId.HasValue ? [queueEntryId.Value] : null,
                    extraDiagnosticOrderIds: [orderId],
                    includeChargeAndPaymentLocks: false,
                    ct: ct,
                    extraPatientIds: preVisit.PatientId is > 0 ? [preVisit.PatientId.Value] : null);

                if (_db is DbContext trackerCtx)
                {
                    trackerCtx.ChangeTracker.Clear();
                }

                order = await _db.DiagnosticOrders.FirstOrDefaultAsync(o => o.Id == orderId, ct)
                    ?? throw new NotFoundException("DiagnosticOrder", orderId);
                visit = await _db.Visits.FirstOrDefaultAsync(v => v.Id == preVisit.Id, ct)
                    ?? throw new NotFoundException("Visit", preVisit.Id);
                service = order.ServiceId.HasValue
                    ? await _db.Services.AsNoTracking().FirstOrDefaultAsync(s => s.Id == order.ServiceId.Value, ct)
                        ?? throw new ConflictException("Chỉ định cận lâm sàng không còn dịch vụ hợp lệ để điều phối.")
                    : throw new ConflictException("Chỉ định cận lâm sàng không còn dịch vụ hợp lệ để điều phối.");

                EnsureOrderCanBeRerouted(order);
                queueEntry = order.QueueEntryId.HasValue
                    ? await _db.QueueEntries.FirstOrDefaultAsync(q => q.Id == order.QueueEntryId.Value, ct)
                    : null;
                if (queueEntry is not null)
                {
                    EnsureQueueEntryCanBeRerouted(queueEntry);
                }

                var servicePoints = await _db.QueueServicePoints.AsNoTracking()
                    .Where(sp => sp.HospitalId == hospitalId && sp.PointType != null && sp.PointType.ToUpper() == "DIAGNOSTIC")
                    .OrderBy(sp => sp.Id)
                    .ToListAsync(ct);
                var roomsById = await _db.HospitalRooms.AsNoTracking()
                    .Where(r => r.HospitalId == hospitalId)
                    .ToDictionaryAsync(r => r.Id, ct);
                var departmentsById = await _db.Departments.AsNoTracking()
                    .Where(d => d.HospitalId == hospitalId)
                    .ToDictionaryAsync(d => d.Id, ct);

                targetPoint = ResolveTargetPoint(
                    request,
                    service,
                    servicePoints,
                    roomsById,
                    departmentsById,
                    hospitalId);
                if (order.AssignedServicePointId == targetPoint.Id)
                {
                    throw new ConflictException("Điểm phục vụ cận lâm sàng đích trùng với điểm đã được phân công.");
                }

                previousPoint = order.AssignedServicePointId.HasValue
                    ? servicePoints.FirstOrDefault(sp => sp.Id == order.AssignedServicePointId.Value)
                        ?? await _db.QueueServicePoints.AsNoTracking().FirstOrDefaultAsync(sp => sp.Id == order.AssignedServicePointId.Value, ct)
                    : null;

                roomsById.TryGetValue(targetPoint.RoomId ?? 0, out targetRoom);

                order.AssignedServicePointId = targetPoint.Id;
                queueEntryId = order.QueueEntryId;
                var queueMoved = false;
                if (queueEntry is not null)
                {
                    queueEntry.ServicePointId = targetPoint.Id;
                    queueEntry.CurrentServicePointId = targetPoint.Id;
                    queueEntry.StateVersion = (queueEntry.StateVersion is > 0 ? queueEntry.StateVersion.Value : 1) + 1;
                    queueMoved = true;
                }

                var history = new DiagnosticOrderRoutingHistory
                {
                    DiagnosticOrderId = order.Id,
                    VisitId = visit.Id,
                    HospitalId = hospitalId,
                    FromServicePointId = previousPoint?.Id,
                    ToServicePointId = targetPoint.Id,
                    Source = DiagnosticOrderRoutingHistory.SourceManualReassign,
                    Reason = reason,
                    ChangedByUserId = actorUserId,
                    ChangedByRole = actingRole,
                    CreatedAt = nowLocal,
                };
                _db.DiagnosticOrderRoutingHistories.Add(history);

                _db.AuditLogs.Add(new AuditLog
                {
                    ActorUserId = actorUserId,
                    HospitalId = hospitalId,
                    Action = "DIAGNOSTIC_ORDER_REASSIGNED",
                    EntityType = "DIAGNOSTIC_ORDER",
                    EntityId = !string.IsNullOrWhiteSpace(order.OrderCode) ? order.OrderCode!.Trim() : order.Id.ToString(CultureInfo.InvariantCulture),
                    Reason = reason,
                    ChangeData = JsonSerializer.Serialize(new
                    {
                        visitId = visit.Id,
                        visitCode = visit.VisitCode,
                        diagnosticOrderId = order.Id,
                        orderCode = order.OrderCode,
                        serviceId = service.Id,
                        serviceCode = service.ServiceCode,
                        serviceName = service.Name,
                        fromServicePointId = previousPoint?.Id,
                        fromServicePointName = previousPoint?.Name,
                        toServicePointId = targetPoint.Id,
                        toServicePointName = targetPoint.Name,
                        queueEntryId = queueEntry?.Id,
                        queueMoved,
                        actorRole = actingRole,
                        reason,
                    }),
                    IpAddress = IPAddress.TryParse(ipAddress, out var parsedIp) ? parsedIp : null,
                    OccurredAt = nowUtc,
                });

                await _db.SaveChangesAsync(ct);
                historyId = history.Id;
                await tx.CommitAsync(ct);
            }
            catch
            {
                try
                {
                    await tx.RollbackAsync(ct);
                }
                catch
                {
                    // Transaction already rolled back / not supported.
                }

                throw;
            }
        }

        var queueNotifications = new[]
        {
            new QueueChangedNotification(HospitalId: hospitalId, ServicePointId: previousPoint?.Id, RoomId: previousPoint?.RoomId),
            new QueueChangedNotification(HospitalId: hospitalId, ServicePointId: targetPoint.Id, RoomId: targetPoint.RoomId),
        };
        foreach (var notification in queueNotifications.Where(n => n.ServicePointId.HasValue).Distinct())
        {
            await _realtimePublisher.TryPublishQueueChangedSafeAsync(notification, ct);
        }

        return new ReassignDiagnosticOrderResponseDto(
            OrderId: order.Id,
            OrderCode: order.OrderCode,
            VisitId: visit.Id,
            VisitCode: visit.VisitCode,
            PreviousServicePointId: previousPoint?.Id,
            PreviousServicePointName: previousPoint?.Name,
            AssignedServicePointId: targetPoint.Id,
            AssignedServicePointName: targetPoint.Name,
            RoomId: targetPoint.RoomId,
            RoomCode: targetRoom?.RoomCode,
            RoomName: targetRoom?.Name,
            Reason: reason,
            QueueEntryId: queueEntryId,
            QueueEntryMoved: queueEntry is not null,
            QueueStatus: queueEntry?.Status,
            SequenceNo: order.SequenceNo,
            RoutingHistoryId: historyId,
            ChangedAtUtc: nowUtc);
    }

    public async Task<DiagnosticOrderAssignmentSlipDto> GetVisitAssignmentsAsync(
        long actorUserId,
        string visitIdOrCode,
        CancellationToken ct = default)
    {
        await VisitPaymentAccess.RequireActiveUserAsync(_db, actorUserId, _timeProvider, ct);
        var (_, roleCodes) = await VisitPaymentAccess.LoadActorRolesAsync(_db, actorUserId, ct);
        if (!roleCodes.Any(role => AllowedSlipRoles.Contains(role)))
        {
            throw new ForbiddenException(SlipForbiddenMessage);
        }

        var parsed = VisitPaymentAccess.ParseVisitIdentifier(visitIdOrCode);
        var visit = await VisitPaymentAccess.FindVisitAsync(_db, parsed, ct)
            ?? throw new NotFoundException(VisitPaymentAccess.VisitNotFoundMessage);

        var hospitalIds = await VisitHospitalResolver.CollectAsync(_db, visit, ct);
        if (hospitalIds.Count == 0)
        {
            throw new NotFoundException(VisitPaymentAccess.VisitNotFoundMessage);
        }
        if (hospitalIds.Count > 1)
        {
            throw new ConflictException(VisitPaymentAccess.HospitalConflictMessage);
        }

        var hospitalId = hospitalIds.First();
        var isPatient = roleCodes.Contains(RoleNames.Patient);
        if (isPatient)
        {
            var owned = await VisitPaymentAccess.LoadOwnedPatientIdsAsync(_db, actorUserId, _timeProvider, ct);
            if (!visit.PatientId.HasValue || !owned.Contains(visit.PatientId.Value))
            {
                throw new NotFoundException(VisitPaymentAccess.VisitNotFoundMessage);
            }
        }
        else
        {
            var staff = await _db.Staff.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == actorUserId, ct);
            if (staff is null ||
                !string.Equals(staff.Status?.Trim(), "ACTIVE", StringComparison.OrdinalIgnoreCase) ||
                staff.HospitalId != hospitalId)
            {
                throw new ForbiddenException(SlipForbiddenMessage);
            }
        }

        var orders = await _db.DiagnosticOrders.AsNoTracking()
            .Where(o => o.VisitId == visit.Id && (o.Status == null || o.Status != "CANCELLED"))
            .OrderBy(o => o.SequenceNo ?? int.MaxValue)
            .ThenBy(o => o.OrderedAt)
            .ThenBy(o => o.Id)
            .ToListAsync(ct);

        var serviceIds = orders.Where(o => o.ServiceId.HasValue).Select(o => o.ServiceId!.Value).Distinct().ToList();
        var servicesById = serviceIds.Count == 0
            ? new Dictionary<long, Service>()
            : await _db.Services.AsNoTracking().Where(s => serviceIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);

        var pointIds = orders.Where(o => o.AssignedServicePointId.HasValue)
            .Select(o => o.AssignedServicePointId!.Value).Distinct().ToList();
        var pointsById = pointIds.Count == 0
            ? new Dictionary<long, QueueServicePoint>()
            : await _db.QueueServicePoints.AsNoTracking().Where(sp => pointIds.Contains(sp.Id)).ToDictionaryAsync(sp => sp.Id, ct);

        var roomIds = pointsById.Values.Where(sp => sp.RoomId.HasValue).Select(sp => sp.RoomId!.Value).Distinct().ToList();
        var roomsById = roomIds.Count == 0
            ? new Dictionary<long, HospitalRoom>()
            : await _db.HospitalRooms.AsNoTracking().Where(r => roomIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, ct);

        var queueIds = orders.Where(o => o.QueueEntryId.HasValue).Select(o => o.QueueEntryId!.Value).Distinct().ToList();
        var queuesById = queueIds.Count == 0
            ? new Dictionary<long, QueueEntry>()
            : await _db.QueueEntries.AsNoTracking().Where(q => queueIds.Contains(q.Id)).ToDictionaryAsync(q => q.Id, ct);

        var orderIds = orders.Select(o => o.Id).ToList();
        var declines = orderIds.Count == 0
            ? new List<DiagnosticOrderDecline>()
            : await _db.DiagnosticOrderDeclines.AsNoTracking()
                .Where(d => orderIds.Contains(d.DiagnosticOrderId))
                .OrderByDescending(d => d.CreatedAt)
                .ThenByDescending(d => d.Id)
                .ToListAsync(ct);
        var latestDeclineByOrderId = declines
            .GroupBy(d => d.DiagnosticOrderId)
            .ToDictionary(g => g.Key, g => g.First());

        Patient? patient = visit.PatientId.HasValue
            ? await _db.Patients.AsNoTracking().FirstOrDefaultAsync(p => p.Id == visit.PatientId.Value, ct)
            : null;

        string? doctorDisplay = null;
        if (visit.AssignedDoctorId.HasValue)
        {
            var doctor = await _db.Doctors.AsNoTracking()
                .Include(d => d.Staff)
                .FirstOrDefaultAsync(d => d.Id == visit.AssignedDoctorId.Value, ct);
            if (doctor is not null)
            {
                var title = string.IsNullOrWhiteSpace(doctor.ProfessionalTitle) ? "BS" : doctor.ProfessionalTitle.Trim();
                var name = doctor.Staff?.FullName ?? doctor.DoctorCode;
                doctorDisplay = $"{title} {name}".Trim();
            }
        }

        var items = orders.Select(order =>
        {
            servicesById.TryGetValue(order.ServiceId ?? 0, out var service);
            pointsById.TryGetValue(order.AssignedServicePointId ?? 0, out var point);
            roomsById.TryGetValue(point?.RoomId ?? 0, out var room);
            queuesById.TryGetValue(order.QueueEntryId ?? 0, out var queue);
            latestDeclineByOrderId.TryGetValue(order.Id, out var decline);
            var orderStatus = NormalizeStatusKey(order.Status);
            return new DiagnosticOrderAssignmentItemDto(
                OrderId: order.Id,
                OrderCode: order.OrderCode,
                SequenceNo: order.SequenceNo,
                ServiceId: service?.Id ?? order.ServiceId ?? 0,
                ServiceCode: service?.ServiceCode ?? string.Empty,
                ServiceName: service?.Name ?? string.Empty,
                DiagnosticGroup: DiagnosticRoutingRules.NormalizeGroup(service?.DiagnosticGroup),
                DiagnosticGroupLabel: GroupLabel(DiagnosticRoutingRules.NormalizeGroup(service?.DiagnosticGroup)),
                RequiresFasting: service?.RequiresFasting == true,
                RequiredForConclusion: order.RequiredForConclusion == true,
                Status: orderStatus,
                StatusLabel: OrderStatusLabel(orderStatus),
                SelectionStatus: order.SelectionStatus,
                AssignedServicePointId: point?.Id ?? order.AssignedServicePointId,
                AssignedServicePointName: point?.Name,
                AssignedServicePointActive: point?.IsActive == true,
                RoomId: room?.Id ?? point?.RoomId,
                RoomCode: room?.RoomCode,
                RoomName: room?.Name,
                RoomLocation: room?.Location,
                QueueEntryId: queue?.Id,
                TicketNo: queue is not null
                    ? MedFlow.Application.Services.Visits.VisitQueueEnrollmentService.FormatTicketNo(
                        (queue.QueueType ?? "DIAGNOSTIC").Trim().ToUpperInvariant(),
                        queue.QueueNumber)
                    : null,
                QueueNumber: queue?.QueueNumber,
                QueueStatus: queue is not null ? NormalizeStatusKey(queue.Status) : null,
                IsExecuted: orderStatus is "IN_PROGRESS" or "COMPLETED" or "PERFORMED_AWAITING_RESULT",
                IsDeclined: decline is not null,
                DeclineReason: decline?.Reason,
                DeclinedAt: decline?.CreatedAt);
        }).ToList();

        return new DiagnosticOrderAssignmentSlipDto(
            VisitId: visit.Id,
            VisitCode: string.IsNullOrWhiteSpace(visit.VisitCode) ? $"#{visit.Id}" : visit.VisitCode!.Trim(),
            PatientId: patient?.Id ?? visit.PatientId,
            PatientCode: patient?.PatientCode,
            PatientName: patient?.FullName,
            AssignedDoctorDisplay: doctorDisplay,
            GeneratedAtUtc: _timeProvider.GetUtcNow().UtcDateTime,
            Orders: items);
    }

    private async Task<long> ResolveOrderIdAsync(string orderIdOrCode, long hospitalId, CancellationToken ct)
    {
        var (numericId, orderCode) = MedFlow.Application.Services.Visits.QueueEntryRequestParsing.ParseIdentifier(orderIdOrCode ?? string.Empty);
        if (numericId.HasValue)
        {
            return numericId.Value;
        }

        if (string.IsNullOrWhiteSpace(orderCode))
        {
            throw new ValidationException("Mã phiếu chỉ định cận lâm sàng không hợp lệ.");
        }

        var normalizedOrderCode = orderCode.Trim().ToUpperInvariant();
        var candidates = await _db.DiagnosticOrders.AsNoTracking()
            .Where(o => o.OrderCode != null && o.OrderCode.Trim().ToUpper() == normalizedOrderCode)
            .OrderByDescending(o => o.Id)
            .Take(5)
            .ToListAsync(ct);
        foreach (var candidate in candidates)
        {
            if (!candidate.VisitId.HasValue)
            {
                continue;
            }

            var visit = await _db.Visits.AsNoTracking().FirstOrDefaultAsync(v => v.Id == candidate.VisitId.Value, ct);
            if (visit is null)
            {
                continue;
            }

            var hospitalIds = await VisitHospitalResolver.CollectAsync(_db, visit, ct);
            if (hospitalIds.Count == 1 && hospitalIds.First() == hospitalId)
            {
                return candidate.Id;
            }
        }

        throw new NotFoundException("DiagnosticOrder", orderCode);
    }

    private static QueueServicePoint ResolveTargetPoint(
        ReassignDiagnosticOrderRequest request,
        Service service,
        IReadOnlyList<QueueServicePoint> servicePoints,
        IReadOnlyDictionary<long, HospitalRoom> roomsById,
        IReadOnlyDictionary<long, Department> departmentsById,
        long hospitalId)
    {
        QueueServicePoint? target = null;
        if (request.TargetServicePointId.HasValue)
        {
            target = servicePoints.FirstOrDefault(sp => sp.Id == request.TargetServicePointId.Value);
            if (target is null)
            {
                throw new NotFoundException("QueueServicePoint", request.TargetServicePointId.Value);
            }

            if (request.TargetRoomId.HasValue && target.RoomId != request.TargetRoomId.Value)
            {
                throw new ValidationException("Phòng đích không khớp với điểm phục vụ cận lâm sàng đã chọn.");
            }
        }
        else if (request.TargetRoomId.HasValue)
        {
            target = servicePoints
                .Where(sp => sp.RoomId == request.TargetRoomId.Value)
                .Where(sp => DiagnosticServicePointResolver.IsOperational(sp, roomsById, departmentsById, hospitalId))
                .Where(sp => DiagnosticServicePointResolver.IsCompatibleForOrder(
                    sp, roomsById, service.Id, service, departmentsById, hospitalId))
                .OrderBy(sp => sp.ServiceId.HasValue && sp.ServiceId.Value == service.Id ? 0 : 1)
                .ThenBy(sp => sp.Id)
                .FirstOrDefault()
                ?? throw new ConflictException(
                    "[INCOMPATIBLE_DIAGNOSTIC_SERVICE_POINT] Phòng đích không có điểm phục vụ cận lâm sàng hoạt động và tương thích với dịch vụ.");
        }

        if (target is null ||
            !DiagnosticServicePointResolver.IsOperational(target, roomsById, departmentsById, hospitalId) ||
            !DiagnosticServicePointResolver.IsCompatibleForOrder(
                target, roomsById, service.Id, service, departmentsById, hospitalId))
        {
            throw new ConflictException(
                "[INCOMPATIBLE_DIAGNOSTIC_SERVICE_POINT] Điểm phục vụ cận lâm sàng đích không hoạt động hoặc không tương thích với dịch vụ của chỉ định.");
        }

        return target;
    }

    private static void EnsureOrderCanBeRerouted(DiagnosticOrder order)
    {
        var status = NormalizeStatusKey(order.Status);
        if (status is "CANCELLED" or "COMPLETED" or "PERFORMED_AWAITING_RESULT" or "IN_PROGRESS")
        {
            throw new ConflictException(InProgressMessage);
        }
    }

    private static void EnsureQueueEntryCanBeRerouted(QueueEntry queueEntry)
    {
        var status = NormalizeStatusKey(queueEntry.Status);
        if (status is "IN_PROGRESS" or "COMPLETED" or "CANCELLED")
        {
            throw new ConflictException(InProgressMessage);
        }

        if (queueEntry.StartedAt.HasValue && !queueEntry.CompletedAt.HasValue)
        {
            throw new ConflictException(InProgressMessage);
        }
    }

    private static string ValidateReason(string? reason)
    {
        var trimmed = reason?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.Length < 5)
        {
            throw new ValidationException(ReasonRequiredMessage);
        }

        if (trimmed.Length > 1000)
        {
            throw new ValidationException("Lý do điều phối lại không được vượt quá 1000 ký tự.");
        }

        return trimmed;
    }

    private static string NormalizeStatusKey(string? status) =>
        string.IsNullOrWhiteSpace(status) ? "UNKNOWN" : status.Trim().ToUpperInvariant();

    private static string? GroupLabel(string? group) => group switch
    {
        DiagnosticRoutingRules.GroupBloodTest => "Xét nghiệm máu",
        DiagnosticRoutingRules.GroupImaging => "Chẩn đoán hình ảnh",
        DiagnosticRoutingRules.GroupFunctional => "Thăm dò chức năng",
        _ => null,
    };

    private static string OrderStatusLabel(string status) => status switch
    {
        "ORDERED" => "Chờ xác nhận",
        "CONFIRMED" => "Đã xác nhận",
        "IN_PROGRESS" => "Đang thực hiện",
        "PERFORMED_AWAITING_RESULT" => "Đã thực hiện, chờ kết quả",
        "COMPLETED" => "Hoàn tất",
        "CANCELLED" => "Đã hủy",
        _ => status,
    };
}
