using MedFlow.Application.Services.Patients;
using MedFlow.Domain.Exceptions;

namespace MedFlow.UnitTests.Patients;

public sealed class IdentificationNumberTests
{
    [Theory]
    [InlineData("012345678")]
    [InlineData("012345678901")]
    public void Normalize_PreservesNineOrTwelveDigits(string value)
    {
        Assert.Equal(value, IdentificationNumber.Normalize(value));
    }

    [Theory]
    [InlineData(" 012 345 678 ")]
    [InlineData("012-345-678")]
    [InlineData(" 012-345-678-901 ")]
    public void Normalize_RemovesSpacesAndDashesWithoutChangingLength(string value)
    {
        var result = IdentificationNumber.Normalize(value);

        Assert.Matches(@"^\d{9}$|^\d{12}$", result);
        Assert.DoesNotContain(" ", result);
        Assert.DoesNotContain("-", result);
    }

    [Theory]
    [InlineData("12345678")]
    [InlineData("1234567890")]
    [InlineData("12345678901")]
    [InlineData("1234567890123")]
    [InlineData("abcdefghi")]
    [InlineData("01234567890a")]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_RejectsInvalidFormats(string value)
    {
        var ex = Assert.Throws<ValidationException>(() => IdentificationNumber.Normalize(value));

        Assert.Equal(IdentificationNumber.InvalidMessage, ex.Message);
    }

    [Fact]
    public void Normalize_DoesNotConvertBetweenNineAndTwelveDigits()
    {
        var nine = IdentificationNumber.Normalize("012345678");
        var twelve = IdentificationNumber.Normalize("012345678901");

        Assert.Equal("012345678", nine);
        Assert.Equal("012345678901", twelve);
        Assert.NotEqual(nine, twelve);
    }

    [Fact]
    public void ComputeSha256_IsStableAndLowercaseHex()
    {
        var first = IdentificationNumber.ComputeSha256("012345678901");
        var second = IdentificationNumber.ComputeSha256("012345678901");

        Assert.Equal(first, second);
        Assert.Matches("^[0-9a-f]{64}$", first);
    }
}
