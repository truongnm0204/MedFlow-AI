BEGIN;
INSERT INTO "examination_slots" ("service_id", "doctor_id", "room_id", "starts_at", "ends_at", "capacity", "status")
SELECT p.service_id, p.doctor_id, p.room_id,
       d::date + s.t, d::date + s.t + interval '30 minutes', 3, 'OPEN'
FROM (VALUES (1::bigint, 1::bigint, 1::bigint), (2::bigint, 2::bigint, 3::bigint)) AS p(service_id, doctor_id, room_id)
CROSS JOIN generate_series(CURRENT_DATE + 1, CURRENT_DATE + 14, interval '1 day') AS d
CROSS JOIN (VALUES (time '07:30'), (time '08:00'), (time '08:30'), (time '09:00'), (time '09:30'),
                   (time '10:00'), (time '10:30'), (time '11:00'),
                   (time '13:30'), (time '14:00'), (time '14:30'), (time '15:00'), (time '15:30'), (time '16:00')) AS s(t)
WHERE EXTRACT(ISODOW FROM d) <> 7
  AND NOT EXISTS (SELECT 1 FROM "examination_slots" es
                  WHERE es."doctor_id" = p.doctor_id AND es."service_id" = p.service_id
                    AND es."starts_at" = d::date + s.t);
COMMIT;
