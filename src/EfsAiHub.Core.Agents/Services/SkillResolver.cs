using System.Collections.Concurrent;
using EfsAiHub.Core.Agents;
using EfsAiHub.Core.Agents.Skills;
using Microsoft.Extensions.Logging;

namespace EfsAiHub.Core.Agents.Services;

/// <summary>
/// Fase 3 — resolve <see cref="SkillRef"/>s em snapshots concretos de <see cref="Skill"/>.
/// Serve como cache in-memory sobre (<see cref="ISkillRepository"/>, <see cref="ISkillVersionRepository"/>),
/// usado pelo AgentFactory no hot-path de construção de agentes.
///
/// TTL curto (2min) — skills mudam raramente e stale leituras não comprometem execuções
/// porque cada AgentVersion já carrega o snapshot congelado da skill no momento do publish.
/// </summary>
public interface ISkillResolver
{
    /// <summary>
    /// Resolve uma skill por id+version. Se <see cref="SkillRef.SkillVersionId"/> for null,
    /// retorna a revisão corrente. null se não encontrada.
    /// </summary>
    Task<Skill?> ResolveAsync(SkillRef reference, CancellationToken ct = default);
}

public sealed class SkillResolver : ISkillResolver
{
    private static readonly TimeSpan CurrentTtl = TimeSpan.FromMinutes(2);

    private readonly ISkillRepository _skills;
    private readonly ISkillVersionRepository _versions;
    private readonly ILogger<SkillResolver> _logger;
    private readonly ConcurrentDictionary<string, (Skill? Value, DateTime ExpiresAt)> _current = new(StringComparer.OrdinalIgnoreCase);
    // Versões específicas são imutáveis — cache sem expiração.
    private readonly ConcurrentDictionary<string, Skill?> _byVersion = new(StringComparer.OrdinalIgnoreCase);

    public SkillResolver(
        ISkillRepository skills,
        ISkillVersionRepository versions,
        ILogger<SkillResolver> logger)
    {
        _skills = skills;
        _versions = versions;
        _logger = logger;
    }

    public async Task<Skill?> ResolveAsync(SkillRef reference, CancellationToken ct = default)
    {
        if (!string.IsNullOrEmpty(reference.SkillVersionId))
        {
            if (_byVersion.TryGetValue(reference.SkillVersionId, out var cachedVersion))
                return cachedVersion;
            var version = await _versions.GetByIdAsync(reference.SkillVersionId, ct);
            var snap = version?.Snapshot;
            _byVersion[reference.SkillVersionId] = snap;
            return snap;
        }

        var now = DateTime.UtcNow;
        if (_current.TryGetValue(reference.SkillId, out var entry) && entry.ExpiresAt > now)
            return entry.Value;

        var skill = await _skills.GetByIdAsync(reference.SkillId, ct);
        _current[reference.SkillId] = (skill, now + CurrentTtl);
        if (skill is null)
            _logger.LogWarning("[SkillResolver] Skill '{SkillId}' não encontrada.", reference.SkillId);
        return skill;
    }
}

/// <summary>
/// Fase 3 — helpers para fundir um conjunto de skills resolvidas em um <see cref="AgentDefinition"/>
/// efetivo, concatenando <see cref="Skill.InstructionsAddendum"/> ao prompt e agregando tools.
/// </summary>
public static class SkillMerger
{
    private const string AddendumSeparator = "\n\n---\n\n";

    public static AgentDefinition ApplySkills(AgentDefinition definition, IReadOnlyList<Skill> resolved)
    {
        if (resolved.Count == 0) return definition;

        var mergedTools = new List<AgentToolDefinition>(definition.Tools);
        var seen = new HashSet<string>(
            definition.Tools
                .Where(t => !string.IsNullOrEmpty(t.Name))
                .Select(t => $"{t.Type}:{t.Name}"),
            StringComparer.OrdinalIgnoreCase);

        var addenda = new List<string>();
        if (!string.IsNullOrWhiteSpace(definition.Instructions))
            addenda.Add(definition.Instructions!);

        foreach (var skill in resolved)
        {
            if (!string.IsNullOrWhiteSpace(skill.InstructionsAddendum))
                addenda.Add(skill.InstructionsAddendum!.Trim());

            foreach (var tool in skill.Tools)
            {
                var key = $"{tool.Type}:{tool.Name}";
                if (string.IsNullOrEmpty(tool.Name) || seen.Add(key))
                    mergedTools.Add(tool);
            }
        }

        var merged = new AgentDefinition
        {
            Id = definition.Id,
            Name = definition.Name,
            Description = definition.Description,
            Model = definition.Model,
            Provider = definition.Provider,
            Instructions = string.Join(AddendumSeparator, addenda),
            Tools = mergedTools,
            StructuredOutput = definition.StructuredOutput,
            Middlewares = definition.Middlewares,
            Resilience = definition.Resilience,
            CostBudget = definition.CostBudget,
            SkillRefs = definition.SkillRefs,
            Metadata = definition.Metadata,
            CreatedAt = definition.CreatedAt,
            UpdatedAt = definition.UpdatedAt
        };

        return merged;
    }
}
