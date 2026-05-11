using System.Text.Json;
using EfsAiHub.Core.Abstractions.Identity;
using Microsoft.Extensions.Logging;

namespace EfsAiHub.Tests.Unit.Application;

[Trait("Category", "Unit")]
public class AgentServiceConversationalValidationTests
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

    private const string CanonicalSchemaJson = """
        {
          "type": "object",
          "properties": {
            "ui_component": { "type": "string", "enum": ["text", "card"] },
            "message": { "type": "string" },
            "output": { "type": "object" }
          },
          "required": ["ui_component", "message", "output"],
          "additionalProperties": false
        }
        """;

    private static AgentDefinition BuildBaselineConversational(
        string deployment = "gpt-5.4-mini",
        float temperature = 0.7f,
        int maxTokens = 1800,
        bool securityGuardrails = true,
        bool agUiStateMiddleware = true,
        string? uiComponentsRaw = "[\"text\",\"card\"]",
        string schemaJson = CanonicalSchemaJson,
        bool structuredOutputAsJsonSchema = true)
    {
        var middlewares = new List<AgentMiddlewareConfig>();
        if (securityGuardrails)
            middlewares.Add(new AgentMiddlewareConfig { Type = "SecurityGuardrails", Enabled = true });
        if (agUiStateMiddleware)
            middlewares.Add(new AgentMiddlewareConfig { Type = "StructuredOutputState", Enabled = true });

        var so = new AgentStructuredOutputDefinition
        {
            ResponseFormat = structuredOutputAsJsonSchema ? "json_schema" : "text",
            SchemaName = "ConversationalTurn",
            Schema = string.IsNullOrEmpty(schemaJson) ? null : JsonDocument.Parse(schemaJson),
        };

        var metadata = new Dictionary<string, string>();
        if (!string.IsNullOrEmpty(uiComponentsRaw))
            metadata[AgentDefinition.ConversationalUiComponentsMetadataKey] = uiComponentsRaw;

        return new AgentDefinition
        {
            Id = "cv-test",
            Name = "Conversational Teste",
            Type = AgentType.Conversational,
            Model = new AgentModelConfig
            {
                DeploymentName = deployment,
                Temperature = temperature,
                MaxTokens = maxTokens,
            },
            Instructions = "Você é um assistente conversacional. ...",
            ProjectId = "default",
            Middlewares = middlewares,
            StructuredOutput = so,
            Metadata = metadata,
        };
    }

    [Fact]
    public async Task ValidateAsync_Conversational_ReturnsError_WhenStructuredOutputIsNull()
    {
        var service = BuildService();
        var agent = BuildBaselineConversational(schemaJson: string.Empty);

        var (isValid, errors, _) = await service.ValidateAsync(agent);

        isValid.Should().BeFalse();
        errors.Should().Contain(e => e.Contains("structuredOutput"));
    }

    [Fact]
    public async Task ValidateAsync_Conversational_ReturnsError_WhenSchemaMissingCanonicalTopLevel()
    {
        var service = BuildService();
        var brokenSchema = """{"type":"object","properties":{"foo":{"type":"string"}}}""";
        var agent = BuildBaselineConversational(schemaJson: brokenSchema);

        var (isValid, errors, _) = await service.ValidateAsync(agent);

        isValid.Should().BeFalse();
        errors.Should().Contain(e =>
            e.Contains("ui_component") && e.Contains("message") && e.Contains("output"));
    }

    [Fact]
    public async Task ValidateAsync_Conversational_ReturnsMaxTokensWarning_WhenAbove4000()
    {
        var service = BuildService();
        var agent = BuildBaselineConversational(maxTokens: 5000);

        var (isValid, _, warnings) = await service.ValidateAsync(agent);

        isValid.Should().BeTrue();
        warnings.Should().Contain(w => w.Contains("maxTokens") && w.Contains("5000"));
    }

    [Fact]
    public async Task ValidateAsync_Conversational_ReturnsMaxTokensWarning_WhenBelow800()
    {
        var service = BuildService();
        var agent = BuildBaselineConversational(maxTokens: 500);

        var (isValid, _, warnings) = await service.ValidateAsync(agent);

        isValid.Should().BeTrue();
        warnings.Should().Contain(w => w.Contains("maxTokens") && w.Contains("500"));
    }

    [Fact]
    public async Task ValidateAsync_Conversational_ReturnsTemperatureWarning_WhenOutOfRange()
    {
        var service = BuildService();
        var agent = BuildBaselineConversational(temperature: 1.5f);

        var (isValid, _, warnings) = await service.ValidateAsync(agent);

        isValid.Should().BeTrue();
        warnings.Should().Contain(w => w.Contains("temperature"));
    }

    [Fact]
    public async Task ValidateAsync_Conversational_ReturnsSecurityWarning_WhenGuardrailsOff()
    {
        var service = BuildService();
        var agent = BuildBaselineConversational(securityGuardrails: false);

        var (isValid, _, warnings) = await service.ValidateAsync(agent);

        isValid.Should().BeTrue();
        warnings.Should().Contain(w => w.Contains("SecurityGuardrails"));
    }

    [Fact]
    public async Task ValidateAsync_Conversational_ReturnsAgUiStateWarning_WhenOff()
    {
        var service = BuildService();
        var agent = BuildBaselineConversational(agUiStateMiddleware: false);

        var (isValid, _, warnings) = await service.ValidateAsync(agent);

        isValid.Should().BeTrue();
        warnings.Should().Contain(w => w.Contains("StructuredOutputState"));
    }

    [Fact]
    public async Task ValidateAsync_Conversational_ReturnsUiComponentsWarning_WhenListEmpty()
    {
        var service = BuildService();
        var agent = BuildBaselineConversational(uiComponentsRaw: "[]");

        var (isValid, _, warnings) = await service.ValidateAsync(agent);

        isValid.Should().BeTrue();
        warnings.Should().Contain(w => w.Contains("ui_component"));
    }

    [Fact]
    public async Task ValidateAsync_Conversational_ReturnsUiComponentsWarning_WhenJsonInvalid()
    {
        var service = BuildService();
        var agent = BuildBaselineConversational(uiComponentsRaw: "not-json");

        var (isValid, _, warnings) = await service.ValidateAsync(agent);

        isValid.Should().BeTrue();
        warnings.Should().Contain(w => w.Contains("x-conversational-ui-components"));
    }

    [Fact]
    public async Task ValidateAsync_Conversational_ReturnsUiComponentsWarning_WhenItemsNotStrings()
    {
        var service = BuildService();
        var agent = BuildBaselineConversational(uiComponentsRaw: "[1, null, \"card\"]");

        var (isValid, _, warnings) = await service.ValidateAsync(agent);

        isValid.Should().BeTrue();
        warnings.Should().Contain(w => w.Contains("items inválidos"));
    }

    [Fact]
    public async Task ValidateAsync_Conversational_ReturnsNoWarnings_WhenMatchesTemplate()
    {
        var service = BuildService();
        var agent = BuildBaselineConversational();

        var (isValid, _, warnings) = await service.ValidateAsync(agent);

        isValid.Should().BeTrue();
        warnings.Should().BeEmpty();
    }
}
