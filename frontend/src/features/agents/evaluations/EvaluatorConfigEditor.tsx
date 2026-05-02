import { useEffect, useState } from 'react'
import {
  useEvaluatorConfig,
  useUpsertEvaluatorConfig,
  useEvaluatorConfigHistory,
} from '../../../api/evaluations'
import type {
  EvaluatorBinding,
  SplitterStrategy,
} from '../../../api/evaluations'
import { Button } from '../../../shared/ui/Button'
import { Card } from '../../../shared/ui/Card'
import { Input } from '../../../shared/ui/Input'
import { Badge } from '../../../shared/ui/Badge'
import { EvaluatorPicker } from '../../evaluations/components/EvaluatorPicker'

interface Props {
  agentId: string
}

const PRESET_BINDINGS: EvaluatorBinding[] = [
  { kind: 'Meai', name: 'Relevance', enabled: true, weight: 1, bindingIndex: 0 },
  { kind: 'Meai', name: 'Coherence', enabled: true, weight: 1, bindingIndex: 0 },
  { kind: 'Local', name: 'ContainsExpected', enabled: true, weight: 1, bindingIndex: 0 },
]

export function EvaluatorConfigEditor({ agentId }: Props) {
  const { data, isLoading } = useEvaluatorConfig(agentId)
  const { data: history } = useEvaluatorConfigHistory(agentId)
  const upsertMutation = useUpsertEvaluatorConfig()

  const [name, setName] = useState('default')
  const [bindings, setBindings] = useState<EvaluatorBinding[]>(PRESET_BINDINGS)
  const [splitter, setSplitter] = useState<SplitterStrategy>('LastTurn')
  const [numRepetitions, setNumRepetitions] = useState(3)
  const [changeReason, setChangeReason] = useState('')

  // Hidrata form com config atual; reseta em troca de agentId pra evitar
  // exibir config do agente anterior.
  const [hydrated, setHydrated] = useState(false)
  useEffect(() => { setHydrated(false) }, [agentId])
  useEffect(() => {
    if (hydrated || isLoading) return
    if (data?.currentVersion && data.config) {
      const v = data.currentVersion
      setName(data.config.name)
      setBindings(v.bindings)
      setSplitter(v.splitter)
      setNumRepetitions(v.numRepetitions)
    }
    setHydrated(true)
  }, [data, isLoading, hydrated])

  const handleSubmit = async () => {
    if (bindings.length === 0) return
    await upsertMutation.mutateAsync({
      agentId,
      body: {
        name,
        bindings,
        splitter,
        numRepetitions,
        changeReason: changeReason.trim() || undefined,
      },
    })
    setChangeReason('')
  }

  return (
    <div className="space-y-4">
      <Card>
        <h3 className="text-lg font-semibold mb-3">Bindings</h3>
        <EvaluatorPicker selected={bindings} onChange={setBindings} />
      </Card>

      <Card>
        <h3 className="text-lg font-semibold mb-3">Parâmetros de execução</h3>
        <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
          <div>
            <label className="block text-xs text-text-muted mb-1">Splitter</label>
            <select
              value={splitter}
              onChange={(e) => setSplitter(e.target.value as SplitterStrategy)}
              className="w-full px-3 py-2 text-sm rounded-md bg-bg-secondary border border-border-secondary text-text-primary"
            >
              <option value="LastTurn">LastTurn (default — última mensagem)</option>
              <option value="Full">Full (toda a conversa)</option>
              <option value="PerTurn">PerTurn (1 eval por turno)</option>
            </select>
          </div>
          <Input
            label="Repetições (1-10)"
            type="number"
            min={1}
            max={10}
            value={numRepetitions}
            onChange={(e) => setNumRepetitions(Number(e.target.value) || 1)}
          />
        </div>
        <div className="mt-2 text-xs text-text-muted">
          NumRepetitions=3 reduz variância do judge LLM; n=1 mascara regressão.
        </div>
      </Card>

      <Card>
        <Input
          label="Change reason (opcional)"
          value={changeReason}
          onChange={(e) => setChangeReason(e.target.value)}
          placeholder="Ex.: Adicionado Groundedness pra cobertura RAG"
        />
        <div className="flex justify-between items-center mt-3">
          <div className="text-xs text-text-muted">
            {bindings.length} binding{bindings.length !== 1 ? 's' : ''} ativo{bindings.length !== 1 ? 's' : ''}.
            Mudança cria nova version (idempotente — config idêntica é no-op).
          </div>
          <Button
            variant="primary"
            onClick={handleSubmit}
            loading={upsertMutation.isPending}
            disabled={bindings.length === 0}
          >
            Salvar config
          </Button>
        </div>
        {upsertMutation.error && (
          <div className="mt-2 text-sm text-red-400">{(upsertMutation.error as Error).message}</div>
        )}
      </Card>

      {history && history.length > 0 && (
        <Card>
          <h3 className="text-lg font-semibold mb-3">Histórico de versões</h3>
          <div className="space-y-2 max-h-72 overflow-y-auto">
            {history.map((v) => (
              <div
                key={v.evaluatorConfigVersionId}
                className="border border-border-secondary rounded-lg px-3 py-2"
              >
                <div className="flex items-center gap-2 mb-1">
                  <span className="font-mono text-sm font-semibold">rev {v.revision}</span>
                  <Badge variant={v.status === 'Published' ? 'green' : 'gray'}>{v.status}</Badge>
                  <span className="text-xs font-mono text-text-muted">
                    {v.contentHash.slice(0, 8)}…
                  </span>
                  <span className="text-xs text-text-muted ml-auto">
                    {new Date(v.createdAt).toLocaleString('pt-BR')}
                  </span>
                </div>
                {v.changeReason && (
                  <div className="text-xs text-text-muted">{v.changeReason}</div>
                )}
                <div className="text-xs text-text-muted mt-1">
                  {v.bindings.length} binding{v.bindings.length !== 1 ? 's' : ''} · splitter={v.splitter} · reps={v.numRepetitions}
                </div>
              </div>
            ))}
          </div>
        </Card>
      )}
    </div>
  )
}
