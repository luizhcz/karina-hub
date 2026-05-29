import { get } from './client'

// Tools nativas registradas em C# no backend (IFunctionToolRegistry). Diferente
// das GenericTool (HTTP configurável via DB), estas são código embarcado e
// portanto read-only no MVP — o usuário só escolhe quais usar no agente.
export interface FunctionToolInfo {
  name: string
  description?: string
  jsonSchema?: object
  fingerprint?: string
}

interface FunctionsResponse {
  functionTools: FunctionToolInfo[]
}

export const listFunctionTools = async (): Promise<FunctionToolInfo[]> => {
  const res = await get<FunctionsResponse>('/functions')
  return res?.functionTools ?? []
}
