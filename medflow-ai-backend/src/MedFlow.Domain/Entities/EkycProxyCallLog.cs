namespace MedFlow.Domain.Entities;

/// <summary>
/// Per-call metadata for one request the backend proxied to VNPT on behalf of an eKYC session.
/// Maps to <c>ekyc_proxy_call_logs</c> (migration 030).
/// METADATA ONLY — never images, never base64 payloads, never OCR text.
/// Exists for finalize audit, step-binding validation and e2e debug signals.
/// </summary>
public class EkycProxyCallLog
{
    public long Id { get; set; }
    public long SessionId { get; set; }

    /// <summary>VNPT endpoint path relative to backend_url (e.g. /ai/v1/web/ocr/id/front).</summary>
    public string EndpointPath { get; set; } = string.Empty;

    public string HttpMethod { get; set; } = "POST";
    public int HttpStatus { get; set; }

    /// <summary>JSON array of object-hash refs extracted from the request body (img/img_front/img_back).</summary>
    public string? RequestObjectHashes { get; set; }

    /// <summary>object.hash extracted from an addFile response — server-issued upload reference.</summary>
    public string? ResponseObjectHash { get; set; }

    /// <summary>dataSign (base64 RSA signature over the dataBase64 string) captured from the response.
    /// Cryptographic proof material for finalize verification — not secret, not an image.</summary>
    public string? ResponseDataSign { get; set; }

    /// <summary>dataBase64 response payload encrypted at rest with AES-256-GCM (EkycCrypto).
    /// Plaintext is OCR JSON text (personal data — never images); decrypted only during finalize.</summary>
    public byte[]? ResponsePayloadEncrypted { get; set; }

    /// <summary>Machine-readable parse-failure reason when body shape diverges from assumptions.</summary>
    public string? ParseWarning { get; set; }

    public int? DurationMs { get; set; }
    public DateTime CreatedAt { get; set; }
}
