-- Read-only preflight audit before applying 024_grouped_payment_allocations_selection_and_cross_day_visits.sql.
-- Zero writes. Safe to execute both BEFORE and AFTER Migration 024 (uses query_to_xml only when payment_allocations exists).
-- Usage:
--   psql -h localhost -U postgres -d medflow_db -v ON_ERROR_STOP=1 -f database/scripts/preflight_024_audit.sql

WITH allocation_xml AS (
  SELECT
    CASE
      WHEN to_regclass('public.payment_allocations') IS NOT NULL THEN
        query_to_xml(
          $q$
          SELECT
            COUNT(*)::bigint AS violation_count,
            coalesce(string_agg(pa_id::text, ', ' ORDER BY pa_id) FILTER (WHERE rn <= 10), '') AS sample_ids
          FROM (
            SELECT pa."id" AS pa_id, row_number() OVER (ORDER BY pa."id") AS rn
            FROM "payment_allocations" pa
            LEFT JOIN "payments" p ON p."id" = pa."payment_id" AND p."visit_id" = pa."visit_id"
            LEFT JOIN "visit_charges" vc ON vc."id" = pa."visit_charge_id" AND vc."visit_id" = pa."visit_id"
            WHERE pa."visit_id" IS NULL
               OR pa."allocated_amount" IS NULL
               OR pa."allocated_amount" < 0
               OR p."id" IS NULL
               OR vc."id" IS NULL
          ) mismatch_rows
          $q$,
          true,
          false,
          ''
        )
      ELSE NULL
    END AS mismatch_doc,
    CASE
      WHEN to_regclass('public.payment_allocations') IS NOT NULL THEN
        query_to_xml(
          $q$
          SELECT
            COUNT(*)::bigint AS violation_count,
            coalesce(string_agg(visit_charge_id::text, ', ' ORDER BY visit_charge_id) FILTER (WHERE rn <= 10), '') AS sample_ids
          FROM (
            SELECT
              pa."visit_charge_id" AS visit_charge_id,
              row_number() OVER (ORDER BY pa."visit_charge_id") AS rn
            FROM "payment_allocations" pa
            GROUP BY pa."visit_charge_id"
            HAVING COUNT(*) FILTER (WHERE upper(btrim(coalesce(pa."status", ''))) = 'PAID') > 1
                OR COUNT(*) FILTER (WHERE upper(btrim(coalesce(pa."status", ''))) = 'PENDING') > 1
          ) dup_rows
          $q$,
          true,
          false,
          ''
        )
      ELSE NULL
    END AS dup_doc
),
checks AS (
  SELECT
    'payments_null_visit_or_negative_amount' AS check_name,
    COUNT(*)::bigint AS violation_count,
    string_agg(id::text, ', ' ORDER BY id) FILTER (WHERE rn <= 10) AS sample_ids
  FROM (
    SELECT "id" AS id, row_number() OVER (ORDER BY "id") AS rn
    FROM "payments"
    WHERE "visit_id" IS NULL OR "amount" IS NULL OR "amount" < 0
  ) t

  UNION ALL

  SELECT
    'payments_cross_visit_or_missing_charge',
    COUNT(*)::bigint,
    string_agg(p_id::text, ', ' ORDER BY p_id) FILTER (WHERE rn <= 10)
  FROM (
    SELECT p."id" AS p_id, row_number() OVER (ORDER BY p."id") AS rn
    FROM "payments" p
    LEFT JOIN "visit_charges" vc
      ON vc."id" = p."visit_charge_id"
     AND vc."visit_id" = p."visit_id"
    WHERE p."visit_charge_id" IS NOT NULL
      AND vc."id" IS NULL
  ) t

  UNION ALL

  SELECT
    'payments_duplicate_paid_per_charge',
    COUNT(*)::bigint,
    string_agg(visit_charge_id::text, ', ' ORDER BY visit_charge_id) FILTER (WHERE rn <= 10)
  FROM (
    SELECT "visit_charge_id" AS visit_charge_id, row_number() OVER (ORDER BY "visit_charge_id") AS rn
    FROM "payments"
    WHERE "visit_charge_id" IS NOT NULL
      AND upper(btrim(coalesce("status", ''))) = 'PAID'
    GROUP BY "visit_charge_id"
    HAVING COUNT(*) > 1
  ) t

  UNION ALL

  SELECT
    'payments_duplicate_pending_per_charge',
    COUNT(*)::bigint,
    string_agg(visit_charge_id::text, ', ' ORDER BY visit_charge_id) FILTER (WHERE rn <= 10)
  FROM (
    SELECT "visit_charge_id" AS visit_charge_id, row_number() OVER (ORDER BY "visit_charge_id") AS rn
    FROM "payments"
    WHERE "visit_charge_id" IS NOT NULL
      AND upper(btrim(coalesce("status", ''))) = 'PENDING'
    GROUP BY "visit_charge_id"
    HAVING COUNT(*) > 1
  ) t

  UNION ALL

  SELECT
    'payment_allocations_cross_visit_or_negative_amount',
    CASE
      WHEN mismatch_doc IS NULL THEN 0::bigint
      ELSE coalesce(((xpath('/table/row/violation_count/text()', mismatch_doc))[1])::text::bigint, 0::bigint)
    END,
    CASE
      WHEN mismatch_doc IS NULL THEN ''
      ELSE coalesce(((xpath('/table/row/sample_ids/text()', mismatch_doc))[1])::text, '')
    END
  FROM allocation_xml

  UNION ALL

  SELECT
    'payment_allocations_duplicate_paid_or_pending_per_charge',
    CASE
      WHEN dup_doc IS NULL THEN 0::bigint
      ELSE coalesce(((xpath('/table/row/violation_count/text()', dup_doc))[1])::text::bigint, 0::bigint)
    END,
    CASE
      WHEN dup_doc IS NULL THEN ''
      ELSE coalesce(((xpath('/table/row/sample_ids/text()', dup_doc))[1])::text, '')
    END
  FROM allocation_xml
)
SELECT check_name, violation_count, coalesce(sample_ids, '') AS sample_ids
FROM checks
ORDER BY violation_count DESC, check_name ASC;
