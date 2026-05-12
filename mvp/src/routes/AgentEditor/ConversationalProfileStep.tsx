import { ProfileStep } from './ProfileStep'
import type { FormState } from './types'

interface ConversationalProfileStepProps {
  form: FormState
  setForm: (mutator: (prev: FormState) => FormState) => void
  readonly: boolean
}

// Step de identificação do Conversational. Reusa o ProfileStep do Custom
// (mesma UX BlockNote com Papel/Objetivo/Contexto + Regras/Restrições).
// O componente único de UI saiu daqui pra uma etapa dedicada
// (`ConversationalComponentStep`).
export function ConversationalProfileStep({
  form,
  setForm,
  readonly,
}: ConversationalProfileStepProps) {
  return <ProfileStep form={form} setForm={setForm} readonly={readonly} />
}
