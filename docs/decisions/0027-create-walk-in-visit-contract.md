# 0027 — Create Walk-in Visit Contract (`UC25` / Issue `#76` / `M2-024`)

- Ngay: 2026-11-09
- Trang thai: accepted
- Lane: high-risk

## Context

Issue `#76` (`M2-024 — [M2][BE] Create Walk-in Visit`, `UC25`) requires the backend API contract, two-layer hospital-scoped authorization, validation, duplicate-visit conflict protection, transactional `Visit` + `QueueEntry` + `Payment` + `AuditLog` persistence, walk-in form catalog endpoint, and downstream integration (`UC24` check-in result, `UC26` doctor assignment, `UC27` room assignment, `UC29` examination ticket printing) for outpatient patients arriving at reception without a prior appointment.

In schema v4.1 (`database/medflow_schema_v4_1.sql` + `011_outpatient_clinical_priority.sql`):
- `visits.appointment_id` is nullable (`uq_visits_appointment_id` allows multiple `NULL` rows in PostgreSQL).
- `queue_entries.manual_priority` is constrained by `ck_queue_entries_manual_priority` to `NORMAL`, `ELDERLY`, `PREGNANT` (`ADR-0015`).
- `visits` does not have dedicated columns for `department_id`, `service_id`, `symptoms`, or counter vital signs; structured registration metadata is persisted in `audit_logs.change_data` (`action = 'WALK_IN_VISIT_CREATED'`) and initial fee tracking in `payments` (`status = 'PENDING'`), avoiding any SQL schema migration.

## Decision

1. **HTTP Endpoints (`ReceptionWalkInVisitController`)**:
   - Create Walk-in Visit (`201 Created` -> `CreateWalkInVisitResponseDto`):
     - `POST /api/v1/reception/visits/walk-in` (canonical)
     - `POST /api/v1/reception/walk-in`
     - `POST /api/v1/reception/walk-in/visits`
     - `POST /api/v1/visits/walk-in`
     - `POST /api/v1/reception/visits`
     - `POST /api/v1/reception/patients/{patientIdOrCode}/walk-in`
     - `POST /api/v1/reception/patients/{patientIdOrCode}/visits/walk-in`
     - `POST /api/v1/patients/{patientIdOrCode}/visits/walk-in`
   - Get Walk-in Form Catalog (`200 OK` -> `WalkInCatalogResponseDto`):
     - `GET /api/v1/reception/walk-in/catalog` (canonical)
     - `GET /api/v1/reception/visits/walk-in/catalog`
     - `GET /api/v1/reception/walk-in/departments`
   - Get Walk-in Visit Summary (`200 OK` -> `CreateWalkInVisitResponseDto`):
     - `GET /api/v1/reception/visits/{visitIdOrCode}/walk-in` (canonical)
     - `GET /api/v1/visits/{visitIdOrCode}/walk-in`
     - `GET /api/v1/reception/walk-in/visits/{visitIdOrCode}`

2. **Two-Layer Authorization (`401` / `403` / `404`)**:
   - **Layer 1**: `[Authorize(Roles = "RECEPTION,HOSPITAL_ADMIN,SYSTEM_ADMIN")]` on `ReceptionWalkInVisitController`.
   - **Layer 2 (`CreateWalkInVisitService.EnsureWalkInActorAccessAsync`)**:
     - Verifies `actorUserId > 0`, `Users.AccountStatus == ACTIVE`, and `LockedUntil` not active.
     - Verifies database role membership in `{"RECEPTION", "HOSPITAL_ADMIN", "SYSTEM_ADMIN"}` and permission `PATIENT_CHECKIN` / `VISIT_ASSIGN` for `RECEPTION` (or `PATIENT_CHECKIN` / `VISIT_ASSIGN` / `QUEUE_MANAGE` / `HOSPITAL_CONFIG` for `HOSPITAL_ADMIN`).
     - Verifies active `Staff` and active `Hospital` for non-`SYSTEM_ADMIN` actors, enforcing hospital ownership (`403 Forbidden`) across selected `Department`, `Service`, `Doctor`, and `HospitalRoom`.

3. **Validation & Conflict Guards (`400` / `409`)**:
   - Validates patient identifier (`patientId` or `patientCode`), department (`departmentId` or `departmentCode`), and `EXAMINATION` service (`serviceId` or `serviceCode`), ensuring department-service membership match.
   - Validates `reasonForVisit` (`3..500` chars), `symptoms` (`<= 20` items, case-insensitively deduplicated), optional vitals (`bloodPressure`, `heartRate`, `temperature`), `isIdentityVerified != false`, and `paymentMethod` (`VIETQR` or `CASHIER`).
   - Maps frontend `clinicalPriority = "STANDARD"` to `"NORMAL"`, enforces ADR-0015 (`NORMAL`, `ELDERLY`, `PREGNANT`; rejects `URGENT`/`EMERGENCY`), validates age/gender eligibility (`ELDERLY >= 75`, `PREGNANT != MALE`), and checks `QueuePolicyReader.IsManualPriorityVerificationRequiredAsync`.
   - Returns `409 Conflict` when:
     - `Patient`, `Department`, `Service`, `Doctor`, or `HospitalRoom` is inactive, or `HospitalRoom` is at capacity;
     - The patient already has an active same-day `Visit`;
     - The patient already has an active (`BOOKED`/`CONFIRMED`/`CHECKED_IN`) same-day scheduled `Appointment` in the same hospital (preventing duplicate walk-in creation when `UC24` scheduled check-in applies).

4. **Transactional Persistence & Downstream Interoperability**:
   - Within `BeginTransactionAsync`:
     - Inserts `Visit` (`AppointmentId = null`, `VisitCode = VIS-{yyyyMM}-{id:D4}`, `Status = "WAITING"` when both doctor and room are assigned, else `"CHECKED_IN"`).
     - Inserts `QueueEntry` (`QueueType = "EXAMINATION"`, `Status = "WAITING"`, `PresenceStatus = "PRESENT"`, `ArrivalClass = "WALK_IN"`, `TicketNo = A{queueNumber:D3}`).
     - Inserts `Payment` (`Status = "PENDING"`, amount resolved from active `ServicePrices`) and `AuditLog` (`Action = "WALK_IN_VISIT_CREATED"`, `EntityType = "VISIT"`).
   - `CheckInScheduledPatientService`, `VisitRoomAssignmentService` (`UC27`), and `VisitTicketService` (`UC29`) read `WALK_IN_VISIT_CREATED` audit metadata so walk-in visits preserve hospital isolation, department/room compatibility, and selected service details end-to-end.

## Consequences

- **Duoc**: Complete outpatient walk-in registration workflow (`UC25`) compatible with both the frontend walk-in page (`#75`) and downstream routing, queue, and ticket printing modules (`UC26`–`UC29`) without schema changes.
- **Mat**: Walk-in department/service selection, symptoms, and counter vitals prior to doctor consultation are reconstructed from `audit_logs.change_data` (`WALK_IN_VISIT_CREATED`).
- **Rui ro con lai**: Concurrent walk-in requests for the same patient rely on application-level same-day active visit checks within the reception workflow.
