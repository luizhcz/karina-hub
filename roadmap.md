# Roadmap — EfsAiHub MVP

## Débito — `agentType` no protocolo AG-UI

Hoje o tipo do agente (Router / Conversational / Worker / ToolRunner / Custom)
é propagado pro stream AG-UI através do CUSTOM event paralelo
`agent.lifecycle` (`CustomValue = { nodeId, phase, agentType, agentName,
durationMs? }`). Os clientes leem esse evento side-channel e mantêm um mapa
`agentId → agentType` localmente, usado pra renderers especializados (ex:
`RouterDecisionCard` no chat sandbox).

Essa escolha (Opção B do planejamento) foi feita pra não quebrar o wire
format dos eventos canônicos do AG-UI. Funciona, mas tem custo:

- **Evento dobrado** por node: pra cada agent o backend emite
  `STEP_STARTED` + `CUSTOM[agent.lifecycle started]` + `STEP_FINISHED` +
  `CUSTOM[agent.lifecycle finished]`. Em workflows com muitos steps o
  overhead cresce linearmente.
- **Side-channel discovery**: clientes que tipam estritamente o `AgUiEvent`
  ignoram CUSTOM events que não conhecem. O tipo do agente só chega pra
  quem decidiu mapear `customName === "agent.lifecycle"` na mão.
- **Frontend mantém merge defensivo**: `mergeAgentTypes(fromListAgents,
  fromStream)` no [ChatDeploymentSandbox.tsx](mvp/src/routes/ChatDeploymentSandbox.tsx)
  existe porque até o primeiro `agent.lifecycle` chegar a UI precisa de um
  fallback via `listAgents()`. Esse merge desaparece quando o tipo já vem
  em `STEP_STARTED`.

### Solução (Opção C, deixada pra esta dívida)

Promover `agentType` a cidadão de 1ª classe no `AgUiStepStartedEvent` /
`AgUiStepFinishedEvent`:

1. Adicionar `AgentType?: string` em
   [src/EfsAiHub.Host.Api/Chat/AgUi/Models/AgUiEvent.cs](src/EfsAiHub.Host.Api/Chat/AgUi/Models/AgUiEvent.cs).
2. Propagar do payload upstream (já carrega `agentType` desde a Fase 8 do
   chat-deploy — ver `WorkflowRunnerService.CreateNodeCallback` e
   `AgentHandoffEventHandler`) no
   [AgUiEventMapper.MapNodeStarted](src/EfsAiHub.Host.Api/Chat/AgUi/AgUiEventMapper.cs)
   e `MapStepCompleted`.
3. Remover a emissão do `CUSTOM[agent.lifecycle]` paralelo (deletar
   `BuildAgentLifecycle` + os dois testes novos do
   [AgUiEventMapperTests.cs](tests/EfsAiHub.Tests.Unit/AgUi/AgUiEventMapperTests.cs)
   que cobrem o custom event).
4. Frontend: remover `mergeAgentTypes` no
   [ChatDeploymentSandbox.tsx](mvp/src/routes/ChatDeploymentSandbox.tsx) +
   `agentTypeByNodeId` no [useChatStream.ts](mvp/src/hooks/useChatStream.ts).
   Consumir `stepEvent.agentType` direto.
5. Comunicar pra qualquer consumidor externo do AG-UI: campo novo `agentType`
   nos eventos `STEP_STARTED` / `STEP_FINISHED` — opcional pra clientes
   antigos (omissão segura).

### Riscos & critério pra priorizar

- **Compatibilidade**: a mudança é aditiva (novo campo opcional), então
  clientes legados não quebram — mas tipos TS/JSON-schemas estritos
  precisam ser regenerados.
- **Quando vale**: se aparecer um segundo consumidor do AG-UI (ex: mobile,
  outro SDK) que precise do tipo do agente sem montar a lógica do
  side-channel. Enquanto for só o chat sandbox, a Opção B atual cobre.
- **Estimativa**: 5 arquivos backend, 2 frontend, +1 teste, ~2h de trabalho
  incluindo TLR.
