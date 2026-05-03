import {
  Card,
  CardHeader,
  CheckIcon,
  ErrorMessage,
  SparklesIcon,
  Spinner,
  cn,
} from '../../ui'
import type { PredefinedModel } from '../../api/predefinedModels'
import type { FormState } from './types'

interface ModelStepProps {
  form: FormState
  setForm: (mutator: (prev: FormState) => FormState) => void
  models: PredefinedModel[]
  modelsLoading: boolean
  modelsError: string | null
  readonly: boolean
}

export function ModelStep({
  form,
  setForm,
  models,
  modelsLoading,
  modelsError,
  readonly,
}: ModelStepProps) {
  const setModel = (id: string) => setForm((prev) => ({ ...prev, predefinedModelId: id }))

  return (
    <Card className="space-y-4">
      <CardHeader
        title="Modelo do agente"
        description="Cada preset traz provedor, deployment e parâmetros padrão de geração já configurados pela equipe da plataforma. Escolha o que melhor descreve o uso do agente."
      />

      {modelsLoading && (
        <div className="flex items-center justify-center py-10">
          <Spinner className="h-6 w-6 text-fg-muted" />
        </div>
      )}

      {!modelsLoading && modelsError && <ErrorMessage message={modelsError} />}

      {!modelsLoading && !modelsError && models.length === 0 && (
        <div className="rounded-lg border border-dashed border-border px-4 py-8 text-center text-sm text-fg-muted">
          Nenhum modelo disponível. Contate o admin para liberar presets no projeto.
        </div>
      )}

      {!modelsLoading && !modelsError && models.length > 0 && (
        <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
          {models.map((model) => (
            <ModelCard
              key={model.id}
              model={model}
              selected={form.predefinedModelId === model.id}
              onSelect={() => !readonly && setModel(model.id)}
              disabled={readonly}
            />
          ))}
        </div>
      )}
    </Card>
  )
}

interface ModelCardProps {
  model: PredefinedModel
  selected: boolean
  onSelect: () => void
  disabled: boolean
}

function ModelCard({ model, selected, onSelect, disabled }: ModelCardProps) {
  return (
    <button
      type="button"
      onClick={onSelect}
      disabled={disabled}
      className={cn(
        'group relative flex flex-col items-stretch gap-3 overflow-hidden rounded-xl border p-5 text-left transition focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent/40',
        selected
          ? 'border-accent bg-accent-subtle/50 shadow-soft'
          : 'border-border bg-surface hover:border-accent/40 hover:shadow-soft',
        disabled && 'cursor-not-allowed opacity-60',
      )}
    >
      {selected && (
        <div className="absolute right-4 top-4 flex h-7 w-7 items-center justify-center rounded-full bg-accent text-accent-contrast shadow-soft">
          <CheckIcon className="h-4 w-4" />
        </div>
      )}

      <div className="flex items-center gap-3 pr-9">
        <div
          className={cn(
            'flex h-11 w-11 shrink-0 items-center justify-center rounded-xl transition',
            selected
              ? 'bg-accent text-accent-contrast'
              : 'bg-accent-subtle text-accent group-hover:scale-105',
          )}
        >
          <SparklesIcon className="h-5 w-5" />
        </div>
        <div className="min-w-0">
          <h3 className="truncate text-base font-semibold text-fg">{model.displayName}</h3>
          <p className="mt-0.5 font-mono text-[10px] uppercase tracking-wider text-fg-dim">
            {model.id}
          </p>
        </div>
      </div>

      {model.description && (
        <p className="text-sm text-fg-muted">{model.description}</p>
      )}

      <dl className="mt-auto grid grid-cols-2 gap-x-3 gap-y-2 border-t border-border pt-3 text-xs">
        <MetaRow label="Provider" value={model.provider} />
        <MetaRow label="Deployment" value={model.deploymentName} mono />
        {model.defaultTemperature != null && (
          <MetaRow label="Temperature" value={model.defaultTemperature.toFixed(1)} mono />
        )}
        {model.defaultMaxTokens != null && (
          <MetaRow label="Max tokens" value={model.defaultMaxTokens.toLocaleString('pt-BR')} mono />
        )}
        {model.clientType && (
          <MetaRow label="Client" value={model.clientType} />
        )}
      </dl>
    </button>
  )
}

interface MetaRowProps {
  label: string
  value: string
  mono?: boolean
}

function MetaRow({ label, value, mono }: MetaRowProps) {
  return (
    <div className="flex flex-col gap-0.5 leading-tight">
      <dt className="text-[10px] uppercase tracking-wider text-fg-dim">{label}</dt>
      <dd className={cn('text-fg', mono && 'font-mono text-[12px]')}>{value}</dd>
    </div>
  )
}
