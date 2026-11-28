-- Migration 032: patient identity status — Patient Identity Verification separated from
-- User Account Activation (eKYC Phase 1 slice 5, owner scope 2026-11-28).
--
-- Adds:
--   identity_status   VERIFIED | UNVERIFIED (NOT NULL, default UNVERIFIED)
--   unverified_reason one of the owner-approved UNVERIFIED creation reasons, NULL otherwise
--   identity_note     free-text operational note (optional)
--
-- Rationale:
--   * Only a successful eKYC finalize (document + card liveness + tampering clean + OCR matches
--     declared name/DOB/CCCD) may write VERIFIED. There is no API to set it manually.
--   * Reception may create a profile UNVERIFIED for the owner-approved exception cases; the
--     service layer requires a reason + audit and runs the dedupe matrix first.
--   * Existing rows are NOT treated as verified — pre-eKYC profiles default to UNVERIFIED.
--   * VERIFIED rows must not carry a reason (constraint); UNVERIFIED rows may carry NULL while
--     an eKYC session is still in progress.
--
-- 100% idempotent. Never edits rows of other tables.

BEGIN;

SET LOCAL lock_timeout = '10s';

-- ============================================================================
-- 0. FAIL-FAST PREFLIGHT (read-only; guards re-run consistency)
-- ============================================================================
DO $$
DECLARE
  v_bad_count bigint := 0;
  v_samples text := '';
  v_has_status boolean := false;
  v_has_reason boolean := false;
BEGIN
  IF to_regclass('public.patients') IS NOT NULL THEN
    SELECT EXISTS (
      SELECT 1 FROM information_schema.columns
      WHERE table_name = 'patients' AND column_name = 'identity_status'
    ) INTO v_has_status;
    SELECT EXISTS (
      SELECT 1 FROM information_schema.columns
      WHERE table_name = 'patients' AND column_name = 'unverified_reason'
    ) INTO v_has_reason;

    IF v_has_status THEN
      EXECUTE format($dyn$
        SELECT COUNT(*), coalesce(string_agg(id::text, ', ' ORDER BY id), '')
        FROM (
          SELECT id
          FROM patients
          WHERE identity_status NOT IN ('VERIFIED','UNVERIFIED')
             OR (identity_status = 'VERIFIED' AND %s IS NOT NULL)
             OR (%s IS NOT NULL AND %s NOT IN ('CHILD','FOREIGNER','NO_VALID_DOCUMENT','CONSENT_DECLINED','VNPT_UNAVAILABLE','QUOTA_EXHAUSTED'))
          ORDER BY id
          LIMIT 10
        ) s
      $dyn$,
        CASE WHEN v_has_reason THEN 'unverified_reason' ELSE 'NULL' END,
        CASE WHEN v_has_reason THEN 'unverified_reason' ELSE 'NULL' END,
        CASE WHEN v_has_reason THEN 'unverified_reason' ELSE 'NULL' END
      ) INTO v_bad_count, v_samples;

      IF v_bad_count > 0 THEN
        RAISE EXCEPTION '[PREFLIGHT_032_FAILED] patients has rows violating identity_status/unverified_reason invariants (% row(s); sample ids: %). Run database/scripts/preflight_032_audit.sql.',
          v_bad_count, v_samples;
      END IF;
    END IF;
  END IF;
END $$;

-- ============================================================================
-- 1. patients identity-status columns
-- ============================================================================
ALTER TABLE "patients"
  ADD COLUMN IF NOT EXISTS "identity_status" varchar(16) NOT NULL DEFAULT 'UNVERIFIED',
  ADD COLUMN IF NOT EXISTS "unverified_reason" varchar(32) NULL,
  ADD COLUMN IF NOT EXISTS "identity_note" text NULL;

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_patients_identity_status'
                   AND conrelid = '"patients"'::regclass) THEN
    ALTER TABLE "patients"
      ADD CONSTRAINT "chk_patients_identity_status"
      CHECK ("identity_status" IN ('VERIFIED','UNVERIFIED'));
  END IF;

  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_patients_unverified_reason'
                   AND conrelid = '"patients"'::regclass) THEN
    ALTER TABLE "patients"
      ADD CONSTRAINT "chk_patients_unverified_reason"
      CHECK (
        "unverified_reason" IS NULL
        OR ("identity_status" = 'UNVERIFIED'
            AND "unverified_reason" IN ('CHILD','FOREIGNER','NO_VALID_DOCUMENT','CONSENT_DECLINED','VNPT_UNAVAILABLE','QUOTA_EXHAUSTED'))
      );
  END IF;
END $$;

-- Reception queue: count/list profiles awaiting eKYC verification.
CREATE INDEX IF NOT EXISTS "ix_patients_identity_status"
  ON "patients" ("identity_status");

COMMIT;

-- ============================================================================
-- Notes (NOT executed here):
--   - No data backfill: existing profiles are UNVERIFIED by definition (no eKYC evidence).
--   - VERIFIED is service-write-once via eKYC finalize; no manual endpoint exists.
--   - Account activation remains users.status (PENDING → ACTIVE) and is gated separately by
--     the eKYC activation gate — patient identity status is NOT an authorization grant.
-- ============================================================================
