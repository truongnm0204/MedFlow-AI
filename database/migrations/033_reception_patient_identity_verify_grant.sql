BEGIN;

-- Owner decision 2026-11-28 (eKYC Phase 1 slice 6): reception staff run the VNPT eKYC engine at
-- the counter (self-service assistance + patient-profile verification). This grants the existing
-- PATIENT_IDENTITY_VERIFY permission to the seeded RECEPTION role — it does NOT introduce a
-- manual VERIFIED path: patient identity_status is still written only by a successful eKYC
-- finalize (EkycVerificationService.TryMarkPatientVerifiedAsync).
INSERT INTO role_permissions (role_id, permission_id)
SELECT r.id, p.id
FROM roles r
CROSS JOIN permissions p
WHERE p.code = 'PATIENT_IDENTITY_VERIFY'
  AND r.code = 'RECEPTION'
ON CONFLICT DO NOTHING;

COMMIT;
