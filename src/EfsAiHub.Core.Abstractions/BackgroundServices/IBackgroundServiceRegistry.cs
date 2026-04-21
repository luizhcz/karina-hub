namespace EfsAiHub.Core.Abstractions.BackgroundServices;

public interface IBackgroundServiceRegistry
{
    void Register(string name, BackgroundServiceDescriptor descriptor);
    BackgroundServiceDescriptor? Get(string name);
    IReadOnlyDictionary<string, BackgroundServiceDescriptor> GetAll();
}
