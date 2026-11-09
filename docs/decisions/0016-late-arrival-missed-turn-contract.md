# ADR 0016: Late Arrival Classification & Missed Turn / Restore Turn Contract (M2-046 / UC28)

- **Status**: Accepted
- **Date**: 2026-04-20
- **Issue**: `#98` (`M2-046 — [M2][BE] Handle Late Arrival / Missed Turn`, UC28)
- **Lane**: high-risk

## Context

Under UC28 (`docs/MedFlow_Quy_Tac_Dieu_Phoi_Hang_Doi.md`), reception staff need backend enforcement for two scenarios:
1. **Late Check-in / Room Assignment**: Scheduled patients who arrive after `ScheduledAt + grace_minutes` must be classified as `arrivalClass = LATE`, which removes appointment protection (`isAppointmentProtected = false`) so they do not preempt on-time appointments. Previously, `VisitRoomAssignmentService.UpsertExaminationQueueEntryAsync` persisted `"ON_TIME_OR_GRACE"` unconditionally.
2. **Absent When Called (`Missed Turn`) & Subsequent Return (`Restore Turn`)**: When a patient in `CALLED` status does not appear at the room, staff mark the entry as `SKIPPED` (`presenceStatus = LEFT`) with a mandatory reason while preserving `queueNumber`. When the patient returns to reception and presence is confirmed (`ConfirmPresent = true`), the entry transitions `SKIPPED -> READY` (`presenceStatus = PRESENT`), re-evaluates arrival classification using the return time against `ScheduledAt + grace_minutes`, and queues FIFO from the return timestamp (`ReadyAt = nowLocal`).

## Decision

1. **Shared Arrival Classifier (`QueueArrivalClassifier`) & Room Assignment Persistence**:
   - Extract `QueueArrivalClassifier.Classify(isScheduled, scheduledAt, arrivedAt, graceMinutes, fallback)` in `MedFlow.Application.Services.Visits.Queueing` and share it between `ExaminationQueueOrderingEngine.Evaluate` and `VisitRoomAssignmentService.UpsertExaminationQueueEntryAsync`.
   - At room assignment, load the effective `grace_minutes` policy for the hospital/service point via `QueueOrderingPolicyLoader` (default `15`) and persist the computed `ArrivalClass` (`WALK_IN`, `EARLY`, `ON_TIME_OR_GRACE`, or `LATE`) on `queue_entries`.

2. **Board Presence-Confirmation Timestamp Precedence**:
   - In `ExaminationQueueService.BuildQueueBoardAsync`, pass `ArrivedAt: q?.ArrivedAt ?? v?.CheckedInAt` into `QueueOrderingItem` while keeping the display `CheckedInAt` field anchored to the original check-in timestamp.
   - For newly assigned entries, `q.ArrivedAt` equals `v.CheckedInAt`. When a skipped entry is restored, `q.ArrivedAt` is updated to the return timestamp (`nowLocal`), allowing `ExaminationQueueOrderingEngine` to reclassify the restored entry as `ON_TIME_OR_GRACE` (if still within `ScheduledAt + grace_minutes`) or `LATE` (if past grace).

3. **Dedicated Missed-Turn & Restore-Turn HTTP Endpoints**:
   - `POST /api/v1/reception/queue-entries/{idOrCode}/missed-turn` and alias `POST /api/v1/reception/examination-queue/entries/{idOrCode}/missed-turn` (`MarkMissedTurnRequest`: `servicePointId`, `reason`, `notes`, `expectedStateVersion`).
   - `POST /api/v1/reception/queue-entries/{idOrCode}/restore-turn` and alias `POST /api/v1/reception/examination-queue/entries/{idOrCode}/restore-turn` (`RestoreQueueTurnRequest`: `servicePointId`, `confirmPresent`, `reason`, `notes`, `expectedStateVersion`).
   - Protected by Layer 1 `[Authorize(Roles = "RECEPTION,HOSPITAL_ADMIN,SYSTEM_ADMIN")]` and Layer 2 hospital/service-point ownership verification via `BuildQueueBoardAsync`.
   - Both return `200 OK` with `QueueTurnActionResponseDto` (including `previousServingOrder`, recomputed `servingOrder`, `isAppointmentProtected`, `stateVersion`, and `updatedEntry`).
   - The generic queue status endpoint (`PUT /api/v1/reception/queue-entries/{idOrCode}/status`, ADR 0012) remains unchanged.

4. **Transactional State Transitions & Audit Logging**:
   - **Mark Missed Turn (`MarkMissedTurnAsync`)**:
     - Requires current board status `CALLED` (otherwise `409 Conflict`), today's queue date, matching `servicePointId`, non-terminal visit status, and matching `ExpectedStateVersion` when supplied.
     - Transitions `Status = "SKIPPED"`, `PresenceStatus = "LEFT"`, `BlockedReason = reason`, `StartedAt = null`, increments `StateVersion`, syncs `visit.Status = "WAITING"`, preserves `QueueNumber` and `CalledAt`, and writes an `audit_logs` row with `Action = "QUEUE_MISSED_TURN"` and `EntityType = "QUEUE_ENTRY"`.
   - **Restore Skipped Turn (`RestoreSkippedTurnAsync`)**:
     - Requires `ConfirmPresent == true` (`400 Bad Request` otherwise) and current board status `SKIPPED` (`409 Conflict` otherwise).
     - Transitions `Status = "READY"`, `PresenceStatus = "PRESENT"`, `ArrivedAt = nowLocal`, `ReadyAt = nowLocal`, clears `CalledAt`, `StartedAt`, `BlockedReason`, `BackfilledAt`, and `BackfillReason`, increments `StateVersion`, syncs `visit.Status = "WAITING"`, preserves `QueueNumber`, syncs `target.ArrivalClass` to the post-restore `EffectiveArrivalClass`, and writes an `audit_logs` row with `Action = "QUEUE_TURN_RESTORED"` and `EntityType = "QUEUE_ENTRY"`.

## Consequences

- **Benefits**: Late check-ins and post-grace returns lose appointment protection deterministically on the backend while within-grace returns keep protection; `QueueNumber` stays immutable; `ServingOrder` and `AuditLog` entries reflect every missed-turn and restore action atomically.
- **Trade-offs**: The generic `PUT /queue-entries/{id}/status` `SKIPPED -> READY` transition does not update `ArrivedAt` and therefore preserves prior placement; reception UI (`#97`) should use the dedicated `missed-turn` and `restore-turn` endpoints for UC28 missed-turn workflows.
