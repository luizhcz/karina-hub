#!/usr/bin/env python3
"""Verifica se operationalMemory vaza pro histórico/transcript.
Cria 1 Conversational COM operationalMemory + output, router default→ele,
roda 2 turnos. Inspeção esperada:
  - operationalMemory NÃO no STATE_DELTA (StructuredOutputState é post-memory)
  - operationalMemory NÃO em chat_messages.StructuredOutput (strippado)
  - operationalMemory NÃO nas mensagens de histórico do prompt
  - operationalMemory SIM no bloco <operational_memory> (estado atual, legítimo)
"""
from __future__ import annotations
import json, sys, time
import urllib.request, urllib.error

BASE = "http://localhost:5189/api/aihub"
PRESET = "grok-4-1-fast-reasoning"
RUN = f"om-{int(time.time())}"
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


ia = http("POST", "/router-intents", {
    "displayName": f"Coletar {RUN}", "description": "Cliente informa dados de perfil/objetivo.",
    "examples": ["meu nome é Ana", "quero aposentadoria", "meu objetivo é renda"]})

agent = f"demo-memoria-{RUN}"
http("POST", "/agents", {
    "id": agent, "name": f"Memoria Teste {RUN}",
    "description": "Agente com operationalMemory pra teste de leak.",
    "type": "Conversational",
    "model": {"deploymentName": "", "predefinedModelId": PRESET},
    "authorInstructions": (
        "Você coleta o perfil do cliente em output {nome, objetivo} e mantém o estado "
        "interno em operationalMemory {cliente_nome, ultimo_objetivo, turnos}. "
        "Em operationalMemory.turnos incremente a cada turno. Confirme cordialmente na message."),
    "metadata": {"x-conversational-output-type": "text",
                 "x-conversational-output-statuses": "[\"coletando\"]"},
    "structuredOutput": {
        "responseFormat": "json_schema", "schemaName": "PerfilMem",
        "schema": {
            "type": "object",
            "properties": {
                "nome": {"type": ["string", "null"]},
                "objetivo": {"type": ["string", "null"]},
            },
            "required": ["nome", "objetivo"], "additionalProperties": False,
        },
    },
    "operationalMemory": {
        "schema": {
            "type": "object",
            "properties": {
                "cliente_nome": {"type": ["string", "null"], "description": "nome do cliente"},
                "ultimo_objetivo": {"type": ["string", "null"], "description": "último objetivo informado"},
                "turnos": {"type": "integer", "description": "contador de turnos"},
            },
        },
    },
    "visibility": "global",
})

router = f"demo-router-{RUN}"
http("POST", "/agents", {
    "id": router, "name": f"Router Mem {RUN}", "description": "router",
    "type": "Router", "model": {"deploymentName": "", "predefinedModelId": PRESET},
    "authorInstructions": "Classifique.", "routerIntentIds": [ia["id"]], "visibility": "global"})

wf = f"demo-mem-wf-{RUN}"
http("POST", "/workflows", {
    "id": wf, "name": f"Mem WF {RUN}", "description": "router → memoria (default)",
    "orchestrationMode": "Graph",
    "agents": [{"agentId": router, "role": "Router"}, {"agentId": agent, "role": "BranchAgent"}],
    "edges": [{"from": router, "edgeType": "Switch", "cases": [
        {"predicate": {"path": "$.intent", "operator": "Eq", "value": ia["name"], "valueType": "String"}, "targets": [agent]},
        {"isDefault": True, "targets": [agent]},
    ], "inputSource": "WorkflowInput"}],
    "configuration": {"inputMode": "Chat", "maxRounds": 5, "maxHistoryMessages": 10, "timeoutSeconds": 90},
    "metadata": {"deploymentKind": "chat"}, "visibility": "project"})

print(f"agent={agent}  workflow={wf}")

SH = dict(H); SH["x-workflow-id"] = wf; SH["Accept"] = "text/event-stream"


def turn(label, text, tid):
    print(f"\n=== {label}: '{text}' ===")
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
            elif t == "STATE_DELTA":
                print(f"  STATE_DELTA: {json.dumps(e.get('delta'), ensure_ascii=False)}")
            elif t == "RUN_FINISHED":
                print(f"  RUN_FINISHED: {(e.get('output') or '')[:240]}")
                break
            elif t == "RUN_ERROR":
                print(f"  RUN_ERROR: {e.get('error')}"); break
    return server


tid = turn("TURNO 1", "Oi, meu nome é Ana e meu objetivo é aposentadoria.", None)
turn("TURNO 2", "Na verdade meu objetivo mudou para comprar um imóvel.", tid)
print(f"\nCONVERSATION_ID={tid}\nAGENT={agent}")
