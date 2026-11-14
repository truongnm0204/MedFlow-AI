using System.Globalization;
using System.Text;
using MedFlow.Application.Services.Payments;

namespace MedFlow.UnitTests.Payments;

public sealed class VietQrPayloadBuilderTests
{
    private static readonly VietQrAccount Account = new("970422", "0123456789", "BENH VIEN DEMO");

    [Fact]
    public void MerchantName_StripsVietnameseMarks()
    {
        Assert.Equal("BENH VIEN DA KHOA", VietQrPayloadBuilder.MerchantName("Bệnh viện đa khoa"));
    }

    [Fact]
    public void MerchantName_EmptyFallsBack()
    {
        Assert.Equal(VietQrPayloadBuilder.FallbackMerchantName, VietQrPayloadBuilder.MerchantName("   "));
    }

    [Fact]
    public void Build_ContainsEmvTagsAndIndependentCrc()
    {
        var payload = VietQrPayloadBuilder.Build(Account, 150_000m, "MF18", "BENH VIEN");

        Assert.StartsWith("000201010212", payload, StringComparison.Ordinal);
        Assert.Contains("A000000727", payload, StringComparison.Ordinal);
        Assert.Contains("970422", payload, StringComparison.Ordinal);
        Assert.Contains("0123456789", payload, StringComparison.Ordinal);
        Assert.Contains("QRIBFTTA", payload, StringComparison.Ordinal);
        Assert.Contains("5303704", payload, StringComparison.Ordinal);
        Assert.Contains("5406150000", payload, StringComparison.Ordinal);
        Assert.Contains("5802VN", payload, StringComparison.Ordinal);
        Assert.Contains("MF18", payload, StringComparison.Ordinal);
        Assert.Equal(payload[^4..], IndependentCrc(payload[..^4]));
        Assert.DoesNotContain(".", payload, StringComparison.Ordinal);
    }

    private static string IndependentCrc(string payload)
    {
        ushort crc = 0xFFFF;
        foreach (var b in Encoding.ASCII.GetBytes(payload))
        {
            crc ^= (ushort)(b << 8);
            for (var i = 0; i < 8; i++)
                crc = (crc & 0x8000) != 0 ? (ushort)((crc << 1) ^ 0x1021) : (ushort)(crc << 1);
        }

        return crc.ToString("X4", CultureInfo.InvariantCulture);
    }
}
