using System.Text;
using MedFlow.Application.Options;
using MedFlow.Application.Services.Ekyc;
using Microsoft.Extensions.Options;

namespace MedFlow.UnitTests.Ekyc;

public sealed class EkycCryptoTests
{
    private static readonly byte[] TestKey = new byte[32]
        .Select((_, i) => (byte)(i + 1)).ToArray();

    private static IOptions<EkycOptions> BuildOptions(
        string? encryptionKey = null,
        string? hmacKey = null)
    {
        return Options.Create(new EkycOptions
        {
            EncryptionKeyBase64 = encryptionKey,
            HmacKey = hmacKey,
        });
    }

    // ---------------------------------------------------------------
    // AES-256-GCM encryption round-trip
    // ---------------------------------------------------------------

    [Fact]
    public void EncryptDecrypt_RoundTripsPayload()
    {
        var plaintext = Encoding.UTF8.GetBytes("eyJ0eXBlX2RvY3VtZW50Ijo5fQ==");

        var encrypted = EkycCrypto.Encrypt(plaintext, TestKey);
        var decrypted = EkycCrypto.Decrypt(encrypted, TestKey);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Encrypt_ProducesFreshNoncePerCall()
    {
        var plaintext = Encoding.UTF8.GetBytes("same-input");

        var first = EkycCrypto.Encrypt(plaintext, TestKey);
        var second = EkycCrypto.Encrypt(plaintext, TestKey);

        // Same plaintext + same key must still yield different ciphertexts (random nonce).
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Encrypt_OutputLayoutIsNonceTagCiphertext()
    {
        var plaintext = Encoding.UTF8.GetBytes("abc");

        var encrypted = EkycCrypto.Encrypt(plaintext, TestKey);

        // 12B nonce + 16B tag + 3B ciphertext
        Assert.Equal(12 + 16 + plaintext.Length, encrypted.Length);
    }

    [Fact]
    public void Decrypt_RejectsTamperedCiphertext()
    {
        var plaintext = Encoding.UTF8.GetBytes("payload");
        var encrypted = EkycCrypto.Encrypt(plaintext, TestKey);

        encrypted[^1] ^= 0xFF; // flip last ciphertext bit
        // .NET 10 throws AuthenticationTagMismatchException (subclass of CryptographicException).
        Assert.Throws<System.Security.Cryptography.AuthenticationTagMismatchException>(
            () => EkycCrypto.Decrypt(encrypted, TestKey));
    }

    [Fact]
    public void Decrypt_RejectsTamperedTag()
    {
        var plaintext = Encoding.UTF8.GetBytes("payload");
        var encrypted = EkycCrypto.Encrypt(plaintext, TestKey);

        encrypted[12] ^= 0x01; // flip first tag byte

        Assert.Throws<System.Security.Cryptography.AuthenticationTagMismatchException>(
            () => EkycCrypto.Decrypt(encrypted, TestKey));
    }

    [Fact]
    public void Decrypt_RejectsWrongKey()
    {
        var plaintext = Encoding.UTF8.GetBytes("payload");
        var encrypted = EkycCrypto.Encrypt(plaintext, TestKey);

        var wrongKey = new byte[32];
        wrongKey[0] = 99;

        Assert.Throws<System.Security.Cryptography.AuthenticationTagMismatchException>(
            () => EkycCrypto.Decrypt(encrypted, wrongKey));
    }

    [Fact]
    public void Encrypt_RejectsWrongKeySize()
    {
        Assert.Throws<ArgumentException>(
            () => EkycCrypto.Encrypt([1, 2, 3], new byte[16]));
    }

    // ---------------------------------------------------------------
    // Key resolution
    // ---------------------------------------------------------------

    [Fact]
    public void RequireEncryptionKey_AcceptsValidBase64Key()
    {
        var keyBytes = new byte[32];
        keyBytes[0] = 7;
        var options = BuildOptions(encryptionKey: Convert.ToBase64String(keyBytes));

        var resolved = EkycCrypto.RequireEncryptionKey(options);

        Assert.Equal(32, resolved.Length);
        Assert.Equal(7, resolved[0]);
    }

    [Fact]
    public void RequireEncryptionKey_RejectsMissingKey()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => EkycCrypto.RequireEncryptionKey(BuildOptions()));

        Assert.Equal(EkycCrypto.MissingEncryptionKeyMessage, ex.Message);
    }

    [Fact]
    public void RequireEncryptionKey_RejectsNonBase64Key()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => EkycCrypto.RequireEncryptionKey(BuildOptions(encryptionKey: "!!!not-base64!!!")));

        Assert.Equal(EkycCrypto.MissingEncryptionKeyMessage, ex.Message);
    }

    [Fact]
    public void RequireEncryptionKey_RejectsWrongLengthKey()
    {
        var shortKey = Convert.ToBase64String(new byte[16]);

        var ex = Assert.Throws<InvalidOperationException>(
            () => EkycCrypto.RequireEncryptionKey(BuildOptions(encryptionKey: shortKey)));

        Assert.Equal(EkycCrypto.MissingEncryptionKeyMessage, ex.Message);
    }

    // ---------------------------------------------------------------
    // HMAC key resolution
    // ---------------------------------------------------------------

    [Fact]
    public void RequireHmacKey_PrefersEkycKey()
    {
        var ekyc = BuildOptions(hmacKey: "ekyc-secret");
        var identity = Options.Create(new IdentityHashOptions { HashKey = "identity-secret" });

        Assert.Equal("ekyc-secret", EkycCrypto.RequireHmacKey(ekyc, identity));
    }

    [Fact]
    public void RequireHmacKey_FallsBackToIdentityHashKey()
    {
        var ekyc = BuildOptions();
        var identity = Options.Create(new IdentityHashOptions { HashKey = "identity-secret" });

        Assert.Equal("identity-secret", EkycCrypto.RequireHmacKey(ekyc, identity));
    }

    [Fact]
    public void RequireHmacKey_ThrowsWhenNeitherConfigured()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => EkycCrypto.RequireHmacKey(BuildOptions(), Options.Create(new IdentityHashOptions())));

        Assert.Equal(EkycCrypto.MissingHmacKeyMessage, ex.Message);
    }

    // ---------------------------------------------------------------
    // HMAC + digest construction
    // ---------------------------------------------------------------

    [Fact]
    public void ComputeIdentificationHmac_MatchesIdentificationNumberConstruction()
    {
        // Dedupe correctness requires byte-identical output to the patients-side helper:
        // HMAC-SHA256(key, UTF8(SHA256hex(canonical))) — cross-match with patients.identification_hash_hmac.
        const string canonical = "012345678901";
        const string key = "test-hmac-key";

        var ekycHmac = EkycCrypto.ComputeIdentificationHmac(canonical, key);
        var patientHmac = MedFlow.Application.Services.Patients.IdentificationNumber.ComputeHmac(canonical, key);

        Assert.Equal(patientHmac, ekycHmac);
    }

    [Fact]
    public void ComputeIdentificationHmac_IsStableAndLowercaseHex()
    {
        var first = EkycCrypto.ComputeIdentificationHmac("012345678901", "k");
        var second = EkycCrypto.ComputeIdentificationHmac("012345678901", "k");

        Assert.Equal(first, second);
        Assert.Equal(64, first.Length);
        Assert.Matches("^[0-9a-f]{64}$", first);
    }

    [Fact]
    public void ComputeIdentificationHmac_DiffersAcrossKeys()
    {
        var a = EkycCrypto.ComputeIdentificationHmac("012345678901", "key-a");
        var b = EkycCrypto.ComputeIdentificationHmac("012345678901", "key-b");

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void ComputePayloadDigest_IsSha256HexOfRawString()
    {
        const string dataBase64 = "eyJhIjoxfQ==";
        var expected = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(dataBase64))).ToLowerInvariant();

        Assert.Equal(expected, EkycCrypto.ComputePayloadDigest(dataBase64));
    }

    [Fact]
    public void FixedTimeEquals_MatchesEqualStrings()
    {
        Assert.True(EkycCrypto.FixedTimeEquals("abc", "abc"));
        Assert.False(EkycCrypto.FixedTimeEquals("abc", "abd"));
    }
}
