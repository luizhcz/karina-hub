import { useState, useEffect } from 'react'
import { api } from '../../api'
import type { WorkflowDef } from '../../types'

export interface TestCase {
  id: string
  workflowId: string
  workflowName: string
  input: string
  expectedOutput: string
  promptVersionId?: string
  savedAt: string
}

type RunStatus = 'idle' | 'running' | 'match' | 'divergent' | 'failed'

interface RunResult {
  status: RunStatus
  actualOutput?: string
  executionId?: string
}

interface Props {
  workflows: WorkflowDef[]
  testCases: TestCase[]
  onDelete: (id: string) => void
}

function simpleDiff(expected: string, actual: string): { type: 'same' | 'add' | 'remove'; text: string }[] {
  const expLines = expected.split('\n')
  const actLines = actual.split('\n')
  const result: { type: 'same' | 'add' | 'remove'; text: string }[] = []
  const maxLen = Math.max(expLines.length, actLines.length)
  for (let i = 0; i < maxLen; i++) {
    const e = expLines[i]
    const a = actLines[i]
    if (e === a) {
      if (e !== undefined) result.push({ type: 'same', text: e })
    } else {
      if (e !== undefined) result.push({ type: 'remove', text: e })
      if (a !== undefined) result.push({ type: 'add', text: a })
    }
  }
  return result
}

export function TestCasesPanel({ workflows, testCases, onDelete }: Props) {
  const [runResults, setRunResults] = useState<Record<string, RunResult>>({})
  const [expandedId, setExpandedId] = useState<string | null>(null)

  const wfMap = Object.fromEntries(workflows.map(w => [w.id, w.name]))

  const runTestCase = async (tc: TestCase) => {
    setRunResults(prev => ({ ...prev, [tc.id]: { status: 'running' } }))
    try {
      const { executionId } = await api.triggerWorkflow(tc.workflowId, tc.input)
      // Poll until completed (max 60s)
      let output: string | undefined
      let status: RunStatus = 'running'
      for (let i = 0; i < 60; i++) {
        await new Promise(r => setTimeout(r, 1000))
        const exec = await api.getExecution(executionId)
        if (exec.status === 'Completed') {
          output = exec.output ?? ''
          const norm = (s: string) => s.trim().replace(/\s+/g, ' ')
          status = norm(output) === norm(tc.expectedOutput) ? 'match' : 'divergent'
          break
        }
        if (exec.status === 'Failed' || exec.status === 'Cancelled') {
          output = exec.errorMessage ?? 'Execução falhou'
          status = 'failed'
          break
        }
      }
      if (status === 'running') {
        status = 'failed'
        output = 'Timeout — execução demorou mais de 60s'
      }
      setRunResults(prev => ({ ...prev, [tc.id]: { status, actualOutput: output, executionId } }))
    } catch (e) {
      setRunResults(prev => ({ ...prev, [tc.id]: { status: 'failed', actualOutput: String(e) } }))
    }
  }

  if (testCases.length === 0) {
    return (
      <div className="p-4">
        <div className="text-center text-[#3E5F7D] text-sm py-16 flex flex-col items-center gap-3">
          <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="#3E5F7D" strokeWidth="1.5">
            <path d="M9 5H7a2 2 0 00-2 2v12a2 2 0 002 2h10a2 2 0 002-2V7a2 2 0 00-2-2h-2" />
            <rect x="9" y="3" width="6" height="4" rx="1" />
            <path d="M9 12h6M9 16h4" />
          </svg>
          <div>
            <p className="font-medium text-[#4A6B8A]">Nenhum caso de teste salvo</p>
            <p className="text-[12px] mt-1">Abra uma execução no Troubleshooting e clique em "Salvar como Caso de Teste"</p>
          </div>
        </div>
      </div>
    )
  }

  return (
    <div className="p-4 space-y-3">
      <div className="flex items-center justify-between mb-1">
        <div className="text-xs font-medium text-[#7596B8] uppercase tracking-wider">{testCases.length} caso{testCases.length !== 1 ? 's' : ''} de teste</div>
        <div className="text-[11px] text-[#3E5F7D]">Armazenados na sessão atual</div>
      </div>

      {testCases.map(tc => {
        const result = runResults[tc.id]
        const isExpanded = expandedId === tc.id
        const wfName = wfMap[tc.workflowId] ?? tc.workflowId

        const statusColor = !result || result.status === 'idle' ? ''
          : result.status === 'running' ? 'text-blue-400'
          : result.status === 'match' ? 'text-emerald-400'
          : result.status === 'divergent' ? 'text-amber-400'
          : 'text-red-400'

        const statusIcon = !result || result.status === 'idle' ? ''
          : result.status === 'running' ? '↻'
          : result.status === 'match' ? '✓ Match'
          : result.status === 'divergent' ? '⚠ Divergente'
          : '✕ Falhou'

        return (
          <div key={tc.id} className="bg-[#04091A] border border-[#0C1D38] rounded-xl overflow-hidden">
            {/* Header */}
            <div className="flex items-center gap-3 px-4 py-3 border-b border-[#0C1D38]/50">
              <div className="flex-1 min-w-0">
                <div className="flex items-center gap-2">
                  <span className="text-xs text-[#DCE8F5] font-medium truncate">{wfName}</span>
                  {tc.promptVersionId && (
                    <span className="px-1 py-0.5 rounded text-[9px] bg-violet-500/10 text-violet-400 border border-violet-500/20 font-mono shrink-0">
                      {tc.promptVersionId}
                    </span>
                  )}
                </div>
                <div className="text-[11px] text-[#4A6B8A] mt-0.5 font-mono truncate">
                  {tc.input.slice(0, 80)}{tc.input.length > 80 ? '…' : ''}
                </div>
              </div>
              <div className="flex items-center gap-2 shrink-0">
                {result?.status === 'running' ? (
                  <div className="w-3 h-3 border-[1.5px] border-[#254980] border-t-[#7596B8] rounded-full animate-spin" />
                ) : (
                  statusIcon && <span className={`text-[11px] font-medium ${statusColor}`}>{statusIcon}</span>
                )}
                {(result?.status === 'match' || result?.status === 'divergent' || result?.status === 'failed') && (
                  <button
                    onClick={() => setExpandedId(isExpanded ? null : tc.id)}
                    className="text-[11px] text-[#4A6B8A] hover:text-[#B8CEE5] transition-colors"
                  >
                    {isExpanded ? '▲' : '▼'}
                  </button>
                )}
                <button
                  onClick={() => runTestCase(tc)}
                  disabled={result?.status === 'running'}
                  className="px-2.5 py-1 rounded-md text-[11px] font-medium bg-white text-black hover:bg-[#e0e0e0] transition-colors disabled:opacity-40"
                >
                  Executar
                </button>
                <button
                  onClick={() => onDelete(tc.id)}
                  className="p-1 text-[#444] hover:text-[#ff4444] transition-colors"
                  title="Remover caso de teste"
                >
                  <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                    <path d="M3 6h18M19 6v14a2 2 0 01-2 2H7a2 2 0 01-2-2V6M8 6V4a2 2 0 012-2h4a2 2 0 012 2v2" />
                  </svg>
                </button>
              </div>
            </div>

            {/* Expanded result */}
            {isExpanded && result && (result.status === 'match' || result.status === 'divergent' || result.status === 'failed') && (
              <div className="px-4 py-3 space-y-3 bg-[#081529]/40">
                {result.status === 'match' ? (
                  <div className="text-[12px] text-emerald-400 flex items-center gap-2">
                    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5"><path d="M20 6L9 17l-5-5" /></svg>
                    Output corresponde ao esperado
                  </div>
                ) : result.status === 'failed' ? (
                  <div>
                    <div className="text-[10px] text-[#4A6B8A] uppercase tracking-wider mb-1">Erro</div>
                    <pre className="text-[11px] text-red-400 font-mono whitespace-pre-wrap bg-red-500/5 border border-red-500/10 rounded-lg p-2">{result.actualOutput}</pre>
                  </div>
                ) : (
                  <div className="grid grid-cols-2 gap-3">
                    <div>
                      <div className="text-[10px] text-[#4A6B8A] uppercase tracking-wider mb-1">Esperado</div>
                      <pre className="text-[11px] text-[#B8CEE5] font-mono whitespace-pre-wrap bg-[#04091A] border border-[#0C1D38] rounded-lg p-2 max-h-40 overflow-y-auto">{tc.expectedOutput}</pre>
                    </div>
                    <div>
                      <div className="text-[10px] text-[#4A6B8A] uppercase tracking-wider mb-1">Real</div>
                      <pre className="text-[11px] text-amber-300 font-mono whitespace-pre-wrap bg-[#04091A] border border-amber-500/20 rounded-lg p-2 max-h-40 overflow-y-auto">{result.actualOutput}</pre>
                    </div>
                    <div className="col-span-2">
                      <div className="text-[10px] text-[#4A6B8A] uppercase tracking-wider mb-1">Diff</div>
                      <div className="text-[11px] font-mono bg-[#04091A] border border-[#0C1D38] rounded-lg p-2 max-h-40 overflow-y-auto space-y-px">
                        {simpleDiff(tc.expectedOutput, result.actualOutput ?? '').map((line, i) => (
                          <div
                            key={i}
                            className={
                              line.type === 'same' ? 'text-[#4A6B8A]'
                              : line.type === 'add' ? 'text-emerald-400 bg-emerald-500/5'
                              : 'text-red-400 bg-red-500/5 line-through'
                            }
                          >
                            {line.type === 'add' ? '+ ' : line.type === 'remove' ? '- ' : '  '}{line.text || ' '}
                          </div>
                        ))}
                      </div>
                    </div>
                  </div>
                )}
              </div>
            )}
          </div>
        )
      })}
    </div>
  )
}
