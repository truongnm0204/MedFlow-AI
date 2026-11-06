BEGIN;
-- RETURN_RESULT was a priority; it is now a queue class.
UPDATE "queue_entries"
SET "queue_type" = 'RETURN_CONSULTATION', "manual_priority" = 'NORMAL'
WHERE upper("manual_priority") = 'RETURN_RESULT'
  AND ("queue_type" IS NULL OR upper("queue_type") = 'EXAMINATION');
-- Uppercase surviving valid values, then collapse everything else (URGENT, EMERGENCY, HIGH, CRITICAL, RETURN_RESULT on non-exam rows, junk) to NORMAL.
UPDATE "queue_entries" SET "manual_priority" = upper("manual_priority")
WHERE "manual_priority" IS NOT NULL AND upper("manual_priority") IN ('NORMAL','ELDERLY','PREGNANT');
UPDATE "queue_entries" SET "manual_priority" = 'NORMAL'
WHERE "manual_priority" IS NOT NULL AND "manual_priority" NOT IN ('NORMAL','ELDERLY','PREGNANT');
ALTER TABLE "queue_entries" ADD CONSTRAINT "ck_queue_entries_manual_priority"
  CHECK ("manual_priority" IS NULL OR "manual_priority" IN ('NORMAL','ELDERLY','PREGNANT'));
COMMIT;
