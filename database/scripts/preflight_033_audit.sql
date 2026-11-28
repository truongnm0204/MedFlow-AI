-- Read-only preflight audit before/after applying 033_reception_patient_identity_verify_grant.sql.
-- Zero writes. Safe both BEFORE and AFTER Migration 033.
-- Usage:
--   psql -h localhost -U postgres -d medflow_db -v ON_ERROR_STOP=1 -f database/scripts/preflight_033_audit.sql

SELECT
  (SELECT COUNT(*) FROM roles WHERE code = 'RECEPTION') AS reception_role_present,
  (SELECT COUNT(*) FROM permissions WHERE code = 'PATIENT_IDENTITY_VERIFY') AS permission_present,

  -- AFTER 033 this must be 0 (grant exists). BEFORE 033 it is 1 — expected, not an error.
  CASE WHEN EXISTS (
    SELECT 1
    FROM role_permissions rp
    JOIN roles r ON r.id = rp.role_id
    JOIN permissions p ON p.id = rp.permission_id
    WHERE r.code = 'RECEPTION' AND p.code = 'PATIENT_IDENTITY_VERIFY'
  ) THEN 0 ELSE 1 END AS reception_grant_missing_rows,

  -- role_permissions has a composite PK, so duplicates are structurally impossible; keep the
  -- check for imported/legacy databases whose constraint was dropped.
  (SELECT COUNT(*)
   FROM (
     SELECT rp.role_id, rp.permission_id
     FROM role_permissions rp
     JOIN roles r ON r.id = rp.role_id
     JOIN permissions p ON p.id = rp.permission_id
     WHERE r.code = 'RECEPTION' AND p.code = 'PATIENT_IDENTITY_VERIFY'
     GROUP BY rp.role_id, rp.permission_id
     HAVING COUNT(*) > 1
   ) duplicates) AS reception_grant_duplicate_rows,

  -- The owner decision names the seeded role RECEPTION; a RECEPTIONIST role would mean the
  -- grant silently landed on nothing.
  (SELECT COUNT(*) FROM roles WHERE code = 'RECEPTIONIST') AS receptionist_role_rows
;
