import { Card, CardHeader, Input } from '../../ui'
import type { FormState } from './types'

interface WorkerProfileStepProps {
  form: FormState
  setForm: (mutator: (prev: FormState) => FormState) => void
  readonly: boolean
}

const SCOPE_MIN = 40
const SCOPE_MAX = 4000

// Step próprio do Worker. Captura nome + domínio de análise (texto livre
// PT-BR). O domínio NÃO entra no instructions persistido — vai pro
// metadata['x-worker-scope'] e o runtime injeta como bloco "# Domínio de
// análise" ao final das instructions. Sem fetch externo, sem dependência
// de pool — Worker é standalone.
export function WorkerProfileStep({ form, setForm, readonly }: WorkerProfileStepProps) {
  const scope = form.workerScope
  const trimmedLength = scope.trim().length
  const tooShort = trimmedLength > 0 && trimmedLength < SCOPE_MIN
  const empty = trimmedLength === 0

  const setName = (value: string) =>
    setForm((prev) => ({ ...prev, name: value }))

  const setScope = (value: string) =>
    setForm((prev) => ({ ...prev, workerScope: value.slice(0, SCOPE_MAX) }))

  return (
    <div className="space-y-5">
      <Card className="space-y-3">
        <CardHeader
          title="Identificação"
          description="Como o Worker aparece na listagem e no chamador downstream."
        />
        <div>
          <label
            htmlFor="worker-name"
            className="text-[11px] uppercase tracking-wider text-fg-dim"
          >
            Nome do Worker
          </label>
          <Input
            id="worker-name"
            value={form.name}
            onChange={(e) => setName(e.target.value)}
            placeholder="Ex.: Analista de Crédito"
            disabled={readonly}
            className="mt-1"
          />
        </div>
      </Card>

      <Card className="space-y-3">
        <CardHeader
          title="Domínio de análise"
          description="Texto livre injetado em runtime ao final do system prompt — bloco '# Domínio de análise'. Use linguagem natural pra delimitar escopo, dados de entrada esperados, política aplicável e o que está fora do escopo."
        />
        <div>
          <label
            htmlFor="worker-scope"
            className="text-[11px] uppercase tracking-wider text-fg-dim"
          >
            Escopo
          </label>
          <textarea
            id="worker-scope"
            value={scope}
            onChange={(e) => setScope(e.target.value)}
            placeholder="Ex.: Análise de risco de crédito empresarial. Recebe perfil do solicitante (porte, segmento, faturamento, histórico, garantias) e produto solicitado. Avalia capacidade de pagamento, exposição agregada, aderência ao apetite de risco. Não consulta bureaus em tempo real (esses dados chegam no input). Não toma decisão final — recomenda; comitê delibera."
            disabled={readonly}
            aria-describedby="worker-scope-help"
            className="mt-1 block min-h-[180px] w-full resize-y rounded-lg border border-border bg-surface px-3 py-2 text-sm leading-relaxed text-fg shadow-sm focus:border-accent focus:outline-none focus:ring-2 focus:ring-accent/30 disabled:cursor-not-allowed disabled:opacity-60"
          />
          <div
            id="worker-scope-help"
            className="mt-1.5 flex items-center justify-between text-[11px]"
          >
            <span className="text-fg-muted">
              {empty
                ? 'Defina o domínio antes de salvar para o Worker funcionar bem em runtime.'
                : tooShort
                  ? 'Domínio muito curto — descreva o escopo com mais detalhe pra orientar o LLM.'
                  : 'Será injetado como bloco anchor no system prompt; edits propagam pra próxima chamada.'}
            </span>
            <span className="text-fg-dim">
              {trimmedLength}/{SCOPE_MAX}
            </span>
          </div>
        </div>
      </Card>
    </div>
  )
}
