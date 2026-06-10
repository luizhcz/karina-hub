#!/usr/bin/env python3
"""Hand-off determinístico A→B. O router Grok-reasoning é intermitente (às vezes
vaza chain-of-thought em vez do JSON de intent), então pra observar o hand-off
do shared state de forma confiável fixamos a rota por turno via default:
  - Turno 1: default→A (Coletor) coleta o perfil → grava output no shared state.
  - Turno 2: default→B (Recomendador) lê o perfil de A e usa.
O mecanismo de shared state em si não é tocado.
"""
from __future__ import annotations
import json, sys
import urllib.request, urllib.error

BASE = "http://localhost:5189/api/aihub"
RUN = "ho-1781117740"
WF, ROUTER = f"demo-handoff-{RUN}", f"demo-router-{RUN}"
A, B = f"demo-coletor-perfil-{RUN}", f"demo-recomendador-{RUN}"
IA, IB = f"informar_perfil_{RUN}", f"pedir_recomendacao_{RUN}"
H = {"x-efs-user-profile-id": "perf-script", "x-efs-permissions": "efs.admin",
     "x-project-id": "sales-trader-ai", "app_origin": "e2e-script", "Content-Type": "application/json"}


def http(method, path, body=None):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(f"{BASE}{path}", data=data, method=method, headers=H)
    with urllib.request.urlopen(req, timeout=60) as r:
        txt = r.read().decode() or "{}"
        return json.loads(txt) if txt.strip() else {}


def set_default(target):
    http("PUT", f"/workflows/{WF}", {
        "id": WF, "name": f"Demo Hand-off {RUN}", "description": "hand-off demo",
        "orchestrationMode": "Graph",
        "agents": [{"agentId": ROUTER, "role": "Router"}, {"agentId": A, "role": "BranchAgent"}, {"agentId": B, "role": "BranchAgent"}],
        "edges": [{"from": ROUTER, "edgeType": "Switch", "cases": [
            {"predicate": {"path": "$.intent", "operator": "Eq", "value": IA, "valueType": "String"}, "targets": [A]},
            {"predicate": {"path": "$.intent", "operator": "Eq", "value": IB, "valueType": "String"}, "targets": [B]},
            {"isDefault": True, "targets": [target]},
        ], "inputSource": "WorkflowInput"}],
        "configuration": {"inputMode": "Chat", "maxRounds": 5, "maxHistoryMessages": 10, "timeoutSeconds": 90},
        "metadata": {"deploymentKind": "chat"}, "visibility": "project",
    })


SH = dict(H); SH["x-workflow-id"] = WF; SH["Accept"] = "text/event-stream"


def turn(label, text, tid):
    print(f"\n=== {label}: '{text}' (threadId={tid}) ===")
    payload = {"messages": [{"role": "user", "content": text}]}
    if tid:
        payload["threadId"] = tid
    req = urllib.request.Request(f"{BASE}/chat/ag-ui/stream", data=json.dumps(payload).encode(), method="POST", headers=SH)
    server = tid
    with urllib.request.urlopen(req, timeout=120) as r:
        for raw in r:
            line = raw.decode("utf-8", "replace").rstrip("\n")
            if not line.startswith("data:"):
                continue
            p = line[5:].strip()
            if not p:
                continue
            try:
                e = json.loads(p)
            except json.JSONDecodeError:
                continue
            t = e.get("type")
            if t == "RUN_STARTED":
                server = e.get("threadId") or server
            elif t == "STEP_STARTED":
                print(f"  → step: {e.get('stepName')}")
            elif t == "STATE_DELTA":
                print(f"  → STATE_DELTA: {json.dumps(e.get('delta'), ensure_ascii=False)}")
            elif t == "RUN_FINISHED":
                print(f"  → RUN_FINISHED: {e.get('output')}")
                break
            elif t == "RUN_ERROR":
                print(f"  → RUN_ERROR: {e.get('error')}"); break
    return server


set_default(A)
tid = turn("TURNO 1 (Coletor A coleta o perfil)",
           "Oi! Meu nome é Marina, tenho perfil conservador e meu objetivo é reserva de emergência em 24 meses.", None)
set_default(B)
turn("TURNO 2 (Recomendador B usa o perfil de A)",
     "Show. Agora me monta uma recomendação de carteira pra mim.", tid)
print(f"\nCONVERSATION_ID={tid}")
