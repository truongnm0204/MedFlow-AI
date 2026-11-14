-- Backfill HMAC cho du lieu cu. Chay thu cong trong cua so bao tri, sau migration 015.
-- Yeu cau: pgcrypto; dat key truoc khi chay:
--   psql -v ON_ERROR_STOP=1 -c "SET app.identity_hash_key = '<key>'" -f database/scripts/backfill_identification_hmac.sql
-- Gia tri = HMAC-SHA256(key, hex(SHA256(cccd))) = hmac(identification_hash, key, 'sha256').
-- Khong luu key trong file/migration.

CREATE EXTENSION IF NOT EXISTS pgcrypto;

DO $$
DECLARE
  hash_key TEXT := current_setting('app.identity_hash_key', true);
BEGIN
  IF hash_key IS NULL OR length(hash_key) = 0 THEN
    RAISE EXCEPTION 'app.identity_hash_key chua duoc dat';
  END IF;
END $$;

UPDATE patients
SET identification_hash_hmac = encode(hmac(identification_hash, current_setting('app.identity_hash_key'), 'sha256'), 'hex'),
    hash_version = 1
WHERE identification_hash IS NOT NULL
  AND identification_hash_hmac IS NULL;

-- Kiem tra ket qua
SELECT count(*) AS missing_hmac
FROM patients
WHERE identification_hash IS NOT NULL
  AND identification_hash_hmac IS NULL;
