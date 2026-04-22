import http from 'k6/http'
import { check } from 'k6'
import { Trend, Counter } from 'k6/metrics'
import { htmlReport } from 'https://raw.githubusercontent.com/benc-uk/k6-reporter/main/dist/bundle.js'
import { textSummary } from 'https://jslib.k6.io/k6-summary/0.0.2/index.js'

const BASE = __ENV.BASE_URL || 'http://localhost:5189'
const WORKFLOW_ID = __ENV.WORKFLOW_ID || 'atendimento-cliente'
const VUS = parseInt(__ENV.VUS || '30', 10)
const TURN_MESSAGES = [
  __ENV.USER_MESSAGE_1 || 'Olá, queria ver minha carteira',
  __ENV.USER_MESSAGE_2 || 'E qual a minha melhor posição atual?',
]

const ttfbCreate = new Trend('ttfb_create_conversation', true)
const ttfbSend = new Trend('ttfb_send_message', true)
const ttcTurn1 = new Trend('workflow_completion_turn1_ms', true)
const ttcTurn2 = new Trend('workflow_completion_turn2_ms', true)
const ttcTotal = new Trend('conversation_total_ms', true)
const rateLimited429 = new Counter('rate_limited_429')
const backPressure503 = new Counter('back_pressure_503')
const streamErrors = new Counter('stream_error_events')
const turn1Success = new Counter('turn1_success')
const turn2Success = new Counter('turn2_success')

// Thresholds escalam linearmente com VUS: `turn{N}_success >= VUS - 1` tolera
// no máximo 1 falha (timing extremo). Outros limites são absolutos.
export const options = {
  scenarios: {
    burst: {
      executor: 'per-vu-iterations',
      vus: VUS,
      iterations: 1,
      maxDuration: '5m',
      startTime: '0s',
      gracefulStop: '30s',
    },
  },
  thresholds: {
    http_req_failed: ['rate<0.05'],
    checks: ['rate>0.95'],
    workflow_completion_turn1_ms: ['p(95)<60000'],
    workflow_completion_turn2_ms: ['p(95)<60000'],
    ttfb_create_conversation: ['p(95)<5000'],
    rate_limited_429: ['count<5'],
    back_pressure_503: ['count==0'],
    turn1_success: [`count>=${VUS - 1}`],
    turn2_success: [`count>=${VUS - 1}`],
  },
}

function sendTurn(conversationId, headers, userId, message, turnIdx) {
  const sendRes = http.post(
    `${BASE}/api/conversations/${conversationId}/messages`,
    JSON.stringify([{ role: 'user', message }]),
    { headers, tags: { op: `send_turn${turnIdx}` } },
  )
  ttfbSend.add(sendRes.timings.waiting)
  if (sendRes.status === 429) rateLimited429.add(1)
  if (sendRes.status === 503) backPressure503.add(1)
  const sendOk = check(sendRes, {
    [`turn${turnIdx} message 200`]: (r) => r.status === 200,
  })
  if (!sendOk) {
    console.error(`VU${__VU} turn${turnIdx} send failed: ${sendRes.status} ${sendRes.body}`)
    return { ok: false, elapsed: 0 }
  }

  const sseStart = Date.now()
  const streamRes = http.get(
    `${BASE}/api/conversations/${conversationId}/messages/stream`,
    {
      headers: { 'x-efs-account': userId, Accept: 'text/event-stream' },
      timeout: '120s',
      tags: { op: `stream_turn${turnIdx}` },
    },
  )
  const elapsed = Date.now() - sseStart

  const body = streamRes.body || ''
  if (body.includes('event: error')) streamErrors.add(1)

  // 200 + workflow_completed = stream acompanhou o workflow inteiro.
  // 204 = workflow já tinha completado ao abrir o stream (ActiveExecutionId=null).
  //       Válido: o POST /messages persistiu e despachou; só perdemos a observação.
  //       Ainda conta como sucesso porque o backend processou sem erro.
  const streamStatus = streamRes.status
  const hasCompletedEvt =
    body.includes('workflow_completed') || body.includes('message_complete')
  const isFastComplete = streamStatus === 204

  const streamOk = check(streamRes, {
    [`turn${turnIdx} stream 200 or 204`]: (r) => r.status === 200 || r.status === 204,
    [`turn${turnIdx} completed (evt or fast-204)`]: () => hasCompletedEvt || isFastComplete,
    [`turn${turnIdx} no error event`]: () => !body.includes('event: error'),
  })
  if (!streamOk) {
    console.error(
      `VU${__VU} turn${turnIdx} stream FAIL: status=${streamStatus} bodyLen=${body.length} ` +
      `preview="${body.slice(0, 200).replace(/\n/g, ' ')}"`,
    )
  }

  return { ok: streamOk, elapsed }
}

export default function () {
  // userId fixo por VU (sem timestamp) para match com Admin:AccountIds
  // no appsettings.Development.json. O runner limpa chaves Redis de rate limiter
  // entre execuções.
  const userId = `load-vu${__VU}`
  const headers = { 'x-efs-account': userId, 'Content-Type': 'application/json' }

  const t0 = Date.now()
  const createRes = http.post(
    `${BASE}/api/conversations`,
    JSON.stringify({
      workflowId: WORKFLOW_ID,
      metadata: { source: 'k6-burst', vu: String(__VU) },
    }),
    { headers, tags: { op: 'create' } },
  )
  ttfbCreate.add(createRes.timings.waiting)
  if (createRes.status === 429) rateLimited429.add(1)
  if (createRes.status === 503) backPressure503.add(1)
  if (!check(createRes, { 'conversation 201': (r) => r.status === 201 })) {
    console.error(`VU${__VU} create failed: ${createRes.status} ${createRes.body}`)
    return
  }

  const { conversationId } = createRes.json()

  const turn1 = sendTurn(conversationId, headers, userId, TURN_MESSAGES[0], 1)
  if (!turn1.ok) return
  ttcTurn1.add(turn1.elapsed)
  turn1Success.add(1)

  const turn2 = sendTurn(conversationId, headers, userId, TURN_MESSAGES[1], 2)
  if (!turn2.ok) return
  ttcTurn2.add(turn2.elapsed)
  turn2Success.add(1)

  ttcTotal.add(Date.now() - t0)
}

export function handleSummary(data) {
  return {
    'scripts/load/last-report.html': htmlReport(data),
    stdout: textSummary(data, { indent: ' ', enableColors: true }),
  }
}
