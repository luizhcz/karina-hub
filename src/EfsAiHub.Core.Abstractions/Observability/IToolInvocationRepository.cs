namespace EfsAiHub.Core.Abstractions.Observability;

public interface IToolInvocationRepository
{
    Task AppendAsync(ToolInvocation invocation, CancellationToken ct = default);
    Task<IReadOnlyList<ToolInvocation>> GetByExecutionAsync(string executionId, CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, IReadOnlyList<ToolInvocation>>> GetByExecutionIdsAsync(IEnumerable<string> executionIds, CancellationToken ct = default);
}
