using EfsAiHub.Core.Agents;
using EfsAiHub.Core.Agents.Evaluation;
using EfsAiHub.Platform.Runtime.Evaluation.Adapters;
using Microsoft.Extensions.AI;

namespace EfsAiHub.Platform.Runtime.Evaluation;

/// <summary>Constrói <see cref="IAgentEvaluator"/>s a partir de <see cref="EvaluatorConfigVersion"/>; valida bindings e resolve judge clients (ADR 0015).</summary>
public sealed class EvaluatorFactory
{
    private readonly IFoundryJudgeClientFactory? _foundryFactory;
    private readonly ILogger<EvaluatorFactory> _logger;

    public EvaluatorFactory(
        ILogger<EvaluatorFactory> logger,
        IFoundryJudgeClientFactory? foundryFactory = null)
    {
        _logger = logger;
        _foundryFactory = foundryFactory;
    }

    /// <summary>Constrói os evaluators ativos da config; async porque <see cref="IFoundryJudgeClientFactory.ResolveAsync"/> faz I/O.</summary>
    public async Task<IReadOnlyList<IAgentEvaluator>> BuildAsync(
        EvaluatorConfigVersion version,
        IChatClient agentJudgeClient,
        string? agentJudgeModelId,
        string projectId,
        IReadOnlySet<string> agentToolNames,
        CancellationToken ct = default)
    {
        var enabled = version.Bindings.Where(b => b.Enabled).ToList();
        if (enabled.Count == 0)
            throw new InvalidOperationException(
                $"EvaluatorConfigVersion '{version.EvaluatorConfigVersionId}' não tem nenhum binding enabled.");

        // Azure AI Content Safety exige Foundry Project endpoint + TokenCredential.
        // Sem ProjectEndpoint, pula Safety bindings (Quality/Local/Meai continuam) em vez de derrubar a run.
        var safetyBindings = enabled
            .Where(b => b.Kind == EvaluatorKind.Foundry && Adapters.FoundryEvaluatorAdapter.IsSafetyEvaluator(b.Name))
            .ToList();
        IReadOnlyList<EvaluatorBinding> skippedSafety = Array.Empty<EvaluatorBinding>();
        if (safetyBindings.Count > 0)
        {
            var foundryConfig = _foundryFactory is null ? null : await _foundryFactory.ResolveAsync(projectId, ct);
            if (foundryConfig?.SafetyConfig is null)
            {
                _logger.LogWarning(
                    "[EvaluatorFactory] Skipping {Count} Foundry Safety binding(s) ({Names}) — projeto '{ProjectId}' não tem 'evaluation.foundry.projectEndpoint' configurado.",
                    safetyBindings.Count, string.Join(", ", safetyBindings.Select(b => b.Name)), projectId);
                skippedSafety = safetyBindings;
            }
        }
        var supported = enabled.Except(skippedSafety).ToList();
        if (supported.Count == 0)
            throw new InvalidOperationException(
                $"EvaluatorConfigVersion '{version.EvaluatorConfigVersionId}' só tem bindings desabilitados ou não suportados.");

        var evaluators = new List<IAgentEvaluator>(supported.Count);
        foreach (var binding in supported)
        {
            IAgentEvaluator adapter = binding.Kind switch
            {
                EvaluatorKind.Local => new LocalEvaluatorAdapter(binding),
                EvaluatorKind.Meai => new MeaiEvaluatorAdapter(binding, agentJudgeClient, agentJudgeModelId),
                EvaluatorKind.Foundry => await BuildFoundryAdapterAsync(binding, projectId, ct),
                _ => throw new NotSupportedException($"EvaluatorKind '{binding.Kind}' não suportado.")
            };
            evaluators.Add(adapter);
        }
        return evaluators;
    }

    /// <summary>Valida que cada ExpectedToolCalls referencia tool existente no AgentVersion; lança <see cref="EvaluationValidationException"/> em órfã.</summary>
    public static void ValidateCasesAgainstAgent(
        IReadOnlyList<EvaluationTestCase> cases,
        IReadOnlySet<string> agentToolNames)
    {
        var orphans = new List<(string caseId, string toolName)>();
        foreach (var c in cases)
        {
            if (c.ExpectedToolCalls is null) continue;
            if (c.ExpectedToolCalls.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array) continue;
            foreach (var item in c.ExpectedToolCalls.RootElement.EnumerateArray())
            {
                if (!item.TryGetProperty("name", out var name) ||
                    name.ValueKind != System.Text.Json.JsonValueKind.String) continue;
                var toolName = name.GetString();
                if (string.IsNullOrEmpty(toolName)) continue;
                if (!agentToolNames.Contains(toolName))
                    orphans.Add((c.CaseId, toolName));
            }
        }

        if (orphans.Count > 0)
        {
            var msg = string.Join("; ", orphans.Select(o =>
                $"case '{o.caseId}' referencia tool '{o.toolName}'"));
            throw new EvaluationValidationException(
                $"ExpectedToolCalls referenciam tools que não existem no AgentVersion atual: {msg}. " +
                $"Bump do test set version (removendo ExpectedToolCalls obsoletos) ou bump do agent version (re-adicionando a tool) é necessário.");
        }
    }

    private async Task<IAgentEvaluator> BuildFoundryAdapterAsync(
        EvaluatorBinding binding,
        string projectId,
        CancellationToken ct)
    {
        if (_foundryFactory is null)
        {
            throw new EvaluationValidationException(
                $"Foundry evaluator '{binding.Name}' configurado mas IFoundryJudgeClientFactory não registrado em DI.");
        }

        var config = await _foundryFactory.ResolveAsync(projectId, ct);
        if (config is null)
        {
            throw new EvaluationValidationException(
                $"Project '{projectId}' não tem 'evaluation.foundry' configurado em settings. " +
                $"Foundry evaluator '{binding.Name}' rejeitado.");
        }

        return new FoundryEvaluatorAdapter(binding, config.Client, config.DeploymentName, config.SafetyConfig);
    }
}

/// <summary>Resolve <see cref="IChatClient"/> apontando pra deployment Foundry dedicado do projeto.</summary>
public interface IFoundryJudgeClientFactory
{
    Task<FoundryJudgeConfig?> ResolveAsync(string projectId, CancellationToken ct);
}

public sealed record FoundryJudgeConfig(
    IChatClient Client,
    string DeploymentName,
    string Endpoint,
    Microsoft.Extensions.AI.Evaluation.Safety.ContentSafetyServiceConfiguration? SafetyConfig);

/// <summary>Erro de config inválida — endpoints respondem 400 com Reason.</summary>
public sealed class EvaluationValidationException : Exception
{
    public EvaluationValidationException(string message) : base(message) { }
}
