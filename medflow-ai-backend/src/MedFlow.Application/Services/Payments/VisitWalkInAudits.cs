using System.Globalization;
using System.Text.Json;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Payments;

internal static class VisitWalkInAudits
{
    public const string Action = "WALK_IN_VISIT_CREATED";
    public const string EntityType = "VISIT";

    public static async Task<List<WalkInAudit>> LoadAsync(
        IApplicationDbContext db,
        long visitId,
        CancellationToken ct)
    {
        var all = await LoadManyAsync(db, [visitId], ct);
        return all.TryGetValue(visitId, out var audits) ? audits : [];
    }

    /// <summary>Load walk-in audit cho nhiều visit trong một truy vấn, giữ thứ tự id giảm dần.</summary>
    public static async Task<Dictionary<long, List<WalkInAudit>>> LoadManyAsync(
        IApplicationDbContext db,
        IReadOnlyList<long> visitIds,
        CancellationToken ct)
    {
        var result = visitIds.ToDictionary(id => id, _ => new List<WalkInAudit>());
        if (visitIds.Count == 0)
            return result;

        var entityIds = visitIds.Select(id => id.ToString(CultureInfo.InvariantCulture)).ToList();
        var rows = await db.AuditLogs.AsNoTracking()
            .Where(a =>
                a.EntityId != null &&
                entityIds.Contains(a.EntityId) &&
                a.EntityType.ToUpper() == EntityType &&
                a.Action.ToUpper() == Action)
            .OrderByDescending(a => a.Id)
            .ToListAsync(ct);

        foreach (var row in rows)
        {
            if (row.EntityId is null ||
                !long.TryParse(row.EntityId, NumberStyles.None, CultureInfo.InvariantCulture, out var visitId) ||
                !result.TryGetValue(visitId, out var audits))
                continue;
            audits.Add(Parse(row));
        }

        return result;
    }

    public static string? NormalizePaymentMethod(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        return raw.Trim().ToUpperInvariant() switch
        {
            "VIETQR" or "QR" or "BANK_TRANSFER" => "VIETQR",
            "CASHIER" or "CASH" or "COUNTER" => "CASHIER",
            _ => null,
        };
    }

    private static WalkInAudit Parse(AuditLog row)
    {
        long? serviceId = null;
        string? serviceCode = null;
        string? serviceName = null;
        decimal? servicePrice = null;
        string? paymentMethod = null;

        if (!string.IsNullOrWhiteSpace(row.ChangeData))
        {
            try
            {
                using var doc = JsonDocument.Parse(row.ChangeData);
                var root = doc.RootElement;
                serviceId = ReadInt64(root, "serviceId");
                serviceCode = ReadString(root, "serviceCode");
                serviceName = ReadString(root, "serviceName");
                servicePrice = ReadDecimal(root, "servicePrice");
                paymentMethod = ReadString(root, "paymentMethod");
            }
            catch (JsonException)
            {
                // Malformed audit payload does not fail the read.
            }
        }

        return new WalkInAudit(row.HospitalId, serviceId, serviceCode, serviceName, servicePrice, paymentMethod);
    }

    private static long? ReadInt64(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value))
            return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
            return number;
        if (value.ValueKind == JsonValueKind.String &&
            long.TryParse(value.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
            return parsed;
        return null;
    }

    private static decimal? ReadDecimal(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value))
            return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
            return number;
        if (value.ValueKind == JsonValueKind.String &&
            decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
            return parsed;
        return null;
    }

    private static string? ReadString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            return null;
        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    internal sealed record WalkInAudit(
        long? HospitalId,
        long? ServiceId,
        string? ServiceCode,
        string? ServiceName,
        decimal? ServicePrice,
        string? PaymentMethod);
}
