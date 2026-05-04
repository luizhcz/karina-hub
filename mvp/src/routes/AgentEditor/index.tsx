import { useEffect, useMemo, useState } from 'react'
import { useNavigate, useParams, useSearchParams } from 'react-router'
import {
  createAgentDraft,
  generateDraftId,
  getAgentDraft,
  submitAgentDraft,
  updateAgentDraft,
  type AgentDraft,
  type AgentDraftStatus,
} from '../../api/agentDrafts'
import { listPredefinedModels, type PredefinedModel } from '../../api/predefinedModels'
import { listGenericTools, type GenericTool } from '../../api/genericTools'
import { listMcpServers, type McpServer } from '../../api/mcpServers'
import { ApiError, friendlyError } from '../../api/client'
import {
  ArrowLeftIcon,
  ArrowRightIcon,
  Badge,
  Button,
  Card,
  CardHeader,
  ErrorMessage,
  Modal,
  Spinner,
  cn,
} from '../../ui'
import { Stepper, type StepDescriptor } from './Stepper'
import { ProfileStep } from './ProfileStep'
import { ToolsKnowledgeStep } from './ToolsKnowledgeStep'
import { InputStep } from './InputStep'
import { OutputStep } from './OutputStep'
import { ModelStep } from './ModelStep'
import { ReviewStep } from './ReviewStep'
import { buildPayload, emptyFormState, fromDraft } from './formCodec'
import type { AgentMode, FormState, StepKey } from './types'

interface Props {
  mode: 'create' | 'edit'
}

const STATUS_TONE: Record<AgentDraftStatus, 'neutral' | 'accent' | 'warning'> = {
  Draft: 'neutral',
  PendingApproval: 'accent',
  Rejected: 'warning',
}

const STATUS_LABEL: Record<AgentDraftStatus, string> = {
  Draft: 'Rascunho',
  PendingApproval: 'Aguardando aprovação',
  Rejected: 'Rejeitado',
}

const BASIC_STEPS: StepDescriptor[] = [
  { key: 'profile', label: 'Perfil' },
  { key: 'tools', label: 'Ferramentas' },
  { key: 'model', label: 'Modelo' },
  { key: 'review', label: 'Revisão' },
]

const ADVANCED_STEPS: StepDescriptor[] = [
  { key: 'profile', label: 'Perfil' },
  { key: 'tools', label: 'Ferramentas' },
  { key: 'input', label: 'Input' },
  { key: 'output', label: 'Output' },
  { key: 'model', label: 'Modelo' },
  { key: 'review', label: 'Revisão' },
]

function stepsFor(mode: AgentMode): StepDescriptor[] {
  return mode === 'advanced' ? ADVANCED_STEPS : BASIC_STEPS
}

export function AgentEditor({ mode }: Props) {
  const navigate = useNavigate()
  const { id } = useParams<{ id?: string }>()
  const [searchParams] = useSearchParams()

  // No fluxo de criação, ?mode=advanced (ou ?mode=basic) define o modo inicial
  // — a tela é aberta a partir do modal de "Novo agente" da listagem com esse
  // parâmetro. Em edit, o modo é inferido do conteúdo do draft pelo formCodec.
  const initialMode = mode === 'create' && searchParams.get('mode') === 'advanced'
    ? 'advanced'
    : 'basic'
  const [form, setForm] = useState<FormState>(() => ({
    ...emptyFormState(),
    agentMode: initialMode,
  }))
  const [draft, setDraft] = useState<AgentDraft | null>(null)
  const [loading, setLoading] = useState(mode === 'edit')
  const [loadError, setLoadError] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)
  const [submittingApproval, setSubmittingApproval] = useState(false)
  const [confirmSubmit, setConfirmSubmit] = useState(false)

  const [models, setModels] = useState<PredefinedModel[]>([])
  const [modelsLoading, setModelsLoading] = useState(true)
  const [modelsError, setModelsError] = useState<string | null>(null)

  const [tools, setTools] = useState<GenericTool[]>([])
  const [toolsLoading, setToolsLoading] = useState(true)
  const [toolsError, setToolsError] = useState<string | null>(null)

  const [mcps, setMcps] = useState<McpServer[]>([])
  const [mcpsLoading, setMcpsLoading] = useState(true)
  const [mcpsError, setMcpsError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    setModelsLoading(true)
    listPredefinedModels()
      .then((list) => {
        if (!cancelled) setModels(list)
      })
      .catch((err: unknown) => {
        if (!cancelled) setModelsError(friendlyError(err, 'Não foi possível carregar os modelos.'))
      })
      .finally(() => {
        if (!cancelled) setModelsLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [])

  useEffect(() => {
    let cancelled = false
    setToolsLoading(true)
    listGenericTools()
      .then((list) => {
        if (!cancelled) setTools(list)
      })
      .catch((err: unknown) => {
        if (!cancelled) setToolsError(friendlyError(err, 'Não foi possível carregar as ferramentas.'))
      })
      .finally(() => {
        if (!cancelled) setToolsLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [])

  useEffect(() => {
    let cancelled = false
    setMcpsLoading(true)
    listMcpServers()
      .then((list) => {
        if (!cancelled) setMcps(list)
      })
      .catch((err: unknown) => {
        if (!cancelled) setMcpsError(friendlyError(err, 'Não foi possível carregar os MCPs.'))
      })
      .finally(() => {
        if (!cancelled) setMcpsLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [])

  useEffect(() => {
    if (mode !== 'edit' || !id) return
    let cancelled = false
    setLoading(true)
    getAgentDraft(id)
      .then((d) => {
        if (cancelled) return
        setDraft(d)
        setForm(fromDraft(d))
      })
      .catch((err: unknown) => {
        if (cancelled) return
        setLoadError(friendlyError(err, 'Não foi possível carregar o rascunho.'))
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [mode, id])

  const status: AgentDraftStatus = draft?.status ?? 'Draft'
  const isPending = status === 'PendingApproval'
  const canSubmit = status === 'Draft' || status === 'Rejected'
  const readonly = isPending

  const steps = useMemo(() => stepsFor(form.agentMode), [form.agentMode])
  const currentIndex = Math.max(
    steps.findIndex((s) => s.key === form.currentStep),
    0,
  )
  const isFirst = currentIndex === 0
  const isLast = currentIndex === steps.length - 1

  const goTo = (key: StepKey) => setForm((prev) => ({ ...prev, currentStep: key }))
  const goNext = () => {
    const next = steps[currentIndex + 1]
    if (next) goTo(next.key)
  }
  const goPrev = () => {
    const prev = steps[currentIndex - 1]
    if (prev) goTo(prev.key)
  }

  const setMode = (next: AgentMode) => {
    if (next === form.agentMode) return
    setForm((prev) => {
      const nextSteps = stepsFor(next)
      // Mantém step se ainda existe na nova lista; senão, vai pra "tools"
      // (último step comum entre os dois modos antes da Revisão).
      const stillExists = nextSteps.some((s) => s.key === prev.currentStep)
      return {
        ...prev,
        agentMode: next,
        currentStep: stillExists ? prev.currentStep : 'tools',
      }
    })
  }

  const reloadDraft = async (draftId: string) => {
    try {
      const fresh = await getAgentDraft(draftId)
      setDraft(fresh)
      setForm(fromDraft(fresh))
    } catch {
      // erro silencioso — o usuário ainda pode tentar manualmente.
    }
  }

  const onSave = async () => {
    if (readonly) return
    setError(null)
    setSubmitting(true)
    try {
      if (mode === 'edit' && id && draft) {
        const payload = buildPayload(draft.payload, form, tools, mcps)
        const updated = await updateAgentDraft(id, {
          payload,
          expectedUpdatedAt: draft.updatedAt,
        })
        setDraft(updated)
        setForm((prev) => ({ ...fromDraft(updated), currentStep: prev.currentStep, agentMode: prev.agentMode }))
      } else {
        const payload = buildPayload(undefined, form, tools, mcps)
        const created = await createAgentDraft({ id: generateDraftId(), payload })
        navigate(`/agentes/${created.id}`, { replace: true })
      }
    } catch (err) {
      if (err instanceof ApiError && err.status === 412 && id) {
        setError('Esse rascunho foi alterado em outro lugar. Recarregamos com os valores mais recentes — confira antes de salvar de novo.')
        await reloadDraft(id)
      } else {
        setError(friendlyError(err, 'Não foi possível salvar o rascunho.'))
      }
    } finally {
      setSubmitting(false)
    }
  }

  const validateForSubmit = (): string | null => {
    if (!form.name.trim()) return 'Informe um nome antes de submeter.'
    if (!form.predefinedModelId.trim()) return 'Selecione um modelo antes de submeter.'
    return null
  }

  const onConfirmSubmit = async () => {
    if (!id) return
    const validation = validateForSubmit()
    if (validation) {
      setError(validation)
      setConfirmSubmit(false)
      return
    }
    setError(null)
    setSubmittingApproval(true)
    try {
      if (draft) {
        const payload = buildPayload(draft.payload, form, tools, mcps)
        const updated = await updateAgentDraft(id, {
          payload,
          expectedUpdatedAt: draft.updatedAt,
        })
        setDraft(updated)
      }
      const result = await submitAgentDraft(id)
      setConfirmSubmit(false)
      navigate(result.autoApproved ? '/agentes?tab=published' : '/agentes', { replace: true })
    } catch (err) {
      if (err instanceof ApiError && err.status === 412) {
        setError('O rascunho foi alterado em paralelo. Recarregamos os valores — revise antes de submeter de novo.')
        await reloadDraft(id)
      } else {
        setError(friendlyError(err, 'Não foi possível submeter o rascunho.'))
      }
      setConfirmSubmit(false)
    } finally {
      setSubmittingApproval(false)
    }
  }

  if (loading) {
    return (
      <Card className="mx-auto max-w-4xl flex items-center justify-center py-12">
        <Spinner className="h-6 w-6 text-fg-muted" />
      </Card>
    )
  }
  if (loadError) {
    return <ErrorMessage message={loadError} className="mx-auto max-w-4xl" />
  }

  const titleText = mode === 'edit'
    ? form.name.trim() || draft?.name || 'Rascunho sem nome'
    : 'Novo agente'

  return (
    <div className="mx-auto max-w-4xl pb-28">
      <div className="mb-5 flex items-center justify-between gap-4">
        <div className="min-w-0">
          <Button
            variant="ghost"
            size="sm"
            leftIcon={<ArrowLeftIcon className="h-4 w-4" />}
            onClick={() => navigate('/agentes')}
            className="-ml-2 mb-1"
          >
            Voltar
          </Button>
          <div className="flex items-center gap-3">
            <h1 className="truncate text-2xl font-semibold tracking-tight">{titleText}</h1>
            {mode === 'edit' && (
              <Badge tone={STATUS_TONE[status]}>{STATUS_LABEL[status]}</Badge>
            )}
          </div>
        </div>
      </div>

      {isPending && (
        <Card className="mb-5 border-accent/40 bg-accent-subtle">
          <p className="text-sm font-medium text-accent">
            Aguardando aprovação. O rascunho não pode ser editado neste momento — você verá a
            decisão aqui assim que for processada.
          </p>
        </Card>
      )}

      {status === 'Rejected' && draft?.rejectionFeedback && (
        <Card className="mb-5 border-warning/40 bg-warning/10">
          <p className="text-xs font-semibold uppercase tracking-wider text-warning">Rejeitado</p>
          <p className="mt-1 text-sm text-fg">{draft.rejectionFeedback}</p>
          <p className="mt-2 text-xs text-fg-muted">
            Ajuste os pontos acima e submeta novamente quando estiver pronto.
          </p>
        </Card>
      )}

      <Card className="mb-5 space-y-4" padded>
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div>
            <p className="text-xs font-semibold uppercase tracking-wider text-fg-dim">
              Tipo de agente
            </p>
            <p className="mt-1 text-xs text-fg-muted">
              {form.agentMode === 'basic'
                ? 'Configuração rápida com perfil, ferramentas e revisão.'
                : 'Inclui input e output estruturados além do básico.'}
            </p>
          </div>
          <div className="flex rounded-lg border border-border bg-bg-soft p-1">
            <ModeToggleButton
              active={form.agentMode === 'basic'}
              label="Básico"
              onClick={() => setMode('basic')}
              disabled={readonly}
            />
            <ModeToggleButton
              active={form.agentMode === 'advanced'}
              label="Avançado"
              onClick={() => setMode('advanced')}
              disabled={readonly}
            />
          </div>
        </div>
        <Stepper
          steps={steps}
          current={form.currentStep}
          onSelect={goTo}
          disabled={readonly}
        />
      </Card>

      <div className="mb-5">
        {form.currentStep === 'profile' && (
          <ProfileStep form={form} setForm={setForm} readonly={readonly} />
        )}
        {form.currentStep === 'tools' && (
          <ToolsKnowledgeStep
            form={form}
            setForm={setForm}
            tools={tools}
            toolsLoading={toolsLoading}
            toolsError={toolsError}
            mcps={mcps}
            mcpsLoading={mcpsLoading}
            mcpsError={mcpsError}
            readonly={readonly}
          />
        )}
        {form.currentStep === 'input' && form.agentMode === 'advanced' && (
          <InputStep form={form} setForm={setForm} readonly={readonly} />
        )}
        {form.currentStep === 'output' && form.agentMode === 'advanced' && (
          <OutputStep form={form} setForm={setForm} readonly={readonly} />
        )}
        {form.currentStep === 'model' && (
          <ModelStep
            form={form}
            setForm={setForm}
            models={models}
            modelsLoading={modelsLoading}
            modelsError={modelsError}
            readonly={readonly}
          />
        )}
        {form.currentStep === 'review' && (
          <div className="space-y-5">
            <ReviewStep form={form} models={models} tools={tools} mcps={mcps} />

            {mode === 'edit' && canSubmit && (
              <Card className="space-y-3 border-accent/40 bg-accent-subtle/40">
                <CardHeader
                  title="Submeter para aprovação"
                  description="Envie este rascunho pra revisão. Depois de submeter, o agente fica em modo somente leitura até a decisão."
                />
                <Button
                  className="w-full sm:w-auto"
                  onClick={() => {
                    const validation = validateForSubmit()
                    if (validation) {
                      setError(validation)
                      return
                    }
                    setError(null)
                    setConfirmSubmit(true)
                  }}
                >
                  {status === 'Rejected' ? 'Submeter novamente' : 'Submeter para aprovação'}
                </Button>
              </Card>
            )}
          </div>
        )}
      </div>

      {error && <ErrorMessage message={error} className="mb-4" />}

      {mode === 'edit' && draft && (
        <p className="mt-2 text-[11px] text-fg-dim">
          Atualizado em {new Date(draft.updatedAt).toLocaleString('pt-BR')}
          {draft.createdBy ? ` por ${draft.createdBy}` : ''}.
        </p>
      )}

      {/* Botões flutuantes no rodapé — ancorados ao mesmo grid do conteúdo
          (left-60 = sidebar; mx-auto max-w-4xl = mesmo eixo do step).
          pointer-events-none no wrapper deixa o conteúdo atrás clicável;
          pointer-events-auto reativa só nos botões. shadow-xl dá elevação
          sem fundo de footer. */}
      <div className="pointer-events-none fixed bottom-6 left-60 right-0 z-30 px-8">
        <div className="mx-auto flex max-w-4xl items-center justify-between">
          <div className="pointer-events-auto">
            {!isFirst && (
              <Button
                variant="secondary"
                onClick={goPrev}
                disabled={readonly}
                leftIcon={<ArrowLeftIcon className="h-4 w-4" />}
                className="shadow-xl"
              >
                Voltar
              </Button>
            )}
          </div>

          <div className="pointer-events-auto">
            {!isLast ? (
              <Button
                onClick={goNext}
                disabled={readonly}
                rightIcon={<ArrowRightIcon className="h-4 w-4" />}
                className="shadow-xl"
              >
                Avançar
              </Button>
            ) : (
              <Button onClick={onSave} loading={submitting} disabled={readonly} className="shadow-xl">
                {mode === 'edit' ? 'Salvar' : 'Criar rascunho'}
              </Button>
            )}
          </div>
        </div>
      </div>

      <Modal
        open={confirmSubmit}
        onClose={() => setConfirmSubmit(false)}
        title="Submeter para aprovação"
        description="Depois de submeter, o rascunho fica em modo somente leitura até ser aprovado ou rejeitado."
        footer={
          <div className="flex justify-end gap-2">
            <Button variant="ghost" onClick={() => setConfirmSubmit(false)}>
              Cancelar
            </Button>
            <Button onClick={onConfirmSubmit} loading={submittingApproval}>
              Submeter
            </Button>
          </div>
        }
      >
        <p className="text-sm text-fg-muted">
          Vamos salvar as alterações em aberto e enviar este rascunho para aprovação. Tudo certo?
        </p>
      </Modal>
    </div>
  )
}

interface ModeToggleButtonProps {
  active: boolean
  label: string
  onClick: () => void
  disabled?: boolean
}

function ModeToggleButton({ active, label, onClick, disabled }: ModeToggleButtonProps) {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      className={cn(
        'rounded-md px-3 py-1.5 text-xs font-medium transition focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent/30',
        disabled && 'cursor-not-allowed opacity-60',
        active
          ? 'bg-surface text-fg shadow-soft'
          : 'text-fg-muted hover:text-fg',
      )}
    >
      {label}
    </button>
  )
}
