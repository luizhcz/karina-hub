import { useState } from 'react'
import { useNavigate } from 'react-router'
import { useUpdateSkill } from '../../api/skills'
import type { UpdateSkillRequest } from '../../api/skills'
import { Card } from '../../shared/ui/Card'
import { Input } from '../../shared/ui/Input'
import { Textarea } from '../../shared/ui/Textarea'
import { Button } from '../../shared/ui/Button'
import { ErrorCard } from '../../shared/ui/ErrorCard'

interface KVPair {
  key: string
  value: string
}

function KVEditor({ pairs, onChange }: { pairs: KVPair[]; onChange: (p: KVPair[]) => void }) {
  const add = () => onChange([...pairs, { key: '', value: '' }])
  const remove = (i: number) => onChange(pairs.filter((_, idx) => idx !== i))
  const update = (i: number, field: 'key' | 'value', val: string) => {
    onChange(pairs.map((p, idx) => (idx === i ? { ...p, [field]: val } : p)))
  }

  return (
    <div className="flex flex-col gap-2">
      {pairs.map((p, i) => (
        <div key={i} className="flex items-center gap-2">
          <Input placeholder="chave" value={p.key} onChange={(e) => update(i, 'key', e.target.value)} className="flex-1" />
          <Input placeholder="valor" value={p.value} onChange={(e) => update(i, 'value', e.target.value)} className="flex-1" />
          <Button variant="danger" size="sm" onClick={() => remove(i)}>✕</Button>
        </div>
      ))}
      <Button variant="secondary" size="sm" onClick={add} className="self-start">+ Adicionar</Button>
    </div>
  )
}

export function SkillCreatePage() {
  const navigate = useNavigate()
  const createSkill = useUpdateSkill()

  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [type, setType] = useState('')
  const [config, setConfig] = useState<KVPair[]>([])
  const [error, setError] = useState<string | null>(null)

  const handleSubmit = async () => {
    if (!name.trim()) { setError('Nome é obrigatório'); return }
    if (!type.trim()) { setError('Tipo é obrigatório'); return }

    const configObj = config.reduce<Record<string, string>>((acc, { key, value }) => {
      if (key.trim()) acc[key.trim()] = value
      return acc
    }, {})

    const body: UpdateSkillRequest = {
      name,
      description: description || undefined,
      type,
      configuration: Object.keys(configObj).length > 0 ? configObj : undefined,
    }

    try {
      await createSkill.mutateAsync({ id: crypto.randomUUID(), body })
      navigate('/skills')
    } catch {
      setError('Erro ao criar skill. Verifique os dados e tente novamente.')
    }
  }

  return (
    <div className="flex flex-col gap-6 p-6 max-w-2xl">
      <div className="flex items-center gap-4">
        <Button variant="ghost" size="sm" onClick={() => navigate('/skills')}>← Voltar</Button>
        <div>
          <h1 className="text-2xl font-bold text-text-primary">Nova Skill</h1>
          <p className="text-sm text-text-muted mt-1">Crie uma nova skill para os agentes</p>
        </div>
      </div>

      {error && <ErrorCard message={error} />}

      <Card title="Identidade">
        <div className="flex flex-col gap-4">
          <Input
            label="Nome *"
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="minha-skill"
          />
          <Input
            label="Tipo *"
            value={type}
            onChange={(e) => setType(e.target.value)}
            placeholder="ex: WebSearch, CodeExecution"
          />
          <Textarea
            label="Descrição"
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            placeholder="Descreva o propósito desta skill..."
            rows={3}
          />
        </div>
      </Card>

      <Card title="Configuração (chave-valor)">
        <KVEditor pairs={config} onChange={setConfig} />
      </Card>

      <div className="flex justify-end gap-3">
        <Button variant="ghost" onClick={() => navigate('/skills')}>Cancelar</Button>
        <Button onClick={handleSubmit} loading={createSkill.isPending}>Criar Skill</Button>
      </div>
    </div>
  )
}
