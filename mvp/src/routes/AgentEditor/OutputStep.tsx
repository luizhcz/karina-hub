import { Card, CardHeader, Input, JsonSchemaBuilder, Textarea, cn } from '../../ui'
import type { FormState, StructuredSection } from './types'

interface OutputStepProps {
  form: FormState
  setForm: (mutator: (prev: FormState) => FormState) => void
  readonly: boolean
}

interface ModeCardProps {
  active: boolean
  title: string
  description: string
  onClick: () => void
  disabled: boolean
}

function ModeCard({ active, title, description, onClick, disabled }: ModeCardProps) {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      className={cn(
        'flex flex-1 flex-col items-start gap-1 rounded-xl border px-4 py-3 text-left transition focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent/30',
        disabled && 'cursor-not-allowed opacity-60',
        active
          ? 'border-accent bg-accent-subtle/40'
          : 'border-border bg-surface hover:border-accent/40 hover:bg-surface-hover',
      )}
    >
      <span className="text-sm font-semibold text-fg">{title}</span>
      <span className="text-xs text-fg-muted">{description}</span>
    </button>
  )
}

const COMPONENT_MAX_LENGTH = 64

export function OutputStep({ form, setForm, readonly }: OutputStepProps) {
  const updateOutput = (mutator: (prev: StructuredSection) => StructuredSection) =>
    setForm((prev) => ({ ...prev, output: mutator(prev.output) }))

  const isConversational = form.type === 'Conversational'
  const isStructured = form.output.mode === 'structured'

  const componentValue = form.conversationalUiComponents[0] ?? ''
  const setComponent = (value: string) => {
    const trimmed = value.slice(0, COMPONENT_MAX_LENGTH)
    setForm((prev) => ({
      ...prev,
      // Array com 1 elemento (ou vazio quando o user limpa o input) — o codec
      // gera enum de 1 valor no schema canônico `ui_component`. Sem branch
      // pra manter a serialização consistente com agentes legacy.
      conversationalUiComponents: trimmed.trim() ? [trimmed.trim()] : [],
    }))
  }

  return (
    <div className="space-y-5">
      <Card className="space-y-3">
        <CardHeader
          title="Tipo de saída"
          description={
            isConversational
              ? 'Como o agente responde no chat. Texto livre pra conversa natural; estruturado quando o front precisa desenhar um cartão (botões, lista, formulário, etc.).'
              : 'Como o agente responde. Texto livre pra conversa natural; estruturado quando outro sistema vai consumir a saída.'
          }
        />
        <div className="flex gap-3">
          <ModeCard
            active={form.output.mode === 'text'}
            title="Texto livre"
            description={
              isConversational
                ? 'O agente responde em mensagem comum — só texto na bolha do chat.'
                : 'O agente responde em linguagem natural.'
            }
            onClick={() => updateOutput((prev) => ({ ...prev, mode: 'text' }))}
            disabled={readonly}
          />
          <ModeCard
            active={form.output.mode === 'structured'}
            title="Estruturado"
            description={
              isConversational
                ? 'O agente preenche um cartão com campos definidos (além da mensagem de texto).'
                : 'O agente devolve um objeto com campos definidos.'
            }
            onClick={() => updateOutput((prev) => ({ ...prev, mode: 'structured' }))}
            disabled={readonly}
          />
        </div>
      </Card>

      {isConversational && isStructured && (
        <ConversationalComponentCard
          value={componentValue}
          onChange={setComponent}
          readonly={readonly}
        />
      )}

      {isStructured && (
        <>
          <Card className="space-y-3">
            <CardHeader
              title={isConversational ? 'Que dados o cartão precisa' : 'Regras do output'}
              description={
                isConversational
                  ? 'Em texto, descreva os dados que esse cartão recebe a cada resposta — o agente vai preencher esses dados quando responder.'
                  : 'Descreva em linguagem natural o que cada campo significa e quando o agente deve preencher.'
              }
            />
            <Textarea
              value={form.output.description}
              onChange={(e) =>
                updateOutput((prev) => ({ ...prev, description: e.target.value }))
              }
              placeholder={
                isConversational
                  ? 'Ex.: "ticker, quantidade e preço da ordem que o cliente acabou de confirmar."'
                  : 'Ex.: "Retorna análise da conversa com sentimento (positivo|neutro|negativo) e resumo curto."'
              }
              className="min-h-[100px]"
              disabled={readonly}
            />
          </Card>

          <Card className="space-y-3">
            <CardHeader
              title={isConversational ? 'Campos do cartão' : 'Estrutura do output'}
              description={
                isConversational
                  ? 'Liste cada dado que o cartão recebe, com o tipo (texto, número, etc.). O agente vai preencher esses campos quando responder.'
                  : 'Monte os campos que o agente vai retornar. Disponível também em modo JSON cru.'
              }
            />
            <JsonSchemaBuilder
              value={form.output.schema}
              onChange={(schema) => updateOutput((prev) => ({ ...prev, schema }))}
              emptyHint={
                isConversational
                  ? 'Adicione os campos que o agente vai preencher. Deixe vazio se o cartão só mostra o texto da mensagem.'
                  : 'Adicione os campos que o agente vai retornar.'
              }
            />
          </Card>
        </>
      )}
    </div>
  )
}

interface ConversationalComponentCardProps {
  value: string
  onChange: (next: string) => void
  readonly: boolean
}

// Antes morava num step separado ('component') — fundido aqui pra reduzir
// o número de etapas do wizard e refletir que componente + output são duas
// peças do MESMO contrato canônico { ui_component, message, output }. O
// banner azul reforça que a saída é sempre estruturada pra Conversational.
function ConversationalComponentCard({ value, onChange, readonly }: ConversationalComponentCardProps) {
  return (
    <>
      <div className="rounded-lg border border-accent/20 bg-accent/[0.04] p-4 text-sm">
        <h3 className="text-[13px] font-semibold text-fg">Como o agente responde no chat</h3>
        <p className="mt-1 text-[12px] leading-relaxed text-fg-muted">
          A cada resposta o agente entrega três coisas:{' '}
          <strong className="text-fg">qual cartão</strong> mostrar pro cliente (definido abaixo),
          {' '}
          <strong className="text-fg">o texto da mensagem</strong> que aparece na bolha do chat,
          e <strong className="text-fg">os dados</strong> que o cartão precisa pra ser desenhado
          (você descreve esses dados nos campos abaixo).
        </p>
      </div>

      <Card className="space-y-3">
        <CardHeader
          title="Nome do cartão"
          description="Nome curto do visual que o agente desenha na tela do chat (ex.: card_pedido, lista_extratos, alerta_risco). O time de front usa esse nome pra exibir o cartão certo. Use letras minúsculas, números e underscore."
        />
        <div>
          <label
            htmlFor="conversational-component"
            className="text-[11px] uppercase tracking-wider text-fg-dim"
          >
            Nome
          </label>
          <Input
            id="conversational-component"
            value={value}
            onChange={(e) => onChange(e.target.value)}
            placeholder="card_pedido"
            disabled={readonly}
            className="mt-1"
            aria-describedby="conversational-component-help"
          />
          <p id="conversational-component-help" className="mt-1.5 text-[11px] text-fg-dim">
            {value
              ? `O chat vai mostrar o cartão "${value}" a cada resposta.`
              : 'Defina o nome do cartão antes de salvar — sem ele, o chat só mostra o texto da mensagem.'}
          </p>
        </div>
      </Card>
    </>
  )
}
