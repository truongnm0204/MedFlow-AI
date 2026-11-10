# 0026 — View Patient Detail at Reception Contract (`#66` / `M2-014` / `UC22`)

- Ngay: 2026-04-20
- Trang thai: accepted
- Lane: high-risk

## Context

Issue `#66` (`M2-014 — [M2][BE] View Patient Detail`, `UC22 Search Patient`) requires a backend read endpoint for reception counter staff to inspect a patient's profile, masked identity verification state, emergency contact, today's appointment status, and past completed visit history before check-in or walk-in registration.

While reception staff already have search (`GET /api/v1/reception/patients/search`), create (`POST /api/v1/reception/patients`), and update (`PUT /api/v1/reception/patients/{idOrCode}`) endpoints, viewing the full patient detail modal (`PatientDetailModal`) requires:
1. Two-layer authorization for reception/hospital staff (`RECEPTION`, `HOSPITAL_ADMIN`, `SYSTEM_ADMIN`) with hospital-scoped visibility on appointments and past visits.
2. Strict PII protection so plaintext CCCD is never exposed (`patients.identification_hash` is a one-way SHA-256 digest).
3. Schema v4.1 compatibility without adding redundant columns to `patients` (`EmergencyContact` derived from active `patient_family_links`, `BhytNumber` / `HealthInsuranceNumber` returned as `null`).
4. Alignment between backend reception DTO conventions and the frontend `PatientDetail` modal contract (`medflow-ai-frontend/src/features/reception/types/patient-detail.types.ts`).

## Decision

1. **Routes & HTTP Contract**:
   - Canonical route: `GET /api/v1/reception/patients/{idOrCode}`
   - Alias routes: `GET /api/v1/reception/patients/{idOrCode}/detail` and `GET /api/v1/patients/{idOrCode}/reception-detail`
   - Identifier `idOrCode` accepts either a positive numeric `patients.id` or a case-insensitive `patients.patient_code` (with optional leading `#`). Invalid identifiers (`<= 0`, empty, `> 40` chars, invalid characters) fail fast with `400 Bad Request` (`ValidationException`).
   - Non-existent patients return `404 Not Found` (`NotFoundException`); patients with `status != 'ACTIVE'` return `409 Conflict` (`ConflictException`).

2. **Two-Layer Authorization**:
   - **Layer 1 (Controller)**: `[Authorize(Roles = "RECEPTION,HOSPITAL_ADMIN,SYSTEM_ADMIN")]` on `ReceptionPatientDetailController`.
   - **Layer 2 (Service)**: `ReceptionPatientDetailService` verifies against the database that:
     - The actor user exists, has `account_status == ACTIVE`, and is not locked (`locked_until > nowUtc`).
     - The actor holds `RECEPTION`, `HOSPITAL_ADMIN`, or `SYSTEM_ADMIN` in `user_roles`/`roles`.
     - When `role_permissions` are configured for non-`SYSTEM_ADMIN` actors: `RECEPTION` requires `PATIENT_PROFILE_READ` or `PATIENT_CHECKIN`; `HOSPITAL_ADMIN` requires at least one of `PATIENT_PROFILE_READ`, `PATIENT_CHECKIN`, `VISIT_ASSIGN`, `QUEUE_MANAGE`, or `HOSPITAL_CONFIG`.
     - Non-`SYSTEM_ADMIN` actors have an `ACTIVE` `staff` record bound to an active `hospital` (`is_active == true`).

3. **PII Masking & Demographics**:
   - When `patients.identification_hash` is populated, `isIdentityVerified = true` and both `cccd` and `identityCardMasked` return `"****** (Đã xác thực CCCD)"`. When null/blank, `isIdentityVerified = false`, `identityCardMasked = null`, and `cccd = "Chưa cập nhật"`.
   - `gender` is normalized to `"MALE" | "FEMALE" | "OTHER"` (defaulting to `"OTHER"`) with Vietnamese `genderLabel` (`"Nam" | "Nữ" | "Khác"`).
   - `age` is calculated in Vietnam time (`Asia/Ho_Chi_Minh`, UTC+7) via `AppointmentSlotService.CalculateAge`.

4. **Emergency Contact, Today's Appointment & Past Visits**:
   - `EmergencyContact`: resolved from active, non-revoked, non-expired `patient_family_links` rows (`VERIFIED` prioritized first, then most recent `verified_at`), mapping relationship + counterpart gender to Vietnamese labels (`"Vợ"`, `"Chồng"`, `"Con trai"`, `"Con gái"`, `"Bố ruột"`, `"Mẹ ruột"`, `"Người thân"`).
   - `TodayAppointment`: selects today's primary non-cancelled, non-no-show appointment in Vietnam time (scoped to the actor's hospital for non-`SYSTEM_ADMIN`), prioritizing `CHECKED_IN` then earliest `slot.starts_at`, and maps live appointment/visit/queue state to `"BOOKED" | "ARRIVED" | "IN_PROGRESS" | "DONE"`.
   - `PastVisits`: lists completed visits (`status = 'COMPLETED'`) scoped to the actor's hospital (or all hospitals for `SYSTEM_ADMIN`), ordered by `completed_at ?? checked_in_at` descending, resolving primary diagnosis from `visit_diagnoses` (`clinical_diagnosis ?? name_snapshot ?? code_snapshot`) with fallback to latest `medical_record_revisions.clinical_conclusion` and `visits.reason_for_visit`.

## Consequences

- **Duoc**:
  - Reception counter staff can verify patient identity, emergency contact, today's appointment status, and past visit history in a single read call without schema changes.
  - Enforces strict two-layer RBAC + hospital scoping and prevents plaintext CCCD leakage.
- **Mat**:
  - Aggregating family links, today's appointment/visit/queue state, and past visit diagnoses requires multiple read queries per detail view (acceptable for single-patient modal open at reception).
- **Rui ro con lai**:
  - When schema v4.1 adds a dedicated health insurance column in a future milestone, `BhytNumber` / `HealthInsuranceNumber` can be mapped from that column without breaking the response contract.
