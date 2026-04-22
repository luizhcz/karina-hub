using System.Diagnostics;
using System.Net;
using System.Runtime.CompilerServices;
using EfsAiHub.Core.Agents;
using Microsoft.Extensions.AI;

namespace EfsAiHub.Platform.Runtime.Factories;

/// <summary>
/// DelegatingChatClient que aplica retry com exponential backoff em erros transientes do LLM
/// (HTTP 429, 500, 502, 503). Registra métricas de retry no OTel.
/// </summary>
public class RetryingChatClient : DelegatingChatClient
{
    private readonly string _agentId;
    private readonly string _modelId;
    private readonly int _maxRetries;
    private readonly TimeSpan _initialDelay;
    private readonly double _backoffMultiplier;
    private readonly TimeSpan? _callTimeout;
    private readonly double _jitterRatio;
    private readonly HashSet<HttpStatusCode> _retriableStatusCodes;
    private readonly ILogger _logger;

    private static readonly HashSet<HttpStatusCode> DefaultRetriableStatusCodes =
    [
        HttpStatusCode.TooManyRequests,        // 429
        HttpStatusCode.InternalServerError,    // 500
        HttpStatusCode.BadGateway,             // 502
        HttpStatusCode.ServiceUnavailable      // 503
    ];

    public RetryingChatClient(
        IChatClient innerClient,
        string agentId,
        string modelId,
        ILogger logger,
        ResiliencePolicy? policy = null)
        : base(innerClient)
    {
        _agentId = agentId;
        _modelId = modelId;
        _logger = logger;

        var p = policy ?? ResiliencePolicy.Default;
        _maxRetries = p.MaxRetries;
        _initialDelay = TimeSpan.FromMilliseconds(p.InitialDelayMs);
        _backoffMultiplier = p.BackoffMultiplier;
        _callTimeout = p.CallTimeoutMs is > 0
            ? TimeSpan.FromMilliseconds(p.CallTimeoutMs.Value)
            : null;
        _jitterRatio = Math.Clamp(p.JitterRatio, 0.0, 1.0);
        _retriableStatusCodes = p.RetriableHttpStatusCodes is { Count: > 0 }
            ? new HashSet<HttpStatusCode>(p.RetriableHttpStatusCodes.Select(c => (HttpStatusCode)c))
            : DefaultRetriableStatusCodes;
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var delay = _initialDelay;
        for (var attempt = 0; ; attempt++)
        {
            // Timeout per-call (não acumulado em retries) — se o provider travar, abortamos
            // a chamada específica e entramos no retry em vez de segurar o slot do workflow.
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (_callTimeout is { } t) linkedCts.CancelAfter(t);

            try
            {
                return await base.GetResponseAsync(messages, options, linkedCts.Token);
            }
            catch (OperationCanceledException) when (
                _callTimeout is not null
                && !cancellationToken.IsCancellationRequested
                && linkedCts.IsCancellationRequested
                && attempt < _maxRetries)
            {
                RecordRetry(attempt + 1, statusCode: null, timeoutTriggered: true);
                _logger.LogWarning(
                    "[Retry] Agent={AgentId} Model={ModelId} attempt={Attempt}/{Max} TIMEOUT ({TimeoutMs}ms) delay={DelayMs}ms",
                    _agentId, _modelId, attempt + 1, _maxRetries, _callTimeout.Value.TotalMilliseconds, delay.TotalMilliseconds);
                await Task.Delay(ApplyJitter(delay), cancellationToken);
                delay = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * _backoffMultiplier);
            }
            catch (HttpRequestException ex) when (attempt < _maxRetries && IsRetriable(ex))
            {
                RecordRetry(attempt + 1, (int?)ex.StatusCode, timeoutTriggered: false);
                _logger.LogWarning(
                    "[Retry] Agent={AgentId} Model={ModelId} attempt={Attempt}/{Max} status={StatusCode} delay={DelayMs}ms",
                    _agentId, _modelId, attempt + 1, _maxRetries, (int?)ex.StatusCode, delay.TotalMilliseconds);
                await Task.Delay(ApplyJitter(delay), cancellationToken);
                delay = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * _backoffMultiplier);
            }
        }
    }

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return StreamWithRetryAsync(messages, options, cancellationToken);
    }

    private async IAsyncEnumerable<ChatResponseUpdate> StreamWithRetryAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Retry apenas na conexão inicial; após o início do streaming, erros não são retentados.
        // Timeout per-call cobre só a fase de conexão — após primeiro token, o stream continua
        // sob o CancellationToken externo (streams legítimos podem durar minutos).
        var delay = _initialDelay;
        IAsyncEnumerator<ChatResponseUpdate>? enumerator = null;

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (_callTimeout is { } t) linkedCts.CancelAfter(t);

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                enumerator = base.GetStreamingResponseAsync(messages, options, linkedCts.Token)
                    .GetAsyncEnumerator(linkedCts.Token);
                // Tenta obter o primeiro elemento para verificar a conexão
                if (!await enumerator.MoveNextAsync())
                    yield break;
                break; // Conexão bem-sucedida
            }
            catch (OperationCanceledException) when (
                _callTimeout is not null
                && !cancellationToken.IsCancellationRequested
                && linkedCts.IsCancellationRequested
                && attempt < _maxRetries)
            {
                if (enumerator is not null) await enumerator.DisposeAsync();
                enumerator = null;
                RecordRetry(attempt + 1, statusCode: null, timeoutTriggered: true);
                _logger.LogWarning(
                    "[Retry] Agent={AgentId} Model={ModelId} attempt={Attempt}/{Max} TIMEOUT ({TimeoutMs}ms) delay={DelayMs}ms (streaming)",
                    _agentId, _modelId, attempt + 1, _maxRetries, _callTimeout.Value.TotalMilliseconds, delay.TotalMilliseconds);
                await Task.Delay(ApplyJitter(delay), cancellationToken);
                delay = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * _backoffMultiplier);
                // Rearma o deadline para a próxima tentativa
                linkedCts.CancelAfter(_callTimeout.Value);
            }
            catch (HttpRequestException ex) when (attempt < _maxRetries && IsRetriable(ex))
            {
                if (enumerator is not null) await enumerator.DisposeAsync();
                enumerator = null;
                RecordRetry(attempt + 1, (int?)ex.StatusCode, timeoutTriggered: false);
                _logger.LogWarning(
                    "[Retry] Agent={AgentId} Model={ModelId} attempt={Attempt}/{Max} status={StatusCode} delay={DelayMs}ms (streaming)",
                    _agentId, _modelId, attempt + 1, _maxRetries, (int?)ex.StatusCode, delay.TotalMilliseconds);
                await Task.Delay(ApplyJitter(delay), cancellationToken);
                delay = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * _backoffMultiplier);
            }
        }

        // Conexão estabelecida — desliga o deadline de timeout para permitir streams longos.
        if (_callTimeout is not null)
            linkedCts.CancelAfter(Timeout.InfiniteTimeSpan);

        // Emite o primeiro elemento e todos os restantes
        try
        {
            yield return enumerator!.Current;
            while (await enumerator.MoveNextAsync())
                yield return enumerator.Current;
        }
        finally
        {
            if (enumerator is not null) await enumerator.DisposeAsync();
        }
    }

    private bool IsRetriable(HttpRequestException ex)
        => ex.StatusCode.HasValue && _retriableStatusCodes.Contains(ex.StatusCode.Value);

    /// <summary>
    /// Aplica jitter aleatório ao delay de backoff para reduzir thundering herd.
    /// Internal para testabilidade — expõe o cálculo determinístico a partir de Random.Shared.
    /// </summary>
    internal TimeSpan ApplyJitter(TimeSpan delay)
    {
        if (_jitterRatio <= 0.0) return delay;
        var maxJitterMs = (int)(delay.TotalMilliseconds * _jitterRatio);
        if (maxJitterMs <= 0) return delay;
        var jitterMs = Random.Shared.Next(0, maxJitterMs + 1);
        return TimeSpan.FromMilliseconds(delay.TotalMilliseconds + jitterMs);
    }

    private void RecordRetry(int attempt, int? statusCode, bool timeoutTriggered)
    {
        MetricsRegistry.LlmRetries.Add(1,
            new KeyValuePair<string, object?>("agent.id", _agentId),
            new KeyValuePair<string, object?>("model.id", _modelId),
            new KeyValuePair<string, object?>("attempt", attempt),
            new KeyValuePair<string, object?>("status_code", statusCode),
            new KeyValuePair<string, object?>("timeout_triggered", timeoutTriggered));

        // Registra evento de retry no span atual para visibilidade no tracing
        Activity.Current?.AddEvent(new ActivityEvent("llm.retry",
            tags: new ActivityTagsCollection
            {
                { "attempt", attempt },
                { "status_code", statusCode },
                { "timeout_triggered", timeoutTriggered },
                { "agent.id", _agentId }
            }));
    }
}
