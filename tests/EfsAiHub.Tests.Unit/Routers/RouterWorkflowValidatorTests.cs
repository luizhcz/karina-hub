using System.Text.Json;
using EfsAiHub.Core.Agents.RouterIntents;

namespace EfsAiHub.Tests.Unit.Routers;

/// <summary>
/// Cobre P1 pelo lado da validação de workflow: Switch a partir de um Router
/// precisa de um caminho de fallback explícito — IsDefault=true OU case
/// <c>$.intent Eq "out_of_scope"</c>. Sem isso, mensagens fora do escopo
/// declarado podem chegar ao Switch sem case correspondente e travar a
/// execução.
/// </summary>
[Trait("Category", "Unit")]
public class RouterWorkflowValidatorTests
{
    private static EfsAiHub.Core.Agents.AgentDefinition NewRouter(string id) => new()
    {
        Id = id,
        Name = id,
        Type = EfsAiHub.Core.Agents.AgentType.Router,
        Model = new EfsAiHub.Core.Agents.AgentModelConfig { DeploymentName = "gpt-5.4-mini" },
        Instructions = "Router",
        StructuredOutput = RouterDefaults.OutputSchema(),
        ProjectId = "default",
        TenantId = "default",
        Enabled = true,
    };

    private static WorkflowDefinition BuildWorkflow(WorkflowSwitchCase[] cases)
    {
        return new WorkflowDefinition
        {
            Id = "wf-test",
            Name = "wf-test",
            Version = "1.0",
            OrchestrationMode = OrchestrationMode.Graph,
            ProjectId = "default",
            Visibility = "project",
            Agents = new List<WorkflowAgentReference>
            {
                new() { AgentId = "router-test", AgentVersionId = "v1" },
                new() { AgentId = "spec-a", AgentVersionId = "v1" },
                new() { AgentId = "fallback-atendimento", AgentVersionId = "v1" },
            },
            Executors = new List<WorkflowExecutorStep>(),
            Edges = new List<WorkflowEdge>
            {
                new()
                {
                    From = "router-test",
                    To = null,
                    EdgeType = WorkflowEdgeType.Switch,
                    Cases = cases.ToList(),
                },
            },
            RoutingRules = Array.Empty<RoutingRule>(),
            Configuration = new WorkflowConfiguration(),
        };
    }

    private static IAgentDefinitionRepository BuildRepo()
    {
        var router = NewRouter("router-test");
        var specA = new EfsAiHub.Core.Agents.AgentDefinition
        {
            Id = "spec-a", Name = "Spec", Type = EfsAiHub.Core.Agents.AgentType.Custom,
            Model = new EfsAiHub.Core.Agents.AgentModelConfig { DeploymentName = "gpt-4o" },
            Instructions = "spec", ProjectId = "default", TenantId = "default", Enabled = true,
        };
        var fallback = new EfsAiHub.Core.Agents.AgentDefinition
        {
            Id = "fallback-atendimento", Name = "Fallback", Type = EfsAiHub.Core.Agents.AgentType.Custom,
            Model = new EfsAiHub.Core.Agents.AgentModelConfig { DeploymentName = "gpt-4o" },
            Instructions = "fallback", ProjectId = "default", TenantId = "default", Enabled = true,
        };

        var repo = Substitute.For<IAgentDefinitionRepository>();
        repo.GetByIdAsync("router-test", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<EfsAiHub.Core.Agents.AgentDefinition?>(router));
        repo.GetByIdAsync("spec-a", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<EfsAiHub.Core.Agents.AgentDefinition?>(specA));
        repo.GetByIdAsync("fallback-atendimento", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<EfsAiHub.Core.Agents.AgentDefinition?>(fallback));
        repo.GetExistingIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlySet<string>>(
                new HashSet<string> { "router-test", "spec-a", "fallback-atendimento" }));
        return repo;
    }

    private static WorkflowSwitchCase BusinessCase(string value, string target)
    {
        using var doc = JsonDocument.Parse($"\"{value}\"");
        return new WorkflowSwitchCase
        {
            Predicate = new EdgePredicate(
                Path: "$.intent",
                Operator: EdgeOperator.Eq,
                Value: doc.RootElement.Clone(),
                ValueType: EdgePredicateValueType.String),
            Targets = new List<string> { target },
            IsDefault = false,
        };
    }

    private static WorkflowSwitchCase DefaultCase(string target) => new()
    {
        Predicate = null,
        Targets = new List<string> { target },
        IsDefault = true,
    };

    [Fact]
    public async Task Switch_WithoutDefaultAndWithoutOutOfScopeCase_FailsValidation()
    {
        // Caso ruim: Router roteia pra spec-a só. Mensagem fora do escopo
        // que classifica como out_of_scope chega no Switch e nenhum case
        // bate — sem default, sem out_of_scope. Validation tem que falhar.
        var validator = new WorkflowValidator(BuildRepo());
        var wf = BuildWorkflow(new[] { BusinessCase("compra", "spec-a") });

        var (isValid, errors) = await validator.ValidateAsync(wf);

        isValid.Should().BeFalse();
        errors.Should().Contain(e => e.Contains("não tem caminho de fallback"));
    }

    [Fact]
    public async Task Switch_WithDefaultCase_PassesValidation()
    {
        // Caso bom: tem default explícito → fallback garantido.
        var validator = new WorkflowValidator(BuildRepo());
        var wf = BuildWorkflow(new[]
        {
            BusinessCase("compra", "spec-a"),
            DefaultCase("fallback-atendimento"),
        });

        var (_, errors) = await validator.ValidateAsync(wf);

        errors.Should().NotContain(e => e.Contains("não tem caminho de fallback"));
    }

    [Fact]
    public async Task Switch_WithOutOfScopeCase_PassesValidation()
    {
        // Caso bom: case explícito pra out_of_scope → fallback garantido
        // mesmo sem IsDefault.
        var validator = new WorkflowValidator(BuildRepo());
        var wf = BuildWorkflow(new[]
        {
            BusinessCase("compra", "spec-a"),
            BusinessCase(SystemIntents.OutOfScopeName, "fallback-atendimento"),
        });

        var (_, errors) = await validator.ValidateAsync(wf);

        errors.Should().NotContain(e => e.Contains("não tem caminho de fallback"));
    }
}
