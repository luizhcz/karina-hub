import { useSyncExternalStore } from 'react'

// Identidade do PM/PO persistida em localStorage. Campos:
// - name: display visual no header.
// - account: identificador opaco do usuário. Vai como `x-efs-account` quando
//   userType=cliente; como `x-efs-user-profile-id` quando userType=admin.
//   Backend interpreta o header como origem (cliente vs admin), o que afeta
//   ChatRouting default + esquema de Persona (ClientPersona/AdminPersona) +
//   templates de prompt.
// - userType: cliente ou admin. Define qual header de identidade enviar.
//   Default 'cliente' pra compatibilidade com identities já salvas em
//   localStorage que não tinham o campo.
// - permissions: lista CSV de permissions resolvidas pelo proxy. Vai como
//   `x-efs-permissions` em todo request com identidade. Admin gating é
//   derivado dessa lista (match contra Admin:AdminPermissions no backend).
//   Default [] — usuário autenticado sem nenhuma permission. Em dev local,
//   `VITE_DEV_PERMISSIONS` no .env injeta um fallback.
// - projectId/projectName/chatDeploymentAllowed: scope de projeto + cache UI.

const STORAGE_KEY = 'efs-mvp-identity'

export type UserType = 'cliente' | 'admin'

export interface Identity {
  name: string
  account: string
  userType: UserType
  permissions: string[]
  projectId: string
  projectName: string
  chatDeploymentAllowed: boolean
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
      // Identidades antigas (sem o campo) caem em 'cliente' — backward-compat
      // com o comportamento anterior (MVP sempre mandava x-efs-account).
      userType: parsed.userType === 'admin' ? 'admin' : 'cliente',
      permissions: Array.isArray(parsed.permissions) ? parsed.permissions : [],
      projectId: parsed.projectId ?? '',
      projectName: parsed.projectName ?? '',
      // Identidades antigas (sem o campo) caem em false — fail-safe: user
      // perde acesso ao card "Chat" até reabrir o ProjectSelector e atualizar.
      chatDeploymentAllowed: parsed.chatDeploymentAllowed === true,
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
  return () => subscribers.delete(cb)
}

// Snapshot estável pra useSyncExternalStore — `cached` é reatribuído (não
// mutado in-place), então a referência é estável entre publishes.
function getSnapshot(): Identity | null {
  return cached
}

// Hook React pra componentes reagirem ao trocar de projeto/identidade.
// Substitui a chamada direta `getIdentity()` que não re-renderiza.
export function useIdentity(): Identity | null {
  return useSyncExternalStore(subscribeIdentity, getSnapshot, getSnapshot)
}
