import '@xyflow/react/dist/style.css'

import { useMemo, useCallback, useState } from 'react'
import {
  ReactFlow,
  ReactFlowProvider,
  Background,
  BackgroundVariant,
  Controls,
  MiniMap,
  MarkerType,
  BaseEdge,
  Handle,
  Position,
  getSmoothStepPath,
  EdgeLabelRenderer,
  type Node,
  type Edge,
  type NodeProps,
  type EdgeProps,
  type NodeMouseHandler,
} from '@xyflow/react'
import dagre from '@dagrejs/dagre'
import type { WorkflowDef, NodeRecord } from '../types'

// ── Props ─────────────────────────────────────────────────────────────────────

interface Props {
  workflow: WorkflowDef
  nodeStates: Record<string, NodeRecord>
  onNodeClick: (nodeId: string, nodeType: 'agent' | 'executor') => void
}

// ── Palette / Theme ───────────────────────────────────────────────────────────

const STATUS: Record<string, { color: string; bg: string; label: string }> = {
  running:   { color: '#f59e0b', bg: '#422006', label: 'Running' },
  completed: { color: '#10b981', bg: '#052e16', label: 'Completed' },
  failed:    { color: '#ef4444', bg: '#450a0a', label: 'Failed' },
  pending:   { color: '#4A6B8A', bg: '#0C1D38', label: 'Pending' },
}

const S_COLOR: Record<string, string> = {
  running:   '#f59e0b',
  completed: '#10b981',
  failed:    '#ef4444',
  pending:   '#4A6B8A',
}

const NODE_THEME = {
  agent:    {
    accent: '#0057E0',
    bg: '#04091A',
    icon: 'M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm0 3c1.66 0 3 1.34 3 3s-1.34 3-3 3-3-1.34-3-3 1.34-3 3-3zm0 14.2a7.2 7.2 0 01-6-3.22c.03-1.99 4-3.08 6-3.08 1.99 0 5.97 1.09 6 3.08a7.2 7.2 0 01-6 3.22z',
    label: 'Agent',
  },
  executor: {
    accent: '#10b981',
    bg: '#022c22',
    icon: 'M8 5v14l11-7z',
    label: 'Executor',
  },
} as const

type NodeKind = keyof typeof NODE_THEME

// ── Layout constants ──────────────────────────────────────────────────────────

// Graph engine (DFS / Kahn)
const GRAPH_NODE_W = 280
const GRAPH_NODE_H = 180

// Dagre engine
const DAGRE_NODE_W = 240
const DAGRE_NODE_H = 100
const VIRTUAL_SIZE = 30

// ── Helpers ───────────────────────────────────────────────────────────────────

function humanize(id: string): string {
  return id.replace(/[-_]/g, ' ').replace(/\b\w/g, c => c.toUpperCase())
}

function shortDuration(ms: number): string {
  if (ms < 1000) return `${Math.round(ms)}ms`
  return `${(ms / 1000).toFixed(1)}s`
}

// ── Custom Edge: pill label, dashed for back-edges ────────────────────────────

function LabelEdge(props: EdgeProps) {
  const { sourceX, sourceY, targetX, targetY, sourcePosition, targetPosition, data, style, markerEnd } = props
  const [path, labelX, labelY] = getSmoothStepPath({
    sourceX, sourceY, targetX, targetY,
    sourcePosition, targetPosition,
    borderRadius: 16,
  })
  const label = data?.label as string | undefined
  const isBack = data?.isBack as boolean | undefined

  return (
    <>
      <BaseEdge path={path} style={style} markerEnd={markerEnd} />
      {label && (
        <EdgeLabelRenderer>
          <div
            style={{
              position: 'absolute',
              transform: `translate(-50%, -50%) translate(${labelX}px,${labelY}px)`,
              pointerEvents: 'all',
            }}
            className="nodrag nopan"
          >
            <span
              style={{
                display: 'inline-flex', alignItems: 'center', gap: 4,
                padding: '2px 8px', borderRadius: 20,
                fontSize: 10, fontWeight: 600, letterSpacing: 0.3,
                background: isBack ? '#0046B8' : '#1A3357',
                color: isBack ? '#B8CEE5' : '#DCE8F5',
                border: `1px solid ${isBack ? '#0057E0' : '#4A6B8A'}`,
                whiteSpace: 'nowrap',
              }}
            >
              {isBack && (
                <svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5">
                  <path d="M1 4v6h6" /><path d="M3.51 15a9 9 0 1 0 2.13-9.36L1 10" />
                </svg>
              )}
              {label}
            </span>
          </div>
        </EdgeLabelRenderer>
      )}
    </>
  )
}

// ── Custom Node: Agent / Executor card (rich) ─────────────────────────────────

interface AgentCardData {
  nodeId: string
  kind: NodeKind
  status: string
  output?: string
  startedAt?: string
  completedAt?: string
  description?: string
  functionName?: string
  tokensUsed?: number
  [key: string]: unknown
}

function AgentCardNode({ data }: NodeProps) {
  const d = data as AgentCardData
  const [expanded, setExpanded] = useState(false)
  const s = STATUS[d.status] ?? STATUS.pending
  const t = NODE_THEME[d.kind]

  const durationMs = d.startedAt && d.completedAt
    ? new Date(d.completedAt).getTime() - new Date(d.startedAt).getTime()
    : null

  return (
    <div
      style={{
        background: `linear-gradient(135deg, ${t.bg}, ${t.bg}dd)`,
        border: `1.5px solid ${s.color}40`,
        borderRadius: 14,
        padding: '12px 14px',
        width: DAGRE_NODE_W,
        minHeight: 72,
        boxShadow: d.status === 'running'
          ? `0 0 20px ${s.color}20, 0 4px 12px rgba(0,0,0,0.4)`
          : '0 2px 8px rgba(0,0,0,0.3)',
        transition: 'box-shadow 0.3s ease, border-color 0.3s ease',
        cursor: d.kind === 'agent' ? 'pointer' : 'default',
        boxSizing: 'border-box',
        position: 'relative',
      }}
    >
      <Handle type="target" position={Position.Top} style={{ opacity: 0 }} />
      <Handle type="source" position={Position.Bottom} style={{ opacity: 0 }} />
      {/* Header: type badge + status */}
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 8 }}>
        <div style={{
          display: 'inline-flex', alignItems: 'center', gap: 5,
          padding: '3px 8px', borderRadius: 6,
          background: `${t.accent}20`,
          border: `1px solid ${t.accent}30`,
        }}>
          <svg width="12" height="12" viewBox="0 0 24 24" fill={t.accent}>
            <path d={t.icon} />
          </svg>
          <span style={{ fontSize: 10, fontWeight: 700, color: t.accent, textTransform: 'uppercase', letterSpacing: 0.5 }}>
            {t.label}
          </span>
        </div>
        <StatusBadge status={d.status} color={s.color} bg={s.bg} label={s.label} />
      </div>

      {/* Name */}
      <div style={{
        fontWeight: 700, fontSize: 13, color: '#DCE8F5',
        letterSpacing: -0.3, lineHeight: 1.3,
        marginBottom: 2,
      }}>
        {humanize(d.nodeId)}
      </div>

      {/* Subtitle */}
      {(d.description || d.functionName) && (
        <div style={{
          fontSize: 11, color: '#7596B8', lineHeight: 1.3,
          marginBottom: 6,
          overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap',
        }}>
          {d.description || `fn: ${d.functionName}`}
        </div>
      )}

      {/* Metrics row */}
      {(durationMs !== null || d.tokensUsed) && (
        <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginTop: 4, marginBottom: d.output ? 6 : 0 }}>
          {durationMs !== null && (
            <MetricPill
              icon="M12 8v4l3 3m6-3a9 9 0 11-18 0 9 9 0 0118 0z"
              value={shortDuration(durationMs)}
            />
          )}
          {d.tokensUsed != null && d.tokensUsed > 0 && (
            <MetricPill
              icon="M7 7h.01M7 3h5c.512 0 1.024.195 1.414.586l7 7a2 2 0 010 2.828l-7 7a2 2 0 01-2.828 0l-7-7A1.994 1.994 0 013 12V7a4 4 0 014-4z"
              value={d.tokensUsed > 999 ? `${(d.tokensUsed / 1000).toFixed(1)}k` : `${d.tokensUsed}`}
            />
          )}
        </div>
      )}

      {/* Output preview */}
      {d.output && (
        <div
          onClick={(e) => { e.stopPropagation(); setExpanded(!expanded) }}
          style={{
            marginTop: 6, fontSize: 11, color: '#B8CEE5', lineHeight: 1.4,
            background: '#04091A80', borderRadius: 8, padding: '6px 8px',
            maxHeight: expanded ? 200 : 48, overflow: 'hidden',
            fontFamily: 'ui-monospace, monospace', whiteSpace: 'pre-wrap',
            borderLeft: `2px solid ${s.color}40`,
            cursor: 'pointer',
            transition: 'max-height 0.2s ease',
          }}
        >
          {expanded ? d.output.slice(0, 500) : d.output.slice(0, 100)}
          {d.output.length > (expanded ? 500 : 100) ? '...' : ''}
        </div>
      )}
    </div>
  )
}

// ── Custom Node: Virtual START / END ──────────────────────────────────────────

interface VirtualNodeData {
  virtual: 'start' | 'end'
  [key: string]: unknown
}

function VirtualNode({ data }: NodeProps) {
  const d = data as VirtualNodeData
  const isStart = d.virtual === 'start'
  return (
    <div style={{
      width: VIRTUAL_SIZE,
      height: VIRTUAL_SIZE,
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'center',
      background: '#04091A',
      border: '1.5px solid #0057E0',
      borderRadius: isStart ? '50%' : 3,
      boxSizing: 'border-box',
      position: 'relative',
    }}>
      {isStart
        ? <Handle type="source" position={Position.Bottom} style={{ opacity: 0 }} />
        : <Handle type="target" position={Position.Top} style={{ opacity: 0 }} />
      }
      <span style={{ fontSize: 5, fontWeight: 800, color: '#4D8EF5', letterSpacing: 0.5 }}>
        {isStart ? 'START' : 'END'}
      </span>
    </div>
  )
}

// ── Node types registry ───────────────────────────────────────────────────────

const nodeTypes = {
  agentCard: AgentCardNode,
  executorCard: AgentCardNode, // same component, accent/icon differ via data.kind
  virtual: VirtualNode,
}

const edgeTypes = { label: LabelEdge }

// ── Sub-components ────────────────────────────────────────────────────────────

function StatusBadge({ status, color, bg, label }: { status: string; color: string; bg: string; label: string }) {
  return (
    <div style={{
      display: 'inline-flex', alignItems: 'center', gap: 5,
      padding: '3px 8px', borderRadius: 20,
      background: bg, border: `1px solid ${color}30`,
    }}>
      <span style={{
        width: 6, height: 6, borderRadius: '50%',
        background: color, display: 'inline-block',
        boxShadow: status === 'running' ? `0 0 8px ${color}` : 'none',
        animation: status === 'running' ? 'pulse-dot 1.5s ease-in-out infinite' : 'none',
      }} />
      <span style={{ fontSize: 10, fontWeight: 600, color, letterSpacing: 0.3 }}>
        {label}
      </span>
      <style>{`@keyframes pulse-dot { 0%,100% { opacity: 1; } 50% { opacity: 0.4; } }`}</style>
    </div>
  )
}

function MetricPill({ icon, value }: { icon: string; value: string }) {
  return (
    <div style={{
      display: 'inline-flex', alignItems: 'center', gap: 4,
      padding: '2px 6px', borderRadius: 6,
      background: '#0C1D38', border: '1px solid #1A3357',
    }}>
      <svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="#7596B8" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
        <path d={icon} />
      </svg>
      <span style={{ fontSize: 10, fontWeight: 600, color: '#7596B8' }}>{value}</span>
    </div>
  )
}

// ── Graph layout engine (DFS back-edges + Kahn levels) ────────────────────────

interface GraphLayoutResult {
  allNodeIds: string[]
  positions: Record<string, { x: number; y: number }>
  backEdges: Set<string>
}

function computeGraphLayout(workflow: WorkflowDef): GraphLayoutResult {
  const agentIds = new Set(workflow.agents?.map(a => a.agentId) ?? [])
  const explicitExecutorIds = workflow.executors?.map(e => e.id) ?? []
  const edgeNodeIds = new Set<string>()
  for (const e of workflow.edges ?? []) {
    if (e.from) edgeNodeIds.add(e.from)
    if (e.to) edgeNodeIds.add(e.to)
    for (const t of e.targets ?? []) edgeNodeIds.add(t)
    for (const c of e.cases ?? []) for (const t of c.targets ?? []) edgeNodeIds.add(t)
  }
  const allNodeIds = [
    ...agentIds,
    ...explicitExecutorIds,
    ...[...edgeNodeIds].filter(id => !agentIds.has(id) && !explicitExecutorIds.includes(id)),
  ]

  // Adjacency
  const succs: Record<string, string[]> = {}
  for (const id of allNodeIds) succs[id] = []
  const allEdgePairs: [string, string][] = []
  for (const e of workflow.edges ?? []) {
    if (!e.from) continue
    const targets: string[] = e.edgeType === 'Switch'
      ? (e.cases ?? []).flatMap(c => c.targets ?? [])
      : e.targets?.length ? e.targets : e.to ? [e.to] : []
    for (const t of targets) {
      if (!succs[e.from]) succs[e.from] = []
      if (!succs[e.from].includes(t)) {
        succs[e.from].push(t)
        allEdgePairs.push([e.from, t])
      }
    }
  }

  // DFS to find back-edges
  const backEdges = new Set<string>()
  const WHITE = 0, GRAY = 1, BLACK = 2
  const color: Record<string, number> = {}
  for (const id of allNodeIds) color[id] = WHITE

  function dfs(u: string) {
    color[u] = GRAY
    for (const v of succs[u] ?? []) {
      if (color[v] === GRAY) {
        backEdges.add(`${u}->${v}`)
      } else if (color[v] === WHITE) {
        dfs(v)
      }
    }
    color[u] = BLACK
  }
  for (const id of allNodeIds) {
    if (color[id] === WHITE) dfs(id)
  }

  // Kahn's algorithm for level assignment
  const inDegree: Record<string, number> = {}
  for (const id of allNodeIds) inDegree[id] = 0
  for (const [from, to] of allEdgePairs) {
    if (!backEdges.has(`${from}->${to}`)) {
      inDegree[to] = (inDegree[to] ?? 0) + 1
    }
  }

  const level: Record<string, number> = {}
  const queue = allNodeIds.filter(id => !inDegree[id])
  for (const id of queue) level[id] = 0

  while (queue.length) {
    const cur = queue.shift()!
    for (const s of succs[cur] ?? []) {
      if (backEdges.has(`${cur}->${s}`)) continue
      level[s] = Math.max(level[s] ?? 0, (level[cur] ?? 0) + 1)
      inDegree[s]--
      if (inDegree[s] === 0) queue.push(s)
    }
  }
  for (const id of allNodeIds) {
    if (level[id] === undefined) level[id] = 0
  }

  // Group by level and compute positions
  const byLevel: Record<number, string[]> = {}
  for (const id of allNodeIds) {
    const l = level[id] ?? 0
    if (!byLevel[l]) byLevel[l] = []
    byLevel[l].push(id)
  }

  const positions: Record<string, { x: number; y: number }> = {}
  for (const [l, ids] of Object.entries(byLevel)) {
    const lNum = parseInt(l)
    const totalWidth = ids.length * GRAPH_NODE_W
    ids.forEach((id, i) => {
      positions[id] = {
        x: i * GRAPH_NODE_W - totalWidth / 2 + GRAPH_NODE_W / 2,
        y: lNum * GRAPH_NODE_H,
      }
    })
  }

  return { allNodeIds, positions, backEdges }
}

// ── Dagre layout engine ───────────────────────────────────────────────────────

function applyDagreLayout(nodes: Node[], edges: Edge[], rankdir: 'LR' | 'TB'): Node[] {
  const g = new dagre.graphlib.Graph()
  g.setDefaultEdgeLabel(() => ({}))
  g.setGraph({ rankdir, nodesep: 50, ranksep: 80 })

  nodes.forEach(n => {
    const w = n.type === 'virtual' ? VIRTUAL_SIZE : DAGRE_NODE_W
    const h = n.type === 'virtual' ? VIRTUAL_SIZE : DAGRE_NODE_H
    g.setNode(n.id, { width: w, height: h })
  })
  edges.forEach(e => g.setEdge(e.source, e.target))
  dagre.layout(g)

  return nodes.map(n => {
    const pos = g.node(n.id)
    const w = n.type === 'virtual' ? VIRTUAL_SIZE : DAGRE_NODE_W
    const h = n.type === 'virtual' ? VIRTUAL_SIZE : DAGRE_NODE_H
    return { ...n, position: { x: pos.x - w / 2, y: pos.y - h / 2 } }
  })
}

// ── Build graph using graph-engine (Graph mode / edges present) ───────────────

function buildGraphEngineFlow(
  workflow: WorkflowDef,
  nodeStates: Record<string, NodeRecord>,
  agentIds: Set<string>,
  executorMap: Map<string, { functionName: string; description?: string }>,
): { nodes: Node[]; edges: Edge[] } {
  const { allNodeIds, positions, backEdges } = computeGraphLayout(workflow)

  const nodes: Node[] = []
  let eid = 0

  const makeEdge = (from: string, to: string, label?: string): Edge => {
    const isBack = backEdges.has(`${from}->${to}`)
    const isRunning = nodeStates[from]?.status === 'running'
    return {
      id: `e${eid++}`,
      source: from,
      target: to,
      type: 'label',
      data: { label, isBack },
      animated: isRunning,
      style: {
        stroke: isBack ? '#4D8EF5' : isRunning ? '#f59e0b' : '#4A6B8A',
        strokeDasharray: isBack ? '6 3' : undefined,
        strokeWidth: isBack ? 1.5 : 2,
        transition: 'stroke 0.3s ease',
      },
      markerEnd: {
        type: MarkerType.ArrowClosed,
        color: isBack ? '#4D8EF5' : isRunning ? '#f59e0b' : '#4A6B8A',
        width: 16,
        height: 16,
      },
    }
  }

  // Find roots and sinks for START/END placement
  const rootIds = allNodeIds.filter(id => {
    return !(workflow.edges ?? []).some(e => {
      if (e.edgeType === 'Switch') return (e.cases ?? []).some(c => (c.targets ?? []).includes(id))
      return e.to === id || (e.targets ?? []).includes(id)
    })
  })
  const rootId = rootIds[0] ?? allNodeIds[0]
  const firstLevel = allNodeIds.length ? Math.min(...Object.values(positions).map(p => p.y)) : 0
  const lastLevel = allNodeIds.length ? Math.max(...Object.values(positions).map(p => p.y)) : 0

  const rootX = (positions[rootId]?.x ?? 0) + DAGRE_NODE_W / 2 - VIRTUAL_SIZE / 2
  nodes.push({
    id: '__start__',
    type: 'virtual',
    position: { x: rootX, y: firstLevel - 100 },
    data: { virtual: 'start' } satisfies VirtualNodeData,
  })

  // Real nodes
  for (let i = 0; i < allNodeIds.length; i++) {
    const id = allNodeIds[i]
    const state = nodeStates[id]
    const status = state?.status ?? 'pending'
    const kind: NodeKind = agentIds.has(id) ? 'agent' : 'executor'
    const pos = positions[id] ?? { x: i * GRAPH_NODE_W, y: 200 }
    const exec = executorMap.get(id)

    nodes.push({
      id,
      type: kind === 'agent' ? 'agentCard' : 'executorCard',
      position: pos,
      data: {
        nodeId: id,
        kind,
        status,
        output: state?.output,
        startedAt: state?.startedAt,
        completedAt: state?.completedAt,
        description: exec?.description,
        functionName: exec?.functionName,
        tokensUsed: state?.tokensUsed,
      } satisfies AgentCardData,
    })
  }

  const sinkIds = allNodeIds.filter(id => !(workflow.edges ?? []).some(e => e.from === id))
  const sinkId = sinkIds[0] ?? allNodeIds[allNodeIds.length - 1]
  const sinkX = (positions[sinkId]?.x ?? 0) + DAGRE_NODE_W / 2 - VIRTUAL_SIZE / 2
  nodes.push({
    id: '__end__',
    type: 'virtual',
    position: { x: sinkX, y: lastLevel + GRAPH_NODE_H + 20 },
    data: { virtual: 'end' } satisfies VirtualNodeData,
  })

  // Build edges
  const edges: Edge[] = []

  // Start → roots
  const rootsForEdges = allNodeIds.filter(id => {
    const inEdges = (workflow.edges ?? []).some(e => {
      if (e.edgeType === 'Switch') return (e.cases ?? []).some(c => (c.targets ?? []).includes(id))
      return e.to === id || (e.targets ?? []).includes(id)
    })
    return !inEdges
  })
  if (rootsForEdges.length === 0 && allNodeIds.length > 0) rootsForEdges.push(allNodeIds[0])
  for (const r of rootsForEdges) edges.push(makeEdge('__start__', r))

  for (const e of workflow.edges ?? []) {
    if (e.edgeType === 'Switch' && e.cases) {
      for (const c of e.cases) {
        for (const t of c.targets ?? []) {
          edges.push(makeEdge(e.from ?? '', t, c.isDefault ? 'default' : (c.condition ?? '')))
        }
      }
    } else {
      const targets = e.targets?.length ? e.targets : e.to ? [e.to] : []
      for (const t of targets) {
        const label = e.condition ?? (e.edgeType !== 'Direct' ? e.edgeType : undefined)
        edges.push(makeEdge(e.from ?? '', t, label))
      }
    }
  }

  // Sinks → End
  const sinksForEdges = allNodeIds.filter(id => !(workflow.edges ?? []).some(e => e.from === id))
  for (const s of sinksForEdges) edges.push(makeEdge(s, '__end__'))

  return { nodes, edges }
}

// ── Build graph using dagre engine (implicit topology modes) ──────────────────

function buildDagreFlow(
  workflow: WorkflowDef,
  nodeStates: Record<string, NodeRecord>,
  agentIds: Set<string>,
  executorMap: Map<string, { functionName: string; description?: string }>,
): { nodes: Node[]; edges: Edge[] } {
  const mode = workflow.orchestrationMode
  const agentIdList = workflow.agents.map(a => a.agentId)

  let rankdir: 'LR' | 'TB' = 'LR'
  if (mode === 'Handoff' || mode === 'GroupChat' || (!['Sequential', 'Concurrent'].includes(mode))) {
    rankdir = 'TB'
  }

  function makeNode(id: string): Node {
    const state = nodeStates[id]
    const status = state?.status ?? 'pending'
    const kind: NodeKind = agentIds.has(id) ? 'agent' : 'executor'
    const exec = executorMap.get(id)
    return {
      id,
      type: kind === 'agent' ? 'agentCard' : 'executorCard',
      position: { x: 0, y: 0 },
      data: {
        nodeId: id,
        kind,
        status,
        output: state?.output,
        startedAt: state?.startedAt,
        completedAt: state?.completedAt,
        description: exec?.description,
        functionName: exec?.functionName,
        tokensUsed: state?.tokensUsed,
      } satisfies AgentCardData,
    }
  }

  const startNode: Node = {
    id: '__start__',
    type: 'virtual',
    position: { x: 0, y: 0 },
    data: { virtual: 'start' } satisfies VirtualNodeData,
  }
  const endNode: Node = {
    id: '__end__',
    type: 'virtual',
    position: { x: 0, y: 0 },
    data: { virtual: 'end' } satisfies VirtualNodeData,
  }

  let eid = 0
  function makeEdge(source: string, target: string, label?: string): Edge {
    const isRunning = nodeStates[source]?.status === 'running'
    const col = isRunning ? '#f59e0b' : '#4A6B8A'
    return {
      id: `e${eid++}`,
      source,
      target,
      type: 'smoothstep',
      data: { label, isBack: false },
      animated: isRunning,
      style: { stroke: col, strokeWidth: 1.5 },
      markerEnd: { type: MarkerType.ArrowClosed, color: col, width: 14, height: 14 },
    }
  }

  const agentNodes = agentIdList.map(makeNode)
  const rfEdges: Edge[] = []

  if (mode === 'Sequential') {
    rfEdges.push(makeEdge('__start__', agentIdList[0]))
    agentIdList.slice(0, -1).forEach((id, i) => rfEdges.push(makeEdge(id, agentIdList[i + 1])))
    if (agentIdList.length > 0) rfEdges.push(makeEdge(agentIdList[agentIdList.length - 1], '__end__'))
  } else if (mode === 'Concurrent') {
    agentIdList.forEach(id => {
      rfEdges.push(makeEdge('__start__', id))
      rfEdges.push(makeEdge(id, '__end__'))
    })
  } else if (mode === 'Handoff') {
    const entry = agentIdList[0]
    const specialists = agentIdList.slice(1)
    rfEdges.push(makeEdge('__start__', entry))
    specialists.forEach(id => rfEdges.push(makeEdge(entry, id)))
    const exitNodes = specialists.length > 0 ? specialists : [entry]
    exitNodes.forEach(id => rfEdges.push(makeEdge(id, '__end__')))
  } else if (mode === 'GroupChat') {
    const managerRef = workflow.agents.find(a => a.role === 'manager')
    const managerId = managerRef ? managerRef.agentId : agentIdList[0]
    const others = agentIdList.filter(id => id !== managerId)
    rfEdges.push(makeEdge('__start__', managerId))
    others.forEach((id, i) => {
      rfEdges.push(makeEdge(managerId, id))
      rfEdges.push(makeEdge(id, managerId))
      void i
    })
    rfEdges.push(makeEdge(managerId, '__end__'))
  } else {
    // Fallback: sequential chain
    rfEdges.push(makeEdge('__start__', agentIdList[0]))
    agentIdList.slice(0, -1).forEach((id, i) => rfEdges.push(makeEdge(id, agentIdList[i + 1])))
    if (agentIdList.length > 0) rfEdges.push(makeEdge(agentIdList[agentIdList.length - 1], '__end__'))
  }

  const rawNodes = [startNode, ...agentNodes, endNode]
  const positioned = applyDagreLayout(rawNodes, rfEdges, rankdir)
  return { nodes: positioned, edges: rfEdges }
}

// ── MiniMap node color helper ─────────────────────────────────────────────────

function miniMapColor(node: Node): string {
  if (node.type === 'virtual') return '#0057E0'
  const d = node.data as AgentCardData
  return S_COLOR[d.status ?? 'pending'] ?? S_COLOR.pending
}

// ── Inner component (must be inside ReactFlowProvider) ────────────────────────

function WorkflowCanvasInner({ workflow, nodeStates, onNodeClick }: Props) {
  const agentIds = useMemo(() => new Set(workflow.agents?.map(a => a.agentId) ?? []), [workflow])
  const executorMap = useMemo(() => {
    const m = new Map<string, { functionName: string; description?: string }>()
    for (const e of workflow.executors ?? []) m.set(e.id, { functionName: e.functionName, description: e.description })
    return m
  }, [workflow])

  // Determine which engine to use
  const useGraphEngine = useMemo(() => {
    return workflow.orchestrationMode === 'Graph' || (workflow.edges ?? []).length > 0
  }, [workflow])

  const { nodes, edges } = useMemo(() => {
    if (useGraphEngine) {
      return buildGraphEngineFlow(workflow, nodeStates, agentIds, executorMap)
    } else {
      return buildDagreFlow(workflow, nodeStates, agentIds, executorMap)
    }
  }, [workflow, nodeStates, agentIds, executorMap, useGraphEngine])

  const handleNodeClick: NodeMouseHandler = useCallback((_event, node) => {
    if (node.id === '__start__' || node.id === '__end__') return
    const nodeType = agentIds.has(node.id) ? 'agent' as const : 'executor' as const
    onNodeClick(node.id, nodeType)
  }, [agentIds, onNodeClick])

  return (
    <div style={{ width: '100%', height: '100%', background: '#04091A' }}>
      <ReactFlow
        nodes={nodes}
        edges={edges}
        nodeTypes={nodeTypes}
        edgeTypes={edgeTypes}
        onNodeClick={handleNodeClick}
        fitView
        fitViewOptions={{ padding: 0.2 }}
        minZoom={0.2}
        maxZoom={1.5}
        colorMode="dark"
        nodesDraggable={false}
        nodesConnectable={false}
        zoomOnDoubleClick={false}
        proOptions={{ hideAttribution: true }}
      >
        <Background
          variant={BackgroundVariant.Dots}
          color="#0C1D38"
          gap={20}
          size={1}
          style={{ background: '#04091A' }}
        />
        <MiniMap
          style={{ background: '#04091A' }}
          nodeColor={miniMapColor}
          maskColor="rgba(4, 9, 26, 0.7)"
        />
        <Controls
          showInteractive={false}
          style={{
            background: '#0C1D38',
            border: '1px solid #1A3357',
            borderRadius: 8,
            overflow: 'hidden',
          }}
        />
      </ReactFlow>
    </div>
  )
}

// ── Main export ───────────────────────────────────────────────────────────────

export function WorkflowCanvas({ workflow, nodeStates, onNodeClick }: Props) {
  return (
    <ReactFlowProvider>
      <WorkflowCanvasInner
        workflow={workflow}
        nodeStates={nodeStates}
        onNodeClick={onNodeClick}
      />
    </ReactFlowProvider>
  )
}
