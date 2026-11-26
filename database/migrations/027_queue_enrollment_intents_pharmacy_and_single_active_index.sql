-- Migration 027: Unified Queue Enrollment (QUEUE_ENROLL), Presence Session Tracking,
-- Target-Bound Queue Entries, Single Active Post-Exam Partial Unique Index, Pharmacy Queue Seed,
-- and Durable Payment Enrollment Intents (ADR-0061 Part 2 / Issue #858).
-- 100% idempotent across repeated runs. Never silently deletes or closes active IN_PROGRESS queues.

BEGIN;

SET LOCAL lock_timeout = '10s';

-- ============================================================================
-- 0. FAIL-FAST PREFLIGHT AUDIT (zero auto-delete, zero silent IN_PROGRESS close)
-- ============================================================================
DO $$
DECLARE
  v_bad_count bigint := 0;
  v_samples text := '';
BEGIN
  -- 0.1. visits with > 1 active post-exam queue entry (DIAGNOSTIC / RETURN_CONSULTATION / PHARMACY)
  SELECT COUNT(*), coalesce(string_agg(visit_id::text, ', ' ORDER BY visit_id), '')
    INTO v_bad_count, v_samples
  FROM (
    SELECT qe."visit_id" AS visit_id
    FROM "queue_entries" qe
    WHERE qe."visit_id" IS NOT NULL
      AND upper(btrim(coalesce(qe."queue_type", ''))) IN ('DIAGNOSTIC', 'RETURN_CONSULTATION', 'PHARMACY')
      AND upper(btrim(coalesce(qe."status", ''))) IN ('WAITING', 'READY', 'CALLED', 'IN_PROGRESS', 'IN_CONSULTATION', 'SERVING')
    GROUP BY qe."visit_id"
    HAVING COUNT(*) > 1
    ORDER BY qe."visit_id"
    LIMIT 10
  ) s;

  IF v_bad_count > 0 THEN
    RAISE EXCEPTION '[PREFLIGHT_027_FAILED] visits has multiple active post-exam queue_entries (% visit(s); sample visit ids: %). Run database/scripts/preflight_027_audit.sql.',
      v_bad_count, v_samples;
  END IF;

  -- 0.2. diagnostic_orders.queue_entry_id pointing to missing or cross-visit queue_entries
  SELECT COUNT(*), coalesce(string_agg(ord_id::text, ', ' ORDER BY ord_id), '')
    INTO v_bad_count, v_samples
  FROM (
    SELECT o."id" AS ord_id
    FROM "diagnostic_orders" o
    LEFT JOIN "queue_entries" q ON q."id" = o."queue_entry_id"
    WHERE o."queue_entry_id" IS NOT NULL
      AND (q."id" IS NULL OR q."visit_id" IS DISTINCT FROM o."visit_id")
    ORDER BY o."id"
    LIMIT 10
  ) s;

  IF v_bad_count > 0 THEN
    RAISE EXCEPTION '[PREFLIGHT_027_FAILED] diagnostic_orders has queue_entry_id missing or belonging to another visit (% order(s); sample order ids: %). Run database/scripts/preflight_027_audit.sql.',
      v_bad_count, v_samples;
  END IF;

  -- 0.3. prescriptions with NULL visit_id
  SELECT COUNT(*), coalesce(string_agg(rx_id::text, ', ' ORDER BY rx_id), '')
    INTO v_bad_count, v_samples
  FROM (
    SELECT p."id" AS rx_id
    FROM "prescriptions" p
    WHERE p."visit_id" IS NULL
    ORDER BY p."id"
    LIMIT 10
  ) s;

  IF v_bad_count > 0 THEN
    RAISE EXCEPTION '[PREFLIGHT_027_FAILED] prescriptions has NULL visit_id (% row(s); sample prescription ids: %). Run database/scripts/preflight_027_audit.sql.',
      v_bad_count, v_samples;
  END IF;

  -- 0.4. Re-run guard for visits.presence_session_no
  IF EXISTS (
    SELECT 1
    FROM information_schema.columns
    WHERE table_schema = 'public'
      AND table_name = 'visits'
      AND column_name = 'presence_session_no'
  ) THEN
    EXECUTE $dyn$
      SELECT COUNT(*), coalesce(string_agg(v_id::text, ', ' ORDER BY v_id), '')
      FROM (
        SELECT v."id" AS v_id
        FROM "visits" v
        WHERE v."presence_session_no" IS NULL OR v."presence_session_no" < 1
        ORDER BY v."id"
        LIMIT 10
      ) s
    $dyn$ INTO v_bad_count, v_samples;

    IF v_bad_count > 0 THEN
      RAISE EXCEPTION '[PREFLIGHT_027_FAILED] visits has invalid presence_session_no (% row(s); sample visit ids: %).',
        v_bad_count, v_samples;
    END IF;
  END IF;

  -- 0.5. Re-run guard for payment_enrollment_intents
  IF to_regclass('public.payment_enrollment_intents') IS NOT NULL THEN
    EXECUTE $dyn$
      SELECT COUNT(*), coalesce(string_agg(pei_id::text, ', ' ORDER BY pei_id), '')
      FROM (
        SELECT pei."id" AS pei_id
        FROM "payment_enrollment_intents" pei
        LEFT JOIN "payments" p ON p."id" = pei."payment_id" AND p."visit_id" = pei."visit_id"
        LEFT JOIN "diagnostic_orders" o ON o."id" = pei."diagnostic_order_id" AND o."visit_id" = pei."visit_id"
        WHERE p."id" IS NULL
           OR o."id" IS NULL
           OR pei."retry_count" < 0
           OR pei."status" NOT IN (
             'PENDING_SETTLEMENT',
             'PENDING_POST_COMMIT',
             'CONSUMED_ENROLLED',
             'SKIPPED_NOT_ELIGIBLE',
             'CANCELLED',
             'EXPIRED'
           )
        ORDER BY pei."id"
        LIMIT 10
      ) s
    $dyn$ INTO v_bad_count, v_samples;

    IF v_bad_count > 0 THEN
      RAISE EXCEPTION '[PREFLIGHT_027_FAILED] payment_enrollment_intents has orphan or invalid status rows (% row(s); sample ids: %).',
        v_bad_count, v_samples;
    END IF;
  END IF;
END $$;

-- ============================================================================
-- 1. Permission QUEUE_ENROLL & dev/seed role_permissions
-- ============================================================================
INSERT INTO "permissions" ("code")
SELECT 'QUEUE_ENROLL'
WHERE NOT EXISTS (SELECT 1 FROM "permissions" WHERE "code" = 'QUEUE_ENROLL');

-- Grant QUEUE_ENROLL in dev/test seeded environments ONLY (gated on reception01@medflow.test)
-- to RECEPTION, DIAGNOSTIC_STAFF, PHARMACY_STAFF, and HOSPITAL_ADMIN (NOT SYSTEM_ADMIN or DOCTOR).
INSERT INTO "role_permissions" ("role_id", "permission_id")
SELECT r."id", p."id"
FROM "roles" r
CROSS JOIN "permissions" p
WHERE p."code" = 'QUEUE_ENROLL'
  AND r."code" IN ('RECEPTION', 'DIAGNOSTIC_STAFF', 'PHARMACY_STAFF', 'HOSPITAL_ADMIN')
  AND EXISTS (SELECT 1 FROM "users" WHERE "email" = 'reception01@medflow.test')
ON CONFLICT DO NOTHING;

-- ============================================================================
-- 2. visits.presence_session_no (monotonically incremented per presence session)
-- ============================================================================
ALTER TABLE "visits"
  ADD COLUMN IF NOT EXISTS "presence_session_no" integer NOT NULL DEFAULT 1;

DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1
    FROM pg_constraint
    WHERE conname = 'chk_visits_presence_session_no_positive'
      AND conrelid = '"visits"'::regclass
  ) THEN
    ALTER TABLE "visits"
      ADD CONSTRAINT "chk_visits_presence_session_no_positive"
      CHECK ("presence_session_no" >= 1);
  END IF;
END $$;

-- ============================================================================
-- 3. Composite unique targets, queue_entries target bindings & partial unique index
-- ============================================================================
ALTER TABLE "prescriptions"
  ALTER COLUMN "visit_id" SET NOT NULL;

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'uq_diagnostic_orders_id_visit_id') THEN
    ALTER TABLE "diagnostic_orders"
      ADD CONSTRAINT "uq_diagnostic_orders_id_visit_id" UNIQUE ("id", "visit_id");
  END IF;

  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'uq_prescriptions_id_visit_id') THEN
    ALTER TABLE "prescriptions"
      ADD CONSTRAINT "uq_prescriptions_id_visit_id" UNIQUE ("id", "visit_id");
  END IF;

  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'uq_payments_id_visit_id') THEN
    ALTER TABLE "payments"
      ADD CONSTRAINT "uq_payments_id_visit_id" UNIQUE ("id", "visit_id");
  END IF;
END $$;

ALTER TABLE "queue_entries"
  ADD COLUMN IF NOT EXISTS "diagnostic_order_id" bigint NULL,
  ADD COLUMN IF NOT EXISTS "prescription_id" bigint NULL;

-- Deterministic backfill from diagnostic_orders.queue_entry_id where visit_id matches
WITH matched_orders AS (
  SELECT
    o."queue_entry_id" AS queue_entry_id,
    o."visit_id" AS visit_id,
    MIN(o."id") AS diagnostic_order_id
  FROM "diagnostic_orders" o
  WHERE o."queue_entry_id" IS NOT NULL
    AND o."visit_id" IS NOT NULL
  GROUP BY o."queue_entry_id", o."visit_id"
)
UPDATE "queue_entries" q
SET "diagnostic_order_id" = mo.diagnostic_order_id
FROM matched_orders mo
WHERE q."id" = mo.queue_entry_id
  AND q."visit_id" = mo.visit_id
  AND q."diagnostic_order_id" IS NULL
  AND q."prescription_id" IS NULL;

DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1
    FROM pg_constraint
    WHERE conname = 'fk_queue_entries_diagnostic_order_visit'
      AND conrelid = '"queue_entries"'::regclass
  ) THEN
    ALTER TABLE "queue_entries"
      ADD CONSTRAINT "fk_queue_entries_diagnostic_order_visit"
      FOREIGN KEY ("diagnostic_order_id", "visit_id")
      REFERENCES "diagnostic_orders" ("id", "visit_id")
      ON DELETE RESTRICT;
  END IF;

  IF NOT EXISTS (
    SELECT 1
    FROM pg_constraint
    WHERE conname = 'fk_queue_entries_prescription_visit'
      AND conrelid = '"queue_entries"'::regclass
  ) THEN
    ALTER TABLE "queue_entries"
      ADD CONSTRAINT "fk_queue_entries_prescription_visit"
      FOREIGN KEY ("prescription_id", "visit_id")
      REFERENCES "prescriptions" ("id", "visit_id")
      ON DELETE RESTRICT;
  END IF;

  IF NOT EXISTS (
    SELECT 1
    FROM pg_constraint
    WHERE conname = 'chk_queue_entries_target_exclusivity'
      AND conrelid = '"queue_entries"'::regclass
  ) THEN
    ALTER TABLE "queue_entries"
      ADD CONSTRAINT "chk_queue_entries_target_exclusivity"
      CHECK (NOT ("diagnostic_order_id" IS NOT NULL AND "prescription_id" IS NOT NULL));
  END IF;
END $$;

CREATE INDEX IF NOT EXISTS "ix_queue_entries_diagnostic_order_id"
  ON "queue_entries" ("diagnostic_order_id");

CREATE INDEX IF NOT EXISTS "ix_queue_entries_prescription_id"
  ON "queue_entries" ("prescription_id");

CREATE UNIQUE INDEX IF NOT EXISTS "ux_queue_entries_one_active_post_exam_per_visit"
  ON "queue_entries" ("visit_id")
  WHERE "visit_id" IS NOT NULL
    AND upper(btrim(coalesce("queue_type", ''))) IN ('DIAGNOSTIC', 'RETURN_CONSULTATION', 'PHARMACY')
    AND upper(btrim(coalesce("status", ''))) IN ('WAITING', 'READY', 'CALLED', 'IN_PROGRESS', 'IN_CONSULTATION', 'SERVING');

-- ============================================================================
-- 4. Seed active PHARMACY room & QueueServicePoint for dev/seed Hospital 1
-- ============================================================================
INSERT INTO "hospital_rooms" (
  "hospital_id",
  "department_id",
  "room_code",
  "name",
  "room_type",
  "location",
  "capacity",
  "status"
)
SELECT
  d."hospital_id",
  d."id",
  'RM-PHA-01',
  'Quầy Cấp phát thuốc trung tâm',
  'PHARMACY',
  'Tầng 1, Khu Dược',
  2,
  'ACTIVE'
FROM "departments" d
WHERE d."hospital_id" = 1
  AND d."department_code" = 'PHA'
  AND d."is_active" IS DISTINCT FROM FALSE
  AND EXISTS (SELECT 1 FROM "users" WHERE "email" = 'reception01@medflow.test')
  AND NOT EXISTS (
    SELECT 1
    FROM "hospital_rooms" r
    WHERE r."hospital_id" = d."hospital_id"
      AND (r."room_code" = 'RM-PHA-01' OR upper(btrim(coalesce(r."room_type", ''))) = 'PHARMACY')
  );

INSERT INTO "queue_service_points" (
  "hospital_id",
  "room_id",
  "doctor_id",
  "service_id",
  "point_type",
  "name",
  "is_active"
)
SELECT
  r."hospital_id",
  r."id",
  NULL,
  NULL,
  'PHARMACY',
  'Quầy cấp phát thuốc 1',
  TRUE
FROM "hospital_rooms" r
WHERE r."hospital_id" = 1
  AND upper(btrim(coalesce(r."room_type", ''))) = 'PHARMACY'
  AND upper(btrim(coalesce(r."status", ''))) = 'ACTIVE'
  AND EXISTS (SELECT 1 FROM "users" WHERE "email" = 'reception01@medflow.test')
  AND NOT EXISTS (
    SELECT 1
    FROM "queue_service_points" sp
    WHERE sp."hospital_id" = r."hospital_id"
      AND upper(btrim(coalesce(sp."point_type", ''))) = 'PHARMACY'
  )
ORDER BY r."id"
LIMIT 1;

-- ============================================================================
-- 5. Durable payment_enrollment_intents table (Level 16c)
-- ============================================================================
CREATE TABLE IF NOT EXISTS "payment_enrollment_intents" (
  "id" bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
  "payment_id" bigint NOT NULL,
  "visit_id" bigint NOT NULL,
  "patient_id" bigint NOT NULL,
  "diagnostic_order_id" bigint NOT NULL,
  "requested_by_user_id" bigint NULL,
  "requested_by_role" varchar(32) NOT NULL,
  "idempotency_key" varchar(128) NOT NULL,
  "presence_verified_date" date NULL,
  "presence_session_no" integer NULL,
  "status" varchar(32) NOT NULL,
  "retry_count" integer NOT NULL DEFAULT 0,
  "outcome_code" varchar(64) NULL,
  "outcome_message" varchar(500) NULL,
  "queue_entry_id" bigint NULL,
  "created_at" timestamp without time zone NOT NULL,
  "expires_at" timestamp without time zone NOT NULL,
  "processed_at" timestamp without time zone NULL
);

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'uq_payment_enrollment_intents_payment_id') THEN
    ALTER TABLE "payment_enrollment_intents"
      ADD CONSTRAINT "uq_payment_enrollment_intents_payment_id"
      UNIQUE ("payment_id");
  END IF;

  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'uq_payment_enrollment_intents_idempotency_key') THEN
    ALTER TABLE "payment_enrollment_intents"
      ADD CONSTRAINT "uq_payment_enrollment_intents_idempotency_key"
      UNIQUE ("idempotency_key");
  END IF;

  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_payment_enrollment_intents_patient_id') THEN
    ALTER TABLE "payment_enrollment_intents"
      ADD CONSTRAINT "fk_payment_enrollment_intents_patient_id"
      FOREIGN KEY ("patient_id") REFERENCES "patients" ("id");
  END IF;

  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_payment_enrollment_intents_requested_by_user_id') THEN
    ALTER TABLE "payment_enrollment_intents"
      ADD CONSTRAINT "fk_payment_enrollment_intents_requested_by_user_id"
      FOREIGN KEY ("requested_by_user_id") REFERENCES "users" ("id");
  END IF;

  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_payment_enrollment_intents_queue_entry_id') THEN
    ALTER TABLE "payment_enrollment_intents"
      ADD CONSTRAINT "fk_payment_enrollment_intents_queue_entry_id"
      FOREIGN KEY ("queue_entry_id") REFERENCES "queue_entries" ("id");
  END IF;

  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_payment_enrollment_intents_payment_visit') THEN
    ALTER TABLE "payment_enrollment_intents"
      ADD CONSTRAINT "fk_payment_enrollment_intents_payment_visit"
      FOREIGN KEY ("payment_id", "visit_id") REFERENCES "payments" ("id", "visit_id")
      ON DELETE CASCADE;
  END IF;

  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_payment_enrollment_intents_order_visit') THEN
    ALTER TABLE "payment_enrollment_intents"
      ADD CONSTRAINT "fk_payment_enrollment_intents_order_visit"
      FOREIGN KEY ("diagnostic_order_id", "visit_id") REFERENCES "diagnostic_orders" ("id", "visit_id")
      ON DELETE RESTRICT;
  END IF;

  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_payment_enrollment_intents_status') THEN
    ALTER TABLE "payment_enrollment_intents"
      ADD CONSTRAINT "chk_payment_enrollment_intents_status"
      CHECK ("status" IN (
        'PENDING_SETTLEMENT',
        'PENDING_POST_COMMIT',
        'CONSUMED_ENROLLED',
        'SKIPPED_NOT_ELIGIBLE',
        'CANCELLED',
        'EXPIRED'
      ));
  END IF;

  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_payment_enrollment_intents_retry_nonneg') THEN
    ALTER TABLE "payment_enrollment_intents"
      ADD CONSTRAINT "chk_payment_enrollment_intents_retry_nonneg"
      CHECK ("retry_count" >= 0);
  END IF;
END $$;

CREATE INDEX IF NOT EXISTS "ix_payment_enrollment_intents_visit_id"
  ON "payment_enrollment_intents" ("visit_id");

CREATE INDEX IF NOT EXISTS "ix_payment_enrollment_intents_patient_id"
  ON "payment_enrollment_intents" ("patient_id");

CREATE INDEX IF NOT EXISTS "ix_payment_enrollment_intents_diagnostic_order_id"
  ON "payment_enrollment_intents" ("diagnostic_order_id");

CREATE INDEX IF NOT EXISTS "ix_payment_enrollment_intents_requested_by_user_id"
  ON "payment_enrollment_intents" ("requested_by_user_id");

CREATE INDEX IF NOT EXISTS "ix_payment_enrollment_intents_queue_entry_id"
  ON "payment_enrollment_intents" ("queue_entry_id");

CREATE INDEX IF NOT EXISTS "ix_payment_enrollment_intents_recovery"
  ON "payment_enrollment_intents" ("status", "created_at")
  WHERE "status" IN ('PENDING_SETTLEMENT', 'PENDING_POST_COMMIT');

COMMIT;
