-- Read-only preflight audit before/after applying 029_ekyc_verification_sessions.sql.
-- Zero writes. Safe to execute both BEFORE and AFTER Migration 029 (guards the new table via to_regclass).
-- Usage:
--   psql -h localhost -U postgres -d medflow_db -v ON_ERROR_STOP=1 -f database/scripts/preflight_029_audit.sql

WITH ekyc_xml AS (
  SELECT
    CASE
      WHEN to_regclass('public.ekyc_verification_sessions') IS NOT NULL THEN
        query_to_xml(
          $q$
          SELECT
            COUNT(*)::bigint AS violation_count,
            coalesce(string_agg(id::text, ', ' ORDER BY id) FILTER (WHERE rn <= 10), '') AS sample_ids
          FROM (
            SELECT
              s."id" AS id,
              row_number() OVER (ORDER BY s."id") AS rn
            FROM "ekyc_verification_sessions" s
            LEFT JOIN "users" u ON u."id" = s."user_id"
            LEFT JOIN "patients" p ON p."id" = s."patient_id"
            LEFT JOIN "users" v ON v."id" = s."verified_by"
            WHERE (s."user_id" IS NOT NULL AND u."id" IS NULL)
               OR (s."patient_id" IS NOT NULL AND p."id" IS NULL)
               OR (s."verified_by" IS NOT NULL AND v."id" IS NULL)
               OR s."status" NOT IN ('INITIATED','DOCUMENT_FRONT_DONE','DOCUMENT_BACK_DONE','FACE_DONE','FINALIZED','VERIFIED','REJECTED','EXPIRED','CONSUMED')
               OR s."verification_method" NOT IN ('EKYC_VNPT','RECEPTION_MANUAL')
               OR (s."verification_method" = 'RECEPTION_MANUAL' AND s."verified_by" IS NULL)
               OR (s."verification_method" = 'EKYC_VNPT' AND s."verified_by" IS NOT NULL)
               OR (s."verified_by" IS NOT NULL AND s."user_id" IS NOT NULL AND s."verified_by" = s."user_id")
               OR s."attempt_count" < 0
               OR (s."identification_hmac" IS NOT NULL AND length(s."identification_hmac") > 128)
            ORDER BY s."id"
            LIMIT 10
          ) violations
          $q$,
          true, false, '')
      ELSE NULL
    END AS row_violations
)
SELECT * FROM ekyc_xml;
