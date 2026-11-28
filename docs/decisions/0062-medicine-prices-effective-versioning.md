# 0062 — Medicine Prices: medicine-scoped effective versioning & admin ownership

- Ngày: 2026-11-29
- Trạng thái: accepted (implemented — Issue #865 / S3a)
- Lane: high-risk
- Liên quan: BF-03 §4 (CL-4), Issue #866 (downstream billing), `0059-global-lock-hierarchy...`

## Context

v4.1 removed the legacy `medicine_prices` table; `service_prices` (service-scoped) remained the only
effective-price implementation. BF-03 §4 requires a hospital-admin-managed **medicine** price list with
effective dates and history. Owner decision D1 fixed the scope: price belongs to `medicines`, never to
`medicine_batches` (batch rows carry stock, not price).

## Decision

1. **New table `medicine_prices` (migration 034)** with the same effective-window shape as
   `service_prices`, plus explicit audit metadata (`decision_number`, `applied_by`, `note`,
   `created_at`, `created_by_user_id`) and a denormalized `hospital_id` FK used for tenant scoping.
   Half-open interval `[effective_from, effective_to)`, `effective_to NULL` = currently effective.
2. **Overlap policy replicated from `ServicePriceAdminService`**, not a DB EXCLUDE constraint:
   a medicine row is locked `FOR UPDATE` (new lock level **14a `medicines`**), then a bounded window
   splits/resumes the baseline; an overlapping scheduled future window is rejected with `409`.
   EXCLUDE would need `btree_gist` and would reject the legitimate split/resume sequence.
3. **Billing contract (#866)** is `IMedicinePriceResolver.TryGetEffectivePriceAsync(hospitalId,
   medicineId, atHospitalLocal)`: read-only, hospital-scoped, returns the unique covering row or null.
   Callers pass the billing instant (e.g. dispense time), enabling historical repricing.
4. **Admin surface** mirrors `/api/v1/admin/service-prices` conventions (roles, problem-details,
   validation messages in Vietnamese, `MEDICINE_PRICE_UPDATED` audit with closed/resumed IDs),
   plus a dedicated history endpoint. No DELETE: price history is immutable.

## Consequences

- `medicine_batches` is untouched; no batch-level price join is introduced.
- Admin updates serialize per medicine, so concurrent writers cannot create overlapping windows.
- The resolver is the single read path for future billing code; it must be used instead of ad-hoc
  `MedicinePrices` queries to preserve the half-open interval semantics.
