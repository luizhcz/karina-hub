using System.Text.Json;

namespace EfsAiHub.Tests.Unit.Domain;

// Cobre o template aplicado a Conversational — wrap canônico do schema,
// idempotência do bloco no prompt, idempotência do middleware. Round-trip
// de drafts legados (wrapped) também valida que o desempacotador defensivo
// recupera o sub-schema antes de re-wrappar.
[Trait("Category", "Unit")]
public class AgentTemplateServiceTests
{
    private static readonly IAgentTemplateService Service = new AgentTemplateService(NullLogger<AgentTemplateService>.Instance);

    private static AgentDefinition NewConversational(
        string? instructions = null,
        AgentStructuredOutputDefinition? structuredOutput = null,
        IReadOnlyList<AgentMiddlewareConfig>? middlewares = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        return new AgentDefinition
        {
            Id = "conv-1",
            Name = "Conversational Test",
            Type = AgentType.Conversational,
            Model = new AgentModelConfig { DeploymentName = "gpt-4o" },
            Instructions = instructions,
            StructuredOutput = structuredOutput,
            Middlewares = middlewares ?? Array.Empty<AgentMiddlewareConfig>(),
            Metadata = metadata ?? new Dictionary<string, string>(),
            ProjectId = "default",
            TenantId = "default",
        };
    }

    private static JsonDocument JsonDoc(string raw) => JsonDocument.Parse(raw);

    private static string SerializeSchema(AgentDefinition def)
    {
        return def.StructuredOutput?.Schema?.RootElement.GetRawText() ?? string.Empty;
    }

    [Fact]
    public void Apply_Conversational_SubSchema_WrappaEmShapeCanonico()
    {
        var subSchema = JsonDoc("""
            {"type":"object","properties":{"ticker":{"type":"string"}},"required":["ticker"]}
        """);
        var def = NewConversational(structuredOutput: new AgentStructuredOutputDefinition
        {
            ResponseFormat = "json_schema",
            SchemaName = "ConversationalTurn",
            Schema = subSchema,
        });

        var result = Service.Apply(def);

        var root = result.StructuredOutput!.Schema!.RootElement;
        root.GetProperty("type").GetString().Should().Be("object");
        root.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
        var props = root.GetProperty("properties");
        props.TryGetProperty("output_type", out _).Should().BeTrue();
        props.TryGetProperty("output_status", out _).Should().BeTrue();
        props.TryGetProperty("message", out _).Should().BeTrue();
        props.TryGetProperty("historyText", out _).Should().BeTrue();
        props.TryGetProperty("output", out var output).Should().BeTrue();
        output.GetProperty("properties").GetProperty("ticker").GetProperty("type").GetString()
            .Should().Be("string");
        root.GetProperty("required").EnumerateArray().Select(e => e.GetString()).Should()
            .BeEquivalentTo(new[] { "output_type", "output_status", "message", "historyText", "output" });
    }

    [Fact]
    public void Apply_Conversational_TextoLivre_OmiteOutputERequired()
    {
        var def = NewConversational(structuredOutput: null);

        var result = Service.Apply(def);

        var root = result.StructuredOutput!.Schema!.RootElement;
        var props = root.GetProperty("properties");
        props.TryGetProperty("output_type", out _).Should().BeTrue();
        props.TryGetProperty("output_status", out _).Should().BeTrue();
        props.TryGetProperty("message", out _).Should().BeTrue();
        props.TryGetProperty("historyText", out _).Should().BeTrue();
        props.TryGetProperty("output", out _).Should().BeFalse();
        root.GetProperty("required").EnumerateArray().Select(e => e.GetString()).Should()
            .BeEquivalentTo(new[] { "output_type", "output_status", "message", "historyText" });
    }

    [Fact]
    public void Apply_Conversational_InjetaStructuredOutputStateMiddleware()
    {
        var def = NewConversational();

        var result = Service.Apply(def);

        result.Middlewares.Should().ContainSingle(m =>
            string.Equals(m.Type, "StructuredOutputState", StringComparison.OrdinalIgnoreCase)
            && m.Enabled);
    }

    [Fact]
    public void Apply_Conversational_MiddlewareDesabilitado_EhPromovidoPraEnabled()
    {
        // Conversational depende do middleware ativo pra emitir STATE_DELTA;
        // uma entry inert (enabled=false) deixava o agente sem renderização
        // real do schema canônico — promovemos pra true ao detectar.
        var def = NewConversational(middlewares: new[]
        {
            new AgentMiddlewareConfig { Type = "StructuredOutputState", Enabled = false },
        });

        var result = Service.Apply(def);

        result.Middlewares.Should().ContainSingle(m =>
            string.Equals(m.Type, "StructuredOutputState", StringComparison.OrdinalIgnoreCase));
        result.Middlewares
            .First(m => string.Equals(m.Type, "StructuredOutputState", StringComparison.OrdinalIgnoreCase))
            .Enabled.Should().BeTrue();
    }

    [Fact]
    public void Apply_Conversational_MiddlewareJaPresente_NaoDuplica()
    {
        var def = NewConversational(middlewares: new[]
        {
            new AgentMiddlewareConfig { Type = "StructuredOutputState", Enabled = true },
            new AgentMiddlewareConfig { Type = "SecurityGuardrails", Enabled = true },
        });

        var result = Service.Apply(def);

        result.Middlewares.Count(m =>
            string.Equals(m.Type, "StructuredOutputState", StringComparison.OrdinalIgnoreCase))
            .Should().Be(1);
        result.Middlewares.Should().Contain(m => m.Type == "SecurityGuardrails");
    }

    [Fact]
    public void Apply_Conversational_DescartaEntriesDuplicadasDoMiddleware()
    {
        // Payload com 2 entries do mesmo tipo (caso patológico: importação
        // mal-formada). Resultado deve ter exatamente 1 entry com Enabled=true.
        var def = NewConversational(middlewares: new[]
        {
            new AgentMiddlewareConfig { Type = "StructuredOutputState", Enabled = false },
            new AgentMiddlewareConfig { Type = "StructuredOutputState", Enabled = false },
        });

        var result = Service.Apply(def);

        var entries = result.Middlewares
            .Where(m => string.Equals(m.Type, "StructuredOutputState", StringComparison.OrdinalIgnoreCase))
            .ToList();
        entries.Should().HaveCount(1);
        entries[0].Enabled.Should().BeTrue();
    }

    [Fact]
    public void Apply_Conversational_AplicaEnumDeOutputStatusDoMetadata()
    {
        var def = NewConversational(metadata: new Dictionary<string, string>
        {
            [AgentDefinition.ConversationalOutputTypeMetadataKey] = "boleta",
            [AgentDefinition.ConversationalOutputStatusesMetadataKey] = "[\"nova\",\"confirmada\"]",
        });

        var result = Service.Apply(def);

        var properties = result.StructuredOutput!.Schema!.RootElement.GetProperty("properties");
        properties.GetProperty("output_type").GetProperty("enum").EnumerateArray()
            .Select(e => e.GetString()).ToArray()
            .Should().BeEquivalentTo(new[] { "boleta" });
        properties.GetProperty("output_status").GetProperty("enum").EnumerateArray()
            .Select(e => e.GetString()).ToArray()
            .Should().BeEquivalentTo(new[] { "nova", "confirmada" });
    }

    [Fact]
    public void Apply_Conversational_MetadataOutputStatusesInvalido_CaiNoDefault()
    {
        var def = NewConversational(metadata: new Dictionary<string, string>
        {
            [AgentDefinition.ConversationalOutputStatusesMetadataKey] = "nao-eh-json-array",
        });

        var result = Service.Apply(def);

        var properties = result.StructuredOutput!.Schema!.RootElement.GetProperty("properties");
        // Default "text" pra output_type quando metadata key ausente.
        properties.GetProperty("output_type").GetProperty("enum").EnumerateArray()
            .Select(e => e.GetString()).ToArray()
            .Should().BeEquivalentTo(new[] { "text" });
        // Default ["default"] pra output_status quando lista inválida.
        properties.GetProperty("output_status").GetProperty("enum").EnumerateArray()
            .Select(e => e.GetString()).ToArray()
            .Should().BeEquivalentTo(new[] { "default" });
    }

    [Fact]
    public void Apply_NonConversational_NaoAltera()
    {
        var custom = new AgentDefinition
        {
            Id = "custom-1",
            Name = "Custom",
            Type = AgentType.Custom,
            Model = new AgentModelConfig { DeploymentName = "gpt-4o" },
            Instructions = "instrucoes livres",
            StructuredOutput = null,
            Middlewares = Array.Empty<AgentMiddlewareConfig>(),
            ProjectId = "default",
            TenantId = "default",
        };

        var result = Service.Apply(custom);

        result.Instructions.Should().Be("instrucoes livres");
        result.StructuredOutput.Should().BeNull();
        result.Middlewares.Should().BeEmpty();
    }

    [Fact]
    public void Apply_Conversational_DuasChamadasSequenciais_SaoIdempotentes()
    {
        var def = NewConversational(
            instructions: "P",
            structuredOutput: new AgentStructuredOutputDefinition
            {
                ResponseFormat = "json_schema",
                SchemaName = "ConversationalTurn",
                Schema = JsonDoc("""{"type":"object","properties":{"x":{"type":"string"}}}"""),
            });

        var first = Service.Apply(def);
        var second = Service.Apply(first);

        SerializeSchema(first).Should().Be(SerializeSchema(second));
        first.Instructions.Should().Be(second.Instructions);
        first.Middlewares.Count.Should().Be(second.Middlewares.Count);
    }

    // ── Router: auto-inject de RouterDecisionTelemetry ─────────────────────

    private static AgentDefinition NewRouter(
        IReadOnlyList<AgentMiddlewareConfig>? middlewares = null) => new()
    {
        Id = "router-x",
        Name = "Router Teste",
        Type = AgentType.Router,
        Model = new AgentModelConfig { DeploymentName = "gpt-4o-mini" },
        Instructions = "x",
        ProjectId = "default",
        TenantId = "default",
        RouterIntentIds = new[] { "i1", "i2" },
        Middlewares = middlewares ?? Array.Empty<AgentMiddlewareConfig>(),
    };

    [Fact]
    public void Apply_Router_InjetaRouterDecisionTelemetryComoPrimeiroMiddleware()
    {
        // O middleware precisa ficar como PRIMEIRA entry do array porque o
        // pipeline wrap-from-inside-out faz com que a primeira entry vire o
        // middleware mais interno — telemetria + rewrite rodam ANTES do
        // StructuredOutputState (que emite STATE_DELTA) e do Blocklist.
        var def = NewRouter();

        var result = Service.Apply(def);

        result.Middlewares.Should().NotBeEmpty();
        result.Middlewares[0].Type.Should().Be("RouterDecisionTelemetry");
        result.Middlewares[0].Enabled.Should().BeTrue();
    }

    [Fact]
    public void Apply_Router_PreservaOutrosMiddlewares()
    {
        var def = NewRouter(new[]
        {
            new AgentMiddlewareConfig { Type = "StructuredOutputState", Enabled = true },
            new AgentMiddlewareConfig { Type = "SecurityGuardrails", Enabled = true },
        });

        var result = Service.Apply(def);

        result.Middlewares.Should().HaveCount(3);
        result.Middlewares[0].Type.Should().Be("RouterDecisionTelemetry");
        result.Middlewares.Should().Contain(m => m.Type == "StructuredOutputState");
        result.Middlewares.Should().Contain(m => m.Type == "SecurityGuardrails");
    }

    [Fact]
    public void Apply_Router_NaoDuplicaTelemetryQuandoJaPresente()
    {
        // Idempotência: re-aplicar template sobre payload já normalizado mantém
        // 1 entry. Caso comum: AgentService.Apply roda em create E update.
        var def = NewRouter(new[]
        {
            new AgentMiddlewareConfig { Type = "RouterDecisionTelemetry", Enabled = true },
        });

        var result = Service.Apply(def);

        result.Middlewares.Count(m =>
            string.Equals(m.Type, "RouterDecisionTelemetry", StringComparison.OrdinalIgnoreCase))
            .Should().Be(1);
    }

    [Fact]
    public void Apply_Router_PromoveTelemetryDesabilitadaParaEnabled()
    {
        // Entry inert quebra o pipeline silenciosamente — middleware é
        // promovido pra Enabled=true. Caller que precise desativar tem que
        // remover o entry inteiro (mas template re-injeta — telemetria não é
        // opt-out por design pra Router).
        var def = NewRouter(new[]
        {
            new AgentMiddlewareConfig { Type = "RouterDecisionTelemetry", Enabled = false },
        });

        var result = Service.Apply(def);

        result.Middlewares
            .First(m => string.Equals(m.Type, "RouterDecisionTelemetry", StringComparison.OrdinalIgnoreCase))
            .Enabled.Should().BeTrue();
    }

    [Fact]
    public void Apply_Router_DescartaEntriesDuplicadasDeTelemetry()
    {
        var def = NewRouter(new[]
        {
            new AgentMiddlewareConfig { Type = "RouterDecisionTelemetry", Enabled = false },
            new AgentMiddlewareConfig { Type = "RouterDecisionTelemetry", Enabled = true },
            new AgentMiddlewareConfig { Type = "SecurityGuardrails", Enabled = true },
        });

        var result = Service.Apply(def);

        result.Middlewares.Count(m =>
            string.Equals(m.Type, "RouterDecisionTelemetry", StringComparison.OrdinalIgnoreCase))
            .Should().Be(1);
        result.Middlewares.Should().Contain(m => m.Type == "SecurityGuardrails");
    }
}
