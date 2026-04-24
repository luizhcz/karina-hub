namespace EfsAiHub.Core.Abstractions.Identity.Persona;

/// <summary>
/// F6 — CRUD de experiments A/B de templates.
/// </summary>
public interface IPersonaPromptExperimentRepository
{
    /// <summary>
    /// Retorna o experiment ativo (EndedAt IS NULL) para o par
    /// (<paramref name="projectId"/>, <paramref name="scope"/>). Null se
    /// nenhum ativo. Hot path — pode ser chamado por request.
    /// </summary>
    Task<PersonaPromptExperiment?> GetActiveAsync(
        string projectId, string scope, CancellationToken ct = default);

    Task<PersonaPromptExperiment?> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Lista experiments do project (ativos e encerrados), mais recentes
    /// primeiro. Paginação é out-of-scope do MVP.
    /// </summary>
    Task<IReadOnlyList<PersonaPromptExperiment>> GetByProjectAsync(
        string projectId, CancellationToken ct = default);

    Task<PersonaPromptExperiment> CreateAsync(
        PersonaPromptExperiment experiment, CancellationToken ct = default);

    /// <summary>
    /// Encerra o experiment (seta EndedAt=NOW). Idempotente — calls
    /// subsequentes no-op.
    /// </summary>
    Task<bool> EndAsync(int id, CancellationToken ct = default);

    Task<bool> DeleteAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Resultado agregado do experiment — uma linha por variant.
    /// Lê llm_token_usage filtrando por ExperimentId e agrupando por variant.
    /// </summary>
    Task<IReadOnlyList<ExperimentVariantResult>> GetResultsAsync(
        int experimentId, CancellationToken ct = default);
}

/// <summary>F6 — agregado por variant (A/B) pra dashboard de resultados.</summary>
public sealed class ExperimentVariantResult
{
    public required char Variant { get; init; }
    public required int SampleCount { get; init; }
    public required long TotalTokens { get; init; }
    public required long CachedTokens { get; init; }
    public required double AvgTotalTokens { get; init; }
    public required double AvgDurationMs { get; init; }
}
