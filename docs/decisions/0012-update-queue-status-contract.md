# ADR 0012: Update Queue Status Contract (M2-038 / UC28)

- **Status**: Accepted
- **Date**: 2026-04-19
- **Issue**: `#90` (`M2-038 — [M2][BE] Update Queue Status`, UC28)

## Context

Outpatient clinical queues (`queue_entries`) require strict status transitions (`WAITING`, `READY`, `CALLED`, `IN_PROGRESS`, `COMPLETED`, `SKIPPED`, `CANCELLED`), optimistic concurrency protection via `queue_entries.state_version`, synchronized `visits.status` / `visits.completed_at` updates, two-layer hospital-scoped authorization, and an immutable `audit_logs` trail (`QUEUE_STATUS_UPDATED`). `database/medflow_schema_v4_1.sql` already defines `queue_entries.state_version`, so no SQL schema migration is required.

## Decision

1. **HTTP Endpoints (`QueueStatusController`)**:
   - `GET /api/v1/queue-entries/{idOrCode}/status` (aliases under `/api/v1/reception/queue-entries/{idOrCode}/status`, `/api/v1/reception/examination-queue/entries/{idOrCode}/status`, `/api/v1/visits/{idOrCode}/queue-status`, `/api/v1/reception/visits/{idOrCode}/queue-status`) returns `QueueEntryStatusDetailDto` with current status, `stateVersion`, `allowedTargetStatuses`, and `statusHistory`.
   - `PUT|PATCH|POST /api/v1/queue-entries/{idOrCode}/status` (and matching aliases) validates and executes the transition, returning `UpdateQueueStatusResponseDto`.

2. **Two-Layer Authorization**:
   - **Layer 1**: `[Authorize(Roles = "RECEPTION,DOCTOR,DIAGNOSTIC_STAFF,HOSPITAL_ADMIN,SYSTEM_ADMIN")]` on `QueueStatusController`.
   - **Layer 2 (`QueueStatusService`)**: Verifies actor user is `ACTIVE` and unlocked, holds an allowed operator role (`PATIENT` and `PHARMACY_STAFF` rejected with `403 Forbidden`) and `QUEUE_MANAGE` (or clinical equivalent permission when `role_permissions` is populated), has an active `Staff` profile scoped to an active `Hospital`, enforces hospital boundary ownership on the `QueueEntry`/`Visit`, and restricts `DOCTOR`-only actors to queue entries assigned to their doctor profile or service point.

3. **State Machine, Concurrency, and Synchronization**:
   - **Allowed Transitions**:
     - `RESERVED → WAITING | READY | CANCELLED`
     - `WAITING → READY | CALLED | SKIPPED | CANCELLED`
     - `READY → WAITING | CALLED | SKIPPED | CANCELLED`
     - `CALLED → IN_PROGRESS | WAITING | READY | SKIPPED | CANCELLED`
     - `IN_PROGRESS → COMPLETED | WAITING | SKIPPED`
     - `SKIPPED → WAITING | READY | CALLED | CANCELLED`
     - `COMPLETED` and `CANCELLED` are terminal (`409 Conflict` on further transitions).
   - **Single Active `IN_PROGRESS` Guard**: Prevents starting `IN_PROGRESS` when another entry in the same service point or examination room on the same `QueueDate` is already `IN_PROGRESS` (`409 Conflict`).
   - **Optimistic Concurrency (`state_version`)**: Maps `QueueEntry.StateVersion` to `queue_entries.state_version`, validates optional `expectedStateVersion` (`409 Conflict` on mismatch), and increments `StateVersion` on every transition while keeping `QueueNumber` immutable.
   - **Visit & Audit Synchronization**: Updates `ReadyAt`, `CalledAt`, `StartedAt`, `CompletedAt`, `PresenceStatus`, and `BlockedReason` on `QueueEntry`, synchronizes `Visit.Status` (`WAITING`, `CALLED`, `IN_PROGRESS`, `COMPLETED`, `CANCELLED`) and `Visit.CompletedAt`, and appends an `AuditLog` (`Action = "QUEUE_STATUS_UPDATED"`, `EntityType = "QUEUE_ENTRY"`).
