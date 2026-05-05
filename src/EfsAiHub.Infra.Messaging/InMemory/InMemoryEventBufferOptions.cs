namespace EfsAiHub.Infra.Messaging.InMemory;

public sealed class InMemoryEventBufferOptions
{
    public int MaxLengthPerStream { get; set; } = 5000;

    public int RetentionAfterTerminalMinutes { get; set; } = 30;

    public int IdleEvictionAfterMinutes { get; set; } = 120;

    public int CleanupIntervalSeconds { get; set; } = 60;
}
