-- Read-only preflight audit before/after applying 030_ekyc_proxy_call_logs.sql.
-- Zero writes. Safe both BEFORE and AFTER Migration 030 (guards new objects via to_regclass/pg_attribute).
-- Usage:
--   psql -h localhost -U postgres -d medflow_db -v ON_ERROR_STOP=1 -f database/scripts/preflight_030_audit.sql

WITH log_xml AS (
  SELECT
    CASE
      WHEN to_regclass('public.ekyc_proxy_call_logs') IS NOT NULL THEN
        query_to_xml(
          $q$
          SELECT
            COUNT(*)::bigint AS violation_count,
            coalesce(string_agg(id::text, ', ' ORDER BY id) FILTER (WHERE rn <= 10), '') AS sample_ids
          FROM (
            SELECT
              l."id" AS id,
              row_number() OVER (ORDER BY l."id") AS rn
            FROM "ekyc_proxy_call_logs" l
            LEFT JOIN "ekyc_verification_sessions" s ON s."id" = l."session_id"
            WHERE s."id" IS NULL
               OR l."http_status" < 100 OR l."http_status" > 599
               OR length(l."endpoint_path") > 256
            ORDER BY l."id"
            LIMIT 10
          ) violations
          $q$,
          true, false, '')
      ELSE NULL
    END AS log_violations
),
binding_xml AS (
  SELECT
    CASE
      WHEN to_regclass('public.ekyc_verification_sessions') IS NOT NULL
           AND EXISTS (
             SELECT 1 FROM pg_attribute
             WHERE attrelid = 'public.ekyc_verification_sessions'::regclass
               AND attname = 'binding_state' AND NOT attisdropped)
      THEN
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
            WHERE s."binding_state" NOT IN ('PENDING','VALID','INVALID_BINDING','UNVERIFIABLE_BINDING')
            ORDER BY s."id"
            LIMIT 10
          ) violations
          $q$,
          true, false, '')
      ELSE NULL
    END AS binding_violations
)
SELECT * FROM log_xml, binding_xml;
