-- Read-only preflight audit before/after applying 029_ekyc_verification_sessions.sql.
-- Zero writes. Safe both BEFORE and AFTER Migration 029 (guards columns via information_schema).
-- Usage:
--   psql -h localhost -U postgres -d medflow_db -v ON_ERROR_STOP=1 -f database/scripts/preflight_029_audit.sql

DO $$
DECLARE
  v_has_mode boolean := false;
  v_has_purpose boolean := false;
  v_has_performed_by boolean := false;
BEGIN
  IF to_regclass('public.ekyc_verification_sessions') IS NULL THEN
    RAISE NOTICE 'ekyc_verification_sessions not present yet — pre-migration state.';
    RETURN;
  END IF;

  SELECT EXISTS (SELECT 1 FROM information_schema.columns
    WHERE table_name = 'ekyc_verification_sessions' AND column_name = 'verification_mode') INTO v_has_mode;
  SELECT EXISTS (SELECT 1 FROM information_schema.columns
    WHERE table_name = 'ekyc_verification_sessions' AND column_name = 'purpose') INTO v_has_purpose;
  SELECT EXISTS (SELECT 1 FROM information_schema.columns
    WHERE table_name = 'ekyc_verification_sessions' AND column_name = 'performed_by') INTO v_has_performed_by;

  RAISE NOTICE 'columns: verification_mode=%, purpose=%, performed_by=%',
    v_has_mode, v_has_purpose, v_has_performed_by;
END $$;

SELECT
  (SELECT COUNT(*) FROM information_schema.columns
    WHERE table_name = 'ekyc_verification_sessions'
      AND column_name IN ('verification_mode','purpose','performed_by',
                          'declared_full_name','declared_birth_day','declared_identification_hmac')) AS new_columns_present,

  -- Old discriminator must be gone after the owner-scope migration.
  (SELECT COUNT(*) FROM information_schema.columns
    WHERE table_name = 'ekyc_verification_sessions' AND column_name = 'verification_method') AS legacy_method_columns,

  (SELECT COUNT(*) FROM ekyc_verification_sessions
    WHERE status NOT IN ('INITIATED','DOCUMENT_FRONT_DONE','DOCUMENT_BACK_DONE','FACE_DONE','FINALIZED','VERIFIED','REJECTED','EXPIRED','CONSUMED')) AS bad_status_rows,

  (SELECT COUNT(*) FROM ekyc_verification_sessions
    WHERE verification_mode NOT IN ('SELF_SERVICE','RECEPTION_ASSISTED')) AS bad_mode_rows,

  (SELECT COUNT(*) FROM ekyc_verification_sessions
    WHERE purpose NOT IN ('ACCOUNT_ACTIVATION','PATIENT_PROFILE')) AS bad_purpose_rows,

  (SELECT COUNT(*) FROM ekyc_verification_sessions
    WHERE (verification_mode = 'SELF_SERVICE' AND performed_by IS NOT NULL)
       OR (verification_mode = 'RECEPTION_ASSISTED'
           AND (performed_by IS NULL OR performed_by = user_id))) AS bad_performed_by_rows,

  (SELECT COUNT(*) FROM ekyc_verification_sessions WHERE attempt_count < 0) AS negative_attempt_rows
;
