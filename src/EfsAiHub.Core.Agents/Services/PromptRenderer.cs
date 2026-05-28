using System.Text;
using System.Text.Json;
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
        bool hasOperationalMemory = false)
    {
        var hasAuthor = !string.IsNullOrWhiteSpace(authorInstructions);
        var intentsBlock = RenderRouterIntentsBlock(type, routerIntents);
        var skillsBlock = RenderSkillsBlock(skills);
        var workerScopeBlock = RenderWorkerScopeBlock(type, metadata);
        var responseFormatBlock = RenderConversationalResponseFormatBlock(type, metadata, hasOperationalMemory);

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

        // <intents> — catálogo + escape de OOS. Strict enum no schema já
        // impede valores inventados; texto aqui foca no QUANDO escolher cada.
        sb.Append("<intents>\n");
        sb.Append(
            "Escolha exatamente uma intent do enum.\n" +
            "Quando nenhuma intent de negócio combinar (saudações, perguntas genéricas, fora do domínio), " +
            $"use `{SystemIntents.OutOfScopeName}` com confidence >= 0.7.\n" +
            $"Confidence abaixo de 0.7 numa intent de negócio? Caia para `{SystemIntents.OutOfScopeName}`.\n\n");

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

        // <multi_turn_classification> — heurística de continuidade. Regras
        // separadas (não enumeradas) pra cada uma ter um único directive,
        // evitando mistura positivo/negativo no mesmo item.
        sb.Append("<multi_turn_classification>\n");
        sb.Append(
            "Sempre leia o histórico antes de classificar. Mensagens curtas isoladas " +
            "(números, datas, tickers, \"sim\"/\"não\", confirmações) quase nunca são intents " +
            "independentes — são respostas ao turno anterior.\n\n" +

            "Quando o último turno do assistant terminou com pergunta ou pedido de dado, " +
            "a mensagem atual continua o mesmo intent. Use `last_intent` como pista principal.\n\n" +

            "Quando a mensagem introduz claramente novo assunto (mudança de domínio, saudação isolada, " +
            $"pergunta sobre outro produto), classifique pelo novo conteúdo. Cai em `{SystemIntents.OutOfScopeName}` " +
            "se nada do catálogo combinar.\n\n" +

            "Em dúvida, prefira continuação. Mas se aplicar o intent anterior à mensagem atual daria " +
            "confidence < 0.7, reclassifique.\n");
        sb.Append("</multi_turn_classification>\n\n");

        // <operational_memory> — o "sempre preencha" sumiu: schema required já força.
        // Múltiplos exemplos de last_reason (não um só) pra evitar overfit literal.
        sb.Append("<operational_memory>\n");
        sb.Append(
            "No campo `operationalMemory` do output:\n" +
            "- `last_intent`: copie o `intent` escolhido neste turno.\n" +
            "- `last_reason`: copie o `reason` (até 200 chars).\n\n" +
            "Quando mantém o intent do turno anterior, mencione em `last_reason` que é continuação. " +
            "Exemplos: \"continuação: usuário forneceu dado solicitado\", " +
            "\"continuação: confirmação numérica\", " +
            "\"continuação: resposta direta à pergunta anterior\".\n");
        sb.Append("</operational_memory>");

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
    /// <paramref name="hasOperationalMemory"/>: quando true, o bloco anuncia
    /// o 5º campo. Sem isso o prompt diz N campos mas o schema strict requer
    /// N+1 (injetado pelo <c>OutputSchemaRenderer</c>) — modelo entra em
    /// conflito e tenta resolver dumping a memória dentro de <c>message</c>,
    /// vazando estado interno pro usuário.
    /// </summary>
    private static string? RenderConversationalResponseFormatBlock(
        AgentType type,
        IReadOnlyDictionary<string, string>? metadata,
        bool hasOperationalMemory)
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

        sb.Append("Sua resposta é um JSON com os campos:\n");
        sb.Append("- `output_type`: constante `").Append(outputType).Append("`.\n");
        sb.Append("- `output_status`: um de ").Append(statusList)
          .Append(" — escolha conforme o estado do turno.\n");
        sb.Append(
            "- `message`: texto humano pro usuário no idioma da conversa, curto e direto.\n");
        sb.Append(
            "- `output`: payload conforme o sub-schema do agente (pode ser ausente quando não-aplicável).");

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
