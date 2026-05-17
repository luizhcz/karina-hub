using EfsAiHub.Core.Agents.RouterIntents;
using EfsAiHub.Platform.Runtime.Factories;
using System.Text.Json;

namespace EfsAiHub.Tests.Unit.Routers;

/// <summary>
/// Cobre P1 do redesign canônico do Router: a intent reservada
/// <see cref="SystemIntents.OutOfScopeName"/> sempre existe como saída
/// segura quando nenhuma intent de negócio combina. Sem isso, o LLM era
/// instruído a forçar uma intent de negócio com confidence baixa — sintoma
/// recorrente de classificação ruim em produção.
/// </summary>
[Trait("Category", "Unit")]
public class RouterOutOfScopeFallbackTests
{
    private static EfsAiHub.Core.Agents.AgentDefinition NewRouter(
        IReadOnlyList<string>? intentIds = null)
    {
        return new EfsAiHub.Core.Agents.AgentDefinition
        {
            Id = "router-test",
            Name = "Router Test",
            Type = EfsAiHub.Core.Agents.AgentType.Router,
            Model = new EfsAiHub.Core.Agents.AgentModelConfig { DeploymentName = "gpt-5.4-mini" },
            Instructions = "Você é um router de intenções.",
            RouterIntentIds = intentIds,
            ProjectId = "default",
            TenantId = "default",
        };
    }

    [Fact]
    public void SystemIntents_OutOfScopeName_EhConstanteCanonica()
    {
        // Smoke test: o nome canônico não pode mudar acidentalmente porque
        // está acoplado ao seed da migration 004 e ao Switch dos workflows.
        SystemIntents.OutOfScopeName.Should().Be("out_of_scope");
        SystemIntents.IsReserved("out_of_scope").Should().BeTrue();
        SystemIntents.IsReserved("compra_boleta").Should().BeFalse();
    }

    [Fact]
    public void ChatOptionsBuilder_AppendsOutOfScopeClause_InRouterIntentBlock()
    {
        // P1: o bloco "# Intenções disponíveis" precisa instruir o LLM a
        // usar out_of_scope quando nada bater. Sem essa cláusula, prompt
        // engineering volta a forçar confidence baixa.
        var def = NewRouter();
        var intents = new List<RouterIntent>
        {
            new() { Id = "i-1", TenantId = "default", ProjectId = "default", Name = "boleta", Description = "Operações" },
            new() { Id = "i-2", TenantId = "default", ProjectId = "default", Name = "out_of_scope", Description = "Fora", IsSystem = true },
        };

        var instructions = ChatOptionsBuilder.AppendRouterIntentBlockIfApplicable(
            def, def.Instructions, intents);

        instructions.Should().NotBeNull();
        instructions!.Should().Contain("# Intenções disponíveis");
        instructions.Should().Contain(SystemIntents.OutOfScopeName);
        instructions.Should().Contain("`confidence >= 0.7`");
    }

    [Fact]
    public void ChatOptionsBuilder_AppendsOperationalMemoryClause_InRouterIntentBlock()
    {
        // P2: o bloco precisa instruir o LLM a preencher operationalMemory.
        // Sem essa cláusula, o LLM omite o campo e o middleware não persiste.
        var def = NewRouter();
        var intents = new List<RouterIntent>
        {
            new() { Id = "i-1", TenantId = "default", ProjectId = "default", Name = "boleta", Description = "Operações" },
            new() { Id = "i-2", TenantId = "default", ProjectId = "default", Name = "out_of_scope", Description = "Fora", IsSystem = true },
        };

        var instructions = ChatOptionsBuilder.AppendRouterIntentBlockIfApplicable(
            def, def.Instructions, intents);

        instructions.Should().NotBeNull();
        instructions!.Should().Contain("# Memória operacional");
        instructions.Should().Contain("last_intent");
        instructions.Should().Contain("last_reason");
    }

    [Fact]
    public void RouterDefaults_OutputSchema_ContemCamposCanonicos()
    {
        // Garante que o schema canônico tem os 4 campos esperados pelo
        // middleware OperationalMemoryChatClient e pelos Switches dos
        // workflows ($.intent). Mudar o nome de "intent" ou
        // "operationalMemory" quebra runtime — esse teste pega cedo.
        var output = RouterDefaults.OutputSchema();
        output.Schema.Should().NotBeNull();
        var root = output.Schema!.RootElement;
        var props = root.GetProperty("properties");

        props.TryGetProperty("intent", out _).Should().BeTrue();
        props.TryGetProperty("confidence", out _).Should().BeTrue();
        props.TryGetProperty("reason", out _).Should().BeTrue();
        props.TryGetProperty("operationalMemory", out var mem).Should().BeTrue();
        mem.GetProperty("properties").TryGetProperty("last_intent", out _).Should().BeTrue();
        mem.GetProperty("properties").TryGetProperty("last_reason", out _).Should().BeTrue();

        var required = root.GetProperty("required").EnumerateArray()
            .Select(e => e.GetString())
            .ToList();
        required.Should().Contain(new[] { "intent", "confidence", "reason", "operationalMemory" });
    }

    [Fact]
    public void AgentTemplateService_ApplyRouter_AutoDefaultsStructuredOutputEOperationalMemory()
    {
        // P2: Router salvo sem schema/memory recebe o canônico automaticamente.
        // Sem isso, admin teria que configurar manualmente em cada save.
        var service = new EfsAiHub.Core.Agents.Services.AgentTemplateService(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<EfsAiHub.Core.Agents.Services.AgentTemplateService>.Instance);

        var def = NewRouter();
        // Caller não cadastrou nada → template preenche o canônico.
        def.StructuredOutput.Should().BeNull();
        def.OperationalMemory.Should().BeNull();

        var result = service.Apply(def);

        result.StructuredOutput.Should().NotBeNull();
        result.StructuredOutput!.SchemaName.Should().Be(RouterDefaults.SchemaName);
        result.OperationalMemory.Should().NotBeNull();
        result.OperationalMemory!.MaxBytes.Should().Be(RouterDefaults.OperationalMemoryMaxBytes);
    }

    [Fact]
    public void AgentTemplateService_ApplyRouter_PreservaSchemaCustomAdmin()
    {
        // Quando admin cadastrou schema canônico próprio (contém "intent" +
        // "operationalMemory"), o template NÃO sobrescreve. Idempotente:
        // re-aplicar não quebra customização.
        var service = new EfsAiHub.Core.Agents.Services.AgentTemplateService(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<EfsAiHub.Core.Agents.Services.AgentTemplateService>.Instance);

        var customSchema = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "intent": { "type": "string" },
            "confidence": { "type": "number" },
            "reason": { "type": "string" },
            "operationalMemory": { "type": "object" },
            "custom_field": { "type": "string" }
          },
          "required": ["intent", "operationalMemory"]
        }
        """);
        var def = NewRouter();
        var withCustom = new EfsAiHub.Core.Agents.AgentDefinition
        {
            Id = def.Id, Name = def.Name, Type = def.Type, Model = def.Model,
            Instructions = def.Instructions, ProjectId = def.ProjectId, TenantId = def.TenantId,
            StructuredOutput = new EfsAiHub.Core.Agents.AgentStructuredOutputDefinition
            {
                ResponseFormat = "json_schema",
                SchemaName = "MyCustom",
                Schema = customSchema,
            },
        };

        var result = service.Apply(withCustom);

        result.StructuredOutput!.SchemaName.Should().Be("MyCustom");
        result.StructuredOutput.Schema!.RootElement.GetProperty("properties")
            .TryGetProperty("custom_field", out _).Should().BeTrue();
    }
}
