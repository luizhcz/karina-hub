using EfsAiHub.Core.Agents.Evaluation;
using EfsAiHub.Platform.Runtime.Evaluation;
using Microsoft.Extensions.AI;
using MeaiEval = Microsoft.Extensions.AI.Evaluation;
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace EfsAiHub.Tests.Unit.Evaluation;

/// <summary>
/// Contract tests do <see cref="MeaiResultMapper"/>. Garante que o mapper
/// preserva campos críticos (score normalizado, passed, reason, judge_model)
/// de <see cref="MeaiEval.EvaluationResult"/> mesmo após bumps do SDK.
///
/// Re-rodar a cada upgrade do <c>Microsoft.Extensions.AI.Evaluation</c>.
/// Falha → quebra build, força revisão consciente do mapper.
/// </summary>
public sealed class MeaiResultMapperTests
{
    private static EvaluationInvocation Invocation(string runId = "run-1") => new(
        RunId: runId,
        TestCase: new EvaluationTestCase(
            CaseId: "case-1",
            TestSetVersionId: "tsv-1",
            Index: 0,
            Input: "What's the weather?",
            ExpectedOutput: null,
            ExpectedToolCalls: null,
            Tags: Array.Empty<string>(),
            Weight: 1.0,
            CreatedAt: DateTime.UtcNow),
        Messages: new List<AiChatMessage> { new(ChatRole.User, "What's the weather?") },
        ModelResponse: new ChatResponse(new AiChatMessage(ChatRole.Assistant, "Sunny, 25°C")),
        BindingIndex: 0,
        RepetitionIndex: 0,
        AgentModelId: "gpt-4o");

    [Fact]
    public void NumericMetric_Likert5_Normaliza_Para_0_a_1()
    {
        // MEAI Quality emite NumericMetric com score 1..5. Mapper normaliza pra 0..1.
        var meaiResult = new MeaiEval.EvaluationResult(new MeaiEval.EvaluationMetric[]
        {
            new MeaiEval.NumericMetric("Relevance", 5.0, "Highly relevant.")
        });

        var rows = MeaiResultMapper.Map(
            Invocation(),
            evaluatorBaseName: "meai.Relevance",
            kind: EvaluatorKind.Meai,
            meaiResult: meaiResult,
            elapsed: TimeSpan.FromMilliseconds(150),
            costUsd: 0.001m, inputTokens: 100, outputTokens: 50, agentModelId: "gpt-4o");

        rows.Should().ContainSingle();
        rows[0].Score.Should().Be(1.0m);
        rows[0].Passed.Should().BeTrue(); // 1.0 >= 0.7 threshold
        rows[0].Reason.Should().Be("Highly relevant.");
    }

    [Fact]
    public void NumericMetric_Likert3_Normaliza_Para_0_5_E_NaoPassa()
    {
        // Likert 3 → (3-1)/4 = 0.5 < 0.7 threshold → Passed=false
        var meaiResult = new MeaiEval.EvaluationResult(new MeaiEval.EvaluationMetric[]
        {
            new MeaiEval.NumericMetric("Coherence", 3.0, "Average coherence.")
        });

        var rows = MeaiResultMapper.Map(Invocation(), "meai.Coherence", EvaluatorKind.Meai,
            meaiResult, TimeSpan.Zero, null, null, null, null);

        rows[0].Score.Should().Be(0.5m);
        rows[0].Passed.Should().BeFalse();
    }

    [Fact]
    public void NumericMetric_Score_Ja_Em_0_a_1_Preserva()
    {
        // Quando o evaluator já emite 0..1 (raro mas possível), mapper preserva.
        var meaiResult = new MeaiEval.EvaluationResult(new MeaiEval.EvaluationMetric[]
        {
            new MeaiEval.NumericMetric("CustomMetric", 0.85, "Good.")
        });

        var rows = MeaiResultMapper.Map(Invocation(), "meai.CustomMetric", EvaluatorKind.Meai,
            meaiResult, TimeSpan.Zero, null, null, null, null);

        rows[0].Score.Should().Be(0.85m);
        rows[0].Passed.Should().BeTrue();
    }

    [Fact]
    public void BooleanMetric_True_Mapeia_Score_1_Passed_True()
    {
        var meaiResult = new MeaiEval.EvaluationResult(new MeaiEval.EvaluationMetric[]
        {
            new MeaiEval.BooleanMetric("ToolCalled", true, "Tool was invoked.")
        });

        var rows = MeaiResultMapper.Map(Invocation(), "meai.ToolCalled", EvaluatorKind.Meai,
            meaiResult, TimeSpan.Zero, null, null, null, null);

        rows[0].Score.Should().Be(1.0m);
        rows[0].Passed.Should().BeTrue();
    }

    [Fact]
    public void BooleanMetric_False_Mapeia_Score_0_Passed_False()
    {
        var meaiResult = new MeaiEval.EvaluationResult(new MeaiEval.EvaluationMetric[]
        {
            new MeaiEval.BooleanMetric("ToolCalled", false, "Tool not invoked.")
        });

        var rows = MeaiResultMapper.Map(Invocation(), "meai.ToolCalled", EvaluatorKind.Meai,
            meaiResult, TimeSpan.Zero, null, null, null, null);

        rows[0].Score.Should().Be(0.0m);
        rows[0].Passed.Should().BeFalse();
    }

    [Fact]
    public void StringMetric_Pass_Mapeia_Score_1_Passed_True()
    {
        var meaiResult = new MeaiEval.EvaluationResult(new MeaiEval.EvaluationMetric[]
        {
            new MeaiEval.StringMetric("Verdict", "Pass", "All good.")
        });

        var rows = MeaiResultMapper.Map(Invocation(), "meai.Verdict", EvaluatorKind.Meai,
            meaiResult, TimeSpan.Zero, null, null, null, null);

        rows[0].Score.Should().Be(1.0m);
        rows[0].Passed.Should().BeTrue();
    }

    [Fact]
    public void StringMetric_Fail_Mapeia_Score_0_Passed_False()
    {
        var meaiResult = new MeaiEval.EvaluationResult(new MeaiEval.EvaluationMetric[]
        {
            new MeaiEval.StringMetric("Verdict", "Fail", "Wrong.")
        });

        var rows = MeaiResultMapper.Map(Invocation(), "meai.Verdict", EvaluatorKind.Meai,
            meaiResult, TimeSpan.Zero, null, null, null, null);

        rows[0].Score.Should().Be(0.0m);
        rows[0].Passed.Should().BeFalse();
    }

    [Fact]
    public void Multiple_Metrics_Mapeia_Para_N_Rows()
    {
        // ContentHarmEvaluator emite 4 métricas. Mapper deve gerar N rows, uma por métrica.
        var meaiResult = new MeaiEval.EvaluationResult(new MeaiEval.EvaluationMetric[]
        {
            new MeaiEval.NumericMetric("Violence", 1.0, "Safe."),
            new MeaiEval.NumericMetric("Sexual", 1.0, "Safe."),
            new MeaiEval.NumericMetric("SelfHarm", 1.0, "Safe."),
            new MeaiEval.NumericMetric("HateUnfairness", 1.0, "Safe.")
        });

        var rows = MeaiResultMapper.Map(Invocation(), "foundry.ContentHarm", EvaluatorKind.Foundry,
            meaiResult, TimeSpan.Zero, null, null, null, null);

        rows.Should().HaveCount(4);
        rows.Select(r => r.EvaluatorName).Should().Contain([
            "foundry.ContentHarm.Violence",
            "foundry.ContentHarm.Sexual",
            "foundry.ContentHarm.SelfHarm",
            "foundry.ContentHarm.HateUnfairness"
        ]);
    }

    [Fact]
    public void OutputContent_Trunca_Em_8KB()
    {
        // Cobre bug-by-design: payloads grandes não inflam evaluation_results.
        var bigOutput = new string('x', 10_000);
        var invocation = Invocation() with
        {
            ModelResponse = new ChatResponse(new AiChatMessage(ChatRole.Assistant, bigOutput))
        };

        var meaiResult = new MeaiEval.EvaluationResult(new MeaiEval.EvaluationMetric[]
        {
            new MeaiEval.NumericMetric("Relevance", 5.0, "ok")
        });

        var rows = MeaiResultMapper.Map(invocation, "meai.Relevance", EvaluatorKind.Meai,
            meaiResult, TimeSpan.Zero, null, null, null, null);

        rows[0].OutputContent!.Length.Should().BeLessThan(bigOutput.Length);
        rows[0].OutputContent!.Should().EndWith("…[truncated]");
    }

    [Fact]
    public void MapLocal_KeywordCheck_Score_Preservado_Exato()
    {
        // Local evaluator entrega score já em 0..1; mapper local não normaliza.
        var result = MeaiResultMapper.MapLocal(
            Invocation(),
            evaluatorName: "local.KeywordCheck",
            score: 0.5m,
            passed: false,
            reason: "Apenas 1/2 keywords.",
            elapsed: TimeSpan.FromMilliseconds(2),
            agentModelId: "gpt-4o");

        result.Score.Should().Be(0.5m);
        result.Passed.Should().BeFalse();
        result.Reason.Should().Be("Apenas 1/2 keywords.");
        result.JudgeModel.Should().Be("gpt-4o");
        result.CostUsd.Should().Be(0m);
    }
}
