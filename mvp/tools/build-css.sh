#!/usr/bin/env bash
# =============================================================================
# build-css.sh — regera mvp/public/css/utilities.css a partir do tailwind.config.
#
# Roda LOCALMENTE quando o design system muda. NÃO faz parte do CI/Docker.
# Output é versionado em git (mvp/public/css/utilities.css).
#
# Uso:
#   bash mvp/tools/build-css.sh
# =============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
MVP_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

cd "$MVP_ROOT"

# Tailwind ainda vem do package.json do React (durante a migração). Após Fase 5
# (cleanup React), o tailwindcss + tailwind.config.ts ficam aqui em tools/ pra
# manter capacidade de regerar CSS.
echo "▶ Regerando mvp/public/css/utilities.css..."
npx tailwindcss \
    -c tailwind.config.ts \
    -i ./src/index.css \
    -o ./public/css/utilities.css \
    --minify

echo "✓ public/css/utilities.css atualizado. Lembre de commitar."
