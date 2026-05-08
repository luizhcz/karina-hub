# Roadmap — EfsAiHub MVP

Features planejadas pro próximo ciclo do MVP. Cada item descreve a entrega de
forma sucinta; o detalhamento técnico (ADR/contratos) entra no momento do
refinamento.

---

## 1. Implantação avançada — workflows multi-agente

**Hoje:** a tela de Implantações cria um workflow Graph trivial (1 agente
publicado → 1 deployment), exposto via `/api/aihub/workflows/{id}/trigger`.

**Próximo passo:** permitir que o usuário monte um **workflow com múltiplos
agentes** orquestrados — saídas de um agente alimentam outro, com ramificações
condicionais e nós de transformação entre eles. Reaproveita o `WorkflowDefinition`
e o runtime de Graph que já existe; o esforço fica na **UX de composição visual**
(arrastar agentes, conectar arestas, declarar mapeamento de input/output entre
nós) e na **validação semântica** (schema de saída de A é compatível com schema
de entrada de B?).

**Pontos de atenção:**
- Preservar o fluxo simples atual como atalho — "implantação básica" continua
  sendo 1 agente.
- Versionamento: editar um agente que está dentro de um workflow multi-agente
  deve disparar o mesmo gate de breaking change que já existe.
- Validação de schema entre nós: já temos o `JsonSchemaBuilder` — usar ele pra
  oferecer mapeamento campo-a-campo na UI.

---

## 2. Tela de integrações — filas, bancos, etc.

**Hoje:** integrações externas só existem como `GenericTool` (HTTP genérico) —
qualquer fila ou banco precisa estar atrás de um endpoint REST.

**Próximo passo:** **catálogo de integrações nativas** com adapters que o
agente pode consumir diretamente. Tipos iniciais previstos:
- **Filas:** RabbitMQ, Kafka, SQS — publicar/consumir mensagens.
- **Bancos:** PostgreSQL, SQL Server — query parametrizada com schema declarado.
- **Outros:** redis (cache), S3/blob (upload/download).

Cada integração vira um tipo de tool com configuração própria (similar ao
GenericTool atual) + adapter no runtime que cuida de connection pooling,
secrets e validação. UI espelha o ToolEditor — listagem por projeto, editor
com campos específicos de cada tipo, botão "Testar" reaproveitando o padrão
isolado que já existe pro GenericTool.

**Pontos de atenção:**
- Secrets via secrets manager (já temos o pipeline pra GenericTool — reusar).
- Cada tipo precisa de uma estratégia de timeout/retry própria — filas têm
  semântica de delivery diferente de HTTP.
- Validação no editor é mais cara: testar "consigo conectar nesse banco?"
  envolve credenciais reais.

---

## 3. Novo tipo de criação de agente — `Chat`

**Hoje:** o wizard cria agentes que são chamados via API (single-shot) ou em
workflows. Não há um modo conversacional onde o agente mantém estado de
mensagens entre turns.

**Próximo passo:** novo modo `chat` no wizard de criação que produz um agente
com **histórico de conversa persistido** e endpoint de chat dedicado. O agente
mantém contexto entre turns sem que o caller precise reenviar o histórico
inteiro a cada chamada.

**Pontos de atenção:**
- Persistência: já existe `aihub.conversations` + `chat_messages` — o
  AgentSandbox usa isso pra debug. Reaproveitar como base.
- Janela de contexto: definir política de truncamento/sumarização quando a
  conversa cresce demais (configurável por agente).
- UX do wizard: simplificar steps que não fazem sentido no modo chat (ex.:
  Output estruturado é menos útil — saída é texto livre por padrão).
- Endpoint público: contrato similar ao Agent Sessions atual mas com `chatId`
  estável fornecido pelo caller.

---

## 4. Consumir workflow de versão específica — header `x-version`

**Hoje:** workflow é sempre executado a partir do estado mutável atual em
`workflow_definitions`. `WorkflowVersion` existe como snapshot append-only mas
serve só pra histórico em UI e rollback (que reescreve o estado atual com a
versão antiga). Não há como rodar duas versões em paralelo (canary, A/B).

**Próximo passo:** aceitar um header `x-version: <workflowVersionId>` opcional
nos 3 endpoints que criam execução — `POST /workflows/{id}/trigger`,
`POST /workflows/{id}/sandbox` e `POST /conversations/{id}/messages`. Quando
presente, o `WorkflowService.TriggerAsync` carrega a definition do snapshot
(`IWorkflowVersionRepository.GetDefinitionSnapshotAsync` já existe — usado
pelo Rollback) em vez do estado atual. Sem header, comportamento legado
preservado.

Endpoints de streaming (`/messages/stream`, `/executions/{id}/stream`,
polling fallback) **não mudam** — apenas relêem o event bus pelo `executionId`,
que já carrega a versão escolhida no trigger.

**Pontos de atenção:**
- Auditoria: adicionar coluna `WorkflowVersionId` em `aihub.workflow_executions`
  (migration aditiva). Sem isso, o cliente que consome via stream não tem como
  saber qual versão rodou — `GET /executions/{id}` precisa expor.
- Validação: 404 quando version não existe; 400 quando o snapshot pertence a
  outro workflow (`version.WorkflowDefinitionId != workflowId`). Mesmo padrão
  já implementado no `RollbackAsync`.
- Tenant boundary: `IWorkflowVersionRepository` herda `HasQueryFilter` por
  tenant — versão de outro tenant some naturalmente (404).
- Pin de agente: o `WorkflowAgentReference.AgentVersionId` já viaja dentro do
  snapshot — agentes pinados resolvem corretamente sem ajuste no runtime.
- Semântica no chat: na primeira entrega, header é por-mensagem (paridade com
  `/trigger`). Se evoluir pra "versão fixa por conversa", basta mover o pin
  pra coluna de `conversations` no futuro.
- Esforço estimado: ~5-6h sênior (3 controllers + service + migration + smoke
  test). Runtime do executor é version-agnostic — recebe `WorkflowDefinition`
  pronto e não muda.
