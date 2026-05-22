#!/usr/bin/env python3
"""
Analisa latência do Chat Sales Trader AI a partir do audit log (workflow_event_audit).

Lê as últimas N execuções com Status=Completed pra `deploy-chat-sales-trader-ai`,
extrai eventos com timestamp, computa por execução:
  - T_total: workflow_started → workflow_completed (backend)
  - T_per_node: node_started → node_completed por agente
  - T_llm: soma das durações de agentes (LLM + tools por nó)
  - T_flow: T_total − T_llm
  - TTFT: workflow_started → 1º token (quando há streaming)
  - T_stream: 1º → último token

Agrega em p50/p90/p99 + mostra um trace detalhado da execução mediana.

Uso:
  python3 analyze_chat_latency.py [N=20]
"""
from __future__ import annotations

import json
import statistics
import subprocess
import sys
from datetime import datetime
from typing import Any


WORKFLOW_ID = "deploy-chat-sales-trader-ai"
PG_USER = "efs_ai_hub"
PG_DB = "efs_ai_hub"
PG_CONTAINER = "repositorio-postgres-1"


def psql(sql: str) -> list[list[str]]:
    """Roda SQL via docker exec, retorna rows como lista de listas (-tAF '|')."""
    result = subprocess.run(
        ["docker", "exec", PG_CONTAINER, "psql", "-U", PG_USER, "-d", PG_DB,
         "-tA", "-F", "\x1f", "-c", sql],
        capture_output=True, text=True, check=True,
    )
    rows = [r.split("\x1f") for r in result.stdout.strip().split("\n") if r.strip()]
    return rows


def parse_ts(s: str) -> datetime:
    # Formato Postgres timestamptz: "2026-05-22 02:21:45.060599+00"
    # Python datetime aceita com tweaks.
    return datetime.fromisoformat(s.replace(" ", "T"))


def fmt_ms(ms: float) -> str:
    if ms < 1000:
        return f"{ms:7.1f} ms"
    return f"{ms / 1000:7.2f}  s"


def percentile(values: list[float], p: float) -> float:
    if not values:
        return 0.0
    s = sorted(values)
    k = (len(s) - 1) * (p / 100)
    f = int(k)
    c = min(f + 1, len(s) - 1)
    if f == c:
        return s[f]
    return s[f] + (s[c] - s[f]) * (k - f)


def analyze_execution(exec_id: str) -> dict[str, Any] | None:
    """Extrai métricas de uma execução. Retorna None se eventos insuficientes."""
    rows = psql(
        f"SELECT \"EventType\", \"Timestamp\", \"Payload\" "
        f"FROM aihub.workflow_event_audit "
        f"WHERE \"ExecutionId\" = '{exec_id}' "
        f"ORDER BY \"Timestamp\", \"Id\";"
    )
    if not rows:
        return None

    events = []
    for r in rows:
        if len(r) < 3:
            continue
        evt_type, ts_raw, payload_raw = r[0], r[1], r[2]
        try:
            payload = json.loads(payload_raw)
        except json.JSONDecodeError:
            payload = {}
        # Alguns event payloads são lista (ex.: state_delta JSON Patch — RFC 6902).
        # Tratamos como dict vazio pra extração de nodeId.
        if not isinstance(payload, dict):
            payload = {}
        events.append({"type": evt_type, "ts": parse_ts(ts_raw), "payload": payload})

    started = next((e for e in events if e["type"] == "workflow_started"), None)
    completed = next(
        (e for e in events if e["type"] in ("workflow_completed", "error")), None
    )
    if not started or not completed:
        return None

    total_ms = (completed["ts"] - started["ts"]).total_seconds() * 1000

    # Per-node: node_started + node_completed por nodeId/agentId.
    nodes: dict[str, dict] = {}
    for e in events:
        p = e["payload"] or {}
        nid = p.get("nodeId") or p.get("agentId")
        if not nid:
            continue
        if e["type"] == "node_started":
            nodes.setdefault(nid, {"name": p.get("agentName") or nid})["start"] = e["ts"]
        elif e["type"] == "node_completed":
            nodes.setdefault(nid, {"name": p.get("agentName") or nid})["end"] = e["ts"]

    node_durations: list[tuple[str, float]] = []
    llm_total_ms = 0.0
    for nid, info in nodes.items():
        if "start" in info and "end" in info:
            dur = (info["end"] - info["start"]).total_seconds() * 1000
            llm_total_ms += dur
            node_durations.append((info["name"], dur))

    # Tokens (streaming visibility).
    token_events = [e for e in events if e["type"] == "token"]
    ttft_ms = stream_ms = None
    if token_events:
        ttft_ms = (token_events[0]["ts"] - started["ts"]).total_seconds() * 1000
        if len(token_events) > 1:
            stream_ms = (
                token_events[-1]["ts"] - token_events[0]["ts"]
            ).total_seconds() * 1000

    return {
        "exec_id": exec_id,
        "total_ms": total_ms,
        "llm_ms": llm_total_ms,
        "flow_ms": total_ms - llm_total_ms,
        "ttft_ms": ttft_ms,
        "stream_ms": stream_ms,
        "token_count": len(token_events),
        "node_durations": node_durations,
        "event_count": len(events),
    }


def main(n: int) -> None:
    print(f"\n=== Análise de latência — Chat Sales Trader AI ===")
    print(f"Lendo últimas {n} execuções com Status=Completed\n")

    rows = psql(
        f"SELECT \"ExecutionId\" FROM aihub.workflow_executions "
        f"WHERE \"WorkflowId\" = '{WORKFLOW_ID}' AND \"Status\" = 'Completed' "
        f"ORDER BY \"StartedAt\" DESC LIMIT {n};"
    )
    if not rows:
        print("Nenhuma execução Completed encontrada.")
        return

    analyses = []
    for r in rows:
        result = analyze_execution(r[0])
        if result:
            analyses.append(result)

    if not analyses:
        print("Nenhum trace utilizável (eventos ausentes).")
        return

    print(f"Analisadas: {len(analyses)} execuções\n")

    totals = [a["total_ms"] for a in analyses]
    llms = [a["llm_ms"] for a in analyses]
    flows = [a["flow_ms"] for a in analyses]
    ttfts = [a["ttft_ms"] for a in analyses if a["ttft_ms"] is not None]
    streams = [a["stream_ms"] for a in analyses if a["stream_ms"] is not None]

    def stats(label: str, values: list[float]) -> None:
        if not values:
            print(f"  {label:38} (sem amostras)")
            return
        print(
            f"  {label:38} "
            f"p50={fmt_ms(percentile(values, 50))}  "
            f"p90={fmt_ms(percentile(values, 90))}  "
            f"p99={fmt_ms(percentile(values, 99))}  "
            f"n={len(values)}"
        )

    print("=== Distribuições ===\n")
    stats("Backend total (start→complete)", totals)
    stats("LLM (soma de durações de agente)", llms)
    stats("Fluxo (total − soma de agentes)", flows)
    stats("TTFT (start→1º token)", ttfts)
    stats("Streaming (1º→último token)", streams)

    avg_llm_pct = statistics.mean([a["llm_ms"] / a["total_ms"] * 100 for a in analyses])
    avg_flow_pct = 100 - avg_llm_pct
    print(f"\n  Mix médio: LLM {avg_llm_pct:.1f}%  |  Fluxo {avg_flow_pct:.1f}%")

    # Agregação por agente — média de duração de cada nome de agente
    # que aparece nas execuções, pra saber qual nó domina o tempo.
    per_agent: dict[str, list[float]] = {}
    for a in analyses:
        for name, ms in a["node_durations"]:
            per_agent.setdefault(name, []).append(ms)
    print(f"\n=== Por agente (agregado em {len(analyses)} execuções) ===\n")
    rows_agent = sorted(
        per_agent.items(),
        key=lambda kv: -percentile(kv[1], 50),
    )
    for name, durs in rows_agent:
        print(
            f"  {name[:45]:45} "
            f"p50={fmt_ms(percentile(durs, 50))}  "
            f"p90={fmt_ms(percentile(durs, 90))}  "
            f"n={len(durs)}"
        )

    # Trace detalhado da execução mediana.
    median_idx = sorted(range(len(totals)), key=lambda i: totals[i])[len(totals) // 2]
    trace = analyses[median_idx]
    print(f"\n=== Trace da execução mediana ({trace['exec_id']}) ===\n")
    print(f"  Total      = {fmt_ms(trace['total_ms'])}")
    print(f"  LLM        = {fmt_ms(trace['llm_ms'])}   ({trace['llm_ms']/trace['total_ms']*100:.1f}%)")
    print(f"  Fluxo      = {fmt_ms(trace['flow_ms'])}   ({trace['flow_ms']/trace['total_ms']*100:.1f}%)")
    if trace["ttft_ms"] is not None:
        print(f"  TTFT       = {fmt_ms(trace['ttft_ms'])}")
        print(f"  Streaming  = {fmt_ms(trace['stream_ms'])}  ({trace['token_count']} tokens)")
    print(f"  Eventos    = {trace['event_count']}")
    print(f"\n  Por agente:")
    for name, ms in sorted(trace["node_durations"], key=lambda r: -r[1]):
        pct = ms / trace["total_ms"] * 100
        print(f"    {fmt_ms(ms)}  ({pct:5.1f}%)  {name}")

    # Top 3 e bottom 3 pra ver dispersão.
    print(f"\n=== Extremos (Total) ===")
    sorted_total = sorted(analyses, key=lambda a: a["total_ms"])
    print("\n  Mais rápidos:")
    for a in sorted_total[:3]:
        print(f"    {fmt_ms(a['total_ms'])}  LLM={fmt_ms(a['llm_ms'])}  flow={fmt_ms(a['flow_ms'])}  ({a['exec_id'][:8]}…)")
    print("\n  Mais lentos:")
    for a in sorted_total[-3:]:
        print(f"    {fmt_ms(a['total_ms'])}  LLM={fmt_ms(a['llm_ms'])}  flow={fmt_ms(a['flow_ms'])}  ({a['exec_id'][:8]}…)")
    print()


if __name__ == "__main__":
    n = int(sys.argv[1]) if len(sys.argv) > 1 else 20
    main(n)
