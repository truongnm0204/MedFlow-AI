using System.Globalization;
using System.Text;

namespace MedFlow.Application.Services.Payments;

internal static class VietQrPayloadBuilder
{
    public const string FallbackMerchantName = "MEDFLOW";

    public static string MerchantName(string? hospitalName)
    {
        if (string.IsNullOrWhiteSpace(hospitalName))
            return FallbackMerchantName;

        var folded = Fold(hospitalName);
        if (folded.Length == 0)
            return FallbackMerchantName;
        return folded.Length <= 25 ? folded : folded[..25];
    }

    public static string Build(VietQrAccount account, decimal amount, string transferContent, string merchantName)
    {
        var beneficiary = Tlv("00", account.Bin) + Tlv("01", account.AccountNumber);
        var merchantAccount = Tlv("00", "A000000727")
            + Tlv("01", beneficiary)
            + Tlv("02", "QRIBFTTA");
        var additional = Tlv("08", transferContent);
        var withoutCrc =
            Tlv("00", "01")
            + Tlv("01", "12")
            + Tlv("38", merchantAccount)
            + Tlv("53", "704")
            + Tlv("54", amount.ToString("0", CultureInfo.InvariantCulture))
            + Tlv("58", "VN")
            + Tlv("59", merchantName)
            + Tlv("62", additional)
            + "6304";
        return withoutCrc + Crc16(withoutCrc);
    }

    public static string Crc16(string payload)
    {
        ushort crc = 0xFFFF;
        foreach (var b in Encoding.ASCII.GetBytes(payload))
        {
            crc ^= (ushort)(b << 8);
            for (var i = 0; i < 8; i++)
            {
                if ((crc & 0x8000) != 0)
                    crc = (ushort)((crc << 1) ^ 0x1021);
                else
                    crc <<= 1;
            }
        }

        return crc.ToString("X4");
    }

    private static string Tlv(string id, string value)
    {
        if (value.Length > 99)
            throw new InvalidOperationException("EMV field exceeds 99 characters.");
        return id + value.Length.ToString("00", CultureInfo.InvariantCulture) + value;
    }

    private static string Fold(string value)
    {
        var replaced = value.Replace('đ', 'd').Replace('Đ', 'D');
        var form = replaced.Normalize(NormalizationForm.FormD);
        var buffer = new StringBuilder(form.Length);
        foreach (var c in form)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;
            var upper = char.ToUpperInvariant(c);
            if (upper is (>= 'A' and <= 'Z') or (>= '0' and <= '9') or ' ')
                buffer.Append(upper);
        }

        return string.Join(' ', buffer.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
