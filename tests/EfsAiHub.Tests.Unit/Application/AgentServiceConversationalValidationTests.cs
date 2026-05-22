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
            templateService: new AgentTemplateService(NullLogger<AgentTemplateService>.Instance),
            composer: AgentServiceTestBuilder.IdentityComposer(),
            decomposer: AgentServiceTestBuilder.IdentityDecomposer(),
            logger: Substitute.For<ILogger<AgentService>>());
    }

    private const string CanonicalSchemaJson = """
        {
          "type": "object",
          "properties": {
            "output_type": { "type": "string", "enum": ["text"] },
            "output_status": { "type": "string", "enum": ["default", "success"] },
            "message": { "type": "string" },
            "output": { "type": "object" }
          },
          "required": ["output_type", "output_status", "message"],
          "additionalProperties": false
        }
        """;

    private static AgentDefinition BuildBaselineConversational(
        string deployment = "gpt-5.4-mini",
        float temperature = 0.7f,
        int maxTokens = 1800,
        bool securityGuardrails = true,
        bool agUiStateMiddleware = true,
        string? outputStatusesRaw = "[\"default\",\"success\"]",
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
        if (!string.IsNullOrEmpty(outputStatusesRaw))
            metadata[AgentDefinition.ConversationalOutputStatusesMetadataKey] = outputStatusesRaw;

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

        // ValidateAsync é chamado direto sem o template ter passado por cima
        // — simula um caller bypass (admin override, importação) que persiste
        // schema cru. output_type/output_status/message são obrigatórios no
        // shape final; 'output' é opcional (modo texto livre quando ausente).
        var (isValid, errors, _) = await service.ValidateAsync(agent);

        isValid.Should().BeFalse();
        errors.Should().Contain(e =>
            e.Contains("output_type") && e.Contains("output_status") && e.Contains("message"));
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
    public async Task ValidateAsync_Conversational_ReturnsOutputStatusesWarning_WhenListEmpty()
    {
        var service = BuildService();
        var agent = BuildBaselineConversational(outputStatusesRaw: "[]");

        var (isValid, _, warnings) = await service.ValidateAsync(agent);

        isValid.Should().BeTrue();
        warnings.Should().Contain(w => w.Contains("output_status"));
    }

    [Fact]
    public async Task ValidateAsync_Conversational_ReturnsOutputStatusesWarning_WhenJsonInvalid()
    {
        var service = BuildService();
        var agent = BuildBaselineConversational(outputStatusesRaw: "not-json");

        var (isValid, _, warnings) = await service.ValidateAsync(agent);

        isValid.Should().BeTrue();
        warnings.Should().Contain(w => w.Contains("x-conversational-output-statuses"));
    }

    [Fact]
    public async Task ValidateAsync_Conversational_ReturnsOutputStatusesWarning_WhenItemsNotStrings()
    {
        var service = BuildService();
        var agent = BuildBaselineConversational(outputStatusesRaw: "[1, null, \"success\"]");

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
