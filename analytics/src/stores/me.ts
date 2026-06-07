import { useEffect, useState } from 'react'
import { getMe, type MeResponse, type ProjectRef } from '../api/me'
import { subscribeIdentity, getIdentity, setIdentity } from './identity'

// Cache singleton da resposta de `/api/aihub/me`. Resolve uma única vez por
// carregamento de página — qualquer componente que chame useMe()/useProjects()
// reaproveita o resultado em memória. Evita N requests `/me` quando vários
// widgets do dashboard montam ao mesmo tempo.

let cachedMe: MeResponse | null = null
let inflight: Promise<MeResponse> | null = null
const subscribers = new Set<(me: MeResponse) => void>()

function fetchMe(): Promise<MeResponse> {
  if (inflight) return inflight
  inflight = getMe()
    .catch(() => {
      // Falha de rede / endpoint indisponível: assume sessão anônima.
      // UI cai no caminho "sem identity" e mostra mensagem amigável; operações
      // reais ainda são gateadas no backend.
      return {
        accountId: null,
        userType: null,
        isAdmin: false,
        permissions: [],
        projects: [],
      } as MeResponse
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

// Invalida cachedMe quando a identidade local muda (logout, troca de usuário).
// Sem isso, o próximo render veria dados do usuário anterior por um frame.
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
 * Mesma lógica do mvp/: cria identity quando ausente, sobrescreve em troca de
 * usuário, patch incremental quando o account bate.
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
    tenantId: me.tenantId ?? undefined,
  }
  if (!current || current.account !== me.accountId) {
    setIdentity(fromMe)
    return
  }
  setIdentity({ ...current, ...fromMe })
}

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

export function useProjects(): ProjectRef[] | null {
  const me = useMe()
  return me ? me.projects : null
}

export function refreshMe(): Promise<MeResponse> {
  cachedMe = null
  inflight = null
  return fetchMe()
}
