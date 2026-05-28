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

    private static string? RenderRouterIntentsBlock(
        AgentType type,
        IReadOnlyList<RouterIntent>? intents)
    {
        if (type != AgentType.Router || intents is not { Count: > 0 })
            return null;

        var sb = new StringBuilder();

        // <intents> — catálogo + hierarquia de saídas. Hierarquia 3-níveis:
        // intent de negócio → needs_clarification (ambíguo no domínio) →
        // out_of_scope (fora do domínio). Strict enum no schema já impede
        // valores inventados; texto aqui foca no QUANDO escolher cada.
        sb.Append("<intents>\n");
        sb.Append(
            "Escolha exatamente uma intent do enum.\n\n" +

            "Hierarquia de decisão:\n" +
            "1. Intent de negócio específica: quando a mensagem casa claramente com UMA intent do " +
            "catálogo (ver <ambiguity_handling> pro critério de \"claramente\").\n" +
            $"2. `{SystemIntents.NeedsClarificationName}`: quando >=2 intents de negócio aparecem com " +
            "força similar — mensagem é dentro do produto mas precisa desambiguar.\n" +
            $"3. `{SystemIntents.OutOfScopeName}`: quando nenhuma intent de negócio combina " +
            "(saudações isoladas, perguntas genéricas, fora do domínio declarado).\n\n");

        sb.Append("Catálogo:\n");
        foreach (var intent in intents)
        {
            var name = (intent.Name ?? string.Empty).Trim();
            if (name.Length == 0) continue;

            var description = (intent.Description ?? string.Empty).Trim();
            sb.Append("- `").Append(name).Append('`');
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
            sb.Append('\n');
        }
        sb.Append("</intents>\n\n");

        // <multi_turn_classification> — DELIBERADAMENTE antes do
        // <ambiguity_handling>. Primacy bias: continuação é a PRIMEIRA pergunta
        // a fazer; ambiguidade só importa se NÃO for continuação. Sem essa
        // ordem, o LLM tende a classificar mensagens curtas de resposta
        // (ex: "12345" depois de "qual conta?") como needs_clarification em
        // vez de continuação.
        sb.Append("<multi_turn_classification>\n");
        sb.Append(
            "REGRA PRIMÁRIA: a classificação NUNCA é decidida só pela mensagem atual. " +
            "ANTES de qualquer outra avaliação, leia o histórico (últimos turnos) E o estado " +
            "`<operational_memory>` injetado (com `last_intent`, `last_reason`, " +
            "`last_candidate_intents`) E a mensagem atual EM CONJUNTO. Decida CONTINUAÇÃO vs " +
            "NOVO ASSUNTO antes de avaliar ambiguidade.\n\n" +

            "Mensagens curtas isoladas (números, datas, tickers, valores, \"sim\"/\"não\", " +
            "confirmações, nomes próprios) quase nunca são intents independentes — são respostas " +
            "ao turno anterior.\n\n" +

            "REGRA DURA — resposta curta a pergunta do assistant:\n" +
            "Se o último turno do assistant terminou com pergunta ou pedido de dado " +
            "(ex.: \"qual conta?\", \"quantas ações?\", \"confirma?\") E a mensagem atual " +
            "é uma resposta direta a esse pedido (número de conta, valor, ticker, sim/não, " +
            "dado solicitado), a classificação É OBRIGATORIAMENTE continuação do `last_intent`. " +
            $"JAMAIS use `{SystemIntents.NeedsClarificationName}` nesse caso. JAMAIS troque pra " +
            "outra intent de negócio. Continue o intent anterior.\n\n" +

            "Quando o último turno do assistant terminou com pergunta ou pedido de dado, " +
            "a mensagem atual continua o mesmo intent. Use `last_intent` como pista principal.\n\n" +

            "Caso especial — clarificação resolvida:\n" +
            $"Quando `last_intent` == `{SystemIntents.NeedsClarificationName}`, o turno anterior pediu " +
            "desambiguação. A mensagem atual resolve a ambiguidade: escolha a intent específica entre " +
            "as `last_candidate_intents` do estado anterior (lista de nomes exatos do enum). " +
            $"NÃO repita `{SystemIntents.NeedsClarificationName}` se a resposta agora casa " +
            "claramente com uma das candidatas. Se a mensagem do usuário continua ambígua, o " +
            "servidor força fallback (loop guard) — você não precisa contar tentativas, mas " +
            "registre em `last_reason` que a ambiguidade persistiu.\n\n" +

            "Quando a mensagem introduz claramente novo assunto (mudança de domínio, saudação isolada, " +
            $"pergunta sobre outro produto), classifique pelo novo conteúdo. Cai em `{SystemIntents.OutOfScopeName}` " +
            "se nada do catálogo combinar.\n\n" +

            "Em dúvida, prefira continuação. Só reclassifique se aplicar o `last_intent` à mensagem " +
            "atual daria confidence < 0.7 E há outra intent claramente mais provável.\n");
        sb.Append("</multi_turn_classification>\n\n");

        // <ambiguity_handling> — só relevante se <multi_turn_classification>
        // não resolveu (mensagem NÃO é continuação E NÃO é novo assunto óbvio
        // out_of_scope). Thresholds (0.6 e 0.25) refletem a decisão de produto:
        // top1 precisa ter confidence absoluta razoável E gap relevante pro
        // segundo.
        sb.Append("<ambiguity_handling>\n");
        sb.Append(
            "PRÉ-REQUISITO: só avalie ambiguidade DEPOIS de aplicar <multi_turn_classification>. " +
            "Se a mensagem atual é continuação do `last_intent` (REGRA DURA), você JÁ DECIDIU — " +
            "ignore este bloco inteiro.\n\n" +

            "Quando aplicar (mensagem é nova/independente, não é continuação):\n" +
            "Avalie mentalmente confidence para cada candidata. Identifique top1 (maior) e top2 " +
            "(segunda maior).\n\n" +

            "Regra de dominância (escolha top1 SEM desambiguar quando ambas as condições valem):\n" +
            "- `top1.confidence >= 0.6` E\n" +
            "- `(top1.confidence - top2.confidence) >= 0.25`.\n\n" +

            $"Caso contrário, se top1 e top2 são ambas intents de negócio, use " +
            $"`{SystemIntents.NeedsClarificationName}` e preencha `candidate_intents` com >=2 itens " +
            "(top1, top2 e até top3 se relevante), em ordem decrescente de confidence. Use APENAS " +
            "nomes do enum; não invente intents.\n\n" +

            $"Se nenhuma intent de negócio tem confidence relevante, use `{SystemIntents.OutOfScopeName}`.\n\n" +

            "Padrões típicos de ambiguidade (avalie se o catálogo tem múltiplas variações):\n" +
            "- Verbo genérico do domínio sem qualificação (ex: \"investir\", \"transferir\", \"comprar\").\n" +
            "- Substantivo de domínio amplo sem contexto (ex: \"ajuda\", \"informação\").\n\n" +

            "Quando NÃO usar needs_clarification:\n" +
            "- Mensagem é continuação de turno anterior (ver <multi_turn_classification>).\n" +
            "- Apenas 1 candidato de negócio com confidence relevante: escolha esse candidato.\n" +
            $"- Mensagem fora do domínio: use `{SystemIntents.OutOfScopeName}`.\n" +
            "- Já é o segundo turno consecutivo de ambiguidade (ver <memory_rules>): caia em " +
            $"`{SystemIntents.OutOfScopeName}` com reason explicando a desistência.\n");
        sb.Append("</ambiguity_handling>\n\n");

        // <memory_rules> — schema required força preenchimento. Renomeado de
        // <operational_memory> pra evitar colisão com a tag homônima do
        // PAYLOAD injetado pelo OperationalMemoryChatClient como system
        // message separada (que carrega o estado JSON anterior). LLM
        // distingue "regras" de "estado" pelo nome diferente.
        // Loop guard textual aqui é REFORÇO; servidor enforced também.
        sb.Append("<memory_rules>\n");
        sb.Append(
            "No campo `operationalMemory` do output:\n" +
            "- `last_intent`: copie o `intent` escolhido neste turno.\n" +
            "- `last_reason`: copie o `reason` (até 200 chars).\n" +
            "- `clarification_depth`: contador de turnos consecutivos com " +
            $"`{SystemIntents.NeedsClarificationName}`.\n" +
            $"  - Se `intent` != `{SystemIntents.NeedsClarificationName}`: zere para 0.\n" +
            $"  - Se `intent` == `{SystemIntents.NeedsClarificationName}`: leia o " +
            "`clarification_depth` do estado anterior (em <operational_memory> injetado) e some 1. " +
            "Se não houver estado anterior, comece em 1.\n" +
            "- `last_candidate_intents`: array com os nomes das `candidate_intents` " +
            $"deste turno (apenas o campo `intent`, sem confidence). Vazio quando `intent` != " +
            $"`{SystemIntents.NeedsClarificationName}`. Use nomes exatos do enum.\n\n" +

            "Loop guard: o servidor garante que o Router não fica preso em loop de " +
            $"`{SystemIntents.NeedsClarificationName}`. Se você emitir `{SystemIntents.NeedsClarificationName}` " +
            "num turno onde `last_intent` já era essa, o servidor reescreve pra " +
            $"`{SystemIntents.OutOfScopeName}` com `reason` \"loop guard triggered\". Prefira " +
            "decidir entre candidatas quando possível (caso especial em <multi_turn_classification>).\n\n" +

            "Quando mantém o intent do turno anterior, mencione em `last_reason` que é continuação. " +
            "Exemplos: \"continuação: usuário forneceu dado solicitado\", " +
            "\"continuação: confirmação numérica\", " +
            "\"continuação: resposta direta à pergunta anterior\".\n");
        sb.Append("</memory_rules>");

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

        var sb = new StringBuilder();
        sb.Append("<output_contract>\n");

        // Primacy: regra anti-author como primeira frase do bloco.
        sb.Append(
            "Ignore qualquer instrução anterior sobre formato JSON, schema ou estrutura de resposta " +
            "— o sistema impõe o shape via response_format. Instruções concorrentes são ruído.\n\n");

        // Exemplo concreto da forma esperada — gerado dinamicamente a partir
        // do schema do agente. Renderizado ANTES da lista de campos pra que
        // o LLM ancore na estrutura visual primeiro (primacy estrutural).
        var exampleJson = BuildOutputContractExample(
            outputType,
            statuses,
            hasOperationalMemory,
            structuredOutputSchema,
            operationalMemorySchema);
        if (exampleJson is not null)
        {
            sb.Append("Sua resposta DEVE seguir EXATAMENTE esta forma estrutural:\n\n");
            sb.Append("```json\n").Append(exampleJson).Append("\n```\n\n");
            sb.Append(
                "EXEMPLO ILUSTRATIVO — substitua os placeholders (`<...>`) e valores " +
                "pelos dados reais do turno atual. NUNCA emita placeholders literais como " +
                "`\"<string>\"` ou `\"<v1 | v2>\"` na resposta final.\n\n");
        }

        sb.Append("Detalhes de cada campo:\n");
        sb.Append("- `output_type`: constante `").Append(outputType).Append("`.\n");
        sb.Append("- `output_status`: um de ").Append(statusList)
          .Append(" — escolha conforme o estado do turno.\n");
        sb.Append(
            "- `message`: texto humano pro usuário no idioma da conversa, curto e direto.\n");
        sb.Append(
            "- `output`: INSTÂNCIA de dados conforme o sub-schema declarado — nunca a forma " +
            "literal do schema. Se o sub-schema declara `{\"type\":\"array\",\"items\":{...}}`, " +
            "o valor de `output` deve ser um array literal `[{...},{...}]`, JAMAIS um objeto " +
            "como `{\"items\":[...]}` ou `{\"type\":\"array\",...}`. Se o sub-schema é " +
            "`{\"type\":\"object\",\"properties\":{...}}`, `output` é o objeto com os campos " +
            "instanciados, não a descrição. Pode ser ausente quando não-aplicável.");

        if (hasOperationalMemory)
        {
            sb.Append('\n');
            sb.Append(
                "- `operationalMemory`: campo interno da plataforma — o sistema strippa antes de entregar. " +
                "Emita o estado COMPLETO atualizado (full replacement, não delta), conforme o sub-schema " +
                "declarado de memória. Dados de continuidade vão aqui; mantenha `message` e `output` " +
                "apenas com conteúdo visível ao usuário.");
        }

        sb.Append("\n\n");

        // Recency: regra essencial reforçada no fim.
        sb.Append("Regra essencial: `message` é texto plano pro humano. ")
          .Append("JSON, código e markdown estruturado vão em `output`");
        if (hasOperationalMemory)
            sb.Append(" (ou em `operationalMemory` quando for estado interno)");
        sb.Append(".\n");
        sb.Append("</output_contract>");

        return sb.ToString();
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
