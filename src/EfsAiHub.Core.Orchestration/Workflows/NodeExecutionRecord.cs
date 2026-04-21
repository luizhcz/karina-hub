namespace EfsAiHub.Core.Orchestration.Workflows;

public class NodeExecutionRecord
{
    public required string NodeId { get; init; }
    public required string ExecutionId { get; init; }
    public string NodeType { get; set; } = "executor"; // "agent" | "executor" | "trigger"
    public string Status { get; set; } = "pending";    // "pending" | "running" | "completed" | "failed"
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? Output { get; set; }
    public bool OutputTruncated { get; set; }
    public int Iteration { get; set; } = 1;
    public int TokensUsed { get; set; }
}
