# 0060 — Grouped Payment Allocations, Selective CLS, Doctor QR, Reception Cash Collection & Cross-Day Foundations

- **Date**: 2026-11-26
- **Status**: **Accepted (Stage B `#851` + Stage C `#853` Cross-Day Visit Lifecycle)**
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

### 2.5. Stage C (`#853`, `Migration 025`): Cross-Day Visit Lifecycle, Durable Daily Presence & Presence-Gated `ReadyForReturn`

1. **Durable Database Presence Model (`Migration 025`)**:
   - `visits` stores `last_present_date date NULL`, `presence_state varchar(16) NOT NULL DEFAULT 'PRESENT' CHECK (presence_state IN ('PRESENT', 'LEFT', 'DEFERRED'))`, and `last_resumed_at timestamp without time zone NULL`. `visits.checked_in_at` remains the immutable initial episode intake timestamp.
   - `queue_entries` adds `idempotency_key varchar(100) NULL` with partial unique index `ux_queue_entries_idempotency_key` (`WHERE idempotency_key IS NOT NULL`) to serialize idempotent cross-day `Resume` requests cleanly.
   - `VisitPresenceEvaluator.IsPresentToday(visit, visitQueueEntries, todayLocal)` evaluates durable presence without inferring presence merely because a `Visit` is open or a `DiagnosticResult` arrived:
     1. `last_present_date == todayLocal` and `presence_state IN ('LEFT', 'DEFERRED')` -> `false`.
     2. `last_present_date == todayLocal` and `presence_state == 'PRESENT'` -> `true` (survives restarts).
     3. Backwards-compatible fallback when `last_present_date IS NULL` and `CheckedInAt` is `todayLocal` (not `'LEFT'`/`'DEFERRED'`), or an active non-cancelled, non-`DEFERRED`, uncompleted `QueueEntry` exists on `todayLocal` with `PresenceStatus == 'PRESENT'` -> `true`.
     4. Otherwise (prior-day `Visit` after `UTC+7` midnight rollover before Reception `Resume`) -> `false`.
2. **Presence-Gated `ReadyForReturn` & Mandatory Readiness Re-Evaluation**:
   - `DiagnosticOrderLifecycleHelper.TryTransitionVisitToReturnConsultationAsync` verifies that **all** mandatory (`required_for_conclusion = true`) orders have valid finalized/verified `UC43` results (`REVIEW_REQUIRED` / `TechnicalAbort` mandatory orders are **not** satisfied) and no non-cancelled `ACCEPTED` optional order is mid-execution (`IN_PROGRESS`) or `COMPLETED` awaiting `UC43` result.
   - Whenever readiness ceases (e.g., Doctor creates a new mandatory `DiagnosticOrder`, or a mandatory order undergoes `TechnicalAbort`), `visit.ReadyForReturnAt` is deterministically reset to `NULL`.
   - When ready (`visit.ReadyForReturnAt ??= nowLocal`) and `require_presence_for_return_queue = true` (default):
     - **Present today**: creates/activates at most **1** active `RETURN_CONSULTATION` `QueueEntry` (`QueueDate = todayLocal`, `Status = 'READY'`, `PresenceStatus = 'PRESENT'`) and sets `visit.Status = 'RETURN_CONSULTATION'`.
     - **Absent today**: keeps `visit.Status = 'AWAITING_RESULTS'` with `ReadyForReturnAt` stamped, creates **zero** callable queue entries, and enqueues an idempotent Zero-PHI `VISIT_RETURN_READY` notification (`visit-return-ready:{visitId}:{ReadyForReturnAt:yyyyMMddHHmmss}`).
3. **`Resume` & `Defer` Lock Hierarchy, Collision Guards & Policies**:
   - `ResumeVisitAsync` locks strictly in `05 doctors -> 06 hospital_rooms -> 09 visits -> 10 queue_service_points -> 11 queue_entries -> 12 diagnostic_orders` order, closes stale `QueueDate < todayLocal` non-`IN_PROGRESS` queue entries as `'DEFERRED'` (`PresenceStatus = 'LEFT'`), stamps `LastPresentDate = todayLocal` / `PresenceState = 'PRESENT'` / `LastResumedAt = nowLocal`, creates zero duplicate `EXAMINATION` or `DIAGNOSTIC` charges, and supports doctor reassignment when the original doctor is off-duty.
   - `DeferVisitOrOrdersAsync` locks `09 -> 10 -> 11 -> 12`, rejects any `IN_PROGRESS` target (`409` in `Defer vs StartDiagnosticExecution`), marks waiting/ready/called queues `'DEFERRED'`, stamps `deferred_until_date` without cancelling orders or payments, and updates `visit.PresenceState` when the patient leaves.
   - `CheckInScheduledPatientService` and `CreateWalkInVisitService` lock `patients (02)` and reject new intakes with `409 [VISIT_RESUME_REQUIRED]` when an open prior-day `Visit` exists in the same hospital unless `AllowNewEpisodeWhileVisitOpen = true`.
   - `CrossDayVisitPolicyReader` reads configurable `VISIT`/`QUEUE` policies (`allow_cross_day_visit = true`, `diagnostic_order_validity_days = 7`, `return_consultation_window_days = 7`, `require_presence_for_return_queue = true`, `allow_doctor_reassignment = true`, `auto_cancel_overdue_visit = false`). Orders that already completed `UC42` (`COMPLETED`) never expire while awaiting `UC43`, and overdue unexecuted orders surface `REVIEW_REQUIRED` (`409 [DIAGNOSTIC_ORDER_EXPIRED_REVIEW_REQUIRED]`) without auto-cancellation or forfeiting `PAID` settlements.

---

## 3. Consequences

- **Benefits**: Eliminates double-settlement and partial-bundle ambiguity across grouped VietQR, single VietQR, SePay webhooks, and Reception cash; enforces strict two-layer `PAYMENT_COLLECT` governance; cleanly separates `PAID_NOT_PERFORMED` voluntary skip from hospital-error `CANCELLED_REFUND_PENDING` without building an out-of-scope refund subsystem; provides restart-safe daily presence tracking, zero-duplicate cross-day `Resume`/`Defer` queue lifecycles, and accurate `NewCheckInsToday` vs `ResumedVisitsToday` operational metrics.
- **Trade-offs**: Payment, selection, and cross-day resume/defer mutations acquire ordered lock chains (`05 -> 06 -> 09 -> 10 -> 11 -> 12 -> 15 -> 16 -> 16b`) inside explicit PostgreSQL transactions and reload entities post-lock.
- **Residual Risks**: Pre-`023` `LEGACY_VISIT` rows remain visit-level by design (zero guessed allocations); overdue open visits remain visible for staff/doctor review rather than silently auto-cancelling when `auto_cancel_overdue_visit = false`.
