using MedFlow.Application.Services.Ekyc;

namespace MedFlow.UnitTests.Ekyc;

/// <summary>
/// Verify-sign matrix per probe Q2 evidence: valid / tampered / wrong-key.
/// Test vectors generated with a real RSA-512 keypair (same construction as VNPT's CA —
/// RSA-SHA256, PKCS#1 v1.5, signed over the verbatim dataBase64 string as UTF-8).
/// The vectors mirror the probe's validation method exactly: two independent captures verified
/// true; tampering the last 4 chars of the base64 string verified false; wrong key verified false.
/// </summary>
public sealed class EkycDataSignVerifierTests
{
    // Generated RSA-512 test keypair (dev/test only — mirrors trial CA key size).
    private const string TestPublicKey =
        "MFwwDQYJKoZIhvcNAQEBBQADSwAwSAJBAOeLFfK2OXWZ+y+7QjrjvzHUnE9kQFLkUPA+wcrEcEOB9oWPGqRg8TpDClzg275jmTJvLyU9M5E2KDFsmETs9dMCAwEAAQ==";

    private const string WrongPublicKey =
        "MFwwDQYJKoZIhvcNAQEBBQADSwAwSAJBANaI55YrR3h/KJoZCB1gScQxkJl8zHLFxpLoK9uQezvgGjmJ8w9jfe1BdIHLht7eBEP7ZuJqwwdQekgpssb9YAkCAwEAAQ==";

    // OCR payload: CCCD fields + tampering legal, base64 of the JSON — signed with the test key.
    private const string OcrDataBase64 =
        "eyJvYmplY3QiOnsiaWQiOiIwMTIzNDU2Nzg5MDEiLCJuYW1lIjoiTkdVWUVOIFZBTiBBTiIsImJpcnRoX2RheSI6IjE1LzA2LzE5OTUiLCJnZW5kZXIiOiJOYW0iLCJvcmlnaW5fbG9jYXRpb24iOiJIYSBOb2kiLCJyZWNlbnRfbG9jYXRpb24iOiJUUCBIbyBDaGkgTWluaCIsIm5hdGlvbmFsaXR5IjoiVmlldCBOYW0iLCJ2YWxpZF9kYXRlIjoiMTUvMDYvMjA0MCIsImlzc3VlX2RhdGUiOiIxNS8wNi8yMDI1IiwiaXNzdWVfcGxhY2UiOiJDdWMgQ2FuaCBzYXQgUUxIQyB2ZSBUVFhIIiwiaWRfcHJvYiI6Ijk5LjUiLCJuYW1lX3Byb2IiOiI5OC4yIiwiYmlydGhfZGF5X3Byb2IiOiI5OS4xIiwidGFtcGVyaW5nIjp7ImlzX2xlZ2FsIjoieWVzIiwid2FybmluZyI6W119fSwidHlwZV9kb2N1bWVudCI6OSwiY2hhbGxlbmdlQ29kZSI6IjExMTExIiwic2VydmVyX3ZlcnNpb24iOiIyLjEuMyJ9";

    private const string OcrDataSign = "nGMvNHpOq8BxWIjSf9nvRVG0hZbnxczUiopvMbukM/lefImyUBku7k9EAs93tSuR2DHvKVXf4sytblF6PAm7ng==";

    // Same payload with the LAST 4 base64 chars modified — probe's exact negative control.
    private const string TamperedDataBase64 =
        "eyJvYmplY3QiOnsiaWQiOiIwMTIzNDU2Nzg5MDEiLCJuYW1lIjoiTkdVWUVOIFZBTiBBTiIsImJpcnRoX2RheSI6IjE1LzA2LzE5OTUiLCJnZW5kZXIiOiJOYW0iLCJvcmlnaW5fbG9jYXRpb24iOiJIYSBOb2kiLCJyZWNlbnRfbG9jYXRpb24iOiJUUCBIbyBDaGkgTWluaCIsIm5hdGlvbmFsaXR5IjoiVmlldCBOYW0iLCJ2YWxpZF9kYXRlIjoiMTUvMDYvMjA0MCIsImlzc3VlX2RhdGUiOiIxNS8wNi8yMDI1IiwiaXNzdWVfcGxhY2UiOiJDdWMgQ2FuaCBzYXQgUUxIQyB2ZSBUVFhIIiwiaWRfcHJvZiI6Ijk5LjUiLCJuYW1lX3Byb2IiOiI5OC4yIiwiYmlydGhfZGF5X3Byb2IiOiI5OS4xIiwidGFtcGVyaW5nIjp7ImlzX2xlZ2FsIjoieWVzIiwid2FybmluZyI6W119fSwidHlwZV9kb2N1bWVudCI6OSwiY2hhbGxlbmdlQ29kZSI6IjExMTExIiwic2VydmVyX3ZlcnNpb24iOiIyLjEuAAAA";

    private const string LivenessDataBase64 = "eyJvYmplY3QiOnsibGl2ZW5lc3MiOiJzdWNjZXNzIiwibGl2ZW5lc3NfbXNnIjoiT0sifSwiY2hhbGxlbmdlQ29kZSI6IjExMTExIn0=";
    private const string LivenessDataSign = "4EXpAJxTZ+EwA63gAqh7eEUWiH4BBElfKILn50cSLarVKHJS+Nxo8tTDWPduyYWi3owbVrJ4ryOEtge2TcJMVg==";

    // ---------------------------------------------------------------
    // Positive cases — probe Q2: two independent captures verify true
    // ---------------------------------------------------------------

    [Fact]
    public void Verify_ValidOcrSignature_ReturnsTrue()
    {
        Assert.True(EkycDataSignVerifier.Verify(OcrDataBase64, OcrDataSign, TestPublicKey));
    }

    [Fact]
    public void Verify_ValidLivenessSignature_ReturnsTrue()
    {
        // Second independent capture — different payload, same key family.
        Assert.True(EkycDataSignVerifier.Verify(LivenessDataBase64, LivenessDataSign, TestPublicKey));
    }

    // ---------------------------------------------------------------
    // Negative controls — probe Q2 matrix
    // ---------------------------------------------------------------

    [Fact]
    public void Verify_TamperedBase64Tail_ReturnsFalse()
    {
        // Tamper: last 4 chars of the base64 string modified — integrity protection must catch it.
        Assert.False(EkycDataSignVerifier.Verify(TamperedDataBase64, OcrDataSign, TestPublicKey));
    }

    [Fact]
    public void Verify_WrongKey_ReturnsFalse()
    {
        // Probe negative control: TOKEN_KEY (different key) must NOT verify CA-signed material.
        Assert.False(EkycDataSignVerifier.Verify(OcrDataBase64, OcrDataSign, WrongPublicKey));
    }

    [Fact]
    public void Verify_SwappedSignatureAcrossPayloads_ReturnsFalse()
    {
        // Signature belongs to the OCR payload, not the liveness payload.
        Assert.False(EkycDataSignVerifier.Verify(LivenessDataBase64, OcrDataSign, TestPublicKey));
    }

    // ---------------------------------------------------------------
    // Malformed input resilience — never throws for verification-negative cases
    // ---------------------------------------------------------------

    [Fact]
    public void Verify_NullInputs_ReturnFalse()
    {
        Assert.False(EkycDataSignVerifier.Verify(null, OcrDataSign, TestPublicKey));
        Assert.False(EkycDataSignVerifier.Verify(OcrDataBase64, null, TestPublicKey));
        Assert.False(EkycDataSignVerifier.Verify(OcrDataBase64, OcrDataSign, null));
    }

    [Fact]
    public void Verify_MalformedSignatureBase64_ReturnsFalse()
    {
        Assert.False(EkycDataSignVerifier.Verify(OcrDataBase64, "!!!not-base64!!!", TestPublicKey));
    }

    [Fact]
    public void Verify_TruncatedSignature_ReturnsFalse()
    {
        var truncated = OcrDataSign[..20] + "==";
        Assert.False(EkycDataSignVerifier.Verify(OcrDataBase64, truncated, TestPublicKey));
    }

    [Fact]
    public void Verify_MalformedCaKey_ThrowsConfigError()
    {
        // A broken configured key is a CONFIG error, not a verification failure — must surface.
        Assert.Throws<InvalidOperationException>(
            () => EkycDataSignVerifier.Verify(OcrDataBase64, OcrDataSign, "%%%"));
    }

    // ---------------------------------------------------------------
    // DecodeVerifiedPayload — only meaningful post-verify
    // ---------------------------------------------------------------

    [Fact]
    public void DecodeVerifiedPayload_ValidBase64_ReturnsJsonBytes()
    {
        var decoded = EkycDataSignVerifier.DecodeVerifiedPayload(LivenessDataBase64);

        Assert.NotNull(decoded);
        var json = System.Text.Encoding.UTF8.GetString(decoded);
        Assert.Contains("\"liveness\"", json);
    }

    [Fact]
    public void DecodeVerifiedPayload_MalformedBase64_ReturnsNull()
    {
        Assert.Null(EkycDataSignVerifier.DecodeVerifiedPayload("not-valid-base64!!!"));
    }
}
