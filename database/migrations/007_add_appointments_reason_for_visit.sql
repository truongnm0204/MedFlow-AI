-- Migration 007: add appointments.reason_for_visit column
-- Reason: UC12 (Book Appointment) persists the optional symptom note (<= 500 chars)
--   submitted from the booking review screen onto the appointments table.
-- Rule reference: docs/decisions/0003-book-appointment-contract.md

BEGIN;

ALTER TABLE "appointments"
    ADD COLUMN IF NOT EXISTS "reason_for_visit" text;

COMMIT;
