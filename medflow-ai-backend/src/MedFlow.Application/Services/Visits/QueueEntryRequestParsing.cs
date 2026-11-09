using System.Globalization;
using System.Net;
using MedFlow.Domain.Exceptions;

namespace MedFlow.Application.Services.Visits;

internal static class QueueEntryRequestParsing
{
    public static (long? Id, string? Code) ParseIdentifier(string? idOrCode)
    {
        if (string.IsNullOrWhiteSpace(idOrCode))
        {
            throw new ValidationException("Mã định danh lượt xếp hàng không hợp lệ.");
        }

        var trimmed = idOrCode.Trim();
        if (trimmed.StartsWith('#'))
        {
            trimmed = trimmed[1..].TrimStart();
        }

        if (trimmed.Length >= 3 &&
            (trimmed.StartsWith("q-", StringComparison.OrdinalIgnoreCase) ||
             trimmed.StartsWith("q_", StringComparison.OrdinalIgnoreCase)))
        {
            var suffix = trimmed[2..];
            if (suffix.All(char.IsAsciiDigit) ||
                (suffix.StartsWith('-') && suffix.Length > 1 && suffix[1..].All(char.IsAsciiDigit)))
            {
                trimmed = suffix;
            }
        }

        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > 40)
        {
            throw new ValidationException("Mã định danh lượt xếp hàng không hợp lệ.");
        }

        if (trimmed.All(char.IsAsciiDigit) ||
            (trimmed.StartsWith('-') && trimmed.Length > 1 && trimmed[1..].All(char.IsAsciiDigit)))
        {
            if (!long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numericId) ||
                numericId <= 0)
            {
                throw new ValidationException("ID lượt xếp hàng phải lớn hơn 0.");
            }

            return (numericId, null);
        }

        if (trimmed.StartsWith('-') ||
            !trimmed.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '-' || ch == '_'))
        {
            throw new ValidationException("Mã định danh lượt xếp hàng không hợp lệ.");
        }

        return (null, trimmed.ToUpperInvariant());
    }

    public static IPAddress? ParseIpAddress(string? ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            return null;
        }

        return IPAddress.TryParse(ipAddress.Trim(), out var parsed) ? parsed : null;
    }
}
