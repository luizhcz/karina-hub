import { Badge, Card, cn } from '../../ui'
import type { AutoDeployPreset } from '../../api/profileEvaluation'

interface PresetSelectorProps {
  value: AutoDeployPreset
  onChange: (preset: AutoDeployPreset) => void
  disabled?: boolean
}

interface PresetCard {
  key: AutoDeployPreset
  title: string
  description: string
  cases: number
  metricsChips: string[]
  duration: string
  cost: string
  /** Hint sobre quando o preset é/não é apropriado (mostrado em cor sutil sob a descrição). */
  fitHint?: string
  /** Banner amarelo no card (ex.: aviso de custo). */
  warning?: string
}

const PRESETS: PresetCard[] = [
  {
    key: 'basic',
    title: 'Básica',
    description: 'Heurísticas locais sem custo. Ideal pra smoke tests rápidos.',
    fitHint: 'Faz sentido pra agentes com tools ou output literal previsível. Em agente de chat puro, prefira Média.',
    cases: 5,
    metricsChips: ['ContainsExpected', 'ToolCalledCheck'],
    duration: '<30s',
    cost: '$0',
  },
  {
    key: 'medium',
    title: 'Média',
    description: 'Quality via LLM-as-judge. Avaliação semântica — paráfrase passa.',
    fitHint: 'Recomendada pra qualquer agente. Cobre chat livre, agentes com tools e structured output.',
    cases: 5,
    metricsChips: ['Relevance', 'Coherence', 'ToolCallAccuracy', 'TaskAdherence'],
    duration: '~1min',
    cost: '~$0.10',
  },
  {
    key: 'advanced',
    title: 'Avançada',
    description: '15 cases sintéticos × 8 métricas MEAI. Cobertura máxima sem Foundry.',
    fitHint: 'Pra agentes cliente-facing ou de operação sensível, antes de promoção a produção.',
    cases: 15,
    metricsChips: ['+ Fluency', '+ Completeness', '+ Equivalence', '+ IntentResolution'],
    duration: '~3min',
    cost: '~$0.50',
    warning: 'Custo mais alto — confirme antes',
  },
]

export function PresetSelector({ value, onChange, disabled }: PresetSelectorProps) {
  return (
    <div className="grid gap-3 sm:grid-cols-3">
      {PRESETS.map((p) => {
        const active = value === p.key
        return (
          <Card
            key={p.key}
            interactive={!disabled}
            onClick={() => !disabled && onChange(p.key)}
            className={cn(
              'cursor-pointer space-y-2',
              disabled && 'cursor-not-allowed opacity-60',
              active && 'border-accent bg-accent-subtle/40 ring-2 ring-accent/30',
            )}
          >
            <div className="flex items-start justify-between gap-2">
              <div>
                <h3 className="text-sm font-semibold text-fg">{p.title}</h3>
                {p.key === 'basic' && (
                  <Badge tone="neutral" className="mt-0.5 text-[10px]">default</Badge>
                )}
              </div>
              <div className="text-right">
                <div className="text-sm font-semibold text-fg">{p.cost}</div>
                <div className="text-[10px] text-fg-dim">{p.duration}</div>
              </div>
            </div>
            <p className="text-xs leading-relaxed text-fg-muted">{p.description}</p>
            {p.fitHint && (
              <p className="rounded-md bg-bg-soft px-2 py-1 text-[10px] leading-snug text-fg-dim">
                {p.fitHint}
              </p>
            )}
            <div className="flex flex-wrap gap-1">
              <Badge tone="accent" className="text-[10px]">{p.cases} cases</Badge>
              {p.metricsChips.map((chip) => (
                <Badge key={chip} tone="neutral" className="text-[10px]">{chip}</Badge>
              ))}
            </div>
            {p.warning && (
              <p className="rounded-md bg-warning/10 px-2 py-1 text-[10px] text-warning">{p.warning}</p>
            )}
          </Card>
        )
      })}
    </div>
  )
}
