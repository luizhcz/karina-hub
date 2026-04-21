using System.Text.Json;

namespace EfsAiHub.Host.Api.Chat.AgUi.State;

/// <summary>
/// Estado compartilhado entre agente e frontend.
/// Thread-safe via lock. Sincronizado via STATE_SNAPSHOT e STATE_DELTA.
/// </summary>
public sealed class AgUiSharedState
{
    private JsonElement _state;
    private readonly object _lock = new();

    public AgUiSharedState(JsonElement? initial = null)
    {
        _state = initial ?? JsonDocument.Parse("{}").RootElement.Clone();
    }

    /// <summary>Estado completo (para SNAPSHOT).</summary>
    public JsonElement GetSnapshot()
    {
        lock (_lock) return _state.Clone();
    }

    /// <summary>Aplica delta do frontend (JSON Patch RFC 6902).</summary>
    public void ApplyDelta(JsonElement patch)
    {
        lock (_lock)
        {
            _state = JsonPatchApplier.Apply(_state, patch);
        }
    }

    /// <summary>Gera delta para mudança do agente.</summary>
    public JsonElement SetValue(string path, JsonElement value)
    {
        lock (_lock)
        {
            var oldState = _state.Clone();
            _state = JsonPatchApplier.SetPath(_state, path, value);
            return JsonPatchApplier.GenerateDiff(oldState, _state);
        }
    }
}
