namespace EfsAiHub.Core.Agents;

/// <summary>
/// Fase 2 — política de resiliência por agente. Substitui os valores hard-coded
/// em <see cref="Engine.Factories.RetryingChatClient"/>. Aplicada pelo AgentFactory
/// ao montar a pipeline de IChatClient do snapshot de <see cref="AgentVersion"/>.
/// </summary>
public sealed record ResiliencePolicy(
    int MaxRetries = 3,
    int InitialDelayMs = 1000,
    double BackoffMultiplier = 2.0,
    IReadOnlyList<int>? RetriableHttpStatusCodes = null)
{
    public static ResiliencePolicy Default { get; } = new();
}

/// <summary>
/// Fase 2 — orçamento de custo em USD por execução. Calculado incrementalmente no
/// TokenTrackingChatClient a partir de <see cref="Domain.Observability.ModelPricing"/>.
/// Null = sem enforcement de custo (mantém comportamento legado só com tokens).
/// </summary>
public sealed record AgentCostBudget(decimal MaxCostUsd);
