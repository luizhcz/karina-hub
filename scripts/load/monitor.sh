#!/usr/bin/env bash
# Side-car de observabilidade — rode em um terminal separado enquanto
# ./run_burst.sh está disparando. Ctrl-C para sair.
set -euo pipefail

PG_USER="${POSTGRES_USER:-efs_ai_hub}"
PG_DB="${POSTGRES_DB:-efs_ai_hub}"
INTERVAL="${INTERVAL:-2}"

echo "==> monitorando (refresh ${INTERVAL}s — Ctrl-C para sair)"
echo ""

while true; do
  ts=$(date +%H:%M:%S)

  pg_stats=$(docker compose exec -T postgres psql -U "$PG_USER" -d "$PG_DB" -tAc \
    "select state, count(*) from pg_stat_activity where datname='$PG_DB' group by state;" 2>/dev/null \
    | awk -F'|' '{ printf "%s=%s ", $1, $2 }' || echo "pg?")

  redis_mem=$(docker compose exec -T redis redis-cli info memory 2>/dev/null \
    | awk -F: '/used_memory_human:/ { gsub(/[\r\n]/,"",$2); print $2 }' || echo "?")

  redis_keys=$(docker compose exec -T redis redis-cli --scan --pattern 'efs-ai-hub:*' 2>/dev/null \
    | wc -l | tr -d '[:space:]' || echo "?")

  printf "[%s] pg{ %s} redis{ mem=%s keys=%s }\n" "$ts" "$pg_stats" "$redis_mem" "$redis_keys"

  sleep "$INTERVAL"
done
