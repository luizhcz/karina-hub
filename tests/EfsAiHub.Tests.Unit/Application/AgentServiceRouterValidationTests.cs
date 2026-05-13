using System.Text.Json;
using EfsAiHub.Core.Abstractions.Identity;
using Microsoft.Extensions.Logging;

namespace EfsAiHub.Tests.Unit.Application;

[Trait("Category", "Unit")]
public class AgentServiceRouterValidationTests
{
    private static AgentService BuildService(
        IAgentRouterIntentLinkRepository? linkRepo = null)
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
            logger: Substitute.For<ILogger<AgentService>>(),
            intentLinkRepo: linkRepo);
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
}
