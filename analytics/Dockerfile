# Build do bundle Vite e serve via nginx alpine; nginx.conf usa BACKEND_URL
# como template substituído em runtime via envsubst (mesmo padrão do mvp/).

# ── Stage 1: build ───────────────────────────────────────────────────────────
FROM node:22-alpine AS builder

WORKDIR /app

COPY package.json package-lock.json* ./
RUN npm install

COPY . .

# Build-args opcionais que SOBRESCREVEM os defaults do `.env` (committed).
# Vite lê em build-time com precedência: .env.production > .env. Pra que o
# .env da source seja respeitado quando o build-arg vem vazio, só escrevemos
# .env.production se pelo menos um arg tiver valor.
#
#   VITE_API_HOST       — URL do backend (vazio = path relativo /api/aihub)
#   VITE_DEV_PERMISSIONS — permissions fake pra dev local sem proxy
#
# Override em CI/prod: `--build-arg VITE_API_HOST=https://api.efs.com`.
ARG VITE_API_HOST=""
ARG VITE_DEV_PERMISSIONS=""
RUN if [ -n "$VITE_API_HOST" ] || [ -n "$VITE_DEV_PERMISSIONS" ]; then \
        : > .env.production; \
        if [ -n "$VITE_API_HOST" ];        then echo "VITE_API_HOST=$VITE_API_HOST"               >> .env.production; fi; \
        if [ -n "$VITE_DEV_PERMISSIONS" ]; then echo "VITE_DEV_PERMISSIONS=$VITE_DEV_PERMISSIONS" >> .env.production; fi; \
    fi

RUN npm run build

# ── Stage 2: serve ───────────────────────────────────────────────────────────
FROM nginx:1.27-alpine AS runner

COPY --from=builder /app/dist /usr/share/nginx/html

# Template processado automaticamente pelo nginx:alpine no startup
COPY nginx.conf /etc/nginx/templates/default.conf.template

EXPOSE 80

# Aponta pro service backend dentro da rede do compose
ENV BACKEND_URL=http://backend:5189
