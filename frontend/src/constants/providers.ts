// ── LLM Provider constants ───────────────────────────────────────────────────
// Temporary frontend constants until GET /api/functions exposes providers (A1)

export const PROVIDER_TYPES = ['OPENAI', 'AZUREOPENAI', 'AZUREFOUNDRY'] as const

export type ProviderType = (typeof PROVIDER_TYPES)[number]

export const PROVIDER_OPTIONS: { value: string; label: string }[] = [
  { value: '', label: 'Selecione...' },
  { value: 'AzureFoundry', label: 'Azure Foundry' },
  { value: 'AzureOpenAI', label: 'Azure OpenAI' },
  { value: 'OpenAI', label: 'OpenAI' },
]

/** Maps model catalog provider key → agent definition provider value */
export const CATALOG_TO_PROVIDER: Record<string, string> = {
  OPENAI: 'OpenAI',
  AZUREOPENAI: 'AzureOpenAI',
  AZUREFOUNDRY: 'AzureFoundry',
}
