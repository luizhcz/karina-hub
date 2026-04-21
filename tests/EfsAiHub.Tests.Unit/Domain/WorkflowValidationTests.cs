using EfsAiHub.Host.Api.Services;

namespace EfsAiHub.Tests.Unit.Domain;

[Trait("Category", "Unit")]
public class WorkflowValidationTests
{
    private static WorkflowValidator BuildValidator()
    {
        var agentRepo = Substitute.For<IAgentDefinitionRepository>();
        // ValidateAgentReferencesAsync verifica existência — stub retorna todos os IDs como existentes
        agentRepo.GetExistingIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => (IReadOnlySet<string>)callInfo.Arg<IEnumerable<string>>().ToHashSet());

        return new WorkflowValidator(agentRepo);
    }

    private static WorkflowDefinition BuildValid(OrchestrationMode mode = OrchestrationMode.Sequential) => new()
    {
        Id = "wf-test",
        Name = "Workflow Válido",
        OrchestrationMode = mode,
        Agents = [new WorkflowAgentReference { AgentId = "agent-1" }],
    };

    [Fact]
    public async Task Sequential_SemAgentes_RetornaErro()
    {
        var validator = BuildValidator();
        var def = new WorkflowDefinition
        {
            Id = "wf-seq",
            Name = "Sem agentes",
            OrchestrationMode = OrchestrationMode.Sequential,
            Agents = [],
        };

        var (isValid, errors) = await validator.ValidateAsync(def);

        isValid.Should().BeFalse();
        errors.Should().Contain(e => e.Contains("agente"));
    }

    [Fact]
    public async Task Sequential_ComAgente_EValido()
    {
        var validator = BuildValidator();
        var def = BuildValid();

        var (isValid, _) = await validator.ValidateAsync(def);

        isValid.Should().BeTrue();
    }

    [Fact]
    public async Task Graph_SemAgentesNemExecutores_RetornaErro()
    {
        var validator = BuildValidator();
        var def = new WorkflowDefinition
        {
            Id = "wf-graph",
            Name = "Graph Vazio",
            OrchestrationMode = OrchestrationMode.Graph,
            Agents = [],
            Executors = [],
        };

        var (isValid, errors) = await validator.ValidateAsync(def);

        isValid.Should().BeFalse();
        errors.Should().Contain(e => e.Contains("Graph"));
    }

    [Fact]
    public async Task Handoff_ApenasUmAgente_RetornaErro()
    {
        var validator = BuildValidator();
        var def = BuildValid(OrchestrationMode.Handoff);

        var (isValid, errors) = await validator.ValidateAsync(def);

        isValid.Should().BeFalse();
        errors.Should().Contain(e => e.Contains("Handoff"));
    }

    [Fact]
    public async Task IdVazio_RetornaErro()
    {
        var validator = BuildValidator();
        var def = new WorkflowDefinition
        {
            Id = "",
            Name = "Nome OK",
            OrchestrationMode = OrchestrationMode.Sequential,
            Agents = [new WorkflowAgentReference { AgentId = "a-1" }],
        };

        var (isValid, errors) = await validator.ValidateAsync(def);

        isValid.Should().BeFalse();
        errors.Should().Contain(e => e.Contains("id"));
    }

    [Fact]
    public async Task ExecutorsForaModoGraph_RetornaErro()
    {
        var validator = BuildValidator();
        var def = new WorkflowDefinition
        {
            Id = "wf-seq",
            Name = "Sequential com executors",
            OrchestrationMode = OrchestrationMode.Sequential,
            Agents = [new WorkflowAgentReference { AgentId = "a-1" }],
            Executors = [new WorkflowExecutorStep { Id = "exec-1", FunctionName = "fn" }],
        };

        var (isValid, errors) = await validator.ValidateAsync(def);

        isValid.Should().BeFalse();
        errors.Should().Contain(e => e.Contains("Graph"));
    }
}
