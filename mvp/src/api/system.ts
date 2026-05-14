import { get } from './client'

export interface ConsumeHeader {
  key: string
  value: string
}

export interface SystemInfo {
  publicBaseUrl: string
  consumeHeaders: ConsumeHeader[]
}

export const getSystemInfo = () => get<SystemInfo>('/system/info')
