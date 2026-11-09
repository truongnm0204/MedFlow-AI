# 0024 — Check-in Scheduled Patient Contract (`UC24` / Issue `#72` / `M2-020`)

- Ngay: 2026-04-19
- Trang thai: accepted
- Lane: high-risk

## Context

Issue `#72` (`M2-020`, `UC24 — Check-in Patient with Appointment`) requires the backend API contract, two-layer authorization, transactional persistence, clinical queue activation, and audit logging for reception staff checking in a scheduled patient at the counter.

Prior to this change:
- `medflow-ai-backend` had no endpoint or service that created a `Visit` row from a scheduled `Appointment` (`BOOKED` / `CONFIRMED`) or transitioned a pre-reserved `RESERVED` `QueueEntry` into an active `WAITING` + `PRESENT` examination queue entry.
- The frontend reception check-in module (`medflow-ai-frontend/src/features/reception`) already defined the client payload (`reasonForVisit`, `bloodPressure`, `heartRate`, `temperature`, `isIdentityVerified`, `isInsuranceVerified`, `priority`, `notes`) and expected response fields (`CheckInSuccessResult` in `checkin.types.ts`).
- `medflow_schema_v4_1.sql` enforces `uq_visits_appointment_id` (at most one `Visit` per `Appointment`) and `ck_queue_entries_manual_priority` (`NORMAL`, `ELDERLY`, `PREGNANT` only), and does not store reception counter vital signs on the `visits` table (`visit_vitals` was removed in schema v4.1; `medical_record_revisions.vital_signs` belongs to doctor-authored clinical revisions).

## Decision

1. **HTTP Endpoints (`ReceptionCheckInController`)**:
   - Route-parameter check-in endpoints:
     - `POST /api/v1/reception/appointments/{idOrCode}/check-in` (canonical)
     - `POST /api/v1/reception/appointments/{idOrCode}/checkin`
     - `POST /api/v1/appointments/{idOrCode}/check-in`
     - `POST /api/v1/reception/check-in/appointments/{idOrCode}`
   - Body-identifier check-in aliases (`appointmentId` or `appointmentCode` in JSON body):
     - `POST /api/v1/reception/check-in`
     - `POST /api/v1/reception/visits/check-in`
     - `POST /api/v1/visits/check-in`

2. **Two-Layer Authorization (`401` / `403` / `404`)**:
   - **Layer 1**: `[Authorize(Roles = "RECEPTION,HOSPITAL_ADMIN,SYSTEM_ADMIN")]` on `ReceptionCheckInController`.
   - **Layer 2 (`CheckInScheduledPatientService.EnsureCheckInActorAccessAsync`)**:
     - Verify `actorUserId > 0`, `Users.AccountStatus == ACTIVE`, and `LockedUntil` not active.
     - Verify database role membership in `{"RECEPTION", "HOSPITAL_ADMIN", "SYSTEM_ADMIN"}` and role permission `PATIENT_CHECKIN` for `RECEPTION` (or `PATIENT_CHECKIN` / `VISIT_ASSIGN` / `QUEUE_MANAGE` for `HOSPITAL_ADMIN`).
     - Verify active `Staff` profile and active `Hospital` assignment for non-`SYSTEM_ADMIN` actors, and enforce hospital ownership (`appointmentHospitalId == actorHospitalId`).

3. **Validation & Business Preconditions (`400` / `409`)**:
   - Require `isIdentityVerified == true` (`IsIdentityVerified ?? IdentityVerified == true`); otherwise return `400 Bad Request`.
   - Validate optional counter vitals (`BloodPressure` matching `^\d{2,3}/\d{2,3}$`, `HeartRate` in `40..220`, `Temperature` in `34.0..43.0`) and `ReasonForVisit` (`3..300` chars, falling back to `appointment.ReasonForVisit` or `appointment.ReceptionNote` when omitted).
   - Validate outpatient priority against ADR-0015 (`NORMAL`, `ELDERLY`, `PREGNANT` via `ClinicalQueueRules.NormalizePriorityOrThrow` and `EnsurePatientEligible`) plus `QueuePolicyReader.IsManualPriorityVerificationRequiredAsync`. Submitting `"URGENT"` returns `400 Bad Request`.
   - Return `409 Conflict` when:
     - `Patient.Status != ACTIVE`;
     - `Appointment.Status` is `CANCELLED`, `NO_SHOW`, `COMPLETED`, `CHECKED_IN`, or a `Visit` already exists for `AppointmentId` (`uq_visits_appointment_id`);
     - `Slot.StartsAt` date is not today in Vietnam time (`UTC+7`);
     - Assigned `Doctor`, `HospitalRoom`, or `Department` is inactive/unavailable;
     - The patient already has another active (non-`COMPLETED`, non-`CANCELLED`) visit today.

4. **Transactional State Transition, Queue Activation & Audit Logging**:
   - Within a database transaction (`BeginTransactionAsync`):
     - Transition `Appointment.Status` to `CHECKED_IN` and persist `Visit` (`Status = "WAITING"` when both doctor and room are assigned, else `"CHECKED_IN"`, `VisitCode = VIS-{yyyyMM}-{id:D4}`).
     - If a non-`CANCELLED` `EXAMINATION` `QueueEntry` already exists for the appointment (`RESERVED`), activate it in-place to `Status = "WAITING"`, `PresenceStatus = "PRESENT"`, `ArrivedAt = nowLocal`, `ReadyAt = nowLocal`, preserving its immutable `QueueNumber`. Otherwise allocate `MAX(queue_number) + 1` on `(servicePointId, queueDate)`.
     - Classify `ArrivalClass` (`EARLY`, `ON_TIME_OR_GRACE`, `LATE`) using `QueueArrivalClassifier.Classify` and hospital/service-point `grace_minutes` policy (`QueueOrderingPolicyLoader`).
     - Write an `AuditLog` (`Action = "PATIENT_CHECKED_IN"`, `EntityType = "VISIT"`) capturing the state transition, queue assignment, verification flags, and optional counter vitals in `ChangeData` JSONB without requiring a schema migration.

## Consequences

- **Duoc**: Complete end-to-end scheduled check-in workflow (`UC24`) compatible with both existing frontend reception services and downstream queue/consultation modules (`UC26`–`UC31`), while preserving `queue_number` immutability and `uq_visits_appointment_id` idempotency.
- **Mat**: Counter vitals captured at reception check-in live in `audit_logs.change_data` and the check-in response payload rather than dedicated columns on `visits` (until doctor consultation creates a clinical revision).
- **Rui ro con lai**: Concurrent check-in requests for the same appointment rely on `uq_visits_appointment_id` (`DbUpdateException` mapped to `409 Conflict`).
