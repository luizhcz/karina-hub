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

    [Fact]
    public async Task Switch_FailureMessage_MentionsNeedsClarificationOption()
    {
        // PR 4: mensagem de erro agora indica needs_clarification como case
        // adicional recomendado. Quando o PR do Clarifier introduzir a
        // migration, a mensagem orienta o autor a também adicionar o case
        // dedicado em vez de só corrigir o out_of_scope mínimo.
        var validator = new WorkflowValidator(BuildRepo());
        var wf = BuildWorkflow(new[] { BusinessCase("compra", "spec-a") });

        var (_, errors) = await validator.ValidateAsync(wf);

        errors.Should().Contain(e =>
            e.Contains("não tem caminho de fallback")
            && e.Contains(SystemIntents.NeedsClarificationName));
    }

    [Fact]
    public async Task Switch_WithNeedsClarificationCase_StillNeedsFallback()
    {
        // Defensiva: needs_clarification SOZINHO não cobre out_of_scope. Sem
        // default e sem case out_of_scope, a validação continua falhando
        // mesmo com case needs_clarification declarado — needs_clarification
        // é um SEGUNDO caminho, não substitui o fallback canônico.
        var validator = new WorkflowValidator(BuildRepo());
        var wf = BuildWorkflow(new[]
        {
            BusinessCase("compra", "spec-a"),
            BusinessCase(SystemIntents.NeedsClarificationName, "fallback-atendimento"),
        });

        var (isValid, errors) = await validator.ValidateAsync(wf);

        isValid.Should().BeFalse();
        errors.Should().Contain(e => e.Contains("não tem caminho de fallback"));
    }

    [Fact]
    public async Task Switch_WithNeedsClarificationAndOutOfScopeCases_PassesValidation()
    {
        // Layout esperado pós-PR do Clarifier: dois cases canônicos lado a
        // lado. out_of_scope → fallback-atendimento; needs_clarification →
        // (futuro Clarifier; aqui placeholder pra spec-a porque o teste não
        // simula o agente novo).
        var validator = new WorkflowValidator(BuildRepo());
        var wf = BuildWorkflow(new[]
        {
            BusinessCase("compra", "spec-a"),
            BusinessCase(SystemIntents.OutOfScopeName, "fallback-atendimento"),
            BusinessCase(SystemIntents.NeedsClarificationName, "spec-a"),
        });

        var (isValid, errors) = await validator.ValidateAsync(wf);

        isValid.Should().BeTrue();
        errors.Should().NotContain(e => e.Contains("fallback"));
    }

    // ── Helper internal HasNeedsClarificationCase ──────────────────────────
    // PR do Clarifier vai usar pra evitar dupla inserção do case na migration.
    // Testado isoladamente pra não acoplar à pipeline cheia de validação.

    [Fact]
    public void HasNeedsClarificationCase_QuandoCasePresente_RetornaTrue()
    {
        var edge = new WorkflowEdge
        {
            From = "router-x",
            EdgeType = WorkflowEdgeType.Switch,
            Cases = new List<WorkflowSwitchCase>
            {
                BusinessCase(SystemIntents.NeedsClarificationName, "clarifier"),
            },
        };

        WorkflowValidator.HasNeedsClarificationCase(edge).Should().BeTrue();
    }

    [Fact]
    public void HasNeedsClarificationCase_QuandoAusente_RetornaFalse()
    {
        var edge = new WorkflowEdge
        {
            From = "router-x",
            EdgeType = WorkflowEdgeType.Switch,
            Cases = new List<WorkflowSwitchCase>
            {
                BusinessCase("compra", "spec-a"),
                BusinessCase(SystemIntents.OutOfScopeName, "fallback-atendimento"),
                DefaultCase("fallback-atendimento"),
            },
        };

        WorkflowValidator.HasNeedsClarificationCase(edge).Should().BeFalse();
    }

    [Fact]
    public void HasNeedsClarificationCase_CaseInsensitive()
    {
        // SystemIntents.NeedsClarificationName é "needs_clarification"; case
        // do JSON em runtime pode variar (validação no save é OrdinalIgnoreCase).
        using var doc = JsonDocument.Parse("\"NEEDS_CLARIFICATION\"");
        var edge = new WorkflowEdge
        {
            From = "router-x",
            EdgeType = WorkflowEdgeType.Switch,
            Cases = new List<WorkflowSwitchCase>
            {
                new()
                {
                    Predicate = new EdgePredicate(
                        Path: "$.intent",
                        Operator: EdgeOperator.Eq,
                        Value: doc.RootElement.Clone(),
                        ValueType: EdgePredicateValueType.String),
                    Targets = new List<string> { "clarifier" },
                    IsDefault = false,
                },
            },
        };

        WorkflowValidator.HasNeedsClarificationCase(edge).Should().BeTrue();
    }
}
