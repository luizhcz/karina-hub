namespace EfsAiHub.Tests.Unit.Versioning;

[Trait("Category", "Unit")]
public class AgentToolGenericHttpHashTests
{
    private static AgentDefinition BuildAgentWithFunctionTool(string fingerprint = "fp-1") => new()
    {
        Id = "agent-x",
        Name = "X",
        Description = "x",
        Model = new AgentModelConfig { DeploymentName = "gpt-4o" },
        Provider = new AgentProviderConfig { Type = "AzureOpenAI" },
        Instructions = "do stuff",
        Tools = new List<AgentToolDefinition>
        {
            new()
            {
                Type = "function",
                Name = "search_asset",
                FingerprintHash = fingerprint,
            },
        },
        ProjectId = "p",
        TenantId = "t",
    };

    [Fact]
    public void FromDefinition_HashEstavel_QuandoGenericToolIdAusente()
    {
        var def = BuildAgentWithFunctionTool();
        var v1 = AgentVersion.FromDefinition(def, revision: 1, promptContent: "p", promptVersionId: null);
        var v2 = AgentVersion.FromDefinition(def, revision: 2, promptContent: "p", promptVersionId: null);

        v1.ContentHash.Should().Be(v2.ContentHash);
    }

    [Fact]
    public void FromDefinition_HashMuda_QuandoAdicionaGenericTool()
    {
        var defSemGeneric = BuildAgentWithFunctionTool();
        var defComGeneric = new AgentDefinition
        {
            Id = defSemGeneric.Id,
            Name = defSemGeneric.Name,
            Description = defSemGeneric.Description,
            Model = defSemGeneric.Model,
            Provider = defSemGeneric.Provider,
            Instructions = defSemGeneric.Instructions,
            Tools = new List<AgentToolDefinition>(defSemGeneric.Tools)
            {
                new() { Type = "generic_http", GenericToolId = "tool-a" },
            },
            ProjectId = defSemGeneric.ProjectId,
            TenantId = defSemGeneric.TenantId,
        };

        var v1 = AgentVersion.FromDefinition(defSemGeneric, revision: 1, promptContent: "p", promptVersionId: null);
        var v2 = AgentVersion.FromDefinition(defComGeneric, revision: 1, promptContent: "p", promptVersionId: null);

        v1.ContentHash.Should().NotBe(v2.ContentHash);
    }

    [Fact]
    public void FromDefinition_GenericToolId_ViajaNoSnapshot()
    {
        var def = new AgentDefinition
        {
            Id = "agent-x",
            Name = "X",
            Model = new AgentModelConfig { DeploymentName = "gpt-4o" },
            ProjectId = "p",
            TenantId = "t",
            Tools = new List<AgentToolDefinition>
            {
                new() { Type = "generic_http", GenericToolId = "tool-a", Name = "Search Users" },
            },
        };

        var v = AgentVersion.FromDefinition(def, revision: 1, promptContent: null, promptVersionId: null);

        v.Tools.Should().NotBeNull();
        var snapshot = v.Tools![0];
        snapshot.Type.Should().Be("generic_http");
        snapshot.GenericToolId.Should().Be("tool-a");

        var roundtrip = snapshot.ToDefinition();
        roundtrip.GenericToolId.Should().Be("tool-a");
        roundtrip.Type.Should().Be("generic_http");
    }
}
