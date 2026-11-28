-- Migration 031: eKYC signed-evidence capture on proxy call logs (Phase 1 slice 3 — trust anchor).
--
-- Probe Q3 established: every VNPT AI response carries dataSign + dataBase64, and there is
-- NO server-side re-query API — the backend MUST capture the signed material at proxy time,
-- or finalize (slice 3) has nothing to verify. Slice 2 captured metadata only; this migration
-- adds the signed-evidence columns finalize needs:
--
--   response_data_sign        — RSA signature (base64) over the dataBase64 string. NOT secret,
--                               NOT an image — cryptographic proof material.
--   response_payload_encrypted — the dataBase64 string encrypted at rest with AES-256-GCM
--                               (EkycCrypto, key from env — same construction as
--                               evidence_payload_full on ekyc_verification_sessions).
--                               The plaintext is OCR personal data (name/id/dob) — personal
--                               data, but never images: dataBase64 decodes to JSON text only.
--
-- Why encrypted-at-rest even though the payload is textual OCR data: Luật 91/2025 personal
-- data protection — CCCD fields are sensitive personal data; at-rest encryption is the
-- slice-1-established pattern for exactly this material.
--
-- 100% idempotent. Never edits rows of other tables.

BEGIN;

SET LOCAL lock_timeout = '10s';

ALTER TABLE "ekyc_proxy_call_logs"
  ADD COLUMN IF NOT EXISTS "response_data_sign" text NULL,
  ADD COLUMN IF NOT EXISTS "response_payload_encrypted" bytea NULL;

COMMIT;
