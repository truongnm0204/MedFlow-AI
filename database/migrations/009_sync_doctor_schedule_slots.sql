BEGIN;

-- 1. Remove unbooked OPEN examination slots from 2026-11-01 onwards that do not match each doctor's duty schedule:
--    - Doctor 1 (DOC-001, Tiêu hóa): works Mon (1), Tue (2) full day, Thu (4) morning only (< 12:00)
--    - Doctor 2 (DOC-002, Tim mạch): works Tue (2), Wed (3) full day, Fri (5) morning only (< 12:00)
DELETE FROM "examination_slots" es
WHERE es."status" = 'OPEN'
  AND es."starts_at" >= TIMESTAMP '2026-11-01 00:00:00'
  AND NOT EXISTS (
      SELECT 1 FROM "appointments" a WHERE a."slot_id" = es."id"
  )
  AND (
      (es."doctor_id" = 1 AND (
          EXTRACT(ISODOW FROM es."starts_at") NOT IN (1, 2, 4)
          OR (EXTRACT(ISODOW FROM es."starts_at") = 4 AND es."starts_at"::time >= TIME '12:00')
      ))
      OR
      (es."doctor_id" = 2 AND (
          EXTRACT(ISODOW FROM es."starts_at") NOT IN (2, 3, 5)
          OR (EXTRACT(ISODOW FROM es."starts_at") = 5 AND es."starts_at"::time >= TIME '12:00')
      ))
  );


UPDATE "examination_slots" es
SET "status" = 'CLOSED'
WHERE es."status" = 'OPEN'
  AND es."starts_at" >= TIMESTAMP '2026-11-01 00:00:00'
  AND (
      (es."doctor_id" = 1 AND (
          EXTRACT(ISODOW FROM es."starts_at") NOT IN (1, 2, 4)
          OR (EXTRACT(ISODOW FROM es."starts_at") = 4 AND es."starts_at"::time >= TIME '12:00')
      ))
      OR
      (es."doctor_id" = 2 AND (
          EXTRACT(ISODOW FROM es."starts_at") NOT IN (2, 3, 5)
          OR (EXTRACT(ISODOW FROM es."starts_at") = 5 AND es."starts_at"::time >= TIME '12:00')
      ))
  );
-- 2. Seed OPEN slots from CURRENT_DATE through CURRENT_DATE + 30 strictly matching each doctor's duty schedule
INSERT INTO "examination_slots" ("service_id", "doctor_id", "room_id", "starts_at", "ends_at", "capacity", "status")
SELECT p.service_id, p.doctor_id, p.room_id,
       d::date + s.t, d::date + s.t + interval '30 minutes', 3, 'OPEN'
FROM (VALUES (1::bigint, 1::bigint, 1::bigint), (2::bigint, 2::bigint, 3::bigint)) AS p(service_id, doctor_id, room_id)
CROSS JOIN generate_series(CURRENT_DATE, CURRENT_DATE + 30, interval '1 day') AS d
CROSS JOIN (VALUES (time '07:30'), (time '08:00'), (time '08:30'), (time '09:00'), (time '09:30'),
                   (time '10:00'), (time '10:30'), (time '11:00'),
                   (time '13:30'), (time '14:00'), (time '14:30'), (time '15:00'), (time '15:30'), (time '16:00')) AS s(t)
WHERE (
    (p.doctor_id = 1 AND (
        EXTRACT(ISODOW FROM d) IN (1, 2)
        OR (EXTRACT(ISODOW FROM d) = 4 AND s.t < TIME '12:00')
    ))
    OR
    (p.doctor_id = 2 AND (
        EXTRACT(ISODOW FROM d) IN (2, 3)
        OR (EXTRACT(ISODOW FROM d) = 5 AND s.t < TIME '12:00')
    ))
)
AND NOT EXISTS (
    SELECT 1 FROM "examination_slots" es
    WHERE es."doctor_id" = p.doctor_id
      AND es."service_id" = p.service_id
      AND es."starts_at" = d::date + s.t
);

-- 3. Seed service-level OPEN slots (doctor_id IS NULL, allocated at reception) for remaining weekday shifts (Mon-Sat)
--    so Service Booking (/appointment/services/:id/booking) has full weekday coverage
INSERT INTO "examination_slots" ("service_id", "doctor_id", "room_id", "starts_at", "ends_at", "capacity", "status")
SELECT p.service_id, NULL::bigint, p.room_id,
       d::date + s.t, d::date + s.t + interval '30 minutes', 3, 'OPEN'
FROM (VALUES (1::bigint, 1::bigint), (2::bigint, 3::bigint)) AS p(service_id, room_id)
CROSS JOIN generate_series(CURRENT_DATE, CURRENT_DATE + 30, interval '1 day') AS d
CROSS JOIN (VALUES (time '07:30'), (time '08:00'), (time '08:30'), (time '09:00'), (time '09:30'),
                   (time '10:00'), (time '10:30'), (time '11:00'),
                   (time '13:30'), (time '14:00'), (time '14:30'), (time '15:00'), (time '15:30'), (time '16:00')) AS s(t)
WHERE EXTRACT(ISODOW FROM d) <> 7
  AND NOT (EXTRACT(ISODOW FROM d) = 6 AND s.t >= TIME '12:00')
  AND NOT EXISTS (
      SELECT 1 FROM "examination_slots" es
      WHERE es."service_id" = p.service_id
        AND es."starts_at" = d::date + s.t
  );

COMMIT;
