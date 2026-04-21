using System.Collections.Concurrent;
using EfsAiHub.Core.Abstractions.BackgroundServices;

namespace EfsAiHub.Platform.Runtime.Services;

public class BackgroundServiceRegistry : IBackgroundServiceRegistry
{
    private readonly ConcurrentDictionary<string, BackgroundServiceDescriptor> _services = new(StringComparer.OrdinalIgnoreCase);

    public void Register(string name, BackgroundServiceDescriptor descriptor) => _services[name] = descriptor;
    public BackgroundServiceDescriptor? Get(string name) => _services.GetValueOrDefault(name);
    public IReadOnlyDictionary<string, BackgroundServiceDescriptor> GetAll() => _services;
}
