# ── Stage 1: build ───────────────────────────────────────────────────────────
FROM node:22-alpine AS builder

WORKDIR /app

# Copiar manifests primeiro para cache de camada
COPY package.json package-lock.json* ./
RUN npm ci

# Copiar fonte e buildar
COPY . .
RUN npm run build

# ── Stage 2: serve ────────────────────────────────────────────────────────────
FROM nginx:1.27-alpine AS runner

# Artefatos do build
COPY --from=builder /app/dist /usr/share/nginx/html

# nginx.conf como template: ${BACKEND_URL} é substituído em runtime via envsubst
# O nginx:alpine processa /etc/nginx/templates/*.template automaticamente no startup
COPY nginx.conf /etc/nginx/templates/default.conf.template

EXPOSE 80

# BACKEND_URL padrão aponta para o serviço backend no docker-compose
ENV BACKEND_URL=http://backend:5189
