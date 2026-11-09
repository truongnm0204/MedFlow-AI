-- Migration 010: add queue_entries early-arrival safe-backfill columns and default QUEUE policies
-- Reason: UC28 / M2-044 (#96 Handle Early Arrival / Safe Backfill) persists backfill timestamp and reason
--   and seeds default expected_duration_minutes (15) and safety_buffer_minutes (5) per hospital.
-- Rule reference: docs/decisions/0014-early-arrival-safe-backfill-contract.md

BEGIN;

ALTER TABLE "queue_entries"
    ADD COLUMN IF NOT EXISTS "backfilled_at" timestamp without time zone,
    ADD COLUMN IF NOT EXISTS "backfill_reason" text;

SELECT setval(pg_get_serial_sequence('"hospital_policies"', 'id'),
              (SELECT COALESCE(MAX("id"), 0) FROM "hospital_policies") + 1, false);

INSERT INTO "hospital_policies" ("hospital_id", "queue_service_point_id", "policy_type", "policy_key", "policy_value", "policy_version", "effective_from", "effective_to")
SELECT h."id", NULL, 'QUEUE', v.policy_key, v.policy_value::jsonb, 1, '2026-10-01 00:00:00', NULL
FROM "hospitals" h
CROSS JOIN (VALUES ('expected_duration_minutes', '{"minutes":15}'),
                   ('safety_buffer_minutes',     '{"minutes":5}')) AS v(policy_key, policy_value)
WHERE NOT EXISTS (SELECT 1 FROM "hospital_policies" p
                  WHERE p."hospital_id" = h."id" AND p."queue_service_point_id" IS NULL
                    AND p."policy_type" = 'QUEUE' AND p."policy_key" = v.policy_key);

COMMIT;
