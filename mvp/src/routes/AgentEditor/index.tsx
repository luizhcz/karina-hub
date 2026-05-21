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
import { ApiError, friendlyError } from '../../api/client'
import {
  AssistantFailureError,
  AssistantTimeoutError,
  analisarPerfil,
  countCriticas,
  hashInput,
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
import { applyOperationToMarkdown, type AssistantOperacao } from './instructionsCodec'
import { Stepper, type StepDescriptor } from './Stepper'
import { ProfileStep } from './ProfileStep'
import { RouterProfileStep } from './RouterProfileStep'
import { WorkerProfileStep } from './WorkerProfileStep'
import { ToolRunnerProfileStep } from './ToolRunnerProfileStep'
import { ConversationalProfileStep } from './ConversationalProfileStep'
import { ToolsKnowledgeStep } from './ToolsKnowledgeStep'
import { SecurityStep } from './SecurityStep'
import { MemoryStep } from './MemoryStep'
import { OutputStep } from './OutputStep'
import { ModelStep } from './ModelStep'
import { ReviewStep } from './ReviewStep'
import { ChangeReasonModal } from './ChangeReasonModal'
import { AssistantDrawer } from './AssistantDrawer'
import { buildPayload, emptyFormState, fromDraft } from './formCodec'
import type { AgentMode, FormState, StepKey } from './types'
import type { AgentType } from '../../api/agentDrafts'

// Cooldown longo (10 min) — análise de perfil é cara (Foundry Responses
// estruturado) e o resultado raramente muda entre edits curtos. Força o
// PO a iterar no markdown antes de re-pedir refinamento.
const COOLDOWN_MS = 10 * 60 * 1000
const MIN_PROFILE_CHARS = 20

// Formata um countdown em segundos pra display compacto: <60s usa "Xs";
function profileInputFrom(form: FormState): ProfileInput {
  return {
    name: form.name,
    profile: form.profile,
  }
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

// O tipo é escolhido no modal "Novo agente" antes de entrar no editor — pra
// trocar o tipo o usuário volta na listagem e abre um novo. Por isso o wizard
// começa direto no step de Perfil/Intenções/Identificação/Domínio (conforme o tipo).

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
  { key: 'output', label: 'Output' },
  { key: 'model', label: 'Modelo' },
  { key: 'review', label: 'Revisão' },
]

// Router não compartilha o ProfileStep do Custom — o profile dele É a tabela
// de intenções (nome + descrição + exemplo). Categorias e Profile genérico
// foram fundidos num step único; tools/security/memory/input/output não fazem
// sentido pro template e ficam de fora.
const ROUTER_STEPS: StepDescriptor[] = [
  { key: 'profile', label: 'Intenções' },
  { key: 'model', label: 'Modelo' },
  { key: 'review', label: 'Revisão' },
]

// Worker substitui o ProfileStep por um step próprio (Domínio) que
// captura nome + scope. Inclui Tools (opcional), Segurança (recomendado
// on por default), Output structured (recomendado) e Modelo. Pula
// Memória e Input — Worker é single-shot, sem multi-turn nem schema de
// input separado.
const WORKER_STEPS: StepDescriptor[] = [
  { key: 'profile', label: 'Domínio' },
  { key: 'tools', label: 'Ferramentas' },
  { key: 'security', label: 'Segurança' },
  { key: 'output', label: 'Output' },
  { key: 'model', label: 'Modelo' },
  { key: 'review', label: 'Revisão' },
]

// Tool Runner substitui o ProfileStep por um step próprio (Identificação +
// política HITL). Inclui Tools (obrigatório por warning), Segurança
// (AccountGuard/Guardrails recomendados), Memória (multi-turn possível,
// diferente do Worker), Output (opcional), Modelo. Sem Input — schema de
// input é coberto pelas tools (cada uma carrega seu schema).
const TOOL_RUNNER_STEPS: StepDescriptor[] = [
  { key: 'profile', label: 'Identificação' },
  { key: 'tools', label: 'Ferramentas' },
  { key: 'security', label: 'Segurança' },
  { key: 'memory', label: 'Memória' },
  { key: 'output', label: 'Output' },
  { key: 'model', label: 'Modelo' },
  { key: 'review', label: 'Revisão' },
]

// Conversational substitui o ProfileStep por um step próprio (Perfil =
// Identificação + Persona). Avançado expõe Componente/Segurança/Memória/
// Output explicitamente; básico usa defaults conservadores e fica com
// Perfil → Ferramentas → Modelo → Revisão. Output é SEMPRE structured pro
// Conversational (frontend chat exige `ui_component`) — o encoder usa
// schema canônico mesmo em basic.
const CONVERSATIONAL_BASIC_STEPS: StepDescriptor[] = [
  { key: 'profile', label: 'Perfil' },
  { key: 'tools', label: 'Ferramentas' },
  { key: 'model', label: 'Modelo' },
  { key: 'review', label: 'Revisão' },
]

const CONVERSATIONAL_ADVANCED_STEPS: StepDescriptor[] = [
  { key: 'profile', label: 'Perfil' },
  { key: 'tools', label: 'Ferramentas' },
  { key: 'security', label: 'Segurança' },
  { key: 'memory', label: 'Memória' },
  { key: 'output', label: 'Output' },
  { key: 'model', label: 'Modelo' },
  { key: 'review', label: 'Revisão' },
]

function stepsFor(mode: AgentMode, type: AgentType): StepDescriptor[] {
  if (type === 'Router') return ROUTER_STEPS
  if (type === 'Worker') return WORKER_STEPS
  if (type === 'ToolRunner') return TOOL_RUNNER_STEPS
  if (type === 'Conversational') {
    return mode === 'advanced' ? CONVERSATIONAL_ADVANCED_STEPS : CONVERSATIONAL_BASIC_STEPS
  }
  return mode === 'advanced' ? ADVANCED_STEPS : BASIC_STEPS
}

export function AgentEditor({ mode }: Props) {
  const navigate = useNavigate()
  const { id } = useParams<{ id?: string }>()
  const [searchParams] = useSearchParams()

  // No fluxo de criação, ?mode=advanced (ou ?mode=basic) define o modo inicial
  // — a tela é aberta a partir do modal de "Novo agente" da listagem com esse
  // parâmetro. Em edit, o modo é inferido do conteúdo do draft pelo formCodec.
  // ?type=Router|Custom|Conversational|Worker|ToolRunner define o tipo formal
  // e o set de steps.
  const initialMode = mode === 'create' && searchParams.get('mode') === 'advanced'
    ? 'advanced'
    : 'basic'
  const initialType: AgentType =
    mode === 'create'
      ? searchParams.get('type') === 'Router'
        ? 'Router'
        : searchParams.get('type') === 'Worker'
          ? 'Worker'
          : searchParams.get('type') === 'ToolRunner'
            ? 'ToolRunner'
            : searchParams.get('type') === 'Conversational'
              ? 'Conversational'
              : 'Custom'
      : 'Custom'
  const [form, setForm] = useState<FormState>(() => ({
    // Router/Custom/Conversational pulam o step "Tipo" — já foi escolhido no
    // modal — e entram direto em Perfil/Intenções/Identificação.
    ...emptyFormState(),
    agentMode: initialMode,
    type: initialType,
    currentStep: 'profile',
  }))
  const [draft, setDraft] = useState<AgentDraft | null>(null)
  const [loading, setLoading] = useState(mode === 'edit')
  const [loadError, setLoadError] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)
  const [submittingApproval, setSubmittingApproval] = useState(false)
  const [confirmSubmit, setConfirmSubmit] = useState(false)
  // Router exige motivo da mudança (≥10 chars) no submit — o texto vai pro
  // histórico do agente e dá contexto ao revisor da fila de aprovações.
  // State separado do confirmador genérico pra UX dedicada.
  const [routerReasonOpen, setRouterReasonOpen] = useState(false)

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

  const steps = useMemo(() => stepsFor(form.agentMode, form.type), [form.agentMode, form.type])
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
    if (!form.predefinedModelId.trim()) issues.model = 'Selecione um modelo'

    if (form.type === 'Router') {
      const selectedCount = form.routerIntentIds.length
      if (!form.name.trim()) {
        issues.profile = 'Informe um nome'
      } else if (selectedCount < 2) {
        issues.profile = 'Selecione ao menos 2 intenções'
      }
    } else if (form.type === 'Worker') {
      // Worker tem validações soft no backend (warnings). No wizard, só o
      // nome é hard requirement; scope vazio é warning de qualidade exposto
      // no review (não bloqueia "Próximo" no Stepper).
      if (!form.name.trim()) issues.profile = 'Informe um nome'
      if (form.output.mode === 'structured') {
        const trimmed = form.output.schema.trim()
        if (!trimmed) {
          issues.output = 'Schema do output vazio — defina ou troque pra texto livre.'
        } else {
          try {
            const parsed = JSON.parse(trimmed)
            if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) {
              issues.output = 'Schema do output precisa ser um objeto JSON.'
            }
          } catch {
            issues.output = 'Schema do output não é JSON válido.'
          }
        }
      }
    } else if (form.type === 'ToolRunner') {
      // Tool Runner: nome é hard requirement no wizard; tools/middlewares/
      // modelo são warnings soft no backend (não bloqueiam "Próximo"). O
      // toggle HITL é declarativo — sem validação de "obrigatoriedade",
      // só consistência (avaliada em ValidateToolRunner no save).
      if (!form.name.trim()) issues.profile = 'Informe um nome'
      if (form.output.mode === 'structured') {
        const trimmed = form.output.schema.trim()
        if (trimmed) {
          try {
            const parsed = JSON.parse(trimmed)
            if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) {
              issues.output = 'Schema do output precisa ser um objeto JSON.'
            }
          } catch {
            issues.output = 'Schema do output não é JSON válido.'
          }
        }
      }
    } else if (form.type === 'Conversational') {
      // Conversational: nome obrigatório. O sub-schema do output deve ser
      // JSON válido (codec envolve no shape canônico no save; schema
      // inválido cai pro default vazio e gera warning soft no backend).
      if (!form.name.trim()) issues.profile = 'Informe um nome'
      const trimmed = form.output.schema.trim()
      if (trimmed) {
        try {
          const parsed = JSON.parse(trimmed)
          if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) {
            issues.output = 'Subschema do `output` precisa ser um objeto JSON.'
          }
        } catch {
          issues.output = 'Subschema do `output` não é JSON válido.'
        }
      }
    } else {
      if (!form.name.trim()) issues.profile = 'Informe um nome'
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
    }
    return issues
  }, [
    form.name,
    form.predefinedModelId,
    form.type,
    form.routerIntentIds,
    form.memory.enabled,
    form.memory.schema,
    form.output.mode,
    form.output.schema,
  ])

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
    if (mode === 'edit') return new Set(stepsFor(initialMode, form.type).map((s) => s.key))
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
      const nextSteps = stepsFor(next, prev.type)
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
  const profileReady = form.profile.trim().length >= MIN_PROFILE_CHARS
  const assistantDisabled = readonly || !profileReady || onCooldown || assistantLoading

  // Aplica uma sugestão do assistente direto no markdown do profile. O usuário
  // já decidiu a operação (substituir/mesclar) no toggle do AssistantDiff;
  // aqui só delegamos pro helper canônico do codec.
  const onAssistantApply = (op: AssistantOperacao, secao: string | null, conteudo: string) => {
    setForm((prev) => ({
      ...prev,
      profile: applyOperationToMarkdown(prev.profile, op, secao, conteudo),
    }))
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
        const payload = buildPayload(draft.payload, form)
        const updated = await updateAgentDraft(id, {
          payload,
          expectedUpdatedAt: draft.updatedAt,
        })
        setDraft(updated)
        setForm((prev) => ({ ...fromDraft(updated), currentStep: prev.currentStep, agentMode: prev.agentMode }))
      } else {
        const payload = buildPayload(undefined, form)
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
    // Worker grava form.output em payload.structuredOutput (json_schema).
    // Schema inválido vai pro backend e quebra a chamada — checar antes do
    // submit pra dar feedback inline.
    if (form.type === 'Worker' && form.output.mode === 'structured') {
      const trimmedSchema = form.output.schema.trim()
      if (!trimmedSchema) return 'Worker com output estruturado exige um schema — defina ou troque pra texto livre.'
      try {
        const parsed = JSON.parse(trimmedSchema)
        if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed))
          return 'Schema do output do Worker precisa ser um objeto JSON.'
      } catch {
        return 'Schema do output do Worker não é JSON válido.'
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
    // Router: modal dedicado pede motivo obrigatório (≥10 chars). Demais
    // tipos seguem o modal de confirmação genérico.
    if (form.type === 'Router') {
      setRouterReasonOpen(true)
      return
    }
    setConfirmSubmit(true)
  }

  const onConfirmSubmit = async (changeReason?: string) => {
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
        const payload = buildPayload(undefined, form)
        const created = await createAgentDraft({ id: generateDraftId(), payload })
        draftId = created.id
        setDraft(created)
      } else if (draft) {
        const payload = buildPayload(draft.payload, form)
        const updated = await updateAgentDraft(draftId, {
          payload,
          expectedUpdatedAt: draft.updatedAt,
        })
        setDraft(updated)
      }
      const result = await submitAgentDraft(draftId!, changeReason)
      setConfirmSubmit(false)
      setRouterReasonOpen(false)
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
      setRouterReasonOpen(false)
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
            {/* Nome do agente editável inline. Obrigatório — placeholder mostra
                titleText como hint e o asterisco rubro sinaliza o user quando
                vazio. A validation real continua no submit (stepIssues). */}
            <input
              type="text"
              value={form.name}
              onChange={(e) => setForm((prev) => ({ ...prev, name: e.target.value }))}
              placeholder={titleText}
              disabled={readonly}
              aria-label="Nome do agente"
              aria-required="true"
              required
              className={cn(
                'min-w-0 flex-1 truncate rounded-lg border border-border bg-surface px-3 py-2 text-2xl font-semibold tracking-tight',
                'text-fg placeholder:text-fg-dim transition-colors',
                'hover:border-border-strong focus:outline-none focus:border-accent focus:ring-2 focus:ring-accent/20',
                'disabled:cursor-not-allowed disabled:opacity-60',
              )}
            />
            {form.name.trim().length === 0 && (
              <span
                className="text-base font-semibold text-rose-500"
                aria-hidden="true"
                title="Nome é obrigatório"
              >
                *
              </span>
            )}
            {mode === 'edit' && (
              <Badge tone={STATUS_TONE[status]}>{STATUS_LABEL[status]}</Badge>
            )}
          </div>
        </div>
        {form.type !== 'Router'
          && form.type !== 'Worker'
          && form.type !== 'ToolRunner' && (
          <div className="flex shrink-0 flex-col items-end gap-1">
            <Button
              size="sm"
              variant="secondary"
              disabled
              leftIcon={<SparklesIcon className="h-4 w-4" />}
              title="Refinamento com IA está temporariamente desativado nesta fase do MVP."
            >
              Refinar com IA
            </Button>
          </div>
        )}
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
        {/* min-h-9 reserva a altura do toggle Básico/Avançado mesmo quando
            ele não está visível (tipos diferentes de Custom). Sem isso o
            Stepper "pula" verticalmente ao trocar de tipo. */}
        <div className="flex min-h-9 flex-wrap items-center justify-between gap-3">
          <div>
            <p className="text-xs font-semibold uppercase tracking-wider text-fg-dim">
              Tipo do agente
            </p>
          </div>
          {form.type !== 'Router'
            && form.type !== 'Worker'
            && form.type !== 'ToolRunner' && (
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
          )}
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
        {form.currentStep === 'profile' && form.type === 'Router' && (
          <RouterProfileStep form={form} setForm={setForm} readonly={readonly} />
        )}
        {form.currentStep === 'profile' && form.type === 'Worker' && (
          // Mesmo padrão do Custom ProfileStep: key força re-mount quando o
          // GET em edit mode termina, garantindo que o BlockNote inicialize
          // a partir do workerScope carregado.
          <WorkerProfileStep
            key={
              id
                ? `worker-edit-${form.name.trim().length > 0 ? 'loaded' : 'empty'}`
                : 'worker-new'
            }
            form={form}
            setForm={setForm}
            readonly={readonly}
          />
        )}
        {form.currentStep === 'profile' && form.type === 'ToolRunner' && (
          <ToolRunnerProfileStep form={form} setForm={setForm} readonly={readonly} />
        )}
        {form.currentStep === 'profile' && form.type === 'Conversational' && (
          <ConversationalProfileStep form={form} setForm={setForm} readonly={readonly} />
        )}
        {form.currentStep === 'profile'
          && form.type !== 'Router'
          && form.type !== 'Worker'
          && form.type !== 'ToolRunner'
          && form.type !== 'Conversational' && (
            // `key` muda quando o draft chega do GET (form.name vazio →
            // populado). Força re-mount do ProfileStep com BlockNote já
            // inicializado a partir do profile decodificado. Sem isso o
            // editor monta com template e a re-hidratação reativa pelo
            // useEffect não reflete consistentemente em WYSIWYG.
            <ProfileStep
              key={
                id
                  ? `profile-edit-${form.name.trim().length > 0 ? 'loaded' : 'empty'}`
                  : 'profile-new'
              }
              form={form}
              setForm={setForm}
              readonly={readonly}
            />
          )}
        {form.currentStep === 'tools' && (
          <ToolsKnowledgeStep
            form={form}
            setForm={setForm}
            tools={tools}
            toolsLoading={toolsLoading}
            toolsError={toolsError}
            readonly={readonly}
          />
        )}
        {form.currentStep === 'security'
          && (form.agentMode === 'advanced'
            || form.type === 'Worker'
            || form.type === 'ToolRunner'
            || form.type === 'Conversational') && (
            <SecurityStep form={form} setForm={setForm} readonly={readonly} />
          )}
        {form.currentStep === 'memory'
          && (form.agentMode === 'advanced'
            || form.type === 'ToolRunner'
            || form.type === 'Conversational') && (
            <MemoryStep form={form} setForm={setForm} readonly={readonly} />
          )}
        {form.currentStep === 'output'
          && (form.agentMode === 'advanced'
            || form.type === 'Worker'
            || form.type === 'ToolRunner'
            || form.type === 'Conversational') && (
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
          <ReviewStep form={form} setForm={setForm} models={models} tools={tools} readonly={readonly} />
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
            <Button onClick={() => onConfirmSubmit()} loading={submittingApproval}>
              Submeter
            </Button>
          </div>
        }
      >
        <p className="text-sm text-fg-muted">
          Vamos salvar as alterações em aberto e enviar este rascunho para aprovação. Tudo certo?
        </p>
      </Modal>

      <ChangeReasonModal
        open={routerReasonOpen}
        agentType="Router"
        submitting={submittingApproval}
        onClose={() => setRouterReasonOpen(false)}
        onConfirm={(reason) => onConfirmSubmit(reason)}
      />

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
                // Router pula o modal genérico — abre o modal de motivo.
                if (form.type === 'Router') setRouterReasonOpen(true)
                else setConfirmSubmit(true)
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
        currentMarkdown={form.profile}
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
