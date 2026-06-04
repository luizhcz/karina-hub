using EfsAiHub.Core.Agents;
using EfsAiHub.Core.Agents.RouterIntents;
using EfsAiHub.Core.Agents.Services;

namespace EfsAiHub.Tests.Unit.Routers;

/// <summary>
/// Cobre P1-D do plano de ambiguidade: <see cref="OutputSchemaRenderer"/>
/// injeta o enum dinâmico de intents NÃO SÓ no top-level <c>intent</c> mas
/// também no inner <c>candidate_intents[].intent</c>. Sem isso, o LLM podia
/// inventar nomes em candidate_intents — o Clarifier downstream teria que
/// defender contra valores fora do enum.
/// </summary>
[Trait("Category", "Unit")]
public class OutputSchemaRendererEnumTests
{
    private static IReadOnlyList<RouterIntent> Intents() => new[]
    {
        new RouterIntent
        {
            Id = "biz-1", TenantId = "default", ProjectId = "default",
            Name = "investir_renda_fixa", Description = "RF",
        },
        new RouterIntent
        {
            Id = "biz-2", TenantId = "default", ProjectId = "default",
            Name = "investir_renda_variavel", Description = "RV",
        },
        new RouterIntent
        {
            Id = "sys-oos", TenantId = "default", ProjectId = "default",
            Name = SystemIntents.OutOfScopeName, Description = "OOS", IsSystem = true,
        },
        new RouterIntent
        {
            Id = "sys-nc", TenantId = "default", ProjectId = "default",
            Name = SystemIntents.NeedsClarificationName, Description = "NC", IsSystem = true,
        },
    };

    private static AgentDefinition Router() => new()
    {
        Id = "router-x",
        Name = "Router",
        Type = AgentType.Router,
        Model = new AgentModelConfig { DeploymentName = "gpt-4o-mini" },
        Instructions = "x",
        ProjectId = "default",
        TenantId = "default",
        StructuredOutput = RouterDefaults.OutputSchema(),
        OperationalMemory = RouterDefaults.OperationalMemoryV1(),
        RouterIntentIds = new[] { "biz-1", "biz-2", "sys-oos", "sys-nc" },
    };

    [Fact]
    public void Render_Router_InjetaEnumNoTopLevelEntent()
    {
        var rendered = OutputSchemaRenderer.Render(Router(), Intents());

        rendered.Should().NotBeNull();
        var root = rendered!.Schema!.RootElement;
        var topIntent = root.GetProperty("properties").GetProperty("intent");
        var enumValues = topIntent.GetProperty("enum").EnumerateArray()
            .Select(e => e.GetString())
            .ToList();

        enumValues.Should().BeEquivalentTo(new[]
        {
            "investir_renda_fixa", "investir_renda_variavel",
            SystemIntents.OutOfScopeName, SystemIntents.NeedsClarificationName,
        });
    }

    [Fact]
    public void Render_Router_InjetaEnumTambemNoInnerCandidateIntentsIntent()
    {
        // P1-D: sem enum aqui, o LLM podia preencher candidate_intents com
        // nomes inventados. Clarifier downstream consome esse array como
        // verdade — se receber nome inválido, geraria pergunta sem sentido.
        var rendered = OutputSchemaRenderer.Render(Router(), Intents());

        var root = rendered!.Schema!.RootElement;
        var innerIntent = root
            .GetProperty("properties")
            .GetProperty("candidate_intents")
            .GetProperty("items")
            .GetProperty("properties")
            .GetProperty("intent");

        var enumValues = innerIntent.GetProperty("enum").EnumerateArray()
            .Select(e => e.GetString())
            .ToList();

        enumValues.Should().BeEquivalentTo(new[]
        {
            "investir_renda_fixa", "investir_renda_variavel",
            SystemIntents.OutOfScopeName, SystemIntents.NeedsClarificationName,
        });
    }

    [Fact]
    public void Render_Router_SemIntents_NaoInjetaEnum()
    {
        // Defensiva: sem pool resolvido, schema persiste sem enum. Backend
        // bloqueia save (count >= 2) — mas o renderer é robusto.
        var rendered = OutputSchemaRenderer.Render(Router(), routerIntents: null);
        var root = rendered!.Schema!.RootElement;

        root.GetProperty("properties").GetProperty("intent")
            .TryGetProperty("enum", out _).Should().BeFalse();
    }
}
