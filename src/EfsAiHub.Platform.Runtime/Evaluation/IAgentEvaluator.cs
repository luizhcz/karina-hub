using EfsAiHub.Core.Agents.Evaluation;

namespace EfsAiHub.Platform.Runtime.Evaluation;

/// <summary>Única fronteira tocando Microsoft.Extensions.AI.Evaluation — tipos MS não escapam daqui (ver ADR 0015).</summary>
public interface IAgentEvaluator
{
    string Id { get; }

    EvaluatorKind Kind { get; }

    /// <summary>Retorna 1+ results por evaluator (ex.: ContentHarmEvaluator emite 4 métricas).</summary>
    Task<IReadOnlyList<EvaluationResult>> EvaluateAsync(
        EvaluationInvocation invocation,
        CancellationToken ct = default);
}

public sealed record EvaluationInvocation(
    string RunId,
    EvaluationTestCase TestCase,
    IReadOnlyList<Microsoft.Extensions.AI.ChatMessage> Messages,
    Microsoft.Extensions.AI.ChatResponse ModelResponse,
    int BindingIndex,
    int RepetitionIndex,
    string? AgentModelId);
