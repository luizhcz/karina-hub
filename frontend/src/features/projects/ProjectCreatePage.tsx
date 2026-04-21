import { useState } from 'react'
import { useNavigate } from 'react-router'
import { useCreateProject } from '../../api/projects'
import type { CreateProjectRequest, ProjectLlmConfigInput } from '../../api/projects'
import { Card } from '../../shared/ui/Card'
import { Input } from '../../shared/ui/Input'
import { Textarea } from '../../shared/ui/Textarea'
import { Button } from '../../shared/ui/Button'
import { ErrorCard } from '../../shared/ui/ErrorCard'
import { LlmCredentialsSection } from './LlmCredentialsSection'

export function ProjectCreatePage() {
  const navigate = useNavigate()
  const createProject = useCreateProject()
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [llmConfig, setLlmConfig] = useState<ProjectLlmConfigInput | undefined>()
  const [error, setError] = useState<string | null>(null)

  const handleSubmit = async () => {
    if (!name.trim()) { setError('Nome é obrigatório'); return }

    const body: CreateProjectRequest = {
      name,
      description: description || undefined,
      llmConfig,
    }

    try {
      await createProject.mutateAsync(body)
      navigate('/projects')
    } catch {
      setError('Erro ao criar projeto. Verifique os dados e tente novamente.')
    }
  }

  return (
    <div className="flex flex-col gap-6 p-6 max-w-2xl">
      <div className="flex items-center gap-4">
        <Button variant="ghost" size="sm" onClick={() => navigate('/projects')}>← Voltar</Button>
        <div>
          <h1 className="text-2xl font-bold text-text-primary">Novo Projeto</h1>
          <p className="text-sm text-text-muted mt-1">Crie um novo projeto para organizar seus recursos</p>
        </div>
      </div>

      {error && <ErrorCard message={error} />}

      <Card title="Informações">
        <div className="flex flex-col gap-4">
          <Input label="Nome *" value={name} onChange={(e) => setName(e.target.value)} placeholder="Meu Projeto" />
          <Textarea
            label="Descrição"
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            placeholder="Descreva o propósito deste projeto..."
            rows={3}
          />
        </div>
      </Card>

      <LlmCredentialsSection onChange={setLlmConfig} />

      <div className="flex justify-end gap-3">
        <Button variant="ghost" onClick={() => navigate('/projects')}>Cancelar</Button>
        <Button onClick={handleSubmit} loading={createProject.isPending}>Criar Projeto</Button>
      </div>
    </div>
  )
}
