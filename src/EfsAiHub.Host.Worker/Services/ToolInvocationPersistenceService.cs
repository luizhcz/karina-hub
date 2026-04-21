using System.Threading.Channels;

namespace EfsAiHub.Host.Worker.Services;

/// <summary>
/// Consome <see cref="ToolInvocation"/> de um Channel bounded e persiste em batch.
/// Substitui os fire-and-forget (Task.Run) espalhados pelo código.
/// </summary>
public sealed class ToolInvocationPersistenceService : BackgroundService, IToolInvocationSink
{
    private const int ChannelCapacity = 1_000;
    private const int MaxBatchSize = 10;

    private readonly Channel<ToolInvocation> _channel;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ToolInvocationPersistenceService> _logger;

    public ChannelWriter<ToolInvocation> Writer => _channel.Writer;

    /// <summary>
    /// Enfileira um item para persistência. Loga warning e registra métrica quando o channel
    /// está cheio e o item mais antigo será descartado (DropOldest).
    /// </summary>
    public void Enqueue(ToolInvocation item)
    {
        if (_channel.Reader.Count >= ChannelCapacity)
        {
            _logger.LogWarning("[ToolInvocationPersistence] Channel cheio ({Capacity} items) — item mais antigo descartado.", ChannelCapacity);
            MetricsRegistry.PersistenceChannelDropped.Add(1, new KeyValuePair<string, object?>("channel", "tool_invocation"));
        }
        _channel.Writer.TryWrite(item);
    }

    public ToolInvocationPersistenceService(
        IServiceScopeFactory scopeFactory,
        ILogger<ToolInvocationPersistenceService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _channel = Channel.CreateBounded<ToolInvocation>(new BoundedChannelOptions(ChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true
        });
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[ToolInvocationPersistence] Background service started.");

        await foreach (var item in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            var batch = new List<ToolInvocation>(MaxBatchSize) { item };

            while (batch.Count < MaxBatchSize && _channel.Reader.TryRead(out var extra))
                batch.Add(extra);

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<IToolInvocationRepository>();

                foreach (var invocation in batch)
                    await repo.AppendAsync(invocation, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "[ToolInvocationPersistence] Falha ao persistir batch de {Count} item(ns).", batch.Count);
            }
        }

        _logger.LogInformation("[ToolInvocationPersistence] Background service stopped.");
    }
}
