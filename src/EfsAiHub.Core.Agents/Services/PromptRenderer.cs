using System.Text;
using EfsAiHub.Core.Agents.RouterIntents;
using EfsAiHub.Core.Agents.Skills;

namespace EfsAiHub.Core.Agents.Services;

/// <summary>
/// Composição determinística do <see cref="AgentDefinition.Instructions"/>
/// final a partir do texto autoral do owner mais os blocos auto-gerados a
/// partir de dependências resolvidas. O resultado vai gravado no banco;
/// runtime consome direto, sem mutar.
///
/// Cada bloco auto-gerado é envolvido pelos marcadores de
/// <see cref="AgentInstructionsMarkers"/> pra que o decomposer possa
/// devolver apenas o autoral ao client. Mesma input → mesmo output
/// byte-a-byte.
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

        if (!hasAuthor && intentsBlock is null && skillsBlock is null && workerScopeBlock is null)
            return authorInstructions;

        var sb = new StringBuilder();

        if (hasAuthor)
            sb.Append(authorInstructions!.TrimEnd());

        AppendBlock(sb, intentsBlock);
        AppendBlock(sb, skillsBlock);
        AppendBlock(sb, workerScopeBlock);

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
        sb.Append(AgentInstructionsMarkers.IntentsBegin);
        sb.Append('\n');
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
        sb.Append("# Memória operacional\n\n");
        sb.Append(
            "No campo `operationalMemory` do output, **sempre preencha**:\n" +
            "- `last_intent`: copie o valor de `intent` que você escolheu.\n" +
            "- `last_reason`: copie o valor de `reason` (até 200 chars).\n\n" +
            "Esta memória é persistida e injetada no próximo turno como contexto. " +
            "Não invente outros campos.");

        sb.Append('\n');
        sb.Append(AgentInstructionsMarkers.IntentsEnd);
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
        sb.Append(AgentInstructionsMarkers.SkillsBegin);
        sb.Append('\n');
        for (var i = 0; i < addenda.Count; i++)
        {
            if (i > 0) sb.Append("\n\n---\n\n");
            sb.Append(addenda[i]);
        }
        sb.Append('\n');
        sb.Append(AgentInstructionsMarkers.SkillsEnd);
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
        sb.Append(AgentInstructionsMarkers.WorkerScopeBegin);
        sb.Append('\n');
        sb.Append("# Domínio de análise\n\n");
        sb.Append(trimmed);
        sb.Append("\n\n---\n");
        sb.Append(AgentInstructionsMarkers.WorkerScopeEnd);
        return sb.ToString();
    }
}
