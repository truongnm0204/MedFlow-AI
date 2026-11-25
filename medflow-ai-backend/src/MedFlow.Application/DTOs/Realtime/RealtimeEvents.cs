using System.Text.Json.Serialization;

namespace MedFlow.Application.DTOs.Realtime;

public static class RealtimeEventNames
{
    public const string QueueChanged = "QueueChanged";
    public const string VisitPaymentChanged = "VisitPaymentChanged";
    public const string QueueScopeChanged = "QueueScopeChanged";
    public const string MembershipRevoked = "MembershipRevoked";
}

/// <summary>
/// Minimal Zero-PHI wire payload pushed to SignalR clients when a queue changes.
/// Never carries patient names, patient IDs, ticket numbers, or clinical/medical data.
/// </summary>
public sealed record QueueChangedWireDto(
    string EventType = RealtimeEventNames.QueueChanged,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    long? ServicePointId = null);

/// <summary>
/// Minimal Zero-PHI wire payload pushed to <c>visit:{visitId}</c> when payment state changes.
/// Never carries payment status (PAID), amount, bank details, or patient identity;
/// clients must refetch the authoritative REST charges endpoint.
/// </summary>
public sealed record VisitPaymentChangedWireDto(
    string EventType,
    long VisitId)
{
    public VisitPaymentChangedWireDto(long visitId)
        : this(RealtimeEventNames.VisitPaymentChanged, visitId)
    {
    }
}

/// <summary>
/// Wire signal instructing an authorized appointment subscriber to refresh its room/bucket scope.
/// </summary>
public sealed record QueueScopeChangedWireDto(
    string EventType,
    long AppointmentId)
{
    public QueueScopeChangedWireDto(long appointmentId)
        : this(RealtimeEventNames.QueueScopeChanged, appointmentId)
    {
    }
}

/// <summary>
/// Wire signal sent to a connection when server-side authorization revokes an active subscription.
/// </summary>
public sealed record MembershipRevokedWireDto(
    string EventType,
    string ResourceKey,
    string ReasonCode)
{
    public MembershipRevokedWireDto(string resourceKey, string reasonCode)
        : this(RealtimeEventNames.MembershipRevoked, resourceKey, reasonCode)
    {
    }
}

/// <summary>
/// Acknowledgement returned to authenticated clients after joining a Hub subscription.
/// </summary>
public sealed record RealtimeSubscriptionAckDto(
    string ResourceKey,
    DateTime ExpiresAtUtc);

/// <summary>
/// Internal Application-to-Publisher routing command (never serialized directly onto the wire).
/// </summary>
public sealed record QueueChangedNotification(
    long HospitalId,
    long? ServicePointId = null,
    long? RoomId = null,
    long? AppointmentId = null);

/// <summary>
/// Internal Application-to-Publisher routing command for SePay payment state changes.
/// </summary>
public sealed record VisitPaymentChangedNotification(
    long VisitId);

/// <summary>
/// Server-side authorization grant computed by <c>IClinicalRealtimeAccessService</c>.
/// </summary>
public sealed record AuthorizedRealtimeSubscription(
    string ResourceKey,
    IReadOnlyList<string> PrimaryGroupNames,
    IReadOnlyList<string> BucketGroupNames,
    long? HospitalId = null,
    long? PatientId = null,
    long? AppointmentId = null,
    long? VisitId = null)
{
    public IReadOnlyList<string> AllGroupNames =>
        BucketGroupNames.Count == 0
            ? PrimaryGroupNames
            : PrimaryGroupNames.Concat(BucketGroupNames).Distinct(StringComparer.Ordinal).ToArray();
}
