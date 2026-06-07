import { useSyncExternalStore } from 'react'

// Identidade do PM/PO persistida em localStorage. Mirror reduzido do mvp/:
// removido `chatDeploymentAllowed` porque analytics não tem gating de chat.
//
// - account: identificador opaco do usuário. Vira header `x-efs-account`
//   quando userType=cliente; `x-efs-user-profile-id` quando admin. Backend
//   usa o header pra escolher Persona e templates de prompt.
// - permissions: CSV enviada em `x-efs-permissions`. Default [] = autenticado
//   sem nenhuma permission; em dev local, `VITE_DEV_PERMISSIONS` injeta
//   fallback (ver headers.ts).
// - tenantId: ecoado pelo `/me`. Pode ficar undefined em dev sem proxy — o
//   backend cai em "default" nesse caso.

const STORAGE_KEY = 'efs-analytics-identity'

export type UserType = 'cliente' | 'admin'

export interface Identity {
  name: string
  account: string
  userType: UserType
  permissions: string[]
  projectId: string
  projectName: string
  tenantId?: string
}

const subscribers = new Set<() => void>()

let cached: Identity | null = readFromStorage()

function readFromStorage(): Identity | null {
  if (typeof window === 'undefined') return null
  try {
    const raw = window.localStorage.getItem(STORAGE_KEY)
    if (!raw) return null
    const parsed = JSON.parse(raw) as Partial<Identity>
    // account é o identificador efetivo (vira header). Sem ele, identity é
    // inútil. name é só display — fallback pro próprio account preserva o
    // refresh quando algum estado parcial chegou a localStorage.
    if (!parsed.account) return null
    return {
      name: parsed.name && parsed.name.length > 0 ? parsed.name : parsed.account,
      account: parsed.account,
      userType: parsed.userType === 'admin' ? 'admin' : 'cliente',
      permissions: Array.isArray(parsed.permissions) ? parsed.permissions : [],
      projectId: parsed.projectId ?? '',
      projectName: parsed.projectName ?? '',
      tenantId: parsed.tenantId,
    }
  } catch {
    return null
  }
}

function persist() {
  if (typeof window === 'undefined') return
  if (cached) {
    window.localStorage.setItem(STORAGE_KEY, JSON.stringify(cached))
  } else {
    window.localStorage.removeItem(STORAGE_KEY)
  }
  subscribers.forEach((cb) => cb())
}

export function getIdentity(): Identity | null {
  return cached
}

export function setIdentity(identity: Identity) {
  cached = identity
  persist()
}

export function patchIdentity(patch: Partial<Identity>) {
  if (!cached) return
  cached = { ...cached, ...patch }
  persist()
}

export function clearIdentity() {
  cached = null
  persist()
}

export function subscribeIdentity(cb: () => void): () => void {
  subscribers.add(cb)
  return () => {
    subscribers.delete(cb)
  }
}

// Snapshot estável pra useSyncExternalStore — `cached` é reatribuído (não
// mutado in-place), então a referência é estável entre publishes.
function getSnapshot(): Identity | null {
  return cached
}

export function useIdentity(): Identity | null {
  return useSyncExternalStore(subscribeIdentity, getSnapshot, getSnapshot)
}
