-- 023_business_integrity_constraints_and_indexes.sql
-- ADR-0059: Per-Charge Pay-Before-Service (Option A), Cross-Visit Composite FKs,
-- Lineage/EMR additive columns, and Pharmacy/Clinical integrity constraints.
-- Non-destructive fail-fast preflight: aborts and rolls back on any pre-existing violation
-- without clamping negative stock or deleting/merging clinical/payment records.

BEGIN;

-- ============================================================
-- 1. Fail-Fast Preflight Validation (Zero Auto-Clamp / Zero Auto-Delete)
-- ============================================================
DO $$
DECLARE
  v_count bigint;
  v_samples text;
  v_has_charge_type boolean;
BEGIN
  SELECT COUNT(*), coalesce(string_agg(id::text, ', '), '')
  INTO v_count, v_samples
  FROM (
    SELECT "id" FROM "medicine_batches"
    WHERE "quantity_on_hand" IS NULL OR "quantity_on_hand" < 0
    ORDER BY "id" LIMIT 10
  ) s;
  IF v_count > 0 THEN
    RAISE EXCEPTION '023 preflight failed: medicine_batches has null or negative quantity_on_hand (sample IDs: %). Reconcile stock before migrating.', v_samples;
  END IF;

  SELECT COUNT(*), coalesce(string_agg(id::text, ', '), '')
  INTO v_count, v_samples
  FROM (
    SELECT "id" FROM "diagnostic_orders"
    WHERE "visit_id" IS NULL
    ORDER BY "id" LIMIT 10
  ) s;
  IF v_count > 0 THEN
    RAISE EXCEPTION '023 preflight failed: diagnostic_orders has NULL visit_id (sample IDs: %).', v_samples;
  END IF;

  SELECT COUNT(*), coalesce(string_agg(id::text, ', '), '')
  INTO v_count, v_samples
  FROM (
    SELECT "id" FROM "visit_charges"
    WHERE "visit_id" IS NULL OR ("unit_price" IS NOT NULL AND "unit_price" < 0)
    ORDER BY "id" LIMIT 10
  ) s;
  IF v_count > 0 THEN
    RAISE EXCEPTION '023 preflight failed: visit_charges has NULL visit_id or negative unit_price (sample IDs: %).', v_samples;
  END IF;

  SELECT COUNT(*), coalesce(string_agg(vc_id::text, ', '), '')
  INTO v_count, v_samples
  FROM (
    SELECT vc."id" AS vc_id
    FROM "visit_charges" vc
    LEFT JOIN "diagnostic_orders" d ON d."id" = vc."diagnostic_order_id"
    WHERE vc."diagnostic_order_id" IS NOT NULL
      AND (d."id" IS NULL OR d."visit_id" IS DISTINCT FROM vc."visit_id")
    ORDER BY vc."id" LIMIT 10
  ) s;
  IF v_count > 0 THEN
    RAISE EXCEPTION '023 preflight failed: visit_charges has diagnostic_order_id belonging to a different visit (sample charge IDs: %).', v_samples;
  END IF;

  SELECT EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_schema = 'public' AND table_name = 'visit_charges' AND column_name = 'charge_type'
  ) INTO v_has_charge_type;

  IF NOT v_has_charge_type THEN
    SELECT COUNT(*), coalesce(string_agg(visit_id::text, ', '), '')
    INTO v_count, v_samples
    FROM (
      SELECT "visit_id" AS visit_id
      FROM "visit_charges"
      WHERE "diagnostic_order_id" IS NULL
      GROUP BY "visit_id"
      HAVING COUNT(*) > 1
      ORDER BY "visit_id" LIMIT 10
    ) s;
    IF v_count > 0 THEN
      RAISE EXCEPTION '023 preflight failed: visit_charges has multiple non-diagnostic charges on the same visit (sample visit IDs: %).', v_samples;
    END IF;
  END IF;

  SELECT COUNT(*), coalesce(string_agg(diagnostic_order_id::text, ', '), '')
  INTO v_count, v_samples
  FROM (
    SELECT "diagnostic_order_id" AS diagnostic_order_id
    FROM "visit_charges"
    WHERE "diagnostic_order_id" IS NOT NULL
    GROUP BY "diagnostic_order_id"
    HAVING COUNT(*) > 1
    ORDER BY "diagnostic_order_id" LIMIT 10
  ) s;
  IF v_count > 0 THEN
    RAISE EXCEPTION '023 preflight failed: visit_charges has duplicate diagnostic_order_id (sample order IDs: %).', v_samples;
  END IF;

  SELECT COUNT(*), coalesce(string_agg(id::text, ', '), '')
  INTO v_count, v_samples
  FROM (
    SELECT "id" FROM "payments"
    WHERE "visit_id" IS NULL
    ORDER BY "id" LIMIT 10
  ) s;
  IF v_count > 0 THEN
    RAISE EXCEPTION '023 preflight failed: payments has NULL visit_id (sample payment IDs: %).', v_samples;
  END IF;

  SELECT COUNT(*), coalesce(string_agg(prescription_id::text, ', '), '')
  INTO v_count, v_samples
  FROM (
    SELECT "prescription_id" AS prescription_id
    FROM "medication_dispenses"
    WHERE "prescription_id" IS NOT NULL
      AND upper(btrim(coalesce("status", ''))) IN ('DISPENSED', 'COMPLETED')
    GROUP BY "prescription_id"
    HAVING COUNT(*) > 1
    ORDER BY "prescription_id" LIMIT 10
  ) s;
  IF v_count > 0 THEN
    RAISE EXCEPTION '023 preflight failed: medication_dispenses has duplicate DISPENSED/COMPLETED rows (sample prescription IDs: %).', v_samples;
  END IF;

  SELECT COUNT(*), coalesce(string_agg(visit_id::text, ', '), '')
  INTO v_count, v_samples
  FROM (
    SELECT "visit_id" AS visit_id
    FROM "prescriptions"
    WHERE "visit_id" IS NOT NULL
      AND upper(btrim(coalesce("status", ''))) = 'DRAFT'
    GROUP BY "visit_id"
    HAVING COUNT(*) > 1
    ORDER BY "visit_id" LIMIT 10
  ) s;
  IF v_count > 0 THEN
    RAISE EXCEPTION '023 preflight failed: prescriptions has duplicate DRAFT rows per visit (sample visit IDs: %).', v_samples;
  END IF;

  SELECT COUNT(*), coalesce(string_agg(visit_id::text, ', '), '')
  INTO v_count, v_samples
  FROM (
    SELECT "visit_id" AS visit_id
    FROM "medical_records"
    WHERE "visit_id" IS NOT NULL
    GROUP BY "visit_id"
    HAVING COUNT(*) > 1
    ORDER BY "visit_id" LIMIT 10
  ) s;
  IF v_count > 0 THEN
    RAISE EXCEPTION '023 preflight failed: medical_records has duplicate visit_id rows (sample visit IDs: %).', v_samples;
  END IF;

  SELECT COUNT(*) INTO v_count
  FROM (
    SELECT 1
    FROM "medical_record_revisions"
    GROUP BY "medical_record_id", "revision_no"
    HAVING COUNT(*) > 1
  ) s;
  IF v_count > 0 THEN
    RAISE EXCEPTION '023 preflight failed: medical_record_revisions has duplicate (medical_record_id, revision_no) pairs.';
  END IF;

  SELECT COUNT(*) INTO v_count
  FROM (
    SELECT 1
    FROM "diagnostic_results"
    GROUP BY "diagnostic_order_id"
    HAVING COUNT(*) > 1
  ) s;
  IF v_count > 0 THEN
    RAISE EXCEPTION '023 preflight failed: diagnostic_results has duplicate diagnostic_order_id rows.';
  END IF;

  SELECT COUNT(*) INTO v_count
  FROM (
    SELECT 1
    FROM "diagnostic_result_versions"
    GROUP BY "diagnostic_result_id", "version_no"
    HAVING COUNT(*) > 1
  ) s;
  IF v_count > 0 THEN
    RAISE EXCEPTION '023 preflight failed: diagnostic_result_versions has duplicate (diagnostic_result_id, version_no) pairs.';
  END IF;
END $$;

-- ============================================================
-- 2. Cross-Visit Composite FKs & Option A Per-Charge Billing
-- ============================================================
ALTER TABLE "diagnostic_orders"
  ALTER COLUMN "visit_id" SET NOT NULL;

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'uq_diagnostic_orders_id_visit_id') THEN
    ALTER TABLE "diagnostic_orders"
      ADD CONSTRAINT "uq_diagnostic_orders_id_visit_id" UNIQUE ("id", "visit_id");
  END IF;
END $$;

ALTER TABLE "visit_charges"
  ALTER COLUMN "visit_id" SET NOT NULL,
  ADD COLUMN IF NOT EXISTS "charge_type" varchar(20) NOT NULL DEFAULT 'EXAMINATION',
  ADD COLUMN IF NOT EXISTS "status" varchar(24) NOT NULL DEFAULT 'ACTIVE',
  ADD COLUMN IF NOT EXISTS "created_at" timestamp without time zone NOT NULL DEFAULT (now() AT TIME ZONE 'Asia/Ho_Chi_Minh'),
  ADD COLUMN IF NOT EXISTS "cancelled_at" timestamp without time zone NULL;

-- Deterministic FK-based tag for existing diagnostic_order_id rows (zero price/payment guessing):
UPDATE "visit_charges"
SET "charge_type" = 'DIAGNOSTIC'
WHERE "diagnostic_order_id" IS NOT NULL
  AND "charge_type" = 'EXAMINATION';

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'uq_visit_charges_id_visit_id') THEN
    ALTER TABLE "visit_charges"
      ADD CONSTRAINT "uq_visit_charges_id_visit_id" UNIQUE ("id", "visit_id");
  END IF;

  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_visit_charges_unit_price_nonneg') THEN
    ALTER TABLE "visit_charges"
      ADD CONSTRAINT "chk_visit_charges_unit_price_nonneg"
      CHECK ("unit_price" IS NULL OR "unit_price" >= 0);
  END IF;

  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_visit_charges_status') THEN
    ALTER TABLE "visit_charges"
      ADD CONSTRAINT "chk_visit_charges_status"
      CHECK ("status" IN ('ACTIVE', 'CANCELLED', 'CANCELLED_REFUND_PENDING'));
  END IF;

  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_visit_charges_type_consistency') THEN
    ALTER TABLE "visit_charges"
      ADD CONSTRAINT "chk_visit_charges_type_consistency"
      CHECK (
        ("charge_type" = 'EXAMINATION' AND "diagnostic_order_id" IS NULL)
        OR ("charge_type" = 'DIAGNOSTIC' AND "diagnostic_order_id" IS NOT NULL)
      );
  END IF;

  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_visit_charges_diagnostic_order_visit') THEN
    ALTER TABLE "visit_charges"
      ADD CONSTRAINT "fk_visit_charges_diagnostic_order_visit"
      FOREIGN KEY ("diagnostic_order_id", "visit_id")
      REFERENCES "diagnostic_orders" ("id", "visit_id");
  END IF;
END $$;

CREATE UNIQUE INDEX IF NOT EXISTS "uq_visit_charges_active_exam_per_visit"
  ON "visit_charges" ("visit_id")
  WHERE "charge_type" = 'EXAMINATION' AND "status" = 'ACTIVE';

CREATE UNIQUE INDEX IF NOT EXISTS "uq_visit_charges_diagnostic_order"
  ON "visit_charges" ("diagnostic_order_id")
  WHERE "diagnostic_order_id" IS NOT NULL;

ALTER TABLE "payments"
  ALTER COLUMN "visit_id" SET NOT NULL,
  ADD COLUMN IF NOT EXISTS "visit_charge_id" bigint NULL;

DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_schema = 'public' AND table_name = 'payments' AND column_name = 'payment_scope'
  ) THEN
    -- Catalog-only default tags pre-023 historical rows as 'LEGACY_VISIT', then switches default to 'CHARGE':
    ALTER TABLE "payments"
      ADD COLUMN "payment_scope" varchar(16) NOT NULL DEFAULT 'LEGACY_VISIT';
    ALTER TABLE "payments"
      ALTER COLUMN "payment_scope" SET DEFAULT 'CHARGE';
  END IF;

  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_payments_scope_charge_consistency') THEN
    ALTER TABLE "payments"
      ADD CONSTRAINT "chk_payments_scope_charge_consistency"
      CHECK (
        ("payment_scope" = 'CHARGE' AND "visit_charge_id" IS NOT NULL AND "visit_id" IS NOT NULL)
        OR ("payment_scope" = 'LEGACY_VISIT' AND "visit_charge_id" IS NULL AND "visit_id" IS NOT NULL)
      );
  END IF;

  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_payments_charge_visit') THEN
    ALTER TABLE "payments"
      ADD CONSTRAINT "fk_payments_charge_visit"
      FOREIGN KEY ("visit_charge_id", "visit_id")
      REFERENCES "visit_charges" ("id", "visit_id");
  END IF;
END $$;

CREATE INDEX IF NOT EXISTS "ix_payments_visit_charge_id"
  ON "payments" ("visit_charge_id");

DROP INDEX IF EXISTS "ux_payments_one_pending_per_visit";

CREATE UNIQUE INDEX IF NOT EXISTS "ux_payments_one_pending_per_charge"
  ON "payments" ("visit_charge_id")
  WHERE "visit_charge_id" IS NOT NULL AND upper(btrim("status")) = 'PENDING';

CREATE UNIQUE INDEX IF NOT EXISTS "ux_payments_one_paid_per_charge"
  ON "payments" ("visit_charge_id")
  WHERE "visit_charge_id" IS NOT NULL AND upper(btrim("status")) = 'PAID';

CREATE UNIQUE INDEX IF NOT EXISTS "ux_payments_one_pending_legacy_visit"
  ON "payments" ("visit_id")
  WHERE "visit_id" IS NOT NULL AND "visit_charge_id" IS NULL AND upper(btrim("status")) = 'PENDING';

-- ============================================================
-- 3. Auth Lineage, Appointment Lineage & EMR Revision Columns
-- ============================================================
ALTER TABLE "auth_sessions"
  ADD COLUMN IF NOT EXISTS "root_session_id" bigint NULL REFERENCES "auth_sessions" ("id"),
  ADD COLUMN IF NOT EXISTS "revoked_reason" varchar(32) NULL;

CREATE INDEX IF NOT EXISTS "ix_auth_sessions_root_session_id"
  ON "auth_sessions" ("root_session_id");

ALTER TABLE "appointments"
  ADD COLUMN IF NOT EXISTS "root_appointment_id" bigint NULL REFERENCES "appointments" ("id"),
  ADD COLUMN IF NOT EXISTS "rescheduled_from_appointment_id" bigint NULL REFERENCES "appointments" ("id");

CREATE INDEX IF NOT EXISTS "ix_appointments_root_appointment_id"
  ON "appointments" ("root_appointment_id");

ALTER TABLE "medical_record_revisions"
  ADD COLUMN IF NOT EXISTS "clinical_progression_note" text NULL,
  ADD COLUMN IF NOT EXISTS "clinical_interpretation" text NULL,
  ADD COLUMN IF NOT EXISTS "treatment_plan" text NULL,
  ADD COLUMN IF NOT EXISTS "discharge_instructions" text NULL,
  ADD COLUMN IF NOT EXISTS "follow_up_instructions" text NULL,
  ADD COLUMN IF NOT EXISTS "follow_up_date" date NULL,
  ADD COLUMN IF NOT EXISTS "warning_signs" text NULL;

-- ============================================================
-- 4. Pharmacy & Clinical Integrity Constraints
-- ============================================================
ALTER TABLE "medicine_batches"
  ALTER COLUMN "quantity_on_hand" SET NOT NULL;

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_medicine_batches_quantity_non_negative') THEN
    ALTER TABLE "medicine_batches"
      ADD CONSTRAINT "chk_medicine_batches_quantity_non_negative"
      CHECK ("quantity_on_hand" >= 0);
  END IF;
END $$;

CREATE UNIQUE INDEX IF NOT EXISTS "uq_medication_dispenses_dispensed_prescription"
  ON "medication_dispenses" ("prescription_id")
  WHERE "prescription_id" IS NOT NULL AND upper(btrim("status")) IN ('DISPENSED', 'COMPLETED');

CREATE UNIQUE INDEX IF NOT EXISTS "uq_prescriptions_one_draft_per_visit"
  ON "prescriptions" ("visit_id")
  WHERE "visit_id" IS NOT NULL AND upper(btrim("status")) = 'DRAFT';

CREATE UNIQUE INDEX IF NOT EXISTS "uq_medical_records_visit_id"
  ON "medical_records" ("visit_id")
  WHERE "visit_id" IS NOT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS "uq_medical_record_revisions_record_revision_no"
  ON "medical_record_revisions" ("medical_record_id", "revision_no");

CREATE UNIQUE INDEX IF NOT EXISTS "uq_diagnostic_results_order_id"
  ON "diagnostic_results" ("diagnostic_order_id");

CREATE UNIQUE INDEX IF NOT EXISTS "uq_diagnostic_result_versions_result_version_no"
  ON "diagnostic_result_versions" ("diagnostic_result_id", "version_no");

COMMIT;
