# Architecture Decision Records (ADR)

Format: `NNNN-short-slug.md` (NNNN = so thu tu tang dan).

Moi file ghi 1 quyet dinh kien truc/co y nghia lau dai. Khong phai changelog — chi quyet dinh + ly do + trade-off.

## Template

```markdown
# NNNN — Tieu de quyet dinh

- Ngay: yyyy-mm-dd
- Trang thai: proposed | accepted | superseded by NNNN
- Lane: normal | high-risk

## Context
Van de/dieu kien dan den quyet dinh.

## Decision
Quyet dinh gi — cu the.

## Consequences
- Duoc: ...
- Mat: ...
- Rui ro con lai: ...
```

## Quyet dinh can ADR

- Chon/migration DB, schema approach
- Auth mechanism (JWT strategy, OTP design, token storage)
- External provider integration
- Breaking API contract changes
- Thay doi cau truc layer/module

## Hien co (chua formalize)

- JWT refresh token rotation + reuse detection → xem `docs/backend-auth-architecture.md` (nen chuyen thanh ADR-0001 khi co thoi gian)
- SQL migrations thay EF migrations → `database/migrations/`
- `0013-view-examination-queue-contract.md` — View Examination Queue API contract, two-layer authz, service point grouping & UC28 serving order (`#88` / `M2-036`)
- `0014-early-arrival-safe-backfill-contract.md` — Early Arrival / Safe Backfill command & ExaminationQueueOrderingEngine (`#96` / `M2-044`)
- `0015-outpatient-clinical-priority.md` — Outpatient Clinical Priority (`NORMAL`/`ELDERLY`/`PREGNANT`), `RETURN_CONSULTATION` queue class & 3-tier waiting queue ordering (`#102`)
- `0016-late-arrival-missed-turn-contract.md` — Late arrival classification (`LATE` via `grace_minutes`) and dedicated `missed-turn` / `restore-turn` endpoints (`#98` / `M2-046`)
- `0016-cancel-queue-entry-contract.md` — Dedicated Cancel Queue Entry endpoint (`POST /api/v1/queue-entries/{idOrCode}/cancel`), single entry point consolidation & non-reuse of cancelled queue numbers (`#104` / `M2-050`)
- `0017-doctor-assigned-queue-contract.md` — Doctor Assigned Queue (`GET /api/v1/doctor/assigned-queue`), `queueGroup`/`ineligibleReason`, engine tiers (due appointment > gap-safe ELDERLY/PREGNANT/`RETURN_CONSULTATION`), blocked/CLS eligibility; supersedes the waiting-tier order of 0015 (`#92` / UC30)
- `0018-call-next-eligible-patient-contract.md` — Call Next Eligible Patient (`POST /api/v1/queue/service-points/{servicePointId}/call-next`), `walkin_priority_ratio` / `queue_fairness_states` rotation & concurrency serialization (`#94` / UC31)
- `0019-return-to-doctor-queue-entry-contract.md` — Return-to-Doctor Queue Entry (`POST /api/v1/reception/queue-entries/{idOrCode}/return-to-doctor`), `RETURN_CONSULTATION` readiness gating (`DIAGNOSTIC/final_result_required` + `PRESENT`), queue number sequence & fairness integration (`#100` / `M3-002` / UC28)
- `0020-patient-queue-tracking-contract.md` — Track Patient Queue (`GET /api/v1/appointments/{idOrCode}/queue-status`), two-layer patient/family ownership, single-bucket `ExaminationQueueOrderingEngine` evaluation, 3-step clinical progress & `409 Conflict` terminal guards (`#86` / `M2-034` / UC16)
- `0021-update-role-contract.md` — Update Role API (`PUT/PATCH /api/v1/admin/roles/{roleId}`), two-layer `SYSTEM_ADMIN` authz, system-role immutability/self-lockout guards, SoD validation & `ROLE_UPDATED` audit metadata reconstruction (`#238` / `M2-076` / UC58)
- `0022-open-consultation-workspace-contract.md` — Open Consultation Workspace API (`GET /api/v1/doctor/consultation/{idOrCode}` & `POST /api/v1/doctor/consultation/{idOrCode}/open`), two-layer `DOCTOR` + visit/service-point ownership authz, `CALLED -> IN_PROGRESS` state transition, draft `medical_records` bootstrap & clinical workspace aggregate (`#106` / `M2-052` / UC31)
- `0023-view-medical-history-contract.md` — View Medical History API (`GET /api/v1/doctor/patients/{patientIdOrCode}/medical-history`, `GET /api/v1/doctor/visits/{visitIdOrCode}/medical-history`), two-layer `DOCTOR` clinical care-relationship scope, longitudinal clinical/diagnostic/prescription aggregation & `MEDICAL_HISTORY_VIEWED` PHI audit log (`#108` / `M2-054` / UC32)
- `0024-check-in-scheduled-patient-contract.md` — Check-in Scheduled Patient (`POST /api/v1/reception/appointments/{idOrCode}/check-in`) & View Check-in Result (`GET /api/v1/reception/appointments/{idOrCode}/check-in-result`, `GET /api/v1/reception/visits/{idOrCode}/check-in-result`), two-layer reception/hospital authz, `Appointment -> Visit` creation, `RESERVED -> WAITING` queue activation, clinical priority validation, and `PATIENT_CHECKED_IN` audit log reconstruction (`#72` / `M2-020` & `#74` / `M2-022` / UC24)
- `0025-record-clinical-examination-contract.md` — Record Clinical Examination API (`POST|PUT /api/v1/doctor/consultation/{idOrCode}/clinical-examination`), two-layer `DOCTOR` + `MEDICAL_RECORD_WRITE` + visit/service-point ownership authz, clinical/vital-sign validation, `IN_PROGRESS` + `DRAFT` state & concurrency guards, append-only `medical_record_revisions` persistence & `CLINICAL_EXAMINATION_RECORDED` audit log (`#110` / `M2-056` / UC33)
- `0026-view-patient-detail-contract.md` — View Patient Detail at Reception API (`GET /api/v1/reception/patients/{idOrCode}`), two-layer reception/hospital authz, CCCD masking, family-link emergency contact, today appointment status mapping & past visit diagnosis resolution (`#66` / `M2-014` / UC22)
- `0027-create-walk-in-visit-contract.md` — Create Walk-in Visit API (`POST /api/v1/reception/visits/walk-in`, `GET /api/v1/reception/walk-in/catalog`, `GET /api/v1/reception/visits/{visitIdOrCode}/walk-in`), two-layer reception/hospital authz, ADR-0015 clinical priority & same-day duplicate guards, transactional `Visit` + `QueueEntry` (`WALK_IN`) + `Payment` + `WALK_IN_VISIT_CREATED` audit log & downstream `UC24`/`UC27`/`UC29` integration (`#76` / `M2-024` / UC25)
- `0027-assign-doctor-to-visit-contract.md` — Assign Doctor to Visit API (`GET /api/v1/visits/{visitIdOrCode}/available-doctors` & `PUT|POST /api/v1/visits/{visitIdOrCode}/doctor`), two-layer reception/hospital authz, service authorization & department compatibility validation, live doctor workload calculation (`LOW`/`MEDIUM`/`HIGH`), queue service-point sync & `VISIT_DOCTOR_ASSIGNED` audit log (`#78` / `M2-026` / UC26)
- `0028-close-examination-slot-contract.md` — Close, Batch-Close & Reopen Examination Slot API (`POST|PATCH|PUT /api/v1/admin/examination-slots/{idOrCode}/close`, `POST /api/v1/admin/examination-slots/batch-close`, `POST|PATCH|PUT /api/v1/admin/examination-slots/{idOrCode}/reopen`), two-layer `HOSPITAL_ADMIN`/`SYSTEM_ADMIN` + hospital ownership authz, dependent booking warnings & `KEEP_EXISTING`/`RESCHEDULE_REQUIRED` handling, and `EXAMINATION_SLOT_CLOSED`/`EXAMINATION_SLOT_REOPENED` audit metadata reconstruction (`#216` / `M2-066` / `UC53`)
- `0029-record-diagnosis-clinical-conclusion-contract.md` — Record Diagnosis & Clinical Conclusion API (`POST|PUT /api/v1/doctor/consultation/{idOrCode}/diagnosis`, `GET /api/v1/doctor/icd10-codes`), two-layer `DOCTOR` + `MEDICAL_RECORD_WRITE` + visit/service-point ownership authz, ICD-10 catalog/selectability validation (`is_selectable`, `not_primary_dx`), mandatory diagnostic readiness guard (`UC35`), `visit_diagnoses` sync + append-only `medical_record_revisions` & `DIAGNOSIS_RECORDED` audit log (`#112` / `M3-004` / `UC36`)
- `0030-create-diagnostic-order-contract.md` — Create Diagnostic Order API (`GET|POST /api/v1/doctor/consultation/{idOrCode}/diagnostic-orders`), two-layer `DOCTOR` + `DIAGNOSTIC_ORDER` + visit/service authorization checks, transactional `QueueEntry` (`DIAGNOSTIC`) + `DiagnosticOrder` persistence, mandatory-result readiness gating & `DIAGNOSTIC_ORDER_CREATED` audit log (`#120` / `M3-012` / `UC34`)
- `0031-complete-visit-contract.md` — Complete Visit API (`POST|PUT /api/v1/doctor/consultation/{idOrCode}/complete`), two-layer `DOCTOR` + `MEDICAL_RECORD_WRITE` + visit/service-point ownership authz, clinical/diagnostic/diagnosis/prescription completeness guards, transactional `MedicalRecord` (`FINALIZED`) + `VisitDiagnosis` (`CONFIRMED`) + `QueueEntry` (`COMPLETED`) + `Visit` (`COMPLETED`) finalization & `VISIT_COMPLETED` audit log (`#118` / `M3-010` / `UC38`)
- `0032-manage-diagnostic-queue-contract.md` — Manage Diagnostic Queue API (`GET /api/v1/diagnostic/queue`, `GET /api/v1/diagnostic/queue/{idOrCode}`, `POST /api/v1/diagnostic/queue/service-points/{servicePointId}/call-next`, `PUT|PATCH|POST /api/v1/diagnostic/queue/{idOrCode}/status`, `missed-turn`, `restore-turn`, `cancel`), two-layer `DIAGNOSTIC_STAFF` + hospital ownership authz, deterministic `STAT > URGENT > NORMAL` + clinical priority bucket ranking, `DiagnosticOrder.Status` sync, `Visit.Status` preservation & `DIAGNOSTIC_QUEUE_*` audit log (`#130` / `M3-022` / `UC41`)
- `0033-enter-diagnostic-result-contract.md` — Enter Diagnostic Result API (`GET /api/v1/diagnostic/results`, `GET /api/v1/diagnostic/results/{idOrCode}`, `POST|PUT|PATCH /api/v1/diagnostic/orders/{idOrCode}/result`, `POST|PUT /api/v1/diagnostic/results`), two-layer `DIAGNOSTIC_STAFF` + `DIAGNOSTIC_RESULT_WRITE` + hospital ownership authz, UC42 execution prerequisite (`IN_PROGRESS`/`COMPLETED`), transactional `DiagnosticResult` + `DiagnosticResultVersion` + `DiagnosticOrder`/`QueueEntry` completion sync, `Visit.Status` preservation & `DIAGNOSTIC_RESULT_ENTERED`/`DIAGNOSTIC_RESULT_FINALIZED` audit log (`#134` / `M3-026` / `UC43`)
- `0034-update-diagnostic-service-status-contract.md` — Update Diagnostic Service Status API (`GET /api/v1/diagnostic/orders/execution`, `GET /api/v1/diagnostic/orders/{idOrCode}/execution`, `PUT|PATCH|POST /api/v1/diagnostic/orders/{idOrCode}/execution-status`, `/start`, `/complete`, batch update), two-layer `DIAGNOSTIC_STAFF` + hospital ownership authz, `CONFIRMED -> IN_PROGRESS -> COMPLETED` / `CANCELLED` state machine, `QueueEntry` (`DIAGNOSTIC`) sync, `Visit.Status` preservation & `DIAGNOSTIC_SERVICE_EXECUTION_*` audit log (`#132` / `M3-024` / `UC42`)
