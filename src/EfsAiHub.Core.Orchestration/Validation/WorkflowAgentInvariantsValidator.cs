using EfsAiHub.Core.Agents;
using EfsAiHub.Core.Orchestration.Interfaces;
using EfsAiHub.Core.Orchestration.Workflows;

namespace EfsAiHub.Core.Orchestration.Validation;

/// <summary>
/// Valida invariantes que envolvem o tipo dos agentes referenciados em um workflow.
/// Hoje cobre: agente <c>Conversational</c> exige <c>InputMode=Chat</c> (depende de
/// ConversationId, AG-UI shared state e continuidade multi-turn). Pensado pra
/// abrigar regras futuras do mesmo gênero (ToolRunner requer Tools declaradas,
/// Router requer Intents, etc.) sem novo arquivo por regra.
///
/// Retorna <see cref="WorkflowInvariantError"/> estruturado pro controller
/// devolver envelope com code + hint.
/// </summary>
public sealed class WorkflowAgentInvariantsValidator
{
    private const string StandaloneInputMode = "Standalone";

    private readonly IAgentDefinitionRepository _agentRepo;

    public WorkflowAgentInvariantsValidator(IAgentDefinitionRepository agentRepo)
    {
        _agentRepo = agentRepo;
    }

    public async Task<IReadOnlyList<WorkflowInvariantError>> ValidateAsync(
        WorkflowDefinition definition, CancellationToken ct = default)
    {
        var errors = new List<WorkflowInvariantError>();
        if (definition.Agents.Count == 0) return errors;

        // Lookups em paralelo: workflows com muitos agentes saturariam o save
        // em N round-trips sequenciais. NpgsqlDataSource serializa por conexão
        // mas múltiplas conexões executam simultaneamente. Mesmo padrão do
        // EdgeInvariantsValidator.BuildSchemaSourceMapAsync.
        var agentIds = definition.Agents.Select(a => a.AgentId).Distinct().ToArray();
        var loaded = await Task.WhenAll(
            agentIds.Select(id => _agentRepo.GetByIdAsync(id, ct)));
        var byId = new Dictionary<string, AgentDefinition?>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < agentIds.Length; i++)
            byId[agentIds[i]] = loaded[i];

        var isStandalone = string.Equals(
            definition.Configuration.InputMode, StandaloneInputMode,
            StringComparison.OrdinalIgnoreCase);

        if (isStandalone)
            CollectConversationalRequiresChat(definition, byId, errors);

        return errors;
    }

    private static void CollectConversationalRequiresChat(
        WorkflowDefinition definition,
        IReadOnlyDictionary<string, AgentDefinition?> byId,
        List<WorkflowInvariantError> errors)
    {
        foreach (var agentRef in definition.Agents)
        {
            if (!byId.TryGetValue(agentRef.AgentId, out var agent) || agent is null) continue;
            if (agent.Type != AgentType.Conversational) continue;

            errors.Add(new WorkflowInvariantError(
                ErrorCode: WorkflowErrorCodes.ConversationalRequiresChat,
                Message:
                    $"Agente '{agent.Name}' ({agentRef.AgentId}) é Conversational e não pode ser referenciado em workflow Standalone. " +
                    "Conversational depende de ConversationId e continuidade multi-turn que só existem em InputMode=Chat.",
                Hint:
                    "Use uma implantação Chat (Router + branches) ou troque o tipo do agente."));
        }
    }
}
