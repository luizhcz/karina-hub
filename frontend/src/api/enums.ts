import { get } from './client'
import { useQuery } from '@tanstack/react-query'


export interface EnumsResponse {
  orchestrationModes: string[]
  edgeTypes: string[]
  triggerTypes: string[]
  executionStatuses: string[]
  hitlStatuses: string[]
  middlewarePhases: string[]
}


export const KEYS = {
  enums: ['enums'] as const,
}


export const getEnums = () => get<EnumsResponse>('/enums')


export function useEnums() {
  return useQuery({
    queryKey: KEYS.enums,
    queryFn: getEnums,
    staleTime: Infinity,
  })
}
