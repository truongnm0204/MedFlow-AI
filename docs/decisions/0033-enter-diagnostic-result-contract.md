# 0033 — Enter Diagnostic Result API Contract & Versioning (`#134` / `M3-026` / `UC43`)

- Ngay: 2026-04-19
- Trang thai: accepted
- Lane: high-risk

## Context

Issue `#134` (`M3-026`, `UC43 — Update Diagnostic Result`, prerequisite `UC42 — Update Service Execution Status`) requires the backend API enabling Diagnostic Services Staff (`DIAGNOSTIC_STAFF` with `DIAGNOSTIC_RESULT_WRITE`) to inspect diagnostic orders ready for result entry and enter diagnostic results (`ResultText`) and conclusions (`Conclusion`) — both unfinalized `DRAFT`/`PRELIMINARY`/`PENDING_VERIFY` versions and finalized/verified `FINALIZED`/`VERIFIED` results — for executed diagnostic orders without overwriting clinical visit status (`visits.status`).

## Decision

1. **Endpoints (`DiagnosticResultsController`)**:
   - `GET /api/v1/diagnostic/results` (aliases: `/api/v1/diagnostic/orders/results`, `/api/v1/diagnostic/orders/result`, `/api/v1/diagnostic/orders/enter-result`, `/api/v1/diagnostic-results`, `/api/v1/diagnostic-orders/results`, `/api/v1/diagnostic-orders/enter-result`, `/api/v1/diagnostic/workbench/results`) — lists diagnostic orders scoped to the staff member's hospital with summary stats (`DiagnosticResultEntrySummaryStatsDto`), available service points, and result-entry eligibility (`DiagnosticResultEntryEligibilityDto`).
   - `GET /api/v1/diagnostic/results/{idOrCode}` (aliases: `/api/v1/diagnostic/orders/{idOrCode}/result`, `/api/v1/diagnostic/orders/{idOrCode}/results`, `/api/v1/diagnostic/orders/{idOrCode}/enter-result`, `/api/v1/diagnostic-results/{idOrCode}`, `/api/v1/diagnostic-orders/{idOrCode}/result`, `/api/v1/diagnostic-orders/{idOrCode}/results`, `/api/v1/diagnostic-orders/{idOrCode}/enter-result`, `/api/v1/diagnostic/workbench/orders/{idOrCode}/result`) — returns full order detail, clinical context (`DiagnosticOrderClinicalSummaryDto`), version list (`DiagnosticResultVersionItemDto`), and audit status history (`DiagnosticResultHistoryItemDto`).
   - `POST|PUT|PATCH /api/v1/diagnostic/orders/{idOrCode}/result` (plus route aliases `/results`, `/enter-result`, `/api/v1/diagnostic/results/{idOrCode}`, `/api/v1/diagnostic-orders/{idOrCode}/result`, `/api/v1/diagnostic-results/{idOrCode}`, `/api/v1/diagnostic/workbench/orders/{idOrCode}/result` and body-identifier routes `POST|PUT /api/v1/diagnostic/results`, `/api/v1/diagnostic/orders/result`, `/api/v1/diagnostic-results`) — enters a diagnostic result version and optionally finalizes the result.
   - Route regex exclusions in `DiagnosticWorklistController` and `DiagnosticOrdersController` exclude `result$|results$|enter-result$` so static segments never collide with `{orderIdOrCode}` / `{idOrCode}`.

2. **Two-Layer Authorization & Hospital Scope**:
   - Layer 1: `[Authorize(Roles = "DIAGNOSTIC_STAFF")]`.
   - Layer 2 (`DiagnosticResultEntryService.EnsureDiagnosticStaffAccessAsync` & `EnsureOrderHospitalOwnership`): verifies active/unlocked `User`, `DIAGNOSTIC_STAFF` role, active `Staff` profile assigned to an active `Hospital` and active `Department`, read permission (`DIAGNOSTIC_RESULT_WRITE | QUEUE_MANAGE | DIAGNOSTIC_ORDER`) for GET endpoints, `DIAGNOSTIC_RESULT_WRITE` permission for write endpoints, and hospital ownership across `Service`, `Department`, `Doctor`, `QueueServicePoint`, `HospitalRoom`, and `Visit`.

3. **UC42 Execution Prerequisite & State Guards (`409 Conflict`)**:
   - Entering a result requires the diagnostic service execution (`UC42`) to have started or completed (`DiagnosticOrder.Status` in `IN_PROGRESS | COMPLETED` or `QueueEntry.Status` in `IN_PROGRESS | COMPLETED` or `QueueEntry.StartedAt`/`CompletedAt` set or an unfinalized `DiagnosticResult` already present). Orders still in `ORDERED` or `CONFIRMED` without execution started are rejected with `409 Conflict`.
   - Once a `DiagnosticResult` row is finalized (`FinalizedAt` set, `Status` in `FINALIZED | VERIFIED | COMPLETED`, or latest `DiagnosticResultVersion.IsFinalized == true`), subsequent `EnterDiagnosticResultAsync` calls are rejected with `409 Conflict` (post-finalization amendments belong to `#136`).
   - Cancelled orders/queues, `COMPLETED`/`CANCELLED`/`NO_SHOW` visits, `FINALIZED` medical records, inactive patient/service/department/service-point/room, and `ExpectedStateVersion` mismatches are rejected with `409 Conflict`.

4. **Transactional Persistence & Visit Status Preservation**:
   - Persists `DiagnosticResult` and appends `DiagnosticResultVersion` (`VersionNo = max + 1`, `ResultText`, `Conclusion`, `ReportFileUrl`, `EnteredByStaffId`, `EnteredAt`, `IsFinalized`, `VerifiedByStaffId`, `VerifiedAt`, `CorrectionReason`).
   - When `IsFinalized == true` (`FINALIZED` or `VERIFIED`), sets `DiagnosticResult.FinalizedAt` and `FinalizedByStaffId`, transitions `DiagnosticOrder.Status = "COMPLETED"` and `QueueEntry.Status = "COMPLETED"` (`CompletedAt` populated, `StateVersion + 1`), and writes `AuditLog` (`Action = "DIAGNOSTIC_RESULT_FINALIZED"`, `EntityType = "DIAGNOSTIC_RESULT"`).
   - When `IsFinalized == false` (`DRAFT`, `PRELIMINARY`, `PENDING_VERIFY`), keeps `DiagnosticOrder.Status = "IN_PROGRESS"` (unless already `COMPLETED`), increments `QueueEntry.StateVersion`, and writes `AuditLog` (`Action = "DIAGNOSTIC_RESULT_ENTERED"`, `EntityType = "DIAGNOSTIC_RESULT"`).
   - Never overwrites `Visit.Status` (`AWAITING_RESULTS` / `IN_PROGRESS`), preserving clinical consultation workflow integrity for `UC35`/`UC36`.

## Consequences

- **Duoc**: Complete end-to-end backend support for `UC43` (`#134`) compatible with `DiagnosticWorklistService` (`UC39`) and `ConsultationWorkspaceService` (`UC35`/`UC36`, `AllMandatoryResultsReady`).
- **Mat**: Additional route aliases to support both order-centric and result-centric REST paths.
- **Rui ro con lai**: Post-finalization result amendments (`#136`) must explicitly build on `DiagnosticResultVersion` (`VersionNo + 1`) with mandatory `CorrectionReason` rather than calling the initial result entry guard.
