# ADR 000 — Shape do SDK Microsoft.Extensions.AI / OpenAI: cached_tokens e prompt_cache_key

**Status:** Aceito
**Data:** 2026-04-23
**Fase:** 0 (spike bloqueante pra F1 e F2)

## Context

Antes de implementar a captura de OpenAI prompt caching metrics e roteamento estável
via `prompt_cache_key`, fizemos spike nos SDKs em uso pra evitar retrabalho quando a
assumption não bater com o runtime real.

Packages instalados (verificados em `src/**/*.csproj`):

| Package                            | Versão   |
|------------------------------------|----------|
| `Microsoft.Extensions.AI`          | 10.5.0   |
| `Microsoft.Extensions.AI.Abstractions` | 10.5.0 |
| `Microsoft.Extensions.AI.OpenAI`   | 10.4.0 (transitive) |
| `Microsoft.Agents.AI`              | 1.1.0    |
| `Microsoft.Agents.AI.OpenAI`       | 1.1.0    |
| `OpenAI`                           | 2.10.0 (transitive via Microsoft.Agents.AI.OpenAI) |

O spike cobriu **duas perguntas originais**:

1. Qual é o shape de `cached_tokens` no `UsageDetails` do `Microsoft.Extensions.AI`?
2. Qual caminho fazer pra que `prompt_cache_key` chegue no HTTP do OpenAI?

O `user` param do OpenAI aparece mencionado nos Follow-ups por estar implicado na
mesma camada (adapter `Microsoft.Extensions.AI.OpenAI`), mas a decisão formal sobre
ele fica para um ADR próprio — **não** é escopo deste spike.

## Decision

### 1. `cached_tokens` — capturar via propriedade tipada `CachedInputTokenCount`

`Microsoft.Extensions.AI.UsageDetails` 10.5.0 expõe a propriedade tipada
`CachedInputTokenCount` (`long?`).

**Evidência empírica** (confirma o mapping runtime, não só documentação):

```
$ strings ~/.nuget/packages/microsoft.extensions.ai.openai/10.4.0/lib/net9.0/Microsoft.Extensions.AI.OpenAI.dll | grep -iE 'cachedinput|cachedtoken|inputtokendetails'
get_InputTokenDetails
set_InputTokenDetails
get_CachedTokenCount
set_CachedTokenCount
set_CachedInputTokenCount
```

Os setters/getters no DLL compilado mostram que o adapter lê
`ChatTokenUsage.InputTokenDetails.CachedTokenCount` (OpenAI SDK 2.10.0) e grava em
`UsageDetails.CachedInputTokenCount`. Além disso, a doc XML oficial
(`Microsoft.Extensions.AI.Abstractions.xml` linha 7624) documenta explicitamente:

> `Gets or sets the number of input tokens that were read from a cache.`
> `Cached input tokens should be counted as part of InputTokenCount.`

**Como ler em `TokenTrackingChatClient.TrackUsage`:**

```csharp
var cachedInputTokens = (int)(usage?.CachedInputTokenCount ?? 0);
```

Sem necessidade de reflection ou `AdditionalCounts`. Persistir em
`LlmTokenUsage.CachedTokens` (coluna nova via migration na Fase 1).

### 2. `prompt_cache_key` — **postergar** até SDK suportar

O parâmetro `prompt_cache_key` do OpenAI (introduzido em 2025) **não está
suportado** em `OpenAI` SDK 2.10.0. Issue oficial:
[openai/openai-dotnet#641](https://github.com/openai/openai-dotnet/issues/641)
— status "blocked: spec" desde a introdução da feature na REST API.

Caminhos testados no spike:

- **`ChatOptions.AdditionalProperties["prompt_cache_key"]`** — o adapter
  `Microsoft.Extensions.AI.OpenAI` ignora propriedades arbitrárias; não chega
  no wire. Confirmado por inspeção do source do adapter no repositório
  dotnet/extensions.
- **Reflection em `ChatCompletionOptions.PromptCacheKey`** — o campo **não
  existe** no tipo, nem privado.
- **Decorator custom no `IChatClient` + raw OpenAI client** — viável mas
  quebra abstração do SDK; manutenção cara.

**Decisão:** não implementar roteamento explícito por `prompt_cache_key` agora.
Re-avaliar quando aparecer no changelog do `OpenAI` SDK — esperamos na ordem
de **2.11.x ou 3.x**. Gatilho concreto: monitorar fechamento do issue #641 e
release notes que mencionem "prompt_cache_key".

**Mitigação:** o OpenAI **tenta** rotear por prefixo do prompt quando não há
`prompt_cache_key` explícito. Como já garantimos prefixo invariante no system
message (instructions do agente primeiro, persona depois), devemos ter **cache
hit provável no happy-path** — **não garantido**, porque o roteamento interno
da OpenAI também considera hash de servidor/shard que não controlamos. Em
cenários de alto throughput com load balancer mal distribuído, pode haver
variação. Monitoramos via a métrica de cached_tokens decidida na Decisão 1.

## Consequences

**Positivo:**
- Fase 1 fica mais enxuta: só cached_tokens + OTel tags + métricas órfãs + read audit.
- Evita código frágil com reflection ou wrapper custom sobre OpenAI client puro.
- Cached tokens cobre a pergunta estratégica principal ("o cache da OpenAI está
  funcionando na prática?") — sem `prompt_cache_key` ainda temos visibilidade.

**Negativo:**
- Perdemos controle explícito de shard de cache em cenários de alto volume —
  dependemos da heurística interna da OpenAI, que é best-effort.

**Follow-ups (abrir itens concretos):**
- **PERSONA-OBS-1** — "Revisitar `prompt_cache_key` quando `OpenAI` SDK resolver
  issue [#641](https://github.com/openai/openai-dotnet/issues/641)". Gatilho:
  changelog da versão seguinte à 2.10.0. Será adicionado em `docs/backlog.md`
  no fim da Fase 0.
- **PERSONA-OBS-2** — "Avaliar suporte a `user` param (abuse tracking) no
  adapter `Microsoft.Extensions.AI.OpenAI`". Fora do escopo deste spike.
  Será ADR próprio.

## References

- `src/EfsAiHub.Platform.Runtime/Factories/TokenTrackingChatClient.cs:131-133` — ponto onde cached_tokens será lido.
- `~/.nuget/packages/microsoft.extensions.ai.abstractions/10.5.0/lib/net9.0/Microsoft.Extensions.AI.Abstractions.xml:7624` — doc oficial da propriedade.
- `~/.nuget/packages/microsoft.extensions.ai.openai/10.4.0/lib/net9.0/Microsoft.Extensions.AI.OpenAI.dll` — evidência binária de `set_CachedInputTokenCount`.
- [Microsoft.Extensions.AI.UsageDetails](https://learn.microsoft.com/dotnet/api/microsoft.extensions.ai.usagedetails)
- [openai-dotnet#641 — prompt_cache_key support](https://github.com/openai/openai-dotnet/issues/641)
