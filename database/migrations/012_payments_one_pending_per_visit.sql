BEGIN;

DO $$
BEGIN
  IF EXISTS (
    SELECT 1
    FROM payments
    WHERE visit_id IS NOT NULL
      AND upper(btrim(status)) = 'PENDING'
    GROUP BY visit_id
    HAVING COUNT(*) > 1
  ) THEN
    RAISE EXCEPTION 'duplicate PENDING payments exist';
  END IF;
END $$;

CREATE UNIQUE INDEX IF NOT EXISTS ux_payments_one_pending_per_visit
  ON payments (visit_id)
  WHERE visit_id IS NOT NULL AND upper(btrim(status)) = 'PENDING';

COMMIT;
