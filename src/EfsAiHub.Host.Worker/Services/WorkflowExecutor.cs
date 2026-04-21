using EfsAiHub.Platform.Runtime.Interfaces;
using Microsoft.Extensions.Options;

namespace EfsAiHub.Host.Worker.Services;

/// <summary>
/// Implementação de IWorkflowExecutor.
/// Scoped — criado por execução dentro de um IServiceScope próprio.
/// </summary>
public class WorkflowExecutor : IWorkflowExecutor
{
    private readonly IWorkflowFactory _workflowFactory;
    private readonly WorkflowRunnerService _runner;
    private readonly IAgentDefinitionRepository _agentRepo;
    private readonly WorkflowEngineOptions _options;

    public WorkflowExecutor(
        IWorkflowFactory workflowFactory,
        WorkflowRunnerService runner,
        IAgentDefinitionRepository agentRepo,
        IOptions<WorkflowEngineOptions> options)
    {
        _workflowFactory = workflowFactory;
        _runner = runner;
        _agentRepo = agentRepo;
        _options = options.Value;
    }

    public async Task ExecuteAsync(
        WorkflowExecution execution,
        WorkflowDefinition definition,
        CancellationToken ct = default)
    {
        execution.Metadata.TryGetValue("startAgentId", out var startAgentId);
        var executableWorkflow = await _workflowFactory.BuildWorkflowAsync(definition, startAgentId, ct);

        int timeout = definition.Configuration.TimeoutSeconds > 0
            ? definition.Configuration.TimeoutSeconds
            : _options.DefaultTimeoutSeconds;

        var agentNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var guardMode = EfsAiHub.Core.Agents.Execution.AccountGuardMode.None;
        foreach (var agentRef in definition.Agents)
        {
            var agentDef = await _agentRepo.GetByIdAsync(agentRef.AgentId, ct);
            if (agentDef is null) continue;
            agentNames[agentDef.Id] = agentDef.Name;

            // Se qualquer agente do workflow tem AccountGuard habilitado → execução é ClientLocked.
            if (guardMode == EfsAiHub.Core.Agents.Execution.AccountGuardMode.None &&
                agentDef.Middlewares.Any(m =>
                    m.Enabled &&
                    string.Equals(m.Type, "AccountGuard", StringComparison.OrdinalIgnoreCase)))
            {
                guardMode = EfsAiHub.Core.Agents.Execution.AccountGuardMode.ClientLocked;
            }
        }

        await _runner.RunAsync(execution, executableWorkflow.Value, timeout,
            definition.Configuration.MaxAgentInvocations,
            definition.Configuration.MaxTokensPerExecution,
            definition.Configuration.MaxCostUsdPerExecution,
            guardMode,
            agentNames, definition.OrchestrationMode,
            definition.Configuration.EnrichmentRules,
            ct);
    }
}
