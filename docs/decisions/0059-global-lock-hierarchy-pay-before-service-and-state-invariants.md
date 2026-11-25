# 0059 — Global Lock Hierarchy, Per-Charge Pay-Before-Service Billing & Cross-Module State Invariants

- **Date**: 2026-11-25
- **Status**: **Accepted (Conditional GO for Phase 01 / Wave 0)**
- **Lane**: high-risk (DB Schema / Concurrency / Auth & PHI / Billing & Payment / Clinical State Machine)
- **Related ADRs**: `0014`, `0015`, `0016`, `0018`, `0024`, `0027`, `0030`, `0031`, `0032`, `0033`, `0040`, `0041`, `0042`, `0044`, `0045`, `0046`, `0047`, `0049`, `0055`, `0056`

---

## 1. Context

A 4-cluster audit at commit `75ecc96` uncovered 41 latent bug groups across MedFlowAI's business modules. Following two rounds of architectural review, **Option A (`payments.visit_charge_id`)**, the **16-level Global Lock Hierarchy**, and **preserving v4.1 Pharmacy scope (`ADR-0047` — no retail medicine price catalog in this remediation)** are approved with mandatory database-consistency, state-transition, and concurrency invariants codified below.

---

## 2. Decisions

### 2.1. 16-Level Global Lock Hierarchy & Aggregate Root Lock Protocol

Callers **do not** lock every table; each workflow locks its owning **Aggregate Root** plus only the contended rows it mutates. Whenever a transaction acquires locks on more than one resource, it **MUST** acquire them in strictly increasing hierarchy level (`01 -> 16`), and lock multiple rows at the same level in **ascending `id` order (`ORDER BY id ASC`)**:

| Level | Physical Resource / Table (`v4.1`) | Lock Helper / Mechanism | Hierarchy & Aggregate Rules |
|---:|---|---|---|
| `01` | `users` | `TryLockUserForUpdateAsync` | Serializes OTP `attempts` updates, password/session mutations, and user identity claims. |
| `02` | `patients` | `TryLockPatientsForUpdateAsync` | Sort `patientIds ASC`. Serializes same-day active `Visit` creation across Walk-in, Check-in, and Booking, plus Identity Claim `[currentId, targetId]`. |
| `03` | `patient_identity_claims` | `TryLockIdentityClaimForUpdateAsync` | Locked after `users` (`01`) and `patients` (`02`). |
| `04` | `services` | `TryLockServiceForUpdateAsync` | Serializes concurrent admin `ServicePrice` edits and service deactivation against booking. |
| `05` | `doctors` | `TryLockDoctorsForUpdateAsync` | Sort `doctorIds ASC`. Must be acquired **before** `hospital_rooms` (`06`) and `examination_slots` (`07`). |
| `06` | `hospital_rooms` | `TryLockRoomsForUpdateAsync` | **Physical table is `hospital_rooms`** (`medflow_schema_v4_1.sql:107`). Sort `[sourceRoomId, targetRoomId] ASC` on room transfer. |
| `07` | `examination_slots` | `TryLockSlotsForUpdateAsync` | Sort `slotIds ASC`. Acquired after `doctors`/`hospital_rooms` and before `appointments`. |
| `08` | `appointments` | `TryLockAppointmentsForUpdateAsync` | Sort `[rootId, currentId] ASC` on reschedule. Always acquired **before** `visits` (`09`); paired with PostgreSQL `xmin` concurrency token. |
| `09` | `visits` | `TryLockVisitForUpdateAsync` | **Clinical Aggregate Root Lock**: mandatory for room/doctor transfer, consultation workspace writes, diagnostic order creation, diagnostic transitions that mutate `Visit.Status` or enqueue `RETURN_CONSULTATION`, visit completion, and `VisitCharge` creation. |
| `10` | `queue_service_points` *(+ Advisory Lock `queue-unrouted`)* | `TryLockServicePointsForUpdateAsync` / `LockUnroutedQueueAdvisoryAsync` | Sort `[sourceSpId, targetSpId] ASC` on transfer. For unrouted hospital-day intake (`service_point_id IS NULL`), deterministic `pg_advisory_xact_lock` (`SHA-256` 64-bit key `medflow:queue-unrouted:{hospitalId}:{queueDate}`) occupies **Level `10`** (never acquired while holding `11..16`, and never acquiring `01..09` after it). |
| `11` | `queue_entries` | `TryLockQueueEntriesForUpdateAsync` | Sort `id ASC` when mutating queue entries; paired with `state_version`. |
| `12` | `diagnostic_orders` | `TryLockDiagnosticOrdersForUpdateAsync` | Sort `id ASC`. **Anti-deadlock rule**: any diagnostic operation that may read/update `Visit.Status` or enqueue `RETURN_CONSULTATION` **MUST** lock `visits` (`09`) **before** `diagnostic_orders` (`12`). |
| `13` | `prescriptions` | `TryLockPrescriptionForUpdateAsync` | Locked before `medicine_batches` (`14`) during preparation/dispensing. |
| `14` | `medicine_batches` | `TryLockMedicineBatchesForUpdateAsync` | Deduplicate & sort `batchIds ASC`. Sum requested quantities per `batchId` across all lines before checking `quantity_on_hand`. |
| `15` | `visit_charges` | `TryLockVisitChargesForUpdateAsync` | Sort `id ASC`. Locked before `payments` (`16`). |
| `16` | `payments` | `TryLockPaymentsForUpdateAsync` | Sort `id ASC`. Serializes VietQR issuance, `ExpireStalePendingAsync`, `CASHIER` settlement, and `SePayWebhookService`. |

#### Mandatory Concurrency & Lock Execution Principles
1. **`BeginTransactionAsync` First**: Calling any relational row-lock or advisory-lock helper when `Database.CurrentTransaction is null` throws `InvalidOperationException`.
2. **No Downward Locking**: Never acquire a lock at level `k` while already holding any lock at level `> k`.
3. **Existence Check + Post-Lock Reload**: Each lock verifies that the target row(s) exist in PostgreSQL (`SELECT id FROM ... FOR UPDATE`) and callers **MUST** reload/re-query business fields (`Status`, `SlotId`, `AssignedDoctorId`, `QuantityOnHand`, `Attempts`, active `Visits`) **after** acquiring locks — never acting on pre-lock reads.
4. **Implicit Foreign-Key Lock Alignment**: PostgreSQL `INSERT`/`UPDATE` on child rows (`appointments -> examination_slots`, `visits -> patients/appointments`, `visit_charges -> visits/diagnostic_orders`, `payments -> visit_charges/visits`, `dispense_items -> medicine_batches`) acquires `FOR KEY SHARE` on parent rows in the exact same parent-to-child direction (`02 -> 07 -> 08 -> 09 -> 12 -> 15 -> 16`) as explicit `FOR UPDATE` locks, and every new FK column is indexed to avoid table-scan lock escalation.
5. **PostgreSQL Concurrency Exception Handling (No Blind Retry)**:
   - `23505` (`unique_violation`): handled per domain (idempotent replay return for SePay `payment_gateway_events` / `idempotency_key` / active QR reuse, or `409 ConflictException`).
   - `40P01` (`deadlock_detected`) & `40001` (`serialization_failure`): rollback transaction, clear `ChangeTracker`, log structured telemetry (`SqlState`, operation), and map via middleware/service guard to deterministic `409 ConflictException` (never blind catch-all retries across side-effecting logic, and never unhandled HTTP `500`).
6. **Zero Network / SignalR Inside Transactions**: Never call SMTP, external HTTP APIs, or `IRealtimeNotificationPublisher` while holding a DB transaction; publish SignalR invalidations strictly **after** `tx.CommitAsync(ct)` succeeds.
7. **Real PostgreSQL Concurrency Verification**: Lock ordering stabilizes compliant writers (does not magically protect rogue writers outside the protocol); all concurrency tests must run on real PostgreSQL with independent `NpgsqlConnection`s/`DbContext`s and deterministic barriers.

---

### 2.2. Per-Charge `Pay Before Service` Schema & State Machine (`Option A`)

#### A. Migration `023` Billing Constraints (Cross-Visit Proof & Legacy Separation)
1. **Cross-Visit Referential Integrity (`CRITICAL`)**:
   - `diagnostic_orders`: ensure `visit_id IS NOT NULL` (preflight verified) + `CONSTRAINT uq_diagnostic_orders_id_visit_id UNIQUE (id, visit_id)`.
   - `visit_charges`:
     - Ensure `visit_id IS NOT NULL` + `CONSTRAINT uq_visit_charges_id_visit_id UNIQUE (id, visit_id)`.
     - Add `charge_type varchar(20) NOT NULL DEFAULT 'EXAMINATION'`, `status varchar(24) NOT NULL DEFAULT 'ACTIVE'`, `created_at timestamp without time zone NOT NULL DEFAULT (now() AT TIME ZONE 'Asia/Ho_Chi_Minh')`, `cancelled_at timestamp without time zone NULL`.
     - Add `CONSTRAINT chk_visit_charges_type_consistency CHECK ((charge_type = 'EXAMINATION' AND diagnostic_order_id IS NULL) OR (charge_type = 'DIAGNOSTIC' AND diagnostic_order_id IS NOT NULL))` (v4.1 supports `EXAMINATION` and `DIAGNOSTIC`; future `PHARMACY` charges require a dedicated ADR & migration).
     - Add `CONSTRAINT fk_visit_charges_diagnostic_order_visit FOREIGN KEY (diagnostic_order_id, visit_id) REFERENCES diagnostic_orders (id, visit_id)` — **guaranteeing `visit_charges.visit_id == diagnostic_orders.visit_id`**.
     - Add `uq_visit_charges_active_exam_per_visit UNIQUE (visit_id) WHERE charge_type = 'EXAMINATION' AND status = 'ACTIVE'` and `uq_visit_charges_diagnostic_order UNIQUE (diagnostic_order_id) WHERE diagnostic_order_id IS NOT NULL`.
   - `payments`:
     - Add `visit_charge_id bigint NULL` + `ix_payments_visit_charge_id`.
     - Add `CONSTRAINT fk_payments_charge_visit FOREIGN KEY (visit_charge_id, visit_id) REFERENCES visit_charges (id, visit_id)` — **guaranteeing `payments.visit_id == visit_charges.visit_id`** (cross-visit payment attribution is impossible in PostgreSQL).
2. **Preventing Unlinked New Payments While Preserving Historical Data**:
   - Add `payment_scope varchar(16)` on `payments`: catalog-defaulted to `'LEGACY_VISIT'` for pre-`023` rows (zero row rewrite, zero amount-matching backfill), then immediately `ALTER COLUMN payment_scope SET DEFAULT 'CHARGE'` for all new rows.
   - Add `CONSTRAINT chk_payments_scope_charge_consistency CHECK ((payment_scope = 'CHARGE' AND visit_charge_id IS NOT NULL AND visit_id IS NOT NULL) OR (payment_scope = 'LEGACY_VISIT' AND visit_charge_id IS NULL AND visit_id IS NOT NULL))`.
   - Any post-cutover `CHARGE` payment missing `visit_charge_id` or `visit_id` is rejected by PostgreSQL (`23514`) and Application validation.
   - **Active Historical Visit Cutover Policy**: If a pre-`023` visit is still active at deployment time: (a) its existing `LEGACY_VISIT` `PENDING` payment can still complete (`PAID`/`EXPIRED`); (b) any newly ordered `DiagnosticOrder` creates a `DIAGNOSTIC` `VisitCharge` (`payment_scope = 'CHARGE'`); (c) if an unpaid historical visit has `0` `PAID` legacy payments and needs a new examination QR after cutover, the service materializes its single `EXAMINATION` `VisitCharge` from its walk-in audit / slot price snapshot (never from `payments.amount`!) and issues a `CHARGE`-scoped payment.
3. **Three Partial Unique Indexes on `payments`** (replacing `012`'s `ux_payments_one_pending_per_visit` via `DROP INDEX IF EXISTS` in `023`, leaving `001`–`022` untouched):
   - `ux_payments_one_pending_per_charge ON payments (visit_charge_id) WHERE visit_charge_id IS NOT NULL AND upper(btrim(status)) = 'PENDING'`
   - `ux_payments_one_paid_per_charge ON payments (visit_charge_id) WHERE visit_charge_id IS NOT NULL AND upper(btrim(status)) = 'PAID'`
   - `ux_payments_one_pending_legacy_visit ON payments (visit_id) WHERE visit_id IS NOT NULL AND visit_charge_id IS NULL AND upper(btrim(status)) = 'PENDING'`

#### B. Payment State Machine & Late/Cancelled Webhook Rules
- **All Payment Writers Audited**: Every payment path (`GenerateQrPaymentService`, `CreateWalkInVisitService`, `SePayWebhookService`, and any `CASHIER` confirmation path) must validate `VisitCharge` ownership, `ACTIVE` status, non-null valid `UnitPrice >= 0` (missing price is **never** free!), and lock `visits -> visit_charges -> payments`.
- **Exact Settlement Only**: Underpayment (`< expected`) and overpayment (`> expected`) record `PaymentGatewayEvent` (`PARTIAL` / `OVERPAID`) + `AuditLog` (`PAYMENT_RECONCILIATION_REQUIRED`) and transition `PENDING -> MISMATCH` **without** marking `PAID` or unlocking the service.
- **Webhook After Service / Charge / Visit Cancelled (`CRITICAL`)**:
  - `SePayWebhookService` locks `visits -> diagnostic_orders (if linked) -> visit_charges -> payments` and checks **all four** statuses before unlocking.
  - If `Visit` is `CANCELLED`/`NO_SHOW`, `DiagnosticOrder` is `CANCELLED`, or `VisitCharge` is `CANCELLED`/`CANCELLED_REFUND_PENDING`:
    - Persist `PaymentGatewayEvent` (`Outcome = 'CANCELLED_SERVICE_REFUND_REQUIRED'`) with full bank reference & amount.
    - Transition `VisitCharge.Status = 'CANCELLED_REFUND_PENDING'`, mark `Payment` for reconciliation/refund (never normal `PAID` unlock), write `AuditLog` (`PAYMENT_RECEIVED_FOR_CANCELLED_SERVICE`), and **NEVER re-open or unlock the cancelled `Visit` or `DiagnosticOrder`**.
- **Old/Expired QR Paid After New QR Created or Paid**:
  - Always persist `PaymentGatewayEvent`.
  - If a newer `Payment` for the same `visit_charge_id` is already `PAID`, `ux_payments_one_paid_per_charge` + service check prevent a second `PAID`; record `Outcome = 'DUPLICATE_CHARGE_RECONCILE'` + `AuditLog` for refund.
  - If a newer `PENDING` QR exists for the same `visit_charge_id` (unpaid) and the customer paid the **old `EXPIRED` QR** with exact `Amount == charge.UnitPrice`: expire/cancel the unused newer `PENDING` QR, transition the paid row `EXPIRED -> PAID`, and unlock the `VisitCharge` cleanly so the patient's real payment is honored without double-charging!

---

### 2.3. Pharmacy Scope Preservation (`ADR-0047` — v4.1)

- **Confirmed Out-of-Scope**: Do **NOT** add `medicine_prices`, unit-conversion pricing (tablet/blister/box), retail pharmacy billing, or partial-dispense price recalculation in this bug-remediation plan.
- Medicine prices remain `null` / `"Chưa có dữ liệu"` per `ADR-0047` — **never** inferred as free (`0đ`) and **never** inferred from a `PAID` examination fee.
- Pharmacy dispensing focuses strictly on: valid `ISSUED` hospital prescription, active visit & examination-charge policy check (`IVisitChargeSettlementReader`), multi-batch FEFO preservation, per-batch summed `QuantityOnHand` lock checks, `DispensedQuantity` (`null` vs `0` vs `> 0`), anti-double-dispense, non-negative stock, and accurate receipt codes (`SCR-PHA-04`).

---

### 2.4. Wave Merge vs Production Release Policy

- Merging individual Phases (`01` -> `02/03` -> `04/05/06` -> `07`) into `develop` is permitted once each Phase passes its full test and quality gates on an isolated PostgreSQL test database.
- **Production Deployment Hold**: Do **NOT** deploy Migration `023` or partial Waves (`01` alone without `03/05/06`) to Production until all `Pay Before Service` writers (`03`, `05`, `06`) and Phase `07` release acceptance gates are complete.
