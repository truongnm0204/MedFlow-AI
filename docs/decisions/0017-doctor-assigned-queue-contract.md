# ADR 0017: Doctor Assigned Queue Contract (M2 / #92 / UC30)

- **Status**: Accepted
- **Date**: 2026-11-07
- **Issue**: `#92` (`[M2][BE] View Doctor Assigned Queue`)
- **Lane**: high-risk (DOCTOR authorization + new endpoint)
- **Supersedes**: waiting-tier order of ADR 0015 (elevated priority no longer unconditionally first) for `ExaminationQueueOrderingEngine`

## Context

Doctors need to see the queue of the service points / examination rooms they own, in the exact serving order produced by `ExaminationQueueOrderingEngine`, with a clear distinction between `IN_PROGRESS`, `CALLED`, call-eligible and not-yet-eligible entries (not arrived, blocked by `blockedReason`, waiting for mandatory diagnostic results).

`docs/MedFlow_Quy_Tac_Dieu_Phoi_Hang_Doi.md` also requires the engine to put due protected appointments above ELDERLY/PREGNANT and to let ELDERLY/PREGNANT and `RETURN_CONSULTATION` jump ahead only when the next protected appointment still has a safe gap.

## Decision

1. **Endpoint**: `GET /api/v1/doctor/assigned-queue?queueDate=yyyy-MM-dd` (single route, no alias). `queueDate` optional; invalid format → `400`.
2. **Authorization**:
   - Layer 1: `[Authorize(Roles = "DOCTOR")]`.
   - Layer 2 (service): same actor chain as the doctor dashboard — active user, `DOCTOR` role, active staff with hospital, active `Doctor` profile in the same hospital, active hospital. No permission code required.
   - Ownership: only board buckets whose `DoctorId == doctor.Id` are returned. No bucket → `200` with `servicePoints = []` and zero stats.
3. **Response** (`DoctorAssignedQueueResponseDto`): `queueDate`, `facilityName`, `shiftLabel`, `generatedAtUtc`, `doctorName`, `stats`, `servicePoints[]`. Each service point carries `currentServingTicketNo`, `nextEligibleTicketNo`, `stats` and a **flat** `entries[]` list in `servingOrder` (reused `ExaminationQueueEntryDto`). Every entry carries `queueGroup` (`IN_PROGRESS | CALLED | ELIGIBLE | NOT_ELIGIBLE | SKIPPED | COMPLETED`) and `ineligibleReason` (`NOT_ARRIVED | LEFT | BLOCKED | AWAITING_RESULTS`, only for WAITING/READY entries that are not eligible). The reception board gets the same two additive fields.
4. **Engine tiers** (WAITING/READY only):

   |Tier|Condition|Primary time|
   |---|---|---|
   |0|due protected appointment|`scheduledAt`|
   |1|eligible ELDERLY/PREGNANT or `RETURN_CONSULTATION` passing the gap gate|`readySince ?? arrivedAt`|
   |2|eligible scheduled `ON_TIME_OR_GRACE`|`scheduledAt`|
   |3|eligible READY backfilled|`backfilledAt`|
   |4|eligible|`readySince ?? arrivedAt`|
   |5|other|`scheduledAt`|

   Gap gate: no other protected-but-not-yet-due appointment, or `now + d + b <= nextProtectedScheduledAt` (`d = expected_duration_minutes`, `b = safety_buffer_minutes`). Missing `d`/`b` with an upcoming protected appointment → no fast track. Due appointments are excluded from the gate since they already sit in tier 0.
5. **Blocked entries**: a WAITING/READY entry with `blockedReason` is not eligible, **except** a scheduled entry whose `scheduledAt <= now` (the `HOLD_FOR_SLOT` hold expires at appointment time and due protection is restored). Safe Backfill is not blocked by `blockedReason` (it is an explicit override that clears it).
6. **Diagnostic result signal**: a `RETURN_CONSULTATION` entry is not eligible while `DIAGNOSTIC/final_result_required` (`{"enabled":true}`, default true) is on and the visit has an open `DIAGNOSTIC` queue entry (not completed/cancelled) or `visits.status` is `AWAITING_DIAGNOSTIC | AWAITING_RESULTS`. Every DIAGNOSTIC entry counts as mandatory: `diagnostic_orders.required_for_conclusion` is not mapped in EF (ADR 0008), same heuristic as `DoctorDashboardService.ResolveQueueStatus`.
7. **Doctor dashboard** (`DoctorDashboardService.BuildAssignedQueue`, `ClinicalQueueRules.GetWaitingTier`) is **not** migrated to the engine in this issue.

## Consequences

- Reception board order changes for ELDERLY/PREGNANT entries when a protected appointment is due or the gap is too short (expected by the coordination rules).
- FE #91 consumes `GET /api/v1/doctor/assigned-queue`.
- Until the dashboard migrates, dashboard and assigned-queue ordering may differ.
