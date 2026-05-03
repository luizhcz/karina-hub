import { useMemo, useState } from 'react'
import ReactMarkdown from 'react-markdown'
import remarkGfm from 'remark-gfm'
import { Badge, Button, Card, CardHeader, cn } from '../../ui'
import type { GenericTool } from '../../api/genericTools'
import type { McpServer } from '../../api/mcpServers'
import type { PredefinedModel } from '../../api/predefinedModels'
import { encodeInstructions } from './instructionsCodec'
import { buildToolDescriptors } from './formCodec'
import type { FormState } from './types'

interface ReviewStepProps {
  form: FormState
  models: PredefinedModel[]
  tools: GenericTool[]
  mcps: McpServer[]
}

export function ReviewStep({ form, models, tools, mcps }: ReviewStepProps) {
  const includeStructured = form.agentMode === 'advanced'
  const inputForCodec = form.input.mode === 'structured' ? form.input : { description: '', schema: '' }
  const outputForCodec = form.output.mode === 'structured' ? form.output : { description: '', schema: '' }

  const toolDocs = useMemo(
    () => buildToolDescriptors(form.toolIds, form.mcpIds, tools, mcps),
    [form.toolIds, form.mcpIds, tools, mcps],
  )

  const prompt = useMemo(
    () => encodeInstructions(form.profile, inputForCodec, outputForCodec, toolDocs, includeStructured),
    [form.profile, inputForCodec, outputForCodec, toolDocs, includeStructured],
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
            <dt className="text-[11px] uppercase tracking-wider text-fg-dim">Modelo</dt>
            <dd className="mt-1 text-sm text-fg">
              {selectedModel ? selectedModel.displayName : <span className="italic text-fg-dim">não selecionado</span>}
            </dd>
          </div>
        </dl>
      </Card>

      <Card className="space-y-4">
        <CardHeader
          title="Prompt do agente"
          description="Preview do que será enviado ao modelo. Inclui input/output estruturados como seções do próprio prompt."
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
          title="Ferramentas e MCPs"
          description="Recursos que o agente poderá invocar em runtime."
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
            <p className="mb-1 text-[11px] uppercase tracking-wider text-fg-dim">MCPs</p>
            {selectedMcps.length === 0 ? (
              <p className="text-sm text-fg-muted">Nenhum selecionado.</p>
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
