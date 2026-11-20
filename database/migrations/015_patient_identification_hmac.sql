BEGIN;

ALTER TABLE patients
  ADD COLUMN IF NOT EXISTS identification_hash_hmac VARCHAR(128);
ALTER TABLE patients
  ADD COLUMN IF NOT EXISTS hash_version SMALLINT;

CREATE UNIQUE INDEX IF NOT EXISTS uq_patients_identification_hash_hmac
  ON patients (identification_hash_hmac);

COMMIT;
