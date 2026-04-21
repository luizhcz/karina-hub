using EfsAiHub.Core.Abstractions.Observability;

namespace EfsAiHub.Core.Orchestration.Workflows;

/// <summary>
/// Lê o dump completo de uma execução: metadata, nodes, tools e eventos.
/// Centraliza a lógica de assembly que antes estava duplicada em controllers.
/// </summary>
public interface IExecutionDetailReader
{
    Task<ExecutionFullDetail?> GetFullAsync(string executionId, CancellationToken ct = default);
    Task<IReadOnlyList<ExecutionFullDetail>> GetFullBatchAsync(IEnumerable<string> executionIds, CancellationToken ct = default);
}

public record ExecutionFullDetail(
    WorkflowExecution Execution,
    IReadOnlyList<NodeExecutionRecord> Nodes,
    IReadOnlyList<ToolInvocation> Tools,
    IReadOnlyList<WorkflowEventEnvelope> Events);
