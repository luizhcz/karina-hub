namespace EfsAiHub.Core.Orchestration.Enums;

public enum ErrorCategory
{
    Unknown,
    Timeout,
    AgentLoopLimit,
    LlmError,
    LlmRateLimit,
    LlmContentFilter,
    ToolError,
    Cancelled,
    FrameworkError,
    BackPressureRejected,
    BudgetExceeded,
    CheckpointRecoveryFailed,
    HitlRejected
}
