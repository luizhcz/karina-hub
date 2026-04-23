namespace EfsAiHub.Core.Agents.DocumentIntelligence;

/// <summary>
/// Wrapper para o Azure Document Intelligence SDK.
/// outputFormat: "markdown" | "text" — controla <c>AnalyzeDocumentOptions.OutputContentFormat</c>.
/// Markdown é o default desde 2026 (mais estrutura pro LLM consumir).
/// </summary>
public interface IDocumentIntelligenceService
{
    Task<DiAnalyzeResult> AnalyzeAsync(
        Uri sourceUri, string model, string[]? features, string outputFormat, CancellationToken ct);

    Task<DiAnalyzeResult> AnalyzeBytesAsync(
        byte[] content, string model, string[]? features, string outputFormat, CancellationToken ct);
}

/// <summary>
/// Resultado da análise do Azure Document Intelligence.
/// </summary>
public record DiAnalyzeResult(
    string OperationId,
    string RawJson,
    string Content,
    int PageCount,
    bool HasTables,
    bool HasHandwriting,
    string? PrimaryLanguage,
    int DurationMs);
