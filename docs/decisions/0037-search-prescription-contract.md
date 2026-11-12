# 0037 — Hospital-scoped prescription search (UC44 / #142)

- Ngay: 2026-11-10
- Trang thai: accepted
- Lane: high-risk

## Context

Pharmacy staff must find and open valid prescriptions for preparation and dispensing. The existing pharmacy dashboard shows only a bounded queue and derives hospital association from multiple fallbacks, including prescribed medicine; this is not a sufficient ownership check for PHI search. UC38 issues prescriptions as `ISSUED`; UC37 creation is tracked separately.

## Decision

- Expose `GET /api/v1/pharmacy/prescriptions` with `query`, `status`, `page`, `pageSize` and `GET /api/v1/pharmacy/prescriptions/{idOrCode}`. Response is minimal patient/visit/prescription context plus actual medicine lines in detail. The API never returns phone, email, identity number, address or fabricated price/allergy/stock information.
- Require `[Authorize(Roles = "PHARMACY_STAFF")]` and in service check active/unlocked user, the database-backed `PHARMACY_STAFF` role, `MEDICINE_DISPENSE`, active staff and active hospital. Restrict prescriptions to a completed, hospital-proven visit with consistent room/assigned-doctor ownership, a prescribing doctor in the same hospital when present, hospital fulfillment, issued-or-later status, and medicine-backed line items belonging to the same hospital; exclude drafts, cancelled and superseded prescriptions. A draft replacement does not supersede the currently issued prescription. Missing/foreign/inconsistent detail shares the same 404 response.
- Normalize workflow presentation to `PENDING` (issued, not draft), `PREPARING`, `READY`, `DISPENSED` using the current dispense state; search never changes state. Validate malformed filters/identifiers before querying and audit successful PHI reads with parsed client IP but without storing the raw search phrase.
- Stable pagination and total count apply after scope and filters; no cross-hospital search or medicine-only association.

## Consequences

- Good: independently verifiable two-layer access, visit-bound hospital isolation and discoverability by RX/visit/patient; no schema migration.
- Cost: additional scoped database joins/subqueries and an audit write for each successful read.
- Remaining risk: upstream UC37 is tracked independently; until real prescriptions are issued, this API serves only existing persisted issued records. Frontend search is mock-only and will need a separate consumer integration task.
