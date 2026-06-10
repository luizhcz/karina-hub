namespace EfsAiHub.Core.Agents.DocumentIntelligence;

/// <summary>
/// Pipeline canônico de extração via Azure Document Intelligence.
/// É o ÚNICO ponto que escreve em aihub.document_extraction_jobs/events/cache
/// e em Redis (di:v2:*) — qualquer caminho que precise extrair documentos
/// (tool de agente, ingestion, futuros endpoints) DEVE passar por aqui.
///
/// O wrapper raw do SDK Azure (<c>DocumentIntelligenceService</c>) é
/// dependência interna deste pipeline — injetado apenas aqui, sem interface
/// pública e sem registro próprio no DI fora do escopo deste assembly.
///
/// Contrato:
/// <list type="bullet">
///   <item>Source é uma união fechada (<see cref="ExtractionSource.Bytes"/> ou
///         <see cref="ExtractionSource.Url"/>) — impossível passar ambos.</item>
///   <item>Computa SHA-256 do conteúdo pra dedup por cache (Postgres + Redis).</item>
///   <item>Aplica gate de concorrência (DocumentIntelligenceOptions.MaxConcurrentExtractions).</item>
///   <item>Resolve custo via DocumentIntelligencePricingCache (DB → fallback hardcoded).</item>
///   <item>NUNCA lança em condições previsíveis (PDF inválido, gate timeout, Azure 4xx);
///         retorna <see cref="ExtractionResult"/> com Status="failed" + ErrorCode preenchido.
///         Apenas erros catastróficos (Azure 401/403, OperationCanceledException de shutdown)
///         propagam.</item>
/// </list>
/// </summary>
public interface IDocumentIntelligenceExtractor
{
    Task<ExtractionResult> ExtractAsync(ExtractionInput input, CancellationToken ct);

    /// <summary>
    /// Checagem barata e ADVISORY de capacidade do gate de concorrência: <c>true</c>
    /// se provavelmente há vaga pra uma extração agora. NÃO reserva nada — o teto
    /// real é aplicado atomicamente dentro de <see cref="ExtractAsync"/> (que devolve
    /// <c>GATE_TIMEOUT</c> se a vaga sumir entre esta checagem e a aquisição). Serve
    /// pro caller evitar trabalho caro (baixar o PDF) quando o gate já está
    /// visivelmente cheio — crítico numa espera longa, em que o PDF já saiu do cache
    /// e seria re-baixado a cada ciclo só pra bater no gate.
    /// </summary>
    Task<bool> HasCapacityAsync(CancellationToken ct);
}

/// <summary>
/// Entrada do pipeline. <see cref="ConversationId"/> e <see cref="UserId"/> vão
/// direto pras colunas em <c>document_extraction_jobs</c> — pra ingestion
/// (sem conversa real), use sintético "ingestion:{standaloneJobId}" no
/// ConversationId e o AgentId no UserId.
/// </summary>
public sealed record ExtractionInput(
    ExtractionSource Source,
    string ConversationId,
    string UserId,
    string Model,
    string OutputFormat = "markdown",
    string[]? Features = null,
    bool CacheEnabled = true);

/// <summary>
/// Resultado normalizado da extração. Estados terminais:
/// <list type="bullet">
///   <item><c>"succeeded"</c> — Azure DI processou; Content/ResultRef/CostUsd preenchidos.</item>
///   <item><c>"cached"</c> — cache HIT; Content veio do Redis, CostUsd=0.</item>
///   <item><c>"failed"</c> — ErrorCode + ErrorMessage preenchidos.</item>
/// </list>
/// </summary>
public sealed record ExtractionResult(
    Guid JobId,
    string Status,
    string? Content,
    /// <summary>Chave-prefixo no Redis (di:v2:{sha256}:{model}:{format}). Null em failure pré-DI.</summary>
    string? ResultRef,
    int PageCount,
    decimal CostUsd,
    bool FromCache,
    /// <summary>OperationId do Azure DI. Null em cache hit ou failure pré-DI.</summary>
    string? OperationId,
    int? DurationMs,
    string? ErrorCode,
    string? ErrorMessage,
    /// <summary>Detalhe opcional pra failure (ex: actualSizeBytes/maxAllowedBytes).</summary>
    object? ErrorDetail);
