using System.Threading.Channels;
using EfsAiHub.Core.Abstractions.BackgroundServices;
using EfsAiHub.Core.Abstractions.Execution;

namespace EfsAiHub.Host.Worker.Services;

/// <summary>
/// Quando preenchido, indica que o job de node_completed corresponde a uma
/// resposta de agente assistant que deve virar uma ChatMessage persistida. O
/// service chama <see cref="ExecutionFailureWriter.MarkStepCompletedAsync"/>
/// ANTES de publicar o evento — garantindo que clientes AG-UI que fizerem GET
/// /messages ao ver STEP_FINISHED encontrem a mensagem.
/// </summary>
public sealed record NodeChatStepInfo(
    string ConversationId,
    string AgentId,
    string MessageId,
    string Output);

/// <summary>
/// Job de persistência de estado de nó emitido pelo NodeCallback (síncrono).
/// O payload já é serializado no momento do enqueue para evitar captura de objetos anônimos.
/// </summary>
public sealed record NodePersistenceJob(
    NodeExecutionRecord Record,
    string ExecutionId,
    string EventType,
    string PayloadJson,
    NodeChatStepInfo? ChatStep = null);

/// <summary>
/// Consome <see cref="NodePersistenceJob"/> de um Channel bounded e persiste sequencialmente.
/// Substitui os dois blocos fire-and-forget (Task.Run) do NodeCallback em WorkflowRunnerService,
/// eliminando a race condition em workflows Concurrent onde nós paralelos terminam simultaneamente.
/// </summary>
public sealed class NodePersistenceService : BackgroundService
{
    private const string HeartbeatName = "NodePersistence";
    private const int ChannelCapacity = 2_000;

    private readonly Channel<NodePersistenceJob> _channel;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IBackgroundServiceHeartbeatSink _heartbeat;
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
        IBackgroundServiceHeartbeatSink heartbeat,
        ILogger<NodePersistenceService> logger)
    {
        _scopeFactory = scopeFactory;
        _heartbeat = heartbeat;
        _logger = logger;
        _channel = Channel.CreateBounded<NodePersistenceJob>(new BoundedChannelOptions(ChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true
        });
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _heartbeat.Started(HeartbeatName, DateTimeOffset.UtcNow);
        _logger.LogInformation("[NodePersistence] Background service started.");

        await foreach (var job in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var nodeRepo = scope.ServiceProvider.GetRequiredService<INodeExecutionRepository>();
                var eventBus = scope.ServiceProvider.GetRequiredService<IWorkflowEventBus>();

                await nodeRepo.SetNodeAsync(job.Record);

                // Persiste a ChatMessage assistant ANTES de publicar o evento — garante
                // que cliente que faz GET /messages ao receber STEP_FINISHED encontra
                // a mensagem. Aplica-se a agentes non-streamed (caminho via NodeCallback);
                // agentes streamed seguem via AgentHandoffEventHandler.FinalizePreviousAgentAsync.
                if (job.ChatStep is { } chat)
                {
                    var observers = scope.ServiceProvider.GetServices<IExecutionLifecycleObserver>();
                    foreach (var observer in observers)
                    {
                        try
                        {
                            await observer.OnStepCompletedAsync(
                                chat.ConversationId, job.ExecutionId, chat.AgentId,
                                chat.MessageId, chat.Output, stoppingToken);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex,
                                "[NodePersistence] Observer '{Observer}' falhou em OnStepCompletedAsync (exec='{ExecId}', agent='{AgentId}').",
                                observer.GetType().Name, job.ExecutionId, chat.AgentId);
                        }
                    }
                }

                await eventBus.PublishAsync(job.ExecutionId, new WorkflowEventEnvelope
                {
                    EventType = job.EventType,
                    ExecutionId = job.ExecutionId,
                    Payload = job.PayloadJson
                });
                _heartbeat.RecordSuccess(HeartbeatName, DateTimeOffset.UtcNow);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _heartbeat.RecordError(HeartbeatName, DateTimeOffset.UtcNow, ex);
                _logger.LogWarning(ex,
                    "[NodePersistence] Falha ao persistir job {EventType} para execução '{ExecutionId}'.",
                    job.EventType, job.ExecutionId);
            }
        }

        _logger.LogInformation("[NodePersistence] Background service stopped.");
    }
}
