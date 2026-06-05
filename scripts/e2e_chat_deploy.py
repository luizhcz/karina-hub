#!/usr/bin/env python3
"""
Reproduz o que a MVP faz pra criar um chat deployment ponta-a-ponta:
  1. Cria 2 router intents
  2. Cria 1 Router agent (preset grok) com as 2 intents
  3. Cria 2 Conversational agents (1 por intent)
  4. Cria um workflow chat (Graph + Switch sobre $.intent)
  5. Dispara o AG-UI stream e captura os eventos

Uso:
  python3 scripts/e2e_chat_deploy.py
"""
from __future__ import annotations

import json
import sys
import time
import uuid
import urllib.error
import urllib.request
from typing import Any

BASE = "http://localhost:5189/api/aihub"
PROJECT_ID = "sales-trader-ai"
PRESET_ID = "grok-4-1-fast-reasoning"
RUN_ID = f"e2e-{int(time.time())}"

HEADERS = {
    "x-efs-user-profile-id": "perf-script",
    "x-efs-permissions": "efs.admin",
    "x-project-id": PROJECT_ID,
    "app_origin": "e2e-script",
    "Content-Type": "application/json",
}


def http(method: str, path: str, body: Any = None, expected: tuple[int, ...] = (200, 201)) -> Any:
    url = f"{BASE}{path}"
    data = json.dumps(body).encode("utf-8") if body is not None else None
    req = urllib.request.Request(url, data=data, method=method, headers=HEADERS)
    try:
        with urllib.request.urlopen(req, timeout=60) as resp:
            payload = resp.read().decode("utf-8") or "{}"
            if resp.status not in expected:
                raise RuntimeError(f"{method} {path} → {resp.status}, esperava {expected}: {payload[:300]}")
            return json.loads(payload) if payload.strip() else {}
    except urllib.error.HTTPError as e:
        body_txt = e.read().decode("utf-8", errors="replace")
        print(f"\nERRO {method} {path} → HTTP {e.code}\n{body_txt[:1000]}\n", file=sys.stderr)
        raise


def section(title: str) -> None:
    print(f"\n=== {title} ===")


# ── 1. Intents ───────────────────────────────────────────────────────────────
section(f"1. Criando 2 router intents (project={PROJECT_ID})")

intent_a = http("POST", "/router-intents", {
    "displayName": f"Saudação E2E {RUN_ID}",
    "description": "Cumprimentos, oi, olá, bom dia — quem fala 'oi' cai aqui.",
    "examples": ["oi", "olá", "bom dia"],
})
print(f"  intent saudacao_id={intent_a['id']}  name={intent_a['name']}")

intent_b = http("POST", "/router-intents", {
    "displayName": f"Despedida E2E {RUN_ID}",
    "description": "Despedidas, tchau, até logo, falou.",
    "examples": ["tchau", "até logo", "falou"],
})
print(f"  intent despedida_id={intent_b['id']}  name={intent_b['name']}")

INTENT_A_ID = intent_a["id"]
INTENT_B_ID = intent_b["id"]
INTENT_A_NAME = intent_a["name"]
INTENT_B_NAME = intent_b["name"]

# ── 2. Router agent ──────────────────────────────────────────────────────────
section("2. Criando agent Router com preset Grok + 2 intents")

router_id = f"e2e-router-{RUN_ID}"
http("POST", "/agents", {
    "id": router_id,
    "name": f"E2E Router {RUN_ID}",
    "description": "Router automático criado por script E2E.",
    "type": "Router",
    "model": {"deploymentName": "", "predefinedModelId": PRESET_ID},
    "authorInstructions": "Classifique a mensagem do usuário entre as intenções disponíveis.",
    "routerIntentIds": [INTENT_A_ID, INTENT_B_ID],
    "visibility": "global",
}, expected=(201,))
print(f"  router_id={router_id}")

# ── 3. Conversational agents ────────────────────────────────────────────────
section("3. Criando 2 agents Conversational (1 por intent)")

conv_a_id = f"e2e-conv-saudacao-{RUN_ID}"
http("POST", "/agents", {
    "id": conv_a_id,
    "name": f"E2E Saudação {RUN_ID}",
    "description": "Responde saudações em PT-BR.",
    "type": "Conversational",
    "model": {"deploymentName": "", "predefinedModelId": PRESET_ID},
    "authorInstructions": "Você é um agente de saudação. Responda cordialmente em até 1 frase.",
    "metadata": {
        "x-conversational-output-type": "text",
        "x-conversational-output-statuses": "[\"default\"]",
        "x-conversational-persona": "Atendente amigável e direto.",
    },
    "visibility": "global",
}, expected=(201,))
print(f"  conv_saudacao_id={conv_a_id}")

conv_b_id = f"e2e-conv-despedida-{RUN_ID}"
http("POST", "/agents", {
    "id": conv_b_id,
    "name": f"E2E Despedida {RUN_ID}",
    "description": "Responde despedidas em PT-BR.",
    "type": "Conversational",
    "model": {"deploymentName": "", "predefinedModelId": PRESET_ID},
    "authorInstructions": "Você é um agente de despedida. Deseje boa continuidade em até 1 frase.",
    "metadata": {
        "x-conversational-output-type": "text",
        "x-conversational-output-statuses": "[\"default\"]",
        "x-conversational-persona": "Atendente educado.",
    },
    "visibility": "global",
}, expected=(201,))
print(f"  conv_despedida_id={conv_b_id}")

# ── 4. Workflow chat ─────────────────────────────────────────────────────────
section("4. Criando workflow chat deployment")

workflow_id = f"e2e-chat-{RUN_ID}"
workflow_body = {
    "id": workflow_id,
    "name": f"E2E Chat {RUN_ID}",
    "description": "Router → Saudação | Despedida (Switch sobre $.intent).",
    "orchestrationMode": "Graph",
    "agents": [
        {"agentId": router_id, "role": "Router"},
        {"agentId": conv_a_id, "role": "BranchAgent"},
        {"agentId": conv_b_id, "role": "BranchAgent"},
    ],
    "edges": [
        {
            "from": router_id,
            "edgeType": "Switch",
            "cases": [
                {
                    "predicate": {
                        "path": "$.intent",
                        "operator": "Eq",
                        "value": INTENT_A_NAME,
                        "valueType": "String",
                    },
                    "targets": [conv_a_id],
                },
                {
                    "predicate": {
                        "path": "$.intent",
                        "operator": "Eq",
                        "value": INTENT_B_NAME,
                        "valueType": "String",
                    },
                    "targets": [conv_b_id],
                },
                {"isDefault": True, "targets": [conv_a_id]},
            ],
            "inputSource": "WorkflowInput",
        },
    ],
    "configuration": {
        "inputMode": "Chat",
        "maxRounds": 5,
        "maxHistoryMessages": 10,
        "timeoutSeconds": 60,
    },
    "metadata": {"deploymentKind": "chat"},
    "visibility": "project",
}
http("POST", "/workflows", workflow_body, expected=(201,))
print(f"  workflow_id={workflow_id}")

# ── 5. AG-UI stream test ─────────────────────────────────────────────────────
section("5. Disparando AG-UI stream + capturando eventos")

import urllib.request as _r
import re

stream_body = json.dumps({
    "messages": [{"role": "user", "content": "Oi, tudo bem?"}]
}).encode("utf-8")

stream_headers = dict(HEADERS)
stream_headers["x-workflow-id"] = workflow_id
stream_headers["Accept"] = "text/event-stream"

req = _r.Request(
    f"{BASE}/chat/ag-ui/stream",
    data=stream_body,
    method="POST",
    headers=stream_headers,
)

events_received: list[dict] = []
try:
    with _r.urlopen(req, timeout=60) as resp:
        for raw in resp:
            line = raw.decode("utf-8", errors="replace").rstrip("\n")
            if not line.startswith("data:"):
                continue
            payload = line[len("data:"):].strip()
            if not payload:
                continue
            try:
                evt = json.loads(payload)
            except json.JSONDecodeError:
                continue
            events_received.append(evt)
            evt_type = evt.get("type", "?")
            tag = evt_type
            if evt_type == "CUSTOM":
                tag = f"CUSTOM[{evt.get('customName')}] phase={evt.get('customValue',{}).get('phase')} agentType={evt.get('customValue',{}).get('agentType')}"
            elif evt_type == "STEP_STARTED" or evt_type == "STEP_FINISHED":
                tag = f"{evt_type}  stepName={evt.get('stepName')}"
            elif evt_type == "STATE_DELTA":
                tag = f"STATE_DELTA  delta={json.dumps(evt.get('delta'), ensure_ascii=False)[:160]}"
            elif evt_type == "TEXT_MESSAGE_CONTENT":
                d = evt.get("delta") or ""
                d_short = (d[:120] + "…") if len(d) > 120 else d
                tag = f"TEXT_MESSAGE_CONTENT  delta={d_short!r}"
            elif evt_type == "RUN_FINISHED":
                out = evt.get("output") or ""
                tag = f"RUN_FINISHED  output={(out[:200] + '…') if len(out) > 200 else out}"
            elif evt_type == "RUN_ERROR":
                tag = f"RUN_ERROR  code={evt.get('errorCode')}  error={evt.get('error')}"
            print(f"  ← {tag}")
            if evt_type in ("RUN_FINISHED", "RUN_ERROR", "SAFETY_VIOLATION"):
                break
except urllib.error.HTTPError as e:
    body_txt = e.read().decode("utf-8", errors="replace")
    print(f"\nERRO no stream: HTTP {e.code}\n{body_txt[:500]}", file=sys.stderr)
    sys.exit(1)

# ── 6. Summary ───────────────────────────────────────────────────────────────
section("Resumo")
print(f"  Total de eventos recebidos: {len(events_received)}")
by_type: dict[str, int] = {}
for e in events_received:
    by_type[e.get("type", "?")] = by_type.get(e.get("type", "?"), 0) + 1
for t, n in sorted(by_type.items(), key=lambda kv: -kv[1]):
    print(f"    {n:3d} × {t}")
print(f"\n  workflow_id={workflow_id}")
print(f"  router_id={router_id}")
print(f"  conv_saudacao_id={conv_a_id}")
print(f"  conv_despedida_id={conv_b_id}")
