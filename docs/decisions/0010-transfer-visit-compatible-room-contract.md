# ADR 0010: Transfer Patient to Compatible Examination Room Contract (`#82` / `M2-030` / `UC27`)

- **Status**: Accepted
- **Date**: 2026-11-05
- **Issue**: `#82` (`M2-030 — [M2][BE] Transfer Patient to Compatible Room`, UC27)
- **Lane**: high-risk

## Context

Outpatient visits (`visits`) that have already been assigned to an initial clinical examination room (`hospital_rooms` with `room_type = 'EXAMINATION'`) may need to be transferred to another compatible, available examination room when a room becomes overloaded, enters maintenance, or requires clinical load balancing. All required tables and columns (`visits`, `hospital_rooms`, `departments`, `doctors`, `doctor_service_authorizations`, `services`, `examination_slots`, `appointments`, `queue_entries`, `audit_logs`) already exist in `database/medflow_schema_v4_1.sql`.

## Decision

1. **HTTP Endpoints (`VisitRoomTransferController`)**:
   - Inspect compatible rooms & transfer history (`200 OK` -> `VisitCompatibleRoomsResponseDto`):
     - `GET /api/v1/visits/{idOrCode}/compatible-rooms`
     - `GET /api/v1/reception/visits/{idOrCode}/compatible-rooms`
     - `GET /api/v1/visits/{idOrCode}/transfer-room`
     - `GET /api/v1/visits/{idOrCode}/room-transfers`
   - Transfer patient to a compatible room (`200 OK` -> `VisitRoomTransferResponseDto`):
     - `POST /api/v1/visits/{idOrCode}/transfer-room`
     - `POST /api/v1/reception/visits/{idOrCode}/transfer-room`
     - `PUT /api/v1/visits/{idOrCode}/transfer-room`
     - `PATCH /api/v1/visits/{idOrCode}/examination-room/transfer`

2. **Two-Layer Authorization**:
   - **Layer 1**: `[Authorize(Roles = "RECEPTION,HOSPITAL_ADMIN,SYSTEM_ADMIN")]` on `VisitRoomTransferController`.
   - **Layer 2 (`VisitRoomTransferService`)**: Verifies `actorUserId > 0` (`401`), `User` is `ACTIVE` and unlocked (`403`), holds `RECEPTION`/`HOSPITAL_ADMIN`/`SYSTEM_ADMIN` role and at least one of `VISIT_ASSIGN`, `PATIENT_CHECKIN`, `QUEUE_MANAGE`, `HOSPITAL_CONFIG` permissions (for non-system-admin roles when `role_permissions` is populated), has an active `Staff` profile scoped to an active `Hospital`, and enforces hospital boundary ownership on the `Visit`, target `HospitalRoom`, and optional `TargetDoctorId`.

3. **Room Compatibility, Eligibility, Queue Immutability, and Audit Trail**:
   - **Eligibility**: Visit must have an initial `ExaminationRoomId` (`409 Conflict` if unassigned), must not be in terminal state (`COMPLETED`, `CANCELLED`, `NO_SHOW`, `DONE`, or `CompletedAt != null` -> `409 Conflict`), and must not have started clinical consultation (`IN_CONSULTATION`, `IN_PROGRESS`, or `QueueEntry.StartedAt != null` -> `409 Conflict`).
   - **Compatibility & Availability**: Target room must belong to the same hospital, differ from the current room (`400`), have `RoomType == "EXAMINATION"` (`400`), match the visit's required `DepartmentId` (`400`), be `ACTIVE` in an active `Department` (`409`), and have active visit count strictly below `Capacity` (`409`).
   - **Queue Immutability (`public_number_immutable`)**: Preserves `QueueEntry.QueueNumber` unchanged and resets a `CALLED` visit/queue entry back to `WAITING` (`CalledAt = null`).
   - **Audit History**: Persists an `AuditLog` with `Action = "VISIT_ROOM_TRANSFER"`, `EntityType = "VISIT"`, `EntityId = visit.Id.ToString()`, `Reason`, and camelCase JSON `ChangeData`, reconstructing ordered `TransferHistory` (newest first) on both `GET` and `POST`/`PUT`/`PATCH`.
