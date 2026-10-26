-- Migration 005: re-add auth_sessions audit columns + audit_logs.ip_address
-- Reason: v4.1 dropped these but they are required for
--   1. refresh-token-reuse detection (replaced_by_token_hash)
--   2. security audit trail (created_by_ip / revoked_by_ip / audit_logs.ip_address)
-- Rule reference: .claude/rules/api-contract-and-errors.md — "moi su kien nhay cam
-- ghi audit_logs ... IP lay X-Forwarded-For".

BEGIN;

-- auth_sessions: add refresh-token rotation + IP audit columns
ALTER TABLE "auth_sessions"
    ADD COLUMN IF NOT EXISTS "replaced_by_token_hash" varchar(128),
    ADD COLUMN IF NOT EXISTS "created_by_ip" varchar(45),
    ADD COLUMN IF NOT EXISTS "revoked_by_ip" varchar(45),
    ADD COLUMN IF NOT EXISTS "created_at" timestamp without time zone NOT NULL DEFAULT now();

-- audit_logs: re-add ip_address (dropped in v4.1)
ALTER TABLE "audit_logs"
    ADD COLUMN IF NOT EXISTS "ip_address" inet;

-- index for IP-based audit queries
CREATE INDEX IF NOT EXISTS "ix_audit_logs_ip_address" ON "audit_logs" ("ip_address");

COMMIT;
