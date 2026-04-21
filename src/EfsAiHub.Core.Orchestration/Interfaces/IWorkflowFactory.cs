using EfsAiHub.Core.Orchestration.Models;
using EfsAiHub.Core.Orchestration.Workflows;

namespace EfsAiHub.Core.Orchestration.Interfaces;

public interface IWorkflowFactory
{
    /// <summary>
    /// Constrói um workflow executável a partir de uma WorkflowDefinition.
    /// Usa AgentWorkflowBuilder.BuildSequential/BuildConcurrent/CreateHandoffBuilderWith/
    /// CreateGroupChatBuilderWith conforme o OrchestrationMode.
    /// </summary>
    /// <param name="startAgentId">
    /// Opcional — para Handoff mode, indica qual agente deve ser o entry point
    /// (otimização: evita passar pelo manager em continuações de conversa).
    /// </param>
    Task<ExecutableWorkflow> BuildWorkflowAsync(WorkflowDefinition definition, string? startAgentId = null, CancellationToken ct = default);
}
