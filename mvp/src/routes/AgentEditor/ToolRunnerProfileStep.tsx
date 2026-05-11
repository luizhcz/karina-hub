import { Card, CardHeader, Input, cn } from '../../ui'
import type { FormState } from './types'

interface ToolRunnerProfileStepProps {
  form: FormState
  setForm: (mutator: (prev: FormState) => FormState) => void
  readonly: boolean
}

// Step próprio do Tool Runner. Captura nome + política de aprovação
// humana (HITL). O HITL é declaração de intenção: persistido em
// metadata['x-tool-runner-hitl-required'] e usado pela validação de save
// pra emitir warning quando há tools com requiresApproval=true e a flag
// está off. Runtime de chamada de tool ainda não enforça o bloqueio.
export function ToolRunnerProfileStep({ form, setForm, readonly }: ToolRunnerProfileStepProps) {
  const setName = (value: string) =>
    setForm((prev) => ({ ...prev, name: value }))

  const toggleHitl = () =>
    setForm((prev) => ({
      ...prev,
      toolRunnerHitlRequired: !prev.toolRunnerHitlRequired,
    }))

  const hitlOn = form.toolRunnerHitlRequired

  return (
    <div className="space-y-5">
      <Card className="space-y-3">
        <CardHeader
          title="Identificação"
          description="Nome do agente como aparece na listagem e no chamador downstream."
        />
        <div>
          <label
            htmlFor="tool-runner-name"
            className="text-[11px] uppercase tracking-wider text-fg-dim"
          >
            Nome do Tool Runner
          </label>
          <Input
            id="tool-runner-name"
            value={form.name}
            onChange={(e) => setName(e.target.value)}
            placeholder="Ex.: Coletor de Boleta"
            disabled={readonly}
            className="mt-1"
          />
        </div>
      </Card>

      <Card className="space-y-3">
        <CardHeader
          title="Política operacional"
          description="Tool Runner é o tipo com maior superfície de risco — tools podem custar dinheiro, mover dados ou criar registros. Esta seção captura a intenção declarada do agente; ferramentas e middlewares são selecionados nos próximos steps."
        />
        <div
          className={cn(
            'flex items-start justify-between gap-3 rounded-lg border px-3 py-3',
            hitlOn ? 'border-warning/40 bg-warning/10' : 'border-border bg-bg-soft',
          )}
        >
          <div className="min-w-0">
            <p className="text-xs font-medium text-fg">
              Exigir aprovação humana antes de tools com side-effect
            </p>
            <p className="mt-1 text-[11px] leading-relaxed text-fg-muted">
              Sinaliza que o agente só deve invocar tools com{' '}
              <code className="rounded bg-bg-soft px-1 py-0.5 font-mono text-[10px]">
                requiresApproval=true
              </code>{' '}
              após confirmação humana. A validação no save reporta inconsistência
              (tools de aprovação selecionadas com este flag desligado).
            </p>
          </div>
          <button
            type="button"
            onClick={toggleHitl}
            disabled={readonly}
            aria-pressed={hitlOn}
            className={cn(
              'shrink-0 rounded-full border px-3 py-1 text-[11px] font-semibold uppercase tracking-wide transition',
              hitlOn
                ? 'border-warning bg-warning text-white'
                : 'border-border bg-surface text-fg-muted hover:border-accent/40',
              readonly && 'cursor-not-allowed opacity-60',
            )}
          >
            {hitlOn ? 'Exigido' : 'Não exigido'}
          </button>
        </div>
        <p className="text-[11px] leading-relaxed text-fg-dim">
          Recomendação do template: ativar AccountGuard no step Segurança para
          isolar conta/escopo das chamadas, mesmo quando HITL não estiver
          marcado.
        </p>
      </Card>
    </div>
  )
}
