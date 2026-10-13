-- Minimal auth fixtures for integration tests (medflow_test).
-- Only what the auth flow needs: the PATIENT role required by RegisterAsync.
-- Do NOT add demo data here; extend only when a test needs the row.

INSERT INTO medflow.roles (id, code, name, is_active)
VALUES ('7aa4841c-3327-477d-ab20-441e7fec7948', 'PATIENT', 'Patient', true);
