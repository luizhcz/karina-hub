using System.Text.Json;
using EfsAiHub.Core.Abstractions.Identity;
using Microsoft.Extensions.Logging;

namespace EfsAiHub.Tests.Unit.Application;

[Trait("Category", "Unit")]
public class AgentServiceWorkerValidationTests
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
            logger: Substitute.For<ILogger<AgentService>>());
    }

    private static AgentDefinition BuildBaselineWorker(
        string deployment = "gpt-5",
        int maxTokens = 2500,
        bool structuredOutput = true,
        bool securityGuardrails = true,
        string scope = "Análise de risco de crédito empresarial. Recebe perfil do solicitante e produto solicitado.")
    {
        var middlewares = securityGuardrails
            ? new[] { new AgentMiddlewareConfig { Type = "SecurityGuardrails", Enabled = true } }
            : Array.Empty<AgentMiddlewareConfig>();

        var schema = structuredOutput
            ? new AgentStructuredOutputDefinition
            {
                ResponseFormat = "json_schema",
                SchemaName = "WorkerOutput",
                Schema = JsonDocument.Parse("""{"type":"object","properties":{"r":{"type":"string"}}}"""),
            }
            : null;

        var metadata = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(scope))
            metadata[AgentDefinition.WorkerScopeMetadataKey] = scope;

        return new AgentDefinition
        {
            Id = "wk-test",
            Name = "Worker Teste",
            Type = AgentType.Worker,
            Model = new AgentModelConfig { DeploymentName = deployment, MaxTokens = maxTokens },
            Instructions = "x",
            ProjectId = "default",
            Middlewares = middlewares,
            StructuredOutput = schema,
            Metadata = metadata,
        };
    }

    [Fact]
    public async Task ValidateAsync_Worker_ReturnsScopeWarning_WhenScopeMissing()
    {
        var service = BuildService();
        var agent = BuildBaselineWorker(scope: "");

        var (isValid, _, warnings) = await service.ValidateAsync(agent);

        isValid.Should().BeTrue();
        warnings.Should().Contain(w => w.Contains("domínio"));
    }

    [Fact]
    public async Task ValidateAsync_Worker_ReturnsModelWarning_WhenDeploymentIsMini()
    {
        var service = BuildService();
        var agent = BuildBaselineWorker(deployment: "gpt-4o-mini");

        var (isValid, _, warnings) = await service.ValidateAsync(agent);

        isValid.Should().BeTrue();
        warnings.Should().Contain(w => w.Contains("modelo full"));
    }

    [Fact]
    public async Task ValidateAsync_Worker_ReturnsStructuredOutputWarning_WhenOff()
    {
        var service = BuildService();
        var agent = BuildBaselineWorker(structuredOutput: false);

        var (isValid, _, warnings) = await service.ValidateAsync(agent);

        isValid.Should().BeTrue();
        warnings.Should().Contain(w => w.Contains("StructuredOutput"));
    }

    [Fact]
    public async Task ValidateAsync_Worker_ReturnsMaxTokensWarning_WhenBelow1500()
    {
        var service = BuildService();
        var agent = BuildBaselineWorker(maxTokens: 800);

        var (isValid, _, warnings) = await service.ValidateAsync(agent);

        isValid.Should().BeTrue();
        warnings.Should().Contain(w => w.Contains("maxTokens"));
    }

    [Fact]
    public async Task ValidateAsync_Worker_ReturnsSecurityWarning_WhenGuardrailsOff()
    {
        var service = BuildService();
        var agent = BuildBaselineWorker(securityGuardrails: false);

        var (isValid, _, warnings) = await service.ValidateAsync(agent);

        isValid.Should().BeTrue();
        warnings.Should().Contain(w => w.Contains("SecurityGuardrails"));
    }

    [Fact]
    public async Task ValidateAsync_Worker_ReturnsNoWarnings_WhenMatchesTemplate()
    {
        var service = BuildService();
        var agent = BuildBaselineWorker();

        var (isValid, _, warnings) = await service.ValidateAsync(agent);

        isValid.Should().BeTrue();
        warnings.Should().BeEmpty();
    }
}
