using System.Security.Cryptography;
using System.Text;

namespace MedFlow.Application.Services.Ekyc;

/// <summary>
/// Server-side verification of VNPT <c>dataSign</c> signatures — the cryptographic trust anchor
/// of the whole eKYC pipeline (probe Q2, CRACKED):
/// <list type="bullet">
///   <item>Algorithm: RSA-SHA256, padding PKCS#1 v1.5.</item>
///   <item>Signed material: the <c>dataBase64</c> STRING VERBATIM — the base64 text encoded UTF-8.
///         NOT the decoded payload, NOT a concatenation of hash fields.</item>
///   <item>Key: <c>PUBLIC_KEY_CA</c> from env config (base64 DER/SPKI).</item>
/// </list>
/// Probe evidence: two independent captures verify true; tampering the last 4 chars of the
/// base64 string verifies false; using TOKEN_KEY instead of CA verifies false.
///
/// SECURITY NOTE (probe Q2 warning, documented): the trial CA key is RSA 512-bit — weak by
/// modern standards and factored in practice. Verification still proves integrity/authenticity
/// within the VNPT trust model, but production deployment MUST confirm the production CA key
/// size with VNPT (question D1) before relying on dataSign as sole root-of-trust.
/// </summary>
internal static class EkycDataSignVerifier
{
    /// <summary>
    /// Verifies <paramref name="dataSign"/> (base64 RSA signature) over the verbatim
    /// <paramref name="dataBase64"/> string using the PEM/DER-SPKI public key material in
    /// <paramref name="publicKeyCaBase64"/> (base64 DER). Returns false on ANY failure —
    /// malformed inputs, wrong key, tampered payload — never throws for verification-negative
    /// cases. Throws only when the configured key itself is unusable (config error).
    /// </summary>
    public static bool Verify(string? dataBase64, string? dataSign, string? publicKeyCaBase64)
    {
        if (string.IsNullOrWhiteSpace(dataBase64)
            || string.IsNullOrWhiteSpace(dataSign)
            || string.IsNullOrWhiteSpace(publicKeyCaBase64))
        {
            return false;
        }

        byte[] signature;
        byte[] keyDer;
        try
        {
            signature = Convert.FromBase64String(dataSign.Trim());
        }
        catch (FormatException)
        {
            return false;
        }

        try
        {
            keyDer = Convert.FromBase64String(publicKeyCaBase64.Trim());
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException(
                "Ekyc:PublicKeyCa không phải base64 hợp lệ của khóa DER/SPKI.", ex);
        }

        using var rsa = RSA.Create();
        try
        {
            rsa.ImportSubjectPublicKeyInfo(keyDer, out _);
        }
        catch (CryptographicException ex)
        {
            throw new InvalidOperationException(
                "Ekyc:PublicKeyCa không import được dưới dạng SubjectPublicKeyInfo (DER).", ex);
        }

        // The signed material is the base64 STRING ITSELF as UTF-8 — probe Q2 evidence.
        var signedMaterial = Encoding.UTF8.GetBytes(dataBase64);

        try
        {
            return rsa.VerifyData(
                signedMaterial,
                signature,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
        }
        catch (CryptographicException)
        {
            // Malformed signature length/shape — treat as verification failure, not error.
            return false;
        }
    }

    /// <summary>
    /// Decodes a VERIFIED dataBase64 string into its JSON payload bytes.
    /// MUST only be called after <see cref="Verify"/> returned true for the same pair —
    /// decoding unverified material would process attacker-controlled JSON as authentic.
    /// Returns null when the base64 is malformed (should not happen for verified material
    /// but stay defensive).
    /// </summary>
    public static byte[]? DecodeVerifiedPayload(string dataBase64)
    {
        try
        {
            return Convert.FromBase64String(dataBase64);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
