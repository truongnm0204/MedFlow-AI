-- Read-only preflight audit before/after applying 027_queue_enrollment_intents_pharmacy_and_single_active_index.sql.
-- Zero writes. Safe to execute both BEFORE and AFTER Migration 027 (guards payment_enrollment_intents via to_regclass + query_to_xml).
-- Usage:
--   psql -h localhost -U postgres -d medflow_db -v ON_ERROR_STOP=1 -f database/scripts/preflight_027_audit.sql

WITH intent_xml AS (
  SELECT
    CASE
      WHEN to_regclass('public.payment_enrollment_intents') IS NOT NULL THEN
        query_to_xml(
          $q$
          SELECT
            COUNT(*)::bigint AS violation_count,
            coalesce(string_agg(pei_id::text, ', ' ORDER BY pei_id) FILTER (WHERE rn <= 10), '') AS sample_ids
          FROM (
            SELECT
              pei."id" AS pei_id,
              row_number() OVER (ORDER BY pei."id") AS rn
            FROM "payment_enrollment_intents" pei
            LEFT JOIN "payments" p
              ON p."id" = pei."payment_id"
             AND p."visit_id" = pei."visit_id"
            LEFT JOIN "diagnostic_orders" o
              ON o."id" = pei."diagnostic_order_id"
             AND o."visit_id" = pei."visit_id"
            LEFT JOIN "patients" pt
              ON pt."id" = pei."patient_id"
            LEFT JOIN "queue_entries" qe
              ON qe."id" = pei."queue_entry_id"
            WHERE pei."payment_id" IS NULL
               OR pei."visit_id" IS NULL
               OR pei."patient_id" IS NULL
               OR pei."diagnostic_order_id" IS NULL
               OR p."id" IS NULL
               OR o."id" IS NULL
               OR pt."id" IS NULL
               OR (pei."queue_entry_id" IS NOT NULL AND (qe."id" IS NULL OR qe."visit_id" IS DISTINCT FROM pei."visit_id"))
               OR pei."retry_count" < 0
               OR pei."status" NOT IN (
                 'PENDING_SETTLEMENT',
                 'PENDING_POST_COMMIT',
                 'CONSUMED_ENROLLED',
                 'SKIPPED_NOT_ELIGIBLE',
                 'CANCELLED',
                 'EXPIRED'
               )
          ) bad_intents
          $q$,
          true,
          false,
          ''
        )
      ELSE NULL
    END AS intent_doc
),
checks AS (
  SELECT
    'visits_multiple_active_post_exam_queues' AS check_name,
    COUNT(*)::bigint AS violation_count,
    coalesce(string_agg(visit_id::text, ', ' ORDER BY visit_id) FILTER (WHERE rn <= 10), '') AS sample_ids
  FROM (
    SELECT
      qe."visit_id" AS visit_id,
      row_number() OVER (ORDER BY qe."visit_id") AS rn
    FROM "queue_entries" qe
    WHERE qe."visit_id" IS NOT NULL
      AND upper(btrim(coalesce(qe."queue_type", ''))) IN ('DIAGNOSTIC', 'RETURN_CONSULTATION', 'PHARMACY')
      AND upper(btrim(coalesce(qe."status", ''))) IN ('WAITING', 'READY', 'CALLED', 'IN_PROGRESS', 'IN_CONSULTATION', 'SERVING')
    GROUP BY qe."visit_id"
    HAVING COUNT(*) > 1
  ) dup_visit_queues

  UNION ALL

  SELECT
    'patients_multiple_active_serving_queues_same_hospital',
    COUNT(*)::bigint,
    coalesce(string_agg(sample_key, ', ' ORDER BY sample_key) FILTER (WHERE rn <= 10), '')
  FROM (
    SELECT
      (v."patient_id"::text || '@H' || coalesce(resolved_hosp.hospital_id::text, '0')) AS sample_key,
      row_number() OVER (ORDER BY v."patient_id", coalesce(resolved_hosp.hospital_id, 0)) AS rn
    FROM "queue_entries" qe
    JOIN "visits" v ON v."id" = qe."visit_id"
    LEFT JOIN "queue_service_points" sp
      ON sp."id" = coalesce(qe."current_service_point_id", qe."service_point_id")
    LEFT JOIN "hospital_rooms" rm
      ON rm."id" = coalesce(sp."room_id", v."examination_room_id")
    LEFT JOIN "doctors" doc
      ON doc."id" = v."assigned_doctor_id"
    LEFT JOIN "staff" stf
      ON stf."id" = v."checked_in_by_staff_id"
    CROSS JOIN LATERAL (
      SELECT coalesce(sp."hospital_id", rm."hospital_id", doc."hospital_id", stf."hospital_id") AS hospital_id
    ) resolved_hosp
    WHERE qe."visit_id" IS NOT NULL
      AND v."patient_id" IS NOT NULL
      AND upper(btrim(coalesce(qe."queue_type", ''))) IN ('EXAMINATION', 'DIAGNOSTIC', 'RETURN_CONSULTATION', 'PHARMACY')
      AND upper(btrim(coalesce(qe."status", ''))) IN ('WAITING', 'READY', 'CALLED', 'IN_PROGRESS', 'IN_CONSULTATION', 'SERVING')
    GROUP BY v."patient_id", resolved_hosp.hospital_id
    HAVING COUNT(*) > 1
  ) dup_patient_queues

  UNION ALL

  SELECT
    'diagnostic_orders_cross_visit_queue_entry',
    COUNT(*)::bigint,
    coalesce(string_agg(ord_id::text, ', ' ORDER BY ord_id) FILTER (WHERE rn <= 10), '')
  FROM (
    SELECT
      o."id" AS ord_id,
      row_number() OVER (ORDER BY o."id") AS rn
    FROM "diagnostic_orders" o
    LEFT JOIN "queue_entries" q ON q."id" = o."queue_entry_id"
    WHERE o."queue_entry_id" IS NOT NULL
      AND (q."id" IS NULL OR q."visit_id" IS DISTINCT FROM o."visit_id")
  ) bad_order_qe

  UNION ALL

  SELECT
    'clinical_staff_missing_department_id',
    COUNT(*)::bigint,
    coalesce(string_agg(staff_id::text, ', ' ORDER BY staff_id) FILTER (WHERE rn <= 10), '')
  FROM (
    SELECT
      s."id" AS staff_id,
      row_number() OVER (ORDER BY s."id") AS rn
    FROM (
      SELECT DISTINCT s_inner."id"
      FROM "staff" s_inner
      JOIN "user_roles" ur ON ur."user_id" = s_inner."user_id"
      JOIN "roles" r ON r."id" = ur."role_id"
      WHERE upper(btrim(coalesce(s_inner."status", ''))) = 'ACTIVE'
        AND upper(btrim(coalesce(r."code", ''))) IN ('DOCTOR', 'DIAGNOSTIC_STAFF', 'PHARMACY_STAFF')
        AND s_inner."department_id" IS NULL
    ) s
  ) missing_dept

  UNION ALL

  SELECT
    'prescriptions_null_visit_id',
    COUNT(*)::bigint,
    coalesce(string_agg(rx_id::text, ', ' ORDER BY rx_id) FILTER (WHERE rn <= 10), '')
  FROM (
    SELECT
      p."id" AS rx_id,
      row_number() OVER (ORDER BY p."id") AS rn
    FROM "prescriptions" p
    WHERE p."visit_id" IS NULL
  ) null_rx_visit

  UNION ALL

  SELECT
    'payment_enrollment_intents_orphan_or_invalid',
    CASE
      WHEN intent_doc IS NULL THEN 0::bigint
      ELSE coalesce(((xpath('/table/row/violation_count/text()', intent_doc))[1])::text::bigint, 0::bigint)
    END,
    CASE
      WHEN intent_doc IS NULL THEN ''
      ELSE coalesce(((xpath('/table/row/sample_ids/text()', intent_doc))[1])::text, '')
    END
  FROM intent_xml
)
SELECT
  check_name,
  violation_count,
  coalesce(sample_ids, '') AS sample_ids
FROM checks
ORDER BY violation_count DESC, check_name ASC;
