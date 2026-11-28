-- Read-only preflight audit before/after applying 032_patients_identity_status.sql.
-- Zero writes. Safe both BEFORE and AFTER Migration 032.
-- Usage:
--   psql -h localhost -U postgres -d medflow_db -v ON_ERROR_STOP=1 -f database/scripts/preflight_032_audit.sql

SELECT
  (SELECT COUNT(*) FROM information_schema.columns
    WHERE table_name = 'patients'
      AND column_name IN ('identity_status','unverified_reason','identity_note')) AS identity_columns_present,

  (SELECT COUNT(*) FROM patients
    WHERE identity_status NOT IN ('VERIFIED','UNVERIFIED')) AS bad_identity_status_rows,

  (SELECT COUNT(*) FROM patients
    WHERE identity_status = 'VERIFIED' AND unverified_reason IS NOT NULL) AS verified_with_reason_rows,

  (SELECT COUNT(*) FROM patients
    WHERE unverified_reason IS NOT NULL
      AND unverified_reason NOT IN ('CHILD','FOREIGNER','NO_VALID_DOCUMENT','CONSENT_DECLINED','VNPT_UNAVAILABLE','QUOTA_EXHAUSTED')) AS bad_reason_rows,

  (SELECT COUNT(*) FROM patients WHERE identity_status = 'VERIFIED') AS verified_rows,
  (SELECT COUNT(*) FROM patients WHERE identity_status = 'UNVERIFIED') AS unverified_rows,

  -- Every VERIFIED profile must have verified-identity evidence (HMAC written by finalize).
  (SELECT COUNT(*) FROM patients
    WHERE identity_status = 'VERIFIED' AND identification_hash_hmac IS NULL) AS verified_without_hmac_rows
;
