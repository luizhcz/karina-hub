import type { KvRow } from '../../components/PostmanEditor/KvTable'

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

export type StepKey = 'profile' | 'tools' | 'memory' | 'input' | 'output' | 'model' | 'review'

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
  // null = usa default do backend (8 KB).
  maxBytes: number | null
}

export interface FormState {
  name: string
  predefinedModelId: string
  profile: ProfileFields
  toolIds: string[]
  mcpIds: string[]
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
