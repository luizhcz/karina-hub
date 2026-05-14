import { useEffect, useState } from 'react'
import { useNavigate, useParams } from 'react-router'
import { getAgent, type Agent } from '../api/agents'
import { predictRouterIntent, type RouterPredictResult } from '../api/agentSandbox'
import { friendlyError } from '../api/client'
import {
  ArrowLeftIcon,
  Badge,
  Button,
  Card,
  CardHeader,
  ErrorMessage,
  Spinner,
} from '../ui'

/**
 * Predict-only de Router. Stateless: cada submit gera nova classificação,
 * nenhuma persistência. UX foca em diagnosticar prompt — mostra intent
 * extraído, reasoning, e raw output (colapsado) pra debug quando JSON sai
 * malformado.
 */
export function RouterIntentPredictor() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()

  const [agent, setAgent] = useState<Agent | null>(null)
  const [loading, setLoading] = useState(true)
  const [loadError, setLoadError] = useState<string | null>(null)

  const [input, setInput] = useState('')
  const [predicting, setPredicting] = useState(false)
  const [result, setResult] = useState<RouterPredictResult | null>(null)
  const [predictError, setPredictError] = useState<string | null>(null)
  const [showRaw, setShowRaw] = useState(false)

  useEffect(() => {
    if (!id) return
    setLoading(true)
    setLoadError(null)
    getAgent(id)
      .then(setAgent)
      .catch((err) => setLoadError(friendlyError(err, 'Não foi possível carregar o agente.')))
      .finally(() => setLoading(false))
  }, [id])

  const handleSubmit = async () => {
    if (!id || !input.trim()) return
    setPredicting(true)
    setPredictError(null)
    setResult(null)
    try {
      const r = await predictRouterIntent(id, { input: input.trim() })
      setResult(r)
    } catch (err) {
      setPredictError(friendlyError(err, 'Falha ao classificar intent.'))
    } finally {
      setPredicting(false)
    }
  }

  if (loading) {
    return (
      <div className="flex items-center justify-center p-12">
        <Spinner />
      </div>
    )
  }

  if (loadError || !agent) {
    return <ErrorMessage message={loadError ?? 'Agente não encontrado.'} />
  }

  if (agent.type !== 'Router') {
    return (
      <ErrorMessage
        message={`Agente '${agent.id}' é do tipo ${agent.type} — predict-intent só funciona pra Router.`}
      />
    )
  }

  const intent = result?.intent ?? null
  const intentIsUnknown = intent === 'unknown'

  return (
    <div className="mx-auto max-w-3xl space-y-6 p-6">
      <div className="flex items-center justify-between">
        <Button variant="ghost" size="sm" onClick={() => navigate('/agentes')} leftIcon={<ArrowLeftIcon className="h-4 w-4" />}>
          Voltar
        </Button>
        <Badge tone="accent">Router · predict-only</Badge>
      </div>

      <Card>
        <CardHeader
          title={`Predict intent · ${agent.name}`}
          description="Stateless: cada execução chama o LLM diretamente, sem criar session ou workflow. Use pra validar que o prompt classifica os intents corretamente."
        />
        <div className="space-y-3 p-6">
          <label className="block text-sm font-medium text-fg" htmlFor="predictor-input">
            Mensagem de entrada
          </label>
          <textarea
            id="predictor-input"
            value={input}
            onChange={(e) => setInput(e.target.value)}
            className="min-h-[120px] w-full rounded-md border border-border bg-bg p-3 text-sm font-mono focus:border-accent focus:outline-none"
            placeholder="ex: preciso de ajuda com pagamento da minha fatura"
            disabled={predicting}
          />
          <div className="flex justify-end">
            <Button
              onClick={handleSubmit}
              loading={predicting}
              disabled={!input.trim()}
            >
              Classificar
            </Button>
          </div>
        </div>
      </Card>

      {predictError && <ErrorMessage message={predictError} />}

      {result && (
        <Card>
          <CardHeader title="Resultado" description={`Latência: ${result.latencyMs} ms · versão pinada: ${result.agentVersionId}`} />
          <div className="space-y-4 p-6">
            <div>
              <span className="text-xs uppercase tracking-wider text-fg-muted">Intent</span>
              <div className="mt-1">
                <span
                  className={
                    intentIsUnknown
                      ? 'inline-block rounded-md border border-amber-500/40 bg-amber-500/10 px-3 py-1 text-base font-semibold text-amber-600 dark:text-amber-400'
                      : 'inline-block rounded-md border border-emerald-500/40 bg-emerald-500/10 px-3 py-1 text-base font-semibold text-emerald-600 dark:text-emerald-400'
                  }
                >
                  {intent}
                </span>
                {intentIsUnknown && (
                  <p className="mt-2 text-xs text-fg-muted">
                    Output do LLM não bateu com o schema canônico de Router
                    (<code>{`{ intent, reasoning? }`}</code>). Veja o raw abaixo
                    pra entender o que voltou.
                  </p>
                )}
              </div>
            </div>

            {result.reasoning && (
              <div>
                <span className="text-xs uppercase tracking-wider text-fg-muted">Reasoning</span>
                <p className="mt-1 whitespace-pre-wrap text-sm text-fg">{result.reasoning}</p>
              </div>
            )}

            <div>
              <button
                type="button"
                onClick={() => setShowRaw((v) => !v)}
                className="text-xs text-fg-muted underline-offset-2 hover:underline"
              >
                {showRaw ? 'Esconder raw output' : 'Mostrar raw output'}
              </button>
              {showRaw && (
                <pre className="mt-2 max-h-96 overflow-auto rounded-md bg-bg-elevated p-3 text-xs">
                  {result.rawOutput || '(vazio)'}
                </pre>
              )}
            </div>
          </div>
        </Card>
      )}
    </div>
  )
}
