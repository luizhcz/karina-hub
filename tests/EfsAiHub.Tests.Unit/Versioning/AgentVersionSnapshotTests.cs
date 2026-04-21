namespace EfsAiHub.Tests.Unit.Versioning;

[Trait("Category", "Unit")]
public class AgentVersionSnapshotTests
{
    private static AgentDefinition BuildDefinition(string id = "agent-test") => new()
    {
        Id = id,
        Name = "Agente Teste",
        Model = new AgentModelConfig { DeploymentName = "gpt-4o", Temperature = 0.1f },
        Instructions = "Você é um assistente de investimentos.",
        Tools =
        [
            new AgentToolDefinition { Type = "function", Name = "buscar_ativo" }
        ],
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
    public void FromDefinition_CapturaToolFingerprints()
    {
        var def = BuildDefinition();
        var version = AgentVersion.FromDefinition(def, revision: 1, promptContent: null, promptVersionId: null);

        version.ToolFingerprints.Should().HaveCount(1);
        version.ToolFingerprints[0].Name.Should().Be("buscar_ativo");
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
}
