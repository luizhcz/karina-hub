using System.Text.Json;
using EfsAiHub.Core.Abstractions.Identity;
using EfsAiHub.Core.Agents.RouterIntents;
using Microsoft.Extensions.Logging;

namespace EfsAiHub.Tests.Unit.Application;

[Trait("Category", "Unit")]
public class AgentServiceRouterValidationTests
{
    private static AgentService BuildService(
        IAgentRouterIntentLinkRepository? linkRepo = null,
        IRouterIntentRepository? intentRepo = null)
    {
        var repo = Substitute.For<IAgentDefinitionRepository>();
        var promptRepo = Substitute.For<IAgentPromptRepository>();
        var accessor = Substitute.For<IProjectContextAccessor>();
        accessor.Current.Returns(new ProjectContext("default"));

        return new AgentService(
            repository: repo,
            promptRepo: promptRepo,
            projectAccessor: accessor,
            templateService: new AgentTemplateService(NullLogger<AgentTemplateService>.Instance),
            composer: AgentServiceTestBuilder.IdentityComposer(),
            decomposer: AgentServiceTestBuilder.IdentityDecomposer(),
            logger: Substitute.For<ILogger<AgentService>>(),
            intentLinkRepo: linkRepo,
            intentRepo: intentRepo);
    }

    private static AgentDefinition BuildRouter(
        IReadOnlyList<string>? intentIds = null,
        string deployment = "gpt-4o-mini")
    {
        return new AgentDefinition
        {
            Id = "router-x",
            Name = "Router Teste",
            Type = AgentType.Router,
            RouterIntentIds = intentIds,
            Model = new AgentModelConfig { DeploymentName = deployment, MaxTokens = 200 },
            Instructions = "x",
            ProjectId = "default",
            StructuredOutput = new AgentStructuredOutputDefinition
            {
                ResponseFormat = "json_schema",
                SchemaName = "router_intent",
                Schema = JsonDocument.Parse("""{"type":"object","properties":{"intent":{"type":"string"}}}"""),
            },
        };
    }

    [Fact]
    public async Task ValidateAsync_Router_ReturnsError_WhenIntentCountIsZero()
    {
        var service = BuildService();
        var agent = BuildRouter(intentIds: Array.Empty<string>());

        var (isValid, errors, _) = await service.ValidateAsync(agent);

        isValid.Should().BeFalse();
        errors.Should().Contain(e => e.Contains("pelo menos 2 intenções"));
    }

    [Fact]
    public async Task ValidateAsync_Router_ReturnsError_WhenIntentCountIsOne()
    {
        var service = BuildService();
        var agent = BuildRouter(intentIds: new[] { "i1" });

        var (isValid, errors, _) = await service.ValidateAsync(agent);

        isValid.Should().BeFalse();
        errors.Should().Contain(e => e.Contains("pelo menos 2 intenções"));
    }

    [Fact]
    public async Task ValidateAsync_Router_PassesValidation_WhenIntentCountIsTwo()
    {
        var service = BuildService();
        var agent = BuildRouter(intentIds: new[] { "i1", "i2" });

        var (isValid, errors, _) = await service.ValidateAsync(agent);

        isValid.Should().BeTrue();
        errors.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_Router_FallsBackToLinkRepo_WhenIntentIdsNull()
    {
        var linkRepo = Substitute.For<IAgentRouterIntentLinkRepository>();
        linkRepo.CountForAgentAsync("router-x", Arg.Any<CancellationToken>()).Returns(3);
        var service = BuildService(linkRepo: linkRepo);
        var agent = BuildRouter(intentIds: null);

        var (isValid, errors, _) = await service.ValidateAsync(agent);

        isValid.Should().BeTrue();
        errors.Should().BeEmpty();
        await linkRepo.Received(1).CountForAgentAsync("router-x", Arg.Any<CancellationToken>());
    }

    // ── Auto-link das system intents ────────────────────────────────────────
    //
    // ValidateRouterAsync chama EnsureSystemIntentLinkedAsync pra `out_of_scope`
    // E `needs_clarification`. Os testes confirmam que ambas entram em
    // RouterIntentIds quando ausentes — sem isso, o Router perde a saída
    // canônica de ambiguidade (PR plan: needs_clarification é par de
    // out_of_scope, par auto-linkado).

    private static RouterIntent BuildSystemIntent(string id, string name) => new()
    {
        Id = id,
        TenantId = "default",
        ProjectId = "default",
        Name = name,
        Description = "system",
        IsSystem = true,
    };

    [Fact]
    public async Task ValidateAsync_Router_AutoLinks_OutOfScope_E_NeedsClarification()
    {
        var intentRepo = Substitute.For<IRouterIntentRepository>();
        intentRepo.ListAsync(Arg.Any<CancellationToken>()).Returns(
            new List<RouterIntent>
            {
                BuildSystemIntent("sys-oos-default", SystemIntents.OutOfScopeName),
                BuildSystemIntent("sys-nc-default", SystemIntents.NeedsClarificationName),
            });

        var service = BuildService(intentRepo: intentRepo);
        var agent = BuildRouter(intentIds: new[] { "biz-1", "biz-2" });

        await service.ValidateAsync(agent);

        agent.RouterIntentIds.Should()
            .Contain("sys-oos-default")
            .And.Contain("sys-nc-default");
    }

    [Fact]
    public async Task ValidateAsync_Router_AutoLink_Idempotente_QuandoSystemIntentJaPresente()
    {
        var intentRepo = Substitute.For<IRouterIntentRepository>();
        intentRepo.ListAsync(Arg.Any<CancellationToken>()).Returns(
            new List<RouterIntent>
            {
                BuildSystemIntent("sys-oos-default", SystemIntents.OutOfScopeName),
                BuildSystemIntent("sys-nc-default", SystemIntents.NeedsClarificationName),
            });

        var service = BuildService(intentRepo: intentRepo);
        var agent = BuildRouter(intentIds: new[]
        {
            "biz-1",
            "sys-oos-default",
            "sys-nc-default",
        });

        await service.ValidateAsync(agent);

        // Não duplica — IDs canônicos aparecem exatamente 1x cada.
        agent.RouterIntentIds.Should()
            .ContainSingle(id => id == "sys-oos-default")
            .And.Subject.ToList().Should().ContainSingle(id => id == "sys-nc-default");
    }

    [Fact]
    public async Task ValidateAsync_Router_SemRepoDeIntents_NaoBloqueia()
    {
        // Pre-condition do legado de testes: a maioria das suites constrói
        // AgentService sem intentRepo. Auto-link vira no-op nesse caso —
        // validação continua passando puramente pelo count >= 2.
        var service = BuildService(intentRepo: null);
        var agent = BuildRouter(intentIds: new[] { "biz-1", "biz-2" });

        var (isValid, _, _) = await service.ValidateAsync(agent);

        isValid.Should().BeTrue();
    }
}
