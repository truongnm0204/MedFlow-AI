# 0064 — Pharmacy hand-over gate includes MEDICINE charges & backfills pre-gate READY orders

- Ngày: 2026-11-29
- Trạng thái: accepted (implemented — Issue #867 / S3c)
- Lane: high-risk
- Liên quan: `0063-medicine-charges-pharmacy-counter`, BR-BF03-08/BR-BF03-09, owner decision D1

## Context

#866 made medicine a first-class `MEDICINE` visit charge and added a prescription-scoped medicine
clearance used by `ConfirmMedicineDispensingService`. Remaining gaps:

1. `VisitChargeSettlementReader.CheckPharmacyVisitClearanceAsync` still evaluated only the
   EXAMINATION charge, so any caller of the visit-level clearance (or a visit with only medicine
   charges) could reach hand-over without a medicine payment decision.
2. Prescriptions already `READY`/`ISSUED` before this feature (or whose items were skipped by #866
   because no price existed yet) have no MEDICINE charge, so the gate had nothing to enforce.
3. The dispense context showed "Miễn phí / BHYT" whenever no payment row existed, even when medicine
   was still payable; BHYT is out of scope.

## Decision

1. **Visit hand-over clearance now evaluates MEDICINE.** `CheckPharmacyVisitClearanceAsync` checks the
   examination fee first (unchanged ADR-0047 semantics) and then every ACTIVE MEDICINE charge of the
   visit through the existing per-charge settlement evaluator. A visit with only MEDICINE charges is
   evaluated as medicine instead of being reported as a cancelled examination fee. Unpaid medicine
   returns `[MEDICINE_PAYMENT_REQUIRED]` with a Vietnamese message.
2. **Queue enrollment keeps the legacy fee scope.** The new
   `CheckPharmacyQueueClearanceAsync` (examination/diagnostic only) is used by
   `VisitQueueEnrollmentService`. Taking a pharmacy queue number must not require medicine payment
   first: D1 says the pharmacy counter collects the money, and a patient needs to reach that counter.
   The financial block is at hand-over, not at queue entry.
3. **Backfill, never block, for pre-gate READY orders.** `ConfirmMedicineDispensingService` calls
   `MedicineChargeProvisioner.EnsureForPrescriptionAsync` (idempotent, one charge per prescription
   line) from both the dispense context and the confirm flow. The backfill is persisted **before** the
   dispense transaction so a payment-blocked confirm does not roll the new charges back. Prices are
   snapshotted from the current effective `medicine_prices` row at hand-over; an audit
   `MEDICINE_CHARGE_BACKFILLED` records the prescription, visit and created lines. Orders whose
   medicines still have no effective price are skipped + audited by `MEDICINE_CHARGE_UNPRICED` and are
   not cancelled; hand-over continues with no fabricated charge (documented residual risk).
4. **No "Miễn phí / BHYT" label.** The dispense context reports the real state: `PENDING`/`MISMATCH`
   while blocked, `PAID` + actual method(s) (`Tiền mặt`, `VietQR`) when settled, `UNPAID` with
   "Chưa có giao dịch thanh toán" when no payment exists and nothing is blocked. `0 ₫` is shown when
   the fee is legitimately zero/waived.

## Consequences

- Được: one visit-level gate covers examination + medicine; READY legacy orders become billable at
  hand-over without destroying the order; payment labels no longer imply free/BHYT.
- Mất: backfilling a pre-gate order uses the price effective at hand-over, not at original issue time
  (no historical price exists for those orders). Backfill also runs on the dispense-context read
  (idempotent, audited) so the counter can see and collect the fee.
- Rủi ro còn lại: (a) a visit with several prescriptions is gated by all active medicine charges of
  that visit; (b) an order whose medicine has no effective price can still be handed over uncharged
  (consistent with #866's never-invent-price policy) — administrators must configure prices before
  hand-over to collect; (c) production must grant `PAYMENT_COLLECT` to `PHARMACY_STAFF` explicitly.
