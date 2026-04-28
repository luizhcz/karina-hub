import { useEffect, useState } from 'react'
import { Card } from '../../shared/ui/Card'
import { Input } from '../../shared/ui/Input'
import { SecretReferenceInput } from '../../shared/ui/SecretReferenceInput'
import type { EvaluationProjectSettings, FoundryEvaluationSettings } from '../../api/projects'

interface Props {
  existing?: EvaluationProjectSettings | null
  onChange: (settings: EvaluationProjectSettings | undefined) => void
}

const AWS_PREFIX = 'secret://aws/'

export function EvaluationSettingsSection({ existing, onChange }: Props) {
  const initial: FoundryEvaluationSettings = existing?.foundry ?? {}

  // Backend retorna a referência AWS verbatim. Literais legacy não voltam mais
  // (response devolve null), então o input começa vazio e o operador é forçado
  // a recadastrar com uma referência válida.
  const initialRef = initial.apiKeyRef ?? ''
  const isAwsRef = initialRef.startsWith(AWS_PREFIX)

  const [enabled, setEnabled] = useState(initial.enabled ?? false)
  const [endpoint, setEndpoint] = useState(initial.endpoint ?? '')
  const [modelDeployment, setModelDeployment] = useState(initial.modelDeployment ?? '')
  const [apiKey, setApiKey] = useState(isAwsRef ? initialRef : '')
  const [projectEndpoint, setProjectEndpoint] = useState(initial.projectEndpoint ?? '')

  useEffect(() => {
    const trimmedEndpoint = endpoint.trim()
    const trimmedDeployment = modelDeployment.trim()
    const trimmedKey = apiKey.trim()
    const trimmedProjectEndpoint = projectEndpoint.trim()

    const noUserInput =
      !enabled
      && !trimmedEndpoint
      && !trimmedDeployment
      && !trimmedKey
      && !trimmedProjectEndpoint
      && !isAwsRef

    if (noUserInput) {
      onChange(undefined)
      return
    }

    onChange({
      foundry: {
        enabled,
        endpoint: trimmedEndpoint || null,
        modelDeployment: trimmedDeployment || null,
        // Vazio = preservar referência existente (backend faz o merge).
        apiKeyRef: trimmedKey || null,
        projectEndpoint: trimmedProjectEndpoint || null,
      },
    })
  }, [enabled, endpoint, modelDeployment, apiKey, projectEndpoint, isAwsRef, onChange])

  return (
    <Card title="Avaliação (Foundry-as-Judge)">
      <div className="text-xs text-text-muted mb-3">
        Configura o deployment Azure AI Foundry usado como judge LLM em runs de evaluation.
        Necessário apenas para evaluator bindings com <code>kind=foundry</code>.
        Bindings <code>kind=meai</code> e <code>kind=local</code> não dependem desta config.
      </div>

      <label className="flex items-center gap-2 mb-4 cursor-pointer">
        <input
          type="checkbox"
          checked={enabled}
          onChange={(e) => setEnabled(e.target.checked)}
          className="h-4 w-4 accent-blue-500"
        />
        <span className="text-sm text-text-primary">Habilitar Foundry para este projeto</span>
      </label>

      <div className="flex flex-col gap-4">
        <Input
          label="Endpoint"
          value={endpoint}
          onChange={(e) => setEndpoint(e.target.value)}
          placeholder="https://efs-ai-hub-foundry-tenant-x.cognitiveservices.azure.com"
        />
        <Input
          label="Model deployment"
          value={modelDeployment}
          onChange={(e) => setModelDeployment(e.target.value)}
          placeholder="gpt-4o-eval"
        />
        <SecretReferenceInput
          label="API key (AWS Secrets Manager reference)"
          value={apiKey}
          onChange={setApiKey}
        />
        <div>
          <Input
            label="Project endpoint (Safety evaluators — opcional)"
            value={projectEndpoint}
            onChange={(e) => setProjectEndpoint(e.target.value)}
            placeholder="https://my-resource.services.ai.azure.com/api/projects/my-project"
          />
          <p className="text-xs text-text-muted mt-1">
            Necessário só para evaluators <code>Violence</code>, <code>Sexual</code>, <code>SelfHarm</code>, <code>HateAndUnfairness</code>.
            Sem isso, esses bindings são pulados com warning. Auth via Service Principal
            (configurado em <code>Azure:ServicePrincipal:*</code> no backend, resolvido do AWS Secrets Manager).
          </p>
        </div>
      </div>

      {enabled && (!endpoint.trim() || !modelDeployment.trim() || (!apiKey.trim() && !isAwsRef)) && (
        <div className="mt-3 rounded-md border border-amber-500/30 bg-amber-500/10 px-3 py-2 text-xs text-amber-300">
          Habilitado mas faltam campos obrigatórios — runs com binding <code>kind=foundry</code> Quality retornarão 400
          até endpoint, deployment e api key estarem preenchidos.
        </div>
      )}
    </Card>
  )
}
