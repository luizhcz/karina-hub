using System.Text.Json;
using EfsAiHub.Core.Agents.PredefinedModels;
using EfsAiHub.Core.Agents.RouterIntents;
using EfsAiHub.Core.Agents.Skills;

namespace EfsAiHub.Tests.Unit.Composition;

[Trait("Category", "Unit")]
public class AgentDefinitionComposerTests
{
    private readonly IGenericToolRepository _genericTools = Substitute.For<IGenericToolRepository>();
    private readonly IPredefinedModelRepository _predefinedModels = Substitute.For<IPredefinedModelRepository>();
    private readonly ISkillResolver _skillResolver = Substitute.For<ISkillResolver>();
    private readonly IAgentRouterIntentLinkRepository _routerIntentLinks = Substitute.For<IAgentRouterIntentLinkRepository>();

    private AgentDefinitionComposer NewComposer() =>
        new(_genericTools, _predefinedModels, _skillResolver, _routerIntentLinks);

    private AgentDefinitionDecomposer NewDecomposer() => new();

    [Fact]
    public async Task Compose_CustomSemDeps_PreservaInstructionsAutoral()
    {
        var input = BuildAgent(
            id: "agent-custom",
            type: AgentType.Custom,
            instructions: "Você é um assistente. Responda perguntas sobre produtos.");

        var composed = await NewComposer().ComposeAsync(input);

        composed.AuthorInstructions.Should().Be(input.AuthorInstructions);
        composed.Instructions.Should().Be(input.AuthorInstructions);
    }

    [Fact]
    public async Task Compose_Router_InjetaIntentsBlock_NoInstructions()
    {
        _routerIntentLinks
            .ListIntentsForAgentAsync("agent-router", Arg.Any<CancellationToken>())
            .Returns(new List<RouterIntent>
            {
                new()
                {
                    Id = "intent-cot",
                    TenantId = "t",
                    ProjectId = "p",
                    Name = "consultar_cotacao",
                    Description = "Cliente pergunta o preço atual de um ativo.",
                },
            });

        var input = BuildAgent(
            id: "agent-router",
            type: AgentType.Router,
            instructions: "Classifique a entrada.");

        var composed = await NewComposer().ComposeAsync(input);

        composed.Instructions.Should().NotContain("<!--", "system prompt do LLM não pode embutir markers HTML");
        composed.Instructions.Should().Contain("# Intenções disponíveis");
        composed.Instructions.Should().Contain("consultar_cotacao");
        composed.AuthorInstructions.Should().Be("Classifique a entrada.");
        composed.RouterIntentIds.Should().BeEquivalentTo(new[] { "intent-cot" });
    }

    [Fact]
    public async Task Compose_GenericHttp_ExpandeFieldsInline()
    {
        _genericTools
            .GetByIdAsync("tool-quote", "p", Arg.Any<CancellationToken>())
            .Returns(new GenericTool
            {
                Id = "tool-quote",
                ProjectId = "p",
                TenantId = "t",
                Name = "Get Quote",
                HttpMethod = HttpMethodType.GET,
                UrlTemplate = "https://api.example.com/{ticker}",
                PathParams = new Dictionary<string, ParamDefinition>
                {
                    ["ticker"] = new ParamDefinition("string", "Ticker", true),
                },
                OutputContentType = OutputContentType.Json,
                OutputSchema = "{\"type\":\"object\"}",
            });

        var input = BuildAgent(
            id: "agent-with-tool",
            type: AgentType.Custom,
            instructions: "Use a ferramenta.",
            tools: new List<AgentToolDefinition>
            {
                new() { Type = "generic_http", GenericToolId = "tool-quote" },
            });

        var composed = await NewComposer().ComposeAsync(input);

        composed.Tools.Should().HaveCount(1);
        var tool = composed.Tools[0];
        tool.HttpMethod.Should().Be(HttpMethodType.GET);
        tool.UrlTemplate.Should().Be("https://api.example.com/{ticker}");
        tool.PathParams.Should().ContainKey("ticker");
        tool.OutputContentType.Should().Be(OutputContentType.Json);
        tool.OutputSchemaJson.Should().Be("{\"type\":\"object\"}");
    }

    [Fact]
    public async Task Compose_Skill_MergeAddendumNoInstructionsETools()
    {
        var skillTool = new AgentToolDefinition
        {
            Type = "function",
            Name = "calc_taxa",
        };
        _skillResolver
            .ResolveAsync(Arg.Any<SkillRef>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new Skill
            {
                Id = "skill-finance",
                Name = "Finance",
                InstructionsAddendum = "Conhecimento financeiro: taxas, fees.",
                Tools = new List<AgentToolDefinition> { skillTool },
                ProjectId = "p",
            });

        var input = BuildAgent(
            id: "agent-with-skill",
            type: AgentType.Custom,
            instructions: "Atendimento financeiro.",
            skillRefs: new[]
            {
                new SkillRef("skill-finance", "v1"),
            });

        var composed = await NewComposer().ComposeAsync(input);

        composed.Instructions.Should().NotContain("<!--", "addendum de skill é texto puro, sem markers");
        composed.Instructions.Should().Contain("Conhecimento financeiro");
        composed.AuthorInstructions.Should().Be("Atendimento financeiro.");
        composed.Tools.Should().Contain(t => t.Name == "calc_taxa" && t.SourceSkillId == "skill-finance");
    }

    [Fact]
    public async Task Compose_PredefinedModel_ExpandeDeployment()
    {
        _predefinedModels
            .GetByIdAsync("gpt-4o-default", Arg.Any<CancellationToken>())
            .Returns(new PredefinedModel
            {
                Id = "gpt-4o-default",
                DisplayName = "GPT-4o",
                Provider = "AzureOpenAI",
                DeploymentName = "gpt-4o-prod",
                Endpoint = "https://prod.openai.azure.com",
                DefaultTemperature = 0.5f,
                DefaultMaxTokens = 4000,
            });

        var input = BuildAgent(
            id: "agent-with-preset",
            type: AgentType.Custom,
            instructions: "Use o preset.",
            model: new AgentModelConfig { DeploymentName = string.Empty, PredefinedModelId = "gpt-4o-default" });

        var composed = await NewComposer().ComposeAsync(input);

        composed.Model.DeploymentName.Should().Be("gpt-4o-prod");
        composed.Model.Temperature.Should().Be(0.5f);
        composed.Model.MaxTokens.Should().Be(4000);
        composed.Model.PredefinedModelId.Should().Be("gpt-4o-default");
    }

    [Fact]
    public async Task Compose_PredefinedModel_SobreescreveProviderDoInput()
    {
        // Regressão: sem propagação, qualquer agent criado via preset sem
        // Provider explícito caía no default "AzureFoundry" e era publicado
        // com o tipo errado — runtime caía em fallback de SP em vez de usar
        // a ApiKey configurada pro preset.
        _predefinedModels
            .GetByIdAsync("aoai-preset", Arg.Any<CancellationToken>())
            .Returns(new PredefinedModel
            {
                Id = "aoai-preset",
                DisplayName = "Azure OpenAI Preset",
                Provider = "AzureOpenAI",
                ClientType = "ChatCompletion",
                DeploymentName = "gpt-4o",
                Endpoint = "https://my-aoai.openai.azure.com",
            });

        var input = BuildAgent(
            id: "agent-aoai-preset",
            type: AgentType.Custom,
            instructions: "use o preset",
            model: new AgentModelConfig
            {
                DeploymentName = string.Empty,
                PredefinedModelId = "aoai-preset",
            });
        // Provider chega com o default ("AzureFoundry") porque a UI não
        // serializa Provider quando o user só seleciona preset.
        input.Provider.Type.Should().Be("AzureFoundry");

        var composed = await NewComposer().ComposeAsync(input);

        composed.Provider.Type.Should().Be("AzureOpenAI");
        composed.Provider.ClientType.Should().Be("ChatCompletion");
        composed.Provider.Endpoint.Should().Be("https://my-aoai.openai.azure.com");
    }

    [Fact]
    public async Task Compose_PredefinedModel_PreservaApiKeyDoInput()
    {
        // ApiKey é independente do preset — pode ser per-agent (override do
        // user) ou global (config). Preset não tem ApiKey; composer não pode
        // limpar a key que veio do input.
        _predefinedModels
            .GetByIdAsync("preset-x", Arg.Any<CancellationToken>())
            .Returns(new PredefinedModel
            {
                Id = "preset-x",
                DisplayName = "X",
                Provider = "OpenAI",
                DeploymentName = "gpt-4o",
            });

        var input = new AgentDefinition
        {
            Id = "agent-with-key",
            Name = "agent-with-key",
            Type = AgentType.Custom,
            Model = new AgentModelConfig { DeploymentName = string.Empty, PredefinedModelId = "preset-x" },
            Provider = new AgentProviderConfig
            {
                Type = "AzureFoundry", // será sobrescrito pelo preset
                ApiKey = "secret://aws/efs-ai-hub/openai-default",
            },
            Tools = Array.Empty<AgentToolDefinition>(),
            SkillRefs = Array.Empty<SkillRef>(),
            ProjectId = "p",
            TenantId = "t",
        };

        var composed = await NewComposer().ComposeAsync(input);

        composed.Provider.Type.Should().Be("OpenAI");
        composed.Provider.ApiKey.Should().Be("secret://aws/efs-ai-hub/openai-default");
    }

    [Fact]
    public async Task Roundtrip_Custom_ComposeDecomposeRetornaAutoral()
    {
        var input = BuildAgent(
            id: "agent-custom-rt",
            type: AgentType.Custom,
            instructions: "Atenda o usuário com cordialidade.");

        var composed = await NewComposer().ComposeAsync(input);
        var decomposed = NewDecomposer().Decompose(composed);

        decomposed.AuthorInstructions.Should().Be(input.AuthorInstructions);
        decomposed.Instructions.Should().BeNull("decomposer não devolve o composto pro editor");
        decomposed.Tools.Should().BeEquivalentTo(input.Tools);
    }

    [Fact]
    public async Task Roundtrip_Router_ComIntents_DecomposeRetornaAutoral()
    {
        _routerIntentLinks
            .ListIntentsForAgentAsync("agent-router-rt", Arg.Any<CancellationToken>())
            .Returns(new List<RouterIntent>
            {
                new()
                {
                    Id = "i1",
                    TenantId = "t",
                    ProjectId = "p",
                    Name = "consultar",
                    Description = "Consulta",
                },
            });

        var input = BuildAgent(
            id: "agent-router-rt",
            type: AgentType.Router,
            instructions: "Classifique a entrada.",
            routerIntentIds: new[] { "i1" });

        var composed = await NewComposer().ComposeAsync(input);
        composed.Instructions.Should().NotContain("<!--");
        composed.Instructions.Should().Contain("# Intenções disponíveis");

        var decomposed = NewDecomposer().Decompose(composed);
        decomposed.AuthorInstructions.Should().Be(input.AuthorInstructions);
        decomposed.Instructions.Should().BeNull();
        decomposed.RouterIntentIds.Should().BeEquivalentTo(new[] { "i1" });
    }

    [Fact]
    public async Task Roundtrip_GenericHttp_DecomposeRemoveExpansao()
    {
        _genericTools
            .GetByIdAsync("tool-rt", "p", Arg.Any<CancellationToken>())
            .Returns(new GenericTool
            {
                Id = "tool-rt",
                ProjectId = "p",
                TenantId = "t",
                Name = "Search",
                HttpMethod = HttpMethodType.POST,
                UrlTemplate = "https://api.example.com/search",
                OutputContentType = OutputContentType.Json,
                OutputSchema = "{\"type\":\"object\"}",
                InputContentType = InputContentType.Json,
                InputSchema = "{\"type\":\"object\",\"properties\":{\"q\":{\"type\":\"string\"}}}",
            });

        var input = BuildAgent(
            id: "agent-tool-rt",
            type: AgentType.Custom,
            instructions: "Use a ferramenta.",
            tools: new List<AgentToolDefinition>
            {
                new() { Type = "generic_http", GenericToolId = "tool-rt" },
            });

        var composed = await NewComposer().ComposeAsync(input);
        composed.Tools[0].UrlTemplate.Should().NotBeNull();

        var decomposed = NewDecomposer().Decompose(composed);
        decomposed.AuthorInstructions.Should().Be(input.AuthorInstructions);
        decomposed.Tools[0].UrlTemplate.Should().BeNull();
        decomposed.Tools[0].GenericToolId.Should().Be("tool-rt");
        decomposed.Tools[0].HttpMethod.Should().BeNull();
    }

    [Fact]
    public async Task Roundtrip_Skill_DecomposeRemoveToolsMescladas()
    {
        var skillTool = new AgentToolDefinition { Type = "function", Name = "calc_x" };
        _skillResolver
            .ResolveAsync(Arg.Any<SkillRef>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new Skill
            {
                Id = "skill-y",
                Name = "Y",
                InstructionsAddendum = "Y addendum.",
                Tools = new List<AgentToolDefinition> { skillTool },
                ProjectId = "p",
            });

        var input = BuildAgent(
            id: "agent-skill-rt",
            type: AgentType.Custom,
            instructions: "Faça Y.",
            skillRefs: new[] { new SkillRef("skill-y", "v1") });

        var composed = await NewComposer().ComposeAsync(input);
        composed.Tools.Should().Contain(t => t.SourceSkillId == "skill-y");

        var decomposed = NewDecomposer().Decompose(composed);
        decomposed.AuthorInstructions.Should().Be(input.AuthorInstructions);
        decomposed.Tools.Should().NotContain(t => t.SourceSkillId == "skill-y");
        decomposed.Tools.Should().BeEmpty();
    }

    private static AgentDefinition BuildAgent(
        string id,
        AgentType type,
        string? instructions = null,
        IReadOnlyList<AgentToolDefinition>? tools = null,
        IReadOnlyList<SkillRef>? skillRefs = null,
        AgentModelConfig? model = null,
        IReadOnlyList<string>? routerIntentIds = null)
    {
        return new AgentDefinition
        {
            Id = id,
            Name = id,
            Type = type,
            Model = model ?? new AgentModelConfig { DeploymentName = "gpt-4o" },
            AuthorInstructions = instructions,
            Tools = tools ?? Array.Empty<AgentToolDefinition>(),
            SkillRefs = skillRefs ?? Array.Empty<SkillRef>(),
            RouterIntentIds = routerIntentIds,
            ProjectId = "p",
            TenantId = "t",
        };
    }
}
