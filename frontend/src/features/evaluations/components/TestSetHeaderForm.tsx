import { Card } from '../../../shared/ui/Card'
import { Input } from '../../../shared/ui/Input'

export interface TestSetHeaderValues {
  name: string
  description: string
  visibility: 'project' | 'global'
}

interface TestSetHeaderFormProps {
  values: TestSetHeaderValues
  onChange: (next: TestSetHeaderValues) => void
}

/**
 * Form de header (name/description/visibility) usado no modo CREATE do editor
 * unificado. Visibility=global é desabilitado — promoção exige permissão admin
 * separada (não modelada nesta versão).
 */
export function TestSetHeaderForm({ values, onChange }: TestSetHeaderFormProps) {
  return (
    <Card>
      <div className="flex flex-col gap-4">
        <Input
          label="Nome *"
          value={values.name}
          onChange={(e) => onChange({ ...values, name: e.target.value })}
          placeholder="Ex.: Cobertura básica de saudações"
          required
        />
        <Input
          label="Descrição (opcional)"
          value={values.description}
          onChange={(e) => onChange({ ...values, description: e.target.value })}
          placeholder="O que esse test set valida"
        />
        <div>
          <label className="text-sm font-medium text-text-primary mb-2 block">
            Visibilidade
          </label>
          <div className="flex gap-3 text-sm">
            <label className="flex items-center gap-2 cursor-pointer">
              <input
                type="radio"
                checked={values.visibility === 'project'}
                onChange={() => onChange({ ...values, visibility: 'project' })}
              />
              <span className="text-text-primary">Projeto</span>
              <span className="text-text-muted text-xs">— visível só neste projeto</span>
            </label>
            <label className="flex items-center gap-2 cursor-not-allowed opacity-60" title="Promoção a global requer permissão admin (não disponível nesta versão)">
              <input
                type="radio"
                checked={values.visibility === 'global'}
                disabled
                onChange={() => undefined}
              />
              <span className="text-text-primary">Global</span>
              <span className="text-text-muted text-xs">— requer admin (em breve)</span>
            </label>
          </div>
        </div>
      </div>
    </Card>
  )
}
