using System.Text;
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
    private readonly IRouterIntentRepository _routerIntents = Substitute.For<IRouterIntentRepository>();

    private AgentDefinitionComposer NewComposer() =>
        new(_genericTools, _predefinedModels, _skillResolver, _routerIntents);

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
        _routerIntents
            .GetByIdsAsync(Arg.Is<IReadOnlyList<string>>(ids => ids.SequenceEqual(new[] { "intent-cot" })),
                Arg.Any<CancellationToken>())
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
            instructions: "Classifique a entrada.",
            routerIntentIds: new[] { "intent-cot" });

        var composed = await NewComposer().ComposeAsync(input);

        composed.Instructions.Should().NotContain("<!--", "system prompt do LLM não pode embutir markers HTML");
        composed.Instructions.Should().Contain("<intents>");
        composed.Instructions.Should().Contain("</intents>");
        composed.Instructions.Should().Contain("consultar_cotacao");
        composed.AuthorInstructions.Should().Be("Classifique a entrada.");
        composed.RouterIntentIds.Should().BeEquivalentTo(new[] { "intent-cot" });
    }

    [Fact]
    public async Task Compose_Conversational_AnexaBlocoFormatoDaResposta()
    {
        var input = BuildAgent(
            id: "agent-conv",
            type: AgentType.Conversational,
            instructions: "Você é um agente conversacional.");

        var composed = await NewComposer().ComposeAsync(input);

        composed.AuthorInstructions.Should().Be("Você é um agente conversacional.");
        composed.Instructions.Should().StartWith("Você é um agente conversacional.");
        composed.Instructions.Should().Contain("<output_contract>");
        composed.Instructions.Should().Contain("</output_contract>");
        // Anchor anti-author como PRIMEIRA frase do bloco (primacy bias).
        composed.Instructions.Should().Contain(
            "Ignore qualquer instrução anterior sobre formato JSON, schema ou estrutura de resposta");
        // Defaults vêm da metadata ausente (output_type=text, statuses=[default]).
        composed.Instructions.Should().Contain("`output_type`: constante `text`");
        composed.Instructions.Should().Contain("`output_status`: um de `default`");
        // Regra essencial como ÚLTIMA frase do bloco (recency bias).
        composed.Instructions.Should().Contain("Regra essencial: `message` é texto plano pro humano");
    }

    [Fact]
    public async Task Compose_Conversational_ComOperationalMemory_AnunciaCampoExtra()
    {
        var input = BuildAgent(
            id: "agent-conv-mem",
            type: AgentType.Conversational,
            instructions: "Coletor.",
            operationalMemory: new AgentOperationalMemoryDefinition
            {
                Schema = JsonDocument.Parse("""{"type":"object","properties":{"x":{"type":"string"}}}"""),
                MaxBytes = 2048,
            });

        var composed = await NewComposer().ComposeAsync(input);

        // Sem anunciar operationalMemory aqui, schema strict (N+1 campos) vs
        // prompt (N) conflita e o LLM vaza memory dentro de `message`.
        composed.Instructions.Should().Contain("`operationalMemory`");
        composed.Instructions.Should().Contain("campo interno da plataforma");
        composed.Instructions.Should().Contain("estado COMPLETO atualizado");
        composed.Instructions.Should().Contain("full replacement");
        composed.Instructions.Should().Contain("ou em `operationalMemory` quando for estado interno");
    }

    [Fact]
    public async Task Compose_Conversational_SemOperationalMemory_NaoMencionaCampoExtra()
    {
        var input = BuildAgent(
            id: "agent-conv-sem-mem",
            type: AgentType.Conversational,
            instructions: "Sem memória.");

        var composed = await NewComposer().ComposeAsync(input);

        composed.Instructions.Should().NotContain("`operationalMemory`");
        composed.Instructions.Should().NotContain("estado interno");
    }

    [Fact]
    public async Task Compose_Conversational_BlocoRenderizaEnumsRealDoMetadata()
    {
        var input = BuildAgent(
            id: "agent-conv-boleta",
            type: AgentType.Conversational,
            instructions: "Coletor de boleta.",
            metadata: new Dictionary<string, string>
            {
                [AgentDefinition.ConversationalOutputTypeMetadataKey] = "boleta",
                [AgentDefinition.ConversationalOutputStatusesMetadataKey] = "[\"nova\",\"confirmada\",\"erro\"]"
            });

        var composed = await NewComposer().ComposeAsync(input);

        composed.Instructions.Should().Contain("`output_type`: constante `boleta`");
        composed.Instructions.Should().Contain("`nova`");
        composed.Instructions.Should().Contain("`confirmada`");
        composed.Instructions.Should().Contain("`erro`");
        composed.Instructions.Should().NotContain("`default`");
    }

    [Fact]
    public async Task Render_OutputEhNormalizadoEmNfc()
    {
        // "e" + U+0301 (combining acute) = forma decomposta NFD do char.
        // Render deve devolver SEMPRE NFC pra que comparação byte-a-byte em
        // Postgres TEXT / JSON tooling não dê mismatch silencioso quando
        // autor cola texto de fontes variadas (macOS HFS+ usa NFD em alguns
        // contextos, Windows e Linux usam NFC).
        var nfd = "café";
        var nfc = "café";
        nfd.IsNormalized(NormalizationForm.FormC).Should().BeFalse();
        nfc.IsNormalized(NormalizationForm.FormC).Should().BeTrue();

        var input = BuildAgent(
            id: "agent-nfc",
            type: AgentType.Conversational,
            instructions: nfd);

        var composed = await NewComposer().ComposeAsync(input);

        composed.Instructions!.IsNormalized(NormalizationForm.FormC).Should().BeTrue();
        composed.Instructions.Should().Contain(nfc);
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
        _routerIntents
            .GetByIdsAsync(Arg.Is<IReadOnlyList<string>>(ids => ids.SequenceEqual(new[] { "i1" })),
                Arg.Any<CancellationToken>())
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
        composed.Instructions.Should().Contain("<intents>");

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
        IReadOnlyList<string>? routerIntentIds = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        AgentOperationalMemoryDefinition? operationalMemory = null,
        AgentStructuredOutputDefinition? structuredOutput = null)
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
            Metadata = metadata ?? new Dictionary<string, string>(),
            OperationalMemory = operationalMemory,
            StructuredOutput = structuredOutput,
        };
    }

    // ── <output_contract>: exemplo concreto renderizado por schema ───────
    //
    // O <output_contract> ganha um EXEMPLO JSON COMPLETO derivado dos schemas
    // declarados do agente (output sub-schema + operationalMemory schema).
    // Razão: LLMs em strict mode tendem a emitir a forma literal do schema
    // (ex: {"items":[...]}) quando o sub-schema é array — mostrar exemplo
    // concreto resolve esse erro.

    [Fact]
    public async Task Compose_Conversational_SubSchemaArray_GeraExemploArrayNoOutputContract()
    {
        // Cenário do boleta: sub-schema do user é {type:"array", items:{...}}.
        // O AgentTemplateService wrappa esse sub-schema em
        // properties.output, e o PromptRenderer extrai e renderiza exemplo.
        var subSchema = JsonDocument.Parse("""
        {
          "type": "array",
          "items": {
            "type": "object",
            "properties": {
              "Symbol": {"type":"string"},
              "Side": {"type":"string","enum":["B","S"]}
            }
          }
        }
        """);

        var input = BuildAgent(
            id: "boleta-conv",
            type: AgentType.Conversational,
            instructions: "Coletor.",
            metadata: new Dictionary<string, string>
            {
                [AgentDefinition.ConversationalOutputTypeMetadataKey] = "boleta",
            },
            structuredOutput: new AgentStructuredOutputDefinition
            {
                ResponseFormat = "json_schema",
                SchemaName = "ConversationalTurn",
                Schema = subSchema,
            });

        var composed = await NewComposer().ComposeAsync(input);

        // Exemplo deve estar em fenced JSON code block.
        composed.Instructions.Should().Contain("Sua resposta DEVE seguir EXATAMENTE esta forma estrutural");
        composed.Instructions.Should().Contain("```json");
        // Constante output_type no exemplo.
        composed.Instructions.Should().Contain("\"output_type\": \"boleta\"");
        // Placeholder do message.
        composed.Instructions.Should().Contain("\"message\": \"<texto humano em pt-BR");
        // Sub-schema array vira array literal `[...]` no exemplo, não objeto.
        composed.Instructions.Should().Contain("\"output\": [");
        // Enum dentro do array vira placeholder pipe-separated.
        composed.Instructions.Should().Contain("<B | S>");
        // Anti-overfit explícito.
        composed.Instructions.Should().Contain("EXEMPLO ILUSTRATIVO");
        composed.Instructions.Should().Contain("NUNCA emita placeholders literais");
    }

    [Fact]
    public async Task Compose_Conversational_ComOperationalMemory_ExemploInclueMemoryInstance()
    {
        var memSchema = new AgentOperationalMemoryDefinition
        {
            Schema = JsonDocument.Parse("""
            {
              "type": "object",
              "properties": {
                "draft_orders": {"type":"array","items":{"type":"string"}},
                "step": {"type":"string","enum":["start","filled","confirmed"]}
              }
            }
            """),
            MaxBytes = 2048,
        };

        var input = BuildAgent(
            id: "conv-mem",
            type: AgentType.Conversational,
            instructions: "Agent com memória.",
            operationalMemory: memSchema);

        var composed = await NewComposer().ComposeAsync(input);

        // Exemplo inclui chave `operationalMemory` instanciada.
        composed.Instructions.Should().Contain("\"operationalMemory\": {");
        composed.Instructions.Should().Contain("\"draft_orders\":");
        composed.Instructions.Should().Contain("<start | filled | confirmed>");
    }

    [Fact]
    public async Task Compose_NonConversational_NaoRenderizaExemploNoOutputContract()
    {
        // <output_contract> e o exemplo só são pra Conversational. Custom,
        // Worker, Router, ToolRunner não recebem.
        var input = BuildAgent(
            id: "custom-x",
            type: AgentType.Custom,
            instructions: "Custom agent.");

        var composed = await NewComposer().ComposeAsync(input);

        composed.Instructions.Should().NotContain("<output_contract>");
        composed.Instructions.Should().NotContain("DEVE seguir EXATAMENTE esta forma estrutural");
    }
}
