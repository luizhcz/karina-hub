using System.Collections.Concurrent;
using EfsAiHub.Core.Abstractions.Execution;
using EfsAiHub.Host.Api.Configuration;
using EfsAiHub.Infra.Observability;
using Microsoft.Extensions.Options;

namespace EfsAiHub.Host.Api.BackgroundServices;

/// <summary>
/// Implementação Singleton de IExecutionSlotRegistry.
/// CancellationTokens são locais (in-process), contagem de slots é distribuída via Redis.
///
/// Controla back-pressure do Chat Path via <see cref="IDistributedSlotCounter"/>
/// (scope: "chat") com limite configurado em <see cref="WorkflowEngineOptions.ChatMaxConcurrentExecutions"/>.
/// Ao bater o teto cross-pod, TryAcquireSlotAsync retorna false e o caller rejeita com HTTP 429.
/// </summary>
public sealed class ChatExecutionRegistry : IExecutionSlotRegistry
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _map = new();
    private readonly IDistributedSlotCounter _slotCounter;
    private readonly int _maxSlots;
    private static readonly TimeSpan SlotTtl = TimeSpan.FromMinutes(5);
    private const string Scope = "chat";

    public ChatExecutionRegistry(
        IOptions<WorkflowEngineOptions> options,
        IDistributedSlotCounter slotCounter)
    {
        _slotCounter = slotCounter;
        _maxSlots = options.Value.ChatMaxConcurrentExecutions;
        if (_maxSlots <= 0) _maxSlots = 200;
    }

    public async Task<bool> TryAcquireSlotAsync()
    {
        var acquired = await _slotCounter.TryAcquireAsync(Scope, _maxSlots, SlotTtl);
        if (!acquired)
        {
            MetricsRegistry.ChatBackPressureRejections.Add(1);
            return false;
        }
        MetricsRegistry.ChatActiveExecutions.Add(1);
        return true;
    }

    public void Register(string executionId, CancellationTokenSource cts)
        => _map[executionId] = cts;

    public bool TryCancel(string executionId)
    {
        if (_map.TryGetValue(executionId, out var cts))
        {
            cts.Cancel();
            return true;
        }
        return false;
    }

    public void Cleanup(string executionId)
    {
        if (_map.TryRemove(executionId, out var cts))
            cts.Dispose();

        // Fire-and-forget release — slot TTL is safety net
        _ = ReleaseSlotAsync();
    }

    public async Task ReleaseSlotAsync()
    {
        await _slotCounter.ReleaseAsync(Scope);
        MetricsRegistry.ChatActiveExecutions.Add(-1);
    }
}
