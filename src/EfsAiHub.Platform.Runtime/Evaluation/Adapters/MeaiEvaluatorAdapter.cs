using System.Diagnostics;
using EfsAiHub.Core.Agents.Evaluation;
using Microsoft.Extensions.AI;
using MeaiEval = Microsoft.Extensions.AI.Evaluation;
using MeaiQuality = Microsoft.Extensions.AI.Evaluation.Quality;

namespace EfsAiHub.Platform.Runtime.Evaluation.Adapters;

/// <summary>MEAI Quality evaluators usando o IChatClient default do projeto como judge; para judge dedicado por tenant use <see cref="FoundryEvaluatorAdapter"/>.</summary>
public sealed class MeaiEvaluatorAdapter : IAgentEvaluator
{
    private readonly EvaluatorBinding _binding;
    private readonly MeaiEval.IEvaluator _evaluator;
    private readonly MeaiEval.ChatConfiguration _chatConfig;
    private readonly string? _judgeModelId;

    public MeaiEvaluatorAdapter(EvaluatorBinding binding, IChatClient judgeClient, string? judgeModelId)
    {
        if (binding.Kind != EvaluatorKind.Meai)
            throw new ArgumentException($"MeaiEvaluatorAdapter exige Kind=Meai, recebido {binding.Kind}.", nameof(binding));
        _binding = binding;
        _judgeModelId = judgeModelId;
        _evaluator = ResolveEvaluator(binding.Name);
        _chatConfig = new MeaiEval.ChatConfiguration(judgeClient);
    }

    public string Id => $"meai.{_binding.Name}.{_binding.BindingIndex}";
    public EvaluatorKind Kind => EvaluatorKind.Meai;

    public async Task<IReadOnlyList<EvaluationResult>> EvaluateAsync(
        EvaluationInvocation invocation,
        CancellationToken ct = default)
    {
        if (FoundryEvaluatorAdapter.NotApplicable(_binding.Name, invocation))
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
                evaluatorBaseName: $"meai.{_binding.Name}",
                kind: EvaluatorKind.Meai,
                meaiResult: meaiResult,
                elapsed: sw.Elapsed,
                costUsd: null,
                inputTokens: null,
                outputTokens: null,
                agentModelId: _judgeModelId ?? invocation.AgentModelId);
        }
        catch (Exception ex)
        {
            sw.Stop();
            // Falha no judge vira result Passed=false em vez de derrubar a run inteira.
            return new[]
            {
                MeaiResultMapper.MapLocal(
                    invocation,
                    evaluatorName: $"meai.{_binding.Name}.error",
                    score: 0m,
                    passed: false,
                    reason: $"MEAI evaluator '{_binding.Name}' lançou {ex.GetType().Name}: {ex.Message}",
                    elapsed: sw.Elapsed,
                    agentModelId: _judgeModelId)
            };
        }
    }

    private static MeaiEval.IEvaluator ResolveEvaluator(string name) => name switch
    {
        "Relevance"          => new MeaiQuality.RelevanceEvaluator(),
        "Coherence"          => new MeaiQuality.CoherenceEvaluator(),
        "Groundedness"       => new MeaiQuality.GroundednessEvaluator(),
        "ToolCallAccuracy"   => new MeaiQuality.ToolCallAccuracyEvaluator(),
        "Fluency"            => new MeaiQuality.FluencyEvaluator(),
        "Completeness"       => new MeaiQuality.CompletenessEvaluator(),
        "Equivalence"        => new MeaiQuality.EquivalenceEvaluator(),
        "TaskAdherence"      => new MeaiQuality.TaskAdherenceEvaluator(),
        "IntentResolution"   => new MeaiQuality.IntentResolutionEvaluator(),
        "Retrieval"          => new MeaiQuality.RetrievalEvaluator(),
        _ => throw new NotSupportedException(
            $"MEAI evaluator '{name}' não suportado. Use Relevance, Coherence, Groundedness, " +
            $"ToolCallAccuracy, Fluency, Completeness, Equivalence, TaskAdherence, IntentResolution ou Retrieval.")
    };
}
