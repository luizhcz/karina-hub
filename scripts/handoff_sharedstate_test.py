#!/usr/bin/env python3
"""Hand-off via shared state: o OUTPUT estruturado do agente A (turno 1) é lido
e USADO pelo agente B (turno 2).

  - Agente A (Coletor de Perfil): output {nome, perfil_risco, objetivo, horizonte_meses}
  - Agente B (Recomendador):      lê o perfil de A no <shared_state> e monta alocação

Cria intents + 2 conversationais (preset Grok) + 1 router + workflow chat,
depois roda 2 turnos na MESMA conversa (threadId via RUN_STARTED).
"""
from __future__ import annotations
import json, sys, time
import urllib.request, urllib.error

BASE = "http://localhost:5189/api/aihub"
PRESET = "grok-4-1-fast-reasoning"
RUN = f"ho-{int(time.time())}"
H = {
    "x-efs-user-profile-id": "perf-script",
    "x-efs-permissions": "efs.admin",
    "x-project-id": "sales-trader-ai",
    "app_origin": "e2e-script",
    "Content-Type": "application/json",
}


def http(method, path, body=None, expected=(200, 201)):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(f"{BASE}{path}", data=data, method=method, headers=H)
    try:
        with urllib.request.urlopen(req, timeout=60) as r:
            txt = r.read().decode() or "{}"
            return json.loads(txt) if txt.strip() else {}
    except urllib.error.HTTPError as e:
        print(f"ERRO {method} {path} → {e.code}\n{e.read().decode('utf-8','replace')[:800]}", file=sys.stderr)
        raise


# ── 1. intents ───────────────────────────────────────────────────────────────
ia = http("POST", "/router-intents", {
    "displayName": f"Informar Perfil {RUN}",
    "description": "Cliente informa seus dados de investidor: nome, perfil de risco (conservador/moderado/arrojado), objetivo, horizonte.",
    "examples": ["meu nome é João, perfil arrojado, quero aposentadoria",
                 "sou conservador", "tenho perfil moderado e objetivo de renda"],
})
ib = http("POST", "/router-intents", {
    "displayName": f"Pedir Recomendacao {RUN}",
    "description": "Cliente pede recomendação/sugestão de carteira ou alocação de investimentos.",
    "examples": ["me recomenda uma carteira", "monta uma alocação pra mim",
                 "o que devo investir"],
})
print(f"intents: A={ia['name']}  B={ib['name']}")

# ── 2. agente A (Coletor de Perfil) ─────────────────────────────────────────
agent_a = f"demo-coletor-perfil-{RUN}"
http("POST", "/agents", {
    "id": agent_a, "name": f"Coletor de Perfil {RUN}",
    "description": "Coleta o perfil de investidor do cliente (demo hand-off).",
    "type": "Conversational",
    "model": {"deploymentName": "", "predefinedModelId": PRESET},
    "authorInstructions": (
        "Você é o Coletor de Perfil. Extraia da mensagem do cliente e preencha output: "
        "nome (se informado), perfil_risco (conservador|moderado|arrojado), objetivo "
        "(ex: aposentadoria, reserva, renda), horizonte_meses (em MESES; converta anos). "
        "Campos não informados = null. Na message, confirme cordialmente o que entendeu. "
        "NÃO recomende nada — apenas colete."
    ),
    "metadata": {"x-conversational-output-type": "text",
                 "x-conversational-output-statuses": "[\"perfil_coletado\"]"},
    "structuredOutput": {
        "responseFormat": "json_schema", "schemaName": "PerfilInvestidor",
        "schema": {
            "type": "object",
            "properties": {
                "nome": {"type": ["string", "null"]},
                "perfil_risco": {"type": ["string", "null"]},
                "objetivo": {"type": ["string", "null"]},
                "horizonte_meses": {"type": ["integer", "null"]},
            },
            "required": ["nome", "perfil_risco", "objetivo", "horizonte_meses"],
            "additionalProperties": False,
        },
    },
    "visibility": "global",
}, expected=(201,))

# ── 3. agente B (Recomendador) ───────────────────────────────────────────────
agent_b = f"demo-recomendador-{RUN}"
http("POST", "/agents", {
    "id": agent_b, "name": f"Recomendador {RUN}",
    "description": "Recomenda carteira usando o perfil coletado (demo hand-off).",
    "type": "Conversational",
    "model": {"deploymentName": "", "predefinedModelId": PRESET},
    "authorInstructions": (
        "Você é o Recomendador de Carteira. No bloco <shared_state> você recebe o perfil "
        f"coletado pelo agente 'Coletor de Perfil {RUN}'. USE esses dados: cite o NOME do "
        "cliente e o PERFIL_RISCO dele na message, e monte uma alocação coerente com o "
        "perfil em output.alocacao (lista de {classe, percentual} somando ~100). Preencha "
        "output.perfil_considerado com o perfil_risco que você LEU do shared_state. Se não "
        "houver perfil no shared_state, diga que precisa coletar o perfil antes."
    ),
    "metadata": {"x-conversational-output-type": "text",
                 "x-conversational-output-statuses": "[\"recomendado\"]"},
    "structuredOutput": {
        "responseFormat": "json_schema", "schemaName": "RecomendacaoCarteira",
        "schema": {
            "type": "object",
            "properties": {
                "perfil_considerado": {"type": ["string", "null"]},
                "alocacao": {
                    "type": "array",
                    "items": {
                        "type": "object",
                        "properties": {
                            "classe": {"type": "string"},
                            "percentual": {"type": ["number", "null"]},
                        },
                        "required": ["classe", "percentual"],
                        "additionalProperties": False,
                    },
                },
            },
            "required": ["perfil_considerado", "alocacao"],
            "additionalProperties": False,
        },
    },
    "visibility": "global",
}, expected=(201,))

# ── 4. router ────────────────────────────────────────────────────────────────
router = f"demo-router-{RUN}"
http("POST", "/agents", {
    "id": router, "name": f"Router Demo {RUN}",
    "description": "Roteia entre coletar perfil e recomendar.",
    "type": "Router",
    "model": {"deploymentName": "", "predefinedModelId": PRESET},
    "authorInstructions": "Classifique a mensagem entre as intenções disponíveis.",
    "routerIntentIds": [ia["id"], ib["id"]],
    "visibility": "global",
}, expected=(201,))

# ── 5. workflow ──────────────────────────────────────────────────────────────
wf = f"demo-handoff-{RUN}"
http("POST", "/workflows", {
    "id": wf, "name": f"Demo Hand-off {RUN}",
    "description": "Coletor de Perfil → Recomendador (Switch sobre $.intent).",
    "orchestrationMode": "Graph",
    "agents": [
        {"agentId": router, "role": "Router"},
        {"agentId": agent_a, "role": "BranchAgent"},
        {"agentId": agent_b, "role": "BranchAgent"},
    ],
    "edges": [{
        "from": router, "edgeType": "Switch",
        "cases": [
            {"predicate": {"path": "$.intent", "operator": "Eq", "value": ia["name"], "valueType": "String"}, "targets": [agent_a]},
            {"predicate": {"path": "$.intent", "operator": "Eq", "value": ib["name"], "valueType": "String"}, "targets": [agent_b]},
            {"isDefault": True, "targets": [agent_a]},
        ],
        "inputSource": "WorkflowInput",
    }],
    "configuration": {"inputMode": "Chat", "maxRounds": 5, "maxHistoryMessages": 10, "timeoutSeconds": 90},
    "metadata": {"deploymentKind": "chat"},
    "visibility": "project",
}, expected=(201,))
print(f"workflow={wf}  router={router}  A={agent_a}  B={agent_b}")

# ── 6. 2 turnos na mesma conversa ────────────────────────────────────────────
SH = dict(H); SH["x-workflow-id"] = wf; SH["Accept"] = "text/event-stream"


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


tid = turn("TURNO 1 (perfil → agente A coleta)",
           "Oi! Meu nome é João, tenho perfil arrojado e meu objetivo é aposentadoria em 10 anos.", None)
turn("TURNO 2 (recomendação → agente B usa o perfil de A)",
     "Perfeito. Agora me monta uma recomendação de carteira.", tid)
print(f"\nCONVERSATION_ID={tid}\nWORKFLOW={wf}\nAGENT_A={agent_a}\nAGENT_B={agent_b}")
