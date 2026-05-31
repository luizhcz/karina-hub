import type { KvRow } from '../../components/PostmanEditor/KvTable'
import type { AgentType } from '../../api/agentDrafts'

export type AgentMode = 'basic' | 'advanced'

export type StepKey = 'profile' | 'tools' | 'security' | 'memory' | 'input' | 'output' | 'model' | 'review'

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
   * Flag declarativa: o Router é usado em chat (workflow `InputMode=Chat`).
   * Quando true, o codec ativa o middleware `StructuredOutputState` no
   * save — ele lê o output JSON do classify e dispara STATE_DELTA via
   * SSE pro frontend chat. Persistido em
   * payload.metadata['x-router-for-chat']. Vazio/false quando o Router é
   * standalone (pipeline batch, default).
   */
  routerForChat: boolean
  /**
   * Override opcional do `authorInstructions` do Router. Vazio = codec gera
   * o esqueleto determinístico via `encodeRouterInstructions(name)`. Não
   * vazio = o texto vai integral pro payload e o esqueleto é ignorado.
   * Round-trip preserva o texto exato (sem decodificação markdown), porque
   * Router não tem perfil/output estruturado pra extrair. Vazio quando
   * type !== 'Router'.
   */
  routerAuthorInstructions: string
  /**
   * Domínio de análise do Worker — texto livre PT-BR injetado em runtime
   * ao final das instructions (bloco "# Domínio de análise"). Persistido
   * em payload.metadata['x-worker-scope']. Vazio quando type !== 'Worker'.
   */
  workerScope: string
  /**
   * Flag declarativa pra Tool Runner: o agente exige aprovação humana
   * antes de invocar tools com side-effect. Persistido em
   * payload.metadata['x-tool-runner-hitl-required']. Save valida
   * consistência (warning quando há tool com requiresApproval=true e a
   * flag está off); runtime de chamada de tool ainda não enforça.
   */
  toolRunnerHitlRequired: boolean
  /**
   * Família de renderer do Conversational — string única que vira enum
   * de 1 elemento em `output_type` no schema canônico. Persistido em
   * payload.metadata['x-conversational-output-type']. Default "text"
   * pra basic mode; advanced pode customizar.
   */
  conversationalOutputType: string
  /**
   * Lista canônica de variações de status do output_type. Persistida como
   * JSON array em payload.metadata['x-conversational-output-statuses']. O
   * codec injeta como enum em `output_status`; frontend chat usa pra escolher
   * a variação dentro da família output_type.
   * Vazio quando type !== 'Conversational'.
   *
   * Persona / papel / objetivo / contexto vivem em <c>profile</c> como
   * markdown integral — o codec apenas anexa blocos auto-gerados
   * (tools/structured/formato) sem decompor o profile.
   */
  conversationalOutputStatuses: string[]
  predefinedModelId: string
  /**
   * Perfil do agente em markdown raw. O usuário escreve livremente no
   * BlockNote WYSIWYG do ProfileStep; o codec preserva a string como
   * está e apenas anexa blocos auto-gerados no save. Vazio = template
   * inicial será exibido pelo ProfileStep ao montar.
   */
  profile: string
  toolIds: string[]
  /**
   * Nomes das function tools nativas (C#) atribuídas ao agente. Round-trip
   * via payload.tools[] entries com type='function'. Read-only no MVP: o
   * catálogo vem de GET /functions e o usuário só marca/desmarca.
   */
  functionToolNames: string[]
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
