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
    /// <param name="freezeExact">
    /// Execução disparada com x-version (WorkflowVersion pinada): congela a versão
    /// EXATA dos agentes (sem patch-propagation). false (ao vivo) → propaga não-breaking.
    /// </param>
    Task<ExecutableWorkflow> BuildWorkflowAsync(WorkflowDefinition definition, string? startAgentId = null, CancellationToken ct = default, bool freezeExact = false);
}
