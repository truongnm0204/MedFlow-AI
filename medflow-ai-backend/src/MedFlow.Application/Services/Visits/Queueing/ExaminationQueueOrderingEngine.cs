using System.Globalization;

namespace MedFlow.Application.Services.Visits.Queueing;

public static class ExaminationQueueOrderingEngine
{
    // Note: Fairness counters (walkin_priority_ratio, queue_fairness_states) belong to #94 call-next, not #96.
    public static QueueOrderingResult Evaluate(
        IReadOnlyList<QueueOrderingItem> items,
        QueueOrderingPolicy policy,
        DateTime nowLocal)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(policy);

        var count = items.Count;
        if (count == 0)
        {
            return new QueueOrderingResult(Array.Empty<int>(), Array.Empty<QueueEntryEvaluation>());
        }

        var g = policy.GraceMinutes;
        var now = nowLocal;

        var effectiveArrivalClasses = new string[count];
        var activeWaitings = new bool[count];
        var isEligibles = new bool[count];
        var isAppointmentProtecteds = new bool[count];
        var isDueProtecteds = new bool[count];
        var roomIdle = true;

        for (var i = 0; i < count; i++)
        {
            var item = items[i];
            if (item.Status is "CALLED" or "IN_PROGRESS")
            {
                roomIdle = false;
            }

            var effectiveArrivalClass = QueueArrivalClassifier.Classify(
                item.IsScheduled,
                item.ScheduledAt,
                item.ArrivedAt,
                g,
                item.StoredArrivalClass);

            var activeWaiting = item.Status is "WAITING" or "READY";
            var isEligible = activeWaiting && item.PresenceStatus == "PRESENT";
            var isAppointmentProtected = item.IsScheduled
                && activeWaiting
                && item.ScheduledAt.HasValue
                && item.PresenceStatus != "LEFT"
                && effectiveArrivalClass != "LATE"
                && (item.PresenceStatus == "PRESENT" || now <= item.ScheduledAt.Value.AddMinutes(g));
            var isDueProtected = isAppointmentProtected
                && item.PresenceStatus == "PRESENT"
                && item.ScheduledAt!.Value <= now;

            effectiveArrivalClasses[i] = effectiveArrivalClass;
            activeWaitings[i] = activeWaiting;
            isEligibles[i] = isEligible;
            isAppointmentProtecteds[i] = isAppointmentProtected;
            isDueProtecteds[i] = isDueProtected;
        }

        var orderedIndices = Enumerable.Range(0, count)
            .OrderBy(i => GetStatusRank(items[i].Status))
            .ThenBy(i => GetTier(items[i], GetStatusRank(items[i].Status), effectiveArrivalClasses[i], isDueProtecteds[i], isEligibles[i]))
            .ThenBy(i =>
            {
                var statusRank = GetStatusRank(items[i].Status);
                var tier = GetTier(items[i], statusRank, effectiveArrivalClasses[i], isDueProtecteds[i], isEligibles[i]);
                return GetPrimaryTime(items[i], statusRank, tier);
            })
            .ThenBy(i => items[i].ReadySince ?? DateTime.MaxValue)
            .ThenBy(i => items[i].QueueNumber)
            .ThenBy(i => i)
            .ToArray();

        var servingOrders = new int[count];
        for (var pos = 0; pos < count; pos++)
        {
            servingOrders[orderedIndices[pos]] = pos + 1;
        }

        var evaluations = new QueueEntryEvaluation[count];
        for (var i = 0; i < count; i++)
        {
            var hasDueProtectedOther = false;
            DateTime? protectedNextStartsAt = null;

            for (var j = 0; j < count; j++)
            {
                if (j == i)
                {
                    continue;
                }

                if (isDueProtecteds[j])
                {
                    hasDueProtectedOther = true;
                }

                if (isAppointmentProtecteds[j] && items[j].ScheduledAt.HasValue)
                {
                    var candidate = items[j].ScheduledAt!.Value;
                    if (!protectedNextStartsAt.HasValue || candidate < protectedNextStartsAt.Value)
                    {
                        protectedNextStartsAt = candidate;
                    }
                }
            }

            var (canSafeBackfill, holdReason) = EvaluateSafeBackfill(
                items[i],
                activeWaitings[i],
                isDueProtecteds[i],
                hasDueProtectedOther,
                roomIdle,
                protectedNextStartsAt,
                policy,
                now);

            evaluations[i] = new QueueEntryEvaluation(
                ServingOrder: servingOrders[i],
                EffectiveArrivalClass: effectiveArrivalClasses[i],
                IsEligible: isEligibles[i],
                IsAppointmentProtected: isAppointmentProtecteds[i],
                IsDueProtected: isDueProtecteds[i],
                CanSafeBackfill: canSafeBackfill,
                HoldReason: holdReason,
                HasDueProtectedAppointment: hasDueProtectedOther,
                ProtectedNextStartsAt: protectedNextStartsAt);
        }

        return new QueueOrderingResult(orderedIndices, evaluations);
    }

    private static (bool CanSafeBackfill, string? HoldReason) EvaluateSafeBackfill(
        QueueOrderingItem item,
        bool activeWaiting,
        bool isDueProtectedSelf,
        bool hasDueProtectedAppointment,
        bool roomIdle,
        DateTime? protectedNextStartsAt,
        QueueOrderingPolicy policy,
        DateTime now)
    {
        if (!activeWaiting)
        {
            return (false, null);
        }

        if (item.PresenceStatus != "PRESENT")
        {
            return (false, "Bệnh nhân phải ở trạng thái Có mặt trước khi Safe Backfill.");
        }

        if (isDueProtectedSelf)
        {
            return (false, "Lượt khám đang được bảo vệ theo giờ hẹn, không cần Safe Backfill.");
        }

        if (hasDueProtectedAppointment)
        {
            return (false, "Đang có bệnh nhân đúng lịch đã đến giờ hẹn cần được phục vụ trước.");
        }

        if (!roomIdle)
        {
            return (false, "Phòng khám đang có bệnh nhân được gọi hoặc đang khám.");
        }

        if (!protectedNextStartsAt.HasValue)
        {
            return (true, null);
        }

        if (!policy.ExpectedDurationMinutes.HasValue || !policy.SafetyBufferMinutes.HasValue)
        {
            return (false, "Chưa xác định được thời lượng khám dự kiến hoặc thời gian dự phòng; giữ khoảng trống cho lịch hẹn được bảo vệ.");
        }

        var d = policy.ExpectedDurationMinutes.Value;
        var b = policy.SafetyBufferMinutes.Value;
        if (now.AddMinutes(d + b) > protectedNextStartsAt.Value)
        {
            return (
                false,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Không đủ khoảng trống an toàn trước lịch hẹn được bảo vệ lúc {protectedNextStartsAt.Value:HH:mm} (cần {d} phút khám + {b} phút dự phòng)."));
        }

        return (true, null);
    }

    private static int GetStatusRank(string status) => status switch
    {
        "IN_PROGRESS" => 0,
        "CALLED" => 1,
        "WAITING" or "READY" => 2,
        "SKIPPED" => 3,
        "COMPLETED" => 4,
        _ => 5,
    };

    private static int GetTier(
        QueueOrderingItem item,
        int statusRank,
        string effectiveArrivalClass,
        bool isDueProtected,
        bool isEligible)
    {
        if (statusRank != 2)
        {
            return 0;
        }

        if (isEligible && ClinicalQueueRules.IsElevated(item.Priority))
        {
            return 0;
        }

        if (isDueProtected || (isEligible && item.IsScheduled && effectiveArrivalClass == "ON_TIME_OR_GRACE"))
        {
            return 1;
        }

        if (isEligible && item.Status == "READY" && item.BackfilledAt.HasValue)
        {
            return 2;
        }

        if (isEligible)
        {
            return 3;
        }

        return 4;
    }

    private static DateTime GetPrimaryTime(QueueOrderingItem item, int statusRank, int tier)
    {
        if (statusRank != 2)
        {
            return item.ReadySince ?? DateTime.MaxValue;
        }

        return tier switch
        {
            0 => item.ReadySince ?? item.ArrivedAt ?? DateTime.MaxValue,
            1 => item.ScheduledAt ?? item.ReadySince ?? DateTime.MaxValue,
            2 => item.BackfilledAt ?? item.ReadySince ?? DateTime.MaxValue,
            3 => item.ReadySince ?? item.ArrivedAt ?? DateTime.MaxValue,
            _ => item.ScheduledAt ?? item.ReadySince ?? DateTime.MaxValue,
        };
    }
}
