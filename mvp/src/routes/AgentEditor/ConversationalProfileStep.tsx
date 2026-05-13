import { ProfileStep } from './ProfileStep'
import type { FormState } from './types'

interface ConversationalProfileStepProps {
  form: FormState
  setForm: (mutator: (prev: FormState) => FormState) => void
  readonly: boolean
}

// Step de identificação do Conversational. Reusa o ProfileStep do Custom
// (mesma UX BlockNote com Papel/Objetivo/Contexto + Regras/Restrições).
// O componente único de UI (`ui_component`) é configurado no step Output
// junto com o sub-schema do payload — ambos são partes do shape canônico
// `{ ui_component, message, output }`.
export function ConversationalProfileStep({
  form,
  setForm,
  readonly,
}: ConversationalProfileStepProps) {
  return <ProfileStep form={form} setForm={setForm} readonly={readonly} />
}
