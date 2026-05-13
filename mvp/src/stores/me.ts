import { useEffect, useState } from 'react'
import { getMe, type MeResponse } from '../api/me'

// Cache singleton da resposta de `/api/aihub/me`. Resolve uma única vez por
// carregamento de página — qualquer componente que chame `useIsAdmin()` ou
// `useMe()` reaproveita o resultado em memória. Evita N requests `/me` e
// elimina o ruído de 403 que aparecia quando cada tela non-admin tentava
// chamar seu endpoint protegido pra "descobrir" se era admin.

let cachedMe: MeResponse | null = null
let inflight: Promise<MeResponse> | null = null
const subscribers = new Set<(me: MeResponse) => void>()

function fetchMe(): Promise<MeResponse> {
  if (inflight) return inflight
  inflight = getMe()
    .catch(() => {
      // Falha de rede / endpoint indisponível: presume admin pra não bloquear
      // a UX. Backend ainda enforça via 403 no save de qualquer operação real.
      return { accountId: null, userType: null, isAdmin: true } as MeResponse
    })
    .then((me) => {
      cachedMe = me
      inflight = null
      subscribers.forEach((cb) => cb(me))
      return me
    })
  return inflight
}

/** Hook que retorna `boolean | null` — null enquanto ainda checando. */
export function useIsAdmin(): boolean | null {
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

  return me?.isAdmin ?? null
}

/** Versão imperativa pra uso fora de componentes. */
export function loadIsAdmin(): Promise<boolean> {
  return fetchMe().then((me) => me.isAdmin)
}
