namespace EfsAiHub.Platform.Runtime.Resilience;

/// <summary>
/// Estado do circuit breaker para um provider key específico.
/// </summary>
public enum CircuitStatus
{
    Closed,
    Open,
    HalfOpen
}

/// <summary>
/// Estado mutável de um circuito individual. Thread-safe via lock no <see cref="LlmCircuitBreaker"/>.
/// </summary>
public sealed class CircuitState
{
    public CircuitStatus Status { get; set; } = CircuitStatus.Closed;
    public int ConsecutiveFailures { get; set; }
    public DateTime? OpensAt { get; set; }
    public DateTime? HalfOpenDeadline { get; set; }
}
