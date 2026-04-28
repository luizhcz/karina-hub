using System.Text.Json;
using EfsAiHub.Core.Abstractions.Persistence;
using EfsAiHub.Core.Agents.Evaluation;
using MeaiEval = Microsoft.Extensions.AI.Evaluation;

namespace EfsAiHub.Platform.Runtime.Evaluation;

/// <summary>Fronteira MS Eval → domain. 1 EvaluationResult por métrica MEAI; scores normalizam Likert 1..5 → 0..1; pass threshold = 0.7.</summary>
public static class MeaiResultMapper
{
    public const double PassThreshold = 0.7;
    public const int MaxOutputContentChars = 8 * 1024;

    public static IReadOnlyList<EvaluationResult> Map(
        EvaluationInvocation invocation,
        string evaluatorBaseName,
        EvaluatorKind kind,
        MeaiEval.EvaluationResult meaiResult,
        TimeSpan elapsed,
        decimal? costUsd,
        int? inputTokens,
        int? outputTokens,
        string? agentModelId)
    {
        var rows = new List<EvaluationResult>();
        var metrics = meaiResult.Metrics ?? new Dictionary<string, MeaiEval.EvaluationMetric>();

        foreach (var (metricName, metric) in metrics)
        {
            var (score, passed) = ExtractScore(metric);
            var judgeModel = TryGetJudgeModel(metric);
            var responseText = TruncateOutput(invocation.ModelResponse.Text);
            var rawMetadata = SerializeMetricMetadata(metric);

            rows.Add(new EvaluationResult(
                ResultId: Guid.NewGuid().ToString("N"),
                RunId: invocation.RunId,
                CaseId: invocation.TestCase.CaseId,
                EvaluatorName: $"{evaluatorBaseName}.{metricName}",
                BindingIndex: invocation.BindingIndex,
                RepetitionIndex: invocation.RepetitionIndex,
                Score: score,
                Passed: passed,
                Reason: metric.Reason,
                OutputContent: responseText,
                JudgeModel: judgeModel ?? agentModelId,
                LatencyMs: elapsed.TotalMilliseconds,
                CostUsd: costUsd,
                InputTokens: inputTokens,
                OutputTokens: outputTokens,
                EvaluatorMetadata: rawMetadata,
                CreatedAt: DateTime.UtcNow));
        }

        return rows;
    }

    /// <summary>Caminho rápido para evaluators locais (sem chamada LLM) que emitem 1 métrica direta.</summary>
    public static EvaluationResult MapLocal(
        EvaluationInvocation invocation,
        string evaluatorName,
        decimal score,
        bool passed,
        string? reason,
        TimeSpan elapsed,
        string? agentModelId)
    {
        return new EvaluationResult(
            ResultId: Guid.NewGuid().ToString("N"),
            RunId: invocation.RunId,
            CaseId: invocation.TestCase.CaseId,
            EvaluatorName: evaluatorName,
            BindingIndex: invocation.BindingIndex,
            RepetitionIndex: invocation.RepetitionIndex,
            Score: score,
            Passed: passed,
            Reason: reason,
            OutputContent: TruncateOutput(invocation.ModelResponse.Text),
            JudgeModel: agentModelId,
            LatencyMs: elapsed.TotalMilliseconds,
            CostUsd: 0m,
            InputTokens: 0,
            OutputTokens: 0,
            EvaluatorMetadata: null,
            CreatedAt: DateTime.UtcNow);
    }

    private static (decimal? score, bool passed) ExtractScore(MeaiEval.EvaluationMetric metric)
    {
        switch (metric)
        {
            case MeaiEval.NumericMetric numeric when numeric.Value.HasValue:
            {
                // MEAI Quality emite Likert 1..5; normaliza pra 0..1.
                var raw = numeric.Value.Value;
                var normalized = raw > 1 ? Math.Clamp((raw - 1) / 4.0, 0, 1) : Math.Clamp(raw, 0, 1);
                return ((decimal)normalized, normalized >= PassThreshold);
            }
            case MeaiEval.BooleanMetric boolean when boolean.Value.HasValue:
            {
                var v = boolean.Value.Value ? 1.0m : 0.0m;
                return (v, boolean.Value.Value);
            }
            case MeaiEval.StringMetric str when !string.IsNullOrEmpty(str.Value):
            {
                if (string.Equals(str.Value, "Pass", StringComparison.OrdinalIgnoreCase))
                    return (1.0m, true);
                if (string.Equals(str.Value, "Fail", StringComparison.OrdinalIgnoreCase))
                    return (0.0m, false);
                return (null, false);
            }
            default:
                return (null, false);
        }
    }

    private static string? TryGetJudgeModel(MeaiEval.EvaluationMetric metric)
    {
        if (metric.Metadata is null) return null;
        // MEAI usa chaves heterogêneas dependendo do evaluator — cobre as 3 conhecidas.
        foreach (var key in new[] { "model", "ModelUsed", "modelId" })
        {
            if (metric.Metadata.TryGetValue(key, out var v) && v is string s && !string.IsNullOrEmpty(s))
                return s;
        }
        return null;
    }

    private static JsonDocument? SerializeMetricMetadata(MeaiEval.EvaluationMetric metric)
    {
        try
        {
            var payload = new
            {
                interpretation = metric.Interpretation is null
                    ? null
                    : new
                    {
                        rating = metric.Interpretation.Rating.ToString(),
                        reason = metric.Interpretation.Reason,
                        failed = metric.Interpretation.Failed
                    },
                diagnostics = metric.Diagnostics?.Select(d => new
                {
                    severity = d.Severity.ToString(),
                    message = d.Message
                }),
                metadata = metric.Metadata
            };
            return JsonDocument.Parse(JsonSerializer.Serialize(payload, JsonDefaults.Domain));
        }
        catch
        {
            return null;
        }
    }

    private static string? TruncateOutput(string? content)
    {
        if (string.IsNullOrEmpty(content)) return content;
        if (content.Length <= MaxOutputContentChars) return content;
        return content[..MaxOutputContentChars] + "…[truncated]";
    }
}
