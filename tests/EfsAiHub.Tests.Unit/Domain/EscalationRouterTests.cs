using EfsAiHub.Core.Agents.Signals;
using EfsAiHub.Core.Orchestration.Routing;
using Microsoft.Extensions.Logging.Abstractions;

namespace EfsAiHub.Tests.Unit.Domain;

[Trait("Category", "Unit")]
public class EscalationRouterTests
{
    private static EscalationRouter BuildRouter(Action<string?, bool>? sink = null)
        => new(NullLogger<EscalationRouter>.Instance, sink);

    private static AgentEscalationSignal Signal(
        string category,
        string reason = "test",
        string[]? tags = null) => new()
    {
        Category = category,
        Reason = reason,
        SuggestedTargetTags = tags ?? []
    };

    // ── Match por category ────────────────────────────────────────────────────

    [Fact]
    public void MatchCategory_Exata_RetornaTarget()
    {
        var router = BuildRouter();
        var rules = new List<RoutingRule>
        {
            new() { Match = "category:billing", TargetNodeId = "billing-agent" }
        };

        var result = router.Route(Signal("billing"), rules);

        result.Should().Be("billing-agent");
    }

    [Fact]
    public void MatchCategory_CaseInsensitive_RetornaTarget()
    {
        var router = BuildRouter();
        var rules = new List<RoutingRule>
        {
            new() { Match = "category:BILLING", TargetNodeId = "billing-agent" }
        };

        var result = router.Route(Signal("billing"), rules);

        result.Should().Be("billing-agent");
    }

    [Fact]
    public void SemMatch_RetornaNull()
    {
        var router = BuildRouter();
        var rules = new List<RoutingRule>
        {
            new() { Match = "category:billing", TargetNodeId = "billing-agent" }
        };

        var result = router.Route(Signal("support"), rules);

        result.Should().BeNull();
    }

    // ── Match any ─────────────────────────────────────────────────────────────

    [Fact]
    public void MatchAny_SempreRetornaTarget()
    {
        var router = BuildRouter();
        var rules = new List<RoutingRule>
        {
            new() { Match = "any", TargetNodeId = "fallback-agent" }
        };

        var result = router.Route(Signal("qualquer-categoria"), rules);

        result.Should().Be("fallback-agent");
    }

    // ── Prioridade ────────────────────────────────────────────────────────────

    [Fact]
    public void Prioridade_RegraComMaiorValorVence()
    {
        var router = BuildRouter();
        var rules = new List<RoutingRule>
        {
            new() { Match = "any",              TargetNodeId = "fallback",      Priority = 0 },
            new() { Match = "category:billing", TargetNodeId = "billing-agent", Priority = 10 }
        };

        var result = router.Route(Signal("billing"), rules);

        result.Should().Be("billing-agent");
    }

    [Fact]
    public void Prioridade_AnyFallback_QuandoNenhumEspecificoMatch()
    {
        var router = BuildRouter();
        var rules = new List<RoutingRule>
        {
            new() { Match = "any",              TargetNodeId = "fallback",      Priority = 0 },
            new() { Match = "category:billing", TargetNodeId = "billing-agent", Priority = 10 }
        };

        var result = router.Route(Signal("support"), rules);

        result.Should().Be("fallback");
    }

    // ── Match por tag ─────────────────────────────────────────────────────────

    [Fact]
    public void MatchTag_TagPresente_RetornaTarget()
    {
        var router = BuildRouter();
        var rules = new List<RoutingRule>
        {
            new() { Match = "tag:priority", TargetNodeId = "priority-agent" }
        };

        var result = router.Route(Signal("generic", tags: ["priority", "urgent"]), rules);

        result.Should().Be("priority-agent");
    }

    [Fact]
    public void MatchTag_TagAusente_RetornaNull()
    {
        var router = BuildRouter();
        var rules = new List<RoutingRule>
        {
            new() { Match = "tag:priority", TargetNodeId = "priority-agent" }
        };

        var result = router.Route(Signal("generic", tags: ["routine"]), rules);

        result.Should().BeNull();
    }

    // ── Match por regex ───────────────────────────────────────────────────────

    [Fact]
    public void MatchRegex_ReasonCompativel_RetornaTarget()
    {
        var router = BuildRouter();
        var rules = new List<RoutingRule>
        {
            new() { Match = "regex:^refund.*", TargetNodeId = "refund-agent" }
        };

        var result = router.Route(Signal("generic", reason: "refund requested"), rules);

        result.Should().Be("refund-agent");
    }

    [Fact]
    public void MatchRegex_ReasonIncompativel_RetornaNull()
    {
        var router = BuildRouter();
        var rules = new List<RoutingRule>
        {
            new() { Match = "regex:^refund.*", TargetNodeId = "refund-agent" }
        };

        var result = router.Route(Signal("generic", reason: "cancel subscription"), rules);

        result.Should().BeNull();
    }

    [Fact]
    public void MatchRegex_PatternInvalido_NaoLanca()
    {
        var router = BuildRouter();
        var rules = new List<RoutingRule>
        {
            new() { Match = "regex:[invalid(", TargetNodeId = "agent" }
        };

        var act = () => router.Route(Signal("any"), rules);

        act.Should().NotThrow();
    }

    // ── Edge cases ────────────────────────────────────────────────────────────

    [Fact]
    public void ListaVazia_RetornaNull()
    {
        var router = BuildRouter();

        var result = router.Route(Signal("billing"), []);

        result.Should().BeNull();
    }

    [Fact]
    public void MetricSink_ChamadoComCategoriaEResultado()
    {
        string? capturedCategory = null;
        bool? capturedMatch = null;
        var router = BuildRouter((cat, matched) => { capturedCategory = cat; capturedMatch = matched; });

        router.Route(Signal("billing"), [new() { Match = "any", TargetNodeId = "fallback" }]);

        capturedCategory.Should().Be("billing");
        capturedMatch.Should().BeTrue();
    }

    [Fact]
    public void MetricSink_SemMatch_ChamadoComFalse()
    {
        bool? capturedMatch = null;
        var router = BuildRouter((_, matched) => { capturedMatch = matched; });

        router.Route(Signal("billing"), [new() { Match = "category:support", TargetNodeId = "support" }]);

        capturedMatch.Should().BeFalse();
    }
}
