import { useEffect, useRef, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router'
import {
  useTestSet,
  useTestSetVersionCases,
  useCreateTestSet,
  usePublishTestSetVersion,
  useImportTestSetCsv,
  useUpdateTestSetVersionStatus,
} from '../../api/evaluations'
import type { TestSetVersion } from '../../api/evaluations'
import { useProjectStore } from '../../stores/project'
import { Button } from '../../shared/ui/Button'
import { Card } from '../../shared/ui/Card'
import { Badge } from '../../shared/ui/Badge'
import { Input } from '../../shared/ui/Input'
import { Tabs } from '../../shared/ui/Tabs'
import { PageLoader } from '../../shared/ui/LoadingSpinner'
import { ErrorCard } from '../../shared/ui/ErrorCard'
import { TestSetHeaderForm, type TestSetHeaderValues } from './components/TestSetHeaderForm'
import { GenerateCasesModal } from './components/GenerateCasesModal'
import type { GeneratedCase } from '../../api/testCaseGenerator'

interface CaseDraft {
  input: string
  expectedOutput: string
  tags: string
  weight: string
}

const EMPTY_CASE: CaseDraft = { input: '', expectedOutput: '', tags: '', weight: '1.0' }

export function TestSetEditorPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const projectId = useProjectStore((s) => s.projectId) ?? 'default'
  // Modo CREATE quando rota é /evaluations/test-sets/new — esse path bate na
  // rota literal sem param `:id`, então useParams().id é undefined. Tratamos
  // tanto undefined quanto "new" (caso o React Router resolva a literal pra
  // /:id="new" em outra config) pra cobrir os dois cenários.
  const isCreateMode = !id || id === 'new'

  const { data, isLoading, error, refetch } = useTestSet(id!, !isCreateMode && !!id)
  const createMutation = useCreateTestSet()
  const publishMutation = usePublishTestSetVersion()
  const importMutation = useImportTestSetCsv()
  const updateStatusMutation = useUpdateTestSetVersionStatus()

  const [activeTab, setActiveTab] = useState<string>('inline')
  const [drafts, setDrafts] = useState<CaseDraft[]>([{ ...EMPTY_CASE }])
  const [changeReason, setChangeReason] = useState('')
  const [csvFile, setCsvFile] = useState<File | null>(null)
  const fileInputRef = useRef<HTMLInputElement>(null)
  const [versionToView, setVersionToView] = useState<string | null>(null)
  const [headerValues, setHeaderValues] = useState<TestSetHeaderValues>({
    name: '',
    description: '',
    visibility: 'project',
  })
  const [createError, setCreateError] = useState<string | null>(null)
  const [generatorOpen, setGeneratorOpen] = useState(false)

  const hasFilledCases = drafts.some((d) => d.input.trim())

  const handleGeneratedCases = (cases: GeneratedCase[], replace: boolean) => {
    const newDrafts: CaseDraft[] = cases.map((c) => ({
      input: c.input,
      expectedOutput: c.expectedOutput ?? '',
      tags: (c.tags ?? []).join(', '),
      weight: String(c.weight ?? 1.0),
    }))
    if (newDrafts.length === 0) return
    if (replace || !hasFilledCases) {
      setDrafts(newDrafts)
    } else {
      // Mantém os que já tinham input + adiciona os gerados.
      const existing = drafts.filter((d) => d.input.trim())
      setDrafts([...existing, ...newDrafts])
    }
  }

  useEffect(() => {
    if (isCreateMode) return
    if (!versionToView && data?.versions?.[0]) {
      setVersionToView(data.versions[0].testSetVersionId)
    }
  }, [data, versionToView, isCreateMode])

  const versionCasesQuery = useTestSetVersionCases(
    versionToView ?? '',
    !isCreateMode && !!versionToView,
  )

  if (!isCreateMode && isLoading) return <PageLoader />
  if (!isCreateMode && (error || !data)) {
    return <ErrorCard message="Erro ao carregar test set." onRetry={refetch} />
  }

  const testSet = isCreateMode ? null : data!.testSet
  const versions = isCreateMode ? [] : data!.versions

  const showTrivialWarning = drafts.length > 0 && drafts.length < 10
  const showNoExpectedWarning =
    drafts.length >= 5 &&
    drafts.filter((d) => !d.expectedOutput.trim()).length / drafts.length > 0.5

  const buildCases = () => drafts
    .filter((d) => d.input.trim())
    .map((d) => ({
      input: d.input.trim(),
      expectedOutput: d.expectedOutput.trim() || undefined,
      tags: d.tags.split(',').map((t) => t.trim()).filter(Boolean),
      weight: Number(d.weight) || 1.0,
    }))

  const handlePublishInline = async () => {
    const cases = buildCases()
    if (cases.length === 0) return
    await publishMutation.mutateAsync({
      id: testSet!.id,
      body: { cases, changeReason: changeReason.trim() || undefined },
    })
    setDrafts([{ ...EMPTY_CASE }])
    setChangeReason('')
  }

  // Handler unificado do modo CREATE: cria TestSet + publica primeira version
  // em sequência. Se publish falhar mid-flow, TestSet fica órfão sem versions —
  // user pode retry o publish reabrindo a página de detalhe (já navegada).
  const handleCreateUnified = async () => {
    setCreateError(null)
    if (!headerValues.name.trim()) {
      setCreateError('Nome é obrigatório.')
      return
    }
    const cases = buildCases()
    if (cases.length === 0) {
      setCreateError('Adicione pelo menos um case antes de salvar.')
      return
    }
    try {
      const created = await createMutation.mutateAsync({
        projectId,
        body: {
          name: headerValues.name.trim(),
          description: headerValues.description.trim() || undefined,
          visibility: headerValues.visibility,
        },
      })
      try {
        await publishMutation.mutateAsync({
          id: created.id,
          body: { cases, changeReason: changeReason.trim() || undefined },
        })
      } catch (publishErr) {
        // TestSet criado mas publish falhou — navega pro detalhe pra user retry
        // (cases ficam no state local do detalhe). Mostra toast leve.
        navigate(`/evaluations/test-sets/${created.id}`, { replace: true })
        setCreateError(
          publishErr instanceof Error
            ? `Test set criado, mas falhou ao salvar cases: ${publishErr.message}. Tente salvar de novo.`
            : 'Test set criado, mas falhou ao salvar cases. Tente salvar de novo.',
        )
        return
      }
      navigate(`/evaluations/test-sets/${created.id}`, { replace: true })
    } catch (err) {
      setCreateError(
        err instanceof Error ? err.message : 'Erro ao criar test set. Tente novamente.',
      )
    }
  }

  const CSV_MAX_BYTES = 5 * 1024 * 1024 // 5MB hard limit
  const csvTooLarge = csvFile && csvFile.size > CSV_MAX_BYTES

  const handlePublishCsv = async () => {
    if (!csvFile || csvTooLarge) return
    await importMutation.mutateAsync({
      id: testSet!.id,
      file: csvFile,
      changeReason: changeReason.trim() || undefined,
    })
    setCsvFile(null)
    if (fileInputRef.current) fileInputRef.current.value = ''
    setChangeReason('')
  }

  // No modo CREATE só faz sentido a tab Cases — Importar e Histórico exigem
  // TestSet já persistido e versão. Esconde delas até o save.
  const tabItems = isCreateMode
    ? [{ key: 'inline', label: 'Cases' }]
    : [
        { key: 'inline', label: 'Cases' },
        { key: 'csv', label: 'Importar' },
        { key: 'versions', label: 'Histórico', badge: versions.length },
      ]

  return (
    <div className="flex flex-col gap-4">
      <div className="flex items-center gap-3">
        <Link to="/evaluations/test-sets">
          <Button variant="ghost" size="sm">&larr; Test Sets</Button>
        </Link>
        <h1 className="text-2xl font-bold text-text-primary truncate flex-1">
          {isCreateMode ? 'Novo Test Set' : testSet!.name}
        </h1>
        {!isCreateMode && (
          <Badge variant={testSet!.visibility === 'global' ? 'purple' : 'gray'}>{testSet!.visibility}</Badge>
        )}
      </div>
      {!isCreateMode && testSet!.description && (
        <div className="text-sm text-text-muted">{testSet!.description}</div>
      )}

      {isCreateMode && (
        <TestSetHeaderForm values={headerValues} onChange={setHeaderValues} />
      )}

      <Tabs items={tabItems} active={activeTab} onChange={setActiveTab} />

      {activeTab === 'inline' && (
        <Card>
          {showTrivialWarning && (
            <div className="mb-3 rounded-md border border-amber-500/30 bg-amber-500/10 px-3 py-2 text-xs text-amber-300">
              ⚠ Test sets com menos de 10 cases podem produzir falsa sensação de segurança.
              Considere adicionar mais cobertura antes de usar como regression baseline.
            </div>
          )}
          {showNoExpectedWarning && (
            <div className="mb-3 rounded-md border border-amber-500/30 bg-amber-500/10 px-3 py-2 text-xs text-amber-300">
              ⚠ Mais de 50% dos cases sem ExpectedOutput. Evaluators como ContainsExpected/Equivalence
              não funcionam sem ele.
            </div>
          )}

          <div className="space-y-3">
            {drafts.map((d, idx) => (
              <div key={idx} className="border border-border-secondary rounded-lg p-3 space-y-2">
                <div className="flex items-center justify-between">
                  <span className="text-xs font-mono text-text-muted">Case #{idx + 1}</span>
                  <button
                    type="button"
                    onClick={() => setDrafts(drafts.filter((_, i) => i !== idx))}
                    className="text-xs text-red-400 hover:underline"
                  >
                    Remover
                  </button>
                </div>
                <textarea
                  value={d.input}
                  onChange={(e) => {
                    const next = [...drafts]
                    next[idx] = { ...d, input: e.target.value }
                    setDrafts(next)
                  }}
                  placeholder="Input (pergunta para o agente)"
                  rows={2}
                  className="w-full px-3 py-2 text-sm rounded-md bg-bg-secondary border border-border-secondary text-text-primary"
                />
                <textarea
                  value={d.expectedOutput}
                  onChange={(e) => {
                    const next = [...drafts]
                    next[idx] = { ...d, expectedOutput: e.target.value }
                    setDrafts(next)
                  }}
                  placeholder="Resposta esperada (opcional — usado por ContainsExpected e Equivalence; deixe em branco para Local skip)"
                  rows={2}
                  className="w-full px-3 py-2 text-sm rounded-md bg-bg-secondary border border-border-secondary text-text-primary"
                />
                <div className="grid grid-cols-2 gap-2">
                  <Input
                    label="Tags (separadas por vírgula)"
                    value={d.tags}
                    onChange={(e) => {
                      const next = [...drafts]
                      next[idx] = { ...d, tags: e.target.value }
                      setDrafts(next)
                    }}
                    placeholder="weather,easy"
                  />
                  <Input
                    label="Peso"
                    title="Peso do caso na média (1.0 padrão; >1 = caso mais importante)"
                    type="number"
                    step="0.1"
                    value={d.weight}
                    onChange={(e) => {
                      const next = [...drafts]
                      next[idx] = { ...d, weight: e.target.value }
                      setDrafts(next)
                    }}
                  />
                </div>
              </div>
            ))}
            <div className="flex flex-wrap gap-2">
              <Button variant="secondary" onClick={() => setDrafts([...drafts, { ...EMPTY_CASE }])}>
                + Adicionar caso
              </Button>
              <Button variant="secondary" onClick={() => setGeneratorOpen(true)}>
                ✨ Gerar com IA
              </Button>
            </div>
          </div>

          <div className="mt-4 space-y-2">
            <Input
              label="Change reason (opcional)"
              value={changeReason}
              onChange={(e) => setChangeReason(e.target.value)}
              placeholder="Ex.: Cobertura inicial das saudações"
            />
            <Button
              variant="primary"
              onClick={isCreateMode ? handleCreateUnified : handlePublishInline}
              loading={isCreateMode
                ? createMutation.isPending || publishMutation.isPending
                : publishMutation.isPending}
              disabled={drafts.every((d) => !d.input.trim())}
            >
              {isCreateMode ? 'Salvar test set' : 'Salvar cases'}
            </Button>
            {createError && (
              <div className="text-sm text-red-400">{createError}</div>
            )}
            {!isCreateMode && publishMutation.error && (
              <div className="text-sm text-red-400">{(publishMutation.error as Error).message}</div>
            )}
          </div>
        </Card>
      )}

      {activeTab === 'csv' && (
        <Card>
          <div className="space-y-3">
            <div className="text-sm text-text-secondary">
              Formato CSV (header obrigatório, ordem livre, case-insensitive):
              <code className="block mt-1 px-2 py-1 bg-bg-tertiary rounded font-mono text-xs">
                input,expectedOutput,tags,weight,expectedToolCalls
              </code>
              <ul className="text-xs text-text-muted mt-2 space-y-0.5">
                <li>· Coluna obrigatória: <code>input</code></li>
                <li>· Tags separadas por <code>|</code> dentro do campo</li>
                <li>· ExpectedToolCalls é JSON array literal: <code>[{`{"name":"get_weather"}`}]</code></li>
                <li>· BOM UTF-8/UTF-16 detectado e ignorado</li>
              </ul>
            </div>
            <input
              type="file"
              ref={fileInputRef}
              accept=".csv,text/csv"
              onChange={(e) => setCsvFile(e.target.files?.[0] ?? null)}
              className="block w-full text-sm text-text-secondary file:mr-3 file:py-1.5 file:px-3 file:rounded file:border-0 file:bg-blue-500/15 file:text-blue-400 file:cursor-pointer"
            />
            {csvTooLarge && (
              <div className="text-xs text-red-400">
                Arquivo excede 5MB ({(csvFile.size / 1024 / 1024).toFixed(1)}MB).
                Para imports maiores, divida em múltiplas versions.
              </div>
            )}
            <Input
              label="Change reason (opcional)"
              value={changeReason}
              onChange={(e) => setChangeReason(e.target.value)}
            />
            <Button
              variant="primary"
              onClick={handlePublishCsv}
              loading={importMutation.isPending}
              disabled={!csvFile || !!csvTooLarge}
            >
              Importar e publicar
            </Button>
            {importMutation.error && (
              <div className="text-sm text-red-400">{(importMutation.error as Error).message}</div>
            )}
          </div>
        </Card>
      )}

      {activeTab === 'versions' && (
        <Card>
          {versions.length === 0 ? (
            <div className="text-sm text-text-muted py-4 text-center">Sem versões publicadas ainda.</div>
          ) : (
            <div className="space-y-2">
              {versions.map((v: TestSetVersion) => (
                <div
                  key={v.testSetVersionId}
                  className={
                    'border rounded-lg px-3 py-2 cursor-pointer ' +
                    (versionToView === v.testSetVersionId
                      ? 'border-blue-500/50 bg-blue-500/5'
                      : 'border-border-secondary hover:border-border-primary')
                  }
                  onClick={() => setVersionToView(v.testSetVersionId)}
                >
                  <div className="flex items-center justify-between mb-1">
                    <div className="flex items-center gap-2">
                      <span className="font-mono text-sm font-semibold">v{v.revision}</span>
                      <span
                        title={
                          v.status === 'Published' ? 'Versão em uso por runs novas' :
                          v.status === 'Deprecated' ? 'Não usar mais — runs antigas mantidas' :
                          'Rascunho — ainda não publicado'
                        }
                      >
                        <Badge
                          variant={
                            v.status === 'Published' ? 'green' : v.status === 'Deprecated' ? 'red' : 'gray'
                          }
                        >
                          {v.status === 'Published' ? 'Em uso' :
                           v.status === 'Deprecated' ? 'Não usar mais' :
                           'Rascunho'}
                        </Badge>
                      </span>
                      <span className="text-xs font-mono text-text-muted">
                        {v.contentHash.slice(0, 8)}…
                      </span>
                    </div>
                    {v.status !== 'Deprecated' && (
                      <Button
                        size="sm"
                        variant="secondary"
                        onClick={(e) => {
                          e.stopPropagation()
                          if (!testSet) return
                          updateStatusMutation.mutate({
                            id: testSet.id,
                            vid: v.testSetVersionId,
                            status: 'Deprecated',
                          })
                        }}
                      >
                        Deprecate
                      </Button>
                    )}
                  </div>
                  {v.changeReason && (
                    <div className="text-xs text-text-muted">{v.changeReason}</div>
                  )}
                  <div className="text-xs text-text-muted">
                    {new Date(v.createdAt).toLocaleString('pt-BR')}
                    {v.createdBy ? ` · por ${v.createdBy}` : ''}
                  </div>
                </div>
              ))}
            </div>
          )}

          {versionToView && versionCasesQuery.data && (
            <div className="mt-4 border-t border-border-secondary pt-4">
              <div className="flex items-center justify-between mb-2">
                <h4 className="text-sm font-semibold">
                  Cases da version selecionada ({versionCasesQuery.data.length})
                </h4>
                {versionCasesQuery.data.length > 0 && (
                  <Button
                    size="sm"
                    variant="secondary"
                    onClick={() => {
                      const cloned: CaseDraft[] = versionCasesQuery.data!.map((c) => ({
                        input: c.input,
                        expectedOutput: c.expectedOutput ?? '',
                        tags: c.tags.join(', '),
                        weight: String(c.weight ?? 1.0),
                      }))
                      setDrafts(cloned.length > 0 ? cloned : [{ ...EMPTY_CASE }])
                      setActiveTab('inline')
                    }}
                  >
                    Clonar para draft
                  </Button>
                )}
              </div>
              <div className="space-y-2 max-h-96 overflow-y-auto">
                {versionCasesQuery.data.map((c) => (
                  <div key={c.caseId} className="text-xs border border-border-secondary rounded p-2 space-y-1">
                    <div className="font-mono text-text-muted">#{c.index}</div>
                    <div className="text-text-primary">{c.input.slice(0, 200)}</div>
                    {c.expectedOutput && (
                      <div className="text-text-secondary">→ {c.expectedOutput.slice(0, 150)}</div>
                    )}
                    {c.tags.length > 0 && (
                      <div className="flex gap-1 flex-wrap">
                        {c.tags.map((t) => (
                          <Badge key={t} variant="gray">{t}</Badge>
                        ))}
                      </div>
                    )}
                  </div>
                ))}
              </div>
            </div>
          )}
        </Card>
      )}

      <GenerateCasesModal
        open={generatorOpen}
        onClose={() => setGeneratorOpen(false)}
        onGenerated={handleGeneratedCases}
        hasExistingCases={hasFilledCases}
      />
    </div>
  )
}
