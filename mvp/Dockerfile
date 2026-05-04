# Build do bundle Vite e serve via nginx alpine; nginx.conf usa BACKEND_URL
# como template substituído em runtime via envsubst (igual frontend principal).

# ── Stage 1: build ───────────────────────────────────────────────────────────
FROM node:22-alpine AS builder

WORKDIR /app

COPY package.json package-lock.json* ./
RUN npm install

COPY . .
RUN npm run build

# ── Stage 2: serve ───────────────────────────────────────────────────────────
FROM nginx:1.27-alpine AS runner

COPY --from=builder /app/dist /usr/share/nginx/html

# Template processado automaticamente pelo nginx:alpine no startup
COPY nginx.conf /etc/nginx/templates/default.conf.template

EXPOSE 80

# Aponta pro service backend dentro da rede do compose
ENV BACKEND_URL=http://backend:5189
