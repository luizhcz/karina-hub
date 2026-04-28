interface Props {
  judgeModel?: string
  agentModel?: string
}

export function SelfEnhancementBiasBanner({ judgeModel, agentModel }: Props) {
  if (!judgeModel || !agentModel) return null
  const family = (m: string) => m.toLowerCase().split(/[-_/]/)[0]
  const fJudge = family(judgeModel)
  const fAgent = family(agentModel)
  if (fJudge !== fAgent) return null

  return (
    <div className="mb-4 rounded-lg border border-amber-500/30 bg-amber-500/10 px-4 py-3 text-sm text-amber-300">
      <div className="font-semibold mb-1">⚠ Self-enhancement bias detectado</div>
      <div className="text-amber-200/80 leading-relaxed">
        Judge ({judgeModel}) e agente ({agentModel}) pertencem à mesma família LLM ({fJudge}*).
        LLM-as-judge tende a favorecer outputs do mesmo provider. Considere usar judge de
        família distinta (ex.: agente Claude → judge GPT-4o) para Quality evaluations críticas.
      </div>
    </div>
  )
}
