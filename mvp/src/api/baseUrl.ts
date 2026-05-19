/**
 * URL base de toda chamada REST/SSE do MVP. O path `/api/aihub` é fixo
 * (contrato com o backend) — a env var configura apenas a origem.
 *
 * Modos:
 *   1. <c>VITE_API_HOST</c> vazio/ausente → retorna `/api/aihub` (path
 *      relativo). Em dev local usa o proxy do Vite (vite.config.ts aponta
 *      pra http://localhost:5189). Em prod, depende do reverse proxy do
 *      mesmo origin servir o backend.
 *   2. <c>VITE_API_HOST</c> setado (ex.: `https://api.efs.com`) → resolve
 *      pra `${VITE_API_HOST}/api/aihub`. Backend precisa liberar o origin
 *      do frontend em `Cors:AllowedOrigins` (appsettings).
 *
 * Resiliência: strip de trailing slashes e do sufixo `/api/aihub` quando
 * o user inadvertidamente colar o path inteiro na var — evita
 * `https://host/api/aihub/api/aihub`.
 */
const API_PATH = '/api/aihub'

function resolveApiBaseUrl(): string {
  const raw = import.meta.env.VITE_API_HOST
  if (typeof raw !== 'string') return API_PATH
  const host = raw.trim().replace(/\/+$/, '').replace(/\/api\/aihub$/, '')
  return host.length > 0 ? `${host}${API_PATH}` : API_PATH
}

export const API_BASE_URL = resolveApiBaseUrl()
