BEGIN;

DO $$
DECLARE
  duplicate_rows TEXT;
BEGIN
  SELECT string_agg(user_id::text || ':' || cnt::text, ', ')
  INTO duplicate_rows
  FROM (
    SELECT user_id, count(*) AS cnt
    FROM patients
    WHERE user_id IS NOT NULL
    GROUP BY user_id
    HAVING count(*) > 1
  ) d;

  IF duplicate_rows IS NOT NULL THEN
    RAISE EXCEPTION 'Duplicate patients.user_id: %', duplicate_rows;
  END IF;
END $$;

CREATE UNIQUE INDEX IF NOT EXISTS uq_patients_user_id
  ON patients (user_id)
  WHERE user_id IS NOT NULL;

COMMIT;
