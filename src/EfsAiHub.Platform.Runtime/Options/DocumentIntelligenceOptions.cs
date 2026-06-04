namespace EfsAiHub.Platform.Runtime.Options;

/// <summary>
/// Configuração do executor Document Intelligence.
/// Seção: "DocumentIntelligence" no appsettings.json.
/// </summary>
public class DocumentIntelligenceOptions
{
    public const string SectionName = "DocumentIntelligence";

    public string Endpoint { get; init; } = "";
    public bool UseManagedIdentity { get; init; } = true;
    public string? ApiKey { get; init; }
    public string DefaultModel { get; init; } = "prebuilt-layout";
    /// <summary>Tamanho máximo do arquivo PDF em bytes (padrão: 50 MB).</summary>
    public long MaxFileSizeBytes { get; init; } = 50 * 1024 * 1024;
    public int PollingTimeoutSeconds { get; init; } = 180;
    public int GateWaitTimeoutSeconds { get; init; } = 600;
    public int CacheTtlDays { get; init; } = 7;

    /// <summary>
    /// Concorrência máxima de extrações simultâneas por pod. Default 4 — Azure DI
    /// tem rate limit por endpoint (15 RPS em S0), N pods × 4 extrações cobre
    /// volume típico. Valores muito altos saturam o endpoint e geram 429 do Azure;
    /// muito baixos serializam demais ingestão de PDF em massa.
    /// </summary>
    public int MaxConcurrentExtractions { get; init; } = 4;
}
