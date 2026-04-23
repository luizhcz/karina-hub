import { useEffect, useMemo, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router'
import {
  previewPersonaTemplate,
  usePersonaTemplate,
  usePersonaTemplates,
  useUpsertPersonaTemplate,
  userTypeFromScope,
  type PersonaClientPreviewSample,
  type PersonaAdminPreviewSample,
} from '../../api/personaTemplates'
import type { UserType } from '../../api/personas'
import { ApiError } from '../../api/client'
import { Button } from '../../shared/ui/Button'
import { Card } from '../../shared/ui/Card'
import { Input } from '../../shared/ui/Input'
import { Textarea } from '../../shared/ui/Textarea'
import { Badge } from '../../shared/ui/Badge'
import { PageLoader } from '../../shared/ui/LoadingSpinner'
import { ErrorCard } from '../../shared/ui/ErrorCard'

// Amostras default plausíveis por userType.
const DEFAULT_CLIENT_SAMPLE: PersonaClientPreviewSample = {
  clientName: 'João Silva',
  suitabilityLevel: 'moderado',
  suitabilityDescription: 'Perfil moderado — aceita volatilidade controlada',
  businessSegment: 'private',
  country: 'BR',
  isOffshore: false,
}

const DEFAULT_ADMIN_SAMPLE: PersonaAdminPreviewSample = {
  username: 'ana.gestora',
  partnerType: 'GESTOR',
  segments: ['B2B', 'WM'],
  institutions: ['BTG'],
  isInternal: true,
  isWm: true,
  isMaster: false,
  isBroker: false,
}

export function PersonaTemplateEditPage() {
  const { id } = useParams<{ id?: string }>()
  const navigate = useNavigate()

  const isNew = id === undefined || id === 'new'
  const numericId = isNew ? undefined : Number(id)

  const existingQuery = usePersonaTemplate(numericId)
  const listQuery = usePersonaTemplates()
  const upsert = useUpsertPersonaTemplate()

  const [scope, setScope] = useState(isNew ? 'global:cliente' : '')
  const [name, setName] = useState('')
  const [template, setTemplate] = useState('')

  const [clientSample, setClientSample] = useState<PersonaClientPreviewSample>(DEFAULT_CLIENT_SAMPLE)
  const [adminSample, setAdminSample] = useState<PersonaAdminPreviewSample>(DEFAULT_ADMIN_SAMPLE)

  const [preview, setPreview] = useState<string | null>(null)
  const [previewError, setPreviewError] = useState<string | null>(null)
  const [previewing, setPreviewing] = useState(false)

  // UserType deduzido pelo scope — determina qual sample mostrar + quais
  // placeholders sugerir + qual tipo enviar no preview.
  const userType: UserType = userTypeFromScope(scope)

  // Hidrata o form quando o template existente carrega.
  useEffect(() => {
    if (existingQuery.data) {
      setScope(existingQuery.data.scope)
      setName(existingQuery.data.name)
      setTemplate(existingQuery.data.template)
    }
  }, [existingQuery.data])

  // Debounce 400ms. Envia o sample do userType do scope.
  useEffect(() => {
    if (!template.trim()) {
      setPreview(null)
      setPreviewError(null)
      return
    }
    const handle = setTimeout(async () => {
      setPreviewing(true)
      setPreviewError(null)
      try {
        const res = await previewPersonaTemplate({
          template,
          userType,
          client: userType === 'cliente' ? clientSample : undefined,
          admin: userType === 'admin' ? adminSample : undefined,
        })
        setPreview(res.rendered ?? '')
      } catch (err) {
        setPreviewError(err instanceof ApiError ? err.message : 'Falha no preview.')
        setPreview(null)
      } finally {
        setPreviewing(false)
      }
    }, 400)
    return () => clearTimeout(handle)
  }, [template, userType, clientSample, adminSample])

  const scopeConflict = useMemo(() => {
    const all = listQuery.data?.items ?? []
    return all.find((t) => t.scope === scope.trim() && t.id !== numericId)
  }, [listQuery.data, scope, numericId])

  if (!isNew && existingQuery.isLoading) return <PageLoader />
  if (!isNew && (existingQuery.error || !existingQuery.data)) {
    return <ErrorCard message="Template não encontrado." onRetry={existingQuery.refetch} />
  }

  const scopeTrimmed = scope.trim()
  const nameTrimmed = name.trim()
  const templateTrimmed = template.trim()

  const scopeInvalid =
    scopeTrimmed.length > 0 &&
    !scopeTrimmed.endsWith(':cliente') &&
    !scopeTrimmed.endsWith(':admin')

  const canSubmit =
    scopeTrimmed.length > 0 &&
    nameTrimmed.length > 0 &&
    templateTrimmed.length > 0 &&
    !scopeInvalid &&
    !upsert.isPending

  const handleSubmit = () => {
    upsert.mutate(
      { scope: scopeTrimmed, name: nameTrimmed, template },
      { onSuccess: () => navigate('/admin/persona-templates') },
    )
  }

  const placeholders =
    userType === 'cliente'
      ? listQuery.data?.placeholders.client ?? []
      : listQuery.data?.placeholders.admin ?? []

  return (
    <div className="flex flex-col gap-4 p-6">
      <div className="flex items-center gap-3">
        <Link to="/admin/persona-templates">
          <Button variant="ghost" size="sm">← Persona Templates</Button>
        </Link>
        <h1 className="text-2xl font-bold text-text-primary">
          {isNew ? 'Novo template' : nameTrimmed || 'Editar template'}
        </h1>
        {!isNew && existingQuery.data && (
          <span className="font-mono text-xs text-text-dimmed">#{existingQuery.data.id}</span>
        )}
        <Badge variant={userType === 'cliente' ? 'blue' : 'purple'}>
          userType: {userType}
        </Badge>
        {!isNew && existingQuery.data && (
          <div className="ml-auto">
            <Link to={`/admin/persona-templates/${existingQuery.data.id}/versions`}>
              <Button variant="secondary" size="sm">Histórico de versões</Button>
            </Link>
          </div>
        )}
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
        {/* Coluna esquerda — editor */}
        <div className="flex flex-col gap-4">
          <Card title="Identificação">
            <div className="flex flex-col gap-3">
              <Input
                label="Scope *"
                value={scope}
                onChange={(e) => setScope(e.target.value)}
                placeholder="global:cliente ou agent:atendimento-cliente:cliente"
                error={
                  scopeInvalid
                    ? 'Scope deve terminar em :cliente ou :admin.'
                    : scopeConflict
                      ? `Scope já cadastrado (template #${scopeConflict.id}: ${scopeConflict.name}).`
                      : undefined
                }
              />
              <p className="text-xs text-text-muted -mt-1">
                Formato: <Badge variant="blue">global:cliente</Badge> /{' '}
                <Badge variant="blue">global:admin</Badge> (default por tipo) ou{' '}
                <Badge variant="purple">agent:{'{id}'}:cliente</Badge> /{' '}
                <Badge variant="purple">agent:{'{id}'}:admin</Badge> (override).
                Upsert: se já existir, atualiza in-place.
              </p>

              <Input
                label="Nome *"
                value={name}
                onChange={(e) => setName(e.target.value)}
                placeholder="Global Cliente — investimentos"
              />
            </div>
          </Card>

          <Card
            title="Template"
            actions={
              placeholders.length > 0 ? (
                <div className="flex flex-wrap gap-1">
                  {placeholders.map((p) => (
                    <button
                      key={p}
                      type="button"
                      onClick={() => setTemplate((prev) => `${prev}{{${p}}}`)}
                      className="px-2 py-0.5 rounded bg-bg-tertiary border border-border-secondary text-[10px] font-mono text-text-secondary hover:border-accent-blue hover:text-text-primary transition-colors"
                      title={`Inserir {{${p}}} no final`}
                    >
                      {'{{'}{p}{'}}'}
                    </button>
                  ))}
                </div>
              ) : null
            }
          >
            <Textarea
              value={template}
              onChange={(e) => setTemplate(e.target.value)}
              placeholder={
                userType === 'cliente'
                  ? '## Persona\n- Segmento: {{business_segment}}\n- Suitability: {{suitability_level}}\n\n## Tone Policy\n...'
                  : '## Perfil do operador\n- Partner: {{partner_type}}\n- Interno: {{is_internal}}\n\n## Tone Policy\n...'
              }
              rows={20}
              className="font-mono text-xs"
            />
            <p className="text-xs text-text-muted mt-2">
              Apenas <code className="font-mono">{'{{placeholder}}'}</code> literal é substituído.
              Booleanos renderizam <code className="font-mono">sim/não</code>, listas como CSV
              <code className="font-mono">"A, B, C"</code>. Placeholders desconhecidos ficam
              literais no output pra expor typos.
            </p>
          </Card>
        </div>

        {/* Coluna direita — sample + preview */}
        <div className="flex flex-col gap-4">
          {userType === 'cliente' ? (
            <Card title="Amostra de cliente (preview)">
              <div className="grid grid-cols-2 gap-3">
                <Input
                  label="client_name"
                  value={clientSample.clientName ?? ''}
                  onChange={(e) => setClientSample({ ...clientSample, clientName: e.target.value || null })}
                />
                <Input
                  label="country"
                  value={clientSample.country ?? ''}
                  onChange={(e) => setClientSample({ ...clientSample, country: e.target.value || null })}
                  placeholder="BR / US / ..."
                />
                <Input
                  label="business_segment"
                  value={clientSample.businessSegment ?? ''}
                  onChange={(e) => setClientSample({ ...clientSample, businessSegment: e.target.value || null })}
                  placeholder="private / varejo / institucional / ..."
                />
                <Input
                  label="suitability_level"
                  value={clientSample.suitabilityLevel ?? ''}
                  onChange={(e) => setClientSample({ ...clientSample, suitabilityLevel: e.target.value || null })}
                  placeholder="conservador / moderado / agressivo"
                />
                <div className="col-span-2">
                  <Input
                    label="suitability_description"
                    value={clientSample.suitabilityDescription ?? ''}
                    onChange={(e) =>
                      setClientSample({ ...clientSample, suitabilityDescription: e.target.value || null })
                    }
                  />
                </div>
                <label className="flex items-center gap-2 text-sm text-text-primary col-span-2">
                  <input
                    type="checkbox"
                    checked={clientSample.isOffshore}
                    onChange={(e) => setClientSample({ ...clientSample, isOffshore: e.target.checked })}
                  />
                  is_offshore
                </label>
              </div>
            </Card>
          ) : (
            <Card title="Amostra de admin (preview)">
              <div className="grid grid-cols-2 gap-3">
                <Input
                  label="username"
                  value={adminSample.username ?? ''}
                  onChange={(e) => setAdminSample({ ...adminSample, username: e.target.value || null })}
                />
                <Input
                  label="partner_type"
                  value={adminSample.partnerType ?? ''}
                  onChange={(e) => setAdminSample({ ...adminSample, partnerType: e.target.value || null })}
                  placeholder="DEFAULT / CONSULTOR / GESTOR / ADVISORS"
                />
                <div className="col-span-2">
                  <Input
                    label="segments (CSV)"
                    value={(adminSample.segments ?? []).join(', ')}
                    onChange={(e) =>
                      setAdminSample({
                        ...adminSample,
                        segments: e.target.value
                          .split(',')
                          .map((s) => s.trim())
                          .filter((s) => s.length > 0),
                      })
                    }
                    placeholder="B2B, WM, IB"
                  />
                </div>
                <div className="col-span-2">
                  <Input
                    label="institutions (CSV)"
                    value={(adminSample.institutions ?? []).join(', ')}
                    onChange={(e) =>
                      setAdminSample({
                        ...adminSample,
                        institutions: e.target.value
                          .split(',')
                          .map((s) => s.trim())
                          .filter((s) => s.length > 0),
                      })
                    }
                    placeholder="BTG, EQI"
                  />
                </div>
                {(['isInternal', 'isWm', 'isMaster', 'isBroker'] as const).map((flag) => (
                  <label key={flag} className="flex items-center gap-2 text-sm text-text-primary">
                    <input
                      type="checkbox"
                      checked={adminSample[flag]}
                      onChange={(e) => setAdminSample({ ...adminSample, [flag]: e.target.checked })}
                    />
                    {flag}
                  </label>
                ))}
              </div>
            </Card>
          )}

          <Card
            title="Renderização"
            actions={
              <span className="text-xs text-text-muted">
                {previewing ? 'Renderizando…' : template.trim() ? 'ao vivo' : '—'}
              </span>
            }
          >
            {previewError ? (
              <div className="px-4 py-3 rounded-lg text-sm bg-red-500/10 border border-red-500/30 text-red-400">
                {previewError}
              </div>
            ) : preview === null ? (
              <p className="text-sm text-text-dimmed italic">
                Digite o template à esquerda pra ver o resultado.
              </p>
            ) : preview === '' ? (
              <p className="text-sm text-text-dimmed italic">
                Template vazio ou persona anônima — render retornou null (bloco seria omitido no system message).
              </p>
            ) : (
              <pre className="whitespace-pre-wrap font-mono text-xs text-text-primary bg-bg-tertiary rounded-lg p-3 border border-border-secondary max-h-[480px] overflow-auto">
                {preview}
              </pre>
            )}
          </Card>
        </div>
      </div>

      <div className="flex items-center justify-end gap-3 pt-2">
        <Link to="/admin/persona-templates">
          <Button variant="ghost">Cancelar</Button>
        </Link>
        <Button onClick={handleSubmit} disabled={!canSubmit} loading={upsert.isPending}>
          {isNew ? 'Criar template' : 'Salvar alterações'}
        </Button>
      </div>

      {upsert.isError && (
        <div className="px-4 py-3 rounded-lg text-sm bg-red-500/10 border border-red-500/30 text-red-400">
          {upsert.error instanceof ApiError ? upsert.error.message : 'Erro ao salvar template.'}
        </div>
      )}
    </div>
  )
}
