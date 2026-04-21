using System.Text.Json;

namespace EfsAiHub.Host.Api.Chat.AgUi.State;

/// <summary>
/// Persistência distribuída do estado compartilhado AG-UI.
/// Redis-backed para que reconnect em outro pod preserve o estado.
/// </summary>
public interface IAgUiStateStore
{
    Task<JsonElement?> GetAsync(string threadId);
    Task SaveAsync(string threadId, JsonElement snapshot, TimeSpan ttl);
    Task DeleteAsync(string threadId);
}
