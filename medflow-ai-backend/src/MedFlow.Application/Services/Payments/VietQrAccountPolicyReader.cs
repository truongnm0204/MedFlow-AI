using System.Text.Json;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Payments;

internal static class VietQrAccountPolicyReader
{
    public const string MissingMessage = "Cơ sở chưa cấu hình VietQR.";
    public const string InvalidMessage = "Cấu hình VietQR của cơ sở không hợp lệ.";

    public static async Task<VietQrAccount> ReadAsync(
        IApplicationDbContext db,
        long hospitalId,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var rows = await db.HospitalPolicies.AsNoTracking()
            .Where(p => p.HospitalId == hospitalId
                && p.PolicyType == PaymentPolicyKeys.PolicyType
                && p.PolicyKey == PaymentPolicyKeys.VietQrAccount
                && p.QueueServicePointId == null)
            .ToListAsync(ct);

        var selected = rows
            .Where(r => IsEffective(r.EffectiveFrom, r.EffectiveTo, nowUtc))
            .OrderByDescending(r => r.PolicyVersion ?? 0)
            .ThenByDescending(r => r.Id)
            .FirstOrDefault();

        if (selected is null || string.IsNullOrWhiteSpace(selected.PolicyValue))
            throw new ConflictException(selected is null ? MissingMessage : InvalidMessage);

        try
        {
            using var doc = JsonDocument.Parse(selected.PolicyValue);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                throw new ConflictException(InvalidMessage);

            var bin = ReadString(doc.RootElement, "bin");
            var accountNumber = ReadString(doc.RootElement, "accountNumber");
            var accountName = ReadString(doc.RootElement, "accountName");
            if (!IsBin(bin) || !IsAccountNumber(accountNumber) || !IsAccountName(accountName))
                throw new ConflictException(InvalidMessage);

            return new VietQrAccount(bin!, accountNumber!, accountName!);
        }
        catch (JsonException)
        {
            throw new ConflictException(InvalidMessage);
        }
    }

    private static bool IsEffective(DateTime? from, DateTime? to, DateTime now)
        => (from is null || from <= now) && (to is null || to > now);

    private static string? ReadString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            return null;
        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static bool IsBin(string? value) =>
        value is not null && value.Length == 6 && value.All(static c => c is >= '0' and <= '9');

    private static bool IsAccountNumber(string? value) =>
        value is not null && value.Length is >= 6 and <= 19 && value.All(static c => c is >= '0' and <= '9');

    private static bool IsAccountName(string? value) =>
        value is not null && value.Length is >= 1 and <= 50;
}
