import { get } from './client'

// Catálogo de presets — endpoint público (já liberado no AdminGate). Backend
// filtra Enabled=true server-side. Espelha PredefinedModelResponse do backend
// pra que a tela monte cards detalhados (description/provider/temperature/etc).
export interface PredefinedModel {
  id: string
  displayName: string
  description: string
  provider: string
  clientType?: string | null
  endpoint?: string | null
  deploymentName: string
  defaultTemperature?: number | null
  defaultMaxTokens?: number | null
  enabled: boolean
  createdAt: string
  updatedAt: string
}

export const listPredefinedModels = () =>
  get<PredefinedModel[]>('/predefined-models')
