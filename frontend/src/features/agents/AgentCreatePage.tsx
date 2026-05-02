import { useRef } from 'react'
import { Link, useNavigate } from 'react-router'
import { Button } from '../../shared/ui/Button'
import { useCreateAgent, useAgents } from '../../api/agents'
import {
  useAgentDrafts,
  useCreateAgentDraft,
} from '../../api/agentDrafts'
import { ApiError } from '../../api/client'
import { AgentForm } from './components/AgentForm'
import { formToRequest } from './formToRequest'
import { requestToDraftPayload } from './draftConverters'
import { toast } from '../../stores/toast'
import type { AgentFormValues } from './types'

export function AgentCreatePage() {
  const navigate = useNavigate()
  const createMutation = useCreateAgent()
  const createDraftMutation = useCreateAgentDraft()
  const { data: existingAgents } = useAgents()
  const { data: drafts } = useAgentDrafts()
  const existingIds = new Set([
    ...(existingAgents ?? []).map((a) => a.id),
    ...(drafts ?? []).map((d) => d.id),
  ])

  // Ref evita closure stale: o handleSubmit é envolvido por methods.handleSubmit
  // num render anterior ao click; ler `mode` via state aqui pegaria o valor velho
  // (ver react-hook-form async resolver). Ref muta sincronamente, o submit lê ok.
  const modeRef = useRef<'draft' | 'publish'>('draft')

  const handleSubmit = (values: AgentFormValues) => {
    const result = formToRequest(values)
    if (!result.ok) {
      toast.error(result.error)
      return
    }

    if (modeRef.current === 'publish') {
      createMutation.mutate(result.body, {
        onSuccess: () => {
          toast.success('Agente publicado.')
          navigate('/agents')
        },
        onError: (err) => {
          const msg = err instanceof ApiError ? err.message : 'Erro ao publicar agente.'
          toast.error(msg)
        },
      })
      return
    }

    createDraftMutation.mutate(
      { id: result.body.id, payload: requestToDraftPayload(result.body) },
      {
        onSuccess: (draft) => {
          toast.success('Rascunho salvo.')
          navigate(`/agents/drafts/${draft.id}`)
        },
        onError: (err) => {
          const msg = err instanceof ApiError ? err.message : 'Erro ao salvar rascunho.'
          toast.error(msg)
        },
      },
    )
  }

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center gap-3">
        <Link to="/agents">
          <Button variant="ghost" size="sm">
            &larr; Agentes
          </Button>
        </Link>
        <div>
          <h1 className="text-2xl font-bold text-text-primary">Novo Agente</h1>
          <p className="text-sm text-text-muted mt-1">
            Salve como rascunho para refinar antes ou publique direto.
          </p>
        </div>
      </div>

      <AgentForm
        onSubmit={handleSubmit}
        loading={createMutation.isPending || createDraftMutation.isPending}
        existingIds={existingIds}
        submitOverride={
          <div className="flex justify-end gap-2">
            <Button
              type="submit"
              variant="secondary"
              loading={createDraftMutation.isPending}
              onClick={() => { modeRef.current = 'draft' }}
            >
              Salvar rascunho
            </Button>
            <Button
              type="submit"
              loading={createMutation.isPending}
              onClick={() => { modeRef.current = 'publish' }}
            >
              Publicar agora
            </Button>
          </div>
        }
      />
    </div>
  )
}
