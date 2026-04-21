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
            try
            {
                return await base.GetResponseAsync(messages, options, cancellationToken);
            }
            catch (HttpRequestException ex) when (attempt < _maxRetries && IsRetriable(ex))
            {
                RecordRetry(attempt + 1, (int?)ex.StatusCode);
                _logger.LogWarning(
                    "[Retry] Agent={AgentId} Model={ModelId} attempt={Attempt}/{Max} status={StatusCode} delay={DelayMs}ms",
                    _agentId, _modelId, attempt + 1, _maxRetries, (int?)ex.StatusCode, delay.TotalMilliseconds);
                await Task.Delay(delay, cancellationToken);
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
        // Retry apenas na conexão inicial; após o início do streaming, erros não são retentados
        var delay = _initialDelay;
        IAsyncEnumerator<ChatResponseUpdate>? enumerator = null;

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                enumerator = base.GetStreamingResponseAsync(messages, options, cancellationToken)
                    .GetAsyncEnumerator(cancellationToken);
                // Tenta obter o primeiro elemento para verificar a conexão
                if (!await enumerator.MoveNextAsync())
                    yield break;
                break; // Conexão bem-sucedida
            }
            catch (HttpRequestException ex) when (attempt < _maxRetries && IsRetriable(ex))
            {
                if (enumerator is not null) await enumerator.DisposeAsync();
                enumerator = null;
                RecordRetry(attempt + 1, (int?)ex.StatusCode);
                _logger.LogWarning(
                    "[Retry] Agent={AgentId} Model={ModelId} attempt={Attempt}/{Max} status={StatusCode} delay={DelayMs}ms (streaming)",
                    _agentId, _modelId, attempt + 1, _maxRetries, (int?)ex.StatusCode, delay.TotalMilliseconds);
                await Task.Delay(delay, cancellationToken);
                delay = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * _backoffMultiplier);
            }
        }

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

    private void RecordRetry(int attempt, int? statusCode)
    {
        MetricsRegistry.LlmRetries.Add(1,
            new KeyValuePair<string, object?>("agent.id", _agentId),
            new KeyValuePair<string, object?>("model.id", _modelId),
            new KeyValuePair<string, object?>("attempt", attempt),
            new KeyValuePair<string, object?>("status_code", statusCode));

        // Registra evento de retry no span atual para visibilidade no tracing
        Activity.Current?.AddEvent(new ActivityEvent("llm.retry",
            tags: new ActivityTagsCollection
            {
                { "attempt", attempt },
                { "status_code", statusCode },
                { "agent.id", _agentId }
            }));
    }
}
