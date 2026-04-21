namespace EfsAiHub.Core.Abstractions.Observability;

public class LlmTokenUsage
{
    public long Id { get; set; }
    public string AgentId { get; set; } = "";
    public string ModelId { get; set; } = "";
    public string? ExecutionId { get; set; }
    public string? WorkflowId { get; set; }
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public int TotalTokens { get; set; }
    public double DurationMs { get; set; }
    public string? PromptVersionId { get; set; }
    public string? AgentVersionId { get; set; }
    public string? OutputContent { get; set; }
    public int RetryCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
