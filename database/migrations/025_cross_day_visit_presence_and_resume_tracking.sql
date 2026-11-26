-- 025_cross_day_visit_presence_and_resume_tracking.sql
-- Stage C (#853, ADR-0060): Durable daily presence state on visits (last_present_date, presence_state, last_resumed_at)
-- and idempotent resume queue serialization (queue_entries.idempotency_key + partial unique index).
-- 100% idempotent on re-run. Never edits committed migrations 001..024.

BEGIN;

SET LOCAL lock_timeout = '10s';

-- 1. Fail-fast preflight check before acquiring schema locks or adding constraints
DO $$
DECLARE
  v_bad_presence bigint := 0;
  v_dup_queue_idem bigint := 0;
BEGIN
  IF EXISTS (
    SELECT 1
    FROM information_schema.columns
    WHERE table_schema = 'public'
      AND table_name = 'visits'
      AND column_name = 'presence_state'
  ) THEN
    EXECUTE $sql$
      SELECT COUNT(*)
      FROM "visits"
      WHERE "presence_state" IS NULL
         OR "presence_state" NOT IN ('PRESENT', 'LEFT', 'DEFERRED')
    $sql$ INTO v_bad_presence;

    IF v_bad_presence > 0 THEN
      RAISE EXCEPTION 'Migration 025 preflight failed: % visits row(s) have invalid presence_state. Run database/scripts/preflight_025_audit.sql.', v_bad_presence;
    END IF;
  END IF;

  IF EXISTS (
    SELECT 1
    FROM information_schema.columns
    WHERE table_schema = 'public'
      AND table_name = 'queue_entries'
      AND column_name = 'idempotency_key'
  ) THEN
    EXECUTE $sql$
      SELECT COUNT(*)
      FROM (
        SELECT "idempotency_key"
        FROM "queue_entries"
        WHERE "idempotency_key" IS NOT NULL
        GROUP BY "idempotency_key"
        HAVING COUNT(*) > 1
      ) d
    $sql$ INTO v_dup_queue_idem;

    IF v_dup_queue_idem > 0 THEN
      RAISE EXCEPTION 'Migration 025 preflight failed: % duplicate queue_entries.idempotency_key value(s). Run database/scripts/preflight_025_audit.sql.', v_dup_queue_idem;
    END IF;
  END IF;
END $$;

-- 2. Durable presence and cross-day resume columns on visits
ALTER TABLE "visits"
  ADD COLUMN IF NOT EXISTS "last_present_date" date NULL,
  ADD COLUMN IF NOT EXISTS "presence_state" varchar(16) NOT NULL DEFAULT 'PRESENT',
  ADD COLUMN IF NOT EXISTS "last_resumed_at" timestamp without time zone NULL;

DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1
    FROM pg_constraint
    WHERE conname = 'chk_visits_presence_state'
      AND conrelid = 'public.visits'::regclass
  ) THEN
    ALTER TABLE "visits"
      ADD CONSTRAINT "chk_visits_presence_state"
      CHECK ("presence_state" IN ('PRESENT', 'LEFT', 'DEFERRED'));
  END IF;
END $$;

-- Backfill historical visits deterministically from checked_in_at date without modifying checked_in_at
UPDATE "visits"
SET "last_present_date" = ("checked_in_at")::date
WHERE "last_present_date" IS NULL
  AND "checked_in_at" IS NOT NULL;

CREATE INDEX IF NOT EXISTS "ix_visits_patient_status_checked_in"
  ON "visits" ("patient_id", "status", "checked_in_at");

-- 3. Idempotent cross-day resume serialization on queue_entries
ALTER TABLE "queue_entries"
  ADD COLUMN IF NOT EXISTS "idempotency_key" varchar(100) NULL;

CREATE UNIQUE INDEX IF NOT EXISTS "ux_queue_entries_idempotency_key"
  ON "queue_entries" ("idempotency_key")
  WHERE "idempotency_key" IS NOT NULL;

CREATE INDEX IF NOT EXISTS "ix_queue_entries_visit_date_type_status"
  ON "queue_entries" ("visit_id", "queue_date", "queue_type", "status");

COMMIT;
