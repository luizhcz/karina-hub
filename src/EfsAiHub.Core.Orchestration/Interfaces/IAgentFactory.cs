using EfsAiHub.Core.Orchestration.Models;
using EfsAiHub.Core.Agents;
using EfsAiHub.Core.Orchestration.Workflows;

namespace EfsAiHub.Core.Orchestration.Interfaces;

public interface IAgentFactory
{
    /// <summary>
    /// Cria uma instância de agente do framework a partir de uma AgentDefinition.
    /// Retorna object para desacoplar do tipo concreto do framework.
    /// </summary>
    Task<ExecutableWorkflow> CreateAgentAsync(AgentDefinition definition, CancellationToken ct = default);

    /// <summary>
    /// Cria instâncias de agentes para todas as referências de um workflow.
    /// Valida que todos os agentes referenciados existem no repositório.
    /// </summary>
    Task<IReadOnlyDictionary<string, ExecutableWorkflow>> CreateAgentsForWorkflowAsync(
        WorkflowDefinition workflow,
        CancellationToken ct = default);

    /// <summary>
    /// Cria um handler string→string para uso como DelegateExecutor em Graph mode.
    /// Chama o LLM diretamente via IChatClient sem o overhead do AIAgent.
    /// Necessário porque WorkflowBuilder requer que todos os nós declarem o mesmo tipo (string).
    /// </summary>
    Task<Func<string, CancellationToken, Task<string>>> CreateLlmHandlerAsync(
        string agentId,
        CancellationToken ct = default);
}
