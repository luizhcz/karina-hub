using EfsAiHub.Core.Abstractions.BackgroundServices;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EfsAiHub.Infra.Messaging.InMemory;

/// <summary>
/// Hosted service que colhe streams ociosos do <see cref="InMemoryEventBuffer"/>:
/// terminal expirado (TTL retention) e idle sem terminal (proteção contra
/// MarkTerminal nunca chamado).
/// </summary>
public sealed class InMemoryEventBufferCleaner : BackgroundService
{
    private const string HeartbeatName = "InMemoryEventBufferCleaner";

    private readonly InMemoryEventBuffer _buffer;
    private readonly InMemoryEventBufferOptions _options;
    private readonly IBackgroundServiceHeartbeatSink _heartbeat;
    private readonly ILogger<InMemoryEventBufferCleaner> _logger;

    public InMemoryEventBufferCleaner(
        InMemoryEventBuffer buffer,
        IOptions<InMemoryEventBufferOptions> options,
        IBackgroundServiceHeartbeatSink heartbeat,
        ILogger<InMemoryEventBufferCleaner> logger)
    {
        _buffer = buffer;
        _options = options.Value;
        _heartbeat = heartbeat;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _heartbeat.Started(HeartbeatName, DateTimeOffset.UtcNow);
        var interval = TimeSpan.FromSeconds(Math.Max(5, _options.CleanupIntervalSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                Sweep();
                _heartbeat.RecordSuccess(HeartbeatName, DateTimeOffset.UtcNow);
            }
            catch (Exception ex)
            {
                _heartbeat.RecordError(HeartbeatName, DateTimeOffset.UtcNow, ex);
                _logger.LogWarning(ex, "InMemoryEventBuffer sweep failed");
            }

            try
            {
                await Task.Delay(interval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private void Sweep()
    {
        var now = DateTimeOffset.UtcNow;
        var retentionAfterTerminal = TimeSpan.FromMinutes(Math.Max(1, _options.RetentionAfterTerminalMinutes));
        var idleEviction = TimeSpan.FromMinutes(Math.Max(_options.RetentionAfterTerminalMinutes + 1, _options.IdleEvictionAfterMinutes));

        int evictedTerminal = 0;
        int evictedIdle = 0;

        foreach (var kv in _buffer.StreamsForCleanup)
        {
            var state = kv.Value;
            bool shouldRemove;
            lock (state.Lock)
            {
                if (state.Terminal && state.TerminalAt is { } t && now - t >= retentionAfterTerminal)
                {
                    shouldRemove = true;
                    evictedTerminal++;
                }
                else if (!state.Terminal && now - state.LastActivity >= idleEviction)
                {
                    shouldRemove = true;
                    evictedIdle++;
                }
                else
                {
                    shouldRemove = false;
                }
            }

            if (shouldRemove)
            {
                _buffer.TryRemove(kv.Key, out _);
            }
        }

        if (evictedTerminal + evictedIdle > 0)
        {
            _logger.LogDebug(
                "InMemoryEventBuffer sweep: terminal={Terminal} idle={Idle} active={Active} overflow={Overflow}",
                evictedTerminal, evictedIdle, _buffer.ActiveStreamCount, _buffer.OverflowCount);
        }
    }
}
