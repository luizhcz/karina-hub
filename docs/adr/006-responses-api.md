# ADR 006 — Responses API: defer até Extensions.AI consolidar

**Status:** Aceito (spike / decisão de não migrar agora)
**Data:** 2026-04-23
**Fase:** 7 (rebaixada de POC pra ADR)

## Context

A OpenAI lançou a **Responses API** (2025) como sucessor designado da
Chat Completions pra agentes LLM. Promete:

- `previous_response_id` pra conversação multi-turn sem reenvio do
  histórico (state server-side);
- `store:true` persistindo responses no lado OpenAI (referenciáveis
  por ID depois);
- **Server-side tool loop**: modelo chama tools iterativamente em um
  request único sem round-trip cliente-servidor entre toolcalls;
- **Built-in tools**: `web_search`, `file_search`, `computer_use`;
- `reasoning_effort` e reasoning tokens em modelos reasoning;
- **`prompt_cache_key` nativo** (resolve o workaround do [ADR 000](000-opensdk-shape.md)
  de prefixo invariante).

O plano de refinamento original previa F7 como **POC** (1 agente
isolado usando Responses API pra validar). O validador externo do plano
vetou:

> "POC em 1 agente sem tools não prova nada porque perde exatamente o
> que justifica Responses API (state + server-side tool loop)."

Rebaixamos pra ADR sem implementação.

## Decision

**Manter Chat Completions API via `Microsoft.Extensions.AI.IChatClient`
como contract único de LLM pelos próximos 2 quarters. Revisitar no Q3
2026** ou quando um dos gatilhos abaixo disparar.

## Racional da decisão

### 1. Ecossistema .NET ainda em transição

- **`Microsoft.Extensions.AI` 10.5.0**: expõe `MicrosoftExtensionsAIResponsesExtensions`
  (extension methods `AsOpenAIResponseItems`) pra conversão de tipos,
  mas **não substitui `IChatClient`**. O contrato de `IChatClient`
  continua amarrado a Chat Completions.
- **`Microsoft.Agents.AI.Hosting.OpenAI` 1.1.0**: suporta ambos os
  endpoints mas o roteamento é manual — o caller escolhe qual usar,
  não há abstração unificada.
- **`OpenAI` SDK 2.10+**: cliente Responses estável
  (`client.Responses.CreateAsync(CreateResponseOptions)`), mas dual —
  coexiste com `client.Chat.Completions.CreateAsync()` sem abstração.

Issues abertas que confirmam a transição ainda é quebradiça:
- [dotnet/extensions#7060](https://github.com/dotnet/extensions/issues/7060)
  — `WebSearchTool` intermitente em cenários reais.
- [dotnet/extensions#6753](https://github.com/dotnet/extensions/issues/6753)
  — "Tool_calls must be followed by tool messages" quando
  `GetResponseAsync` adapta Responses → IChatClient.

### 2. Custo de arquitetura é desproporcional ao payoff imediato

Nosso stack é **100% `IChatClient`**. Todo fluxo de agent passa por:

```
AgentFactory → ChatOptionsBuilder.BuildCoreOptions → IChatClient
  ↳ wrappers: TokenTrackingChatClient (F1), AccountGuardChatClient,
              StructuredOutputStateChatClient
```

Migrar pra Responses exige escrever um `IResponsesClient` custom (ou
uma impl de `IChatClient` que internamente chame `client.Responses.*`)
e traduzir:

- `ChatOptions` → `CreateResponseOptions` (shape diferente);
- `ChatMessage[]` → `ResponseItem[]`;
- Conversar com o state: `previous_response_id` exige rastrear o ID
  em cada turn, o que significa persistir em `ConversationSession` e
  revalidar contra nossos próprios checkpoints;
- Refazer os wrappers (token tracking, guards) pra entender o novo
  formato.

Budget estimado: **~2 semanas (1 dev)** pro wrap básico, sem
aproveitar server-side tool loop (que exige rearquitetar o flow de
tools que hoje usa `Microsoft.Agents.AI.Workflows`).

### 3. O que já temos cobre 80% do valor

- **Prompt caching via prefixo invariante** ([ADR 000](000-opensdk-shape.md)):
  hit rate em produção já cobre o benefício do `prompt_cache_key` sem
  mudar API.
- **Tool calling via Agents.AI**: tools já funcionam em loop no cliente
  com retry/budget/observabilidade. Server-side tool loop da Responses
  seria mais enxuto mas não resolve problema atual em aberto.
- **Cached tokens** (F1) já capturados em `llm_token_usage.CachedTokens`
  — mesma métrica que tiraríamos da Responses.

### 4. `store:true` levanta questão de data residency

Requisito compliance do produto (LGPD + cliente financeiro
regulado) faz com que persistir conversas na OpenAI via `store:true`
exija revisão legal. Escopo não coberto no MVP.

## Gatilhos de revisão (quando reabrir)

1. **Extensions.AI expõe `IChatClient` alternativo pra Responses API**
   (sem necessidade de wrap custom). Monitorar
   [dotnet/extensions releases](https://github.com/dotnet/extensions/releases).
2. **Microsoft.Agents.AI 2.x** consolida Chat Completions + Responses
   em abstração unificada.
3. **Feature OpenAI-only** que exigimos (ex: reasoning tokens mais
   ricos, `computer_use` em produção) se tornar business-critical.
4. **Stream de tools multi-turn** começar a dominar arquitetura (hoje
   não está — workflows estáticos via `Microsoft.Agents.AI.Workflows`
   cobrem a maioria dos casos de uso).

## Consequences

### Positive
- Zero re-arquitetura agora — time continua entregando valor em
  outras frentes (F6 A/B testing, F8 i18n).
- Risco técnico absorvido pela Microsoft: quando Extensions.AI tiver
  suporte nativo, upgrade fica na camada da library e nós herdamos.
- ADR deixa critério explícito pra revisão — não fica "esquecido".

### Negative
- Continuamos sem `prompt_cache_key` nativo (mitigação de ADR 000
  segue vigente).
- Sem `store:true` / server-side state, precisamos manter
  `ConversationSession` rica no nosso lado (já temos).
- Se outra plataforma migrar primeiro e nossa UX começar a parecer
  "antiquada" em tool loops, pressão de produto pode antecipar
  decisão.

## Entrega dessa fase

Esta ADR **é** a entrega de F7. Nada mais.

## Referências

- [Microsoft.Extensions.AI 10.5.0 NuGet](https://www.nuget.org/packages/Microsoft.Extensions.AI.OpenAI)
- [MicrosoftExtensionsAIResponsesExtensions — Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/api/openai.responses.microsoftextensionsairesponsesextensions?view=net-10.0-pp)
- [Migrate to the Responses API — OpenAI Docs](https://platform.openai.com/docs/guides/migrate-to-responses)
- [Conversation State — OpenAI API](https://developers.openai.com/api/docs/guides/conversation-state)
- [Issue #7060 — dotnet/extensions](https://github.com/dotnet/extensions/issues/7060)
- [Issue #6753 — dotnet/extensions](https://github.com/dotnet/extensions/issues/6753)
- [openai/openai-dotnet CHANGELOG](https://github.com/openai/openai-dotnet/blob/main/CHANGELOG.md)
