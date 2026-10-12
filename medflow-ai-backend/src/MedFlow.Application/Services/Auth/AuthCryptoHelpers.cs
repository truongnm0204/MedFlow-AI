using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace MedFlow.Application.Services.Auth;

/// <summary>Helpers thuần cho OTP HMAC + patient code — tách từ AuthService để giữ file nhỏ.</summary>
internal static class AuthCryptoHelpers
{
    public static string GetOtpSecret(IConfiguration configuration)
        => configuration["Auth:OtpSecret"]
            ?? throw new InvalidOperationException("Auth:OtpSecret not configured.");

    public static string GenerateOtp()
        => RandomNumberGenerator.GetInt32(100000, 999999).ToString();

    public static byte[] ComputeHmacBytes(string input, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(input));
    }

    public static string ComputeHmacHex(string input, string secret)
    {
        var bytes = ComputeHmacBytes(input, secret);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static bool FixedTimeEquals(string a, string b)
        => CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}
