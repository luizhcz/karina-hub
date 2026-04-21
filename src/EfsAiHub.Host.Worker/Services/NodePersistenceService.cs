using System.Threading.Channels;

namespace EfsAiHub.Host.Worker.Services;

/// <summary>
/// Job de persistência de estado de nó emitido pelo NodeCallback (síncrono).
/// O payload já é serializado no momento do enqueue para evitar captura de objetos anônimos.
/// </summary>
public sealed record NodePersistenceJob(
    NodeExecutionRecord Record,
    string ExecutionId,
    string EventType,
    string PayloadJson);

/// <summary>
/// Consome <see cref="NodePersistenceJob"/> de um Channel bounded e persiste sequencialmente.
/// Substitui os dois blocos fire-and-forget (Task.Run) do NodeCallback em WorkflowRunnerService,
/// eliminando a race condition em workflows Concurrent onde nós paralelos terminam simultaneamente.
/// </summary>
public sealed class NodePersistenceService : BackgroundService
{
    private const int ChannelCapacity = 2_000;

    private readonly Channel<NodePersistenceJob> _channel;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<NodePersistenceService> _logger;

    public ChannelWriter<NodePersistenceJob> Writer => _channel.Writer;

    /// <summary>
    /// Enfileira um job para persistência. Loga warning e registra métrica quando o channel
    /// está cheio e o item mais antigo será descartado (DropOldest).
    /// </summary>
    public void Enqueue(NodePersistenceJob job)
    {
        if (_channel.Reader.Count >= ChannelCapacity)
        {
            _logger.LogWarning("[NodePersistence] Channel cheio ({Capacity} items) — item mais antigo descartado.", ChannelCapacity);
            MetricsRegistry.PersistenceChannelDropped.Add(1, new KeyValuePair<string, object?>("channel", "node"));
        }
        _channel.Writer.TryWrite(job);
    }

    public NodePersistenceService(
        IServiceScopeFactory scopeFactory,
        ILogger<NodePersistenceService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _channel = Channel.CreateBounded<NodePersistenceJob>(new BoundedChannelOptions(ChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true
        });
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[NodePersistence] Background service started.");

        await foreach (var job in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var nodeRepo = scope.ServiceProvider.GetRequiredService<INodeExecutionRepository>();
                var eventBus = scope.ServiceProvider.GetRequiredService<IWorkflowEventBus>();

                await nodeRepo.SetNodeAsync(job.Record);
                await eventBus.PublishAsync(job.ExecutionId, new WorkflowEventEnvelope
                {
                    EventType = job.EventType,
                    ExecutionId = job.ExecutionId,
                    Payload = job.PayloadJson
                });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "[NodePersistence] Falha ao persistir job {EventType} para execução '{ExecutionId}'.",
                    job.EventType, job.ExecutionId);
            }
        }

        _logger.LogInformation("[NodePersistence] Background service stopped.");
    }
}
