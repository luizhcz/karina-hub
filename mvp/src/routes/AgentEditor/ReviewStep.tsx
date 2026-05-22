import { useEffect, useMemo, useState } from 'react'
import ReactMarkdown from 'react-markdown'
import remarkGfm from 'remark-gfm'
import { Badge, Button, Card, CardHeader, cn } from '../../ui'
import type { GenericTool } from '../../api/genericTools'
import { listRouterIntents, type RouterIntent } from '../../api/routerIntents'
import { encodeInstructions } from './instructionsCodec'
import {
  encodeConversationalInstructions,
  encodeRouterInstructions,
  encodeToolRunnerInstructions,
  encodeWorkerInstructions,
} from './formCodec'
import type { FormState } from './types'
import { buildEnrichedDescriptors } from './preview/toolDescriptors'
import { ToolsPreview } from './preview/ToolsPreview'
import { ToolCallExample } from './preview/ToolCallExample'

interface ReviewStepProps {
  form: FormState
  tools: GenericTool[]
}

// Espelha PromptRenderer.RenderRouterIntentsBlock do backend — bloco
// "# Intenções disponíveis" + "# Memória operacional" injetado no
// Instructions do snapshot em compose-time. Manter sincronizado com
// src/EfsAiHub.Core.Agents/Services/PromptRenderer.cs.
function buildRouterIntentsPromptBlock(intents: RouterIntent[]): string | null {
  if (intents.length === 0) return null
  const lines: string[] = []
  lines.push('# Intenções disponíveis')
  lines.push('')
  lines.push(
    'Escolha **exatamente uma** categoria do enum `intent` para cada input. ' +
      'Não invente categorias. Não combine. **Quando nenhuma intent de negócio combinar ' +
      '(saudações, perguntas genéricas, fora do domínio do agente), escolha `out_of_scope` ' +
      'com `confidence >= 0.7`**. Não force uma intent de negócio com confidence baixa — ' +
      'isso é um sintoma de classificação ruim, use a intent de fora-de-escopo.',
  )
  lines.push('')
  for (const intent of intents) {
    const name = intent.name.trim()
    if (!name) continue
    const desc = intent.description.trim()
    lines.push(desc ? `- \`${name}\` — ${desc}` : `- \`${name}\``)
    const validExamples = (intent.examples ?? [])
      .map((e) => (e ?? '').trim())
      .filter((e) => e.length > 0)
    if (validExamples.length > 0) {
      lines.push('  Exemplos:')
      for (const ex of validExamples) lines.push(`  • "${ex}"`)
    }
  }
  lines.push('')
  lines.push('# Memória operacional')
  lines.push('')
  lines.push(
    'No campo `operationalMemory` do output, **sempre preencha**:\n' +
      '- `last_intent`: copie o valor de `intent` que você escolheu.\n' +
      '- `last_reason`: copie o valor de `reason` (até 200 chars).\n\n' +
      'Esta memória é persistida e injetada no próximo turno como contexto. ' +
      'Não invente outros campos.',
  )
  return lines.join('\n')
}

// Compose final do system prompt do Router pra preview: esqueleto autoral
// (encodeRouterInstructions) seguido do bloco auto-injetado de intenções.
// Quando o set de intents ainda não carregou, devolve só o esqueleto.
function buildRouterFullSystemPrompt(name: string, intents: RouterIntent[]): string {
  const skeleton = encodeRouterInstructions(name)
  const block = buildRouterIntentsPromptBlock(intents)
  if (!block) return skeleton
  return `${skeleton}\n\n${block}`
}

export function ReviewStep({ form, tools }: ReviewStepProps) {
  const includeStructured = form.agentMode === 'advanced'
  const inputForCodec = form.input.mode === 'structured' ? form.input : { description: '', schema: '' }
  const outputForCodec = form.output.mode === 'structured' ? form.output : { description: '', schema: '' }

  // Descritores ricos das tools anexadas. Vão pra <ToolsPreview> +
  // <ToolCallExample> renderizarem visão estruturada (JSON Schema, badges,
  // exemplo de tool_call) — não pro prompt (tools chegam ao LLM via
  // function-calling nativo, não como texto concatenado).
  const enrichedTools = useMemo(
    () => buildEnrichedDescriptors(form.toolIds, tools),
    [form.toolIds, tools],
  )

  // Pool de intents só carrega quando o agente é Router — usado pra resolver
  // o prompt de preview com o enum visível.
  const [routerIntentsPool, setRouterIntentsPool] = useState<RouterIntent[]>([])
  const [routerIntentsLoading, setRouterIntentsLoading] = useState(false)
  useEffect(() => {
    if (form.type !== 'Router') return
    let cancelled = false
    setRouterIntentsLoading(true)
    listRouterIntents()
      .then((items) => {
        if (!cancelled) setRouterIntentsPool(items)
      })
      .catch(() => undefined)
      .finally(() => {
        if (!cancelled) setRouterIntentsLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [form.type])

  const selectedRouterIntents = useMemo(
    () =>
      form.type === 'Router'
        ? routerIntentsPool.filter((i) => form.routerIntentIds.includes(i.id))
        : [],
    [form.type, routerIntentsPool, form.routerIntentIds],
  )

  const prompt = useMemo(
    () =>
      form.type === 'Router'
        ? buildRouterFullSystemPrompt(form.name, selectedRouterIntents)
        : form.type === 'Worker'
          ? encodeWorkerInstructions(form.name)
          : form.type === 'ToolRunner'
            ? encodeToolRunnerInstructions(form.name)
            : form.type === 'Conversational'
              ? encodeConversationalInstructions(form.profile)
              : encodeInstructions(form.profile, inputForCodec, outputForCodec, includeStructured),
    [
      form.type,
      form.name,
      form.profile,
      inputForCodec,
      outputForCodec,
      includeStructured,
      selectedRouterIntents,
    ],
  )

  const [copied, setCopied] = useState(false)

  const onCopy = () => {
    if (!prompt) return
    navigator.clipboard?.writeText(prompt).then(() => {
      setCopied(true)
      window.setTimeout(() => setCopied(false), 1500)
    })
  }

  return (
    <div className="space-y-5">
      {form.type === 'Router' && (
        <RouterPreview
          intents={selectedRouterIntents}
          loading={routerIntentsLoading}
          declaredCount={form.routerIntentIds.length}
        />
      )}

      {form.type === 'Worker' && <WorkerPreview form={form} />}

      {form.type === 'ToolRunner' && (
        <ToolRunnerPreview form={form} tools={tools} />
      )}

      {form.type === 'Conversational' && <ConversationalPreview form={form} />}

      <Card className="space-y-4">
        <CardHeader
          title="System prompt"
          description="Texto que vai como instrução do agente. As ferramentas anexadas (próximo bloco) NÃO entram aqui — são entregues ao modelo separadamente via function calling."
          actions={
            <Button variant="secondary" size="sm" onClick={onCopy} disabled={!prompt}>
              {copied ? 'Copiado!' : 'Copiar system prompt'}
            </Button>
          }
        />
        {prompt ? (
          <PromptPreview content={prompt} />
        ) : (
          <p className="rounded-lg border border-dashed border-border px-4 py-6 text-center text-xs text-fg-muted">
            Nenhum campo de instrução preenchido ainda. Volte pra etapa Perfil.
          </p>
        )}
      </Card>

      <ToolsPreview tools={enrichedTools} />

      <ToolCallExample tools={enrichedTools} />
    </div>
  )
}

interface RouterPreviewProps {
  intents: RouterIntent[]
  loading: boolean
  declaredCount: number
}

// Reproduz o schema canônico de RouterDefaults.OutputSchemaJson com o enum
// de `intent` populado pelas intents resolvidas — espelha o que o composer
// grava no snapshot via OutputSchemaRenderer.ApplyRouterEnumIfApplicable.
// Mantém sincronizado com o backend; se RouterDefaults mudar, atualizar aqui.
function buildRouterCanonicalSchema(intents: RouterIntent[]): Record<string, unknown> {
  const enumValues = intents.map((i) => i.name).filter((n) => n.length > 0)
  return {
    type: 'object',
    additionalProperties: false,
    properties: {
      intent: {
        type: 'string',
        description: 'Categoria escolhida do enum de intents resolvido em runtime.',
        ...(enumValues.length > 0 ? { enum: enumValues } : {}),
      },
      confidence: {
        type: 'number',
        minimum: 0,
        maximum: 1,
        description: 'Confiança da classificação (0..1).',
      },
      reason: {
        type: 'string',
        description: 'Justificativa curta da escolha (uso interno de auditoria/debug).',
      },
      operationalMemory: {
        type: 'object',
        additionalProperties: false,
        properties: {
          last_intent: { type: 'string' },
          last_reason: { type: 'string' },
        },
        required: ['last_intent', 'last_reason'],
      },
    },
    required: ['intent', 'confidence', 'reason', 'operationalMemory'],
  }
}

// Preview do Router no Review com duas visões alternáveis (mirror do
// Conversational): "visual" explica em PT-BR o contrato — para diretor — e
// "schema" mostra o JSON Schema cru que vai pro response_format do LLM.
// Tecnicamente o LLM recebe as intents via enum no schema, NÃO no system
// prompt — esse card é onde elas ficam transparentes no review.
function RouterPreview({ intents, loading, declaredCount }: RouterPreviewProps) {
  const [viewMode, setViewMode] = useState<'visual' | 'schema'>('visual')
  const tooFew = declaredCount < 2
  const canonicalSchema = useMemo(() => buildRouterCanonicalSchema(intents), [intents])
  const canonicalSchemaJson = useMemo(
    () => JSON.stringify(canonicalSchema, null, 2),
    [canonicalSchema],
  )

  return (
    <Card className="space-y-3">
      <CardHeader
        title="Modelo pré definido enviado ao LLM"
        description="Garanta que as respostas do classificador estejam em conformidade com um esquema JSON definido por você. O enum de intent é injetado a partir das intenções selecionadas."
        actions={
          <Button
            variant="secondary"
            size="sm"
            onClick={() => setViewMode((prev) => (prev === 'visual' ? 'schema' : 'visual'))}
          >
            {viewMode === 'visual' ? 'Ver JSON Schema' : 'Ver versão visual'}
          </Button>
        }
      />
      {loading && <p className="text-xs text-fg-muted">Carregando intenções…</p>}
      {tooFew && (
        <p className="rounded-lg border border-warning/40 bg-warning/10 px-3 py-2 text-xs text-warning">
          Pelo menos 2 intenções são obrigatórias. Selecione mais antes de submeter.
        </p>
      )}
      {viewMode === 'schema' ? (
        <pre className="m-0 max-h-72 overflow-auto rounded-lg border border-border bg-bg-soft px-3 py-2 font-mono text-[11px] leading-snug text-fg">
          {canonicalSchemaJson}
        </pre>
      ) : (
        <RouterVisualPreview intents={intents} />
      )}
    </Card>
  )
}

interface RouterVisualPreviewProps {
  intents: RouterIntent[]
}

// Versão "executiva" do contrato do Router — 4 blocos representando os
// campos top-level que o LLM preenche: intent (escolhida do enum),
// confidence, reason, operationalMemory.
function RouterVisualPreview({ intents }: RouterVisualPreviewProps) {
  return (
    <div className="space-y-3">
      <div className="rounded-lg border border-border bg-bg-soft p-3">
        <div className="flex items-center gap-2">
          <span className="flex h-7 w-7 items-center justify-center rounded-full bg-accent-subtle text-base">
            🎯
          </span>
          <h4 className="text-sm font-semibold text-fg">Intenção escolhida</h4>
        </div>
        <p className="mt-2 pl-9 text-xs text-fg-muted">
          A cada classificação, o agente escolhe exatamente uma das intenções abaixo.
        </p>
        <div className="mt-2 pl-9">
          {intents.length === 0 ? (
            <p className="text-xs text-fg-dim">Nenhuma intenção selecionada.</p>
          ) : (
            <ul className="space-y-1.5">
              {intents.map((intent) => {
                const label = (intent.displayName?.trim() || intent.name).trim()
                const desc = intent.description.trim()
                return (
                  <li key={intent.id} className="flex flex-col gap-0.5">
                    <div className="flex items-baseline gap-2">
                      <Badge tone="accent">
                        <code className="font-mono text-[11px]">{intent.name}</code>
                      </Badge>
                      {label !== intent.name && (
                        <span className="text-xs text-fg-muted">{label}</span>
                      )}
                    </div>
                    {desc && (
                      <p className="pl-1 text-[11px] leading-relaxed text-fg-muted">{desc}</p>
                    )}
                  </li>
                )
              })}
            </ul>
          )}
        </div>
      </div>

      <div className="rounded-lg border border-border bg-bg-soft p-3">
        <div className="flex items-center gap-2">
          <span className="flex h-7 w-7 items-center justify-center rounded-full bg-accent-subtle text-base">
            📊
          </span>
          <h4 className="text-sm font-semibold text-fg">Confiança</h4>
        </div>
        <p className="mt-2 pl-9 text-xs text-fg-muted">
          Número entre 0 e 1 indicando quão certa foi a classificação. Abaixo de 0,5 sinaliza ambiguidade.
        </p>
      </div>

      <div className="rounded-lg border border-border bg-bg-soft p-3">
        <div className="flex items-center gap-2">
          <span className="flex h-7 w-7 items-center justify-center rounded-full bg-accent-subtle text-base">
            💭
          </span>
          <h4 className="text-sm font-semibold text-fg">Justificativa</h4>
        </div>
        <p className="mt-2 pl-9 text-xs text-fg-muted">
          Texto curto explicando por que a intenção foi escolhida — usado em auditoria e debug.
        </p>
      </div>

      <div className="rounded-lg border border-border bg-bg-soft p-3">
        <div className="flex items-center gap-2">
          <span className="flex h-7 w-7 items-center justify-center rounded-full bg-accent-subtle text-base">
            🧠
          </span>
          <h4 className="text-sm font-semibold text-fg">Memória operacional</h4>
        </div>
        <p className="mt-2 pl-9 text-xs text-fg-muted">
          Última intenção e razão persistidas a cada turno — alimenta a próxima classificação com contexto.
        </p>
      </div>
    </div>
  )
}

interface WorkerPreviewProps {
  form: FormState
}

// Preview do Worker no Review: mostra o domínio (scope) que será injetado
// em runtime + warnings soft (scope vazio). Sem fetch — Worker é
// standalone (sem pool global). Validações hard de Worker são apenas o
// nome; demais expectativas (modelo full, security on, output structured)
// são warnings exibidos em outros cards do Review.
function WorkerPreview({ form }: WorkerPreviewProps) {
  const scope = form.workerScope.trim()
  const empty = scope.length === 0
  const SCOPE_PREVIEW_MAX = 200
  const truncated = scope.length > SCOPE_PREVIEW_MAX
  const [expanded, setExpanded] = useState(false)
  const visible = !truncated || expanded ? scope : scope.slice(0, SCOPE_PREVIEW_MAX) + '…'

  return (
    <Card className="space-y-3">
      <CardHeader
        title="Domínio de análise"
        description="Texto injetado em runtime ao final do system prompt como bloco '# Domínio de análise'. Edits no scope propagam pra próxima chamada — sem necessidade de re-publish."
      />
      {empty ? (
        <p className="rounded-lg border border-warning/40 bg-warning/10 px-3 py-2 text-xs text-warning">
          Nenhum domínio definido. O Worker funciona, mas o LLM não recebe orientação de escopo — qualidade pode degradar. Volte pra etapa Domínio.
        </p>
      ) : (
        <div className="rounded-lg border border-border bg-bg-soft px-3 py-2.5">
          <p className="whitespace-pre-wrap text-sm leading-relaxed text-fg">{visible}</p>
          {truncated && (
            <Button
              variant="ghost"
              size="sm"
              onClick={() => setExpanded((x) => !x)}
              className="mt-2 -ml-1"
            >
              {expanded ? 'Ver menos' : 'Ver mais'}
            </Button>
          )}
          <p className="mt-2 text-[11px] text-fg-dim">
            {scope.length} caracteres
          </p>
        </div>
      )}
    </Card>
  )
}

interface ToolRunnerPreviewProps {
  form: FormState
  tools: GenericTool[]
}

// Preview do Tool Runner no Review: mostra tools selecionadas (cada uma
// sinaliza requiresApproval=true se aplicável), status do flag HITL e
// status do middleware AccountGuard (presente no payload.middlewares —
// avaliado via FormState pra UX, valor real é montado no save).
// Tool Runner sem tools recebe warning soft no save; aqui é sinalizado.
function ToolRunnerPreview({ form, tools }: ToolRunnerPreviewProps) {
  const selectedTools = tools.filter((t) => form.toolIds.includes(t.id))
  const noTools = selectedTools.length === 0
  const hitlOn = form.toolRunnerHitlRequired

  return (
    <Card className="space-y-3">
      <CardHeader
        title="Política operacional"
        description="Resumo do que será gravado no Tool Runner: ferramentas disponíveis, exigência declarada de HITL e recomendação de AccountGuard. Avisos soft do template aparecem no log do save."
      />
      {noTools ? (
        <p className="rounded-lg border border-warning/40 bg-warning/10 px-3 py-2 text-xs text-warning">
          Nenhuma ferramenta selecionada. Tool Runner sem tools não tem o que executar — volte pra etapa Ferramentas e marque ao menos uma.
        </p>
      ) : (
        <div>
          <p className="mb-1 text-[11px] uppercase tracking-wider text-fg-dim">
            Ferramentas
          </p>
          <div className="flex flex-wrap gap-2">
            {selectedTools.map((t) => (
              <Badge key={t.id} tone="accent">
                {t.name || t.id}
              </Badge>
            ))}
          </div>
        </div>
      )}
      <div className="flex items-center justify-between gap-3 rounded-lg border border-border bg-bg-soft px-3 py-2.5">
        <div className="min-w-0">
          <p className="text-xs font-medium text-fg">
            Aprovação humana antes de tools com side-effect
          </p>
          <p className="mt-0.5 text-[11px] text-fg-muted">
            {hitlOn
              ? 'Declarada como exigida — tools com requiresApproval=true devem aguardar confirmação humana.'
              : 'Não exigida — agente invoca tools direto. Marque no step Identificação se quiser sinalizar a exigência.'}
          </p>
        </div>
        <Badge tone={hitlOn ? 'warning' : 'neutral'}>
          {hitlOn ? 'Exigida' : 'Não exigida'}
        </Badge>
      </div>
    </Card>
  )
}

interface ConversationalPreviewProps {
  form: FormState
}

// Descrições espelham as constantes do backend (AgentTemplateService.cs).
// Manter sincronizado — schema mostrado aqui ≡ schema gravado/enviado ao LLM.
const CONVERSATIONAL_OUTPUT_TYPE_DESCRIPTION =
  'Família de renderer que o frontend deve usar pra esta resposta. Valor único definido pelo agente.'
const CONVERSATIONAL_OUTPUT_STATUS_DESCRIPTION =
  'Variação de status dentro do output_type. Valor escolhido entre as opções configuradas pelo agente.'
const CONVERSATIONAL_MESSAGE_DESCRIPTION =
  'Texto humano em PT-BR pro usuário — curto, claro, direto.'

// Reproduz o wrap canônico { output_type, output_status, message, output? }
// que o backend monta via AgentTemplateService.BuildCanonicalSchema. Defaults
// alinhados (text + ["default"]) pra que basic mode mostre o schema final
// mesmo sem o user configurar os campos.
function buildConversationalCanonicalSchema(
  outputType: string,
  outputStatuses: string[],
  outputSchemaJson: string,
  isStructured: boolean,
): Record<string, unknown> {
  const sanitizedType = outputType.trim().length > 0 ? outputType.trim() : 'text'
  const sanitizedStatuses = outputStatuses
    .map((s) => s.trim())
    .filter((s) => s.length > 0)
  const finalStatuses = sanitizedStatuses.length > 0 ? sanitizedStatuses : ['default']

  const properties: Record<string, unknown> = {
    output_type: {
      type: 'string',
      description: CONVERSATIONAL_OUTPUT_TYPE_DESCRIPTION,
      enum: [sanitizedType],
    },
    output_status: {
      type: 'string',
      description: CONVERSATIONAL_OUTPUT_STATUS_DESCRIPTION,
      enum: finalStatuses,
    },
    message: {
      type: 'string',
      description: CONVERSATIONAL_MESSAGE_DESCRIPTION,
    },
  }
  const required = ['output_type', 'output_status', 'message']

  if (isStructured) {
    const trimmed = outputSchemaJson.trim()
    if (trimmed.length > 0) {
      try {
        const parsed = JSON.parse(trimmed)
        if (parsed && typeof parsed === 'object' && !Array.isArray(parsed)) {
          properties.output = parsed
          required.push('output')
        }
      } catch {
        // Schema inválido — bloco invalid aparece acima; aqui só omite output.
      }
    }
  }

  return {
    type: 'object',
    properties,
    required,
    additionalProperties: false,
  }
}

// Resumo dos campos top-level do output sub-schema pra view visual. Devolve
// null quando o schema é JSON inválido — UI mostra warning.
interface OutputFieldSummary {
  name: string
  typeLabel: string
  required: boolean
}
function summarizeOutputFields(rawSchema: string): OutputFieldSummary[] | null {
  const trimmed = rawSchema.trim()
  if (!trimmed) return []
  let parsed: unknown
  try {
    parsed = JSON.parse(trimmed)
  } catch {
    return null
  }
  if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) return null
  const obj = parsed as Record<string, unknown>
  const props = obj.properties
  if (!props || typeof props !== 'object' || Array.isArray(props)) return []
  const requiredArr = Array.isArray(obj.required)
    ? (obj.required as unknown[]).filter((r): r is string => typeof r === 'string')
    : []
  const requiredSet = new Set(requiredArr)
  const propsObj = props as Record<string, unknown>
  return Object.entries(propsObj).map(([name, sub]) => {
    let typeLabel = '—'
    if (sub && typeof sub === 'object' && !Array.isArray(sub)) {
      const subObj = sub as Record<string, unknown>
      const t = subObj.type
      if (typeof t === 'string') typeLabel = t
      else if (Array.isArray(t)) typeLabel = t.filter((x) => x !== 'null').join('|') || '—'
      if (t === 'array') {
        const items = subObj.items as Record<string, unknown> | undefined
        const itemType = items && typeof items.type === 'string' ? items.type : 'item'
        typeLabel = `lista de ${itemType}`
      }
    }
    return { name, typeLabel, required: requiredSet.has(name) }
  })
}

// Preview do Conversational no Review com duas visões alternáveis:
//   - "visual" (default): explica em linguagem executiva o que o agente
//     entrega a cada turno — para o diretor enxergar o contrato sem
//     precisar ler JSON Schema.
//   - "schema": o JSON Schema cru que vai pro response_format do LLM —
//     para PM/dev validar o contrato técnico.
function ConversationalPreview({ form }: ConversationalPreviewProps) {
  const [viewMode, setViewMode] = useState<'visual' | 'schema'>('visual')
  const isStructured = form.output.mode === 'structured'
  const outputType = form.conversationalOutputType.trim().length > 0
    ? form.conversationalOutputType.trim()
    : 'text'
  const outputStatuses = form.conversationalOutputStatuses
    .map((s) => s.trim())
    .filter((s) => s.length > 0)
  const effectiveStatuses = outputStatuses.length > 0 ? outputStatuses : ['default']
  const outputFields = isStructured ? summarizeOutputFields(form.output.schema) : []
  const schemaInvalid = outputFields === null
  const fieldList = outputFields ?? []
  const canonicalSchema = buildConversationalCanonicalSchema(
    outputType,
    outputStatuses,
    form.output.schema,
    isStructured,
  )
  const canonicalSchemaJson = JSON.stringify(canonicalSchema, null, 2)

  return (
    <Card className="space-y-3">
      <CardHeader
        title="Modelo pré definido enviado ao LLM"
        description={
          'Garanta que as respostas de texto do modelo estejam em conformidade com um esquema JSON definido por você.'
        }
        actions={
          <Button
            variant="secondary"
            size="sm"
            onClick={() => setViewMode((prev) => (prev === 'visual' ? 'schema' : 'visual'))}
          >
            {viewMode === 'visual' ? 'Ver JSON Schema' : 'Ver versão visual'}
          </Button>
        }
      />

      {viewMode === 'schema' ? (
        <pre className="m-0 max-h-72 overflow-auto rounded-lg border border-border bg-bg-soft px-3 py-2 font-mono text-[11px] leading-snug text-fg">
          {canonicalSchemaJson}
        </pre>
      ) : (
        <ConversationalVisualPreview
          outputType={outputType}
          outputStatuses={effectiveStatuses}
          isStructured={isStructured}
          schemaInvalid={schemaInvalid}
          fields={fieldList}
        />
      )}
    </Card>
  )
}

interface ConversationalVisualPreviewProps {
  outputType: string
  outputStatuses: string[]
  isStructured: boolean
  schemaInvalid: boolean
  fields: OutputFieldSummary[]
}

// Versão "executiva" do contrato — sem JSON, sem schema. Blocos representando
// os campos que o agente preenche a cada resposta: tipo do cartão (fixo),
// status escolhido (entre opções), mensagem (sempre presente) e dados
// anexos (quando o output é estruturado).
function ConversationalVisualPreview({
  outputType,
  outputStatuses,
  isStructured,
  schemaInvalid,
  fields,
}: ConversationalVisualPreviewProps) {
  return (
    <div className="space-y-3">
      <div className="rounded-lg border border-border bg-bg-soft p-3">
        <div className="flex items-center gap-2">
          <span className="flex h-7 w-7 items-center justify-center rounded-full bg-accent-subtle text-base">
            🧩
          </span>
          <h4 className="text-sm font-semibold text-fg">Tipo do cartão</h4>
        </div>
        <p className="mt-2 pl-9 text-xs text-fg-muted">
          Família de renderer fixa do agente — o front sabe qual cartão desenhar.
        </p>
        <div className="mt-2 pl-9">
          <Badge tone="accent">
            <code className="font-mono text-[11px]">{outputType}</code>
          </Badge>
        </div>
      </div>

      <div className="rounded-lg border border-border bg-bg-soft p-3">
        <div className="flex items-center gap-2">
          <span className="flex h-7 w-7 items-center justify-center rounded-full bg-accent-subtle text-base">
            🎯
          </span>
          <h4 className="text-sm font-semibold text-fg">Status escolhido</h4>
        </div>
        <p className="mt-2 pl-9 text-xs text-fg-muted">
          A cada resposta, o agente escolhe exatamente um dos status abaixo.
        </p>
        <div className="mt-2 flex flex-wrap gap-2 pl-9">
          {outputStatuses.map((value) => (
            <Badge key={value} tone="neutral">
              <code className="font-mono text-[11px]">{value}</code>
            </Badge>
          ))}
        </div>
      </div>

      <div className="rounded-lg border border-border bg-bg-soft p-3">
        <div className="flex items-center gap-2">
          <span className="flex h-7 w-7 items-center justify-center rounded-full bg-accent-subtle text-base">
            💬
          </span>
          <h4 className="text-sm font-semibold text-fg">Mensagem para o usuário</h4>
        </div>
        <p className="mt-2 pl-9 text-xs text-fg-muted">
          Texto curto em PT-BR que aparece no chat. Sempre presente em toda resposta do agente.
        </p>
      </div>

      <div className="rounded-lg border border-border bg-bg-soft p-3">
        <div className="flex items-center gap-2">
          <span className="flex h-7 w-7 items-center justify-center rounded-full bg-accent-subtle text-base">
            📦
          </span>
          <h4 className="text-sm font-semibold text-fg">Dados anexos</h4>
        </div>
        <div className="mt-2 pl-9">
          {!isStructured ? (
            <p className="text-xs text-fg-muted">
              Resposta em texto livre — agente entrega só a mensagem, sem dados extras.
            </p>
          ) : schemaInvalid ? (
            <p className="rounded-md border border-warning/40 bg-warning/10 px-2 py-1.5 text-xs text-warning">
              Estrutura dos dados inválida — corrija na etapa Output.
            </p>
          ) : fields.length === 0 ? (
            <p className="text-xs text-fg-muted">
              Nenhum campo definido — agente entrega só a mensagem.
            </p>
          ) : (
            <ul className="space-y-1.5">
              {fields.map((field) => (
                <li key={field.name} className="flex items-baseline gap-2 text-xs">
                  <code className="font-mono text-[11px] text-accent">{field.name}</code>
                  <span className="text-fg-dim">·</span>
                  <span className="text-fg-muted">{field.typeLabel}</span>
                  {field.required && (
                    <Badge tone="neutral" className="text-[10px]">obrigatório</Badge>
                  )}
                </li>
              ))}
            </ul>
          )}
        </div>
      </div>
    </div>
  )
}

interface PromptPreviewProps {
  content: string
}

// Renderiza a string canônica de instructions como markdown rico em vez de
// texto puro: H2 viram subtítulos, listas com bullets/números, fenced ```json
// vira bloco de código com bg destacado. Os schemas de input/output viajam
// inline como seções do próprio prompt — sem cards duplicados embaixo.
function PromptPreview({ content }: PromptPreviewProps) {
  return (
    <div className="rounded-lg border border-border bg-surface px-5 py-4">
      <ReactMarkdown
        remarkPlugins={[remarkGfm]}
        components={{
          h1: ({ children }) => (
            <h1 className="mt-5 text-2xl font-bold tracking-tight text-fg first:mt-0">
              {children}
            </h1>
          ),
          h2: ({ children }) => (
            <h2 className="mt-5 text-base font-bold text-fg first:mt-0">{children}</h2>
          ),
          h3: ({ children }) => (
            <h3 className="mt-4 text-sm font-bold text-fg first:mt-0">{children}</h3>
          ),
          p: ({ children }) => (
            <p className="mt-2 text-[15px] leading-relaxed text-fg first:mt-0">{children}</p>
          ),
          ul: ({ children }) => (
            <ul className="mt-2 ml-5 list-disc space-y-1 text-[15px] leading-relaxed text-fg marker:text-fg-dim">
              {children}
            </ul>
          ),
          ol: ({ children }) => (
            <ol className="mt-2 ml-5 list-decimal space-y-1 text-[15px] leading-relaxed text-fg marker:text-fg-dim">
              {children}
            </ol>
          ),
          li: ({ children }) => <li className="text-fg">{children}</li>,
          strong: ({ children }) => <strong className="font-semibold text-fg">{children}</strong>,
          em: ({ children }) => <em className="italic">{children}</em>,
          a: ({ href, children }) => (
            <a
              href={href}
              target="_blank"
              rel="noreferrer"
              className="text-accent underline-offset-2 hover:underline"
            >
              {children}
            </a>
          ),
          hr: () => <hr className="my-4 border-border" />,
          blockquote: ({ children }) => (
            <blockquote className="mt-3 border-l-2 border-border pl-4 text-fg-muted">
              {children}
            </blockquote>
          ),
          // Inline code (sem className) ganha pílula bg-soft; fenced (com
          // language-* setado pelo remark-gfm) cai no <pre> abaixo, transparente.
          code: ({ className, children, ...rest }) => {
            const isFenced = typeof className === 'string' && className.startsWith('language-')
            return (
              <code
                className={cn(
                  'font-mono text-[12px]',
                  isFenced
                    ? 'block text-fg'
                    : 'rounded bg-bg-soft px-1.5 py-0.5 text-fg',
                  isFenced && className,
                )}
                {...rest}
              >
                {children}
              </code>
            )
          },
          pre: ({ children }) => (
            <pre className="mt-3 overflow-auto rounded-lg border border-border bg-bg-soft p-4 leading-relaxed">
              {children}
            </pre>
          ),
        }}
      >
        {content}
      </ReactMarkdown>
    </div>
  )
}
