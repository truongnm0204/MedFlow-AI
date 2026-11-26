using System.Text.Json;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Appointments;
using MedFlow.Domain.Constants;
using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Policies;

public sealed record CrossDayVisitPolicySnapshot(
    bool AllowCrossDayVisit,
    int DiagnosticOrderValidityDays,
    int ReturnConsultationWindowDays,
    bool RequirePresenceForReturnQueue,
    bool AllowDoctorReassignment,
    bool AutoCancelOverdueVisit,
    int? PresenceTtlMinutes = null,
    bool AllowHospitalAdminReturnCoordination = false,
    int? MaxActiveQueuePerDoctor = null)
{
    public static readonly CrossDayVisitPolicySnapshot Defaults = new(
        AllowCrossDayVisit: true,
        DiagnosticOrderValidityDays: 7,
        ReturnConsultationWindowDays: 7,
        RequirePresenceForReturnQueue: true,
        AllowDoctorReassignment: true,
        AutoCancelOverdueVisit: false,
        PresenceTtlMinutes: null,
        AllowHospitalAdminReturnCoordination: false,
        MaxActiveQueuePerDoctor: null);
}

public static class CrossDayVisitPolicyKeys
{
    public const string VisitPolicyType = "VISIT";
    public const string QueuePolicyType = "QUEUE";

    public const string AllowCrossDayVisit = "allow_cross_day_visit";
    public const string DiagnosticOrderValidityDays = "diagnostic_order_validity_days";
    public const string ReturnConsultationWindowDays = "return_consultation_window_days";
    public const string RequirePresenceForReturnQueue = "require_presence_for_return_queue";
    public const string AllowDoctorReassignment = "allow_doctor_reassignment";
    public const string AutoCancelOverdueVisit = "auto_cancel_overdue_visit";
    public const string PresenceTtlMinutes = "presence_ttl_minutes";
    public const string AllowHospitalAdminReturnCoordination = "allow_hospital_admin_return_coordination";
    public const string MaxActiveQueuePerDoctor = "max_active_queue_per_doctor";

    public const string OverdueReasonDiagnosticOrder = "DIAGNOSTIC_ORDER_OVERDUE_REVIEW";
    public const string OverdueReasonReturnConsultation = "RETURN_CONSULTATION_OVERDUE_REVIEW";
}

public static class CrossDayVisitPolicyReader
{
    public static async Task<CrossDayVisitPolicySnapshot> LoadAsync(
        IApplicationDbContext db,
        long? hospitalId,
        DateTime nowUtc,
        CancellationToken ct = default)
    {
        if (!hospitalId.HasValue || hospitalId.Value <= 0)
        {
            return CrossDayVisitPolicySnapshot.Defaults;
        }

        List<HospitalPolicy> rows;
        try
        {
            rows = await db.HospitalPolicies
                .AsNoTracking()
                .Where(p =>
                    p.HospitalId == hospitalId.Value &&
                    p.QueueServicePointId == null &&
                    (p.PolicyType == CrossDayVisitPolicyKeys.VisitPolicyType ||
                     p.PolicyType == CrossDayVisitPolicyKeys.QueuePolicyType))
                .ToListAsync(ct);
        }
        catch (NotSupportedException)
        {
            return CrossDayVisitPolicySnapshot.Defaults;
        }

        if (rows.Count == 0)
        {
            return CrossDayVisitPolicySnapshot.Defaults;
        }

        var activeByKey = rows
            .Where(r =>
                !string.IsNullOrWhiteSpace(r.PolicyKey) &&
                (!r.EffectiveFrom.HasValue || r.EffectiveFrom.Value <= nowUtc) &&
                (!r.EffectiveTo.HasValue || r.EffectiveTo.Value > nowUtc))
            .GroupBy(r => r.PolicyKey!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g
                    .OrderByDescending(r => string.Equals(r.PolicyType?.Trim(), CrossDayVisitPolicyKeys.VisitPolicyType, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                    .ThenByDescending(r => r.PolicyVersion ?? 0)
                    .ThenByDescending(r => r.Id)
                    .First(),
                StringComparer.OrdinalIgnoreCase);

        var defaults = CrossDayVisitPolicySnapshot.Defaults;

        return new CrossDayVisitPolicySnapshot(
            AllowCrossDayVisit: ReadBool(activeByKey, CrossDayVisitPolicyKeys.AllowCrossDayVisit, defaults.AllowCrossDayVisit),
            DiagnosticOrderValidityDays: Math.Clamp(
                ReadInt(activeByKey, CrossDayVisitPolicyKeys.DiagnosticOrderValidityDays, defaults.DiagnosticOrderValidityDays),
                1,
                365),
            ReturnConsultationWindowDays: Math.Clamp(
                ReadInt(activeByKey, CrossDayVisitPolicyKeys.ReturnConsultationWindowDays, defaults.ReturnConsultationWindowDays),
                1,
                365),
            RequirePresenceForReturnQueue: ReadBool(
                activeByKey,
                CrossDayVisitPolicyKeys.RequirePresenceForReturnQueue,
                defaults.RequirePresenceForReturnQueue),
            AllowDoctorReassignment: ReadBool(
                activeByKey,
                CrossDayVisitPolicyKeys.AllowDoctorReassignment,
                defaults.AllowDoctorReassignment),
            AutoCancelOverdueVisit: ReadBool(
                activeByKey,
                CrossDayVisitPolicyKeys.AutoCancelOverdueVisit,
                defaults.AutoCancelOverdueVisit),
            PresenceTtlMinutes: ReadOptionalPositiveInt(activeByKey, CrossDayVisitPolicyKeys.PresenceTtlMinutes),
            AllowHospitalAdminReturnCoordination: ReadBool(
                activeByKey,
                CrossDayVisitPolicyKeys.AllowHospitalAdminReturnCoordination,
                defaults.AllowHospitalAdminReturnCoordination),
            MaxActiveQueuePerDoctor: ReadOptionalPositiveInt(activeByKey, CrossDayVisitPolicyKeys.MaxActiveQueuePerDoctor));
    }

    public const string ExpiredOrderDoctorApprovalRequiredMessage =
        "[DIAGNOSTIC_ORDER_EXPIRED_DOCTOR_APPROVAL_REQUIRED] Phiếu chỉ định cận lâm sàng đã quá thời hạn hiệu lực. Cần bác sĩ phụ trách đánh giá lại hoặc gia hạn hiệu lực trước khi thực hiện.";

    public static DateTime? ComputeOrderValidityExpiresAt(DiagnosticOrder order, int validityDays)
    {
        ArgumentNullException.ThrowIfNull(order);
        DateTime? baseExpiry = order.OrderedAt.HasValue && validityDays > 0
            ? order.OrderedAt.Value.AddDays(validityDays)
            : null;

        if (order.ValidityExtendedUntil.HasValue)
        {
            return baseExpiry.HasValue && baseExpiry.Value > order.ValidityExtendedUntil.Value
                ? baseExpiry.Value
                : order.ValidityExtendedUntil.Value;
        }

        return baseExpiry;
    }

    /// <summary>
    /// CD-T11 / CD-T12: Only unexecuted orders (ORDERED / CONFIRMED) expire by diagnostic_order_validity_days.
    /// Orders already in execution (IN_PROGRESS) or completed execution awaiting UC43 (COMPLETED) NEVER expire!
    /// </summary>
    public static bool IsOrderExpiredForExecution(
        DiagnosticOrder order,
        int validityDays,
        DateTime nowLocal)
    {
        ArgumentNullException.ThrowIfNull(order);

        var status = AppointmentBookingRules.NormalizeStatusKey(order.Status);
        if (status is "COMPLETED" or "IN_PROGRESS" or "CANCELLED")
        {
            return false;
        }

        var selection = string.IsNullOrWhiteSpace(order.SelectionStatus)
            ? DiagnosticSelectionStatuses.Accepted
            : order.SelectionStatus.Trim().ToUpperInvariant();
        if (selection is DiagnosticSelectionStatuses.Declined or DiagnosticSelectionStatuses.SkippedAfterPaid)
        {
            return false;
        }

        var expiresAt = ComputeOrderValidityExpiresAt(order, validityDays);
        return expiresAt.HasValue && nowLocal > expiresAt.Value;
    }

    public static bool IsReturnConsultationOverdue(
        Visit visit,
        int returnWindowDays,
        DateTime nowLocal)
    {
        ArgumentNullException.ThrowIfNull(visit);

        var status = AppointmentBookingRules.NormalizeStatusKey(visit.Status);
        if (status is "COMPLETED" or "DONE" or "CANCELLED" or "NO_SHOW")
        {
            return false;
        }

        if (!visit.ReadyForReturnAt.HasValue || returnWindowDays <= 0)
        {
            return false;
        }

        return nowLocal > visit.ReadyForReturnAt.Value.AddDays(returnWindowDays);
    }

    private static bool ReadBool(
        IReadOnlyDictionary<string, HospitalPolicy> activeByKey,
        string key,
        bool fallback)
    {
        if (!activeByKey.TryGetValue(key, out var row) || string.IsNullOrWhiteSpace(row.PolicyValue))
        {
            return fallback;
        }

        try
        {
            using var doc = JsonDocument.Parse(row.PolicyValue);
            var root = doc.RootElement;
            if (root.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                return root.GetBoolean();
            }

            if (root.ValueKind == JsonValueKind.Object)
            {
                foreach (var propName in new[] { "enabled", "value", "required", "allow" })
                {
                    if (root.TryGetProperty(propName, out var prop))
                    {
                        if (prop.ValueKind is JsonValueKind.True or JsonValueKind.False)
                        {
                            return prop.GetBoolean();
                        }

                        if (prop.ValueKind == JsonValueKind.String &&
                            bool.TryParse(prop.GetString(), out var parsedStrBool))
                        {
                            return parsedStrBool;
                        }
                    }
                }
            }

            if (root.ValueKind == JsonValueKind.String &&
                bool.TryParse(root.GetString(), out var strBool))
            {
                return strBool;
            }
        }
        catch (JsonException)
        {
            if (bool.TryParse(row.PolicyValue.Trim(), out var rawBool))
            {
                return rawBool;
            }
        }

        return fallback;
    }

    private static int ReadInt(
        IReadOnlyDictionary<string, HospitalPolicy> activeByKey,
        string key,
        int fallback)
    {
        if (!activeByKey.TryGetValue(key, out var row) || string.IsNullOrWhiteSpace(row.PolicyValue))
        {
            return fallback;
        }

        try
        {
            using var doc = JsonDocument.Parse(row.PolicyValue);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Number && root.TryGetInt32(out var numVal))
            {
                return numVal;
            }

            if (root.ValueKind == JsonValueKind.Object)
            {
                foreach (var propName in new[] { "days", "value", "windowDays", "maxDays" })
                {
                    if (root.TryGetProperty(propName, out var prop))
                    {
                        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var propInt))
                        {
                            return propInt;
                        }

                        if (prop.ValueKind == JsonValueKind.String &&
                            int.TryParse(prop.GetString(), out var parsedStrInt))
                        {
                            return parsedStrInt;
                        }
                    }
                }
            }

            if (root.ValueKind == JsonValueKind.String &&
                int.TryParse(root.GetString(), out var strInt))
            {
                return strInt;
            }
        }
        catch (JsonException)
        {
            if (int.TryParse(row.PolicyValue.Trim(), out var rawInt))
            {
                return rawInt;
            }
        }

        return fallback;
    }

    private static int? ReadOptionalPositiveInt(
        IReadOnlyDictionary<string, HospitalPolicy> activeByKey,
        string key)
    {
        if (!activeByKey.TryGetValue(key, out _))
        {
            return null;
        }

        var parsed = ReadInt(activeByKey, key, -1);
        return parsed > 0 ? parsed : null;
    }
}
