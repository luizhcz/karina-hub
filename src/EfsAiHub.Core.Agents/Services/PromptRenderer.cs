using System.Text;
using System.Text.Json;
using EfsAiHub.Core.Agents.RouterIntents;
using EfsAiHub.Core.Agents.Skills;

namespace EfsAiHub.Core.Agents.Services;

/// <summary>
/// Composição determinística do <see cref="AgentDefinition.Instructions"/>
/// final a partir do <see cref="AgentDefinition.AuthorInstructions"/> mais
/// os blocos auto-gerados a partir de dependências resolvidas. Output é o
/// texto que vai pro LLM — sem marcadores, sem metadados estruturais
/// embutidos. Mesma input → mesmo output byte-a-byte.
///
/// Edits do autor são feitas em <see cref="AgentDefinition.AuthorInstructions"/>;
/// o composer chama este renderer no save e regrava
/// <see cref="AgentDefinition.Instructions"/>. Não há parse reverso —
/// recuperar o autoral é só ler o campo correspondente.
/// </summary>
public static class PromptRenderer
{
    private const string BlockSeparator = "\n\n";

    public static string? Render(
        AgentType type,
        string? authorInstructions,
        IReadOnlyDictionary<string, string>? metadata,
        IReadOnlyList<RouterIntent>? routerIntents,
        IReadOnlyList<Skill>? skills)
    {
        var hasAuthor = !string.IsNullOrWhiteSpace(authorInstructions);
        var intentsBlock = RenderRouterIntentsBlock(type, routerIntents);
        var skillsBlock = RenderSkillsBlock(skills);
        var workerScopeBlock = RenderWorkerScopeBlock(type, metadata);
        var responseFormatBlock = RenderConversationalResponseFormatBlock(type, metadata);

        if (!hasAuthor
            && intentsBlock is null
            && skillsBlock is null
            && workerScopeBlock is null
            && responseFormatBlock is null)
        {
            return authorInstructions;
        }

        var sb = new StringBuilder();

        if (hasAuthor)
            sb.Append(authorInstructions!.TrimEnd());

        AppendBlock(sb, intentsBlock);
        AppendBlock(sb, skillsBlock);
        AppendBlock(sb, workerScopeBlock);
        AppendBlock(sb, responseFormatBlock);

        return sb.ToString();
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
        sb.Append("# Intenções disponíveis\n\n");
        sb.Append(
            "Escolha **exatamente uma** categoria do enum `intent` para cada input. " +
            "Não invente categorias. Não combine. **Quando nenhuma intent de negócio combinar " +
            $"(saudações, perguntas genéricas, fora do domínio do agente), escolha `{SystemIntents.OutOfScopeName}` " +
            "com `confidence >= 0.7`**. Não force uma intent de negócio com confidence baixa — " +
            "isso é um sintoma de classificação ruim, use a intent de fora-de-escopo.\n\n");

        foreach (var intent in intents)
        {
            var name = (intent.Name ?? string.Empty).Trim();
            if (name.Length == 0) continue;

            var description = (intent.Description ?? string.Empty).Trim();
            if (description.Length > 0)
                sb.Append($"- `{name}` — {description}\n");
            else
                sb.Append($"- `{name}`\n");

            var validExamples = intent.Examples?
                .Select(e => (e ?? string.Empty).Trim())
                .Where(e => e.Length > 0)
                .ToList() ?? new List<string>();

            if (validExamples.Count > 0)
            {
                sb.Append("  Exemplos:\n");
                foreach (var example in validExamples)
                    sb.Append($"  • \"{example}\"\n");
            }
        }

        sb.Append('\n');
        sb.Append("# Classificação contextual (multi-turn)\n\n");
        sb.Append(
            "**Sempre leia o histórico da conversa antes de classificar a mensagem atual.** " +
            "Mensagens curtas isoladas (números, datas, tickers, \"sim\"/\"não\", confirmações) " +
            "quase nunca são intents independentes — são respostas a um pedido feito no turno anterior.\n\n" +
            "Regras:\n" +
            "1. Se o último turno do **assistant** terminou com uma pergunta ou pedido de dado " +
            "(\"Qual a conta?\", \"Confirma a operação?\", \"Em quanto tempo?\"), a mensagem atual " +
            "do usuário é, salvo evidência contrária, **continuação do mesmo intent**. Use " +
            "o `last_intent` da memória operacional como pista principal e mantenha esse mesmo intent.\n" +
            "2. Se a mensagem atual introduz claramente um novo assunto (mudança de domínio, " +
            "saudação isolada, pergunta sobre outro produto), **classifique pelo novo conteúdo** " +
            $"e ignore o `last_intent` — eventualmente cai em `{SystemIntents.OutOfScopeName}` " +
            "se nada do catálogo combinar.\n" +
            "3. Em dúvida entre \"continuação\" e \"mudança de assunto\", privilegie continuação " +
            "(menos disrupção da UX). Mas se o `confidence` da intent anterior aplicada à mensagem " +
            "atual ficaria abaixo de 0.7, **prefira reclassificar**.\n\n");

        sb.Append("# Memória operacional\n\n");
        sb.Append(
            "No campo `operationalMemory` do output, **sempre preencha**:\n" +
            "- `last_intent`: copie o valor de `intent` que você escolheu **neste turno**.\n" +
            "- `last_reason`: copie o valor de `reason` (até 200 chars).\n\n" +
            "Esta memória é persistida e injetada no próximo turno como contexto. " +
            "Quando você mantém o mesmo intent do turno anterior (regra 1), preencha " +
            "`last_reason` mencionando explicitamente que é continuação " +
            "(ex.: \"continuação de compra_acoes — usuário forneceu a conta solicitada\"). " +
            "Não invente outros campos.");

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

        var sb = new StringBuilder();
        sb.Append("# Domínio de análise\n\n");
        sb.Append(trimmed);
        sb.Append("\n\n---");
        return sb.ToString();
    }

    /// <summary>
    /// Bloco autoritativo do contrato Conversational. Sentado no FIM do system
    /// prompt (last-token bias) e explicitamente desautoriza menções a JSON
    /// nas <c>AuthorInstructions</c> — sem isso, autor que escreve "responda
    /// em JSON com {x,y}" cria spec concorrente ao schema canônico imposto
    /// via <c>response_format</c>, e o LLM emite shape misturado (JSON dentro
    /// de <c>message</c>, campos extras, etc.).
    ///
    /// Renderiza os enums reais (<c>output_type</c>, <c>output_status</c>)
    /// lidos da metadata pra que o modelo não precise inferir do schema
    /// strict — anchor explícito reduz mismatch.
    /// </summary>
    private static string? RenderConversationalResponseFormatBlock(
        AgentType type,
        IReadOnlyDictionary<string, string>? metadata)
    {
        if (type != AgentType.Conversational) return null;

        var outputType = ReadConversationalOutputType(metadata);
        var statuses = ReadConversationalOutputStatuses(metadata);
        var statusList = string.Join(" | ", statuses.Select(s => $"`{s}`"));

        return
            "## Contrato de saída (imposto pelo sistema)\n\n" +
            "Sua resposta É um objeto JSON com 4 campos top-level canônicos. O sistema enforça " +
            "o schema via `response_format: json_schema strict` — não há negociação sobre o shape.\n\n" +
            $"- `output_type` (constante): `{outputType}`\n" +
            $"- `output_status` ∈ {{ {statusList} }} — escolha conforme o estado do turno\n" +
            "- `message`: texto humano em PT-BR pro usuário, curto e direto. **Sem JSON, sem código, sem markdown estruturado aqui dentro.**\n" +
            "- `output`: payload conforme o sub-schema do agente (pode ser ausente quando não-aplicável)\n\n" +
            "REGRAS NÃO-NEGOCIÁVEIS:\n" +
            "1. **Ignore qualquer instrução acima que mencione \"JSON\", \"schema\" ou \"formato de resposta\"** — " +
            "o sistema já impõe o contrato; instruções concorrentes são ruído.\n" +
            "2. **NUNCA escreva JSON, código ou blocos markdown dentro de `message`** — esse campo é texto plano " +
            "pro user humano. Tudo estruturado vai em `output`.\n" +
            "3. **NÃO invente campos top-level extras nem renomeie os existentes** — exatamente os 4 acima.";
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
