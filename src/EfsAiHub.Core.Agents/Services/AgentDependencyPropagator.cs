using EfsAiHub.Core.Abstractions.Exceptions;
using Microsoft.Extensions.Logging;

namespace EfsAiHub.Core.Agents.Services;

/// <summary>
/// Recompõe agentes referenciadores quando uma dependência (RouterIntent,
/// GenericTool, PredefinedModel ou master prompt) é editada. Cada
/// <c>PropagateAsync</c> resolve os agentes afetados, recompõe via
/// <see cref="IAgentDefinitionComposer"/> sobre a forma autoral (obtida
/// decompondo o estado persistido) e republica via
/// <see cref="IAgentDefinitionRepository.UpsertAsync"/> — o dual-write de
/// <c>agent_versions</c> é idempotente por ContentHash, então propagações
/// que não afetam o snapshot final são no-op silencioso.
/// </summary>
public interface IAgentDependencyPropagator
{
    Task PropagateRouterIntentEditAsync(string intentId, CancellationToken ct = default);
    Task PropagateGenericToolEditAsync(string genericToolId, CancellationToken ct = default);
    Task PropagatePredefinedModelEditAsync(string predefinedModelId, CancellationToken ct = default);
    Task PropagateAgentPromptChangeAsync(string agentId, CancellationToken ct = default);
}

public sealed class AgentDependencyPropagator : IAgentDependencyPropagator
{
    public const string ActorUserId = "system:dependency-propagator";

    private readonly IAgentDefinitionRepository _agentRepo;
    private readonly IAgentRouterIntentLinkRepository _routerLinks;
    private readonly IAgentDefinitionComposer _composer;
    private readonly IAgentDefinitionDecomposer _decomposer;
    private readonly IAgentDraftRepository _draftRepo;
    private readonly ILogger<AgentDependencyPropagator> _logger;

    public AgentDependencyPropagator(
        IAgentDefinitionRepository agentRepo,
        IAgentRouterIntentLinkRepository routerLinks,
        IAgentDefinitionComposer composer,
        IAgentDefinitionDecomposer decomposer,
        IAgentDraftRepository draftRepo,
        ILogger<AgentDependencyPropagator> logger)
    {
        _agentRepo = agentRepo;
        _routerLinks = routerLinks;
        _composer = composer;
        _decomposer = decomposer;
        _draftRepo = draftRepo;
        _logger = logger;
    }

    public async Task PropagateRouterIntentEditAsync(string intentId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(intentId)) return;
        var usages = await _routerLinks.ListAgentsForIntentAsync(intentId, ct);
        var agentIds = usages.Select(u => u.AgentId).ToList();
        await PropagateBatchAsync(agentIds, $"router intent '{intentId}'", ct);
    }

    public async Task PropagateGenericToolEditAsync(string genericToolId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(genericToolId)) return;
        var ids = await _agentRepo.ListAgentIdsUsingGenericToolAsync(genericToolId, ct);
        await PropagateBatchAsync(ids, $"generic tool '{genericToolId}'", ct);
    }

    public async Task PropagatePredefinedModelEditAsync(string predefinedModelId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(predefinedModelId)) return;
        var ids = await _agentRepo.ListAgentIdsUsingPredefinedModelAsync(predefinedModelId, ct);
        await PropagateBatchAsync(ids, $"predefined model '{predefinedModelId}'", ct);
    }

    public async Task PropagateAgentPromptChangeAsync(string agentId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(agentId)) return;
        await PropagateBatchAsync(new[] { agentId }, $"master prompt of agent '{agentId}'", ct);
    }

    /// <summary>
    /// Recompõe cada agente afetado em laço resiliente: falha em um agente
    /// (ex: dep referenciada mas inexistente) loga warning e segue pro
    /// próximo — propagation total nunca falha por causa de um único agente
    /// quebrado. O composer normalmente lança <see cref="DomainException"/>
    /// nesses casos.
    /// </summary>
    private async Task PropagateBatchAsync(IReadOnlyList<string> agentIds, string trigger, CancellationToken ct)
    {
        if (agentIds.Count == 0)
        {
            _logger.LogDebug(
                "[Propagator] Trigger '{Trigger}' — nenhum agente afetado.", trigger);
            return;
        }

        _logger.LogInformation(
            "[Propagator] Trigger '{Trigger}' — recompondo {Count} agente(s).",
            trigger, agentIds.Count);

        int succeeded = 0, failed = 0;
        foreach (var agentId in agentIds)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                var stored = await _agentRepo.GetByIdAsync(agentId, ct);
                if (stored is null)
                {
                    _logger.LogWarning(
                        "[Propagator] Agente '{AgentId}' referenciado mas não encontrado — skip.", agentId);
                    continue;
                }

                // Decompõe → recompõe → upsert. Recomposição re-resolve TODAS
                // as deps (intent/tool/model/skill/prompt), incorporando a
                // versão atual da que disparou a propagation.
                var authored = _decomposer.Decompose(stored);
                var composed = await _composer.ComposeAsync(authored, ct);

                var changeReason = $"Auto-snapshot: dependency change ({trigger}).";
                var published = await _agentRepo.UpsertAsync(
                    composed,
                    ct,
                    breakingChange: false,
                    changeReason: changeReason,
                    createdBy: ActorUserId,
                    isCosmeticOnly: false);

                // Trail em agent_approval_history. AppendAsync da version é
                // idempotente por ContentHash — quando o snapshot não mudou,
                // history ainda registra a tentativa pra rastreabilidade da
                // propagation (operacional querer ver "edit X disparou
                // recomposição do agente Y, sem efeito final").
                try
                {
                    await _draftRepo.AppendPropagationAsync(
                        published.Id, published.TenantId, ActorUserId, changeReason, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "[Propagator] Falha ao registrar trail PropagatedDependency pra '{AgentId}'.",
                        published.Id);
                }

                succeeded++;
            }
            catch (DomainException ex)
            {
                failed++;
                _logger.LogWarning(ex,
                    "[Propagator] Composer falhou pra agente '{AgentId}' (trigger '{Trigger}') — agente segue com snapshot atual.",
                    agentId, trigger);
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogError(ex,
                    "[Propagator] Erro inesperado ao recompor agente '{AgentId}' (trigger '{Trigger}').",
                    agentId, trigger);
            }
        }

        _logger.LogInformation(
            "[Propagator] Trigger '{Trigger}' concluído: {Succeeded} OK, {Failed} falhas.",
            trigger, succeeded, failed);
    }
}
