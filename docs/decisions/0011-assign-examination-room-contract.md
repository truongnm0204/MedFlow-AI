# ADR 0011: Assign Examination Room Contract (M2-028 / UC27)

- **Status**: Accepted
- **Date**: 2026-04-19
- **Issue**: `#80` (`M2-028 — [M2][BE] Assign Examination Room`, UC27)

## Context

Outpatient visits (`visits`) checked in at reception require routing to a compatible, active, and available clinical examination room (`hospital_rooms` with `room_type = 'EXAMINATION'`). In the v4.1 PostgreSQL schema (`database/medflow_schema_v4_1.sql`), `visits.examination_room_id`, `queue_service_points`, and `queue_entries` already exist, so no SQL schema migration is required.

## Decision

1. **HTTP Endpoints (`VisitsController`)**:
   - `GET /api/v1/visits/{visitIdOrCode}/available-rooms` (alias: `/assignable-rooms`, also mounted under `/api/v1/reception/visits`) returns all `EXAMINATION` rooms in the visit's hospital with `isCompatible`, `isAvailable`, `currentOccupancy`, `availableCapacity`, and `unavailableReason`.
   - `PUT|POST /api/v1/visits/{visitIdOrCode}/examination-room` (alias: `/assign-room`, also mounted under `/api/v1/reception/visits`) assigns a target room by `examinationRoomId`/`roomId` or `roomCode`, optionally setting `priority` (`NORMAL`, `URGENT`, `ELDERLY`, `PREGNANT`, `RETURN_RESULT`) and `reason`/`note`.

2. **Two-Layer Authorization**:
   - **Layer 1**: `[Authorize(Roles = "RECEPTION,HOSPITAL_ADMIN,SYSTEM_ADMIN")]` on `VisitsController`.
   - **Layer 2 (`VisitRoomAssignmentService`)**: Verifies actor user is `ACTIVE` and unlocked, holds `RECEPTION`/`HOSPITAL_ADMIN`/`SYSTEM_ADMIN` role and `VISIT_ASSIGN` permission (for non-system-admin roles when `role_permissions` is populated), has an active `Staff` profile scoped to an active `Hospital`, and enforces hospital boundary ownership on both the `Visit` and the target `HospitalRoom`.

3. **Domain Invariants & Queue Routing**:
   - **UC26 Prerequisite**: A visit must have an active assigned doctor (`Visit.AssignedDoctorId` or `Appointment.Slot.DoctorId`, which is persisted onto `Visit.AssignedDoctorId` if not yet set) before an examination room can be assigned (`409 Conflict` if missing).
   - **Compatibility**: Target room must belong to the same hospital, have `RoomType == "EXAMINATION"`, `Status == "ACTIVE"`, active `Department`, and match the department of the visit's service/assigned doctor (`400 Bad Request` on type/department mismatch; `409 Conflict` on inactive/maintenance room).
   - **Capacity**: Active occupancy (`CHECKED_IN`, `CONFIRMED`, `ROUTED`, `WAITING`, `CALLED`, `IN_PROGRESS`, `IN_CONSULTATION` excluding the current visit) must be strictly less than `room.Capacity` (`409 Conflict` when full). Excluding the current visit makes same-room re-assignment idempotent.
   - **Queue & Audit**: Assigning a room transitions `Visit.Status` to `WAITING` (preserving `CALLED` if already called), links or creates the `EXAMINATION` `QueueEntry` (`Status = "WAITING"`, `PresenceStatus = "PRESENT"`, `CurrentServicePointId` resolved from `queue_service_points`), and writes an `AuditLog` with `Action = "VISIT_EXAMINATION_ROOM_ASSIGNED"`.

4. **Test-Safe `IApplicationDbContext` Extension**:
   - `QueueServicePoint` is mapped to `"queue_service_points"` in `MedFlow.Infrastructure` and exposed on `IApplicationDbContext` with a default interface member throwing `NotSupportedException` so existing unit-test `DbContext` doubles continue compiling without modification.
