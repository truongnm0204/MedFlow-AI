-- Read-only preflight audit before/after applying 031_ekyc_signed_evidence_capture.sql.
-- Zero writes. Safe both BEFORE and AFTER Migration 031 (guards new objects via pg_attribute).
-- Usage:
--   psql -h localhost -U postgres -d medflow_db -v ON_ERROR_STOP=1 -f database/scripts/preflight_031_audit.sql

SELECT
  -- Column presence check: both capture columns must exist after migration.
  (SELECT COUNT(*) FROM information_schema.columns
    WHERE table_name = 'ekyc_proxy_call_logs'
      AND column_name IN ('response_data_sign', 'response_payload_encrypted')) AS capture_columns_present,

  -- Integrity spot-check: rows carrying a dataSign must also carry the encrypted payload
  -- (capture is atomic — a sign without payload means partial capture bug).
  (SELECT COUNT(*) FROM ekyc_proxy_call_logs
    WHERE response_data_sign IS NOT NULL AND response_payload_encrypted IS NULL) AS sign_without_payload_rows,

  -- Reverse is legitimate (encryption-key-missing fallback documented in slice 3) but visible.
  (SELECT COUNT(*) FROM ekyc_proxy_call_logs
    WHERE response_data_sign IS NULL AND response_payload_encrypted IS NOT NULL) AS payload_without_sign_rows
;
