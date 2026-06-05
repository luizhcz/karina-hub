import { ProfileStep } from './ProfileStep'
import type { FormState } from './types'

interface ConversationalProfileStepProps {
  form: FormState
  setForm: (mutator: (prev: FormState) => FormState) => void
  readonly: boolean
}

// Step de identificação do Conversational. Reusa o ProfileStep do Custom
// (mesma UX BlockNote com Papel/Objetivo/Contexto + Regras/Restrições).
// O `output_type` e a lista de `output_status` são configurados no step
// Output junto com o sub-schema do payload — partes do shape canônico
// `{ output_type, output_status, message, output }`.
export function ConversationalProfileStep({
  form,
  setForm,
  readonly,
}: ConversationalProfileStepProps) {
  return <ProfileStep form={form} setForm={setForm} readonly={readonly} />
}
