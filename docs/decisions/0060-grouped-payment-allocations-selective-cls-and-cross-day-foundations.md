# 0060 — Grouped Payment Allocations, Selective CLS, Doctor QR, Reception Cash Collection & Cross-Day Foundations

- **Date**: 2026-11-26
- **Status**: **Accepted (Stage B — Issue `#851`)**
- **Lane**: high-risk (DB Schema / Concurrency / Auth & RBAC / Billing & Payment / Clinical State Machine)
- **Related ADRs**: `0019`, `0025`, `0027`, `0029`, `0030`, `0031`, `0032`, `0033`, `0034`, `0047`, `0059`

---

## 1. Context

`ADR-0059` (`Migration 023`) established per-charge `Pay Before Service` (`Option A`: `payments.visit_charge_id`) and the 16-level Global Lock Hierarchy (`01..16`). Operational outpatient workflows in Stage B (`Issue #851`) require:

1. **Flexible Single or Grouped Settlement**: Patients, assigned Doctors (VietQR display only), and Reception cashiers must be able to settle either **1 charge** or **2+ selected charges** in a single transaction (`1 Payment -> N PaymentAllocations`) without losing deterministic per-charge auditability or corrupting historical `LEGACY_VISIT` rows.
2. **Selective Diagnostic (`CLS`) Consent & No-Refund Voluntary Abandonment**: Mandatory CLS (`required_for_conclusion = true`) vs Optional CLS (`required_for_conclusion = false`) have distinct clinical and financial lifecycles. Optional orders start `UNDECIDED` unless accepted, can be declined before payment (`DECLINED` + `VisitCharge = CANCELLED`), or voluntarily skipped after payment (`SKIPPED_AFTER_PAID` + `VisitCharge = PAID_NOT_PERFORMED`) while keeping real `PAID` revenue intact and **without** introducing any refund module.
3. **Reception Cash Collection (`PAYMENT_COLLECT`)**: Counter cash collection requires live database permission verification (no `HOSPITAL_ADMIN` bypass when `PAYMENT_COLLECT` is revoked via `UC58`), strict hospital isolation, and mandatory `idempotency_key` replay protection.
4. **Cross-Day Visit Foundations**: Additive nullable columns (`diagnostic_orders.deferred_until_date`, `visits.ready_for_return_at`) prepare Stage C cross-day deferred diagnostic execution without breaking single-day flows.

---

## 2. Decisions

### 2.1. Extended Global Lock Hierarchy (`Level 16b: payment_allocations`)

`ADR-0059` is extended with **`Level 16b`** (`payment_allocations`), acquired strictly **after** `payments` (`Level 16`) in ascending `id` order (`ORDER BY id ASC`):

```
09 visits -> 10 queue_service_points -> 11 queue_entries -> 12 diagnostic_orders
          -> 13 prescriptions -> 14 medicine_batches
          -> 15 visit_charges -> 16 payments -> 16b payment_allocations
```

- `IApplicationDbContext.TryLockPaymentAllocationsForUpdateAsync` enforces an active relational transaction, sorts distinct positive IDs ascending, executes `SELECT id AS "Value" FROM payment_allocations WHERE id = ANY(@ids) ORDER BY id ASC FOR UPDATE`, and verifies exact row existence before returning `true`.
- Foreign keys on `payment_allocations` (`(payment_id, visit_id) -> payments(id, visit_id) ON DELETE CASCADE` and `(visit_charge_id, visit_id) -> visit_charges(id, visit_id) ON DELETE RESTRICT`) align implicit PostgreSQL `FOR KEY SHARE` parent locks with explicit `15 -> 16 -> 16b` acquisition order.

### 2.2. Canonical `PaymentAllocation` Settlement Model & Non-Coexistence Rules

1. **Three Explicit `payment_scope` Modes**:
   - `ALLOCATED`: Stage B canonical mode (`1 Payment -> 1..N payment_allocations`). Single-charge `ALLOCATED` rows populate `visit_charge_id = chargeIds[0]` for backwards compatibility; multi-charge grouped rows (`>= 2` charges) store `visit_charge_id = NULL`.
   - `CHARGE`: `ADR-0059` single-charge rows (`visit_charge_id IS NOT NULL`), deterministically backfilled 1-to-1 into `payment_allocations` by `Migration 024`.
   - `LEGACY_VISIT`: Pre-`023` historical visit-level rows (`visit_charge_id IS NULL`), preserved with **zero** guessed `payment_allocations` rows.
2. **Database Uniqueness + Application `PAID`/`PENDING` Non-Coexistence**:
   - Partial unique indexes `ux_payment_allocations_one_pending_per_charge` and `ux_payment_allocations_one_paid_per_charge` forbid two `PENDING` or two `PAID` allocations on the same `visit_charge_id`.
   - Because two separate partial unique indexes cannot prevent `(charge 100, PAID)` and `(charge 100, PENDING)` from coexisting, **every** payment writer (`GenerateQrPaymentService`, `CollectCashPaymentService`, `SePayWebhookService`) locks `09 -> 12 -> 15 -> 16 -> 16b`, reloads post-lock, rejects new QR/Cash requests with `409 [CHARGE_ALREADY_PAID]` if any target charge is already `PAID`, and atomically cancels/expires all overlapping `PENDING` attempts + allocations whenever a charge settles `PAID`.
3. **Whole-Attempt Replacement on Selection Change**:
   - Changing selected `visitChargeIds` never mutates an existing `Payment.Amount` or orphans subset allocations. Any overlapping open `PENDING` QR with a different sorted charge set is atomically cancelled in full (`Payment.Status = 'CANCELLED'` and **all** its `PENDING` allocations -> `'CANCELLED'`) before issuing the replacement `Payment`.
4. **Late / Duplicate / Cancelled SePay Reconciliation Isolation**:
   - Under/overpayment transitions the entire `Payment` to `MISMATCH` and all its allocations to `RECONCILE` (**zero** partial `PAID` allocations).
   - Late transfer on a cancelled/declined service records `CANCELLED_SERVICE_REFUND_REQUIRED`, sets non-`PAID` allocations to `RECONCILE`, marks unpaid cancelled charges `CANCELLED_REFUND_PENDING` (never downgrading `PAID` or `PAID_NOT_PERFORMED`), and never unlocks cancelled services.
   - Late transfer on a charge already `PAID` by Cash or a newer QR records `DUPLICATE_CHARGE_RECONCILE` without mutating the valid `PAID` settlement, and reconciliation items on one charge **never** block payment or QR generation on unrelated `ACTIVE` charges of the same visit.

### 2.3. RBAC Matrix: Doctor QR Display & Reception Cash Collection (`PAYMENT_COLLECT`)

| Capability | `PATIENT` | Assigned `DOCTOR` | `RECEPTION` | `HOSPITAL_ADMIN` | `DIAGNOSTIC_STAFF` / `PHARMACY_STAFF` / `SYSTEM_ADMIN` |
|---|---|---|---|---|---|
| `GET /visits/{id}/charges` | Own / PHI-verified family | Assigned / ordering / SP doctor in hospital | Same hospital (`PAYMENT_VIEW` / `PATIENT_CHECKIN` / `PAYMENT_COLLECT`) | Same hospital | Denied (`403`) |
| `POST /visits/{id}/payments/qr` | Allowed (single & grouped) | Allowed (display VietQR only; unassigned -> `403`) | Allowed in same hospital | Allowed in same hospital | Denied (`403`) |
| `POST /visits/{id}/payments/cash` | Denied (`403`) | Denied (`403` — zero manual `PAID`) | Same hospital + live `PAYMENT_COLLECT` | Same hospital + live `PAYMENT_COLLECT` (no role bypass!) | Denied (`403`) |

- Every cash collection requires `idempotencyKey` (`1..100` chars), persists `payment_method = 'CASH'`, `collected_by_staff_id`, `idempotency_key`, and `CASH_PAYMENT_COLLECTED` audit metadata; replaying the same key with the same visit + charge set returns `200 OK`, whereas mismatched payloads fail with `409 [IDEMPOTENCY_PAYLOAD_MISMATCH]`.
- Migration `024` seeds `PAYMENT_COLLECT` into `role_permissions` **only** when dev/CI seed account `reception01@medflow.test` exists; production environments require explicit hospital admin role-permission assignment (`UC58`).

### 2.4. Selective Diagnostic (`CLS`) Lifecycle & Voluntary Paid Skip (Zero Refund Module)

- **Order Creation**: Mandatory orders (`required_for_conclusion = true`) start `selection_status = 'ACCEPTED'`. Optional orders (`required_for_conclusion = false`) start `selection_status = 'UNDECIDED'` unless explicitly accepted, are excluded from default-checked payment totals until selected, and are blocked from `UC41`/`UC42` execution while `UNDECIDED` or unpaid.
- **Mandatory Order Protection**: `PATIENT`, `RECEPTION`, and non-technical `DIAGNOSTIC_STAFF` cancellations on mandatory orders fail with `409 [MANDATORY_ORDER_REVIEW_REQUIRED]`. A `DIAGNOSTIC_STAFF` `TechnicalAbort` sets `selection_status = 'REVIEW_REQUIRED'` and aborts the room queue **without** releasing `HasPendingMandatoryDiagnosticOrdersAsync` in `CompleteVisitAsync` until the `DOCTOR` clinically cancels/replaces the mandatory order.
- **Unpaid Optional Decline**: Transitions `DiagnosticOrder` (`CANCELLED` + `DECLINED`), `QueueEntry` (`CANCELLED`), `VisitCharge` (`CANCELLED`), and cancels any overlapping `PENDING` QR attempt in full.
- **Voluntary Skip After Payment (`SKIPPED_AFTER_PAID` + `PAID_NOT_PERFORMED`)**: Recorded only by same-hospital `RECEPTION` or assigned `DOCTOR` before execution starts (`PATIENT` self-call returns `409` directing to Reception/Doctor). Cancels `DiagnosticOrder` (`SKIPPED_AFTER_PAID`) and `QueueEntry`, marks `VisitCharge = 'PAID_NOT_PERFORMED'`, preserves `Payment` & `PaymentAllocation` as `PAID`, writes `PAID_DIAGNOSTIC_ORDER_SKIPPED_BY_PATIENT`, and triggers `RETURN_CONSULTATION` readiness if no other active orders remain.
- **Visit Completion Guard**: `CompleteVisitAsync` rejects lingering unpaid `UNDECIDED` optional orders with `409 [UNDECIDED_OPTIONAL_ORDERS_OPEN]` unless `CloseUndecidedOptionalOrders = true`, which atomically closes them as `DECLINED` + `VisitCharge = CANCELLED` without forcing payment.

---

## 3. Consequences

- **Benefits**: Eliminates double-settlement and partial-bundle ambiguity across grouped VietQR, single VietQR, SePay webhooks, and Reception cash; enforces strict two-layer `PAYMENT_COLLECT` governance; cleanly separates `PAID_NOT_PERFORMED` voluntary skip from hospital-error `CANCELLED_REFUND_PENDING` without building an out-of-scope refund subsystem.
- **Trade-offs**: Every payment and diagnostic selection mutation acquires the full ordered chain (`09 -> 12 -> 15 -> 16 -> 16b`) inside an explicit PostgreSQL transaction and reloads entities post-lock.
- **Residual Risks**: Pre-`023` `LEGACY_VISIT` rows remain visit-level by design (zero guessed allocations); Stage C cross-day deferred return visits will build upon `deferred_until_date` and `ready_for_return_at`.
