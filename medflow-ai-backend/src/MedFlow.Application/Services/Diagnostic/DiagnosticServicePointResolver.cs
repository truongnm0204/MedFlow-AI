using MedFlow.Domain.Entities;
using MedFlow.Domain.Exceptions;

namespace MedFlow.Application.Services.Diagnostic;

public static class DiagnosticServicePointResolver
{
    public const string IncompatibleServicePointCode = "INCOMPATIBLE_DIAGNOSTIC_SERVICE_POINT";
    public const string NoActiveCompatiblePointCode = "NO_ACTIVE_COMPATIBLE_DIAGNOSTIC_POINT";

    /// <summary>
    /// Fail-closed operational check for a diagnostic queue service point:
    /// - Point exists, <c>IsActive == true</c>, <c>PointType == "DIAGNOSTIC"</c>, <c>HospitalId == hospitalId</c>
    /// - Linked room exists (<c>RoomId != null</c>), <c>HospitalId == hospitalId</c>, <c>RoomType == "DIAGNOSTIC"</c>, <c>Status == "ACTIVE"</c>
    /// - Linked room has a real <c>DepartmentId</c> whose <see cref="Department"/> is active and belongs to <paramref name="hospitalId"/>.
    /// Never treats <c>null</c> as a wildcard.
    /// </summary>
    public static bool IsOperational(
        QueueServicePoint? sp,
        IReadOnlyDictionary<long, HospitalRoom> roomsById,
        IReadOnlyDictionary<long, Department>? departmentsById,
        long hospitalId)
    {
        if (sp is null || sp.IsActive != true)
        {
            return false;
        }

        if (!string.Equals(sp.PointType?.Trim(), "DIAGNOSTIC", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!sp.HospitalId.HasValue || sp.HospitalId.Value != hospitalId)
        {
            return false;
        }

        if (!sp.RoomId.HasValue || !roomsById.TryGetValue(sp.RoomId.Value, out var room))
        {
            return false;
        }

        if (!room.HospitalId.HasValue || room.HospitalId.Value != hospitalId)
        {
            return false;
        }

        if (!string.Equals(room.RoomType?.Trim(), "DIAGNOSTIC", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(room.Status?.Trim(), "ACTIVE", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!room.DepartmentId.HasValue ||
            departmentsById is null ||
            !departmentsById.TryGetValue(room.DepartmentId.Value, out var roomDept))
        {
            return false;
        }

        return roomDept.IsActive == true && roomDept.HospitalId == hospitalId;
    }

    /// <summary>
    /// Fail-closed department compatibility check: both the room and the diagnostic service MUST have
    /// non-null, matching <c>DepartmentId</c> belonging to an active <see cref="Department"/> in <paramref name="hospitalId"/>.
    /// </summary>
    public static bool IsDepartmentCompatible(
        HospitalRoom? spRoom,
        Service? orderService,
        IReadOnlyDictionary<long, Department>? departmentsById,
        long hospitalId)
    {
        if (spRoom?.DepartmentId is not long roomDeptId ||
            orderService?.DepartmentId is not long svcDeptId)
        {
            return false;
        }

        if (roomDeptId != svcDeptId)
        {
            return false;
        }

        if (departmentsById is null || !departmentsById.TryGetValue(svcDeptId, out var dept))
        {
            return false;
        }

        return dept.IsActive == true && dept.HospitalId == hospitalId;
    }

    /// <summary>
    /// Fail-closed clinical/catalog compatibility check between a diagnostic service point and a diagnostic order's service:
    /// - <paramref name="orderService"/> exists, <c>IsActive == true</c>, <c>ServiceType == "DIAGNOSTIC"</c>, matches <paramref name="orderServiceId"/>
    /// - Service belongs to <paramref name="hospitalId"/> (when <c>Service.HospitalId</c> is populated it must equal <paramref name="hospitalId"/>,
    ///   and its <see cref="Department"/> must always exist, be active, and have <c>Department.HospitalId == hospitalId</c>)
    /// - Room and Service belong to the exact same active department (<see cref="IsDepartmentCompatible"/>)
    /// - If <c>sp.ServiceId.HasValue</c>, it must equal <paramref name="orderServiceId"/>; if <c>!sp.ServiceId.HasValue</c> (shared point),
    ///   it is compatible iff <see cref="IsDepartmentCompatible"/> holds.
    /// </summary>
    public static bool IsCompatibleForOrder(
        QueueServicePoint? sp,
        IReadOnlyDictionary<long, HospitalRoom> roomsById,
        long? orderServiceId,
        Service? orderService,
        IReadOnlyDictionary<long, Department>? departmentsById,
        long hospitalId)
    {
        if (sp is null || orderService is null || !orderServiceId.HasValue)
        {
            return false;
        }

        if (orderService.Id != orderServiceId.Value || orderService.IsActive != true)
        {
            return false;
        }

        if (!string.Equals(orderService.ServiceType?.Trim(), "DIAGNOSTIC", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (orderService.HospitalId.HasValue && orderService.HospitalId.Value != hospitalId)
        {
            return false;
        }

        if (!sp.RoomId.HasValue || !roomsById.TryGetValue(sp.RoomId.Value, out var spRoom))
        {
            return false;
        }

        if (!IsDepartmentCompatible(spRoom, orderService, departmentsById, hospitalId))
        {
            return false;
        }

        if (sp.ServiceId.HasValue)
        {
            return sp.ServiceId.Value == orderServiceId.Value;
        }

        return true;
    }

    /// <summary>
    /// Resolves a strictly operational and compatible <c>DIAGNOSTIC</c> <see cref="QueueServicePoint"/>
    /// for <paramref name="order"/> without ever falling back to a <see cref="QueueServicePoint"/>
    /// bound to another service or another department, or silently ignoring an explicit point/room request.
    /// </summary>
    public static long ResolveForExecutionOrThrow(
        DiagnosticOrder order,
        Service? orderService,
        long? requestedSpId,
        IReadOnlyList<QueueServicePoint> hospitalDiagnosticServicePoints,
        IReadOnlyDictionary<long, HospitalRoom> roomsById,
        IReadOnlyList<QueueEntry>? visitQueues,
        long hospitalId,
        IReadOnlyDictionary<long, Department>? departmentsById = null,
        long? requestedRoomId = null)
    {
        var orderLabel = !string.IsNullOrWhiteSpace(order.OrderCode)
            ? order.OrderCode.Trim()
            : $"#{order.Id}";
        var serviceLabel = orderService is not null
            ? $"'{orderService.Name}' ({orderService.ServiceCode})"
            : $"service_id={order.ServiceId?.ToString() ?? "null"}";

        bool IsValidCandidate(QueueServicePoint? candidate) =>
            IsOperational(candidate, roomsById, departmentsById, hospitalId) &&
            IsCompatibleForOrder(candidate, roomsById, order.ServiceId, orderService, departmentsById, hospitalId);

        // 1. Explicit ServicePointId and/or RoomId requested -> validate strictly, NEVER silently fall back!
        if (requestedSpId.HasValue || requestedRoomId.HasValue)
        {
            if (requestedSpId.HasValue)
            {
                var explicitSp = hospitalDiagnosticServicePoints
                    .FirstOrDefault(sp => sp.Id == requestedSpId.Value && sp.HospitalId == hospitalId);

                if (explicitSp is null ||
                    (requestedRoomId.HasValue && explicitSp.RoomId != requestedRoomId.Value) ||
                    !IsValidCandidate(explicitSp))
                {
                    throw new ConflictException(
                        $"[{IncompatibleServicePointCode}] Điểm phục vụ cận lâm sàng được chọn (#{requestedSpId.Value}) không hoạt động hoặc không tương thích với chỉ định '{orderLabel}' ({serviceLabel}).");
                }

                return explicitSp.Id;
            }

            // Explicit RoomId without ServicePointId: find operational & compatible point inside that exact room only.
            var explicitRoomSp = hospitalDiagnosticServicePoints
                .Where(sp => sp.HospitalId == hospitalId && sp.RoomId == requestedRoomId!.Value)
                .Where(sp => order.ServiceId.HasValue && sp.ServiceId == order.ServiceId.Value)
                .OrderBy(sp => sp.Id)
                .FirstOrDefault(IsValidCandidate)
                ?? hospitalDiagnosticServicePoints
                    .Where(sp => sp.HospitalId == hospitalId && sp.RoomId == requestedRoomId!.Value && !sp.ServiceId.HasValue)
                    .OrderBy(sp => sp.Id)
                    .FirstOrDefault(IsValidCandidate);

            if (explicitRoomSp is null)
            {
                throw new ConflictException(
                    $"[{IncompatibleServicePointCode}] Phòng thực hiện cận lâm sàng được chọn (#{requestedRoomId!.Value}) không hoạt động hoặc không có điểm phục vụ tương thích với chỉ định '{orderLabel}' ({serviceLabel}).");
            }

            return explicitRoomSp.Id;
        }

        // 2. Điểm đã được phân công khi tạo chỉ định (#869) — chỉ dùng nếu vẫn hoạt động & tương thích.
        //    Điểm đóng/bảo trì/không còn tương thích sẽ bị bỏ qua và chuyển sang bước chọn thay thế.
        if (order.AssignedServicePointId.HasValue)
        {
            var assignedSp = hospitalDiagnosticServicePoints
                .FirstOrDefault(sp => sp.Id == order.AssignedServicePointId.Value && sp.HospitalId == hospitalId);
            if (IsValidCandidate(assignedSp))
            {
                return assignedSp!.Id;
            }
        }

        // 3. Reuse existing linked queue entry's service point ONLY if it is still operational AND compatible.
        if (order.QueueEntryId.HasValue && visitQueues is not null)
        {
            var priorQe = visitQueues.FirstOrDefault(q => q.Id == order.QueueEntryId.Value);
            var priorSpId = priorQe?.CurrentServicePointId ?? priorQe?.ServicePointId;
            if (priorSpId.HasValue)
            {
                var priorSp = hospitalDiagnosticServicePoints
                    .FirstOrDefault(sp => sp.Id == priorSpId.Value && sp.HospitalId == hospitalId);
                if (IsValidCandidate(priorSp))
                {
                    return priorSp!.Id;
                }
            }
        }

        // 3. Exact ServiceId match (operational + compatible)
        if (order.ServiceId.HasValue)
        {
            var exactMatch = hospitalDiagnosticServicePoints
                .Where(sp => sp.ServiceId.HasValue && sp.ServiceId.Value == order.ServiceId.Value)
                .OrderBy(sp => sp.Id)
                .FirstOrDefault(IsValidCandidate);
            if (exactMatch is not null)
            {
                return exactMatch.Id;
            }
        }

        // 4. Shared compatible diagnostic point (ServiceId == null, operational + same active department)
        var sharedMatch = hospitalDiagnosticServicePoints
            .Where(sp => !sp.ServiceId.HasValue)
            .OrderBy(sp => sp.Id)
            .FirstOrDefault(IsValidCandidate);
        if (sharedMatch is not null)
        {
            return sharedMatch.Id;
        }

        throw new ConflictException(
            $"[{NoActiveCompatiblePointCode}] Hiện không có phòng hoặc điểm phục vụ cận lâm sàng tương thích đang hoạt động cho chỉ định '{orderLabel}' ({serviceLabel}). Vui lòng kiểm tra trạng thái phòng thực hiện trước khi điều phối.");
    }

    /// <summary>
    /// Chọn điểm phục vụ CLS cho một chỉ định mới dựa trên TẢI (số người đang chờ tại điểm, D3),
    /// sau khi đã loại các điểm không hoạt động/không tương thích chuyên môn.
    /// Trả <c>null</c> khi không còn điểm hợp lệ (caller giữ order ORDERED và cảnh báo nhân viên),
    /// tuyệt đối không ném lỗi để không chặn bác sĩ tạo chỉ định.
    /// </summary>
    public static QueueServicePoint? ResolveForAssignment(
        Service? orderService,
        IReadOnlyList<QueueServicePoint> hospitalDiagnosticServicePoints,
        IReadOnlyDictionary<long, HospitalRoom> roomsById,
        IReadOnlyDictionary<long, Department>? departmentsById,
        long hospitalId,
        IReadOnlyDictionary<long, int> waitingCountsByPointId)
    {
        if (orderService is null ||
            !string.Equals(orderService.ServiceType?.Trim(), "DIAGNOSTIC", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return hospitalDiagnosticServicePoints
            .Where(sp =>
                IsOperational(sp, roomsById, departmentsById, hospitalId) &&
                IsCompatibleForOrder(sp, roomsById, orderService.Id, orderService, departmentsById, hospitalId))
            .OrderBy(sp => waitingCountsByPointId.TryGetValue(sp.Id, out var waiting) ? waiting : 0)
            .ThenBy(sp => sp.ServiceId.HasValue && sp.ServiceId.Value == orderService.Id ? 0 : 1)
            .ThenBy(sp => sp.Id)
            .FirstOrDefault();
    }

    /// <summary>
    /// Read-only catalog/display helper: resolves a compatible diagnostic service point for UI display only.
    /// Never selects a point bound to another service or an incompatible department; never used as an execution gate.
    /// </summary>
    public static QueueServicePoint? TryResolveCompatibleForCatalog(
        long serviceId,
        Service? orderService,
        IReadOnlyList<QueueServicePoint> hospitalDiagnosticServicePoints,
        IReadOnlyDictionary<long, HospitalRoom>? roomsById = null,
        IReadOnlyDictionary<long, Department>? departmentsById = null,
        long? hospitalId = null)
    {
        if (hospitalDiagnosticServicePoints.Count == 0)
        {
            return null;
        }

        bool IsRoomAndDeptCompatibleForDisplay(QueueServicePoint sp)
        {
            if (hospitalId.HasValue && (!sp.HospitalId.HasValue || sp.HospitalId.Value != hospitalId.Value))
            {
                return false;
            }

            if (roomsById is null)
            {
                return sp.RoomId.HasValue;
            }

            if (!sp.RoomId.HasValue || !roomsById.TryGetValue(sp.RoomId.Value, out var room))
            {
                return false;
            }

            if (hospitalId.HasValue && (!room.HospitalId.HasValue || room.HospitalId.Value != hospitalId.Value))
            {
                return false;
            }

            if (!string.Equals(room.RoomType?.Trim(), "DIAGNOSTIC", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (orderService is not null)
            {
                if (!room.DepartmentId.HasValue || !orderService.DepartmentId.HasValue ||
                    room.DepartmentId.Value != orderService.DepartmentId.Value)
                {
                    return false;
                }

                if (departmentsById is not null &&
                    (!departmentsById.TryGetValue(orderService.DepartmentId.Value, out var dept) ||
                     dept.IsActive != true ||
                     (hospitalId.HasValue && dept.HospitalId != hospitalId.Value)))
                {
                    return false;
                }
            }

            return true;
        }

        bool IsActiveRoomAndPoint(QueueServicePoint sp)
        {
            if (sp.IsActive != true)
            {
                return false;
            }

            if (roomsById is not null && sp.RoomId.HasValue && roomsById.TryGetValue(sp.RoomId.Value, out var room))
            {
                return string.Equals(room.Status?.Trim(), "ACTIVE", StringComparison.OrdinalIgnoreCase);
            }

            return true;
        }

        var exactActive = hospitalDiagnosticServicePoints
            .Where(sp => sp.ServiceId.HasValue && sp.ServiceId.Value == serviceId && IsRoomAndDeptCompatibleForDisplay(sp) && IsActiveRoomAndPoint(sp))
            .OrderBy(sp => sp.Id)
            .FirstOrDefault();
        if (exactActive is not null)
        {
            return exactActive;
        }

        if (orderService?.DepartmentId.HasValue == true && roomsById is not null)
        {
            var sharedActive = hospitalDiagnosticServicePoints
                .Where(sp => !sp.ServiceId.HasValue && IsRoomAndDeptCompatibleForDisplay(sp) && IsActiveRoomAndPoint(sp))
                .OrderBy(sp => sp.Id)
                .FirstOrDefault();
            if (sharedActive is not null)
            {
                return sharedActive;
            }
        }

        // Read-only catalog display may still show an inactive exact-service point so staff see its status,
        // but NEVER selects another service's point or an unverified shared point.
        return hospitalDiagnosticServicePoints
            .Where(sp => sp.ServiceId.HasValue && sp.ServiceId.Value == serviceId && IsRoomAndDeptCompatibleForDisplay(sp))
            .OrderBy(sp => sp.Id)
            .FirstOrDefault();
    }
}
