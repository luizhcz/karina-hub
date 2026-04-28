import { Badge } from '../../../shared/ui/Badge'

interface Props {
  score?: number | null
  passed?: boolean
}

export function EvalScoreBadge({ score, passed }: Props) {
  if (score === null || score === undefined) {
    if (passed === true) return <Badge variant="green">PASS</Badge>
    if (passed === false) return <Badge variant="red">FAIL</Badge>
    return <Badge variant="gray">—</Badge>
  }
  const pct = Math.round(score * 100)
  if (score >= 0.8) return <Badge variant="green">{pct}%</Badge>
  if (score >= 0.5) return <Badge variant="yellow">{pct}%</Badge>
  return <Badge variant="red">{pct}%</Badge>
}
