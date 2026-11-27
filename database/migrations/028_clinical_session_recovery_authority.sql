-- Migration 028: Clinical Session Recovery Authority (PR #859 / Issue #858, F01 Option B).
--
-- Adds ONE narrowly-scoped request/grant table for clinically interrupted EXAMINATION /
-- RETURN_CONSULTATION sessions, plus the two catalog-only permissions
-- CLINICAL_SESSION_RECOVERY_GRANT and CLINICAL_SESSION_RECOVER.
--
-- Explicitly OUT of scope: no new roles, no blank grants to RECEPTION / HOSPITAL_ADMIN /
-- SYSTEM_ADMIN / DOCTOR, no generic delegation subsystem, no CLS/Pharmacy abort path,
-- no automatic Visit finalization, payment/EMR/presence mutation or reassignment.
--
-- 100% idempotent across repeated runs. Never auto-closes existing queue entries.

BEGIN;

SET LOCAL lock_timeout = '10s';

-- ============================================================================
-- 0. FAIL-FAST PREFLIGHT AUDIT (read-only; zero auto-delete / zero silent close)
-- ============================================================================
DO $$
DECLARE
  v_bad_count bigint := 0;
  v_samples text := '';
BEGIN
  -- 0.1. Re-run guard: duplicate unresolved recovery rows per QueueEntry.
  IF to_regclass('public.clinical_session_recovery_requests') IS NOT NULL THEN
    EXECUTE $dyn$
      SELECT COUNT(*), coalesce(string_agg(queue_entry_id::text, ', ' ORDER BY queue_entry_id), '')
      FROM (
        SELECT queue_entry_id
        FROM clinical_session_recovery_requests
        WHERE status IN ('REQUESTED', 'GRANTED')
        GROUP BY queue_entry_id
        HAVING COUNT(*) > 1
        ORDER BY queue_entry_id
        LIMIT 10
      ) s
    $dyn$ INTO v_bad_count, v_samples;

    IF v_bad_count > 0 THEN
      RAISE EXCEPTION '[PREFLIGHT_028_FAILED] queue_entries has more than one unresolved clinical_session_recovery_request (% queue entry(ies); sample ids: %). Run database/scripts/preflight_028_audit.sql.',
        v_bad_count, v_samples;
    END IF;

    -- 0.2. Re-run guard: status/metadata inconsistency.
    EXECUTE $dyn$
      SELECT COUNT(*), coalesce(string_agg(id::text, ', ' ORDER BY id), '')
      FROM (
        SELECT id
        FROM clinical_session_recovery_requests
        WHERE (status = 'GRANTED' AND (
                  authorized_doctor_id IS NULL OR granted_by_user_id IS NULL OR granted_by_doctor_id IS NULL
                  OR granted_at IS NULL OR expires_at IS NULL OR btrim(coalesce(grant_reason, '')) = ''))
           OR (status = 'RECOVERED' AND (
                  recovered_by_user_id IS NULL OR recovered_by_doctor_id IS NULL OR recovered_at IS NULL
                  OR btrim(coalesce(recovery_reason, '')) = ''
                  OR (own_session = false AND authorized_doctor_id IS NULL)))
           OR (status = 'REVOKED' AND (
                  revoked_by_user_id IS NULL OR revoked_at IS NULL OR btrim(coalesce(revocation_reason, '')) = ''))
           OR (status = 'REJECTED' AND (
                  rejected_by_user_id IS NULL OR rejected_at IS NULL OR btrim(coalesce(rejection_reason, '')) = ''))
           OR (status = 'CANCELLED' AND (
                  cancelled_by_user_id IS NULL OR cancelled_at IS NULL OR btrim(coalesce(cancellation_reason, '')) = ''))
           OR (status = 'EXPIRED' AND expired_at IS NULL)
           OR (status NOT IN ('REQUESTED','GRANTED','REJECTED','CANCELLED','RECOVERED','REVOKED','EXPIRED'))
           OR (granted_by_doctor_id IS NOT NULL AND authorized_doctor_id IS NOT NULL
               AND granted_by_doctor_id = authorized_doctor_id)
           OR (expected_queue_state_version IS NULL OR expected_queue_state_version < 1)
        ORDER BY id
        LIMIT 10
      ) s
    $dyn$ INTO v_bad_count, v_samples;

    IF v_bad_count > 0 THEN
      RAISE EXCEPTION '[PREFLIGHT_028_FAILED] clinical_session_recovery_requests has inconsistent status/metadata (% row(s); sample ids: %). Run database/scripts/preflight_028_audit.sql.',
        v_bad_count, v_samples;
    END IF;

    -- 0.3. Re-run guard: duplicate idempotency tuple (actor, operation, key).
    EXECUTE $dyn$
      SELECT COUNT(*), coalesce(string_agg(key_tuple, ', ' ORDER BY key_tuple), '')
      FROM (
        SELECT concat_ws('|', idempotency_actor_user_id, idempotency_operation, idempotency_key) AS key_tuple
        FROM clinical_session_recovery_requests
        WHERE idempotency_key IS NOT NULL
        GROUP BY idempotency_actor_user_id, idempotency_operation, idempotency_key
        HAVING COUNT(*) > 1
        ORDER BY 1
        LIMIT 10
      ) s
    $dyn$ INTO v_bad_count, v_samples;

    IF v_bad_count > 0 THEN
      RAISE EXCEPTION '[PREFLIGHT_028_FAILED] duplicate clinical recovery idempotency tuples (% tuple(s); sample: %). Run database/scripts/preflight_028_audit.sql.',
        v_bad_count, v_samples;
    END IF;
  END IF;

  -- 0.4. queue_entries must not already have a conflicting (id, visit_id) uniqueness definition.
  IF EXISTS (
    SELECT 1
    FROM pg_constraint c
    JOIN pg_class t ON t.oid = c.conrelid
    WHERE t.relname = 'queue_entries'
      AND c.contype = 'u'
      AND c.conname = 'uq_queue_entries_id_visit_id'
  ) THEN
    -- already applied; nothing to do
    NULL;
  END IF;
END $$;

-- ============================================================================
-- 1. Permission catalog (CATALOG ONLY — no role_permissions seeding by design)
-- ============================================================================
INSERT INTO "permissions" ("code")
SELECT 'CLINICAL_SESSION_RECOVERY_GRANT'
WHERE NOT EXISTS (SELECT 1 FROM "permissions" WHERE "code" = 'CLINICAL_SESSION_RECOVERY_GRANT');

INSERT INTO "permissions" ("code")
SELECT 'CLINICAL_SESSION_RECOVER'
WHERE NOT EXISTS (SELECT 1 FROM "permissions" WHERE "code" = 'CLINICAL_SESSION_RECOVER');

-- ============================================================================
-- 2. Composite FK target: queue entry must belong to the declared Visit
-- ============================================================================
DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1
    FROM pg_constraint c
    JOIN pg_class t ON t.oid = c.conrelid
    WHERE t.relname = 'queue_entries'
      AND c.contype = 'u'
      AND c.conname = 'uq_queue_entries_id_visit_id'
  ) THEN
    ALTER TABLE "queue_entries"
      ADD CONSTRAINT "uq_queue_entries_id_visit_id" UNIQUE ("id", "visit_id");
  END IF;
END $$;

-- ============================================================================
-- 3. clinical_session_recovery_requests
-- ============================================================================
CREATE TABLE IF NOT EXISTS "clinical_session_recovery_requests" (
  "id" bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
  "hospital_id" bigint NOT NULL REFERENCES "hospitals" ("id") ON DELETE RESTRICT,
  "visit_id" bigint NOT NULL REFERENCES "visits" ("id") ON DELETE RESTRICT,
  "queue_entry_id" bigint NOT NULL REFERENCES "queue_entries" ("id") ON DELETE RESTRICT,
  "original_doctor_id" bigint NULL REFERENCES "doctors" ("id") ON DELETE RESTRICT,
  "authorized_doctor_id" bigint NULL REFERENCES "doctors" ("id") ON DELETE RESTRICT,
  "requested_by_user_id" bigint NOT NULL REFERENCES "users" ("id") ON DELETE RESTRICT,
  "requested_at" timestamp without time zone NOT NULL,
  "request_reason" varchar(500) NOT NULL,
  "granted_by_user_id" bigint NULL REFERENCES "users" ("id") ON DELETE RESTRICT,
  "granted_by_doctor_id" bigint NULL REFERENCES "doctors" ("id") ON DELETE RESTRICT,
  "granted_at" timestamp without time zone NULL,
  "grant_reason" varchar(500) NULL,
  "expires_at" timestamp without time zone NULL,
  "expected_queue_state_version" integer NOT NULL,
  "status" varchar(16) NOT NULL,
  "own_session" boolean NOT NULL DEFAULT false,
  "recovered_by_user_id" bigint NULL REFERENCES "users" ("id") ON DELETE RESTRICT,
  "recovered_by_doctor_id" bigint NULL REFERENCES "doctors" ("id") ON DELETE RESTRICT,
  "recovered_at" timestamp without time zone NULL,
  "recovery_reason" varchar(500) NULL,
  "revoked_by_user_id" bigint NULL REFERENCES "users" ("id") ON DELETE RESTRICT,
  "revoked_by_doctor_id" bigint NULL REFERENCES "doctors" ("id") ON DELETE RESTRICT,
  "revoked_at" timestamp without time zone NULL,
  "revocation_reason" varchar(500) NULL,
  "rejected_by_user_id" bigint NULL REFERENCES "users" ("id") ON DELETE RESTRICT,
  "rejected_at" timestamp without time zone NULL,
  "rejection_reason" varchar(500) NULL,
  "cancelled_by_user_id" bigint NULL REFERENCES "users" ("id") ON DELETE RESTRICT,
  "cancelled_at" timestamp without time zone NULL,
  "cancellation_reason" varchar(500) NULL,
  "expired_at" timestamp without time zone NULL,
  "idempotency_key" varchar(128) NULL,
  "idempotency_actor_user_id" bigint NULL REFERENCES "users" ("id") ON DELETE RESTRICT,
  "idempotency_operation" varchar(16) NULL,
  "confirmation_payload_hash" varchar(64) NULL,
  "state_version" integer NOT NULL DEFAULT 1,
  "created_at" timestamp without time zone NOT NULL,
  "updated_at" timestamp without time zone NOT NULL
);

-- 3.1. Composite FK: queue_entry must belong to the same Visit.
DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1
    FROM pg_constraint c
    JOIN pg_class t ON t.oid = c.conrelid
    WHERE t.relname = 'clinical_session_recovery_requests'
      AND c.contype = 'f'
      AND c.conname = 'fk_csrr_queue_entry_visit'
  ) THEN
    ALTER TABLE "clinical_session_recovery_requests"
      ADD CONSTRAINT "fk_csrr_queue_entry_visit"
      FOREIGN KEY ("queue_entry_id", "visit_id")
      REFERENCES "queue_entries" ("id", "visit_id")
      ON DELETE RESTRICT;
  END IF;
END $$;

-- 3.2. Check constraints (status machine + no self-grant + metadata consistency).
DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_csrr_status'
                   AND conrelid = '"clinical_session_recovery_requests"'::regclass) THEN
    ALTER TABLE "clinical_session_recovery_requests"
      ADD CONSTRAINT "chk_csrr_status"
      CHECK ("status" IN ('REQUESTED','GRANTED','REJECTED','CANCELLED','RECOVERED','REVOKED','EXPIRED'));
  END IF;

  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_csrr_queue_state_positive'
                   AND conrelid = '"clinical_session_recovery_requests"'::regclass) THEN
    ALTER TABLE "clinical_session_recovery_requests"
      ADD CONSTRAINT "chk_csrr_queue_state_positive"
      CHECK ("expected_queue_state_version" >= 1);
  END IF;

  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_csrr_no_self_grant'
                   AND conrelid = '"clinical_session_recovery_requests"'::regclass) THEN
    ALTER TABLE "clinical_session_recovery_requests"
      ADD CONSTRAINT "chk_csrr_no_self_grant"
      CHECK ("granted_by_doctor_id" IS NULL OR "authorized_doctor_id" IS NULL
             OR "granted_by_doctor_id" <> "authorized_doctor_id");
  END IF;

  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_csrr_idempotency_tuple'
                   AND conrelid = '"clinical_session_recovery_requests"'::regclass) THEN
    ALTER TABLE "clinical_session_recovery_requests"
      ADD CONSTRAINT "chk_csrr_idempotency_tuple"
      CHECK (("idempotency_key" IS NULL AND "idempotency_actor_user_id" IS NULL AND "idempotency_operation" IS NULL)
             OR ("idempotency_key" IS NOT NULL AND "idempotency_actor_user_id" IS NOT NULL AND "idempotency_operation" IS NOT NULL));
  END IF;

  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'chk_csrr_status_metadata'
                   AND conrelid = '"clinical_session_recovery_requests"'::regclass) THEN
    ALTER TABLE "clinical_session_recovery_requests"
      ADD CONSTRAINT "chk_csrr_status_metadata"
      CHECK (
        ("status" = 'REQUESTED'
          AND "granted_by_user_id" IS NULL AND "granted_at" IS NULL AND "authorized_doctor_id" IS NULL
          AND "recovered_by_user_id" IS NULL AND "rejected_by_user_id" IS NULL
          AND "cancelled_by_user_id" IS NULL AND "revoked_by_user_id" IS NULL AND "expired_at" IS NULL)
        OR ("status" = 'GRANTED'
          AND "authorized_doctor_id" IS NOT NULL AND "granted_by_user_id" IS NOT NULL
          AND "granted_by_doctor_id" IS NOT NULL AND "granted_at" IS NOT NULL
          AND "expires_at" IS NOT NULL AND btrim(coalesce("grant_reason", '')) <> '')
        OR ("status" = 'RECOVERED'
          AND "recovered_by_user_id" IS NOT NULL AND "recovered_by_doctor_id" IS NOT NULL
          AND "recovered_at" IS NOT NULL AND btrim(coalesce("recovery_reason", '')) <> ''
          AND ("own_session" OR "authorized_doctor_id" IS NOT NULL))
        OR ("status" = 'REJECTED'
          AND "rejected_by_user_id" IS NOT NULL AND "rejected_at" IS NOT NULL
          AND btrim(coalesce("rejection_reason", '')) <> '')
        OR ("status" = 'CANCELLED'
          AND "cancelled_by_user_id" IS NOT NULL AND "cancelled_at" IS NOT NULL
          AND btrim(coalesce("cancellation_reason", '')) <> '')
        OR ("status" = 'REVOKED'
          AND "revoked_by_user_id" IS NOT NULL AND "revoked_at" IS NOT NULL
          AND btrim(coalesce("revocation_reason", '')) <> '')
        OR ("status" = 'EXPIRED' AND "expired_at" IS NOT NULL)
      );
  END IF;
END $$;

-- ============================================================================
-- 4. Indexes
-- ============================================================================
CREATE INDEX IF NOT EXISTS "ix_csrr_visit_id"
  ON "clinical_session_recovery_requests" ("visit_id");

CREATE INDEX IF NOT EXISTS "ix_csrr_queue_entry_id"
  ON "clinical_session_recovery_requests" ("queue_entry_id");

CREATE INDEX IF NOT EXISTS "ix_csrr_hospital_id"
  ON "clinical_session_recovery_requests" ("hospital_id");

CREATE INDEX IF NOT EXISTS "ix_csrr_original_doctor_id"
  ON "clinical_session_recovery_requests" ("original_doctor_id");

CREATE INDEX IF NOT EXISTS "ix_csrr_authorized_doctor_id"
  ON "clinical_session_recovery_requests" ("authorized_doctor_id");

CREATE INDEX IF NOT EXISTS "ix_csrr_status_expires_at"
  ON "clinical_session_recovery_requests" ("status", "expires_at");

-- One unresolved request/grant per QueueEntry. No now() in the predicate: expiry is enforced
-- by the trusted clock in a transaction that first transitions expired grants to EXPIRED.
CREATE UNIQUE INDEX IF NOT EXISTS "ux_csrr_one_unresolved_per_queue_entry"
  ON "clinical_session_recovery_requests" ("queue_entry_id")
  WHERE "status" IN ('REQUESTED', 'GRANTED');

-- Idempotency binds (actor, operation, key) — never a bare client-supplied key.
CREATE UNIQUE INDEX IF NOT EXISTS "ux_csrr_idempotency_actor_operation_key"
  ON "clinical_session_recovery_requests" ("idempotency_actor_user_id", "idempotency_operation", "idempotency_key")
  WHERE "idempotency_key" IS NOT NULL
    AND "idempotency_actor_user_id" IS NOT NULL
    AND "idempotency_operation" IS NOT NULL;

COMMIT;
