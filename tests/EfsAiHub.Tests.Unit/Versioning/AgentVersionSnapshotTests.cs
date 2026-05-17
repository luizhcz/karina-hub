namespace EfsAiHub.Tests.Unit.Versioning;

[Trait("Category", "Unit")]
public class AgentVersionSnapshotTests
{
    private static AgentDefinition BuildDefinition(
        string id = "agent-test",
        AgentOperationalMemoryDefinition? memory = null) => new()
    {
        Id = id,
        Name = "Agente Teste",
        Model = new AgentModelConfig { DeploymentName = "gpt-4o", Temperature = 0.1f },
        Instructions = "Você é um assistente de investimentos.",
        Tools =
        [
            new AgentToolDefinition { Type = "function", Name = "search_asset" }
        ],
        OperationalMemory = memory,
    };

    [Fact]
    public void FromDefinition_CapturaIdEName()
    {
        var def = BuildDefinition("agent-1");
        var version = AgentVersion.FromDefinition(def, revision: 1, promptContent: null, promptVersionId: null);

        version.AgentDefinitionId.Should().Be("agent-1");
        version.AgentVersionId.Should().NotBeNullOrEmpty();
        version.Revision.Should().Be(1);
    }

    [Fact]
    public void FromDefinition_CapturaModel()
    {
        var def = BuildDefinition();
        var version = AgentVersion.FromDefinition(def, revision: 1, promptContent: null, promptVersionId: null);

        version.Model.Should().NotBeNull();
        version.Model!.DeploymentName.Should().Be("gpt-4o");
        version.Model.Temperature.Should().BeApproximately(0.1f, 0.001f);
    }

    [Fact]
    public void FromDefinition_CapturaTools()
    {
        var def = BuildDefinition();
        var version = AgentVersion.FromDefinition(def, revision: 1, promptContent: null, promptVersionId: null);

        version.Tools.Should().NotBeNull();
        version.Tools!.Should().HaveCount(1);
        version.Tools![0].Name.Should().Be("search_asset");
    }

    [Fact]
    public void FromDefinition_ContentHashReproduzivel()
    {
        var def = BuildDefinition();
        var v1 = AgentVersion.FromDefinition(def, revision: 1, promptContent: null, promptVersionId: null);
        var v2 = AgentVersion.FromDefinition(def, revision: 1, promptContent: null, promptVersionId: null);

        v1.ContentHash.Should().Be(v2.ContentHash);
    }

    [Fact]
    public void FromDefinition_ConteudoDiferente_HashDiferente()
    {
        var def1 = BuildDefinition("agent-1");
        var def2 = new AgentDefinition
        {
            Id = def1.Id,
            Name = def1.Name,
            Model = new AgentModelConfig { DeploymentName = "gpt-4o", Temperature = 0.1f },
            Instructions = "Instruções completamente diferentes.",
        };

        var v1 = AgentVersion.FromDefinition(def1, revision: 1, promptContent: null, promptVersionId: null);
        var v2 = AgentVersion.FromDefinition(def2, revision: 1, promptContent: null, promptVersionId: null);

        v1.ContentHash.Should().NotBe(v2.ContentHash);
    }

    [Fact]
    public void FromDefinition_StatusEDraft()
    {
        var def = BuildDefinition();
        var version = AgentVersion.FromDefinition(def, revision: 1, promptContent: null, promptVersionId: null);

        version.Status.Should().Be(AgentVersionStatus.Published);
    }

    [Fact]
    public void FromDefinition_ContentHashNaoVazio()
    {
        var def = BuildDefinition();
        var version = AgentVersion.FromDefinition(def, revision: 1, promptContent: null, promptVersionId: null);

        version.ContentHash.Should().NotBeNullOrEmpty();
        version.ContentHash.Length.Should().BeGreaterThan(20); // SHA256 hex = 64 chars
    }

    [Fact]
    public void FromDefinition_RevisaoDiferenteMesmoConteudo_MesmoHash()
    {
        // ContentHash depende apenas do conteúdo, não da revisão
        var def = BuildDefinition();
        var v1 = AgentVersion.FromDefinition(def, revision: 1, promptContent: null, promptVersionId: null);
        var v2 = AgentVersion.FromDefinition(def, revision: 2, promptContent: null, promptVersionId: null);

        v1.ContentHash.Should().Be(v2.ContentHash);
    }

    private static AgentOperationalMemoryDefinition BuildMemory(string schemaJson, int? maxBytes = null) =>
        new()
        {
            Schema = System.Text.Json.JsonDocument.Parse(schemaJson),
            MaxBytes = maxBytes,
        };

    [Fact]
    public void FromDefinition_OperationalMemoryPresente_CapturadoNoSnapshot()
    {
        // Regressão: antes, AgentVersion.FromDefinition NÃO incluía OperationalMemory
        // no canonical do ContentHash nem no snapshot. Como AppendAsync dedup por
        // hash, mudanças em memory passavam batido — runtime continuava com a
        // versão antiga sem schema e o middleware OperationalMemory não ativava.
        var def = BuildDefinition(
            memory: BuildMemory("""{"type":"object","properties":{"ultimo_ticker":{"type":"string"}}}""", maxBytes: 4096));

        var version = AgentVersion.FromDefinition(def, revision: 1, promptContent: null, promptVersionId: null);

        version.OperationalMemory.Should().NotBeNull();
        version.OperationalMemory!.SchemaJson.Should().Contain("ultimo_ticker");
        version.OperationalMemory.MaxBytes.Should().Be(4096);
    }

    [Fact]
    public void FromDefinition_MesmoConteudoComEsemMemory_HashDiferente()
    {
        // Regressão direta: ativar memory tem que produzir hash distinto pra
        // que AppendAsync crie nova revision em vez de fazer dedup silencioso.
        var defSemMemory = BuildDefinition();
        var defComMemory = BuildDefinition(
            memory: BuildMemory("""{"type":"object","properties":{"x":{"type":"string"}}}"""));

        var vSem = AgentVersion.FromDefinition(defSemMemory, revision: 1, promptContent: null, promptVersionId: null);
        var vCom = AgentVersion.FromDefinition(defComMemory, revision: 1, promptContent: null, promptVersionId: null);

        vSem.ContentHash.Should().NotBe(vCom.ContentHash);
    }

    [Fact]
    public void RoundTrip_ToDefinition_ComMemory_PreservaSchema()
    {
        // FromDefinition → ToDefinition deve preservar OperationalMemory byte-a-byte.
        var def = BuildDefinition(
            memory: BuildMemory("""{"type":"object","properties":{"cliente_id":{"type":"string"},"ultimo_ticker":{"type":"string"}}}""", maxBytes: 2048));

        var snapshot = AgentVersion.FromDefinition(def, revision: 1, promptContent: null, promptVersionId: null);
        var hydrated = snapshot.ToDefinition();

        hydrated.OperationalMemory.Should().NotBeNull();
        hydrated.OperationalMemory!.MaxBytes.Should().Be(2048);
        hydrated.OperationalMemory.Schema!.RootElement.GetRawText()
            .Should().Contain("cliente_id")
            .And.Contain("ultimo_ticker");
    }

    [Fact]
    public void ToDefinition_SnapshotLegacySemMemory_HidrataNull()
    {
        // Snapshot persistido ANTES do fix não tem OperationalMemory no JSON.
        // Construindo direto com default null garantimos retrocompat — campo
        // ausente vira null em ToDefinition sem disparar exception.
        var snapshot = new AgentVersion(
            AgentVersionId: "v-legacy",
            AgentDefinitionId: "agent-legacy",
            Revision: 1,
            CreatedAt: System.DateTime.UtcNow,
            CreatedBy: null,
            ChangeReason: null,
            Status: AgentVersionStatus.Published,
            PromptContent: "instr",
            PromptVersionId: null,
            Model: new AgentModelSnapshot("gpt-4o", 0.1f, null, null),
            Provider: new AgentProviderSnapshot("AzureOpenAI", "ChatCompletion", null, false),
            MiddlewarePipeline: new List<AgentMiddlewareSnapshot>(),
            OutputSchema: null,
            Resilience: null,
            CostBudget: null,
            SkillRefs: new List<EfsAiHub.Core.Agents.Skills.SkillRef>(),
            ContentHash: "deadbeef");
        // OperationalMemory default null — não passamos.

        var hydrated = snapshot.ToDefinition();
        hydrated.OperationalMemory.Should().BeNull();
    }
}
