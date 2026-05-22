import { Card, CardHeader, Input, JsonSchemaBuilder, StringListEditor, Textarea, cn } from '../../ui'
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

const STATUS_MAX_LENGTH = 64
const STATUS_MAX_ITEMS = 10
const OUTPUT_TYPE_MAX_LENGTH = 64

export function OutputStep({ form, setForm, readonly }: OutputStepProps) {
  const updateOutput = (mutator: (prev: StructuredSection) => StructuredSection) =>
    setForm((prev) => ({ ...prev, output: mutator(prev.output) }))

  const isConversational = form.type === 'Conversational'
  const isStructured = form.output.mode === 'structured'

  const setOutputType = (next: string) =>
    setForm((prev) => ({ ...prev, conversationalOutputType: next }))
  const setOutputStatuses = (values: string[]) => {
    // Encoder aplica dedupe + cap + default no save (encodeConversationalMetadata).
    // Aqui só repassamos o estado da UI — usuário vê o que digitou.
    setForm((prev) => ({ ...prev, conversationalOutputStatuses: values }))
  }

  return (
    <div className="space-y-5">
      {isConversational && (
        <ConversationalContractCard
          outputType={form.conversationalOutputType}
          onOutputTypeChange={setOutputType}
          statuses={form.conversationalOutputStatuses}
          onStatusesChange={setOutputStatuses}
          readonly={readonly}
        />
      )}

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
              autoGrow
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

interface ConversationalContractCardProps {
  outputType: string
  onOutputTypeChange: (next: string) => void
  statuses: string[]
  onStatusesChange: (next: string[]) => void
  readonly: boolean
}

// Contrato canônico do Conversational: output_type (família única do
// renderer) + output_status (variações). O front consome ambos pra escolher
// qual cartão desenhar a cada resposta. Mostrado no topo do step pra que o
// PM declare o contrato antes de detalhar o output.
function ConversationalContractCard({
  outputType,
  onOutputTypeChange,
  statuses,
  onStatusesChange,
  readonly,
}: ConversationalContractCardProps) {
  const statusCount = statuses.filter((v) => v.trim().length > 0).length

  return (
    <>
      <div className="rounded-lg border border-accent/20 bg-accent/[0.04] p-4 text-sm">
        <h3 className="text-[13px] font-semibold text-fg">Como o agente responde no chat</h3>
        <p className="mt-1 text-[12px] leading-relaxed text-fg-muted">
          A cada resposta o agente entrega quatro coisas:{' '}
          <strong className="text-fg">output_type</strong> (família do cartão — fixa por agente),{' '}
          <strong className="text-fg">output_status</strong> (variação dentro dessa família —
          escolhida entre as opções listadas abaixo),{' '}
          <strong className="text-fg">message</strong> (texto da bolha do chat) e,{' '}
          opcionalmente,{' '}
          <strong className="text-fg">output</strong> (dados estruturados pro cartão).
        </p>
      </div>

      <Card className="space-y-3">
        <CardHeader
          title="Tipo do cartão (output_type)"
          description={
            'String única que identifica a família de renderer no front. ' +
            'Ex.: boleta, cotacao, alerta_risco. Mantenha estável — trocar o tipo após o front mapear ' +
            'gera retrabalho.'
          }
        />
        <Input
          value={outputType}
          onChange={(e) => onOutputTypeChange(e.target.value)}
          placeholder="text"
          maxLength={OUTPUT_TYPE_MAX_LENGTH}
          monospace
          disabled={readonly}
        />
        <p className="text-[11px] text-fg-dim">
          Vazio cai pro padrão <code className="font-mono">text</code> (resposta só em texto).
        </p>
      </Card>

      <Card className="space-y-3">
        <CardHeader
          title="Status possíveis (output_status)"
          description={
            'Lista as variações de estado que o agente pode emitir dentro do output_type acima. ' +
            'Ex.: nova, confirmada, erro, cancelada. O LLM escolhe exatamente um a cada resposta.'
          }
        />
        <StringListEditor
          values={statuses}
          onChange={readonly ? () => {} : onStatusesChange}
          itemPlaceholder="default"
          emptyHint='Nenhum status listado — o agente vai usar "default".'
          monospace
          max={STATUS_MAX_ITEMS}
          maxLength={STATUS_MAX_LENGTH}
        />
        <p className="text-[11px] text-fg-dim">
          {statusCount > 0
            ? `O chat vai escolher um entre ${statusCount} status${statusCount === 1 ? '' : 'es'} a cada resposta.`
            : 'Sem statuses listados, o agente sempre responde com o status "default".'}
        </p>
      </Card>
    </>
  )
}
