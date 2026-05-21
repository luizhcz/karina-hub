import { useEffect, useState } from 'react'
import {
  listAgentVersions,
  type AgentVersionSummary,
} from '../api/agents'
import { friendlyError } from '../api/client'
import { Select } from '../ui'

interface Props {
  agentId: string
  /** Versão pinada selecionada. `null` = "Mais recente" (auto-pin no backend). */
  value: string | null
  onChange: (versionId: string | null) => void
  label?: string
  /** Esconde o picker quando o agente ainda não foi selecionado. */
  hideWhenAgentEmpty?: boolean
}

/** Sentinela do option "latest"; vazio bate com o placeholder default do Select. */
const LATEST_OPTION_VALUE = ''

function formatLabel(v: AgentVersionSummary, isCurrent: boolean): string {
  const date = v.createdAt ? new Date(v.createdAt).toLocaleDateString() : ''
  const prefix = `Versão ${v.revision}`
  const tail: string[] = []
  if (date) tail.push(date)
  if (isCurrent) tail.push('atual')
  if (v.breakingChange) tail.push('mudança incompatível')
  return tail.length > 0 ? `${prefix} (${tail.join(' • ')})` : prefix
}

/**
 * Picker secundário ao select de agente. Lista as versões publicadas do
 * agente e permite pinar uma revisão específica no workflow; o valor "Mais
 * recente" envia <c>agentVersionId=null</c>, deixando o backend resolver pra
 * versão Published corrente no momento do save.
 */
export function AgentVersionPicker({ agentId, value, onChange, label, hideWhenAgentEmpty }: Props) {
  const [versions, setVersions] = useState<AgentVersionSummary[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!agentId) {
      setVersions([])
      setError(null)
      return
    }
    let cancelled = false
    setLoading(true)
    setError(null)
    listAgentVersions(agentId)
      .then((items) => {
        if (cancelled) return
        // Backend pode devolver em qualquer ordem; renderiza desc por revision
        // pra que a versão atual fique no topo do dropdown.
        const sorted = [...items].sort((a, b) => b.revision - a.revision)
        setVersions(sorted)
      })
      .catch((err) => {
        if (cancelled) return
        setVersions([])
        setError(friendlyError(err, 'Falha ao carregar versões do agente.'))
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [agentId])

  if (!agentId && hideWhenAgentEmpty) return null

  const currentRevision = versions.length > 0 ? versions[0].revision : null
  const options = [
    {
      value: LATEST_OPTION_VALUE,
      label:
        currentRevision !== null
          ? `Sempre a mais recente (hoje versão ${currentRevision})`
          : 'Sempre a mais recente',
    },
    ...versions.map((v) => ({
      value: v.agentVersionId,
      label: formatLabel(v, v.revision === currentRevision),
    })),
  ]

  return (
    <Select
      label={label ?? 'Versão do agente'}
      value={value ?? LATEST_OPTION_VALUE}
      onChange={(e) => onChange(e.target.value === LATEST_OPTION_VALUE ? null : e.target.value)}
      options={options}
      disabled={!agentId || loading || (versions.length === 0 && !error)}
      hint={
        error
          ? undefined
          : loading
            ? 'Carregando versões…'
            : !agentId
              ? 'Selecione o agente primeiro.'
              : versions.length === 0
                ? 'Nenhuma versão publicada ainda.'
                : value === null
                  ? 'O deploy acompanha sempre a versão atual.'
                  : 'Travado em uma versão específica.'
      }
      error={error ?? undefined}
    />
  )
}
