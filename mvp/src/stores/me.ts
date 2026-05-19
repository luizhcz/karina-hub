import { useEffect, useState } from 'react'
import { getMe, type MeResponse, type ProjectRef } from '../api/me'
import { subscribeIdentity, getIdentity, setIdentity } from './identity'

// Cache singleton da resposta de `/api/aihub/me`. Resolve uma única vez por
// carregamento de página — qualquer componente que chame `useIsAdmin()`,
// `useMe()` ou `useProjects()` reaproveita o resultado em memória. Evita N
// requests `/me` e elimina o ruído de 403 que aparecia quando cada tela
// non-admin tentava chamar seu endpoint protegido pra "descobrir" se era admin.

let cachedMe: MeResponse | null = null
let inflight: Promise<MeResponse> | null = null
const subscribers = new Set<(me: MeResponse) => void>()

function fetchMe(): Promise<MeResponse> {
  if (inflight) return inflight
  inflight = getMe()
    .catch(() => {
      // Falha de rede / endpoint indisponível: assume não-admin pra fail-safe.
      // Esconder UI admin é melhor que mostrar pra quem não é. Operações reais
      // ainda são gateadas no backend via 403.
      return { accountId: null, userType: null, isAdmin: false, permissions: [], projects: [] } as MeResponse
    })
    .then((me) => {
      cachedMe = me
      inflight = null
      syncIdentityFromMe(me)
      subscribers.forEach((cb) => cb(me))
      return me
    })
  return inflight
}

// Invalida cachedMe sempre que a identidade local muda (logout, troca de
// usuário no Onboarding). Sem isso, próxima renderização veria dados do
// usuário anterior por um frame.
let lastIdentityAccount: string | null | undefined = getIdentity()?.account
subscribeIdentity(() => {
  const current = getIdentity()?.account
  if (current !== lastIdentityAccount) {
    lastIdentityAccount = current
    cachedMe = null
    inflight = null
  }
})

/**
 * Reconcilia o identity store local com o que o backend resolveu via /me.
 * 3 cenários:
 *   1. Sem identity local + /me devolveu accountId → cria identity completa
 *      (caso do fluxo via proxy: usuário entrou com access_token na URL,
 *      proxy traduziu pra x-efs-*, /me reportou quem é).
 *   2. Identity local com account divergente do accountId reportado → trata
 *      como troca de usuário e sobrescreve (defesa contra estado stale).
 *   3. Mesmo account → patch incremental de permissions.
 * Identity sem accountId no /me (anônimo) não dispara nada — fluxo dev local
 * sem proxy mantém o Onboarding manual.
 */
function syncIdentityFromMe(me: MeResponse): void {
  if (!me.accountId || !me.userType) return
  const current = getIdentity()
  const fromMe = {
    name: me.displayName ?? me.accountId,
    account: me.accountId,
    userType: me.userType,
    permissions: me.permissions ?? [],
    projectId: current?.projectId ?? '',
    projectName: current?.projectName ?? '',
    chatDeploymentAllowed: current?.chatDeploymentAllowed ?? false,
  }
  if (!current || current.account !== me.accountId) {
    setIdentity(fromMe)
    return
  }
  setIdentity({ ...current, ...fromMe })
}

/** Hook que retorna `boolean | null` — null enquanto ainda checando. */
export function useIsAdmin(): boolean | null {
  const me = useMe()
  return me?.isAdmin ?? null
}

/** Hook que retorna a resposta completa de /me ou null enquanto carrega. */
export function useMe(): MeResponse | null {
  const [me, setMe] = useState<MeResponse | null>(cachedMe)

  useEffect(() => {
    if (cachedMe) return
    let cancelled = false
    const sub = (next: MeResponse) => {
      if (!cancelled) setMe(next)
    }
    subscribers.add(sub)
    void fetchMe().then((next) => {
      if (!cancelled) setMe(next)
    })
    return () => {
      cancelled = true
      subscribers.delete(sub)
    }
  }, [])

  return me
}

/** Lista de projetos visíveis ao usuário corrente. null enquanto carrega. */
export function useProjects(): ProjectRef[] | null {
  const me = useMe()
  return me ? me.projects : null
}

/** Versão imperativa pra uso fora de componentes. */
export function loadIsAdmin(): Promise<boolean> {
  return fetchMe().then((me) => me.isAdmin)
}

/**
 * Força refetch de /me — usado após mudanças que afetam o próprio user
 * (ex.: admin vincula projeto e o user pede pra atualizar a tela welcome).
 */
export function refreshMe(): Promise<MeResponse> {
  cachedMe = null
  inflight = null
  return fetchMe()
}
