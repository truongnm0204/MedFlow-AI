#!/usr/bin/env bash
# Bootstrap the medflow_test database: schema snapshot + minimal auth seed.
# Used by CI (.github/workflows/ci.yml) and locally for integration tests.
# Usage: init-test-db.sh <connection-uri>
#   e.g. init-test-db.sh "postgresql://postgres:postgres@localhost:5432/medflow_test"
set -euo pipefail

DB="${1:?Usage: init-test-db.sh <connection-uri>}"
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
BASELINE=1   # migration 001 is already inside the schema snapshot

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

# btree_gist is built into PG18 but must be installed on PG16 — the schema's
# EXCLUDE constraints on uuid columns (ex_*_overlap) fail without it.
psql "$DB" -X -v ON_ERROR_STOP=1 -q -c "CREATE EXTENSION IF NOT EXISTS btree_gist"

# Schema snapshot + minimal seed in ONE transaction.
# The PG18 dump needs lines stripped that PG16 cannot parse.
# --single-transaction requires -f; "-f -" reads stdin.
{
  sed -e '/^\\restrict /d' \
      -e '/^\\unrestrict /d' \
      -e '/^SET transaction_timeout = /d' \
    "$REPO_ROOT/database/medflow_schema_v1_2.sql"
  cat "$REPO_ROOT/database/seed_auth_test.sql"
} | psql "$DB" -X -v ON_ERROR_STOP=1 --single-transaction -f - -q

# Apply migrations newer than the baseline, in filename order.
for f in "$REPO_ROOT"/database/migrations/*.sql; do
  [[ -e "$f" ]] || continue
  n=$(basename "$f" | grep -oE '^[0-9]+' | sed 's/^0*//')
  if [[ -n "$n" && "$n" -gt "$BASELINE" ]]; then
    echo "applying post-baseline migration: $(basename "$f")"
    psql "$DB" -X -v ON_ERROR_STOP=1 -q -f "$f"
  fi
done

echo "medflow_test initialized (schema v1.2 + auth seed)."
