# 0061 — Stage E Clinical Revalidation, Overdue Return Snapshot & Queue Coordination Roadmap

- Ngay: 2026-11-26
- Trang thai: accepted (Part 1 — Stage E1 implemented; Part 2 — Stage E2 forward roadmap)
- Lane: high-risk
- Supersedes / refines: Overdue clinical override clauses of `0060-grouped-payment-allocations-selective-cls-and-cross-day-foundations.md`

## Context

Post-Stage D clinical, payment, and concurrency verification (`Issue #856`) identified six high-priority hardening requirements before introducing Stage E2's active queue enrollment (`Queue When Ready`) and centralized return coordination:

1. **Stale/Closed QR Idempotency Replay (`BUG-01` / `PE-01`)**: Replaying an expired `PENDING` QR `idempotencyKey` rolled back the `EXPIRED` state transition when throwing `409`, replaying a `CANCELLED`/`EXPIRED`/`MISMATCH` key fell through to mint a brand-new `Payment`, and `PAID` / non-`PENDING` DTOs still built scannable EMVCo QR payloads.
2. **Post-Lock Actor & Doctor Assignment TOCTOU (`BUG-02`)**: Actor roles, permissions, hospital scope, family ownership, and `Visit.AssignedDoctorId` were checked pre-lock; if a doctor transfer or permission revocation committed while waiting on `TryLockVisitForUpdateAsync`, the stale actor continued mutating selection, declining orders, or issuing QR/cash payments.
3. **Incompatible Diagnostic `ServicePoint` Fallback (`BUG-03`)**: Cross-day diagnostic resume fell back via `.FirstOrDefault()` to any active `DIAGNOSTIC` service point (e.g., routing a Laboratory blood test to Ultrasound when the Laboratory room closed) and did not check `HospitalRoom.Status == 'ACTIVE'` or order validity across all CLS entry points.
4. **Overdue Clinical Override Separation of Duties (`BUG-04` / `QE-24` / `RC-20`)**: Reception could previously pass `ConfirmOverdueOverride = true` to override expired `DiagnosticOrder` validity or overdue `RETURN_CONSULTATION` clinical validity. Extending diagnostic order clinical validity or confirming the clinical validity of overdue diagnostic results is strictly a physician medical decision, whereas Reception owns operational queue coordination.
5. **Legacy Visit Settlement Summary (`BUG-05` / `PE-07`)**: `VisitCrossDayLifecycleService.BuildChargesSummaryAsync` treated any legacy visit-level `PAID` payment (`VisitChargeId IS NULL`, `0` allocations) as settling newly added `DIAGNOSTIC` `VisitCharge` rows and coalesced `null` prices to `0m`.
6. **Distinct Duplicate Transfer on Already-`PAID` QR (`PE-02`)**: While identical `externalId` webhook replays return `IGNORED`, a second distinct bank transfer (`new externalId`) arriving on an already-`PAID` `Payment` previously returned `ALREADY_PAID` without recording `DUPLICATE_RECONCILE`, `LATE_DUPLICATE_TRANSFER_DETECTED`, or surfacing `HasReconciliationItems = true`.

## Decision

### Part 1 — Stage E1: Post-Stage D Hardening & Clinical Revalidation (`Migration 026`)

1. **Additive Schema (`026_clinical_revalidation_overdue_approval_and_review_purpose.sql`)**:
   - `diagnostic_orders`: `validity_extended_until`, `revalidated_by_doctor_id` (`FK -> doctors.id`), `revalidated_at`, `revalidation_note`.
   - `visits`: `overdue_return_approved_by_doctor_id` (`FK -> doctors.id`), `overdue_return_approved_at`, `overdue_return_approval_note`, `overdue_return_result_snapshot` (`varchar(128)` canonical SHA-256 hex).
   - `queue_entries`: `coordination_purpose varchar(24) NULL CHECK (coordination_purpose IS NULL OR coordination_purpose IN ('RESULT_CONCLUSION', 'CLINICAL_REVIEW'))`.
2. **Lock Hierarchy Invariant (Preserving `ADR-0059` / `ADR-0060`)**:
   - Stage E1 introduces zero new lock levels and strictly obeys the canonical `01..16b` acquisition order (`09 visits -> 10 queue_service_points -> 11 queue_entries -> 12 diagnostic_orders -> 13 prescriptions -> 15 visit_charges -> 16 payments -> 16b payment_allocations`).
   - All payment/selection mutators (`DiagnosticOrderSelectionService`, `GenerateQrPaymentService`, `CollectCashPaymentService`) execute `VisitPaymentAccess.RevalidateActorPostLockAsync` immediately after acquiring `09 visits` + `ChangeTracker.Clear()` + reloading `lockedVisit`.
3. **Strict QR Lifecycle & Duplicate Transfer Audit (`BUG-01`, `PE-02`, `BUG-05`)**:
   - Expired `PENDING` QR replays persist `Status = 'EXPIRED'` on `Payment` and `PENDING` `PaymentAllocations`, commit the transaction **first**, and then return `409 [QR_ATTEMPT_EXPIRED]`.
   - Closed (`CANCELLED`, `EXPIRED`, `MISMATCH`) `idempotencyKey` replays always reject with `409 [QR_ATTEMPT_CLOSED]` without minting a replacement row.
   - `PAID` replays return `200 OK` (`IssueKind = 'ALREADY_PAID'`) with `QrPayload = ""`; `ToDto` emits EMVCo payloads strictly when `Status == 'PENDING'` and unexpired.
   - Distinct `externalId` SePay transfers hitting an already-`PAID` `Payment` preserve `Payment.Status = 'PAID'` and `PaymentAllocation.Status = 'PAID'`, record `PaymentGatewayEvent.Outcome = 'DUPLICATE_RECONCILE'` + `AuditLog(LATE_DUPLICATE_TRANSFER_DETECTED)`, and surface `HasReconciliationItems = true` via `VisitChargeAssessor`.
   - `VisitCrossDayLifecycleService` delegates charge totals exclusively to `VisitChargeAssessor.AssessAsync`, keeping `LEGACY_VISIT` credit bounded to pre-`023` `EXAMINATION` charges and preserving `null` `AmountDue` on unpriced payable lines.
4. **Compatible CLS Routing, Universal Order Validity & Clinical Revalidation (`BUG-03`, `BUG-04`, `E1-01..03`)**:
   - `DiagnosticServicePointResolver` enforces two fail-closed predicates (`IsOperational` and `IsCompatibleForOrder` with zero `null` wildcards): `sp.IsActive == true`, `PointType == 'DIAGNOSTIC'`, `sp.HospitalId == hospitalId`, linked `HospitalRoom` (`RoomId IS NOT NULL`, `RoomType == 'DIAGNOSTIC'`, `Status == 'ACTIVE'`, `HospitalId == hospitalId`), and matching active `Department` (`room.DepartmentId == service.DepartmentId` with `dept.IsActive == true && dept.HospitalId == hospitalId`). Explicit `ServicePointId` / `RoomId` requests never fall back silently (`409 [INCOMPATIBLE_DIAGNOSTIC_SERVICE_POINT]`), missing active compatible points fail with `409 [NO_ACTIVE_COMPATIBLE_DIAGNOSTIC_POINT]`, and all CLS mutation callers re-verify post-lock under canonical `04 services -> 06 hospital_rooms -> 09 visits -> 10 queue_service_points -> 11 queue_entries -> 12 diagnostic_orders` order without trapping already-`IN_PROGRESS` specimens (`DR-16`).
   - `CrossDayVisitPolicyReader.IsOrderExpiredForExecution` evaluates `Max(OrderedAt + validityDays, ValidityExtendedUntil)` and blocks expired un-revalidated orders (`409 [DIAGNOSTIC_ORDER_EXPIRED_DOCTOR_APPROVAL_REQUIRED]`) across **all** CLS execution entry points (`ResumeVisitAsync`, `ConfirmDiagnosticOrderAsync`, `ReceiveDiagnosticPatient*`, `DiagnosticQueueService` call-next/manual start, and `DiagnosticServiceExecutionService`).
   - Reception cannot override expired diagnostic orders (`ConfirmOverdueOverride` bypass removed). Instead, Reception coordinates the patient on the **same `Visit`** (`0` extra `EXAMINATION` fee) via `Target = 'RETURN_CONSULTATION', CoordinationPurpose = 'CLINICAL_REVIEW'`.
   - Extending order validity (`POST /api/v1/doctor/consultation/{visit}/diagnostic-orders/{order}/revalidate`, writing `DIAGNOSTIC_ORDER_REVALIDATED`) and closing a `CLINICAL_REVIEW` session (`POST /api/v1/doctor/consultation/{visit}/complete-clinical-review`, writing `CLINICAL_REVIEW_COMPLETED`) are strictly separate commands (`E1-02`): `revalidate` requires an `IN_PROGRESS` clinical session (`409 [CLINICAL_SESSION_NOT_IN_PROGRESS]`), only extends `ORDERED`/`CONFIRMED` non-`DECLINED`/`SKIPPED_AFTER_PAID` orders without auto-flipping `REVIEW_REQUIRED` or closing the queue session, and `complete-clinical-review` verifies `ExpectedStateVersion`, `CoordinationPurpose == 'CLINICAL_REVIEW'`, and zero remaining expired/`REVIEW_REQUIRED` orders (`409 [UNRESOLVED_CLINICAL_REVIEW_ITEMS]`) before closing the ticket and transitioning `Visit.Status` to `AWAITING_RESULTS` or `RETURN_CONSULTATION` without minting a new queue ticket.
   - Overdue `RETURN_CONSULTATION` (`RESULT_CONCLUSION`) may be coordinated by Reception so the patient sees the physician, while `ConsultationWorkspaceService` blocks `CONFIRMED` diagnosis, `ISSUED` prescription, and `CompleteVisit` (`409 [OVERDUE_RETURN_DOCTOR_APPROVAL_REQUIRED]`) until the currently assigned `DOCTOR` submits a matching 64-hex `ExpectedResultSnapshot` (`v2` digest of order states, result IDs, version IDs/numbers, verification state, and result text/conclusion/file content) via `POST /api/v1/doctor/consultation/{visit}/approve-overdue-results` (or inline with matching `ExpectedResultSnapshot`, failing closed with `409 [OVERDUE_RESULT_SNAPSHOT_MISMATCH]` on concurrent edits and preserving `ReadyForReturnAt` on already-overdue visits when additional orders are added — `SN-14`).

### Part 2 — Forward Roadmap Reserved for Stage E2 (Out of Scope for Stage E1)

Stage E2 will build atop Stage E1 in a dedicated follow-up migration and PR:
- Explicit `Queue When Ready` CLS enrollment (`QUEUE_ENROLL`), single-hospital-active-ticket invariant across `PatientId`, and `payment_enrollment_intents` for atomic pay-and-enroll-one-CLS.
- Decoupling `CreateDiagnosticOrderCoreAsync`, `UC42`, and `UC43` from automatic next-ticket creation.
- Centralized Reception-only `RETURN_CONSULTATION` enrollment with doctor assignment/load-balancing and Pharmacy `Queue When Ready` dispensing tickets on `COMPLETED` visits.

## Consequences

- **Duoc**: Closes all 6 post-Stage D financial, concurrency, routing, and clinical separation-of-duties gaps with deterministic PostgreSQL lock guarantees and immutable audit trails before Stage E2's workflow expansion.
- **Mat**: Expired diagnostic orders and overdue return consultations require explicit physician clinical action (`revalidate` / `approve-overdue-results`) rather than a single-step Reception checkbox.
- **Rui ro con lai**: Automatic `QueueEntry(DIAGNOSTIC)` creation at `UC34` remains baseline behavior in Stage E1 until Stage E2 introduces `QUEUE_ENROLL` and `payment_enrollment_intents`.
