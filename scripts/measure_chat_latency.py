#!/usr/bin/env python3
"""
Mede latência ponta-a-ponta de uma interação no Chat Sales Trader AI.

Fluxo:
  1. Cria conversa nova no workflow `deploy-chat-sales-trader-ai`.
  2. POST /messages com texto do usuário (T0_post → T1_response).
  3. Polling /messages/events com waitMs longo até workflow_completed.
  4. Extrai OccurredAt de cada evento (backend timestamps).
  5. Computa:
     - T_total: workflow_started → workflow_completed (backend)
     - T_perceived: T0_post (client) → workflow_completed (client recv)
     - T_llm: soma de (node_completed - node_started) por agente
     - T_flow: T_total - T_llm
     - Breakdown por nó (agente)

Uso:
  python3 measure_chat_latency.py "Cotação de PETR4 hoje"
"""
from __future__ import annotations

import json
import sys
import time
import urllib.error
import urllib.request
from datetime import datetime, timezone
from typing import Any

BASE_URL = "http://localhost:5189/api/aihub"
WORKFLOW_ID = "deploy-chat-sales-trader-ai"
PROJECT_ID = "sales-trader-ai"

HEADERS = {
    # Em Development, `Admin:AdminPermissions = ["efs.admin"]` (appsettings.Development.json).
    # Qualquer ExternalUserId vira admin se enviar essa permission — IsAdmin
    # bypassa ACL de projeto em ProjectMiddleware.
    "x-efs-user-profile-id": "perf-script",
    "x-efs-permissions": "efs.admin",
    "x-project-id": PROJECT_ID,
    "Content-Type": "application/json",
    "app_origin": "perf-script",
}


def http(method: str, path: str, body: Any = None) -> tuple[int, dict]:
    data = json.dumps(body).encode("utf-8") if body is not None else None
    req = urllib.request.Request(
        f"{BASE_URL}{path}", data=data, method=method, headers=HEADERS
    )
    try:
        with urllib.request.urlopen(req, timeout=60) as resp:
            payload = resp.read().decode("utf-8")
            return resp.status, json.loads(payload) if payload else {}
    except urllib.error.HTTPError as e:
        err_body = e.read().decode("utf-8", errors="replace")
        print(f"HTTP {e.code} on {method} {path}: {err_body}", file=sys.stderr)
        raise


def parse_iso(ts: str) -> datetime:
    # Aceita formato ISO com fração de segundos. `fromisoformat` em 3.11+
    # cobre o sufixo Z; antes precisa swap pra "+00:00".
    if ts.endswith("Z"):
        ts = ts[:-1] + "+00:00"
    return datetime.fromisoformat(ts).astimezone(timezone.utc)


def fmt_ms(ms: float) -> str:
    if ms < 1000:
        return f"{ms:6.1f} ms"
    return f"{ms / 1000:6.2f}  s"


def main(text: str) -> None:
    print(f"\n=== Chat Sales Trader AI — medição de latência ===")
    print(f"Mensagem: {text!r}\n")

    # 1. Cria conversa.
    print("→ Criando conversa…")
    t_create = time.perf_counter()
    _, conv = http("POST", "/conversations", {"workflowId": WORKFLOW_ID, "metadata": {}})
    conv_id = conv["conversationId"]
    print(f"  conversationId = {conv_id}")
    print(f"  setup           = {fmt_ms((time.perf_counter() - t_create) * 1000)}\n")

    # 2. POST message. Captura wall-clock antes do request e depois da resposta
    # — diferença mede ack do backend (não a execução, que continua async).
    print("→ Enviando mensagem…")
    t_post_client = time.perf_counter()
    _, send_resp = http(
        "POST",
        f"/conversations/{conv_id}/messages",
        [{"role": "user", "message": text}],
    )
    t_ack_client = time.perf_counter()
    execution_id = send_resp["executionId"]
    print(f"  executionId     = {execution_id}")
    print(f"  POST ack        = {fmt_ms((t_ack_client - t_post_client) * 1000)}\n")

    # 3. Polling de eventos. waitMs=10000 reduz chattiness; loop até terminal.
    events: list[dict] = []
    cursor = 0
    print("→ Aguardando eventos…")
    while True:
        _, page = http(
            "GET",
            f"/conversations/{conv_id}/messages/events?since={cursor}&waitMs=10000&limit=500",
        )
        items = page.get("items", [])
        if items:
            events.extend(items)
            cursor = items[-1]["seq"]
        if page.get("terminal", False):
            break
        if not items:
            # Sem novos eventos no waitMs — workflow ainda corre. Loop again.
            continue
    t_terminal_client = time.perf_counter()
    print(f"  recebidos       = {len(events)} evento(s)\n")

    # 4. Extrai timestamps backend.
    def ts(ev: dict) -> datetime:
        return parse_iso(ev["occurredAt"])

    started = next((e for e in events if e["type"] == "workflow_started"), None)
    completed = next(
        (e for e in events if e["type"] in ("workflow_completed", "error")), None
    )
    if started is None or completed is None:
        print("ERRO: workflow_started ou workflow_completed ausente nos eventos.")
        return

    t0_backend = ts(started)
    t_end_backend = ts(completed)
    total_backend_ms = (t_end_backend - t0_backend).total_seconds() * 1000

    # 5. Per-node duration (LLM proxy).
    nodes: dict[str, dict] = {}
    for ev in events:
        payload = ev.get("payload") or {}
        node_id = payload.get("nodeId") or payload.get("agentId")
        if not node_id:
            continue
        if ev["type"] == "node_started":
            nodes.setdefault(node_id, {"name": payload.get("agentName") or node_id})
            nodes[node_id]["started"] = ts(ev)
        elif ev["type"] == "node_completed":
            nodes.setdefault(node_id, {"name": payload.get("agentName") or node_id})
            nodes[node_id]["completed"] = ts(ev)

    # 6. Token events (streaming visibility).
    token_events = [e for e in events if e["type"] == "token"]
    first_token = ts(token_events[0]) if token_events else None
    last_token = ts(token_events[-1]) if token_events else None

    # 7. Computa.
    llm_total_ms = 0.0
    node_rows: list[tuple[str, float]] = []
    for nid, info in nodes.items():
        s = info.get("started")
        c = info.get("completed")
        if s is None or c is None:
            continue
        dur_ms = (c - s).total_seconds() * 1000
        llm_total_ms += dur_ms
        node_rows.append((info.get("name", nid), dur_ms))

    flow_ms = total_backend_ms - llm_total_ms
    perceived_ms = (t_terminal_client - t_post_client) * 1000

    # 8. Relatório.
    print("=== Resultados ===\n")
    print(f"Percebido pelo usuário (T_post → T_terminal client)")
    print(f"  {fmt_ms(perceived_ms)}")
    print()
    print(f"Backend total (workflow_started → workflow_completed)")
    print(f"  {fmt_ms(total_backend_ms)}")
    print()
    print(f"LLM (soma das durações de agente, ~LLM + tools)")
    print(f"  {fmt_ms(llm_total_ms)}    ({llm_total_ms / total_backend_ms * 100:.1f}% do backend)")
    print()
    print(f"Fluxo/orquestração (backend total − soma de agentes)")
    print(f"  {fmt_ms(flow_ms)}    ({flow_ms / total_backend_ms * 100:.1f}% do backend)")
    print()
    if first_token and last_token:
        ttft_ms = (first_token - t0_backend).total_seconds() * 1000
        stream_ms = (last_token - first_token).total_seconds() * 1000
        print(f"Time-to-first-token (workflow_started → 1º token)")
        print(f"  {fmt_ms(ttft_ms)}")
        print()
        print(f"Streaming window (1º → último token)")
        print(f"  {fmt_ms(stream_ms)}    ({len(token_events)} tokens)")
        print()
    print("=== Por agente ===")
    for name, ms in sorted(node_rows, key=lambda r: -r[1]):
        print(f"  {fmt_ms(ms)}   {name}")
    print()
    print(f"  conversationId={conv_id}")
    print(f"  executionId={execution_id}")


if __name__ == "__main__":
    msg = " ".join(sys.argv[1:]) if len(sys.argv) > 1 else "Cotação de PETR4 hoje"
    main(msg)
