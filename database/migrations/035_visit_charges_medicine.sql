-- Migration 035: MEDICINE visit charges (Issue #866 / S3b, BF-03 CL-4/CL-5, owner decision D1).
--
-- Owner decision D1: the hospital pharmacy counter collects medicine money (Cash / VietQR).
-- A monthly medicine price is snapshot from medicine_prices (#865) when the charge is created;
-- the price kept in visit_charges.medicine_unit_price is NEVER re-read from the price table, so
-- historical bills and refunds stay correct after a price change.
--
-- Charge model (mirrors the DIAGNOSTIC model: one order = one charge line):
--   * one MEDICINE charge per prescription_items row (never per prescription total), so a
--     refund/cancel of one drug never touches the other lines;
--   * unit_price  = rounded line total (medicine_unit_price * quantity), the amount the existing
--     payment allocation/deallocation logic already uses;
--   * medicine_unit_price = effective per-unit price snapshot (numeric(14,2));
--   * quantity = prescribed quantity snapshot (numeric(14,3), same scale as prescription_items).
--
-- Idempotent: guarded ADD COLUMN / ADD CONSTRAINT / CREATE INDEX; safe to re-run.
-- ROLLBACK:
--   ALTER TABLE "visit_charges" DROP CONSTRAINT IF EXISTS "chk_visit_charges_type_consistency";
--   ALTER TABLE "visit_charges"
--     ADD CONSTRAINT "chk_visit_charges_type_consistency"
--     CHECK (("charge_type" = 'EXAMINATION' AND "diagnostic_order_id" IS NULL)
--         OR ("charge_type" = 'DIAGNOSTIC' AND "diagnostic_order_id" IS NOT NULL));
--   ALTER TABLE "visit_charges" DROP CONSTRAINT IF EXISTS "fk_visit_charges_medicine";
--   ALTER TABLE "visit_charges" DROP CONSTRAINT IF EXISTS "fk_visit_charges_prescription_item";
--   ALTER TABLE "visit_charges" DROP CONSTRAINT IF EXISTS "fk_visit_charges_prescription";
--   ALTER TABLE "visit_charges" DROP CONSTRAINT IF EXISTS "chk_visit_charges_medicine_quantity";
--   ALTER TABLE "visit_charges" DROP CONSTRAINT IF EXISTS "chk_visit_charges_medicine_unit_price";
--   DROP INDEX IF EXISTS "uq_visit_charges_prescription_item";
--   DROP INDEX IF EXISTS "ix_visit_charges_prescription_id";
--   DROP INDEX IF EXISTS "ix_visit_charges_medicine_id";
--   DROP INDEX IF EXISTS "ix_visit_charges_visit_type";
--   ALTER TABLE "visit_charges"
--     DROP COLUMN IF EXISTS "medicine_unit_price",
--     DROP COLUMN IF EXISTS "quantity",
--     DROP COLUMN IF EXISTS "medicine_id",
--     DROP COLUMN IF EXISTS "prescription_item_id",
--     DROP COLUMN IF EXISTS "prescription_id";
--   DELETE FROM "role_permissions" rp
--   USING "roles" r, "permissions" p
--   WHERE rp."role_id" = r."id" AND rp."permission_id" = p."id"
--     AND r."code" = 'PHARMACY_STAFF' AND p."code" = 'PAYMENT_COLLECT';

BEGIN;

SET LOCAL lock_timeout = '10s';

-- ============================================================================
-- 1. Link columns
-- ============================================================================
ALTER TABLE "visit_charges"
  ADD COLUMN IF NOT EXISTS "prescription_id" bigint NULL,
  ADD COLUMN IF NOT EXISTS "prescription_item_id" bigint NULL,
  ADD COLUMN IF NOT EXISTS "medicine_id" bigint NULL,
  ADD COLUMN IF NOT EXISTS "quantity" numeric(14,3) NULL,
  ADD COLUMN IF NOT EXISTS "medicine_unit_price" numeric(14,2) NULL;

-- ============================================================================
-- 2. Foreign keys + medicine charge invariants
-- ============================================================================
DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_visit_charges_prescription'
                   AND conrelid = '"visit_charges"'::regclass) THEN
    ALTER TABLE "visit_charges"
      ADD CONSTRAINT "fk_visit_charges_prescription"
      FOREIGN KEY ("prescription_id") REFERENCES "prescriptions" ("id") ON DELETE RESTRICT;
  END IF;

  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_visit_charges_prescription_item'
                   AND conrelid = '"visit_charges"'::regclass) THEN
    ALTER TABLE "visit_charges"
      ADD CONSTRAINT "fk_visit_charges_prescription_item"
      FOREIGN KEY ("prescription_item_id") REFERENCES "prescription_items" ("id") ON DELETE RESTRICT;
  END IF;

  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_visit_charges_medicine'
                   AND conrelid = '"visit_charges"'::regclass) THEN
    ALTER TABLE "visit_charges"
      ADD CONSTRAINT "fk_visit_charges_medicine"
      FOREIGN KEY ("medicine_id") REFERENCES "medicines" ("id") ON DELETE RESTRICT;
  END IF;

  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_visit_charges_medicine_quantity'
                   AND conrelid = '"visit_charges"'::regclass) THEN
    ALTER TABLE "visit_charges"
      ADD CONSTRAINT "chk_visit_charges_medicine_quantity"
      CHECK ("quantity" IS NULL OR "quantity" > 0);
  END IF;

  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_visit_charges_medicine_unit_price'
                   AND conrelid = '"visit_charges"'::regclass) THEN
    ALTER TABLE "visit_charges"
      ADD CONSTRAINT "chk_visit_charges_medicine_unit_price"
      CHECK ("medicine_unit_price" IS NULL OR "medicine_unit_price" >= 0);
  END IF;
END $$;

-- ============================================================================
-- 3. Charge-type consistency (extend migration 023's EXAMINATION/DIAGNOSTIC guard)
-- ============================================================================
ALTER TABLE "visit_charges" DROP CONSTRAINT IF EXISTS "chk_visit_charges_type_consistency";
ALTER TABLE "visit_charges"
  ADD CONSTRAINT "chk_visit_charges_type_consistency"
  CHECK (
    ("charge_type" = 'EXAMINATION' AND "diagnostic_order_id" IS NULL)
    OR ("charge_type" = 'DIAGNOSTIC' AND "diagnostic_order_id" IS NOT NULL)
    OR (
      "charge_type" = 'MEDICINE'
      AND "diagnostic_order_id" IS NULL
      AND "prescription_id" IS NOT NULL
      AND "prescription_item_id" IS NOT NULL
      AND "medicine_id" IS NOT NULL
      AND "quantity" IS NOT NULL
      AND "medicine_unit_price" IS NOT NULL
    )
  );

-- ============================================================================
-- 4. Indexes
-- ============================================================================
-- One charge per prescription line (idempotent charge materialisation).
CREATE UNIQUE INDEX IF NOT EXISTS "uq_visit_charges_prescription_item"
  ON "visit_charges" ("prescription_item_id")
  WHERE "prescription_item_id" IS NOT NULL;

-- Pharmacy counter: list all medicine charges of a prescription / visit.
CREATE INDEX IF NOT EXISTS "ix_visit_charges_prescription_id"
  ON "visit_charges" ("prescription_id");

CREATE INDEX IF NOT EXISTS "ix_visit_charges_medicine_id"
  ON "visit_charges" ("medicine_id");

CREATE INDEX IF NOT EXISTS "ix_visit_charges_visit_type"
  ON "visit_charges" ("visit_id", "charge_type");

-- ============================================================================
-- 4. PAYMENT_COLLECT for PHARMACY_STAFF (dev/test seeds ONLY, mirrors migration 024 §6)
--    Production administrators must grant it explicitly (UC58) before the pharmacy
--    counter can collect Cash/QR.
-- ============================================================================
INSERT INTO "role_permissions" ("role_id", "permission_id")
SELECT r."id", p."id"
FROM "roles" r
CROSS JOIN "permissions" p
WHERE p."code" = 'PAYMENT_COLLECT'
  AND r."code" = 'PHARMACY_STAFF'
  AND EXISTS (SELECT 1 FROM "users" WHERE "email" = 'reception01@medflow.test')
ON CONFLICT DO NOTHING;

COMMIT;
