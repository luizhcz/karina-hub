import { useState, useEffect } from 'react'
import { useNavigate, useParams } from 'react-router'
import { useProject, useUpdateProject } from '../../api/projects'
import type {
  UpdateProjectRequest,
  ProjectLlmConfigInput,
  EvaluationProjectSettings,
} from '../../api/projects'
import { Card } from '../../shared/ui/Card'
import { Input } from '../../shared/ui/Input'
import { Textarea } from '../../shared/ui/Textarea'
import { Button } from '../../shared/ui/Button'
import { PageLoader } from '../../shared/ui/LoadingSpinner'
import { ErrorCard } from '../../shared/ui/ErrorCard'
import { LlmCredentialsSection } from './LlmCredentialsSection'
import { EvaluationSettingsSection } from './EvaluationSettingsSection'

export function ProjectEditPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const { data: project, isLoading, error, refetch } = useProject(id ?? '', !!id)
  const updateProject = useUpdateProject()

  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [llmConfig, setLlmConfig] = useState<ProjectLlmConfigInput | undefined>()
  const [evaluation, setEvaluation] = useState<EvaluationProjectSettings | undefined>()
  const [formError, setFormError] = useState<string | null>(null)

  useEffect(() => {
    if (project) {
      setName(project.name)
      setDescription(project.description ?? '')
    }
  }, [project])

  if (isLoading) return <PageLoader />
  if (error) return <ErrorCard message="Projeto não encontrado" onRetry={refetch} />

  const handleSubmit = async () => {
    if (!name.trim()) { setFormError('Nome é obrigatório'); return }

    // PUT /projects substitui settings inteiro — preservamos os campos
    // existentes (HitlEnabled, MaxSandboxTokensPerDay, etc.) e sobrescrevemos
    // só evaluation. Sem isso, salvar Foundry resetaria tudo pro default.
    const settings = evaluation !== undefined || project?.settings
      ? { ...(project?.settings ?? {}), evaluation: evaluation ?? project?.settings?.evaluation ?? null }
      : undefined

    const body: UpdateProjectRequest = {
      name,
      description: description || undefined,
      settings,
      llmConfig,
    }

    try {
      await updateProject.mutateAsync({ id: id!, body })
      navigate('/projects')
    } catch {
      setFormError('Erro ao atualizar projeto.')
    }
  }

  return (
    <div className="flex flex-col gap-6 p-6 max-w-2xl">
      <div className="flex items-center gap-4">
        <Button variant="ghost" size="sm" onClick={() => navigate('/projects')}>← Voltar</Button>
        <div>
          <h1 className="text-2xl font-bold text-text-primary">Editar Projeto</h1>
          <p className="text-sm text-text-muted mt-1 font-mono">{id?.slice(0, 24)}</p>
        </div>
      </div>

      {formError && <ErrorCard message={formError} />}

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

      <LlmCredentialsSection existing={project?.llmConfig} onChange={setLlmConfig} />

      <EvaluationSettingsSection existing={project?.settings?.evaluation} onChange={setEvaluation} />

      <Card title="Blocklist (Guardrail)">
        <div className="flex items-center justify-between gap-4">
          <div className="flex-1">
            <p className="text-sm text-text-secondary">
              Bloqueia conteúdo proibido (PII, secrets, padrões customizados) em input/output dos agentes.
            </p>
            <p className="text-xs text-text-muted mt-1">
              Ligar/desligar grupos curados, sobrescrever ações, adicionar patterns específicos do projeto.
            </p>
          </div>
          <Button variant="ghost" onClick={() => navigate(`/projects/${id}/blocklist`)}>
            Configurar →
          </Button>
        </div>
      </Card>

      <div className="flex justify-end gap-3">
        <Button variant="ghost" onClick={() => navigate('/projects')}>Cancelar</Button>
        <Button onClick={handleSubmit} loading={updateProject.isPending}>Salvar Alterações</Button>
      </div>
    </div>
  )
}
