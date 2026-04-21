import { useState } from 'react'
import { useNavigate } from 'react-router'
import { Card } from '../../shared/ui/Card'
import { Button } from '../../shared/ui/Button'
import { Select } from '../../shared/ui/Select'
import { Input } from '../../shared/ui/Input'
import { ErrorCard } from '../../shared/ui/ErrorCard'

type JobType = 'CleanupOldExecutions' | 'RecalculatePricing' | 'ExportReport' | 'ReindexSearch'

interface KVPair {
  key: string
  value: string
}

const JOB_TYPE_OPTIONS = [
  { value: 'CleanupOldExecutions', label: 'Cleanup Old Executions' },
  { value: 'RecalculatePricing', label: 'Recalculate Pricing' },
  { value: 'ExportReport', label: 'Export Report' },
  { value: 'ReindexSearch', label: 'Reindex Search' },
]

const JOB_DESCRIPTIONS: Record<JobType, string> = {
  CleanupOldExecutions: 'Remove execuções antigas do banco de dados conforme política de retenção.',
  RecalculatePricing: 'Recalcula os custos históricos de token usage com os preços atuais.',
  ExportReport: 'Gera e exporta relatórios de uso e métricas em formato CSV/JSON.',
  ReindexSearch: 'Reconstrói os índices de busca para execuções e conversações.',
}

function KVEditor({ pairs, onChange }: { pairs: KVPair[]; onChange: (p: KVPair[]) => void }) {
  const add = () => onChange([...pairs, { key: '', value: '' }])
  const remove = (i: number) => onChange(pairs.filter((_, idx) => idx !== i))
  const update = (i: number, field: 'key' | 'value', val: string) => {
    const next = pairs.map((p, idx) => (idx === i ? { ...p, [field]: val } : p))
    onChange(next)
  }

  return (
    <div className="flex flex-col gap-2">
      {pairs.length === 0 && (
        <p className="text-xs text-text-dimmed">Nenhum metadado (opcional).</p>
      )}
      {pairs.map((p, i) => (
        <div key={i} className="flex items-center gap-2">
          <Input
            placeholder="chave"
            value={p.key}
            onChange={(e) => update(i, 'key', e.target.value)}
            className="flex-1"
          />
          <Input
            placeholder="valor"
            value={p.value}
            onChange={(e) => update(i, 'value', e.target.value)}
            className="flex-1"
          />
          <Button variant="danger" size="sm" onClick={() => remove(i)}>✕</Button>
        </div>
      ))}
      <Button variant="secondary" size="sm" onClick={add} className="self-start">+ Adicionar</Button>
    </div>
  )
}

export function NewJobPage() {
  const navigate = useNavigate()
  const [jobType, setJobType] = useState<JobType>('CleanupOldExecutions')
  const [metadata, setMetadata] = useState<KVPair[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const handleCreate = async () => {
    setLoading(true)
    setError(null)
    try {
      await new Promise((resolve) => setTimeout(resolve, 800))
      navigate('/background')
    } catch {
      setError('Erro ao criar job. Tente novamente.')
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="flex flex-col gap-6 p-6 max-w-2xl">
      <div className="flex items-center gap-4">
        <Button variant="ghost" size="sm" onClick={() => navigate('/background')}>← Voltar</Button>
        <div>
          <h1 className="text-2xl font-bold text-text-primary">Novo Background Job</h1>
          <p className="text-sm text-text-muted mt-1">Agende uma tarefa assíncrona</p>
        </div>
      </div>

      {error && <ErrorCard message={error} />}

      <Card title="Tipo de Job">
        <div className="flex flex-col gap-4">
          <Select
            label="Tipo *"
            value={jobType}
            onChange={(e) => setJobType(e.target.value as JobType)}
            options={JOB_TYPE_OPTIONS}
          />
          <div className="bg-bg-tertiary border border-border-primary rounded-lg p-3">
            <p className="text-xs text-text-muted">{JOB_DESCRIPTIONS[jobType]}</p>
          </div>
        </div>
      </Card>

      <Card title="Metadata (opcional)">
        <KVEditor pairs={metadata} onChange={setMetadata} />
      </Card>

      <div className="bg-amber-500/10 border border-amber-500/30 rounded-xl p-4">
        <p className="text-sm text-amber-300 font-medium">Atenção</p>
        <p className="text-xs text-amber-400/80 mt-1">
          Jobs de background são executados assincronamente. O progresso pode ser acompanhado
          na lista de jobs. Alguns jobs podem impactar a performance do sistema durante a execução.
        </p>
      </div>

      <div className="flex justify-end gap-3">
        <Button variant="ghost" onClick={() => navigate('/background')}>Cancelar</Button>
        <Button onClick={handleCreate} loading={loading}>Criar Job</Button>
      </div>
    </div>
  )
}
