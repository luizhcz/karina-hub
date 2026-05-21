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

    [Fact]
    public void FromDefinition_HashEstavel_QuandoGenericToolIdSemExpansao()
    {
        var def = BuildAgentWithFunctionTool();
        var defGeneric = new AgentDefinition
        {
            Id = def.Id,
            Name = def.Name,
            Description = def.Description,
            Model = def.Model,
            Provider = def.Provider,
            Instructions = def.Instructions,
            Tools = new List<AgentToolDefinition>(def.Tools)
            {
                new() { Type = "generic_http", GenericToolId = "tool-a" },
            },
            ProjectId = def.ProjectId,
            TenantId = def.TenantId,
        };

        var v1 = AgentVersion.FromDefinition(defGeneric, revision: 1, promptContent: "p", promptVersionId: null);
        var v2 = AgentVersion.FromDefinition(defGeneric, revision: 2, promptContent: "p", promptVersionId: null);

        v1.ContentHash.Should().Be(v2.ContentHash);
    }

    [Fact]
    public void FromDefinition_HashMuda_QuandoExpansaoHttpAdicionada()
    {
        var defSemExpansao = new AgentDefinition
        {
            Id = "agent-y",
            Name = "Y",
            Model = new AgentModelConfig { DeploymentName = "gpt-4o" },
            ProjectId = "p",
            TenantId = "t",
            Tools = new List<AgentToolDefinition>
            {
                new() { Type = "generic_http", GenericToolId = "tool-a", Name = "Search" },
            },
        };

        var defComExpansao = new AgentDefinition
        {
            Id = defSemExpansao.Id,
            Name = defSemExpansao.Name,
            Model = defSemExpansao.Model,
            ProjectId = defSemExpansao.ProjectId,
            TenantId = defSemExpansao.TenantId,
            Tools = new List<AgentToolDefinition>
            {
                new()
                {
                    Type = "generic_http",
                    GenericToolId = "tool-a",
                    Name = "Search",
                    HttpMethod = HttpMethodType.GET,
                    UrlTemplate = "https://api.example.com/search",
                    OutputContentType = OutputContentType.Json,
                    OutputSchemaJson = "{\"type\":\"object\"}",
                },
            },
        };

        var v1 = AgentVersion.FromDefinition(defSemExpansao, revision: 1, promptContent: null, promptVersionId: null);
        var v2 = AgentVersion.FromDefinition(defComExpansao, revision: 1, promptContent: null, promptVersionId: null);

        v1.ContentHash.Should().NotBe(v2.ContentHash);
    }

    [Fact]
    public void FromDefinition_ExpansaoHttp_ViajaNoSnapshot()
    {
        var def = new AgentDefinition
        {
            Id = "agent-y",
            Name = "Y",
            Model = new AgentModelConfig { DeploymentName = "gpt-4o" },
            ProjectId = "p",
            TenantId = "t",
            Tools = new List<AgentToolDefinition>
            {
                new()
                {
                    Type = "generic_http",
                    GenericToolId = "tool-a",
                    Name = "Lookup",
                    HttpMethod = HttpMethodType.POST,
                    UrlTemplate = "https://api.example.com/assets/{ticker}",
                    PathParams = new Dictionary<string, ParamDefinition>
                    {
                        ["ticker"] = new ParamDefinition("string", "Ticker do ativo", true),
                    },
                    QueryParams = new Dictionary<string, ParamDefinition>
                    {
                        ["scope"] = new ParamDefinition("string", "Escopo opcional", false),
                    },
                    CustomHeaders = new Dictionary<string, string>
                    {
                        ["X-Source"] = "aihub",
                    },
                    InputContentType = InputContentType.Json,
                    InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"hint\":{\"type\":\"string\"}}}",
                    OutputContentType = OutputContentType.Json,
                    OutputSchemaJson = "{\"type\":\"object\",\"properties\":{\"price\":{\"type\":\"number\"}}}",
                    OutputProjectionMode = OutputProjectionMode.Project,
                    TimeoutSecondsOverride = 30,
                    IsExclusive = true,
                },
            },
        };

        var v = AgentVersion.FromDefinition(def, revision: 1, promptContent: null, promptVersionId: null);

        var snapshot = v.Tools![0];
        snapshot.HttpMethod.Should().Be(HttpMethodType.POST);
        snapshot.UrlTemplate.Should().Be("https://api.example.com/assets/{ticker}");
        snapshot.PathParams.Should().ContainKey("ticker");
        snapshot.QueryParams.Should().ContainKey("scope");
        snapshot.CustomHeaders.Should().ContainKey("X-Source");
        snapshot.InputContentType.Should().Be(InputContentType.Json);
        snapshot.InputSchemaJson.Should().Contain("hint");
        snapshot.OutputContentType.Should().Be(OutputContentType.Json);
        snapshot.OutputProjectionMode.Should().Be(OutputProjectionMode.Project);
        snapshot.TimeoutSecondsOverride.Should().Be(30);
        snapshot.IsExclusive.Should().BeTrue();

        var roundtrip = snapshot.ToDefinition();
        roundtrip.HttpMethod.Should().Be(HttpMethodType.POST);
        roundtrip.UrlTemplate.Should().Be("https://api.example.com/assets/{ticker}");
        roundtrip.PathParams.Should().ContainKey("ticker");
        roundtrip.CustomHeaders.Should().ContainKey("X-Source");
        roundtrip.InputSchemaJson.Should().Contain("hint");
        roundtrip.OutputProjectionMode.Should().Be(OutputProjectionMode.Project);
        roundtrip.IsExclusive.Should().BeTrue();
    }
}
