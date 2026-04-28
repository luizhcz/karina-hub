using System.Diagnostics;
using System.Text.Json;
using EfsAiHub.Core.Agents.Evaluation;
using Microsoft.Extensions.AI;

namespace EfsAiHub.Platform.Runtime.Evaluation.Adapters;

/// <summary>Evaluators determinísticos sem chamada LLM: KeywordCheck, ToolCalledCheck, ContainsExpected.</summary>
public sealed class LocalEvaluatorAdapter : IAgentEvaluator
{
    private readonly EvaluatorBinding _binding;

    public LocalEvaluatorAdapter(EvaluatorBinding binding)
    {
        if (binding.Kind != EvaluatorKind.Local)
            throw new ArgumentException($"LocalEvaluatorAdapter exige Kind=Local, recebido {binding.Kind}.", nameof(binding));
        _binding = binding;
    }

    public string Id => $"local.{_binding.Name}.{_binding.BindingIndex}";
    public EvaluatorKind Kind => EvaluatorKind.Local;

    public Task<IReadOnlyList<EvaluationResult>> EvaluateAsync(EvaluationInvocation invocation, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var (score, passed, reason) = _binding.Name switch
        {
            "KeywordCheck"     => RunKeywordCheck(invocation),
            "ToolCalledCheck"  => RunToolCalledCheck(invocation),
            "ContainsExpected" => RunContainsExpected(invocation),
            _ => (0m, false, $"Local evaluator '{_binding.Name}' não suportado.")
        };
        sw.Stop();

        var result = MeaiResultMapper.MapLocal(
            invocation,
            $"local.{_binding.Name}",
            score,
            passed,
            reason,
            sw.Elapsed,
            invocation.AgentModelId);

        return Task.FromResult<IReadOnlyList<EvaluationResult>>(new[] { result });
    }

    private (decimal score, bool passed, string reason) RunKeywordCheck(EvaluationInvocation invocation)
    {
        var output = invocation.ModelResponse.Text ?? string.Empty;
        var keywords = ExtractStringArray(_binding.Params, "keywords");
        if (keywords.Count == 0)
            return (0m, false, "KeywordCheck.params.keywords ausente ou vazio.");

        var caseSensitive = ExtractBool(_binding.Params, "caseSensitive", defaultValue: false);
        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var matchMode = ExtractString(_binding.Params, "matchMode", defaultValue: "any") ?? "any";

        var hits = keywords.Where(k => output.IndexOf(k, comparison) >= 0).ToList();
        bool passed = matchMode.Equals("all", StringComparison.OrdinalIgnoreCase)
            ? hits.Count == keywords.Count
            : hits.Count > 0;

        var score = (decimal)((double)hits.Count / keywords.Count);
        var reason = passed
            ? $"KeywordCheck OK: {hits.Count}/{keywords.Count} keywords presentes ({matchMode})."
            : $"KeywordCheck falhou: apenas {hits.Count}/{keywords.Count} keywords presentes ({matchMode}). Faltando: [{string.Join(", ", keywords.Except(hits, StringComparer.OrdinalIgnoreCase))}]";
        return (score, passed, reason);
    }

    private (decimal score, bool passed, string reason) RunToolCalledCheck(EvaluationInvocation invocation)
    {
        // params.expectedToolName tem prioridade; cai pro primeiro nome em case.ExpectedToolCalls.
        var expected = ExtractString(_binding.Params, "expectedToolName")
            ?? ExtractFirstToolName(invocation.TestCase.ExpectedToolCalls);
        if (string.IsNullOrEmpty(expected))
            return (0m, false, "ToolCalledCheck: nem params.expectedToolName nem case.ExpectedToolCalls populados.");

        var calledTools = invocation.ModelResponse.Messages
            .SelectMany(m => m.Contents)
            .OfType<FunctionCallContent>()
            .Select(fc => fc.Name)
            .ToList();

        var present = calledTools.Any(name => string.Equals(name, expected, StringComparison.Ordinal));
        var reason = present
            ? $"ToolCalledCheck OK: tool '{expected}' invocada."
            : $"ToolCalledCheck falhou: tool '{expected}' não invocada (chamadas: [{string.Join(", ", calledTools)}]).";
        return (present ? 1m : 0m, present, reason);
    }

    private (decimal score, bool passed, string reason) RunContainsExpected(EvaluationInvocation invocation)
    {
        var expected = invocation.TestCase.ExpectedOutput;
        if (string.IsNullOrEmpty(expected))
            return (0m, false, "ContainsExpected: case.ExpectedOutput vazio.");

        var output = invocation.ModelResponse.Text ?? string.Empty;
        var caseSensitive = ExtractBool(_binding.Params, "caseSensitive", defaultValue: false);
        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var contains = output.IndexOf(expected, comparison) >= 0;
        var reason = contains
            ? "ContainsExpected OK: output contém o ExpectedOutput."
            : "ContainsExpected falhou: output não contém o ExpectedOutput.";
        return (contains ? 1m : 0m, contains, reason);
    }

    private static IReadOnlyList<string> ExtractStringArray(JsonDocument? @params, string key)
    {
        if (@params is null) return Array.Empty<string>();
        if (!@params.RootElement.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();
        var list = new List<string>(arr.GetArrayLength());
        foreach (var item in arr.EnumerateArray())
            if (item.ValueKind == JsonValueKind.String) list.Add(item.GetString() ?? string.Empty);
        return list;
    }

    private static string? ExtractString(JsonDocument? @params, string key, string? defaultValue = null)
    {
        if (@params is null) return defaultValue;
        if (!@params.RootElement.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.String)
            return defaultValue;
        return v.GetString() ?? defaultValue;
    }

    private static bool ExtractBool(JsonDocument? @params, string key, bool defaultValue)
    {
        if (@params is null) return defaultValue;
        if (!@params.RootElement.TryGetProperty(key, out var v)) return defaultValue;
        if (v.ValueKind == JsonValueKind.True) return true;
        if (v.ValueKind == JsonValueKind.False) return false;
        return defaultValue;
    }

    private static string? ExtractFirstToolName(JsonDocument? expectedToolCalls)
    {
        if (expectedToolCalls is null) return null;
        if (expectedToolCalls.RootElement.ValueKind != JsonValueKind.Array) return null;
        foreach (var item in expectedToolCalls.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            if (!item.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String) continue;
            return name.GetString();
        }
        return null;
    }
}
