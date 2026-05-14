import { Card, CardHeader, JsonSchemaBuilder, StringListEditor, Textarea, cn } from '../../ui'
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
const COMPONENT_MAX_ITEMS = 10

export function OutputStep({ form, setForm, readonly }: OutputStepProps) {
  const updateOutput = (mutator: (prev: StructuredSection) => StructuredSection) =>
    setForm((prev) => ({ ...prev, output: mutator(prev.output) }))

  const isConversational = form.type === 'Conversational'
  const isStructured = form.output.mode === 'structured'

  const componentValues = form.conversationalUiComponents
  const setComponentValues = (values: string[]) => {
    // O codec aplica dedupe + cap + default 'text' no save (encodeConversationalMetadata).
    // Aqui só repassamos o estado da UI sem mexer na ordem ou em duplicatas
    // intermediárias — usuário vê o que digitou; backend recebe limpo.
    setForm((prev) => ({ ...prev, conversationalUiComponents: values }))
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
          values={componentValues}
          onChange={setComponentValues}
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
  values: string[]
  onChange: (next: string[]) => void
  readonly: boolean
}

// Antes morava num step separado ('component') — fundido aqui pra reduzir
// o número de etapas do wizard e refletir que componente + output são duas
// peças do MESMO contrato canônico { ui_component, message, output }. O
// banner azul reforça que a saída é sempre estruturada pra Conversational.
function ConversationalComponentCard({ values, onChange, readonly }: ConversationalComponentCardProps) {
  const count = values.filter((v) => v.trim().length > 0).length
  const limit = COMPONENT_MAX_ITEMS

  return (
    <>
      <div className="rounded-lg border border-accent/20 bg-accent/[0.04] p-4 text-sm">
        <h3 className="text-[13px] font-semibold text-fg">Como o agente responde no chat</h3>
        <p className="mt-1 text-[12px] leading-relaxed text-fg-muted">
          A cada resposta o agente entrega três coisas:{' '}
          <strong className="text-fg">qual cartão</strong> mostrar pro cliente (escolhe entre os
          listados abaixo),{' '}
          <strong className="text-fg">o texto da mensagem</strong> que aparece na bolha do chat,
          e <strong className="text-fg">os dados</strong> que o cartão precisa pra ser desenhado
          (você descreve esses dados nos campos abaixo).
        </p>
      </div>

      <Card className="space-y-3">
        <CardHeader
          title="Cartões possíveis"
          description={
            'Liste os visuais que o front pode desenhar (ex.: card_pedido, lista_extratos, alerta_risco). ' +
            'O agente vai escolher exatamente um deles a cada resposta. ' +
            'Sem nenhum cartão listado, o agente cai no padrão "text" (só a mensagem do chat).'
          }
        />
        <div aria-describedby="conversational-component-help">
          <StringListEditor
            values={values}
            onChange={readonly ? () => {} : onChange}
            itemPlaceholder="card_pedido"
            emptyHint="Nenhum cartão listado — o agente vai usar 'text' (só mensagem)."
            monospace
            max={limit}
            maxLength={COMPONENT_MAX_LENGTH}
          />
          <p id="conversational-component-help" className="mt-2 text-[11px] text-fg-dim">
            {count > 0
              ? `O chat vai escolher um entre ${count} cartões a cada resposta.`
              : 'Sem cartões listados, o agente sempre responde com o padrão "text".'}
          </p>
        </div>
      </Card>
    </>
  )
}
