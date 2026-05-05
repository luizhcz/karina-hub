import { useState } from 'react'
import { Button, Card, CardHeader, SparklesIcon, cn } from '../../ui'
import { type AutoDeployPreset, presetMeta } from '../../api/profileEvaluation'

interface RerunEvalCardProps {
  defaultPreset: AutoDeployPreset
  running: boolean
  onRerun: (preset: AutoDeployPreset) => void
  deduplicatedHint: boolean
}

const PRESETS: AutoDeployPreset[] = ['basic', 'medium', 'advanced']

export function RerunEvalCard({ defaultPreset, running, onRerun, deduplicatedHint }: RerunEvalCardProps) {
  const [preset, setPreset] = useState<AutoDeployPreset>(defaultPreset)
  const meta = presetMeta(preset)

  return (
    <Card className="space-y-3" padded>
      <CardHeader
        title="Rodar nova avaliação"
        description="Dispara uma run on-demand sem precisar reimplantar o agente."
      />

      <div className="flex flex-wrap gap-1.5">
        {PRESETS.map((p) => {
          const m = presetMeta(p)
          const active = preset === p
          return (
            <button
              key={p}
              onClick={() => setPreset(p)}
              disabled={running}
              className={cn(
                'rounded-full border px-3 py-1 text-[11px] font-medium transition',
                active
                  ? 'border-accent bg-accent-subtle text-accent'
                  : 'border-border bg-surface text-fg-muted hover:bg-surface-hover hover:text-fg',
                running && 'cursor-not-allowed opacity-60',
              )}
            >
              {m.label} <span className="text-fg-dim">{m.cost}</span>
            </button>
          )
        })}
      </div>

      <p className="text-[11px] text-fg-dim">
        Latência estimada: <strong>{meta.duration}</strong> · custo: <strong>{meta.cost}</strong>.
        Backend deduplica chamadas iguais (mesmo preset + mesma versão do agente) por 30min — troque de preset
        ou aguarde pra forçar uma run nova.
      </p>

      {deduplicatedHint && (
        <div className="rounded-md border border-accent/30 bg-accent-subtle/40 px-3 py-2 text-xs text-accent">
          <p className="font-semibold">Resultado em cache</p>
          <p className="mt-1 text-fg-muted">
            Já existe uma run recente neste preset. Espere a janela de 30min expirar ou escolha um preset
            diferente pra forçar uma avaliação nova.
          </p>
        </div>
      )}

      <div className="flex justify-end">
        <Button
          size="sm"
          onClick={() => onRerun(preset)}
          loading={running}
          leftIcon={<SparklesIcon className="h-4 w-4" />}
        >
          {running ? 'Avaliando…' : 'Avaliar agora'}
        </Button>
      </div>
    </Card>
  )
}
