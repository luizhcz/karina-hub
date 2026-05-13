import { useEffect, useMemo, useState } from 'react'
import ReactMarkdown from 'react-markdown'
import remarkGfm from 'remark-gfm'
import { Badge, Button, Card, CardHeader, cn } from '../../ui'
import type { GenericTool } from '../../api/genericTools'
import type { McpServer } from '../../api/mcpServers'
import type { PredefinedModel } from '../../api/predefinedModels'
import { listRouterIntents, type RouterIntent } from '../../api/routerIntents'
import { encodeInstructions } from './instructionsCodec'
import {
  buildToolDescriptors,
  encodeConversationalInstructions,
  encodeRouterInstructions,
  encodeToolRunnerInstructions,
  encodeWorkerInstructions,
} from './formCodec'
import type { FormState } from './types'

interface ReviewStepProps {
  form: FormState
  setForm: (mutator: (prev: FormState) => FormState) => void
  models: PredefinedModel[]
  tools: GenericTool[]
  mcps: McpServer[]
  readonly: boolean
}

export function ReviewStep({ form, setForm, models, tools, mcps, readonly }: ReviewStepProps) {
  const includeStructured = form.agentMode === 'advanced'
  const inputForCodec = form.input.mode === 'structured' ? form.input : { description: '', schema: '' }
  const outputForCodec = form.output.mode === 'structured' ? form.output : { description: '', schema: '' }

  const toolDocs = useMemo(
    () => buildToolDescriptors(form.toolIds, form.mcpIds, tools, mcps),
    [form.toolIds, form.mcpIds, tools, mcps],
  )

  const prompt = useMemo(
    () =>
      form.type === 'Router'
        ? encodeRouterInstructions(form.name)
        : form.type === 'Worker'
          ? encodeWorkerInstructions(form.name)
          : form.type === 'ToolRunner'
            ? encodeToolRunnerInstructions(form.name)
            : form.type === 'Conversational'
              ? encodeConversationalInstructions(form.profile)
              : encodeInstructions(form.profile, inputForCodec, outputForCodec, toolDocs, includeStructured),
    [
      form.type,
      form.name,
      form.profile,
      inputForCodec,
      outputForCodec,
      toolDocs,
      includeStructured,
    ],
  )

  const selectedModel = models.find((m) => m.id === form.predefinedModelId) ?? null

  const selectedTools = useMemo(
    () => tools.filter((t) => form.toolIds.includes(t.id)),
    [tools, form.toolIds],
  )
  const selectedMcps = useMemo(
    () => mcps.filter((m) => form.mcpIds.includes(m.id)),
    [mcps, form.mcpIds],
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
      <Card className="space-y-3">
        <CardHeader
          title="Identificação"
          description="Resumo do que será gravado no rascunho."
        />
        <dl className="grid grid-cols-1 gap-3 sm:grid-cols-2">
          <div>
            <dt className="text-[11px] uppercase tracking-wider text-fg-dim">Nome</dt>
            <dd className="mt-1 text-sm text-fg">
              {form.name.trim() || <span className="italic text-fg-dim">não preenchido</span>}
            </dd>
          </div>
          <div>
            <dt className="text-[11px] uppercase tracking-wider text-fg-dim">Tipo</dt>
            <dd className="mt-1 text-sm text-fg">
              <Badge tone={form.type === 'Router' ? 'accent' : 'neutral'}>
                {form.type}
              </Badge>
            </dd>
          </div>
          <div>
            <dt className="text-[11px] uppercase tracking-wider text-fg-dim">Modelo</dt>
            <dd className="mt-1 text-sm text-fg">
              {selectedModel ? selectedModel.displayName : <span className="italic text-fg-dim">não selecionado</span>}
            </dd>
          </div>
        </dl>
      </Card>

      {form.type === 'Router' && <RouterPreview form={form} />}

      {form.type === 'Worker' && <WorkerPreview form={form} />}

      {form.type === 'ToolRunner' && (
        <ToolRunnerPreview form={form} tools={tools} mcps={mcps} />
      )}

      {form.type === 'Conversational' && <ConversationalPreview form={form} />}

      <SecurityReviewCard
        enabled={form.security.enabled}
        type={form.type}
        disabled={readonly}
        onToggle={() =>
          setForm((prev) => ({
            ...prev,
            security: { ...prev.security, enabled: !prev.security.enabled },
          }))
        }
      />

      <Card className="space-y-4">
        <CardHeader
          title="Prompt do agente"
          description="Texto completo que o agente vai ler antes de cada resposta — o que você escreveu + a documentação das ferramentas + as regras de formato."
          actions={
            <Button variant="secondary" size="sm" onClick={onCopy} disabled={!prompt}>
              {copied ? 'Copiado!' : 'Copiar'}
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

      <Card className="space-y-3">
        <CardHeader
          title="Ferramentas e integrações externas"
          description="Recursos que o agente pode usar durante a conversa pra buscar dados ou executar ações."
        />
        <div className="space-y-3">
          <div>
            <p className="mb-1 text-[11px] uppercase tracking-wider text-fg-dim">Ferramentas</p>
            {selectedTools.length === 0 ? (
              <p className="text-sm text-fg-muted">Nenhuma selecionada.</p>
            ) : (
              <div className="flex flex-wrap gap-2">
                {selectedTools.map((t) => (
                  <Badge key={t.id} tone="accent">
                    {t.name || t.id}
                  </Badge>
                ))}
              </div>
            )}
          </div>
          <div>
            <p
              className="mb-1 text-[11px] uppercase tracking-wider text-fg-dim"
              title="MCPs (Model Context Protocol) — servidores externos que expõem ferramentas e dados pro agente consumir."
            >
              Integrações externas
            </p>
            {selectedMcps.length === 0 ? (
              <p className="text-sm text-fg-muted">Nenhuma selecionada.</p>
            ) : (
              <div className="flex flex-wrap gap-2">
                {selectedMcps.map((m) => (
                  <Badge key={m.id} tone="accent">
                    {m.name || m.id}
                  </Badge>
                ))}
              </div>
            )}
          </div>
        </div>
      </Card>
    </div>
  )
}

interface RouterPreviewProps {
  form: FormState
}

// Preview no Review das intents que este Router atende. Fetch do pool global
// + filter pelas selecionadas em form.routerIntentIds. Mostra warning quando
// <2 selecionadas — backend rejeita o save.
function RouterPreview({ form }: RouterPreviewProps) {
  const [pool, setPool] = useState<RouterIntent[]>([])
  const [loading, setLoading] = useState(true)
  useEffect(() => {
    let cancelled = false
    listRouterIntents()
      .then((items) => {
        if (!cancelled) setPool(items)
      })
      .catch(() => undefined)
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [])

  const selected = pool.filter((i) => form.routerIntentIds.includes(i.id))
  const tooFew = form.routerIntentIds.length < 2

  return (
    <Card className="space-y-3">
      <CardHeader
        title="Intenções atendidas"
        description="As categorias que este Router pode escolher. Edits no pool propagam pra próxima chamada — adicionar intenção nova ao pool não inclui automaticamente neste Router."
      />
      {loading ? (
        <p className="text-xs text-fg-muted">Carregando…</p>
      ) : form.routerIntentIds.length === 0 ? (
        <p
          className={cn(
            'rounded-lg border px-3 py-2 text-xs',
            'border-warning/40 bg-warning/10 text-warning',
          )}
        >
          Nenhuma intenção marcada. Volte pra etapa Intenções e selecione ao menos 2 antes de submeter.
        </p>
      ) : (
        <>
          <ul className="space-y-2">
            {selected.map((intent) => (
              <li
                key={intent.id}
                className="rounded-lg border border-border bg-bg-soft px-3 py-2"
              >
                <div className="flex items-center gap-2">
                  <Badge tone="accent">{intent.name}</Badge>
                </div>
                {intent.description.trim() && (
                  <p className="mt-1 text-xs leading-relaxed text-fg-muted">
                    {intent.description.trim()}
                  </p>
                )}
              </li>
            ))}
          </ul>
          {tooFew && (
            <p className="rounded-lg border border-warning/40 bg-warning/10 px-3 py-2 text-xs text-warning">
              Pelo menos 2 intenções são obrigatórias. Selecione mais antes de submeter.
            </p>
          )}
        </>
      )}
    </Card>
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
  mcps: McpServer[]
}

// Preview do Tool Runner no Review: mostra tools/MCPs selecionados (cada
// um sinaliza requiresApproval=true se aplicável), status do flag HITL e
// status do middleware AccountGuard (presente no payload.middlewares —
// avaliado via FormState pra UX, valor real é montado no save).
// Tool Runner sem tools recebe warning soft no save; aqui é sinalizado.
function ToolRunnerPreview({ form, tools, mcps }: ToolRunnerPreviewProps) {
  const selectedTools = tools.filter((t) => form.toolIds.includes(t.id))
  const selectedMcps = mcps.filter((m) => form.mcpIds.includes(m.id))
  const noTools = selectedTools.length === 0 && selectedMcps.length === 0
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
        <div className="space-y-2">
          <div>
            <p className="mb-1 text-[11px] uppercase tracking-wider text-fg-dim">
              Ferramentas
            </p>
            {selectedTools.length === 0 ? (
              <p className="text-sm text-fg-muted">Nenhuma function/HTTP selecionada.</p>
            ) : (
              <div className="flex flex-wrap gap-2">
                {selectedTools.map((t) => (
                  <Badge key={t.id} tone="accent">
                    {t.name || t.id}
                  </Badge>
                ))}
              </div>
            )}
          </div>
          <div>
            <p className="mb-1 text-[11px] uppercase tracking-wider text-fg-dim">
              MCPs
            </p>
            {selectedMcps.length === 0 ? (
              <p className="text-sm text-fg-muted">Nenhum MCP selecionado.</p>
            ) : (
              <div className="flex flex-wrap gap-2">
                {selectedMcps.map((m) => (
                  <Badge key={m.id} tone="accent">
                    {m.name || m.serverLabel || m.id}
                  </Badge>
                ))}
              </div>
            )}
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

// Lê o sub-schema do output (form.output.schema é texto JSON) e devolve a
// lista das propriedades top-level pra exibir no Review como bullets.
// Schema inválido devolve null (caller mostra warning).
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
      // type pode ser string (primitive) ou array (nullable JSON Schema
      // standard). Em arrays mostra "lista de X".
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

// Preview do Conversational no Review: mostra o nome do cartão (info
// exclusiva do tipo) e os dados que ele preenche a cada resposta — bullets
// dos campos top-level com tipo e obrigatoriedade. A descrição em markdown
// já aparece renderizada no card "Prompt do agente" logo abaixo; aqui
// concentramos no contrato do cartão (cartão + dados), que é o que o time
// de frontend precisa pra construir o renderer.
function ConversationalPreview({ form }: ConversationalPreviewProps) {
  const uiComponents = form.conversationalUiComponents
  const noUiComponents = uiComponents.length === 0
  const isStructured = form.output.mode === 'structured'
  const outputFields = isStructured ? summarizeOutputFields(form.output.schema) : []
  const schemaInvalid = outputFields === null

  return (
    <Card className="space-y-3">
      <CardHeader
        title="Cartão desenhado no chat"
        description="Nome do visual que o agente entrega a cada resposta — o time de front usa pra desenhar o cartão certo na tela do chat."
      />
      {noUiComponents ? (
        <p className="rounded-lg border border-warning/40 bg-warning/10 px-3 py-2 text-xs text-warning">
          Nome do cartão não definido — o chat vai mostrar só o texto da mensagem, sem visual personalizado.
        </p>
      ) : (
        <div className="flex flex-wrap gap-2">
          {uiComponents.map((value) => (
            <Badge key={value} tone="accent">
              <code className="font-mono text-[11px]">{value}</code>
            </Badge>
          ))}
        </div>
      )}

      <div>
        <p className="mb-1 text-[11px] uppercase tracking-wider text-fg-dim">
          Dados que o cartão recebe
        </p>
        {!isStructured ? (
          <p className="rounded-lg border border-border bg-bg-soft px-3 py-2 text-xs text-fg-muted">
            Resposta em texto livre — o agente só entrega a mensagem, sem dados extras pro cartão.
          </p>
        ) : schemaInvalid ? (
          <p className="rounded-lg border border-warning/40 bg-warning/10 px-3 py-2 text-xs text-warning">
            Estrutura do cartão inválida — corrija na etapa Output.
          </p>
        ) : outputFields.length === 0 ? (
          <p className="rounded-lg border border-border bg-bg-soft px-3 py-2 text-xs text-fg-muted">
            Nenhum campo definido — o cartão recebe só a mensagem.
          </p>
        ) : (
          <ul className="space-y-1 rounded-lg border border-border bg-bg-soft px-3 py-2">
            {outputFields.map((field) => (
              <li
                key={field.name}
                className="flex items-baseline gap-2 text-xs text-fg"
              >
                <code className="font-mono text-[11px] text-accent">{field.name}</code>
                <span className="text-fg-muted">·</span>
                <span className="text-fg-muted">{field.typeLabel}</span>
                {field.required && (
                  <Badge tone="neutral" className="text-[10px]">obrigatório</Badge>
                )}
              </li>
            ))}
          </ul>
        )}
      </div>
    </Card>
  )
}

interface SecurityReviewCardProps {
  enabled: boolean
  type: FormState['type']
  disabled: boolean
  onToggle: () => void
}

// Card permanente de Segurança no Review. Diferente do SecurityBanner (que é
// reativo e pode passar despercebido quando ativo), este sempre aparece com
// estado on/off + toggle inline + hint específico pelo tipo do agente —
// crítico pro Router que não tem step próprio de Segurança no Stepper.
function SecurityReviewCard({ enabled, type, disabled, onToggle }: SecurityReviewCardProps) {
  const description =
    type === 'Router'
      ? 'Router classifica e devolve label estruturada — output não vai pro usuário, então o vetor de prompt injection é menor. Ative se o input vem de canal hostil e quer proteção extra; do contrário, manter desligado economiza ~300 tokens/chamada.'
      : 'Adiciona política de sistema fixa: trata input do usuário como dado (não instrução), bloqueia troca de persona, recusa pedidos fora do escopo e impede vazamento de instruções/identificadores. Custo ~300 tokens por chamada.'

  return (
    <Card className="space-y-3">
      <CardHeader
        title="Guardrails de segurança"
        description={description}
        actions={
          <Badge tone={enabled ? 'success' : 'neutral'}>
            {enabled ? 'Ativos' : 'Desligados'}
          </Badge>
        }
      />
      <div className="flex items-center justify-between gap-3 rounded-lg border border-border bg-bg-soft px-3 py-2.5">
        <div className="min-w-0">
          <p className="text-xs font-medium text-fg">
            {enabled
              ? 'Guardrails ativos — agente bloqueia prompt injection e respostas fora do escopo.'
              : 'Sem guardrails — agente segue apenas o perfil declarado.'}
          </p>
          <p className="mt-0.5 text-[11px] text-fg-muted">
            Política fixa da plataforma; texto não é editável.
          </p>
        </div>
        <Button
          variant="secondary"
          size="sm"
          onClick={onToggle}
          disabled={disabled}
          className="shrink-0"
        >
          {enabled ? 'Desativar' : 'Ativar'}
        </Button>
      </div>
    </Card>
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
