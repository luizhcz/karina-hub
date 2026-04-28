import { post, del, get } from './client'
import { useMutation } from '@tanstack/react-query'

export interface SecretValidateRequest {
  reference: string
}

export interface SecretValidateResponse {
  exists: boolean
  lastChanged?: string | null
  tags?: Record<string, string> | null
  error?: string | null
}

export interface SecretHealthResponse {
  awsReachable: boolean
  error?: string | null
}

export const validateSecret = (reference: string) =>
  post<SecretValidateResponse>('/secrets/validate', { reference } as SecretValidateRequest)

export const invalidateSecretCache = (reference: string) =>
  del<void>(`/secrets/cache?reference=${encodeURIComponent(reference)}`)

export const getSecretsHealth = () => get<SecretHealthResponse>('/secrets/health')

export function useValidateSecret() {
  return useMutation({
    mutationFn: (reference: string) => validateSecret(reference)
  })
}

export function useInvalidateSecretCache() {
  return useMutation({
    mutationFn: (reference: string) => invalidateSecretCache(reference)
  })
}
