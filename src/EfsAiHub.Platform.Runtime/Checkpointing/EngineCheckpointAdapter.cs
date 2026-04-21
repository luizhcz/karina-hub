using System.Text.Json;
using Microsoft.Agents.AI.Workflows;

namespace EfsAiHub.Platform.Runtime.Checkpointing;

/// <summary>
/// Implementação singleton do <see cref="IEngineCheckpointAdapter"/>. Cacheia
/// o <see cref="CheckpointManager"/> para não recriá-lo por execução.
/// </summary>
public sealed class EngineCheckpointAdapter : IEngineCheckpointAdapter
{
    private readonly FrameworkCheckpointStoreAdapter _store;
    private readonly EfsAiHub.Core.Orchestration.Workflows.ICheckpointStore _innerStore;
    private readonly ILogger<EngineCheckpointAdapter> _logger;
    private readonly CheckpointManager _manager;

    public EngineCheckpointAdapter(
        FrameworkCheckpointStoreAdapter store,
        EfsAiHub.Core.Orchestration.Workflows.ICheckpointStore innerStore,
        ILogger<EngineCheckpointAdapter> logger)
    {
        _store = store;
        _innerStore = innerStore;
        _logger = logger;
        _manager = CheckpointManager.CreateJson(store, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    public CheckpointManager CreateManager() => _manager;

    public async Task<CheckpointInfo?> GetLatestAsync(string executionId, CancellationToken ct = default)
    {
        try
        {
            return await _store.GetLatestAsync(executionId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[EngineCheckpointAdapter] Falha ao obter último checkpoint de '{ExecutionId}'.", executionId);
            return null;
        }
    }

    public async Task<StreamingRun?> TryResumeAsync(Workflow workflow, string executionId, CancellationToken ct = default)
    {
        try
        {
            var info = await GetLatestAsync(executionId, ct);
            if (info is null)
            {
                _logger.LogWarning(
                    "[EngineCheckpointAdapter] Sem checkpoint para '{ExecutionId}' — recovery impossível.", executionId);
                return null;
            }

            return await InProcessExecution.ResumeStreamingAsync(workflow, info, _manager, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[EngineCheckpointAdapter] Falha ao retomar execução '{ExecutionId}' a partir do checkpoint.",
                executionId);
            return null;
        }
    }

    public async Task EvictSessionAsync(string executionId, bool deletePersistent = true, CancellationToken ct = default)
    {
        try
        {
            _store.EvictSession(executionId);
            if (deletePersistent)
                await _innerStore.DeleteCheckpointAsync(executionId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[EngineCheckpointAdapter] Falha ao evict sessão '{ExecutionId}'.", executionId);
        }
    }
}
