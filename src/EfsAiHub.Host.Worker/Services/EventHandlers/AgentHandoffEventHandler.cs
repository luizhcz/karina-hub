using System.Text.Json;
using Microsoft.Agents.AI.Workflows;

namespace EfsAiHub.Host.Worker.Services.EventHandlers;

/// <summary>
/// Handler extraído do <see cref="WorkflowRunnerService.HandleEventAsync"/>
/// para tratar o evento <see cref="AgentResponseUpdateEvent"/> — que concentrava
/// ~80 linhas de lógica (detecção de handoff, finalização de agente anterior,
/// emissão de <c>node_completed</c>/<c>handoff</c>/<c>node_started</c>,
/// acumulação de tokens e gestão de spans).
/// </summary>
/// <remarks>
/// Dependências (injetadas): <see cref="INodeExecutionRepository"/> para
/// persistir registros de nó, <see cref="IWorkflowEventBus"/> para publicar
/// eventos SSE e <see cref="TokenBatcher"/> para enfileirar tokens do streaming.
/// O <see cref="NodeStateTracker"/> é passado por chamada pois é estado por-execução
/// (não injetado via DI).
/// </remarks>
public sealed class AgentHandoffEventHandler
{
    private const int NodeCompletedOutputPreviewChars = 300;

    private readonly INodeExecutionRepository _nodeRepo;
    private readonly IWorkflowEventBus _eventBus;
    private readonly TokenBatcher _tokenBatcher;
    private readonly ILogger<AgentHandoffEventHandler> _logger;

    public AgentHandoffEventHandler(
        INodeExecutionRepository nodeRepo,
        IWorkflowEventBus eventBus,
        TokenBatcher tokenBatcher,
        ILogger<AgentHandoffEventHandler> logger)
    {
        _nodeRepo = nodeRepo;
        _eventBus = eventBus;
        _tokenBatcher = tokenBatcher;
        _logger = logger;
    }

    /// <summary>
    /// Processa um <see cref="AgentResponseUpdateEvent"/>: detecta troca de agente
    /// ativo no tracker, finaliza o agente anterior com métricas/span/persist,
    /// emite eventos de controle e acumula o token no buffer do agente atual.
    /// </summary>
    public async Task HandleAsync(
        AgentResponseUpdateEvent tokenEvt,
        WorkflowExecution execution,
        NodeStateTracker nodeTracker,
        IReadOnlyDictionary<string, string>? agentNames,
        CancellationToken ct)
    {
        var agentId = tokenEvt.ExecutorId;
        var tokenText = tokenEvt.Data?.ToString() ?? string.Empty;

        if (agentId is not null && agentId != nodeTracker.CurrentAgentId)
        {
            await FinalizePreviousAgentAsync(execution, nodeTracker, agentNames);
            await EmitHandoffAsync(execution, nodeTracker.CurrentAgentId, agentId, agentNames);
            await StartNewAgentAsync(execution, nodeTracker, agentId, agentNames);
        }

        // Fix #A3: acumula tokens em StringBuilder do tracker (sem string concat O(N²)
        // e sem SetNodeAsync inline no hot path). O output é materializado e persistido
        // ao encerrar o agente (handoff ou fim do workflow).
        if (agentId is not null)
            nodeTracker.AppendOutput(agentId, tokenText);

        _tokenBatcher.Enqueue(execution.ExecutionId, tokenEvt.ExecutorId, tokenText);
    }

    private async Task FinalizePreviousAgentAsync(
        WorkflowExecution execution,
        NodeStateTracker nodeTracker,
        IReadOnlyDictionary<string, string>? agentNames)
    {
        var previousAgentId = nodeTracker.CurrentAgentId;
        if (previousAgentId is null
            || !nodeTracker.TryGetRecord(previousAgentId, out var prev)
            || prev.Status != "running")
        {
            return;
        }

        prev.Status = "completed";
        prev.CompletedAt = DateTime.UtcNow;
        if (prev.StartedAt.HasValue)
        {
            var duration = (prev.CompletedAt.Value - prev.StartedAt.Value).TotalSeconds;
            MetricsRegistry.AgentInvocationDuration.Record(duration,
                new KeyValuePair<string, object?>("agent.id", previousAgentId),
                new KeyValuePair<string, object?>("workflow.id", execution.WorkflowId));
        }

        nodeTracker.TryEndAgentSpan(previousAgentId, out _);
        nodeTracker.MaterializeOutput(previousAgentId);
        await _nodeRepo.SetNodeAsync(prev);

        var previousAgentName = agentNames is not null
            && agentNames.TryGetValue(previousAgentId, out var pan) ? pan : null;

        var output = prev.Output ?? string.Empty;
        await PublishEventAsync(execution.ExecutionId, "node_completed", new
        {
            nodeId = previousAgentId,
            nodeType = "agent",
            agentId = previousAgentId,
            agentName = previousAgentName,
            output = output[..Math.Min(NodeCompletedOutputPreviewChars, output.Length)],
            timestamp = prev.CompletedAt
        });
    }

    private Task EmitHandoffAsync(
        WorkflowExecution execution,
        string? fromAgentId,
        string toAgentId,
        IReadOnlyDictionary<string, string>? agentNames)
    {
        var fromAgentName = fromAgentId is not null && agentNames is not null
            && agentNames.TryGetValue(fromAgentId, out var fan) ? fan : null;
        var toAgentName = agentNames is not null
            && agentNames.TryGetValue(toAgentId, out var tan) ? tan : null;

        return PublishEventAsync(execution.ExecutionId, "handoff", new
        {
            fromAgentId,
            fromAgentName,
            toAgentId,
            toAgentName,
            timestamp = DateTime.UtcNow
        });
    }

    private async Task StartNewAgentAsync(
        WorkflowExecution execution,
        NodeStateTracker nodeTracker,
        string agentId,
        IReadOnlyDictionary<string, string>? agentNames)
    {
        var record = new NodeExecutionRecord
        {
            NodeId = agentId,
            ExecutionId = execution.ExecutionId,
            NodeType = "agent",
            Status = "running",
            StartedAt = DateTime.UtcNow
        };
        nodeTracker.SetRecord(agentId, record);
        nodeTracker.CurrentAgentId = agentId;

        var agentName = agentNames is not null
            && agentNames.TryGetValue(agentId, out var nan) ? nan : agentId;
        nodeTracker.StartAgentSpan(agentId, agentName, execution.WorkflowId, execution.ExecutionId);

        await _nodeRepo.SetNodeAsync(record);
        await PublishEventAsync(execution.ExecutionId, "node_started", new
        {
            nodeId = agentId,
            nodeType = "agent",
            timestamp = record.StartedAt
        });
    }

    private async Task PublishEventAsync(string executionId, string eventType, object payload)
    {
        // Flush tokens acumulados antes do evento de controle — garante ordem correta no SSE.
        await _tokenBatcher.FlushAsync(executionId);

        await _eventBus.PublishAsync(executionId, new WorkflowEventEnvelope
        {
            EventType = eventType,
            ExecutionId = executionId,
            Payload = JsonSerializer.Serialize(payload)
        });
    }
}
