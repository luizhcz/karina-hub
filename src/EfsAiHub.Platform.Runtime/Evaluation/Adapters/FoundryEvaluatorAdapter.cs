using System.Diagnostics;
using EfsAiHub.Core.Agents.Evaluation;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation.Safety;
using MeaiEval = Microsoft.Extensions.AI.Evaluation;
using MeaiQuality = Microsoft.Extensions.AI.Evaluation.Quality;
using MeaiSafety = Microsoft.Extensions.AI.Evaluation.Safety;

namespace EfsAiHub.Platform.Runtime.Evaluation.Adapters;

/// <summary>MEAI Quality + Safety evaluators contra judge Foundry-deployment do projeto (ADR 0015).</summary>
public sealed class FoundryEvaluatorAdapter : IAgentEvaluator
{
    private readonly EvaluatorBinding _binding;
    private readonly MeaiEval.IEvaluator _evaluator;
    private readonly MeaiEval.ChatConfiguration _chatConfig;
    private readonly string _judgeDeployment;

    public FoundryEvaluatorAdapter(
        EvaluatorBinding binding,
        IChatClient foundryJudgeClient,
        string judgeDeployment,
        MeaiSafety.ContentSafetyServiceConfiguration? safetyConfig = null)
    {
        if (binding.Kind != EvaluatorKind.Foundry)
            throw new ArgumentException($"FoundryEvaluatorAdapter exige Kind=Foundry, recebido {binding.Kind}.", nameof(binding));

        _binding = binding;
        _judgeDeployment = judgeDeployment;
        _evaluator = ResolveEvaluator(binding.Name);

        // Safety evaluators precisam do ChatConfiguration retornado por safetyConfig.ToChatConfiguration(),
        // que redireciona pra Azure AI Content Safety API; Quality usa o chat client direto.
        if (IsSafetyEvaluator(binding.Name))
        {
            if (safetyConfig is null)
                throw new InvalidOperationException(
                    $"Foundry Safety evaluator '{binding.Name}' exige " +
                    "'projects.settings.evaluation.foundry.projectEndpoint' configurado. " +
                    "Sem isso, EvaluatorFactory deveria ter pulado este binding.");
            _chatConfig = safetyConfig.ToChatConfiguration(foundryJudgeClient);
        }
        else
        {
            _chatConfig = new MeaiEval.ChatConfiguration(foundryJudgeClient);
        }
    }

    public string Id => $"foundry.{_binding.Name}.{_binding.BindingIndex}";
    public EvaluatorKind Kind => EvaluatorKind.Foundry;

    public async Task<IReadOnlyList<EvaluationResult>> EvaluateAsync(
        EvaluationInvocation invocation,
        CancellationToken ct = default)
    {
        // Sem o contexto exigido, MEAI marcaria como failed ("additionalContext was not found").
        // Retornar vazio mantém o pass rate honesto.
        if (NotApplicable(_binding.Name, invocation))
            return Array.Empty<EvaluationResult>();

        var sw = Stopwatch.StartNew();
        try
        {
            var meaiResult = await _evaluator.EvaluateAsync(
                invocation.Messages,
                invocation.ModelResponse,
                _chatConfig,
                additionalContext: null,
                cancellationToken: ct);
            sw.Stop();

            return MeaiResultMapper.Map(
                invocation,
                evaluatorBaseName: $"foundry.{_binding.Name}",
                kind: EvaluatorKind.Foundry,
                meaiResult: meaiResult,
                elapsed: sw.Elapsed,
                costUsd: null,
                inputTokens: null,
                outputTokens: null,
                agentModelId: _judgeDeployment);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new[]
            {
                MeaiResultMapper.MapLocal(
                    invocation,
                    evaluatorName: $"foundry.{_binding.Name}.error",
                    score: 0m,
                    passed: false,
                    reason: $"Foundry evaluator '{_binding.Name}' lançou {ex.GetType().Name}: {ex.Message}",
                    elapsed: sw.Elapsed,
                    agentModelId: _judgeDeployment)
            };
        }
    }

    public static bool IsSafetyEvaluator(string name) =>
        name is "Violence" or "Sexual" or "SelfHarm" or "HateAndUnfairness";

    /// <summary>Evaluators context-dependent que devem ser pulados (sem result) quando o contexto exigido falta.</summary>
    public static bool NotApplicable(string evaluatorName, EvaluationInvocation invocation)
    {
        if (evaluatorName == "ToolCallAccuracy")
        {
            var hasToolCalls = invocation.ModelResponse.Messages
                .SelectMany(m => m.Contents)
                .OfType<Microsoft.Extensions.AI.FunctionCallContent>()
                .Any();
            return !hasToolCalls;
        }

        // Groundedness exige GroundednessEvaluatorContext — não modelamos esse contexto no test case ainda.
        if (evaluatorName == "Groundedness")
            return true;

        return false;
    }

    private static MeaiEval.IEvaluator ResolveEvaluator(string name) => name switch
    {
        "Relevance"          => new MeaiQuality.RelevanceEvaluator(),
        "Coherence"          => new MeaiQuality.CoherenceEvaluator(),
        "Groundedness"       => new MeaiQuality.GroundednessEvaluator(),
        "ToolCallAccuracy"   => new MeaiQuality.ToolCallAccuracyEvaluator(),
        "TaskAdherence"      => new MeaiQuality.TaskAdherenceEvaluator(),
        "Violence"           => new MeaiSafety.ViolenceEvaluator(),
        "Sexual"             => new MeaiSafety.SexualEvaluator(),
        "SelfHarm"           => new MeaiSafety.SelfHarmEvaluator(),
        "HateAndUnfairness"  => new MeaiSafety.HateAndUnfairnessEvaluator(),
        _ => throw new NotSupportedException(
            $"Foundry evaluator '{name}' não suportado. Use Relevance, Coherence, Groundedness, " +
            $"ToolCallAccuracy, TaskAdherence, Violence, Sexual, SelfHarm ou HateAndUnfairness.")
    };
}
