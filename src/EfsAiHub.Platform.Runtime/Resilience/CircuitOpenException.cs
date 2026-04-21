namespace EfsAiHub.Platform.Runtime.Resilience;

/// <summary>
/// Lançada quando o circuit breaker está aberto e não há fallback provider configurado (R3).
/// </summary>
public sealed class CircuitOpenException : InvalidOperationException
{
    public string ProviderKey { get; }
    public DateTime? RetryAfter { get; }

    public CircuitOpenException(string providerKey, DateTime? retryAfter)
        : base($"Circuit breaker OPEN for provider '{providerKey}'. " +
               (retryAfter.HasValue ? $"Retry after {retryAfter:HH:mm:ss} UTC." : "No fallback configured."))
    {
        ProviderKey = providerKey;
        RetryAfter = retryAfter;
    }
}
