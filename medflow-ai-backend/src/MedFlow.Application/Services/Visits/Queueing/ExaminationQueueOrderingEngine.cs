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
        var awaitingResults = new bool[count];
        var isEligibles = new bool[count];
        var isAppointmentProtecteds = new bool[count];
        var isDueProtecteds = new bool[count];
        var ineligibleReasons = new string?[count];
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

            // A HOLD_FOR_SLOT block only lasts until the appointment time; once due, protection is restored.
            var holdExpired = item.IsScheduled && item.ScheduledAt.HasValue && item.ScheduledAt.Value <= now;
            var isBlocked = !string.IsNullOrWhiteSpace(item.BlockedReason) && !holdExpired;
            var isAwaitingResults = item.QueueType == ClinicalQueueRules.QueueTypeReturnConsultation
                && policy.FinalResultRequired
                && item.PendingRequiredResults;

            var isEligible = activeWaiting
                && item.PresenceStatus == "PRESENT"
                && !isBlocked
                && !isAwaitingResults;
            var isAppointmentProtected = item.IsScheduled
                && activeWaiting
                && item.ScheduledAt.HasValue
                && item.PresenceStatus != "LEFT"
                && effectiveArrivalClass != "LATE"
                && (item.PresenceStatus == "PRESENT" || now <= item.ScheduledAt.Value.AddMinutes(g));
            var isDueProtected = isAppointmentProtected
                && isEligible
                && item.ScheduledAt!.Value <= now;

            effectiveArrivalClasses[i] = effectiveArrivalClass;
            activeWaitings[i] = activeWaiting;
            awaitingResults[i] = isAwaitingResults;
            isEligibles[i] = isEligible;
            isAppointmentProtecteds[i] = isAppointmentProtected;
            isDueProtecteds[i] = isDueProtected;
            ineligibleReasons[i] = activeWaiting && !isEligible
                ? ResolveIneligibleReason(item, isBlocked, isAwaitingResults)
                : null;
        }

        var hasDueProtectedOthers = new bool[count];
        var protectedNextStartsAts = new DateTime?[count];
        var statusRanks = new int[count];
        var tiers = new int[count];
        var primaryTimes = new DateTime[count];

        for (var i = 0; i < count; i++)
        {
            var item = items[i];
            var hasDueProtectedOther = false;
            DateTime? protectedNextStartsAt = null;
            DateTime? upcomingProtectedOther = null;

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

                if (!isAppointmentProtecteds[j] || !items[j].ScheduledAt.HasValue)
                {
                    continue;
                }

                var candidate = items[j].ScheduledAt!.Value;
                if (!protectedNextStartsAt.HasValue || candidate < protectedNextStartsAt.Value)
                {
                    protectedNextStartsAt = candidate;
                }

                if (!isDueProtecteds[j] && (!upcomingProtectedOther.HasValue || candidate < upcomingProtectedOther.Value))
                {
                    upcomingProtectedOther = candidate;
                }
            }

            var statusRank = GetStatusRank(item.Status);

            // Due appointments always sit in tier 0 above the fast track, so the gap gate only
            // protects the next appointment that is not yet due.
            var canFastTrack = statusRank == 2
                && isEligibles[i]
                && !isDueProtecteds[i]
                && (ClinicalQueueRules.IsElevated(item.Priority)
                    || item.QueueType == ClinicalQueueRules.QueueTypeReturnConsultation)
                && HasSafeGap(upcomingProtectedOther, policy, now);

            var tier = GetTier(item, statusRank, effectiveArrivalClasses[i], isDueProtecteds[i], canFastTrack, isEligibles[i]);

            hasDueProtectedOthers[i] = hasDueProtectedOther;
            protectedNextStartsAts[i] = protectedNextStartsAt;
            statusRanks[i] = statusRank;
            tiers[i] = tier;
            primaryTimes[i] = GetPrimaryTime(item, statusRank, tier);
        }

        var orderedIndices = Enumerable.Range(0, count)
            .OrderBy(i => statusRanks[i])
            .ThenBy(i => tiers[i])
            .ThenBy(i => primaryTimes[i])
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
            var (canSafeBackfill, holdReason) = EvaluateSafeBackfill(
                items[i],
                activeWaitings[i],
                awaitingResults[i],
                isDueProtecteds[i],
                hasDueProtectedOthers[i],
                roomIdle,
                protectedNextStartsAts[i],
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
                HasDueProtectedAppointment: hasDueProtectedOthers[i],
                ProtectedNextStartsAt: protectedNextStartsAts[i],
                IneligibleReason: ineligibleReasons[i]);
        }

        return new QueueOrderingResult(orderedIndices, evaluations);
    }

    private static string? ResolveIneligibleReason(QueueOrderingItem item, bool isBlocked, bool isAwaitingResults)
    {
        if (item.PresenceStatus == "LEFT")
        {
            return QueueIneligibleReasons.Left;
        }

        if (item.PresenceStatus != "PRESENT")
        {
            return QueueIneligibleReasons.NotArrived;
        }

        if (isBlocked)
        {
            return QueueIneligibleReasons.Blocked;
        }

        return isAwaitingResults ? QueueIneligibleReasons.AwaitingResults : null;
    }

    private static bool HasSafeGap(DateTime? nextProtectedStartsAt, QueueOrderingPolicy policy, DateTime now)
    {
        if (!nextProtectedStartsAt.HasValue)
        {
            return true;
        }

        if (!policy.ExpectedDurationMinutes.HasValue || !policy.SafetyBufferMinutes.HasValue)
        {
            return false;
        }

        return now.AddMinutes(policy.ExpectedDurationMinutes.Value + policy.SafetyBufferMinutes.Value)
            <= nextProtectedStartsAt.Value;
    }

    private static (bool CanSafeBackfill, string? HoldReason) EvaluateSafeBackfill(
        QueueOrderingItem item,
        bool activeWaiting,
        bool awaitingResults,
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

        if (awaitingResults)
        {
            return (false, "Lượt khám lại đang chờ đủ kết quả cận lâm sàng bắt buộc.");
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
        bool canFastTrack,
        bool isEligible)
    {
        if (statusRank != 2)
        {
            return 0;
        }

        if (isDueProtected)
        {
            return 0;
        }

        if (canFastTrack)
        {
            return 1;
        }

        if (isEligible && item.IsScheduled && effectiveArrivalClass == "ON_TIME_OR_GRACE")
        {
            return 2;
        }

        if (isEligible && item.Status == "READY" && item.BackfilledAt.HasValue)
        {
            return 3;
        }

        if (isEligible)
        {
            return 4;
        }

        return 5;
    }

    private static DateTime GetPrimaryTime(QueueOrderingItem item, int statusRank, int tier)
    {
        if (statusRank != 2)
        {
            return item.ReadySince ?? DateTime.MaxValue;
        }

        return tier switch
        {
            0 => item.ScheduledAt ?? item.ReadySince ?? DateTime.MaxValue,
            1 => item.ReadySince ?? item.ArrivedAt ?? DateTime.MaxValue,
            2 => item.ScheduledAt ?? item.ReadySince ?? DateTime.MaxValue,
            3 => item.BackfilledAt ?? item.ReadySince ?? DateTime.MaxValue,
            4 => item.ReadySince ?? item.ArrivedAt ?? DateTime.MaxValue,
            _ => item.ScheduledAt ?? item.ReadySince ?? DateTime.MaxValue,
        };
    }
}
