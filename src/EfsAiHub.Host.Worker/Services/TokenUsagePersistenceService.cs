using System.Threading.Channels;

namespace EfsAiHub.Host.Worker.Services;

/// <summary>
/// Consome <see cref="LlmTokenUsage"/> de um Channel bounded e persiste em batch.
/// Substitui os fire-and-forget (Task.Run) espalhados pelo código.
/// </summary>
public sealed class TokenUsagePersistenceService : BackgroundService, ITokenUsageSink
{
    private const int ChannelCapacity = 1_000;
    private const int MaxBatchSize = 10;

    private readonly Channel<LlmTokenUsage> _channel;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TokenUsagePersistenceService> _logger;

    public ChannelWriter<LlmTokenUsage> Writer => _channel.Writer;

    /// <summary>
    /// Enfileira um item para persistência. Loga warning e registra métrica quando o channel
    /// está cheio e o item mais antigo será descartado (DropOldest).
    /// </summary>
    public void Enqueue(LlmTokenUsage item)
    {
        if (_channel.Reader.Count >= ChannelCapacity)
        {
            _logger.LogWarning("[TokenUsagePersistence] Channel cheio ({Capacity} items) — item mais antigo descartado.", ChannelCapacity);
            MetricsRegistry.PersistenceChannelDropped.Add(1, new KeyValuePair<string, object?>("channel", "token_usage"));
        }
        _channel.Writer.TryWrite(item);
    }

    public TokenUsagePersistenceService(
        IServiceScopeFactory scopeFactory,
        ILogger<TokenUsagePersistenceService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _channel = Channel.CreateBounded<LlmTokenUsage>(new BoundedChannelOptions(ChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true
        });
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[TokenUsagePersistence] Background service started.");

        await foreach (var item in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            var batch = new List<LlmTokenUsage>(MaxBatchSize) { item };

            // Drena até MaxBatchSize itens se disponíveis
            while (batch.Count < MaxBatchSize && _channel.Reader.TryRead(out var extra))
                batch.Add(extra);

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<ILlmTokenUsageRepository>();

                foreach (var usage in batch)
                    await repo.AppendAsync(usage, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "[TokenUsagePersistence] Falha ao persistir batch de {Count} item(ns).", batch.Count);
            }
        }

        _logger.LogInformation("[TokenUsagePersistence] Background service stopped.");
    }
}
