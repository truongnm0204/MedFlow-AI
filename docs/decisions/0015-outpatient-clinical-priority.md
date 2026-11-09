# ADR 0015: Outpatient Clinical Priority & Queue Class Contract (M2 / #102)

- **Status**: Accepted
- **Date**: 2026-04-20
- **Issue**: `#102` (`[M2][BE] Apply Clinical Priority`)
- **Lane**: high-risk

## Context

MedFlowAI models outpatient clinics without an emergency department flow. Previously, `queue_entries.manual_priority` accepted `URGENT` and `RETURN_RESULT` alongside `NORMAL`, `ELDERLY`, and `PREGNANT`, and priority could be mutated ad-hoc during status transitions without document verification or dedicated audit logging.

Under the Vietnam Law on Medical Examination and Treatment and the hospital queue policy (`manual_priority_requires_verification`):
1. Outpatient clinical priority only recognizes `NORMAL`, `ELDERLY` (aged >= 75), and `PREGNANT`. `URGENT` is removed from outpatient clinical queues.
2. Post-diagnostic return visits (`RETURN_RESULT`) are a queue classification (`queueType = RETURN_CONSULTATION`), not an elevated clinical priority.
3. Applying or changing priority requires a dedicated endpoint with role/permission checks, mandatory reason, policy-driven document verification (`verificationConfirmed = true`), optimistic concurrency (`expectedStateVersion`), and an explicit `QUEUE_PRIORITY_UPDATED` audit record.
4. Queue ordering must protect on-time scheduled appointments (`ON_TIME_OR_GRACE`) against walk-ins/late/early arrivals without allowing appointment protection to override legal priority (`ELDERLY`/`PREGNANT`).

## Decision

1. **Allowed Priorities & Database Constraint (`011_outpatient_clinical_priority.sql`)**:
   - Allowed `queue_entries.manual_priority` values are restricted to `NORMAL`, `ELDERLY`, `PREGNANT` via database constraint `ck_queue_entries_manual_priority`.
   - Existing rows with `manual_priority = 'RETURN_RESULT'` on clinical examination queues are migrated to `queue_type = 'RETURN_CONSULTATION'` and `manual_priority = 'NORMAL'`.
   - Any other legacy values (`URGENT`, `EMERGENCY`, etc.) are collapsed to `NORMAL`.
   - Auto-derived `ELDERLY` age threshold is unified at **75 years** across reception queue views, doctor dashboard, and ticket printing.

2. **Three-Tier Waiting Queue Ordering**:
   - Within `READY`/`WAITING` candidates per examination service point:
     - **Tier 0 (Legal Priority)**: `ELDERLY` or `PREGNANT` (manual or auto-derived age >= 75), ordered FIFO by check-in/ready time. Appointment protection never overrides Tier 0.
     - **Tier 1 (Protected Scheduled Appointments)**: `visitType == SCHEDULED` and `arrivalClass == ON_TIME_OR_GRACE` with `NORMAL` priority, ordered by scheduled slot time then check-in FIFO.
     - **Tier 2 (Standard Pool)**: All remaining `NORMAL` entries (`RETURN_CONSULTATION`, `WALK_IN`, `EARLY`, `LATE`), ordered in a single FIFO stream by check-in/ready time.
   - `queueNumber` is immutable after issuance; only 1-based `servingOrder` is recomputed dynamically by the backend.

3. **Dedicated Clinical Priority Endpoint**:
   - `PATCH /api/v1/queue-entries/{idOrCode}/priority` (aliases: `/api/v1/reception/queue-entries/{idOrCode}/priority`, `/api/v1/reception/examination-queue/entries/{idOrCode}/priority`).
   - **Authorization**: Layer 1 `[Authorize(Roles = "RECEPTION,HOSPITAL_ADMIN,SYSTEM_ADMIN")]`; Layer 2 requires active staff in the queue entry's hospital and permission `QUEUE_MANAGE` for non-system-admins (`DOCTOR` and `DIAGNOSTIC_STAFF` receive `403 Forbidden`).
   - **Validation & Eligibility**:
     - `priority` must be `NORMAL`, `ELDERLY`, or `PREGNANT`.
     - `reason` is mandatory (`1..500` chars).
     - `expectedStateVersion` (`>= 1`) must match current `queue_entries.state_version` (`409 Conflict` on mismatch or no-op priority).
     - Target `ELDERLY` rejects patients with known DOB where age `< 75`; target `PREGNANT` rejects patients with known gender `MALE`.
     - When elevating to `ELDERLY` or `PREGNANT` and effective `QUEUE` policy `manual_priority_requires_verification.enabled` is `true` (fail-safe default `true`), `verificationConfirmed == true` is required.
   - **Mutation & Audit**: Updates `manual_priority`, increments `state_version`, leaves `queue_number` untouched, and writes an `audit_logs` entry with `action = 'QUEUE_PRIORITY_UPDATED'`.

4. **Entry Point Consolidation**:
   - `UpdateQueueStatusRequest` (`PATCH .../status`) no longer accepts `priority`.
   - `AssignExaminationRoomRequest` (`POST .../assign-room`) retains optional `priority` + `priorityVerificationConfirmed`, enforcing the same eligibility, mandatory reason, and policy verification rules when elevating to `ELDERLY`/`PREGNANT`.

## Consequences

- **Benefits**: Enforces statutory clinical priority rules, prevents unverified priority elevation, preserves scheduled appointment protection without violating legal priority, and provides complete auditability.
- **Trade-offs**: Frontend clients sending legacy `URGENT` or `RETURN_RESULT` priority values will receive `400 Bad Request` until updated in `#101`.
