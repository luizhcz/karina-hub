using EfsAiHub.Core.Abstractions.Sharing;
using EfsAiHub.Platform.Runtime.Interfaces;
using Microsoft.Extensions.Options;

namespace EfsAiHub.Platform.Runtime.Services;

/// <summary>
/// Valida WorkflowDefinitions de forma independente.
/// Extraído do WorkflowService para SRP: validação é uma responsabilidade isolada.
/// </summary>
public class WorkflowValidator
{
    private static readonly HashSet<string> ValidCheckpointModes =
        new(StringComparer.OrdinalIgnoreCase) { "InMemory", "Postgres", "Blob" };

    private static readonly HashSet<string> ValidInputModes =
        new(StringComparer.OrdinalIgnoreCase) { "Standalone", "Chat" };

    private static readonly HashSet<string> ValidGroupChatRoles =
        new(StringComparer.OrdinalIgnoreCase) { "manager", "participant" };

    private readonly IAgentDefinitionRepository _agentRepo;
    private readonly IAgentVersionRepository? _versionRepo;
    private readonly IOptionsMonitor<SharingOptions>? _sharingOptions;

    public WorkflowValidator(
        IAgentDefinitionRepository agentRepo,
        IAgentVersionRepository? versionRepo = null,
        IOptionsMonitor<SharingOptions>? sharingOptions = null)
    {
        _agentRepo = agentRepo;
        _versionRepo = versionRepo;
        _sharingOptions = sharingOptions;
    }

    public async Task<(bool IsValid, IReadOnlyList<string> Errors)> ValidateAsync(
        WorkflowDefinition definition, CancellationToken ct = default)
    {
        var errors = new List<string>();

        ValidateCommonFields(definition, errors);
        ValidateVisibility(definition, errors);

        var agentIdSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var agentRef in definition.Agents)
        {
            if (!agentIdSet.Add(agentRef.AgentId))
                errors.Add($"AgentId duplicado em 'agents': '{agentRef.AgentId}'.");
        }

        ValidateAgentsPresence(definition, errors);
        ValidateGroupChatMode(definition, errors);
        ValidateExecutors(definition, errors);
        ValidateEdges(definition, agentIdSet, errors);
        ValidateConfiguration(definition, errors);
        await ValidateAgentReferencesAsync(definition, errors, ct);

        return (errors.Count == 0, errors);
    }

    private static void ValidateVisibility(WorkflowDefinition definition, List<string> errors)
    {
        if (!WorkflowDefinition.AllowedVisibilities.Contains(definition.Visibility))
            errors.Add(
                $"Visibility '{definition.Visibility}' inválida. Permitidos: " +
                string.Join(", ", WorkflowDefinition.AllowedVisibilities) + ".");
    }

    /// <summary>
    /// Valida transição de visibilidade. Promote (project→global) é permitido —
    /// visibilidade é metadata; workflow só falha se outro projeto tentar executar
    /// referenciando agents inacessíveis (validação acontece em runtime).
    /// </summary>
    public Task<(bool IsValid, IReadOnlyList<string> Errors)> ValidateVisibilityChangeAsync(
        WorkflowDefinition definition, string newVisibility, CancellationToken ct = default)
    {
        var errors = new List<string>();
        if (!WorkflowDefinition.AllowedVisibilities.Contains(newVisibility))
        {
            errors.Add(
                $"Visibility '{newVisibility}' inválida. Permitidos: " +
                string.Join(", ", WorkflowDefinition.AllowedVisibilities) + ".");
            return Task.FromResult<(bool, IReadOnlyList<string>)>((false, errors));
        }
        // Demote (global→project) é sempre permitido.
        // Promote (project→global): aceitamos sem validar refs cross-project — visibilidade
        // é metadata; o workflow só falha se alguém tentar executar de outro projeto
        // referenciando agents inacessíveis. UI orienta o operador.
        return Task.FromResult<(bool, IReadOnlyList<string>)>((true, errors));
    }

    private static void ValidateCommonFields(WorkflowDefinition definition, List<string> errors)
    {
        ValidationContext.RequireIdentifier(errors, definition.Id, "id");
        ValidationContext.RequireString(errors, definition.Name, "name", maxLength: 200);

        if (!string.IsNullOrWhiteSpace(definition.Version))
            ValidationContext.RequireMaxLength(errors, definition.Version, 50, "version");
    }

    private static void ValidateAgentsPresence(WorkflowDefinition definition, List<string> errors)
    {
        var mode = definition.OrchestrationMode;
        switch (mode)
        {
            case OrchestrationMode.Sequential or OrchestrationMode.Concurrent:
                if (definition.Agents.Count < 1)
                    errors.Add($"Modo '{mode}' requer ao menos 1 agente em 'agents'.");
                break;
            case OrchestrationMode.Handoff:
                if (definition.Agents.Count < 2)
                    errors.Add("Modo 'Handoff' requer ao menos 2 agentes em 'agents' (manager + ao menos 1 especialista).");
                break;
            case OrchestrationMode.GroupChat:
                if (definition.Agents.Count < 1)
                    errors.Add("Modo 'GroupChat' requer ao menos 1 agente em 'agents'.");
                break;
            case OrchestrationMode.Graph:
                if (definition.Agents.Count == 0 && definition.Executors.Count == 0)
                    errors.Add("Modo 'Graph' requer ao menos 1 agente OU 1 executor em 'agents'/'executors'.");
                break;
        }
    }

    private static void ValidateGroupChatMode(WorkflowDefinition definition, List<string> errors)
    {
        if (definition.OrchestrationMode != OrchestrationMode.GroupChat) return;

        var managerCount = 0;
        foreach (var agentRef in definition.Agents)
        {
            if (string.IsNullOrWhiteSpace(agentRef.Role)) continue;

            if (!ValidGroupChatRoles.Contains(agentRef.Role))
                errors.Add($"Role inválido para agente '{agentRef.AgentId}': '{agentRef.Role}'. Valores aceitos: manager, participant.");

            if (agentRef.Role.Equals("manager", StringComparison.OrdinalIgnoreCase))
                managerCount++;
        }
        if (managerCount > 1)
            errors.Add($"Modo 'GroupChat' permite no máximo 1 agente com role 'manager' (encontrado: {managerCount}).");
    }

    private static void ValidateExecutors(WorkflowDefinition definition, List<string> errors)
    {
        var mode = definition.OrchestrationMode;
        if (definition.Executors.Count > 0 && mode != OrchestrationMode.Graph)
            errors.Add($"Campo 'executors' é válido apenas no modo 'Graph' (modo atual: '{mode}').");

        var executorIdSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var executor in definition.Executors)
        {
            if (string.IsNullOrWhiteSpace(executor.Id))
            {
                errors.Add("Cada executor deve ter o campo 'id' não vazio.");
            }
            else if (!executorIdSet.Add(executor.Id))
            {
                errors.Add($"Id de executor duplicado: '{executor.Id}'.");
            }

            if (string.IsNullOrWhiteSpace(executor.FunctionName))
                errors.Add($"Executor '{executor.Id ?? "(sem id)"}' deve ter o campo 'functionName' não vazio.");
        }
    }

    private static void ValidateEdges(WorkflowDefinition definition, HashSet<string> agentIdSet, List<string> errors)
        => EdgeValidator.Validate(definition, agentIdSet, errors);

    private static void ValidateConfiguration(WorkflowDefinition definition, List<string> errors)
    {
        var cfg = definition.Configuration;

        if (cfg.TimeoutSeconds <= 0)
            errors.Add($"'configuration.timeoutSeconds' deve ser maior que zero (recebido: {cfg.TimeoutSeconds}).");
        if (cfg.MaxRounds is { } maxRounds && maxRounds <= 0)
            errors.Add($"'configuration.maxRounds' deve ser maior que zero quando definido (recebido: {maxRounds}).");
        if (cfg.MaxAgentInvocations <= 0)
            errors.Add($"'configuration.maxAgentInvocations' deve ser maior que zero (recebido: {cfg.MaxAgentInvocations}).");
        if (cfg.MaxHistoryMessages <= 0)
            errors.Add($"'configuration.maxHistoryMessages' deve ser maior que zero (recebido: {cfg.MaxHistoryMessages}).");
        if (cfg.MaxTokensPerExecution <= 0)
            errors.Add($"'configuration.maxTokensPerExecution' deve ser maior que zero (recebido: {cfg.MaxTokensPerExecution}).");
        if (!ValidCheckpointModes.Contains(cfg.CheckpointMode))
            errors.Add($"'configuration.checkpointMode' inválido: '{cfg.CheckpointMode}'. Valores aceitos: {string.Join(", ", ValidCheckpointModes)}.");
        if (!ValidInputModes.Contains(cfg.InputMode))
            errors.Add($"'configuration.inputMode' inválido: '{cfg.InputMode}'. Valores aceitos: {string.Join(", ", ValidInputModes)}.");
        if (cfg.ExposeAsAgent && string.IsNullOrWhiteSpace(cfg.ExposedAgentDescription))
            errors.Add("'configuration.exposedAgentDescription' é obrigatório quando 'exposeAsAgent' é true.");
    }

    private async Task ValidateAgentReferencesAsync(WorkflowDefinition definition, List<string> errors, CancellationToken ct)
    {
        if (definition.Agents.Count == 0) return;

        var mandatoryPin = _sharingOptions?.CurrentValue.MandatoryPin ?? false;

        var requestedIds = definition.Agents.Select(a => a.AgentId);
        var existingIds = await _agentRepo.GetExistingIdsAsync(requestedIds, ct);
        foreach (var agentRef in definition.Agents)
        {
            if (!existingIds.Contains(agentRef.AgentId))
            {
                errors.Add($"Agente '{agentRef.AgentId}' referenciado no workflow não foi encontrado.");
                continue;
            }

            // MandatoryPin: ref sem AgentVersionId é rejeitada com mensagem clara
            // direcionando o caller pra obter um pin via GET /api/agents/{id}/versions.
            if (mandatoryPin && string.IsNullOrEmpty(agentRef.AgentVersionId))
            {
                errors.Add(
                    $"Agent '{agentRef.AgentId}' precisa de pin de versão (Sharing:MandatoryPin=true). " +
                    $"Use GET /api/agents/{agentRef.AgentId}/versions e informe AgentVersionId.");
                continue;
            }

            // Pin: valida que a version existe e pertence ao agent.
            // _versionRepo é optional (BC com chamadores legacy), mas é provido em DI.
            if (!string.IsNullOrEmpty(agentRef.AgentVersionId) && _versionRepo is not null)
            {
                var pinned = await _versionRepo.GetByIdAsync(agentRef.AgentVersionId, ct);
                if (pinned is null)
                {
                    errors.Add(
                        $"AgentVersion '{agentRef.AgentVersionId}' (pin do agent '{agentRef.AgentId}') não foi encontrada.");
                }
                else if (!string.Equals(pinned.AgentDefinitionId, agentRef.AgentId, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add(
                        $"AgentVersion '{agentRef.AgentVersionId}' não pertence ao agent '{agentRef.AgentId}'.");
                }
            }
        }
    }
}
