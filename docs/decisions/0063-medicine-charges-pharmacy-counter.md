# 0063 — Medicine charges: per-prescription-line snapshot & pharmacy-counter settlement

- Ngày: 2026-11-29
- Trạng thái: accepted (implemented — Issue #866 / S3b)
- Lane: high-risk
- Liên quan: BF-03 §4 (CL-4/CL-5), BF-04 Payment Allocation, `0059-global-lock-hierarchy...`,
  `0060-grouped-payment-allocations...`, `0062-medicine-prices-effective-versioning`

## Context

S3a (#865) introduced the hospital-scoped effective `medicine_prices` table and
`IMedicinePriceResolver`. BF-03 requires dispensed medicine to become a payable visit fee and BF-04
requires every payment to allocate to a specific charge. Owner decision D1 fixes the collection
point: **the pharmacy counter**, by pharmacy staff, Cash or VietQR.

The existing `visit_charges` row addressed only EXAMINATION / DIAGNOSTIC (one order = one line).
Medicine has prescribed quantities and a price list, so the slice needed a charge model, a creation
instant, a missing-price policy and a second authorized collector without weakening the existing
reception flow.

## Decision

1. **One MEDICINE charge per `prescription_items` row**, never one aggregate per prescription
   (mirrors DIAGNOSTIC's one-order-one-line model; a per-drug refund/cancel cannot corrupt the other
   lines). New columns (migration 035): `prescription_id`, `prescription_item_id`, `medicine_id`,
   `quantity numeric(14,3)`, `medicine_unit_price numeric(14,2)`. `unit_price` remains the line
   amount used by allocation: `round(medicine_unit_price × quantity)` (VND integer, half away from
   zero). A partial unique index on `prescription_item_id` makes materialisation idempotent.
2. **Charge creation instant = prescription ISSUED** (`CreatePrescriptionAsync` with issue-immediately
   and the draft→ISSUED transition in `CompleteVisitAsync`). Only hospital fulfilment
   (`HOSPITAL` / `HOSPITAL_PHARMACY` / null default) is billable. Prices are snapshotted through
   `IMedicinePriceResolver` at that instant; later price changes never rewrite history.
3. **Missing effective price = skip + durable audit, never 0đ.** `MEDICINE_CHARGE_UNPRICED` records
   the prescription, visit and unpriced medicine IDs. Prescribing/visit completion are not blocked
   (no clinical deadlock); the medicine line is simply not billable until the admin configures a
   price and a new prescription/charge materialisation runs. No UNPRICED charge row is fabricated
   because that would degrade the whole visit settlement state.
4. **Collector split (D1).** MEDICINE charges are settled only by `PHARMACY_STAFF` with the live
   `PAYMENT_COLLECT` permission, via new pharmacy routes for existing Cash/QR services. Reception /
   hospital-admin retain the old scope (EXAMINATION/DIAGNOSTIC) and are rejected with
   `[PHARMACY_COUNTER_ONLY]`; pharmacy staff are rejected for non-medicine charges with
   `[PHARMACY_MEDICINE_ONLY]`. Production must grant `PAYMENT_COLLECT` to `PHARMACY_STAFF`
   explicitly (UC58); migration 035 only grants it in dev/test seeds, matching migration 024.
5. **Dispense gate.** `CheckPharmacyMedicineEligibilityAsync` requires every ACTIVE priced MEDICINE
   charge of the prescription to be PAID before hand-over. Prescriptions with no materialised
   medicine charge keep the pre-existing clearance behaviour (no regression for legacy data).

## Consequences

- Được: fee is a first-class allocated charge; cash and VietQR share ADR-0060 allocation semantics;
  refunds/cancels can target one drug line; historical snapshots survive price changes.
- Mất: a medicine whose price is configured after ISSUED is not billed automatically; an explicit
  re-materialisation is required. Missing prices are only visible via audit, not as a charge row.
- Rủi ro còn lại: splitting the pharmacy staff role from `PAYMENT_COLLECT` in production depends on
  admin configuration; a misconfigured role yields 403 at the counter, which is fail-closed.
