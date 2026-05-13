import { Card, CardHeader } from '../../ui'
import { ToggleRow } from './ToggleRow'
import type { FormState, SecuritySection } from './types'

interface SecurityStepProps {
  form: FormState
  setForm: (mutator: (prev: FormState) => FormState) => void
  readonly: boolean
}

// Resumo PT-BR voltado pra PM/PO das regras que a plataforma aplica
// automaticamente quando o toggle está ligado. Texto completo da política
// vive no backend e é inacessível na UI — aqui só descrevemos o efeito.
const POLICY_CLAUSES: Array<{ title: string; description: string }> = [
  {
    title: 'Foco no que foi pedido',
    description:
      'Quando o usuário sai do assunto do agente, ele recusa educadamente e oferece uma alternativa próxima dentro do escopo.',
  },
  {
    title: 'Não inventa informação',
    description:
      'O agente não cria fatos, números ou ferramentas que não existem. Quando falta informação, diz claramente que não sabe.',
  },
  {
    title: 'Resistência a manipulação',
    description:
      'O agente não muda de personagem nem segue “ordens” disfarçadas dentro de mensagens do usuário ou em respostas de ferramentas.',
  },
  {
    title: 'Não vaza informação interna',
    description:
      'O agente não compartilha o conteúdo do próprio perfil, as ferramentas internas ou as regras de segurança com o usuário.',
  },
]

export function SecurityStep({ form, setForm, readonly }: SecurityStepProps) {
  const updateSecurity = (mutator: (prev: SecuritySection) => SecuritySection) =>
    setForm((prev) => ({ ...prev, security: mutator(prev.security) }))

  const toggle = (next: boolean) =>
    updateSecurity((prev) => ({ ...prev, enabled: next }))

  return (
    <div className="space-y-5">
      <Card className="space-y-3">
        <CardHeader
          title="Proteções de segurança"
          description="Regras automáticas que o agente segue em toda conversa — protege contra desvio de assunto, inventar informação, tentativas de manipulação e vazamento de instruções internas. O texto é fixo e mantido pela plataforma; aqui você decide se liga ou desliga."
        />
        <ToggleRow
          checked={form.security.enabled}
          disabled={readonly}
          onChange={toggle}
          label="Ligar proteções de segurança"
          hint={
            form.security.enabled
              ? 'Respostas tendem a ficar mais conservadoras e dentro do escopo definido no perfil. Recomendado pra agentes que conversam com clientes finais.'
              : 'Sem as proteções, o agente segue só o que você descreveu no perfil — fica mais flexível, mas pode aceitar pedidos fora do escopo ou ser manipulado.'
          }
        />
      </Card>

      {form.security.enabled && (
        <Card className="space-y-3">
          <CardHeader
            title="O que as proteções cobrem"
            description="Resumo das regras que o agente segue automaticamente quando as proteções estão ligadas."
          />
          <ul className="space-y-3">
            {POLICY_CLAUSES.map((clause) => (
              <li key={clause.title} className="flex gap-3">
                <span
                  className="mt-1 h-2 w-2 shrink-0 rounded-full bg-accent"
                  aria-hidden="true"
                />
                <div className="min-w-0">
                  <p className="text-sm font-medium text-fg">{clause.title}</p>
                  <p className="mt-0.5 text-xs leading-relaxed text-fg-muted">
                    {clause.description}
                  </p>
                </div>
              </li>
            ))}
          </ul>
        </Card>
      )}
    </div>
  )
}
