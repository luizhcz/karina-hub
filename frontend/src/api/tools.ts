import { get } from './client'
import { useQuery } from '@tanstack/react-query'


export interface FunctionToolInfo {
  name: string
  description?: string
  jsonSchema?: object
  fingerprint?: string
}

export interface CodeExecutorInfo {
  name: string
  inputType?: string
  outputType?: string
  /** JSON Schema do tipo de input gerado via System.Text.Json.Schema. Presente para executors registrados via Register&lt;TIn,TOut&gt;. */
  inputSchema?: JsonSchemaObject
  /** JSON Schema do tipo de output. Predicate de Conditional/Switch resolve Path contra este schema. */
  outputSchema?: JsonSchemaObject
  /** Hash sha256 curto do output schema — invalida cache de definição quando o produtor muda schema. */
  outputSchemaVersion?: string
}

/**
 * JSON Schema mínimo (subset suportado pelo predicate editor).
 * Usado só como hint de UX — backend é autoritativo.
 */
export interface JsonSchemaObject {
  type?: string | string[]
  properties?: Record<string, JsonSchemaObject>
  items?: JsonSchemaObject | JsonSchemaObject[]
  enum?: unknown[]
  required?: string[]
  oneOf?: JsonSchemaObject[]
  anyOf?: JsonSchemaObject[]
  allOf?: JsonSchemaObject[]
  description?: string
  // permite campos extras sem forçar typecheck
  [key: string]: unknown
}

export interface MiddlewareSettingOption {
  value: string
  label: string
}

export interface MiddlewareSettingInfo {
  key: string
  label: string
  type: 'select' | 'text'
  options?: MiddlewareSettingOption[]
  defaultValue: string
}

export interface MiddlewareTypeInfo {
  name: string
  phase?: 'Pre' | 'Post' | 'Both'
  label?: string
  description?: string
  settings?: MiddlewareSettingInfo[]
}

export interface LlmProviderInfo {
  type: string
}

export interface AvailableFunctions {
  functionTools: FunctionToolInfo[]
  codeExecutors: CodeExecutorInfo[]
  middlewareTypes: MiddlewareTypeInfo[]
  availableProviders: LlmProviderInfo[]
}


export const KEYS = {
  functions: ['functions'] as const,
}


export const getFunctions = () => get<AvailableFunctions>('/functions')


export function useFunctions() {
  return useQuery({ queryKey: KEYS.functions, queryFn: getFunctions })
}
