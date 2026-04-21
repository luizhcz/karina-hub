using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EfsAiHub.Core.Agents;
using EfsAiHub.Core.Abstractions.Persistence;

namespace EfsAiHub.Core.Agents.Skills;

/// <summary>
/// Fase 3 — Skill é um agrupamento de <c>tools + addendum de prompt + policy +
/// referências a knowledge sources</c>. Um agente referencia skills por id/version em vez
/// de listar tools flat. Prepara a Fase 4 (RAG).
///
/// Regra: Skill contém AgentToolDefinition (reuso do shape), NÃO duplica impl de tool.
/// O registry global resolve a implementação via <see cref="Application.Interfaces.IFunctionToolRegistry"/>.
/// </summary>
public sealed class Skill : IProjectScoped
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }

    /// <summary>Bloco de texto concatenado ao prompt do agente quando a skill é ativada.</summary>
    public string? InstructionsAddendum { get; init; }

    /// <summary>Tools expostas pela skill (reutiliza o shape de AgentToolDefinition).</summary>
    public List<AgentToolDefinition> Tools { get; init; } = [];

    /// <summary>
    /// IDs de knowledge sources consumidas pela skill. Consumidos na Fase 4 pelo
    /// RagAugmentationMiddleware. Ignorado na Fase 3 — apenas persistido.
    /// </summary>
    public List<string> KnowledgeSourceIds { get; init; } = [];

    public SkillPolicy? Policy { get; init; }

    public Dictionary<string, string> Metadata { get; init; } = [];
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string ProjectId { get; set; } = "default";

    /// <summary>Hash canônico do estado atual da skill — usado para idempotência de snapshot.</summary>
    public string ContentHash => ComputeHash(this);

    public static string ComputeHash(Skill skill)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            skill.Id,
            skill.Name,
            skill.Description,
            skill.InstructionsAddendum,
            tools = skill.Tools.Select(t => new
            {
                t.Type,
                t.Name,
                t.RequiresApproval,
                t.ServerLabel,
                t.ServerUrl,
                t.AllowedTools,
                t.RequireApproval,
                t.ConnectionId
            }),
            knowledgeSources = skill.KnowledgeSourceIds,
            policy = skill.Policy
        }, JsonDefaults.Domain);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}

/// <summary>
/// Fase 3 — política declarativa aplicada a invocações dentro do contexto de uma skill.
/// MVP: apenas persiste; enforcement completo fica para fases seguintes.
/// </summary>
public sealed record SkillPolicy(
    int? AllowedCallsPerRun = null,
    double? CostWeight = null,
    IReadOnlyList<string>? RequiredGuards = null);

/// <summary>
/// Fase 3 — referência de um agente a uma skill, opcionalmente amarrada a uma version específica.
/// Null em <see cref="SkillVersionId"/> = resolver pega a revisão mais recente no momento da construção.
/// Quando o snapshot de <see cref="Agents.AgentVersion"/> é gravado, o SkillVersionId é materializado,
/// garantindo rollback determinístico.
/// </summary>
public sealed record SkillRef(string SkillId, string? SkillVersionId = null);
