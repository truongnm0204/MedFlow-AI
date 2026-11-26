-- Migration 024: Grouped & Single Payment Allocations, Selective CLS, Reception Cash Collection & Cross-Day Visit Foundations (ADR-0060 / Issue #851)
-- Idempotent when re-executed against an already-migrated v4.1 + 002..023 database.
-- NEVER auto-deletes or auto-clamps historical financial rows; aborts with descriptive EXCEPTION on preflight violations.

BEGIN;

SET LOCAL lock_timeout = '10s';

-- ============================================================================
-- 0. FAIL-FAST PREFLIGHT AUDIT (zero auto-delete, zero auto-clamp)
-- ============================================================================
DO $$
DECLARE
  v_bad_count bigint;
  v_samples text;
BEGIN
  -- 0.1. payments: null visit_id or null/negative amount
  SELECT COUNT(*), string_agg(id::text, ', ' ORDER BY id)
    INTO v_bad_count, v_samples
  FROM (
    SELECT "id" AS id
    FROM "payments"
    WHERE "visit_id" IS NULL OR "amount" IS NULL OR "amount" < 0
    ORDER BY "id"
    LIMIT 10
  ) s;
  IF v_bad_count > 0 THEN
    RAISE EXCEPTION '[PREFLIGHT_024_FAILED] payments has null visit_id or negative amount (% rows; sample ids: %). Run database/scripts/preflight_024_audit.sql.',
      v_bad_count, coalesce(v_samples, '');
  END IF;

  -- 0.2. payments: visit_charge_id IS NOT NULL must match (visit_charge_id, visit_id) in visit_charges
  SELECT COUNT(*), string_agg(p_id::text, ', ' ORDER BY p_id)
    INTO v_bad_count, v_samples
  FROM (
    SELECT p."id" AS p_id
    FROM "payments" p
    LEFT JOIN "visit_charges" vc
      ON vc."id" = p."visit_charge_id"
     AND vc."visit_id" = p."visit_id"
    WHERE p."visit_charge_id" IS NOT NULL
      AND vc."id" IS NULL
    ORDER BY p."id"
    LIMIT 10
  ) s;
  IF v_bad_count > 0 THEN
    RAISE EXCEPTION '[PREFLIGHT_024_FAILED] payments has visit_charge_id missing or mismatched with visit_charges.visit_id (% rows; sample payment ids: %).',
      v_bad_count, coalesce(v_samples, '');
  END IF;

  -- 0.3. payments: > 1 PAID or > 1 PENDING per visit_charge_id
  SELECT COUNT(*), string_agg(visit_charge_id::text, ', ' ORDER BY visit_charge_id)
    INTO v_bad_count, v_samples
  FROM (
    SELECT "visit_charge_id" AS visit_charge_id
    FROM "payments"
    WHERE "visit_charge_id" IS NOT NULL
    GROUP BY "visit_charge_id"
    HAVING COUNT(*) FILTER (WHERE upper(btrim(coalesce("status", ''))) = 'PAID') > 1
        OR COUNT(*) FILTER (WHERE upper(btrim(coalesce("status", ''))) = 'PENDING') > 1
    ORDER BY "visit_charge_id"
    LIMIT 10
  ) s;
  IF v_bad_count > 0 THEN
    RAISE EXCEPTION '[PREFLIGHT_024_FAILED] payments has duplicate PAID or duplicate PENDING rows per visit_charge_id (% charges; sample charge ids: %).',
      v_bad_count, coalesce(v_samples, '');
  END IF;

  -- 0.4. If payment_allocations already exists (idempotent re-run), verify cross-visit integrity and per-charge uniqueness
  IF to_regclass('public.payment_allocations') IS NOT NULL THEN
    EXECUTE $dyn$
      SELECT COUNT(*), string_agg(pa_id::text, ', ' ORDER BY pa_id)
      FROM (
        SELECT pa."id" AS pa_id
        FROM "payment_allocations" pa
        LEFT JOIN "payments" p ON p."id" = pa."payment_id" AND p."visit_id" = pa."visit_id"
        LEFT JOIN "visit_charges" vc ON vc."id" = pa."visit_charge_id" AND vc."visit_id" = pa."visit_id"
        WHERE pa."visit_id" IS NULL
           OR pa."allocated_amount" IS NULL
           OR pa."allocated_amount" < 0
           OR p."id" IS NULL
           OR vc."id" IS NULL
        ORDER BY pa."id"
        LIMIT 10
      ) s
    $dyn$ INTO v_bad_count, v_samples;

    IF v_bad_count > 0 THEN
      RAISE EXCEPTION '[PREFLIGHT_024_FAILED] payment_allocations has cross-visit mismatch or negative allocated_amount (% rows; sample allocation ids: %).',
        v_bad_count, coalesce(v_samples, '');
    END IF;

    EXECUTE $dyn$
      SELECT COUNT(*), string_agg(visit_charge_id::text, ', ' ORDER BY visit_charge_id)
      FROM (
        SELECT pa."visit_charge_id" AS visit_charge_id
        FROM "payment_allocations" pa
        GROUP BY pa."visit_charge_id"
        HAVING COUNT(*) FILTER (WHERE upper(btrim(coalesce(pa."status", ''))) = 'PAID') > 1
            OR COUNT(*) FILTER (WHERE upper(btrim(coalesce(pa."status", ''))) = 'PENDING') > 1
        ORDER BY pa."visit_charge_id"
        LIMIT 10
      ) s
    $dyn$ INTO v_bad_count, v_samples;

    IF v_bad_count > 0 THEN
      RAISE EXCEPTION '[PREFLIGHT_024_FAILED] payment_allocations has duplicate PAID or duplicate PENDING rows per visit_charge_id (% charges; sample charge ids: %).',
        v_bad_count, coalesce(v_samples, '');
    END IF;
  END IF;
END $$;

-- ============================================================================
-- 1. payments: composite unique key, cash/method/idempotency columns & ALLOCATED scope
-- ============================================================================
DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'uq_payments_id_visit_id') THEN
    ALTER TABLE "payments"
      ADD CONSTRAINT "uq_payments_id_visit_id" UNIQUE ("id", "visit_id");
  END IF;
END $$;

ALTER TABLE "payments"
  ADD COLUMN IF NOT EXISTS "payment_method" varchar(16) NULL,
  ADD COLUMN IF NOT EXISTS "collected_by_staff_id" bigint NULL,
  ADD COLUMN IF NOT EXISTS "idempotency_key" varchar(100) NULL;

ALTER TABLE "payments"
  ALTER COLUMN "payment_scope" SET DEFAULT 'CHARGE';

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_payments_collected_by_staff_id') THEN
    ALTER TABLE "payments"
      ADD CONSTRAINT "fk_payments_collected_by_staff_id"
      FOREIGN KEY ("collected_by_staff_id") REFERENCES "staff" ("id");
  END IF;

  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_payments_method') THEN
    ALTER TABLE "payments"
      ADD CONSTRAINT "chk_payments_method"
      CHECK ("payment_method" IS NULL OR "payment_method" IN ('VIETQR', 'CASH'));
  END IF;

  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_payments_cash_requirements') THEN
    ALTER TABLE "payments"
      ADD CONSTRAINT "chk_payments_cash_requirements"
      CHECK ("payment_method" IS DISTINCT FROM 'CASH' OR ("collected_by_staff_id" IS NOT NULL AND "idempotency_key" IS NOT NULL));
  END IF;
END $$;

ALTER TABLE "payments"
  DROP CONSTRAINT IF EXISTS "chk_payments_scope_charge_consistency";

ALTER TABLE "payments"
  ADD CONSTRAINT "chk_payments_scope_charge_consistency" CHECK (
    ("payment_scope" = 'CHARGE' AND "visit_charge_id" IS NOT NULL AND "visit_id" IS NOT NULL)
    OR ("payment_scope" = 'ALLOCATED' AND "visit_id" IS NOT NULL)
    OR ("payment_scope" = 'LEGACY_VISIT' AND "visit_charge_id" IS NULL AND "visit_id" IS NOT NULL)
  );

CREATE INDEX IF NOT EXISTS "ix_payments_collected_by_staff_id"
  ON "payments" ("collected_by_staff_id");

CREATE UNIQUE INDEX IF NOT EXISTS "ux_payments_idempotency_key"
  ON "payments" ("idempotency_key")
  WHERE "idempotency_key" IS NOT NULL;

-- Scope ux_payments_one_pending_legacy_visit strictly to LEGACY_VISIT so disjoint grouped ALLOCATED QRs
-- (where visit_charge_id IS NULL) are governed per-charge by payment_allocations indexes instead.
DROP INDEX IF EXISTS "ux_payments_one_pending_legacy_visit";

CREATE UNIQUE INDEX IF NOT EXISTS "ux_payments_one_pending_legacy_visit"
  ON "payments" ("visit_id")
  WHERE "visit_id" IS NOT NULL
    AND "visit_charge_id" IS NULL
    AND "payment_scope" = 'LEGACY_VISIT'
    AND upper(btrim("status")) = 'PENDING';

-- ============================================================================
-- 2. payment_allocations: canonical per-charge settlement table (Level 16b)
-- ============================================================================
CREATE TABLE IF NOT EXISTS "payment_allocations" (
  "id" bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
  "payment_id" bigint NOT NULL,
  "visit_charge_id" bigint NOT NULL,
  "visit_id" bigint NOT NULL,
  "allocated_amount" numeric(14,2) NOT NULL,
  "status" varchar(16) NOT NULL DEFAULT 'PENDING',
  "created_at" timestamp without time zone NOT NULL DEFAULT (now() AT TIME ZONE 'Asia/Ho_Chi_Minh')
);

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_payment_allocations_allocated_amount_nonneg') THEN
    ALTER TABLE "payment_allocations"
      ADD CONSTRAINT "chk_payment_allocations_allocated_amount_nonneg"
      CHECK ("allocated_amount" >= 0);
  END IF;

  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_payment_allocations_status') THEN
    ALTER TABLE "payment_allocations"
      ADD CONSTRAINT "chk_payment_allocations_status"
      CHECK ("status" IN ('PENDING', 'PAID', 'EXPIRED', 'CANCELLED', 'RECONCILE'));
  END IF;

  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'uq_payment_allocations_payment_charge') THEN
    ALTER TABLE "payment_allocations"
      ADD CONSTRAINT "uq_payment_allocations_payment_charge"
      UNIQUE ("payment_id", "visit_charge_id");
  END IF;

  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_payment_allocations_payment_visit') THEN
    ALTER TABLE "payment_allocations"
      ADD CONSTRAINT "fk_payment_allocations_payment_visit"
      FOREIGN KEY ("payment_id", "visit_id") REFERENCES "payments" ("id", "visit_id") ON DELETE CASCADE;
  END IF;

  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_payment_allocations_charge_visit') THEN
    ALTER TABLE "payment_allocations"
      ADD CONSTRAINT "fk_payment_allocations_charge_visit"
      FOREIGN KEY ("visit_charge_id", "visit_id") REFERENCES "visit_charges" ("id", "visit_id") ON DELETE RESTRICT;
  END IF;
END $$;

CREATE INDEX IF NOT EXISTS "ix_payment_allocations_visit_id"
  ON "payment_allocations" ("visit_id");

CREATE INDEX IF NOT EXISTS "ix_payment_allocations_visit_charge_id"
  ON "payment_allocations" ("visit_charge_id");

CREATE UNIQUE INDEX IF NOT EXISTS "ux_payment_allocations_one_pending_per_charge"
  ON "payment_allocations" ("visit_charge_id")
  WHERE upper(btrim("status")) = 'PENDING';

CREATE UNIQUE INDEX IF NOT EXISTS "ux_payment_allocations_one_paid_per_charge"
  ON "payment_allocations" ("visit_charge_id")
  WHERE upper(btrim("status")) = 'PAID';

-- ============================================================================
-- 3. Deterministic 1-to-1 backfill ONLY for payments where visit_charge_id IS NOT NULL
--    (LEGACY_VISIT rows with visit_charge_id IS NULL remain 100% untouched with 0 allocations)
-- ============================================================================
INSERT INTO "payment_allocations" (
  "payment_id",
  "visit_charge_id",
  "visit_id",
  "allocated_amount",
  "status",
  "created_at"
)
SELECT
  p."id",
  p."visit_charge_id",
  p."visit_id",
  p."amount"::numeric(14,2),
  CASE upper(btrim(coalesce(p."status", '')))
    WHEN 'PAID' THEN 'PAID'
    WHEN 'PENDING' THEN 'PENDING'
    WHEN 'EXPIRED' THEN 'EXPIRED'
    WHEN 'CANCELLED' THEN 'CANCELLED'
    ELSE 'RECONCILE'
  END,
  coalesce(p."created_at", (now() AT TIME ZONE 'Asia/Ho_Chi_Minh'))
FROM "payments" p
WHERE p."visit_charge_id" IS NOT NULL
ON CONFLICT ("payment_id", "visit_charge_id") DO NOTHING;

-- ============================================================================
-- 4. visit_charges: add PAID_NOT_PERFORMED to chk_visit_charges_status
-- ============================================================================
ALTER TABLE "visit_charges"
  DROP CONSTRAINT IF EXISTS "chk_visit_charges_status";

ALTER TABLE "visit_charges"
  ADD CONSTRAINT "chk_visit_charges_status"
  CHECK ("status" IN ('ACTIVE', 'CANCELLED', 'PAID_NOT_PERFORMED', 'CANCELLED_REFUND_PENDING'));

-- ============================================================================
-- 5. diagnostic_orders & visits: selective CLS + cross-day visit columns
-- ============================================================================
ALTER TABLE "diagnostic_orders"
  ADD COLUMN IF NOT EXISTS "selection_status" varchar(24) NOT NULL DEFAULT 'ACCEPTED',
  ADD COLUMN IF NOT EXISTS "deferred_until_date" date NULL;

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_diagnostic_orders_selection_status') THEN
    ALTER TABLE "diagnostic_orders"
      ADD CONSTRAINT "chk_diagnostic_orders_selection_status"
      CHECK ("selection_status" IN ('UNDECIDED', 'ACCEPTED', 'DECLINED', 'REVIEW_REQUIRED', 'SKIPPED_AFTER_PAID'));
  END IF;
END $$;

ALTER TABLE "visits"
  ADD COLUMN IF NOT EXISTS "ready_for_return_at" timestamp without time zone NULL;

-- ============================================================================
-- 6. permissions & dev/seed role_permissions for PAYMENT_COLLECT
-- ============================================================================
INSERT INTO "permissions" ("code")
SELECT 'PAYMENT_COLLECT'
WHERE NOT EXISTS (SELECT 1 FROM "permissions" WHERE "code" = 'PAYMENT_COLLECT');

-- Grant PAYMENT_COLLECT to RECEPTION and HOSPITAL_ADMIN ONLY when dev/test seed accounts exist
-- so Production databases never auto-grant financial permissions without explicit admin approval (UC58).
INSERT INTO "role_permissions" ("role_id", "permission_id")
SELECT r."id", p."id"
FROM "roles" r
CROSS JOIN "permissions" p
WHERE p."code" = 'PAYMENT_COLLECT'
  AND r."code" IN ('RECEPTION', 'HOSPITAL_ADMIN')
  AND EXISTS (SELECT 1 FROM "users" WHERE "email" = 'reception01@medflow.test')
ON CONFLICT DO NOTHING;

COMMIT;
