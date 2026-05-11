import { useSyncExternalStore } from 'react'

// Identidade do PM/PO persistida em localStorage. Campos:
// - name: display visual no header.
// - account: vai como header `x-efs-account` em toda chamada — backend trata
//   como userType=cliente.
// - projectId: define o scope das chamadas project-scoped (ex: generic-tools).
//   Vai como header `x-efs-project-id`.
// - projectName: cache do nome humano-legível pra exibir no header sem
//   precisar buscar a lista de projetos toda vez.
// - chatDeploymentAllowed: cache da flag do projeto (vem de
//   ProjectResponse.chatDeploymentAllowed). Usado pra desabilitar o card
//   "Chat" no modal de nova implantação sem precisar buscar o projeto.

const STORAGE_KEY = 'efs-mvp-identity'

export interface Identity {
  name: string
  account: string
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
    if (!parsed.name || !parsed.account) return null
    return {
      name: parsed.name,
      account: parsed.account,
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
