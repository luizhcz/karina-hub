using EfsAiHub.Core.Agents;
using EfsAiHub.Core.Agents.RouterIntents;
using EfsAiHub.Core.Orchestration.Workflows;

namespace EfsAiHub.Platform.Runtime.Application.Services;

/// <summary>
/// Resolve a janela efetiva de histórico per-agente dentro de um workflow.
/// Centraliza a regra: Router usa <see cref="RouterDefaults.HistoryMessages"/>
/// (=5) por padrão, demais tipos herdam <c>WorkflowConfiguration.MaxHistoryMessages</c>
/// (=20). Override declarativo via <see cref="WorkflowAgentReference.HistoryOverride"/>
/// vence ambos quando setado.
///
/// Separa a regra do <c>ConversationService</c> (que precisa só do MAX entre
/// global e overrides pra carregar do DB) e do <c>ChatTurnContextMapper</c>
/// (que precisa do valor exato pra fatiar per-agente).
/// </summary>
public static class WorkflowAgentHistoryResolver
{
    /// <summary>
    /// Janela efetiva pra um agente específico do workflow. Null = sem slice
    /// (o agente recebe o histórico inteiro que o ConversationService carregou
    /// — preserva o comportamento legado pra Conversational/Worker/etc).
    /// </summary>
    public static int? Resolve(
        AgentType agentType,
        WorkflowAgentReference? workflowRef,
        WorkflowConfiguration? workflowConfig)
    {
        if (workflowRef?.HistoryOverride is { } overrideValue && overrideValue > 0)
            return overrideValue;

        if (agentType == AgentType.Router)
            return RouterDefaults.HistoryMessages;

        return null;
    }

    /// <summary>
    /// Maior janela exigida no workflow inteiro. Usado pelo
    /// <c>ConversationService</c> pra calcular quantas mensagens carregar do
    /// DB — o slice per-agente acontece depois no mapper. Sem isso, se um
    /// agente pede 50 mensagens via override e o global é 20, o histórico
    /// carregado já vem truncado em 20.
    /// </summary>
    public static int MaxRequired(
        WorkflowConfiguration? workflowConfig,
        IReadOnlyList<WorkflowAgentReference>? agents)
    {
        var globalMax = workflowConfig?.MaxHistoryMessages ?? 20;
        if (agents is null || agents.Count == 0)
            return globalMax;

        var overrideMax = 0;
        foreach (var a in agents)
        {
            if (a.HistoryOverride is { } v && v > overrideMax)
                overrideMax = v;
        }

        // Router default (5) é sempre <= 20, então não puxa o máximo. Só
        // overrides explícitos podem exigir mais do que o global.
        return Math.Max(globalMax, overrideMax);
    }
}
