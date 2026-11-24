using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MedFlow.Application.Services.SystemAdmin;

internal static partial class SensitiveDataMasker
{
    public const string RedactedPlaceholder = "***REDACTED***";
    public const string MaskedSecretDisplay = "••••••••••••";

    private static readonly string[] SensitiveKeySubstrings =
    [
        "password",
        "passwd",
        "pwd",
        "secret",
        "apikey",
        "webhookkey",
        "privatekey",
        "hashkey",
        "signingkey",
        "encryptionkey",
        "accesstoken",
        "refreshtoken",
        "idtoken",
        "authtoken",
        "tokenhash",
        "sessiontoken",
        "bearertoken",
        "resettoken",
        "authorization",
        "credential",
        "identificationnumber",
        "identificationhash",
        "cccd",
        "cmnd",
        "nationalid",
    ];

    private static readonly HashSet<string> ExactSensitiveKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "token",
        "otp",
        "pin",
        "cvv",
        "ssn",
    };

    private static readonly HashSet<string> SafeKeyAllowlist = new(StringComparer.OrdinalIgnoreCase)
    {
        "maxtokens",
        "tokencount",
        "prompttokens",
        "completiontokens",
        "totaltokens",
    };

    public static (bool IsConfigured, string? MaskedValue) MaskRuntimeSecret(string? rawSecret)
    {
        if (string.IsNullOrWhiteSpace(rawSecret))
        {
            return (false, null);
        }

        return (true, MaskedSecretDisplay);
    }

    public static string? MaskPlainText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var scrubbed = AuthHeaderSchemeRegex().Replace(text, "$1 " + RedactedPlaceholder);
        scrubbed = InlineKeyValueSecretRegex().Replace(scrubbed, "$1$2" + RedactedPlaceholder);
        return scrubbed;
    }

    public static (string? MaskedJson, IReadOnlyList<string> RedactedFields) MaskJsonMetadata(string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return (null, []);
        }

        var redactedFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                WriteMaskedElement(writer, doc.RootElement, redactedFields);
            }

            var masked = Encoding.UTF8.GetString(stream.ToArray());
            var sortedFields = redactedFields
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return (masked, sortedFields);
        }
        catch (JsonException)
        {
            var scrubbed = MaskPlainText(rawJson);
            if (!string.Equals(scrubbed, rawJson, StringComparison.Ordinal))
            {
                redactedFields.Add("rawText");
            }

            return (scrubbed, redactedFields.ToArray());
        }
    }

    public static bool IsSensitivePropertyName(string propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return false;
        }

        var normalized = NormalizeKey(propertyName);
        if (normalized.Length == 0 || SafeKeyAllowlist.Contains(normalized))
        {
            return false;
        }

        if (ExactSensitiveKeys.Contains(normalized) || HasDiscreteWordToken(propertyName, "otp"))
        {
            return true;
        }

        foreach (var marker in SensitiveKeySubstrings)
        {
            if (normalized.Contains(marker, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static void WriteMaskedElement(
        Utf8JsonWriter writer,
        JsonElement element,
        HashSet<string> redactedFields)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);
                    if (IsSensitivePropertyName(property.Name))
                    {
                        if (property.Value.ValueKind == JsonValueKind.Null)
                        {
                            writer.WriteNullValue();
                        }
                        else
                        {
                            redactedFields.Add(property.Name);
                            writer.WriteStringValue(RedactedPlaceholder);
                        }
                    }
                    else
                    {
                        WriteMaskedPropertyValue(writer, property.Name, property.Value, redactedFields);
                    }
                }

                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteMaskedElement(writer, item, redactedFields);
                }

                writer.WriteEndArray();
                break;

            case JsonValueKind.String:
                WriteMaskedStringScalar(writer, null, element.GetString(), redactedFields);
                break;

            default:
                element.WriteTo(writer);
                break;
        }
    }

    private static void WriteMaskedPropertyValue(
        Utf8JsonWriter writer,
        string propertyName,
        JsonElement value,
        HashSet<string> redactedFields)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            WriteMaskedStringScalar(writer, propertyName, value.GetString(), redactedFields);
            return;
        }

        WriteMaskedElement(writer, value, redactedFields);
    }

    private static void WriteMaskedStringScalar(
        Utf8JsonWriter writer,
        string? propertyName,
        string? strValue,
        HashSet<string> redactedFields)
    {
        if (strValue is null)
        {
            writer.WriteNullValue();
            return;
        }

        var trimmed = strValue.Trim();
        if ((trimmed.StartsWith('{') && trimmed.EndsWith('}')) ||
            (trimmed.StartsWith('[') && trimmed.EndsWith(']')))
        {
            try
            {
                using var nestedDoc = JsonDocument.Parse(trimmed);
                using var nestedStream = new MemoryStream();
                var nestedRedacted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using (var nestedWriter = new Utf8JsonWriter(nestedStream))
                {
                    WriteMaskedElement(nestedWriter, nestedDoc.RootElement, nestedRedacted);
                }

                if (nestedRedacted.Count > 0)
                {
                    foreach (var field in nestedRedacted)
                    {
                        redactedFields.Add(propertyName is null ? field : $"{propertyName}.{field}");
                    }

                    writer.WriteStringValue(Encoding.UTF8.GetString(nestedStream.ToArray()));
                    return;
                }
            }
            catch (JsonException)
            {
                // Fall through to plain-text scrubbing when string only looks like JSON.
            }
        }

        var scrubbed = MaskPlainText(strValue) ?? string.Empty;
        if (!string.Equals(scrubbed, strValue, StringComparison.Ordinal))
        {
            redactedFields.Add(propertyName ?? "value");
        }

        writer.WriteStringValue(scrubbed);
    }

    private static string NormalizeKey(string key)
    {
        var sb = new StringBuilder(key.Length);
        foreach (var ch in key)
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
            }
        }

        return sb.ToString();
    }

    private static bool HasDiscreteWordToken(string propertyName, string targetWord)
    {
        var current = new StringBuilder();
        for (var i = 0; i < propertyName.Length; i++)
        {
            var ch = propertyName[i];
            if (!char.IsLetterOrDigit(ch))
            {
                if (current.Length > 0 && string.Equals(current.ToString(), targetWord, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                current.Clear();
                continue;
            }

            if (current.Length > 0 &&
                char.IsUpper(ch) &&
                (char.IsLower(propertyName[i - 1]) ||
                 (i + 1 < propertyName.Length && char.IsLower(propertyName[i + 1]))))
            {
                if (string.Equals(current.ToString(), targetWord, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                current.Clear();
            }

            current.Append(ch);
        }

        return current.Length > 0 &&
               string.Equals(current.ToString(), targetWord, StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"\b(Bearer|Apikey|Basic)\s+[A-Za-z0-9\-._~+/]+=*", RegexOptions.IgnoreCase)]
    private static partial Regex AuthHeaderSchemeRegex();

    [GeneratedRegex(@"\b(password|passwd|secret|api[_-]?key|webhook[_-]?api[_-]?key|access[_-]?token|refresh[_-]?token|otp)(\s*[:=]\s*)([^\s,;""'}]+)", RegexOptions.IgnoreCase)]
    private static partial Regex InlineKeyValueSecretRegex();
}
