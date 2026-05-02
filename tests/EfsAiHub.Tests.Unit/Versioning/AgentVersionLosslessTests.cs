using System.Text.Json;
using EfsAiHub.Core.Abstractions.Persistence;

namespace EfsAiHub.Tests.Unit.Versioning;

[Trait("Category", "Unit")]
public class AgentVersionLosslessTests
{
    private static AgentDefinition BuildRichDefinition(string id = "agent-rich") => new()
    {
        Id = id,
        Name = "Agente Rico",
        Description = "Descrição não-trivial preservada no snapshot.",
        ProjectId = "alpha",
        TenantId = "tenant-a",
        Visibility = "global",
        AllowedProjectIds = new[] { "alpha", "beta" },
        Model = new AgentModelConfig { DeploymentName = "gpt-4o", Temperature = 0.3f, MaxTokens = 1024 },
        Provider = new AgentProviderConfig { Type = "AzureOpenAI", ClientType = "ChatCompletion", Endpoint = "https://x.openai.azure.com" },
        FallbackProvider = new AgentProviderConfig { Type = "OpenAI", ClientType = "ChatCompletion", Endpoint = null },
        Instructions = "Você é um agente de testes lossless.",
        Tools =
        [
            new AgentToolDefinition
            {
                Type = "function",
                Name = "search_asset",
                FingerprintHash = "abc123",
                RequiresApproval = false,
            },
            new AgentToolDefinition
            {
                Type = "mcp",
                Name = "filesystem",
                McpServerId = "mcp-fs-1",
                ServerLabel = "fs",
                ServerUrl = "http://localhost:3000",
                AllowedTools = ["read_file", "list_dir"],
                RequireApproval = "never",
                Headers = new Dictionary<string, string> { ["X-Token"] = "secret-ref" },
            },
        ],
        Middlewares =
        [
            new AgentMiddlewareConfig
            {
                Type = "AccountGuard",
                Enabled = true,
                Settings = new Dictionary<string, string> { ["MaxBalance"] = "1000" },
            },
        ],
        Resilience = new ResiliencePolicy(MaxRetries: 3),
        CostBudget = new AgentCostBudget(5.0m),
        Metadata = new Dictionary<string, string> { ["env"] = "prod", ["team"] = "platform" },
    };

    [Fact]
    public void FromDefinition_CapturaDescription()
    {
        var def = BuildRichDefinition();
        var version = AgentVersion.FromDefinition(def, revision: 1, promptContent: null, promptVersionId: null);

        version.Description.Should().Be("Descrição não-trivial preservada no snapshot.");
    }

    [Fact]
    public void FromDefinition_CapturaMetadata()
    {
        var def = BuildRichDefinition();
        var version = AgentVersion.FromDefinition(def, revision: 1, promptContent: null, promptVersionId: null);

        version.Metadata.Should().NotBeNull();
        version.Metadata!.Should().HaveCount(2);
        version.Metadata["env"].Should().Be("prod");
        version.Metadata["team"].Should().Be("platform");
    }

    [Fact]
    public void FromDefinition_MetadataVazia_PersisteNull()
    {
        var def = new AgentDefinition
        {
            Id = "agent-empty-meta",
            Name = "X",
            Model = new AgentModelConfig { DeploymentName = "gpt-4o" },
            Metadata = new Dictionary<string, string>(),
        };

        var version = AgentVersion.FromDefinition(def, revision: 1, promptContent: null, promptVersionId: null);

        version.Metadata.Should().BeNull();
    }

    [Fact]
    public void FromDefinition_CapturaFallbackProvider()
    {
        var def = BuildRichDefinition();
        var version = AgentVersion.FromDefinition(def, revision: 1, promptContent: null, promptVersionId: null);

        version.FallbackProvider.Should().NotBeNull();
        version.FallbackProvider!.Type.Should().Be("OpenAI");
        version.FallbackProvider.ClientType.Should().Be("ChatCompletion");
        version.FallbackProvider.HasValue.Should().BeFalse(); // sem ApiKey no fallback
    }

    [Fact]
    public void FromDefinition_CapturaToolsCheias()
    {
        var def = BuildRichDefinition();
        var version = AgentVersion.FromDefinition(def, revision: 1, promptContent: null, promptVersionId: null);

        version.Tools.Should().NotBeNull();
        var tools = version.Tools!;
        tools.Should().HaveCount(2);

        var fn = tools[0];
        fn.Type.Should().Be("function");
        fn.Name.Should().Be("search_asset");
        fn.FingerprintHash.Should().Be("abc123");

        var mcp = tools[1];
        mcp.Type.Should().Be("mcp");
        mcp.McpServerId.Should().Be("mcp-fs-1");
        mcp.ServerLabel.Should().Be("fs");
        mcp.ServerUrl.Should().Be("http://localhost:3000");
        mcp.AllowedTools.Should().BeEquivalentTo(new[] { "read_file", "list_dir" });
        mcp.RequireApproval.Should().Be("never");
        mcp.Headers["X-Token"].Should().Be("secret-ref");
    }

    [Fact]
    public void ToDefinition_SemGovernanceSource_ProduzAgentDefinitionComDefaultsSeguros()
    {
        var def = BuildRichDefinition();
        var version = AgentVersion.FromDefinition(def, revision: 1, promptContent: def.Instructions, promptVersionId: null);

        var roundtripped = version.ToDefinition(governanceSource: null);

        // Behavior preservado.
        roundtripped.Id.Should().Be(def.Id);
        roundtripped.Description.Should().Be(def.Description);
        roundtripped.Model.DeploymentName.Should().Be(def.Model.DeploymentName);
        roundtripped.Model.Temperature.Should().BeApproximately(def.Model.Temperature!.Value, 0.001f);
        roundtripped.Model.MaxTokens.Should().Be(def.Model.MaxTokens);
        roundtripped.Provider.Type.Should().Be(def.Provider.Type);
        roundtripped.Provider.Endpoint.Should().Be(def.Provider.Endpoint);
        roundtripped.FallbackProvider!.Type.Should().Be(def.FallbackProvider!.Type);
        roundtripped.Instructions.Should().Be(def.Instructions);
        roundtripped.Tools.Should().HaveCount(def.Tools.Count);
        roundtripped.Middlewares.Should().HaveCount(def.Middlewares.Count);
        roundtripped.Resilience.Should().NotBeNull();
        roundtripped.CostBudget.Should().NotBeNull();
        roundtripped.Metadata.Should().HaveCount(def.Metadata.Count);

        // Governança cai pros defaults seguros (não vem do snapshot).
        roundtripped.ProjectId.Should().Be("default");
        roundtripped.Visibility.Should().Be("project");
        roundtripped.TenantId.Should().Be("default");
        roundtripped.AllowedProjectIds.Should().BeNull();
    }

    [Fact]
    public void ToDefinition_ComGovernanceSource_HidrataVisibilityProjectIdTenantId()
    {
        var def = BuildRichDefinition();
        var version = AgentVersion.FromDefinition(def, revision: 1, promptContent: def.Instructions, promptVersionId: null);

        // Governance vem da row corrente (mutável). Simula owner que mudou de global → project.
        var governance = new AgentDefinition
        {
            Id = def.Id,
            Name = def.Name,
            Model = def.Model,
            ProjectId = "alpha",
            TenantId = "tenant-a",
            Visibility = "project", // Foi global, agora é project — workflow pinado vê o estado atual.
            AllowedProjectIds = null,
        };

        var roundtripped = version.ToDefinition(governance);

        roundtripped.ProjectId.Should().Be("alpha");
        roundtripped.Visibility.Should().Be("project");
        roundtripped.TenantId.Should().Be("tenant-a");
        roundtripped.AllowedProjectIds.Should().BeNull();
    }

    [Fact]
    public void ToDefinition_RoundtripPreservaToolsCheias()
    {
        var def = BuildRichDefinition();
        var version = AgentVersion.FromDefinition(def, revision: 1, promptContent: def.Instructions, promptVersionId: null);

        var roundtripped = version.ToDefinition(governanceSource: null);

        var mcp = roundtripped.Tools.First(t => t.Type == "mcp");
        mcp.McpServerId.Should().Be("mcp-fs-1");
        mcp.ServerUrl.Should().Be("http://localhost:3000");
        mcp.AllowedTools.Should().BeEquivalentTo(new[] { "read_file", "list_dir" });
        mcp.Headers["X-Token"].Should().Be("secret-ref");
        mcp.RequireApproval.Should().Be("never");
    }

    [Fact]
    public void ToDefinition_ToolsNull_ProduzListaVazia()
    {
        // Defesa contra snapshot JSON corrompido onde Tools veio null —
        // ToDefinition deve retornar lista vazia em vez de NRE.
        var v = new AgentVersion(
            AgentVersionId: "v1-id",
            AgentDefinitionId: "agent-empty",
            Revision: 1,
            CreatedAt: DateTime.UtcNow,
            CreatedBy: null,
            ChangeReason: null,
            Status: AgentVersionStatus.Published,
            PromptContent: "instr",
            PromptVersionId: null,
            Model: new AgentModelSnapshot("gpt-4o", null, null),
            Provider: new AgentProviderSnapshot("AzureOpenAI", "ChatCompletion", null, false),
            MiddlewarePipeline: Array.Empty<AgentMiddlewareSnapshot>(),
            OutputSchema: null,
            Resilience: null,
            CostBudget: null,
            SkillRefs: Array.Empty<EfsAiHub.Core.Agents.Skills.SkillRef>(),
            ContentHash: "hash",
            Tools: null);

        var def = v.ToDefinition(governanceSource: null);

        def.Tools.Should().BeEmpty();
    }

    [Fact]
    public void ToDefinition_RoundtripPreservaOutputSchema()
    {
        var schemaJson = """{"type":"object","properties":{"answer":{"type":"string"}},"required":["answer"]}""";
        var def = new AgentDefinition
        {
            Id = "agent-output",
            Name = "Agente Output",
            Model = new AgentModelConfig { DeploymentName = "gpt-4o" },
            StructuredOutput = new AgentStructuredOutputDefinition
            {
                ResponseFormat = "json_schema",
                SchemaName = "AnswerSchema",
                SchemaDescription = "Schema da resposta canônica.",
                Schema = JsonDocument.Parse(schemaJson),
            },
        };

        var version = AgentVersion.FromDefinition(def, revision: 1, promptContent: null, promptVersionId: null);
        var roundtripped = version.ToDefinition(governanceSource: null);

        roundtripped.StructuredOutput.Should().NotBeNull();
        roundtripped.StructuredOutput!.ResponseFormat.Should().Be("json_schema");
        roundtripped.StructuredOutput.SchemaName.Should().Be("AnswerSchema");
        roundtripped.StructuredOutput.SchemaDescription.Should().Be("Schema da resposta canônica.");
        roundtripped.StructuredOutput.Schema.Should().NotBeNull();
        roundtripped.StructuredOutput.Schema!.RootElement.GetProperty("type").GetString().Should().Be("object");
        roundtripped.StructuredOutput.Schema.RootElement.GetProperty("required")[0].GetString().Should().Be("answer");
    }

    [Fact]
    public void FromDefinition_HeadersOrdemDiferente_HashIdentico()
    {
        // Defesa: Dictionary preserva ordem de inserção, mas snapshot canonical
        // ordena por chave — duas tools com headers em ordem diferente geram mesmo hash.
        var tool1 = new AgentToolDefinition
        {
            Type = "mcp",
            Name = "fs",
            McpServerId = "mcp-1",
            Headers = new Dictionary<string, string>
            {
                ["X-A"] = "1",
                ["X-B"] = "2",
                ["X-C"] = "3",
            },
        };
        var tool2 = new AgentToolDefinition
        {
            Type = "mcp",
            Name = "fs",
            McpServerId = "mcp-1",
            Headers = new Dictionary<string, string>
            {
                ["X-C"] = "3",
                ["X-A"] = "1",
                ["X-B"] = "2",
            },
        };

        var def1 = new AgentDefinition
        {
            Id = "h-1", Name = "H", Model = new AgentModelConfig { DeploymentName = "gpt-4o" },
            Tools = [tool1],
        };
        var def2 = new AgentDefinition
        {
            Id = "h-1", Name = "H", Model = new AgentModelConfig { DeploymentName = "gpt-4o" },
            Tools = [tool2],
        };

        var v1 = AgentVersion.FromDefinition(def1, revision: 1, promptContent: null, promptVersionId: null);
        var v2 = AgentVersion.FromDefinition(def2, revision: 1, promptContent: null, promptVersionId: null);

        v1.ContentHash.Should().Be(v2.ContentHash);
    }

    [Fact]
    public void ToDefinition_RoundtripPreservaMiddlewareSettings()
    {
        var def = BuildRichDefinition();
        var version = AgentVersion.FromDefinition(def, revision: 1, promptContent: null, promptVersionId: null);

        var roundtripped = version.ToDefinition(governanceSource: null);

        roundtripped.Middlewares.Should().HaveCount(1);
        var mw = roundtripped.Middlewares[0];
        mw.Type.Should().Be("AccountGuard");
        mw.Enabled.Should().BeTrue();
        mw.Settings["MaxBalance"].Should().Be("1000");
    }

    [Fact]
    public void ToDefinition_RoundtripCanonicoIgualOriginal()
    {
        var def = BuildRichDefinition();
        var version = AgentVersion.FromDefinition(def, revision: 1, promptContent: def.Instructions, promptVersionId: null);

        var roundtripped = version.ToDefinition(governanceSource: def);

        // Compara serializações canônicas dos campos de behavior. Governança vem do source,
        // que é o próprio def aqui — então tudo (exceto timestamps + UpdatedAt automático) deve bater.
        var originalCanonical = JsonSerializer.Serialize(new
        {
            def.Id,
            def.Description,
            def.Instructions,
            def.Model.DeploymentName,
            def.Model.Temperature,
            def.Model.MaxTokens,
            ProviderType = def.Provider.Type,
            ProviderEndpoint = def.Provider.Endpoint,
            FallbackType = def.FallbackProvider?.Type,
            ToolsCount = def.Tools.Count,
            FirstToolName = def.Tools.FirstOrDefault()?.Name,
            MiddlewareCount = def.Middlewares.Count,
            FirstMiddlewareType = def.Middlewares.FirstOrDefault()?.Type,
            MetadataCount = def.Metadata.Count,
            def.ProjectId,
            def.Visibility,
            def.TenantId,
        }, JsonDefaults.Domain);

        var roundtrippedCanonical = JsonSerializer.Serialize(new
        {
            roundtripped.Id,
            roundtripped.Description,
            roundtripped.Instructions,
            roundtripped.Model.DeploymentName,
            roundtripped.Model.Temperature,
            roundtripped.Model.MaxTokens,
            ProviderType = roundtripped.Provider.Type,
            ProviderEndpoint = roundtripped.Provider.Endpoint,
            FallbackType = roundtripped.FallbackProvider?.Type,
            ToolsCount = roundtripped.Tools.Count,
            FirstToolName = roundtripped.Tools.FirstOrDefault()?.Name,
            MiddlewareCount = roundtripped.Middlewares.Count,
            FirstMiddlewareType = roundtripped.Middlewares.FirstOrDefault()?.Type,
            MetadataCount = roundtripped.Metadata.Count,
            roundtripped.ProjectId,
            roundtripped.Visibility,
            roundtripped.TenantId,
        }, JsonDefaults.Domain);

        roundtrippedCanonical.Should().Be(originalCanonical);
    }

    [Fact]
    public void FromDefinition_DefinicoesIguais_HashIdentico()
    {
        var def1 = BuildRichDefinition();
        var def2 = BuildRichDefinition();

        var v1 = AgentVersion.FromDefinition(def1, revision: 1, promptContent: "x", promptVersionId: null);
        var v2 = AgentVersion.FromDefinition(def2, revision: 7, promptContent: "x", promptVersionId: null);

        v1.ContentHash.Should().Be(v2.ContentHash);
    }

    [Fact]
    public void FromDefinition_DescricaoDiferente_HashDiferente()
    {
        var def1 = BuildRichDefinition();
        var def2 = BuildRichDefinition();
        // Mesmo conteúdo, exceto Description.
        var def2WithDifferentDescription = new AgentDefinition
        {
            Id = def2.Id,
            Name = def2.Name,
            Description = "outra descrição",
            Model = def2.Model,
            Provider = def2.Provider,
            FallbackProvider = def2.FallbackProvider,
            Instructions = def2.Instructions,
            Tools = def2.Tools,
            Middlewares = def2.Middlewares,
            Resilience = def2.Resilience,
            CostBudget = def2.CostBudget,
            Metadata = def2.Metadata,
        };

        var v1 = AgentVersion.FromDefinition(def1, revision: 1, promptContent: null, promptVersionId: null);
        var v2 = AgentVersion.FromDefinition(def2WithDifferentDescription, revision: 1, promptContent: null, promptVersionId: null);

        v1.ContentHash.Should().NotBe(v2.ContentHash);
    }
}
