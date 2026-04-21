namespace EfsAiHub.Core.Orchestration.Executors;

/// <summary>
/// Lançada pelo HitlDecoratorExecutor quando o humano rejeita a interação.
/// Capturada pelo WorkflowRunnerService para marcar a execução como Failed.
/// </summary>
public sealed class HitlRejectedException : Exception
{
    public string NodeId { get; }
    public string Resolution { get; }

    public HitlRejectedException(string nodeId, string resolution)
        : base($"HITL rejeitado no nó '{nodeId}': {resolution}")
    {
        NodeId = nodeId;
        Resolution = resolution;
    }
}
