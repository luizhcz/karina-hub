using System.Text.Json;
using EfsAiHub.Core.Abstractions.AgUi;

namespace EfsAiHub.Host.Api.Chat.AgUi.State;

/// <summary>
/// Adapter que implementa IAgUiSharedStateWriter delegando ao AgUiStateManager.
/// Registrado como singleton no DI para ser injetado no WorkflowRunnerService
/// (que vive em Host.Worker e não pode referenciar Host.Api diretamente).
/// </summary>
public sealed class AgUiSharedStateWriterAdapter : IAgUiSharedStateWriter
{
    private readonly AgUiStateManager _stateManager;

    public AgUiSharedStateWriterAdapter(AgUiStateManager stateManager)
    {
        _stateManager = stateManager;
    }

    public async Task UpdateAsync(string threadId, string path, JsonElement value)
    {
        // SetAgentValueAsync atualiza o L1 (in-memory) + L2 (Redis)
        // e retorna um AgUiEvent STATE_DELTA que o SSE handler emitirá.
        await _stateManager.SetAgentValueAsync(threadId, path, value);
    }

    public async Task<JsonElement?> GetSnapshotAsync(string threadId)
    {
        var state = await _stateManager.GetOrCreateAsync(threadId);
        var snapshot = state.GetSnapshot();

        // Retorna null se o state está vazio ({})
        if (snapshot.ValueKind == JsonValueKind.Object)
        {
            using var enumerator = snapshot.EnumerateObject();
            if (!enumerator.MoveNext())
                return null;
        }

        return snapshot;
    }
}
