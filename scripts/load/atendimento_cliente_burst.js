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

// ── Métricas ─────────────────────────────────────────────────────────────────
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

// ── Métricas de eventos SSE (AG-UI / workflow native) ────────────────────────
// Contadores por tipo ajudam a detectar regressão silenciosa: se o backend parar
// de emitir tokens, ou sumir um node_*, aparece aqui.
const eventsTotal = new Counter('events_total')
const eventsPerTurn = new Trend('events_per_turn')
const tokensPerTurn = new Trend('tokens_per_turn')
const sequenceValid = new Counter('sequence_valid') // começou + terminou bem
const sequenceBroken = new Counter('sequence_broken')

// Principais tipos esperados no fluxo atendimento-cliente — contador por tipo
// permite distribuição agregada na saída.
const TYPED_EVENTS = [
  'workflow_started',
  'workflow_completed',
  'message_complete',
  'node_started',
  'node_completed',
  'step_started',
  'step_finished',
  'state_snapshot',
  'state_delta',
  'messages_snapshot',
  'text_message_start',
  'text_message_content',
  'text_message_end',
  'token',
  'tool_call_start',
  'tool_call_end',
  'run_started',
  'run_finished',
  'run_error',
  'error',
  'hitl_required',
  'waiting_for_input',
]
const eventCounters = Object.fromEntries(
  TYPED_EVENTS.map((t) => [t, new Counter(`evt_${t}`)]),
)
const eventUnknown = new Counter('evt_unknown')

// Thresholds: escalam com VUS. Tolerância absoluta + p95 absoluto.
export const options = {
  scenarios: {
    burst: {
      executor: 'per-vu-iterations',
      vus: VUS,
      iterations: 1,
      maxDuration: '10m',
      startTime: '0s',
      gracefulStop: '30s',
    },
  },
  thresholds: {
    http_req_failed: ['rate<0.05'],
    checks: ['rate>0.95'],
    workflow_completion_turn1_ms: ['p(95)<90000'],
    workflow_completion_turn2_ms: ['p(95)<90000'],
    ttfb_create_conversation: ['p(95)<10000'],
    rate_limited_429: [`count<${Math.max(5, Math.floor(VUS * 0.02))}`],
    back_pressure_503: [`count<${Math.max(1, Math.floor(VUS * 0.02))}`],
    turn1_success: [`count>=${Math.floor(VUS * 0.98)}`],
    turn2_success: [`count>=${Math.floor(VUS * 0.98)}`],
    // Qualquer quebra de sequência é relevante — não tolera mais que 2% dos turnos
    sequence_broken: [`count<${Math.max(2, Math.floor(VUS * 0.02))}`],
  },
}

// ── Parser SSE ───────────────────────────────────────────────────────────────
// Formato: blocos separados por "\n\n", cada bloco com linhas "event: X" e "data: {...}".
// Retorna {events: [{type, data}], totalCount, typeCounts}.
function parseSseBody(body) {
  const blocks = body.split('\n\n')
  const events = []
  const typeCounts = {}
  for (const block of blocks) {
    if (!block.trim()) continue
    let type = null
    let data = null
    for (const line of block.split('\n')) {
      if (line.startsWith('event: ')) type = line.slice(7).trim()
      else if (line.startsWith('data: ')) data = line.slice(6)
    }
    if (type) {
      events.push({ type, data })
      typeCounts[type] = (typeCounts[type] || 0) + 1
    }
  }
  return { events, totalCount: events.length, typeCounts }
}

function recordEventMetrics(typeCounts) {
  for (const [type, count] of Object.entries(typeCounts)) {
    eventsTotal.add(count)
    if (eventCounters[type]) eventCounters[type].add(count)
    else eventUnknown.add(count, { type })
  }
}

// Validação mínima: turno teve ao menos 1 evento terminal e nenhum de erro.
function classifySequence(typeCounts, fast204) {
  if (fast204) return 'ok_fast' // workflow já completou antes do stream abrir
  const hasTerminal =
    (typeCounts['workflow_completed'] || 0) +
      (typeCounts['message_complete'] || 0) +
      (typeCounts['run_finished'] || 0) >
    0
  const hasError =
    (typeCounts['error'] || 0) +
      (typeCounts['run_error'] || 0) >
    0
  if (hasError) return 'error'
  if (!hasTerminal) return 'no_terminal'
  return 'ok'
}

// Log error preserva 1 linha por VU, mas limita payload. Em 1000 VUs o log seria inútil
// se cada VU despeja 200 chars de preview.
function logOnce(msg) {
  if (__VU <= 5 || __VU % 100 === 0) console.error(msg)
}

// ── Turno único: send + stream + parse + métricas ────────────────────────────
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
    logOnce(`VU${__VU} turn${turnIdx} send FAIL: ${sendRes.status} ${String(sendRes.body).slice(0, 120)}`)
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
  const streamStatus = streamRes.status
  const fast204 = streamStatus === 204

  // Parse + contagem por tipo. Fast-204 não tem body.
  let events = []
  let typeCounts = {}
  if (!fast204 && body.length > 0) {
    const parsed = parseSseBody(body)
    events = parsed.events
    typeCounts = parsed.typeCounts
    recordEventMetrics(typeCounts)
    eventsPerTurn.add(parsed.totalCount)
    tokensPerTurn.add(
      (typeCounts['token'] || 0) + (typeCounts['text_message_content'] || 0),
    )
  }

  const classification = classifySequence(typeCounts, fast204)
  if (classification === 'ok' || classification === 'ok_fast') {
    sequenceValid.add(1)
  } else {
    sequenceBroken.add(1, { reason: classification })
    logOnce(
      `VU${__VU} turn${turnIdx} seq ${classification} | status=${streamStatus} ` +
        `events=${events.length} types=${JSON.stringify(typeCounts).slice(0, 160)}`,
    )
  }

  if ((typeCounts['error'] || 0) + (typeCounts['run_error'] || 0) > 0) {
    streamErrors.add(1)
  }

  const streamOk = check(streamRes, {
    [`turn${turnIdx} stream 200 or 204`]: (r) => r.status === 200 || r.status === 204,
    [`turn${turnIdx} sequence ok`]: () =>
      classification === 'ok' || classification === 'ok_fast',
    [`turn${turnIdx} no error event`]: () => classification !== 'error',
  })

  return { ok: streamOk, elapsed }
}

export default function () {
  // userId fixo por VU (sem timestamp) para match com Admin:AccountIds
  // no appsettings.Development.json.
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
    logOnce(`VU${__VU} create FAIL: ${createRes.status} ${String(createRes.body).slice(0, 120)}`)
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
