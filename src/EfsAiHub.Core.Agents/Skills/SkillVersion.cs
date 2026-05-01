namespace EfsAiHub.Core.Agents.Skills;

/// <summary>
/// Snapshot imutável append-only de uma skill. Mesma mecânica de AgentVersion:
/// toda UpsertAsync de <see cref="Skill"/> cria uma nova SkillVersion (revision = MAX+1)
/// ou reusa a última se ContentHash for idêntico (idempotência).
/// </summary>
public sealed record SkillVersion(
    string SkillVersionId,
    string SkillId,
    int Revision,
    DateTime CreatedAt,
    string? CreatedBy,
    string? ChangeReason,
    Skill Snapshot,
    string ContentHash);

public interface ISkillRepository
{
    Task<Skill?> GetByIdAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Busca skill bypassing project query filter, restrita ao project dono
    /// fornecido. Uso exclusivo de cross-project resolution (agent global referenciando
    /// skill local do owner). Retorna null se skill não existe ou não pertence ao owner.
    /// </summary>
    Task<Skill?> GetByIdForOwnerAsync(string id, string ownerProjectId, CancellationToken ct = default);

    Task<IReadOnlyList<Skill>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Skill>> GetAllAsync(int page, int pageSize, CancellationToken ct = default);
    Task<int> CountAsync(CancellationToken ct = default);
    /// <summary>Retorna todas as skills de todos os projetos. Uso exclusivo de workers e operações admin.</summary>
    Task<IReadOnlyList<Skill>> GetAllAcrossProjectsAsync(CancellationToken ct = default);
    Task<Skill> UpsertAsync(Skill skill, CancellationToken ct = default);
    Task<bool> DeleteAsync(string id, CancellationToken ct = default);
}

public interface ISkillVersionRepository
{
    Task<SkillVersion?> GetByIdAsync(string skillVersionId, CancellationToken ct = default);
    Task<SkillVersion?> GetCurrentAsync(string skillId, CancellationToken ct = default);
    Task<IReadOnlyList<SkillVersion>> ListBySkillAsync(string skillId, CancellationToken ct = default);
    Task<SkillVersion> AppendAsync(SkillVersion version, CancellationToken ct = default);
    Task<int> GetNextRevisionAsync(string skillId, CancellationToken ct = default);
}
