#!/usr/bin/env bash
# =============================================================================
# db/apply.sh — aplica schemas → views → seeds → migrations em sequência fixa.
#
# Uso:
#   ./db/apply.sh                                    # PG* env defaults
#   ./db/apply.sh postgres://user:pass@host:port/db  # URL explícita
#
# Docker local:
#   docker exec -i repositorio-postgres-1 bash -c \
#     'PGPASSWORD=agentfw123 psql -U efs_ai_hub -d efs_ai_hub' < db/apply.sh
#
# Todos os arquivos são idempotentes (IF NOT EXISTS / ON CONFLICT / WHERE NOT EXISTS).
# Saída: 0 = sucesso, >0 = parou na primeira falha (ON_ERROR_STOP=on).
# =============================================================================
set -euo pipefail

DB_URL="${1:-${DATABASE_URL:-}}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PSQL_ARGS=(-v ON_ERROR_STOP=on -X --quiet)
[[ -n "$DB_URL" ]] && PSQL_ARGS+=("$DB_URL")

echo "▶ db/apply.sh — aplicando em $SCRIPT_DIR"
for f in schemas.sql views.sql seeds.sql; do
  echo "  ▸ $f"
  psql "${PSQL_ARGS[@]}" -f "$SCRIPT_DIR/$f" >/dev/null
done

# Migrations incrementais (ordem alfabética = ordem de execução). Cada arquivo
# é idempotente via ON CONFLICT DO UPDATE/DO NOTHING — re-roda sem efeito.
if [ -d "$SCRIPT_DIR/migrations" ]; then
  for f in "$SCRIPT_DIR"/migrations/*.sql; do
    [ -f "$f" ] || continue
    echo "  ▸ migrations/$(basename "$f")"
    psql "${PSQL_ARGS[@]}" -f "$f" >/dev/null
  done
fi

echo "✓ aplicado."
