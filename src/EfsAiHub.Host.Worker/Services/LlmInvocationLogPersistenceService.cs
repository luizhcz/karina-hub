using System.Threading.Channels;
using EfsAiHub.Core.Agents.Capture;
using EfsAiHub.Core.Orchestration.Interfaces;
using EfsAiHub.Infra.Observability;

namespace EfsAiHub.Host.Worker.Services;

/// <summary>
/// Drena o channel de captura de prompts LLM e persiste em
/// <c>aihub.llm_invocation_log</c> em batches de 50. Channel é bounded
/// (5000 items, <c>DropOldest</c>) — captura é DEBUG; perder uma linha
/// quando o consumer está lento é preferível a bloquear o turno LLM.
///
/// Padrão idêntico ao <see cref="TokenUsagePersistenceService"/> — ambos
/// usam <c>BackgroundService.ExecuteAsync</c> + <c>ChannelReader.ReadAllAsync</c>
/// e fazem batch INSERT via repository scoped.
/// </summary>
public sealed class LlmInvocationLogPersistenceService : BackgroundService, ILlmInvocationLogSink
{
    private const int ChannelCapacity = 5_000;
    private const int MaxBatchSize = 50;

    private readonly Channel<LlmInvocationLogEntry> _channel;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LlmInvocationLogPersistenceService> _logger;

    public ChannelWriter<LlmInvocationLogEntry> Writer => _channel.Writer;

    public LlmInvocationLogPersistenceService(
        IServiceScopeFactory scopeFactory,
        ILogger<LlmInvocationLogPersistenceService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _channel = Channel.CreateBounded<LlmInvocationLogEntry>(new BoundedChannelOptions(ChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[LlmInvocationLogPersistence] Background service started.");

        await foreach (var item in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            var batch = new List<LlmInvocationLogEntry>(MaxBatchSize) { item };
            while (batch.Count < MaxBatchSize && _channel.Reader.TryRead(out var extra))
                batch.Add(extra);

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<ILlmInvocationLogRepository>();
                await repo.InsertBatchAsync(batch, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "[LlmInvocationLogPersistence] Falha ao persistir batch de {Count} entries.",
                    batch.Count);
                MetricsRegistry.PersistenceChannelDropped.Add(batch.Count,
                    new KeyValuePair<string, object?>("channel", "llm_invocation_log"));
            }
        }

        _logger.LogInformation("[LlmInvocationLogPersistence] Background service stopped.");
    }
}
