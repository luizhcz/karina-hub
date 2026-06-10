#!/usr/bin/env python3
"""Aponta o default do workflow demo-handoff p/ o Recomendador (B) e rerroda
2 turnos na mesma conversa, pra demonstrar o hand-off do output de A→B mesmo
quando o router cerca a saída em ```json (default determinístico p/ B)."""
from __future__ import annotations
import json, sys
import urllib.request, urllib.error

BASE = "http://localhost:5189/api/aihub"
RUN = "ho-1781117740"
WF = f"demo-handoff-{RUN}"
ROUTER = f"demo-router-{RUN}"
A = f"demo-coletor-perfil-{RUN}"
B = f"demo-recomendador-{RUN}"
IA = f"informar_perfil_{RUN}"
IB = f"pedir_recomendacao_{RUN}"
H = {"x-efs-user-profile-id": "perf-script", "x-efs-permissions": "efs.admin",
     "x-project-id": "sales-trader-ai", "app_origin": "e2e-script", "Content-Type": "application/json"}


def http(method, path, body=None):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(f"{BASE}{path}", data=data, method=method, headers=H)
    try:
        with urllib.request.urlopen(req, timeout=60) as r:
            txt = r.read().decode() or "{}"
            return json.loads(txt) if txt.strip() else {}
    except urllib.error.HTTPError as e:
        print(f"ERRO {method} {path} → {e.code}\n{e.read().decode('utf-8','replace')[:800]}", file=sys.stderr)
        raise


# default → B (recomendador): turno de recomendação chega em B mesmo se a
# extração de intent falhar pelo fence do router.
http("PUT", f"/workflows/{WF}", {
    "id": WF, "name": f"Demo Hand-off {RUN}",
    "description": "Coletor de Perfil → Recomendador (default→B).",
    "orchestrationMode": "Graph",
    "agents": [{"agentId": ROUTER, "role": "Router"},
               {"agentId": A, "role": "BranchAgent"},
               {"agentId": B, "role": "BranchAgent"}],
    "edges": [{"from": ROUTER, "edgeType": "Switch", "cases": [
        {"predicate": {"path": "$.intent", "operator": "Eq", "value": IA, "valueType": "String"}, "targets": [A]},
        {"predicate": {"path": "$.intent", "operator": "Eq", "value": IB, "valueType": "String"}, "targets": [B]},
        {"isDefault": True, "targets": [A]},
    ], "inputSource": "WorkflowInput"}],
    "configuration": {"inputMode": "Chat", "maxRounds": 5, "maxHistoryMessages": 10, "timeoutSeconds": 90},
    "metadata": {"deploymentKind": "chat"}, "visibility": "project",
})
print(f"workflow {WF} atualizado: default→B")

SH = dict(H); SH["x-workflow-id"] = WF; SH["Accept"] = "text/event-stream"


def turn(label, text, tid):
    print(f"\n=== {label}: '{text}' (threadId={tid}) ===")
    payload = {"messages": [{"role": "user", "content": text}]}
    if tid:
        payload["threadId"] = tid
    req = urllib.request.Request(f"{BASE}/chat/ag-ui/stream", data=json.dumps(payload).encode(),
                                 method="POST", headers=SH)
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


tid = turn("TURNO 1 (perfil → A coleta)",
           "Oi! Meu nome é Marina, tenho perfil conservador e meu objetivo é reserva de emergência em 24 meses.", None)
turn("TURNO 2 (recomendação → B usa o perfil de A)",
     "Show. Agora me monta uma recomendação de carteira pra mim.", tid)
print(f"\nCONVERSATION_ID={tid}")
