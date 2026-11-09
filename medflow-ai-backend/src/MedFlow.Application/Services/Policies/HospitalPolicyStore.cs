using System.Text.Json;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Domain.Entities;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Policies;

internal static class HospitalPolicyStore
{
    public static async Task<long> ResolveAdminHospitalIdAsync(
        IApplicationDbContext db,
        long userId,
        CancellationToken ct)
    {
        var hospitalId = await db.Staff
            .AsNoTracking()
            .Where(s => s.UserId == userId && s.Status == "ACTIVE")
            .Select(s => s.HospitalId)
            .FirstOrDefaultAsync(ct);

        if (hospitalId is null)
        {
            throw new ForbiddenException("Tài khoản quản trị chưa được gán bệnh viện.");
        }

        return hospitalId.Value;
    }

    public static async Task<List<HospitalPolicy>> LoadRowsAsync(
        IApplicationDbContext db,
        long hospitalId,
        string policyType,
        bool tracking,
        CancellationToken ct)
    {
        var query = db.HospitalPolicies
            .Where(p => p.HospitalId == hospitalId
                && p.PolicyType == policyType
                && p.QueueServicePointId == null);

        if (!tracking)
        {
            query = query.AsNoTracking();
        }

        return await query.ToListAsync(ct);
    }

    public static Dictionary<string, HospitalPolicy> SelectCurrent(
        IEnumerable<HospitalPolicy> rows,
        DateTime now)
    {
        return rows
            .Where(r => IsEffective(r, now))
            .GroupBy(r => r.PolicyKey)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(r => r.PolicyVersion ?? 0)
                      .ThenByDescending(r => r.Id)
                      .First());
    }

    public static bool IsEffective(HospitalPolicy row, DateTime now)
        => (row.EffectiveFrom is null || row.EffectiveFrom <= now)
            && (row.EffectiveTo is null || row.EffectiveTo > now);

    public static int ReadInt(HospitalPolicy? row, string propertyName, int defaultValue)
    {
        if (row is null || string.IsNullOrWhiteSpace(row.PolicyValue))
        {
            return defaultValue;
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
            // Fallback to defaultValue when JSON is malformed.
        }

        return defaultValue;
    }

    public static bool ReadBool(HospitalPolicy? row, string propertyName, bool defaultValue)
    {
        if (row is null || string.IsNullOrWhiteSpace(row.PolicyValue))
        {
            return defaultValue;
        }

        try
        {
            using var doc = JsonDocument.Parse(row.PolicyValue);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty(propertyName, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.True)
                {
                    return true;
                }

                if (prop.ValueKind == JsonValueKind.False)
                {
                    return false;
                }
            }
        }
        catch (JsonException)
        {
            // Fallback to defaultValue when JSON is malformed.
        }

        return defaultValue;
    }

    public static int ValidateRange(int? value, string label, int min, int max)
    {
        if (value is null)
        {
            throw new ValidationException($"{label} là bắt buộc.");
        }

        if (value.Value < min || value.Value > max)
        {
            throw new ValidationException($"{label} phải từ {min} đến {max}.");
        }

        return value.Value;
    }

    public static int AppendVersion(
        IApplicationDbContext db,
        List<HospitalPolicy> rows,
        long hospitalId,
        string policyType,
        string key,
        string newJson,
        DateTime now)
    {
        foreach (var row in rows.Where(r => r.PolicyKey == key && IsEffective(r, now)))
        {
            row.EffectiveTo = now;
        }

        var nextVersion = rows
            .Where(r => r.PolicyKey == key)
            .Select(r => r.PolicyVersion ?? 0)
            .DefaultIfEmpty(0)
            .Max() + 1;

        db.HospitalPolicies.Add(new HospitalPolicy
        {
            HospitalId = hospitalId,
            QueueServicePointId = null,
            PolicyType = policyType,
            PolicyKey = key,
            PolicyValue = newJson,
            PolicyVersion = nextVersion,
            EffectiveFrom = now,
            EffectiveTo = null,
        });

        return nextVersion;
    }
}
