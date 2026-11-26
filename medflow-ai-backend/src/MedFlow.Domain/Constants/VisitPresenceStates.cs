namespace MedFlow.Domain.Constants;

public static class VisitPresenceStates
{
    public const string Present = "PRESENT";
    public const string Left = "LEFT";
    public const string Deferred = "DEFERRED";

    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim().ToUpperInvariant();
        return normalized is Present or Left or Deferred;
    }
}
