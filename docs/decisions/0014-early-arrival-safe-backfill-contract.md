# ADR 0014: Early Arrival / Safe Backfill & Queue Ordering Engine Contract (M2-044 / UC28)

- **Status**: Accepted
- **Date**: 2026-04-20
- **Issue**: `#96` (`M2-044 — [M2][BE] Handle Early Arrival / Safe Backfill`, UC28)
- **Lane**: high-risk

## Context

Reception staff need a controlled server-side mechanism to evaluate and execute **Safe Backfill** (`SAFE_BACKFILL`) or **Hold for Slot** (`HOLD_FOR_SLOT`) for early-arriving or waiting examination queue patients without violating appointment-time protection for on-time scheduled appointments (`docs/MedFlow_Quy_Tac_Dieu_Phoi_Hang_Doi.md` §2–§4, §7–§8). Previously, `ExaminationQueueService` used a static priority/arrival sort and the frontend computed backfill eligibility heuristically. Backend must become the single decision-maker for queue ordering (`ExaminationQueueOrderingEngine`) and persist backfill actions transactionally with full audit logging.

## Decision

1. **Database Schema (`010_queue_early_arrival_backfill.sql`) & Optimistic Concurrency**:
   - Add `"backfilled_at" timestamp without time zone` and `"backfill_reason" text` to `queue_entries`.
   - Seed hospital-level `QUEUE` policies (`expected_duration_minutes = {"minutes": 15}`, `safety_buffer_minutes = {"minutes": 5}`) for all existing hospitals where missing.
   - Configure `QueueEntry.StateVersion` (`state_version`) as an EF Core concurrency token (`.IsConcurrencyToken()`) and map `DbUpdateConcurrencyException` to `409 Conflict` (`ProblemDetails`) in `ExceptionMiddleware`.

2. **Queue Policy Loader (`QueueOrderingPolicyLoader`)**:
   - Reads active `QUEUE` policies (`grace_minutes`, `expected_duration_minutes`, `safety_buffer_minutes`) scoped to the hospital at `nowUtc`, preferring service-point-specific rows (`queue_service_point_id == servicePointId`) over hospital-wide defaults (`queue_service_point_id IS NULL`), ordered by highest `policy_version` then `id`.
   - Sanitizes missing/invalid values: `grace_minutes` defaults to `15` when missing or `< 0`; `expected_duration_minutes` (`<= 0` or missing) and `safety_buffer_minutes` (`< 0` or missing) evaluate to `null`.

3. **Pure Queue Ordering & Safe-Backfill Engine (`ExaminationQueueOrderingEngine`)**:
   - Computes `EffectiveArrivalClass` from `IsScheduled`, `ScheduledAt`, `ArrivedAt`, and `grace_minutes` (`WALK_IN`, `EARLY`, `ON_TIME_OR_GRACE`, `LATE`), falling back to `StoredArrivalClass` if timestamps are absent.
   - Computes `IsEligible` (`Status in {WAITING, READY}` and `PresenceStatus == PRESENT`), `IsAppointmentProtected` (scheduled, active waiting, not `LEFT`, not `LATE`, and either `PRESENT` or within `ScheduledAt + grace_minutes`), and `IsDueProtected` (`IsAppointmentProtected && PresenceStatus == PRESENT && ScheduledAt <= now`).
   - Evaluates `CanSafeBackfill` and Vietnamese `HoldReason`: requires active waiting (`WAITING`/`READY`), `PresenceStatus == PRESENT`, not already due-protected itself, no other due-protected appointment in the bucket, room idle (no `CALLED` or `IN_PROGRESS` entry in the bucket), and either no upcoming protected appointment (`ProtectedNextStartsAt == null`) or valid `expected_duration_minutes` (`d`) + `safety_buffer_minutes` (`b`) satisfying `now + d + b <= ProtectedNextStartsAt`.
   - Orders entries deterministically:
     1. Status rank: `IN_PROGRESS` (0) < `CALLED` (1) < `WAITING` / `READY` merged (2) < `SKIPPED` (3) < `COMPLETED` (4) < other (5).
     2. Active waiting tier (for status rank 2): `IsDueProtected` (0, ordered by `ScheduledAt`) < `IsEligible && Status == READY && BackfilledAt != null` (1, ordered by `BackfilledAt`) < `IsEligible` (2, ordered by `priorityRank`: `ELDERLY` 0 < `PREGNANT` 1 < all others including `NORMAL`, legacy `URGENT`, and `RETURN_RESULT` 2, then `ReadySince`) < non-eligible waiting (3, ordered by `ScheduledAt`).
     3. Tie-breakers: `ReadySince`, `QueueNumber` (immutable), then input index.
   - Supersedes ADR 0013 Decision 4 serving order.

4. **HTTP Endpoints & Transactional Command (`ApplyEarlyArrivalBackfillAsync`)**:
   - `POST /api/v1/reception/queue-entries/{idOrCode}/early-arrival-backfill` and alias `POST /api/v1/reception/examination-queue/entries/{idOrCode}/early-arrival-backfill` restricted to `RECEPTION,HOSPITAL_ADMIN,SYSTEM_ADMIN` (Layer 1) plus service-level hospital/staff/permission verification (Layer 2).
   - Inside a database transaction, re-evaluates the queue board for today's queue date, validates service point membership and `WAITING`/`READY` status, applies `SAFE_BACKFILL` (transitions to `READY`, sets `BackfilledAt`, `BackfillReason`, clears `BlockedReason`) or `HOLD_FOR_SLOT` (transitions to `WAITING`, sets `BlockedReason`, clears `BackfilledAt`/`BackfillReason`), increments `StateVersion`, writes an `audit_logs` entry (`Action = "QUEUE_EARLY_ARRIVAL_BACKFILL"`, `EntityType = "QUEUE_ENTRY"`), re-evaluates the board, and returns `200 OK` with `EarlyArrivalBackfillResponseDto` including `updatedEntry` and updated `servingOrder`.

## Consequences

- **Benefits**: Backend enforces appointment protection and safe-backfill invariants atomically with optimistic concurrency and auditability; `QueueNumber` remains immutable while `ServingOrder` reflects real-time clinical and scheduling rules.
- **Trade-offs**: Legacy `URGENT` priority no longer bypasses `ELDERLY`/`PREGNANT` or appointment protection in the examination queue engine (cleanup tracked in `#101`/`#102`); fairness counter rotation (`queue_fairness_states`) remains scoped to `#94` call-next.
