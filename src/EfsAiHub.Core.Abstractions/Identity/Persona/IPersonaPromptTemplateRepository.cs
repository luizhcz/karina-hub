namespace EfsAiHub.Core.Abstractions.Identity.Persona;

public interface IPersonaPromptTemplateRepository
{
    Task<PersonaPromptTemplate?> GetByScopeAsync(string scope, CancellationToken ct = default);
    Task<PersonaPromptTemplate?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<PersonaPromptTemplate>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Upsert por <see cref="PersonaPromptTemplate.Scope"/>. Cria se não existe,
    /// atualiza in-place caso contrário (constraint UNIQUE(Scope) força 1 linha por scope).
    /// </summary>
    Task<PersonaPromptTemplate> UpsertAsync(PersonaPromptTemplate template, CancellationToken ct = default);

    Task<bool> DeleteAsync(int id, CancellationToken ct = default);
}
