import { useState } from 'react'
import { Card } from '../../shared/ui/Card'
import { Input } from '../../shared/ui/Input'
import type { ProjectLlmConfigInput, ProjectLlmConfigResponse } from '../../api/projects'
import { useModelCatalog } from '../../api/modelCatalog'

const PROVIDERS = ['OPENAI', 'AZUREOPENAI', 'AZUREFOUNDRY'] as const
type Provider = typeof PROVIDERS[number]

interface ProviderState {
  apiKey: string
  endpoint: string
}

interface Props {
  existing?: ProjectLlmConfigResponse
  onChange: (config: ProjectLlmConfigInput | undefined) => void
}

export function LlmCredentialsSection({ existing, onChange }: Props) {
  const { data: models = [] } = useModelCatalog()
  const [open, setOpen] = useState(false)
  const [defaultProvider, setDefaultProvider] = useState(existing?.defaultProvider ?? '')
  const [defaultModel, setDefaultModel] = useState(existing?.defaultModel ?? '')
  const [credentials, setCredentials] = useState<Record<Provider, ProviderState>>(() => {
    const init: Record<string, ProviderState> = {}
    for (const p of PROVIDERS) init[p] = { apiKey: '', endpoint: existing?.credentials?.[p]?.endpoint ?? '' }
    return init as Record<Provider, ProviderState>
  })

  const availableModels = models.filter(
    (m) => !defaultProvider || m.provider === defaultProvider
  )

  const emit = (
    creds: Record<Provider, ProviderState>,
    dp: string,
    dm: string
  ) => {
    const credMap: ProjectLlmConfigInput['credentials'] = {}
    for (const p of PROVIDERS) {
      if (creds[p].apiKey || creds[p].endpoint) {
        credMap[p] = {
          apiKey: creds[p].apiKey || undefined,
          endpoint: creds[p].endpoint || undefined,
        }
      }
    }
    onChange(
      Object.keys(credMap).length > 0 || dp || dm
        ? { credentials: credMap, defaultProvider: dp || undefined, defaultModel: dm || undefined }
        : undefined
    )
  }

  const updateCred = (provider: Provider, field: 'apiKey' | 'endpoint', value: string) => {
    const next = { ...credentials, [provider]: { ...credentials[provider], [field]: value } }
    setCredentials(next)
    emit(next, defaultProvider, defaultModel)
  }

  const updateDefault = (dp: string, dm: string) => {
    setDefaultProvider(dp)
    setDefaultModel(dm)
    emit(credentials, dp, dm)
  }

  return (
    <Card title="Configuração LLM">
      <div className="flex flex-col gap-4">
        <div className="flex gap-3">
          <div className="flex flex-col gap-1 flex-1 min-w-0">
            <label className="text-xs text-text-muted">Provider padrão</label>
            <select
              value={defaultProvider}
              onChange={(e) => updateDefault(e.target.value, defaultModel)}
              className="w-full bg-bg-tertiary border border-border-secondary rounded-md px-2.5 py-1.5 text-sm text-text-primary focus:outline-none focus:border-accent-blue"
            >
              <option value="">Usar global (appsettings)</option>
              {PROVIDERS.map((p) => <option key={p} value={p}>{p}</option>)}
            </select>
          </div>
          <div className="flex flex-col gap-1 flex-1 min-w-0">
            <label className="text-xs text-text-muted">Modelo padrão</label>
            <select
              value={defaultModel}
              onChange={(e) => updateDefault(defaultProvider, e.target.value)}
              className="w-full bg-bg-tertiary border border-border-secondary rounded-md px-2.5 py-1.5 text-sm text-text-primary focus:outline-none focus:border-accent-blue"
            >
              <option value="">Usar global (appsettings)</option>
              {availableModels.map((m) => (
                <option key={`${m.provider}/${m.id}`} value={m.id}>{m.displayName}</option>
              ))}
            </select>
          </div>
        </div>

        <button
          type="button"
          onClick={() => setOpen(!open)}
          className="flex items-center gap-2 text-xs text-text-muted hover:text-text-primary transition-colors self-start"
        >
          <span className={`transition-transform ${open ? 'rotate-90' : ''}`}>▶</span>
          Credenciais por provider
          {PROVIDERS.some((p) => existing?.credentials?.[p]?.apiKeySet) && (
            <span className="ml-1 px-1.5 py-0.5 bg-accent-blue/20 text-accent-blue rounded text-[10px]">configurado</span>
          )}
        </button>

        {open && (
          <div className="flex flex-col gap-5 pl-3 border-l border-border-primary">
            {PROVIDERS.map((provider) => {
              const existingCred = existing?.credentials?.[provider]
              return (
                <div key={provider} className="flex flex-col gap-2">
                  <div className="flex items-center gap-2">
                    <span className="text-xs font-semibold text-text-secondary">{provider}</span>
                    {existingCred?.apiKeySet && (
                      <span className="text-[10px] px-1.5 py-0.5 bg-green-500/20 text-green-400 rounded">API Key configurada</span>
                    )}
                  </div>
                  <div className="flex gap-3">
                    <div className="flex-1">
                      <Input
                        label="API Key"
                        type="password"
                        value={credentials[provider].apiKey}
                        onChange={(e) => updateCred(provider, 'apiKey', e.target.value)}
                        placeholder={existingCred?.apiKeySet ? '••••••••  (deixe vazio para manter)' : 'sk-proj-...'}
                        autoComplete="new-password"
                      />
                    </div>
                    <div className="flex-1">
                      <Input
                        label="Endpoint (opcional)"
                        value={credentials[provider].endpoint}
                        onChange={(e) => updateCred(provider, 'endpoint', e.target.value)}
                        placeholder="https://..."
                      />
                    </div>
                  </div>
                </div>
              )
            })}
          </div>
        )}
      </div>
    </Card>
  )
}
