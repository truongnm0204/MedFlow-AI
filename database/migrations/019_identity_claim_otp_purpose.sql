BEGIN;

ALTER TABLE auth_verification_challenges
  DROP CONSTRAINT IF EXISTS ck_avc_purpose;

ALTER TABLE auth_verification_challenges
  ADD CONSTRAINT ck_avc_purpose
  CHECK (purpose IN ('REGISTER_EMAIL', 'CHANGE_EMAIL', 'CHANGE_PHONE', 'PASSWORD_RESET', 'IDENTITY_CLAIM'));

COMMIT;
