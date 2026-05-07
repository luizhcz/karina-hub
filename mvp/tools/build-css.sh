#!/usr/bin/env bash
# =============================================================================
# build-css.sh — regera mvp/public/css/utilities.css a partir do Tailwind.
#
# Roda LOCALMENTE quando o design system muda. NÃO faz parte do CI/Docker.
# Output é versionado em git (mvp/public/css/utilities.css).
#
# Uso:
#   bash mvp/tools/build-css.sh
#
# Self-contained: usa `npx --yes` pra baixar tailwindcss em cache temporário,
# sem precisar de package.json no projeto.
# =============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

cd "$SCRIPT_DIR"

echo "▶ Regerando ../public/css/utilities.css..."
npx --yes tailwindcss@3.4.17 \
    -c ./tailwind.config.js \
    -i ./css-source.css \
    -o ../public/css/utilities.css \
    --minify

echo "✓ ../public/css/utilities.css atualizado. Lembre de commitar."
