BEGIN;

ALTER TABLE patient_family_links
  ADD COLUMN IF NOT EXISTS verification_method VARCHAR(16);
ALTER TABLE patient_family_links
  ADD COLUMN IF NOT EXISTS verified_by_staff_id BIGINT REFERENCES staff (id);
ALTER TABLE patient_family_links
  ADD COLUMN IF NOT EXISTS legacy_access_until TIMESTAMP WITHOUT TIME ZONE;

-- Link VERIFIED cu khong co bang chung xac minh doc lap: giu ban ghi, danh dau LEGACY.
UPDATE patient_family_links
SET verification_method = 'LEGACY',
    legacy_access_until = (now() AT TIME ZONE 'Asia/Ho_Chi_Minh') + INTERVAL '30 days'
WHERE upper(coalesce(verification_status, '')) = 'VERIFIED'
  AND verification_method IS NULL;

COMMIT;
