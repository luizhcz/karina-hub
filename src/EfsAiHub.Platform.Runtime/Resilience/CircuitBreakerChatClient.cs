using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace EfsAiHub.Platform.Runtime.Resilience;

/// <summary>
/// DelegatingChatClient que intercepta chamadas LLM e aplica o circuit breaker.
/// Posição na cadeia (C5): Retry → Circuit → Inner.
///
/// Quando o circuito está aberto:
///   - Se há fallback configurado (tipo diferente do primary — R3): redireciona para o fallback.
///   - Senão: throw <see cref="CircuitOpenException"/> (fail-fast).
///
/// Quando HalfOpen: permite 1 request como probe. Sucesso = fecha; falha = reabre.
/// </summary>
public class CircuitBreakerChatClient : DelegatingChatClient
{
    private readonly LlmCircuitBreaker _circuitBreaker;
    private readonly string _providerKey;
    private readonly IChatClient? _fallbackClient;
    private readonly string? _fallbackProviderType;
    private readonly ILogger _logger;

    public CircuitBreakerChatClient(
        IChatClient innerClient,
        LlmCircuitBreaker circuitBreaker,
        string providerKey,
        ILogger logger,
        IChatClient? fallbackClient = null,
        string? fallbackProviderType = null)
        : base(innerClient)
    {
        _circuitBreaker = circuitBreaker;
        _providerKey = providerKey;
        _logger = logger;
        _fallbackClient = fallbackClient;
        _fallbackProviderType = fallbackProviderType;
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var status = _circuitBreaker.GetStatus(_providerKey);

        if (status == CircuitStatus.Open)
            return await HandleOpenCircuit(messages, options, cancellationToken);

        try
        {
            var response = await base.GetResponseAsync(messages, options, cancellationToken);
            _circuitBreaker.RecordSuccess(_providerKey);
            return response;
        }
        catch (Exception ex) when (IsTransient(ex))
        {
            _circuitBreaker.RecordFailure(_providerKey);
            throw;
        }
    }

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var status = _circuitBreaker.GetStatus(_providerKey);

        if (status == CircuitStatus.Open)
            return HandleOpenCircuitStreaming(messages, options, cancellationToken);

        return StreamWithCircuitBreaker(messages, options, cancellationToken);
    }

    private async IAsyncEnumerable<ChatResponseUpdate> StreamWithCircuitBreaker(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        IAsyncEnumerator<ChatResponseUpdate>? enumerator = null;
        try
        {
            enumerator = base.GetStreamingResponseAsync(messages, options, cancellationToken)
                .GetAsyncEnumerator(cancellationToken);

            // Probe na primeira mensagem
            bool hasFirst;
            try
            {
                hasFirst = await enumerator.MoveNextAsync();
            }
            catch (Exception ex) when (IsTransient(ex))
            {
                _circuitBreaker.RecordFailure(_providerKey);
                throw;
            }

            if (!hasFirst)
            {
                _circuitBreaker.RecordSuccess(_providerKey);
                yield break;
            }

            _circuitBreaker.RecordSuccess(_providerKey);
            yield return enumerator.Current;

            while (await enumerator.MoveNextAsync())
                yield return enumerator.Current;
        }
        finally
        {
            if (enumerator is not null) await enumerator.DisposeAsync();
        }
    }

    private async Task<ChatResponse> HandleOpenCircuit(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options,
        CancellationToken cancellationToken)
    {
        if (_fallbackClient is not null)
        {
            _logger.LogWarning(
                "[CircuitBreaker] Open for '{ProviderKey}' — routing to fallback '{Fallback}'.",
                _providerKey, _fallbackProviderType);

            MetricsRegistry.CircuitBreakerFallbacks.Add(1,
                new KeyValuePair<string, object?>("primary", _providerKey),
                new KeyValuePair<string, object?>("fallback", _fallbackProviderType ?? "unknown"));

            return await _fallbackClient.GetResponseAsync(messages, options, cancellationToken);
        }

        MetricsRegistry.CircuitBreakerRejected.Add(1,
            new KeyValuePair<string, object?>("provider", _providerKey));

        throw new CircuitOpenException(_providerKey, null);
    }

    private async IAsyncEnumerable<ChatResponseUpdate> HandleOpenCircuitStreaming(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (_fallbackClient is not null)
        {
            _logger.LogWarning(
                "[CircuitBreaker] Open for '{ProviderKey}' — routing streaming to fallback '{Fallback}'.",
                _providerKey, _fallbackProviderType);

            MetricsRegistry.CircuitBreakerFallbacks.Add(1,
                new KeyValuePair<string, object?>("primary", _providerKey),
                new KeyValuePair<string, object?>("fallback", _fallbackProviderType ?? "unknown"));

            await foreach (var update in _fallbackClient.GetStreamingResponseAsync(messages, options, cancellationToken))
                yield return update;
            yield break;
        }

        MetricsRegistry.CircuitBreakerRejected.Add(1,
            new KeyValuePair<string, object?>("provider", _providerKey));

        throw new CircuitOpenException(_providerKey, null);
    }

    private static bool IsTransient(Exception ex)
    {
        // Defesa explícita: violações de política nunca são transientes — sobem direto
        // sem RecordFailure pra evitar abrir o circuito por causa de blocklist (que é
        // determinístico: mesmo input vai dar mesma violação independente do provider).
        if (ex is EfsAiHub.Platform.Runtime.Guards.BlocklistViolationException) return false;

        return ex is HttpRequestException { StatusCode: System.Net.HttpStatusCode.TooManyRequests
            or System.Net.HttpStatusCode.InternalServerError
            or System.Net.HttpStatusCode.BadGateway
            or System.Net.HttpStatusCode.ServiceUnavailable };
    }
}
