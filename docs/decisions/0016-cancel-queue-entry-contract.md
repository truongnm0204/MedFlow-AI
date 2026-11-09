# ADR 0016: Cancel Queue Entry Contract (M2 / #104 / M2-050 / UC28)

- **Status**: Accepted
- **Date**: 2026-04-20
- **Issue**: `#104` (`[M2][BE] Cancel Queue Entry`)
- **Lane**: high-risk

## Context

Outpatient reception and queue management require a dedicated workflow (UC28 / M2-050) to cancel an active clinical examination queue entry (`RESERVED / WAITING / READY / CALLED / SKIPPED -> CANCELLED`) when a patient leaves or withdraws before consultation starts.

Previously, `CANCELLED` was listed in the generic `PATCH /api/v1/queue-entries/{idOrCode}/status` transition table without a dedicated endpoint, role restriction (`DOCTOR` could trigger it), or dedicated audit action (`QUEUE_ENTRY_CANCELLED`). Furthermore, if a `RESERVED` queue entry linked to an appointment was cancelled before check-in, subsequent room assignment (`FindExistingExaminationQueueEntryAsync`) could match the cancelled entry instead of allocating a fresh queue number.

## Decision

1. **Dedicated Cancel Queue Entry Endpoint**:
   - `POST /api/v1/queue-entries/{idOrCode}/cancel`
   - Aliases:
     - `POST /api/v1/reception/queue-entries/{idOrCode}/cancel`
     - `POST /api/v1/reception/examination-queue/entries/{idOrCode}/cancel`
   - Returns `200 OK` with `UpdateQueueStatusResponseDto` (`currentStatus = "CANCELLED"`, `allowedTargetStatuses = []`, updated `stateVersion`, preserved `queueNumber` and `ticketNo`, and `statusHistory` including the cancellation event).

2. **Two-Layer Authorization**:
   - **Layer 1 (Controller)**: `[Authorize(Roles = "RECEPTION,HOSPITAL_ADMIN,SYSTEM_ADMIN")]` (`DOCTOR` and `DIAGNOSTIC_STAFF` receive `403 Forbidden`).
   - **Layer 2 (Service)**: Requires active staff in the queue entry's hospital (or `SYSTEM_ADMIN`) and permission `QUEUE_MANAGE` for non-system-admins.

3. **Request Contract & Optimistic Concurrency**:
   - Request DTO `CancelQueueEntryRequest`:
     - `reason` (required, trimmed `1..500` chars) — missing/blank returns `400 Bad Request`.
     - `expectedStateVersion` (required, `>= 1`) — missing or `< 1` returns `400 Bad Request`; mismatch against `queue_entries.state_version` returns `409 Conflict`.
     - `notes` (optional, trimmed `<= 500` chars).

4. **Allowed Source Statuses & Conflict Rules (`409 Conflict`)**:
   - Allowed source statuses: `RESERVED`, `WAITING`, `READY`, `CALLED`, `SKIPPED` on clinical examination queues (`EXAMINATION`, `RETURN_CONSULTATION`).
   - Rejects non-clinical queue types (e.g., `DIAGNOSTIC`) with `409 Conflict`.
   - Rejects entries already in `CANCELLED` (`"Lượt xếp hàng đã được hủy trước đó."`) with `409 Conflict`.
   - Rejects entries in `IN_PROGRESS` or `COMPLETED` (or with terminal visit status) with `409 Conflict`.

5. **Side Effects on Visit, Presence, Queue Number, and Serving Order**:
   - **Queue Number**: `queue_entries.queue_number` is preserved and never reused.
   - **Presence Status**: Preserves `NOT_ARRIVED` if the entry was `NOT_ARRIVED` (e.g., `RESERVED` entry where patient never arrived); otherwise transitions to `LEFT`.
   - **Visit Status**:
     - When `resolvedQueueType == EXAMINATION`, the linked `visit.Status` is set to `CANCELLED` (`CompletedAt = nowLocal`).
     - When `resolvedQueueType == RETURN_CONSULTATION`, the visit remains open (`visit.Status` untouched) because primary examination already occurred.
     - `appointments` are never modified by queue cancellation (appointment cancellation is governed by UC14).
   - **Serving Order**: `servingOrder` is computed dynamically on read by `ExaminationQueueService.GetExaminationQueueAsync`, which excludes `CANCELLED` entries from active candidates and recalculates `1..N` order for remaining entries.

6. **Audit Logging & Status History**:
   - Writes an `audit_logs` record with `action = "QUEUE_ENTRY_CANCELLED"`, `entity_type = "QUEUE_ENTRY"`, `entity_id = queueEntry.Id`, `reason`, and serialized `QueueStatusTransitionAuditPayload` in `change_data`.
   - `LoadStatusHistoryAsync` includes both `QUEUE_STATUS_UPDATED` and `QUEUE_ENTRY_CANCELLED` actions.

7. **Single Entry Point Consolidation & Room Assignment Guard**:
   - `PATCH /api/v1/queue-entries/{idOrCode}/status` no longer accepts `CANCELLED` and returns `400 Bad Request` directing callers to `POST /api/v1/queue-entries/{idOrCode}/cancel`.
   - `VisitRoomAssignmentService.FindExistingExaminationQueueEntryAsync` filters out `CANCELLED` queue entries so that assigning a room after a cancelled reservation allocates a new `queue_entries` row with `MAX(queue_number) + 1` instead of reusing the cancelled entry or number.

## Consequences

- **Benefits**: Enforces a single, auditable, concurrency-safe cancellation path restricted to authorized queue managers, preserves issued queue numbers without reuse, and keeps `UpdateQueueStatusResponseDto` compatible for frontend integration (`#103`).
- **Trade-offs**: Any caller attempting to cancel via `PATCH .../status` receives `400 Bad Request` and must use `POST .../cancel`.
