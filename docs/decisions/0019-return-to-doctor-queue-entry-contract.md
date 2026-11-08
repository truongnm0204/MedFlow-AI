# 0019 — Return-to-Doctor Queue Entry (`RETURN_CONSULTATION`) Contract & Readiness Gating

- Ngay: 2026-04-17
- Trang thai: accepted
- Lane: high-risk

## Context

Issue `#100` (`M3-002 — [M3][BE] Create Return-to-Doctor Queue Entry`, UC28) requires a dedicated backend workflow and HTTP API to create or activate a post-diagnostic return consultation queue entry (`queueType = "RETURN_CONSULTATION"`) when a patient returns to the examination room after completing diagnostic procedures. Previously, `VisitRoomAssignmentService.UpsertExaminationQueueEntryAsync` only inserted `EXAMINATION` queue entries, and no endpoint existed to transition a visit returning from diagnostics into a `RETURN_CONSULTATION` entry or activate an existing `WAITING` return entry once mandatory diagnostic results (`DIAGNOSTIC/final_result_required`) and patient presence (`presenceStatus = "PRESENT"`) were satisfied.

## Decision

1. **Dedicated Return-to-Doctor Endpoints & Two-Layer Authorization**:
   - Expose `POST /api/v1/reception/queue-entries/{idOrCode}/return-to-doctor` (with aliases `/api/v1/reception/examination-queue/entries/{idOrCode}/return-to-doctor`, `/api/v1/queue-entries/{idOrCode}/return-to-doctor`, `/api/v1/visits/{idOrCode}/return-to-doctor`, and `/api/v1/reception/visits/{idOrCode}/return-to-doctor`) on `QueueReturnConsultationController`.
   - Layer 1: `[Authorize(Roles = "DOCTOR,RECEPTION,HOSPITAL_ADMIN,SYSTEM_ADMIN")]` (`401` unauthenticated, `403` for `PATIENT` / `DIAGNOSTIC_STAFF`).
   - Layer 2: `ResolveCallNextActorAsync` + hospital ownership check + `QueueServicePoint.DoctorId == actorDoctor.Id` ownership enforcement when called by a `DOCTOR`.

2. **Orthogonal `queueType` vs. `priority` & Queue Number Sequence**:
   - `queueType` is set to `"RETURN_CONSULTATION"`, strictly separated from `manual_priority` (`NORMAL`, `ELDERLY`, `PREGNANT`), which is inherited from the initial `EXAMINATION` entry.
   - When creating a new `RETURN_CONSULTATION` entry (`actionOutcome = "CREATED"`), any open `EXAMINATION` entry for the visit is closed (`COMPLETED`), and a new per-day, per-service-point `queueNumber` (`MAX(queue_number) + 1` across clinical queue entries on `(service_point_id, queue_date)`) is allocated.
   - `VisitRoomAssignmentService` also includes `RETURN_CONSULTATION` when computing `MAX(queue_number)` so subsequent `EXAMINATION` entries never collide on `uq_queue_entries_service_point_id_queue_date_queue_number`.
   - When activating an existing `WAITING` `RETURN_CONSULTATION` entry (`actionOutcome = "ACTIVATED"`), its `queueNumber` is preserved and `stateVersion` is incremented.

3. **Mandatory Diagnostic Readiness & Presence Gating**:
   - Readiness is governed by `DIAGNOSTIC/final_result_required` (via `QueueOrderingPolicyLoader`) and open `DIAGNOSTIC` rows in `queue_entries` for the visit (`openDiagnosticCount == 0 && request.DiagnosticResultsReady != false`).
   - If `ConfirmPresent == true` and all mandatory diagnostic results are ready, the `RETURN_CONSULTATION` entry transitions to `status = "READY"`, `presenceStatus = "PRESENT"`, `isEligible = true`, `queueGroup = "ELIGIBLE"`, and `visit.Status = "RETURN_CONSULTATION"`.
   - If mandatory diagnostic results are still pending, creating a new return entry places it in `status = "WAITING"`, `isEligible = false`, `queueGroup = "NOT_ELIGIBLE"`, `ineligibleReason = "AWAITING_RESULTS"`, and `visit.Status = "AWAITING_RESULTS"`, while attempting to re-activate an already `WAITING` return entry before results complete returns `409 Conflict`.
   - `QueueStatusService.UpdateQueueEntryStatusAsync` also blocks manual transitions of `RETURN_CONSULTATION` entries to `READY` / `CALLED` / `IN_PROGRESS` while mandatory diagnostics remain open, and preserves `visit.Status = "RETURN_CONSULTATION"` across non-terminal return-entry transitions.

4. **Fairness Rotation, Appointment Protection & Audit Trail**:
   - Newly created `RETURN_CONSULTATION` entries with `ScheduledAt = null` do not inherit the morning initial appointment's `slot.StartsAt`, ensuring `ExaminationQueueOrderingEngine` evaluates them in Tier 1 (fast-tracked ahead of `NORMAL` waiting entries when the safe appointment gap holds, interleaved with `NORMAL` entries via `QueueFairnessRotation` and `walkin_priority_ratio`, and never overriding due/upcoming protected appointments).
   - Every creation/activation writes an `AuditLog` with `Action = "QUEUE_RETURN_TO_DOCTOR"` and `EntityType = "QUEUE_ENTRY"`.

## Consequences

- **Duoc**: Complete UC28 return-to-doctor lifecycle (`CREATED` / `ACTIVATED`) with diagnostic readiness gating, immutable queue numbers, fairness rotation, and optimistic concurrency (`ExpectedStateVersion`).
- **Mat**: Requires callers to supply a valid coordination reason (`5..300` chars) for auditability.
- **Rui ro con lai**: Until `diagnostic_orders` is mapped in EF Core in a later M3 slice, open diagnostic readiness is tracked via `DIAGNOSTIC` rows in `queue_entries` and optional `DiagnosticResultsReady` input.
