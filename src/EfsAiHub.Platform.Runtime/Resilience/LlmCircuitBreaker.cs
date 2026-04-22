using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace EfsAiHub.Platform.Runtime.Resilience;

/// <summary>
/// Circuit breaker state machine por provider key (Type:Endpoint).
/// Singleton — compartilhado entre todas as execuções DO MESMO PROCESSO.
/// </summary>
/// <remarks>
/// <para>
/// <b>Limitação:</b> o estado (<see cref="CircuitState"/>) é mantido em
/// <see cref="ConcurrentDictionary{TKey, TValue}"/> em memória do processo. Em deploys multi-pod,
/// cada réplica mantém seu próprio contador de falhas — não há sincronização cross-pod.
/// Isso significa que, para N réplicas, o sistema aceita até N× o
/// <see cref="CircuitBreakerOptions.FailureThreshold"/> antes de todos os pods abrirem.
/// </para>
/// <para>
/// <b>Mitigação atual:</b> use <see cref="CircuitBreakerOptions.EffectiveReplicaCount"/> para
/// dividir proporcionalmente o threshold. Ver docs/plataforma.md (resiliência) para guidance.
/// </para>
/// <para>
/// <b>Backlog:</b> migrar estado para Redis Lua quando atingir ≥4 réplicas.
/// </para>
/// </remarks>
public sealed class LlmCircuitBreaker
{
    private readonly ConcurrentDictionary<string, CircuitState> _states = new();
    private readonly CircuitBreakerOptions _options;
    private readonly ILogger<LlmCircuitBreaker> _logger;

    public LlmCircuitBreaker(IOptions<CircuitBreakerOptions> options, ILogger<LlmCircuitBreaker> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Verifica o estado do circuito. Retorna o status atual, possivelmente transitando para HalfOpen.
    /// </summary>
    public CircuitStatus GetStatus(string providerKey)
    {
        if (!_options.Enabled) return CircuitStatus.Closed;

        var state = _states.GetOrAdd(providerKey, _ => new CircuitState());
        lock (state)
        {
            return state.Status switch
            {
                CircuitStatus.Closed => CircuitStatus.Closed,
                CircuitStatus.Open when DateTime.UtcNow >= state.OpensAt =>
                    TransitionToHalfOpen(state, providerKey),
                CircuitStatus.Open => CircuitStatus.Open,
                CircuitStatus.HalfOpen when state.HalfOpenDeadline.HasValue
                    && DateTime.UtcNow >= state.HalfOpenDeadline =>
                    TransitionToOpen(state, providerKey),
                CircuitStatus.HalfOpen => CircuitStatus.HalfOpen,
                _ => CircuitStatus.Closed
            };
        }
    }

    /// <summary>
    /// Registra uma falha. Se atingir o threshold, abre o circuito.
    /// Deve ser chamado APÓS todas as retries falharem (C5).
    /// </summary>
    public void RecordFailure(string providerKey)
    {
        if (!_options.Enabled) return;

        var state = _states.GetOrAdd(providerKey, _ => new CircuitState());
        lock (state)
        {
            state.ConsecutiveFailures++;

            var effectiveThreshold = Math.Max(1,
                _options.FailureThreshold / Math.Max(1, _options.EffectiveReplicaCount));

            if (state.ConsecutiveFailures >= effectiveThreshold && state.Status == CircuitStatus.Closed)
            {
                state.Status = CircuitStatus.Open;
                state.OpensAt = DateTime.UtcNow.AddSeconds(_options.OpenDurationSeconds);

                _logger.LogWarning(
                    "[CircuitBreaker] OPEN for provider '{ProviderKey}' after {Failures} consecutive failures " +
                    "(effectiveThreshold={EffectiveThreshold}, replicas={Replicas}). " +
                    "Will try half-open at {OpensAt:HH:mm:ss}.",
                    providerKey, state.ConsecutiveFailures, effectiveThreshold,
                    _options.EffectiveReplicaCount, state.OpensAt);

                MetricsRegistry.CircuitBreakerOpened.Add(1,
                    new KeyValuePair<string, object?>("provider", providerKey),
                    new KeyValuePair<string, object?>("effective_threshold", effectiveThreshold),
                    new KeyValuePair<string, object?>("replicas", _options.EffectiveReplicaCount));
            }

            // Em HalfOpen, uma falha reabre o circuito
            if (state.Status == CircuitStatus.HalfOpen)
            {
                state.Status = CircuitStatus.Open;
                state.OpensAt = DateTime.UtcNow.AddSeconds(_options.OpenDurationSeconds);

                _logger.LogWarning(
                    "[CircuitBreaker] RE-OPENED for provider '{ProviderKey}' (half-open probe failed).",
                    providerKey);
            }
        }
    }

    /// <summary>
    /// Registra um sucesso. Reseta o circuito para Closed.
    /// </summary>
    public void RecordSuccess(string providerKey)
    {
        if (!_options.Enabled) return;

        var state = _states.GetOrAdd(providerKey, _ => new CircuitState());
        lock (state)
        {
            if (state.ConsecutiveFailures > 0 || state.Status != CircuitStatus.Closed)
            {
                _logger.LogInformation(
                    "[CircuitBreaker] CLOSED for provider '{ProviderKey}' (success after {Failures} failures).",
                    providerKey, state.ConsecutiveFailures);
            }

            state.ConsecutiveFailures = 0;
            state.Status = CircuitStatus.Closed;
            state.OpensAt = null;
            state.HalfOpenDeadline = null;
        }
    }

    /// <summary>Retorna o estado para diagnóstico/endpoints de health.</summary>
    public IReadOnlyDictionary<string, CircuitState> GetAllStates()
        => _states.ToDictionary(kv => kv.Key, kv => kv.Value);

    private CircuitStatus TransitionToHalfOpen(CircuitState state, string providerKey)
    {
        state.Status = CircuitStatus.HalfOpen;
        state.HalfOpenDeadline = DateTime.UtcNow.AddSeconds(_options.HalfOpenTimeoutSeconds);

        _logger.LogInformation(
            "[CircuitBreaker] HALF-OPEN for provider '{ProviderKey}'. " +
            "Probing with next request (deadline: {Deadline:HH:mm:ss}).",
            providerKey, state.HalfOpenDeadline);

        return CircuitStatus.HalfOpen;
    }

    private CircuitStatus TransitionToOpen(CircuitState state, string providerKey)
    {
        state.Status = CircuitStatus.Open;
        state.OpensAt = DateTime.UtcNow.AddSeconds(_options.OpenDurationSeconds);

        _logger.LogWarning(
            "[CircuitBreaker] RE-OPENED for provider '{ProviderKey}' (half-open deadline expired).",
            providerKey);

        return CircuitStatus.Open;
    }
}
