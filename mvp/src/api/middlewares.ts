import { get } from './client'

// Catálogo de middlewares mora em GET /functions junto com tools/executors —
// não há endpoint dedicado. Aqui isolamos só o que importa pra tela admin.

export interface MiddlewareSettingOption {
  value: string
  label: string
}

export interface MiddlewareSetting {
  key: string
  label: string
  /** 'select' | 'text' | 'number' | etc. */
  type: string
  defaultValue: string | null
  options?: MiddlewareSettingOption[]
  description?: string | null
}

/** Fase em que o middleware atua na pipeline LLM (Pre = system prompt; Post = resposta; Both = ambos). */
export type MiddlewarePhase = 'Pre' | 'Post' | 'Both'

export interface MiddlewareTypeInfo {
  name: string
  phase: MiddlewarePhase
  label: string
  description: string
  settings: MiddlewareSetting[]
}

interface FunctionsResponse {
  middlewareTypes: MiddlewareTypeInfo[]
}

export async function getMiddlewareCatalog(): Promise<MiddlewareTypeInfo[]> {
  const data = await get<FunctionsResponse>('/functions')
  return data.middlewareTypes ?? []
}

/**
 * Constrói o objeto `settings` (key→value string) a partir do catálogo.
 * Aplica os defaults; settings sem default ficam string vazia.
 */
export function defaultSettingsFor(mw: MiddlewareTypeInfo): Record<string, string> {
  const out: Record<string, string> = {}
  for (const s of mw.settings) {
    out[s.key] = s.defaultValue ?? ''
  }
  return out
}
