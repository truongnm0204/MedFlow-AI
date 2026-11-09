# 0023 — View Medical History Contract & Two-Layer Clinical Scope Authorization (`#108` / `M2-054` / UC32)

- Ngay: 2026-04-20
- Trang thai: accepted
- Lane: high-risk

## Context

Issue `#108` (`M2-054 — [M2][BE] View Medical History`, UC32) requires a backend API and service enabling an authenticated `DOCTOR` to view a patient's longitudinal medical history (past visits, clinical examinations/revisions, vital signs, clinical addenda, ICD-10 diagnoses, diagnostic orders/results, and prescriptions) during outpatient consultation. Because medical history exposes sensitive PHI, the feature requires strict two-layer authorization, hospital isolation, PII minimization, and audit logging.

## Decision

1. **Endpoints (`DoctorMedicalHistoryController`, Layer 1 `[Authorize(Roles = "DOCTOR")]`)**:
   - Patient-centric: `GET /api/v1/doctor/patients/{patientIdOrCode}/medical-history` (plus aliases `/api/v1/patients/{patientIdOrCode}/medical-history`, `/api/v1/consultation/patients/{patientIdOrCode}/medical-history`, `/api/v1/doctor/medical-history/patients/{patientIdOrCode}`).
   - Visit-centric: `GET /api/v1/doctor/visits/{visitIdOrCode}/medical-history` (plus aliases `/api/v1/visits/{visitIdOrCode}/medical-history`, `/api/v1/consultation/visits/{visitIdOrCode}/medical-history`, `/api/v1/doctor/consultations/{visitIdOrCode}/medical-history`).
2. **Layer-2 Doctor Identity, RBAC & Clinical Care Scope (`MedicalHistoryService`)**:
   - Verifies active/unlocked `User`, `DOCTOR` role in DB, clinical permission (`MEDICAL_RECORD_WRITE` or `PATIENT_PROFILE_READ`), active `Staff` with `HospitalId`, active `Doctor` linked to the same hospital, and active `Hospital`.
   - Enforces care-relationship ownership:
     - When accessed via a specific consultation visit (`GetByVisitAsync` or `request.VisitIdOrCode`), that visit must belong to the acting doctor's hospital, not be `CANCELLED`/`NO_SHOW` (`409 Conflict`), and be linked to `actorDoctor.Id` (`AssignedDoctorId`, `CompletedByDoctorId`, `Appointment.Slot.DoctorId`, `QueueServicePoint.DoctorId`, or `MedicalRecord.CreatedByDoctorId`); otherwise throws `403 Forbidden`.
     - When accessed by patient (`GetByPatientAsync`), the patient must have at least one non-cancelled visit, appointment slot, queue entry, or medical record in `hospital.Id` linked to `actorDoctor.Id`; otherwise throws `403 Forbidden`.
   - Once care-relationship authorization succeeds, all non-cancelled visits of that patient within `hospital.Id` are returned (with `isAssignedToCurrentDoctor` per visit) so the consulting doctor has complete longitudinal clinical context (allergies, comorbidities, prior diagnostic results, and prescriptions) within the hospital.
3. **PII Minimization & PHI Audit Trail**:
   - `MedicalHistoryPatientSummaryDto` exposes only clinical demographics (`patientId`, `patientCode`, `fullName`, `gender`, `dateOfBirth`, `age`, `bloodType`, `allergiesSummary`, `chronicConditions`) and never leaks raw contact/identity PII (`phone`, `email`, `address`, `identificationHash`).
   - Every successful read writes an `AuditLog` entry with `Action = "MEDICAL_HISTORY_VIEWED"`, `EntityType = "PATIENT"`, `EntityId = patient.Id`, `HospitalId`, `ActorUserId`, `IpAddress`, and structured `ChangeData`.

## Consequences

- **Duoc**: Complete longitudinal clinical timeline for UC32 consultation workbench; strict two-layer doctor & hospital isolation; deterministic unit and integration test coverage; full PHI audit trail.
- **Mat**: Extra queries across clinical/diagnostic/prescription tables per history view (mitigated via batched `IN` queries over scoped `visitIds`).
- **Rui ro con lai**: Cross-hospital medical record federation is out of scope for v4.1 and remains isolated per `hospital_id`.
