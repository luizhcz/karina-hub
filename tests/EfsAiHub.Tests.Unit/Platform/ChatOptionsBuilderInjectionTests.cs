using EfsAiHub.Core.Agents.RouterIntents;
using EfsAiHub.Platform.Runtime.Factories;

namespace EfsAiHub.Tests.Unit.Platform;

[Trait("Category", "Unit")]
public class ChatOptionsBuilderInjectionTests
{
    private static AgentDefinition BuildAgent(
        AgentType type,
        string? instructions = "Você é o agente.",
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        return new AgentDefinition
        {
            Id = "agent-x",
            Name = "Agente",
            Type = type,
            Model = new AgentModelConfig { DeploymentName = "gpt-5" },
            Instructions = instructions,
            Metadata = metadata ?? new Dictionary<string, string>(),
            ProjectId = "default",
        };
    }

    private static IReadOnlyList<RouterIntent> BuildIntents(params string[] names)
    {
        return names.Select(n => new RouterIntent
        {
            Id = n,
            TenantId = "default",
            ProjectId = "default",
            Name = n,
            Description = $"Descrição de {n}",
            Examples = Array.Empty<string>(),
        }).ToList();
    }

    // ── AppendRouterIntentBlockIfApplicable ──────────────────────────────────

    [Fact]
    public void AppendRouterIntentBlock_NoOp_WhenTypeIsNotRouter()
    {
        var agent = BuildAgent(AgentType.Custom);
        var intents = BuildIntents("a", "b");

        var result = ChatOptionsBuilder.AppendRouterIntentBlockIfApplicable(
            agent, "base", intents);

        result.Should().Be("base");
    }

    [Fact]
    public void AppendRouterIntentBlock_NoOp_WhenIntentsIsEmpty()
    {
        var agent = BuildAgent(AgentType.Router);
        var result = ChatOptionsBuilder.AppendRouterIntentBlockIfApplicable(
            agent, "base", Array.Empty<RouterIntent>());

        result.Should().Be("base");
    }

    [Fact]
    public void AppendRouterIntentBlock_AppendsBlock_WhenInstructionsIsNull()
    {
        var agent = BuildAgent(AgentType.Router, instructions: null);
        var intents = BuildIntents("pix_transferir", "pix_consultar");

        var result = ChatOptionsBuilder.AppendRouterIntentBlockIfApplicable(
            agent, null, intents);

        result.Should().NotBeNull();
        result!.Should().StartWith("# Intenções disponíveis");
        result.Should().Contain("pix_transferir");
        result.Should().Contain("pix_consultar");
    }

    // ── AppendWorkerScopeBlockIfApplicable ───────────────────────────────────

    [Fact]
    public void AppendWorkerScopeBlock_NoOp_WhenTypeIsNotWorker()
    {
        var agent = BuildAgent(AgentType.Custom, metadata: new Dictionary<string, string>
        {
            [AgentDefinition.WorkerScopeMetadataKey] = "Domínio qualquer",
        });

        var result = ChatOptionsBuilder.AppendWorkerScopeBlockIfApplicable(
            agent, "Você é o agente.");

        result.Should().Be("Você é o agente.");
    }

    [Fact]
    public void AppendWorkerScopeBlock_NoOp_WhenScopeIsEmpty()
    {
        var agent = BuildAgent(AgentType.Worker, metadata: new Dictionary<string, string>
        {
            [AgentDefinition.WorkerScopeMetadataKey] = "   ",
        });

        var result = ChatOptionsBuilder.AppendWorkerScopeBlockIfApplicable(
            agent, "Você é o agente.");

        result.Should().Be("Você é o agente.");
    }

    [Fact]
    public void AppendWorkerScopeBlock_AppendsBlockAtEnd_WhenScopePresent()
    {
        var scope = "Análise de risco de crédito empresarial.";
        var agent = BuildAgent(AgentType.Worker, metadata: new Dictionary<string, string>
        {
            [AgentDefinition.WorkerScopeMetadataKey] = scope,
        });

        var result = ChatOptionsBuilder.AppendWorkerScopeBlockIfApplicable(
            agent, "Você é o agente.");

        result.Should().NotBeNull();
        result!.Should().StartWith("Você é o agente.");
        result.Should().Contain("# Domínio de análise");
        result.Should().Contain(scope);
        result.Should().EndWith("---");
    }

    [Fact]
    public void AppendWorkerScopeBlock_NoOp_WhenMetadataKeyAbsent()
    {
        var agent = BuildAgent(AgentType.Worker, metadata: new Dictionary<string, string>());

        var result = ChatOptionsBuilder.AppendWorkerScopeBlockIfApplicable(
            agent, "Você é o agente.");

        result.Should().Be("Você é o agente.");
    }
}
