namespace MedFlow.Domain.Constants;

/// <summary>
/// Canonical dispositions when cancelling a clinical queue entry (ADR-0059 / ADR-0016 extension).
/// </summary>
public static class QueueCancelDispositions
{
    public const string PatientAbandoned = "PATIENT_ABANDONED";
    public const string ReassignRequeue = "REASSIGN_REQUEUE";
    public const string TechnicalError = "TECHNICAL_ERROR";
}
