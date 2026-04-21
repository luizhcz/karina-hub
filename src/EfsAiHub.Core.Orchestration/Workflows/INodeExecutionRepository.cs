
namespace EfsAiHub.Core.Orchestration.Workflows;

public interface INodeExecutionRepository
{
    Task SetNodeAsync(NodeExecutionRecord record, CancellationToken ct = default);
    Task<IReadOnlyList<NodeExecutionRecord>> GetAllAsync(string executionId, CancellationToken ct = default);
    Task<NodeExecutionRecord?> GetNodeAsync(string executionId, string nodeId, CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, IReadOnlyList<NodeExecutionRecord>>> GetAllByExecutionIdsAsync(IEnumerable<string> executionIds, CancellationToken ct = default);
}
