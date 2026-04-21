using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace EfsAiHub.Infra.Observability.Services;

/// <summary>
/// Acumula eventos de token por executionId e publica em batch a cada <see cref="FlushIntervalMs"/>ms.
/// Reduz operações NOTIFY do PostgreSQL de ~2000/execução para ~30-50/execução.
/// </summary>
public sealed class TokenBatcher : IAsyncDisposable
{
    private readonly IWorkflowEventBus _eventBus;
    private readonly ILogger<TokenBatcher> _logger;
    private readonly ConcurrentDictionary<string, BatchState> _batches = new();
    private readonly int _flushIntervalMs;

    public int FlushIntervalMs => _flushIntervalMs;

    public TokenBatcher(
        IWorkflowEventBus eventBus,
        ILogger<TokenBatcher> logger,
        int flushIntervalMs = 75)
    {
        _eventBus = eventBus;
        _logger = logger;
        _flushIntervalMs = flushIntervalMs;
    }

    /// <summary>
    /// Acumula um token para publicação em batch.
    /// </summary>
    public void Enqueue(string executionId, string? agentId, string tokenText)
    {
        var state = _batches.GetOrAdd(executionId, id => new BatchState(id, this));

        lock (state.Lock)
        {
            // Se o agentId mudou, flush o batch anterior antes de acumular
            if (state.AgentId is not null && state.AgentId != agentId)
            {
                _ = FlushStateAsync(state);
            }

            state.AgentId = agentId;
            state.Buffer.Append(tokenText);
            state.EnsureTimerStarted();
        }
    }

    /// <summary>
    /// Força flush de todos os tokens acumulados para um executionId.
    /// Deve ser chamado antes de publicar eventos de controle (não-token).
    /// </summary>
    public async Task FlushAsync(string executionId)
    {
        if (_batches.TryGetValue(executionId, out var state))
        {
            await FlushStateAsync(state);
        }
    }

    /// <summary>
    /// Remove o estado de batch para um executionId finalizado.
    /// </summary>
    public async Task RemoveAsync(string executionId)
    {
        if (_batches.TryRemove(executionId, out var state))
        {
            await FlushStateAsync(state);
            state.DisposeTimer();
        }
    }

    private async Task FlushStateAsync(BatchState state)
    {
        string? text;
        string? agentId;

        lock (state.Lock)
        {
            if (state.Buffer.Length == 0)
                return;

            text = state.Buffer.ToString();
            agentId = state.AgentId;
            state.Buffer.Clear();
            state.StopTimer();
        }

        try
        {
            var envelope = new WorkflowEventEnvelope
            {
                EventType = "token",
                ExecutionId = state.ExecutionId,
                Payload = JsonSerializer.Serialize(new { agentId, text })
            };
            await _eventBus.PublishAsync(state.ExecutionId, envelope);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[TokenBatcher] Falha ao publicar batch de tokens para execução '{ExecutionId}'.",
                state.ExecutionId);
        }
    }

    private void OnTimerFired(object? obj)
    {
        if (obj is BatchState state)
        {
            _ = FlushStateAsync(state);
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var kvp in _batches)
        {
            await FlushStateAsync(kvp.Value);
            kvp.Value.DisposeTimer();
        }
        _batches.Clear();
    }

    private sealed class BatchState
    {
        public string ExecutionId { get; }
        public StringBuilder Buffer { get; } = new();
        public string? AgentId { get; set; }
        public object Lock { get; } = new();

        private Timer? _timer;
        private readonly TokenBatcher _owner;

        public BatchState(string executionId, TokenBatcher owner)
        {
            ExecutionId = executionId;
            _owner = owner;
        }

        public void EnsureTimerStarted()
        {
            _timer ??= new Timer(_owner.OnTimerFired, this, _owner._flushIntervalMs, Timeout.Infinite);
        }

        public void StopTimer()
        {
            _timer?.Change(Timeout.Infinite, Timeout.Infinite);
        }

        public void DisposeTimer()
        {
            _timer?.Dispose();
            _timer = null;
        }
    }
}
