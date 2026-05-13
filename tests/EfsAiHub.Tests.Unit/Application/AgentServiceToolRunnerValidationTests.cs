using EfsAiHub.Core.Abstractions.Identity;
using Microsoft.Extensions.Logging;

namespace EfsAiHub.Tests.Unit.Application;

[Trait("Category", "Unit")]
public class AgentServiceToolRunnerValidationTests
{
    private static AgentService BuildService()
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
            logger: Substitute.For<ILogger<AgentService>>());
    }

    private static AgentToolDefinition BuildFunctionTool(
        string name = "tool-x",
        bool requiresApproval = false) =>
        new()
        {
            Type = "function",
            Name = name,
            RequiresApproval = requiresApproval,
        };

    private static AgentDefinition BuildBaselineToolRunner(
        string deployment = "gpt-5",
        float temperature = 0.1f,
        int maxTokens = 2500,
        bool accountGuard = true,
        bool securityGuardrails = true,
        bool hitlRequired = false,
        bool toolRequiresApproval = false,
        int toolCount = 1)
    {
        var middlewares = new List<AgentMiddlewareConfig>();
        if (accountGuard)
            middlewares.Add(new AgentMiddlewareConfig { Type = "AccountGuard", Enabled = true });
        if (securityGuardrails)
            middlewares.Add(new AgentMiddlewareConfig { Type = "SecurityGuardrails", Enabled = true });

        var tools = Enumerable.Range(0, toolCount)
            .Select(i => BuildFunctionTool($"tool-{i}", toolRequiresApproval && i == 0))
            .ToList();

        var metadata = new Dictionary<string, string>();
        if (hitlRequired)
            metadata[AgentDefinition.ToolRunnerHitlRequiredMetadataKey] = "true";

        return new AgentDefinition
        {
            Id = "tr-test",
            Name = "Tool Runner Teste",
            Type = AgentType.ToolRunner,
            Model = new AgentModelConfig
            {
                DeploymentName = deployment,
                Temperature = temperature,
                MaxTokens = maxTokens,
            },
            Instructions = "x",
            ProjectId = "default",
            Tools = tools,
            Middlewares = middlewares,
            Metadata = metadata,
        };
    }

    [Fact]
    public async Task ValidateAsync_ToolRunner_ReturnsToolsWarning_WhenToolCountIsZero()
    {
        var service = BuildService();
        var agent = BuildBaselineToolRunner(toolCount: 0);

        var (isValid, _, warnings) = await service.ValidateAsync(agent);

        isValid.Should().BeTrue();
        warnings.Should().Contain(w => w.Contains("sem tools"));
    }

    [Fact]
    public async Task ValidateAsync_ToolRunner_ReturnsModelWarning_WhenDeploymentIsMini()
    {
        var service = BuildService();
        var agent = BuildBaselineToolRunner(deployment: "gpt-4o-mini");

        var (isValid, _, warnings) = await service.ValidateAsync(agent);

        isValid.Should().BeTrue();
        warnings.Should().Contain(w => w.Contains("modelo full"));
    }

    [Fact]
    public async Task ValidateAsync_ToolRunner_ReturnsTemperatureWarning_WhenAbove05()
    {
        var service = BuildService();
        var agent = BuildBaselineToolRunner(temperature: 0.7f);

        var (isValid, _, warnings) = await service.ValidateAsync(agent);

        isValid.Should().BeTrue();
        warnings.Should().Contain(w => w.Contains("temperature"));
    }

    [Fact]
    public async Task ValidateAsync_ToolRunner_ReturnsMaxTokensWarning_WhenBelow1500()
    {
        var service = BuildService();
        var agent = BuildBaselineToolRunner(maxTokens: 800);

        var (isValid, _, warnings) = await service.ValidateAsync(agent);

        isValid.Should().BeTrue();
        warnings.Should().Contain(w => w.Contains("maxTokens"));
    }

    [Fact]
    public async Task ValidateAsync_ToolRunner_ReturnsAccountGuardWarning_WhenOff()
    {
        var service = BuildService();
        var agent = BuildBaselineToolRunner(accountGuard: false);

        var (isValid, _, warnings) = await service.ValidateAsync(agent);

        isValid.Should().BeTrue();
        warnings.Should().Contain(w => w.Contains("AccountGuard"));
    }

    [Fact]
    public async Task ValidateAsync_ToolRunner_ReturnsSecurityWarning_WhenGuardrailsOff()
    {
        var service = BuildService();
        var agent = BuildBaselineToolRunner(securityGuardrails: false);

        var (isValid, _, warnings) = await service.ValidateAsync(agent);

        isValid.Should().BeTrue();
        warnings.Should().Contain(w => w.Contains("SecurityGuardrails"));
    }

    [Fact]
    public async Task ValidateAsync_ToolRunner_ReturnsHitlInconsistencyWarning_WhenToolRequiresApprovalButFlagOff()
    {
        var service = BuildService();
        var agent = BuildBaselineToolRunner(
            toolRequiresApproval: true,
            hitlRequired: false);

        var (isValid, _, warnings) = await service.ValidateAsync(agent);

        isValid.Should().BeTrue();
        warnings.Should().Contain(w =>
            w.Contains("RequiresApproval=true") && w.Contains("HITL"));
    }

    [Fact]
    public async Task ValidateAsync_ToolRunner_DoesNotReturnHitlWarning_WhenFlagOnAndToolRequiresApproval()
    {
        var service = BuildService();
        var agent = BuildBaselineToolRunner(
            toolRequiresApproval: true,
            hitlRequired: true);

        var (isValid, _, warnings) = await service.ValidateAsync(agent);

        isValid.Should().BeTrue();
        warnings.Should().NotContain(w => w.Contains("HITL"));
    }

    [Fact]
    public async Task ValidateAsync_ToolRunner_ReturnsNoWarnings_WhenAgentMatchesTemplate()
    {
        var service = BuildService();
        var agent = BuildBaselineToolRunner();

        var (isValid, _, warnings) = await service.ValidateAsync(agent);

        isValid.Should().BeTrue();
        warnings.Should().BeEmpty();
    }
}
