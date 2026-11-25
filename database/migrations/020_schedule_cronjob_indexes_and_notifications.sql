BEGIN;

-- Pre-flight data integrity verification before creating unique indexes.
DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM "notifications"
        WHERE "idempotency_key" IS NOT NULL
        GROUP BY "idempotency_key"
        HAVING COUNT(*) > 1
    ) THEN
        RAISE EXCEPTION 'Migration 020 blocked: duplicate idempotency_key detected in notifications.';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM "examination_slots"
        WHERE "doctor_id" IS NOT NULL
          AND COALESCE(UPPER("status"), '') <> 'CLOSED'
        GROUP BY "doctor_id", "starts_at"
        HAVING COUNT(*) > 1
    ) THEN
        RAISE EXCEPTION 'Migration 020 blocked: duplicate active (doctor_id, starts_at) detected in examination_slots.';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM "examination_slots"
        WHERE "room_id" IS NOT NULL
          AND COALESCE(UPPER("status"), '') <> 'CLOSED'
        GROUP BY "room_id", "starts_at"
        HAVING COUNT(*) > 1
    ) THEN
        RAISE EXCEPTION 'Migration 020 blocked: duplicate active (room_id, starts_at) detected in examination_slots.';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM "examination_slots"
        WHERE "doctor_id" IS NOT NULL
          AND "service_id" IS NOT NULL
        GROUP BY "doctor_id", "service_id", "starts_at"
        HAVING COUNT(*) > 1
    ) THEN
        RAISE EXCEPTION 'Migration 020 blocked: duplicate (doctor_id, service_id, starts_at) detected in examination_slots.';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM "examination_slots"
        WHERE "doctor_id" IS NULL
          AND "service_id" IS NOT NULL
          AND "room_id" IS NOT NULL
        GROUP BY "service_id", "room_id", "starts_at"
        HAVING COUNT(*) > 1
    ) THEN
        RAISE EXCEPTION 'Migration 020 blocked: duplicate unassigned (service_id, room_id, starts_at) detected in examination_slots.';
    END IF;
END $$;

-- Extend notifications for UC17 in-app read tracking and at-least-once outbox lease ownership.
-- Foreign keys fk_notifications_patient_id / appointment_id / visit_id remain NO ACTION.
ALTER TABLE "notifications"
    ADD COLUMN IF NOT EXISTS "read_at" timestamp without time zone NULL,
    ADD COLUMN IF NOT EXISTS "notification_type" varchar(40) NULL,
    ADD COLUMN IF NOT EXISTS "processing_token" varchar(64) NULL,
    ADD COLUMN IF NOT EXISTS "lease_until" timestamp without time zone NULL;

UPDATE "notifications"
SET "notification_type" = CASE
    WHEN "subject" ILIKE 'Nhắc lịch%' THEN 'APPOINTMENT_REMINDER'
    ELSE 'APPOINTMENT_BOOKED'
END
WHERE "notification_type" IS NULL;

-- Partial & Unique Indexes for Notifications, Appointments, and Examination Slots
CREATE INDEX IF NOT EXISTS "ix_notifications_pending_due"
    ON "notifications" ("scheduled_at")
    WHERE "status" = 'PENDING';

CREATE INDEX IF NOT EXISTS "ix_notifications_processing_lease"
    ON "notifications" ("lease_until")
    WHERE "status" = 'PROCESSING';

CREATE UNIQUE INDEX IF NOT EXISTS "uq_notifications_idempotency_key"
    ON "notifications" ("idempotency_key")
    WHERE "idempotency_key" IS NOT NULL;

CREATE INDEX IF NOT EXISTS "ix_notifications_patient_unread"
    ON "notifications" ("patient_id", "created_at" DESC)
    WHERE "read_at" IS NULL;

CREATE INDEX IF NOT EXISTS "ix_appointments_active_slot"
    ON "appointments" ("slot_id")
    WHERE "status" IN ('BOOKED', 'CONFIRMED');

CREATE INDEX IF NOT EXISTS "ix_examination_slots_time_window"
    ON "examination_slots" ("starts_at", "ends_at");

CREATE UNIQUE INDEX IF NOT EXISTS "uq_examination_slots_active_doctor_start"
    ON "examination_slots" ("doctor_id", "starts_at")
    WHERE "doctor_id" IS NOT NULL AND "status" <> 'CLOSED';

CREATE UNIQUE INDEX IF NOT EXISTS "uq_examination_slots_active_room_start"
    ON "examination_slots" ("room_id", "starts_at")
    WHERE "room_id" IS NOT NULL AND "status" <> 'CLOSED';

CREATE UNIQUE INDEX IF NOT EXISTS "uq_examination_slots_doctor_service_start"
    ON "examination_slots" ("doctor_id", "service_id", "starts_at")
    WHERE "doctor_id" IS NOT NULL AND "service_id" IS NOT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS "uq_examination_slots_unassigned_service_room_start"
    ON "examination_slots" ("service_id", "room_id", "starts_at")
    WHERE "doctor_id" IS NULL AND "service_id" IS NOT NULL AND "room_id" IS NOT NULL;

COMMIT;
