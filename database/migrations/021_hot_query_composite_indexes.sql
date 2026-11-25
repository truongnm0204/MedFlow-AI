-- Migration 021: Composite indexes for hot operational queries (Issue #829)
--
-- Notes on v4.1 physical column mapping (index names preserved verbatim for Issue #829 compatibility):
--   1. ix_queue_entries_sp_status_created -> ("service_point_id", "status", "arrived_at")
--      3-column baseline mapping v4.1 physical columns service_point_id and arrived_at
--      (v4.1 has no queue_service_point_id or created_at; clinical queue ordering rules remain unchanged).
--   2. ix_visits_hospital_created_status -> ("checked_in_at" DESC, "status")
--      v4.1 visits resolves hospital through room/doctor/staff/appointment relations rather than storing hospital_id,
--      and records intake time in checked_in_at (no created_at). Serves UC56 time-window status aggregations.
--   3. ix_audit_logs_hospital_occurred_desc -> ("hospital_id", "occurred_at" DESC)
--      Serves UC60 hospital-scoped audit log filtering and newest-first pagination.
--   4. ix_audit_logs_action_occurred_desc -> ("action", "occurred_at" DESC)
--      Serves action-specific audit lookups (NO_SHOW, SERVICE_PRICE_UPDATED, FEFO_BATCH_SELECTED, etc.).
--   5. ix_medicine_batches_fefo_lookup -> ("medicine_id", "is_blocked", "expiry_date" ASC)
--      v4.1 stores batch lock state in boolean column is_blocked (DTO status is computed in application layer).
--   6. ix_service_prices_effective_lookup -> ("service_id", "effective_from" DESC)
--      v4.1 stores hospital_id on services and effective timestamp in effective_from (validFrom is DTO naming).
--
-- Existing single-column indexes are intentionally retained (no DROP INDEX in Issue #829).
-- Production deployment note: standard CREATE INDEX inside BEGIN/COMMIT acquires a SHARE lock that blocks
-- concurrent writes during index build. On large active production tables, schedule a maintenance window
-- or apply CREATE INDEX CONCURRENTLY outside a transaction block.

BEGIN;

SET LOCAL lock_timeout = '5s';

CREATE INDEX IF NOT EXISTS "ix_queue_entries_sp_status_created"
    ON "queue_entries" ("service_point_id", "status", "arrived_at");

CREATE INDEX IF NOT EXISTS "ix_visits_hospital_created_status"
    ON "visits" ("checked_in_at" DESC, "status");

CREATE INDEX IF NOT EXISTS "ix_audit_logs_hospital_occurred_desc"
    ON "audit_logs" ("hospital_id", "occurred_at" DESC);

CREATE INDEX IF NOT EXISTS "ix_audit_logs_action_occurred_desc"
    ON "audit_logs" ("action", "occurred_at" DESC);

CREATE INDEX IF NOT EXISTS "ix_medicine_batches_fefo_lookup"
    ON "medicine_batches" ("medicine_id", "is_blocked", "expiry_date" ASC);

CREATE INDEX IF NOT EXISTS "ix_service_prices_effective_lookup"
    ON "service_prices" ("service_id", "effective_from" DESC);

COMMIT;
