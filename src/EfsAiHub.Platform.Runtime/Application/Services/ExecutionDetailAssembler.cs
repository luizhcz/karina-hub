using EfsAiHub.Core.Abstractions.Observability;
using EfsAiHub.Core.Orchestration.Workflows;

namespace EfsAiHub.Platform.Runtime.Services;

/// <summary>
/// Monta o dump completo de uma execução (metadata + nodes + tools + events).
/// Elimina a duplicação que existia entre ConversationsController e ExecutionsController.
/// </summary>
public class ExecutionDetailAssembler : IExecutionDetailReader
{
    private readonly IWorkflowService _workflowService;
    private readonly IWorkflowExecutionRepository _executionRepo;
    private readonly INodeExecutionRepository _nodeRepo;
    private readonly IToolInvocationRepository _toolRepo;
    private readonly IWorkflowEventBus _eventBus;

    public ExecutionDetailAssembler(
        IWorkflowService workflowService,
        IWorkflowExecutionRepository executionRepo,
        INodeExecutionRepository nodeRepo,
        IToolInvocationRepository toolRepo,
        IWorkflowEventBus eventBus)
    {
        _workflowService = workflowService;
        _executionRepo = executionRepo;
        _nodeRepo = nodeRepo;
        _toolRepo = toolRepo;
        _eventBus = eventBus;
    }

    public async Task<ExecutionFullDetail?> GetFullAsync(string executionId, CancellationToken ct = default)
    {
        var execution = await _workflowService.GetExecutionAsync(executionId, ct);
        if (execution is null) return null;

        var nodesTask = _nodeRepo.GetAllAsync(executionId, ct);
        var toolsTask = _toolRepo.GetByExecutionAsync(executionId, ct);
        var eventsTask = _eventBus.GetHistoryAsync(executionId, ct);
        await Task.WhenAll(nodesTask, toolsTask, eventsTask);

        return new ExecutionFullDetail(execution, nodesTask.Result, toolsTask.Result, eventsTask.Result);
    }

    public async Task<IReadOnlyList<ExecutionFullDetail>> GetFullBatchAsync(
        IEnumerable<string> executionIds, CancellationToken ct = default)
    {
        var idList = executionIds.ToList();
        if (idList.Count == 0) return Array.Empty<ExecutionFullDetail>();

        // 4 batch queries em paralelo — elimina o problema N+1 (4N → 4 queries)
        var executionsTask = _executionRepo.GetByIdsAsync(idList, ct);
        var nodesTask = _nodeRepo.GetAllByExecutionIdsAsync(idList, ct);
        var toolsTask = _toolRepo.GetByExecutionIdsAsync(idList, ct);
        var eventsTask = _eventBus.GetHistoryBatchAsync(idList, ct);
        await Task.WhenAll(executionsTask, nodesTask, toolsTask, eventsTask);

        var executions = executionsTask.Result;
        var nodesMap = nodesTask.Result;
        var toolsMap = toolsTask.Result;
        var eventsMap = eventsTask.Result;

        var emptyNodes = (IReadOnlyList<NodeExecutionRecord>)Array.Empty<NodeExecutionRecord>();
        var emptyTools = (IReadOnlyList<ToolInvocation>)Array.Empty<ToolInvocation>();
        var emptyEvents = (IReadOnlyList<WorkflowEventEnvelope>)Array.Empty<WorkflowEventEnvelope>();

        return executions.Select(exec => new ExecutionFullDetail(
            exec,
            nodesMap.TryGetValue(exec.ExecutionId, out var nodes) ? nodes : emptyNodes,
            toolsMap.TryGetValue(exec.ExecutionId, out var tools) ? tools : emptyTools,
            eventsMap.TryGetValue(exec.ExecutionId, out var events) ? events : emptyEvents
        )).ToList();
    }
}
