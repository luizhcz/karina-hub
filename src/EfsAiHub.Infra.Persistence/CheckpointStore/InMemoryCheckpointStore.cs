using System.Collections.Concurrent;

namespace EfsAiHub.Infra.Persistence.CheckpointStore;

public class InMemoryCheckpointStore : ICheckpointStore
{
    private readonly ConcurrentDictionary<string, byte[]> _store = new();

    public Task SaveCheckpointAsync(string executionId, byte[] checkpointData, CancellationToken ct = default)
    {
        _store[executionId] = checkpointData;
        return Task.CompletedTask;
    }

    public Task<byte[]?> LoadCheckpointAsync(string executionId, CancellationToken ct = default)
        => Task.FromResult(_store.TryGetValue(executionId, out var data) ? data : null);

    public Task DeleteCheckpointAsync(string executionId, CancellationToken ct = default)
    {
        _store.TryRemove(executionId, out _);
        return Task.CompletedTask;
    }
}
