using System.Globalization;
using MedFlow.Domain.Entities;

namespace MedFlow.Application.Services.Pharmacy;

/// <summary>
/// Canonical dispense receipt code helper (ADR-0047: DISP-yyyy-MMdd-NNNN).
/// Shared by ConfirmMedicineDispensingService and DispensingResultService so SCR-PHA-04 never 404s,
/// while remaining backward-compatible with legacy DISP-yyyy-NNNNNN codes on read.
/// </summary>
public static class PharmacyDispenseCode
{
    public static string Format(MedicationDispense dispense, DateTime fallbackDate)
    {
        ArgumentNullException.ThrowIfNull(dispense);
        var effectiveDate = dispense.DispensedAt ?? dispense.PreparedAt ?? fallbackDate;
        return $"DISP-{effectiveDate:yyyy-MMdd}-{dispense.Id:D4}";
    }

    public static string FormatLegacy(MedicationDispense dispense, DateTime fallbackDate)
    {
        ArgumentNullException.ThrowIfNull(dispense);
        var effectiveDate = dispense.DispensedAt ?? dispense.PreparedAt ?? fallbackDate;
        return $"DISP-{effectiveDate.Year:D4}-{dispense.Id:D6}";
    }

    public static bool TryExtractDispenseId(string? rawCode, out long dispenseId)
    {
        dispenseId = 0;
        if (string.IsNullOrWhiteSpace(rawCode))
        {
            return false;
        }

        var upper = rawCode.Trim().ToUpperInvariant();
        if (!upper.StartsWith("DISP-", StringComparison.Ordinal))
        {
            return false;
        }

        var lastDash = upper.LastIndexOf('-');
        if (lastDash <= 4 || lastDash >= upper.Length - 1)
        {
            return false;
        }

        return long.TryParse(
            upper[(lastDash + 1)..],
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out dispenseId) && dispenseId > 0;
    }

    public static bool Matches(MedicationDispense candidate, string? rawCode, DateTime fallbackDate)
    {
        if (string.IsNullOrWhiteSpace(rawCode))
        {
            return false;
        }

        var upper = rawCode.Trim().ToUpperInvariant();
        return string.Equals(Format(candidate, fallbackDate), upper, StringComparison.Ordinal) ||
               string.Equals(FormatLegacy(candidate, fallbackDate), upper, StringComparison.Ordinal);
    }
}
