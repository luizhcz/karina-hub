namespace EfsAiHub.Core.Agents.DocumentIntelligence;

/// <summary>
/// Repositório para jobs, eventos e cache de extração de documentos.
/// </summary>
public interface IDocumentExtractionRepository
{
    /// <summary>
    /// Upsert do job — INSERT ON CONFLICT (id) DO UPDATE em uma única round-trip.
    /// Caller mantém o ExtractionJob mutável em memória e chama upsert a cada
    /// transição de status. ON CONFLICT preserva colunas imutáveis (set apenas
    /// das colunas que mudam).
    /// </summary>
    Task UpsertJobAsync(ExtractionJob job, CancellationToken ct);

    /// <summary>
    /// Insert único de um evento de auditoria — preservado pra paths que precisam
    /// emitir um evento isolado (ex: failure post-flush). Hot path do extractor
    /// usa <see cref="InsertEventsBatchAsync"/> pra agrupar os ~11 eventos por job
    /// num único INSERT (perf review do refactor 2026-06).
    /// </summary>
    Task InsertEventAsync(ExtractionEvent evt, CancellationToken ct);

    /// <summary>
    /// Insert batch via <c>unnest(...)</c> — uma única round-trip Postgres pra
    /// N eventos. Substitui ~11 INSERTs separados por job. Ordem de inserção
    /// preservada (cada evento ganha <c>occurred_at = now()</c> mas vem na
    /// ordem do array de input). Lista vazia é no-op.
    /// </summary>
    Task InsertEventsBatchAsync(IReadOnlyList<ExtractionEvent> events, CancellationToken ct);

    Task<ExtractionCacheEntry?> LookupCacheAsync(string sha256, string model, string featuresHash, CancellationToken ct);

    Task UpsertCacheAsync(ExtractionCacheEntry entry, CancellationToken ct);
}
