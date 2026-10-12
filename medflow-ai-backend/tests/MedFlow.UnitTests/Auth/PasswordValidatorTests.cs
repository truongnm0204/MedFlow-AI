using System.Text.RegularExpressions;

namespace MedFlow.UnitTests.Auth;

/// <summary>Password policy giống RegisterRequest.RegularExpression.</summary>
public sealed class PasswordValidatorTests
{
    // Mirror RegisterRequest.Password regex: ≥8, có hoa, thường, số.
    private static readonly Regex Policy = new(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d).+$", RegexOptions.Compiled);

    [Theory]
    [InlineData("MedFlow@2026")]
    [InlineData("Abcdefg1")]
    [InlineData("Passw0rd!")]
    public void ValidPassword_MatchesPolicy(string password)
    {
        Assert.True(password.Length >= 8);
        Assert.Matches(Policy, password);
    }

    [Theory]
    [InlineData("abcdefgh")]   // không hoa, không số
    [InlineData("ABCDEFGH")]   // không thường, không số
    [InlineData("12345678")]   // không chữ
    [InlineData("Abcdefgh")]   // không số
    public void InvalidPassword_FailsPolicy(string password)
    {
        Assert.DoesNotMatch(Policy, password);
    }

    [Theory]
    [InlineData("0912345678")]
    [InlineData("0356789012")]
    [InlineData("0789012345")]
    [InlineData("0891234567")]
    public void ValidPhone_MatchesPattern(string phone)
    {
        Assert.Matches(@"^0[35789]\d{8}$", phone);
    }

    [Theory]
    [InlineData("0123456789")]  // đầu 01 — sai pattern
    [InlineData("091234567")]   // thiếu 1 số
    [InlineData("09123456789")] // thừa 1 số
    [InlineData("912345678")]   // thiếu số 0 đầu
    public void InvalidPhone_FailsPattern(string phone)
    {
        Assert.DoesNotMatch(@"^0[35789]\d{8}$", phone);
    }
}
