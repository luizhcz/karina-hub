namespace EfsAiHub.Core.Abstractions.Observability;

public class ToolInvocation
{
    public long Id { get; init; }
    public required string ExecutionId { get; init; }
    public required string AgentId { get; init; }
    public required string ToolName { get; init; }
    public string? Arguments { get; init; }
    public string? Result { get; set; }
    public double DurationMs { get; set; }
    public bool Success { get; set; } = true;
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
