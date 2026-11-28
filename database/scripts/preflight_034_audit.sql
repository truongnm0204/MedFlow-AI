-- Read-only preflight audit for 034_medicine_prices.sql.
-- Zero writes. BEFORE 034 the table is absent (all counts 0); AFTER 034 every violating
-- count must be 0.
-- Usage:
--   psql -h localhost -U postgres -d medflow_db -v ON_ERROR_STOP=1 -f database/scripts/preflight_034_audit.sql

WITH table_present AS (
  SELECT to_regclass('public.medicine_prices') IS NOT NULL AS present
)
SELECT
  (SELECT present FROM table_present) AS medicine_prices_table_present,

  CASE WHEN (SELECT present FROM table_present) THEN
    (SELECT COUNT(*) FROM medicine_prices WHERE price_amount < 0)
  ELSE 0 END AS negative_price_rows,

  CASE WHEN (SELECT present FROM table_present) THEN
    (SELECT COUNT(*) FROM medicine_prices
      WHERE effective_to IS NOT NULL AND effective_to <= effective_from)
  ELSE 0 END AS invalid_effective_range_rows,

  CASE WHEN (SELECT present FROM table_present) THEN
    (SELECT COUNT(*)
       FROM medicine_prices p
       JOIN medicine_prices q
         ON p.medicine_id = q.medicine_id
        AND p.id < q.id
        AND p.effective_from < COALESCE(q.effective_to, 'infinity'::timestamp)
        AND COALESCE(p.effective_to, 'infinity'::timestamp) > q.effective_from)
  ELSE 0 END AS overlapping_effective_rows,

  CASE WHEN (SELECT present FROM table_present) THEN
    (SELECT COUNT(*)
       FROM medicine_prices p
       JOIN medicines m ON m.id = p.medicine_id
      WHERE m.hospital_id IS DISTINCT FROM p.hospital_id)
  ELSE 0 END AS cross_hospital_rows
;
