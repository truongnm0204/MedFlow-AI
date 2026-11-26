-- Read-only preflight audit before/after applying 026_clinical_revalidation_overdue_approval_and_review_purpose.sql.
-- Zero writes. Safe to execute both BEFORE and AFTER Migration 026 (guards optional 026 columns via information_schema + query_to_xml).
-- Usage:
--   psql -h localhost -U postgres -d medflow_db -v ON_ERROR_STOP=1 -f database/scripts/preflight_026_audit.sql

WITH schema_flags AS (
  SELECT
    EXISTS (
      SELECT 1
      FROM information_schema.columns
      WHERE table_schema = 'public'
        AND table_name = 'queue_entries'
        AND column_name = 'coordination_purpose'
    ) AS has_coordination_purpose,
    EXISTS (
      SELECT 1
      FROM information_schema.columns
      WHERE table_schema = 'public'
        AND table_name = 'diagnostic_orders'
        AND column_name = 'revalidated_by_doctor_id'
    ) AS has_revalidated_by_doctor,
    EXISTS (
      SELECT 1
      FROM information_schema.columns
      WHERE table_schema = 'public'
        AND table_name = 'diagnostic_orders'
        AND column_name = 'validity_extended_until'
    ) AS has_validity_extended_until,
    EXISTS (
      SELECT 1
      FROM information_schema.columns
      WHERE table_schema = 'public'
        AND table_name = 'diagnostic_orders'
        AND column_name = 'revalidation_note'
    ) AS has_revalidation_note,
    EXISTS (
      SELECT 1
      FROM information_schema.columns
      WHERE table_schema = 'public'
        AND table_name = 'visits'
        AND column_name = 'overdue_return_approved_by_doctor_id'
    ) AS has_overdue_approved_by_doctor
),
dynamic_xml AS (
  SELECT
    CASE
      WHEN sf.has_coordination_purpose THEN
        query_to_xml(
          $q$
          SELECT
            COUNT(*)::bigint AS violation_count,
            coalesce(string_agg(qe_id::text, ', ' ORDER BY qe_id) FILTER (WHERE rn <= 10), '') AS sample_ids
          FROM (
            SELECT
              qe."id" AS qe_id,
              row_number() OVER (ORDER BY qe."id") AS rn
            FROM "queue_entries" qe
            WHERE qe."coordination_purpose" IS NOT NULL
              AND qe."coordination_purpose" NOT IN ('RESULT_CONCLUSION', 'CLINICAL_REVIEW')
          ) bad_purpose
          $q$,
          true,
          false,
          ''
        )
      ELSE NULL
    END AS purpose_doc,
    CASE
      WHEN sf.has_revalidated_by_doctor THEN
        query_to_xml(
          $q$
          SELECT
            COUNT(*)::bigint AS violation_count,
            coalesce(string_agg(ord_id::text, ', ' ORDER BY ord_id) FILTER (WHERE rn <= 10), '') AS sample_ids
          FROM (
            SELECT
              o."id" AS ord_id,
              row_number() OVER (ORDER BY o."id") AS rn
            FROM "diagnostic_orders" o
            LEFT JOIN "doctors" d ON d."id" = o."revalidated_by_doctor_id"
            WHERE o."revalidated_by_doctor_id" IS NOT NULL
              AND d."id" IS NULL
          ) orphan_reval
          $q$,
          true,
          false,
          ''
        )
      ELSE NULL
    END AS orphan_reval_doc,
    CASE
      WHEN sf.has_overdue_approved_by_doctor THEN
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
            LEFT JOIN "doctors" d ON d."id" = v."overdue_return_approved_by_doctor_id"
            WHERE v."overdue_return_approved_by_doctor_id" IS NOT NULL
              AND d."id" IS NULL
          ) orphan_overdue
          $q$,
          true,
          false,
          ''
        )
      ELSE NULL
    END AS orphan_overdue_doc,
    CASE
      WHEN sf.has_revalidated_by_doctor AND sf.has_validity_extended_until AND sf.has_revalidation_note THEN
        query_to_xml(
          $q$
          SELECT
            COUNT(*)::bigint AS violation_count,
            coalesce(string_agg(ord_id::text, ', ' ORDER BY ord_id) FILTER (WHERE rn <= 10), '') AS sample_ids
          FROM (
            SELECT
              o."id" AS ord_id,
              row_number() OVER (ORDER BY o."id") AS rn
            FROM "diagnostic_orders" o
            WHERE (o."revalidated_by_doctor_id" IS NOT NULL OR o."validity_extended_until" IS NOT NULL)
              AND (
                o."revalidated_by_doctor_id" IS NULL
                OR o."validity_extended_until" IS NULL
                OR btrim(coalesce(o."revalidation_note", '')) = ''
              )
          ) incomplete_reval
          $q$,
          true,
          false,
          ''
        )
      ELSE NULL
    END AS incomplete_reval_doc
  FROM schema_flags sf
),
checks AS (
  SELECT
    'queue_entries_invalid_coordination_purpose' AS check_name,
    CASE
      WHEN purpose_doc IS NULL THEN 0::bigint
      ELSE coalesce(((xpath('/table/row/violation_count/text()', purpose_doc))[1])::text::bigint, 0::bigint)
    END AS violation_count,
    CASE
      WHEN purpose_doc IS NULL THEN ''
      ELSE coalesce(((xpath('/table/row/sample_ids/text()', purpose_doc))[1])::text, '')
    END AS sample_ids
  FROM dynamic_xml

  UNION ALL

  SELECT
    'diagnostic_orders_orphan_revalidated_by_doctor',
    CASE
      WHEN orphan_reval_doc IS NULL THEN 0::bigint
      ELSE coalesce(((xpath('/table/row/violation_count/text()', orphan_reval_doc))[1])::text::bigint, 0::bigint)
    END,
    CASE
      WHEN orphan_reval_doc IS NULL THEN ''
      ELSE coalesce(((xpath('/table/row/sample_ids/text()', orphan_reval_doc))[1])::text, '')
    END
  FROM dynamic_xml

  UNION ALL

  SELECT
    'visits_orphan_overdue_approved_by_doctor',
    CASE
      WHEN orphan_overdue_doc IS NULL THEN 0::bigint
      ELSE coalesce(((xpath('/table/row/violation_count/text()', orphan_overdue_doc))[1])::text::bigint, 0::bigint)
    END,
    CASE
      WHEN orphan_overdue_doc IS NULL THEN ''
      ELSE coalesce(((xpath('/table/row/sample_ids/text()', orphan_overdue_doc))[1])::text, '')
    END
  FROM dynamic_xml

  UNION ALL

  SELECT
    'diagnostic_orders_incomplete_revalidation_metadata',
    CASE
      WHEN incomplete_reval_doc IS NULL THEN 0::bigint
      ELSE coalesce(((xpath('/table/row/violation_count/text()', incomplete_reval_doc))[1])::text::bigint, 0::bigint)
    END,
    CASE
      WHEN incomplete_reval_doc IS NULL THEN ''
      ELSE coalesce(((xpath('/table/row/sample_ids/text()', incomplete_reval_doc))[1])::text, '')
    END
  FROM dynamic_xml
)
SELECT
  check_name,
  violation_count,
  sample_ids
FROM checks
ORDER BY check_name;
