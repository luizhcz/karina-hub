#!/usr/bin/env python3
"""Dispara 2 turnos na MESMA conversa contra um workflow chat existente.
O conversationId real é gerado pelo servidor e volta no evento RUN_STARTED
(campo threadId). Captura-se no turno 1 e reusa-se no turno 2 — assim o
shared state persiste entre turnos. Turno 1 popula (agente A); turno 2
(agente B) lê o draft de A e renderiza no prompt.
"""
from __future__ import annotations
import json, sys
import urllib.request, urllib.error

BASE = "http://localhost:5189/api/aihub"
WORKFLOW_ID = "e2e-chat-e2e-1781116024"

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
    body = json.dumps(payload).encode()
    req = urllib.request.Request(f"{BASE}/chat/ag-ui/stream", data=body,
                                 method="POST", headers=HEADERS)
    server_thread = thread_id
    try:
        with urllib.request.urlopen(req, timeout=90) as resp:
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
                    print(f"  → RUN_STARTED threadId(server)={server_thread}")
                elif t == "STEP_STARTED":
                    print(f"  → roteado p/ step: {evt.get('stepName')}")
                elif t == "STATE_DELTA":
                    print(f"  → STATE_DELTA: {json.dumps(evt.get('delta'), ensure_ascii=False)}")
                elif t == "RUN_FINISHED":
                    out = evt.get("output") or ""
                    print(f"  → RUN_FINISHED: {out[:160]}")
                    break
                elif t == "RUN_ERROR":
                    print(f"  → RUN_ERROR: {evt.get('error')}")
                    break
    except urllib.error.HTTPError as e:
        print(f"ERRO HTTP {e.code}: {e.read().decode('utf-8','replace')[:400]}", file=sys.stderr)
        sys.exit(1)
    return server_thread


tid = turn("TURNO 1 (saudação → popula estado do agente A)", "Oi, bom dia!", None)
turn("TURNO 2 (despedida → agente B lê o estado de A)", "Valeu, tchau, até logo!", tid)
print(f"\nCONVERSATION_ID={tid}")
