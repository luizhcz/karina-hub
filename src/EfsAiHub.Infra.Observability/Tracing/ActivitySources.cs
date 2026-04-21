using System.Diagnostics;

namespace EfsAiHub.Infra.Observability;

public static class ActivitySources
{
    public const string WorkflowExecution = "EfsAiHub.Api.Execution";
    public const string AgentInvocation = "EfsAiHub.Api.AgentInvocation";
    public const string LlmCall = "EfsAiHub.Api.LlmCall";
    public const string ToolCall = "EfsAiHub.Api.ToolCall";

    public static readonly ActivitySource WorkflowExecutionSource = new(WorkflowExecution, "1.0.0");
    public static readonly ActivitySource AgentInvocationSource = new(AgentInvocation, "1.0.0");
    public static readonly ActivitySource LlmCallSource = new(LlmCall, "1.0.0");
    public static readonly ActivitySource ToolCallSource = new(ToolCall, "1.0.0");
}
