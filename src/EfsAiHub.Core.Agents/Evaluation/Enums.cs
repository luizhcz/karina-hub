namespace EfsAiHub.Core.Agents.Evaluation;

public enum TestSetVisibility
{
    Project,
    Global
}

public enum TestSetVersionStatus
{
    Draft,
    Published,
    Deprecated
}

public enum EvaluatorConfigVersionStatus
{
    Draft,
    Published,
    Deprecated
}

public enum SplitterStrategy
{
    LastTurn,
    Full,
    PerTurn
}

public enum EvaluatorKind
{
    Foundry,
    Local,
    Meai
}

public enum EvaluationRunStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled
}

public enum EvaluationTriggerSource
{
    Manual,
    AgentVersionPublished,
    ApiClient
}
