BEGIN;

ALTER TABLE patients
  ADD COLUMN IF NOT EXISTS emergency_contact_name VARCHAR(200) NULL,
  ADD COLUMN IF NOT EXISTS emergency_contact_relationship VARCHAR(60) NULL,
  ADD COLUMN IF NOT EXISTS emergency_contact_phone VARCHAR(30) NULL;

COMMIT;
