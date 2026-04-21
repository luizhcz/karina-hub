namespace EfsAiHub.Core.Agents.DocumentIntelligence;

/// <summary>
/// Entrada de cache para deduplicar processamentos por hash + model + features.
/// </summary>
public record ExtractionCacheEntry(
    string ContentSha256,
    string Model,
    string FeaturesHash,
    string ResultRef,
    int PageCount,
    DateTime ExpiresAt);
