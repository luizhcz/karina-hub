using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using EfsAiHub.Core.Agents.RouterIntents;
using EfsAiHub.Core.Agents.Skills;

namespace EfsAiHub.Core.Agents.Services;

/// <summary>
/// Composição determinística do <see cref="AgentDefinition.Instructions"/>
/// final a partir do <see cref="AgentDefinition.AuthorInstructions"/> mais
/// os blocos auto-gerados a partir de dependências resolvidas. Output é o
/// texto que vai pro LLM — XML tags delimitam cada bloco (atração de atenção
/// mais forte que markdown headers em system prompts). Mesma input → mesmo
/// output byte-a-byte.
///
/// Saída normalizada em NFC pra garantir consistência UTF-8 quando o texto
/// passa por camadas que assumem composições pré-compostas (Postgres TEXT
/// vs alguma JSON tooling). Chars exóticos (∈, ≤) são substituídos por
/// equivalentes ASCII pra robustez cross-provider.
/// </summary>
public static class PromptRenderer
{
    private const string BlockSeparator = "\n\n";

    public static string? Render(
        AgentType type,
        string? authorInstructions,
        IReadOnlyDictionary<string, string>? metadata,
        IReadOnlyList<RouterIntent>? routerIntents,
        IReadOnlyList<Skill>? skills,
        bool hasOperationalMemory = false,
        JsonDocument? structuredOutputSchema = null,
        JsonDocument? operationalMemorySchema = null)
    {
        var hasAuthor = !string.IsNullOrWhiteSpace(authorInstructions);
        var intentsBlock = RenderRouterIntentsBlock(type, routerIntents);
        var skillsBlock = RenderSkillsBlock(skills);
        var workerScopeBlock = RenderWorkerScopeBlock(type, metadata);
        var responseFormatBlock = RenderConversationalResponseFormatBlock(
            type, metadata, hasOperationalMemory,
            structuredOutputSchema, operationalMemorySchema);

        if (!hasAuthor
            && intentsBlock is null
            && skillsBlock is null
            && workerScopeBlock is null
            && responseFormatBlock is null)
        {
            return authorInstructions?.Normalize(NormalizationForm.FormC);
        }

        var sb = new StringBuilder();

        if (hasAuthor)
            sb.Append(authorInstructions!.TrimEnd());

        AppendBlock(sb, intentsBlock);
        AppendBlock(sb, skillsBlock);
        AppendBlock(sb, workerScopeBlock);
        AppendBlock(sb, responseFormatBlock);

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private static void AppendBlock(StringBuilder sb, string? block)
    {
        if (block is null) return;
        if (sb.Length > 0) sb.Append(BlockSeparator);
        sb.Append(block);
    }

    // Prompts dos templates vivem em Services/Prompts/*.md (embedded resources).
    // Junior edita o .md sem mexer em C# — placeholders são strings
    // delimitadas por {{...}} preenchidas em runtime pelos fragmentos
    // dinâmicos (catálogo de intents, exemplo JSON, etc). Nomes canônicos
    // imutáveis (needs_clarification, out_of_scope, output_contract) ficam
    // literais no .md, sem placeholder.
    private const string RouterTemplateResource =
        "EfsAiHub.Core.Agents.Services.Prompts.router.md";
    private const string ConversationalTemplateResource =
        "EfsAiHub.Core.Agents.Services.Prompts.conversational.md";

    private const string IntentsPlaceholder = "{{INTENCOES}}";
    private const string ExampleSectionPlaceholder = "{{EXAMPLE_SECTION}}";
    private const string OutputTypePlaceholder = "{{OUTPUT_TYPE}}";
    private const string OutputStatusListPlaceholder = "{{OUTPUT_STATUS_LIST}}";
    private const string OperationalMemoryFieldPlaceholder = "{{OPERATIONAL_MEMORY_FIELD}}";
    private const string MemoryParentheticalPlaceholder = "{{MEMORY_PARENTHETICAL}}";

    private static readonly Lazy<string> RouterTemplate =
        new(() => LoadEmbeddedTemplate(RouterTemplateResource));
    private static readonly Lazy<string> ConversationalTemplate =
        new(() => LoadEmbeddedTemplate(ConversationalTemplateResource));

    private static string LoadEmbeddedTemplate(string resourceName)
    {
        var asm = typeof(PromptRenderer).Assembly;
        using var stream = asm.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{resourceName}' não encontrado. " +
                "Verifique <EmbeddedResource> em EfsAiHub.Core.Agents.csproj.");
        using var reader = new StreamReader(stream);
        var raw = reader.ReadToEnd();
        // .md gravado com EOL host-dependent (LF no macOS/Linux, CRLF no
        // Windows). Normaliza pra LF antes de o NFC do Render rodar — output
        // do prompt fica determinístico entre plataformas.
        return raw.Replace("\r\n", "\n").TrimEnd('\n');
    }

    private static string? RenderRouterIntentsBlock(
        AgentType type,
        IReadOnlyList<RouterIntent>? intents)
    {
        if (type != AgentType.Router || intents is not { Count: > 0 })
            return null;

        var catalog = FormatIntentsCatalog(intents);
        if (catalog.Length == 0) return null;

        return RouterTemplate.Value.Replace(IntentsPlaceholder, catalog);
    }

    private static string FormatIntentsCatalog(IReadOnlyList<RouterIntent> intents)
    {
        var sb = new StringBuilder();
        var first = true;
        foreach (var intent in intents)
        {
            var name = (intent.Name ?? string.Empty).Trim();
            if (name.Length == 0) continue;

            if (!first) sb.Append('\n');
            first = false;

            sb.Append("- `").Append(name).Append('`');

            var description = (intent.Description ?? string.Empty).Trim();
            if (description.Length > 0)
                sb.Append(" — ").Append(description);

            var validExamples = intent.Examples?
                .Select(e => (e ?? string.Empty).Trim())
                .Where(e => e.Length > 0)
                .ToList() ?? new List<string>();

            if (validExamples.Count > 0)
            {
                sb.Append(". Exemplos: ");
                sb.Append(string.Join(", ", validExamples.Select(e => $"\"{e}\"")));
            }
        }
        return sb.ToString();
    }

    private static string? RenderSkillsBlock(IReadOnlyList<Skill>? skills)
    {
        if (skills is not { Count: > 0 })
            return null;

        var addenda = skills
            .Where(s => !string.IsNullOrWhiteSpace(s.InstructionsAddendum))
            .Select(s => s.InstructionsAddendum!.Trim())
            .ToList();

        if (addenda.Count == 0)
            return null;

        var sb = new StringBuilder();
        for (var i = 0; i < addenda.Count; i++)
        {
            if (i > 0) sb.Append("\n\n---\n\n");
            sb.Append(addenda[i]);
        }
        return sb.ToString();
    }

    private static string? RenderWorkerScopeBlock(
        AgentType type,
        IReadOnlyDictionary<string, string>? metadata)
    {
        if (type != AgentType.Worker || metadata is null) return null;
        if (!metadata.TryGetValue(AgentDefinition.WorkerScopeMetadataKey, out var scope)
            || string.IsNullOrWhiteSpace(scope))
        {
            return null;
        }

        var trimmed = scope.Trim();
        return $"<scope>\n{trimmed}\n</scope>";
    }

    /// <summary>
    /// Bloco autoritativo do contrato Conversational. Estrutura aproveita
    /// primacy + recency bias: a primeira frase é a regra anti-author (mais
    /// crítica), a última reforça "message é texto plano pro humano".
    ///
    /// <para>
    /// Inclui um EXEMPLO COMPLETO do output esperado, gerado dinamicamente a
    /// partir dos schemas declarados (<paramref name="structuredOutputSchema"/>
    /// e <paramref name="operationalMemorySchema"/>). LLMs em strict mode
    /// tendem a copiar a forma literal do schema (ex.: emitir
    /// <c>{"items":[...]}</c> quando o sub-schema é
    /// <c>{type:array, items:...}</c>) — mostrar exemplo concreto reduz
    /// drasticamente esse erro.
    /// </para>
    ///
    /// <paramref name="hasOperationalMemory"/>: quando true, o bloco anuncia
    /// o 5º campo. Sem isso o prompt diz N campos mas o schema strict requer
    /// N+1 (injetado pelo <c>OutputSchemaRenderer</c>) — modelo entra em
    /// conflito e tenta resolver dumping a memória dentro de <c>message</c>,
    /// vazando estado interno pro usuário.
    /// </summary>
    // Fragmentos auxiliares pro template Conversational. Placeholders vazios
    // (sem example, sem memory) produzem o output esperado quando os blocos
    // condicionais não se aplicam — sem deixar linhas em branco extras.
    private const string OperationalMemoryFieldFragment =
        "\n- `operationalMemory`: campo interno da plataforma — o sistema strippa antes de entregar. " +
        "Emita o estado COMPLETO atualizado (full replacement, não delta), conforme o sub-schema " +
        "declarado de memória. Dados de continuidade vão aqui; mantenha `message` e `output` " +
        "apenas com conteúdo visível ao usuário.";

    private const string MemoryParentheticalFragment =
        " (ou em `operationalMemory` quando for estado interno)";

    private static string? RenderConversationalResponseFormatBlock(
        AgentType type,
        IReadOnlyDictionary<string, string>? metadata,
        bool hasOperationalMemory,
        JsonDocument? structuredOutputSchema,
        JsonDocument? operationalMemorySchema)
    {
        if (type != AgentType.Conversational) return null;

        var outputType = ReadConversationalOutputType(metadata);
        var statuses = ReadConversationalOutputStatuses(metadata);
        var statusList = string.Join(" | ", statuses.Select(s => $"`{s}`"));

        // Exemplo concreto da forma esperada — gerado dinamicamente a partir
        // do schema do agente. Renderizado ANTES da lista de campos pra que
        // o LLM ancore na estrutura visual primeiro (primacy estrutural).
        var exampleJson = BuildOutputContractExample(
            outputType,
            statuses,
            hasOperationalMemory,
            structuredOutputSchema,
            operationalMemorySchema);

        var exampleSection = exampleJson is null
            ? string.Empty
            : "Sua resposta DEVE seguir EXATAMENTE esta forma estrutural:\n\n"
              + "```json\n" + exampleJson + "\n```\n\n"
              + "EXEMPLO ILUSTRATIVO — substitua os placeholders (`<...>`) e valores "
              + "pelos dados reais do turno atual. NUNCA emita placeholders literais como "
              + "`\"<string>\"` ou `\"<v1 | v2>\"` na resposta final.\n\n";

        return ConversationalTemplate.Value
            .Replace(ExampleSectionPlaceholder, exampleSection)
            .Replace(OutputTypePlaceholder, outputType)
            .Replace(OutputStatusListPlaceholder, statusList)
            .Replace(OperationalMemoryFieldPlaceholder,
                hasOperationalMemory ? OperationalMemoryFieldFragment : string.Empty)
            .Replace(MemoryParentheticalPlaceholder,
                hasOperationalMemory ? MemoryParentheticalFragment : string.Empty);
    }

    // Serializer dedicado ao exemplo: indented + UnsafeRelaxedJsonEscaping pra
    // que placeholders como `<string>` ou `<v1 | v2>` apareçam sem escape
    // pesado (`<`) que tornaria a leitura difícil pro LLM.
    private static readonly JsonSerializerOptions ExampleSerializerOpts = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Monta o JSON de exemplo do output Conversational completo:
    /// <c>output_type</c> constante, <c>output_status</c> enum truncado,
    /// <c>message</c> placeholder, <c>output</c> instância do sub-schema (se
    /// presente), <c>operationalMemory</c> instância do schema da memória
    /// (se <paramref name="hasOperationalMemory"/>).
    ///
    /// <para>
    /// O sub-schema do <c>output</c> é extraído de
    /// <paramref name="structuredOutputSchema"/> via
    /// <c>.properties.output</c> — o template wrappou o schema do user nesse
    /// caminho. Quando <c>output</c> ausente do schema, o campo simplesmente
    /// não aparece no exemplo (agente texto-livre).
    /// </para>
    /// </summary>
    private static string? BuildOutputContractExample(
        string outputType,
        IReadOnlyList<string> statuses,
        bool hasOperationalMemory,
        JsonDocument? structuredOutputSchema,
        JsonDocument? operationalMemorySchema)
    {
        var example = new JsonObject
        {
            ["output_type"] = outputType,
            ["output_status"] = BuildStatusPlaceholder(statuses),
            ["message"] = "<texto humano em pt-BR, curto e direto>",
            ["historyText"] = "<prosa natural completa e autossuficiente do que foi respondido — sem JSON/markdown>",
        };

        var outputSubSchema = ExtractOutputSubSchema(structuredOutputSchema);
        if (outputSubSchema is { } subSchema)
        {
            var outputExample = JsonSchemaExampleGenerator.Generate(subSchema);
            // GenerateExample pode retornar null pra type=null literal — mas
            // pra um sub-schema válido isso é raro. Inclui mesmo assim.
            example["output"] = outputExample;
        }

        if (hasOperationalMemory && operationalMemorySchema is not null)
        {
            var memExample = JsonSchemaExampleGenerator.Generate(operationalMemorySchema);
            example["operationalMemory"] = memExample;
        }

        return example.ToJsonString(ExampleSerializerOpts);
    }

    private static string BuildStatusPlaceholder(IReadOnlyList<string> statuses)
    {
        if (statuses.Count == 0) return "<status>";
        if (statuses.Count == 1) return statuses[0];
        var shown = statuses.Take(5).ToList();
        var suffix = statuses.Count > 5 ? " | ..." : string.Empty;
        return $"<{string.Join(" | ", shown)}{suffix}>";
    }

    /// <summary>
    /// Resolve o sub-schema do <c>output</c> que vai virar exemplo. Lida com
    /// dois shapes possíveis:
    ///
    /// <list type="number">
    ///   <item><b>Schema canônico Conversational</b> (montado por
    ///         <c>AgentTemplateService.BuildCanonicalSchema</c>) — tem
    ///         <c>properties.output_type</c> + <c>properties.output_status</c>
    ///         como marker. Nesse caso o sub-schema do user vive em
    ///         <c>properties.output</c>.</item>
    ///   <item><b>Sub-schema do user direto</b> — quando o composer é
    ///         chamado fora da pipeline normal (testes, hot paths que pulam o
    ///         <c>AgentTemplateService</c>). O root é tratado como sub-schema
    ///         direto.</item>
    /// </list>
    ///
    /// Retorna null quando agente é texto livre (canônico sem <c>output</c>
    /// declarado) ou quando o schema é inválido.
    /// </summary>
    private static JsonElement? ExtractOutputSubSchema(JsonDocument? doc)
    {
        if (doc is null) return null;
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return null;

        var hasProps = root.TryGetProperty("properties", out var props)
            && props.ValueKind == JsonValueKind.Object;

        // Detecta forma canônica via marker dual (presence of output_type +
        // output_status). Sub-schemas custom do user dificilmente terão essas
        // chaves no top-level, então o discriminador é robusto.
        var isCanonical = hasProps
            && props.TryGetProperty("output_type", out _)
            && props.TryGetProperty("output_status", out _);

        if (isCanonical)
        {
            // Canônico: `output` é a chave esperada; ausente = texto livre.
            if (props.TryGetProperty("output", out var output))
                return output;
            return null;
        }

        // Não-canônico: root é o sub-schema do user direto.
        return root;
    }

    // Defaults mantidos em sync com AgentTemplateService.ApplyConversational —
    // se um deles mudar, o outro precisa acompanhar pra que o prompt anuncie
    // o mesmo enum que o schema enforça. Duplicação consciente (4 caminhos
    // leem essa metadata hoje); helper compartilhado fica como follow-up.
    private static string ReadConversationalOutputType(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null) return "text";
        if (!metadata.TryGetValue(AgentDefinition.ConversationalOutputTypeMetadataKey, out var raw))
            return "text";
        var trimmed = (raw ?? string.Empty).Trim();
        return trimmed.Length == 0 ? "text" : trimmed;
    }

    private static IReadOnlyList<string> ReadConversationalOutputStatuses(
        IReadOnlyDictionary<string, string>? metadata)
    {
        var defaults = new[] { "default" };
        if (metadata is null) return defaults;
        if (!metadata.TryGetValue(AgentDefinition.ConversationalOutputStatusesMetadataKey, out var raw))
            return defaults;
        if (string.IsNullOrWhiteSpace(raw)) return defaults;

        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return defaults;
            var list = new List<string>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String) continue;
                var s = item.GetString();
                if (!string.IsNullOrWhiteSpace(s)) list.Add(s.Trim());
            }
            return list.Count == 0 ? defaults : list;
        }
        catch (JsonException)
        {
            return defaults;
        }
    }
}
