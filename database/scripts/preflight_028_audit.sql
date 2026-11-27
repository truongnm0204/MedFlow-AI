-- Read-only preflight audit before/after applying 028_clinical_session_recovery_authority.sql.
-- Zero writes. Safe to execute both BEFORE and AFTER Migration 028 (guards the new table via to_regclass).
-- Usage:
--   psql -h localhost -U postgres -d medflow_db -v ON_ERROR_STOP=1 -f database/scripts/preflight_028_audit.sql

WITH recovery_xml AS (
  SELECT
    CASE
      WHEN to_regclass('public.clinical_session_recovery_requests') IS NOT NULL THEN
        query_to_xml(
          $q$
          SELECT
            COUNT(*)::bigint AS violation_count,
            coalesce(string_agg(id::text, ', ' ORDER BY id) FILTER (WHERE rn <= 10), '') AS sample_ids
          FROM (
            SELECT
              r."id" AS id,
              row_number() OVER (ORDER BY r."id") AS rn
            FROM "clinical_session_recovery_requests" r
            LEFT JOIN "visits" v ON v."id" = r."visit_id"
            LEFT JOIN "queue_entries" qe ON qe."id" = r."queue_entry_id"
            LEFT JOIN "hospitals" h ON h."id" = r."hospital_id"
            WHERE v."id" IS NULL
               OR qe."id" IS NULL
               OR qe."visit_id" IS DISTINCT FROM r."visit_id"
               OR h."id" IS NULL
               OR r."status" NOT IN ('REQUESTED','GRANTED','REJECTED','CANCELLED','RECOVERED','REVOKED','EXPIRED')
               OR (r."status" = 'GRANTED' AND (
                     r."authorized_doctor_id" IS NULL OR r."granted_by_user_id" IS NULL
                     OR r."granted_by_doctor_id" IS NULL OR r."granted_at" IS NULL
                     OR r."expires_at" IS NULL OR btrim(coalesce(r."grant_reason", '')) = ''))
               OR (r."status" = 'RECOVERED' AND (
                     r."recovered_by_user_id" IS NULL OR r."recovered_by_doctor_id" IS NULL
                     OR r."recovered_at" IS NULL OR btrim(coalesce(r."recovery_reason", '')) = ''
                     OR (r."own_session" = false AND r."authorized_doctor_id" IS NULL)))
               OR (r."status" = 'REVOKED' AND (
                     r."revoked_by_user_id" IS NULL OR r."revoked_at" IS NULL
                     OR btrim(coalesce(r."revocation_reason", '')) = ''))
               OR (r."status" = 'REJECTED' AND (
                     r."rejected_by_user_id" IS NULL OR r."rejected_at" IS NULL
                     OR btrim(coalesce(r."rejection_reason", '')) = ''))
               OR (r."status" = 'CANCELLED' AND (
                     r."cancelled_by_user_id" IS NULL OR r."cancelled_at" IS NULL
                     OR btrim(coalesce(r."cancellation_reason", '')) = ''))
               OR (r."status" = 'EXPIRED' AND r."expired_at" IS NULL)
               OR (r."granted_by_doctor_id" IS NOT NULL AND r."authorized_doctor_id" IS NOT NULL
                   AND r."granted_by_doctor_id" = r."authorized_doctor_id")
               OR r."expected_queue_state_version" IS NULL
               OR r."expected_queue_state_version" < 1
               OR (r."idempotency_key" IS NOT NULL AND (
                     r."idempotency_actor_user_id" IS NULL OR r."idempotency_operation" IS NULL))
            ORDER BY r."id"
          ) bad_recovery_requests
          $q$,
          true,
          false,
          ''
        )
      ELSE NULL
    END AS recovery_doc
),
unresolved_xml AS (
  SELECT
    CASE
      WHEN to_regclass('public.clinical_session_recovery_requests') IS NOT NULL THEN
        query_to_xml(
          $q$
          SELECT
            COUNT(*)::bigint AS violation_count,
            coalesce(string_agg(queue_entry_id::text, ', ' ORDER BY queue_entry_id) FILTER (WHERE rn <= 10), '') AS sample_ids
          FROM (
            SELECT
              r."queue_entry_id" AS queue_entry_id,
              row_number() OVER (ORDER BY r."queue_entry_id") AS rn
            FROM "clinical_session_recovery_requests" r
            WHERE r."status" IN ('REQUESTED','GRANTED')
            GROUP BY r."queue_entry_id"
            HAVING COUNT(*) > 1
          ) duplicates
          $q$,
          true,
          false,
          ''
        )
      ELSE NULL
    END AS unresolved_doc
),
permission_checks AS (
  SELECT
    'permissions_catalog_present' AS check_name,
    2 - COUNT(*)::bigint AS violation_count,
    ''::text AS sample_ids
  FROM "permissions"
  WHERE "code" IN ('CLINICAL_SESSION_RECOVERY_GRANT', 'CLINICAL_SESSION_RECOVER')
),
checks AS (
  SELECT 'recovery_request_status_metadata_scope' AS check_name,
         coalesce(((xpath('/table/row/violation_count/text()', recovery_doc))[1])::text::bigint, 0::bigint) AS violation_count,
         coalesce(((xpath('/table/row/sample_ids/text()', recovery_doc))[1])::text, '') AS sample_ids
  FROM recovery_xml
  UNION ALL
  SELECT 'one_unresolved_recovery_per_queue_entry',
         coalesce(((xpath('/table/row/violation_count/text()', unresolved_doc))[1])::text::bigint, 0::bigint),
         coalesce(((xpath('/table/row/sample_ids/text()', unresolved_doc))[1])::text, '')
  FROM unresolved_xml
  UNION ALL
  SELECT check_name, violation_count, sample_ids
  FROM permission_checks
)
SELECT
  check_name,
  violation_count,
  sample_ids,
  CASE WHEN violation_count = 0 THEN 'PASS' ELSE 'FAIL' END AS status
FROM checks
ORDER BY check_name;
