import { useState } from 'react'
import { Card } from '../../shared/ui/Card'
import { Input } from '../../shared/ui/Input'
import { SecretReferenceInput } from '../../shared/ui/SecretReferenceInput'
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
    for (const p of PROVIDERS) {
      init[p] = {
        // Pre-popula com a referência AWS quando já existe; legacy DPAPI fica vazio
        // (tratado via banner no SecretReferenceInput).
        apiKey: existing?.credentials?.[p]?.secretRef ?? '',
        endpoint: existing?.credentials?.[p]?.endpoint ?? '',
      }
    }
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

  const anyConfigured = PROVIDERS.some((p) => existing?.credentials?.[p]?.apiKeySet)
  const legacyProviders = PROVIDERS.filter((p) => existing?.credentials?.[p]?.legacyDpapi)
  const anyLegacy = legacyProviders.length > 0

  return (
    <Card title="Configuração LLM">
      {anyLegacy && (
        <div className="mb-4 rounded-md border border-amber-500/40 bg-amber-500/10 px-4 py-3">
          <div className="flex items-start gap-3">
            <span className="text-amber-400 text-lg leading-none">⚠</span>
            <div className="flex-1">
              <p className="text-sm font-semibold text-amber-300">
                Credenciais em formato legacy (DPAPI)
              </p>
              <p className="text-xs text-amber-300/80 mt-1">
                {legacyProviders.length === 1 ? 'O provider' : 'Os providers'}{' '}
                <span className="font-mono">{legacyProviders.join(', ')}</span>{' '}
                {legacyProviders.length === 1 ? 'ainda usa' : 'ainda usam'} criptografia local.
                Recadastre apontando uma referência <code>secret://aws/...</code> no AWS Secrets
                Manager para finalizar a migração.
              </p>
            </div>
          </div>
        </div>
      )}
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
          {anyConfigured && (
            <span className="ml-1 px-1.5 py-0.5 bg-accent-blue/20 text-accent-blue rounded text-[10px]">configurado</span>
          )}
          {anyLegacy && (
            <span className="ml-1 px-1.5 py-0.5 bg-amber-500/20 text-amber-400 rounded text-[10px]">legacy DPAPI</span>
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
                    {existingCred?.apiKeySet && !existingCred.legacyDpapi && (
                      <span className="text-[10px] px-1.5 py-0.5 bg-green-500/20 text-green-400 rounded">AWS Secrets Manager</span>
                    )}
                    {existingCred?.legacyDpapi && (
                      <span className="text-[10px] px-1.5 py-0.5 bg-amber-500/20 text-amber-400 rounded">legacy DPAPI</span>
                    )}
                  </div>
                  <SecretReferenceInput
                    label="API Key reference"
                    value={credentials[provider].apiKey}
                    onChange={(v) => updateCred(provider, 'apiKey', v)}
                  />
                  <Input
                    label="Endpoint (opcional)"
                    value={credentials[provider].endpoint}
                    onChange={(e) => updateCred(provider, 'endpoint', e.target.value)}
                    placeholder="https://..."
                  />
                </div>
              )
            })}
          </div>
        )}
      </div>
    </Card>
  )
}
