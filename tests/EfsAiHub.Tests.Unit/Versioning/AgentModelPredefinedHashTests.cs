namespace EfsAiHub.Tests.Unit.Versioning;

[Trait("Category", "Unit")]
public class AgentModelPredefinedHashTests
{
    private static AgentDefinition AgentWithoutPreset() => new()
    {
        Id = "agent-x",
        Name = "X",
        Description = "desc",
        Model = new AgentModelConfig { DeploymentName = "gpt-4o" },
        Provider = new AgentProviderConfig { Type = "AzureOpenAI" },
        Instructions = "do stuff",
        ProjectId = "p",
        TenantId = "t",
    };

    private static AgentDefinition AgentWithPreset(string presetId) => new()
    {
        Id = "agent-x",
        Name = "X",
        Description = "desc",
        Model = new AgentModelConfig
        {
            DeploymentName = "",
            PredefinedModelId = presetId,
        },
        Provider = new AgentProviderConfig { Type = "AzureOpenAI" },
        Instructions = "do stuff",
        ProjectId = "p",
        TenantId = "t",
    };

    [Fact]
    public void FromDefinition_HashEstavel_QuandoModelSemPredefinedModelId()
    {
        var def = AgentWithoutPreset();
        var v1 = AgentVersion.FromDefinition(def, revision: 1, promptContent: "p", promptVersionId: null);
        var v2 = AgentVersion.FromDefinition(def, revision: 2, promptContent: "p", promptVersionId: null);

        v1.ContentHash.Should().Be(v2.ContentHash);
    }

    [Fact]
    public void FromDefinition_HashMuda_QuandoAdicionaPreset()
    {
        var v1 = AgentVersion.FromDefinition(AgentWithoutPreset(), revision: 1, promptContent: "p", promptVersionId: null);
        var v2 = AgentVersion.FromDefinition(AgentWithPreset("smart-default"), revision: 1, promptContent: "p", promptVersionId: null);

        v1.ContentHash.Should().NotBe(v2.ContentHash);
    }

    [Fact]
    public void FromDefinition_PredefinedModelId_ViajaNoSnapshotERoundtrip()
    {
        var def = AgentWithPreset("smart-default");

        var v = AgentVersion.FromDefinition(def, revision: 1, promptContent: null, promptVersionId: null);

        v.Model.PredefinedModelId.Should().Be("smart-default");

        var roundtrip = v.ToDefinition(governanceSource: null);
        roundtrip.Model.PredefinedModelId.Should().Be("smart-default");
    }
}
