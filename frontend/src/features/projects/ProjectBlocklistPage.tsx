import { useEffect, useState } from 'react'
import { useNavigate, useParams } from 'react-router'
import {
  useBlocklistCatalog,
  useProjectBlocklist,
  useUpdateProjectBlocklist,
  type BlocklistSettings,
} from '../../api/blocklist'
import { Button } from '../../shared/ui/Button'
import { PageLoader } from '../../shared/ui/LoadingSpinner'
import { ErrorCard } from '../../shared/ui/ErrorCard'
import { BlocklistStatusCard } from './components/BlocklistStatusCard'
import { BlocklistGroupsCard } from './components/BlocklistGroupsCard'
import { BlocklistCustomPatternsCard } from './components/BlocklistCustomPatternsCard'
import { BlocklistViolationsCard } from './components/BlocklistViolationsCard'

export function ProjectBlocklistPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const projectId = id ?? ''

  const { data: catalog, isLoading: loadingCatalog } = useBlocklistCatalog()
  const { data: project, isLoading: loadingProject, error, refetch } = useProjectBlocklist(projectId)
  const updateMutation = useUpdateProjectBlocklist()

  const [settings, setSettings] = useState<BlocklistSettings | null>(null)
  const [formError, setFormError] = useState<string | null>(null)
  const [savedAt, setSavedAt] = useState<Date | null>(null)

  useEffect(() => {
    if (project) setSettings(project.settings)
  }, [project])

  if (loadingProject || loadingCatalog) return <PageLoader />
  if (error) return <ErrorCard message="Falha ao carregar config de blocklist" onRetry={refetch} />
  if (!settings) return <PageLoader />

  const handleSave = async () => {
    setFormError(null)
    try {
      await updateMutation.mutateAsync({ projectId, body: settings })
      setSavedAt(new Date())
    } catch {
      setFormError('Erro ao salvar. Tente novamente.')
    }
  }

  return (
    <div className="flex flex-col gap-6 p-6 max-w-5xl">
      <div className="flex items-center gap-4">
        <Button variant="ghost" size="sm" onClick={() => navigate(`/projects/${projectId}`)}>
          ← Voltar
        </Button>
        <div className="flex-1">
          <h1 className="text-2xl font-bold text-text-primary">Blocklist do Projeto</h1>
          <p className="text-sm text-text-muted mt-1 font-mono">{projectId}</p>
        </div>
        {savedAt && (
          <span className="text-xs text-text-muted">
            Salvo às {savedAt.toLocaleTimeString('pt-BR')}
          </span>
        )}
      </div>

      {formError && <ErrorCard message={formError} />}

      <BlocklistStatusCard settings={settings} onChange={setSettings} disabled={updateMutation.isPending} />

      <BlocklistGroupsCard
        catalog={catalog}
        settings={settings}
        onChange={setSettings}
        disabled={updateMutation.isPending || !settings.enabled}
      />

      <BlocklistCustomPatternsCard
        settings={settings}
        onChange={setSettings}
        disabled={updateMutation.isPending || !settings.enabled}
      />

      <BlocklistViolationsCard projectId={projectId} />

      <div className="flex justify-end gap-3 sticky bottom-4 bg-bg-primary/95 backdrop-blur p-3 rounded-lg border border-border-secondary">
        <Button variant="ghost" onClick={() => navigate(`/projects/${projectId}`)}>
          Cancelar
        </Button>
        <Button onClick={handleSave} loading={updateMutation.isPending}>
          Salvar Alterações
        </Button>
      </div>
    </div>
  )
}
