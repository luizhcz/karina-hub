namespace EfsAiHub.Core.Agents.DocumentIntelligence;

/// <summary>
/// Resultado bruto do Azure Document Intelligence — saída da camada wrapper
/// (<c>DocumentIntelligenceService</c>) antes do enriquecimento com cache,
/// custo e audit feito pelo <see cref="IDocumentIntelligenceExtractor"/>.
///
/// É um <i>data transport object</i> entre wrapper e pipeline, não faz parte
/// do contrato público do extractor — clients consomem <see cref="ExtractionResult"/>.
/// </summary>
public sealed record DiAnalyzeResult(
    string OperationId,
    string RawJson,
    string Content,
    int PageCount,
    bool HasTables,
    bool HasHandwriting,
    string? PrimaryLanguage,
    int DurationMs);
