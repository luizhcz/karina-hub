#!/usr/bin/env bash
set -euo pipefail

BASE="${BASE_URL:-http://localhost:5189}"
PG_USER="${POSTGRES_USER:-efs_ai_hub}"
PG_DB="${POSTGRES_DB:-efs_ai_hub}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

echo "==> Validando backend em $BASE"
if ! curl -sf -H "x-efs-account: load-vu1" "$BASE/health/ready" >/dev/null; then
  echo "ERRO: backend em $BASE não respondeu /health/ready com 200."
  echo "      Checar: (1) 'docker compose up -d --build' rodou; (2) appsettings.Development.json"
  echo "      tem 'load-vu1'..'load-vu30' em Admin.AccountIds (default guard)."
  exit 1
fi

if ! command -v k6 >/dev/null 2>&1; then
  echo "ERRO: k6 não está instalado. Rode 'brew install k6'."
  exit 1
fi

echo "==> Limpando chaves de rate limiter do Redis (scope: load-*)"
docker compose exec -T redis sh -c "redis-cli --scan --pattern 'efs-ai-hub:rl:chat:load-*' | xargs -r redis-cli del" >/dev/null 2>&1 || true

echo "==> Snapshot de conexões PG pré-teste"
PG_BEFORE=$(docker compose exec -T postgres psql -U "$PG_USER" -d "$PG_DB" -tAc \
  "select count(*) from pg_stat_activity where datname='$PG_DB';" 2>/dev/null | tr -d '[:space:]' || echo "?")
echo "    conexões ativas: $PG_BEFORE"

echo "==> Rodando k6 burst (30 VUs simultâneos)..."
k6 run --env BASE_URL="$BASE" "$SCRIPT_DIR/atendimento_cliente_burst.js"
K6_EXIT=$?

echo ""
echo "==> Snapshot de conexões PG pós-teste"
PG_AFTER=$(docker compose exec -T postgres psql -U "$PG_USER" -d "$PG_DB" -tAc \
  "select count(*) from pg_stat_activity where datname='$PG_DB';" 2>/dev/null | tr -d '[:space:]' || echo "?")
echo "    conexões ativas: $PG_AFTER (antes: $PG_BEFORE)"

echo ""
echo "==> Últimos logs do backend (RateLimiter|CircuitBreaker|BackPressure|ERROR)"
docker compose logs --tail=500 backend 2>&1 \
  | grep -E 'RateLimiter|CircuitBreaker|BackPressure|ChatExecutionRegistry|ERROR|fail' \
  | tail -50 || echo "    (nenhum evento relevante)"

echo ""
echo "==> Relatório HTML: $SCRIPT_DIR/last-report.html"
exit $K6_EXIT
