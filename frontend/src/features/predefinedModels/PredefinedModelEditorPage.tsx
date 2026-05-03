import { useEffect, useState } from 'react'
import { useNavigate, useParams } from 'react-router'
import {
  useAdminPreset,
  useCreatePreset,
  useUpdatePreset,
  type PredefinedModel,
} from '../../api/predefinedModels'
import { Card } from '../../shared/ui/Card'
import { Input } from '../../shared/ui/Input'
import { Textarea } from '../../shared/ui/Textarea'
import { Button } from '../../shared/ui/Button'
import { Select } from '../../shared/ui/Select'
import { ErrorCard } from '../../shared/ui/ErrorCard'
import { PageLoader } from '../../shared/ui/LoadingSpinner'
import { ApiError } from '../../api/client'

const PROVIDER_OPTIONS = [
  { value: 'AzureOpenAI', label: 'AzureOpenAI' },
  { value: 'OpenAI', label: 'OpenAI' },
  { value: 'AzureFoundry', label: 'AzureFoundry' },
]

interface FormState {
  id: string
  displayName: string
  description: string
  provider: string
  clientType: string
  endpoint: string
  deploymentName: string
  defaultTemperature: string
  defaultMaxTokens: string
  enabled: boolean
}

const emptyForm: FormState = {
  id: '',
  displayName: '',
  description: '',
  provider: 'AzureOpenAI',
  clientType: 'ChatCompletion',
  endpoint: '',
  deploymentName: '',
  defaultTemperature: '',
  defaultMaxTokens: '',
  enabled: true,
}

function fromPreset(p: PredefinedModel): FormState {
  return {
    id: p.id,
    displayName: p.displayName,
    description: p.description ?? '',
    provider: p.provider,
    clientType: p.clientType ?? '',
    endpoint: p.endpoint ?? '',
    deploymentName: p.deploymentName,
    defaultTemperature: p.defaultTemperature?.toString() ?? '',
    defaultMaxTokens: p.defaultMaxTokens?.toString() ?? '',
    enabled: p.enabled,
  }
}

export function PredefinedModelEditorPage() {
  const navigate = useNavigate()
  const { id } = useParams<{ id?: string }>()
  const isEdit = Boolean(id) && id !== 'new'
  const { data: existing, isLoading, error: loadError } = useAdminPreset(id ?? '', isEdit)

  const create = useCreatePreset()
  const update = useUpdatePreset()
  const [form, setForm] = useState<FormState>(emptyForm)
  const [submitError, setSubmitError] = useState<string | null>(null)

  useEffect(() => {
    if (existing) setForm(fromPreset(existing))
  }, [existing])

  const set = <K extends keyof FormState>(key: K, value: FormState[K]) => {
    setForm((prev) => ({ ...prev, [key]: value }))
  }

  const onSubmit = async () => {
    setSubmitError(null)

    if (!form.displayName.trim()) {
      setSubmitError('DisplayName é obrigatório.')
      return
    }
    if (!form.provider.trim()) {
      setSubmitError('Provider é obrigatório.')
      return
    }
    if (!form.deploymentName.trim()) {
      setSubmitError('DeploymentName é obrigatório.')
      return
    }
    if (!isEdit && !form.id.trim()) {
      setSubmitError('Id é obrigatório.')
      return
    }

    const temp = form.defaultTemperature.trim() ? Number(form.defaultTemperature) : null
    const maxTokens = form.defaultMaxTokens.trim() ? Number(form.defaultMaxTokens) : null

    if (temp !== null && (Number.isNaN(temp) || temp < 0 || temp > 2)) {
      setSubmitError('Temperature deve estar entre 0 e 2.')
      return
    }
    if (maxTokens !== null && (Number.isNaN(maxTokens) || maxTokens <= 0)) {
      setSubmitError('MaxTokens deve ser maior que zero.')
      return
    }

    const payload = {
      displayName: form.displayName.trim(),
      description: form.description,
      provider: form.provider.trim(),
      clientType: form.clientType.trim() || null,
      endpoint: form.endpoint.trim() || null,
      deploymentName: form.deploymentName.trim(),
      defaultTemperature: temp,
      defaultMaxTokens: maxTokens,
      enabled: form.enabled,
    }

    try {
      if (isEdit && existing) {
        await update.mutateAsync({
          id: existing.id,
          body: { ...payload, expectedUpdatedAt: existing.updatedAt },
        })
      } else {
        await create.mutateAsync({ ...payload, id: form.id.trim() })
      }
      navigate('/admin/predefined-models')
    } catch (err) {
      if (err instanceof ApiError) {
        setSubmitError(err.message)
      } else {
        setSubmitError('Erro ao salvar preset.')
      }
    }
  }

  if (isEdit && isLoading) return <PageLoader />
  if (isEdit && loadError) {
    return <ErrorCard message={loadError instanceof ApiError ? loadError.message : 'Erro ao carregar preset'} />
  }

  return (
    <div className="flex flex-col gap-6 p-6 max-w-3xl">
      <div className="flex items-center gap-4">
        <Button variant="ghost" size="sm" onClick={() => navigate('/admin/predefined-models')}>
          ← Voltar
        </Button>
        <div>
          <h1 className="text-2xl font-bold text-text-primary">
            {isEdit ? 'Editar preset' : 'Novo preset'}
          </h1>
          <p className="text-sm text-text-muted mt-1">
            Receita curada de provider+deployment+defaults disponível pra todos os PMs.
          </p>
        </div>
      </div>

      {submitError && <ErrorCard message={submitError} />}

      <Card title="Identificação">
        <div className="flex flex-col gap-4">
          {!isEdit && (
            <Input
              label="Id (slug imutável após salvar) *"
              value={form.id}
              onChange={(e) => set('id', e.target.value)}
              placeholder="smart-default"
            />
          )}
          <Input
            label="Nome amigável (visto pelo PM) *"
            value={form.displayName}
            onChange={(e) => set('displayName', e.target.value)}
            placeholder="Smart"
          />
          <Textarea
            label="Descrição"
            value={form.description}
            onChange={(e) => set('description', e.target.value)}
            placeholder="Modelo balanceado para uso geral. Bom em raciocínio, tool calling e respostas estruturadas."
          />
        </div>
      </Card>

      <Card title="Backend">
        <div className="flex flex-col gap-4">
          <Select
            label="Provider *"
            value={form.provider}
            onChange={(e) => set('provider', e.target.value)}
            options={PROVIDER_OPTIONS}
          />
          <Input
            label="ClientType"
            value={form.clientType}
            onChange={(e) => set('clientType', e.target.value)}
            placeholder="ChatCompletion"
          />
          <Input
            label="DeploymentName *"
            value={form.deploymentName}
            onChange={(e) => set('deploymentName', e.target.value)}
            placeholder="gpt-4o"
          />
          <Input
            label="Endpoint (override opcional)"
            value={form.endpoint}
            onChange={(e) => set('endpoint', e.target.value)}
            placeholder="https://meu-endpoint.openai.azure.com"
          />
        </div>
      </Card>

      <Card title="Defaults">
        <div className="flex flex-col gap-4">
          <Input
            label="Default Temperature (0..2)"
            type="number"
            step="0.1"
            min={0}
            max={2}
            value={form.defaultTemperature}
            onChange={(e) => set('defaultTemperature', e.target.value)}
            placeholder="0.3"
          />
          <Input
            label="Default MaxTokens"
            type="number"
            min={1}
            value={form.defaultMaxTokens}
            onChange={(e) => set('defaultMaxTokens', e.target.value)}
            placeholder="4096"
          />
          <p className="text-xs text-text-muted">
            Vazios significam "sem default" — o agent usa próprio Temperature/MaxTokens
            quando não há valor no preset.
          </p>
        </div>
      </Card>

      <Card title="Status">
        <label className="flex items-center gap-2 text-sm text-text-secondary cursor-pointer">
          <input
            type="checkbox"
            checked={form.enabled}
            onChange={(e) => set('enabled', e.target.checked)}
            className="accent-accent-blue"
          />
          Preset ativo (visível pra PMs no AgentForm). Inativos só aparecem nesta lista admin.
        </label>
      </Card>

      <div className="flex items-center justify-end gap-2">
        <Button variant="ghost" onClick={() => navigate('/admin/predefined-models')}>
          Cancelar
        </Button>
        <Button onClick={onSubmit} disabled={create.isPending || update.isPending}>
          {create.isPending || update.isPending ? 'Salvando…' : 'Salvar'}
        </Button>
      </div>
    </div>
  )
}
