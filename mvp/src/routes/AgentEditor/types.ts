import type { KvRow } from '../../components/PostmanEditor/KvTable'
import type { AgentType } from '../../api/agentDrafts'

export const PROFILE_FIELDS = ['role', 'goal', 'backstory', 'rules', 'constraints'] as const
export type ProfileField = (typeof PROFILE_FIELDS)[number]

// Rules e constraints são listas (cada item vira um bullet no prompt final);
// os demais são texto livre.
export type ProfileTextField = 'role' | 'goal' | 'backstory'
export type ProfileListField = 'rules' | 'constraints'
export const PROFILE_LIST_FIELDS: ReadonlySet<ProfileField> = new Set<ProfileField>([
  'rules',
  'constraints',
])

export interface ProfileFields {
  role: string
  goal: string
  backstory: string
  rules: string[]
  constraints: string[]
}

export type AgentMode = 'basic' | 'advanced'

export type StepKey = 'type' | 'profile' | 'tools' | 'security' | 'memory' | 'input' | 'output' | 'model' | 'review'

export interface StructuredSection {
  mode: 'text' | 'structured'
  description: string
  // JSON Schema crua (string) — passada direto pro JsonSchemaBuilder.
  schema: string
}

/**
 * Estado do step "Memória" no wizard. <c>enabled</c> false = sem schema, sem
 * write/strip em runtime. Quando true, <c>schema</c> deve ser um JSON Schema
 * válido — validação acontece no save.
 */
export interface OperationalMemorySection {
  enabled: boolean
  // JSON Schema crua (string) — alimenta o JsonSchemaBuilder.
  schema: string
}

/**
 * Estado do step "Segurança". Toggle único: ativa/desativa o middleware
 * SecurityGuardrails server-side (texto fixo, não editável). Mapeia pra
 * uma entry em payload.middlewares[] no round-trip.
 */
export interface SecuritySection {
  enabled: boolean
}

export interface FormState {
  name: string
  /**
   * Tipo formal do agente. Custom (default) = sem template/validações por tipo;
   * Router = classifier (intent + enum) com hard validations e soft warnings.
   * Round-trip via payload.type — apenas selecionado no step "Tipo de agente".
   */
  type: AgentType
  /**
   * IDs das intents do pool global que este Router atende. Vazio quando
   * type !== 'Router'. Persistido via aihub.agent_router_intents (junction)
   * — fonte da verdade no DB. Edits no pool propagam pros Routers via
   * lookup runtime; só criar intent nova é manual.
   */
  routerIntentIds: string[]
  /**
   * Domínio de análise do Worker — texto livre PT-BR injetado em runtime
   * ao final das instructions (bloco "# Domínio de análise"). Persistido
   * em payload.metadata['x-worker-scope']. Vazio quando type !== 'Worker'.
   */
  workerScope: string
  predefinedModelId: string
  profile: ProfileFields
  toolIds: string[]
  mcpIds: string[]
  security: SecuritySection
  memory: OperationalMemorySection
  input: StructuredSection
  output: StructuredSection
  agentMode: AgentMode
  currentStep: StepKey
  // Metadata customizada do usuário fica fora do escopo da tela atual; é
  // preservada via spread no buildPayload e não aparece como campo editável
  // nesse wizard. Mantemos a forma aqui caso seja útil pra preview futura.
  metadataRows: KvRow<string>[]
}
