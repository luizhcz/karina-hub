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

## 4. Consumir workflow de versão específica — header `x-version` ✅

**Status:** entregue. Header opcional `x-version: <workflowVersionId>`
suportado em `POST /workflows/{id}/trigger`, `POST /workflows/{id}/sandbox`
e `POST /conversations/{id}/messages`. Quando presente, a execução roda
contra o snapshot append-only (canary/A/B). Coluna desnormalizada
`WorkflowVersionId` em `aihub.workflow_executions` + index parcial; campo
exposto em `GET /executions/{id}` e `/full`. Validações: 404 (version
inexistente), 400 (version de outro workflow), tenant boundary preservado
via `HasQueryFilter` no workflow + validação cruzada.

**Limitação conhecida (follow-up):** `HitlRecoveryService` retoma
execuções pinadas usando o estado mutável atual em vez do snapshot — issue
de recovery a tratar quando virar caso real.
