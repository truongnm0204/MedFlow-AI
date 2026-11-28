using System.Security.Cryptography;
using System.Text;
using MedFlow.Application.Options;
using Microsoft.Extensions.Options;

namespace MedFlow.Application.Services.Ekyc;

/// <summary>
/// Pure cryptographic primitives for the eKYC evidence chain:
/// <list type="bullet">
///   <item>AES-256-GCM encryption/decryption of the VNPT dataBase64 payload at rest
///         (evidence_payload_full). Key from env <c>Ekyc:EncryptionKeyBase64</c> — never DB,
///         never hardcoded. Output layout: nonce(12B) | tag(16B) | ciphertext.</item>
///   <item>HMAC-SHA256 of the normalized national ID for dedupe lookup
///         (identification_hmac). Key precedence: <c>Ekyc:HmacKey</c> → <c>Identity:HashKey</c>
///         (same server-secret family as patients.identification_hash_hmac so dedupe can
///         cross-match existing patient rows).</item>
///   <item>SHA-256 hex digest of the raw dataBase64 string (evidence_payload_digest) —
///         allows integrity re-verification without storing the payload.</item>
/// </list>
/// Rationale for reimplementing rather than reusing <c>IdentificationNumber.ComputeHmac</c>:
/// that helper is internal to the Patients service and HMACs the SHA256 hex text of the ID;
/// cross-matching requires the exact same construction, so this implementation delegates to
/// the identical byte-level construction and stays consistent. New code — no existing
/// general-purpose AES helper exists in the codebase (grep: only OTP/identity HMAC usage).
/// </summary>
internal static class EkycCrypto
{
    public const string MissingEncryptionKeyMessage = "Ekyc:EncryptionKeyBase64 chưa được cấu hình hoặc không phải base64 của khóa 32-byte.";
    public const string MissingHmacKeyMessage = "Ekyc:HmacKey (hoặc Identity:HashKey fallback) chưa được cấu hình.";

    private const int NonceSizeBytes = 12;  // AES-GCM standard nonce
    private const int TagSizeBytes = 16;    // AES-GCM 128-bit auth tag
    private const int KeySizeBytes = 32;    // AES-256

    // ------------------------------------------------------------------
    // Key resolution
    // ------------------------------------------------------------------

    public static byte[] RequireEncryptionKey(IOptions<EkycOptions>? options)
    {
        var raw = options?.Value.EncryptionKeyBase64;
        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException(MissingEncryptionKeyMessage);

        byte[] key;
        try
        {
            key = Convert.FromBase64String(raw.Trim());
        }
        catch (FormatException)
        {
            throw new InvalidOperationException(MissingEncryptionKeyMessage);
        }

        if (key.Length != KeySizeBytes)
            throw new InvalidOperationException(MissingEncryptionKeyMessage);

        return key;
    }

    /// <summary>
    /// HMAC key resolution with fallback to the Identity hash key — same secret family as
    /// patients.identification_hash_hmac so cross-dedupe against existing patient rows works.
    /// </summary>
    public static string RequireHmacKey(
        IOptions<EkycOptions>? ekycOptions,
        IOptions<IdentityHashOptions>? identityOptions)
    {
        var key = ekycOptions?.Value.HmacKey;
        if (!string.IsNullOrWhiteSpace(key))
            return key.Trim();

        key = identityOptions?.Value.HashKey;
        if (!string.IsNullOrWhiteSpace(key))
            return key.Trim();

        throw new InvalidOperationException(MissingHmacKeyMessage);
    }

    // ------------------------------------------------------------------
    // AES-256-GCM at-rest encryption (evidence_payload_full)
    // ------------------------------------------------------------------

    /// <summary>
    /// Encrypts plaintext with AES-256-GCM. Output: nonce(12) | tag(16) | ciphertext.
    /// Fresh random nonce per call — never reuse (key, nonce) pairs.
    /// </summary>
    public static byte[] Encrypt(byte[] plaintext, byte[] key)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length != KeySizeBytes)
            throw new ArgumentException("Key must be 32 bytes (AES-256).", nameof(key));

        var nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSizeBytes];

        using (var aes = new AesGcm(key, TagSizeBytes))
        {
            aes.Encrypt(nonce, plaintext, ciphertext, tag);
        }

        var output = new byte[NonceSizeBytes + TagSizeBytes + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, output, 0, NonceSizeBytes);
        Buffer.BlockCopy(tag, 0, output, NonceSizeBytes, TagSizeBytes);
        Buffer.BlockCopy(ciphertext, 0, output, NonceSizeBytes + TagSizeBytes, ciphertext.Length);
        return output;
    }

    /// <summary>Decrypts output produced by <see cref="Encrypt(byte[], byte[])"/>. Throws on tamper.</summary>
    public static byte[] Decrypt(byte[] encrypted, byte[] key)
    {
        ArgumentNullException.ThrowIfNull(encrypted);
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length != KeySizeBytes)
            throw new ArgumentException("Key must be 32 bytes (AES-256).", nameof(key));
        if (encrypted.Length < NonceSizeBytes + TagSizeBytes)
            throw new CryptographicException("Encrypted payload too short.");

        var nonce = encrypted.AsSpan(0, NonceSizeBytes);
        var tag = encrypted.AsSpan(NonceSizeBytes, TagSizeBytes);
        var ciphertext = encrypted.AsSpan(NonceSizeBytes + TagSizeBytes);

        var plaintext = new byte[ciphertext.Length];
        using (var aes = new AesGcm(key, TagSizeBytes))
        {
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
        }

        return plaintext;
    }

    // ------------------------------------------------------------------
    // Digests + HMAC
    // ------------------------------------------------------------------

    /// <summary>SHA-256 lowercase hex digest of the raw dataBase64 string (UTF-8 bytes, not decoded).</summary>
    public static string ComputePayloadDigest(string dataBase64)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataBase64);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(dataBase64))).ToLowerInvariant();
    }

    /// <summary>
    /// HMAC-SHA256 lowercase hex over the SHA-256 hex digest of the canonical national ID —
    /// byte-identical construction to <c>IdentificationNumber.ComputeHmac</c> so values
    /// cross-match patients.identification_hash_hmac for dedupe.
    /// </summary>
    public static string ComputeIdentificationHmac(string canonicalIdentification, string hmacKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalIdentification);
        ArgumentException.ThrowIfNullOrWhiteSpace(hmacKey);

        var keyBytes = Encoding.UTF8.GetBytes(hmacKey);
        var shaHex = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalIdentification))).ToLowerInvariant();
        var digest = Encoding.UTF8.GetBytes(shaHex);
        return Convert.ToHexString(HMACSHA256.HashData(keyBytes, digest)).ToLowerInvariant();
    }

    public static bool FixedTimeEquals(string a, string b)
        => CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}
