using System.Collections.Concurrent;
using EfsAiHub.Platform.Runtime.Interfaces;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace EfsAiHub.Platform.Runtime.Factories;

/// <summary>
/// Implementação thread-safe de IAgentMiddlewareRegistry.
/// Registre uma instância singleton no DI e injete no AgentFactory.
/// </summary>
public sealed class AgentMiddlewareRegistry : IAgentMiddlewareRegistry
{
    private sealed record Entry(
        Func<IChatClient, string, Dictionary<string, string>, ILogger, IChatClient> Factory,
        MiddlewarePhase Phase,
        string Label,
        string Description,
        List<MiddlewareSettingDef> Settings);

    private readonly ConcurrentDictionary<string, Entry> _factories = new(StringComparer.OrdinalIgnoreCase);

    public void Register(
        string type,
        MiddlewarePhase phase,
        Func<IChatClient, string, Dictionary<string, string>, ILogger, IChatClient> factory,
        string? label = null,
        string? description = null,
        List<MiddlewareSettingDef>? settings = null)
    {
        _factories[type] = new Entry(factory, phase, label ?? type, description ?? "", settings ?? []);
    }

    public bool TryCreate(
        string type,
        IChatClient inner,
        string agentId,
        Dictionary<string, string> settings,
        ILogger logger,
        out IChatClient result)
    {
        if (_factories.TryGetValue(type, out var entry))
        {
            result = entry.Factory(inner, agentId, settings, logger);
            return true;
        }

        result = inner;
        return false;
    }

    public IReadOnlyCollection<(string Type, MiddlewarePhase Phase)> GetRegisteredTypes()
        => _factories.Select(kv => (kv.Key, kv.Value.Phase)).OrderBy(t => t.Key).ToList();

    public IReadOnlyCollection<MiddlewareMetadata> GetRegisteredMetadata()
        => _factories.Select(kv => new MiddlewareMetadata
        {
            Type = kv.Key,
            Phase = kv.Value.Phase,
            Label = kv.Value.Label,
            Description = kv.Value.Description,
            Settings = kv.Value.Settings,
        }).OrderBy(m => m.Type).ToList();
}
