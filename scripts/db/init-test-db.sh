#!/usr/bin/env bash
# Bootstrap the medflow_test database with schema v4.1 + seed.
# Used by CI (.github/workflows/ci.yml) and locally for integration tests.
# Usage: init-test-db.sh <connection-uri>
#   e.g. init-test-db.sh "postgresql://postgres:postgres@localhost:5432/medflow_test"
set -euo pipefail

DB="${1:?Usage: init-test-db.sh <connection-uri>}"
# REPO_ROOT env override for containerized runs (defaults to script's repo location).
REPO_ROOT="${REPO_ROOT:-$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)}"

command -v psql >/dev/null || { echo "psql not found" >&2; exit 1; }

# Guard 1: target must be exactly medflow_test — never touch dev/prod data.
db_name=$(psql "$DB" -X -t -A -c "SELECT current_database()")
if [[ "$db_name" != "medflow_test" ]]; then
  echo "refusing: expected medflow_test, got '$db_name'" >&2
  exit 1
fi

# Guard 2: database must be completely empty (no user tables in any schema).
existing=$(psql "$DB" -X -t -A -c \
  "SELECT count(*) FROM information_schema.tables
   WHERE table_schema NOT IN ('pg_catalog','information_schema')")
if [[ "$existing" != "0" ]]; then
  echo "refusing: database not empty ($existing user tables)" >&2
  exit 1
fi

# v4.1 migrations: schema (002) → seed (003) → deltas (004+005), all in order.
# Each file already wraps itself in BEGIN;…COMMIT;.
for f in \
  "$REPO_ROOT/database/migrations/002_v41_priority1_schema.sql" \
  "$REPO_ROOT/database/migrations/003_v41_priority1_seed.sql" \
  "$REPO_ROOT/database/migrations/004_readd_auth_verification_challenges.sql" \
  "$REPO_ROOT/database/migrations/005_readd_auth_session_and_audit_cols.sql"; do
  echo "applying: $(basename "$f")"
  psql "$DB" -X -v ON_ERROR_STOP=1 -q -f "$f"
done

echo "medflow_test initialized (schema v4.1 + seed)."
