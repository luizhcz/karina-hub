using System.Text.Json;
using Microsoft.Agents.AI.Workflows;
using EfsAiHub.Core.Abstractions.Persistence;
using EfsAiHub.Platform.Runtime.BackgroundServices;

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
    private readonly INodeExecutionRepository _nodeRepo;
    private readonly IWorkflowEventBus _eventBus;
    private readonly TokenBatcher _tokenBatcher;
    private readonly ExecutionFailureWriter _failureWriter;
    private readonly ILogger<AgentHandoffEventHandler> _logger;

    public AgentHandoffEventHandler(
        INodeExecutionRepository nodeRepo,
        IWorkflowEventBus eventBus,
        TokenBatcher tokenBatcher,
        ExecutionFailureWriter failureWriter,
        ILogger<AgentHandoffEventHandler> logger)
    {
        _nodeRepo = nodeRepo;
        _eventBus = eventBus;
        _tokenBatcher = tokenBatcher;
        _failureWriter = failureWriter;
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
        IReadOnlyDictionary<string, AgentNodeInfo>? agentNames,
        CancellationToken ct)
    {
        var agentId = tokenEvt.ExecutorId;
        var tokenText = tokenEvt.Data?.ToString() ?? string.Empty;

        if (agentId is not null && agentId != nodeTracker.CurrentAgentId)
        {
            await FinalizePreviousAgentAsync(execution, nodeTracker, agentNames, ct);
            await EmitHandoffAsync(execution, nodeTracker.CurrentAgentId, agentId, agentNames);
            await StartNewAgentAsync(execution, nodeTracker, agentId, agentNames);
        }

        // Acumula tokens em StringBuilder do tracker (sem string concat O(N²)
        // e sem SetNodeAsync inline no hot path). O output é materializado e persistido
        // ao encerrar o agente (handoff ou fim do workflow).
        if (agentId is not null)
            nodeTracker.AppendOutput(agentId, tokenText);

        // MessageId aqui é o ID canônico do step (mesmo que vai pra chat_messages no save).
        // Cliente recebe via TEXT_MESSAGE_CONTENT.messageId e pode usar direto pra feedback.
        var messageId = agentId is not null && nodeTracker.TryGetRecord(agentId, out var rec)
            ? rec.MessageId
            : null;
        _tokenBatcher.Enqueue(execution.ExecutionId, agentId, messageId, tokenText);
    }

    private async Task FinalizePreviousAgentAsync(
        WorkflowExecution execution,
        NodeStateTracker nodeTracker,
        IReadOnlyDictionary<string, AgentNodeInfo>? agentNames,
        CancellationToken ct)
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

        var previousInfo = agentNames is not null
            && agentNames.TryGetValue(previousAgentId, out var pan) ? pan : null;

        var output = prev.Output ?? string.Empty;

        // Persiste a ChatMessage do step com o MESMO messageId que foi emitido
        // no stream — cliente que pegou o ID via TEXT_MESSAGE_END pode usar direto
        // pra referenciar a mensagem no banco (feedback, etc).
        if (!string.IsNullOrEmpty(prev.MessageId) && !string.IsNullOrEmpty(output))
        {
            await _failureWriter.MarkStepCompletedAsync(
                execution, previousAgentId, prev.MessageId, output, ct);
        }

        // wasStreamed=true marca que o output já foi entregue via tokens (este
        // handler só é invocado a partir de AgentResponseUpdateEvent — i.e. o
        // LLM streamou). Sem essa flag, o AgUiEventMapper reconstroi o trio
        // sintético TEXT_MESSAGE_*, duplicando a mensagem que o cliente já
        // recebeu chunk a chunk.
        await PublishEventAsync(execution.ExecutionId, "node_completed", new
        {
            nodeId = previousAgentId,
            nodeType = "agent",
            agentId = previousAgentId,
            agentName = previousInfo?.Name,
            agentType = previousInfo?.Type,
            messageId = prev.MessageId,
            output,
            wasStreamed = true,
            timestamp = prev.CompletedAt
        });
    }

    private Task EmitHandoffAsync(
        WorkflowExecution execution,
        string? fromAgentId,
        string toAgentId,
        IReadOnlyDictionary<string, AgentNodeInfo>? agentNames)
    {
        var fromInfo = fromAgentId is not null && agentNames is not null
            && agentNames.TryGetValue(fromAgentId, out var fan) ? fan : null;
        var toInfo = agentNames is not null
            && agentNames.TryGetValue(toAgentId, out var tan) ? tan : null;

        return PublishEventAsync(execution.ExecutionId, "handoff", new
        {
            fromAgentId,
            fromAgentName = fromInfo?.Name,
            fromAgentType = fromInfo?.Type,
            toAgentId,
            toAgentName = toInfo?.Name,
            toAgentType = toInfo?.Type,
            timestamp = DateTime.UtcNow
        });
    }

    private async Task StartNewAgentAsync(
        WorkflowExecution execution,
        NodeStateTracker nodeTracker,
        string agentId,
        IReadOnlyDictionary<string, AgentNodeInfo>? agentNames)
    {
        // MessageId é gerado aqui — quando o agente entra em "running" — e usado
        // tanto no stream AG-UI (TEXT_MESSAGE_*, token, node_completed) quanto na
        // persistência em chat_messages. Um único ID, sem reconciliação posterior.
        var record = new NodeExecutionRecord
        {
            NodeId = agentId,
            ExecutionId = execution.ExecutionId,
            NodeType = "agent",
            Status = "running",
            StartedAt = DateTime.UtcNow,
            MessageId = Guid.NewGuid().ToString("N")
        };
        nodeTracker.SetRecord(agentId, record);
        nodeTracker.CurrentAgentId = agentId;

        var nodeInfo = agentNames is not null && agentNames.TryGetValue(agentId, out var nan) ? nan : null;
        var agentName = nodeInfo?.Name ?? agentId;
        nodeTracker.StartAgentSpan(agentId, agentName, execution.WorkflowId, execution.ExecutionId);

        await _nodeRepo.SetNodeAsync(record);
        await PublishEventAsync(execution.ExecutionId, "node_started", new
        {
            nodeId = agentId,
            nodeType = "agent",
            agentName = nodeInfo?.Name,
            agentType = nodeInfo?.Type,
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
            Payload = JsonSerializer.Serialize(payload, JsonDefaults.Domain)
        });
    }
}
