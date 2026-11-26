-- Migration 026: Clinical Revalidation, Overdue Return Snapshot Approval & Queue Coordination Purpose (ADR-0061 Stage E1)
-- Adds doctor clinical revalidation columns on diagnostic_orders, overdue return snapshot approval columns on visits,
-- and coordination_purpose ('RESULT_CONCLUSION' | 'CLINICAL_REVIEW') on queue_entries.

BEGIN;

SET LOCAL lock_timeout = '10s';

DO $$
DECLARE
  v_bad_purpose bigint := 0;
  v_orphan_reval_doc bigint := 0;
  v_orphan_overdue_doc bigint := 0;
BEGIN
  IF EXISTS (
    SELECT 1
    FROM information_schema.columns
    WHERE table_schema = 'public'
      AND table_name = 'queue_entries'
      AND column_name = 'coordination_purpose'
  ) THEN
    EXECUTE $sql$
      SELECT COUNT(*)
      FROM "queue_entries"
      WHERE "coordination_purpose" IS NOT NULL
        AND "coordination_purpose" NOT IN ('RESULT_CONCLUSION', 'CLINICAL_REVIEW')
    $sql$ INTO v_bad_purpose;

    IF v_bad_purpose > 0 THEN
      RAISE EXCEPTION 'Migration 026 preflight failed: queue_entries has % row(s) with invalid coordination_purpose.', v_bad_purpose;
    END IF;
  END IF;

  IF EXISTS (
    SELECT 1
    FROM information_schema.columns
    WHERE table_schema = 'public'
      AND table_name = 'diagnostic_orders'
      AND column_name = 'revalidated_by_doctor_id'
  ) THEN
    EXECUTE $sql$
      SELECT COUNT(*)
      FROM "diagnostic_orders" o
      LEFT JOIN "doctors" d ON d."id" = o."revalidated_by_doctor_id"
      WHERE o."revalidated_by_doctor_id" IS NOT NULL
        AND d."id" IS NULL
    $sql$ INTO v_orphan_reval_doc;

    IF v_orphan_reval_doc > 0 THEN
      RAISE EXCEPTION 'Migration 026 preflight failed: diagnostic_orders has % row(s) with orphan revalidated_by_doctor_id.', v_orphan_reval_doc;
    END IF;
  END IF;

  IF EXISTS (
    SELECT 1
    FROM information_schema.columns
    WHERE table_schema = 'public'
      AND table_name = 'visits'
      AND column_name = 'overdue_return_approved_by_doctor_id'
  ) THEN
    EXECUTE $sql$
      SELECT COUNT(*)
      FROM "visits" v
      LEFT JOIN "doctors" d ON d."id" = v."overdue_return_approved_by_doctor_id"
      WHERE v."overdue_return_approved_by_doctor_id" IS NOT NULL
        AND d."id" IS NULL
    $sql$ INTO v_orphan_overdue_doc;

    IF v_orphan_overdue_doc > 0 THEN
      RAISE EXCEPTION 'Migration 026 preflight failed: visits has % row(s) with orphan overdue_return_approved_by_doctor_id.', v_orphan_overdue_doc;
    END IF;
  END IF;
END $$;

ALTER TABLE "diagnostic_orders"
  ADD COLUMN IF NOT EXISTS "validity_extended_until" timestamp without time zone NULL,
  ADD COLUMN IF NOT EXISTS "revalidated_by_doctor_id" bigint NULL,
  ADD COLUMN IF NOT EXISTS "revalidated_at" timestamp without time zone NULL,
  ADD COLUMN IF NOT EXISTS "revalidation_note" text NULL;

DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1
    FROM pg_constraint
    WHERE conname = 'fk_diagnostic_orders_revalidated_by_doctor_id'
      AND conrelid = '"diagnostic_orders"'::regclass
  ) THEN
    ALTER TABLE "diagnostic_orders"
      ADD CONSTRAINT "fk_diagnostic_orders_revalidated_by_doctor_id"
      FOREIGN KEY ("revalidated_by_doctor_id") REFERENCES "doctors" ("id");
  END IF;
END $$;

CREATE INDEX IF NOT EXISTS "ix_diagnostic_orders_revalidated_by_doctor_id"
  ON "diagnostic_orders" ("revalidated_by_doctor_id");

ALTER TABLE "visits"
  ADD COLUMN IF NOT EXISTS "overdue_return_approved_by_doctor_id" bigint NULL,
  ADD COLUMN IF NOT EXISTS "overdue_return_approved_at" timestamp without time zone NULL,
  ADD COLUMN IF NOT EXISTS "overdue_return_approval_note" text NULL,
  ADD COLUMN IF NOT EXISTS "overdue_return_result_snapshot" varchar(128) NULL;

DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1
    FROM pg_constraint
    WHERE conname = 'fk_visits_overdue_return_approved_by_doctor_id'
      AND conrelid = '"visits"'::regclass
  ) THEN
    ALTER TABLE "visits"
      ADD CONSTRAINT "fk_visits_overdue_return_approved_by_doctor_id"
      FOREIGN KEY ("overdue_return_approved_by_doctor_id") REFERENCES "doctors" ("id");
  END IF;
END $$;

CREATE INDEX IF NOT EXISTS "ix_visits_overdue_return_approved_by_doctor_id"
  ON "visits" ("overdue_return_approved_by_doctor_id");

ALTER TABLE "queue_entries"
  ADD COLUMN IF NOT EXISTS "coordination_purpose" varchar(24) NULL;

DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1
    FROM pg_constraint
    WHERE conname = 'chk_queue_entries_coordination_purpose'
      AND conrelid = '"queue_entries"'::regclass
  ) THEN
    ALTER TABLE "queue_entries"
      ADD CONSTRAINT "chk_queue_entries_coordination_purpose"
      CHECK ("coordination_purpose" IS NULL OR "coordination_purpose" IN ('RESULT_CONCLUSION', 'CLINICAL_REVIEW'));
  END IF;
END $$;

COMMIT;
