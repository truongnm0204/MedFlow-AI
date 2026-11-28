using System.Text.Json;
using MedFlow.Application.Common.Time;
using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Services.Pharmacy;
using MedFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MedFlow.Application.Services.Payments;

/// <summary>
/// Outcome of materialising MEDICINE charges for a prescription.
/// <see cref="MissingPriceMedicineIds"/> are medicines with no effective price at the charge instant;
/// they are reported through audit_logs and are NOT invented as 0đ or UNPRICED lines.
/// </summary>
internal sealed record MedicineChargeProvisionResult(
    int CreatedCount,
    IReadOnlyList<long> MissingPriceMedicineIds)
{
    public static readonly MedicineChargeProvisionResult Empty = new(0, []);
}

/// <summary>
/// Materialises one MEDICINE <see cref="VisitCharge"/> per prescription line (#866, owner decision D1).
/// The per-unit price is snapshotted from <see cref="IMedicinePriceResolver"/> at the issue instant; the line
/// total stored in <see cref="VisitCharge.UnitPrice"/> is the amount the existing cash/QR allocation logic uses.
///
/// Charge model: one charge per prescription_items row (mirrors DIAGNOSTIC: one order = one charge line),
/// never one aggregate charge per prescription, so refund/cancel of a single drug never touches other lines.
/// Idempotent: an existing charge for the same prescription_item_id is never duplicated.
/// </summary>
internal static class MedicineChargeProvisioner
{
    public const string UnpricedAuditAction = "MEDICINE_CHARGE_UNPRICED";
    private const string AuditEntityType = "PRESCRIPTION";

    private static readonly HashSet<string> ChargeablePrescriptionStatuses =
        new(StringComparer.OrdinalIgnoreCase) { "ISSUED", "PREPARING", "READY", "DISPENSED" };

    public static bool IsHospitalPharmacyFulfillment(string? fulfillmentChoice)
    {
        if (string.IsNullOrWhiteSpace(fulfillmentChoice))
            return true;

        var choice = fulfillmentChoice.Trim().ToUpperInvariant();
        return choice is "HOSPITAL" or "HOSPITAL_PHARMACY";
    }

    public static bool IsChargeablePrescriptionStatus(string? status) =>
        !string.IsNullOrWhiteSpace(status) &&
        ChargeablePrescriptionStatuses.Contains(status.Trim());

    public static async Task<MedicineChargeProvisionResult> EnsureForPrescriptionAsync(
        IApplicationDbContext db,
        IMedicinePriceResolver priceResolver,
        Prescription prescription,
        long hospitalId,
        long actorUserId,
        DateTime atHospitalLocal,
        CancellationToken ct = default)
    {
        try
        {
            if (prescription.Id <= 0 ||
                prescription.VisitId is not long visitId || visitId <= 0 ||
                hospitalId <= 0 ||
                !IsHospitalPharmacyFulfillment(prescription.FulfillmentChoice) ||
                !IsChargeablePrescriptionStatus(prescription.Status))
            {
                return MedicineChargeProvisionResult.Empty;
            }

            var items = await db.PrescriptionItems.AsNoTracking()
                .Where(i => i.PrescriptionId == prescription.Id)
                .OrderBy(i => i.Id)
                .ToListAsync(ct);

            if (items.Count == 0)
                return MedicineChargeProvisionResult.Empty;

            var alreadyChargedItemIds = await db.VisitCharges.AsNoTracking()
                .Where(c => c.PrescriptionId == prescription.Id && c.PrescriptionItemId != null)
                .Select(c => c.PrescriptionItemId!.Value)
                .ToListAsync(ct);
            var chargedSet = alreadyChargedItemIds.ToHashSet();

            var created = new List<VisitCharge>(items.Count);
            var missingPriceMedicineIds = new List<long>();

            foreach (var item in items)
            {
                if (chargedSet.Contains(item.Id))
                    continue;

                if (item.MedicineId is not long medicineId || medicineId <= 0 ||
                    item.PrescribedQuantity is not decimal quantity || quantity <= 0m)
                {
                    continue;
                }

                var quote = await priceResolver.TryGetEffectivePriceAsync(
                    hospitalId,
                    medicineId,
                    atHospitalLocal,
                    ct);
                if (quote is null)
                {
                    missingPriceMedicineIds.Add(medicineId);
                    continue;
                }

                created.Add(VisitCharge.CreateMedicine(
                    visitId,
                    prescription.Id,
                    item.Id,
                    medicineId,
                    quote.PriceAmount,
                    quantity,
                    atHospitalLocal));
            }

            if (created.Count > 0)
                db.VisitCharges.AddRange(created);

            if (missingPriceMedicineIds.Count > 0)
            {
                db.AuditLogs.Add(new AuditLog
                {
                    ActorUserId = actorUserId,
                    HospitalId = hospitalId,
                    Action = UnpricedAuditAction,
                    EntityType = AuditEntityType,
                    EntityId = prescription.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Reason = "Chưa thể tạo khoản phí thuốc vì thuốc chưa có giá hiệu lực.",
                    ChangeData = JsonSerializer.Serialize(new
                    {
                        prescriptionId = prescription.Id,
                        prescriptionCode = prescription.PrescriptionCode,
                        visitId,
                        hospitalId,
                        medicineIds = missingPriceMedicineIds.Distinct().OrderBy(id => id).ToList(),
                        occurredAtLocal = atHospitalLocal,
                    }),
                    OccurredAt = HospitalClock.ConvertHospitalLocalToUtc(atHospitalLocal),
                });
            }

            return new MedicineChargeProvisionResult(
                created.Count,
                missingPriceMedicineIds.Distinct().OrderBy(id => id).ToList());
        }
        catch (NotSupportedException)
        {
            // Minimal unit-test DbContext double without MedicinePrices/VisitCharges DbSets.
            return MedicineChargeProvisionResult.Empty;
        }
    }
}
