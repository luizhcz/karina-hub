// Identidade do PM/PO persistida em localStorage. Campos:
// - name: display visual no header.
// - account: vai como header `x-efs-account` em toda chamada — backend trata
//   como userType=cliente.
// - projectId: define o scope das chamadas project-scoped (ex: generic-tools).
//   Vai como header `x-efs-project-id`.
// - projectName: cache do nome humano-legível pra exibir no header sem
//   precisar buscar a lista de projetos toda vez.

const STORAGE_KEY = 'efs-mvp-identity'

export interface Identity {
  name: string
  account: string
  projectId: string
  projectName: string
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
