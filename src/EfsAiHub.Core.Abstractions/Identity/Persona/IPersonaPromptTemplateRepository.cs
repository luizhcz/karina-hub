namespace EfsAiHub.Core.Abstractions.Identity.Persona;

public interface IPersonaPromptTemplateRepository
{
    Task<PersonaPromptTemplate?> GetByScopeAsync(string scope, CancellationToken ct = default);
    Task<PersonaPromptTemplate?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<PersonaPromptTemplate>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Upsert por <see cref="PersonaPromptTemplate.Scope"/>. Cria se não existe,
    /// atualiza in-place caso contrário (constraint UNIQUE(Scope) força 1 linha por scope).
    ///
    /// <para>F5: também append uma version nova em
    /// <c>persona_prompt_template_versions</c> + move
    /// <see cref="PersonaPromptTemplate.ActiveVersionId"/> pra ela, tudo na
    /// mesma transação.</para>
    /// </summary>
    Task<PersonaPromptTemplate> UpsertAsync(
        PersonaPromptTemplate template,
        string? createdBy = null,
        string? changeReason = null,
        CancellationToken ct = default);

    Task<bool> DeleteAsync(int id, CancellationToken ct = default);

    // ── F5: versionamento ───────────────────────────────────────────────

    /// <summary>
    /// Lista todas as versions de um template, ordenadas por
    /// <see cref="PersonaPromptTemplateVersion.CreatedAt"/> desc
    /// (mais recente primeiro).
    /// </summary>
    Task<IReadOnlyList<PersonaPromptTemplateVersion>> GetVersionsAsync(
        int templateId, CancellationToken ct = default);

    /// <summary>
    /// Rollback: cria nova version com o conteúdo da versão alvo + aponta
    /// <see cref="PersonaPromptTemplate.ActiveVersionId"/> pra ela. Retorna
    /// o template atualizado, ou null se <paramref name="targetVersionId"/>
    /// não pertence ao template.
    /// </summary>
    Task<PersonaPromptTemplate?> RollbackAsync(
        int templateId,
        Guid targetVersionId,
        string? createdBy = null,
        CancellationToken ct = default);
}
