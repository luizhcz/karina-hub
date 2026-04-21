namespace EfsAiHub.Core.Agents.DocumentIntelligence;

/// <summary>
/// Wrapper para o Azure Document Intelligence SDK.
/// </summary>
public interface IDocumentIntelligenceService
{
    Task<DiAnalyzeResult> AnalyzeAsync(Uri sourceUri, string model, string[]? features, CancellationToken ct);
}

/// <summary>
/// Resultado da análise do Azure Document Intelligence.
/// </summary>
public record DiAnalyzeResult(
    string OperationId,
    string RawJson,
    int PageCount,
    bool HasTables,
    bool HasHandwriting,
    string? PrimaryLanguage,
    int DurationMs);
