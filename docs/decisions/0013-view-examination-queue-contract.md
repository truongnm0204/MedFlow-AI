# ADR 0013: View Examination Queue Contract (M2-036 / UC28)

- **Status**: Accepted
- **Date**: 2026-04-19
- **Issue**: `#88` (`M2-036 — [M2][BE] View Examination Queue`, UC28)
- **Lane**: high-risk

## Context

Reception staff (`RECEPTION`) and hospital administrators (`HOSPITAL_ADMIN`, `SYSTEM_ADMIN`) need a live view of clinical examination queues grouped by examination service point / room (`queue_service_points` and `hospital_rooms` with `room_type = 'EXAMINATION'`), showing each patient's current status (`WAITING`, `READY`, `CALLED`, `IN_PROGRESS`, `COMPLETED`, `SKIPPED`), clinical priority (`NORMAL`, `URGENT`, `ELDERLY`, `PREGNANT`, `RETURN_RESULT`), arrival classification, and 1-based serving order (`servingOrder`). The frontend reception module (`#87`, `medflow-ai-frontend/src/features/reception/api/examination-queue.api.ts`) already consumes `GET /api/v1/reception/examination-queue` returning `ExaminationQueueResponseDto`.

## Decision

1. **HTTP Endpoints (`ExaminationQueueController`)**:
   - `GET /api/v1/reception/examination-queue` (aliases: `/api/v1/examination-queue`, `/api/v1/queues/examination`, `/api/v1/reception/queues/examination`) accepts optional query filters `queueDate` (`YYYY-MM-DD`), `servicePointId`, `status`, `priority`, `visitType`, and `keyword` (`<= 100` chars).
   - Returns `200 OK` with `ExaminationQueueResponseDto` (`queueDate`, `facilityName`, `shiftLabel`, `generatedAtUtc`, `stats`, `servicePoints`), `400 Bad Request` (`ProblemDetails`) on invalid filters, `401 Unauthorized` on missing/invalid JWT, `403 Forbidden` on disallowed role/permission/inactive staff or hospital, and `404 Not Found` if the actor or hospital record does not exist.

2. **Two-Layer Authorization & Hospital Boundary Isolation**:
   - **Layer 1**: `[Authorize(Roles = "RECEPTION,HOSPITAL_ADMIN,SYSTEM_ADMIN")]` on `ExaminationQueueController`.
   - **Layer 2 (`ExaminationQueueService`)**: Verifies the actor user exists, is `ACTIVE` and unlocked, holds `RECEPTION`, `HOSPITAL_ADMIN`, or `SYSTEM_ADMIN` role and at least one queue/reception permission (`QUEUE_MANAGE`, `VISIT_ASSIGN`, `PATIENT_CHECKIN`, `HOSPITAL_CONFIG`) when `role_permissions` is populated for non-system-admins, and belongs to an `ACTIVE` `Staff` profile and `ACTIVE` `Hospital`. Only service points, rooms, visits, and queue entries belonging to the actor's hospital are returned.

3. **Service Point Grouping & Room Transfer Reconciliation**:
   - Builds examination service point buckets from hospital-scoped `QueueServicePoint` entities (`PointType` excluding `DIAGNOSTIC`, `RECEPTION`, `PHARMACY`) plus any unlinked `EXAMINATION` `HospitalRoom` in the hospital.
   - Catches `NotSupportedException` when `IApplicationDbContext.QueueServicePoints` is not implemented on legacy test doubles, falling back to `EXAMINATION` `HospitalRoom` buckets.
   - Reconciles `Visit.ExaminationRoomId` overrides (e.g., after UC27 room assignment `#80` or room transfer `#82`) so transferred visits appear under their target room's service point bucket immediately.

4. **UC28 1-Based Serving Order**:
   - Sorts entries within each service point bucket by:
     1. Status order: `IN_PROGRESS` (0) < `CALLED` (1) < `READY` (2) < `WAITING` (3) < `SKIPPED` (4) < `COMPLETED` (5).
     2. Clinical priority (for active/waiting entries): `URGENT` (0) < `RETURN_RESULT` (1) < `ELDERLY` (2) < `PREGNANT` (3) < `NORMAL` (4).
     3. Arrival class (for waiting/ready entries): `ON_TIME_OR_GRACE` (0) < `EARLY` (1) < `WALK_IN` (2) < `LATE` (3).
     4. Check-in timestamp ascending, then `queueNumber` ascending.
   - Computes `currentServingTicketNo` (`IN_PROGRESS` or `CALLED`) and `nextTicketNo` (first `READY`/`WAITING` entry with `presenceStatus != "LEFT"`) from the bucket's sorted queue.

## Consequences

- **Benefits**: Activates live reception queue monitoring in the frontend (`#87`) without schema changes, preserves strict cross-hospital multi-tenancy isolation, and stays synchronized with room assignment (`#80`) and room transfer (`#82`).
- **Trade-offs**: Service-level aggregation joins hospital-scoped visits, slots, and queue entries in memory per hospital day, which is fast for daily outpatient clinic volumes (~hundreds of visits/day) and compatible with both PostgreSQL and EF Core InMemory test providers.
