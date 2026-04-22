namespace EfsAiHub.Platform.Runtime.Resilience;

/// <summary>
/// Configuração do circuit breaker por provider LLM.
/// </summary>
public class CircuitBreakerOptions
{
    public const string SectionName = "LlmCircuitBreaker";

    /// <summary>Número de falhas consecutivas para abrir o circuito. Default: 5.</summary>
    public int FailureThreshold { get; init; } = 5;

    /// <summary>Duração em segundos que o circuito permanece aberto antes de tentar half-open. Default: 30.</summary>
    public int OpenDurationSeconds { get; init; } = 30;

    /// <summary>Timeout em segundos para a tentativa half-open. Default: 10.</summary>
    public int HalfOpenTimeoutSeconds { get; init; } = 10;

    /// <summary>Habilita o circuit breaker. Default: true.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Número efetivo de réplicas (pods) rodando em paralelo. Usado para dividir o
    /// <see cref="FailureThreshold"/> entre os pods, já que o estado do circuit breaker
    /// é mantido em memória per-process (não compartilhado via Redis).
    /// Default: 1. Ajustar para N em deploys multi-pod — o threshold efetivo vira
    /// <c>max(1, FailureThreshold / EffectiveReplicaCount)</c>.
    /// Backlog: migrar estado para Redis Lua quando atingir ≥4 réplicas.
    /// </summary>
    public int EffectiveReplicaCount { get; init; } = 1;
}
