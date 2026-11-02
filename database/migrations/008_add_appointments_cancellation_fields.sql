-- Migration 008: add appointments cancellation columns
-- Reason: UC14 (Cancel Appointment) persists the patient-entered cancellation reason,
--   the cancellation timestamp (Vietnam local, naive) and the cancelling user.
-- Rule reference: docs/decisions/0006-cancel-appointment-contract.md

BEGIN;

ALTER TABLE "appointments"
    ADD COLUMN IF NOT EXISTS "cancellation_reason" text,
    ADD COLUMN IF NOT EXISTS "cancelled_at" timestamp without time zone,
    ADD COLUMN IF NOT EXISTS "cancelled_by_user_id" bigint;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'fk_appointments_cancelled_by_user_id'
    ) THEN
        ALTER TABLE "appointments"
            ADD CONSTRAINT "fk_appointments_cancelled_by_user_id"
            FOREIGN KEY ("cancelled_by_user_id") REFERENCES "users" ("id");
    END IF;
END $$;

COMMIT;
