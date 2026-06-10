namespace EfsAiHub.Core.Agents.DocumentIntelligence;

/// <summary>
/// Códigos de erro do pipeline Document Intelligence.
///
/// Mapeia 1:1 com a constraint <c>chk_dej_error_code</c> em
/// <c>aihub.document_extraction_jobs</c>. Schema legacy é intocável
/// (governance) — adicionar valor novo aqui é proibido até o constraint
/// ser relaxado. Cenários novos (file size, misconfig) devem ser
/// expressos via um dos códigos existentes + detalhe no <c>error_message</c>.
/// </summary>
public static class ExtractionErrorCode
{
    /// <summary>
    /// Azure DI page limit excedido. Também usado por nós quando o file size
    /// estoura o limite local (cenário antes mapeado como FILE_SIZE_EXCEEDED em
    /// versão experimental — descartado pra não criar coluna ou tabela nova).
    /// </summary>
    public const string PageLimitExceeded  = "PAGE_LIMIT_EXCEEDED";
    /// <summary>PDF corrompido (magic bytes inválidos), arquivo muito grande, ou Azure DI rejeitou o conteúdo (400 InvalidContent).</summary>
    public const string UnreadablePdf      = "UNREADABLE_PDF";
    /// <summary>Falha no download da URL (HTTP error, timeout de rede, URL malformada).</summary>
    public const string SourceUnavailable  = "SOURCE_UNAVAILABLE";
    /// <summary>Azure DI falhou — rate-limit (429), 5xx, ou 400 por config (modelo/feature inválida). Conjunto bucket pra erros do provider.</summary>
    public const string AzureDiFailure     = "AZURE_DI_FAILURE";
    /// <summary>Timeout do polling do Azure DI (excedeu PollingTimeoutSeconds).</summary>
    public const string Timeout            = "TIMEOUT";
    /// <summary>
    /// Gate de concorrência distribuído (cross-pod) cheio: as MaxConcurrentExtractions
    /// vagas estão ocupadas. É fail-fast — a aquisição não espera por vaga; quando
    /// esgotado retorna na hora. Não é falha do job (a extração nem rodou): é
    /// backpressure de capacidade, classificado por <see cref="IsCapacityBackpressure"/>.
    /// </summary>
    public const string GateTimeout        = "GATE_TIMEOUT";
    /// <summary>Cancelamento via workflow / token do caller. Não é "permanente" — retomada de boot pode tentar de novo.</summary>
    public const string Cancelled          = "CANCELLED";

    /// <summary>
    /// Heurística de retry: códigos pra os quais retentar é inútil porque o problema
    /// está no INPUT (PDF quebrado, source 404). Caller usa pra decidir
    /// <c>permanent=true</c> em FailAsync, economizando attempts.
    ///
    /// <c>AzureDiFailure</c> ficou NÃO permanente — engloba transientes (429, 5xx)
    /// e config errors (400 com modelo/feature inválida). Retry de misconfig é
    /// desperdício, mas sem código distinto não dá pra separar.
    /// </summary>
    public static bool IsPermanent(string? errorCode) => errorCode switch
    {
        UnreadablePdf      => true,
        SourceUnavailable  => true,
        PageLimitExceeded  => true,
        _ => false,
    };

    /// <summary>
    /// Backpressure de capacidade: o gate de concorrência estava cheio e a
    /// extração NÃO chegou a rodar — não houve chamada ao provider nem falha
    /// real. Caller deve re-enfileirar aguardando capacidade liberar, SEM
    /// consumir tentativa, em vez de contar contra MaxAttempts (senão um job
    /// morre permanente só por encontrar o gate cheio algumas vezes).
    /// </summary>
    public static bool IsCapacityBackpressure(string? errorCode) => errorCode == GateTimeout;
}
