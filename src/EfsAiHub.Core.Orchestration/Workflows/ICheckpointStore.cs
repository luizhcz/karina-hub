namespace EfsAiHub.Core.Orchestration.Workflows;

public interface ICheckpointStore
{
    Task SaveCheckpointAsync(string executionId, byte[] checkpointData, CancellationToken ct = default);
    Task<byte[]?> LoadCheckpointAsync(string executionId, CancellationToken ct = default);
    Task DeleteCheckpointAsync(string executionId, CancellationToken ct = default);
}
