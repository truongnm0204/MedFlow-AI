namespace MedFlow.Application.Services.Visits.Queueing;

internal static class QueueArrivalClassifier
{
    public static string Classify(
        bool isScheduled,
        DateTime? scheduledAt,
        DateTime? arrivedAt,
        int graceMinutes,
        string fallback)
    {
        if (!isScheduled)
        {
            return "WALK_IN";
        }

        if (!scheduledAt.HasValue || !arrivedAt.HasValue)
        {
            return fallback;
        }

        if (arrivedAt.Value < scheduledAt.Value.AddMinutes(-graceMinutes))
        {
            return "EARLY";
        }

        if (arrivedAt.Value > scheduledAt.Value.AddMinutes(graceMinutes))
        {
            return "LATE";
        }

        return "ON_TIME_OR_GRACE";
    }
}
