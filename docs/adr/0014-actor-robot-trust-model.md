# ADR 0014 — `actor=robot` no body do AG-UI sem auth (trust no proxy upstream)

**Status:** Aceito
**Data:** 2026-04-26

## Context

Frontend e robôs (RPA, scripts internos) podem postar mensagens na conversa que registram o resultado de uma chamada externa (CRM, ERP, banking, sistema legado) **sem disparar workflow**. Sem essa marcação, a operação fica invisível na thread — não há rastro de "robô confirmou ação X" no histórico, audit, ou treinamento futuro. Vira "comando fantasma".

Caso de uso canônico (frontend-mediated function calling):

```
1. Humano: "Qual meu saldo?"
2. Workflow → agente LLM produz objeto estruturado:
     { "intent": "consultar_saldo", "endpoint": "/api/cliente/saldo" }
3. Frontend chama esse endpoint EXTERNO (não EFS AI Hub) com auth próprio.
4. Backend externo responde { "saldo": 12480.33 }.
5. Frontend posta no AG-UI:
     POST /api/chat/ag-ui/stream
     { "messages": [..., { "role": "user", "content": "{...}", "actor": "robot" }] }
6. Backend persiste com Role=user + Actor=Robot, NÃO dispara workflow,
   responde com SSE sintético (RUN_STARTED → CUSTOM(actor.persisted) →
   MESSAGES_SNAPSHOT → RUN_FINISHED).
7. Humano: "Pode transferir 5000 pra poupança"
8. Workflow dispara → LLM vê histórico completo (incluindo a robot msg
   como Role=user) e age coerentemente.
```

A modelagem precisava decidir 3 eixos:

1. **Wire**: role nova `robot` (fere spec AG-UI canônica de 5 papéis: developer/system/user/assistant/tool) ou campo aditivo `actor` paralelo (preserva spec)?
2. **Auth**: o backend precisa autenticar quem manda `actor=robot`?
3. **Rate limit**: robot consome cota?

## Decision

**Campo `actor` opcional no body do `messages[]`, sem auth, sem rate limit.**

### Wire (decisão arquitetural)

Role permanece dentro dos 5 canônicos (`developer`, `system`, `user`, `assistant`, `tool`). `actor` é campo aditivo na input message (`"human"` default, `"robot"` opcional). Robot persiste com `Role=user` + `Actor=Robot` no domínio. Aditivo a campo extra é tolerado pela spec (herda OpenAI Chat Completions) — clientes AG-UI canônicos (CopilotKit React SDK, Mastra, Pydantic AI) ignoram `actor` silenciosamente. Interop preservada.

### Auth (decisão de produto)

**Sem header dedicado, sem JWT scope, sem secret.** O backend lê `actor` do body e confia. Trust boundary fica na camada de proxy upstream (Istio mTLS interno, gateway corporativo, cloud LB com IP allowlist) — quem fala diretamente com `:8080` ignorando o proxy não é cenário esperado em produção controlada.

Simetria com headers já existentes: `x-efs-account` em [`AccountGuardChatClient.cs:79-115`](../../src/EfsAiHub.Platform.Runtime/Middlewares/AccountGuardChatClient.cs#L79-L115) é usado pra impor isolamento de conta no output do LLM e **também não é validado por assinatura**. Spoofar `x-efs-account` é estritamente mais perigoso (vazamento cross-account) que spoofar `actor` (bypass de cobrança em conta própria). Se o projeto aceita o primeiro sem auth, aceitar o segundo é consistente.

### Rate limit (decisão de produto)

Robot **bypassa todos os rate limiters** (per-user, per-conversation, project, custom). Nenhuma quota dedicada. Justificativa: rate limit existe pra bloquear abuse; robot é tráfego legítimo originado na infra controlada do cliente, e qualquer flood real seria acidente de implementação no proxy/RPA upstream — responsabilidade da infra, não da aplicação.

## Consequences

### Positivas

- Wire AG-UI puro: `messages[].actor` é aditivo, compatível com clientes da spec sem mudança.
- Custo de implementação mínimo (~300 linhas no backend, ~50 no frontend).
- Simetria com `x-efs-account`/`x-efs-project-id` — modelo mental único de trust no projeto.
- `BuildChatMessage` corrige bug pré-existente que mapeava `role=robot → assistant` — histórico passa a refletir verdade (robot como `user` + `Actor=Robot`).

### Negativas (aceitas)

- Body é fonte da verdade pra fato de segurança ("foi um robô?"). Dívida arquitetural reconhecida, não fundamento.
- Auditoria depende da honestidade do proxy. "Actor real" significa "actor declarado pelo proxy upstream" — admissível enquanto proxy é controlado pela mesma org.
- Spoof attempts ficam invisíveis (não há 403 no backend). Detecção move-se para o proxy/SIEM.
- Cliente que falar direto com o backend (port-forward, LB mal configurado, lift-and-shift cross-cloud) bypassa rate limit e polui audit. Mitigação completa exige v4.

## Invariantes

1. `role ∈ {developer, system, user, assistant, tool}` — inalterada, AG-UI canônica.
2. `actor ∈ {human, robot}`, default `human`, **aditivo a role**, jamais substitui.
3. Robot persiste com `Role=user, Actor=Robot` no domínio. `BuildChatMessage` nunca mais mapeia `robot → assistant`.
4. Robot **não dispara workflow nem consome budget** — short-circuit em `ConversationService.SendMessagesAsync` antes do `TriggerAsync`. Teste de regressão em `AgUiActorRobotTests.Post_ActorRobot_PersistEMantemSemDisparoDeWorkflow`.
5. Robot durante execução em curso (Running, sem HITL) **registra paralelamente sem cancelar**. Early return no branch `lastIsRobot` preserva `ActiveExecutionId`.
6. Robot + HITL pendente → 400 explícito; HITL programático deve usar `POST /api/chat/ag-ui/resolve-hitl`, não `/stream` com `actor=robot`.
7. `actor=robot` que **não é** a última mensagem do batch → 400. Robot fecha turno por design.
8. `actor` com string fora de `{null, "human", "robot"}` (após trim) → 400 explícito. Sem silent default.
9. `Actor` no domínio interno **jamais vaza para `ChatRole`** — `ChatTurnContextMapper` mapeia robot → `ChatRole.User` quando o histórico vai pro LLM.
10. `UpdateConversationTitle` filtra `Actor=Human` — payloads programáticos de robot nunca viram título da thread.

## Anti-patterns evitados

- **Role nova `robot` no wire AG-UI** — fork silencioso da spec, quebra interop com CopilotKit React SDK e qualquer cliente padrão.
- **Mapear `robot → assistant` na persistência** (estado anterior do código) — polui histórico com fala-de-LLM falsa, contamina datasets futuros de fine-tuning.
- **`actor` como string-livre não validada** — repetiria o pattern problemático do fallback silencioso `_ => ChatRole.User` em `ChatTurnContextMapper.cs:114`.
- **Endpoint paralelo `/messages/persist`** — duplicaria stream/state/approval pipelines, drift inevitável entre os dois caminhos.
- **CIDR check / env=Production** como defesa-em-profundidade — discutido no round 2 do refinamento, descartado pelo time. Trust no proxy é decisão consciente, não acidental.

## Riscos remanescentes (priorizados)

1. **Alto — Exposição direta do backend** (port-forward, LB mal configurado, lift-and-shift cross-cloud): trust model quebra silenciosamente. Mitigação completa exige v4 (auth aplicacional). Mitigação operacional: monitoring de "actor=robot vindo de IP fora do range esperado" via dashboard SIEM.
2. **Médio — Lateral movement no mesh**: pod comprometido emite `actor=robot` e bypassa cobrança/auditoria de outros tenants. ProjectId continua boundary (ADR 003), mas drift cross-project com robot fica indetectável aplicacionalmente.
3. **Médio — Bug futuro no short-circuit** (regressão em `lastIsRobot` ou `ConversationService.Messaging.cs`): robot consome budget sem flag. Mitigação: invariante 4 protegida por teste de integração obrigatório.
4. **Baixo — Confusão semântica**: dev novo assume que `actor` é fato verificado. Mitigação: este ADR + comentário XML em `AgUiInputMessage.Actor`, `BuildChatMessage`, e `AgUiEndpoints.StreamAsync` apontando para cá.

## Caminho de evolução

**v3.1** (quando primeiro cliente B2B externo entrar): teto duro de rate limit por `account_id` independente de actor (anti-flood global de baixo custo).

**v4** (multi-cloud ou primeiro cliente externo direto): `actor=robot` exige JWT scope `agent:robot` emitido pelo IdP corporativo; body vira hint, claim vira verdade. Interface (`actor` no body) permanece — muda só a validação por baixo, sem quebrar clientes existentes.

Artefatos obrigatórios já no merge desta v3:
- Este ADR linkado em `docs/ag-ui.md` (seção "Extensão `actor`") e no devportal.
- Comentário XML em `AgUiInputMessage`, `Actor` enum, `BuildChatMessage` e `AgUiEndpoints.StreamAsync` apontando para `ADR 0014`.
- Métrica `RobotMessagesPersisted` em Grafana — base pra dashboard `actor=robot vs human` por workflow.
- Item de backlog explícito: "v3.1 trigger: primeiro cliente externo direto".

Sem esses artefatos, a próxima geração de devs vai tratar `actor` como fundamento, não como escolha datada — e o débito vira dívida silenciosa.

## Critical files

- `src/EfsAiHub.Core.Abstractions/Conversations/Actor.cs` — enum
- `src/EfsAiHub.Core.Abstractions/Conversations/ChatMessage.cs` — campo
- `src/EfsAiHub.Host.Api/Chat/AgUi/Models/AgUiInputMessage.cs` — wire
- `src/EfsAiHub.Host.Api/Chat/AgUi/AgUiEndpoints.cs` — validação + roteamento
- `src/EfsAiHub.Host.Api/Chat/AgUi/Handlers/AgUiSseHandler.cs` — SSE sintético
- `src/EfsAiHub.Host.Api/Chat/Services/ConversationService.cs` — `BuildChatMessage`, `UpdateConversationTitle`
- `src/EfsAiHub.Host.Api/Chat/Services/ConversationService.Messaging.cs` — gate `lastIsRobot`
- `frontend/src/features/chat/components/RobotBubble.tsx` — render
- `db/schemas.sql:344` — coluna `Actor`
