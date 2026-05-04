import { get } from './client'

export interface SystemInfo {
  publicBaseUrl: string
}

export const getSystemInfo = () => get<SystemInfo>('/system/info')
