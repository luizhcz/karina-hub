import { Card } from '../../../shared/ui/Card'
import { Badge } from '../../../shared/ui/Badge'

const PROVIDERS = ['OpenAI', 'Azure', 'Anthropic']

export function CircuitBreakerStatus() {
  return (
    <Card title="Circuit Breakers">
      <div className="flex flex-col gap-2">
        {PROVIDERS.map((p) => (
          <div key={p} className="flex items-center justify-between">
            <span className="text-xs text-text-secondary">{p}</span>
            <Badge variant="green">Closed</Badge>
          </div>
        ))}
        <span className="text-[10px] text-text-muted mt-1">
          Placeholder — real status via OTel
        </span>
      </div>
    </Card>
  )
}
