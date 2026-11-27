using MedFlow.Domain.Constants;

namespace MedFlow.Domain.Entities;

public class PaymentEnrollmentIntent
{
    public long Id { get; set; }
    public long PaymentId { get; set; }
    public long VisitId { get; set; }
    public long PatientId { get; set; }
    public long DiagnosticOrderId { get; set; }
    public long? RequestedByUserId { get; set; }
    public string RequestedByRole { get; set; } = RoleNames.Patient;
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateOnly? PresenceVerifiedDate { get; set; }
    public int? PresenceSessionNo { get; set; }
    public string Status { get; set; } = PaymentEnrollmentIntentStatuses.PendingSettlement;
    public int RetryCount { get; set; }
    public string? OutcomeCode { get; set; }
    public string? OutcomeMessage { get; set; }
    public long? QueueEntryId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
}
