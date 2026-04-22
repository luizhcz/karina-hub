namespace EfsAiHub.Core.Agents;

/// <summary>
/// Fase 2 — política de resiliência por agente. Substitui os valores hard-coded
/// em <see cref="Engine.Factories.RetryingChatClient"/>. Aplicada pelo AgentFactory
/// ao montar a pipeline de IChatClient do snapshot de <see cref="AgentVersion"/>.
/// </summary>
/// <param name="MaxRetries">Máximo de tentativas adicionais após a primeira falha.</param>
/// <param name="InitialDelayMs">Delay inicial em ms para backoff exponencial entre retries.</param>
/// <param name="BackoffMultiplier">Multiplicador aplicado a cada retry (ex: 2.0 dobra o delay).</param>
/// <param name="RetriableHttpStatusCodes">Status codes que disparam retry; null usa default (429/500/502/503).</param>
/// <param name="CallTimeoutMs">
/// Timeout máximo em ms para uma única chamada LLM (NÃO acumulado em retries).
/// Null ou ≤0 → sem timeout adicional, apenas o CancellationToken externo governa.
/// Recomendação produção: 60000 (60s) para chamadas não-streaming.
/// </param>
/// <param name="JitterRatio">
/// Fração de jitter aplicada ao delay de backoff para evitar thundering herd (0.0 a 1.0).
/// Ex: JitterRatio=0.1 adiciona até 10% de jitter aleatório sobre o delay calculado.
/// Default 0.0 = backoff determinístico (compat com comportamento legado).
/// Recomendação produção: 0.1.
/// </param>
public sealed record ResiliencePolicy(
    int MaxRetries = 3,
    int InitialDelayMs = 1000,
    double BackoffMultiplier = 2.0,
    IReadOnlyList<int>? RetriableHttpStatusCodes = null,
    int? CallTimeoutMs = null,
    double JitterRatio = 0.0)
{
    public static ResiliencePolicy Default { get; } = new();
}

/// <summary>
/// Fase 2 — orçamento de custo em USD por execução. Calculado incrementalmente no
/// TokenTrackingChatClient a partir de <see cref="Domain.Observability.ModelPricing"/>.
/// Null = sem enforcement de custo (mantém comportamento legado só com tokens).
/// </summary>
public sealed record AgentCostBudget(decimal MaxCostUsd);
