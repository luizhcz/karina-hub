using EfsAiHub.Core.Agents;
using EfsAiHub.Core.Orchestration.Workflows;

namespace EfsAiHub.Core.Orchestration.Validation;

/// <summary>
/// Computa warnings não-bloqueantes pra Chat deploys com branch agents
/// Conversational sem validation em Chat Sandbox.
///
/// Regras:
///   - Só roda em workflows kind=chat (deploys reais; Chat Sandbox próprio
///     herda kind=chat-sandbox e não é alvo dessa checagem).
///   - Só inspeciona agentes Conversational com role!=Router (Router é entry
///     point, sempre tem validation própria via outro path).
///   - Compara o pin do save (agentVersionId) com o gate
///     <c>agent_definitions.LastChatSandboxValidatedAgentVersionId</c>.
/// </summary>
public sealed class ChatValidationWarningCalculator
{
    private const string RouterRole = "Router";

    private readonly IAgentDefinitionRepository _agentRepo;
    private readonly IAgentVersionRepository? _versionRepo;

    public ChatValidationWarningCalculator(
        IAgentDefinitionRepository agentRepo,
        IAgentVersionRepository? versionRepo = null)
    {
        _agentRepo = agentRepo;
        _versionRepo = versionRepo;
    }

    public async Task<IReadOnlyList<ChatValidationWarning>> ComputeAsync(
        WorkflowDefinition definition, CancellationToken ct = default)
    {
        if (!IsChatProductionDeploy(definition)) return Array.Empty<ChatValidationWarning>();

        var warnings = new List<ChatValidationWarning>();
        foreach (var agentRef in definition.Agents)
        {
            if (string.Equals(agentRef.Role, RouterRole, StringComparison.OrdinalIgnoreCase)) continue;
            if (string.IsNullOrEmpty(agentRef.AgentVersionId)) continue;

            var agent = await _agentRepo.GetByIdAsync(agentRef.AgentId, ct);
            if (agent is null || agent.Type != AgentType.Conversational) continue;

            int? pinnedRevision = await ResolveRevisionAsync(agentRef.AgentVersionId, ct);

            if (string.IsNullOrEmpty(agent.LastChatSandboxValidatedAgentVersionId))
            {
                warnings.Add(new ChatValidationWarning(
                    AgentId: agent.Id,
                    AgentName: agent.Name,
                    Reason: ChatValidationReasons.NoChatSandboxValidation,
                    PinnedAgentVersionId: agentRef.AgentVersionId!,
                    PinnedRevision: pinnedRevision));
                continue;
            }

            if (!string.Equals(
                agent.LastChatSandboxValidatedAgentVersionId,
                agentRef.AgentVersionId,
                StringComparison.OrdinalIgnoreCase))
            {
                var validatedRevision = await ResolveRevisionAsync(
                    agent.LastChatSandboxValidatedAgentVersionId, ct);
                warnings.Add(new ChatValidationWarning(
                    AgentId: agent.Id,
                    AgentName: agent.Name,
                    Reason: ChatValidationReasons.ValidationStale,
                    PinnedAgentVersionId: agentRef.AgentVersionId!,
                    PinnedRevision: pinnedRevision,
                    ValidatedAgentVersionId: agent.LastChatSandboxValidatedAgentVersionId,
                    ValidatedRevision: validatedRevision));
            }
        }

        return warnings;
    }

    private static bool IsChatProductionDeploy(WorkflowDefinition definition)
    {
        if (definition.Metadata is null) return false;
        if (!definition.Metadata.TryGetValue(AgentSandboxMetadata.DeploymentKindKey, out var kind)) return false;
        if (!string.Equals(kind, AgentSandboxMetadata.DeploymentKindChat, StringComparison.OrdinalIgnoreCase)) return false;

        // Chat Sandbox tem deploymentKind=chat MAS é efêmero (kind=chat-sandbox).
        // Warnings só fazem sentido em deploys reais — sandbox próprio bypass.
        if (definition.Metadata.TryGetValue(AgentSandboxMetadata.KindKey, out var kindSpecific)
            && string.Equals(kindSpecific, AgentSandboxMetadata.KindChatSandbox, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        return true;
    }

    private async Task<int?> ResolveRevisionAsync(string agentVersionId, CancellationToken ct)
    {
        if (_versionRepo is null) return null;
        try
        {
            var version = await _versionRepo.GetByIdAsync(agentVersionId, ct);
            return version?.Revision;
        }
        catch
        {
            return null;
        }
    }
}
