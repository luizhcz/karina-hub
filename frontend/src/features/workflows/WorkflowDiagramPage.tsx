import { useMemo } from 'react'
import { Link, useParams } from 'react-router'
import dagre from '@dagrejs/dagre'
import {
  ReactFlow,
  Background,
  Controls,

  type Node,
  type Edge,
  BackgroundVariant,
  MarkerType,
  Handle,
  Position,
} from '@xyflow/react'
import '@xyflow/react/dist/style.css'
import { Button } from '../../shared/ui/Button'
import { Badge } from '../../shared/ui/Badge'
import { PageLoader } from '../../shared/ui/LoadingSpinner'
import { ErrorCard } from '../../shared/ui/ErrorCard'
import { useWorkflow } from '../../api/workflows'

// ── Layout Dagre (DAG) ────────────────────────────────────────────────────────

const NODE_W = 180
const NODE_H = 72
const PILL_W = 80
const PILL_H = 36

function applyDagreLayout(nodes: Node[], edges: Edge[], rankdir: 'LR' | 'TB' = 'LR'): Node[] {
  const g = new dagre.graphlib.Graph()
  g.setDefaultEdgeLabel(() => ({}))
  g.setGraph({ rankdir, nodesep: 80, ranksep: 140 })

  nodes.forEach((n) => {
    const isPill = n.id === '__start__' || n.id === '__end__'
    g.setNode(n.id, { width: isPill ? PILL_W : NODE_W, height: isPill ? PILL_H : NODE_H })
  })
  edges.forEach((e) => g.setEdge(e.source, e.target))

  dagre.layout(g)

  return nodes.map((n) => {
    const isPill = n.id === '__start__' || n.id === '__end__'
    const w = isPill ? PILL_W : NODE_W
    const h = isPill ? PILL_H : NODE_H
    const pos = g.node(n.id)
    return { ...n, position: { x: pos.x - w / 2, y: pos.y - h / 2 } }
  })
}

// ── Layout circular (Handoff / mesh) ─────────────────────────────────────────

/**
 * Retorna o sourceHandle e targetHandle corretos dado o ângulo (em rad)
 * da aresta source → target no plano cartesiano (y cresce para baixo).
 */
function handlesFromAngle(angle: number): { sourceHandle: string; targetHandle: string } {
  const a = ((angle % (2 * Math.PI)) + 2 * Math.PI) % (2 * Math.PI)
  if (a < Math.PI / 4 || a >= (7 * Math.PI) / 4) return { sourceHandle: 's-right',  targetHandle: 't-left'   }
  if (a < (3 * Math.PI) / 4)                       return { sourceHandle: 's-bottom', targetHandle: 't-top'    }
  if (a < (5 * Math.PI) / 4)                       return { sourceHandle: 's-left',  targetHandle: 't-right'  }
  return                                                   { sourceHandle: 's-top',   targetHandle: 't-bottom' }
}

/**
 * Posiciona nós em círculo com o primeiro agente (hub) ao centro
 * e os demais ao redor. Start fica à esquerda do centro.
 * Retorna { nodes laidOut, posMap para cálculo de handles }
 */
function applyCircularLayout(
  startNode: Node,
  hubNode: Node,
  spokeNodes: Node[],
): { laidOut: Node[]; posMap: Record<string, { x: number; y: number }> } {
  const n = spokeNodes.length
  const radius = Math.max(220, n * 80)
  const cx = NODE_W + 120 + radius   // garante que o nó mais à esquerda não colide com Start
  const cy = radius + 20

  const posMap: Record<string, { x: number; y: number }> = {}
  posMap['__start__'] = { x: PILL_W / 2 + 20, y: cy }
  posMap[hubNode.id] = { x: cx, y: cy }

  spokeNodes.forEach((node, i) => {
    const angle = (2 * Math.PI * i) / n - Math.PI / 2
    posMap[node.id] = {
      x: cx + radius * Math.cos(angle),
      y: cy + radius * Math.sin(angle),
    }
  })

  const laidOut: Node[] = [
    { ...startNode, position: { x: 20, y: cy - PILL_H / 2 } },
    { ...hubNode, type: 'handoffNode', position: { x: cx - NODE_W / 2, y: cy - NODE_H / 2 } },
    ...spokeNodes.map((node) => ({
      ...node,
      type: 'handoffNode',
      position: { x: posMap[node.id].x - NODE_W / 2, y: posMap[node.id].y - NODE_H / 2 },
    })),
  ]

  return { laidOut, posMap }
}

/**
 * GroupChat: Start → Manager (topo) → Participantes (linha abaixo, bidirecional) → End (direita do Manager)
 */
function applyGroupChatLayout(
  startNode: Node,
  managerNode: Node,
  participantNodes: Node[],
  showEnd: boolean,
): { laidOut: Node[]; posMap: Record<string, { x: number; y: number }> } {
  const n   = participantNodes.length
  const gap = 60
  const totalW = n * NODE_W + (n - 1) * gap
  const cx  = totalW / 2

  const posMap: Record<string, { x: number; y: number }> = {}
  posMap['__start__']    = { x: cx,                    y: 0 }
  posMap[managerNode.id] = { x: cx,                    y: PILL_H + gap }
  participantNodes.forEach((node, i) => {
    posMap[node.id] = { x: i * (NODE_W + gap), y: PILL_H + gap + NODE_H + gap * 1.5 }
  })
  if (showEnd) posMap['__end__'] = { x: cx + NODE_W + gap, y: PILL_H + gap }

  const laidOut: Node[] = [
    { ...startNode, type: 'startNode', position: { x: cx - PILL_W / 2, y: 0 }, data: { vertical: true } },
    { ...managerNode, type: 'handoffNode', position: { x: cx - NODE_W / 2, y: PILL_H + gap } },
    ...participantNodes.map((node, i) => ({
      ...node,
      type: 'handoffNode',
      position: { x: i * (NODE_W + gap), y: PILL_H + gap + NODE_H + gap * 1.5 },
    })),
    ...(showEnd ? [{ id: '__end__', type: 'endNode', position: { x: cx + NODE_W + gap, y: PILL_H + gap }, data: {} }] : []),
  ]

  return { laidOut, posMap }
}

// ── Cores por tipo ────────────────────────────────────────────────────────────

const TYPE_COLORS: Record<string, string> = {
  agent:        '#3b82f6',
  orchestrator: '#8b5cf6',
  executor:     '#f59e0b',
  router:       '#10b981',
  trigger:      '#22c55e',
  tool:         '#06b6d4',
}

function typeColor(type: string) {
  return TYPE_COLORS[type?.toLowerCase()] ?? '#6b7280'
}

// ── Custom nodes ──────────────────────────────────────────────────────────────

interface NodeData {
  label: string
  nodeType: string
  role?: string
  [key: string]: unknown
}

/** Nó padrão — suporta handles horizontais (LR) ou verticais (TB) */
function WorkflowNode({ data }: { data: NodeData }) {
  const color = typeColor(data.nodeType)
  const vertical = !!data.vertical
  return (
    <>
      <Handle type="target" position={vertical ? Position.Top    : Position.Left}  style={{ background: color }} />
      <div
        className="bg-bg-secondary border-2 rounded-xl px-4 py-3 flex flex-col gap-1 shadow-lg overflow-hidden"
        style={{ borderColor: color, width: NODE_W, minHeight: NODE_H }}
      >
        <div
          className="text-[10px] font-semibold uppercase tracking-wider px-1.5 py-0.5 rounded self-start w-full truncate"
          style={{ backgroundColor: `${color}22`, color }}
          title={String(data.role ?? data.nodeType)}
        >
          {data.role ?? data.nodeType}
        </div>
        <p
          className="text-sm font-medium text-text-primary truncate"
          title={data.label}
        >
          {data.label}
        </p>
      </div>
      <Handle type="source" position={vertical ? Position.Bottom : Position.Right} style={{ background: color }} />
    </>
  )
}

/** Nó para Handoff — handles nos 4 lados com IDs para roteamento direcional */
function HandoffNode({ data }: { data: NodeData }) {
  const color = typeColor(data.nodeType)
  const hs = { background: color, border: 'none', width: 8, height: 8 }
  return (
    <>
      <Handle type="target" position={Position.Left}   id="t-left"   style={hs} />
      <Handle type="target" position={Position.Right}  id="t-right"  style={hs} />
      <Handle type="target" position={Position.Top}    id="t-top"    style={hs} />
      <Handle type="target" position={Position.Bottom} id="t-bottom" style={hs} />
      <div
        className="bg-bg-secondary border-2 rounded-xl px-4 py-3 flex flex-col gap-1 shadow-lg overflow-hidden"
        style={{ borderColor: color, width: NODE_W, minHeight: NODE_H }}
      >
        <div
          className="text-[10px] font-semibold uppercase tracking-wider px-1.5 py-0.5 rounded self-start w-full truncate"
          style={{ backgroundColor: `${color}22`, color }}
          title={String(data.role ?? data.nodeType)}
        >
          {data.role ?? data.nodeType}
        </div>
        <p className="text-sm font-medium text-text-primary truncate" title={data.label}>{data.label}</p>
      </div>
      <Handle type="source" position={Position.Left}   id="s-left"   style={hs} />
      <Handle type="source" position={Position.Right}  id="s-right"  style={hs} />
      <Handle type="source" position={Position.Top}    id="s-top"    style={hs} />
      <Handle type="source" position={Position.Bottom} id="s-bottom" style={hs} />
    </>
  )
}

function StartNode({ data }: { data: { vertical?: boolean } }) {
  return (
    <>
      <div
        className="flex items-center justify-center bg-emerald-500/20 border-2 border-emerald-500 rounded-full shadow-lg"
        style={{ width: PILL_W, height: PILL_H }}
      >
        <span className="text-[11px] font-bold text-emerald-400 uppercase tracking-widest">Start</span>
      </div>
      {data.vertical
        ? <Handle type="source" position={Position.Bottom} style={{ background: '#10b981' }} />
        : <Handle type="source" position={Position.Right}  id="s-right" style={{ background: '#10b981' }} />
      }
    </>
  )
}

function EndNode({ data }: { data: { vertical?: boolean } }) {
  return (
    <>
      {data.vertical
        ? <Handle type="target" position={Position.Top}  style={{ background: '#ef4444' }} />
        : <Handle type="target" position={Position.Left} style={{ background: '#ef4444' }} />
      }
      <div
        className="flex items-center justify-center bg-red-500/20 border-2 border-red-500 rounded-full shadow-lg"
        style={{ width: PILL_W, height: PILL_H }}
      >
        <span className="text-[11px] font-bold text-red-400 uppercase tracking-widest">End</span>
      </div>
    </>
  )
}

const nodeTypes = {
  workflowNode: WorkflowNode,
  handoffNode:  HandoffNode,
  startNode:    StartNode,
  endNode:      EndNode,
}

// ── Edge label colors ─────────────────────────────────────────────────────────

const EDGE_TYPE_COLOR: Record<string, string> = {
  Direct:      '#6b7280',
  Conditional: '#f59e0b',
  Switch:      '#8b5cf6',
  FanOut:      '#06b6d4',
  FanIn:       '#10b981',
}

// ── Page ──────────────────────────────────────────────────────────────────────

export function WorkflowDiagramPage() {
  const { id } = useParams<{ id: string }>()
  const { data: workflow, isLoading, error, refetch } = useWorkflow(id!, !!id)

  const { nodes, edges } = useMemo(() => {
    if (!workflow) return { nodes: [], edges: [] }

    const agents    = workflow.agents ?? []
    const executors = (workflow as any).executors ?? []
    const wfEdges   = workflow.edges ?? []
    const isHandoff = workflow.orchestrationMode === 'Handoff'

    // Nós: agentes + executors
    const agentNodes: Node[] = [
      ...agents.map((a: any) => ({
        id: a.agentId,
        type: 'workflowNode',
        position: { x: 0, y: 0 },
        data: { label: a.agentId, nodeType: 'agent', role: a.role ?? undefined },
      })),
      ...executors.map((ex: any) => ({
        id: ex.id,
        type: 'workflowNode',
        position: { x: 0, y: 0 },
        data: { label: ex.id, nodeType: 'executor', role: ex.description ?? 'Executor' },
      })),
    ]

    const allParticipantIds = new Set([
      ...agents.map((a: any)   => a.agentId as string),
      ...executors.map((ex: any) => ex.id    as string),
    ])

    // Arestas brutas (inclui Switch → cases)
    const rawEdges: Edge[] = []
    wfEdges.forEach((e, idx) => {
      const color = EDGE_TYPE_COLOR[e.edgeType] ?? '#6b7280'
      if (e.edgeType === 'Switch' && e.cases?.length) {
        e.cases.forEach((c, ci) => {
          ;(c.targets ?? []).forEach((target, ti) => {
            rawEdges.push({
              id: `e-${idx}-${ci}-${ti}`,
              source: e.from!,
              target,
              label: c.isDefault ? 'default' : c.condition ?? 'Switch',
              animated: false,
              style: { stroke: color },
              labelStyle: { fill: '#9ca3af', fontSize: 10 },
              labelBgStyle: { fill: 'transparent' },
              markerEnd: { type: MarkerType.ArrowClosed, color },
            })
          })
        })
      } else if (e.from && e.to) {
        rawEdges.push({
          id: `e-${idx}`,
          source: e.from!,
          target: e.to!,
          label: e.condition
            ? e.condition
            : e.edgeType !== 'Direct'
            ? e.edgeType
            : undefined,
          animated: e.edgeType === 'Direct',
          style: { stroke: color },
          labelStyle: { fill: '#9ca3af', fontSize: 10 },
          labelBgStyle: { fill: 'transparent' },
          markerEnd: { type: MarkerType.ArrowClosed, color },
        })
      }
    })

    // Detecta entry / terminal
    const allTargets = new Set(rawEdges.map((e) => e.target))
    const allSources = new Set(rawEdges.map((e) => e.source))
    const outputNodes: string[] = (workflow.configuration as any)?.outputNodes ?? []

    const topoEntries = [...allParticipantIds].filter((id) => !allTargets.has(id))
    const effectiveEntryIds = topoEntries.length > 0 ? topoEntries : agentNodes.slice(0, 1).map((n) => n.id)

    const configOutputs  = outputNodes.filter((n) => allParticipantIds.has(n))
    const topoTerminals  = [...allParticipantIds].filter((id) => !allSources.has(id))
    const effectiveTerminalIds =
      configOutputs.length > 0  ? configOutputs  :
      topoTerminals.length > 0   ? topoTerminals   : []

    const showEnd = effectiveTerminalIds.length > 0

    // ── HANDOFF: layout circular ──────────────────────────────────────────────
    if (isHandoff) {
      const startNode: Node = { id: '__start__', type: 'startNode', position: { x: 0, y: 0 }, data: {} }
      const hubId  = effectiveEntryIds[0]
      const hubN   = agentNodes.find((n) => n.id === hubId) ?? agentNodes[0]
      const spokes = agentNodes.filter((n) => n.id !== hubN.id)

      const { laidOut, posMap } = applyCircularLayout(startNode, hubN, spokes)

      // Arestas com handles direcionais baseados no ângulo
      const handoffEdges: Edge[] = rawEdges.map((e) => {
        const src = posMap[e.source]
        const tgt = posMap[e.target]
        if (!src || !tgt) return { ...e, label: undefined }
        const angle = Math.atan2(tgt.y - src.y, tgt.x - src.x)
        const { sourceHandle, targetHandle } = handlesFromAngle(angle)
        return { ...e, label: undefined, sourceHandle, targetHandle }
      })

      const startEdge: Edge = {
        id: `start-to-${hubId}`,
        source: '__start__',
        target: hubId,
        sourceHandle: 's-right',
        targetHandle: 't-left',
        style: { stroke: '#10b981' },
        markerEnd: { type: MarkerType.ArrowClosed, color: '#10b981' },
      }

      return { nodes: laidOut, edges: [startEdge, ...handoffEdges] }
    }

    // ── GROUPCHAT: Manager central, participantes abaixo, bidirecional ─────────
    if (workflow.orchestrationMode === 'GroupChat') {
      const managerN = agentNodes.find((n) => String(n.data.role).toLowerCase() === 'manager') ?? agentNodes[0]
      const participants = agentNodes.filter((n) => n.id !== managerN.id)

      const { laidOut } = applyGroupChatLayout(
        { id: '__start__', type: 'startNode', position: { x: 0, y: 0 }, data: {} },
        managerN,
        participants,
        true,
      )

      // Start → Manager
      const gcEdges: Edge[] = [
        {
          id: 'start-to-manager',
          source: '__start__',
          target: managerN.id,
          targetHandle: 't-top',
          style: { stroke: '#10b981' },
          markerEnd: { type: MarkerType.ArrowClosed, color: '#10b981' },
        },
        // Manager → End (direita)
        {
          id: 'manager-to-end',
          source: managerN.id,
          target: '__end__',
          sourceHandle: 's-right',
          style: { stroke: '#ef4444' },
          markerEnd: { type: MarkerType.ArrowClosed, color: '#ef4444' },
        },
        // Manager ↔ cada participante
        ...participants.flatMap((p) => {
          return [
            {
              id: `mgr-to-${p.id}`,
              source: managerN.id,
              target: p.id,
              sourceHandle: 's-bottom',
              targetHandle: 't-top',
              style: { stroke: '#6b7280' },
              markerEnd: { type: MarkerType.ArrowClosed, color: '#6b7280' },
            } as Edge,
            {
              id: `${p.id}-to-mgr`,
              source: p.id,
              target: managerN.id,
              sourceHandle: 's-top',
              targetHandle: 't-bottom',
              style: { stroke: '#6b7280', strokeDasharray: '4 2' },
              markerEnd: { type: MarkerType.ArrowClosed, color: '#6b7280' },
            } as Edge,
          ]
        }),
      ]

      return { nodes: laidOut, edges: gcEdges }
    }

    // ── SEQUENTIAL: layout TB com edges implícitas ────────────────────────────
    const isSequential = workflow.orchestrationMode === 'Sequential'
    if (isSequential) {
      const orderedIds = agentNodes.map((n) => n.id)
      // Se não há edges explícitas, cria sequência implícita
      const seqEdges: Edge[] = rawEdges.length > 0 ? rawEdges : orderedIds.slice(0, -1).map((id, i) => ({
        id: `seq-${i}`,
        source: id,
        target: orderedIds[i + 1],
        animated: true,
        style: { stroke: '#6b7280' },
        markerEnd: { type: MarkerType.ArrowClosed, color: '#6b7280' },
      }))

      const seqNodes: Node[] = [
        { id: '__start__', type: 'startNode', position: { x: 0, y: 0 }, data: { vertical: true } },
        ...agentNodes.map((n) => ({ ...n, data: { ...n.data, vertical: true } })),
        { id: '__end__', type: 'endNode', position: { x: 0, y: 0 }, data: { vertical: true } },
      ]

      const seqAllEdges: Edge[] = [
        { id: 'start-to-first', source: '__start__', target: orderedIds[0],
          style: { stroke: '#10b981' }, markerEnd: { type: MarkerType.ArrowClosed, color: '#10b981' } },
        ...seqEdges,
        { id: 'last-to-end', source: orderedIds[orderedIds.length - 1], target: '__end__',
          style: { stroke: '#ef4444' }, markerEnd: { type: MarkerType.ArrowClosed, color: '#ef4444' } },
      ]

      return { nodes: applyDagreLayout(seqNodes, seqAllEdges, 'TB'), edges: seqAllEdges }
    }

    // ── OUTROS MODOS: layout Dagre LR ─────────────────────────────────────────
    const allNodes: Node[] = [
      { id: '__start__', type: 'startNode', position: { x: 0, y: 0 }, data: {} },
      ...agentNodes,
      ...(showEnd ? [{ id: '__end__', type: 'endNode', position: { x: 0, y: 0 }, data: {} }] : []),
    ]

    const allEdges: Edge[] = [
      ...effectiveEntryIds.map((nodeId) => ({
        id: `start-to-${nodeId}`,
        source: '__start__',
        target: nodeId,
        style: { stroke: '#10b981' },
        markerEnd: { type: MarkerType.ArrowClosed, color: '#10b981' },
      })),
      ...rawEdges,
      ...effectiveTerminalIds.map((nodeId) => ({
        id: `${nodeId}-to-end`,
        source: nodeId,
        target: '__end__',
        style: { stroke: '#ef4444' },
        markerEnd: { type: MarkerType.ArrowClosed, color: '#ef4444' },
      })),
    ]

    const laidOut = applyDagreLayout(allNodes, allEdges)
    return { nodes: laidOut, edges: allEdges }
  }, [workflow])

  if (isLoading) return <PageLoader />
  if (error) return <ErrorCard message="Erro ao carregar diagrama." onRetry={refetch} />

  const isEmpty = nodes.length === 0

  return (
    <div className="flex flex-col gap-4" style={{ height: 'calc(100vh - 8rem)' }}>
      {/* Header */}
      <div className="flex items-center gap-3 flex-wrap flex-none">
        <Link to={`/workflows/${id}`}>
          <Button variant="ghost" size="sm">← Editar</Button>
        </Link>
        <Link to="/workflows">
          <Button variant="ghost" size="sm">Workflows</Button>
        </Link>
        <div className="flex-1">
          <div className="flex items-center gap-2">
            <h1 className="text-xl font-bold text-text-primary">
              Diagrama: {workflow?.name ?? id}
            </h1>
            {workflow?.orchestrationMode && (
              <Badge variant="blue">{workflow.orchestrationMode}</Badge>
            )}
          </div>
        </div>
        <Button variant="secondary" size="sm" onClick={() => refetch()}>
          Refresh
        </Button>
      </div>

      {/* Legend */}
      <div className="flex items-center gap-4 flex-wrap flex-none">
        {Object.entries(TYPE_COLORS).map(([type, color]) => (
          <div key={type} className="flex items-center gap-1.5">
            <div className="w-3 h-3 rounded-full" style={{ backgroundColor: color }} />
            <span className="text-xs text-text-muted capitalize">{type}</span>
          </div>
        ))}
        <div className="flex items-center gap-1.5 ml-4">
          <div className="w-6 h-px bg-amber-400" />
          <span className="text-xs text-text-muted">Conditional</span>
        </div>
        <div className="flex items-center gap-1.5">
          <div className="w-6 h-px bg-violet-400" />
          <span className="text-xs text-text-muted">Switch</span>
        </div>
      </div>

      {/* Canvas */}
      <div className="flex-1 bg-bg-secondary border border-border-primary rounded-xl overflow-hidden min-h-0">
        {isEmpty ? (
          <div className="flex flex-col items-center justify-center h-full gap-3">
            <div className="text-4xl text-text-dimmed">◻</div>
            <p className="text-sm text-text-muted">Nenhum agente no workflow.</p>
            <p className="text-xs text-text-dimmed">
              Adicione agentes ao workflow para visualizar o grafo.
            </p>
          </div>
        ) : (
          <ReactFlow
            nodes={nodes}
            edges={edges}
            nodeTypes={nodeTypes}
            fitView
            fitViewOptions={{ padding: 0.25 }}
            minZoom={0.15}
            maxZoom={2}
            proOptions={{ hideAttribution: true }}
          >
            <Background
              variant={BackgroundVariant.Dots}
              gap={20}
              size={1}
              color="#2a2a2a"
            />
            <Controls showInteractive={false} />
          </ReactFlow>
        )}
      </div>

      {/* Stats */}
      <div className="flex items-center gap-6 text-xs text-text-muted flex-none">
        <span>{workflow?.agents?.length ?? 0} agentes</span>
        <span>{workflow?.edges?.length ?? 0} conexões</span>
        <span className="text-text-dimmed">
          Modo: {workflow?.orchestrationMode ?? '—'}
        </span>
      </div>
    </div>
  )
}
