using System.Text.Json;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Policies;
using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Visits.Queueing;

public static class QueueOrderingPolicyLoader
{
    public static async Task<QueueOrderingPolicySet> LoadAsync(
        IApplicationDbContext db,
        long hospitalId,
        DateTime nowUtc,
        CancellationToken ct = default)
    {
        List<HospitalPolicy> rows;
        try
        {
            rows = await db.HospitalPolicies
                .AsNoTracking()
                .Where(p => p.HospitalId == hospitalId
                    && ((p.PolicyType == QueuePolicyKeys.PolicyType
                            && (p.PolicyKey == QueuePolicyKeys.GraceMinutes
                                || p.PolicyKey == QueuePolicyKeys.ExpectedDurationMinutes
                                || p.PolicyKey == QueuePolicyKeys.SafetyBufferMinutes))
                        || (p.PolicyType == QueuePolicyKeys.DiagnosticPolicyType
                            && p.PolicyKey == QueuePolicyKeys.FinalResultRequired)))
                .ToListAsync(ct);
        }
        catch (NotSupportedException)
        {
            rows = [];
        }

        var effectiveRows = rows
            .Where(r => IsEffective(r, nowUtc))
            .ToList();

        return new QueueOrderingPolicySet(effectiveRows);
    }

    private static bool IsEffective(HospitalPolicy row, DateTime now)
        => (row.EffectiveFrom is null || row.EffectiveFrom <= now)
            && (row.EffectiveTo is null || row.EffectiveTo > now);
}

public sealed class QueueOrderingPolicySet
{
    private readonly IReadOnlyList<HospitalPolicy> _effectiveRows;

    internal QueueOrderingPolicySet(IReadOnlyList<HospitalPolicy> effectiveRows)
    {
        _effectiveRows = effectiveRows;
    }

    public QueueOrderingPolicy ForServicePoint(long? queueServicePointId)
    {
        var graceRow = SelectRow(QueuePolicyKeys.GraceMinutes, queueServicePointId);
        var durationRow = SelectRow(QueuePolicyKeys.ExpectedDurationMinutes, queueServicePointId);
        var bufferRow = SelectRow(QueuePolicyKeys.SafetyBufferMinutes, queueServicePointId);
        var finalResultRow = SelectRow(QueuePolicyKeys.FinalResultRequired, queueServicePointId);

        var rawGrace = ReadInt(graceRow, QueuePolicyKeys.PropMinutes);
        var graceMinutes = rawGrace is >= 0 ? rawGrace.Value : QueuePolicyKeys.DefaultGraceMinutes;

        var rawDuration = ReadInt(durationRow, QueuePolicyKeys.PropMinutes);
        int? durationMinutes = rawDuration is > 0 ? rawDuration.Value : null;

        var rawBuffer = ReadInt(bufferRow, QueuePolicyKeys.PropMinutes);
        int? bufferMinutes = rawBuffer is >= 0 ? rawBuffer.Value : null;

        return new QueueOrderingPolicy(
            graceMinutes,
            durationMinutes,
            bufferMinutes,
            FinalResultRequired: HospitalPolicyStore.ReadBool(finalResultRow, QueuePolicyKeys.PropEnabled, defaultValue: true));
    }

    private HospitalPolicy? SelectRow(string policyKey, long? queueServicePointId)
    {
        if (queueServicePointId.HasValue)
        {
            var spRow = _effectiveRows
                .Where(r => r.PolicyKey == policyKey && r.QueueServicePointId == queueServicePointId.Value)
                .OrderByDescending(r => r.PolicyVersion ?? 0)
                .ThenByDescending(r => r.Id)
                .FirstOrDefault();

            if (spRow is not null)
            {
                return spRow;
            }
        }

        return _effectiveRows
            .Where(r => r.PolicyKey == policyKey && r.QueueServicePointId is null)
            .OrderByDescending(r => r.PolicyVersion ?? 0)
            .ThenByDescending(r => r.Id)
            .FirstOrDefault();
    }

    private static int? ReadInt(HospitalPolicy? row, string propertyName)
    {
        if (row is null || string.IsNullOrWhiteSpace(row.PolicyValue))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(row.PolicyValue);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty(propertyName, out var prop)
                && prop.TryGetInt32(out var value))
            {
                return value;
            }
        }
        catch (JsonException)
        {
            // Malformed JSON is treated as missing.
        }

        return null;
    }
}
