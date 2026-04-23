# Backlog — EfsAiHub

Itens gerados por decisões arquiteturais (ADRs) e reviews ao longo da
implementação. Cada entrada tem um ID estável, um gatilho concreto de
re-avaliação e referência ao contexto onde foi decidida.

---

## Persona — Observabilidade & SDK

### PERSONA-OBS-1 — `prompt_cache_key` quando OpenAI SDK suportar

**Origem:** [ADR 000](adr/000-opensdk-shape.md) — Decisão 2.

**Contexto:** O parâmetro `prompt_cache_key` do OpenAI (2025) não está
suportado em `OpenAI` SDK 2.10.0 — issue oficial
[openai/openai-dotnet#641](https://github.com/openai/openai-dotnet/issues/641)
marcada como "blocked: spec". Hoje usamos prefixo invariante no system
message como mitigação best-effort.

**Trabalho:** quando o SDK expuser `prompt_cache_key` nativamente,
popular o campo em `ChatOptions` no `ChatOptionsBuilder.BuildCoreOptions`
com valor estável `hash(tenantId + agentId)` pra forçar stickiness de
shard.

**Gatilho:** release do `OpenAI` SDK que feche o issue #641 (esperado
2.11.x ou 3.x). Monitorar changelog.

**Esforço estimado:** 2h (spike) + 2h (implementação).

---

### PERSONA-OBS-2 — Avaliar suporte a `user` param (abuse tracking)

**Origem:** [ADR 000](adr/000-opensdk-shape.md) — mencionado em follow-ups.

**Contexto:** OpenAI aceita `user` param no body (`hash(userId)` recomendado
pra abuse-tracking). Tipo `ChatCompletionOptions.EndUserId` existe em
`OpenAI` SDK 2.10.0, mas o adapter `Microsoft.Extensions.AI.OpenAI`
10.4.0 não expõe rota pra setá-lo via `ChatOptions`.

**Trabalho:** investigar se vale criar um decorator custom sobre
`IChatClient` que injete `EndUserId` no `ChatCompletionOptions` antes
de enviar. ADR próprio com decisão.

**Gatilho:** a qualquer momento — requisito de compliance/abuse-tracking
pode tornar prioritário.

**Esforço estimado:** 1d.

---
