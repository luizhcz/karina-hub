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
