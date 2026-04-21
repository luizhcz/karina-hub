namespace EfsAiHub.Core.Abstractions.BackgroundServices;

public class BackgroundServiceDescriptor
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required string Lifecycle { get; init; }
    public TimeSpan? Interval { get; init; }
    public required Type ServiceType { get; init; }
}
