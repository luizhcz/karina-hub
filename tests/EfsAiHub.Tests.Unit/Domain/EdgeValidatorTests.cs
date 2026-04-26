using System.Text.Json;
using EfsAiHub.Core.Orchestration.Validation;

namespace EfsAiHub.Tests.Unit.Domain;

[Trait("Category", "Unit")]
public class EdgeValidatorTests
{
    // ── helpers ────────────────────────────────────────────────────────────────

    private static WorkflowDefinition MakeGraph(
        List<WorkflowAgentReference>? agents = null,
        List<WorkflowExecutorStep>? executors = null,
        List<WorkflowEdge>? edges = null) => new()
    {
        Id = "wf-test", Name = "Test",
        OrchestrationMode = OrchestrationMode.Graph,
        Agents = agents ?? [new WorkflowAgentReference { AgentId = "agent-a" }],
        Executors = executors ?? [],
        Edges = edges ?? []
    };

    private static HashSet<string> AgentIds(WorkflowDefinition def) =>
        def.Agents.Select(a => a.AgentId).ToHashSet();

    // ── Direct ─────────────────────────────────────────────────────────────────

    [Fact]
    public void DirectEdge_FromToValidos_SemErros()
    {
        var def = MakeGraph(
            agents: [new WorkflowAgentReference { AgentId = "a" }, new WorkflowAgentReference { AgentId = "b" }],
            edges: [new WorkflowEdge { EdgeType = WorkflowEdgeType.Direct, From = "a", To = "b" }]);

        var errors = new List<string>();
        EdgeValidator.Validate(def, AgentIds(def), errors);

        errors.Should().BeEmpty();
    }

    [Fact]
    public void DirectEdge_FromInexistente_AdicionaErro()
    {
        var def = MakeGraph(
            agents: [new WorkflowAgentReference { AgentId = "b" }],
            edges: [new WorkflowEdge { EdgeType = WorkflowEdgeType.Direct, From = "nao-existe", To = "b" }]);

        var errors = new List<string>();
        EdgeValidator.Validate(def, AgentIds(def), errors);

        errors.Should().ContainSingle(e => e.Contains("nao-existe"));
    }

    [Fact]
    public void DirectEdge_ToInexistente_AdicionaErro()
    {
        var def = MakeGraph(
            agents: [new WorkflowAgentReference { AgentId = "a" }],
            edges: [new WorkflowEdge { EdgeType = WorkflowEdgeType.Direct, From = "a", To = "nao-existe" }]);

        var errors = new List<string>();
        EdgeValidator.Validate(def, AgentIds(def), errors);

        errors.Should().ContainSingle(e => e.Contains("nao-existe"));
    }

    // ── Conditional ────────────────────────────────────────────────────────────

    [Fact]
    public void ConditionalEdge_ComPredicate_SemErros()
    {
        var def = MakeGraph(
            agents: [new WorkflowAgentReference { AgentId = "a" }, new WorkflowAgentReference { AgentId = "b" }],
            edges: [new WorkflowEdge
            {
                EdgeType = WorkflowEdgeType.Conditional,
                From = "a",
                To = "b",
                Predicate = new EdgePredicate("$.status", EdgeOperator.Eq, JsonDocument.Parse("\"sucesso\"").RootElement)
            }]);

        var errors = new List<string>();
        EdgeValidator.Validate(def, AgentIds(def), errors);

        errors.Should().BeEmpty();
    }

    [Fact]
    public void ConditionalEdge_SemPredicate_AdicionaErro()
    {
        var def = MakeGraph(
            agents: [new WorkflowAgentReference { AgentId = "a" }, new WorkflowAgentReference { AgentId = "b" }],
            edges: [new WorkflowEdge { EdgeType = WorkflowEdgeType.Conditional, From = "a", To = "b" }]);

        var errors = new List<string>();
        EdgeValidator.Validate(def, AgentIds(def), errors);

        errors.Should().ContainSingle(e => e.Contains("predicate"));
    }

    // ── FanOut ─────────────────────────────────────────────────────────────────

    [Fact]
    public void FanOutEdge_TargetsValidos_SemErros()
    {
        var def = MakeGraph(
            agents: [
                new WorkflowAgentReference { AgentId = "src" },
                new WorkflowAgentReference { AgentId = "t1" },
                new WorkflowAgentReference { AgentId = "t2" }
            ],
            edges: [new WorkflowEdge
            {
                EdgeType = WorkflowEdgeType.FanOut,
                From = "src",
                Targets = ["t1", "t2"]
            }]);

        var errors = new List<string>();
        EdgeValidator.Validate(def, AgentIds(def), errors);

        errors.Should().BeEmpty();
    }

    [Fact]
    public void FanOutEdge_TargetsVazio_AdicionaErro()
    {
        var def = MakeGraph(
            agents: [new WorkflowAgentReference { AgentId = "src" }],
            edges: [new WorkflowEdge { EdgeType = WorkflowEdgeType.FanOut, From = "src" }]);

        var errors = new List<string>();
        EdgeValidator.Validate(def, AgentIds(def), errors);

        errors.Should().ContainSingle(e => e.Contains("FanOut") && e.Contains("targets"));
    }

    // ── FanIn ──────────────────────────────────────────────────────────────────

    [Fact]
    public void FanInEdge_SourcesValidos_SemErros()
    {
        var def = MakeGraph(
            agents: [
                new WorkflowAgentReference { AgentId = "dst" },
                new WorkflowAgentReference { AgentId = "s1" },
                new WorkflowAgentReference { AgentId = "s2" }
            ],
            edges: [new WorkflowEdge
            {
                EdgeType = WorkflowEdgeType.FanIn,
                To = "dst",
                Sources = ["s1", "s2"]
            }]);

        var errors = new List<string>();
        EdgeValidator.Validate(def, AgentIds(def), errors);

        errors.Should().BeEmpty();
    }

    [Fact]
    public void FanInEdge_SourcesVazio_AdicionaErro()
    {
        var def = MakeGraph(
            agents: [new WorkflowAgentReference { AgentId = "dst" }],
            edges: [new WorkflowEdge { EdgeType = WorkflowEdgeType.FanIn, To = "dst" }]);

        var errors = new List<string>();
        EdgeValidator.Validate(def, AgentIds(def), errors);

        errors.Should().ContainSingle(e => e.Contains("FanIn") && e.Contains("sources"));
    }

    // ── Switch ─────────────────────────────────────────────────────────────────

    [Fact]
    public void SwitchEdge_CasesValidos_SemErros()
    {
        var def = MakeGraph(
            agents: [
                new WorkflowAgentReference { AgentId = "src" },
                new WorkflowAgentReference { AgentId = "t1" }
            ],
            edges: [new WorkflowEdge
            {
                EdgeType = WorkflowEdgeType.Switch,
                From = "src",
                Cases = [new WorkflowSwitchCase
                {
                    Targets = ["t1"],
                    Predicate = new EdgePredicate("$.status", EdgeOperator.Eq, JsonDocument.Parse("\"ok\"").RootElement)
                }]
            }]);

        var errors = new List<string>();
        EdgeValidator.Validate(def, AgentIds(def), errors);

        errors.Should().BeEmpty();
    }

    [Fact]
    public void SwitchEdge_CasesVazio_AdicionaErro()
    {
        var def = MakeGraph(
            agents: [new WorkflowAgentReference { AgentId = "src" }],
            edges: [new WorkflowEdge { EdgeType = WorkflowEdgeType.Switch, From = "src" }]);

        var errors = new List<string>();
        EdgeValidator.Validate(def, AgentIds(def), errors);

        errors.Should().ContainSingle(e => e.Contains("Switch"));
    }

    // ── Mode guard ─────────────────────────────────────────────────────────────

    [Fact]
    public void EdgesEmModoSequential_ComEdges_AdicionaErro()
    {
        var def = new WorkflowDefinition
        {
            Id = "wf", Name = "Test",
            OrchestrationMode = OrchestrationMode.Sequential,
            Agents = [new WorkflowAgentReference { AgentId = "a" }],
            Edges = [new WorkflowEdge { EdgeType = WorkflowEdgeType.Direct, From = "a", To = "b" }]
        };

        var errors = new List<string>();
        EdgeValidator.Validate(def, def.Agents.Select(a => a.AgentId).ToHashSet(), errors);

        errors.Should().ContainSingle(e => e.Contains("Graph") && e.Contains("Handoff"));
    }

    [Fact]
    public void HandoffEdge_AgentesValidos_SemErros()
    {
        var def = new WorkflowDefinition
        {
            Id = "wf", Name = "Test",
            OrchestrationMode = OrchestrationMode.Handoff,
            Agents = [
                new WorkflowAgentReference { AgentId = "a" },
                new WorkflowAgentReference { AgentId = "b" }
            ],
            Edges = [new WorkflowEdge { From = "a", To = "b" }]
        };

        var errors = new List<string>();
        EdgeValidator.Validate(def, def.Agents.Select(a => a.AgentId).ToHashSet(), errors);

        errors.Should().BeEmpty();
    }

    [Fact]
    public void GraphEdge_NodeNoExecutor_Reconhecido()
    {
        var def = MakeGraph(
            agents: [],
            executors: [new WorkflowExecutorStep { Id = "exec-1", FunctionName = "fetch" }],
            edges: [new WorkflowEdge { EdgeType = WorkflowEdgeType.Direct, From = "exec-1", To = "exec-1" }]);

        var errors = new List<string>();
        EdgeValidator.Validate(def, AgentIds(def), errors);

        errors.Should().BeEmpty();
    }
}
