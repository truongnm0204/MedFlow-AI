using System.Text.Json;

namespace MedFlow.Application.Services.Ekyc;

/// <summary>
/// Metadata-only extraction from VNPT proxy request/response bodies.
/// STRICT RULE: extracts object-hash references and parse-failure signals ONLY — never reads,
/// copies or retains image content, scan3d payloads or base64 blobs. Every extraction path
/// treats values as opaque short strings (hash refs are ~40-80 chars; anything longer is rejected).
///
/// Shape assumptions (from probe evidence Q1/Q3 — server-to-server captures):
/// <list type="bullet">
///   <item>addFile RESPONSE: <c>{ "object": { "hash": "idg20261127/IDG01_&lt;uuid&gt;", ... } }</c>.</item>
///   <item>inference REQUEST bodies: JSON with <c>img</c> / <c>img_front</c> / <c>img_back</c> string fields
///         whose values are the hash refs issued by addFile (NOT raw base64 — probe confirmed hash-ref flow).</item>
///   <item>compare REQUEST (<c>/ai/v1/web/face/compare</c>): ASSUMPTION — document reference in
///         <c>img_front</c> (fallback <c>img_back</c>/<c>img</c>). Probe never captured a real compare body
///         (browser flow blocked by domain check), so this is the best-evidence guess and MUST be
///         verified in the first volunteer e2e run — fail-closed design means a wrong assumption
///         yields UNVERIFIABLE_BINDING (safe), never a false pass.</item>
/// </list>
/// </summary>
internal static class EkycProxyMetadataParser
{
    /// <summary>Max length accepted for a hash ref. Real refs look like "idg20261127/IDG01_&lt;uuid&gt;".
    /// Anything longer is almost certainly embedded base64 content — reject to stay metadata-only.</summary>
    public const int MaxHashLength = 256;

    private static readonly string[] RequestHashFields = ["img", "img_front", "img_back"];

    /// <summary>Compare endpoint document-reference fields in priority order — see class-level assumption note.</summary>
    private static readonly string[] CompareDocumentFields = ["img_front", "img_back", "img"];

    // ------------------------------------------------------------------
    // addFile response: extract the server-issued upload hash
    // ------------------------------------------------------------------

    /// <summary>
    /// Extracts <c>object.hash</c> from an addFile response body.
    /// Returns null + sets parseWarning when the shape diverges (never throws).
    /// </summary>
    public static string? ExtractAddFileHash(string? responseBody, out string? parseWarning)
    {
        parseWarning = null;
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            parseWarning = "empty_body";
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                parseWarning = "root_not_object";
                return null;
            }

            if (!doc.RootElement.TryGetProperty("object", out var objectElement)
                || objectElement.ValueKind != JsonValueKind.Object)
            {
                parseWarning = "missing_object_field";
                return null;
            }

            if (!objectElement.TryGetProperty("hash", out var hashElement)
                || hashElement.ValueKind != JsonValueKind.String)
            {
                parseWarning = "missing_hash_field";
                return null;
            }

            var hash = hashElement.GetString();
            if (string.IsNullOrWhiteSpace(hash) || hash.Length > MaxHashLength)
            {
                parseWarning = "hash_field_invalid";
                return null;
            }

            return hash;
        }
        catch (JsonException)
        {
            parseWarning = "malformed_json";
            return null;
        }
    }

    // ------------------------------------------------------------------
    // inference response: extract signed evidence (dataSign + dataBase64)
    // ------------------------------------------------------------------

    /// <summary>
    /// Extracts the <c>dataSign</c>/<c>dataBase64</c> pair from an AI-endpoint response body.
    /// Probe Q3: EVERY AI response (even 4xx/5xx) carries these fields — they are the only
    /// verifiable evidence finalize has, since no server-side re-query API exists.
    /// Shape assumption: fields at response root, with a defensive fallback to the nested
    /// <c>object</c> element. Both are textual cryptographic material — never image content.
    /// Returns false when either field is absent/unparseable (never throws).
    /// </summary>
    public static bool TryExtractSignedEvidence(
        string? responseBody,
        out string? dataSign,
        out string? dataBase64)
    {
        dataSign = null;
        dataBase64 = null;

        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (TryReadEvidencePair(doc.RootElement, out dataSign, out dataBase64))
            {
                return true;
            }

            if (doc.RootElement.TryGetProperty("object", out var objectElement)
                && objectElement.ValueKind == JsonValueKind.Object)
            {
                return TryReadEvidencePair(objectElement, out dataSign, out dataBase64);
            }

            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryReadEvidencePair(
        JsonElement element,
        out string? dataSign,
        out string? dataBase64)
    {
        dataSign = null;
        dataBase64 = null;

        if (!element.TryGetProperty("dataSign", out var signElement)
            || signElement.ValueKind != JsonValueKind.String
            || !element.TryGetProperty("dataBase64", out var payloadElement)
            || payloadElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var sign = signElement.GetString();
        var payload = payloadElement.GetString();
        if (string.IsNullOrWhiteSpace(sign) || string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        dataSign = sign;
        dataBase64 = payload;
        return true;
    }

    // ------------------------------------------------------------------
    // inference request: extract hash refs (img / img_front / img_back)
    // ------------------------------------------------------------------

    /// <summary>
    /// Extracts object-hash refs from an inference request body (img/img_front/img_back fields).
    /// Values longer than <see cref="MaxHashLength"/> are treated as embedded content and skipped —
    /// metadata-only guarantee. Returns empty list + parseWarning on shape divergence.
    /// </summary>
    public static IReadOnlyList<string> ExtractRequestHashes(string? requestBody, out string? parseWarning)
    {
        parseWarning = null;
        if (string.IsNullOrWhiteSpace(requestBody))
        {
            // Empty body is not necessarily an error (multipart requests carry no JSON body fields) —
            // no warning, just no hashes.
            return [];
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(requestBody);
        }
        catch (JsonException)
        {
            // Non-JSON bodies (multipart form-data from addFile uploads) are expected — not a warning.
            parseWarning = null;
            return [];
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                parseWarning = "root_not_object";
                return [];
            }

            var hashes = new List<string>(3);
            foreach (var field in RequestHashFields)
            {
                if (!doc.RootElement.TryGetProperty(field, out var element)
                    || element.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var value = element.GetString();
                if (string.IsNullOrWhiteSpace(value) || value.Length > MaxHashLength)
                {
                    continue; // embedded content or empty — not a hash ref, skip silently (never copy it)
                }

                hashes.Add(value);
            }

            return hashes;
        }
    }

    // ------------------------------------------------------------------
    // compare request: extract the DOCUMENT reference for binding validation
    // ------------------------------------------------------------------

    /// <summary>
    /// Extracts the document hash a face-compare call references. ASSUMPTION documented at class level:
    /// <c>img_front</c> first, then <c>img_back</c>, then <c>img</c>. Returns null when nothing parseable —
    /// callers MUST treat null as UNVERIFIABLE_BINDING (fail-closed).
    /// </summary>
    public static string? ExtractCompareDocumentHash(string? requestBody, out string? parseWarning)
    {
        parseWarning = null;
        if (string.IsNullOrWhiteSpace(requestBody))
        {
            parseWarning = "empty_compare_body";
            return null;
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(requestBody);
        }
        catch (JsonException)
        {
            parseWarning = "malformed_compare_json";
            return null;
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                parseWarning = "compare_root_not_object";
                return null;
            }

            foreach (var field in CompareDocumentFields)
            {
                if (!doc.RootElement.TryGetProperty(field, out var element)
                    || element.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var value = element.GetString();
                if (string.IsNullOrWhiteSpace(value) || value.Length > MaxHashLength)
                {
                    continue;
                }

                return value;
            }

            parseWarning = "missing_compare_document_ref";
            return null;
        }
    }
}
