namespace EfsAiHub.Core.Agents.DocumentIntelligence;

/// <summary>
/// Repositório para jobs, eventos e cache de extração de documentos.
/// </summary>
public interface IDocumentExtractionRepository
{
    Task InsertJobAsync(ExtractionJob job, CancellationToken ct);
    Task UpdateJobAsync(ExtractionJob job, CancellationToken ct);
    Task InsertEventAsync(ExtractionEvent evt, CancellationToken ct);
    Task<ExtractionCacheEntry?> LookupCacheAsync(string sha256, string model, string featuresHash, CancellationToken ct);
    Task UpsertCacheAsync(ExtractionCacheEntry entry, CancellationToken ct);
}
