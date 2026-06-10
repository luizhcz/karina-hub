#!/usr/bin/env python3
"""Testa se o OUTPUT estruturado (sub-objeto `output`) de um agente conversacional
é inserido no shared state. Usa o deploy-chat-sales-trader-ai:
  Turno 1: pede cotação → cv-trader-cotacao produz output.tickers → inserido no state.
  Turno 2: pede ordem    → cv-trader-ordem lê o draft de cotação no <shared_state>.
Captura o conversationId via RUN_STARTED pra manter a mesma conversa.
"""
from __future__ import annotations
import json, sys
import urllib.request, urllib.error

BASE = "http://localhost:5189/api/aihub"
WORKFLOW_ID = "deploy-chat-sales-trader-ai"

HEADERS = {
    "x-efs-user-profile-id": "perf-script",
    "x-efs-permissions": "efs.admin",
    "x-project-id": "sales-trader-ai",
    "app_origin": "e2e-script",
    "x-workflow-id": WORKFLOW_ID,
    "Accept": "text/event-stream",
    "Content-Type": "application/json",
}


def turn(label: str, text: str, thread_id: str | None) -> str | None:
    print(f"\n=== {label}: '{text}'  (threadId={thread_id}) ===")
    payload = {"messages": [{"role": "user", "content": text}]}
    if thread_id:
        payload["threadId"] = thread_id
    req = urllib.request.Request(f"{BASE}/chat/ag-ui/stream",
                                 data=json.dumps(payload).encode(),
                                 method="POST", headers=HEADERS)
    server_thread = thread_id
    try:
        with urllib.request.urlopen(req, timeout=120) as resp:
            for raw in resp:
                line = raw.decode("utf-8", errors="replace").rstrip("\n")
                if not line.startswith("data:"):
                    continue
                p = line[len("data:"):].strip()
                if not p:
                    continue
                try:
                    evt = json.loads(p)
                except json.JSONDecodeError:
                    continue
                t = evt.get("type", "?")
                if t == "RUN_STARTED":
                    server_thread = evt.get("threadId") or server_thread
                elif t == "STEP_STARTED":
                    print(f"  → step: {evt.get('stepName')}")
                elif t == "STATE_DELTA":
                    print(f"  → STATE_DELTA: {json.dumps(evt.get('delta'), ensure_ascii=False)}")
                elif t == "RUN_FINISHED":
                    print(f"  → RUN_FINISHED: {(evt.get('output') or '')[:200]}")
                    break
                elif t == "RUN_ERROR":
                    print(f"  → RUN_ERROR: {evt.get('error')}")
                    break
    except urllib.error.HTTPError as e:
        print(f"ERRO HTTP {e.code}: {e.read().decode('utf-8','replace')[:400]}", file=sys.stderr)
        sys.exit(1)
    return server_thread


tid = turn("TURNO 1 (cotação → insere output.tickers)", "Qual a cotação atual de PETR4 e VALE3?", None)
turn("TURNO 2 (ordem → lê o draft de cotação)", "Agora quero executar uma ordem de compra de 100 PETR4.", tid)
print(f"\nCONVERSATION_ID={tid}")
