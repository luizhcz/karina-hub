namespace EfsAiHub.Core.Abstractions.Observability;

public interface ILlmTokenUsageRepository
{
    Task AppendAsync(LlmTokenUsage usage, CancellationToken ct = default);
    Task<IReadOnlyList<LlmTokenUsage>> GetByExecutionIdAsync(string executionId, CancellationToken ct = default);
    Task<IReadOnlyList<LlmTokenUsage>> GetByAgentIdAsync(string agentId, int limit = 100, CancellationToken ct = default);
    Task<AgentTokenSummary> GetAgentSummaryAsync(string agentId, DateTime from, DateTime to, CancellationToken ct = default);
    Task<IReadOnlyList<AgentTokenSummary>> GetAllAgentsSummaryAsync(DateTime from, DateTime to, CancellationToken ct = default);
    Task<GlobalTokenSummary> GetGlobalSummaryAsync(DateTime from, DateTime to, CancellationToken ct = default);
    Task<ThroughputResult> GetThroughputAsync(DateTime from, DateTime to, CancellationToken ct = default);
    Task<IReadOnlyList<WorkflowTokenSummary>> GetAllWorkflowsSummaryAsync(DateTime from, DateTime to, CancellationToken ct = default);
    Task<IReadOnlyList<ProjectTokenSummary>> GetAllProjectsSummaryAsync(DateTime from, DateTime to, CancellationToken ct = default);
}

public class AgentTokenSummary
{
    public string AgentId { get; set; } = "";
    public string ModelId { get; set; } = "";
    public long TotalInput { get; set; }
    public long TotalOutput { get; set; }
    public long TotalTokens { get; set; }
    public int CallCount { get; set; }
    public double AvgDurationMs { get; set; }
}

public class GlobalTokenSummary
{
    public long TotalInput { get; set; }
    public long TotalOutput { get; set; }
    public long TotalTokens { get; set; }
    public int TotalCalls { get; set; }
    public double AvgDurationMs { get; set; }
    public IReadOnlyList<AgentTokenSummary> ByAgent { get; set; } = [];
}

public class ThroughputBucket
{
    public DateTime Bucket { get; set; }
    public int Executions { get; set; }
    public long Tokens { get; set; }
    public int LlmCalls { get; set; }
    public double AvgDurationMs { get; set; }
}

public class ThroughputResult
{
    public IReadOnlyList<ThroughputBucket> Buckets { get; set; } = [];
    public double AvgExecutionsPerHour { get; set; }
    public double AvgTokensPerHour { get; set; }
    public double AvgCallsPerHour { get; set; }
}

public class WorkflowTokenSummary
{
    public string WorkflowId { get; set; } = "";
    public string ModelId { get; set; } = "";
    public long TotalInput { get; set; }
    public long TotalOutput { get; set; }
    public long TotalTokens { get; set; }
    public int CallCount { get; set; }
    public double AvgDurationMs { get; set; }
}

public class ProjectTokenSummary
{
    public string ProjectId { get; set; } = "";
    public string ModelId { get; set; } = "";
    public long TotalInput { get; set; }
    public long TotalOutput { get; set; }
    public long TotalTokens { get; set; }
    public int CallCount { get; set; }
    public double AvgDurationMs { get; set; }
}
