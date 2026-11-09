using System.Text.Json;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Policies;

internal static class QueuePolicyReader
{
    public static async Task<bool> IsManualPriorityVerificationRequiredAsync(
        IApplicationDbContext db,
        long? hospitalId,
        long? servicePointId,
        DateTime nowUtc,
        CancellationToken ct)
    {
        if (hospitalId is null)
            return true;

        List<HospitalPolicy> rows;
        try
        {
            rows = await db.HospitalPolicies
                .AsNoTracking()
                .Where(p => p.HospitalId == hospitalId.Value
                    && p.PolicyType == QueuePolicyKeys.PolicyType
                    && p.PolicyKey == QueuePolicyKeys.ManualPriorityRequiresVerification
                    && (p.QueueServicePointId == null || p.QueueServicePointId == servicePointId))
                .ToListAsync(ct);
        }
        catch (NotSupportedException)
        {
            return true;
        }

        var selected = rows
            .Where(r => IsEffective(r, nowUtc))
            .OrderByDescending(r => r.QueueServicePointId.HasValue && servicePointId.HasValue && r.QueueServicePointId == servicePointId ? 1 : 0)
            .ThenByDescending(r => r.PolicyVersion ?? 0)
            .ThenByDescending(r => r.Id)
            .FirstOrDefault();

        if (selected is null || string.IsNullOrWhiteSpace(selected.PolicyValue))
            return true;

        try
        {
            using var doc = JsonDocument.Parse(selected.PolicyValue);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("enabled", out var prop))
            {
                if (prop.ValueKind == JsonValueKind.True)
                    return true;
                if (prop.ValueKind == JsonValueKind.False)
                    return false;
            }
        }
        catch (JsonException)
        {
            return true;
        }

        return true;
    }

    private static bool IsEffective(HospitalPolicy row, DateTime now)
        => (row.EffectiveFrom is null || row.EffectiveFrom <= now)
            && (row.EffectiveTo is null || row.EffectiveTo > now);
}
