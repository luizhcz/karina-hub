#!/usr/bin/env bash
# =============================================================================
# db/apply.sh — aplica schema + migrations + seeds na ordem canônica.
#
# Uso:
#   ./db/apply.sh                            # usa env defaults (ver abaixo)
#   ./db/apply.sh postgres://user:pass@host:port/db
#   PGHOST=... PGUSER=... PGPASSWORD=... ./db/apply.sh
#
# Docker compose local (ambiente dev):
#   docker exec -i repositorio-postgres-1 bash -c 'PGPASSWORD=agentfw123 bash -s' < db/apply.sh
#
# Ordem aplicada:
#   1. db/schema.sql             (cria schema base se não existir — idempotente)
#   2. db/migration_*.sql        (ordem alfabética = ordem cronológica pelos nomes)
#   3. db/seed_*.sql             (ordem alfabética)
#
# Requisitos:
#   - psql no PATH
#   - conexão pg válida via PG* env ou DATABASE_URL como $1
#   - todas as migrations idempotentes (usam IF NOT EXISTS ou DO block)
#
# Saída: 0 = sucesso, >0 = parou na primeira falha (ON_ERROR_STOP=on).
# =============================================================================
set -euo pipefail

DB_URL="${1:-${DATABASE_URL:-}}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

PSQL_ARGS=(-v ON_ERROR_STOP=on -X --quiet)
if [[ -n "$DB_URL" ]]; then
    PSQL_ARGS+=("$DB_URL")
fi

echo "▶ db/apply.sh — aplicando em $SCRIPT_DIR"
echo

run_file() {
    local file="$1"
    local label="$2"
    [[ -f "$file" ]] || { echo "  ⚠ $label ausente: $file"; return; }
    echo "  ▸ $label: $(basename "$file")"
    psql "${PSQL_ARGS[@]}" -f "$file" >/dev/null
}

echo "[1/3] schema.sql"
run_file "$SCRIPT_DIR/schema.sql" "schema"
echo

echo "[2/3] migrations (ordem alfabética)"
shopt -s nullglob
for f in "$SCRIPT_DIR"/migration_*.sql; do
    run_file "$f" "migration"
done
shopt -u nullglob
echo

echo "[3/3] seeds (ordem alfabética)"
shopt -s nullglob
for f in "$SCRIPT_DIR"/seed_*.sql; do
    run_file "$f" "seed"
done
shopt -u nullglob
echo

echo "✓ db/apply.sh concluído."
