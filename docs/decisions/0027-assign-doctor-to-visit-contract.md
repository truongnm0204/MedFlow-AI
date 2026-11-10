# 0027 — Assign Doctor to Visit Contract (`#78` / `M2-026` / `UC26`)

- Ngay: 2026-11-10
- Trang thai: accepted
- Lane: high-risk

## Context

Issue `#78` (`M2-026 — [M2][BE] Assign Doctor to Visit`, `UC26`) requires backend endpoints and domain logic for reception staff to inspect candidate doctors with live workload metrics and assign (or re-assign) a compatible, available doctor to an outpatient visit (`visits.assigned_doctor_id`).

In the MedFlow outpatient routing workflow:
1. A patient arrives via scheduled check-in (`UC24`, `#72`) or walk-in registration (`UC25`, `#76`), creating a `Visit` record.
2. Downstream clinical room assignment (`UC27`, `#80` in `VisitRoomAssignmentService`) and ticket printing (`UC29`, `#84` in `VisitTicketService`) enforce `UC26` as a hard prerequisite (`Visit.AssignedDoctorId` must be populated with an active `Doctor`, otherwise returning `409 Conflict`).
3. The frontend reception routing workbench (`#77`, `medflow-ai-frontend/src/features/reception/types/assign-doctor.types.ts`) expects a patient/visit summary (`VisitPatientSummary`), candidate doctor workload cards (`DoctorWorkloadItem` with `waitingCount`, `currentTicket`, `estimatedWait`, `workloadLevel`: `LOW | MEDIUM | HIGH`), and doctor assignment confirmation.
4. All required tables (`visits`, `doctors`, `staff`, `departments`, `specialties`, `doctor_service_authorizations`, `services`, `service_prices`, `examination_slots`, `hospital_rooms`, `queue_service_points`, `queue_entries`, `audit_logs`) already exist in `database/medflow_schema_v4_1.sql` and are exposed on `IApplicationDbContext`. No SQL migration or `IApplicationDbContext` signature change is needed.

## Decision

1. **HTTP Endpoints (`VisitDoctorAssignmentController`)**:
   - Inspect candidate doctors & workload for a visit (`200 OK` -> `AssignableDoctorListResponseDto`):
     - `GET /api/v1/visits/{visitIdOrCode}/available-doctors` (canonical)
     - `GET /api/v1/visits/{visitIdOrCode}/assignable-doctors`
     - `GET /api/v1/visits/{visitIdOrCode}/doctors`
     - `GET /api/v1/reception/visits/{visitIdOrCode}/available-doctors` (canonical reception route)
     - `GET /api/v1/reception/visits/{visitIdOrCode}/assignable-doctors`
     - `GET /api/v1/reception/visits/{visitIdOrCode}/doctors`
     - Supports optional query parameters `serviceId`, `serviceCode`, and `departmentId` to filter or supply service context for walk-in visits that do not have an `AppointmentId`.
   - Assign doctor to a visit (`200 OK` -> `AssignDoctorToVisitResponseDto`):
     - `PUT /api/v1/visits/{visitIdOrCode}/doctor` (canonical)
     - `POST /api/v1/visits/{visitIdOrCode}/doctor`
     - `PATCH /api/v1/visits/{visitIdOrCode}/doctor`
     - `PUT /api/v1/visits/{visitIdOrCode}/assign-doctor`
     - `POST /api/v1/visits/{visitIdOrCode}/assign-doctor`
     - Also mounted under `/api/v1/reception/visits/{visitIdOrCode}/...`.

2. **Two-Layer Authorization (`401` / `403` / `404`)**:
   - **Layer 1**: `[Authorize(Roles = "RECEPTION,HOSPITAL_ADMIN,SYSTEM_ADMIN")]` on `VisitDoctorAssignmentController`.
   - **Layer 2 (`VisitDoctorAssignmentService`)**:
     - Verifies `actorUserId > 0` (`401`), `Users.AccountStatus == ACTIVE` (`403`), and `LockedUntil` not active (`403`).
     - Verifies DB role membership in `{"RECEPTION", "HOSPITAL_ADMIN", "SYSTEM_ADMIN"}` and permission `VISIT_ASSIGN` (or `PATIENT_CHECKIN` / `QUEUE_MANAGE` / `HOSPITAL_CONFIG` for `HOSPITAL_ADMIN`) when `role_permissions` is populated.
     - Verifies an `ACTIVE` `Staff` profile scoped to an active `Hospital` (`IsActive != false`) for non-`SYSTEM_ADMIN` actors.
     - Enforces hospital boundary ownership on the `Visit`, target `Doctor`, and optional target `HospitalRoom` (`403 Forbidden` on cross-hospital access).

3. **Domain Invariants, Compatibility & Workload Calculation (`400` / `404` / `409`)**:
   - **Visit Eligibility (`409 Conflict`)**:
     - Rejects visits in terminal status (`COMPLETED`, `CANCELLED`, `NO_SHOW`, `DONE`, or `CompletedAt != null`).
     - Rejects visits already in active clinical consultation (`IN_CONSULTATION`, `IN_PROGRESS`, `AWAITING_DIAGNOSTIC`, `AWAITING_RESULTS`, `RETURN_CONSULTATION`, or `QueueEntry.StartedAt != null && CompletedAt == null`).
   - **Doctor Availability & Compatibility**:
     - Target doctor is resolved by `doctorId` (`long`) or `doctorCode` (`string`, e.g. `DOC-001`). Missing both returns `400 Bad Request`; non-existent doctor returns `404 Not Found`.
     - Inactive doctor (`!doctor.IsActive`), inactive staff (`staff.Status != "ACTIVE"`), or inactive department (`department.IsActive == false`) returns `409 Conflict`.
     - Service authorization: when the visit has a required examination `Service` (from `Appointment.Slot.ServiceId` or request `serviceId`/`serviceCode`) and `doctor_service_authorizations` is populated for the doctor, the doctor must hold an active authorization (`IsActive == true`) for that service (`400 Bad Request` otherwise).
     - Department compatibility: doctor's department (`doctor.Staff.DepartmentId`) must match the visit's required department and any already-assigned examination room's department (`400 Bad Request` on mismatch).
   - **Workload Metrics**:
     - Computes `waitingCount` (active waiting/ready/called visits or queue entries today in Vietnam time `UTC+7`, excluding the current visit), `currentTicket` (3-digit ticket number of the currently `IN_PROGRESS`/`CALLED` entry or `"---"`), `estimatedWaitMinutes` (`waitingCount * expectedDurationMinutes`), `estimatedWait`, and `workloadLevel` (`LOW` for `0..3`, `MEDIUM` for `4..6`, `HIGH` for `>= 7`).
     - Orders candidate doctors by availability/compatibility first, then `waitingCount` ascending (load balancing), then `DoctorCode`.

4. **State Transition, Queue Synchronization & Audit Trail**:
   - Persists `Visit.AssignedDoctorId = doctor.Id`, sets `CheckedInAt` / `CheckedInByStaffId` if not yet populated, and optionally assigns/validates `ExaminationRoomId` if provided in the request.
   - If the visit has both `AssignedDoctorId` and `ExaminationRoomId`, ensures `Visit.Status` is `"WAITING"` (resetting `"CALLED"` to `"WAITING"` if the doctor changed) and synchronizes the `EXAMINATION` `QueueEntry` (`CurrentServicePointId` resolved from `queue_service_points`, preserving `QueueNumber` immutability). If `ExaminationRoomId` is still `null`, preserves `Visit.Status = "CHECKED_IN"` ready for `UC27`.
   - Writes an `AuditLog` entry with `Action = "VISIT_DOCTOR_ASSIGNED"`, `EntityType = "VISIT"`, `EntityId = visit.Id.ToString()`, `HospitalId`, `ActorUserId`, `Reason`, `IpAddress`, and structured JSON `ChangeData`.

## Consequences

- **Duoc**:
  - Completes the `UC24/UC25 -> UC26 -> UC27 -> UC28 -> UC29` visit routing chain without schema migrations or breaking changes to `IApplicationDbContext`.
  - Supplies both rich backend routing metadata and frontend-ready workload cards (`LOW` / `MEDIUM` / `HIGH`, `waitingCount`, `currentTicket`, `estimatedWait`, `VisitPatientSummary` with masked CCCD status).
- **Mat**:
  - Computing live workload across candidate doctors requires querying today's active visits, queue entries, and service points for the hospital (bounded by the number of doctors in a single hospital, fast in PostgreSQL).
- **Rui ro con lai**:
  - Walk-in visits (`UC25`, `#76`) without an `AppointmentId` rely on optional `serviceId`/`serviceCode`/`departmentId` query/body parameters or existing room/queue context to narrow service authorization when `visits` has no dedicated `service_id` column in schema v4.1.
