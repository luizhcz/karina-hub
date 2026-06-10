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
    public int CacheTtlDays { get; init; } = 7;

    /// <summary>
    /// Teto GLOBAL (cross-pod) de extrações simultâneas, imposto via contador de
    /// slots distribuído num scope Redis compartilhado (<c>document-intelligence</c>).
    /// NÃO é por pod: todas as réplicas disputam o mesmo contador, então este é o
    /// número máximo de extrações em voo no cluster inteiro. Azure DI tem rate
    /// limit por endpoint (~15 RPS em S0) — manter o teto abaixo disso evita 429;
    /// valores muito baixos serializam demais ingestão de PDF em massa.
    /// </summary>
    public int MaxConcurrentExtractions { get; init; } = 10;
}
