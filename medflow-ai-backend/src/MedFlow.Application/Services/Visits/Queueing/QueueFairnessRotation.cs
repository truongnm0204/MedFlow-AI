namespace MedFlow.Application.Services.Visits.Queueing;

public static class QueueFairnessRotation
{
    public static bool PreferPriority(int counter, int priorityWeight, int normalWeight)
    {
        var p = NormalizeWeight(priorityWeight, QueuePolicyKeys.DefaultPriorityWeight);
        var n = NormalizeWeight(normalWeight, QueuePolicyKeys.DefaultNormalWeight);
        return counter >= 0 ? counter < p : -counter >= n;
    }

    public static int Advance(int counter, bool priorityPick, int priorityWeight, int normalWeight)
    {
        var p = NormalizeWeight(priorityWeight, QueuePolicyKeys.DefaultPriorityWeight);
        var n = NormalizeWeight(normalWeight, QueuePolicyKeys.DefaultNormalWeight);

        return priorityPick
            ? (counter >= 0 ? Math.Min(counter + 1, p) : 1)
            : (counter > 0 ? -1 : Math.Max(counter - 1, -n));
    }

    public static int ResolveEffectiveCounter(int? storedCounter, int? storedPolicyVersion, QueueOrderingPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        if (!storedCounter.HasValue || (storedPolicyVersion ?? 0) != policy.WalkinPriorityRatioVersion)
        {
            return 0;
        }

        var p = NormalizeWeight(policy.PriorityWeight, QueuePolicyKeys.DefaultPriorityWeight);
        var n = NormalizeWeight(policy.NormalWeight, QueuePolicyKeys.DefaultNormalWeight);
        return Math.Clamp(storedCounter.Value, -n, p);
    }

    public static bool IsPriorityGroup(string priority, string queueType)
        => ClinicalQueueRules.IsElevated(priority)
            || queueType == ClinicalQueueRules.QueueTypeReturnConsultation;

    private static int NormalizeWeight(int weight, int fallback)
        => weight is >= QueuePolicyKeys.MinRatioWeight and <= QueuePolicyKeys.MaxRatioWeight
            ? weight
            : fallback;
}
