import { useEffect, useMemo, useRef, useState } from 'react'
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
  AssistantFailureError,
  AssistantTimeoutError,
  analisarPerfil,
  countCriticas,
  hashInput,
  type CampoPerfil,
  type ProfileInput,
  type RefinamentoPerfilOutput,
} from '../../api/profileAssistant'
import {
  ArrowLeftIcon,
  ArrowRightIcon,
  Badge,
  Button,
  Card,
  ErrorMessage,
  Modal,
  SparklesIcon,
  Spinner,
  cn,
} from '../../ui'
import { Stepper, type StepDescriptor } from './Stepper'
import { ProfileStep } from './ProfileStep'
import { ToolsKnowledgeStep } from './ToolsKnowledgeStep'
import { SecurityStep } from './SecurityStep'
import { MemoryStep } from './MemoryStep'
import { InputStep } from './InputStep'
import { OutputStep } from './OutputStep'
import { ModelStep } from './ModelStep'
import { ReviewStep } from './ReviewStep'
import { AssistantDrawer } from './AssistantDrawer'
import { buildPayload, emptyFormState, fromDraft } from './formCodec'
import { AGENT_TEMPLATES } from './templates'
import type { AgentMode, FormState, StepKey } from './types'

const COOLDOWN_MS = 10_000
const MIN_ROLE_CHARS = 20

function profileInputFrom(form: FormState): ProfileInput {
  return {
    name: form.name,
    role: form.profile.role,
    goal: form.profile.goal,
    backstory: form.profile.backstory,
    rules: form.profile.rules.join('\n'),
    constraints: form.profile.constraints.join('\n'),
  }
}

function fieldValuesFrom(form: FormState): Record<CampoPerfil, string> {
  return {
    name: form.name,
    description: '',
    role: form.profile.role,
    goal: form.profile.goal,
    backstory: form.profile.backstory,
    rules: form.profile.rules.join('\n'),
    constraints: form.profile.constraints.join('\n'),
  }
}

function splitListField(value: string): string[] {
  return value
    .split('\n')
    .map((s) => s.trim())
    .filter((s) => s.length > 0)
}

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
  { key: 'security', label: 'Segurança' },
  { key: 'memory', label: 'Memória' },
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
  // ?template=<key> hidrata Profile + nome + descrição com um modelo pronto
  // (ver routes/AgentEditor/templates.ts) — atalho pro time-to-first-agent.
  const initialMode = mode === 'create' && searchParams.get('mode') === 'advanced'
    ? 'advanced'
    : 'basic'
  const initialTemplateKey = mode === 'create' ? searchParams.get('template') : null
  const [form, setForm] = useState<FormState>(() => {
    const base: FormState = { ...emptyFormState(), agentMode: initialMode }
    if (!initialTemplateKey) return base
    const tpl = AGENT_TEMPLATES.find((t) => t.key === initialTemplateKey)
    if (!tpl) return base
    return {
      ...base,
      name: tpl.defaults.name,
      profile: { ...tpl.defaults.profile },
    }
  })
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

  // Estado do assistente de refinamento (Onda 1.5).
  const [assistantOpen, setAssistantOpen] = useState(false)
  const [assistantLoading, setAssistantLoading] = useState(false)
  const [assistantResult, setAssistantResult] = useState<RefinamentoPerfilOutput | null>(null)
  const [assistantError, setAssistantError] = useState<string | null>(null)
  const [cooldownUntil, setCooldownUntil] = useState<number>(0)
  const [now, setNow] = useState(() => Date.now())
  const lastInputRef = useRef<{ hash: string; result: RefinamentoPerfilOutput; ts: number } | null>(null)
  const analysisHashRef = useRef<string | null>(null)
  const fieldsTouchedRef = useRef<Set<CampoPerfil>>(new Set())
  const abortRef = useRef<AbortController | null>(null)
  const [confirmFrustration, setConfirmFrustration] = useState(false)
  const frustrationDismissedRef = useRef(false)

  // Tick pra atualizar o countdown do cooldown só enquanto ele está ativo.
  useEffect(() => {
    if (cooldownUntil <= now) return
    const t = setInterval(() => setNow(Date.now()), 250)
    return () => clearInterval(t)
  }, [cooldownUntil, now])

  useEffect(() => () => abortRef.current?.abort(), [])

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

  // Pendências obrigatórias por step. Evita o user descobrir falta de
  // nome/modelo só na Revisão — agora o Stepper sinaliza desde o início.
  // Critério: o que `validateForSubmit` cobre, replicado por step de origem.
  const stepIssues = useMemo<Partial<Record<StepKey, string>>>(() => {
    const issues: Partial<Record<StepKey, string>> = {}
    if (!form.name.trim()) issues.profile = 'Informe um nome'
    if (!form.predefinedModelId.trim()) issues.model = 'Selecione um modelo'
    if (form.memory.enabled) {
      const trimmed = form.memory.schema.trim()
      if (!trimmed) {
        issues.memory = 'Defina a estrutura da memória ou desative.'
      } else {
        try {
          const parsed = JSON.parse(trimmed)
          if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) {
            issues.memory = 'Schema da memória precisa ser um objeto JSON.'
          }
        } catch {
          issues.memory = 'Schema da memória não é JSON válido.'
        }
      }
    }
    return issues
  }, [form.name, form.predefinedModelId, form.memory.enabled, form.memory.schema])

  const goTo = (key: StepKey) => setForm((prev) => ({ ...prev, currentStep: key }))
  const goNext = () => {
    const next = steps[currentIndex + 1]
    if (next) goTo(next.key)
  }
  const goPrev = () => {
    const prev = steps[currentIndex - 1]
    if (prev) goTo(prev.key)
  }

  // Steps que o user já visitou — usado pelo Stepper pra fixar o verde (não
  // depende mais de currentIndex). Em modo edit começa com tudo visitado, já
  // que o agente publicado tem todos os steps válidos. Em create, só o step
  // inicial conta como visitado e o set cresce conforme o user avança.
  const [visitedSteps, setVisitedSteps] = useState<Set<StepKey>>(() => {
    if (mode === 'edit') return new Set(stepsFor(initialMode).map((s) => s.key))
    return new Set([form.currentStep])
  })

  useEffect(() => {
    setVisitedSteps((prev) => {
      if (prev.has(form.currentStep)) return prev
      const next = new Set(prev)
      next.add(form.currentStep)
      return next
    })
  }, [form.currentStep])

  const setMode = (next: AgentMode) => {
    if (next === form.agentMode) return
    setForm((prev) => {
      const nextSteps = stepsFor(next)
      // Mantém step se ainda existe na nova lista; senão, vai pra "tools"
      // (último step comum entre os dois modos antes da Revisão).
      const stillExists = nextSteps.some((s) => s.key === prev.currentStep)
      // Memória vive só no modo avançado: descer pra basic preserva o schema
      // editado mas desliga o toggle pra que o backend não receba a config sem
      // que o user veja onde editá-la. Subir pra advanced restaura o que estava.
      const nextMemory =
        next === 'basic' && prev.memory.enabled
          ? { ...prev.memory, enabled: false }
          : prev.memory
      // Mesma lógica do toggle de memória: o step de Segurança só aparece em
      // advanced; descer pra basic desliga o middleware pra evitar config
      // ativa que o user não vê na UI.
      const nextSecurity =
        next === 'basic' && prev.security.enabled
          ? { ...prev.security, enabled: false }
          : prev.security
      return {
        ...prev,
        agentMode: next,
        currentStep: stillExists ? prev.currentStep : 'tools',
        memory: nextMemory,
        security: nextSecurity,
      }
    })
  }

  // ── Assistente de Refinamento ────────────────────────────────────────
  const cooldownRemaining = Math.max(0, Math.ceil((cooldownUntil - now) / 1000))
  const onCooldown = cooldownRemaining > 0
  const roleReady = form.profile.role.trim().length >= MIN_ROLE_CHARS
  const assistantDisabled = readonly || !roleReady || onCooldown || assistantLoading

  const onAssistantApply = (campo: CampoPerfil, value: string) => {
    setForm((prev) => {
      const next = { ...prev }
      switch (campo) {
        case 'name':
          next.name = value
          break
        case 'role':
          next.profile = { ...prev.profile, role: value }
          break
        case 'goal':
          next.profile = { ...prev.profile, goal: value }
          break
        case 'backstory':
          next.profile = { ...prev.profile, backstory: value }
          break
        case 'rules':
          next.profile = { ...prev.profile, rules: splitListField(value) }
          break
        case 'constraints':
          next.profile = { ...prev.profile, constraints: splitListField(value) }
          break
        // 'description' não tem campo no wizard — sugestões para esse campo
        // são filtradas no AssistantDrawer via groupByCampo (não rendem grupo).
      }
      return next
    })
    fieldsTouchedRef.current.add(campo)
  }

  const runAnalysis = async () => {
    if (assistantDisabled) return
    const input = profileInputFrom(form)
    const h = hashInput(input)

    // Cache local: input idêntico nos últimos 60s reusa o resultado sem
    // bater no backend (anti-abuso + reduz custo).
    const cached = lastInputRef.current
    if (cached && cached.hash === h && Date.now() - cached.ts < 60_000) {
      setAssistantResult(cached.result)
      setAssistantError(null)
      setAssistantOpen(true)
      analysisHashRef.current = h
      fieldsTouchedRef.current = new Set()
      return
    }

    abortRef.current?.abort()
    const controller = new AbortController()
    abortRef.current = controller

    setAssistantOpen(true)
    setAssistantLoading(true)
    setAssistantError(null)
    setAssistantResult(null)
    try {
      const result = await analisarPerfil(input, controller.signal)
      setAssistantResult(result)
      lastInputRef.current = { hash: h, result, ts: Date.now() }
      analysisHashRef.current = h
      fieldsTouchedRef.current = new Set()
      const until = Date.now() + COOLDOWN_MS
      setCooldownUntil(until)
      setNow(Date.now())
    } catch (err) {
      if ((err as Error)?.name === 'AbortError') return
      if (err instanceof AssistantTimeoutError) {
        setAssistantError('O assistente demorou demais — tente novamente em instantes.')
      } else if (err instanceof AssistantFailureError) {
        setAssistantError('Indisponível agora. Você pode salvar normalmente e tentar de novo depois.')
      } else {
        setAssistantError(friendlyError(err, 'Não foi possível analisar o perfil.'))
      }
    } finally {
      setAssistantLoading(false)
    }
  }

  const closeAssistant = () => {
    abortRef.current?.abort()
    setAssistantOpen(false)
    setAssistantLoading(false)
  }

  const criticasPendentes = useMemo(() => {
    if (!assistantResult) return 0
    const currentHash = hashInput(profileInputFrom(form))
    // Se o usuário já editou desde a análise (hash mudou), zera a contagem.
    // Mantemos a contagem se ele só clicou em campos sem alterar conteúdo.
    if (analysisHashRef.current && currentHash !== analysisHashRef.current) return 0
    return countCriticas(assistantResult.sugestoes)
  }, [assistantResult, form])

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
    if (form.memory.enabled) {
      const trimmed = form.memory.schema.trim()
      if (!trimmed) return 'Memória operacional ativa exige um schema — defina a estrutura ou desative.'
      try {
        const parsed = JSON.parse(trimmed)
        if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed))
          return 'Schema da memória operacional precisa ser um objeto JSON.'
      } catch {
        return 'Schema da memória operacional não é JSON válido.'
      }
    }
    return null
  }

  const tryOpenSubmit = () => {
    const validation = validateForSubmit()
    if (validation) {
      setError(validation)
      return
    }
    setError(null)
    if (criticasPendentes >= 3 && !frustrationDismissedRef.current) {
      setConfirmFrustration(true)
      return
    }
    setConfirmSubmit(true)
  }

  const onConfirmSubmit = async () => {
    const validation = validateForSubmit()
    if (validation) {
      setError(validation)
      setConfirmSubmit(false)
      return
    }
    setError(null)
    setSubmittingApproval(true)
    try {
      let draftId = id
      if (mode === 'create' || !draftId) {
        // Create mode: cria o draft inline antes de submeter (1 fluxo, sem
        // viagem extra pra /agentes/{id} no meio).
        const payload = buildPayload(undefined, form, tools, mcps)
        const created = await createAgentDraft({ id: generateDraftId(), payload })
        draftId = created.id
        setDraft(created)
      } else if (draft) {
        const payload = buildPayload(draft.payload, form, tools, mcps)
        const updated = await updateAgentDraft(draftId, {
          payload,
          expectedUpdatedAt: draft.updatedAt,
        })
        setDraft(updated)
      }
      const result = await submitAgentDraft(draftId!)
      setConfirmSubmit(false)
      // Flash de confirmação consumido pelo AgentsList via location.state.
      // Garante feedback explícito de "submeti, e agora?" — sem isso o user
      // vê só a lista e não sabe se a ação chegou.
      const flash = result.autoApproved
        ? {
            tone: 'success' as const,
            title: 'Edição cosmética aprovada automaticamente',
            body: 'A nova versão do agente já está em produção.',
          }
        : {
            tone: 'accent' as const,
            title: 'Rascunho enviado para aprovação',
            body: 'O time de governança recebe a fila e responde em até 2 dias úteis. Acompanhe na aba Rascunhos.',
          }
      navigate(result.autoApproved ? '/agentes?tab=published' : '/agentes', {
        replace: true,
        state: { flash },
      })
    } catch (err) {
      if (err instanceof ApiError && err.status === 412 && id) {
        // 412 só é possível em edit mode (update com expectedUpdatedAt).
        // Create mode não tem versão prévia pra conflitar.
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
        <div className="flex shrink-0 flex-col items-end gap-1">
          <Button
            size="sm"
            variant={assistantResult ? 'secondary' : 'primary'}
            onClick={runAnalysis}
            disabled={assistantDisabled}
            loading={assistantLoading}
            leftIcon={!assistantLoading ? <SparklesIcon className="h-4 w-4" /> : undefined}
            title={
              !roleReady
                ? `Preencha o papel com pelo menos ${MIN_ROLE_CHARS} caracteres pra habilitar.`
                : onCooldown
                ? `Aguarde ${cooldownRemaining}s pra rodar de novo.`
                : 'Analisa o perfil e devolve sugestões granulares.'
            }
          >
            {assistantLoading
              ? 'Analisando perfil…'
              : onCooldown
              ? `Aguarde ${cooldownRemaining}s`
              : assistantResult
              ? 'Reanalisar perfil'
              : 'Refinar com IA'}
          </Button>
          <span className="text-[10px] text-fg-dim">~$0.001 por análise</span>
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
          issues={stepIssues}
          visited={visitedSteps}
        />
        {/* Hint inline pra issue do step ATUAL — substitui o tooltip nativo
            (title="…") que demora 1.5s pra aparecer e some em touch. Aqui o
            user vê na hora "este step tem pendência X" sem hover. */}
        {stepIssues[form.currentStep] && (
          <p className="mt-2 flex items-center gap-1.5 text-[11px] text-warning">
            <span
              className="flex h-3.5 w-3.5 shrink-0 items-center justify-center rounded-full bg-warning/20 font-bold"
              aria-hidden="true"
            >
              !
            </span>
            {stepIssues[form.currentStep]}
          </p>
        )}
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
        {form.currentStep === 'security' && form.agentMode === 'advanced' && (
          <SecurityStep form={form} setForm={setForm} readonly={readonly} />
        )}
        {form.currentStep === 'memory' && form.agentMode === 'advanced' && (
          <MemoryStep form={form} setForm={setForm} readonly={readonly} />
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
          <ReviewStep form={form} setForm={setForm} models={models} tools={tools} mcps={mcps} readonly={readonly} />
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

          <div className="pointer-events-auto flex items-center gap-2">
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
              <>
                <Button
                  variant="secondary"
                  onClick={onSave}
                  loading={submitting}
                  disabled={readonly || submittingApproval}
                  className="shadow-xl"
                >
                  {mode === 'edit' ? 'Salvar' : 'Salvar como rascunho'}
                </Button>
                {(mode === 'create' || canSubmit) && (
                  <Button
                    onClick={tryOpenSubmit}
                    loading={submittingApproval}
                    disabled={readonly || submitting}
                    className="shadow-xl"
                  >
                    {status === 'Rejected' ? 'Salvar e submeter novamente' : 'Salvar e submeter para aprovação'}
                  </Button>
                )}
              </>
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

      <Modal
        open={confirmFrustration}
        onClose={() => setConfirmFrustration(false)}
        title="Itens críticos pendentes"
        description="O assistente sinalizou pontos importantes que ainda não foram tratados."
        size="sm"
        footer={
          <div className="flex justify-end gap-2">
            <Button variant="ghost" onClick={() => setConfirmFrustration(false)}>
              Voltar e revisar
            </Button>
            <Button
              onClick={() => {
                frustrationDismissedRef.current = true
                setConfirmFrustration(false)
                setConfirmSubmit(true)
              }}
            >
              Submeter mesmo assim
            </Button>
          </div>
        }
      >
        <p className="text-sm text-fg-muted">
          Há {criticasPendentes} {criticasPendentes === 1 ? 'item crítico não tratado' : 'itens críticos não tratados'} na última análise.
          Você pode voltar e ajustar ou submeter como está — sua decisão.
        </p>
      </Modal>

      <AssistantDrawer
        open={assistantOpen}
        onClose={closeAssistant}
        loading={assistantLoading}
        result={assistantResult}
        error={assistantError}
        onRetry={runAnalysis}
        onApply={onAssistantApply}
        fieldValues={fieldValuesFrom(form)}
      />
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
