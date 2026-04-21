namespace EfsAiHub.Core.Orchestration.Models;

/// <summary>
/// Wrapper tipado sobre o grafo de agentes retornado pelo Microsoft Agent Framework.
/// Elimina casts espalhados para AIAgent/Workflow no codebase.
/// </summary>
public sealed class ExecutableWorkflow
{
    public object Value { get; }
    public bool IsExposedAsAgent { get; }

    private ExecutableWorkflow(object value, bool isExposedAsAgent)
    {
        Value = value;
        IsExposedAsAgent = isExposedAsAgent;
    }

    public static ExecutableWorkflow FromWorkflow(object workflow) => new(workflow, false);
    public static ExecutableWorkflow FromAgent(object agent) => new(agent, true);
}
