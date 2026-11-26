-- Read-only preflight audit before applying 025_cross_day_visit_presence_and_resume_tracking.sql.
-- Zero writes. Safe to execute both BEFORE and AFTER Migration 025 (guards optional 025 columns via information_schema + query_to_xml).
-- Usage:
--   psql -h localhost -U postgres -d medflow_db -v ON_ERROR_STOP=1 -f database/scripts/preflight_025_audit.sql

WITH schema_flags AS (
  SELECT
    EXISTS (
      SELECT 1
      FROM information_schema.columns
      WHERE table_schema = 'public'
        AND table_name = 'visits'
        AND column_name = 'presence_state'
    ) AS has_visit_presence_state,
    EXISTS (
      SELECT 1
      FROM information_schema.columns
      WHERE table_schema = 'public'
        AND table_name = 'queue_entries'
        AND column_name = 'idempotency_key'
    ) AS has_queue_idempotency_key
),
dynamic_xml AS (
  SELECT
    CASE
      WHEN sf.has_visit_presence_state THEN
        query_to_xml(
          $q$
          SELECT
            COUNT(*)::bigint AS violation_count,
            coalesce(string_agg(v_id::text, ', ' ORDER BY v_id) FILTER (WHERE rn <= 10), '') AS sample_ids
          FROM (
            SELECT
              v."id" AS v_id,
              row_number() OVER (ORDER BY v."id") AS rn
            FROM "visits" v
            WHERE v."presence_state" IS NULL
               OR v."presence_state" NOT IN ('PRESENT', 'LEFT', 'DEFERRED')
          ) bad_presence
          $q$,
          true,
          false,
          ''
        )
      ELSE NULL
    END AS presence_doc,
    CASE
      WHEN sf.has_queue_idempotency_key THEN
        query_to_xml(
          $q$
          SELECT
            COUNT(*)::bigint AS violation_count,
            coalesce(string_agg(idem_key, ', ' ORDER BY idem_key) FILTER (WHERE rn <= 10), '') AS sample_ids
          FROM (
            SELECT
              qe."idempotency_key" AS idem_key,
              row_number() OVER (ORDER BY qe."idempotency_key") AS rn
            FROM "queue_entries" qe
            WHERE qe."idempotency_key" IS NOT NULL
            GROUP BY qe."idempotency_key"
            HAVING COUNT(*) > 1
          ) dup_idem
          $q$,
          true,
          false,
          ''
        )
      ELSE NULL
    END AS idem_doc
  FROM schema_flags sf
),
checks AS (
  SELECT
    'visits_invalid_presence_state' AS check_name,
    CASE
      WHEN presence_doc IS NULL THEN 0::bigint
      ELSE coalesce(((xpath('/table/row/violation_count/text()', presence_doc))[1])::text::bigint, 0::bigint)
    END AS violation_count,
    CASE
      WHEN presence_doc IS NULL THEN ''
      ELSE coalesce(((xpath('/table/row/sample_ids/text()', presence_doc))[1])::text, '')
    END AS sample_ids
  FROM dynamic_xml

  UNION ALL

  SELECT
    'queue_entries_duplicate_idempotency_key',
    CASE
      WHEN idem_doc IS NULL THEN 0::bigint
      ELSE coalesce(((xpath('/table/row/violation_count/text()', idem_doc))[1])::text::bigint, 0::bigint)
    END,
    CASE
      WHEN idem_doc IS NULL THEN ''
      ELSE coalesce(((xpath('/table/row/sample_ids/text()', idem_doc))[1])::text, '')
    END
  FROM dynamic_xml

  UNION ALL

  SELECT
    'queue_entries_duplicate_active_return_consultation_same_day',
    COUNT(*)::bigint,
    coalesce(string_agg(visit_id::text, ', ' ORDER BY visit_id) FILTER (WHERE rn <= 10), '')
  FROM (
    SELECT
      qe."visit_id" AS visit_id,
      row_number() OVER (ORDER BY qe."visit_id") AS rn
    FROM "queue_entries" qe
    WHERE qe."visit_id" IS NOT NULL
      AND qe."queue_date" IS NOT NULL
      AND upper(btrim(coalesce(qe."queue_type", ''))) = 'RETURN_CONSULTATION'
      AND qe."completed_at" IS NULL
      AND upper(btrim(coalesce(qe."status", ''))) NOT IN ('COMPLETED', 'DONE', 'CANCELLED', 'DEFERRED', 'NO_SHOW')
    GROUP BY qe."visit_id", qe."queue_date"
    HAVING COUNT(*) > 1
  ) dup_return

  UNION ALL

  SELECT
    'visits_simultaneous_in_progress_queues',
    COUNT(*)::bigint,
    coalesce(string_agg(visit_id::text, ', ' ORDER BY visit_id) FILTER (WHERE rn <= 10), '')
  FROM (
    SELECT
      qe."visit_id" AS visit_id,
      row_number() OVER (ORDER BY qe."visit_id") AS rn
    FROM "queue_entries" qe
    WHERE qe."visit_id" IS NOT NULL
      AND qe."completed_at" IS NULL
      AND upper(btrim(coalesce(qe."status", ''))) = 'IN_PROGRESS'
    GROUP BY qe."visit_id"
    HAVING COUNT(*) > 1
  ) dup_in_progress
)
SELECT check_name, violation_count, coalesce(sample_ids, '') AS sample_ids
FROM checks
ORDER BY violation_count DESC, check_name ASC;
