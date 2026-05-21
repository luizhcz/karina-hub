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

        composed.Instructions.Should().Be(input.Instructions);
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

        composed.Instructions.Should().Contain(AgentInstructionsMarkers.IntentsBegin);
        composed.Instructions.Should().Contain(AgentInstructionsMarkers.IntentsEnd);
        composed.Instructions.Should().Contain("consultar_cotacao");
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
                Description = "Consulta cotação por ticker.",
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
        tool.Description.Should().Be("Consulta cotação por ticker.");
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

        composed.Instructions.Should().Contain(AgentInstructionsMarkers.SkillsBegin);
        composed.Instructions.Should().Contain("Conhecimento financeiro");
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
    public async Task Roundtrip_Custom_ComposeDecomposeRetornaAutoral()
    {
        var input = BuildAgent(
            id: "agent-custom-rt",
            type: AgentType.Custom,
            instructions: "Atenda o usuário com cordialidade.");

        var composed = await NewComposer().ComposeAsync(input);
        var decomposed = NewDecomposer().Decompose(composed);

        decomposed.Instructions.Should().Be(input.Instructions);
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
        composed.Instructions.Should().Contain(AgentInstructionsMarkers.IntentsBegin);

        var decomposed = NewDecomposer().Decompose(composed);
        decomposed.Instructions.Should().Be(input.Instructions);
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
        decomposed.Instructions.Should().Be(input.Instructions);
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
        decomposed.Instructions.Should().Be(input.Instructions);
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
            Instructions = instructions,
            Tools = tools ?? Array.Empty<AgentToolDefinition>(),
            SkillRefs = skillRefs ?? Array.Empty<SkillRef>(),
            RouterIntentIds = routerIntentIds,
            ProjectId = "p",
            TenantId = "t",
        };
    }
}
