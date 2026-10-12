-- 001_create_refresh_tokens.sql
-- Opaque refresh tokens for JWT rotation. Only SHA-256 hashes are stored.

CREATE TABLE IF NOT EXISTS medflow.refresh_tokens (
    id                      uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id                 uuid NOT NULL REFERENCES medflow.users(id) ON DELETE CASCADE,
    token_hash              varchar(128) NOT NULL UNIQUE,
    expires_at              timestamptz NOT NULL,
    revoked_at              timestamptz,
    replaced_by_token_hash  varchar(128),
    created_by_ip           varchar(45),
    revoked_by_ip           varchar(45),
    created_at              timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS idx_refresh_tokens_active
    ON medflow.refresh_tokens (user_id)
    WHERE revoked_at IS NULL;
