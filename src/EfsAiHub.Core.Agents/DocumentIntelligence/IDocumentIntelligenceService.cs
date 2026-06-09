namespace EfsAiHub.Core.Agents.DocumentIntelligence;

/// <summary>
/// Wrapper RAW para o Azure Document Intelligence SDK. NÃO use diretamente fora
/// do <see cref="IDocumentIntelligenceExtractor"/> — chamar este wrapper bypassa
/// audit (jobs/events), cache (Redis + Postgres), gate de concorrência e custo,
/// como aconteceu pré-refactor com o IngestionJobHandler (4 documentos sumiram
/// da tela DI Analytics).
///
/// Marcado como <c>[Obsolete(error=false)]</c> pra que o compilador emita warning
/// CS0618 em qualquer chamada nova fora do extractor — a cerca, não a placa.
/// </summary>
[Obsolete(
    "Use IDocumentIntelligenceExtractor.ExtractAsync. Chamar IDocumentIntelligenceService " +
    "direto bypassa audit (document_extraction_jobs/events), cache, gate e custo. " +
    "Uso permitido APENAS dentro de DocumentIntelligenceExtractor.")]
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
