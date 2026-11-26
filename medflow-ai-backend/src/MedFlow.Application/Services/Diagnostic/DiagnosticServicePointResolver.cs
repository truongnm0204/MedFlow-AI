using MedFlow.Domain.Entities;
using MedFlow.Domain.Exceptions;

namespace MedFlow.Application.Services.Diagnostic;

public static class DiagnosticServicePointResolver
{
    public const string IncompatiblePointCode = "[INCOMPATIBLE_DIAGNOSTIC_SERVICE_POINT]";
    public const string NoActiveCompatiblePointMessage =
        "[NO_ACTIVE_COMPATIBLE_DIAGNOSTIC_POINT] Không có phòng/điểm thực hiện cận lâm sàng tương thích đang hoạt động cho dịch vụ này. Vui lòng điều phối lại hoặc tạm hoãn sang thời điểm khác.";

    public static bool IsOperational(
        QueueServicePoint sp,
        IReadOnlyDictionary<long, HospitalRoom> roomsById)
    {
        if (sp.IsActive == false ||
            !string.Equals(sp.PointType?.Trim(), "DIAGNOSTIC", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!sp.RoomId.HasValue)
        {
            return true;
        }

        return roomsById.TryGetValue(sp.RoomId.Value, out var room) &&
               string.Equals(room.Status?.Trim(), "ACTIVE", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsDepartmentCompatible(HospitalRoom? spRoom, Service? orderService)
    {
        if (spRoom is null)
        {
            return false;
        }

        if (spRoom.DepartmentId.HasValue && orderService?.DepartmentId.HasValue == true)
        {
            return spRoom.DepartmentId.Value == orderService.DepartmentId.Value;
        }

        return true;
    }

    public static bool IsCompatibleForOrder(
        QueueServicePoint sp,
        IReadOnlyDictionary<long, HospitalRoom> roomsById,
        long? orderServiceId,
        Service? orderService)
    {
        if (sp.ServiceId.HasValue)
        {
            return orderServiceId.HasValue && sp.ServiceId.Value == orderServiceId.Value;
        }

        if (!sp.RoomId.HasValue || !roomsById.TryGetValue(sp.RoomId.Value, out var spRoom))
        {
            return false;
        }

        return IsDepartmentCompatible(spRoom, orderService);
    }

    public static long ResolveForExecutionOrThrow(
        DiagnosticOrder order,
        Service? orderService,
        long? requestedSpId,
        IReadOnlyList<QueueEntry> visitQueues,
        IReadOnlyList<QueueServicePoint> hospitalServicePoints,
        IReadOnlyDictionary<long, HospitalRoom> roomsById)
    {
        ArgumentNullException.ThrowIfNull(order);

        var operationalDiagPoints = hospitalServicePoints
            .Where(sp => IsOperational(sp, roomsById))
            .OrderBy(sp => sp.Id)
            .ToList();

        // (a) Explicit requestedSpId: must be operational AND compatible
        if (requestedSpId.HasValue)
        {
            var candidate = hospitalServicePoints.FirstOrDefault(sp => sp.Id == requestedSpId.Value);
            if (candidate is null || !IsOperational(candidate, roomsById))
            {
                throw new ConflictException(
                    $"{IncompatiblePointCode} Điểm phục vụ cận lâm sàng được chọn không hợp lệ hoặc đang ngừng hoạt động/bảo trì.");
            }

            if (!IsCompatibleForOrder(candidate, roomsById, order.ServiceId, orderService))
            {
                throw new ConflictException(
                    $"{IncompatiblePointCode} Điểm phục vụ '{candidate.Name}' không tương thích với dịch vụ cận lâm sàng của phiếu chỉ định.");
            }

            return candidate.Id;
        }

        // (b) Prior order.QueueEntryId's SP: reuse ONLY if operational AND compatible
        if (order.QueueEntryId.HasValue)
        {
            var prevQ = visitQueues.FirstOrDefault(q => q.Id == order.QueueEntryId.Value);
            var prevSpId = prevQ?.CurrentServicePointId ?? prevQ?.ServicePointId;
            if (prevSpId.HasValue)
            {
                var prevSp = operationalDiagPoints.FirstOrDefault(sp => sp.Id == prevSpId.Value);
                if (prevSp is not null && IsCompatibleForOrder(prevSp, roomsById, order.ServiceId, orderService))
                {
                    return prevSp.Id;
                }
            }
        }

        // (c) Exact service match
        if (order.ServiceId.HasValue)
        {
            var exactMatch = operationalDiagPoints.FirstOrDefault(sp => sp.ServiceId == order.ServiceId.Value);
            if (exactMatch is not null)
            {
                return exactMatch.Id;
            }
        }

        // (d) Shared multi-service point (!sp.ServiceId.HasValue) in compatible active room/department
        var sharedMatch = operationalDiagPoints.FirstOrDefault(sp =>
            !sp.ServiceId.HasValue &&
            sp.RoomId.HasValue &&
            roomsById.TryGetValue(sp.RoomId.Value, out var spRoom) &&
            IsDepartmentCompatible(spRoom, orderService));
        if (sharedMatch is not null)
        {
            return sharedMatch.Id;
        }

        // (e) Never fall back across incompatible services or departments!
        throw new ConflictException(NoActiveCompatiblePointMessage);
    }

    public static QueueServicePoint? TryResolveCompatibleForCatalog(
        long serviceId,
        Service? service,
        IReadOnlyList<QueueServicePoint> hospitalDiagPoints,
        IReadOnlyDictionary<long, HospitalRoom>? roomsById = null)
    {
        var specific = hospitalDiagPoints
            .Where(sp => sp.ServiceId == serviceId)
            .OrderBy(sp =>
            {
                if (sp.IsActive == false)
                    return 2;
                if (roomsById is not null && sp.RoomId.HasValue &&
                    roomsById.TryGetValue(sp.RoomId.Value, out var r) &&
                    !string.Equals(r.Status?.Trim(), "ACTIVE", StringComparison.OrdinalIgnoreCase))
                {
                    return 1;
                }

                return 0;
            })
            .ThenBy(sp => sp.Id)
            .FirstOrDefault();

        if (specific is not null)
        {
            return specific;
        }

        return hospitalDiagPoints
            .Where(sp =>
            {
                if (sp.IsActive == false || sp.ServiceId.HasValue)
                {
                    return false;
                }

                if (roomsById is null || roomsById.Count == 0 || !sp.RoomId.HasValue)
                {
                    return true;
                }

                return roomsById.TryGetValue(sp.RoomId.Value, out var rm) &&
                       string.Equals(rm.Status?.Trim(), "ACTIVE", StringComparison.OrdinalIgnoreCase) &&
                       IsDepartmentCompatible(rm, service);
            })
            .OrderBy(sp => sp.Id)
            .FirstOrDefault();
    }
}
