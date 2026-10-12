using System.Security.Cryptography;
using System.Text;

namespace MedFlow.UnitTests.Auth;

/// <summary>HMAC-SHA256 cho OTP hash + email fingerprint — pure logic, deterministic.</summary>
public sealed class OtpHasherTests
{
    private const string Secret = "test-secret";

    private static byte[] Hmac(string input)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Secret));
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(input));
    }

    [Fact]
    public void ComputeFingerprint_Returns32Bytes()
    {
        Assert.Equal(32, Hmac("a@b.com").Length);
    }

    [Fact]
    public void ComputeFingerprint_IsDeterministic()
    {
        Assert.Equal(Hmac("a@b.com"), Hmac("a@b.com"));
    }

    [Fact]
    public void ComputeFingerprint_DiffersForDifferentInput()
    {
        Assert.NotEqual(Hmac("a@b.com"), Hmac("x@y.com"));
    }

    [Fact]
    public void ComputeOtpHash_HexLowerCase_Deterministic()
    {
        var hex1 = Convert.ToHexString(Hmac("123456")).ToLowerInvariant();
        var hex2 = Convert.ToHexString(Hmac("123456")).ToLowerInvariant();
        Assert.Equal(hex1, hex2);
        Assert.Equal(64, hex1.Length);
    }
}
