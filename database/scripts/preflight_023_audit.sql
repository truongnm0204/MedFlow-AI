-- Read-only preflight audit before applying 023_business_integrity_constraints_and_indexes.sql.
-- Zero writes. Returns one row per violation category with offending count and sample IDs.
-- Usage:
--   psql -h localhost -U postgres -d medflow_db -v ON_ERROR_STOP=1 -f database/scripts/preflight_023_audit.sql

WITH checks AS (
  SELECT
    'medicine_batches_null_or_negative_stock' AS check_name,
    COUNT(*)::bigint AS violation_count,
    string_agg(id::text, ', ' ORDER BY id) FILTER (WHERE rn <= 10) AS sample_ids
  FROM (
    SELECT id, row_number() OVER (ORDER BY id) AS rn
    FROM "medicine_batches"
    WHERE "quantity_on_hand" IS NULL OR "quantity_on_hand" < 0
  ) t

  UNION ALL

  SELECT
    'diagnostic_orders_null_visit_id',
    COUNT(*)::bigint,
    string_agg(id::text, ', ' ORDER BY id) FILTER (WHERE rn <= 10)
  FROM (
    SELECT id, row_number() OVER (ORDER BY id) AS rn
    FROM "diagnostic_orders"
    WHERE "visit_id" IS NULL
  ) t

  UNION ALL

  SELECT
    'visit_charges_null_visit_or_negative_price',
    COUNT(*)::bigint,
    string_agg(id::text, ', ' ORDER BY id) FILTER (WHERE rn <= 10)
  FROM (
    SELECT id, row_number() OVER (ORDER BY id) AS rn
    FROM "visit_charges"
    WHERE "visit_id" IS NULL OR ("unit_price" IS NOT NULL AND "unit_price" < 0)
  ) t

  UNION ALL

  SELECT
    'visit_charges_cross_visit_diagnostic_order',
    COUNT(*)::bigint,
    string_agg(vc_id::text, ', ' ORDER BY vc_id) FILTER (WHERE rn <= 10)
  FROM (
    SELECT vc."id" AS vc_id, row_number() OVER (ORDER BY vc."id") AS rn
    FROM "visit_charges" vc
    LEFT JOIN "diagnostic_orders" d ON d."id" = vc."diagnostic_order_id"
    WHERE vc."diagnostic_order_id" IS NOT NULL
      AND (d."id" IS NULL OR d."visit_id" IS DISTINCT FROM vc."visit_id")
  ) t

  UNION ALL

  SELECT
    'visit_charges_duplicate_exam_per_visit',
    COUNT(*)::bigint,
    string_agg(visit_id::text, ', ' ORDER BY visit_id) FILTER (WHERE rn <= 10)
  FROM (
    SELECT "visit_id" AS visit_id, row_number() OVER (ORDER BY "visit_id") AS rn
    FROM "visit_charges"
    WHERE "diagnostic_order_id" IS NULL
    GROUP BY "visit_id"
    HAVING COUNT(*) > 1
  ) t

  UNION ALL

  SELECT
    'visit_charges_duplicate_diagnostic_order',
    COUNT(*)::bigint,
    string_agg(diagnostic_order_id::text, ', ' ORDER BY diagnostic_order_id) FILTER (WHERE rn <= 10)
  FROM (
    SELECT "diagnostic_order_id" AS diagnostic_order_id, row_number() OVER (ORDER BY "diagnostic_order_id") AS rn
    FROM "visit_charges"
    WHERE "diagnostic_order_id" IS NOT NULL
    GROUP BY "diagnostic_order_id"
    HAVING COUNT(*) > 1
  ) t

  UNION ALL

  SELECT
    'payments_null_visit_id',
    COUNT(*)::bigint,
    string_agg(id::text, ', ' ORDER BY id) FILTER (WHERE rn <= 10)
  FROM (
    SELECT id, row_number() OVER (ORDER BY id) AS rn
    FROM "payments"
    WHERE "visit_id" IS NULL
  ) t

  UNION ALL

  SELECT
    'medication_dispenses_duplicate_completed_or_dispensed',
    COUNT(*)::bigint,
    string_agg(prescription_id::text, ', ' ORDER BY prescription_id) FILTER (WHERE rn <= 10)
  FROM (
    SELECT "prescription_id" AS prescription_id, row_number() OVER (ORDER BY "prescription_id") AS rn
    FROM "medication_dispenses"
    WHERE "prescription_id" IS NOT NULL
      AND upper(btrim(coalesce("status", ''))) IN ('DISPENSED', 'COMPLETED')
    GROUP BY "prescription_id"
    HAVING COUNT(*) > 1
  ) t

  UNION ALL

  SELECT
    'prescriptions_duplicate_draft_per_visit',
    COUNT(*)::bigint,
    string_agg(visit_id::text, ', ' ORDER BY visit_id) FILTER (WHERE rn <= 10)
  FROM (
    SELECT "visit_id" AS visit_id, row_number() OVER (ORDER BY "visit_id") AS rn
    FROM "prescriptions"
    WHERE "visit_id" IS NOT NULL
      AND upper(btrim(coalesce("status", ''))) = 'DRAFT'
    GROUP BY "visit_id"
    HAVING COUNT(*) > 1
  ) t

  UNION ALL

  SELECT
    'medical_records_duplicate_visit_id',
    COUNT(*)::bigint,
    string_agg(visit_id::text, ', ' ORDER BY visit_id) FILTER (WHERE rn <= 10)
  FROM (
    SELECT "visit_id" AS visit_id, row_number() OVER (ORDER BY "visit_id") AS rn
    FROM "medical_records"
    WHERE "visit_id" IS NOT NULL
    GROUP BY "visit_id"
    HAVING COUNT(*) > 1
  ) t

  UNION ALL

  SELECT
    'medical_record_revisions_duplicate_revision_no',
    COUNT(*)::bigint,
    string_agg(key_label, ', ' ORDER BY key_label) FILTER (WHERE rn <= 10)
  FROM (
    SELECT ("medical_record_id"::text || ':' || "revision_no"::text) AS key_label,
           row_number() OVER (ORDER BY "medical_record_id", "revision_no") AS rn
    FROM "medical_record_revisions"
    GROUP BY "medical_record_id", "revision_no"
    HAVING COUNT(*) > 1
  ) t

  UNION ALL

  SELECT
    'diagnostic_results_duplicate_order_id',
    COUNT(*)::bigint,
    string_agg(diagnostic_order_id::text, ', ' ORDER BY diagnostic_order_id) FILTER (WHERE rn <= 10)
  FROM (
    SELECT "diagnostic_order_id" AS diagnostic_order_id, row_number() OVER (ORDER BY "diagnostic_order_id") AS rn
    FROM "diagnostic_results"
    GROUP BY "diagnostic_order_id"
    HAVING COUNT(*) > 1
  ) t

  UNION ALL

  SELECT
    'diagnostic_result_versions_duplicate_version_no',
    COUNT(*)::bigint,
    string_agg(key_label, ', ' ORDER BY key_label) FILTER (WHERE rn <= 10)
  FROM (
    SELECT ("diagnostic_result_id"::text || ':' || "version_no"::text) AS key_label,
           row_number() OVER (ORDER BY "diagnostic_result_id", "version_no") AS rn
    FROM "diagnostic_result_versions"
    GROUP BY "diagnostic_result_id", "version_no"
    HAVING COUNT(*) > 1
  ) t
)
SELECT check_name, violation_count, coalesce(sample_ids, '') AS sample_ids
FROM checks
ORDER BY violation_count DESC, check_name ASC;
