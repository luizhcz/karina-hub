using EfsAiHub.Core.Orchestration.Models;
using EfsAiHub.Core.Agents;
using EfsAiHub.Core.Orchestration.Workflows;

namespace EfsAiHub.Core.Orchestration.Interfaces;

public interface IAgentFactory
{
    /// <summary>
    /// Cria uma instância de agente do framework a partir de uma AgentDefinition.
    /// Retorna object para desacoplar do tipo concreto do framework.
    ///
    /// <paramref name="isStandaloneFlow"/> = true sinaliza que o agente roda
    /// num workflow standalone (sem continuidade entre chamadas) — desliga a
    /// composição do schema com <c>operationalMemory</c> e o middleware de
    /// memória correspondente. Default false preserva BC.
    ///
    /// <paramref name="resolvedVersionId"/> é o AgentVersionId EFETIVO já resolvido
    /// (quando o caller pinou uma versão) — usado só pra telemetria coerente
    /// (LlmTokenUsage). Null = current (fallback legado).
    /// </summary>
    Task<ExecutableWorkflow> CreateAgentAsync(
        AgentDefinition definition,
        CancellationToken ct = default,
        bool isStandaloneFlow = false,
        string? resolvedVersionId = null);

    /// <summary>
    /// Cria instâncias de agentes para todas as referências de um workflow.
    /// Valida que todos os agentes referenciados existem no repositório.
    /// </summary>
    Task<IReadOnlyDictionary<string, ExecutableWorkflow>> CreateAgentsForWorkflowAsync(
        WorkflowDefinition workflow,
        CancellationToken ct = default,
        bool freezeExact = false);

    /// <summary>
    /// Cria um handler string→string para uso como DelegateExecutor em Graph mode.
    /// Chama o LLM diretamente via IChatClient sem o overhead do AIAgent.
    /// Necessário porque WorkflowBuilder requer que todos os nós declarem o mesmo tipo (string).
    ///
    /// <paramref name="isStandaloneFlow"/> equivalente ao
    /// <see cref="CreateAgentAsync"/> — usado pelo WorkflowFactory pra
    /// propagar o <c>InputMode</c> do workflow ao build do handler.
    ///
    /// <paramref name="agentVersionId"/> é o pin de versão do agente vindo do
    /// snapshot do workflow (<c>WorkflowAgentReference.AgentVersionId</c>); null → current.
    ///
    /// <paramref name="freezeExact"/> = execução pinada via x-version: roda a versão
    /// EXATA pinada (sem patch-propagation). false (ao vivo) usa patch-propagation
    /// (propaga não-breaking, trava breaking).
    /// </summary>
    Task<Func<string, CancellationToken, Task<string>>> CreateLlmHandlerAsync(
        string agentId,
        string? agentVersionId = null,
        CancellationToken ct = default,
        bool isStandaloneFlow = false,
        bool freezeExact = false);
}
